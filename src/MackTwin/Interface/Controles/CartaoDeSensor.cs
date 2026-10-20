using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MackTwin.Configuracao;
using MackTwin.Dominio;
using MackTwin.Interface.Recursos;
using MackTwin.Processamento;

namespace MackTwin.Interface.Controles
{
    /// <summary>
    /// Cartão "ao vivo" de um sensor: identificação em quatro linhas (dispositivo, HOSTNAME,
    /// MAC, última leitura) e, abaixo, a última temperatura e a última umidade, cada uma na cor
    /// da sua faixa.
    /// </summary>
    /// <remarks>
    /// Montado em código, sem XAML próprio, porque é um bloco pequeno que nasce e morre quando
    /// a sala exibida muda. As cores vêm de <see cref="Paleta"/> e <see cref="CoresDeConforto"/>,
    /// as mesmas da lista e do modelo.
    /// </remarks>
    public sealed class CartaoDeSensor : UserControl
    {
        private static readonly Brush PincelDaBorda = Paleta.Pincel(Paleta.Borda);
        private static readonly Brush PincelDoTexto = Paleta.Pincel(Paleta.Texto);
        private static readonly Brush PincelDoTextoFraco = Paleta.Pincel(Paleta.Texto3);

        /// <summary>Monta o cartão a partir do estado atual do sensor.</summary>
        public CartaoDeSensor(Sensor sensor)
        {
            var ultima = sensor.UltimaLeitura;
            double temperatura = ultima?.Temperatura ?? double.NaN;
            double umidade = ultima?.Umidade ?? double.NaN;

            var conteudo = new StackPanel();

            conteudo.Children.Add(new TextBlock
            {
                Text = sensor.Rotulo,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = PincelDoTexto,
            });

            conteudo.Children.Add(new TextBlock
            {
                Text = sensor.Hostname,
                FontSize = 10.5,
                Foreground = PincelDoTextoFraco,
            });

            conteudo.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(sensor.EnderecoMac) ? "MAC —" : $"MAC {sensor.EnderecoMac}",
                FontSize = 10.5,
                Foreground = PincelDoTextoFraco,
            });

            conteudo.Children.Add(new TextBlock
            {
                Text = ultima == null
                    ? "Sem leituras"
                    : string.Format(CultureInfo.CurrentCulture, "Última leitura {0:dd/MM/yyyy HH:mm:ss}", ultima.Timestamp),
                FontSize = 10.5,
                Foreground = PincelDoTextoFraco,
                Margin = new Thickness(0, 0, 0, 10),
            });

            var grandezas = new Grid();
            grandezas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grandezas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var blocoTemperatura = MontarGrandeza(
                "Temperatura", temperatura, "°C", "0.0",
                ClassificadorDeConforto.ClassificarTemperatura(temperatura),
                $"faixa {FaixasDeConfortoAmbiental.TemperaturaMinima:0}–{FaixasDeConfortoAmbiental.TemperaturaMaxima:0} °C");

            var blocoUmidade = MontarGrandeza(
                "Umidade", umidade, "%", "0",
                ClassificadorDeConforto.ClassificarUmidade(umidade),
                $"faixa {FaixasDeConfortoAmbiental.UmidadeMinima:0}–{FaixasDeConfortoAmbiental.UmidadeMaxima:0} %");

            Grid.SetColumn(blocoTemperatura, 0);
            Grid.SetColumn(blocoUmidade, 1);
            grandezas.Children.Add(blocoTemperatura);
            grandezas.Children.Add(blocoUmidade);
            conteudo.Children.Add(grandezas);

            Content = new Border
            {
                Background = Brushes.White,
                BorderBrush = PincelDaBorda,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12),
                Child = conteudo,
            };

            Width = 300;
            Margin = new Thickness(0, 0, 12, 12);
        }

        private static UIElement MontarGrandeza(
            string rotulo, double valor, string unidade, string formato,
            ClassificacaoDeConforto classificacao, string faixa)
        {
            var cor = Paleta.Pincel(Paleta.CorDeConforto(classificacao));
            var bloco = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };

            bloco.Children.Add(new TextBlock
            {
                Text = rotulo,
                FontSize = 10.5,
                Foreground = PincelDoTextoFraco,
            });

            bloco.Children.Add(new TextBlock
            {
                Text = double.IsNaN(valor)
                    ? "—"
                    : string.Format(CultureInfo.CurrentCulture, "{0:" + formato + "} {1}", valor, unidade),
                FontSize = 26,
                FontWeight = FontWeights.SemiBold,
                Foreground = cor,
            });

            bloco.Children.Add(new TextBlock
            {
                Text = ClassificadorDeConforto.Descrever(classificacao),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = cor,
            });

            bloco.Children.Add(new TextBlock
            {
                Text = faixa,
                FontSize = 10,
                Foreground = PincelDoTextoFraco,
            });

            return bloco;
        }
    }
}
