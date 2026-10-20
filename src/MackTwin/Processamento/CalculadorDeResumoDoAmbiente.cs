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
    ///
    /// As leituras do ambiente já chegam ordenadas e consolidadas (<see cref="Ambiente.Leituras"/>),
    /// então o início da janela é achado por busca binária, sem percorrer a série.
    /// </remarks>
    public static class CalculadorDeResumoDoAmbiente
    {
        /// <summary>Janela padrão, de <see cref="ConfiguracaoDaPlanilhaGoogle.JanelaPadraoDaSerieEmHoras"/>.</summary>
        public static readonly TimeSpan JanelaPadrao =
            TimeSpan.FromHours(ConfiguracaoDaPlanilhaGoogle.JanelaPadraoDaSerieEmHoras);

        /// <summary>Resume a sala na janela informada (null = todas as leituras).</summary>
        public static ResumoDoAmbiente Resumir(Ambiente ambiente, TimeSpan? janela)
        {
            var ultima = ambiente.UltimaLeitura;
            var consideradas = FiltrarJanela(ambiente.Leituras, ultima, janela);

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

        /// <summary>
        /// Estatística de uma sequência de valores válidos. Sequência vazia produz NaN em tudo e
        /// classificação <see cref="ClassificacaoDeConforto.SemDados"/>.
        /// </summary>
        public static EstatisticaDaGrandeza Calcular(
            IEnumerable<double> valores,
            double ultimo,
            Func<double, ClassificacaoDeConforto> classificar)
        {
            int quantidade = 0;
            double soma = 0, minimo = double.PositiveInfinity, maximo = double.NegativeInfinity;

            foreach (double valor in valores)
            {
                if (double.IsNaN(valor))
                    continue;

                quantidade++;
                soma += valor;
                if (valor < minimo) minimo = valor;
                if (valor > maximo) maximo = valor;
            }

            if (quantidade == 0)
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
                Media = soma / quantidade,
                Minimo = minimo,
                Maximo = maximo,
                Quantidade = quantidade,
                Classificacao = classificar(ultimo),
            };
        }

        /// <summary>
        /// As leituras dentro da janela terminada na última leitura. Janela nula devolve a
        /// própria lista; janela que cobre tudo também — sem cópia.
        /// </summary>
        public static IReadOnlyList<Leitura> FiltrarJanela(IReadOnlyList<Leitura> leituras, Leitura? ultima, TimeSpan? janela)
        {
            if (ultima == null || leituras.Count == 0)
                return Array.Empty<Leitura>();

            if (janela == null)
                return leituras;

            DateTime inicio = ultima.Timestamp - janela.Value;
            int primeiro = PrimeiroIndiceAPartirDe(leituras, inicio);

            if (primeiro == 0)
                return leituras;

            var dentro = new List<Leitura>(leituras.Count - primeiro);

            for (int indice = primeiro; indice < leituras.Count; indice++)
                dentro.Add(leituras[indice]);

            return dentro;
        }

        /// <summary>Busca binária: índice da primeira leitura com carimbo ≥ <paramref name="instante"/>.</summary>
        private static int PrimeiroIndiceAPartirDe(IReadOnlyList<Leitura> leituras, DateTime instante)
        {
            int baixo = 0, alto = leituras.Count;

            while (baixo < alto)
            {
                int meio = baixo + (alto - baixo) / 2;

                if (leituras[meio].Timestamp < instante)
                    baixo = meio + 1;
                else
                    alto = meio;
            }

            return baixo;
        }

        private static double UltimoValor(IReadOnlyList<Leitura> leituras, Func<Leitura, bool> tem, Func<Leitura, double> valor)
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
