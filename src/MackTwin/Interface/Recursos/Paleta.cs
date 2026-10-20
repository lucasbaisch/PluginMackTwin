using System.Windows.Media;
using MackTwin.Configuracao;

namespace MackTwin.Interface.Recursos
{
    /// <summary>
    /// A paleta da interface — branco, cinzas neutros e o vermelho Mackenzie — num só lugar.
    /// </summary>
    /// <remarks>
    /// O XAML lê estas cores por <c>x:Static</c> (ver <c>Paleta.xaml</c>) e o código por
    /// <see cref="Pincel(Color)"/>; antes, os mesmos hexadecimais estavam repetidos em duas
    /// janelas e dois controles. As cores de ESTADO (confortável, abaixo, acima, sem dados) não
    /// estão aqui: são semânticas, compartilhadas com o modelo Revit, e vivem em
    /// <see cref="CoresDeConforto"/>; a paleta só as converte para WPF.
    /// </remarks>
    public static class Paleta
    {
        public static readonly Color Fundo = Cor("#F5F5F7");
        public static readonly Color Superficie = Cor("#FFFFFF");
        public static readonly Color Sutil = Cor("#FAFAFB");
        public static readonly Color Borda = Cor("#E6E6EA");
        public static readonly Color BordaMarca = Cor("#F0C4C8");
        public static readonly Color Texto = Cor("#1C1C1E");
        public static readonly Color Texto2 = Cor("#4A4A52");
        public static readonly Color Texto3 = Cor("#8A8A94");
        public static readonly Color Marca = Cor("#E30613");
        public static readonly Color MarcaEscura = Cor("#B5050F");
        public static readonly Color MarcaSuave = Cor("#FDECEE");
        public static readonly Color Grade = Cor("#ECECF0");
        public static readonly Color Eixo = Cor("#D9D9DE");
        public static readonly Color Desativado = Cor("#D9D9DE");
        public static readonly Color TextoDesativado = Cor("#9A9AA3");

        /// <summary>Linha da temperatura no gráfico: a mesma cor de "acima da faixa".</summary>
        public static readonly Color Temperatura = Cor(CoresDeConforto.AcimaDaFaixa);

        /// <summary>Linha da umidade no gráfico: a mesma cor de "abaixo da faixa".</summary>
        public static readonly Color Umidade = Cor(CoresDeConforto.AbaixoDaFaixa);

        /// <summary>Faixa de conforto sombreada atrás das curvas: verde de "confortável", translúcido.</summary>
        public static readonly Color FaixaDeConforto = ComAlfa(Cor(CoresDeConforto.Confortavel), 0x2E);

        /// <summary>Cor WPF a partir de <c>#RRGGBB</c>.</summary>
        public static Color Cor(string hexadecimal)
        {
            var (r, g, b) = CoresDeConforto.ParaRgb(hexadecimal);
            return Color.FromRgb(r, g, b);
        }

        /// <summary>Cor WPF de uma classificação de conforto.</summary>
        public static Color CorDeConforto(Dominio.ClassificacaoDeConforto classificacao)
        {
            return Cor(CoresDeConforto.Para(classificacao));
        }

        /// <summary>Pincel sólido congelado (seguro para reutilizar entre threads e controles).</summary>
        public static SolidColorBrush Pincel(Color cor)
        {
            var pincel = new SolidColorBrush(cor);
            pincel.Freeze();
            return pincel;
        }

        private static Color ComAlfa(Color cor, byte alfa) => Color.FromArgb(alfa, cor.R, cor.G, cor.B);
    }
}
