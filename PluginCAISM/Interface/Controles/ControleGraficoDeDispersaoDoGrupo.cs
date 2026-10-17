using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Defaults;
using LiveCharts.Wpf;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;
using PluginCAISM.Parsing;
using PluginCAISM.Processamento;

namespace PluginCAISM.Interface.Controles
{
    /// <summary>
    /// Gráfico de dispersão da aba de Dados do Grupo com um ponto por participante e cor única,
    /// parametrizado pelas variáveis dos dois eixos.
    /// </summary>
    /// <remarks>
    /// É o ÚNICO gráfico da aba: o par de variáveis vem dos dois seletores de eixo, e a cor vem da
    /// combinação escolhida. Substituiu os oito gráficos fixos, que eram o mesmo desenho repetido com
    /// pares diferentes.
    ///
    /// As séries usam <c>ChartValues&lt;ScatterPoint&gt;</c>. Tipos anuláveis são proibidos aqui:
    /// o LiveCharts 0.9.7 não tem mapeador para eles e lança dentro do pipeline de layout do WPF,
    /// derrubando o processo hospedeiro (o Revit inteiro).
    /// </remarks>
    public class ControleGraficoDeDispersaoDoGrupo : UserControl
    {
        private static readonly Color ColBd  = Color.FromRgb(0xE4, 0xE7, 0xEC);
        private static readonly Color ColTx  = Color.FromRgb(0x1B, 0x22, 0x30);
        private static readonly Color ColTx2 = Color.FromRgb(0x4B, 0x55, 0x63);
        private static readonly Color ColTx3 = Color.FromRgb(0x7A, 0x83, 0x92);

        /// <summary>Cinza da grade quando ela corre sobre uma banda colorida.</summary>
        private static readonly Color ColSeparadorSobreBanda = Color.FromRgb(0x9A, 0xA3, 0xB0);

        /// <summary>
        /// Opacidade do preenchimento das bandas. Abaixo de opaco para que a grade do eixo,
        /// desenhada por baixo, continue visível através da faixa.
        /// </summary>
        private const byte OpacidadeDaBanda = 0xC4;

        private readonly VariavelDoGrupo _variavelX;
        private readonly VariavelDoGrupo _variavelY;

        /// <summary>Quantidade de participantes efetivamente plotados.</summary>
        public int QuantidadeDePontos { get; }

        /// <summary>Quantidade de participantes descartados por faltar alguma das variáveis.</summary>
        public int QuantidadeDescartada { get; }

        /// <summary>Cor desta combinação de eixos, vinda da paleta de configuração.</summary>
        private readonly string _corHexadecimal;

        /// <summary>
        /// Monta o gráfico cruzando as duas variáveis indicadas.
        /// </summary>
        /// <param name="respostas">Respostas de formulário, uma por participante.</param>
        /// <param name="registrosDeCortisol">
        /// Coletas de cortisol indexadas por identificação do participante — a mesma chave dos
        /// formulários (<c>ID_PARTICIPANTE</c>). Pode ser nulo quando a Etapa 3 não foi carregada.
        /// </param>
        /// <param name="indicadoresDeSbs">
        /// Indicadores de SBS indexados por identificação do participante. Pode ser nulo.
        /// </param>
        /// <param name="variavelX">Variável do eixo horizontal.</param>
        /// <param name="variavelY">Variável do eixo vertical.</param>
        /// <param name="corHexadecimal">Cor dos pontos, determinada pela combinação de eixos.</param>
        public ControleGraficoDeDispersaoDoGrupo(
            IEnumerable<RespostasDeFormularioPorParticipante> respostas,
            IReadOnlyDictionary<string, RegistroDeCortisol> registrosDeCortisol,
            IReadOnlyDictionary<string, IndicadoresDeSbs> indicadoresDeSbs,
            VariavelDoGrupo variavelX,
            VariavelDoGrupo variavelY,
            string corHexadecimal)
        {
            _variavelX = variavelX;
            _variavelY = variavelY;
            _corHexadecimal = corHexadecimal;

            var candidatos = respostas
                .Where(r => !string.IsNullOrWhiteSpace(r.IdentificacaoManual))
                .Select(r => new DadosDoParticipante(
                    r,
                    Localizar(registrosDeCortisol, r.IdentificacaoManual),
                    Localizar(indicadoresDeSbs, r.IdentificacaoManual)))
                .ToList();

            var pontos = candidatos
                .Select(d => (Identificacao: d.Respostas.IdentificacaoManual,
                              Ponto: new ScatterPoint(variavelX.Ler(d), variavelY.Ler(d))))
                .Where(p => !double.IsNaN(p.Ponto.X) && !double.IsNaN(p.Ponto.Y))
                .ToList();

            QuantidadeDePontos = pontos.Count;
            QuantidadeDescartada = candidatos.Count - pontos.Count;

            Content = ConstruirCartao(ConstruirGrafico(pontos));
            Margin = new Thickness(0, 0, 0, 14);
        }

