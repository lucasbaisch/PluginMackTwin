using System;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Processamento
{
    /// <summary>
    /// Camada 2 do Modelo Matemático de Potencial Homeodinâmico (Equação 4, §5.2): converte o
    /// efeito de elemento estimado pela Camada 1 em índice PH_e e em classificação.
    /// </summary>
    /// <remarks>
    /// Regra pura e sem estado — depende só de ν̂_e, se(ν̂_e) e σ_ΔD. Vive em
    /// classe própria, e não dentro de <see cref="CalculadorDeMetricasPorObjeto"/>, porque é a
    /// equação da tese: precisa ser conferível isoladamente, sem montar sessões nem fixações.
    ///
    /// A Camada 1 não está aqui: vive em <see cref="EstimadorDeEfeitoDeElemento"/>, que estima ν̂_e,
    /// se(ν̂_e) e σ_ΔD a partir das fixações.
    /// </remarks>
    public static class CalculadorDaCamada2
    {
        /// <summary>
        /// Calcula PH_e = tanh(−ν̂_e / τ̂_ν), no intervalo aberto (−1, +1).
        /// </summary>
        /// <param name="efeitoNuMm">ν̂_e, efeito aleatório de elemento (encolhido) em mm.</param>
        /// <param name="dispersaoDosEfeitosMm">τ̂_ν, dispersão dos efeitos entre elementos, em mm.</param>
        /// <returns>
        /// PH_e em (−1, +1); 0 (neutro) quando τ̂_ν é ausente ou não positivo, caso em que não há
        /// escala contra a qual medir o efeito; NaN quando ν̂_e não foi estimado.
        /// </returns>
        /// <remarks>
        /// <b>O sinal negativo é a convenção fisiológica da tese, não um detalhe de implementação:</b>
        /// ν̂_e &lt; 0 é constrição pupilar média atribuível ao elemento, logo PH_e &gt; 0
        /// (restaurador, hitpoint verde); ν̂_e &gt; 0 é dilatação, logo PH_e &lt; 0 (estressor,
        /// hitpoint vermelho). Remover o sinal inverteria silenciosamente a leitura de todos os
        /// resultados.
        ///
        /// O denominador é τ̂_ν — o desvio-padrão estimado dos efeitos ENTRE os elementos
        /// (§5.3.2 do TextoFinal) —, que mede o quanto os elementos diferem entre si e põe cada
        /// efeito na escala dessa dispersão típica. Não é se(ν̂_e); ver <see cref="Classificar"/>
        /// e <see cref="ParametrosDaCamada2"/>. Como ν̂_e chega encolhido e τ̂_ν é a dispersão dos
        /// próprios efeitos, um elemento típico produz razão da ordem de 1 e o índice usa a
        /// escala inteira do intervalo (−1, +1).
        ///
        /// O intervalo (−1, +1) é aberto na matemática mas fechado em <c>double</c>: para
        /// |ν̂_e / τ̂_ν| acima de ~19 a tangente hiperbólica satura em exatamente ±1. Consumidores
        /// não devem tratar ±1 como valor impossível.
        /// </remarks>
        public static double CalcularPotencialHomeodinamico(
            double efeitoNuMm, double dispersaoDosEfeitosMm)
        {
            // Sem ν̂_e não há estimativa alguma — distinto de "estimativa neutra".
            if (double.IsNaN(efeitoNuMm))
                return double.NaN;

            // Sem τ̂_ν não há escala: o efeito existe mas não é mensurável em unidades da
            // dispersão entre elementos. O índice sai no ponto neutro, não indefinido.
            if (double.IsNaN(dispersaoDosEfeitosMm) || dispersaoDosEfeitosMm <= 0.0)
                return 0.0;

            return Math.Tanh(-efeitoNuMm / dispersaoDosEfeitosMm);
        }

        /// <summary>
        /// Classifica o elemento pela afirmabilidade estatística do efeito, isto é, pela posição do
        /// intervalo de confiança de 85% de ν̂_e em relação ao zero.
        /// </summary>
        /// <param name="efeitoNuMm">ν̂_e encolhido, em mm.</param>
        /// <param name="erroPadraoMm">se(ν̂_e) a posteriori, em mm.</param>
        /// <returns>
        /// Restaurador quando todo o IC está abaixo de zero (z &lt; −1,44); Estressor quando todo o
        /// IC está acima de zero (z &gt; +1,44); Neutro quando o IC contém o zero OU quando o
        /// erro-padrão não é estimável (menos de duas observações, ou dispersão nula), pois em
        /// ambos os casos falta evidência para afirmar;
        /// <see cref="ClassificacaoDePotencial.Neutro"/> apenas quando não há ν̂_e — isto é,
        /// quando o elemento não foi estimado de forma alguma.
        /// </returns>
        /// <remarks>
        /// O denominador aqui é se(ν̂_e), o ERRO-PADRÃO A POSTERIORI da estimativa — não τ̂_ν. Por
        /// isso um elemento pode ter |PH_e| alto e ainda assim ser Neutro: o efeito é grande, mas
        /// estimado com precisão insuficiente (tipicamente por poucas observações do elemento).
        /// Neutro significa insuficiência de evidência, não ausência de efeito.
        ///
        /// <b>Comparações simultâneas:</b> como ν̂_e e se(ν̂_e) chegam do encolhimento
        /// empírico-Bayes da Camada 1, a razão z aqui avaliada já é √λ_e vezes a razão bruta —
        /// sempre igual ou mais conservadora. É o partial pooling controlando as falsas
        /// descobertas das comparações entre todos os elementos por construção, no lugar de uma
        /// correção de multiplicidade posterior (GELMAN; HILL; YAJIMA, 2012).
        /// </remarks>
        public static ClassificacaoDePotencial Classificar(double efeitoNuMm, double erroPadraoMm)
        {
            if (double.IsNaN(efeitoNuMm))
                return ClassificacaoDePotencial.Neutro;

            // Erro-padrão não estimável (n < 2) ou nulo: existe efeito, falta incerteza. É
            // insuficiência de evidência, exatamente o que Neutro significa no Quadro 41.
            if (double.IsNaN(erroPadraoMm) || erroPadraoMm <= 0.0)
                return ClassificacaoDePotencial.Neutro;

            double razaoDeSignificancia = efeitoNuMm / erroPadraoMm;

            if (razaoDeSignificancia < -ParametrosDaCamada2.QuantilNormal85)
                return ClassificacaoDePotencial.Restaurador;

            if (razaoDeSignificancia > ParametrosDaCamada2.QuantilNormal85)
                return ClassificacaoDePotencial.Estressor;

            return ClassificacaoDePotencial.Neutro;
        }

        /// <summary>
        /// Razão de significância z = ν̂_e / se(ν̂_e), exposta para auditoria e para a tabela da aba
        /// de Potencial Homeodinâmico. NaN quando não calculável.
        /// </summary>
        public static double CalcularRazaoDeSignificancia(double efeitoNuMm, double erroPadraoMm)
        {
            if (double.IsNaN(efeitoNuMm) || double.IsNaN(erroPadraoMm) || erroPadraoMm <= 0.0)
                return double.NaN;

            return efeitoNuMm / erroPadraoMm;
        }
    }
}
