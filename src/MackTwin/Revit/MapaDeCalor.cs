using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Analysis;
using Autodesk.Revit.DB.Architecture;
using MackTwin.Configuracao;
using MackTwin.Dominio;

namespace MackTwin.Revit
{
    /// <summary>O que o mapa de calor desenhou.</summary>
    public sealed class ResultadoDoMapaDeCalor
    {
        /// <summary>Salas que receberam valor.</summary>
        public int Salas { get; init; }

        /// <summary>Menor valor desenhado.</summary>
        public double Minimo { get; init; } = double.NaN;

        /// <summary>Maior valor desenhado.</summary>
        public double Maximo { get; init; } = double.NaN;

        /// <summary>Explicação quando algo ficou de fora, ou null.</summary>
        public string? Aviso { get; init; }
    }

    /// <summary>
    /// Mapa de calor sobre o piso das salas com o Analysis Visualization Framework (AVF): uma
    /// lâmina colorida por sala com a última temperatura (°C) ou umidade (%), e a legenda em
    /// gradiente azul → verde → vermelho.
    /// </summary>
    /// <remarks>
    /// <b>Constante por sala</b> (escolha do usuário, 20/10/2026): cada sala tem um sensor, e o
    /// mapa mostra o que foi medido, sem inventar gradiente entre salas. A lâmina é a face
    /// superior de uma extrusão fina do contorno do Room (<see cref="GeometriaDoRoom.FaceDoPiso"/>),
    /// flutuando 3 cm acima do piso para não ser engolida por ele; os pontos de amostragem são
    /// uma grade UV sobre a face, todos com o mesmo valor.
    ///
    /// <b>Temporário por natureza.</b> Os dados do AVF não são salvos com o modelo e vivem por
    /// vista; <see cref="Remover"/> limpa. O estilo de exibição (cores e legenda) fica no
    /// projeto, um por grandeza, reaproveitado. Chamar em contexto válido da API.
    /// </remarks>
    public static class MapaDeCalor
    {
        private const double PassoDaGradeEmPes = 1.5;
        private const int QuantidadeDeMedidas = 1;

        /// <summary>Desenha o mapa da grandeza na vista com o estado atual. Substitui o mapa anterior.</summary>
        public static ResultadoDoMapaDeCalor Aplicar(
            Document documento,
            View vista,
            ConjuntoDeAmbientes conjunto,
            Func<Ambiente, ResumoDoAmbiente> resumir,
            GrandezaDeConforto grandeza)
        {
            if (grandeza == GrandezaDeConforto.Geral)
                throw new ArgumentException("O mapa de calor é de temperatura ou de umidade.", nameof(grandeza));

            int salas = 0, semGeometria = 0;
            double minimo = double.PositiveInfinity, maximo = double.NegativeInfinity;

            using var transacao = new Transaction(documento, ConfiguracaoDePinturaDeAmbientes.NomeDaTransacaoDeMapaDeCalor);
            transacao.Start();

            var gerente = SpatialFieldManager.GetSpatialFieldManager(vista)
                          ?? SpatialFieldManager.CreateSpatialFieldManager(vista, QuantidadeDeMedidas);

            gerente.Clear();

            int indiceDoResultado = GarantirResultado(gerente, grandeza);

            foreach (var ambiente in conjunto.Ambientes)
            {
                if (!ambiente.IdDoElementoNoModelo.HasValue)
                    continue;

                var resumo = resumir(ambiente);
                double valor = grandeza == GrandezaDeConforto.Temperatura ? resumo.Temperatura.Ultimo : resumo.Umidade.Ultimo;

                if (double.IsNaN(valor))
                    continue;

                if (documento.GetElement(new ElementId(ambiente.IdDoElementoNoModelo.Value)) is not Room room)
                    continue;

                var face = GeometriaDoRoom.FaceDoPiso(room);

                if (face == null)
                {
                    semGeometria++;
                    continue;
                }

                var pontos = AmostrarFace(face);

                if (pontos.Count == 0)
                {
                    semGeometria++;
                    continue;
                }

                int primitiva = gerente.AddSpatialFieldPrimitive(face, Transform.Identity);
                var valores = pontos.Select(_ => new ValueAtPoint(new List<double> { valor })).ToList();

                gerente.UpdateSpatialFieldPrimitive(primitiva, new FieldDomainPointsByUV(pontos), new FieldValues(valores), indiceDoResultado);

                salas++;
                minimo = Math.Min(minimo, valor);
                maximo = Math.Max(maximo, valor);
            }

            vista.AnalysisDisplayStyleId = GarantirEstilo(documento, grandeza).Id;

            transacao.Commit();

            string? aviso = null;

            if (salas == 0)
                aviso = "Nenhuma sala casada tem leitura dessa grandeza, ou os Rooms não têm contorno utilizável.";
            else if (semGeometria > 0)
                aviso = $"{semGeometria} sala(s) ficaram de fora por não terem contorno de piso utilizável.";

            return new ResultadoDoMapaDeCalor
            {
                Salas = salas,
                Minimo = salas == 0 ? double.NaN : minimo,
                Maximo = salas == 0 ? double.NaN : maximo,
                Aviso = aviso,
            };
        }

