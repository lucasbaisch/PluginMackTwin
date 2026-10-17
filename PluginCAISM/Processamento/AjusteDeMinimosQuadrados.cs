using System;
using System.Collections.Generic;

namespace PluginCAISM.Processamento
{
    /// <summary>
    /// Resultado de um ajuste por mínimos quadrados ordinários.
    /// </summary>
    public sealed class ResultadoDoAjuste
    {
        /// <summary>Coeficientes estimados, um por coluna da matriz de delineamento.</summary>
        public double[] Coeficientes { get; init; }

        /// <summary>
        /// Índices das colunas que não puderam ser estimadas por dependência linear com as
        /// demais. O coeficiente correspondente sai zero.
        /// </summary>
        /// <remarks>
        /// Acontece por construção quando um participante aparece em um único cenário, ou quando
        /// um regressor ambiental é constante na amostra — caso da coluna de gás no arquivo
        /// original, que vale 0,83 em todas as leituras. Não é erro: é ausência de informação
        /// para separar aquele efeito dos outros.
        /// </remarks>
        public IReadOnlyList<int> ColunasNaoEstimaveis { get; init; }

        /// <summary>Valores ajustados, um por observação.</summary>
        public double[] Ajustados { get; init; }

        /// <summary>Resíduos y − ŷ, um por observação.</summary>
        public double[] Residuos { get; init; }
    }

    /// <summary>
    /// Ajuste por mínimos quadrados ordinários pelas equações normais.
    /// </summary>
    /// <remarks>
    /// Existe porque a Camada 1 (Equação 3) precisa descontar covariáveis — cenário, linha de
    /// base, posição do olhar e as quatro ambientais — e a pesquisa não admite pacote estatístico
    /// externo. O porte do problema torna a solução direta adequada: a matriz de delineamento tem
    /// algumas dezenas de colunas contra milhares de linhas, de modo que X'X é pequena e a
    /// eliminação gaussiana a resolve sem dificuldade numérica relevante.
    ///
    /// <b>O que este código NÃO faz:</b> não é ajuste misto por REML, não estima componentes de
    /// variância e não produz erros-padrão dos coeficientes. Serve para obter os valores ajustados
    /// e, com eles, os resíduos sobre os quais o efeito de elemento é calculado.
    ///
    /// Colunas linearmente dependentes são detectadas pelo pivô e recebem coeficiente zero, em vez
    /// de fazer a inversão explodir. É o comportamento que qualquer software estatístico adota ao
    /// declarar um termo "aliased".
    /// </remarks>
    public static class AjusteDeMinimosQuadrados
    {
        /// <summary>
        /// Tolerância relativa do pivô abaixo da qual a coluna é dada por não estimável.
        /// </summary>
        private const double ToleranciaDoPivo = 1e-9;

        /// <summary>
        /// Ajusta y sobre as colunas de <paramref name="delineamento"/>.
        /// </summary>
        /// <param name="delineamento">
        /// Matriz X, uma linha por observação. Todas as linhas devem ter o mesmo comprimento.
        /// O intercepto, se desejado, entra como uma coluna de uns — não é acrescentado aqui.
        /// </param>
        /// <param name="resposta">Vetor y, do mesmo comprimento que o número de linhas.</param>
        /// <returns>Coeficientes, ajustados e resíduos; nulo quando não há o que ajustar.</returns>
        public static ResultadoDoAjuste Ajustar(IReadOnlyList<double[]> delineamento, IReadOnlyList<double> resposta)
        {
            if (delineamento == null || resposta == null || delineamento.Count == 0)
                return null;

            int observacoes = delineamento.Count;
            int colunas = delineamento[0].Length;

            if (colunas == 0 || resposta.Count != observacoes)
                return null;

            // X'X e X'y. A simetria de X'X é explorada preenchendo só o triângulo superior.
            var normal = new double[colunas][];
            for (int i = 0; i < colunas; i++)
                normal[i] = new double[colunas];

            var termoIndependente = new double[colunas];

            for (int linha = 0; linha < observacoes; linha++)
            {
                double[] x = delineamento[linha];
                double y = resposta[linha];

                for (int i = 0; i < colunas; i++)
                {
                    if (x[i] == 0.0) continue;      // atalho: matrizes de indicadores são esparsas

                    termoIndependente[i] += x[i] * y;

                    for (int j = i; j < colunas; j++)
                        normal[i][j] += x[i] * x[j];
                }
            }

            for (int i = 0; i < colunas; i++)
                for (int j = 0; j < i; j++)
                    normal[i][j] = normal[j][i];

            AplicarRegularizacao(normal);

            var naoEstimaveis = new List<int>();
            double[] coeficientes = ResolverSistema(normal, termoIndependente, naoEstimaveis);

            var ajustados = new double[observacoes];
            var residuos = new double[observacoes];

            for (int linha = 0; linha < observacoes; linha++)
            {
                double[] x = delineamento[linha];
                double soma = 0.0;

                for (int i = 0; i < colunas; i++)
                    soma += x[i] * coeficientes[i];

                ajustados[linha] = soma;
                residuos[linha] = resposta[linha] - soma;
            }

            return new ResultadoDoAjuste
            {
                Coeficientes         = coeficientes,
                ColunasNaoEstimaveis = naoEstimaveis,
                Ajustados            = ajustados,
                Residuos             = residuos,
            };
        }

