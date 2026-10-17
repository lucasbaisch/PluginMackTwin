using System;
using System.Collections.Generic;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Processamento
{
    /// <summary>
    /// Reduz as pontuações de potencial restaurador — calculadas POR OBJETO — a um valor único
    /// por participante (PH), exigido pelo eixo Y dos gráficos de dispersão da §5.5.1, em que
    /// cada ponto representa um indivíduo.
    /// </summary>
    /// <remarks>
    /// Calculadora pura: recebe as sessões, delega o cálculo por objeto ao
    /// <see cref="CalculadorDeMetricasPorObjeto"/> e agrega o resultado pela regra configurada em
    /// <see cref="AgregacaoDePotencialPorParticipante"/>.
    ///
    /// O valor resultante herda a escala de
    /// <see cref="MetricasPorObjeto.PotencialHomeodinamico"/>, isto é, (-1, +1): positivo
    /// indica potencial restaurador, negativo indica potencial estressor.
    /// </remarks>
    // A PERMANÊNCIA desta classe está decidida pelo TextoFinal de 31/08/2026: o §5.6.4 a descreve
    // nominalmente — "reduz as pontuações por objeto a um PH único por participante ... reutiliza
    // a calculadora de métricas sobre o grupo de sessões de cada indivíduo e agrega o resultado".
    // Ela está sem consumidor na interface desde a remoção do filtro de classificação da aba de
    // Dados do Grupo, mas sai do rol de código a remover: é parte declarada do artefato. A
    // correspondência de identificadores que bloqueava seu uso foi resolvida nos dados
    // definitivos (cenario_*.csv), cujos IDs cruzam 100% com formulários e cortisol.

    // Regra de agregação ADOTADA: a de AgregacaoDePotencialPorParticipante.RegraPadrao (média
    // simples), sobre todos os cenários do participante — o §5.6.4 manda agregar por indivíduo
    // sem especificar a regra nem restringir o escopo a um cenário. Se o texto vier a
    // especificar, os dois pontos de ajuste são a RegraPadrao e o agrupamento em Calcular.
    //
    // ESTIMAÇÃO GLOBAL, decidida pelo autor em 31/08/2026. A versão anterior chamava o
    // calculador de métricas uma vez POR PARTICIPANTE, de modo que a Camada 1 inteira rodava
    // sobre a amostra de um único indivíduo: μ_i era estimado contra a média do próprio
    // participante, τ̂_ν media a dispersão dos efeitos apenas dentro dele, e o PH agregado saía
    // como medida RELATIVA INTRA-INDIVÍDUO, não comparável entre participantes — limitação
    // registrada como relevante para a validade das Figuras 54–56. Agora a Camada 1 roda UMA
    // única vez sobre todas as sessões de todos os participantes — onde a centragem por
    // indivíduo (μ_i) faz o trabalho que lhe cabe — e cada participante recebe a agregação dos
    // PH_e GLOBAIS dos objetos que ele próprio observou. Dois participantes que olharam os
    // mesmos objetos com os mesmos pesos têm PH comparável na mesma escala.
    public class CalculadorDePotencialPorParticipante
    {
        private readonly CalculadorDeMetricasPorObjeto _calculadorDeMetricas;

        /// <summary>
        /// Inicializa o calculador com a calculadora de métricas por objeto a ser reutilizada.
        /// </summary>
        /// <param name="calculadorDeMetricas">Calculadora de métricas por objeto.</param>
        public CalculadorDePotencialPorParticipante(CalculadorDeMetricasPorObjeto calculadorDeMetricas)
        {
            _calculadorDeMetricas = calculadorDeMetricas;
        }

        /// <summary>
        /// Calcula o PH de cada participante presente nas sessões fornecidas.
        /// </summary>
        /// <param name="sessoes">Sessões a considerar.</param>
        /// <param name="regra">
        /// Regra de agregação a aplicar. Quando omitida, usa
        /// <see cref="AgregacaoDePotencialPorParticipante.RegraPadrao"/>.
        /// </param>
        /// <returns>
        /// Dicionário identificação do participante → PH em (-1, +1). Participantes cujos objetos
        /// observados não tiverem nenhuma métrica global com PH estimado ficam de fora do
        /// dicionário.
        /// </returns>
        /// <remarks>
        /// A Camada 1 roda UMA única vez, sobre todas as sessões recebidas — inclusive as sem
        /// identificação de participante, que informam o modelo global mesmo sem receber PH
        /// próprio. Cada participante herda os PH_e globais dos objetos que observou; objetos
        /// sem estimativa (PH_e = NaN) não entram na agregação.
        /// </remarks>
        public Dictionary<string, double> Calcular(
            IEnumerable<Sessao> sessoes,
            RegraDeAgregacaoDePotencial? regra = null)
        {
            var regraEfetiva = regra ?? AgregacaoDePotencialPorParticipante.RegraPadrao;
            var potenciaisPorParticipante = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            var sessoesMaterializadas = sessoes?.ToList() ?? new List<Sessao>();

            var metricasGlobais = _calculadorDeMetricas.Calcular(sessoesMaterializadas);

            if (metricasGlobais.Count == 0)
                return potenciaisPorParticipante;

            var metricasPorNome = new Dictionary<string, MetricasPorObjeto>(StringComparer.OrdinalIgnoreCase);

            foreach (var metrica in metricasGlobais)
            {
                if (!metricasPorNome.ContainsKey(metrica.NomeDoObjeto))
                    metricasPorNome[metrica.NomeDoObjeto] = metrica;
            }

            var sessoesPorParticipante = sessoesMaterializadas
                .Where(sessao => !string.IsNullOrWhiteSpace(sessao.IdentificacaoManual))
                .GroupBy(sessao => sessao.IdentificacaoManual, StringComparer.OrdinalIgnoreCase);

            foreach (var grupo in sessoesPorParticipante)
            {
                var pesosPorObjeto = ContarFixacoesPorObjeto(grupo);

                var metricasDoParticipante = pesosPorObjeto.Keys
                    .Where(metricasPorNome.ContainsKey)
                    .Select(nome => metricasPorNome[nome])
                    .Where(metrica => !double.IsNaN(metrica.PotencialHomeodinamico))
                    .ToList();

                if (metricasDoParticipante.Count == 0)
                    continue;

                potenciaisPorParticipante[grupo.Key] = Agregar(
                    metricasDoParticipante, pesosPorObjeto, regraEfetiva);
            }

            return potenciaisPorParticipante;
        }

        private double Agregar(
            List<MetricasPorObjeto> metricas,
            Dictionary<string, int> pesosPorObjeto,
            RegraDeAgregacaoDePotencial regra)
        {
            switch (regra)
            {
                case RegraDeAgregacaoDePotencial.MedianaDasPontuacoesPorObjeto:
                    return CalcularMediana(metricas.Select(m => m.PotencialHomeodinamico));

                case RegraDeAgregacaoDePotencial.MediaPonderadaPorFixacoes:
                    return CalcularMediaPonderada(metricas, pesosPorObjeto);

                case RegraDeAgregacaoDePotencial.MediaDasPontuacoesPorObjeto:
                default:
                    return metricas.Average(m => m.PotencialHomeodinamico);
            }
        }

        /// <summary>
        /// Conta quantas fixações incidiram em cada objeto, para a ponderação da regra
        /// <see cref="RegraDeAgregacaoDePotencial.MediaPonderadaPorFixacoes"/>.
        /// </summary>
        private Dictionary<string, int> ContarFixacoesPorObjeto(IEnumerable<Sessao> sessoes)
        {
            var contagem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var sessao in sessoes)
            {
                foreach (var fixacao in sessao.Fixacoes)
                {
                    foreach (var nomeDoObjeto in fixacao.NomesDeObjetos)
                    {
                        contagem.TryGetValue(nomeDoObjeto, out int atual);
                        contagem[nomeDoObjeto] = atual + 1;
                    }
                }
            }

            return contagem;
        }

        private double CalcularMediaPonderada(
            List<MetricasPorObjeto> metricas,
            Dictionary<string, int> pesosPorObjeto)
        {
            double somaPonderada = 0.0;
            double somaDosPesos = 0.0;

            foreach (var metrica in metricas)
            {
                pesosPorObjeto.TryGetValue(metrica.NomeDoObjeto, out int peso);

                if (peso <= 0)
                    continue;

                somaPonderada += metrica.PotencialHomeodinamico * peso;
                somaDosPesos += peso;
            }

            if (somaDosPesos <= 0.0)
                return metricas.Average(m => m.PotencialHomeodinamico);

            return somaPonderada / somaDosPesos;
        }

        private static double CalcularMediana(IEnumerable<double> valores)
        {
            var ordenados = valores.Where(valor => !double.IsNaN(valor)).OrderBy(valor => valor).ToList();

            if (ordenados.Count == 0)
                return double.NaN;

            int meio = ordenados.Count / 2;

            if (ordenados.Count % 2 == 1)
                return ordenados[meio];

            return (ordenados[meio - 1] + ordenados[meio]) / 2.0;
        }
    }
}
