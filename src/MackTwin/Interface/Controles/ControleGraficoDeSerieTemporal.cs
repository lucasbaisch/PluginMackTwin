using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MackTwin.Configuracao;
using MackTwin.Dominio;

namespace MackTwin.Interface.Controles
{
    /// <summary>
    /// Série temporal de uma sala em dois painéis empilhados — temperatura e umidade —, cada um
    /// com a sua faixa de conforto sombreada, desenhados com WPF puro.
    /// </summary>
    /// <remarks>
    /// <b>Por que não uma biblioteca de gráficos.</b> O LiveCharts 0.9.7 lançou uma exceção
    /// dentro do layout do WPF e derrubou o Revit inteiro: o que acontece no layout não é
    /// capturável pelo add-in. Um <see cref="Canvas"/> com <see cref="Polyline"/> faz tudo o
    /// que este painel precisa, está sob controle completo e não traz dependência.
    ///
    /// <b>Dois painéis, e não dois eixos.</b> Temperatura e umidade têm escalas incomparáveis;
    /// num eixo duplo a leitura "está dentro ou fora da faixa" fica ambígua. Empilhados, cada
    /// painel tem a sua faixa e o eixo do tempo é o mesmo.
    ///
    /// O desenho acontece em <c>SizeChanged</c>, porque o Canvas só conhece o seu tamanho depois
    /// do layout. Lacunas (NaN) interrompem a linha em vez de puxá-la para zero.
    /// </remarks>
    public sealed class ControleGraficoDeSerieTemporal : UserControl
    {
        /// <summary>Teto de pontos desenhados por série; acima disso a série é reamostrada.</summary>
        public const int MaximoDePontos = 1500;

        private const double MargemEsquerda = 48;
        private const double MargemDireita = 16;
        private const double MargemSuperior = 10;
        private const double MargemInferior = 24;

        private static readonly Color CorTemperatura = Color.FromRgb(0xB3, 0x38, 0x2F);
        private static readonly Color CorUmidade = Color.FromRgb(0x2F, 0x6B, 0xD9);
        private static readonly Color CorFaixa = Color.FromArgb(0x2E, 0x1B, 0x7A, 0x50);
        private static readonly Color CorGrade = Color.FromRgb(0xEC, 0xEC, 0xF0);
        private static readonly Color CorEixo = Color.FromRgb(0xD9, 0xD9, 0xDE);
        private static readonly Color CorTexto = Color.FromRgb(0x8A, 0x8A, 0x94);
        private static readonly Color CorBorda = Color.FromRgb(0xE6, 0xE6, 0xEA);

        private readonly List<Leitura> _leituras;

        /// <summary>Monta o gráfico das leituras informadas (já em ordem cronológica).</summary>
        public ControleGraficoDeSerieTemporal(IReadOnlyList<Leitura> leituras)
        {
            _leituras = Reamostrar(leituras);

            if (_leituras.Count == 0)
            {
                Content = new TextBlock
                {
                    Text = "Sem leituras na janela escolhida.",
                    Foreground = Pincel(CorTexto),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13,
                };
                return;
            }

            var grade = new Grid();
            grade.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grade.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var painelTemperatura = MontarPainel(
                "Temperatura (°C)",
                leitura => leitura.Temperatura,
                FaixasDeConfortoAmbiental.TemperaturaMinima,
                FaixasDeConfortoAmbiental.TemperaturaMaxima,
                CorTemperatura, "0.0", "°C");

            var painelUmidade = MontarPainel(
                "Umidade relativa (%)",
                leitura => leitura.Umidade,
                FaixasDeConfortoAmbiental.UmidadeMinima,
                FaixasDeConfortoAmbiental.UmidadeMaxima,
                CorUmidade, "0", "%");

            Grid.SetRow(painelTemperatura, 0);
            Grid.SetRow(painelUmidade, 1);
            painelUmidade.Margin = new Thickness(0, 10, 0, 0);

            grade.Children.Add(painelTemperatura);
            grade.Children.Add(painelUmidade);
            Content = grade;
        }

        // ── Painel de uma grandeza ───────────────────────────────────────

