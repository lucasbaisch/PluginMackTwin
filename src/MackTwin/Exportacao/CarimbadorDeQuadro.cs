using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MackTwin.Configuracao;
using MackTwin.Dominio;
using MackTwin.Interface.Recursos;
using MackTwin.Processamento;

namespace MackTwin.Exportacao
{
    /// <summary>
    /// Põe sobre cada quadro exportado pelo Revit a faixa de identificação (data e hora, título)
    /// e a legenda das cores de conforto — o que o estudo solar nativo faz com a hora do dia.
    /// </summary>
    /// <remarks>
    /// Desenha com o próprio WPF (<see cref="DrawingVisual"/> + <see cref="RenderTargetBitmap"/>),
    /// que já está carregado pela janela, em vez de System.Drawing — sem dependência nova e com a
    /// mesma paleta da interface. Precisa rodar na thread da interface (a do Revit, onde a
    /// janela vive).
    /// </remarks>
    public static class CarimbadorDeQuadro
    {
        private const int AlturaDaFaixaSuperior = 72;
        private const int AlturaDaFaixaInferior = 44;
        private const int Margem = 18;
        private const double EspessuraDoFilete = 6;

        private static readonly ClassificacaoDeConforto[] Legenda =
        {
            ClassificacaoDeConforto.Confortavel,
            ClassificacaoDeConforto.AbaixoDaFaixa,
            ClassificacaoDeConforto.AcimaDaFaixa,
            ClassificacaoDeConforto.SemDados,
        };

        /// <summary>Lê um PNG do disco sem manter o arquivo aberto.</summary>
        public static BitmapSource Carregar(string caminho)
        {
            using var arquivo = File.OpenRead(caminho);
            var quadro = BitmapFrame.Create(arquivo, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            quadro.Freeze();
            return quadro;
        }

        /// <summary>Compõe a imagem com faixa superior (título e subtítulo) e legenda inferior.</summary>
        public static BitmapSource Carimbar(BitmapSource imagem, string titulo, string subtitulo)
        {
            int largura = imagem.PixelWidth;
            int altura = imagem.PixelHeight;

            var visual = new DrawingVisual();

            using (var contexto = visual.RenderOpen())
            {
                contexto.DrawRectangle(Brushes.White, null, new Rect(0, 0, largura, altura));
                contexto.DrawImage(imagem, new Rect(0, 0, largura, altura));

                // Faixa superior translúcida com filete da marca.
                var faixaSuperior = new Rect(0, 0, largura, AlturaDaFaixaSuperior);
                contexto.DrawRectangle(ComOpacidade(Paleta.Superficie, 0.92), null, faixaSuperior);
                contexto.DrawRectangle(Paleta.Pincel(Paleta.Marca), null, new Rect(0, 0, EspessuraDoFilete, AlturaDaFaixaSuperior));

                var textoDoTitulo = Texto(titulo, 26, FontWeights.Bold, Paleta.Marca);
                var textoDoSubtitulo = Texto(subtitulo, 14, FontWeights.Normal, Paleta.Texto2);
                contexto.DrawText(textoDoTitulo, new Point(Margem + EspessuraDoFilete, 10));
                contexto.DrawText(textoDoSubtitulo, new Point(Margem + EspessuraDoFilete, 14 + textoDoTitulo.Height));

                // Faixa inferior com a legenda.
                double topoDaFaixa = altura - AlturaDaFaixaInferior;
                contexto.DrawRectangle(ComOpacidade(Paleta.Superficie, 0.92), null, new Rect(0, topoDaFaixa, largura, AlturaDaFaixaInferior));
                contexto.DrawLine(new Pen(Paleta.Pincel(Paleta.Borda), 1), new Point(0, topoDaFaixa), new Point(largura, topoDaFaixa));

                double x = Margem;
                double centroY = topoDaFaixa + AlturaDaFaixaInferior / 2.0;

                foreach (var classificacao in Legenda)
                {
                    const double lado = 14;
                    var cor = Paleta.CorDeConforto(classificacao);
                    contexto.DrawRoundedRectangle(Paleta.Pincel(cor), null, new Rect(x, centroY - lado / 2, lado, lado), 3, 3);
                    x += lado + 8;

                    var rotulo = Texto(ClassificadorDeConforto.Descrever(classificacao), 13, FontWeights.SemiBold, Paleta.Texto);
                    contexto.DrawText(rotulo, new Point(x, centroY - rotulo.Height / 2));
                    x += rotulo.Width + 26;
                }

                var faixas = Texto(
                    string.Format(CultureInfo.CurrentCulture, "Conforto: {0:0}–{1:0} °C · {2:0}–{3:0} % UR (NBR 17037)",
                        FaixasDeConfortoAmbiental.TemperaturaMinima, FaixasDeConfortoAmbiental.TemperaturaMaxima,
                        FaixasDeConfortoAmbiental.UmidadeMinima, FaixasDeConfortoAmbiental.UmidadeMaxima),
                    12, FontWeights.Normal, Paleta.Texto3);
                contexto.DrawText(faixas, new Point(largura - Margem - faixas.Width, centroY - faixas.Height / 2));
            }

            var resultado = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
            resultado.Render(visual);
            resultado.Freeze();
            return resultado;
        }

        /// <summary>Grava a imagem como PNG.</summary>
        public static void SalvarPng(BitmapSource imagem, string caminho)
        {
            var codificador = new PngBitmapEncoder();
            codificador.Frames.Add(BitmapFrame.Create(imagem));

            using var arquivo = File.Create(caminho);
            codificador.Save(arquivo);
        }

        /// <summary>Reduz a imagem à paleta do GIF.</summary>
        public static QuadroDeGif ParaQuadroDeGif(BitmapSource imagem, int atrasoEmCentesimos)
        {
            var bgra = new FormatConvertedBitmap(imagem, PixelFormats.Bgra32, null, 0);
            int largura = bgra.PixelWidth;
            int altura = bgra.PixelHeight;
            int passo = largura * 4;
            var pixels = new byte[passo * altura];

            bgra.CopyPixels(pixels, passo, 0);

            return new QuadroDeGif
            {
                Largura = largura,
                Altura = altura,
                Indices = CodificadorDeGif.Quantizar(pixels, largura, altura, passo),
                AtrasoEmCentesimos = atrasoEmCentesimos,
            };
        }

        private static FormattedText Texto(string conteudo, double tamanho, FontWeight peso, Color cor)
        {
            return new FormattedText(
                conteudo,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, peso, FontStretches.Normal),
                tamanho,
                Paleta.Pincel(cor),
                1.0);
        }

        private static Brush ComOpacidade(Color cor, double opacidade)
        {
            var pincel = new SolidColorBrush(cor) { Opacity = opacidade };
            pincel.Freeze();
            return pincel;
        }
    }
}
