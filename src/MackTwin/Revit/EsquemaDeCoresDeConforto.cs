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
    /// Pinta as salas do jeito que o Revit pinta salas: um parâmetro de Room com o estado de
    /// conforto e um esquema de cores por valor aplicado à vista.
    /// </summary>
    /// <remarks>
    /// <b>Por que não sobreposição gráfica.</b> O preenchimento de um Room — na planta e no
    /// volume 3D — é controlado exclusivamente pelo esquema de cores da vista; a sobreposição
    /// por elemento muda as linhas e os padrões de superfície de qualquer categoria, MENOS esse
    /// preenchimento. Foi verificado no modelo de teste: sobreposição aplicada, Room com
    /// volume, nada visível.
    ///
    /// <b>O que fica no projeto.</b> Um parâmetro compartilhado <c>MackTwin Conforto</c> (texto)
    /// vinculado a Ambientes e um esquema de cores <c>MackTwin Conforto</c> da categoria
    /// Ambientes. Ambos são criados uma vez e reaproveitados; o valor do parâmetro é regravado a
    /// cada pintura. O arquivo de parâmetros compartilhados fica na pasta de dados do usuário,
    /// e a configuração anterior do Revit é restaurada ao fim.
    ///
    /// O esquema é criado por duplicação de um esquema de Ambientes já existente, porque a API
    /// não cria esquemas do zero; um projeto sem nenhum esquema de Ambientes recebe aviso.
    /// Todos os métodos precisam de transação ativa.
    /// </remarks>
    public static class EsquemaDeCoresDeConforto
    {
        /// <summary>Nome do parâmetro de Room que recebe o estado da sala.</summary>
        public const string NomeDoParametro = "MackTwin Conforto";

        /// <summary>Nome do esquema de cores da categoria Ambientes.</summary>
        public const string NomeDoEsquema = "MackTwin Conforto";

        private const string NomeDoGrupoDeParametros = "MackTwin";
        private const string NomeDoArquivoDeParametros = "MackTwin.parametros.txt";

        private static readonly ClassificacaoDeConforto[] Estados =
        {
            ClassificacaoDeConforto.Confortavel,
            ClassificacaoDeConforto.AbaixoDaFaixa,
            ClassificacaoDeConforto.AcimaDaFaixa,
            ClassificacaoDeConforto.SemDados,
        };

        private static ElementId IdDeRooms => new(BuiltInCategory.OST_Rooms);

        // ── Parâmetro ────────────────────────────────────────────────────

        /// <summary>
        /// Garante o parâmetro <see cref="NomeDoParametro"/> vinculado a Ambientes e devolve o
        /// seu ElementId (o que o esquema de cores referencia).
        /// </summary>
        public static ElementId GarantirParametro(Document documento)
        {
            var existente = new FilteredElementCollector(documento)
                .OfClass(typeof(SharedParameterElement))
                .Cast<SharedParameterElement>()
                .FirstOrDefault(parametro => parametro.Name == NomeDoParametro);

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

                var definicao = grupo.Definitions.get_Item(NomeDoParametro) as ExternalDefinition
                    ?? (ExternalDefinition)grupo.Definitions.Create(
                        new ExternalDefinitionCreationOptions(NomeDoParametro, SpecTypeId.String.Text)
                        {
                            Description = "Estado de conforto da sala (temperatura e umidade), gravado pelo MackTwin a cada pintura.",
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
        /// Grava, em TODOS os Rooms colocados, o estado da sala: o da classificação para os
        /// casados, "Sem dados" para os demais — assim o prédio inteiro fica legível de relance.
        /// Devolve quantos Rooms casados foram rotulados.
        /// </summary>
        public static int RotularRooms(
            Document documento,
            ConjuntoDeAmbientes conjunto,
            Func<Ambiente, ClassificacaoDeConforto> classificar)
        {
            var ambientesPorId = conjunto.Ambientes
                .Where(ambiente => ambiente.IdDoElementoNoModelo.HasValue)
                .ToDictionary(ambiente => ambiente.IdDoElementoNoModelo!.Value, ambiente => ambiente);

            int rotulados = 0;

            foreach (var room in ColetarRooms(documento))
            {
                var parametro = room.LookupParameter(NomeDoParametro)
                    ?? throw new InvalidOperationException(
                        $"O parâmetro \"{NomeDoParametro}\" existe no projeto, mas não está vinculado à categoria " +
                        "Ambientes. Vincule-o em Gerenciar › Parâmetros do projeto (ou remova-o, que o plug-in o recria).");

                if (parametro.IsReadOnly)
                    continue;

                bool casado = ambientesPorId.TryGetValue(room.Id.Value, out var ambiente);
                var classificacao = casado ? classificar(ambiente!) : ClassificacaoDeConforto.SemDados;

                parametro.Set(ClassificadorDeConforto.Descrever(classificacao));

                if (casado)
                    rotulados++;
            }

            return rotulados;
        }

        // ── Esquema de cores ─────────────────────────────────────────────

        /// <summary>
        /// Garante o esquema <see cref="NomeDoEsquema"/> (categoria Ambientes, por valor do
        /// parâmetro) com as quatro cores de conforto. Devolve null se o projeto não tem nenhum
        /// esquema de Ambientes para servir de base.
        /// </summary>
        public static ColorFillScheme? GarantirEsquema(Document documento, ElementId idDoParametro, FillPatternElement? solido)
        {
            var esquemasDeRooms = new FilteredElementCollector(documento)
                .OfClass(typeof(ColorFillScheme))
                .Cast<ColorFillScheme>()
                .Where(esquema => esquema.CategoryId == IdDeRooms)
                .ToList();

            var esquema = esquemasDeRooms.FirstOrDefault(e => e.Name == NomeDoEsquema);

            if (esquema == null)
            {
                var modelo = esquemasDeRooms.FirstOrDefault();

                if (modelo == null)
                    return null;

                esquema = (ColorFillScheme)documento.GetElement(modelo.Duplicate(NomeDoEsquema));
            }

            if (esquema.ParameterDefinition != idDoParametro)
                esquema.ParameterDefinition = idDoParametro;

            var entradas = new List<ColorFillSchemeEntry>();

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

            esquema.SetEntries(entradas);
            return esquema;
        }

        /// <summary>Aplica o esquema à categoria Ambientes da vista. Devolve false se a vista recusa.</summary>
        public static bool AplicarAVista(View vista, ColorFillScheme esquema)
        {
            try
            {
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
