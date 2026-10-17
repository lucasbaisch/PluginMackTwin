using System.Collections.Generic;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Um objeto da cena e o quanto o olhar do participante permaneceu sobre ele.
    /// </summary>
    public class ObjetoObservado
    {
        /// <summary>Nome do objeto virtual, como vem do CSV. Exemplo: "Parede: Rippas".</summary>
        public string Nome { get; init; }

        /// <summary>Amostras em que o olhar incidiu sobre este objeto.</summary>
        public int QuantidadeDeAmostras { get; init; }

        /// <summary>
        /// Participação deste objeto no total de amostras com objeto nomeado do participante.
        /// </summary>
        public double PercentualDeAmostras { get; init; }
    }

    /// <summary>
    /// Quanto tempo o participante permaneceu em um cenário de realidade virtual.
    /// </summary>
    public class PermanenciaEmCenario
    {
        /// <summary>Nome do cenário. Exemplo: "Cenario3_Dia".</summary>
        public string Cenario { get; init; }

        /// <summary>Sessões que o participante registrou neste cenário.</summary>
        public int QuantidadeDeSessoes { get; init; }

        /// <summary>Soma da duração das sessões neste cenário, em minutos.</summary>
        public double TempoMinutos { get; init; }

        /// <summary>Participação deste cenário no tempo total do participante em RV.</summary>
        public double PercentualDoTempo { get; init; }
    }

    /// <summary>
    /// Retrato do que o <c>dados_hmd.csv</c> registrou para UM participante de realidade virtual.
    /// </summary>
    /// <remarks>
    /// Serve à categoria "Dados do HMD - RV" da aba de contextualização. É descrição da coleta —
    /// não entra no cálculo do Potencial Homeodinâmico, do mesmo modo que os formulários não entram.
    ///
    /// <b>O participante aqui é o do HMD</b> (<see cref="Sessao.IdentificacaoManual"/>: MA001,
    /// LF001, …), que não é o mesmo identificador dos formulários. Ver
    /// <see cref="Configuracao.MapeamentoDeColunasDeFormulario.IdentificacaoDoParticipante"/>.
    ///
    /// Campos sem dado vêm como <see cref="double.NaN"/> ou vazios, nunca inventados.
    /// </remarks>
    public class ResumoDoHmdDoParticipante
    {
        /// <summary>Identificador do participante no HMD. Exemplo: "MA001".</summary>
        public string IdentificacaoManual { get; init; }

        /// <summary>Quantas sessões o participante registrou.</summary>
        public int QuantidadeDeSessoes { get; init; }

        /// <summary>Cenários de RV distintos que o participante percorreu.</summary>
        public int QuantidadeDeCenarios { get; init; }

        /// <summary>
        /// Tempo em cada cenário, do maior para o menor. Explica a contribuição do participante ao
        /// índice: cenários cujas sessões são curtas demais para a janela de detecção não produzem
        /// fixação e, portanto, não produzem elemento.
        /// </summary>
        public IReadOnlyList<PermanenciaEmCenario> PermanenciaPorCenario { get; init; }

        /// <summary>Soma da duração de todas as sessões, em minutos.</summary>
        public double TempoTotalEmRvMinutos { get; init; }

        /// <summary>Fixações detectadas somando todas as sessões do participante.</summary>
        public int QuantidadeDeFixacoes { get; init; }

        /// <summary>Duração média das fixações, em ms. NaN quando não há fixação.</summary>
        public double DuracaoMediaDasFixacoesMs { get; init; }

        /// <summary>Objetos distintos sobre os quais o olhar incidiu.</summary>
        public int QuantidadeDeObjetosObservados { get; init; }

        /// <summary>
        /// Os objetos de maior permanência do olhar, do mais observado para o menos, já limitados
        /// à quantidade exibida na tabela.
        /// </summary>
        public IReadOnlyList<ObjetoObservado> ObjetosMaisObservados { get; init; }
    }
}
