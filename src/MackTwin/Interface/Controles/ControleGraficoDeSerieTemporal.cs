using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MackTwin.Configuracao;
using MackTwin.Dominio;
using MackTwin.Interface.Recursos;
using MackTwin.Processamento;

namespace MackTwin.Interface.Controles
{
    /// <summary>
    /// Série temporal de uma sala em dois painéis empilhados — temperatura e umidade —, cada um
    /// com a sua faixa de conforto sombreada, desenhados com WPF puro.
    /// </summary>
    /// <remarks>
    /// <b>Por que não uma biblioteca de gráficos.</b> Uma exceção lançada dentro do layout do WPF
    /// derruba o Revit inteiro, e foi o que uma biblioteca externa fez. Um <see cref="Canvas"/>
    /// com <see cref="Polyline"/> faz tudo o que este painel precisa, está sob controle completo
    /// e não traz dependência.
    ///
    /// <b>O eixo do tempo é o período pedido</b>, não a extensão dos dados: "24 h" vai de ontem
    /// a agora mesmo que o sensor tenha parado no meio — o vazio à direita é informação.
    /// Em períodos longos a série chega agregada (<see cref="AgregadorDeSerie"/>): a linha é a
    /// média do balde e uma faixa translúcida mostra o mínimo e o máximo.
    ///
    /// <b>Calcula uma vez, desenha quando cabe.</b> Valores, extremos e escala são calculados no
    /// construtor; o desenho só depende do tamanho do Canvas, que só se conhece depois do
    /// layout — e, durante um redimensionamento, chega dezenas de vezes por segundo. Um
    /// <see cref="DispatcherTimer"/> curto junta essas chamadas num único redesenho.
    /// Lacunas (NaN) interrompem a linha em vez de puxá-la para zero.
    /// </remarks>
    public sealed class ControleGraficoDeSerieTemporal : UserControl
    {
        /// <summary>Teto de pontos brutos desenhados por série; acima disso a série é reamostrada.</summary>
        public const int MaximoDePontos = 1500;

        private const double MargemEsquerda = 48;
        private const double MargemDireita = 16;
        private const double MargemSuperior = 10;
        private const double MargemInferior = 24;
        private static readonly TimeSpan AtrasoDoRedesenho = TimeSpan.FromMilliseconds(60);

        private static readonly Brush PincelDaFaixa = Paleta.Pincel(Paleta.FaixaDeConforto);
        private static readonly Brush PincelDaGrade = Paleta.Pincel(Paleta.Grade);
        private static readonly Brush PincelDoEixo = Paleta.Pincel(Paleta.Eixo);
        private static readonly Brush PincelDoTexto = Paleta.Pincel(Paleta.Texto3);
        private static readonly Brush PincelDaBorda = Paleta.Pincel(Paleta.Borda);

        /// <summary>Uma grandeza já preparada para desenhar: valores, escala e aparência.</summary>
        private sealed class Serie
        {
            public double[] Valores = Array.Empty<double>();
            public double[]? Minimos;
            public double[]? Maximos;
            public double ValorMinimo;
            public double ValorMaximo;
            public double FaixaMinima;
            public double FaixaMaxima;
            public Brush Pincel = Brushes.Black;
            public Brush PincelDaBanda = Brushes.Transparent;
            public string Formato = "0";
            public string Unidade = string.Empty;
            public Canvas Canvas = new();
        }

        private readonly DateTime[] _tempos;
        private readonly DateTime _inicioDoEixo;
        private readonly double _duracaoEmSegundos;
        private readonly string _formatoDoTempo;
        private readonly string? _descricaoDoBalde;
        private readonly List<Serie> _series = new();
        private readonly DispatcherTimer _redesenho;

