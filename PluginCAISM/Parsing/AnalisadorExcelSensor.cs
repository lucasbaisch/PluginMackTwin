using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using PluginCAISM.Configuracao;

namespace PluginCAISM.Parsing
{
    /// <summary>
    /// Realiza a leitura do arquivo Excel de dados ambientais de sensores (Input 03)
    /// e produz um índice para enriquecimento das linhas de sessão por timestamp.
    /// </summary>
    /// <remarks>
    /// O schema exato de colunas não é conhecido em tempo de design. A coluna de data/hora é
    /// localizada pelos candidatos de <see cref="MapeamentoDeColunasDeSensor.Timestamp"/>; todas
    /// as demais viram campos dinâmicos (string → string), mapeados aos parâmetros ambientais por
    /// <see cref="MapeamentoDeColunasDeSensor"/>.
    ///
    /// TODAS as planilhas do arquivo são lidas e mescladas em um único índice. Isso é necessário
    /// porque o arquivo real da pesquisa divide as medições em abas por período ("junho julho",
    /// "01 03 2026 - 19 03 2026", …): ler apenas a primeira traria um intervalo de datas que pode
    /// não ter nenhuma interseção com as sessões carregadas, resultando em zero correspondências
    /// sem nenhum erro aparente.
    ///
    /// A linha de cabeçalho não é fixa e pode não existir: no arquivo real, a aba de março começa
    /// com dados já na linha 1. Quando uma aba não tem cabeçalho reconhecível, são reaproveitados
    /// os nomes de coluna por POSIÇÃO de uma aba que tenha, e um aviso é registrado.
    ///
    /// Os timestamps correspondem aos do Input 01 por igualdade exata de DateTime. Por isso
    /// células de data/hora numéricas são convertidas por <see cref="DateTime.FromOADate"/> e
    /// arredondadas ao milissegundo — ler o texto formatado da célula perderia precisão.
    ///
    /// Requer o pacote EPPlus (OfficeOpenXml).
    /// </remarks>
    public class AnalisadorExcelSensor
    {
        private const string FormatoDeTimestamp = "yyyy-MM-dd HH:mm:ss.fff";

        /// <summary>
        /// Número de linhas iniciais inspecionadas na busca pela linha de cabeçalho.
        /// </summary>
        private const int MaximoDeLinhasParaProcurarCabecalho = 20;

        /// <summary>
        /// Disposição de colunas de uma planilha: onde está o cabeçalho, qual coluna é o timestamp
        /// e o nome de cada coluna por índice.
        /// </summary>
        private sealed class DisposicaoDeColunas
        {
            public int LinhaDoCabecalho { get; init; }
            public int IndiceDeTimestamp { get; init; }
            public Dictionary<int, string> NomesPorIndice { get; init; }
        }

        /// <summary>
        /// Analisa o arquivo Excel de sensores e retorna um índice por timestamp, mesclando todas
        /// as planilhas do arquivo.
        /// </summary>
        /// <param name="caminhoDoExcel">Caminho completo do arquivo .xlsx de sensores.</param>
        /// <param name="avisos">
        /// Lista populada com o que foi lido de cada planilha, planilhas ignoradas e uso de
        /// cabeçalho por posição. Pode ser null.
        /// </param>
        /// <returns>
        /// Dicionário onde a chave é o timestamp e o valor é um dicionário de campos
        /// (nome da coluna → valor como string).
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Lançada se o arquivo não contiver planilhas ou se NENHUMA planilha tiver uma coluna de
        /// data/hora reconhecível.
        /// </exception>
        public Dictionary<DateTime, Dictionary<string, string>> Analisar(
            string caminhoDoExcel,
            List<string> avisos = null)
        {
            using var pacote = AbrirArquivoExcel(caminhoDoExcel);

            var planilhas = ObterPlanilhasComConteudo(pacote);

            if (planilhas.Count == 0)
                throw new InvalidOperationException(
                    "O arquivo Excel de sensores não contém nenhuma planilha com dados.");

            var disposicaoDeReferencia = LocalizarPrimeiraDisposicao(planilhas)
                ?? throw new InvalidOperationException(ConstruirMensagemDeColunaAusente(planilhas));

            var indice = new Dictionary<DateTime, Dictionary<string, string>>();
            int duplicados = 0;

            foreach (var planilha in planilhas)
            {
                var disposicao = LocalizarDisposicao(planilha);
                int primeiraLinhaDeDados;

                if (disposicao != null)
                {
                    primeiraLinhaDeDados = disposicao.LinhaDoCabecalho + 1;
                }
                else
                {
                    // Aba sem cabeçalho: reaproveita a disposição por POSIÇÃO e lê desde a linha 1.
                    disposicao = disposicaoDeReferencia;
                    primeiraLinhaDeDados = 1;
                    avisos?.Add($"Planilha '{planilha.Name}': sem linha de cabeçalho reconhecível — " +
                                "os nomes de coluna foram reaproveitados por posição de outra planilha " +
                                "do arquivo, e a leitura começou na linha 1.");
                }

                int lidas = AcumularLinhasDaPlanilha(
                    planilha, disposicao, primeiraLinhaDeDados, indice, ref duplicados);

                avisos?.Add($"Planilha '{planilha.Name}': {lidas} linha(s) de sensor lida(s).");
            }

            if (duplicados > 0)
                avisos?.Add($"{duplicados} timestamp(s) repetido(s) entre planilhas — " +
                            "prevaleceu a última leitura encontrada.");

            if (indice.Count == 0)
                avisos?.Add("Nenhuma linha de sensor com data/hora válida foi encontrada no arquivo.");

            return indice;
        }

