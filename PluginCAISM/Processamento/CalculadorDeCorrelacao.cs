using System;
using System.Collections.Generic;
using System.Linq;

namespace PluginCAISM.Processamento
{
    /// <summary>
    /// Resultado de uma correlação bivariada.
    /// </summary>
    public class ResultadoDeCorrelacao
    {
        /// <summary>Pares completos que entraram no cálculo — o n efetivo.</summary>
        public int QuantidadeDePares { get; init; }

        /// <summary>
        /// Coeficiente de Pearson (r), no intervalo [−1, +1]. NaN quando indefinido: menos de três
        /// pares, ou variância nula em algum dos eixos.
        /// </summary>
        public double Pearson { get; init; }

        /// <summary>
        /// Coeficiente de Spearman (ρ), o Pearson sobre os postos. Mesmas condições de indefinição.
        /// </summary>
        public double Spearman { get; init; }
    }

    /// <summary>
    /// Correlações de Pearson e de Spearman entre duas variáveis.
    /// </summary>
    /// <remarks>
    /// Calculadora pura, sem dependência de biblioteca estatística.
    ///
    /// <b>Os dois coeficientes convivem de propósito.</b> Pearson mede associação linear e supõe
    /// normalidade aproximada; Spearman mede associação monotônica sobre os postos e não a supõe.
    /// Com os 26 respondentes desta pesquisa, IMC e escore IEEE não são normais, e é o Spearman que
    /// sustenta afirmação. Exibir só o Pearson convidaria a ler como ausência de relação o que pode
    /// ser relação não linear.
    ///
    /// Nenhum p-valor é produzido: exigiria a distribuição t e uma decisão sobre correção para
    /// múltiplas comparações — com 25 pares de eixos possíveis, reportar p sem correção seria pior
    /// que não reportar.
    /// </remarks>
    public static class CalculadorDeCorrelacao
    {
        /// <summary>Mínimo de pares para que um coeficiente seja definido.</summary>
        private const int MinimoDePares = 3;

        /// <summary>
        /// Correlaciona duas séries pareadas por posição, descartando os pares em que qualquer
        /// dos valores seja NaN.
        /// </summary>
        public static ResultadoDeCorrelacao Calcular(IEnumerable<double> x, IEnumerable<double> y)
        {
            if (x == null || y == null)
                return Indefinido(0);

            var pares = x.Zip(y, (valorX, valorY) => (X: valorX, Y: valorY))
                .Where(par => !double.IsNaN(par.X) && !double.IsNaN(par.Y))
                .ToList();

            if (pares.Count < MinimoDePares)
                return Indefinido(pares.Count);

            var serieX = pares.Select(par => par.X).ToList();
            var serieY = pares.Select(par => par.Y).ToList();

            return new ResultadoDeCorrelacao
            {
                QuantidadeDePares = pares.Count,
                Pearson  = CalcularPearson(serieX, serieY),
                Spearman = CalcularPearson(ConverterEmPostos(serieX), ConverterEmPostos(serieY)),
            };
        }

        private static ResultadoDeCorrelacao Indefinido(int pares) => new()
        {
            QuantidadeDePares = pares,
            Pearson  = double.NaN,
            Spearman = double.NaN,
        };

        private static double CalcularPearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            double mediaX = x.Average();
            double mediaY = y.Average();

            double covariancia = 0, variacaoX = 0, variacaoY = 0;

            for (int i = 0; i < x.Count; i++)
            {
                double desvioX = x[i] - mediaX;
                double desvioY = y[i] - mediaY;

                covariancia += desvioX * desvioY;
                variacaoX   += desvioX * desvioX;
                variacaoY   += desvioY * desvioY;
            }

            // Variância nula em qualquer eixo: a reta não existe e o coeficiente é indefinido, não
            // zero. Acontece de fato aqui — se todos os participantes de um recorte têm a mesma
            // faixa etária, o eixo Idade é constante.
            if (variacaoX <= 0 || variacaoY <= 0)
                return double.NaN;

            return covariancia / Math.Sqrt(variacaoX * variacaoY);
        }

        /// <summary>
        /// Converte a série em postos, atribuindo a média dos postos aos valores empatados.
        /// </summary>
        /// <remarks>
        /// O tratamento de empates importa nestes dados: idade em faixas de cinco anos e atividade
        /// física em dias por semana produzem muitos valores repetidos, e atribuir postos
        /// arbitrários a eles enviesaria o ρ.
        /// </remarks>
        private static List<double> ConverterEmPostos(IReadOnlyList<double> valores)
        {
            var ordenados = valores
                .Select((valor, indice) => (Valor: valor, Indice: indice))
                .OrderBy(item => item.Valor)
                .ToList();

            var postos = new double[valores.Count];
            int posicao = 0;

            while (posicao < ordenados.Count)
            {
                int fim = posicao;
                while (fim + 1 < ordenados.Count
                       && ordenados[fim + 1].Valor == ordenados[posicao].Valor)
                {
                    fim++;
                }

                // Postos são de base 1; a média de um bloco de empatados vai para todos eles.
                double postoMedio = (posicao + fim) / 2.0 + 1;

                for (int i = posicao; i <= fim; i++)
                    postos[ordenados[i].Indice] = postoMedio;

                posicao = fim + 1;
            }

            return postos.ToList();
        }
    }
}