        /// <summary>Busca por identificação, tolerando dicionário nulo ou chave ausente.</summary>
        private static T Localizar<T>(IReadOnlyDictionary<string, T> dicionario, string identificacao)
            where T : class
        {
            if (dicionario != null && identificacao != null
                && dicionario.TryGetValue(identificacao, out var valor))
            {
                return valor;
            }

            return null;
        }

        /// <remarks>
        /// <b>Uma série por participante, e não uma série com todos os pontos.</b> É o que faz o
        /// balão do LiveCharts identificar quem é o ponto sob o cursor: ele exibe o título da
        /// série, e o título aqui é a identificação do participante. Personalizar o balão de uma
        /// série única exigiria mapeador próprio, caminho que o LiveCharts 0.9.7 atende mal — a
        /// mesma fragilidade que derrubou o processo hospedeiro com <c>ChartValues&lt;double?&gt;</c>.
        ///
        /// Em troca, a legenda é desligada: 26 entradas ocupariam mais que o próprio gráfico. A
        /// contagem de participantes migra para a descrição acima dele.
        /// </remarks>
        private CartesianChart ConstruirGrafico(
            List<(string Identificacao, ScatterPoint Ponto)> pontos)
        {
            var cor = (Color)ColorConverter.ConvertFromString(_corHexadecimal);

            // Pontos empilhados em variáveis discretas só se distinguem pelo acúmulo de opacidade.
            byte opacidade = _variavelX.ValoresDiscretos || _variavelY.ValoresDiscretos
                ? (byte)0x99
                : (byte)0xCC;

            var preenchimento = new SolidColorBrush(Color.FromArgb(opacidade, cor.R, cor.G, cor.B));
            var traco = new SolidColorBrush(cor);

            var series = new SeriesCollection();

            foreach (var (identificacao, ponto) in pontos)
            {
                series.Add(new ScatterSeries
                {
                    Title = string.IsNullOrWhiteSpace(identificacao)
                        ? "(sem identificação)"
                        : identificacao,
                    Values = new ChartValues<ScatterPoint> { ponto },
                    Fill = preenchimento,
                    Stroke = traco,
                    StrokeThickness = 1.5,
                    MinPointShapeDiameter = 12,
                    MaxPointShapeDiameter = 12,
                });
            }

            var grafico = new CartesianChart
            {
                Height = 420,
                DisableAnimations = true,
                Hoverable = true,
                LegendLocation = LegendLocation.None,
                Series = series,

                // Balão reduzido à identificação do participante: os valores dos eixos já estão
                // no gráfico, e o que não se lê dali é de quem é o ponto.
                DataTooltip = new BalaoDeIdentificacaoDoParticipante(),
            };

            grafico.AxisX.Add(ConstruirEixo(_variavelX, pontos.Select(p => p.Ponto.X)));
            grafico.AxisY.Add(ConstruirEixo(_variavelY, pontos.Select(p => p.Ponto.Y)));

            return grafico;
        }

