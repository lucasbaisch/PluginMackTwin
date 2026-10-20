using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MackTwin.Configuracao;
using MackTwin.Dominio;

namespace MackTwin.Parsing
{
    /// <summary>
    /// Baixa uma planilha Google pública como CSV e a converte em leituras de sensor — e, nos
    /// ciclos seguintes, só o que mudou.
    /// </summary>
    /// <remarks>
    /// <b>Acesso.</b> Uma planilha compartilhada como "qualquer pessoa com o link" responde à
    /// URL <c>/export?format=csv&amp;gid=N</c> sem login: o Google devolve 307 para
    /// <c>googleusercontent.com</c>, e o <see cref="HttpClient"/> segue o redirecionamento por
    /// padrão. Nenhuma API, nenhuma credencial. Se a planilha NÃO for pública, a resposta é a
    /// página de login (HTML), e é isso que a mensagem de erro explica.
    ///
    /// <b>Três economias, em cascata.</b> (1) Compressão: o Google entrega o CSV em gzip quando
    /// o cliente aceita — ~12 vezes menor (674 KB → 57 KB medidos na planilha do DHT22).
    /// (2) Incremento: depois do primeiro download completo, o leitor pede ao endpoint
    /// <c>gviz/tq</c> só as linhas com carimbo posterior ao último que tem em cache
    /// (<c>select * where A &gt; datetime '…'</c>; o Google tipa a coluna TIMESTAMP como data,
    /// verificado contra a planilha real). (3) Hash: numa recarga completa, se os bytes são
    /// idênticos aos da última, as leituras em cache são reaproveitadas sem reanalisar.
    /// A cada <see cref="ConfiguracaoDaPlanilhaGoogle.CiclosEntreRecargasCompletas"/> ciclos o
    /// leitor volta ao download completo, porque o incremento não enxerga edições nem exclusões
    /// feitas à mão; qualquer falha no incremento também cai no completo.
    ///
    /// <b>Duas portas.</b> <see cref="BaixarELerAsync"/> faz rede, cache e análise;
    /// <see cref="AnalisarCsv(string, List{string})"/> só análise, a partir de texto — é a porta
    /// que a verificação do modelo usa, sem depender de conexão.
    ///
    /// <b>Convenção do arquivo.</b> Ver <see cref="ConfiguracaoDaPlanilhaGoogle"/>. O delimitador é
    /// decidido junto com o cabeçalho: é a divisão que produz as colunas TIMESTAMP e HOSTNAME que
    /// define qual caractere separa os campos. A análise é feita linha a linha sobre um
    /// <see cref="TextReader"/>, sem materializar o arquivo inteiro em texto.
    /// </remarks>
    public sealed class LeitorDePlanilhaGoogle
    {
        private static readonly HttpClient Http = new(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(ConfiguracaoDaPlanilhaGoogle.TempoLimiteDeDownloadEmSegundos),
        };

        private static readonly Regex PadraoDeId = new(
            @"/spreadsheets/d/(?<id>[A-Za-z0-9_-]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex PadraoDeGid = new(
            @"[?&#]gid=(?<gid>\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly char[] DelimitadoresCandidatos = { ',', ';', '\t' };

        /// <summary>O que o leitor guarda de cada planilha entre ciclos.</summary>
        private sealed class EntradaDeCache
        {
            public byte[] Hash = Array.Empty<byte>();
            public List<Leitura> Leituras = new();
            public HashSet<(DateTime, string)> Chaves = new();
            public DateTime? UltimoCarimbo;
            public int ColunaDeTimestamp;
            public int CiclosDesdeRecargaCompleta;
        }

        private readonly Dictionary<string, EntradaDeCache> _cache = new(StringComparer.Ordinal);

        // ── URL ──────────────────────────────────────────────────────────

        /// <summary>
        /// Converte qualquer endereço da planilha (edição, visualização ou exportação) na URL de
        /// exportação CSV da aba indicada por <c>gid</c> (0 se ausente).
        /// </summary>
        /// <exception cref="ArgumentException">Endereço vazio ou sem o identificador da planilha.</exception>
        public static string MontarUrlDeExportacaoCsv(string? url)
        {
            var (id, gid) = ExtrairIdEGid(url);
            return $"https://docs.google.com/spreadsheets/d/{id}/export?format=csv&gid={gid}";
        }

        /// <summary>
        /// URL do endpoint <c>gviz/tq</c> que devolve, em CSV, só as linhas cujo carimbo é
        /// posterior a <paramref name="desde"/>.
        /// </summary>
        /// <param name="url">Endereço da planilha, em qualquer das formas aceitas.</param>
        /// <param name="indiceDaColunaDeTimestamp">Posição (0 = A) da coluna de data/hora, lida do cabeçalho.</param>
        /// <param name="desde">Último carimbo já em cache.</param>
        public static string MontarUrlDeIncremento(string? url, int indiceDaColunaDeTimestamp, DateTime desde)
        {
            var (id, gid) = ExtrairIdEGid(url);
            char coluna = (char)('A' + Math.Clamp(indiceDaColunaDeTimestamp, 0, 25));
            string consulta = string.Format(CultureInfo.InvariantCulture,
                "select * where {0} > datetime '{1:yyyy-MM-dd HH:mm:ss}'", coluna, desde);

            return $"https://docs.google.com/spreadsheets/d/{id}/gviz/tq?tqx=out:csv&gid={gid}&tq={Uri.EscapeDataString(consulta)}";
        }

        private static (string Id, string Gid) ExtrairIdEGid(string? url)
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
            return (id.Groups["id"].Value, gid.Success ? gid.Groups["gid"].Value : "0");
        }

