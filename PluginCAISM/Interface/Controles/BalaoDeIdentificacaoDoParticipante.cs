using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;

namespace PluginCAISM.Interface.Controles
{
    /// <summary>
    /// Balão dos gráficos do grupo: mostra apenas a identificação do participante.
    /// </summary>
    /// <remarks>
    /// O balão padrão do LiveCharts exibe o título da série e os valores dos dois eixos. Aqui isso
    /// é ruído: os valores já estão nos eixos, e o que não dá para ler do gráfico é <b>de quem</b> é
    /// o ponto — que é justamente o que permite voltar aos dados daquele participante.
    ///
    /// Como cada participante é uma série própria, cujo título é a sua identificação, basta exibir
    /// o título da série sob o cursor.
    ///
    /// Herda de <see cref="UserControl"/> porque é o tipo que <c>CartesianChart.DataTooltip</c>
    /// aceita no LiveCharts 0.9.7 — mas sem par .xaml/.xaml.cs: o conteúdo é montado em código,
    /// por ser apenas uma borda com um texto dentro.
    /// </remarks>
    public class BalaoDeIdentificacaoDoParticipante : UserControl, IChartTooltip
    {
        private static readonly Color ColBd = Color.FromRgb(0xE4, 0xE7, 0xEC);
        private static readonly Color ColTx = Color.FromRgb(0x1B, 0x22, 0x30);

        private readonly TextBlock _identificacao;
        private TooltipData _dados;

        /// <summary>Monta o balão com a moldura e o texto que exibirá a identificação.</summary>
        public BalaoDeIdentificacaoDoParticipante()
        {
            _identificacao = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ColTx),
            };

            Content = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(ColBd),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Child = _identificacao,
            };
        }

        /// <summary>
        /// Só o ponto sob o cursor entra no balão — não a fatia vertical inteira.
        /// </summary>
        public TooltipSelectionMode? SelectionMode { get; set; } = TooltipSelectionMode.OnlySender;

        /// <summary>
        /// Dados do ponto apontado. O LiveCharts atribui esta propriedade a cada movimento do
        /// cursor; a atualização do texto acontece aqui.
        /// </summary>
        public TooltipData Data
        {
            get => _dados;
            set
            {
                _dados = value;
                AtualizarTexto();
                NotificarAlteracao();
            }
        }

        /// <summary>
        /// Exigido por <see cref="IChartTooltip"/>, que herda de
        /// <see cref="INotifyPropertyChanged"/>: o LiveCharts observa a mudança de
        /// <see cref="Data"/> para reposicionar o balão.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotificarAlteracao([CallerMemberName] string propriedade = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propriedade));
        }

        private void AtualizarTexto()
        {
            // O título da série é a identificação do participante. Sem ponto apontado, o balão
            // fica vazio em vez de exibir texto de outro ponto.
            _identificacao.Text = _dados?.Points?.FirstOrDefault()?.Series?.Title ?? string.Empty;
        }
    }
}
