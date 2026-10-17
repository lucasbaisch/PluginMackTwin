using System;
using System.Linq;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Converte em número as respostas categóricas usadas como eixo dos gráficos do grupo.
    /// </summary>
    /// <remarks>
    /// Um eixo cartesiano só desenha número. Estas rotinas traduzem a resposta de texto em uma
    /// posição no eixo, e o rótulo correspondente é devolvido por <c>ObterRotulo…</c> para que a
    /// marca do eixo mostre a categoria, e não o índice.
    ///
    /// <b>Nenhuma delas inventa ordem onde não há.</b> Trabalho doméstico é ordinal e a ordem é a
    /// das faixas; seção de trabalho é nominal, e a posição é apenas um lugar estável na régua —
    /// não significa "mais" nem "menos".
    /// </remarks>
    public static class LeitorDeVariaveisCategoricas
    {
        // ── Horas de trabalho doméstico (Questão 23) ─────────────────────

        /// <summary>Rótulos das três faixas, na ordem crescente de carga.</summary>
        public static readonly string[] FaixasDeTrabalhoDomestico =
        {
            "< 10 h/sem",
            "10 a 20 h/sem",
            "> 20 h/sem",
        };

        /// <summary>
        /// Converte a resposta da Questão 23 na posição da faixa (0, 1 ou 2); NaN se não reconhecida.
        /// </summary>
        /// <remarks>
        /// A planilha traz a mesma faixa grafada de várias formas — "&lt; 10 horas por semana" e
        /// "&lt; 10h por semana", algumas precedidas de "[ ]" residual do formulário. Por isso a
        /// leitura é por conteúdo, e não por igualdade: procura-se o operador de comparação e o
        /// número, ignorando o resto.
        /// </remarks>
        public static double LerTrabalhoDomestico(string resposta)
        {
            if (string.IsNullOrWhiteSpace(resposta))
                return double.NaN;

            string texto = resposta.Replace("[", " ").Replace("]", " ").Trim();

            if (texto.Contains(">"))
                return 2;

            if (texto.Contains("<"))
                return 0;

            // Sem operador, a única faixa restante é a intermediária, escrita como "10 a 20".
            if (texto.Contains(" a "))
                return 1;

            return double.NaN;
        }

        /// <summary>Rótulo da faixa correspondente à posição no eixo.</summary>
        public static string ObterRotuloDeTrabalhoDomestico(double posicao)
        {
            int indice = (int)Math.Round(posicao);

            return indice >= 0 && indice < FaixasDeTrabalhoDomestico.Length
                ? FaixasDeTrabalhoDomestico[indice]
                : string.Empty;
        }

        // ── Outra fonte de renda (Questão 19) ────────────────────────────

        /// <summary>
        /// Converte a resposta da Questão 19 em 0 (não) ou 1 (sim); NaN se não reconhecida.
        /// </summary>
        /// <remarks>
        /// A resposta afirmativa vem descritiva ("Sim, em outro serviço…"), então basta o prefixo.
        /// </remarks>
        public static double LerOutraFonteDeRenda(string resposta)
        {
            if (string.IsNullOrWhiteSpace(resposta))
                return double.NaN;

            string texto = resposta.Trim();

            if (texto.StartsWith("Sim", StringComparison.OrdinalIgnoreCase))
                return 1;

            if (texto.StartsWith("Não", StringComparison.OrdinalIgnoreCase)
                || texto.StartsWith("Nao", StringComparison.OrdinalIgnoreCase))
                return 0;

            return double.NaN;
        }

        /// <summary>Rótulo do eixo binário de outra fonte de renda.</summary>
        public static string ObterRotuloDeOutraFonteDeRenda(double posicao)
        {
            int indice = (int)Math.Round(posicao);

            if (indice == 0) return "Não";
            if (indice == 1) return "Sim";

            return string.Empty;
        }

        // ── Seção da Divisão de Enfermagem (Questão 17) ──────────────────

        /// <summary>
        /// Seções encontradas no arquivo de formulários, em ordem alfabética.
        /// </summary>
        /// <remarks>
        /// A ordem é apenas uma posição estável no eixo: a variável é NOMINAL, e a distância entre
        /// duas seções não significa nada. Fixá-la aqui, em vez de derivá-la dos dados carregados,
        /// evita que o mesmo participante mude de lugar no gráfico conforme quem mais respondeu.
        /// </remarks>
        // LISTA VERIFICADA contra o arquivo definitivo (dados_formularios.xlsx, a_recrutamento,
        // Questão 17, 31/08/2026): as sete seções abaixo são exatamente as que ocorrem — CPCIRAS
        // ×1, DEC ×1, Divisão de Enfermagem ×1, SEPEX ×12, SEPI ×3, UTI-ADULTO ×4,
        // UTI-NEONATAL ×3 —, com 25 respondentes; o 26º deixou a questão em branco e fica fora
        // do gráfico, por desenho (variável ausente descarta o ponto).
        public static readonly string[] SecoesDeTrabalho =
        {
            "CPCIRAS",
            "DEC",
            "Divisão de Enfermagem",
            "SEPEX",
            "SEPI",
            "UTI-ADULTO",
            "UTI-NEONATAL",
        };

        /// <summary>
        /// Converte a seção informada na sua posição em <see cref="SecoesDeTrabalho"/>;
        /// NaN quando vazia ou não listada.
        /// </summary>
        public static double LerSecaoDeTrabalho(string resposta)
        {
            if (string.IsNullOrWhiteSpace(resposta))
                return double.NaN;

            string texto = resposta.Trim();

            int posicao = Array.FindIndex(SecoesDeTrabalho,
                secao => string.Equals(secao, texto, StringComparison.OrdinalIgnoreCase));

            return posicao >= 0 ? posicao : double.NaN;
        }

        /// <summary>Rótulo da seção correspondente à posição no eixo.</summary>
        public static string ObterRotuloDeSecaoDeTrabalho(double posicao)
        {
            int indice = (int)Math.Round(posicao);

            return indice >= 0 && indice < SecoesDeTrabalho.Length
                ? SecoesDeTrabalho[indice]
                : string.Empty;
        }
    }
}