        /// <summary>
        /// Fator de regularização somado à diagonal de X'X, relativo à escala da própria matriz.
        /// </summary>
        private const double FatorDeRegularizacao = 1e-8;

        /// <summary>
        /// Soma um termo mínimo à diagonal de X'X, tornando o sistema sempre solúvel.
        /// </summary>
        /// <remarks>
        /// Com colunas linearmente dependentes — um participante que aparece em um único cenário,
        /// dois regressores ambientais que andam juntos — a matriz normal é singular, e a
        /// eliminação produz coeficientes gigantescos governados apenas por erro de arredondamento.
        /// A regularização impede isso.
        ///
        /// <b>Ela é inofensiva aqui por uma razão específica:</b> nenhum coeficiente deste ajuste é
        /// interpretado. A Camada 1 usa apenas os RESÍDUOS, e o espaço gerado pelas colunas é o
        /// mesmo com ou sem regularização — o que muda é como o ajuste reparte o efeito entre
        /// colunas redundantes, não o valor ajustado. Se algum dia os coeficientes passarem a ser
        /// reportados como β_T, β_H e afins, esta escolha precisa ser revista, porque um
        /// coeficiente regularizado é enviesado para zero.
        /// </remarks>
        private static void AplicarRegularizacao(double[][] normal)
        {
            int ordem = normal.Length;
            double soma = 0.0;

            for (int i = 0; i < ordem; i++)
                soma += Math.Abs(normal[i][i]);

            if (soma <= 0.0)
                return;

            double termo = (soma / ordem) * FatorDeRegularizacao;

            for (int i = 0; i < ordem; i++)
                normal[i][i] += termo;
        }

        /// <summary>
        /// Eliminação gaussiana com pivotamento parcial. Colunas cujo pivô não alcança a tolerância
        /// são eliminadas do sistema e o coeficiente sai zero.
        /// </summary>
        private static double[] ResolverSistema(
            double[][] matriz, double[] termoIndependente, List<int> naoEstimaveis)
        {
            int ordem = termoIndependente.Length;
            var solucao = new double[ordem];

            // Escala de referência para a tolerância: o maior elemento da diagonal original.
            double escala = 0.0;
            for (int i = 0; i < ordem; i++)
                escala = Math.Max(escala, Math.Abs(matriz[i][i]));

            if (escala <= 0.0)
            {
                for (int i = 0; i < ordem; i++) naoEstimaveis.Add(i);
                return solucao;
            }

            double limiar = escala * ToleranciaDoPivo;
            var ordemDasColunas = new int[ordem];
            for (int i = 0; i < ordem; i++) ordemDasColunas[i] = i;

            var ativa = new bool[ordem];
            for (int i = 0; i < ordem; i++) ativa[i] = true;

            for (int passo = 0; passo < ordem; passo++)
            {
                // pivô: maior valor absoluto na coluna, entre as linhas ainda não usadas
                int melhorLinha = -1;
                double melhorValor = limiar;

                for (int linha = passo; linha < ordem; linha++)
                {
                    double valor = Math.Abs(matriz[linha][passo]);
                    if (valor > melhorValor) { melhorValor = valor; melhorLinha = linha; }
                }

                if (melhorLinha < 0)
                {
                    // coluna dependente das anteriores
                    ativa[passo] = false;
                    naoEstimaveis.Add(passo);
                    continue;
                }

                if (melhorLinha != passo)
                {
                    (matriz[passo], matriz[melhorLinha]) = (matriz[melhorLinha], matriz[passo]);
                    (termoIndependente[passo], termoIndependente[melhorLinha]) =
                        (termoIndependente[melhorLinha], termoIndependente[passo]);
                }

                double pivo = matriz[passo][passo];

                for (int linha = passo + 1; linha < ordem; linha++)
                {
                    double fator = matriz[linha][passo] / pivo;
                    if (fator == 0.0) continue;

                    for (int coluna = passo; coluna < ordem; coluna++)
                        matriz[linha][coluna] -= fator * matriz[passo][coluna];

                    termoIndependente[linha] -= fator * termoIndependente[passo];
                }
            }

            // substituição regressiva, pulando as colunas declaradas não estimáveis
            for (int linha = ordem - 1; linha >= 0; linha--)
            {
                if (!ativa[linha]) { solucao[linha] = 0.0; continue; }

                double soma = termoIndependente[linha];

                for (int coluna = linha + 1; coluna < ordem; coluna++)
                    soma -= matriz[linha][coluna] * solucao[coluna];

                double pivo = matriz[linha][linha];
                solucao[linha] = Math.Abs(pivo) > 0.0 ? soma / pivo : 0.0;
            }

            return solucao;
        }
    }
}
