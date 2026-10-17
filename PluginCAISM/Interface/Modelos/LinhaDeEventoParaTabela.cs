using System;
using System.Globalization;
using System.Linq;
using PluginCAISM.Dominio;

namespace PluginCAISM.Interface.Modelos
{
    /// <summary>
    /// Modelo de apresentação para uma linha da tabela de eventos detectados (auditoria da sessão).
    /// Converte os dados de <see cref="EventoDeMovimentoOcular"/> para um formato adequado
    /// ao databinding do DataGrid WPF.
    /// </summary>
    public class LinhaDeEventoParaTabela
    {
        private static readonly CultureInfo Cultura = CultureInfo.InvariantCulture;

        /// <summary>Tipo do evento: "Fixação" ou "Saccade".</summary>
        public string TipoDoEvento { get; }

        /// <summary>
        /// Tempo decorrido entre o início da sessão e o início do evento. É o valor que ordena a
        /// coluna TEMPO — a string formatada serve à leitura, não à comparação.
        /// </summary>
        public TimeSpan TempoDesdeOInicioDaSessao { get; }

        /// <summary>
        /// <see cref="TempoDesdeOInicioDaSessao"/> no formato minuto:segundo:milissegundo
        /// (ex.: "03:24:157"). O primeiro evento possível de uma sessão é "00:00:000".
        /// </summary>
        public string TempoDesdeOInicio { get; }

        /// <summary>Duração do evento em milissegundos.</summary>
        public double DuracaoMs { get; }

        /// <summary>Timestamp de início do evento.</summary>
        public DateTime TimestampInicio { get; }

        /// <summary>Timestamp de fim do evento.</summary>
        public DateTime TimestampFim { get; }

        /// <summary>Nomes dos objetos observados durante o evento, separados por vírgula.</summary>
        public string NomesDeObjetos { get; }

        /// <summary>Componente X do ponto médio de incidência (metros, espaço Unity).</summary>
        public double PontoX { get; }

        /// <summary>Componente Y do ponto médio de incidência (metros, espaço Unity).</summary>
        public double PontoY { get; }

        /// <summary>Componente Z do ponto médio de incidência (metros, espaço Unity).</summary>
        public double PontoZ { get; }

        /// <summary>Diâmetro médio da pupila esquerda em mm.</summary>
        public double DiametroPupilaEsquerda { get; }

        /// <summary>Diâmetro médio da pupila direita em mm.</summary>
        public double DiametroPupilaDireita { get; }

        /// <summary>
        /// Tendência pupilar (apenas para fixações). Para saccades, exibe "N/A".
        /// </summary>
        public string TendenciaPupilar { get; }

        /// <summary>
        /// Classificação DESCRITIVA desta fixação isolada, vinda de
        /// <see cref="Auditoria.ClassificadorDeEventoDaAuditoria"/>. É ela que o XAML usa para
        /// pintar o texto da linha com as MESMAS cores da legenda.
        /// </summary>
        /// <remarks>
        /// Não é a classificação do OBJETO: aquela é por elemento (objeto × cenário), sai da média
        /// dos ΔD de todas as fixações do elemento e é a mesma em todas elas. Esta olha uma
        /// fixação de cada vez, e é por isso que o mesmo objeto pode aparecer aqui ora neutro,
        /// ora estressor, ora restaurador. Saccades são sempre
        /// <see cref="ClassificacaoDePotencial.Neutro"/> — cinza —, porque a leitura pupilar
        /// de um movimento não significa nada.
        /// </remarks>
        public ClassificacaoDePotencial Classificacao { get; }

        /// <summary>Rótulo legível de <see cref="Classificacao"/>; vazio nos saccades.</summary>
        public string ClassificacaoRotulo { get; }

        /// <summary>
        /// ΔD centrado da fixação em mm — o número que decidiu a classificação. Negativo é
        /// constrição, positivo é dilatação. NaN nos saccades e onde falta baseline.
        /// </summary>
        public double DeltaPupilarCentrado { get; }

        /// <summary>
        /// <see cref="DeltaPupilarCentrado"/> formatado, ou "—" quando não há valor. Existe porque
        /// um NaN ligado direto à coluna apareceria como "NaN".
        /// </summary>
        public string DeltaPupilarCentradoTexto { get; }

        /// <summary>
        /// Falso quando o evento cai fora da janela temporal de análise
        /// (<see cref="Configuracao.ParametrosDeDeteccaoOcular.JanelaTemporalInicioMs"/> a
        /// <see cref="Configuracao.ParametrosDeDeteccaoOcular.JanelaTemporalFimMs"/>) e, portanto,
        /// NÃO entrou em nenhum cálculo. O XAML esmaece a linha inteira nesse caso.
        /// </summary>
        public bool DentroDaJanelaDeAnalise { get; }

