using System;
using System.Collections.Generic;
using OfficeOpenXml;

namespace PluginCAISM.Parsing
{
    /// <summary>
    /// Coletas de cortisol salivar de um participante e o delta calculado.
    /// </summary>
    public class RegistroDeCortisol
    {
        /// <summary>Identificação do participante (coluna A, "ID_PARTICIPANTE").</summary>
        public string IdentificacaoDoParticipante { get; init; }

        /// <summary>
        /// Valor basal: coluna B, "SEMANA 1 INICIO DA SEMANA COLETA 1 AO ACORDAR ng/ml".
        /// NaN se ausente ou não numérico.
        /// </summary>
        public double ValorBasal { get; init; }

        /// <summary>
        /// Valor final: coluna S, "SEMANA 3 FINAL DA SEMANA COLETA 3 ANTES DE DORMIR ng/ml".
        /// NaN se ausente ou não numérico.
        /// </summary>
        public double ValorFinal { get; init; }

        /// <summary>
        /// Delta de cortisol: <see cref="ValorFinal"/> − <see cref="ValorBasal"/>.
        /// NaN quando qualquer um dos dois estiver ausente.
        /// </summary>
        public double Delta { get; init; }

        /// <summary>Nome real da coluna usada como valor basal, para exibição na tabela.</summary>
        public string RotuloDoValorBasal { get; init; }

        /// <summary>Nome real da coluna usada como valor final, para exibição na tabela.</summary>
        public string RotuloDoValorFinal { get; init; }

        /// <summary>
        /// As seis sessões de coleta do participante, na ordem das colunas B–S.
        /// </summary>
        public List<SessaoDeColetaDeCortisol> Sessoes { get; init; } = new List<SessaoDeColetaDeCortisol>();
    }

    /// <summary>
    /// Uma sessão de coleta de cortisol: três medições ao longo do dia.
    /// </summary>
    /// <remarks>
    /// Cada sessão corresponde a um trio de colunas consecutivas da planilha
    /// (ao acordar, +30 min, antes de dormir) e alimenta um dos seis gráficos lineares da aba de
    /// Dados do Participante.
    /// </remarks>
    public class SessaoDeColetaDeCortisol
    {
        /// <summary>Título da sessão. Ex.: "SEMANA 1 — INÍCIO DA SEMANA".</summary>
        public string Titulo { get; init; }

        /// <summary>Coleta 1, ao acordar (ng/ml). NaN se ausente.</summary>
        public double AoAcordar { get; init; }

        /// <summary>Coleta 2, trinta minutos após acordar (ng/ml). NaN se ausente.</summary>
        public double TrintaMinutos { get; init; }

        /// <summary>Coleta 3, antes de dormir (ng/ml). NaN se ausente.</summary>
        public double AntesDeDormir { get; init; }
    }