        private Border MontarPainel(
            string titulo, Func<Leitura, double> valor, double faixaMinima, double faixaMaxima,
            Color cor, string formato, string unidade)
        {
            var canvas = new Canvas { ClipToBounds = true, Background = Brushes.Transparent, MinHeight = 110 };

            var cabecalho = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

            cabecalho.Children.Add(new TextBlock
            {
                Text = titulo,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Pincel(cor),
            });

            var faixa = new TextBlock
            {
                Text = string.Format(CultureInfo.CurrentCulture, "faixa de conforto {0:0}–{1:0} {2}", faixaMinima, faixaMaxima, unidade),
                FontSize = 10.5,
                Foreground = Pincel(CorTexto),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            DockPanel.SetDock(faixa, Dock.Right);
            cabecalho.Children.Insert(0, faixa);

            var conteudo = new DockPanel();
            DockPanel.SetDock(cabecalho, Dock.Top);
            conteudo.Children.Add(cabecalho);
            conteudo.Children.Add(canvas);

            canvas.SizeChanged += (remetente, evento) =>
                Desenhar(canvas, valor, faixaMinima, faixaMaxima, cor, formato, unidade);

            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Pincel(CorBorda),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 8),
                Child = conteudo,
            };
        }

        private void Desenhar(
            Canvas canvas, Func<Leitura, double> valor, double faixaMinima, double faixaMaxima,
            Color cor, string formato, string unidade)
        {
            canvas.Children.Clear();

            double largura = canvas.ActualWidth;
            double altura = canvas.ActualHeight;

            if (largura < 80 || altura < 50)
                return;

            double x0 = MargemEsquerda, x1 = largura - MargemDireita;
            double y0 = MargemSuperior, y1 = altura - MargemInferior;

            if (x1 <= x0 || y1 <= y0)
                return;

            // ── escalas ──
            var valores = _leituras.Select(valor).Where(v => !double.IsNaN(v)).ToList();
            double valorMinimo = valores.Count == 0 ? faixaMinima : Math.Min(valores.Min(), faixaMinima);
            double valorMaximo = valores.Count == 0 ? faixaMaxima : Math.Max(valores.Max(), faixaMaxima);
            double folga = Math.Max((valorMaximo - valorMinimo) * 0.08, 0.5);
            valorMinimo -= folga;
            valorMaximo += folga;

            DateTime tempoMinimo = _leituras[0].Timestamp;
            DateTime tempoMaximo = _leituras[_leituras.Count - 1].Timestamp;
            double duracao = Math.Max((tempoMaximo - tempoMinimo).TotalSeconds, 1.0);

            double Y(double v) => y1 - (v - valorMinimo) / (valorMaximo - valorMinimo) * (y1 - y0);
            double X(DateTime t) => x0 + (t - tempoMinimo).TotalSeconds / duracao * (x1 - x0);

            // ── faixa de conforto ──
            double topoDaFaixa = Y(faixaMaxima);
            double baseDaFaixa = Y(faixaMinima);

            var faixa = new Rectangle
            {
                Width = x1 - x0,
                Height = Math.Max(baseDaFaixa - topoDaFaixa, 1),
                Fill = Pincel(CorFaixa),
            };
            Canvas.SetLeft(faixa, x0);
            Canvas.SetTop(faixa, topoDaFaixa);
            canvas.Children.Add(faixa);

            // ── grade horizontal e rótulos do eixo Y ──
            const int divisoes = 4;

            for (int indice = 0; indice <= divisoes; indice++)
            {
                double v = valorMinimo + (valorMaximo - valorMinimo) * indice / divisoes;
                double y = Y(v);

                canvas.Children.Add(new Line
                {
                    X1 = x0, X2 = x1, Y1 = y, Y2 = y,
                    Stroke = Pincel(CorGrade), StrokeThickness = 1,
                });

                var rotulo = new TextBlock
                {
                    Text = v.ToString(formato, CultureInfo.CurrentCulture),
                    FontSize = 10,
                    Foreground = Pincel(CorTexto),
                    Width = MargemEsquerda - 8,
                    TextAlignment = TextAlignment.Right,
                };
                Canvas.SetLeft(rotulo, 0);
                Canvas.SetTop(rotulo, y - 7);
                canvas.Children.Add(rotulo);
            }

            // ── eixo do tempo ──
            canvas.Children.Add(new Line
            {
                X1 = x0, X2 = x1, Y1 = y1, Y2 = y1,
                Stroke = Pincel(CorEixo), StrokeThickness = 1,
            });

            bool maisDeUmDia = tempoMaximo - tempoMinimo > TimeSpan.FromDays(1);
            string formatoDoTempo = maisDeUmDia ? "dd/MM HH:mm" : "HH:mm";
            int rotulosDeTempo = Math.Max(2, Math.Min(7, (int)((x1 - x0) / 90)));

            for (int indice = 0; indice <= rotulosDeTempo; indice++)
            {
                var t = tempoMinimo.AddSeconds(duracao * indice / rotulosDeTempo);
                double x = X(t);

                canvas.Children.Add(new Line
                {
                    X1 = x, X2 = x, Y1 = y1, Y2 = y1 + 3,
                    Stroke = Pincel(CorEixo), StrokeThickness = 1,
                });

                var rotulo = new TextBlock
                {
                    Text = t.ToString(formatoDoTempo, CultureInfo.CurrentCulture),
                    FontSize = 10,
                    Foreground = Pincel(CorTexto),
                    Width = 72,
                    TextAlignment = TextAlignment.Center,
                };
                Canvas.SetLeft(rotulo, x - 36);
                Canvas.SetTop(rotulo, y1 + 5);
                canvas.Children.Add(rotulo);
            }

            // ── linha, interrompida nas lacunas ──
            var segmento = new PointCollection();

            void Despejar()
            {
                if (segmento.Count >= 2)
                {
                    canvas.Children.Add(new Polyline
                    {
                        Points = segmento,
                        Stroke = Pincel(cor),
                        StrokeThickness = 2,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                    });
                }
                else if (segmento.Count == 1)
                {
                    AdicionarPonto(canvas, segmento[0], 2.5, cor);
                }

                segmento = new PointCollection();
            }

            Point? ultimoPonto = null;
            double ultimoValor = double.NaN;

            foreach (var leitura in _leituras)
            {
                double v = valor(leitura);

                if (double.IsNaN(v))
                {
                    Despejar();
                    continue;
                }

                var ponto = new Point(X(leitura.Timestamp), Y(v));
                segmento.Add(ponto);
                ultimoPonto = ponto;
                ultimoValor = v;
            }

            Despejar();

            // ── último valor em destaque ──
            if (ultimoPonto.HasValue)
            {
                AdicionarPonto(canvas, ultimoPonto.Value, 4, cor);

                var destaque = new TextBlock
                {
                    Text = string.Format(CultureInfo.CurrentCulture, "{0:" + formato + "} {1}", ultimoValor, unidade),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Pincel(cor),
                    Background = Brushes.White,
                    Padding = new Thickness(3, 0, 3, 0),
                };

                double larguraEstimada = 8 * destaque.Text.Length;
                bool pertoDaBorda = ultimoPonto.Value.X + larguraEstimada + 10 > x1;

                Canvas.SetLeft(destaque, pertoDaBorda ? ultimoPonto.Value.X - larguraEstimada - 8 : ultimoPonto.Value.X + 8);
                Canvas.SetTop(destaque, Math.Max(y0, ultimoPonto.Value.Y - 18));
                canvas.Children.Add(destaque);
            }
        }

