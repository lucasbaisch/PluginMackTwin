using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using PluginCAISM.Configuracao;

namespace PluginCAISM.Interface
{
    /// <summary>
    /// Um cenário na lista de marcação da <see cref="JanelaDeSelecaoDeCenarios"/>.
    /// </summary>
    /// <remarks>
    /// Implementa <see cref="INotifyPropertyChanged"/> porque o limite de marcação é imposto pela
    /// própria lista: quando o teto é atingido, os itens ainda desmarcados são desabilitados, e
    /// isso só chega à tela se cada item avisar da mudança de <see cref="PodeMarcar"/>.
    /// </remarks>
    public class CenarioParaMarcar : INotifyPropertyChanged
    {
        private bool _marcado;
        private bool _podeMarcar = true;

        /// <summary>Ação chamada a cada mudança de <see cref="Marcado"/>, para reavaliar o limite.</summary>
        private readonly Action _aoMarcar;

        public CenarioParaMarcar(string nome, Action aoMarcar)
        {
            Nome = nome;
            _aoMarcar = aoMarcar;
        }

        /// <summary>Nome do cenário, como aparece nas sessões carregadas.</summary>
        public string Nome { get; }

        /// <summary>Se o usuário marcou este cenário.</summary>
        public bool Marcado
        {
            get => _marcado;
            set
            {
                if (_marcado == value)
                    return;

                _marcado = value;
                Notificar();
                _aoMarcar?.Invoke();
            }
        }

        /// <summary>
        /// Se a caixa está habilitada. Falsa apenas nos itens DESMARCADOS depois que o limite foi
        /// atingido — um item já marcado continua habilitado, senão não haveria como desfazer a
        /// escolha.
        /// </summary>
        public bool PodeMarcar
        {
            get => _podeMarcar;
            set
            {
                if (_podeMarcar == value)
                    return;

                _podeMarcar = value;
                Notificar();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Notificar([CallerMemberName] string propriedade = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propriedade));
        }
    }

    /// <summary>
    /// Diálogo modal que escolhe quais cenários receberão vista 3D no modelo aberto.
    /// </summary>
    /// <remarks>
    /// <b>Existe porque cada cenário é um arquivo Revit diferente.</b> Os hitpoints são elementos
    /// de MODELO, inseridos nas coordenadas registradas pelo HMD; despejar no modelo do Cenário 02
    /// as fixações coletadas no Cenário 01 produz esferas flutuando sobre a geometria errada — e
    /// nada no Revit sinaliza o engano depois de feito. Só quem está olhando para o modelo sabe
    /// qual arquivo está aberto, então a escolha é do usuário e é pedida ANTES de qualquer
    /// transação.
    ///
    /// O teto de <see cref="ConfiguracaoDeSelecaoDeCenarios.LimiteDeCenariosPorGeracao"/> marcações
    /// é imposto desabilitando os itens ainda desmarcados, e não recusando o clique: a caixa
    /// apagada explica sozinha por que não responde, enquanto um clique silenciosamente ignorado
    /// parece defeito.
    /// </remarks>
    public partial class JanelaDeSelecaoDeCenarios : Window
    {
        private readonly ObservableCollection<CenarioParaMarcar> _cenarios = new();

        /// <summary>
        /// Cenários marcados quando o usuário confirmou. Vazio se ele cancelou.
        /// </summary>
        public IReadOnlyList<string> CenariosEscolhidos { get; private set; } = new List<string>();

        /// <param name="cenariosDisponiveis">
        /// Cenários presentes nas sessões carregadas
        /// (<see cref="Dominio.Sessao.RotuloDeCenario"/>), na ordem em que devem aparecer.
        /// </param>
        /// <param name="cenariosPreMarcados">
        /// Cenários a já vir marcados — normalmente o que estiver escolhido no filtro da tela.
        /// Pode ser null.
        /// </param>
        public JanelaDeSelecaoDeCenarios(
            IEnumerable<string> cenariosDisponiveis,
            IEnumerable<string> cenariosPreMarcados = null)
        {
            InitializeComponent();

            var preMarcados = new HashSet<string>(
                cenariosPreMarcados ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var nome in cenariosDisponiveis ?? Enumerable.Empty<string>())
            {
                var item = new CenarioParaMarcar(nome, AoMudarMarcacao);

                // Pré-marcar respeita o mesmo teto: uma lista pré-marcada maior que o limite
                // deixaria a janela abrir já em estado inválido.
                if (preMarcados.Contains(nome)
                    && ContarMarcados() < ConfiguracaoDeSelecaoDeCenarios.LimiteDeCenariosPorGeracao)
                {
                    item.Marcado = true;
                }

                _cenarios.Add(item);
            }

            ListaDeCenarios.ItemsSource = _cenarios;
            AoMudarMarcacao();
        }

        private int ContarMarcados() => _cenarios.Count(cenario => cenario.Marcado);

        /// <summary>
        /// Reavalia o teto e o rodapé a cada marcação.
        /// </summary>
        private void AoMudarMarcacao()
        {
            int marcados = ContarMarcados();
            bool noTeto = marcados >= ConfiguracaoDeSelecaoDeCenarios.LimiteDeCenariosPorGeracao;

            foreach (var cenario in _cenarios)
                cenario.PodeMarcar = cenario.Marcado || !noTeto;

            BotaoOk.IsEnabled = marcados > 0;

            TextoDoContador.Text =
                $"{marcados} de {ConfiguracaoDeSelecaoDeCenarios.LimiteDeCenariosPorGeracao} " +
                $"cenário(s) marcado(s) — será gerada uma vista 3D para cada um." +
                (noTeto ? "  Limite atingido: desmarque um para trocar." : string.Empty);
        }

        private void BotaoOk_Click(object sender, RoutedEventArgs evento)
        {
            CenariosEscolhidos = _cenarios
                .Where(cenario => cenario.Marcado)
                .Select(cenario => cenario.Nome)
                .ToList();

            DialogResult = true;
        }
    }
}
