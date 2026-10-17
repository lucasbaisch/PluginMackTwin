using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PluginCAISM.Dominio;

namespace PluginCAISM.Parsing
{
    /// <summary>
    /// Realiza a leitura e desserialização do arquivo CSV de rastreamento ocular (Input 01),
    /// produzindo uma lista de <see cref="LinhaDeSessao"/> prontas para construção de sessões.
    /// </summary>
    /// <remarks>
    /// O CSV usa quoting RFC 4180: campos com vírgulas (como NomeDoObjeto e NomeDispositivo)
    /// estão envolvidos em aspas duplas. O parser respeita esse formato.
    ///
    /// As linhas resultantes são retornadas em ordem cronológica crescente por timestamp,
    /// independentemente da ordem original do arquivo (que pode ser decrescente).
    ///
    /// Linhas com timestamp não parseável são ignoradas e registradas no log de avisos.
    /// O parsing não é abortado por linhas individuais inválidas.
    /// </remarks>
    public class AnalisadorCsvSessao
    {
        private static readonly string[] CabecalhosObrigatorios =
        {
            "timeStamp", "identificacaoManual", "nomeDaSessao", "selectedScene",
            "selectedSeat", "nomeDoObjeto", "left_PupilDiametre", "right_PupilDiametre",
            "leftHitPoint_X", "leftHitPoint_Y", "leftHitPoint_Z",
            "rightHitPoint_X", "rightHitPoint_Y", "rightHitPoint_Z",
            "left_EyeOpened", "right_EyeOpened"
        };

        private const string FormatoDeTimestamp = "yyyy-MM-dd HH:mm:ss.fff";

        /// <summary>
        /// Analisa o arquivo CSV de sessões e retorna as linhas ordenadas por timestamp crescente.
        /// </summary>
        /// <param name="caminhoDoCsv">Caminho completo do arquivo CSV.</param>
        /// <param name="avisosDeLinhasIgnoradas">
        /// Lista populada com mensagens descritivas para cada linha ignorada por timestamp inválido.
        /// </param>
        /// <returns>Lista de <see cref="LinhaDeSessao"/> ordenadas por <see cref="LinhaDeSessao.Timestamp"/> crescente.</returns>
        /// <exception cref="InvalidOperationException">
        /// Lançada se o arquivo não contiver todos os cabeçalhos obrigatórios.
        /// A mensagem lista os cabeçalhos ausentes.
        /// </exception>
        public List<LinhaDeSessao> Analisar(string caminhoDoCsv, List<string> avisosDeLinhasIgnoradas)
        {
            var todasAsLinhas = LerLinhasDoArquivo(caminhoDoCsv);
            var mapeamentoDeColunas = ExtrairMapeamentoDeColunas(todasAsLinhas[0]);

            ValidarCabecalhosObrigatorios(mapeamentoDeColunas);

            var linhasAnalisadas = AnalisarLinhasDeConteudo(todasAsLinhas, mapeamentoDeColunas, avisosDeLinhasIgnoradas);

            return linhasAnalisadas.OrderBy(linha => linha.Timestamp).ToList();
        }

        private List<string[]> LerLinhasDoArquivo(string caminhoDoCsv)
        {
            var linhas = new List<string[]>();

            using var leitor = new StreamReader(caminhoDoCsv, Encoding.UTF8);
            string linhaAtual;
            while ((linhaAtual = leitor.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(linhaAtual))
                    linhas.Add(SepararCamposRespeitandoAspas(linhaAtual));
            }

            return linhas;
        }

        private Dictionary<string, int> ExtrairMapeamentoDeColunas(string[] cabecalho)
        {
            var mapeamento = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int indice = 0; indice < cabecalho.Length; indice++)
                mapeamento[cabecalho[indice].Trim()] = indice;

            return mapeamento;
        }

        private void ValidarCabecalhosObrigatorios(Dictionary<string, int> mapeamentoDeColunas)
        {
            var cabecalhosAusentes = CabecalhosObrigatorios
                .Where(cabecalho => !mapeamentoDeColunas.ContainsKey(cabecalho))
                .ToList();

            if (cabecalhosAusentes.Any())
                throw new InvalidOperationException(
                    $"O CSV de sessões não contém os seguintes cabeçalhos obrigatórios: {string.Join(", ", cabecalhosAusentes)}");
        }

        private List<LinhaDeSessao> AnalisarLinhasDeConteudo(
            List<string[]> todasAsLinhas,
            Dictionary<string, int> mapeamentoDeColunas,
            List<string> avisosDeLinhasIgnoradas)
        {
            var resultado = new List<LinhaDeSessao>();

            for (int numeroLinha = 1; numeroLinha < todasAsLinhas.Count; numeroLinha++)
            {
                var campos = todasAsLinhas[numeroLinha];
                var linhaOuNulo = TentarAnalisarLinha(campos, mapeamentoDeColunas, numeroLinha, avisosDeLinhasIgnoradas);

                if (linhaOuNulo != null)
                    resultado.Add(linhaOuNulo);
            }

            return resultado;
        }

        private LinhaDeSessao TentarAnalisarLinha(
            string[] campos,
            Dictionary<string, int> mapeamentoDeColunas,
            int numeroLinha,
            List<string> avisosDeLinhasIgnoradas)
        {
            var valorTimestamp = LerCampo(campos, mapeamentoDeColunas, "timeStamp");

            if (!DateTime.TryParseExact(valorTimestamp, FormatoDeTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime timestamp))
            {
                avisosDeLinhasIgnoradas.Add($"Linha {numeroLinha}: timestamp inválido '{valorTimestamp}'. Linha ignorada.");
                return null;
            }

            return ConstruirLinhaDeSessao(campos, mapeamentoDeColunas, numeroLinha - 1, timestamp);
        }

