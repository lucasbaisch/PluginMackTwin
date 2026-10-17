using System;
using System.Collections.Generic;
using System.Linq;
using MackTwin.Configuracao;
using MackTwin.Dominio;

namespace MackTwin.Processamento
{
    /// <summary>
    /// Resume uma sala: última leitura, média/mínimo/máximo de cada grandeza numa janela de
    /// tempo e a classificação geral que define a cor.
    /// </summary>
    /// <remarks>
    /// A janela é contada <b>da última leitura para trás</b>, e não do relógio desta máquina:
    /// se o sensor ficou offline ontem, "as últimas 6 horas" ainda mostram as 6 horas finais em
    /// que ele falou, em vez de uma tela vazia. A data da última leitura, exibida ao lado, é o
    /// que denuncia o atraso.
    ///
    /// A classificação é pela ÚLTIMA leitura — é o estado atual da sala —, e a média existe para
    /// contextualizá-la, não para substituí-la.
    /// </remarks>
    public static class CalculadorDeResumoDoAmbiente
    {
        /// <summary>Janela padrão, de <see cref="ConfiguracaoDaPlanilhaGoogle.JanelaPadraoDaSerieEmHoras"/>.</summary>
        public static readonly TimeSpan JanelaPadrao =
            TimeSpan.FromHours(ConfiguracaoDaPlanilhaGoogle.JanelaPadraoDaSerieEmHoras);

        /// <summary>Resume a sala na janela informada (padrão: <see cref="JanelaPadrao"/>; null = tudo).</summary>
        public static ResumoDoAmbiente Resumir(Ambiente ambiente, TimeSpan? janela)
        {
            var todas = ambiente.Leituras.ToList();
            var ultima = todas.Count == 0 ? null : todas[todas.Count - 1];
            var consideradas = FiltrarJanela(todas, ultima, janela);

            var temperatura = Calcular(
                consideradas.Where(l => l.TemTemperatura).Select(l => l.Temperatura),
                UltimoValor(consideradas, l => l.TemTemperatura, l => l.Temperatura),
                ClassificadorDeConforto.ClassificarTemperatura);

            var umidade = Calcular(
                consideradas.Where(l => l.TemUmidade).Select(l => l.Umidade),
                UltimoValor(consideradas, l => l.TemUmidade, l => l.Umidade),
                ClassificadorDeConforto.ClassificarUmidade);

            return new ResumoDoAmbiente
            {
                Codigo = ambiente.Codigo,
                UltimaLeitura = ultima,
                Temperatura = temperatura,
                Umidade = umidade,
                ClassificacaoGeral = ClassificadorDeConforto.Combinar(temperatura.Classificacao, umidade.Classificacao),
                TotalDeLeituras = consideradas.Count,
                Inicio = consideradas.Count == 0 ? null : consideradas[0].Timestamp,
                Fim = consideradas.Count == 0 ? null : consideradas[consideradas.Count - 1].Timestamp,
                QuantidadeDeSensores = ambiente.Sensores.Count,
            };
        }

        /// <summary>Resume a sala na janela padrão.</summary>
        public static ResumoDoAmbiente Resumir(Ambiente ambiente) => Resumir(ambiente, JanelaPadrao);

        /// <summary>
        /// Estatística de uma sequência de valores válidos. Sequência vazia produz NaN em tudo e
        /// classificação <see cref="ClassificacaoDeConforto.SemDados"/>.
        /// </summary>
        public static EstatisticaDaGrandeza Calcular(
            IEnumerable<double> valores,
            double ultimo,
            Func<double, ClassificacaoDeConforto> classificar)
        {
            var lista = valores.Where(v => !double.IsNaN(v)).ToList();

            if (lista.Count == 0)
            {
                return new EstatisticaDaGrandeza
                {
                    Ultimo = double.NaN,
                    Classificacao = ClassificacaoDeConforto.SemDados,
                };
            }

            return new EstatisticaDaGrandeza
            {
                Ultimo = ultimo,
                Media = lista.Average(),
                Minimo = lista.Min(),
                Maximo = lista.Max(),
                Quantidade = lista.Count,
                Classificacao = classificar(ultimo),
            };
        }

        /// <summary>
        /// As leituras dentro da janela terminada na última leitura. Janela nula devolve tudo.
        /// </summary>
        public static List<Leitura> FiltrarJanela(IReadOnlyList<Leitura> leituras, Leitura? ultima, TimeSpan? janela)
        {
            if (ultima == null)
                return new List<Leitura>();

            if (janela == null)
                return leituras.ToList();

            DateTime inicio = ultima.Timestamp - janela.Value;
            return leituras.Where(l => l.Timestamp >= inicio).ToList();
        }

        private static double UltimoValor(List<Leitura> leituras, Func<Leitura, bool> tem, Func<Leitura, double> valor)
        {
            for (int indice = leituras.Count - 1; indice >= 0; indice--)
            {
                if (tem(leituras[indice]))
                    return valor(leituras[indice]);
            }

            return double.NaN;
        }
    }
}