        private static void AdicionarPonto(Canvas canvas, Point centro, double raio, Color cor)
        {
            var ponto = new Ellipse
            {
                Width = raio * 2,
                Height = raio * 2,
                Fill = Pincel(cor),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
            };
            Canvas.SetLeft(ponto, centro.X - raio);
            Canvas.SetTop(ponto, centro.Y - raio);
            canvas.Children.Add(ponto);
        }

        // ── Utilitários ──────────────────────────────────────────────────

        /// <summary>Reduz a série a no máximo <see cref="MaximoDePontos"/> pontos, mantendo o último.</summary>
        private static List<Leitura> Reamostrar(IReadOnlyList<Leitura> leituras)
        {
            if (leituras.Count <= MaximoDePontos)
                return leituras.ToList();

            int passo = (int)Math.Ceiling(leituras.Count / (double)MaximoDePontos);
            var amostra = new List<Leitura>(MaximoDePontos + 1);

            for (int indice = 0; indice < leituras.Count; indice += passo)
                amostra.Add(leituras[indice]);

            if (!ReferenceEquals(amostra[amostra.Count - 1], leituras[leituras.Count - 1]))
                amostra.Add(leituras[leituras.Count - 1]);

            return amostra;
        }

        private static SolidColorBrush Pincel(Color cor)
        {
            var pincel = new SolidColorBrush(cor);
            pincel.Freeze();
            return pincel;
        }
    }
}
