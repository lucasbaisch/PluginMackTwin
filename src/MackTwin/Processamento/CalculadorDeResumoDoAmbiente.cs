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
    /// A janela é contada <b>do instante de referência para trás</b> — o relógio de agora, na
    /// interface. "24 h" são as últimas 24 horas reais: um sensor parado há dois dias fica sem
    /// leituras na janela e a sala vira "sem dados", que é a informação de monitoramento
    /// correta; a data da última leitura, exibida no cartão, denuncia o atraso.
    ///
    /// <see cref="ResumirEm"/> faz o mesmo para um instante do PASSADO — é o que cada quadro do
    /// vídeo do último dia usa: o estado que a sala tinha às 14 h é o da última leitura até
    /// 14 h, desde que não mais velha que a tolerância.
    ///
    /// A classificação é pela ÚLTIMA leitura dentro da janela — é o estado atual da sala —, e a
    /// média existe para contextualizá-la, não para substituí-la.
    ///
    /// As leituras do ambiente já chegam ordenadas e consolidadas (<see cref="Ambiente.Leituras"/>),
    /// então os limites da janela são achados por busca binária, sem percorrer a série.
    /// </remarks>
    public static class CalculadorDeResumoDoAmbiente
    {
        /// <summary>Janela padrão, de <see cref="ConfiguracaoDaPlanilhaGoogle.JanelaPadraoDaSerieEmHoras"/>.</summary>
        public static readonly TimeSpan JanelaPadrao =
            TimeSpan.FromHours(ConfiguracaoDaPlanilhaGoogle.JanelaPadraoDaSerieEmHoras);

        /// <summary>Resume a sala na janela informada (null = todas as leituras), contada de <paramref name="agora"/> para trás.</summary>
        public static ResumoDoAmbiente Resumir(Ambiente ambiente, TimeSpan? janela, DateTime agora)
        {
            return ResumirConsideradas(ambiente, FiltrarJanela(ambiente.Leituras, janela, agora));
        }

        /// <summary>
        /// O estado da sala num instante: só as leituras em [instante − tolerância, instante]
        /// contam, e a classificação é a da última delas.
        /// </summary>
        public static ResumoDoAmbiente ResumirEm(Ambiente ambiente, DateTime instante, TimeSpan tolerancia)
        {
            return ResumirConsideradas(ambiente, FiltrarIntervalo(ambiente.Leituras, instante - tolerancia, instante));
        }

        private static ResumoDoAmbiente ResumirConsideradas(Ambiente ambiente, IReadOnlyList<Leitura> consideradas)
        {
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
                UltimaLeitura = ambiente.UltimaLeitura,
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
        /// As leituras com carimbo em [<paramref name="agora"/> − janela, +∞). Janela nula devolve
        /// a própria lista; janela que cobre tudo também — sem cópia.
        /// </summary>
        public static IReadOnlyList<Leitura> FiltrarJanela(IReadOnlyList<Leitura> leituras, TimeSpan? janela, DateTime agora)
        {
            if (leituras.Count == 0)
                return Array.Empty<Leitura>();

            if (janela == null)
                return leituras;

            return Fatiar(leituras, PrimeiroIndiceAPartirDe(leituras, agora - janela.Value), leituras.Count);
        }

        /// <summary>As leituras com carimbo em [<paramref name="inicio"/>, <paramref name="fim"/>].</summary>
        public static IReadOnlyList<Leitura> FiltrarIntervalo(IReadOnlyList<Leitura> leituras, DateTime inicio, DateTime fim)
        {
            if (leituras.Count == 0 || fim < inicio)
                return Array.Empty<Leitura>();

            int primeiro = PrimeiroIndiceAPartirDe(leituras, inicio);
            int depoisDoUltimo = PrimeiroIndiceDepoisDe(leituras, fim);

            return Fatiar(leituras, primeiro, depoisDoUltimo);
        }

        private static IReadOnlyList<Leitura> Fatiar(IReadOnlyList<Leitura> leituras, int primeiro, int depoisDoUltimo)
        {
            if (primeiro >= depoisDoUltimo)
                return Array.Empty<Leitura>();

            if (primeiro == 0 && depoisDoUltimo == leituras.Count)
                return leituras;

            var fatia = new List<Leitura>(depoisDoUltimo - primeiro);

            for (int indice = primeiro; indice < depoisDoUltimo; indice++)
                fatia.Add(leituras[indice]);

            return fatia;
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

        /// <summary>Busca binária: índice da primeira leitura com carimbo &gt; <paramref name="instante"/>.</summary>
        private static int PrimeiroIndiceDepoisDe(IReadOnlyList<Leitura> leituras, DateTime instante)
        {
            int baixo = 0, alto = leituras.Count;

            while (baixo < alto)
            {
                int meio = baixo + (alto - baixo) / 2;

                if (leituras[meio].Timestamp <= instante)
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
