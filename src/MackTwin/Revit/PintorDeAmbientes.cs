using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using MackTwin.Configuracao;
using MackTwin.Dominio;

namespace MackTwin.Revit
{
    /// <summary>O que uma pintura produziu, para a interface relatar.</summary>
    public sealed class ResultadoDaPintura
    {
        /// <summary>Salas casadas que receberam estado e cor.</summary>
        public int Pintados { get; init; }

        /// <summary>Explicação quando algo impediu a cor de aparecer, ou null.</summary>
        public string? Aviso { get; init; }
    }

    /// <summary>
    /// Pinta os Rooms de uma vista com a cor do estado de conforto da sala — o prédio de relance.
    /// </summary>
    /// <remarks>
    /// A cor vem do mecanismo nativo do Revit para salas: parâmetro + esquema de cores
    /// (<see cref="EsquemaDeCoresDeConforto"/>), na grandeza pedida — estado geral por padrão;
    /// temperatura ou umidade para a folha de impressão. Os três parâmetros são gravados sempre,
    /// de modo que trocar a grandeza de uma vista é só trocar o esquema. Por cima disso, uma
    /// sobreposição gráfica por elemento colore as LINHAS do Room na mesma cor — só as linhas,
    /// porque o preenchimento de Room ignora sobreposições; some com <see cref="Limpar"/>.
    ///
    /// Na planta, o Room só é visível pela subcategoria "Preenchimento interno", que a maioria
    /// dos templates deixa desligada; por isso <see cref="ExibirCategoriaDeRooms"/> liga a
    /// categoria e todas as subcategorias antes de pintar. Em 3D, o Revit só desenha Rooms com
    /// o cálculo de volumes do projeto ligado (ver <see cref="GeradorDeVistaRevit"/>).
    ///
    /// Deve rodar dentro de um contexto válido da API.
    /// </remarks>
    public static class PintorDeAmbientes
    {
        private static readonly ViewType[] TiposAceitos =
        {
            ViewType.FloorPlan,
            ViewType.CeilingPlan,
            ViewType.AreaPlan,
            ViewType.ThreeD,
            ViewType.Section,
            ViewType.Elevation,
        };

        /// <summary>Se a vista é de um tipo em que a pintura faz sentido e não é um template.</summary>
        public static bool VistaAceitaPintura(View? vista)
        {
            return vista != null && !vista.IsTemplate && TiposAceitos.Contains(vista.ViewType);
        }

        /// <summary>
        /// Rotula os Rooms (as três grandezas), garante o esquema da grandeza pedida, aplica-o à
        /// vista e reforça as linhas.
        /// </summary>
        /// <param name="resumir">Como obter o resumo da sala — normalmente o já calculado para a lista.</param>
        /// <param name="grandeza">Qual estado colore a vista.</param>
        public static ResultadoDaPintura Pintar(
            Document documento,
            View vista,
            ConjuntoDeAmbientes conjunto,
            Func<Ambiente, ResumoDoAmbiente> resumir,
            GrandezaDeConforto grandeza = GrandezaDeConforto.Geral)
        {
            var solido = EsquemaDeCoresDeConforto.LocalizarPadraoSolido(documento);

            using var transacao = new Transaction(documento, ConfiguracaoDePinturaDeAmbientes.NomeDaTransacaoDePintura);
            transacao.Start();

            ExibirCategoriaDeRooms(documento, vista);

            // 1) estado em cada Room + 2) esquema de cores na vista: é isso que pinta.
            var idsDosParametros = EsquemaDeCoresDeConforto.GarantirParametros(documento);
            int rotulados = EsquemaDeCoresDeConforto.RotularRooms(documento, conjunto, resumir);
            var esquema = EsquemaDeCoresDeConforto.GarantirEsquema(documento, grandeza, idsDosParametros[grandeza], solido);
            bool esquemaAplicado = esquema != null && EsquemaDeCoresDeConforto.AplicarAVista(vista, esquema);

            string? aviso = esquema == null
                ? "O projeto não tem nenhum esquema de cores de Ambientes para servir de base. Crie um qualquer em " +
                  "Arquitetura › Ambiente e Área › Esquemas de cores e pinte de novo."
                : esquemaAplicado
                    ? null
                    : "A vista não aceitou o esquema de cores de Ambientes (template de vista ou tipo de vista). " +
                      "Use \"Gerar vista 3D\" ou uma planta sem template.";

            // 3) reforço: linhas do Room na cor do estado.
            foreach (var ambiente in conjunto.Ambientes)
            {
                if (!ambiente.IdDoElementoNoModelo.HasValue)
                    continue;

                var id = new ElementId(ambiente.IdDoElementoNoModelo.Value);

                if (documento.GetElement(id) == null)
                    continue;

                var cor = ParaCorDoRevit(CoresDeConforto.Para(resumir(ambiente).ClassificacaoDe(grandeza)));
                vista.SetElementOverrides(id, MontarSobreposicao(cor, solido));
            }

            transacao.Commit();

            return new ResultadoDaPintura { Pintados = rotulados, Aviso = aviso };
        }

