using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PluginCAISM.Configuracao;

namespace PluginCAISM.Parsing
{
    /// <summary>
    /// Lê o arquivo CSV de dados ambientais (Entrada II) e produz um índice por timestamp,
    /// no mesmo formato que o <see cref="AnalisadorExcelSensor"/> entrega.
    /// </summary>
    /// <remarks>
    /// <b>Por que ler o CSV, e não só a planilha.</b> O CSV é o formato NATIVO do módulo de
    /// monitoramento: é ele que o dispositivo grava. Converter para .xlsx antes de carregar
    /// introduz um intermediário que reescreve os números conforme a localidade de quem
    /// converteu — as células chegam como TEXTO com vírgula decimal, e o que era medida vira
    /// cadeia a reinterpretar. Lendo o original, a única convenção a respeitar é a do próprio
    /// arquivo, que é conhecida.
    ///
    /// <b>Formato aceito.</b> Delimitador ponto e vírgula ou vírgula, detectado pela linha de
    /// cabeçalho; campos opcionalmente entre aspas duplas; codificação UTF-8; uma linha de
    /// cabeçalho seguida das leituras. A coluna de data e hora é localizada pelos mesmos nomes
    /// candidatos de <see cref="MapeamentoDeColunasDeSensor.Timestamp"/> usados na planilha, de
    /// modo que os dois analisadores reconhecem o mesmo arquivo.
    ///
    /// <b>Números saem em cultura invariante.</b> O arquivo da pesquisa usa ponto e vírgula como
    /// delimitador e VÍRGULA como separador decimal ("24,07"). Como quem conhece a convenção do
    /// arquivo é este analisador, é aqui que a conversão acontece: os valores numéricos são
    /// normalizados para ponto decimal antes de seguir adiante. As camadas seguintes recebem
    /// texto em cultura invariante e não precisam adivinhar localidade — mesmo contrato que o
    /// <see cref="AnalisadorExcelSensor"/> cumpre para as células numéricas da planilha.
    /// </remarks>
    public class AnalisadorCsvSensor
    {
        /// <summary>Formato de data e hora gravado pelo módulo de monitoramento.</summary>
        private const string FormatoDeTimestamp = "yyyy-MM-dd HH:mm:ss.fff";

        /// <summary>Número de linhas iniciais inspecionadas na busca pelo cabeçalho.</summary>
        private const int MaximoDeLinhasParaProcurarCabecalho = 20;

        /// <summary>Delimitadores testados, na ordem de preferência.</summary>
        private static readonly char[] DelimitadoresCandidatos = { ';', ',', '\t' };

        /// <summary>
        /// Analisa o CSV de sensores e devolve o índice de leituras por timestamp.
        /// </summary>
        /// <param name="caminhoDoCsv">Caminho completo do arquivo .csv de sensores.</param>
        /// <param name="avisos">
        /// Lista populada com o delimitador detectado, o total de leituras e as linhas
        /// descartadas. Pode ser null.
        /// </param>
        /// <returns>
        /// Dicionário timestamp → dicionário de campos (nome da coluna → valor como texto).
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Lançada quando o arquivo está vazio ou não tem coluna de data/hora reconhecível.
        /// </exception>
        public Dictionary<DateTime, Dictionary<string, string>> Analisar(
            string caminhoDoCsv,
            List<string> avisos = null)
        {
            var linhas = LerLinhas(caminhoDoCsv);

            if (linhas.Count == 0)
                throw new InvalidOperationException("O arquivo CSV de sensores está vazio.");

            var cabecalho = LocalizarCabecalho(linhas)
                ?? throw new InvalidOperationException(ConstruirMensagemDeColunaAusente());

            avisos?.Add(string.Format(CultureInfo.CurrentCulture,
                "CSV de sensores: delimitador '{0}', cabeçalho na linha {1}, {2} coluna(s) " +
                "reconhecida(s).",
                cabecalho.Delimitador == '\t' ? "TAB" : cabecalho.Delimitador.ToString(),
                cabecalho.NumeroDaLinha + 1,
                cabecalho.NomesPorIndice.Count));

            var indice = new Dictionary<DateTime, Dictionary<string, string>>();
            int descartadas = 0;
            int duplicados = 0;

            for (int numeroLinha = cabecalho.NumeroDaLinha + 1; numeroLinha < linhas.Count; numeroLinha++)
            {
                string linha = linhas[numeroLinha];

                if (string.IsNullOrWhiteSpace(linha))
                    continue;

                var campos = SepararCamposRespeitandoAspas(linha, cabecalho.Delimitador);

                if (cabecalho.IndiceDeTimestamp >= campos.Length
                    || !TentarConverterTimestamp(campos[cabecalho.IndiceDeTimestamp], out DateTime timestamp))
                {
                    descartadas++;
                    continue;
                }

                var camposDaLinha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var (indiceColuna, nomeDaColuna) in cabecalho.NomesPorIndice)
                {
                    if (indiceColuna >= campos.Length)
                        continue;

                    camposDaLinha[nomeDaColuna] = NormalizarValor(campos[indiceColuna]);
                }

                // Leituras repetidas no mesmo instante: prevalece a primeira, como na planilha.
                if (indice.ContainsKey(timestamp))
                    duplicados++;
                else
                    indice[timestamp] = camposDaLinha;
            }

