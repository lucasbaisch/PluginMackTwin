using System.Collections.Generic;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Efeito aleatório de um elemento projetual, estimado pela Camada 1 (§5.2) a partir das
    /// fixações das sessões carregadas.
    /// </summary>
    /// <remarks>
    /// Ambos os valores estão em milímetros de ΔD (variação pupilar corrigida pela linha de base),
    /// a mesma unidade de <see cref="MetricasPorObjeto.DeltaPupilarMedioMm"/>.
    /// </remarks>
    public class EfeitoDeElemento
    {
        /// <summary>
        /// Nome do elemento, correspondente a <see cref="LinhaDeSessao.NomeDoObjeto"/>.
        /// </summary>
        public string Elemento { get; init; }

        /// <summary>
        /// Cenário em que o efeito foi estimado, ou nulo quando a origem não o distingue.
        /// </summary>
        /// <remarks>
        /// A §5.2 fala em efeito de ELEMENTO, e o mesmo objeto pode responder de forma diferente em
        /// cenários diferentes — por isso a identidade do efeito é objeto × cenário.
        /// </remarks>
        public string Cenario { get; init; }

        /// <summary>Número de fixações que sustentam a estimativa.</summary>
        public int QuantidadeDeObservacoes { get; init; }

        /// <summary>
        /// ν̂_e — efeito ALEATÓRIO de elemento, em mm, já ENCOLHIDO (BLUP empírico-Bayes):
        /// a média bruta dos resíduos multiplicada por λ_e = τ²_ν / (τ²_ν + se²_e).
        /// Negativo indica constrição pupilar média atribuível ao elemento e, portanto,
        /// potencial restaurador.
        /// </summary>
        /// <remarks>
        /// O encolhimento é o que trata ν_e como efeito aleatório, conforme a Equação 3 declara:
        /// elementos com poucas fixações são puxados para zero na proporção da sua imprecisão, e
        /// elementos bem medidos quase não se movem. É também o controle de falsas descobertas
        /// nas comparações simultâneas entre elementos — o partial pooling faz por construção o
        /// que uma correção de multiplicidade faria por remendo (GELMAN; HILL; YAJIMA, 2012).
        /// </remarks>
        public double EfeitoNuMm { get; init; }

        /// <summary>
        /// Média bruta dos resíduos do elemento, em mm, ANTES do encolhimento. Preservada para
        /// auditoria: a razão EfeitoNuMm / EfeitoBrutoMm é o λ_e aplicado.
        /// </summary>
        public double EfeitoBrutoMm { get; init; } = double.NaN;

        /// <summary>
        /// λ_e — fator de encolhimento aplicado, em [0, 1]. Zero quando o erro-padrão não é
        /// estimável (o elemento é reduzido ao que o conjunto informa: nada); um quando o
        /// encolhimento está desativado por τ̂_ν indefinido.
        /// </summary>
        public double FatorDeEncolhimento { get; init; } = double.NaN;

        /// <summary>
        /// se(ν̂_e) — erro-padrão A POSTERIORI da estimativa encolhida, em mm: √λ_e · se_bruto.
        /// Mede a precisão da estimativa; entra na classificação, não na magnitude do índice.
        /// </summary>
        public double ErroPadraoMm { get; init; }

        /// <summary>Limite inferior do IC de 85% do efeito encolhido: ν̂_e − 1,44·se(ν̂_e).</summary>
        public double LimiteInferiorIc85Mm { get; init; } = double.NaN;

        /// <summary>Limite superior do IC de 85% do efeito encolhido: ν̂_e + 1,44·se(ν̂_e).</summary>
        public double LimiteSuperiorIc85Mm { get; init; } = double.NaN;
    }

    /// <summary>
    /// Saída da Camada 1: os efeitos por elemento e o σ_ΔD global da amostra.
    /// </summary>
    public class ResultadoDaCamada1
    {
        /// <summary>
        /// Efeitos indexados por nome de elemento, com comparação sem distinção de caixa — a mesma
        /// convenção do agrupamento por objeto em
        /// <see cref="Processamento.CalculadorDeMetricasPorObjeto"/>.
        /// </summary>
        public IReadOnlyDictionary<string, EfeitoDeElemento> EfeitosPorElemento { get; init; }

        /// <summary>
        /// σ_ΔD — desvio-padrão total da variação pupilar ΔD na amostra, em mm. Valor
        /// DESCRITIVO, mantido para diagnóstico; não é mais o denominador do índice.
        /// </summary>
        public double DesvioPadraoDoDeltaPupilarMm { get; init; } = double.NaN;

        /// <summary>
        /// τ̂_ν — desvio-padrão estimado dos efeitos ENTRE os elementos, em mm. É o denominador
        /// da magnitude do índice (Equação 4, §5.3.2): PH_e = tanh(−ν̂_e / τ̂_ν).
        /// </summary>
        /// <remarks>
        /// Estimado por método dos momentos: variância das médias brutas por elemento menos a
        /// média das variâncias amostrais, com piso em zero. NaN quando há menos elementos
        /// estimáveis que <see cref="Configuracao.ParametrosDaCamada1.MinimoDeElementosParaDispersao"/>,
        /// caso em que os índices saem no ponto neutro. Zero quando a variação entre elementos é
        /// indistinguível do ruído amostral — caso em que todos os efeitos encolhem a zero e
        /// nenhum elemento é afirmado.
        /// </remarks>
        public double DispersaoDosEfeitosMm { get; init; } = double.NaN;

        /// <summary>Inconsistências encontradas durante a estimação, para diagnóstico.</summary>
        public IReadOnlyList<string> Avisos { get; init; }

        /// <summary>
        /// Compõe a chave de um efeito a partir do objeto e do cenário. Cenário vazio produz a
        /// chave só com o nome do objeto.
        /// </summary>
        /// <remarks>
        /// O separador é o caractere de controle U+241F (Unit Separator), escolhido para não
        /// colidir com nenhum nome de objeto ou cenário vindo do Revit ou do ambiente virtual.
        /// </remarks>
        public static string ComporChave(string nomeDoObjeto, string cenario)
        {
            return string.IsNullOrWhiteSpace(cenario)
                ? nomeDoObjeto
                : nomeDoObjeto + SeparadorDeChave + cenario;
        }

        private const string SeparadorDeChave = "␟";

        /// <summary>Localiza o efeito de um objeto num cenário pela chave objeto × cenário.</summary>
        public bool TentarObterEfeito(string nomeDoObjeto, string cenario, out EfeitoDeElemento efeito)
        {
            efeito = null;

            if (EfeitosPorElemento == null || string.IsNullOrWhiteSpace(nomeDoObjeto))
                return false;

            return EfeitosPorElemento.TryGetValue(ComporChave(nomeDoObjeto, cenario), out efeito);
        }
    }
}