        /// <summary>
        /// Tira o esquema de cores da vista e as sobreposições dos elementos informados.
        /// Devolve quantos elementos foram limpos.
        /// </summary>
        public static int Limpar(Document documento, View vista, IEnumerable<long> idsDosElementos)
        {
            int limpos = 0;

            using var transacao = new Transaction(documento, ConfiguracaoDePinturaDeAmbientes.NomeDaTransacaoDeLimpeza);
            transacao.Start();

            EsquemaDeCoresDeConforto.RemoverDaVista(vista);

            foreach (long valor in idsDosElementos)
            {
                var id = new ElementId(valor);

                if (documento.GetElement(id) == null)
                    continue;

                vista.SetElementOverrides(id, new OverrideGraphicSettings());
                limpos++;
            }

            transacao.Commit();
            return limpos;
        }

        /// <summary>
        /// Liga, na vista, a categoria Rooms e todas as suas subcategorias (inclusive o
        /// "Preenchimento interno", por onde a cor aparece na planta). Precisa de transação ativa.
        /// Devolve quantas categorias foram de fato ligadas.
        /// </summary>
        public static int ExibirCategoriaDeRooms(Document documento, View vista)
        {
            var categoria = Category.GetCategory(documento, BuiltInCategory.OST_Rooms);

            if (categoria == null)
                return 0;

            int ligadas = TentarExibir(vista, categoria.Id) ? 1 : 0;

            foreach (Category subcategoria in categoria.SubCategories)
            {
                if (TentarExibir(vista, subcategoria.Id))
                    ligadas++;
            }

            return ligadas;
        }

        private static bool TentarExibir(View vista, ElementId idDaCategoria)
        {
            try
            {
                if (vista.CanCategoryBeHidden(idDaCategoria) && vista.GetCategoryHidden(idDaCategoria))
                {
                    vista.SetCategoryHidden(idDaCategoria, false);
                    return true;
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Categoria não controlável nesta vista.
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Visibilidade controlada por template de vista.
            }

            return false;
        }

        /// <summary>Cor do Revit a partir do hexadecimal <c>#RRGGBB</c> da configuração.</summary>
        public static Color ParaCorDoRevit(string hexadecimal)
        {
            var (r, g, b) = CoresDeConforto.ParaRgb(hexadecimal);
            return new Color(r, g, b);
        }

        /// <summary>
        /// Sobreposição gráfica na cor dada: linhas, padrão de superfície e padrão de corte
        /// sólidos. Usada nos Rooms (onde só as linhas obedecem) e nos sólidos temporários
        /// (onde tudo obedece).
        /// </summary>
        public static OverrideGraphicSettings MontarSobreposicao(Color cor, FillPatternElement? solido, int transparencia = 0)
        {
            var sobreposicao = new OverrideGraphicSettings();

            if (transparencia > 0)
                sobreposicao.SetSurfaceTransparency(transparencia);

            sobreposicao.SetProjectionLineColor(cor);
            sobreposicao.SetCutLineColor(cor);
            sobreposicao.SetSurfaceForegroundPatternVisible(true);
            sobreposicao.SetSurfaceForegroundPatternColor(cor);
            sobreposicao.SetCutForegroundPatternVisible(true);
            sobreposicao.SetCutForegroundPatternColor(cor);

            if (solido != null)
            {
                sobreposicao.SetSurfaceForegroundPatternId(solido.Id);
                sobreposicao.SetCutForegroundPatternId(solido.Id);
            }

            return sobreposicao;
        }
    }
}