        /// <summary>
        /// Monta o gráfico das leituras (já em ordem cronológica) sobre o eixo
        /// [<paramref name="inicioDoEixo"/>, <paramref name="fimDoEixo"/>], agregando em
        /// <paramref name="balde"/> quando informado.
        /// </summary>
        public ControleGraficoDeSerieTemporal(
            IReadOnlyList<Leitura> leituras, DateTime inicioDoEixo, DateTime fimDoEixo, TimeSpan? balde)
        {
            _redesenho = new DispatcherTimer { Interval = AtrasoDoRedesenho };
            _redesenho.Tick += (remetente, evento) => { _redesenho.Stop(); DesenharTudo(); };

            if (fimDoEixo <= inicioDoEixo)
                fimDoEixo = inicioDoEixo.AddHours(1);

            _inicioDoEixo = inicioDoEixo;
            _duracaoEmSegundos = Math.Max((fimDoEixo - inicioDoEixo).TotalSeconds, 1.0);
            _formatoDoTempo = fimDoEixo - inicioDoEixo > TimeSpan.FromDays(1) ? "dd/MM HH:mm" : "HH:mm";

            Serie temperatura, umidade;

            if (balde.HasValue && leituras.Count > 0)
            {
                var pontos = AgregadorDeSerie.Agregar(leituras, balde.Value);
                _tempos = new DateTime[pontos.Count];
                for (int indice = 0; indice < pontos.Count; indice++)
                    _tempos[indice] = pontos[indice].Inicio;

                _descricaoDoBalde = balde.Value >= TimeSpan.FromDays(1) ? "média por dia, faixa mín–máx" : "média por hora, faixa mín–máx";

                temperatura = PrepararSerieAgregada(pontos,
                    p => p.TemperaturaMedia, p => p.TemperaturaMinima, p => p.TemperaturaMaxima,
                    FaixasDeConfortoAmbiental.TemperaturaMinima, FaixasDeConfortoAmbiental.TemperaturaMaxima,
                    Paleta.Temperatura, "0.0", "°C");

                umidade = PrepararSerieAgregada(pontos,
                    p => p.UmidadeMedia, p => p.UmidadeMinima, p => p.UmidadeMaxima,
                    FaixasDeConfortoAmbiental.UmidadeMinima, FaixasDeConfortoAmbiental.UmidadeMaxima,
                    Paleta.Umidade, "0", "%");
            }
            else
            {
                var amostra = Reamostrar(leituras);
                _tempos = new DateTime[amostra.Count];
                for (int indice = 0; indice < amostra.Count; indice++)
                    _tempos[indice] = amostra[indice].Timestamp;

                temperatura = PrepararSerie(amostra, l => l.Temperatura,
                    FaixasDeConfortoAmbiental.TemperaturaMinima, FaixasDeConfortoAmbiental.TemperaturaMaxima,
                    Paleta.Temperatura, "0.0", "°C");

                umidade = PrepararSerie(amostra, l => l.Umidade,
                    FaixasDeConfortoAmbiental.UmidadeMinima, FaixasDeConfortoAmbiental.UmidadeMaxima,
                    Paleta.Umidade, "0", "%");
            }

            var grade = new Grid();
            grade.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grade.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var painelTemperatura = MontarPainel("Temperatura (°C)", temperatura);
            var painelUmidade = MontarPainel("Umidade relativa (%)", umidade);

            Grid.SetRow(painelTemperatura, 0);
            Grid.SetRow(painelUmidade, 1);
            painelUmidade.Margin = new Thickness(0, 10, 0, 0);

            grade.Children.Add(painelTemperatura);
            grade.Children.Add(painelUmidade);
            Content = grade;
        }

        // ── Preparação (uma vez) ─────────────────────────────────────────

        private Serie PrepararSerie(
            List<Leitura> amostra, Func<Leitura, double> valor,
            double faixaMinima, double faixaMaxima, Color cor, string formato, string unidade)
        {
            var valores = new double[amostra.Count];

            for (int indice = 0; indice < amostra.Count; indice++)
                valores[indice] = valor(amostra[indice]);

            return ConcluirSerie(valores, null, null, faixaMinima, faixaMaxima, cor, formato, unidade);
        }

        private Serie PrepararSerieAgregada(
            List<PontoAgregado> pontos,
            Func<PontoAgregado, double> media, Func<PontoAgregado, double> minimo, Func<PontoAgregado, double> maximo,
            double faixaMinima, double faixaMaxima, Color cor, string formato, string unidade)
        {
            var medias = new double[pontos.Count];
            var minimos = new double[pontos.Count];
            var maximos = new double[pontos.Count];

            for (int indice = 0; indice < pontos.Count; indice++)
            {
                medias[indice] = media(pontos[indice]);
                minimos[indice] = minimo(pontos[indice]);
                maximos[indice] = maximo(pontos[indice]);
            }

            return ConcluirSerie(medias, minimos, maximos, faixaMinima, faixaMaxima, cor, formato, unidade);
        }

        private Serie ConcluirSerie(
            double[] valores, double[]? minimos, double[]? maximos,
            double faixaMinima, double faixaMaxima, Color cor, string formato, string unidade)
        {
            double menor = faixaMinima, maior = faixaMaxima;

            void Considerar(double[]? vetor)
            {
                if (vetor == null) return;

                foreach (double v in vetor)
                {
                    if (double.IsNaN(v)) continue;
                    if (v < menor) menor = v;
                    if (v > maior) maior = v;
                }
            }

            Considerar(valores);
            Considerar(minimos);
            Considerar(maximos);

            double folga = Math.Max((maior - menor) * 0.08, 0.5);

            var serie = new Serie
            {
                Valores = valores,
                Minimos = minimos,
                Maximos = maximos,
                ValorMinimo = menor - folga,
                ValorMaximo = maior + folga,
                FaixaMinima = faixaMinima,
                FaixaMaxima = faixaMaxima,
                Pincel = Paleta.Pincel(cor),
                PincelDaBanda = Paleta.Pincel(Color.FromArgb(0x33, cor.R, cor.G, cor.B)),
                Formato = formato,
                Unidade = unidade,
                Canvas = new Canvas { ClipToBounds = true, Background = Brushes.Transparent, MinHeight = 110 },
            };

            serie.Canvas.SizeChanged += (remetente, evento) => AgendarRedesenho();
            _series.Add(serie);
            return serie;
        }

