using System;
using System.Windows;
using System.Windows.Controls;
using MackTwin.Configuracao;
using MackTwin.Interface.Modelos;

namespace MackTwin.Interface
{
    /// <summary>
    /// A parte da janela que cadastra planilhas — sempre dentro de uma sala, no modo Editar.
    /// </summary>
    /// <remarks>
    /// Os botões dos cartões carregam no <c>Tag</c> o objeto a que se referem (a linha da sala
    /// ou a planilha); <see cref="Contexto{T}"/> o recupera. Toda alteração grava o cadastro e,
    /// quando muda o que há para baixar, dispara um ciclo.
    /// </remarks>
    public partial class JanelaMackTwin
    {
        /// <summary>O objeto que o botão carrega: o Tag, ou o DataContext do item.</summary>
        private static T? Contexto<T>(object sender) where T : class
        {
            var elemento = sender as FrameworkElement;
            return elemento?.Tag as T ?? elemento?.DataContext as T;
        }

        private async void AdicionarPlanilhaNaSala_Click(object sender, RoutedEventArgs evento)
        {
            if (Contexto<LinhaDeAmbiente>(sender) is not { } linha)
                return;

            ListaDeAmbientes.SelectedItem = linha;

            var dialogo = new JanelaDeCadastroDePlanilha(linha.Codigo, null) { Owner = this };

            if (dialogo.ShowDialog() != true)
                return;

            try
            {
                _registro.Adicionar(dialogo.Nome, dialogo.Url, linha.Codigo);
            }
            catch (Exception excecao)
            {
                MessageBox.Show(this, excecao.Message, "Cadastro de planilha", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Informar($"Planilha \"{dialogo.Nome}\" cadastrada na sala {linha.Codigo}.");
            await AtualizarAsync();
        }

        private async void EditarPlanilha_Click(object sender, RoutedEventArgs evento)
        {
            if (Contexto<PlanilhaRegistrada>(sender) is not { } planilha)
                return;

            var dialogo = new JanelaDeCadastroDePlanilha(planilha.Sala, planilha) { Owner = this };

            if (dialogo.ShowDialog() != true)
                return;

            planilha.Nome = dialogo.Nome;
            planilha.Url = dialogo.Url;

            try
            {
                _registro.Salvar();
            }
            catch (Exception excecao)
            {
                MessageBox.Show(this, excecao.Message, "Editar planilha", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await AtualizarAsync();
        }

        private async void RemoverPlanilha_Click(object sender, RoutedEventArgs evento)
        {
            if (Contexto<PlanilhaRegistrada>(sender) is not { } planilha)
                return;

            var resposta = MessageBox.Show(this, $"Remover \"{planilha.Nome}\" do cadastro?", "Remover planilha",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (resposta != MessageBoxResult.Yes)
                return;

            try
            {
                _registro.Remover(planilha);
            }
            catch (Exception excecao)
            {
                MessageBox.Show(this, excecao.Message, "Remover planilha", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await AtualizarAsync();
        }

        /// <summary>Planilha sem sala: passa a pertencer à sala selecionada na lista.</summary>
        private void AtribuirPlanilha_Click(object sender, RoutedEventArgs evento)
        {
            if (Contexto<PlanilhaRegistrada>(sender) is not { } planilha)
                return;

            if (ListaDeAmbientes.SelectedItem is not LinhaDeAmbiente linha)
            {
                Informar("Selecione na lista a sala à qual a planilha pertence e clique de novo em \"atribuir\".");
                return;
            }

            planilha.Sala = linha.Codigo;

            try
            {
                _registro.Salvar();
            }
            catch (Exception excecao)
            {
                AcrescentarAviso("Não foi possível gravar o cadastro de planilhas: " + excecao.Message);
            }

            Informar($"Planilha \"{planilha.Nome}\" atribuída à sala {linha.Codigo}.");
            ReexibirConjunto();
        }

        private void PlanilhaAtivaAlterada(object sender, RoutedEventArgs evento)
        {
            // O evento também dispara quando o binding aplica o valor inicial, antes de o
            // controle carregar; só a mudança feita pelo usuário merece ida ao disco.
            if (sender is CheckBox { IsLoaded: false })
                return;

            try
            {
                _registro.Salvar();
            }
            catch (Exception excecao)
            {
                AcrescentarAviso("Não foi possível gravar o cadastro de planilhas: " + excecao.Message);
            }
        }
    }
}
