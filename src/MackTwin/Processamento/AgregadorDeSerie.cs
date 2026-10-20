using System;
using System.Collections.Generic;
using MackTwin.Dominio;

namespace MackTwin.Processamento
{
    /// <summary>Um balde de tempo da série agregada: média, mínimo e máximo de cada grandeza.</summary>
    public sealed class PontoAgregado
    {
        /// <summary>Início do balde (o instante que o ponto representa no eixo do tempo).</summary>
        public DateTime Inicio { get; init; }

        /// <summary>Quantas leituras caíram no balde.</summary>
        public int Quantidade { get; init; }

        public double TemperaturaMedia { get; init; } = double.NaN;
        public double TemperaturaMinima { get; init; } = double.NaN;
        public double TemperaturaMaxima { get; init; } = double.NaN;
        public double UmidadeMedia { get; init; } = double.NaN;
        public double UmidadeMinima { get; init; } = double.NaN;
        public double UmidadeMaxima { get; init; } = double.NaN;
    }

    /// <summary>
    /// Condensa uma série longa em baldes de tempo (por hora, por dia) com média, mínimo e
    /// máximo — para que semanas de leituras a cada 45 s virem um gráfico legível.
    /// </summary>
    /// <remarks>
    /// Reamostrar (pegar 1 ponto a cada N) esconde picos curtos; a agregação preserva-os na
    /// faixa mínimo–máximo que o gráfico sombreia atrás da média. O tamanho do balde é escolhido
    /// pela duração da janela (<see cref="BaldeRecomendado"/>): até 2 dias, pontos brutos; até
    /// 2 semanas, por hora; acima, por dia. Leituras ausentes (NaN) não entram na estatística.
    /// </remarks>
    public static class AgregadorDeSerie
    {
        private static readonly TimeSpan LimiteParaPontosBrutos = TimeSpan.FromDays(2);
        private static readonly TimeSpan LimiteParaBaldeDeHora = TimeSpan.FromDays(14);

        /// <summary>Balde adequado à duração: null (pontos brutos), uma hora ou um dia.</summary>
        public static TimeSpan? BaldeRecomendado(TimeSpan duracao)
        {
            if (duracao <= LimiteParaPontosBrutos)
                return null;

            return duracao <= LimiteParaBaldeDeHora ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
        }

        /// <summary>Agrega leituras já ordenadas em baldes de <paramref name="balde"/>; baldes sem leitura não aparecem.</summary>
        public static List<PontoAgregado> Agregar(IReadOnlyList<Leitura> leituras, TimeSpan balde)
        {
            if (balde <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(balde), "O balde precisa ser positivo.");

            var pontos = new List<PontoAgregado>();
            long ticksDoBalde = balde.Ticks;
            long baldeAtual = long.MinValue;
            var acumulador = new Acumulador();

            foreach (var leitura in leituras)
            {
                long chave = leitura.Timestamp.Ticks / ticksDoBalde;

                if (chave != baldeAtual)
                {
                    if (baldeAtual != long.MinValue)
                        pontos.Add(acumulador.Fechar(new DateTime(baldeAtual * ticksDoBalde)));

                    baldeAtual = chave;
                    acumulador = new Acumulador();
                }

                acumulador.Somar(leitura);
            }

            if (baldeAtual != long.MinValue)
                pontos.Add(acumulador.Fechar(new DateTime(baldeAtual * ticksDoBalde)));

            return pontos;
        }

        private sealed class Acumulador
        {
            private int _quantidade;
            private int _nTemperatura, _nUmidade;
            private double _somaT, _minT = double.PositiveInfinity, _maxT = double.NegativeInfinity;
            private double _somaU, _minU = double.PositiveInfinity, _maxU = double.NegativeInfinity;

            public void Somar(Leitura leitura)
            {
                _quantidade++;

                if (leitura.TemTemperatura)
                {
                    _nTemperatura++;
                    _somaT += leitura.Temperatura;
                    if (leitura.Temperatura < _minT) _minT = leitura.Temperatura;
                    if (leitura.Temperatura > _maxT) _maxT = leitura.Temperatura;
                }

                if (leitura.TemUmidade)
                {
                    _nUmidade++;
                    _somaU += leitura.Umidade;
                    if (leitura.Umidade < _minU) _minU = leitura.Umidade;
                    if (leitura.Umidade > _maxU) _maxU = leitura.Umidade;
                }
            }

            public PontoAgregado Fechar(DateTime inicio)
            {
                return new PontoAgregado
                {
                    Inicio = inicio,
                    Quantidade = _quantidade,
                    TemperaturaMedia = _nTemperatura == 0 ? double.NaN : _somaT / _nTemperatura,
                    TemperaturaMinima = _nTemperatura == 0 ? double.NaN : _minT,
                    TemperaturaMaxima = _nTemperatura == 0 ? double.NaN : _maxT,
                    UmidadeMedia = _nUmidade == 0 ? double.NaN : _somaU / _nUmidade,
                    UmidadeMinima = _nUmidade == 0 ? double.NaN : _minU,
                    UmidadeMaxima = _nUmidade == 0 ? double.NaN : _maxU,
                };
            }
        }
    }
}