        private Border MontarPainel(string titulo, Serie serie)
        {
            var cabecalho = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

            string legendaDaFaixa = string.Format(CultureInfo.CurrentCulture, "faixa de conforto {0:0}–{1:0} {2}",
                serie.FaixaMinima, serie.FaixaMaxima, serie.Unidade);

            if (_descricaoDoBalde != null)
                legendaDaFaixa = _descricaoDoBalde + "  ·  " + legendaDaFaixa;

            var faixa = new TextBlock
            {
                Text = legendaDaFaixa,
                FontSize = 10.5,
                Foreground = PincelDoTexto,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            DockPanel.SetDock(faixa, Dock.Right);
            cabecalho.Children.Add(faixa);

            cabecalho.Children.Add(new TextBlock
            {
                Text = titulo,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = serie.Pincel,
            });

            var conteudo = new DockPanel();
            DockPanel.SetDock(cabecalho, Dock.Top);
            conteudo.Children.Add(cabecalho);
            conteudo.Children.Add(serie.Canvas);

            return new Border
            {
                Background = Brushes.White,
                BorderBrush = PincelDaBorda,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 8),
                Child = conteudo,
            };
        }

        // ── Desenho (quando o tamanho se estabiliza) ─────────────────────

        private void AgendarRedesenho()
        {
            _redesenho.Stop();
            _redesenho.Start();
        }

        private void DesenharTudo()
        {
            foreach (var serie in _series)
                Desenhar(serie);
        }

        private void Desenhar(Serie serie)
        {
            var canvas = serie.Canvas;
            canvas.Children.Clear();

            double largura = canvas.ActualWidth;
            double altura = canvas.ActualHeight;

            if (largura < 80 || altura < 50)
                return;

            double x0 = MargemEsquerda, x1 = largura - MargemDireita;
            double y0 = MargemSuperior, y1 = altura - MargemInferior;

            if (x1 <= x0 || y1 <= y0)
                return;

            double amplitude = serie.ValorMaximo - serie.ValorMinimo;

            double Y(double v) => y1 - (v - serie.ValorMinimo) / amplitude * (y1 - y0);
            double X(DateTime t) => x0 + (t - _inicioDoEixo).TotalSeconds / _duracaoEmSegundos * (x1 - x0);

            // ── faixa de conforto ──
            double topoDaFaixa = Y(serie.FaixaMaxima);
            double baseDaFaixa = Y(serie.FaixaMinima);

            var faixa = new Rectangle
            {
                Width = x1 - x0,
                Height = Math.Max(baseDaFaixa - topoDaFaixa, 1),
                Fill = PincelDaFaixa,
            };
            Canvas.SetLeft(faixa, x0);
            Canvas.SetTop(faixa, topoDaFaixa);
            canvas.Children.Add(faixa);

            // ── grade horizontal e rótulos do eixo Y ──
            const int divisoes = 4;

            for (int indice = 0; indice <= divisoes; indice++)
            {
                double v = serie.ValorMinimo + amplitude * indice / divisoes;
                double y = Y(v);

                canvas.Children.Add(new Line { X1 = x0, X2 = x1, Y1 = y, Y2 = y, Stroke = PincelDaGrade, StrokeThickness = 1 });

                var rotulo = new TextBlock
                {
                    Text = v.ToString(serie.Formato, CultureInfo.CurrentCulture),
                    FontSize = 10,
                    Foreground = PincelDoTexto,
                    Width = MargemEsquerda - 8,
                    TextAlignment = TextAlignment.Right,
                };
                Canvas.SetLeft(rotulo, 0);
                Canvas.SetTop(rotulo, y - 7);
                canvas.Children.Add(rotulo);
            }

            // ── eixo do tempo (o período pedido, não a extensão dos dados) ──
            canvas.Children.Add(new Line { X1 = x0, X2 = x1, Y1 = y1, Y2 = y1, Stroke = PincelDoEixo, StrokeThickness = 1 });

            int rotulosDeTempo = Math.Max(2, Math.Min(7, (int)((x1 - x0) / 90)));

            for (int indice = 0; indice <= rotulosDeTempo; indice++)
            {
                var t = _inicioDoEixo.AddSeconds(_duracaoEmSegundos * indice / rotulosDeTempo);
                double x = X(t);

                canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = y1, Y2 = y1 + 3, Stroke = PincelDoEixo, StrokeThickness = 1 });