        // ── Rede e cache ─────────────────────────────────────────────────

        /// <summary>Baixa a planilha (ou só as linhas novas) e devolve as leituras em ordem cronológica.</summary>
        /// <param name="url">Endereço da planilha, em qualquer das formas aceitas.</param>
        /// <param name="avisos">Recebe o resumo do download e da análise. Pode ser null.</param>
        /// <param name="cancelamento">Cancela o download em curso.</param>
        public async Task<IReadOnlyList<Leitura>> BaixarELerAsync(
            string url,
            List<string>? avisos = null,
            CancellationToken cancelamento = default)
        {
            string urlDeExportacao = MontarUrlDeExportacaoCsv(url);
            _cache.TryGetValue(urlDeExportacao, out var cache);

            if (cache != null
                && cache.UltimoCarimbo.HasValue
                && cache.CiclosDesdeRecargaCompleta < ConfiguracaoDaPlanilhaGoogle.CiclosEntreRecargasCompletas)
            {
                try
                {
                    return await BaixarIncrementoAsync(urlDeExportacao, cache, avisos, cancelamento).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception excecao)
                {
                    avisos?.Add($"Download incremental falhou ({excecao.Message}); baixando a planilha inteira.");
                }
            }

            byte[] bytes = await Http.GetByteArrayAsync(urlDeExportacao, cancelamento).ConfigureAwait(false);
            byte[] hash = SHA256.HashData(bytes);

            if (cache != null && cache.Hash.AsSpan().SequenceEqual(hash))
            {
                cache.CiclosDesdeRecargaCompleta = 0;
                avisos?.Add(string.Format(CultureInfo.CurrentCulture,
                    "Planilha inalterada ({0:N0} bytes): {1:N0} leitura(s) reaproveitada(s) do cache.",
                    bytes.Length, cache.Leituras.Count));
                return cache.Leituras;
            }

            avisos?.Add(string.Format(CultureInfo.CurrentCulture, "Planilha baixada: {0:N0} bytes.", bytes.Length));

            using var leitor = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var leituras = AnalisarCsv(leitor, avisos, out int colunaDeTimestamp);

            var chaves = new HashSet<(DateTime, string)>(leituras.Count);

            foreach (var leitura in leituras)
                chaves.Add((leitura.Timestamp, leitura.Hostname.ToUpperInvariant()));

            _cache[urlDeExportacao] = new EntradaDeCache
            {
                Hash = hash,
                Leituras = leituras,
                Chaves = chaves,
                UltimoCarimbo = leituras.Count > 0 ? leituras[leituras.Count - 1].Timestamp : null,
                ColunaDeTimestamp = colunaDeTimestamp,
            };

            return leituras;
        }

