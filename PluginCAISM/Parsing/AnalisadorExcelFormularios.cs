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
    /// Realiza a leitura do arquivo Excel de formulários aplicados aos participantes (Arquivo 4)
    /// e devolve as respostas por participante.
    /// </summary>
    /// <remarks>
    /// Espelha <see cref="AnalisadorExcelCortisol"/>: leitura de .xlsx via EPPlus com share read,
    /// primeira planilha do arquivo e tolerância a células vazias ou inválidas.
    ///
    /// Conforme o Requisito 01 (Quadro 39), a validação não interrompe a leitura: linhas
    /// problemáticas são registradas na lista de avisos e puladas, e as demais são processadas —
    /// o mesmo contrato de <see cref="AnalisadorCsvSessao"/>.
    ///
    /// O esquema de colunas não é conhecido em tempo de design: a linha de cabeçalho é localizada
    /// dinamicamente e todas as colunas viram respostas brutas. A identificação do participante
    /// usa os nomes candidatos de <see cref="MapeamentoDeColunasDeFormulario"/>.
    ///
    /// Este é um caminho de ingestão PARALELO: os dados de formulário são contextualização (§5.3)
    /// e não passam pelo <see cref="Processamento.ConstruturDeSessoes"/>.
    /// </remarks>
    public class AnalisadorExcelFormularios
    {
        /// <summary>Número de linhas iniciais inspecionadas na busca pela linha de cabeçalho.</summary>
        private const int MaximoDeLinhasParaProcurarCabecalho = 20;

        /// <summary>Quantidade mínima de células preenchidas para uma linha ser tida como cabeçalho.</summary>
        private const int MinimoDeColunasPreenchidasNoCabecalho = 2;

        /// <summary>
        /// Analisa o arquivo Excel de formulários e retorna as respostas por participante.
        /// </summary>
        /// <param name="caminhoDoExcel">Caminho completo do arquivo .xlsx de formulários.</param>
        /// <param name="avisosDeLinhasIgnoradas">
        /// Lista populada com uma mensagem descritiva para cada linha ignorada. A leitura NÃO é
        /// interrompida por linhas inválidas.
        /// </param>
        /// <returns>
        /// Lista de respostas, uma por linha válida da planilha. As respostas brutas preservam
        /// todas as colunas; os escores derivados são responsabilidade do
        /// <see cref="Processamento.CalculadorDeEscoresDeFormulario"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Lançada apenas em falhas estruturais que impedem qualquer leitura: arquivo sem
        /// planilhas, planilha vazia ou cabeçalho não reconhecido.
        /// </exception>
        public List<RespostaBrutaDeFormulario> Analisar(
            string caminhoDoExcel,
            List<string> avisosDeLinhasIgnoradas)
        {
            using var pacote = AbrirArquivoExcel(caminhoDoExcel);

            var planilhas = pacote.Workbook.Worksheets
                .Where(planilha => planilha.Dimension != null)
                .ToList();

            if (planilhas.Count == 0)
                throw new InvalidOperationException(
                    "O arquivo Excel de formulários não contém nenhuma planilha com dados.");

            AvisarSeIdentificacaoNaoConfigurada(avisosDeLinhasIgnoradas);

            // Um participante pode ter respostas espalhadas em várias planilhas
            // (a_recrutamento, b_ieee, c_percepção_ambiental). Todas são lidas e unidas pelo
            // identificador, de modo que uma única RespostaBrutaDeFormulario reúna o participante
            // inteiro — é o que permite, por exemplo, cruzar a demografia de a_recrutamento com o
            // escore TOTAL de b_ieee.
            var porParticipante = new Dictionary<string, RespostaBrutaDeFormulario>(
                StringComparer.OrdinalIgnoreCase);
            var semIdentificacao = new List<RespostaBrutaDeFormulario>();

            foreach (var planilha in planilhas)
            {
                int linhaDoCabecalho;
                try
                {
                    linhaDoCabecalho = LocalizarLinhaDeCabecalho(planilha);
                }
                catch (InvalidOperationException)
                {
                    avisosDeLinhasIgnoradas?.Add(
                        $"Planilha '{planilha.Name}': ignorada por não ter cabeçalho reconhecível.");
                    continue;
                }

                var nomesDeColunas = ExtrairNomesDeColunas(planilha, linhaDoCabecalho);
                var respostasDaPlanilha = ExtrairRespostas(
                    planilha, linhaDoCabecalho, nomesDeColunas, avisosDeLinhasIgnoradas);

                foreach (var resposta in respostasDaPlanilha)
                {
                    if (string.IsNullOrWhiteSpace(resposta.IdentificacaoManual))
                    {
                        semIdentificacao.Add(resposta);
                        continue;
                    }

                    if (porParticipante.TryGetValue(resposta.IdentificacaoManual, out var existente))
                    {
                        MesclarCampos(existente.Campos, resposta.Campos);
                        // As colunas da nova planilha entram DEPOIS das já acumuladas, preservando
                        // a ordem das planilhas e, dentro de cada uma, a ordem das colunas.
                        foreach (var coluna in resposta.OrdemDasColunas)
                        {
                            if (!existente.OrdemDasColunas.Contains(coluna))
                                existente.OrdemDasColunas.Add(coluna);
                        }
                    }
                    else
                    {
                        porParticipante[resposta.IdentificacaoManual] = resposta;
                    }
                }

                avisosDeLinhasIgnoradas?.Add(
                    $"Planilha '{planilha.Name}': {respostasDaPlanilha.Count} linha(s) lida(s), " +
                    $"{nomesDeColunas.Count} coluna(s).");
            }

            var resultado = porParticipante.Values.ToList();
            resultado.AddRange(semIdentificacao);

            avisosDeLinhasIgnoradas?.Add(
                $"{porParticipante.Count} participante(s) após unir as planilhas pelo identificador.");

            return resultado;
        }

        /// <summary>
        /// Une os campos de uma planilha aos já acumulados para o mesmo participante.
        /// Colunas já presentes não são sobrescritas: como os nomes são prefixados pela planilha
        /// de origem, uma colisão só ocorre se a mesma planilha repetir o participante.
        /// </summary>
        private static void MesclarCampos(
            Dictionary<string, string> acumulados, Dictionary<string, string> novos)
        {
            foreach (var par in novos)
            {
                if (!acumulados.ContainsKey(par.Key))
                    acumulados[par.Key] = par.Value;
            }
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

        private ExcelWorksheet ObterPrimeiraPlanilha(ExcelPackage pacote)
        {
            if (pacote.Workbook.Worksheets.Count == 0)
                throw new InvalidOperationException("O arquivo Excel de formulários não contém nenhuma planilha.");

            return pacote.Workbook.Worksheets[0];
        }

        private void ValidarPlanilha(ExcelWorksheet planilha)
        {
            if (planilha.Dimension == null)
                throw new InvalidOperationException("A planilha de formulários está vazia.");
        }

        private int LocalizarLinhaDeCabecalho(ExcelWorksheet planilha)
        {
            int ultimaLinhaInspecionada = Math.Min(planilha.Dimension.Rows, MaximoDeLinhasParaProcurarCabecalho);

            for (int numeroLinha = 1; numeroLinha <= ultimaLinhaInspecionada; numeroLinha++)
            {
                if (ContarCelulasPreenchidas(planilha, numeroLinha) >= MinimoDeColunasPreenchidasNoCabecalho)
                    return numeroLinha;
            }

            throw new InvalidOperationException(
                $"Não foi possível identificar a linha de cabeçalho do arquivo de formulários " +
                $"nas primeiras {MaximoDeLinhasParaProcurarCabecalho} linhas.");
        }

        private int ContarCelulasPreenchidas(ExcelWorksheet planilha, int numeroLinha)
        {
            int totalDeColunas = planilha.Dimension.Columns;
            int preenchidas = 0;

            for (int indiceColuna = 1; indiceColuna <= totalDeColunas; indiceColuna++)
            {
                if (!string.IsNullOrWhiteSpace(planilha.Cells[numeroLinha, indiceColuna].Text))
                    preenchidas++;
            }

            return preenchidas;
        }

        /// <summary>
        /// Separador entre o nome da planilha de origem e o nome da coluna.
        /// </summary>
        /// <remarks>
        /// Os nomes de coluna são prefixados pela planilha ("b_ieee · TOTAL") por dois motivos:
        /// evitar colisão ao unir as planilhas de um mesmo participante — todas têm uma coluna de
        /// identificação, por exemplo — e permitir que a aba de dados do participante filtre as
        /// linhas por formulário de origem.
        /// </remarks>
        public const string SeparadorDePlanilha = " · ";

        private Dictionary<int, string> ExtrairNomesDeColunas(ExcelWorksheet planilha, int linhaDoCabecalho)
        {
            var nomesPorIndice = new Dictionary<int, string>();
            int totalDeColunas = planilha.Dimension.Columns;

            for (int indiceColuna = 1; indiceColuna <= totalDeColunas; indiceColuna++)
            {
                string nome = planilha.Cells[linhaDoCabecalho, indiceColuna].Text?.Trim();

                if (!string.IsNullOrWhiteSpace(nome))
                    nomesPorIndice[indiceColuna] = $"{planilha.Name}{SeparadorDePlanilha}{nome}";
            }

            return nomesPorIndice;
        }

        /// <summary>
        /// Registra um aviso quando a coluna de identificação do participante ainda não foi
        /// configurada, situação em que nenhuma resposta pode ser associada a uma sessão.
        /// </summary>
        private void AvisarSeIdentificacaoNaoConfigurada(List<string> avisos)
        {
            if (MapeamentoDeColunasDeFormulario.IdentificacaoDoParticipante.Length > 0)
                return;

            avisos?.Add(
                "A coluna de identificação do participante ainda não foi configurada em " +
                "MapeamentoDeColunasDeFormulario.IdentificacaoDoParticipante. As respostas são " +
                "carregadas, mas não podem ser associadas às sessões nem aos gráficos de dispersão.");
        }

        private List<RespostaBrutaDeFormulario> ExtrairRespostas(
            ExcelWorksheet planilha,
            int linhaDoCabecalho,
            Dictionary<int, string> nomesDeColunas,
            List<string> avisos)
        {
            var respostas = new List<RespostaBrutaDeFormulario>();
            var identificacoesJaVistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int totalDeLinhas = planilha.Dimension.Rows;

            for (int numeroLinha = linhaDoCabecalho + 1; numeroLinha <= totalDeLinhas; numeroLinha++)
            {
                var campos = LerCamposDaLinha(planilha, numeroLinha, nomesDeColunas);

                if (!campos.Values.Any(valor => !string.IsNullOrWhiteSpace(valor)))
                    continue;

                string identificacao = LocalizarIdentificacao(campos);

                if (string.IsNullOrWhiteSpace(identificacao)
                    && MapeamentoDeColunasDeFormulario.IdentificacaoDoParticipante.Length > 0)
                {
                    avisos?.Add($"Linha {numeroLinha}: ignorada por não conter identificação do participante.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(identificacao) && !identificacoesJaVistas.Add(identificacao))
                    avisos?.Add($"Linha {numeroLinha}: identificação \"{identificacao}\" repetida — a linha foi mantida.");

                respostas.Add(new RespostaBrutaDeFormulario
                {
                    IdentificacaoManual = identificacao,
                    Campos = campos,
                    // Índices crescentes = colunas da esquerda para a direita.
                    OrdemDasColunas = nomesDeColunas.OrderBy(par => par.Key)
                                                    .Select(par => par.Value)
                                                    .ToList(),
                    NumeroDaLinhaOriginal = numeroLinha,
                });
            }

            return respostas;
        }

        private Dictionary<string, string> LerCamposDaLinha(
            ExcelWorksheet planilha, int numeroLinha, Dictionary<int, string> nomesDeColunas)
        {
            var campos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (indiceColuna, nomeDaColuna) in nomesDeColunas)
                campos[nomeDaColuna] = LerCelulaComoTexto(planilha.Cells[numeroLinha, indiceColuna]);

            return campos;
        }

        /// <summary>
        /// Procura o valor da identificação do participante testando os nomes candidatos
        /// configurados, por igualdade exata e depois por conteúdo.
        /// </summary>
        private string LocalizarIdentificacao(Dictionary<string, string> campos)
        {
            foreach (string candidato in MapeamentoDeColunasDeFormulario.IdentificacaoDoParticipante)
            {
                if (string.IsNullOrWhiteSpace(candidato))
                    continue;

                if (campos.TryGetValue(candidato, out string valorExato) && !string.IsNullOrWhiteSpace(valorExato))
                    return valorExato;

                foreach (var par in campos)
                {
                    if (par.Key.IndexOf(candidato, StringComparison.OrdinalIgnoreCase) >= 0
                        && !string.IsNullOrWhiteSpace(par.Value))
                    {
                        return par.Value;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Converte uma célula em texto, formatando valores numéricos com
        /// <see cref="CultureInfo.InvariantCulture"/> para que a pontuação dos escores não dependa
        /// da cultura da máquina (em pt-BR a propriedade Text do EPPlus devolveria "4,5" e um
        /// parse invariante leria 45).
        /// </summary>
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

    /// <summary>
    /// Uma linha da planilha de formulários, ainda sem escores derivados.
    /// </summary>
    public class RespostaBrutaDeFormulario
    {
        /// <summary>
        /// Identificação do participante lida da coluna configurada, ou null quando a coluna
        /// não está mapeada.
        /// </summary>
        public string IdentificacaoManual { get; init; }

        /// <summary>Respostas da linha, indexadas pelo nome da coluna.</summary>
        public Dictionary<string, string> Campos { get; init; }

        /// <summary>
        /// Nomes das colunas na ORDEM em que aparecem na planilha, da esquerda para a direita.
        /// </summary>
        /// <remarks>
        /// Existe porque <see cref="Campos"/> é um dicionário e não garante ordem de iteração.
        /// As tabelas da aba de Dados do Participante precisam exibir as respostas na mesma
        /// sequência das colunas do arquivo, e não em ordem alfabética.
        /// </remarks>
        public List<string> OrdemDasColunas { get; init; } = new List<string>();

        /// <summary>Número da linha na planilha, para rastreabilidade nos avisos.</summary>
        public int NumeroDaLinhaOriginal { get; init; }
    }
}