        private ExcelPackage AbrirArquivoExcel(string caminhoDoExcel)
        {
            var streamDeArquivo = new FileStream(
                caminhoDoExcel,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            return new ExcelPackage(streamDeArquivo);
        }

        private List<ExcelWorksheet> ObterPlanilhasComConteudo(ExcelPackage pacote)
        {
            return pacote.Workbook.Worksheets
                .Where(planilha => planilha.Dimension != null)
                .ToList();
        }

        /// <summary>
        /// Devolve a primeira disposição de colunas encontrada entre as planilhas, usada como
        /// referência para as abas que não têm cabeçalho.
        /// </summary>
        private DisposicaoDeColunas LocalizarPrimeiraDisposicao(List<ExcelWorksheet> planilhas)
        {
            foreach (var planilha in planilhas)
            {
                var disposicao = LocalizarDisposicao(planilha);
                if (disposicao != null)
                    return disposicao;
            }

            return null;
        }

        /// <summary>
        /// Procura, nas primeiras linhas da planilha, uma que contenha uma célula correspondente a
        /// algum dos nomes candidatos de coluna de data/hora.
        /// </summary>
        /// <returns>A disposição encontrada, ou null se a planilha não tiver cabeçalho.</returns>
        private DisposicaoDeColunas LocalizarDisposicao(ExcelWorksheet planilha)
        {
            int ultimaLinhaInspecionada = Math.Min(planilha.Dimension.Rows, MaximoDeLinhasParaProcurarCabecalho);
            int totalDeColunas = planilha.Dimension.Columns;

            for (int numeroLinha = 1; numeroLinha <= ultimaLinhaInspecionada; numeroLinha++)
            {
                for (int indiceColuna = 1; indiceColuna <= totalDeColunas; indiceColuna++)
                {
                    string conteudo = planilha.Cells[numeroLinha, indiceColuna].Text?.Trim();

                    if (!CorrespondeAAlgumCandidatoDeTimestamp(conteudo))
                        continue;

                    return new DisposicaoDeColunas
                    {
                        LinhaDoCabecalho = numeroLinha,
                        IndiceDeTimestamp = indiceColuna,
                        NomesPorIndice = ExtrairNomesDeColunas(planilha, numeroLinha),
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Verifica se o texto de uma célula de cabeçalho corresponde a algum dos nomes candidatos
        /// de coluna de data/hora, por igualdade exata e depois por conteúdo.
        /// </summary>
        private static bool CorrespondeAAlgumCandidatoDeTimestamp(string conteudo)
        {
            if (string.IsNullOrWhiteSpace(conteudo))
                return false;

            foreach (string candidato in MapeamentoDeColunasDeSensor.Timestamp)
            {
                if (string.IsNullOrWhiteSpace(candidato))
                    continue;

                if (string.Equals(conteudo, candidato, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (conteudo.IndexOf(candidato, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private string ConstruirMensagemDeColunaAusente(List<ExcelWorksheet> planilhas)
        {
            return "O arquivo Excel de sensores não contém uma coluna de data/hora reconhecível " +
                   $"nas primeiras {MaximoDeLinhasParaProcurarCabecalho} linhas de nenhuma das suas " +
                   $"planilhas ({string.Join(", ", planilhas.Select(p => $"'{p.Name}'"))}). " +
                   $"Nomes aceitos: {string.Join(", ", MapeamentoDeColunasDeSensor.Timestamp)} — " +
                   "ajustar MapeamentoDeColunasDeSensor.Timestamp se a coluna tiver outro nome.";
        }

        private Dictionary<int, string> ExtrairNomesDeColunas(ExcelWorksheet planilha, int linhaDoCabecalho)
        {
            var nomesPorIndice = new Dictionary<int, string>();
            int totalDeColunas = planilha.Dimension.Columns;

            for (int indiceColuna = 1; indiceColuna <= totalDeColunas; indiceColuna++)
            {
                string nome = planilha.Cells[linhaDoCabecalho, indiceColuna].Text?.Trim();

                if (!string.IsNullOrWhiteSpace(nome))
                    nomesPorIndice[indiceColuna] = nome;
            }

            return nomesPorIndice;
        }

        /// <summary>
        /// Percorre as linhas de dados de uma planilha e acumula as que têm timestamp válido no
        /// índice compartilhado.
        /// </summary>
        /// <returns>Quantidade de linhas efetivamente acumuladas.</returns>
        private int AcumularLinhasDaPlanilha(
            ExcelWorksheet planilha,
            DisposicaoDeColunas disposicao,
            int primeiraLinhaDeDados,
            Dictionary<DateTime, Dictionary<string, string>> indice,
            ref int duplicados)
        {
            int totalDeLinhas = planilha.Dimension.Rows;
            int lidas = 0;

            for (int numeroLinha = primeiraLinhaDeDados; numeroLinha <= totalDeLinhas; numeroLinha++)
            {
                if (!TentarObterTimestamp(planilha, numeroLinha, disposicao.IndiceDeTimestamp, out DateTime timestamp))
                    continue;

                if (indice.ContainsKey(timestamp))
                    duplicados++;

                indice[timestamp] = ExtrairCamposDaLinha(planilha, numeroLinha, disposicao);
                lidas++;
            }

            return lidas;
        }

        /// <summary>
        /// Lê o timestamp de uma linha, aceitando célula de data/hora real (numérica, serial OADate),
        /// valor já materializado como DateTime, ou texto no formato "yyyy-MM-dd HH:mm:ss.fff".
        /// </summary>
        private bool TentarObterTimestamp(
            ExcelWorksheet planilha, int numeroLinha, int indiceDeTimestamp, out DateTime timestamp)
        {
            timestamp = default;

            var valorDaCelula = planilha.Cells[numeroLinha, indiceDeTimestamp].Value;

            if (valorDaCelula == null)
                return false;

            if (valorDaCelula is DateTime dataDireta)
            {
                timestamp = ArredondarAoMilissegundo(dataDireta);
                return true;
            }

            if (valorDaCelula is double serialDeData)
            {
                try
                {
                    timestamp = ArredondarAoMilissegundo(DateTime.FromOADate(serialDeData));
                    return true;
                }
                catch (ArgumentException)
                {
                    // Numérico fora da faixa de datas do Excel: não é um timestamp.
                    return false;
                }
            }

            string texto = planilha.Cells[numeroLinha, indiceDeTimestamp].Text?.Trim();

            if (string.IsNullOrWhiteSpace(texto))
                return false;

            if (DateTime.TryParseExact(texto, FormatoDeTimestamp, CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out DateTime dataExata))
            {
                timestamp = dataExata;
                return true;
            }

            return DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
        }

        /// <summary>
        /// Arredonda ao milissegundo mais próximo. A conversão de serial OADate para DateTime
        /// carrega erro de ponto flutuante em frações de milissegundo, o que impediria a
        /// correspondência por igualdade exata com os timestamps do Input 01.
        /// </summary>
        private static DateTime ArredondarAoMilissegundo(DateTime valor)
        {
            long ticksPorMilissegundo = TimeSpan.TicksPerMillisecond;
            long ticksArredondados = (long)Math.Round(valor.Ticks / (double)ticksPorMilissegundo) * ticksPorMilissegundo;
            return new DateTime(ticksArredondados, valor.Kind);
        }

        private Dictionary<string, string> ExtrairCamposDaLinha(
            ExcelWorksheet planilha,
            int numeroLinha,
            DisposicaoDeColunas disposicao)
        {
            var camposDaLinha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (indiceColuna, nomeDaColuna) in disposicao.NomesPorIndice)
            {
                if (indiceColuna == disposicao.IndiceDeTimestamp)
                    continue;

                camposDaLinha[nomeDaColuna] = LerCelulaComoTexto(planilha.Cells[numeroLinha, indiceColuna]);
            }

            return camposDaLinha;
        }

        /// <summary>
        /// Converte uma célula em texto, formatando valores numéricos com
        /// <see cref="CultureInfo.InvariantCulture"/>.
        /// </summary>
        /// <remarks>
        /// Necessário porque a propriedade Text do EPPlus aplica a cultura do sistema: em pt-BR
        /// uma temperatura de 23,5 °C sairia como "23,5" e o consumidor
        /// (<see cref="Processamento.CalculadorDeMetricasPorObjeto"/>), que parseia com
        /// InvariantCulture e NumberStyles.Any, leria a vírgula como separador de milhar e
        /// obteria 235 — sem erro e sem aviso. Formatar aqui mantém o contrato
        /// "campos de sensor são texto em cultura invariante".
        /// </remarks>
        private static string LerCelulaComoTexto(ExcelRange celula)
        {
            object valor = celula.Value;

            if (valor == null)
                return string.Empty;

            if (valor is double numeroDecimal)
                return numeroDecimal.ToString(CultureInfo.InvariantCulture);

            if (valor is decimal numeroExato)
                return numeroExato.ToString(CultureInfo.InvariantCulture);

            if (valor is int numeroInteiro)
                return numeroInteiro.ToString(CultureInfo.InvariantCulture);

            if (valor is long numeroLongo)
                return numeroLongo.ToString(CultureInfo.InvariantCulture);

            return celula.Text?.Trim() ?? string.Empty;
        }
    }
}
