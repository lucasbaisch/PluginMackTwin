using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using MackTwin.Configuracao;
using MackTwin.Dominio;
using MackTwin.Processamento;

namespace MackTwin.Revit
{
    /// <summary>
    /// Pinta as salas do jeito que o Revit pinta salas: parâmetros de Room com o estado de
    /// conforto e esquemas de cores por valor aplicados à vista — um conjunto por grandeza.
    /// </summary>
    /// <remarks>
    /// <b>Por que não sobreposição gráfica.</b> O preenchimento de um Room — na planta e no
    /// volume 3D — é controlado exclusivamente pelo esquema de cores da vista; a sobreposição
    /// por elemento muda as linhas e os padrões de superfície de qualquer categoria, MENOS esse
    /// preenchimento. Foi verificado no modelo de teste: sobreposição aplicada, Room com
    /// volume, nada visível.
    ///
    /// <b>Três grandezas, três pares parâmetro/esquema.</b> <c>MackTwin Conforto</c> (estado
    /// geral), <c>MackTwin Temperatura</c> e <c>MackTwin Umidade</c>. A cada pintura os três
    /// parâmetros são gravados em todos os Rooms; a vista recebe o esquema da grandeza pedida.
    /// É o que permite à folha de impressão mostrar temperatura e umidade lado a lado, cada
    /// uma com a sua legenda.
    ///
    /// <b>O que fica no projeto.</b> Os parâmetros compartilhados (texto, vinculados a
    /// Ambientes) e os esquemas de cores da categoria Ambientes. Criados uma vez, reaproveitados;
    /// o arquivo de parâmetros compartilhados fica na pasta de dados do usuário, e a
    /// configuração anterior do Revit é restaurada ao fim.
    ///
    /// <b>Só grava o que mudou.</b> Entradas do esquema só reescritas quando diferem; valor do
    /// parâmetro só gravado se mudou; parâmetro localizado pelo próprio Room quando já existe.
    ///
    /// O esquema é criado por duplicação de um esquema de Ambientes já existente, porque a API
    /// não cria esquemas do zero; um projeto sem nenhum esquema de Ambientes recebe aviso.
    /// Todos os métodos precisam de transação ativa.
    /// </remarks>
    public static class EsquemaDeCoresDeConforto
    {
        private const string NomeDoGrupoDeParametros = "MackTwin";
        private const string NomeDoArquivoDeParametros = "MackTwin.parametros.txt";

        private static readonly GrandezaDeConforto[] Grandezas =
        {
            GrandezaDeConforto.Geral,
            GrandezaDeConforto.Temperatura,
            GrandezaDeConforto.Umidade,
        };

        private static readonly ClassificacaoDeConforto[] Estados =
        {
            ClassificacaoDeConforto.Confortavel,
            ClassificacaoDeConforto.AbaixoDaFaixa,
            ClassificacaoDeConforto.AcimaDaFaixa,
            ClassificacaoDeConforto.SemDados,
        };

        private static ElementId IdDeRooms => new(BuiltInCategory.OST_Rooms);

        /// <summary>Nome do parâmetro de Room que recebe o estado da grandeza.</summary>
        public static string NomeDoParametro(GrandezaDeConforto grandeza)
        {
            return grandeza switch
            {
                GrandezaDeConforto.Temperatura => "MackTwin Temperatura",
                GrandezaDeConforto.Umidade => "MackTwin Umidade",
                _ => "MackTwin Conforto",
            };
        }

        /// <summary>Nome do esquema de cores da grandeza (o mesmo do parâmetro).</summary>
        public static string NomeDoEsquema(GrandezaDeConforto grandeza) => NomeDoParametro(grandeza);

        /// <summary>Descrição curta da grandeza, para rótulos e títulos de vista.</summary>
        public static string Descrever(GrandezaDeConforto grandeza)
        {
            return grandeza switch
            {
                GrandezaDeConforto.Temperatura => "Temperatura",
                GrandezaDeConforto.Umidade => "Umidade",
                _ => "Conforto",
            };
        }

        /// <summary>Título impresso pela legenda de cores da grandeza.</summary>
        public static string TituloDaLegenda(GrandezaDeConforto grandeza)
        {
            return grandeza switch
            {
                GrandezaDeConforto.Temperatura =>
                    $"Temperatura — conforto {FaixasDeConfortoAmbiental.TemperaturaMinima:0}–{FaixasDeConfortoAmbiental.TemperaturaMaxima:0} °C",
                GrandezaDeConforto.Umidade =>
                    $"Umidade relativa — conforto {FaixasDeConfortoAmbiental.UmidadeMinima:0}–{FaixasDeConfortoAmbiental.UmidadeMaxima:0} %",
                _ => "Conforto ambiental (temperatura e umidade)",
            };
        }

        // ── Parâmetros ───────────────────────────────────────────────────

