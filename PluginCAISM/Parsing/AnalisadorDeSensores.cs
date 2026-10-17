using System;
using System.Collections.Generic;
using System.IO;

namespace PluginCAISM.Parsing
{
    /// <summary>
    /// Ponto único de leitura dos dados ambientais (Entrada II): escolhe o analisador conforme a
    /// extensão do arquivo e devolve, em qualquer caso, o mesmo índice por timestamp.
    /// </summary>
    /// <remarks>
    /// A pesquisa recebe os dados ambientais em dois formatos. O <b>.csv</b> é o que o módulo de
    /// monitoramento grava, e é o preferível: nele os números estão como o dispositivo os
    /// registrou. O <b>.xlsx</b> aparece quando o arquivo passou pelo Excel — em geral para
    /// inspeção manual — e continua aceito, porque é o formato dos arquivos já coletados.
    ///
    /// A fachada existe para que essa escolha não vaze para a interface nem para o
    /// <see cref="Processamento.ConstruturDeSessoes"/>: os dois enxergam uma única operação de
    /// leitura, com a mesma assinatura e o mesmo retorno de antes.
    /// </remarks>
    public class AnalisadorDeSensores
    {
        private readonly AnalisadorCsvSensor _analisadorCsv = new AnalisadorCsvSensor();
        private readonly AnalisadorExcelSensor _analisadorExcel = new AnalisadorExcelSensor();

        /// <summary>Extensões aceitas, para compor o filtro da caixa de seleção de arquivo.</summary>
        public const string FiltroDeArquivo =
            "Dados de sensores (*.csv, *.xlsx)|*.csv;*.xlsx|CSV de sensores (*.csv)|*.csv|" +
            "Excel de sensores (*.xlsx)|*.xlsx";

        /// <summary>
        /// Lê o arquivo de dados ambientais e devolve o índice de leituras por timestamp.
        /// </summary>
        /// <param name="caminhoDoArquivo">Caminho completo do .csv ou .xlsx de sensores.</param>
        /// <param name="avisos">Lista populada pelo analisador escolhido. Pode ser null.</param>
        /// <exception cref="InvalidOperationException">
        /// Lançada quando a extensão não é reconhecida, além das condições que cada analisador
        /// já reporta.
        /// </exception>
        public Dictionary<DateTime, Dictionary<string, string>> Analisar(
            string caminhoDoArquivo,
            List<string> avisos = null)
        {
            string extensao = Path.GetExtension(caminhoDoArquivo)?.ToLowerInvariant();

            switch (extensao)
            {
                case ".csv":
                case ".txt":
                    return _analisadorCsv.Analisar(caminhoDoArquivo, avisos);

                case ".xlsx":
                case ".xlsm":
                    return _analisadorExcel.Analisar(caminhoDoArquivo, avisos);

                default:
                    throw new InvalidOperationException(
                        $"Formato não reconhecido para os dados de sensores: '{extensao}'. " +
                        "São aceitos .csv (formato nativo do módulo de monitoramento) e .xlsx.");
            }
        }
    }
}
