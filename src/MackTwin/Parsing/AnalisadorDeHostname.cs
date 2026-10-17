using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using MackTwin.Configuracao;
using MackTwin.Dominio;

namespace MackTwin.Parsing
{
    /// <summary>
    /// Extrai do HOSTNAME a instituição, a unidade, a sala e o número do dispositivo.
    /// </summary>
    /// <remarks>
    /// <c>MMCCTLAB001-001</c> → MM / CCT / LAB001 / 001. É o que casa o sensor com o Room sem
    /// cadastro manual: todo dispositivo instalado com um nome nesse padrão aparece sozinho na
    /// sala certa. Nomes fora do padrão não são erro — o sensor existe e tem leituras —, só não
    /// têm sala; a interface os lista à parte para que alguém os renomeie.
    ///
    /// A comparação ignora caixa e espaços nas pontas, porque é assim que nomes de rede chegam
    /// de firmwares diferentes; o padrão em si está em
    /// <see cref="ConfiguracaoDaPlanilhaGoogle.PadraoDeHostname"/>.
    /// </remarks>
    public static class AnalisadorDeHostname
    {
        private static readonly Regex Padrao = new(
            ConfiguracaoDaPlanilhaGoogle.PadraoDeHostname,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Tenta separar as partes do HOSTNAME. Devolve false, com identificação nula, quando o
        /// nome não segue o padrão.
        /// </summary>
        public static bool TentarIdentificar(string? hostname, [NotNullWhen(true)] out IdentificacaoDeSensor? identificacao)
        {
            identificacao = null;

            if (string.IsNullOrWhiteSpace(hostname))
                return false;

            string texto = hostname.Trim();
            var casamento = Padrao.Match(texto);

            if (!casamento.Success)
                return false;

            identificacao = new IdentificacaoDeSensor
            {
                Hostname = texto,
                Instituicao = casamento.Groups["instituicao"].Value.ToUpperInvariant(),
                Unidade = casamento.Groups["unidade"].Value.ToUpperInvariant(),
                CodigoDaSala = CodigoDeSala.Normalizar(casamento.Groups["sala"].Value),
                NumeroDoDispositivo = casamento.Groups["dispositivo"].Value,
            };

            return true;
        }

        /// <summary>Só o código normalizado da sala, ou vazio se o nome não segue o padrão.</summary>
        public static string ExtrairCodigoDaSala(string? hostname)
        {
            return TentarIdentificar(hostname, out var identificacao)
                ? identificacao.CodigoDaSala
                : string.Empty;
        }
    }
}