    /// <summary>
    /// Realiza a leitura do arquivo Excel de dados de cortisol salivar (Input 02) e calcula
    /// o delta de cortisol de cada participante.
    /// </summary>
    /// <remarks>
    /// Estrutura esperada do Excel (planilha "CORTISOL_ngml" do arquivo da pesquisa):
    /// - Coluna A: "ID_PARTICIPANTE" — a MESMA chave usada na planilha de formulários.
    /// - Colunas B a S: as dezoito coletas de cortisol, em ng/ml (o arquivo real tem exatamente
    ///   19 colunas, A–S, todas com valores literais — não há coluna de delta nem fórmulas).
    ///
    /// <b>Delta de cortisol = valor(coluna S) − valor(coluna B)</b>, isto é
    /// "SEMANA 3 FINAL DA SEMANA COLETA 3 ANTES DE DORMIR" menos
    /// "SEMANA 1 INICIO DA SEMANA COLETA 1 AO ACORDAR" — o "delta entre a coleta basal e a
    /// final de cada participante" definido pelo TextoFinal (RF01, §5.6.4 e §6: "define-se a
    /// variável analítica como o delta entre as coletas basal e final").
    ///
    /// As colunas são localizadas pelo TEXTO do cabeçalho, com índice fixo apenas como fallback:
    /// o rótulo "COLETA 3 ANTES DE DORMIR" aparece cinco vezes na planilha (uma por semana/fase),
    /// então a busca usa o cabeçalho completo para não casar com a coleta errada.
    ///
    /// A linha de cabeçalho também é localizada dinamicamente: no arquivo da pesquisa ela é a
    /// linha 1 e os dados começam na linha 2.
    ///
    /// Linhas sem valor em uma das duas colunas resultam em delta NaN.
    /// O arquivo é lido com suporte a share read para não bloquear abertura simultânea no Excel.
    ///
    /// Requer o pacote EPPlus (OfficeOpenXml).
    /// </remarks>
    // DIREÇÃO E PAREAMENTO RESOLVIDOS pelo TextoFinal de 31/08/2026, que define a variável três
    // vezes com a mesma redação: "o delta entre a coleta basal e a final de cada participante"
    // (RF01/§5.6.4) e "define-se a variável analítica como o delta entre as coletas basal e
    // final" (§6, Objetivo Específico 1). Basal = primeira das dezoito coletas (coluna B);
    // final = última (coluna S); delta = final − basal, positivo quando o cortisol subiu ao
    // longo do protocolo. A planilha real não contém fórmula (verificado no XML: nenhum <f>),
    // então S − B é a própria implementação da definição do texto, não uma reprodução de célula.
    //
    // LIMITAÇÃO documentada (não é pendência de código): B é "AO ACORDAR" e S é "ANTES DE
    // DORMIR" — a subtração cruza fases opostas do ciclo circadiano e semanas diferentes, de
    // modo que o delta mistura o efeito do período com a variação natural do dia. É decisão do
    // texto; se ele vier a parear coletas do mesmo horário, basta trocar os dois cabeçalhos
    // esperados abaixo.
    public class AnalisadorExcelCortisol
    {
        // ── Cabeçalhos esperados ─────────────────────────────────────────
        // Usados na localização por texto; os índices abaixo são só fallback.

        private const string CabecalhoDaIdentificacao = "ID_PARTICIPANTE";
        private const string CabecalhoDoValorBasal = "SEMANA 1 INICIO DA SEMANA COLETA 1 AO ACORDAR";
        private const string CabecalhoDoValorFinal = "SEMANA 3 FINAL DA SEMANA COLETA 3 ANTES DE DORMIR";

        // ── Índices de coluna usados como fallback (base 1) ───────────────

        /// <summary>Coluna A — identificação do participante.</summary>
        private const int IndiceColunaIdentificacao = 1;

        /// <summary>Coluna B — coleta 1 ao acordar da semana 1 (valor basal).</summary>
        private const int IndiceColunaValorBasal = 2;

        /// <summary>Coluna S — coleta 3 antes de dormir da semana 3 (valor final).</summary>
        private const int IndiceColunaColeta3AntesDeDormir = 19;

        /// <summary>Coluna B — primeira das dezoito medições.</summary>
        private const int IndiceDaPrimeiraColunaDeColeta = 2;

