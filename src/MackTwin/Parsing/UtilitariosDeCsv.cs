using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MackTwin.Parsing
{
    /// <summary>
    /// Operações elementares sobre texto CSV: dividir linhas, separar campos respeitando aspas
    /// e converter números com vírgula decimal e carimbos de tempo.
    /// </summary>
    /// <remarks>
    /// <b>Números saem em cultura invariante.</b> A planilha grava a medida com VÍRGULA decimal
    /// (<c>"24,20"</c>), e quem conhece essa convenção é o analisador — por isso a conversão
    /// acontece aqui, uma só vez, e as camadas seguintes recebem <c>double</c>, nunca texto a
    /// reinterpretar conforme a localidade da máquina.
    /// </remarks>
    public static class UtilitariosDeCsv
    {
        private static readonly string[] QuebrasDeLinha = { "\r\n", "\n", "\r" };

        /// <summary>Divide o texto em linhas, aceitando qualquer convenção de quebra.</summary>
        public static List<string> DividirEmLinhas(string texto)
        {
            return new List<string>(texto.Split(QuebrasDeLinha, StringSplitOptions.None));
        }

        /// <summary>
        /// Divide a linha pelo delimitador informado, respeitando campos entre aspas duplas
        /// (dentro das aspas o delimitador é conteúdo). Aspas duplicadas (<c>""</c>) viram uma.
        /// </summary>
        public static string[] SepararCamposRespeitandoAspas(string linha, char delimitador)
        {
            var campos = new List<string>();
            var campoAtual = new StringBuilder();
            bool dentroDeAspas = false;

            for (int indice = 0; indice < linha.Length; indice++)
            {
                char caractere = linha[indice];

                if (caractere == '"')
                {
                    bool proximaEhAspa = indice + 1 < linha.Length && linha[indice + 1] == '"';

                    if (dentroDeAspas && proximaEhAspa)
                    {
                        campoAtual.Append('"');
                        indice++;
                    }
                    else
                    {
                        dentroDeAspas = !dentroDeAspas;
                    }
                }
                else if (caractere == delimitador && !dentroDeAspas)
                {
                    campos.Add(campoAtual.ToString());
                    campoAtual.Clear();
                }
                else
                {
                    campoAtual.Append(caractere);
                }
            }

            campos.Add(campoAtual.ToString());
            return campos.ToArray();
        }

        /// <summary>
        /// Converte texto numérico em <c>double</c>, aceitando vírgula ou ponto como decimal.
        /// </summary>
        /// <remarks>
        /// A decisão é pelo ÚLTIMO separador presente: em "24,07" a vírgula é decimal; em
        /// "1.234,56" a vírgula é decimal e o ponto é milhar; em "1,234.56" o inverso. Textos que
        /// não convertem — vazio, "NaN", "--", endereços — devolvem false, e o chamador decide
        /// o que fazer (normalmente NaN).
        /// </remarks>
        public static bool TentarConverterNumero(string? bruto, out double valor)
        {
            valor = double.NaN;

            if (string.IsNullOrWhiteSpace(bruto))
                return false;

            string texto = bruto.Trim().Trim('"').Trim();

            if (texto.Length == 0)
                return false;

            int ultimoPonto = texto.LastIndexOf('.');
            int ultimaVirgula = texto.LastIndexOf(',');
            string candidato = texto;

            if (ultimoPonto >= 0 || ultimaVirgula >= 0)
            {
                char separadorDecimal = ultimaVirgula > ultimoPonto ? ',' : '.';
                char separadorDeMilhar = separadorDecimal == ',' ? '.' : ',';

                candidato = texto
                    .Replace(separadorDeMilhar.ToString(), string.Empty)
                    .Replace(separadorDecimal, '.');
            }

            return double.TryParse(candidato, NumberStyles.Float, CultureInfo.InvariantCulture, out valor)
                   && !double.IsInfinity(valor);
        }

        /// <summary>
        /// Converte o texto de data e hora tentando, em ordem, os formatos exatos informados e,
        /// por fim, qualquer forma que a cultura invariante reconheça.
        /// </summary>
        public static bool TentarConverterTimestamp(string? bruto, string[] formatos, out DateTime timestamp)
        {
            timestamp = default;

            if (string.IsNullOrWhiteSpace(bruto))
                return false;

            string texto = bruto.Trim().Trim('"').Trim();

            if (DateTime.TryParseExact(texto, formatos, CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out timestamp))
                return true;

            return DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
        }

        /// <summary>
        /// Índice da primeira coluna cujo nome é igual a um dos candidatos (sem distinguir caixa
        /// nem espaços nas pontas), ou −1.
        /// </summary>
        public static int IndiceDaColuna(string[] cabecalho, string[] candidatos)
        {
            for (int indice = 0; indice < cabecalho.Length; indice++)
            {
                string nome = cabecalho[indice].Trim().Trim('"').Trim();

                foreach (string candidato in candidatos)
                {
                    if (string.Equals(nome, candidato, StringComparison.OrdinalIgnoreCase))
                        return indice;
                }
            }

            return -1;
        }

        /// <summary>Campo na posição, aparado e sem aspas, ou vazio se a linha é curta ou o índice é −1.</summary>
        public static string Campo(string[] campos, int indice)
        {
            return indice < 0 || indice >= campos.Length
                ? string.Empty
                : campos[indice].Trim().Trim('"').Trim();
        }
    }
}