        private async Task<IReadOnlyList<Leitura>> BaixarIncrementoAsync(
            string urlDeExportacao, EntradaDeCache cache, List<string>? avisos, CancellationToken cancelamento)
        {
            string urlDeIncremento = MontarUrlDeIncremento(urlDeExportacao, cache.ColunaDeTimestamp, cache.UltimoCarimbo!.Value);
            byte[] bytes = await Http.GetByteArrayAsync(urlDeIncremento, cancelamento).ConfigureAwait(false);

            using var leitor = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var novas = AnalisarCsv(leitor, null, out _);

            int acrescentadas = 0;
            bool foraDeOrdem = false;

            foreach (var leitura in novas)
            {
                if (!cache.Chaves.Add((leitura.Timestamp, leitura.Hostname.ToUpperInvariant())))
                    continue;

                if (cache.Leituras.Count > 0 && leitura.Timestamp < cache.Leituras[cache.Leituras.Count - 1].Timestamp)
                    foraDeOrdem = true;

                cache.Leituras.Add(leitura);
                acrescentadas++;
            }

            if (foraDeOrdem)
                cache.Leituras.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            if (acrescentadas > 0)
                cache.UltimoCarimbo = cache.Leituras[cache.Leituras.Count - 1].Timestamp;

            cache.CiclosDesdeRecargaCompleta++;

            avisos?.Add(string.Format(CultureInfo.CurrentCulture,
                "Incremento: {0:N0} bytes, {1} leitura(s) nova(s); {2:N0} em cache.",
                bytes.Length, acrescentadas, cache.Leituras.Count));

            return cache.Leituras;
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

            using var leitor = new StringReader(conteudo.TrimStart('﻿'));
            return AnalisarCsv(leitor, avisos, out _);
        }

        private static List<Leitura> AnalisarCsv(TextReader leitor, List<string>? avisos, out int indiceDaColunaDeTimestamp)
        {
            var cabecalho = LocalizarCabecalho(leitor);
            indiceDaColunaDeTimestamp = cabecalho.IndiceDeTimestamp;

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
            string? linha;

            while ((linha = leitor.ReadLine()) != null)
            {
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
        /// Consome linhas até achar uma que — dividida por algum delimitador candidato — contenha
        /// as colunas de data/hora e de dispositivo. O leitor fica posicionado na linha seguinte.
        /// </summary>
        private static Cabecalho LocalizarCabecalho(TextReader leitor)
        {
            int inspecionadas = 0;
            int numeroDaLinha = -1;
            bool houveConteudo = false;
            string? linha;

            while (inspecionadas < ConfiguracaoDaPlanilhaGoogle.MaximoDeLinhasParaProcurarCabecalho
                   && (linha = leitor.ReadLine()) != null)
            {
                numeroDaLinha++;

                if (string.IsNullOrWhiteSpace(linha))
                    continue;

                string aparada = linha.TrimStart('﻿').TrimStart();

                if (!houveConteudo && aparada.StartsWith("<", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "A resposta não é CSV, e sim uma página web: a planilha provavelmente não está " +
                        "compartilhada como \"qualquer pessoa com o link\", ou o endereço está errado.");

                houveConteudo = true;
                inspecionadas++;

                foreach (char delimitador in DelimitadoresCandidatos)
                {
                    var campos = UtilitariosDeCsv.SepararCamposRespeitandoAspas(aparada, delimitador);

                    if (campos.Length < 2)
                        continue;

                    int indiceDeTimestamp = UtilitariosDeCsv.IndiceDaColuna(campos, ConfiguracaoDaPlanilhaGoogle.ColunaTimestamp);
                    int indiceDeHostname = UtilitariosDeCsv.IndiceDaColuna(campos, ConfiguracaoDaPlanilhaGoogle.ColunaHostname);

                    if (indiceDeTimestamp < 0 || indiceDeHostname < 0)
                        continue;

                    return new Cabecalho
                    {
                        NumeroDaLinha = numeroDaLinha,
                        Delimitador = delimitador,
                        IndiceDeTimestamp = indiceDeTimestamp,
                        IndiceDeHostname = indiceDeHostname,
                        IndiceDeEnderecoMac = UtilitariosDeCsv.IndiceDaColuna(campos, ConfiguracaoDaPlanilhaGoogle.ColunaEnderecoMac),
                        IndiceDeTemperatura = UtilitariosDeCsv.IndiceDaColuna(campos, ConfiguracaoDaPlanilhaGoogle.ColunaTemperatura),
                        IndiceDeUmidade = UtilitariosDeCsv.IndiceDaColuna(campos, ConfiguracaoDaPlanilhaGoogle.ColunaUmidade),
                    };
                }
            }

            if (!houveConteudo)
                throw new InvalidOperationException("A planilha veio vazia.");

            throw new InvalidOperationException(
                "Não encontrei as colunas de data/hora e de dispositivo " +
                $"({string.Join("/", ConfiguracaoDaPlanilhaGoogle.ColunaTimestamp)} e " +
                $"{string.Join("/", ConfiguracaoDaPlanilhaGoogle.ColunaHostname)}) nas primeiras " +
                $"{ConfiguracaoDaPlanilhaGoogle.MaximoDeLinhasParaProcurarCabecalho} linhas da planilha.");
        }
    }
}
