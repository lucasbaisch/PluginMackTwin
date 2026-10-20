using System.Text.RegularExpressions;
using MackTwin.Configuracao;
using MackTwin.Dominio;

namespace MackTwin.Parsing
{
    /// <summary>
    /// Extrai do HOSTNAME a sala e o número do dispositivo.
    /// </summary>
    /// <remarks>
    /// <c>MMCCTLAB001-001</c> → sala LAB001, dispositivo 001. É o que casa o sensor com o Room
    /// sem cadastro manual: todo dispositivo instalado com um nome nesse padrão aparece sozinho
    /// na sala certa. O padrão inteiro é validado (instituição e unidade inclusive), mas só o
    /// que o painel consome é devolvido. Nomes fora do padrão não são erro — o sensor existe e
    /// tem leituras —, só não têm sala; a interface os lista à parte para que alguém os renomeie.
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
        /// Separa as partes do HOSTNAME: o código normalizado da sala e o número do dispositivo
        /// como texto. Devolve false, com saídas vazias, quando o nome não segue o padrão.
        /// </summary>
        public static bool TentarIdentificar(string? hostname, out string codigoDaSala, out string numeroDoDispositivo)
        {
            codigoDaSala = string.Empty;
            numeroDoDispositivo = string.Empty;

            if (string.IsNullOrWhiteSpace(hostname))
                return false;

            var casamento = Padrao.Match(hostname.Trim());

            if (!casamento.Success)
                return false;

            codigoDaSala = CodigoDeSala.Normalizar(casamento.Groups["sala"].Value);
            numeroDoDispositivo = casamento.Groups["dispositivo"].Value;
            return true;
        }

        /// <summary>Só o código normalizado da sala, ou vazio se o nome não segue o padrão.</summary>
        public static string ExtrairCodigoDaSala(string? hostname)
        {
            return TentarIdentificar(hostname, out string codigoDaSala, out _) ? codigoDaSala : string.Empty;
        }
    }
}