        /// <summary>Limpa o mapa da vista. Devolve false se a vista nunca teve um.</summary>
        public static bool Remover(Document documento, View vista)
        {
            var gerente = SpatialFieldManager.GetSpatialFieldManager(vista);

            if (gerente == null)
                return false;

            using var transacao = new Transaction(documento, ConfiguracaoDePinturaDeAmbientes.NomeDaTransacaoDeMapaDeCalor);
            transacao.Start();
            gerente.Clear();
            transacao.Commit();
            return true;
        }

        /// <summary>Nome do resultado (e do estilo de exibição) da grandeza.</summary>
        public static string NomeDoResultado(GrandezaDeConforto grandeza)
        {
            return grandeza == GrandezaDeConforto.Umidade ? "MackTwin Umidade (%)" : "MackTwin Temperatura (°C)";
        }

        // ── Resultado e estilo ───────────────────────────────────────────

        private static int GarantirResultado(SpatialFieldManager gerente, GrandezaDeConforto grandeza)
        {
            string nome = NomeDoResultado(grandeza);

            foreach (int indice in gerente.GetRegisteredResults())
            {
                if (gerente.GetResultSchema(indice).Name == nome)
                    return indice;
            }

            string unidade = grandeza == GrandezaDeConforto.Umidade ? "%" : "°C";
            var esquema = new AnalysisResultSchema(nome, $"Última leitura de {EsquemaDeCoresDeConforto.Descrever(grandeza).ToLowerInvariant()} de cada sala (MackTwin).");
            esquema.SetUnits(new List<string> { unidade }, new List<double> { 1.0 });

            return gerente.RegisterResult(esquema);
        }

        private static AnalysisDisplayStyle GarantirEstilo(Document documento, GrandezaDeConforto grandeza)
        {
            string nome = NomeDoResultado(grandeza);

            var existente = new FilteredElementCollector(documento)
                .OfClass(typeof(AnalysisDisplayStyle))
                .Cast<AnalysisDisplayStyle>()
                .FirstOrDefault(estilo => estilo.Name == nome);

            if (existente != null)
                return existente;

            var superficie = new AnalysisDisplayColoredSurfaceSettings { ShowGridLines = false };

            var cores = new AnalysisDisplayColorSettings
            {
                MinColor = PintorDeAmbientes.ParaCorDoRevit(CoresDeConforto.AbaixoDaFaixa),
                MaxColor = PintorDeAmbientes.ParaCorDoRevit(CoresDeConforto.AcimaDaFaixa),
                ColorSettingsType = AnalysisDisplayStyleColorSettingsType.GradientColor,
            };
            cores.SetIntermediateColors(new List<AnalysisDisplayColorEntry>
            {
                new(PintorDeAmbientes.ParaCorDoRevit(CoresDeConforto.Confortavel)),
            });

            var legenda = new AnalysisDisplayLegendSettings
            {
                NumberOfSteps = 10,
                Rounding = grandeza == GrandezaDeConforto.Umidade ? 1.0 : 0.5,
                ShowLegend = true,
                ShowUnits = true,
                ShowDataName = true,
                ShowDataDescription = false,
            };

            return AnalysisDisplayStyle.CreateAnalysisDisplayStyle(documento, nome, superficie, cores, legenda);
        }

        // ── Amostragem ───────────────────────────────────────────────────

        /// <summary>Grade UV sobre a face, só com os pontos dentro dela; ao menos o centro.</summary>
        private static IList<UV> AmostrarFace(Face face)
        {
            var caixa = face.GetBoundingBox();
            var pontos = new List<UV>();

            double larguraU = caixa.Max.U - caixa.Min.U;
            double larguraV = caixa.Max.V - caixa.Min.V;
            int colunas = Math.Max(2, (int)Math.Ceiling(larguraU / PassoDaGradeEmPes) + 1);
            int linhas = Math.Max(2, (int)Math.Ceiling(larguraV / PassoDaGradeEmPes) + 1);

            for (int linha = 0; linha < linhas; linha++)
            {
                for (int coluna = 0; coluna < colunas; coluna++)
                {
                    var uv = new UV(
                        caixa.Min.U + larguraU * coluna / (colunas - 1),
                        caixa.Min.V + larguraV * linha / (linhas - 1));

                    if (face.IsInside(uv))
                        pontos.Add(uv);
                }
            }

            if (pontos.Count == 0)
            {
                var centro = new UV((caixa.Min.U + caixa.Max.U) / 2, (caixa.Min.V + caixa.Max.V) / 2);

                if (face.IsInside(centro))
                    pontos.Add(centro);
            }

            return pontos;
        }
    }
}
