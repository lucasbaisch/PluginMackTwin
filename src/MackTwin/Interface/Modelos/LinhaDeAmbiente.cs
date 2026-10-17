using System.Globalization;
using System.Windows.Media;
using MackTwin.Configuracao;
using MackTwin.Dominio;
using MackTwin.Processamento;

namespace MackTwin.Interface.Modelos
{
    /// <summary>
    /// Uma sala na lista da janela: o ambiente, o seu resumo e os textos/cores prontos para o
    /// <c>DataTemplate</c>.
    /// </summary>
    /// <remarks>
    /// Tudo o que a lista exibe é calculado aqui, uma vez por ciclo, para que o XAML só faça
    /// <c>Binding</c> direto — sem conversores e sem lógica de formatação espalhada.
    /// </remarks>
    public sealed class LinhaDeAmbiente
    {
        /// <summary>A sala.</summary>
        public Ambiente Ambiente { get; }

        /// <summary>O resumo na janela de tempo escolhida.</summary>
        public ResumoDoAmbiente Resumo { get; }

        /// <summary>Código da sala (chave).</summary>
        public string Codigo => Ambiente.Codigo;

        /// <summary>Título: código e, se casou, o nome no modelo.</summary>
        public string Titulo => Ambiente.Titulo;

        /// <summary>Última temperatura e umidade, com a hora.</summary>
        public string Detalhe { get; }

        /// <summary>"Room nº X no modelo" ou "sem Room correspondente".</summary>
        public string Casamento { get; }

        /// <summary>Pincel da classificação geral, para o indicador colorido.</summary>
        public Brush Cor { get; }

        /// <summary>Texto da classificação geral.</summary>
        public string Classificacao => ClassificadorDeConforto.Descrever(Resumo.ClassificacaoGeral);

        public LinhaDeAmbiente(Ambiente ambiente, ResumoDoAmbiente resumo)
        {
            Ambiente = ambiente;
            Resumo = resumo;

            Detalhe = MontarDetalhe(resumo);
            Casamento = ambiente.CasadoComOModelo
                ? $"Room {ambiente.NumeroNoModelo} no modelo"
                : "sem Room correspondente no modelo";

            var (r, g, b) = CoresDeConforto.ParaRgb(CoresDeConforto.Para(resumo.ClassificacaoGeral));
            var pincel = new SolidColorBrush(Color.FromRgb(r, g, b));
            pincel.Freeze();
            Cor = pincel;
        }

        private static string MontarDetalhe(ResumoDoAmbiente resumo)
        {
            if (resumo.UltimaLeitura == null)
                return "sem leituras";

            var cultura = CultureInfo.CurrentCulture;
            string temperatura = resumo.Temperatura.TemDados
                ? string.Format(cultura, "{0:0.0} °C", resumo.Temperatura.Ultimo)
                : "— °C";
            string umidade = resumo.Umidade.TemDados
                ? string.Format(cultura, "{0:0} %", resumo.Umidade.Ultimo)
                : "— %";

            return string.Format(cultura, "{0} · {1} · {2:dd/MM HH:mm}",
                temperatura, umidade, resumo.UltimaLeitura.Timestamp);
        }

        public override string ToString() => Titulo;
    }
}
