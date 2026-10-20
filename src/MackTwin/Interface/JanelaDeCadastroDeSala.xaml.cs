using System.Windows;
using MackTwin.Configuracao;

namespace MackTwin.Interface
{
    /// <summary>
    /// Diálogo que cadastra uma sala à mão: código (a chave que casa Room e HOSTNAME) e nome
    /// opcional.
    /// </summary>
    /// <remarks>
    /// Serve para preparar a sala antes de o sensor existir — e para que o cadastro das
    /// planilhas dela tenha onde morar. Quando um sensor com esse código aparecer, a sala
    /// cadastrada e a anunciada pelo HOSTNAME são a mesma.
    /// </remarks>
    public partial class JanelaDeCadastroDeSala : Window
    {
        /// <summary>Código informado, aparado (a normalização é do cadastro).</summary>
        public string Codigo => CaixaCodigo.Text.Trim();

        /// <summary>Nome informado, aparado, ou null se vazio.</summary>
        public string? Nome => string.IsNullOrWhiteSpace(CaixaNome.Text) ? null : CaixaNome.Text.Trim();

        public JanelaDeCadastroDeSala()
        {
            InitializeComponent();
            Loaded += (remetente, evento) => CaixaCodigo.Focus();
        }

        private void BotaoSalvar_Click(object sender, RoutedEventArgs evento)
        {
            string? normalizado = RegistroDePlanilhas.NormalizarSala(Codigo);

            if (normalizado == null)
            {
                TextoResultado.Text = "Informe o código da sala — só letras e dígitos contam (ex.: LAB002).";
                CaixaCodigo.Focus();
                return;
            }

            DialogResult = true;
        }
    }
}
