using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;
using MackTwin.Configuracao;
using MackTwin.Dominio;

namespace MackTwin.Interface.Controles
{
    /// <summary>
    /// Gráfico de linha da temperatura e da umidade de uma sala ao longo do tempo, com as
    /// faixas de conforto sombreadas atrás das curvas.
    /// </summary>
    /// <remarks>
    /// Dois eixos Y — °C à esquerda, % à direita — porque as grandezas têm escalas
    /// incomparáveis e forçá-las no mesmo eixo achataria uma delas. As faixas são
    /// <see cref="AxisSection"/> de cada eixo: a leitura "está dentro ou fora" fica visual,
    /// sem precisar ler números.
    ///
    /// Usa LiveCharts.Wpf 0.9.7, a biblioteca já declarada no projeto. Dois cuidados herdados:
    /// valores em <c>ChartValues&lt;double&gt;</c> (nunca anuláveis — a biblioteca não sabe plotar
    /// <c>Nullable</c> e a exceção derruba o Revit inteiro), com NaN para lacuna; e séries longas
    /// são reamostradas para no máximo <see cref="MaximoDePontos"/> pontos, porque a planilha
    /// cresce ~2 mil linhas por dia e o WPF não precisa desenhar todas.
    /// </remarks>
    public sealed class ControleGraficoDeSerieTemporal : UserControl
    {
        /// <summary>Teto de pontos desenhados por série.</summary>
        public const int MaximoDePontos = 600;

        private static readonly Color CorTemperatura = Color.FromRgb(0xB3, 0x38, 0x2F);
        private static readonly Color CorUmidade = Color.FromRgb(0x2F, 0x6B, 0xD9);
        private static readonly Color CorFaixa = Color.FromArgb(0x33, 0x1B, 0x7A, 0x50);
        private static readonly Color CorTexto = Color.FromRgb(0x7A, 0x83, 0x92);
        private static readonly Color CorGrade = Color.FromRgb(0xE4, 0xE7, 0xEC);

        /// <summary>Monta o gráfico das leituras informadas (já em ordem cronológica).</summary>
        public ControleGraficoDeSerieTemporal(IReadOnlyList<Leitura> leituras)
        {
            if (leituras.Count == 0)
            {
                Content = new TextBlock
                {
                    Text = "Sem leituras na janela escolhida.",
                    Foreground = new SolidColorBrush(CorTexto),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13,
                };
                return;
            }

            var amostra = Reamostrar(leituras);
            bool maisDeUmDia = amostra[amostra.Count - 1].Timestamp - amostra[0].Timestamp > TimeSpan.FromDays(1);
            string formatoDoRotulo = maisDeUmDia ? "dd/MM HH:mm" : "HH:mm";

            var rotulos = amostra
                .Select(l => l.Timestamp.ToString(formatoDoRotulo, CultureInfo.CurrentCulture))
                .ToArray();

            var temperaturas = new ChartValues<double>(amostra.Select(l => l.Temperatura));
            var umidades = new ChartValues<double>(amostra.Select(l => l.Umidade));

            var grafico = new CartesianChart
            {
                DisableAnimations = true,
                Hoverable = true,
                LegendLocation = LegendLocation.Bottom,
                Series = new SeriesCollection
                {
                    new LineSeries
                    {
                        Title = "Temperatura (°C)",
                        Values = temperaturas,
                        Stroke = new SolidColorBrush(CorTemperatura),
                        StrokeThickness = 2,
                        Fill = Brushes.Transparent,
                        PointGeometry = null,
                        LineSmoothness = 0,
                        ScalesYAt = 0,
                    },
                    new LineSeries
                    {
                        Title = "Umidade (%)",
                        Values = umidades,
                        Stroke = new SolidColorBrush(CorUmidade),
                        StrokeThickness = 2,
                        Fill = Brushes.Transparent,
                        PointGeometry = null,
                        LineSmoothness = 0,
                        ScalesYAt = 1,
                    },
                },
            };

            grafico.AxisX.Add(new Axis
            {
                Labels = rotulos,
                Foreground = new SolidColorBrush(CorTexto),
                Separator = new LiveCharts.Wpf.Separator
                {
                    Step = Math.Max(1, rotulos.Length / 8),
                    Stroke = new SolidColorBrush(CorGrade),
                    StrokeThickness = 1,
                },
            });

            grafico.AxisY.Add(MontarEixo(
                "°C",
                temperaturas,
                FaixasDeConfortoAmbiental.TemperaturaMinima,
                FaixasDeConfortoAmbiental.TemperaturaMaxima,
                folga: 1.0,
                AxisPosition.LeftBottom));

            grafico.AxisY.Add(MontarEixo(
                "%",
                umidades,
                FaixasDeConfortoAmbiental.UmidadeMinima,
                FaixasDeConfortoAmbiental.UmidadeMaxima,
                folga: 5.0,
                AxisPosition.RightTop));

            Content = grafico;
        }

        /// <summary>
        /// Eixo com a faixa de conforto sombreada e limites que garantem a faixa visível mesmo
        /// quando todas as leituras caem dentro dela.
        /// </summary>
        private static Axis MontarEixo(
            string titulo, ChartValues<double> valores, double minimoDaFaixa, double maximoDaFaixa,
            double folga, AxisPosition posicao)
        {
            var validos = valores.Where(v => !double.IsNaN(v)).ToList();
            double minimo = validos.Count == 0 ? minimoDaFaixa : Math.Min(validos.Min(), minimoDaFaixa);
            double maximo = validos.Count == 0 ? maximoDaFaixa : Math.Max(validos.Max(), maximoDaFaixa);

            return new Axis
            {
                Title = titulo,
                Position = posicao,
                MinValue = Math.Floor(minimo - folga),
                MaxValue = Math.Ceiling(maximo + folga),
                Foreground = new SolidColorBrush(CorTexto),
                Separator = new LiveCharts.Wpf.Separator
                {
                    Stroke = new SolidColorBrush(CorGrade),
                    StrokeThickness = 1,
                },
                Sections = new SectionsCollection
                {
                    new AxisSection
                    {
                        Value = minimoDaFaixa,
                        SectionWidth = maximoDaFaixa - minimoDaFaixa,
                        Fill = new SolidColorBrush(CorFaixa),
                    },
                },
            };
        }

        /// <summary>Reduz a série a no máximo <see cref="MaximoDePontos"/> pontos, mantendo o último.</summary>
        private static List<Leitura> Reamostrar(IReadOnlyList<Leitura> leituras)
        {
            if (leituras.Count <= MaximoDePontos)
                return leituras.ToList();

            int passo = (int)Math.Ceiling(leituras.Count / (double)MaximoDePontos);
            var amostra = new List<Leitura>(MaximoDePontos + 1);

            for (int indice = 0; indice < leituras.Count; indice += passo)
                amostra.Add(leituras[indice]);

            if (amostra[amostra.Count - 1] != leituras[leituras.Count - 1])
                amostra.Add(leituras[leituras.Count - 1]);

            return amostra;
        }
    }
}