        /// <summary>Garante os três parâmetros vinculados a Ambientes e devolve os seus ElementIds.</summary>
        public static IReadOnlyDictionary<GrandezaDeConforto, ElementId> GarantirParametros(Document documento)
        {
            var ids = new Dictionary<GrandezaDeConforto, ElementId>();

            foreach (var grandeza in Grandezas)
                ids[grandeza] = GarantirParametro(documento, grandeza);

            return ids;
        }

        /// <summary>
        /// Garante o parâmetro da grandeza vinculado a Ambientes e devolve o seu ElementId (o
        /// que o esquema de cores referencia).
        /// </summary>
        public static ElementId GarantirParametro(Document documento, GrandezaDeConforto grandeza)
        {
            string nome = NomeDoParametro(grandeza);

            // Caminho rápido: se qualquer Room já expõe o parâmetro, o Id está nele.
            var primeiroRoom = ColetarRooms(documento).FirstOrDefault();
            var noRoom = primeiroRoom?.LookupParameter(nome);

            if (noRoom != null && noRoom.Id != ElementId.InvalidElementId)
                return noRoom.Id;

            var existente = new FilteredElementCollector(documento)
                .OfClass(typeof(SharedParameterElement))
                .Cast<SharedParameterElement>()
                .FirstOrDefault(parametro => parametro.Name == nome);

            if (existente != null)
                return existente.Id;

            var aplicacao = documento.Application;
            string caminho = Path.Combine(ConfiguracaoDeArmazenamento.PastaDeDados, NomeDoArquivoDeParametros);

            Directory.CreateDirectory(ConfiguracaoDeArmazenamento.PastaDeDados);

            if (!File.Exists(caminho))
                File.WriteAllText(caminho, string.Empty);

            string arquivoAnterior = aplicacao.SharedParametersFilename;

            try
            {
                aplicacao.SharedParametersFilename = caminho;

                var arquivo = aplicacao.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException($"Não foi possível abrir o arquivo de parâmetros compartilhados em {caminho}.");

                var grupo = arquivo.Groups.get_Item(NomeDoGrupoDeParametros)
                    ?? arquivo.Groups.Create(NomeDoGrupoDeParametros);

                var definicao = grupo.Definitions.get_Item(nome) as ExternalDefinition
                    ?? (ExternalDefinition)grupo.Definitions.Create(
                        new ExternalDefinitionCreationOptions(nome, SpecTypeId.String.Text)
                        {
                            Description = $"Estado de conforto da sala ({Descrever(grandeza).ToLowerInvariant()}), gravado pelo MackTwin a cada pintura.",
                            UserModifiable = false,
                        });

                var categorias = aplicacao.Create.NewCategorySet();
                categorias.Insert(Category.GetCategory(documento, BuiltInCategory.OST_Rooms));

                documento.ParameterBindings.Insert(definicao, aplicacao.Create.NewInstanceBinding(categorias), GroupTypeId.Data);

                var elemento = SharedParameterElement.Lookup(documento, definicao.GUID)
                    ?? throw new InvalidOperationException("O parâmetro foi vinculado, mas não foi encontrado no documento.");

                return elemento.Id;
            }
            finally
            {
                if (!string.IsNullOrEmpty(arquivoAnterior))
                    aplicacao.SharedParametersFilename = arquivoAnterior;
            }
        }

        /// <summary>
        /// Grava, em TODOS os Rooms colocados, os três estados da sala: os do resumo para os
        /// casados, "Sem dados" para os demais — assim o prédio inteiro fica legível de relance.
        /// Só toca o parâmetro cujo valor mudou. Devolve quantos Rooms casados foram rotulados.
        /// </summary>
        public static int RotularRooms(
            Document documento,
            ConjuntoDeAmbientes conjunto,
            Func<Ambiente, ResumoDoAmbiente> resumir)
        {
            var ambientesPorId = conjunto.Ambientes
                .Where(ambiente => ambiente.IdDoElementoNoModelo.HasValue)
                .ToDictionary(ambiente => ambiente.IdDoElementoNoModelo!.Value, ambiente => ambiente);

            int rotulados = 0;

            foreach (var room in ColetarRooms(documento))
            {
                bool casado = ambientesPorId.TryGetValue(room.Id.Value, out var ambiente);
                var resumo = casado ? resumir(ambiente!) : null;

                foreach (var grandeza in Grandezas)
                {
                    var parametro = room.LookupParameter(NomeDoParametro(grandeza))
                        ?? throw new InvalidOperationException(
                            $"O parâmetro \"{NomeDoParametro(grandeza)}\" existe no projeto, mas não está vinculado à categoria " +
                            "Ambientes. Vincule-o em Gerenciar › Parâmetros do projeto (ou remova-o, que o plug-in o recria).");

                    if (parametro.IsReadOnly)
                        continue;

                    var classificacao = resumo?.ClassificacaoDe(grandeza) ?? ClassificacaoDeConforto.SemDados;
                    string valor = ClassificadorDeConforto.Descrever(classificacao);

                    if (!string.Equals(parametro.AsString(), valor, StringComparison.Ordinal))
                        parametro.Set(valor);
                }

                if (casado)
                    rotulados++;
            }

            return rotulados;
        }