        /// <summary>
        /// Títulos das seis sessões de coleta, na ordem das colunas: B–D, E–G, H–J, K–M, N–P, Q–S.
        /// </summary>
        /// <remarks>
        /// Os títulos são fixos porque os cabeçalhos das colunas H, I e J estão rotulados como
        /// "SEMANA 1 INICIO" na planilha real, embora a POSIÇÃO corresponda à semana 2 — erro
        /// de digitação no arquivo. Usar a posição evita propagar o engano para o gráfico.
        /// </remarks>
        // CONFIRMADO no arquivo real (dados_cortisol.xlsx, 25/08/2026): no XML da planilha, as
        // células H1–J1 REUTILIZAM os mesmos textos compartilhados de B1–D1 ("SEMANA 1 INICIO DA
        // SEMANA", coletas 1–3) — assinatura típica de copiar-e-colar sem editar. Entre os
        // dezesseis rótulos únicos, "SEMANA 2 INICIO DA SEMANA" é a ÚNICA sessão ausente das
        // seis que o §5.6.4 declara ("estruturam as dezoito coletas em seis sessões de três
        // medições"), e H–J ocupam exatamente a posição dela. A interpretação posicional está,
        // portanto, verificada; resta apenas corrigir o cabeçalho na planilha, o que é tarefa do
        // arquivo de dados, não do código.
        private static readonly string[] TitulosDasSessoes =
        {
            "SEMANA 1 — INÍCIO DA SEMANA",
            "SEMANA 1 — FINAL DA SEMANA",
            "SEMANA 2 — INÍCIO DA SEMANA",
            "SEMANA 2 — FINAL DA SEMANA",
            "SEMANA 3 — INÍCIO DA SEMANA",
            "SEMANA 3 — FINAL DA SEMANA",
        };

        /// <summary>Número de linhas iniciais inspecionadas na busca pela linha de cabeçalho.</summary>
        private const int MaximoDeLinhasParaProcurarCabecalho = 5;

        /// <summary>
        /// Analisa o arquivo Excel de cortisol e retorna o delta indexado pela identificação do
        /// participante.
        /// </summary>
        /// <param name="caminhoDoExcel">Caminho completo do arquivo .xlsx.</param>
        /// <returns>
        /// Dicionário identificação do participante → delta de cortisol.
        /// Participantes com dados insuficientes têm valor double.NaN.
        /// </returns>
        public Dictionary<string, double> Analisar(string caminhoDoExcel)
        {
            var registros = AnalisarRegistros(caminhoDoExcel);
            var deltas = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var par in registros)
                deltas[par.Key] = par.Value.Delta;

            return deltas;
        }

        /// <summary>
        /// Analisa o arquivo e retorna o registro completo de cada participante: as duas coletas
        /// usadas e o delta. Alimenta a tabela de cortisol da aba de Dados do Participante.
        /// </summary>
        /// <param name="caminhoDoExcel">Caminho completo do arquivo .xlsx.</param>
        /// <exception cref="InvalidOperationException">
        /// Lançada se o arquivo não contiver planilhas, se a planilha estiver vazia ou se a linha
        /// de cabeçalho não for reconhecida.
        /// </exception>
        public Dictionary<string, RegistroDeCortisol> AnalisarRegistros(string caminhoDoExcel)
        {
            using var pacote = AbrirArquivoExcel(caminhoDoExcel);
            var planilha = ObterPrimeiraPlanilha(pacote);

            ValidarPlanilha(planilha);

            int linhaDoCabecalho = LocalizarLinhaDeCabecalho(planilha);

            int colunaIdentificacao = LocalizarColuna(
                planilha, linhaDoCabecalho, CabecalhoDaIdentificacao, IndiceColunaIdentificacao);
            int colunaBasal = LocalizarColuna(
                planilha, linhaDoCabecalho, CabecalhoDoValorBasal, IndiceColunaValorBasal);
            int colunaFinal = LocalizarColuna(
                planilha, linhaDoCabecalho, CabecalhoDoValorFinal, IndiceColunaColeta3AntesDeDormir);

            return ExtrairRegistros(planilha, linhaDoCabecalho, colunaIdentificacao, colunaBasal, colunaFinal);
        }

        private ExcelPackage AbrirArquivoExcel(string caminhoDoExcel)
        {
            var streamDeArquivo = new System.IO.FileStream(
                caminhoDoExcel,
                System.IO.FileMode.Open,
                System.IO.FileAccess.Read,
                System.IO.FileShare.ReadWrite);

            return new ExcelPackage(streamDeArquivo);
        }

        private ExcelWorksheet ObterPrimeiraPlanilha(ExcelPackage pacote)
        {
            if (pacote.Workbook.Worksheets.Count == 0)
                throw new InvalidOperationException("O arquivo Excel de cortisol não contém nenhuma planilha.");

            return pacote.Workbook.Worksheets[0];
        }

