using System;
using System.Globalization;
using System.Text.RegularExpressions;
using PluginCAISM.Configuracao;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Interpreta a resposta de atividade física (Q20) e a converte em dias por semana.
    /// </summary>
    /// <remarks>
    /// Regra pura, sem dependência de arquivos ou da interface. Os termos reconhecidos estão em
    /// <see cref="RegrasDeAtividadeFisica"/>.
    ///
    /// <b>Sem consumidor no momento.</b> A atividade física deixou de ser uma das variáveis
    /// plotáveis quando os oito gráficos fixos deram lugar ao gráfico dinâmico de cinco variáveis.
    /// O leitor permanece porque reincluí-la é só acrescentar um descritor a
    /// <c>VariaveisDoGrupo.Selecionaveis</c> — e a leitura das respostas em texto livre já está
    /// resolvida e verificada contra o arquivo real.
    /// </remarks>
    public static class LeitorDeAtividadeFisica
    {
        /// <summary>
        /// Converte a resposta em número de dias de atividade por semana.
        /// </summary>
        /// <param name="resposta">Conteúdo da coluna de atividade física.</param>
        /// <returns>
        /// Dias por semana em 0–7, ou NaN quando a resposta é ausente ou não interpretável.
        /// </returns>
        /// <remarks>
        /// A ordem das regras importa: "NÃO" é testado primeiro porque uma resposta negativa pode
        /// conter dígitos em outro contexto; as expressões por extenso vêm antes da extração
        /// numérica porque não têm dígito algum; e só então se extrai o primeiro inteiro, que
        /// resolve "SIM, 3x/semana", "2 dias", "4x - Musculação" e "5" de uma vez.
        /// </remarks>
        public static double LerDiasPorSemana(string resposta)
        {
            if (string.IsNullOrWhiteSpace(resposta))
                return double.NaN;

            string normalizada = resposta.Trim().ToUpperInvariant();

            if (normalizada.Contains(RegrasDeAtividadeFisica.TermoDeNegacao))
                return 0;

            foreach (var (expressao, dias) in RegrasDeAtividadeFisica.ExpressoesPorExtenso)
            {
                if (normalizada.Contains(expressao))
                    return dias;
            }

            var primeiroInteiro = Regex.Match(normalizada, @"\d+");

            if (primeiroInteiro.Success
                && int.TryParse(primeiroInteiro.Value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int diasExtraidos))
            {
                return diasExtraidos >= 0 && diasExtraidos <= RegrasDeAtividadeFisica.DiasMaximosPorSemana
                    ? diasExtraidos
                    : double.NaN;
            }

            return double.NaN;
        }
    }
}