        private LinhaDeSessao ConstruirLinhaDeSessao(
            string[] campos,
            Dictionary<string, int> mapeamentoDeColunas,
            int indiceOriginal,
            DateTime timestamp)
        {
            return new LinhaDeSessao
            {
                IndiceLinhaOriginal            = indiceOriginal,
                Timestamp                      = timestamp,
                IdentificacaoManual            = LerCampo(campos, mapeamentoDeColunas, "identificacaoManual"),
                NomeDaSessao                   = LerCampo(campos, mapeamentoDeColunas, "nomeDaSessao"),
                CenarioSelecionado             = LerCampo(campos, mapeamentoDeColunas, "selectedScene"),
                AssentoSelecionado             = LerCampo(campos, mapeamentoDeColunas, "selectedSeat"),
                NomeDoObjeto                   = LerCampo(campos, mapeamentoDeColunas, "nomeDoObjeto"),
                DirecaoOlharEsquerdoX          = LerDouble(campos, mapeamentoDeColunas, "left_EyeGaze_X"),
                DirecaoOlharDireitoX           = LerDouble(campos, mapeamentoDeColunas, "right_EyeGaze_X"),
                DirecaoOlharEsquerdoY          = LerDouble(campos, mapeamentoDeColunas, "left_EyeGaze_Y"),
                DirecaoOlharDireitoY           = LerDouble(campos, mapeamentoDeColunas, "right_EyeGaze_Y"),
                DirecaoOlharEsquerdoZ          = LerDouble(campos, mapeamentoDeColunas, "left_EyeGaze_Z"),
                DirecaoOlharDireitoZ           = LerDouble(campos, mapeamentoDeColunas, "right_EyeGaze_Z"),
                OrientacaoOlhoEsquerdoX        = LerDouble(campos, mapeamentoDeColunas, "left_EyeOrientation_X"),
                OrientacaoOlhoDireitoX         = LerDouble(campos, mapeamentoDeColunas, "right_EyeOrientation_X"),
                OrientacaoOlhoEsquerdoY        = LerDouble(campos, mapeamentoDeColunas, "left_EyeOrientation_Y"),
                OrientacaoOlhoDireitoY         = LerDouble(campos, mapeamentoDeColunas, "right_EyeOrientation_Y"),
                OrientacaoOlhoEsquerdoZ        = LerDouble(campos, mapeamentoDeColunas, "left_EyeOrientation_Z"),
                OrientacaoOlhoDireitoZ         = LerDouble(campos, mapeamentoDeColunas, "right_EyeOrientation_Z"),
                OrientacaoOlhoEsquerdoW        = LerDouble(campos, mapeamentoDeColunas, "left_EyeOrientation_W"),
                OrientacaoOlhoDireitoW         = LerDouble(campos, mapeamentoDeColunas, "right_EyeOrientation_W"),
                DiametroPupilaEsquerda         = LerDouble(campos, mapeamentoDeColunas, "left_PupilDiametre"),
                DiametroPupilaDireita          = LerDouble(campos, mapeamentoDeColunas, "right_PupilDiametre"),
                AberturaDoOlhoEsquerdo         = LerDouble(campos, mapeamentoDeColunas, "left_EyeOpened"),
                AberturaDoOlhoDireito          = LerDouble(campos, mapeamentoDeColunas, "right_EyeOpened"),
                PosicaoJogadorX                = LerDouble(campos, mapeamentoDeColunas, "playerPosition_x"),
                PosicaoJogadorY                = LerDouble(campos, mapeamentoDeColunas, "playerPosition_y"),
                PosicaoJogadorZ                = LerDouble(campos, mapeamentoDeColunas, "playerPosition_z"),
                RotacaoJogadorX                = LerDouble(campos, mapeamentoDeColunas, "playerRotation_x"),
                RotacaoJogadorY                = LerDouble(campos, mapeamentoDeColunas, "playerRotation_y"),
                RotacaoJogadorZ                = LerDouble(campos, mapeamentoDeColunas, "playerRotation_z"),
                RotacaoJogadorW                = LerDouble(campos, mapeamentoDeColunas, "playerRotation_w"),
                PontoDeIncidenciaEsquerdoX     = LerDouble(campos, mapeamentoDeColunas, "leftHitPoint_X"),
                PontoDeIncidenciaEsquerdoY     = LerDouble(campos, mapeamentoDeColunas, "leftHitPoint_Y"),
                PontoDeIncidenciaEsquerdoZ     = LerDouble(campos, mapeamentoDeColunas, "leftHitPoint_Z"),
                PontoDeIncidenciaDireitoX      = LerDouble(campos, mapeamentoDeColunas, "rightHitPoint_X"),
                PontoDeIncidenciaDireitoY      = LerDouble(campos, mapeamentoDeColunas, "rightHitPoint_Y"),
                PontoDeIncidenciaDireitoZ      = LerDouble(campos, mapeamentoDeColunas, "rightHitPoint_Z"),
                NomeDispositivo                = LerCampo(campos, mapeamentoDeColunas, "htcDeviceName")
            };
        }

        private string LerCampo(string[] campos, Dictionary<string, int> mapeamento, string nomeDaColuna)
        {
            if (!mapeamento.TryGetValue(nomeDaColuna, out int indice) || indice >= campos.Length)
                return string.Empty;

            return campos[indice].Trim();
        }

        private double LerDouble(string[] campos, Dictionary<string, int> mapeamento, string nomeDaColuna)
        {
            var valor = LerCampo(campos, mapeamento, nomeDaColuna);

            if (double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out double resultado))
                return resultado;

            return 0.0;
        }


        private string[] SepararCamposRespeitandoAspas(string linha)
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
                else if (caractere == ',' && !dentroDeAspas)
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
