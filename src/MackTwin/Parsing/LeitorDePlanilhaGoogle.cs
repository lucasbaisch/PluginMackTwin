using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MackTwin.Configuracao;
using MackTwin.Dominio;

namespace MackTwin.Parsing
{
    /// <summary>
    /// Baixa uma planilha Google pública como CSV e a converte em leituras de sensor.
    /// </summary>
    /// <remarks>
    /// <b>Acesso.</b> Uma planilha compartilhada como "qualquer pessoa com o link" responde à
    /// URL <c>/export?format=csv&amp;gid=N</c> sem login: o Google devolve 307 para
    /// <c>googleusercontent.com</c>, e o <see cref="HttpClient"/> segue o redirecionamento por
    /// padrão. Nenhuma API, nenhuma credencial. Se a planilha NÃO for pública, a resposta é a
    /// página de login (HTML), e é isso que a mensagem de erro explica.
    ///
    /// <b>Duas portas.</b> <see cref="BaixarELerAsync"/> faz rede e análise;
    /// <see cref="AnalisarCsv"/> só análise, a partir de texto — é a porta que a verificação do
    /// modelo usa, para testar a convenção da planilha sem depender de conexão.
    ///
    /// <b>Convenção do arquivo.</b> Ver <see cref="ConfiguracaoDaPlanilhaGoogle"/>. O delimitador é
    /// decidido junto com o cabeçalho: é a divisão que produz as colunas TIMESTAMP e HOSTNAME que
    /// define qual caractere separa os campos.
    /// </remarks>
    public sealed class LeitorDePlanilhaGoogle
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(ConfiguracaoDaPlanilhaGoogle.TempoLimiteDeDownloadEmSegundos),
        };

        private static readonly Regex PadraoDeId = new(
            @"/spreadsheets/d/(?<id>[A-Za-z0-9_-]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex PadraoDeGid = new(
            @"[?&#]gid=(?<gid>\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly char[] DelimitadoresCandidatos = { ',', ';', '\t' };

        // ── URL ──────────────────────────────────────────────────────────

        /// <summary>
        /// Converte qualquer endereço da planilha (edição, visualização ou exportação) na URL de
        /// exportação CSV da aba indicada por <c>gid</c> (0 se ausente).
        /// </summary>
        /// <exception cref="ArgumentException">Endereço vazio ou sem o identificador da planilha.</exception>
        public static string MontarUrlDeExportacaoCsv(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Informe o endereço da planilha Google.", nameof(url));

            string texto = url.Trim();
            var id = PadraoDeId.Match(texto);

            if (!id.Success)
                throw new ArgumentException(
                    "O endereço não parece ser de uma planilha Google " +
                    "(esperado algo como https://docs.google.com/spreadsheets/d/{id}/edit).", nameof(url));

            var gid = PadraoDeGid.Match(texto);
            string gidTexto = gid.Success ? gid.Groups["gid"].Value : "0";

            return $"https://docs.google.com/spreadsheets/d/{id.Groups["id"].Value}/export?format=csv&gid={gidTexto}";
        }

        // ── Rede ─────────────────────────────────────────────────────────

        /// <summary>Baixa a planilha e devolve as leituras em ordem cronológica.</summary>
        /// <param name="url">Endereço da planilha, em qualquer das formas aceitas.</param>
        /// <param name="avisos">Recebe o resumo do download e da análise. Pode ser null.</param>
        /// <param name="cancelamento">Cancela o download em curso.</param>
        public async Task<List<Leitura>> BaixarELerAsync(
            string url,
            List<string>? avisos = null,
            CancellationToken cancelamento = default)
        {
            string urlDeExportacao = MontarUrlDeExportacaoCsv(url);

            byte[] bytes = await Http.GetByteArrayAsync(urlDeExportacao, cancelamento).ConfigureAwait(false);
            string conteudo = Encoding.UTF8.GetString(bytes);

            avisos?.Add(string.Format(CultureInfo.CurrentCulture,
                "Planilha baixada: {0:N0} bytes.", bytes.Length));

            return AnalisarCsv(conteudo, avisos);
        }

        // ── Análise ──────────────────────────────────────────────────────

        /// <summary>Disposição das colunas, uma vez localizado o cabeçalho.</summary>
        private sealed class Cabecalho
        {
            public int NumeroDaLinha { get; init; }
            public char Delimitador { get; init; }
            public int IndiceDeTimestamp { get; init; }
            public int IndiceDeHostname { get; init; }
            public int IndiceDeEnderecoMac { get; init; }
            public int IndiceDeTemperatura { get; init; }
            public int IndiceDeUmidade { get; init; }
        }

        /// <summary>
        /// Converte o texto CSV em leituras, em ordem cronológica. Linhas sem data/hora ou sem
        /// HOSTNAME são descartadas e contadas nos avisos; leituras repetidas (mesmo instante e
        /// mesmo sensor) ficam com a primeira.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Texto vazio, resposta HTML (planilha não pública) ou cabeçalho não reconhecido.
        /// </exception>
        public static List<Leitura> AnalisarCsv(string? conteudo, List<string>? avisos = null)
        {
            if (string.IsNullOrWhiteSpace(conteudo))
                throw new InvalidOperationException("A planilha veio vazia.");

            string texto = conteudo.TrimStart('﻿');

            if (texto.TrimStart().StartsWith("<", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "A resposta não é CSV, e sim uma página web: a planilha provavelmente não está " +
                    "compartilhada como \"qualquer pessoa com o link\", ou o endereço está errado.");

            var linhas = UtilitariosDeCsv.DividirEmLinhas(texto);
            var cabecalho = LocalizarCabecalho(linhas) ?? throw new InvalidOperationException(
                "Não encontrei as colunas de data/hora e de dispositivo " +
                $"({string.Join("/", ConfiguracaoDaPlanilhaGoogle.ColunaTimestamp)} e " +
                $"{string.Join("/", ConfiguracaoDaPlanilhaGoogle.ColunaHostname)}) nas primeiras " +
                $"{ConfiguracaoDaPlanilhaGoogle.MaximoDeLinhasParaProcurarCabecalho} linhas da planilha.");

            avisos?.Add(string.Format(CultureInfo.CurrentCulture,
                "CSV: delimitador '{0}', cabeçalho na linha {1}; temperatura {2}, umidade {3}.",
                cabecalho.Delimitador == '\t' ? "TAB" : cabecalho.Delimitador.ToString(),
                cabecalho.NumeroDaLinha + 1,
                cabecalho.IndiceDeTemperatura >= 0 ? "presente" : "AUSENTE",
                cabecalho.IndiceDeUmidade >= 0 ? "presente" : "AUSENTE"));

            var leituras = new List<Leitura>();
            var vistas = new HashSet<(DateTime, string)>();
            int descartadas = 0;
            int duplicadas = 0;

            for (int numeroLinha = cabecalho.NumeroDaLinha + 1; numeroLinha < linhas.Count; numeroLinha++)
            {
                string linha = linhas[numeroLinha];

                if (string.IsNullOrWhiteSpace(linha))
                    continue;

                var campos = UtilitariosDeCsv.SepararCamposRespeitandoAspas(linha, cabecalho.Delimitador);

                string hostname = UtilitariosDeCsv.Campo(campos, cabecalho.IndiceDeHostname);

                if (hostname.Length == 0
                    || !UtilitariosDeCsv.TentarConverterTimestamp(
                           UtilitariosDeCsv.Campo(campos, cabecalho.IndiceDeTimestamp),
                           ConfiguracaoDaPlanilhaGoogle.FormatosDeTimestamp,
                           out DateTime timestamp))
                {
                    descartadas++;
                    continue;
                }

                if (!vistas.Add((timestamp, hostname.ToUpperInvariant())))
                {
                    duplicadas++;
                    continue;
                }

                leituras.Add(new Leitura
                {
                    Timestamp = timestamp,
                    Hostname = hostname,
                    EnderecoMac = UtilitariosDeCsv.Campo(campos, cabecalho.IndiceDeEnderecoMac),
                    Temperatura = LerNumero(campos, cabecalho.IndiceDeTemperatura),
                    Umidade = LerNumero(campos, cabecalho.IndiceDeUmidade),
                });
            }

            leituras.Sort((a, b) =>
            {
                int porTempo = a.Timestamp.CompareTo(b.Timestamp);
                return porTempo != 0 ? porTempo : string.CompareOrdinal(a.Hostname, b.Hostname);
            });

            if (descartadas > 0)
                avisos?.Add(string.Format(CultureInfo.CurrentCulture,
                    "{0} linha(s) descartada(s) por data/hora inválida ou dispositivo em branco.", descartadas));

            if (duplicadas > 0)
                avisos?.Add(string.Format(CultureInfo.CurrentCulture,
                    "{0} leitura(s) repetida(s) (mesmo instante e sensor): prevaleceu a primeira.", duplicadas));

            if (leituras.Count > 0)
                avisos?.Add(string.Format(CultureInfo.CurrentCulture,
                    "{0} leitura(s) de {1:dd/MM/yyyy HH:mm} a {2:dd/MM/yyyy HH:mm}.",
                    leituras.Count, leituras[0].Timestamp, leituras[leituras.Count - 1].Timestamp));
            else
                avisos?.Add("A planilha não tem nenhuma leitura válida.");

            return leituras;
        }

        private static double LerNumero(string[] campos, int indice)
        {
            return UtilitariosDeCsv.TentarConverterNumero(UtilitariosDeCsv.Campo(campos, indice), out double valor)
                ? valor
                : double.NaN;
        }

        /// <summary>
        /// Procura, nas primeiras linhas, uma que — dividida por algum delimitador candidato —
        /// contenha as colunas de data/hora e de dispositivo.
        /// </summary>
        private static Cabecalho? LocalizarCabecalho(List<string> linhas)
        {
            int ultimaLinha = Math.Min(linhas.Count, ConfiguracaoDaPlanilhaGoogle.MaximoDeLinhasParaProcurarCabecalho);

            for (int numeroLinha = 0; numeroLinha < ultimaLinha; numeroLinha++)
            {
                if (string.IsNullOrWhiteSpace(linhas[numeroLinha]))
                    continue;

                foreach (char delimitador in DelimitadoresCandidatos)
                {
                    var campos = UtilitariosDeCsv.SepararCamposRespeitandoAspas(linhas[numeroLinha], delimitador);

                    if (campos.Length < 2)
                        continue;

                    int indiceDeTimestamp = UtilitariosDeCsv.IndiceDaColuna(campos, ConfiguracaoDaPlanilhaGoogle.ColunaTimestamp);
                    int indiceDeHostname = UtilitariosDeCsv.IndiceDaColuna(campos, ConfiguracaoDaPlanilhaGoogle.ColunaHostname);

                    if (indiceDeTimestamp < 0 || indiceDeHostname < 0)
                        continue;

                    return new Cabecalho
                    {
                        NumeroDaLinha = numeroLinha,
                        Delimitador = delimitador,
                        IndiceDeTimestamp = indiceDeTimestamp,
                        IndiceDeHostname = indiceDeHostname,
                        IndiceDeEnderecoMac = UtilitariosDeCsv.IndiceDaColuna(campos, ConfiguracaoDaPlanilhaGoogle.ColunaEnderecoMac),
                        IndiceDeTemperatura = UtilitariosDeCsv.IndiceDaColuna(campos, ConfiguracaoDaPlanilhaGoogle.ColunaTemperatura),
                        IndiceDeUmidade = UtilitariosDeCsv.IndiceDaColuna(campos, ConfiguracaoDaPlanilhaGoogle.ColunaUmidade),
                    };
                }
            }

            return null;
        }
    }
}
