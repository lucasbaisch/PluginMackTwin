using System;
using System.Collections.Generic;
using System.Linq;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Representa uma sessão de experimento em Realidade Virtual: o conjunto de todas as
    /// amostras (<see cref="LinhaDeSessao"/>) coletadas para um mesmo participante,
    /// sessão, cenário e assento.
    /// </summary>
    /// <remarks>
    /// Uma Sessao é identificada pela combinação de IdentificacaoManual, NomeDaSessao,
    /// CenarioSelecionado e AssentoSelecionado. As linhas são armazenadas em ordem
    /// cronológica crescente (responsabilidade do <see cref="Parsing.AnalisadorCsvSessao"/>).
    ///
    /// Os eventos de fixação e saccade são calculados de forma lazy: apenas na primeira
    /// chamada a <see cref="Fixacoes"/> ou <see cref="Saccades"/>, com cache interno.
    ///
    /// O <see cref="BaselinePupilar"/> é atribuído pelo
    /// <see cref="Processamento.ConstruturDeSessoes"/> imediatamente após a construção da sessão,
    /// pois depende apenas das amostras da própria sessão.
    /// </remarks>
    public class Sessao
    {
        private readonly Func<IReadOnlyList<LinhaDeSessao>, (List<Fixacao>, List<Saccade>)> _funcaoDetectora;
        private List<Fixacao> _fixacoesCacheadas;
        private List<Saccade> _saccadesCacheados;
        private bool _eventosJaCalculados;

        /// <summary>Identificador do participante. Exemplo: "MA001".</summary>
        public string IdentificacaoManual { get; }

        /// <summary>Código da sessão. Chave de junção com os dados de cortisol. Exemplo: "2195-02".</summary>
        public string NomeDaSessao { get; }

        /// <summary>Nome do cenário de realidade virtual. Exemplo: "Cenario2_Dia".</summary>
        public string CenarioSelecionado { get; }

        /// <summary>Assento do participante. Exemplo: "A4".</summary>
        public string AssentoSelecionado { get; }

        /// <summary>
        /// Delta de cortisol calculado a partir do Input 02.
        /// Fórmula: valorUltimaColunaNumerica - valorColunaB.
        /// Retorna double.NaN se não houver correspondência no arquivo de cortisol.
        /// Usado nos gráficos de contextualização (§5.5.1); não entra no cálculo de potencial
        /// restaurador por objeto e não é usado como filtro de sessões (o Requisito 06 prevê
        /// apenas cenário, tipo de evento ocular e tendência pupilar).
        /// </summary>
        public double DeltaCortisol { get; set; }

        /// <summary>
        /// Linha de base pupilar da sessão, em milímetros: mediana do
        /// <see cref="LinhaDeSessao.DiametroPupilaMedio"/> das amostras válidas da janela
        /// pré-estímulo 00:00–01:00 desta sessão.
        /// </summary>
        /// <remarks>
        /// Atribuída pelo <see cref="Processamento.ConstruturDeSessoes"/>. Serve à correção
        /// subtrativa ΔD = D_t − baseline (§5.2 do TextoFinal): ΔD negativo indica constrição
        /// pupilar e, portanto, potencial restaurador.
        /// Retorna double.NaN quando não há amostra pupilar válida na janela pré-estímulo; nesse
        /// caso as métricas derivadas de ΔD também são NaN e o objeto é excluído do termo pupilar.
        /// </remarks>
        public double BaselinePupilar { get; set; }

        /// <summary>
        /// Linhas brutas de rastreamento ocular desta sessão, enriquecidas com dados de sensores,
        /// ordenadas por timestamp crescente.
        /// </summary>
        public IReadOnlyList<LinhaDeSessao> DadosBrutos { get; }

        /// <summary>
        /// O CENÁRIO como a interface, as métricas e a geração de vistas o entendem: o ambiente
        /// VR combinado com o período do dia, sem participante e sem assento.
        /// Exemplo: "Cenário_Cenario1_Dia".
        /// </summary>
        /// <remarks>
        /// <b>É a coluna <c>nomeDaSessao</c> do CSV, não a <c>selectedScene</c>.</b> As duas colunas
        /// vêm trocadas em relação ao que os seus nomes sugerem: <c>selectedScene</c> traz
        /// participante + cena + assento + período (um valor por registro individual), enquanto
        /// <c>nomeDaSessao</c> traz cena + período — exatamente o recorte que corresponde a UM
        /// arquivo Revit.
        ///
        /// Todo agrupamento por cenário passa por esta propriedade e por
        /// <see cref="RotuloDeSessao"/>: são o único lugar onde a inversão é resolvida. Os campos
        /// <see cref="CenarioSelecionado"/> e <see cref="NomeDaSessao"/> continuam espelhando as
        /// colunas do arquivo, para que o parsing permaneça legível ao lado do CSV.
        /// </remarks>
        public string RotuloDeCenario => NomeDaSessao;

        /// <summary>
        /// A SESSÃO como a interface a entende: o registro individual de um participante, num
        /// assento, num período. Exemplo: "MA001_Cenario1_A1_Dia".
        /// </summary>
        /// <remarks>
        /// É a coluna <c>selectedScene</c> do CSV. Ver <see cref="RotuloDeCenario"/> para a
        /// explicação da inversão.
        /// </remarks>
        public string RotuloDeSessao => CenarioSelecionado;

        /// <summary>
        /// Nome da fase Revit correspondente a esta sessão.
        /// Formato: "{CenarioSelecionado}_{IdentificacaoManual}". Exemplo: "Cenario2_Dia_MA001".
        /// Usado para criar e identificar fases no modelo Revit ao gerar vistas.
        /// </summary>
        public string NomeDeFaseRevit => $"{CenarioSelecionado}_{IdentificacaoManual}";

        /// <summary>
        /// Lista de fixações detectadas nesta sessão. Calculada de forma lazy com cache.
        /// </summary>
        public IReadOnlyList<Fixacao> Fixacoes
        {
            get
            {
                GarantirEventosCalculados();
                return _fixacoesCacheadas;
            }
        }

        /// <summary>
        /// Lista de saccades detectados nesta sessão. Calculada de forma lazy com cache.
        /// </summary>
        public IReadOnlyList<Saccade> Saccades
        {
            get
            {
                GarantirEventosCalculados();
                return _saccadesCacheados;
            }
        }

        /// <summary>
        /// Inicializa uma sessão com seus dados de identificação, linhas brutas e a função
        /// responsável pela detecção de eventos oculares.
        /// </summary>
        /// <param name="identificacaoManual">Identificador do participante.</param>
        /// <param name="nomeDaSessao">Código da sessão.</param>
        /// <param name="cenarioSelecionado">Nome do cenário VR.</param>
        /// <param name="assentoSelecionado">Assento do participante.</param>
        /// <param name="dadosBrutos">Linhas brutas ordenadas por timestamp crescente.</param>
        /// <param name="funcaoDetectora">
        /// Função que recebe as linhas brutas e retorna as listas de fixações e saccades.
        /// Injetada para manter a Sessao desacoplada do <see cref="Processamento.DetectorDeMovimentoOcular"/>.
        /// </param>
        public Sessao(
            string identificacaoManual,
            string nomeDaSessao,
            string cenarioSelecionado,
            string assentoSelecionado,
            IReadOnlyList<LinhaDeSessao> dadosBrutos,
            Func<IReadOnlyList<LinhaDeSessao>, (List<Fixacao>, List<Saccade>)> funcaoDetectora)
        {
            IdentificacaoManual = identificacaoManual;
            NomeDaSessao = nomeDaSessao;
            CenarioSelecionado = cenarioSelecionado;
            AssentoSelecionado = assentoSelecionado;
            DadosBrutos = dadosBrutos;
            _funcaoDetectora = funcaoDetectora;
            DeltaCortisol = double.NaN;
            BaselinePupilar = double.NaN;
        }

        /// <summary>
        /// Retorna as linhas brutas onde a pupila apresentou tendência de dilatação,
        /// ou seja, linhas pertencentes a fixações classificadas como <see cref="TendenciaPupilar.Dilatada"/>.
        /// </summary>
        public IEnumerable<LinhaDeSessao> ObterLinhasDePupilasDilatadas()
        {
            return ObterLinhasDasFixacoesPorTendencia(TendenciaPupilar.Dilatada);
        }

        /// <summary>
        /// Retorna as linhas brutas onde a pupila apresentou tendência de contração,
        /// ou seja, linhas pertencentes a fixações classificadas como <see cref="TendenciaPupilar.Contraida"/>.
        /// </summary>
        public IEnumerable<LinhaDeSessao> ObterLinhasDePupilasContraidas()
        {
            return ObterLinhasDasFixacoesPorTendencia(TendenciaPupilar.Contraida);
        }

        /// <summary>
        /// Retorna as fixações desta sessão filtradas pela tendência pupilar especificada.
        /// </summary>
        /// <param name="tendencia">Tendência pupilar desejada.</param>
        public IEnumerable<Fixacao> ObterFixacoesPorTendenciaPupilar(TendenciaPupilar tendencia)
        {
            return Fixacoes.Where(fixacao => fixacao.TendenciaPupilar == tendencia);
        }

        /// <summary>
        /// Retorna a última linha com dado pupilar válido desta sessão (timestamp mais tardio
        /// onde DiametroPupilaMedio não é NaN). Retorna null se nenhuma linha válida for encontrada.
        /// </summary>
        public LinhaDeSessao ObterUltimaLinhaComPupilaValida()
        {
            return DadosBrutos.LastOrDefault(linha => !double.IsNaN(linha.DiametroPupilaMedio));
        }

        private void GarantirEventosCalculados()
        {
            if (_eventosJaCalculados)
                return;

            var (fixacoes, saccades) = _funcaoDetectora(DadosBrutos);
            _fixacoesCacheadas = fixacoes;
            _saccadesCacheados = saccades;
            _eventosJaCalculados = true;
        }

        private IEnumerable<LinhaDeSessao> ObterLinhasDasFixacoesPorTendencia(TendenciaPupilar tendencia)
        {
            var indicesRelevantes = ObterFixacoesPorTendenciaPupilar(tendencia)
                .SelectMany(fixacao => fixacao.IndicesLinhasOriginais)
                .ToHashSet();

            return DadosBrutos.Where(linha => indicesRelevantes.Contains(linha.IndiceLinhaOriginal));
        }
    }
}