        /// <summary>
        /// Monta um eixo a partir do descritor da variável e dos valores observados.
        /// </summary>
        private static Axis ConstruirEixo(VariavelDoGrupo variavel, IEnumerable<double> valores)
        {
            bool temBandas = variavel.FaixasDeReferencia?.Length > 0;

            // Sobre as bandas coloridas, o cinza claro padrão desaparece: banda e linha têm
            // luminosidade parecida. Onde há faixa, a linha escurece e engrossa para ficar à frente.
            var separador = new LiveCharts.Wpf.Separator
            {
                Stroke = new SolidColorBrush(temBandas ? ColSeparadorSobreBanda : ColBd),
                StrokeThickness = temBandas ? 1.4 : 1,
            };

            if (variavel.PassoDoSeparador.HasValue)
                separador.Step = variavel.PassoDoSeparador.Value;

            var eixo = new Axis
            {
                Title = variavel.TituloDoEixo,
                Foreground = new SolidColorBrush(ColTx3),
                Separator = separador,
            };

            if (variavel.FormatarRotulo != null)
                eixo.LabelFormatter = valor => variavel.FormatarRotulo(valor);

            var (minimo, maximo) = CalcularLimites(variavel, valores);
            eixo.MinValue = minimo;
            eixo.MaxValue = maximo;

            // Bandas das faixas de classificação do índice, desenhadas ANTES da linha de zero para
            // que esta fique por cima e continue visível sobre o preenchimento.
            //
            // O preenchimento é PARCIALMENTE TRANSPARENTE de propósito: o LiveCharts desenha as
            // seções sobre a grade, e um fundo opaco apagaria as linhas do eixo. Com alfa, a grade
            // continua atravessando a banda.
            foreach (var faixa in variavel.FaixasDeReferencia ?? System.Array.Empty<FaixaDeReferencia>())
            {
                var cor = (Color)ColorConverter.ConvertFromString(faixa.CorHexadecimal);

                eixo.Sections.Add(new AxisSection
                {
                    Value = faixa.Inicio,
                    SectionWidth = faixa.Fim - faixa.Inicio,
                    Fill = new SolidColorBrush(
                        Color.FromArgb(OpacidadeDaBanda, cor.R, cor.G, cor.B)),
                });
            }

            if (variavel.LinhaDeReferenciaEmZero)
            {
                // Separa os participantes cujo valor subiu dos que caiu; sem ela o sinal se perde.
                eixo.Sections.Add(new AxisSection
                {
                    Value = 0,
                    SectionWidth = 0,
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                        EstiloDosGraficosDoGrupo.CorHexDaLinhaDeReferencia)),
                    StrokeThickness = 2,
                });
            }