        private void ValidarPlanilha(ExcelWorksheet planilha)
        {
            if (planilha.Dimension == null)
                throw new InvalidOperationException("A planilha de cortisol está vazia.");
        }

        /// <summary>
        /// Localiza a linha de cabeçalho procurando a que contenha o rótulo do valor basal.
        /// </summary>
        private int LocalizarLinhaDeCabecalho(ExcelWorksheet planilha)
        {
            int ultimaLinha = Math.Min(planilha.Dimension.Rows, MaximoDeLinhasParaProcurarCabecalho);
            int totalDeColunas = planilha.Dimension.Columns;

            for (int numeroLinha = 1; numeroLinha <= ultimaLinha; numeroLinha++)
            {
                for (int indiceColuna = 1; indiceColuna <= totalDeColunas; indiceColuna++)
                {
                    if (CabecalhoCorresponde(planilha, numeroLinha, indiceColuna, CabecalhoDoValorBasal))
                        return numeroLinha;
                }
            }

            throw new InvalidOperationException(
                $"A planilha de cortisol não contém uma linha de cabeçalho com \"{CabecalhoDoValorBasal}\" " +
                $"nas primeiras {MaximoDeLinhasParaProcurarCabecalho} linhas.");
        }

        /// <summary>
        /// Localiza uma coluna pelo texto do cabeçalho, caindo para o índice fixo se não achar.
        /// </summary>
        private int LocalizarColuna(
            ExcelWorksheet planilha, int linhaDoCabecalho, string textoDoCabecalho, int indiceDeFallback)
        {
            int totalDeColunas = planilha.Dimension.Columns;

            for (int indiceColuna = 1; indiceColuna <= totalDeColunas; indiceColuna++)
            {
                if (CabecalhoCorresponde(planilha, linhaDoCabecalho, indiceColuna, textoDoCabecalho))
                    return indiceColuna;
            }

            return indiceDeFallback;
        }

