using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MackTwin.Parsing;
using MackTwin.Processamento;

namespace MackTwin.Interface
{
    /// <summary>
    /// Diálogo que cadastra uma planilha nova: nome, endereço e um teste de conexão que mostra
    /// quantas leituras e quais salas a planilha traz antes de salvar.
    /// </summary>
    /// <remarks>
    /// O teste existe para que o erro mais comum — planilha não compartilhada publicamente —
    /// apareça aqui, com a explicação do leitor, e não como uma sala que nunca chega ao painel.
    /// </remarks>
    public partial class JanelaDeCadastroDePlanilha : Window
    {
        /// <summary>Nome informado, aparado.</summary>
        public string Nome => CaixaNome.Text.Trim();

        /// <summary>Endereço informado, aparado.</summary>
        public string Url => CaixaUrl.Text.Trim();

        public JanelaDeCadastroDePlanilha()
        {
            InitializeComponent();
            Loaded += (s, e) => CaixaNome.Focus();
        }

        private async void BotaoTestar_Click(object sender, RoutedEventArgs evento)
        {
            BotaoTestar.IsEnabled = false;
            BotaoSalvar.IsEnabled = false;
            TextoResultado.Text = "Baixando…";

            try
            {
                string urlDeExportacao = LeitorDePlanilhaGoogle.MontarUrlDeExportacaoCsv(Url);
                var avisos = new List<string>();
                var leituras = await new LeitorDePlanilhaGoogle().BaixarELerAsync(urlDeExportacao, avisos);
                var conjunto = AgrupadorDeAmbientes.Agrupar(leituras, avisos);

                string salas = conjunto.Ambientes.Count == 0
                    ? "nenhuma sala identificável pelo HOSTNAME"
                    : "sala(s) " + string.Join(", ", conjunto.Ambientes.Select(a => a.Codigo));

                TextoResultado.Text =
                    $"OK: {leituras.Count} leitura(s), {conjunto.TotalDeSensores} sensor(es), {salas}." +
                    (conjunto.SensoresSemSala.Count > 0
                        ? $" {conjunto.SensoresSemSala.Count} sensor(es) sem sala."
                        : string.Empty);
            }
            catch (Exception excecao)
            {
                TextoResultado.Text = "Falha: " + excecao.Message;
            }
            finally
            {
                BotaoTestar.IsEnabled = true;
                BotaoSalvar.IsEnabled = true;
            }
        }

        private void BotaoSalvar_Click(object sender, RoutedEventArgs evento)
        {
            try
            {
                LeitorDePlanilhaGoogle.MontarUrlDeExportacaoCsv(Url);
            }
            catch (ArgumentException excecao)
            {
                TextoResultado.Text = excecao.Message;
                CaixaUrl.Focus();
                return;
            }

            DialogResult = true;
        }
    }
}
