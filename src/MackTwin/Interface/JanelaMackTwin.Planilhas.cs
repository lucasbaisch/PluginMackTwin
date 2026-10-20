using System;
using System.Linq;
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


        // ── Salas cadastradas à mão ──────────────────────────────────────

        private async void BotaoAdicionarSala_Click(object sender, RoutedEventArgs evento)
        {
            var dialogo = new JanelaDeCadastroDeSala { Owner = this };

            if (dialogo.ShowDialog() != true)
                return;

            string codigo;

            try
            {
                codigo = _registro.AdicionarSala(dialogo.Codigo, dialogo.Nome).Codigo;
            }
            catch (Exception excecao)
            {
                MessageBox.Show(this, excecao.Message, "Adicionar sala", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Informar($"Sala {codigo} cadastrada. Numere o Room do modelo com \"{codigo}\" para ela casar, e cadastre as planilhas dela aqui.");
            await AtualizarAsync();
            SelecionarSala(codigo);
        }

        private async void RemoverSala_Click(object sender, RoutedEventArgs evento)
        {
            if (Contexto<LinhaDeAmbiente>(sender) is not { } linha || !linha.PodeRemoverSala)
                return;

            int planilhasDaSala = _registro.Planilhas.Count(planilha => planilha.Sala == linha.Codigo);

            var resposta = MessageBox.Show(this,
                $"Remover a sala {linha.Codigo} do cadastro?" +
                (planilhasDaSala > 0 ? $" A(s) {planilhasDaSala} planilha(s) cadastrada(s) nela será(ão) removida(s) também." : string.Empty),
                "Remover sala", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (resposta != MessageBoxResult.Yes)
                return;

            try
            {
                _registro.Planilhas.RemoveAll(planilha => planilha.Sala == linha.Codigo);
                _registro.RemoverSala(linha.Codigo);
                _registro.Salvar();
            }
            catch (Exception excecao)
            {
                MessageBox.Show(this, excecao.Message, "Remover sala", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await AtualizarAsync();
        }

        /// <summary>Seleciona na lista a sala com o código informado, se ela já estiver lá.</summary>
        private void SelecionarSala(string codigo)
        {
            foreach (var linha in _linhas)
            {
                if (linha.Codigo == codigo)
                {
                    ListaDeAmbientes.SelectedItem = linha;
                    ListaDeAmbientes.ScrollIntoView(linha);
                    return;
                }
            }
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
