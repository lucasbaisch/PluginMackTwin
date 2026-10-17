using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Revit
{
    /// <summary>
    /// Resultado da geração de uma vista 3D para um único cenário.
    /// </summary>
    public class ResultadoDaGeracaoDeVista
    {
        /// <summary>Vista 3D criada no documento Revit. Null se a criação da vista falhar.</summary>
        public View3D VistaCriada { get; init; }

        /// <summary>Nome do cenário VR para o qual a vista foi gerada.</summary>
        public string NomeDoCenario { get; init; }

        /// <summary>Número de hitpoints inseridos com sucesso para o cenário.</summary>
        public int NumeroDeHitpointsInseridos { get; init; }

        /// <summary>Avisos sobre parâmetros não definidos ou fixações com dados ausentes.</summary>
        /// <remarks>
        /// Mutável de propósito: o isolamento por cenário acontece depois que todos os resultados
        /// já foram construídos, e precisa registrar aqui a vista que recusar a ocultação.
        /// </remarks>
        public List<string> Avisos { get; init; }

        /// <summary>
        /// Hitpoints da geração anterior removidos do modelo. Sempre 0 fora de
        /// <see cref="DecisaoDeGeracaoDeVista.Atualizar"/>.
        /// </summary>
        public int NumeroDeHitpointsRemovidos { get; init; }

        /// <summary>
        /// Vistas da geração anterior removidas do documento. Sempre 0 fora de
        /// <see cref="DecisaoDeGeracaoDeVista.Atualizar"/>.
        /// </summary>
        public int NumeroDeVistasRemovidas { get; init; }

        /// <summary>
        /// Ids dos hitpoints inseridos nesta passada, na ordem de inserção.
        /// </summary>
        /// <remarks>
        /// É a fonte AUTORITATIVA de a quem cada esfera pertence no isolamento — mais confiável do
        /// que reler o parâmetro "Cenário" depois, porque não depende de o parâmetro existir na
        /// versão da família carregada nem de a escrita ter dado certo. Se a escrita falhar, o
        /// aviso correspondente é registrado e o hitpoint continua sendo corretamente atribuído.
        /// </remarks>
        public IReadOnlyList<ElementId> HitpointsInseridos { get; init; } = new List<ElementId>();

        /// <summary>
        /// Quantos hitpoints de outros cenários foram ocultados nesta vista.
        /// </summary>
        /// <remarks>
        /// Mutável pelo mesmo motivo de <see cref="Avisos"/>: o isolamento roda depois que todos os
        /// resultados já foram construídos. Aparece no resumo da geração de propósito — sem ele, um
        /// isolamento que não ocultou nada é indistinguível de um que ocultou tudo, e foi
        /// exatamente essa cegueira que deixou o problema passar despercebido.
        /// </remarks>
        public int NumeroDeHitpointsOcultados { get; set; }
    }

    /// <summary>
    /// Orquestra, para cada cenário selecionado, a inserção de hitpoints (um por fixação)
    /// e a geração de uma vista 3D isométrica sem caixa de corte, com os hitpoints coloridos
    /// pelo potencial do objeto observado.
    /// </summary>
    /// <remarks>
    /// Fluxo de execução por chamada a <see cref="Gerar"/>:
    ///
    /// 1. Carga da família (FORA de transação) + Transaction "CAISM — Ativar Tipos do Hitpoint":
    ///    Carrega/recarrega hitpoint.rfa a partir da pasta Assets\ do plugin, garantindo
    ///    que o modelo sempre use a versão distribuída com o build. Sobrescreve a versão
    ///    eventualmente já presente no documento, sem criar uma segunda família.
    ///
    /// 2. Para cada cenário a processar:
    ///    a. Transaction "CAISM — Inserir HitPoints [cenário]":
    ///       Uma instância por fixação detectada no cenário (todas as sessões, sem filtros).
    ///       O TIPO da família é escolhido pela classificação de potencial do objeto observado
    ///       (Quadro 40): verde = Alto, vermelho = Baixo, cinza = sem pontuação. A cor é o
    ///       material do tipo, portanto vale em qualquer vista e estilo visual.
    ///       Parâmetros preenchidos: "Cenário", "Raio" (constante), "Dilatação Média da Pupila",
    ///       "Duração do FIxation". Coordenadas: Unity(X,Y,Z) → Revit(X,Z,Y).
    ///
    ///    b. Transaction "CAISM — Gerar Vista 3D [cenário]":
    ///       Vista 3D isométrica SEM caixa de corte, mostrando o modelo inteiro.
    ///       Nome: CAISM_{cenário}_{yyyyMMdd_HHmm}.
    ///
    /// Cada Transaction é independente — falha em uma não reverte trabalho anterior.
    /// </remarks>
    // RESOLVIDO pela revisão do TextoFinal de 31/08/2026: o Requisito 11 passou a descrever
    // exatamente este fluxo — "raio constante" e "gerar vista tridimensional isométrica com a
    // caixa de corte DESATIVADA, cada operação em transação independente" — e o §5.6.4 confirma
    // ("cria a vista 3D isométrica sem caixa de corte e sem template ... e isola cada vista no
    // seu cenário ocultando as esferas alheias"). Texto e código dizem a mesma coisa.
    public class GeradorDeVistaRevit
    {
        private readonly Document _documento;
        private readonly UIDocument _uiDocumento;
        private readonly InsersorDaFamiliaHitpoint _insersor;

        /// <summary>
        /// Inicializa o gerador com as dependências necessárias.
        /// </summary>
        public GeradorDeVistaRevit(Document documento, UIDocument uiDocumento)
        {
            _documento   = documento;
            _uiDocumento = uiDocumento;
            _insersor    = new InsersorDaFamiliaHitpoint(documento, new ConversorDeCoordenadas());
        }

        // ── Ponto de entrada ─────────────────────────────────────────────

        /// <summary>
        /// Executa o fluxo completo de geração de vistas.
        /// </summary>
        /// <param name="conjuntoDeSessoes">
        /// Conjunto completo de sessões carregadas. Os hitpoints sempre usam TODAS as
        /// sessões de cada cenário, independentemente de qualquer filtro da interface.
        /// </param>
        /// <param name="cenariosSelecionados">
        /// Cenários a gerar — uma vista para cada, com todos os hitpoints de todos os
        /// participantes e assentos daquele cenário. Nulo ou vazio significa todos os cenários
        /// distintos do conjunto.
        /// </param>
        /// <param name="caminhoDaPastaDoPlugin">
        /// Pasta onde o plugin está instalado, usada para localizar
        /// <c>Assets\hitpoint.rfa</c>.
        /// </param>
        /// <param name="classificacoesPorCenarioEObjeto">
        /// Classificações de potencial indexadas por cenário e, dentro dele, por nome de objeto.
        /// Usadas para colorir os hitpoints. É indexado POR CENÁRIO porque as métricas são
        /// calculadas por cenário: um mesmo objeto pode ter classificações diferentes em cenários
        /// diferentes. Pode ser null — nesse caso todas as esferas ficam cinza.
        /// </param>
        /// <returns>
        /// Lista de resultados, um por cenário processado.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Se o arquivo hitpoint.rfa não for encontrado ou se a carga falhar.
        /// </exception>
        public List<ResultadoDaGeracaoDeVista> Gerar(
            ConjuntoDeSessoes conjuntoDeSessoes,
            IReadOnlyList<string> cenariosSelecionados,
            string caminhoDaPastaDoPlugin,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, ClassificacaoDePotencial>> classificacoesPorCenarioEObjeto = null,
            DecisaoDeGeracaoDeVista decisao = DecisaoDeGeracaoDeVista.Atualizar)
        {
            // Step 1: Carregar/recarregar família (uma única vez antes do loop).
            // Devolve um símbolo por tipo (faixa de potencial), todos ativados.
            var simbolosPorTipo = ExecutarTransacaoDeCarregamentoDaFamilia(caminhoDaPastaDoPlugin);

            // Step 2: Determinar cenários a processar. Duplicatas são removidas porque cada nome
            // vira uma vista e uma passada de inserção — repetir um cenário produziria duas vistas
            // iguais e, com "Atualizar", a segunda passada apagaria os hitpoints da primeira.
            var cenarios = cenariosSelecionados == null || cenariosSelecionados.Count == 0
                ? conjuntoDeSessoes.ObterTodosOsCenarios().ToList()
                : cenariosSelecionados
                    .Where(nome => !string.IsNullOrWhiteSpace(nome))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var resultados = new List<ResultadoDaGeracaoDeVista>();

            foreach (var cenario in cenarios)
            {
                var fixacoes = ObterTodasAsFixacoesDoCenario(conjuntoDeSessoes, cenario);
                var classificacoes = ObterClassificacoesDoCenario(classificacoesPorCenarioEObjeto, cenario);
                var resultado = GerarParaCenario(cenario, fixacoes, simbolosPorTipo, classificacoes, decisao);
                resultados.Add(resultado);
            }

            // Step 3: isolar cada vista no seu cenário. Só agora, depois do laço: a vista do
            // primeiro cenário é criada antes de os hitpoints do segundo existirem, e esconder
            // durante o laço deixaria passar tudo o que fosse inserido depois.
            ExecutarTransacaoDeIsolamentoPorCenario(resultados);

            return resultados;
        }

        /// <summary>
        /// Esconde, em cada vista gerada, os hitpoints que não pertencem ao cenário dela.
        /// </summary>
        /// <remarks>
        /// <b>Hitpoints são elementos de MODELO.</b> Uma vista 3D mostra tudo que está no modelo,
        /// então, sem isolamento, todas as vistas exibem o mesmo conjunto completo de esferas e
        /// ficam indistinguíveis entre si — o cenário de cada vista deixa de significar coisa
        /// alguma.
        ///
        /// <b>Roda uma única vez, DEPOIS de todos os cenários.</b> Não é detalhe de arrumação: a
        /// vista do primeiro cenário é criada antes de os hitpoints do segundo existirem, e ocultar
        /// dentro do laço deixaria passar tudo o que fosse inserido em seguida. Com dois cenários
        /// isso já falha; com N, a vista k mostraria as esferas dos cenários k+1..N.
        ///
        /// <b>Por que ocultação de elemento e não filtro de vista.</b> Um
        /// <c>ParameterFilterElement</c> sobre o parâmetro "Cenário" seria dinâmico e valeria para
        /// esferas criadas depois, mas esbarra em duas coisas: o parâmetro precisaria ser
        /// COMPARTILHADO (parâmetro de família comum não entra em filtro de vista, e tornar
        /// compartilhado exige mexer no <c>hitpoint.rfa</c>), e o filtro age sobre uma CATEGORIA
        /// inteira — qualquer Modelo Genérico do projeto sem o parâmetro casaria com a regra
        /// "Cenário ≠ X" e sumiria da vista junto. A ocultação atinge exatamente os elementos que
        /// este plugin criou, e nada mais.
        ///
        /// Limite conhecido: a ocultação é uma fotografia do momento. Hitpoints inseridos numa
        /// geração posterior não estarão escondidos nas vistas antigas — o que não incomoda no uso
        /// normal, em que "Atualizar" refaz a vista junto com as esferas.
        /// </remarks>
        private void ExecutarTransacaoDeIsolamentoPorCenario(
            IReadOnlyList<ResultadoDaGeracaoDeVista> resultados)
        {
            var vistasGeradas = resultados.Where(r => r.VistaCriada != null).ToList();

            if (!vistasGeradas.Any())
                return;

            var cenarioPorHitpoint = MapearHitpointsPorCenario(resultados);

            if (cenarioPorHitpoint.Count == 0)
                return;

            using var t = new Transaction(_documento, "CAISM — Isolar HitPoints por Cenário");
            t.Start();

            foreach (var resultado in vistasGeradas)
            {
                var alheios = cenarioPorHitpoint
                    .Where(par => !string.Equals(par.Value, resultado.NomeDoCenario,
                                                 StringComparison.OrdinalIgnoreCase))
                    .Select(par => par.Key)
                    .ToList();

                resultado.NumeroDeHitpointsOcultados =
                    OcultarNaVista(resultado.VistaCriada, alheios, resultado);
            }

            t.Commit();
        }

        /// <summary>
        /// Diz a que cenário pertence cada hitpoint presente no modelo.
        /// </summary>
        /// <remarks>
        /// Duas fontes, nesta ordem de precedência:
        ///
        /// 1. <b>Os ids devolvidos pela inserção desta geração.</b> São certeza: sabemos sob qual
        ///    cenário cada instância foi criada, sem depender de reler nada. É o que torna o
        ///    isolamento imune a uma falha de escrita do parâmetro "Cenário" — falha que antes
        ///    passava como um simples aviso e depois reaparecia como vista sem filtro.
        ///
        /// 2. <b>O parâmetro "Cenário" das demais instâncias.</b> Cobre as esferas que já estavam
        ///    no modelo: cenários não escolhidos nesta rodada, e as gerações anteriores preservadas
        ///    por <see cref="DecisaoDeGeracaoDeVista.NovaVersao"/>.
        ///
        /// Hitpoint sem cenário identificável entra com string vazia, o que o faz ser ocultado em
        /// TODAS as vistas. É o comportamento correto: ele não pertence a nenhum cenário conhecido,
        /// e deixá-lo visível reproduziria o vazamento entre vistas. Como a fonte 1 é autoritativa,
        /// as esferas desta geração nunca caem neste caso.
        /// </remarks>
        private Dictionary<ElementId, string> MapearHitpointsPorCenario(
            IReadOnlyList<ResultadoDaGeracaoDeVista> resultados)
        {
            var cenarioPorHitpoint = new Dictionary<ElementId, string>();

            foreach (var resultado in resultados)
                foreach (var id in resultado.HitpointsInseridos)
                    cenarioPorHitpoint[id] = resultado.NomeDoCenario;

            foreach (var instancia in ColetarTodosOsHitpoints())
            {
                if (cenarioPorHitpoint.ContainsKey(instancia.Id))
                    continue;

                cenarioPorHitpoint[instancia.Id] = LerCenarioDoHitpoint(instancia) ?? string.Empty;
            }

            return cenarioPorHitpoint;
        }

        /// <summary>
        /// Oculta os elementos informados na vista e devolve quantos foram efetivamente ocultados.
        /// </summary>
        /// <remarks>
        /// <b><see cref="View.HideElements"/> é tudo-ou-nada.</b> Se UM único elemento da coleção
        /// não puder ser ocultado, a chamada inteira lança e nenhum dos outros é escondido — era
        /// assim que uma vista acabava exibindo as esferas de todos os cenários por causa de um
        /// elemento problemático. Daí a peneira por <see cref="Element.CanBeHidden"/> antes, e a
        /// segunda tentativa um a um depois: uma esfera teimosa custa uma esfera, não a vista.
        ///
        /// <b>Deve ser chamado dentro de uma Transaction ativa.</b>
        /// </remarks>
        private int OcultarNaVista(
            View3D vista,
            IReadOnlyList<ElementId> candidatos,
            ResultadoDaGeracaoDeVista resultado)
        {
            var ocultaveis = new List<ElementId>();
            int recusados = 0;

            foreach (var id in candidatos)
            {
                var elemento = _documento.GetElement(id);

                if (elemento == null)
                    continue;

                if (elemento.CanBeHidden(vista))
                    ocultaveis.Add(id);
                else
                    recusados++;
            }

            if (recusados > 0)
            {
                resultado.Avisos.Add(
                    $"[{resultado.NomeDoCenario}] {recusados} hitpoint(s) de outros cenários não " +
                    "podem ser ocultados nesta vista e continuam visíveis. A causa usual é um " +
                    "template de vista controlando as substituições de visibilidade.");
            }

            if (!ocultaveis.Any())
                return 0;

            try
            {
                vista.HideElements(ocultaveis);
                return ocultaveis.Count;
            }
            catch (Exception)
            {
                return OcultarUmAUm(vista, ocultaveis, resultado);
            }
        }

        private int OcultarUmAUm(
            View3D vista,
            IReadOnlyList<ElementId> ocultaveis,
            ResultadoDaGeracaoDeVista resultado)
        {
            int ocultados = 0;
            var falhas = new List<string>();

            foreach (var id in ocultaveis)
            {
                try
                {
                    vista.HideElements(new List<ElementId> { id });
                    ocultados++;
                }
                catch (Exception ex)
                {
                    if (falhas.Count < 3)
                        falhas.Add(ex.Message);
                }
            }

            int naoOcultados = ocultaveis.Count - ocultados;

            if (naoOcultados > 0)
            {
                resultado.Avisos.Add(
                    $"[{resultado.NomeDoCenario}] {naoOcultados} de {ocultaveis.Count} hitpoint(s) " +
                    $"de outros cenários continuam visíveis nesta vista. " +
                    string.Join(" ", falhas.Distinct()));
            }

            return ocultados;
        }

        /// <summary>
        /// Devolve, dentre os cenários informados, aqueles que já têm geração anterior no
        /// documento — vista com o padrão de nome do plugin, hitpoints no modelo, ou ambos.
        /// </summary>
        /// <remarks>
        /// Consulta apenas: não abre transação e não altera o documento. Serve para a interface
        /// decidir se precisa perguntar algo ao usuário antes de gerar.
        ///
        /// As duas evidências são checadas porque podem divergir. Uma geração interrompida entre
        /// a inserção dos hitpoints e a criação da vista deixa esferas órfãs sem vista; e apagar a
        /// vista no Project Browser não apaga as esferas, que são elementos de modelo.
        /// </remarks>
        public IReadOnlyList<string> ObterCenariosComGeracaoAnterior(IEnumerable<string> cenarios)
        {
            if (cenarios == null)
                return new List<string>();

            var nomesDeVistas = ColetarVistasTridimensionais().Select(vista => vista.Name).ToList();
            var cenariosComHitpoints = ColetarCenariosComHitpoints();

            return cenarios
                .Where(cenario => !string.IsNullOrWhiteSpace(cenario))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(cenario => cenariosComHitpoints.Contains(cenario)
                               || nomesDeVistas.Any(nome => EhVistaDoCenario(nome, cenario)))
                .ToList();
        }

        private static IReadOnlyDictionary<string, ClassificacaoDePotencial> ObterClassificacoesDoCenario(
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, ClassificacaoDePotencial>> classificacoesPorCenarioEObjeto,
            string nomeDoCenario)
        {
            if (classificacoesPorCenarioEObjeto != null
                && classificacoesPorCenarioEObjeto.TryGetValue(nomeDoCenario, out var classificacoes))
            {
                return classificacoes;
            }

            return new Dictionary<string, ClassificacaoDePotencial>();
        }

        // ── Por cenário ──────────────────────────────────────────────────

        /// <remarks>
        /// A ordem das etapas não é arbitrária:
        ///
        /// 1. os hitpoints antigos saem ANTES da inserção, senão o modelo fica com duas esferas
        ///    empilhadas em cada coordenada — e, se a classificação mudou entre as gerações, com
        ///    duas cores no mesmo ponto;
        /// 2. as vistas antigas saem DEPOIS que a nova está ativa, porque o Revit não apaga a
        ///    vista ativa. Se a nova não foi criada, nenhuma vista antiga é removida: melhor
        ///    manter uma vista desatualizada do que deixar o cenário sem vista alguma.
        /// </remarks>
        private ResultadoDaGeracaoDeVista GerarParaCenario(
            string nomeDoCenario,
            List<Fixacao> fixacoes,
            IReadOnlyDictionary<string, FamilySymbol> simbolosPorTipo,
            IReadOnlyDictionary<string, ClassificacaoDePotencial> classificacoesPorObjeto,
            DecisaoDeGeracaoDeVista decisao)
        {
            var avisos = new List<string>();
            bool substituir = decisao == DecisaoDeGeracaoDeVista.Atualizar;

            int hitpointsRemovidos = substituir
                ? ExecutarTransacaoDeRemocaoDeHitpoints(nomeDoCenario)
                : 0;

            var hitpoints = ExecutarTransacaoDeInsercaoDeHitpoints(
                nomeDoCenario, fixacoes, simbolosPorTipo, classificacoesPorObjeto, avisos);

            var vista = ExecutarTransacaoDeGeracaoDeVista(nomeDoCenario);

            if (vista != null)
                _uiDocumento.ActiveView = vista;

            int vistasRemovidas = substituir && vista != null
                ? ExecutarTransacaoDeRemocaoDeVistas(nomeDoCenario, vista.Id)
                : 0;

            if (!substituir && hitpoints.Any())
            {
                avisos.Add($"[{nomeDoCenario}] Geração anterior mantida: os hitpoints antigos " +
                           "continuam no modelo, sobrepostos aos novos.");
            }

            return new ResultadoDaGeracaoDeVista
            {
                VistaCriada              = vista,
                NomeDoCenario            = nomeDoCenario,
                NumeroDeHitpointsInseridos = hitpoints.Count,
                HitpointsInseridos       = hitpoints.Select(h => h.Id).ToList(),
                Avisos                   = avisos,
                NumeroDeHitpointsRemovidos = hitpointsRemovidos,
                NumeroDeVistasRemovidas    = vistasRemovidas,
            };
        }

        // ── Remoção da geração anterior ──────────────────────────────────

        /// <summary>
        /// Apaga do modelo os hitpoints do cenário. Devolve quantos saíram.
        /// </summary>
        private int ExecutarTransacaoDeRemocaoDeHitpoints(string nomeDoCenario)
        {
            var ids = ColetarHitpointsDoCenario(nomeDoCenario)
                .Select(instancia => instancia.Id)
                .ToList();

            if (!ids.Any())
                return 0;

            using var t = new Transaction(
                _documento, $"CAISM — Remover HitPoints Anteriores [{nomeDoCenario}]");
            t.Start();
            _documento.Delete(ids);
            t.Commit();
            return ids.Count;
        }

        /// <summary>
        /// Apaga as vistas anteriores do cenário, preservando a recém-criada. Devolve quantas saíram.
        /// </summary>
        private int ExecutarTransacaoDeRemocaoDeVistas(string nomeDoCenario, ElementId idDaVistaNova)
        {
            var ids = ColetarVistasTridimensionais()
                .Where(vista => EhVistaDoCenario(vista.Name, nomeDoCenario))
                .Where(vista => vista.Id != idDaVistaNova)
                .Select(vista => vista.Id)
                .ToList();

            if (!ids.Any())
                return 0;

            using var t = new Transaction(
                _documento, $"CAISM — Remover Vistas Anteriores [{nomeDoCenario}]");
            t.Start();
            _documento.Delete(ids);
            t.Commit();
            return ids.Count;
        }

        // ── Consulta ao documento ────────────────────────────────────────

        /// <summary>
        /// Nome da família no documento. O Revit batiza a família pelo nome do arquivo carregado,
        /// então <c>hitpoint.rfa</c> vira a família <c>hitpoint</c>.
        /// </summary>
        private static string ObterNomeDaFamiliaDeHitpoint()
        {
            return Path.GetFileNameWithoutExtension(
                ConfiguracaoDaFamiliaHitpoint.NomeDoArquivoDaFamilia);
        }

        private List<FamilyInstance> ColetarTodosOsHitpoints()
        {
            string nomeDaFamilia = ObterNomeDaFamiliaDeHitpoint();

            return new FilteredElementCollector(_documento)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(instancia => string.Equals(instancia.Symbol?.FamilyName, nomeDaFamilia,
                                                  StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Hitpoints cujo parâmetro "Cenário" corresponde ao cenário informado.
        /// </summary>
        /// <remarks>
        /// A seleção é pelo parâmetro de instância, não pela vista: hitpoints são elementos de
        /// MODELO e não pertencem a vista nenhuma. É o que permite trocar a geração de um cenário
        /// sem tocar nos demais.
        /// </remarks>
        private List<FamilyInstance> ColetarHitpointsDoCenario(string nomeDoCenario)
        {
            return ColetarTodosOsHitpoints()
                .Where(instancia => string.Equals(LerCenarioDoHitpoint(instancia), nomeDoCenario,
                                                  StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private HashSet<string> ColetarCenariosComHitpoints()
        {
            var cenarios = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var instancia in ColetarTodosOsHitpoints())
            {
                string cenario = LerCenarioDoHitpoint(instancia);
                if (!string.IsNullOrWhiteSpace(cenario))
                    cenarios.Add(cenario);
            }

            return cenarios;
        }

        private static string LerCenarioDoHitpoint(FamilyInstance instancia)
        {
            return instancia
                .LookupParameter(ConfiguracaoDaFamiliaHitpoint.NomeDoParametroCenario)
                ?.AsString();
        }

        private List<View3D> ColetarVistasTridimensionais()
        {
            return new FilteredElementCollector(_documento)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(vista => !vista.IsTemplate)
                .ToList();
        }

        // ── Coleta de fixações ───────────────────────────────────────────

        /// <summary>
        /// Coleta todas as fixações de TODAS as sessões do cenário, sem qualquer filtro.
        /// Este é o conjunto que se torna hitpoints no modelo.
        /// </summary>
        private static List<Fixacao> ObterTodasAsFixacoesDoCenario(
            ConjuntoDeSessoes conjuntoDeSessoes,
            string nomeDoCenario)
        {
            return conjuntoDeSessoes
                .FiltrarPorCenario(nomeDoCenario)
                .SelectMany(s => s.Fixacoes)
                .ToList();
        }

        // ── Transactions ─────────────────────────────────────────────────

        /// <summary>
        /// Carrega ou recarrega hitpoint.rfa a partir da pasta Assets\ do plugin.
        /// Sempre sobrescreve a versão existente no documento para garantir que a
        /// versão do build seja usada.
        /// </summary>
        /// <remarks>
        /// <b>Carga e ativação na MESMA transação, como na main.</b> Houve uma tentativa de tirar o
        /// <c>LoadFamily</c> de dentro da transação, sob a tese de que ali ele falharia em silêncio;
        /// a tese não se sustenta — este arranjo é o que a main usava e funcionava em máquinas
        /// diferentes. O que de fato quebrava a carga era o descasamento de nome do arquivo.
        ///
        /// A ativação precisa de transação de qualquer forma: <c>FamilySymbol.Activate</c> altera o
        /// documento.
        /// </remarks>
        private Dictionary<string, FamilySymbol> ExecutarTransacaoDeCarregamentoDaFamilia(
            string caminhoDaPastaDoPlugin)
        {
            string caminhoDaFamilia = ResolverCaminhoDaFamilia(caminhoDaPastaDoPlugin);

            using var t = new Transaction(_documento, "CAISM — Carregar Família Hitpoint");
            t.Start();
            var simbolos = _insersor.CarregarOuRecarregarFamilia(caminhoDaFamilia);
            _insersor.AtivarSimbolos(simbolos);
            t.Commit();

            return simbolos;
        }


        /// <summary>
        /// Resolve o caminho do arquivo hitpoint.rfa para uso em <see cref="Document.LoadFamily"/>.
        /// </summary>
        /// <remarks>
        /// Estratégia em dois passos:
        /// 1. Usa o arquivo em disco em <c>Assets\hitpoint.rfa</c> relativo à pasta do plugin —
        ///    disponível em instalações normais ou quando a pasta Assets\ está ao lado do DLL.
        /// 2. Se o arquivo não existir (ex: AddinManager que copia apenas o DLL para %TEMP%),
        ///    extrai o recurso embutido no assembly para <c>%TEMP%\hitpoint.rfa</c> e usa esse
        ///    caminho temporário.
        ///
        /// Os dois caminhos terminam num arquivo chamado <c>hitpoint.rfa</c>, e é isso que importa:
        /// o Revit nomeia a família pelo arquivo, e o insersor a procura por esse nome.
        /// </remarks>
        private static string ResolverCaminhoDaFamilia(string caminhoDaPastaDoPlugin)
        {
            string caminhoEmDisco = Path.Combine(
                caminhoDaPastaDoPlugin,
                ConfiguracaoDaFamiliaHitpoint.CaminhoRelativoDaFamilia);

            if (File.Exists(caminhoEmDisco))
                return caminhoEmDisco;

            // Fallback: extrair do recurso embutido no DLL.
            return ExtrairFamiliaDoRecursoEmbutido();
        }

        /// <summary>
        /// Extrai o recurso embutido <c>PluginCAISM.Assets.hitpoint.rfa</c> para um arquivo
        /// temporário e retorna o caminho desse arquivo.
        /// O arquivo de destino é sobrescrito a cada chamada para garantir que a versão
        /// distribuída com o build seja usada.
        /// </summary>
        /// <remarks>
        /// <b>O nome do arquivo extraído é o nome que a família terá no modelo.</b> Por isso tanto
        /// ele quanto o nome do recurso vêm de
        /// <see cref="ConfiguracaoDaFamiliaHitpoint.NomeDoArquivoDaFamilia"/> e não de literais: é a
        /// mesma constante que o insersor usa para procurar a família no documento, e separá-las
        /// faria o plugin carregar com um nome e buscar por outro. Era o que acontecia quando a
        /// extração gravava <c>CAISM_hitpoint.rfa</c> — o modelo ganhava uma segunda família.
        /// </remarks>
        private static string ExtrairFamiliaDoRecursoEmbutido()
        {
            string nomeDoRecurso =
                "PluginCAISM.Assets." + ConfiguracaoDaFamiliaHitpoint.NomeDoArquivoDaFamilia;

            string destino = Path.Combine(
                Path.GetTempPath(), ConfiguracaoDaFamiliaHitpoint.NomeDoArquivoDaFamilia);

            using var stream = typeof(GeradorDeVistaRevit).Assembly
                .GetManifestResourceStream(nomeDoRecurso);

            if (stream == null)
                throw new InvalidOperationException(
                    $"Recurso embutido '{nomeDoRecurso}' não encontrado no assembly. " +
                    $"Verifique se hitpoint.rfa está marcado como EmbeddedResource no .csproj.");

            using var arquivo = new FileStream(destino, FileMode.Create, FileAccess.Write);
            stream.CopyTo(arquivo);

            return destino;
        }

        private List<FamilyInstance> ExecutarTransacaoDeInsercaoDeHitpoints(
            string nomeDoCenario,
            List<Fixacao> fixacoes,
            IReadOnlyDictionary<string, FamilySymbol> simbolosPorTipo,
            IReadOnlyDictionary<string, ClassificacaoDePotencial> classificacoesPorObjeto,
            List<string> avisos)
        {
            if (!fixacoes.Any())
            {
                avisos.Add($"[{nomeDoCenario}] Nenhuma fixação encontrada — nenhum hitpoint inserido.");
                return new List<FamilyInstance>();
            }

            if (classificacoesPorObjeto == null || classificacoesPorObjeto.Count == 0)
                avisos.Add($"[{nomeDoCenario}] Sem pontuações por objeto — todos os hitpoints " +
                           $"recebem o tipo '{ConfiguracaoDaFamiliaHitpoint.NomeDoTipoPotencialNeutro}'.");

            using var t = new Transaction(_documento, $"CAISM — Inserir HitPoints [{nomeDoCenario}]");
            t.Start();
            var hitpoints = _insersor.InserirHitpointsDoCenario(
                fixacoes, nomeDoCenario, simbolosPorTipo, classificacoesPorObjeto, avisos);
            t.Commit();
            return hitpoints;
        }

        private View3D ExecutarTransacaoDeGeracaoDeVista(string nomeDoCenario)
        {
            var tipoDaVista = LocalizarTipoDaVista3D();
            if (tipoDaVista == null)
                return null;

            string nomeDaVista = MontarNomeDaVista(nomeDoCenario);

            using var t = new Transaction(_documento, $"CAISM — Gerar Vista 3D [{nomeDoCenario}]");
            t.Start();
            var vista = CriarVista3D(tipoDaVista, nomeDaVista);
            t.Commit();
            return vista;
        }

        // ── Nome da vista ────────────────────────────────────────────────

        /// <summary>
        /// Monta <c>CAISM_{cenário}_v{NN}_{aaaa-MM-dd_HHmm}</c> com a próxima versão livre.
        /// </summary>
        /// <remarks>
        /// A busca continua incrementando enquanto o nome estiver em uso. Isso cobre o caso que
        /// antes derrubava a geração: duas execuções dentro do mesmo minuto produziam nomes
        /// idênticos, e <c>View.Name</c> recusa nome repetido — só que os hitpoints já tinham sido
        /// commitados numa transação anterior, deixando esferas órfãs sem vista.
        /// </remarks>
        private string MontarNomeDaVista(string nomeDoCenario)
        {
            string carimbo = DateTime.Now.ToString(
                ConfiguracaoDeNomeDeVista.FormatoDoCarimboDeTempo);

            var nomesEmUso = new HashSet<string>(
                new FilteredElementCollector(_documento)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Select(vista => vista.Name),
                StringComparer.OrdinalIgnoreCase);

            int versao = ObterMaiorVersaoExistente(nomeDoCenario) + 1;
            int limite = versao + ConfiguracaoDeNomeDeVista.LimiteDeTentativasDeVersao;
            string nome;

            do
            {
                nome = ComporNomeDaVista(nomeDoCenario, versao, carimbo);
                versao++;
            }
            while (nomesEmUso.Contains(nome) && versao < limite);

            return nome;
        }

        private static string ComporNomeDaVista(string nomeDoCenario, int versao, string carimbo)
        {
            return $"{ConfiguracaoDaFamiliaHitpoint.PrefixoDeNomeDeVista}_{nomeDoCenario}_" +
                   $"{ConfiguracaoDeNomeDeVista.PrefixoDeVersao}" +
                   $"{versao.ToString(ConfiguracaoDeNomeDeVista.FormatoDaVersao)}_{carimbo}";
        }

        /// <summary>
        /// Maior número de versão já usado pelo cenário, ou 0 se não houver nenhum.
        /// </summary>
        /// <remarks>
        /// Lê o maior em vez de contar as vistas existentes: apagar uma versão intermediária não
        /// pode fazer a próxima colidir com uma que ainda está lá. Vistas no formato antigo
        /// (sem <c>vNN</c>) não casam e contam como 0 — a primeira geração depois desta mudança
        /// nasce como v01 ao lado delas.
        /// </remarks>
        private int ObterMaiorVersaoExistente(string nomeDoCenario)
        {
            string padrao = "^"
                + Regex.Escape($"{ConfiguracaoDaFamiliaHitpoint.PrefixoDeNomeDeVista}_{nomeDoCenario}_")
                + Regex.Escape(ConfiguracaoDeNomeDeVista.PrefixoDeVersao)
                + @"(?<versao>\d+)_";

            int maior = 0;

            foreach (var vista in ColetarVistasTridimensionais())
            {
                var casamento = Regex.Match(vista.Name, padrao, RegexOptions.IgnoreCase);

                if (casamento.Success
                    && int.TryParse(casamento.Groups["versao"].Value, out int versao)
                    && versao > maior)
                {
                    maior = versao;
                }
            }

            return maior;
        }

        /// <summary>
        /// Diz se o nome de uma vista pertence ao cenário informado.
        /// </summary>
        /// <remarks>
        /// Aceita o formato atual (<c>..._vNN_</c>) e o anterior (carimbo direto, sem versão), para
        /// que vistas geradas antes desta mudança continuem sendo reconhecidas e substituídas.
        ///
        /// Exigir que venha um <c>v</c> seguido de dígitos, ou um dígito, logo após o nome do
        /// cenário é o que impede um cenário de casar com outro que o tenha como prefixo — sem
        /// isso, "Cenario1" reivindicaria as vistas de "Cenario1_Dia".
        /// </remarks>
        private static bool EhVistaDoCenario(string nomeDaVista, string nomeDoCenario)
        {
            if (string.IsNullOrWhiteSpace(nomeDaVista) || string.IsNullOrWhiteSpace(nomeDoCenario))
                return false;

            string padrao = "^"
                + Regex.Escape($"{ConfiguracaoDaFamiliaHitpoint.PrefixoDeNomeDeVista}_{nomeDoCenario}_")
                + "(" + Regex.Escape(ConfiguracaoDeNomeDeVista.PrefixoDeVersao) + @"\d+_|\d)";

            return Regex.IsMatch(nomeDaVista, padrao, RegexOptions.IgnoreCase);
        }

        // ── Utilitários de vista ─────────────────────────────────────────

        private ViewFamilyType LocalizarTipoDaVista3D()
        {
            return new FilteredElementCollector(_documento)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
        }

        /// <summary>
        /// Cria a vista 3D isométrica, sem caixa de corte e sem template.
        /// </summary>
        /// <remarks>
        /// <see cref="View3D.IsSectionBoxActive"/> é desligado explicitamente como salvaguarda:
        /// o <see cref="ViewFamilyType"/> localizado pode ter um template que a habilite, e a
        /// vista deve mostrar o modelo inteiro (§5.5.1).
        ///
        /// <b>O template é desligado pelo mesmo motivo, e por um mais grave.</b> Um template que
        /// inclua "V/G Overrides Model" entre as propriedades controladas TRAVA a visibilidade da
        /// vista: <see cref="Element.CanBeHidden"/> passa a devolver false e o isolamento por
        /// cenário não tem como esconder nada — a vista sai mostrando as esferas de todos os
        /// cenários, sem erro nenhum. Como a vista é gerada pelo plugin para um fim específico,
        /// não há o que um template do projeto tenha a dizer sobre ela.
        ///
        /// <b>Deve ser chamado dentro de uma Transaction ativa.</b>
        /// </remarks>
        private View3D CriarVista3D(ViewFamilyType tipoDaVista, string nomeDaVista)
        {
            var vista = View3D.CreateIsometric(_documento, tipoDaVista.Id);
            vista.Name = nomeDaVista;

            if (vista.ViewTemplateId != ElementId.InvalidElementId)
                vista.ViewTemplateId = ElementId.InvalidElementId;

            vista.IsSectionBoxActive = false;
            return vista;
        }
    }
}
