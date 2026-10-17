using System.Collections.Generic;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Formulários respondidos por um participante (Arquivo 4), com as respostas brutas e os
    /// escores derivados usados na aba de contextualização de dados complementares (§5.5.1).
    /// </summary>
    /// <remarks>
    /// Estes dados são exclusivamente de CONTEXTUALIZAÇÃO (§5.3): não entram no cálculo do índice
    /// de potencial restaurador e não passam pelo <see cref="Processamento.ConstruturDeSessoes"/>.
    /// A ingestão é paralela — <see cref="Parsing.AnalisadorExcelFormularios"/> lê a planilha e
    /// <see cref="Processamento.CalculadorDeEscoresDeFormulario"/> deriva os escores.
    ///
    /// Escores não configurados em <see cref="Configuracao.ComposicaoDeEscoresDeFormulario"/>
    /// chegam como double.NaN, e não como zero, para que a ausência de mapeamento seja visível
    /// na interface em vez de se disfarçar de resultado.
    /// </remarks>
    public class RespostasDeFormularioPorParticipante
    {
        /// <summary>
        /// Identificador do participante, correspondente a
        /// <see cref="Sessao.IdentificacaoManual"/>. Exemplo: "MA001".
        /// </summary>
        public string IdentificacaoManual { get; init; }

        /// <summary>
        /// Escore total do IEEE, na faixa 34–170. NaN quando a coluna não está mapeada ou o valor
        /// não é numérico.
        /// </summary>
        public double EscoreIeeeTotal { get; init; }

        /// <summary>
        /// Faixa de classificação do <see cref="EscoreIeeeTotal"/> segundo
        /// <see cref="Configuracao.LimiaresDeClassificacaoIeee"/>. Vazio quando o escore é NaN.
        /// </summary>
        public string ClassificacaoIeee { get; init; }

        // EscoresPorDominioDeEstresse e ContagemDeSintomasSbs foram REMOVIDAS. Ambas saíam sempre
        // NaN e nada as substituía: a primeira somava colunas cuja composição nunca foi
        // configurada; a segunda tentava somar respostas que no arquivo real são rótulos de texto
        // ("NUNCA", "RARO", "OCASIONAL", "FREQUENTE"), e não números.
        //
        // O que faz o trabalho de cada uma:
        //   • decomposição do IEEE por domínio — Configuracao.DominiosDoIeee, que associa os 34
        //     itens aos cinco domínios pelo enunciado e é o que a aba de Dados do Participante usa;
        //   • severidade do SBS — Processamento.CalculadorDeIndicadoresDeSbs, que aplica a escala
        //     ordinal aos rótulos e produz IndicadoresDeSbs.Severidade, usada nos gráficos.

        /// <summary>Idade do participante. NaN quando não mapeada ou não numérica.</summary>
        public double Idade { get; init; }

        /// <summary>Peso do participante. NaN quando não mapeado ou não numérico.</summary>
        public double Peso { get; init; }

        /// <summary>
        /// Respostas brutas do participante, indexadas pelo nome da coluna da planilha.
        /// Preservadas na íntegra porque a tabela-síntese da §5.5.1 exibe as respostas por questão,
        /// e não apenas os escores.
        /// </summary>
        public Dictionary<string, string> RespostasBrutas { get; init; }

        /// <summary>
        /// Nomes das colunas na ORDEM em que aparecem nas planilhas, da esquerda para a direita e
        /// na sequência em que as planilhas foram lidas.
        /// </summary>
        /// <remarks>
        /// As tabelas da aba de Dados do Participante iteram esta lista, e não
        /// <see cref="RespostasBrutas"/>, para exibir as respostas na mesma sequência do arquivo.
        /// </remarks>
        public List<string> OrdemDasColunas { get; init; } = new List<string>();

        /// <summary>Número da linha na planilha de origem, para rastreabilidade.</summary>
        public int NumeroDaLinhaOriginal { get; init; }
    }

    /// <summary>
    /// Formulários disponíveis para seleção na aba de contextualização.
    /// </summary>
    /// <remarks>
    /// Determina qual subconjunto de questões a tabela-síntese exibe para o participante
    /// selecionado.
    /// </remarks>
    public enum TipoDeFormulario
    {
        /// <summary>Todas as respostas da planilha "a_recrutamento" (demografia e histórico).</summary>
        Recrutamento,

        /// <summary>
        /// Todas as respostas da planilha "b_ieee": os 34 itens de estresse ocupacional e a
        /// coluna TOTAL, mais o escore e a faixa de classificação derivados.
        /// </summary>
        Ieee,

        /// <summary>Todas as respostas da planilha "c_percepção_ambiental".</summary>
        PercepcaoAmbiental,

        // EstresseOcupacional foi REMOVIDO: nunca esteve no menu de formulários e a tabela que
        // montava vinha de escores por domínio sem composição configurada. A decomposição do IEEE
        // por domínio é feita dentro da própria categoria Ieee, por Configuracao.DominiosDoIeee.

        /// <summary>Os 14 itens do Q37 (frequência) e os 14 do Q38 (atribuição ao trabalho).</summary>
        SindromeDoEdificioDoente,

        /// <summary>Variáveis demográficas (idade, peso).</summary>
        Demografia,

        /// <summary>
        /// Retrato da coleta em realidade virtual do participante, vindo do <c>dados_hmd.csv</c>
        /// e não da planilha de formulários.
        /// </summary>
        /// <remarks>
        /// Único tipo cujo participante NÃO é o do seletor do topo da aba: os identificadores do
        /// HMD (MA001, LF001, …) não têm correspondência com os dos formulários, por isso a
        /// categoria traz seletor próprio.
        /// </remarks>
        DadosDoHmd,

        /// <summary>
        /// Coletas de cortisol salivar do participante e o delta (coluna S − coluna B).
        /// Vem do Input 02, não da planilha de formulários.
        /// </summary>
        Cortisol,

        /// <summary>
        /// Síntese cruzada do participante: demografia, IMC, IEEE, cortisol, concordância
        /// fisiologia ↔ percepção e ressalvas sobre confundidores do cortisol.
        /// </summary>
        Sintese,

        /// <summary>Todas as respostas brutas, de todas as planilhas.</summary>
        TodasAsRespostas
    }
}