            if (descartadas > 0)
            {
                avisos?.Add(string.Format(CultureInfo.CurrentCulture,
                    "{0} linha(s) do CSV de sensores descartada(s) por data e hora inválida ou ausente.",
                    descartadas));
            }

            if (duplicados > 0)
            {
                avisos?.Add(string.Format(CultureInfo.CurrentCulture,
                    "{0} leitura(s) com data e hora repetida: prevaleceu a primeira de cada instante.",
                    duplicados));
            }

            avisos?.Add(string.Format(CultureInfo.CurrentCulture,
                "{0} leitura(s) ambientais indexadas por data e hora.", indice.Count));

            return indice;
        }

        // ── Leitura do arquivo ───────────────────────────────────────────

        /// <summary>
        /// Lê todas as linhas do arquivo, com compartilhamento de leitura para não bloquear o
        /// arquivo caso ele esteja aberto em outro programa.
        /// </summary>
        private static List<string> LerLinhas(string caminhoDoCsv)
        {
            var linhas = new List<string>();

            using var fluxo = new FileStream(
                caminhoDoCsv, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var leitor = new StreamReader(fluxo, Encoding.UTF8);

            string linha;
            while ((linha = leitor.ReadLine()) != null)
                linhas.Add(linha);

            return linhas;
        }

        // ── Cabeçalho e delimitador ──────────────────────────────────────

        /// <summary>Disposição das colunas do arquivo, uma vez localizado o cabeçalho.</summary>
        private sealed class CabecalhoDoCsv
        {
            public int NumeroDaLinha { get; init; }
            public char Delimitador { get; init; }
            public int IndiceDeTimestamp { get; init; }
            public Dictionary<int, string> NomesPorIndice { get; init; }
        }

        /// <summary>
        /// Procura, nas primeiras linhas, uma que contenha um dos nomes candidatos de coluna de
        /// data e hora, testando cada delimitador.
        /// </summary>
        /// <remarks>
        /// O delimitador é decidido junto com o cabeçalho, e não antes: é a divisão que produz
        /// uma coluna de data e hora reconhecível que define qual caractere separa os campos.
        /// Contar ocorrências seria frágil, porque o cabeçalho real contém tanto ponto e vírgula
        /// quanto dois-pontos e espaços dentro do próprio nome da coluna.
        /// </remarks>
        private static CabecalhoDoCsv LocalizarCabecalho(List<string> linhas)
        {
            int ultimaLinha = Math.Min(linhas.Count, MaximoDeLinhasParaProcurarCabecalho);

            for (int numeroLinha = 0; numeroLinha < ultimaLinha; numeroLinha++)
            {
                if (string.IsNullOrWhiteSpace(linhas[numeroLinha]))
                    continue;

                foreach (char delimitador in DelimitadoresCandidatos)
                {
                    var campos = SepararCamposRespeitandoAspas(linhas[numeroLinha], delimitador);

                    if (campos.Length < 2)
                        continue;

                    int indiceDeTimestamp = -1;
                    var nomes = new Dictionary<int, string>();

                    for (int indice = 0; indice < campos.Length; indice++)
                    {
                        string nome = campos[indice].Trim();

                        if (string.IsNullOrWhiteSpace(nome))
                            continue;

                        nomes[indice] = nome;

                        if (indiceDeTimestamp < 0 && CorrespondeACandidatoDeTimestamp(nome))
                            indiceDeTimestamp = indice;
                    }

                    if (indiceDeTimestamp >= 0)
                    {
                        return new CabecalhoDoCsv
                        {
                            NumeroDaLinha = numeroLinha,
                            Delimitador = delimitador,
                            IndiceDeTimestamp = indiceDeTimestamp,
                            NomesPorIndice = nomes,
                        };
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Verifica se um nome de coluna corresponde a algum candidato de data e hora, por
        /// igualdade exata e depois por conteúdo — a mesma regra do analisador de planilha.
        /// </summary>
        private static bool CorrespondeACandidatoDeTimestamp(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome))
                return false;

            foreach (string candidato in MapeamentoDeColunasDeSensor.Timestamp)
            {
                if (string.IsNullOrWhiteSpace(candidato))
                    continue;

                if (string.Equals(nome, candidato, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (nome.IndexOf(candidato, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static string ConstruirMensagemDeColunaAusente()
        {
            return "O arquivo CSV de sensores não contém uma coluna de data/hora reconhecível " +
                   $"nas primeiras {MaximoDeLinhasParaProcurarCabecalho} linhas. " +
                   $"Nomes aceitos: {string.Join(", ", MapeamentoDeColunasDeSensor.Timestamp)} — " +
                   "ajustar MapeamentoDeColunasDeSensor.Timestamp se a coluna tiver outro nome.";
        }

        // ── Conversões ───────────────────────────────────────────────────

        /// <summary>
        /// Converte o texto da coluna de data e hora, aceitando o formato do módulo de
        /// monitoramento e, como alternativa, qualquer forma que a cultura invariante reconheça.
        /// </summary>
        private static bool TentarConverterTimestamp(string texto, out DateTime timestamp)
        {
            timestamp = default;

            if (string.IsNullOrWhiteSpace(texto))
                return false;

            string limpo = texto.Trim().Trim('"');

            if (DateTime.TryParseExact(limpo, FormatoDeTimestamp, CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out DateTime exata))
            {
                timestamp = exata;
                return true;
            }

            return DateTime.TryParse(limpo, CultureInfo.InvariantCulture,
                                     DateTimeStyles.None, out timestamp);
        }

        /// <summary>
        /// Normaliza o valor de um campo: números com vírgula decimal saem com ponto decimal, em
        /// cultura invariante; o restante segue como está, apenas aparado.
        /// </summary>
        /// <remarks>
        /// A decisão é feita pelo ÚLTIMO separador presente no valor: em "24,07" a vírgula é
        /// decimal; em "1.234,56" a vírgula é decimal e o ponto é milhar; em "1,234.56" o
        /// inverso. Valores que não sejam numéricos — "Power On", endereços MAC, endereços de
        /// rede — não têm o que normalizar e passam intactos, o que a verificação final garante:
        /// só se devolve a forma normalizada quando ela de fato converte em número.
        /// </remarks>
        private static string NormalizarValor(string bruto)
        {
            if (string.IsNullOrWhiteSpace(bruto))
                return string.Empty;

            string texto = bruto.Trim().Trim('"').Trim();

            if (texto.Length == 0)
                return string.Empty;

            int ultimoPonto = texto.LastIndexOf('.');
            int ultimaVirgula = texto.LastIndexOf(',');

            if (ultimoPonto < 0 && ultimaVirgula < 0)
                return texto;

            char separadorDecimal = ultimaVirgula > ultimoPonto ? ',' : '.';
            char separadorDeMilhar = separadorDecimal == ',' ? '.' : ',';

            string candidato = texto
                .Replace(separadorDeMilhar.ToString(), string.Empty)
                .Replace(separadorDecimal, '.');

            // Só troca de fato se o resultado for número. Sem esta guarda, um endereço de rede
            // como "181.213.132.2" viraria "181213132.2" — um número plausível e falso.
            return double.TryParse(candidato, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                ? candidato
                : texto;
        }

        /// <summary>
        /// Divide a linha pelo delimitador informado, respeitando campos entre aspas duplas.
        /// </summary>
        private static string[] SepararCamposRespeitandoAspas(string linha, char delimitador)
        {
            var campos = new List<string>();
            var campoAtual = new StringBuilder();
            bool dentroDeAspas = false;

            foreach (char caractere in linha)
            {
                if (caractere == '"')
                {
                    dentroDeAspas = !dentroDeAspas;
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
    }
}