            return eixo;
        }

        /// <summary>
        /// Calcula os limites do eixo: usa os fixos do descritor quando houver e, no restante,
        /// deriva dos valores observados com folga. Uma variável com linha de referência em zero
        /// garante que o zero fique visível, senão a linha cairia fora da área plotada.
        /// </summary>
        private static (double minimo, double maximo) CalcularLimites(
            VariavelDoGrupo variavel, IEnumerable<double> valores)
        {
            var lista = valores.ToList();

            if (!lista.Any())
            {
                return (variavel.MinimoFixo ?? -variavel.Margem,
                        variavel.MaximoFixo ??  variavel.Margem);
            }

            double menor = lista.Min();
            double maior = lista.Max();

            if (variavel.LinhaDeReferenciaEmZero)
            {
                menor = Math.Min(0, menor);
                maior = Math.Max(0, maior);
            }

            return (variavel.MinimoFixo ?? menor - variavel.Margem,
                    variavel.MaximoFixo ?? maior + variavel.Margem);
        }

        private UIElement ConstruirCartao(CartesianChart grafico)
        {
            var conteudo = new StackPanel();

            conteudo.Children.Add(new TextBlock
            {
                Text = $"{_variavelX.NomeCurto} × {_variavelY.NomeCurto}",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ColTx),
                Margin = new Thickness(0, 0, 0, 2),
            });

            // Descrição da combinação escolhida, no mesmo padrão da linha que acompanha o delta de
            // cortisol na aba de Dados do Participante: título em negrito, significado abaixo.
            conteudo.Children.Add(new TextBlock
            {
                Text = _variavelX == _variavelY
                    ? "Mesma variável nos dois eixos: os pontos caem sobre a diagonal."
                    : $"{_variavelX.TituloDoEixo} no eixo X · {_variavelY.TituloDoEixo} no eixo Y",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(ColTx3),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2),
            });

            conteudo.Children.Add(new TextBlock
            {
                Text = MontarDescricao(),
                FontSize = 11,
                Foreground = new SolidColorBrush(ColTx3),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            });

            // Legenda das faixas do índice no eixo Y, quando o índice tiver pontos de corte.
            var legenda = ConstruirLegendaDeFaixas();
            if (legenda != null)
                conteudo.Children.Add(legenda);

            conteudo.Children.Add(grafico);

            return new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(ColBd),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 14, 16, 14),
                Child = conteudo,
            };
        }

        // A linha de estatística (n, Pearson e Spearman) foi REMOVIDA do cartão. O
        // CalculadorDeCorrelacao permanece no projeto, sem consumidor nesta tela.

        /// <summary>
        /// Monta a legenda das faixas de classificação do índice do eixo Y.
        /// </summary>
        /// <remarks>
        /// Devolve null quando o índice não tem pontos de corte definidos — hoje só o IEEE tem. As
        /// bandas sem rótulo seriam faixas coloridas sem significado declarado, o que é pior do que
        /// não desenhar nada.
        /// </remarks>
        private UIElement ConstruirLegendaDeFaixas()
        {
            var faixas = _variavelY.FaixasDeReferencia;

            if (faixas == null || faixas.Length == 0)
                return null;

            var linha = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

            foreach (var faixa in faixas)
            {
                var item = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 16, 0),
                };

                item.Children.Add(new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(faixa.CorHexadecimal)),
                    BorderBrush = new SolidColorBrush(ColBd),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });

                item.Children.Add(new TextBlock
                {
                    Text = faixa.Rotulo,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(ColTx2),
                    VerticalAlignment = VerticalAlignment.Center,
                });

                linha.Children.Add(item);
            }

            return linha;
        }

        private string MontarDescricao()
        {
            if (QuantidadeDePontos == 0)
            {
                return "Sem dados: carregue o arquivo de formulários (Etapa 4)" +
                       (UsaCortisol() ? " e o de cortisol (Etapa 3)" : string.Empty) +
                       " na tela inicial";
            }

            string descricao = $"{QuantidadeDePontos} participante(s) — cada ponto é um indivíduo; " +
                               "passe o cursor sobre um ponto para ver de quem é";

            if (UsaCortisol())
            {
                // O lado da linha depende do eixo em que o delta foi plotado.
                string ladoPositivo = _variavelX.LinhaDeReferenciaEmZero ? "à direita" : "acima";
                string ladoNegativo = _variavelX.LinhaDeReferenciaEmZero ? "à esquerda" : "abaixo";

                descricao += $"; {ladoPositivo} da linha de referência o cortisol subiu entre as " +
                             $"coletas, {ladoNegativo} dela caiu";
            }

            if (QuantidadeDescartada > 0)
            {
                descricao += $" · {QuantidadeDescartada} sem {_variavelX.NomeNoDescarte} ou " +
                             $"{_variavelY.NomeNoDescarte}, fora do gráfico";
            }

            return descricao;
        }

        private bool UsaCortisol() =>
            _variavelX.LinhaDeReferenciaEmZero || _variavelY.LinhaDeReferenciaEmZero;
    }
}
