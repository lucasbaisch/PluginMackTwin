using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MackTwin.Configuracao;
using MackTwin.Parsing;
using MackTwin.Processamento;

namespace MackTwin.Interface
{
    /// <summary>
    /// Diálogo que cadastra ou edita uma planilha: nome, endereço e um teste de conexão que
    /// mostra quantas leituras e quais salas a planilha traz antes de salvar.
    /// </summary>
    /// <remarks>
    /// Aberto de dentro do cartão de uma sala, diz no topo a que sala a planilha vai pertencer —
    /// é o que torna a associação óbvia para quem cadastra. O teste existe para que o erro mais
    /// comum — planilha não compartilhada publicamente — apareça aqui, com a explicação do
    /// leitor, e não como uma sala que nunca chega ao painel.
    /// </remarks>
    public partial class JanelaDeCadastroDePlanilha : Window
    {
        /// <summary>Nome informado, aparado.</summary>
        public string Nome => CaixaNome.Text.Trim();

        /// <summary>Endereço informado, aparado.</summary>
        public string Url => CaixaUrl.Text.Trim();

        /// <summary>
        /// Monta o diálogo para a sala informada; com <paramref name="existente"/>, em modo de edição.
        /// </summary>
        /// <param name="sala">Código da sala a que a planilha pertence, ou null.</param>
        /// <param name="existente">Planilha a editar, ou null para cadastrar uma nova.</param>
        public JanelaDeCadastroDePlanilha(string? sala = null, PlanilhaRegistrada? existente = null)
        {
            InitializeComponent();

            string? codigo = RegistroDePlanilhas.NormalizarSala(sala);

            if (existente != null)
            {
                Title = "Editar planilha";
                BotaoSalvar.Content = "Salvar alterações";
                CaixaNome.Text = existente.Nome;
                CaixaUrl.Text = existente.Url;
            }
            else if (codigo != null)
            {
                Title = $"Adicionar planilha a {codigo}";
            }

            TextoSala.Text = codigo != null
                ? $"Sala {codigo}. A planilha fica cadastrada nesta sala; sensores de outras salas que ela " +
                  "trouxer (pelo HOSTNAME) continuam indo para as salas deles."
                : "Sem sala definida: a sala será inferida pelo HOSTNAME dos sensores da planilha.";

            Loaded += (remetente, evento) => CaixaNome.Focus();
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
