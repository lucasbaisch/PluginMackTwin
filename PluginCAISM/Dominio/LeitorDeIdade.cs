using System.Globalization;
using System.Text.RegularExpressions;
using PluginCAISM.Configuracao;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Interpreta a resposta de idade do formulário de recrutamento e a agrupa em faixas.
    /// </summary>
    /// <remarks>
    /// Regra pura, sem dependência de arquivos ou da interface. O tamanho da faixa está em
    /// <see cref="FaixaEtaria"/>.
    /// </remarks>
    public static class LeitorDeIdade
    {
        /// <summary>
        /// Converte a resposta de idade em número de anos.
        /// </summary>
        /// <param name="resposta">Conteúdo da coluna de idade.</param>
        /// <returns>A idade em anos, ou NaN quando a resposta é ausente ou não interpretável.</returns>
        /// <remarks>
        /// Aceita duas formas, porque o arquivo já apareceu nas duas:
        /// idade individual ("46") e faixa em texto ("40 a 49 anos"). No segundo caso é usado o
        /// PRIMEIRO número do texto, isto é, o limite inferior da faixa declarada.
        /// </remarks>
        // VERIFICADO no arquivo definitivo (dados_formularios.xlsx, a_recrutamento, Questão 5,
        // 31/08/2026): as 26 respostas são números individuais (uma é "-", que vira NaN, como
        // deve). O fallback de faixa em texto nunca é exercido com os dados definitivos — fica
        // mantido apenas como robustez contra planilhas antigas, sem custo quando não usado.
        public static double LerIdadeEmAnos(string resposta)
        {
            if (string.IsNullOrWhiteSpace(resposta))
                return double.NaN;

            string texto = resposta.Trim();

            if (double.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out double idadeDireta))
                return ValidarPlausibilidade(idadeDireta);

            var primeiroNumero = Regex.Match(texto, @"\d+");

            if (primeiroNumero.Success
                && double.TryParse(primeiroNumero.Value, NumberStyles.Any,
                                   CultureInfo.InvariantCulture, out double idadeDoTexto))
            {
                return ValidarPlausibilidade(idadeDoTexto);
            }

            return double.NaN;
        }

        /// <summary>
        /// Limite inferior da faixa etária de uma idade. Ex.: 46 → 45; 32 → 30.
        /// </summary>
        /// <returns>O limite inferior, ou NaN se a idade for NaN.</returns>
        public static double ObterInicioDaFaixa(double idadeEmAnos)
        {
            if (double.IsNaN(idadeEmAnos))
                return double.NaN;

            int tamanho = FaixaEtaria.TamanhoEmAnos;
            return System.Math.Floor(idadeEmAnos / tamanho) * tamanho;
        }

        /// <summary>Rótulo de uma faixa a partir do seu limite inferior. Ex.: 45 → "45–49".</summary>
        public static string ObterRotuloDaFaixa(double inicioDaFaixa)
        {
            int inicio = (int)inicioDaFaixa;
            return $"{inicio}–{inicio + FaixaEtaria.TamanhoEmAnos - 1}";
        }

        /// <summary>
        /// Descarta valores fora da faixa plausível: um número perdido em campo de texto livre
        /// não deve virar idade.
        /// </summary>
        private static double ValidarPlausibilidade(double idade)
        {
            return idade >= FaixaEtaria.IdadeMinimaPlausivel && idade <= FaixaEtaria.IdadeMaximaPlausivel
                ? idade
                : double.NaN;
        }
    }
}
