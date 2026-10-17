using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;
using PluginCAISM.Parsing;

namespace PluginCAISM.Interface.Controles
{
    /// <summary>
    /// Gráfico linear de uma sessão de coleta de cortisol: três pontos ligados por linha,
    /// nos horários "Ao acordar", "+30 min." e "Antes de dormir".
    /// </summary>
    /// <remarks>
    /// Exibido no formulário "Níveis de cortisol" da aba de Dados do Participante, um gráfico por
    /// sessão (seis no total). Usa LiveCharts.Wpf, a biblioteca de gráficos já declarada no
    /// projeto — nenhuma dependência nova.
    ///
    /// Medições ausentes ou não numéricas chegam como NaN e viram lacuna na linha, sem quebrar o
    /// gráfico.
    /// </remarks>
    public class ControleGraficoDeCortisol : UserControl
    {
        private static readonly Color ColBd    = Color.FromRgb(0xE4, 0xE7, 0xEC);
        private static readonly Color ColTx    = Color.FromRgb(0x1B, 0x22, 0x30);
        private static readonly Color ColTx3   = Color.FromRgb(0x7A, 0x83, 0x92);
        private static readonly Color ColLinha = Color.FromRgb(0x7A, 0x4F, 0xD9);

        /// <summary>Rótulos do eixo X, na ordem das colunas de cada sessão.</summary>
        private static readonly string[] RotulosDoEixoX =
        {
            "Ao acordar",
            "+30 min.",
            "Antes de dormir",
        };

        /// <summary>
        /// Inicializa o gráfico de uma sessão.
        /// </summary>
        /// <param name="sessao">Sessão de coleta com as três medições.</param>
        /// <param name="limiteSuperiorDoEixoY">
        /// Teto do eixo Y, compartilhado entre os seis gráficos do participante para que as
        /// sessões sejam visualmente comparáveis.
        /// </param>
        public ControleGraficoDeCortisol(SessaoDeColetaDeCortisol sessao, double limiteSuperiorDoEixoY)
        {
            // ATENÇÃO: precisa ser ChartValues<double>, nunca ChartValues<double?>.
            // O LiveCharts 0.9.7 não tem mapeador para tipos anuláveis e lança
            // LiveChartsException("does not know how to plot Nullable`1") ao montar a série —
            // mesmo que nenhum valor seja nulo. Como isso ocorre dentro do layout do WPF, a
            // exceção não é capturável aqui e derruba o processo hospedeiro (o Revit inteiro).
            // Medições ausentes são representadas por double.NaN, que o LiveCharts desenha como
            // lacuna na linha.
            var valores = new ChartValues<double>
            {
                sessao.AoAcordar,
                sessao.TrintaMinutos,
                sessao.AntesDeDormir,
            };

            var grafico = new CartesianChart
            {
                Height = 190,
                DisableAnimations = true,
                Hoverable = true,
                LegendLocation = LegendLocation.None,
                Series = new SeriesCollection
                {
                    new LineSeries
                    {
                        Title = "Cortisol",
                        Values = valores,
                        Stroke = new SolidColorBrush(ColLinha),
                        StrokeThickness = 2,
                        Fill = Brushes.Transparent,
                        PointGeometrySize = 8,
                        LineSmoothness = 0,
                    }
                },
            };

            grafico.AxisX.Add(new Axis
            {
                Labels = RotulosDoEixoX,
                Foreground = new SolidColorBrush(ColTx3),
                Separator = new LiveCharts.Wpf.Separator { Step = 1, IsEnabled = false },
            });

            grafico.AxisY.Add(new Axis
            {
                Title = "ng/ml",
                MinValue = 0,
                MaxValue = limiteSuperiorDoEixoY,
                Foreground = new SolidColorBrush(ColTx3),
                Separator = new LiveCharts.Wpf.Separator
                {
                    Stroke = new SolidColorBrush(ColBd),
                    StrokeThickness = 1,
                },
            });

            Content = ConstruirCartao(sessao, grafico);
            Margin = new Thickness(0, 0, 10, 10);
        }

        private UIElement ConstruirCartao(SessaoDeColetaDeCortisol sessao, CartesianChart grafico)
        {
            var conteudo = new StackPanel();

            conteudo.Children.Add(new TextBlock
            {
                Text = sessao.Titulo,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ColTx),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            });

            int ausentes = new[] { sessao.AoAcordar, sessao.TrintaMinutos, sessao.AntesDeDormir }
                .Count(double.IsNaN);

            if (ausentes > 0)
            {
                conteudo.Children.Add(new TextBlock
                {
                    Text = $"{ausentes} medição(ões) sem valor",
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(ColTx3),
                    Margin = new Thickness(0, 0, 0, 4),
                });
            }

            conteudo.Children.Add(grafico);

            return new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(ColBd),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Child = conteudo,
            };
        }
    }
}
