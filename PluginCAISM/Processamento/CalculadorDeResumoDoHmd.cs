using System;
using System.Collections.Generic;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Processamento
{
    /// <summary>
    /// Resume, por participante, o que o <c>dados_hmd.csv</c> registrou: extensão da coleta,
    /// fixações detectadas e os objetos de maior permanência do olhar.
    /// </summary>
    /// <remarks>
    /// Calculadora pura: recebe sessões já construídas e devolve valores. Não lê arquivo, não
    /// toca no Revit e não alimenta o índice — é descrição da coleta, para a aba de
    /// contextualização.
    ///
    /// Participantes sem <see cref="Sessao.IdentificacaoManual"/> são ignorados: não há a quem
    /// atribuir o resumo. No arquivo atual isso descarta 14 das 35 sessões.
    /// </remarks>
    public class CalculadorDeResumoDoHmd
    {
        /// <summary>
        /// Resume cada participante presente nas sessões, em ordem alfabética de identificação.
        /// </summary>
        public List<ResumoDoHmdDoParticipante> Calcular(IEnumerable<Sessao> sessoes)
        {
            if (sessoes == null)
                return new List<ResumoDoHmdDoParticipante>();

            return sessoes
                .Where(sessao => sessao != null
                              && !string.IsNullOrWhiteSpace(sessao.IdentificacaoManual))
                .GroupBy(sessao => sessao.IdentificacaoManual, StringComparer.OrdinalIgnoreCase)
                .OrderBy(grupo => grupo.Key, StringComparer.OrdinalIgnoreCase)
                .Select(grupo => Resumir(grupo.Key, grupo.ToList()))
                .ToList();
        }

        private static ResumoDoHmdDoParticipante Resumir(string identificacao, List<Sessao> sessoes)
        {
            var amostras = sessoes.SelectMany(sessao => sessao.DadosBrutos).ToList();
            var fixacoes = sessoes.SelectMany(sessao => sessao.Fixacoes).ToList();

            var objetos = ClassificarObjetosPorPermanencia(amostras);
            double tempoTotal = sessoes.Sum(CalcularDuracaoEmMinutos);

            return new ResumoDoHmdDoParticipante
            {
                PermanenciaPorCenario = DistribuirEntreCenarios(sessoes, tempoTotal),
                IdentificacaoManual   = identificacao,
                QuantidadeDeSessoes   = sessoes.Count,
                QuantidadeDeCenarios  = sessoes
                    .Select(sessao => sessao.RotuloDeCenario)
                    .Where(cenario => !string.IsNullOrWhiteSpace(cenario))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                TempoTotalEmRvMinutos = tempoTotal,

                QuantidadeDeFixacoes      = fixacoes.Count,
                DuracaoMediaDasFixacoesMs = fixacoes.Any()
                    ? fixacoes.Average(fixacao => fixacao.DuracaoEmMilissegundos)
                    : double.NaN,

                QuantidadeDeObjetosObservados = objetos.Count,
                ObjetosMaisObservados = objetos
                    .Take(ParametrosDoResumoDoHmd.QuantidadeDeObjetosMaisObservados)
                    .ToList(),
            };
        }

        /// <summary>
        /// Reparte o tempo do participante entre os cenários que ele percorreu, do maior para o
        /// menor.
        /// </summary>
        /// <remarks>
        /// Não é detalhe descritivo: é o que explica a contribuição do indivíduo ao índice. Um
        /// participante pode ter muito tempo de RV e nenhuma fixação, se esse tempo estiver
        /// concentrado em cenários cujas sessões não alcançam a janela de detecção de 2–4 min.
        /// </remarks>
        private static List<PermanenciaEmCenario> DistribuirEntreCenarios(
            List<Sessao> sessoes, double tempoTotal)
        {
            return sessoes
                .Where(sessao => !string.IsNullOrWhiteSpace(sessao.RotuloDeCenario))
                .GroupBy(sessao => sessao.RotuloDeCenario, StringComparer.OrdinalIgnoreCase)
                .Select(grupo =>
                {
                    double tempo = grupo.Sum(CalcularDuracaoEmMinutos);

                    return new PermanenciaEmCenario
                    {
                        Cenario             = grupo.Key,
                        QuantidadeDeSessoes = grupo.Count(),
                        TempoMinutos        = tempo,
                        PercentualDoTempo   = tempoTotal > 0 ? 100.0 * tempo / tempoTotal : double.NaN,
                    };
                })
                .OrderByDescending(permanencia => permanencia.TempoMinutos)
                .ThenBy(permanencia => permanencia.Cenario, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Ordena os objetos da cena pela permanência do olhar, do maior para o menor.
        /// </summary>
        /// <remarks>
        /// A medida é a CONTAGEM DE AMOSTRAS em que o olhar incidiu sobre o objeto — proxy do
        /// tempo de permanência, já que a taxa de amostragem é constante.
        ///
        /// Não se usa a contagem de fixações, que seria a medida mais canônica: apenas 6 das 35
        /// sessões têm fixação alguma, porque a janela de detecção é de 2–4 min e 23 sessões duram
        /// menos de 2 min. Um ranking por fixação sairia vazio ou com um único objeto para a maior
        /// parte dos participantes, enquanto todas as 35 sessões têm objeto nomeado.
        ///
        /// Empates são desfeitos pelo nome, para que a lista não mude de ordem entre execuções.
        /// </remarks>
        private static List<ObjetoObservado> ClassificarObjetosPorPermanencia(
            List<LinhaDeSessao> amostras)
        {
            var nomes = amostras
                .Select(amostra => amostra.NomeDoObjeto)
                .Where(nome => !string.IsNullOrWhiteSpace(nome))
                .ToList();

            if (!nomes.Any())
                return new List<ObjetoObservado>();

            return nomes
                .GroupBy(nome => nome, StringComparer.OrdinalIgnoreCase)
                .Select(grupo => new ObjetoObservado
                {
                    Nome                 = grupo.Key,
                    QuantidadeDeAmostras = grupo.Count(),
                    PercentualDeAmostras = 100.0 * grupo.Count() / nomes.Count,
                })
                .OrderByDescending(objeto => objeto.QuantidadeDeAmostras)
                .ThenBy(objeto => objeto.Nome, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Duração de uma sessão: intervalo entre a primeira e a última amostra. Zero quando há
        /// menos de duas amostras — não há intervalo a medir.
        /// </summary>
        private static double CalcularDuracaoEmMinutos(Sessao sessao)
        {
            if (sessao.DadosBrutos.Count < 2)
                return 0.0;

            return (sessao.DadosBrutos[sessao.DadosBrutos.Count - 1].Timestamp
                  - sessao.DadosBrutos[0].Timestamp).TotalMinutes;
        }
    }
}
