using System.Collections.Generic;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Métricas agregadas de potencial restaurador e estressor para um objeto virtual específico,
    /// calculadas a partir das fixações e dados ambientais das sessões filtradas.
    /// </summary>
    /// <remarks>
    /// Calculado por <see cref="Processamento.CalculadorDeMetricasPorObjeto"/> para cada
    /// <see cref="LinhaDeSessao.NomeDoObjeto"/> distinto presente nas sessões ativas.
    ///
    /// Os thresholds que definem os limiares de classificação restaurador/estressor
    /// estão em <see cref="Configuracao.LimiaresDePotencialRestaurador"/> e
    /// <see cref="Configuracao.FaixasDeConfortoAmbiental"/>; os pesos ainda precisam ser
    /// confirmados na literatura (Teoria da Restauração da Atenção — Kaplan; Science Direct;
    /// Frontiers in Neuroscience). Não usar valores arbitrários.
    /// </remarks>
    public class MetricasPorObjeto
    {
        /// <summary>Nome do objeto virtual. Exemplo: "Parede: Rippas".</summary>
        public string NomeDoObjeto { get; init; }

        /// <summary>
        /// Cenário em que estas métricas foram apuradas. Compõe a identidade do registro junto com
        /// <see cref="NomeDoObjeto"/>.
        /// </summary>
        /// <remarks>
        /// A §5.2 estima efeito de ELEMENTO, e o mesmo objeto pode responder de forma diferente em
        /// cenários diferentes — daí a chave ser objeto × cenário, e não só objeto. Com um cenário
        /// específico filtrado há um registro por objeto; com "Todos os Cenários", um por par.
        /// </remarks>
        public string Cenario { get; init; }

        /// <summary>
        /// Duração média das fixações para este objeto em milissegundos
        /// (average fixation duration). Métrica primária de potencial restaurador.
        /// Janela de fixação focal adotada na pontuação: 300–500 ms (§5.1 do TextoFinal).
        /// </summary>
        public double DuracaoMediaDasFixacoesMs { get; init; }

        /// <summary>
        /// Temperatura do ar, em °C, média das leituras vigentes no momento das fixações do
        /// elemento. NaN quando nenhuma fixação teve leitura ambiental associada.
        /// </summary>
        public double TemperaturaMediaCelsius { get; init; }

        /// <summary>
        /// Umidade relativa do ar, em %, média das leituras vigentes no momento das fixações.
        /// NaN quando nenhuma fixação teve leitura ambiental associada.
        /// </summary>
        public double UmidadeMediaPercentual { get; init; }

        /// <summary>
        /// Nível de pressão sonora, em dB — média de todas as fixações do elemento com leitura
        /// ambiental vigente (coluna "RUÍDO (dB)" do arquivo de sensores). NaN quando nenhuma
        /// fixação do elemento teve leitura.
        /// </summary>
        public double RuidoMedioDb { get; init; }

        /// <summary>
        /// Fixações do elemento em que o sensor acusou presença de gás (coluna F do arquivo de
        /// sensores).
        /// </summary>
        public int QuantidadeDeFixacoesComGasDetectado { get; init; }

        /// <summary>Fixações do elemento que tinham leitura ambiental vigente associada.</summary>
        public int QuantidadeDeFixacoesComLeituraAmbiental { get; init; }

        /// <summary>
        /// Média dos diâmetros pupilares absolutos (esquerdo e direito) durante as fixações
        /// neste objeto, em milímetros. Valor descritivo, exibido na interface.
        /// A pontuação usa <see cref="DeltaPupilarMedioMm"/>, não este valor absoluto.
        /// </summary>
        public double DiametroPupilaMedioMm { get; init; }

        /// <summary>
        /// Média do ΔD das fixações deste objeto, em milímetros, onde
        /// ΔD = diâmetro pupilar médio da fixação − <see cref="Sessao.BaselinePupilar"/> da sessão
        /// que a originou (correção subtrativa da §5.2 do TextoFinal).
        /// </summary>
        /// <remarks>
        /// ΔD negativo indica constrição pupilar em relação à linha de base pré-estímulo e,
        /// portanto, potencial restaurador (§5.2, Quadros 36 e 40).
        /// Retorna double.NaN quando nenhuma fixação do objeto tem baseline e pupila válidas;
        /// nesse caso o objeto é excluído do termo pupilar da pontuação.
        /// </remarks>
        public double DeltaPupilarMedioMm { get; init; }

        /// <summary>
        /// Sub-índice de conforto ambiental no intervalo [0.0, 1.0], calculado a partir de
        /// <see cref="MediasDosValoresDeSensores"/> e das faixas de
        /// <see cref="Configuracao.FaixasDeConfortoAmbiental"/> (Tabela 31 do TextoFinal).
        /// </summary>
        /// <remarks>
        /// Cada parâmetro localizado (temperatura, umidade, ruído, monóxido de carbono) pontua
        /// 1,0 dentro da faixa de conforto e decai linearmente fora dela; o sub-índice é a média
        /// dos parâmetros encontrados. Vale 0,5 (neutro) quando nenhum parâmetro é localizado.
        /// </remarks>
        public double PontuacaoAmbiental { get; init; }

        /// <summary>
        /// Pontuação de potencial no intervalo [-1.0, +1.0]: valores positivos indicam potencial
        /// restaurador, negativos indicam potencial estressor e zero indica neutralidade
        /// (intervalo da Equação 4 do TextoFinal).
        /// </summary>
        /// <remarks>
        /// Média ponderada de três sub-índices normalizados em [0, 1] — duração média das
        /// fixações, ΔD pupilar com orientação invertida (constrição = restaurador) e
        /// <see cref="PontuacaoAmbiental"/>, com os pesos de
        /// <see cref="Configuracao.LimiaresDePotencialRestaurador"/> — mapeada para (-1, +1) por
        /// (2·x − 1).
        /// Quando <see cref="DeltaPupilarMedioMm"/> é NaN, o termo pupilar é omitido e os pesos
        /// restantes são renormalizados antes do mapeamento.
        /// </remarks>
        /// <summary>
        /// PH_e — índice de Potencial Homeodinâmico do elemento, no intervalo (−1, +1), calculado
        /// pela Equação 4 (§5.2) a partir do efeito ν̂_e importado da Camada 1:
        /// PH_e = tanh(−ν̂_e / σ_ΔD).
        /// </summary>
        /// <remarks>
        /// Positivo = restaurador, negativo = estressor. Vale 0 quando σ_ΔD é nulo ou indefinido, e
        /// NaN apenas quando o elemento não recebeu estimativa de ν̂_e.
        ///
        /// Não confundir com <see cref="ClassificacaoPH"/>: a magnitude vem de σ_ΔD, a classificação
        /// vem de se(ν̂_e). Um elemento pode ter |PH_e| alto e ser classificado como Neutro.
        /// </remarks>
        public double PotencialHomeodinamico { get; init; } = double.NaN;

        /// <summary>
        /// Classificação do elemento por afirmabilidade estatística do efeito, conforme o
        /// Quadro 41: Restaurador / Estressor quando o IC 85% de ν̂_e não contém o zero, Neutro
        /// quando contém.
        /// </summary>
        /// <remarks>
        /// Sempre preenchida, vindo do IC 85% de ν̂_e
        /// (<see cref="Processamento.CalculadorDaCamada2.Classificar"/>).
        /// <see cref="ClassificacaoDePotencial.Neutro"/> indica elemento observado nas sessões
        /// mas sem estimativa de efeito.
        /// </remarks>
        public ClassificacaoDePotencial ClassificacaoPH { get; init; } = ClassificacaoDePotencial.Neutro;

        /// <summary>
        /// ν̂_e — efeito de elemento, em mm de ΔD, ou NaN quando não estimado. Guardado para
        /// auditoria: permite conferir PH_e e a classificação a partir das entradas.
        /// </summary>
        public double EfeitoNuMm { get; init; } = double.NaN;

        /// <summary>
        /// se(ν̂_e) — erro-padrão do efeito, em mm, ou NaN quando não estimável (menos de duas
        /// observações do elemento). Denominador da razão de significância.
        /// </summary>
        public double ErroPadraoDoEfeitoMm { get; init; } = double.NaN;

        /// <summary>
        /// z = ν̂_e / se(ν̂_e) — estatística que decide a <see cref="ClassificacaoPH"/>. NaN quando
        /// não calculável. Guardada para auditoria e relatório.
        /// </summary>
        /// <remarks>
        /// |z| ≤ 1,44 significa que o intervalo de confiança de 85% contém o zero, logo Neutro.
        /// </remarks>
        public double EstatisticaZ { get; init; } = double.NaN;

        /// <summary>Número de fixações que sustentam a estimativa do efeito deste elemento.</summary>
        public int QuantidadeDeObservacoes { get; init; }

        /// <summary>
        /// Médias dos valores dos sensores ambientais durante as fixações neste objeto.
        /// Chaves são os nomes das colunas do CSV de sensores (temperatura, umidade, CO₂, ruído, etc.).
        /// Valores são as médias numéricas calculadas a partir de <see cref="LinhaDeSessao.CamposSensor"/>.
        /// </summary>
        public Dictionary<string, double> MediasDosValoresDeSensores { get; init; }
    }
}