        /// <summary>
        /// Compara o texto de uma célula de cabeçalho com o rótulo esperado, ignorando
        /// maiúsculas/minúsculas e diferenças de espaçamento.
        /// </summary>
        /// <remarks>
        /// A normalização de espaços em branco é necessária: os cabeçalhos da planilha real são
        /// multilinha (a linha do cabeçalho tem altura ampliada e quebra de texto ativada), de
        /// modo que "SEMANA 1 INICIO DA SEMANA COLETA 1 AO ACORDAR ng/ml" contém quebras de linha
        /// entre os trechos. Sem colapsar os espaços, a busca por texto nunca casaria.
        /// </remarks>
        private static bool CabecalhoCorresponde(
            ExcelWorksheet planilha, int numeroLinha, int indiceColuna, string textoEsperado)
        {
            string conteudo = NormalizarEspacos(planilha.Cells[numeroLinha, indiceColuna].Text);

            return !string.IsNullOrWhiteSpace(conteudo)
                && conteudo.IndexOf(NormalizarEspacos(textoEsperado), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Colapsa qualquer sequência de espaços, tabulações e quebras de linha em um único espaço.</summary>
        private static string NormalizarEspacos(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return string.Empty;

            return System.Text.RegularExpressions.Regex.Replace(texto, @"\s+", " ").Trim();
        }

        private Dictionary<string, RegistroDeCortisol> ExtrairRegistros(
            ExcelWorksheet planilha,
            int linhaDoCabecalho,
            int colunaIdentificacao,
            int colunaBasal,
            int colunaFinal)
        {
            var registros = new Dictionary<string, RegistroDeCortisol>(StringComparer.OrdinalIgnoreCase);
            int totalDeLinhas = planilha.Dimension.Rows;

            string rotuloBasal = NormalizarEspacos(planilha.Cells[linhaDoCabecalho, colunaBasal].Text);
            string rotuloFinal = NormalizarEspacos(planilha.Cells[linhaDoCabecalho, colunaFinal].Text);

            for (int numeroLinha = linhaDoCabecalho + 1; numeroLinha <= totalDeLinhas; numeroLinha++)
            {
                string identificacao = planilha.Cells[numeroLinha, colunaIdentificacao].Text?.Trim();

                if (string.IsNullOrWhiteSpace(identificacao))
                    continue;

                double valorBasal = LerValorNumerico(planilha, numeroLinha, colunaBasal);
                double valorFinal = LerValorNumerico(planilha, numeroLinha, colunaFinal);

                registros[identificacao] = new RegistroDeCortisol
                {
                    IdentificacaoDoParticipante = identificacao,
                    ValorBasal = valorBasal,
                    ValorFinal = valorFinal,
                    Delta = CalcularDelta(valorBasal, valorFinal),
                    RotuloDoValorBasal = rotuloBasal,
                    RotuloDoValorFinal = rotuloFinal,
                    Sessoes = ExtrairSessoesDeColeta(planilha, numeroLinha),
                };
            }

            return registros;
        }

        /// <summary>
        /// Extrai as seis sessões de coleta de uma linha, lendo as dezoito colunas B–S em trios
        /// consecutivos (ao acordar, +30 min, antes de dormir).
        /// </summary>
        /// <remarks>
        /// As colunas são lidas por POSIÇÃO, a partir de
        /// <see cref="IndiceDaPrimeiraColunaDeColeta"/>, e não pelo texto do cabeçalho — ver a
        /// nota em <see cref="TitulosDasSessoes"/> sobre o rótulo trocado das colunas H–J
        /// (verificado no arquivo real: H–J reutilizam o texto de B–D por erro de digitação).
        /// Células vazias ou não numéricas viram NaN, que a interface exibe como lacuna no ponto.
        /// </remarks>
        private List<SessaoDeColetaDeCortisol> ExtrairSessoesDeColeta(
            ExcelWorksheet planilha, int numeroLinha)
        {
            var sessoes = new List<SessaoDeColetaDeCortisol>();

            for (int indiceDaSessao = 0; indiceDaSessao < TitulosDasSessoes.Length; indiceDaSessao++)
            {
                int primeiraColuna = IndiceDaPrimeiraColunaDeColeta + (indiceDaSessao * 3);

                sessoes.Add(new SessaoDeColetaDeCortisol
                {
                    Titulo        = TitulosDasSessoes[indiceDaSessao],
                    AoAcordar     = LerValorNumerico(planilha, numeroLinha, primeiraColuna),
                    TrintaMinutos = LerValorNumerico(planilha, numeroLinha, primeiraColuna + 1),
                    AntesDeDormir = LerValorNumerico(planilha, numeroLinha, primeiraColuna + 2),
                });
            }

            return sessoes;
        }

        /// <summary>
        /// Delta de cortisol: valor final (coluna S) menos valor basal (coluna B).
        /// Retorna NaN se qualquer um dos dois estiver ausente.
        /// </summary>
        private static double CalcularDelta(double valorBasal, double valorFinal)
        {
            if (double.IsNaN(valorBasal) || double.IsNaN(valorFinal))
                return double.NaN;

            return valorFinal - valorBasal;
        }

        private double LerValorNumerico(ExcelWorksheet planilha, int numeroLinha, int indiceColuna)
        {
            var celula = planilha.Cells[numeroLinha, indiceColuna];

            if (celula.Value == null || string.IsNullOrWhiteSpace(celula.Text))
                return double.NaN;

            // O valor numérico é preferido ao texto: Text aplica a cultura do sistema e o formato
            // da célula, o que introduziria vírgula decimal e arredondamento de exibição.
            if (celula.Value is double numeroDireto)
                return numeroDireto;

            if (double.TryParse(celula.Text,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double valor))
            {
                return valor;
            }

            return double.NaN;
        }
    }
}