        // ── Esquemas de cores ────────────────────────────────────────────

        /// <summary>
        /// Garante o esquema da grandeza (categoria Ambientes, por valor do parâmetro) com as
        /// quatro cores de conforto. Devolve null se o projeto não tem nenhum esquema de
        /// Ambientes para servir de base.
        /// </summary>
        public static ColorFillScheme? GarantirEsquema(
            Document documento, GrandezaDeConforto grandeza, ElementId idDoParametro, FillPatternElement? solido)
        {
            string nome = NomeDoEsquema(grandeza);

            var esquemasDeRooms = new FilteredElementCollector(documento)
                .OfClass(typeof(ColorFillScheme))
                .Cast<ColorFillScheme>()
                .Where(esquema => esquema.CategoryId == IdDeRooms)
                .ToList();

            var esquema = esquemasDeRooms.FirstOrDefault(e => e.Name == nome);

            if (esquema == null)
            {
                var modelo = esquemasDeRooms.FirstOrDefault();

                if (modelo == null)
                    return null;

                esquema = (ColorFillScheme)documento.GetElement(modelo.Duplicate(nome));
            }

            if (esquema.ParameterDefinition != idDoParametro)
                esquema.ParameterDefinition = idDoParametro;

            // O título é o que a legenda nativa (ColorFillLegend) imprime no topo.
            string titulo = TituloDaLegenda(grandeza);

            if (!string.Equals(esquema.Title, titulo, StringComparison.Ordinal))
                esquema.Title = titulo;

            var entradas = MontarEntradas(solido);

            if (!EntradasIguais(esquema.GetEntries(), entradas))
                esquema.SetEntries(entradas);

            return esquema;
        }

        /// <summary>Aplica o esquema à categoria Ambientes da vista. Devolve false se a vista recusa.</summary>
        public static bool AplicarAVista(View vista, ColorFillScheme esquema)
        {
            try
            {
                if (vista.GetColorFillSchemeId(IdDeRooms) != esquema.Id)
                    vista.SetColorFillSchemeId(IdDeRooms, esquema.Id);

                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return false;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>Remove o esquema de cores de Ambientes da vista, se houver.</summary>
        public static void RemoverDaVista(View vista)
        {
            try
            {
                if (vista.GetColorFillSchemeId(IdDeRooms) != ElementId.InvalidElementId)
                    vista.SetColorFillSchemeId(IdDeRooms, ElementId.InvalidElementId);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // A vista não trabalha com esquema de cores.
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Esquema controlado por template.
            }
        }

        /// <summary>Nome do esquema aplicado à categoria Ambientes da vista, ou null.</summary>
        public static string? NomeDoEsquemaNaVista(Document documento, View vista)
        {
            try
            {
                var id = vista.GetColorFillSchemeId(IdDeRooms);
                return id == ElementId.InvalidElementId ? null : documento.GetElement(id)?.Name;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return null;
            }
        }

        // ── Apoio ────────────────────────────────────────────────────────

        private static List<ColorFillSchemeEntry> MontarEntradas(FillPatternElement? solido)
        {
            var entradas = new List<ColorFillSchemeEntry>(Estados.Length);

            foreach (var estado in Estados)
            {
                var entrada = new ColorFillSchemeEntry(StorageType.String)
                {
                    Color = PintorDeAmbientes.ParaCorDoRevit(CoresDeConforto.Para(estado)),
                    IsVisible = true,
                };

                entrada.SetStringValue(ClassificadorDeConforto.Descrever(estado));

                if (solido != null)
                    entrada.FillPatternId = solido.Id;

                entradas.Add(entrada);
            }

            return entradas;
        }

        private static bool EntradasIguais(IList<ColorFillSchemeEntry> atuais, IList<ColorFillSchemeEntry> desejadas)
        {
            if (atuais.Count != desejadas.Count)
                return false;

            for (int indice = 0; indice < atuais.Count; indice++)
            {
                var a = atuais[indice];
                var d = desejadas[indice];

                if (a.StorageType != StorageType.String
                    || !string.Equals(a.GetStringValue(), d.GetStringValue(), StringComparison.Ordinal)
                    || !a.Color.IsValid
                    || a.Color.Red != d.Color.Red || a.Color.Green != d.Color.Green || a.Color.Blue != d.Color.Blue
                    || a.FillPatternId != d.FillPatternId
                    || a.IsVisible != d.IsVisible)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Padrão de preenchimento sólido do projeto, ou null.</summary>
        public static FillPatternElement? LocalizarPadraoSolido(Document documento)
        {
            return new FilteredElementCollector(documento)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(padrao => padrao.GetFillPattern().IsSolidFill);
        }

        private static IEnumerable<Room> ColetarRooms(Document documento)
        {
            return new FilteredElementCollector(documento)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(room => room.Location != null);
        }
    }
}