                var rotulo = new TextBlock
                {
                    Text = t.ToString(_formatoDoTempo, CultureInfo.CurrentCulture),
                    FontSize = 10,
                    Foreground = PincelDoTexto,
                    Width = 72,
                    TextAlignment = TextAlignment.Center,
                };
                Canvas.SetLeft(rotulo, x - 36);
                Canvas.SetTop(rotulo, y1 + 5);
                canvas.Children.Add(rotulo);
            }

            if (serie.Valores.Length == 0)
            {
                var aviso = new TextBlock
                {
                    Text = "Sem leituras neste período.",
                    FontSize = 11,
                    Foreground = PincelDoTexto,
                };
                Canvas.SetLeft(aviso, x0 + 8);
                Canvas.SetTop(aviso, y0 + 4);
                canvas.Children.Add(aviso);
                return;
            }

            // ── banda mínimo–máximo (séries agregadas) ──
            if (serie.Minimos != null && serie.Maximos != null)
                DesenharBanda(canvas, serie, X, Y);

            // ── linha, interrompida nas lacunas ──
            var segmento = new PointCollection();
            Point? ultimoPonto = null;
            double ultimoValor = double.NaN;

            void Despejar()
            {
                if (segmento.Count >= 2)
                {
                    canvas.Children.Add(new Polyline
                    {
                        Points = segmento,
                        Stroke = serie.Pincel,
                        StrokeThickness = 2,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                    });
                }
                else if (segmento.Count == 1)
                {
                    AdicionarPonto(canvas, segmento[0], 2.5, serie.Pincel);
                }

                segmento = new PointCollection();
            }

            for (int indice = 0; indice < serie.Valores.Length; indice++)
            {
                double v = serie.Valores[indice];

                if (double.IsNaN(v))
                {
                    Despejar();
                    continue;
                }

                var ponto = new Point(X(_tempos[indice]), Y(v));
                segmento.Add(ponto);
                ultimoPonto = ponto;
                ultimoValor = v;
            }

            Despejar();

            // ── último valor em destaque ──
            if (ultimoPonto.HasValue)
            {
                AdicionarPonto(canvas, ultimoPonto.Value, 4, serie.Pincel);

                var destaque = new TextBlock
                {
                    Text = string.Format(CultureInfo.CurrentCulture, "{0:" + serie.Formato + "} {1}", ultimoValor, serie.Unidade),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = serie.Pincel,
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

        /// <summary>Polígono translúcido entre mínimo e máximo, em trechos contínuos (lacunas o interrompem).</summary>
        private void DesenharBanda(Canvas canvas, Serie serie, Func<DateTime, double> X, Func<double, double> Y)
        {
            var superior = new List<Point>();
            var inferior = new List<Point>();

            void Despejar()
            {
                if (superior.Count >= 2)
                {
                    var pontos = new PointCollection(superior.Count * 2);

                    foreach (var p in superior) pontos.Add(p);
                    for (int i = inferior.Count - 1; i >= 0; i--) pontos.Add(inferior[i]);

                    canvas.Children.Add(new Polygon { Points = pontos, Fill = serie.PincelDaBanda });
                }

                superior.Clear();
                inferior.Clear();
            }

            for (int indice = 0; indice < serie.Valores.Length; indice++)
            {
                double maximo = serie.Maximos![indice];
                double minimo = serie.Minimos![indice];

                if (double.IsNaN(maximo) || double.IsNaN(minimo))
                {
                    Despejar();
                    continue;
                }

                double x = X(_tempos[indice]);
                superior.Add(new Point(x, Y(maximo)));
                inferior.Add(new Point(x, Y(minimo)));
            }

            Despejar();
        }

        private static void AdicionarPonto(Canvas canvas, Point centro, double raio, Brush pincel)
        {
            var ponto = new Ellipse
            {
                Width = raio * 2,
                Height = raio * 2,
                Fill = pincel,
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
                return new List<Leitura>(leituras);

            int passo = (int)Math.Ceiling(leituras.Count / (double)MaximoDePontos);
            var amostra = new List<Leitura>(MaximoDePontos + 1);

            for (int indice = 0; indice < leituras.Count; indice += passo)
                amostra.Add(leituras[indice]);

            if (!ReferenceEquals(amostra[amostra.Count - 1], leituras[leituras.Count - 1]))
                amostra.Add(leituras[leituras.Count - 1]);

            return amostra;
        }
    }
}