        /// <summary>
        /// Inicializa a linha a partir de um evento de movimento ocular.
        /// </summary>
        /// <param name="evento">Evento de fixação ou saccade.</param>
        /// <param name="inicioDaSessao">
        /// Instante tomado como zero do relógio da sessão — o mesmo que o
        /// <see cref="Processamento.DetectorDeMovimentoOcular"/> usa como referência da janela
        /// temporal, para que a coluna TEMPO e o esmaecimento concordem.
        /// </param>
        /// <param name="classificacao">
        /// Classificação descritiva desta fixação isolada. Saccades recebem
        /// <see cref="ClassificacaoDePotencial.Neutro"/>.
        /// </param>
        /// <param name="deltaPupilarCentrado">
        /// ΔD centrado que produziu a classificação, em mm; NaN nos saccades.
        /// </param>
        /// <param name="dentroDaJanelaDeAnalise">
        /// Se o evento foi de fato considerado pela análise.
        /// </param>
        public LinhaDeEventoParaTabela(
            EventoDeMovimentoOcular evento,
            DateTime inicioDaSessao,
            ClassificacaoDePotencial classificacao,
            double deltaPupilarCentrado,
            bool dentroDaJanelaDeAnalise)
        {
            // NÃO se chama "Saccade" na tela, e a diferença não é de nomenclatura. O detector
            // agrupa amostras ESPACIALMENTE ESTÁVEIS e classifica cada grupo pela duração: um
            // saccade é, por definição, um movimento, que acontece ENTRE dois grupos estáveis e
            // não dentro de um. O que a faixa de 30–80 ms captura aqui é um trecho curto em que o
            // olhar ficou parado — começo de fixação interrompida por ruído, fim de um movimento,
            // ou artefato de rastreamento. Anunciá-lo como saccade faria a tabela afirmar uma
            // detecção de movimento sacádico que não está acontecendo.
            TipoDoEvento              = evento is Fixacao ? "Fixação" : "Trecho curto";
            TempoDesdeOInicioDaSessao = evento.TimestampInicio - inicioDaSessao;
            TempoDesdeOInicio         = FormatarTempoRelativo(TempoDesdeOInicioDaSessao);
            DuracaoMs                 = evento.DuracaoEmMilissegundos;
            TimestampInicio           = evento.TimestampInicio;
            TimestampFim              = evento.TimestampFim;
            NomesDeObjetos            = evento.NomesDeObjetos == null
                ? string.Empty
                : string.Join(", ", evento.NomesDeObjetos.OrderBy(nome => nome));
            PontoX                    = evento.PontoMedioDeIncidenciaX;
            PontoY                    = evento.PontoMedioDeIncidenciaY;
            PontoZ                    = evento.PontoMedioDeIncidenciaZ;
            DiametroPupilaEsquerda    = evento.DiametroMedianoPupilaEsquerda;
            DiametroPupilaDireita     = evento.DiametroMedianoPupilaDireita;
            TendenciaPupilar          = evento is Fixacao fixacao
                ? fixacao.TendenciaPupilar.ToString()
                : "N/A";
            Classificacao             = classificacao;
            ClassificacaoRotulo       = evento is Fixacao
                ? ClassificadorDePotencialDeHitpoint.ObterRotulo(classificacao)
                : string.Empty;
            DeltaPupilarCentrado      = deltaPupilarCentrado;
            DeltaPupilarCentradoTexto = double.IsNaN(deltaPupilarCentrado)
                ? "—"
                : deltaPupilarCentrado.ToString("N4", Cultura);
            DentroDaJanelaDeAnalise   = dentroDaJanelaDeAnalise;
        }

        /// <summary>
        /// Formata o tempo decorrido como minuto:segundo:milissegundo.
        /// </summary>
        /// <remarks>
        /// Os minutos vêm de <see cref="TimeSpan.TotalMinutes"/>, e não de
        /// <see cref="TimeSpan.Minutes"/>, para que uma sessão de mais de uma hora continue a
        /// exibir o total decorrido em vez de reiniciar a contagem.
        /// </remarks>
        private static string FormatarTempoRelativo(TimeSpan decorrido)
        {
            if (decorrido < TimeSpan.Zero)
                decorrido = TimeSpan.Zero;

            return string.Format(
                Cultura,
                "{0:00}:{1:00}:{2:000}",
                (int)decorrido.TotalMinutes,
                decorrido.Seconds,
                decorrido.Milliseconds);
        }
    }
}
