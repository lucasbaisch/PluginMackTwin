using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using MackTwin.Configuracao;
using MackTwin.Dominio;
using MackTwin.Interface.Controles;
using MackTwin.Interface.Modelos;
using MackTwin.Interface.Recursos;
using MackTwin.Processamento;
using MackTwin.Revit;
using UI = Autodesk.Revit.UI;

namespace MackTwin.Interface
{
    /// <summary>
    /// O painel de sensores por ambiente: lista de salas à esquerda e três visualizações da
    /// sala escolhida à direita (cartões ao vivo, série temporal, a sala no modelo).
    /// </summary>
    /// <remarks>
    /// A classe está em três arquivos, por responsabilidade: este (estado global, ciclo de
    /// atualização, exibição), <c>JanelaMackTwin.Revit.cs</c> (tudo o que toca o modelo) e
    /// <c>JanelaMackTwin.Planilhas.cs</c> (cadastro de planilhas dentro das salas).
    ///
    /// <b>Um estado global: Visualizar ou Editar.</b> O interruptor "Editar" do cabeçalho é a
    /// única chave. Desligado, a janela só mostra salas e leituras. Ligado, cada cartão de sala
    /// passa a cadastrar as SUAS planilhas — o selecionado com o cadastro completo, os demais
    /// com um resumo e o "+ adicionar".
    ///
    /// <b>A lista é atualizada em lugar.</b> As linhas são uma <see cref="ObservableCollection{T}"/>
    /// que persiste entre ciclos: cada sala existente é atualizada (e avisa os seus bindings),
    /// novas entram na posição certa, ausentes saem. Seleção e rolagem sobrevivem, e os cartões
    /// e o gráfico da sala exibida só são refeitos quando a sua <see cref="LinhaDeAmbiente.Assinatura"/>
    /// muda. Com a janela minimizada ou oculta o ciclo é adiado, e roda assim que ela volta.
    ///
    /// <b>Dois relógios, duas direções.</b> O <see cref="DispatcherTimer"/> puxa as planilhas a
    /// cada minuto; o evento <c>SelectionChanged</c> do Revit empurra para o painel a sala que o
    /// usuário clicou no modelo. Nenhum dos dois roda em contexto da API, então o que precisa do
    /// Revit entra pelo <see cref="DespachanteDoRevit"/>, que executa na thread da interface.
    ///
    /// <b>Rede de segurança.</b> Uma exceção não tratada na thread de interface derruba o Revit
    /// inteiro. <see cref="DispatcherFalhou"/> intercepta as que vêm do MackTwin, registra nos
    /// avisos e marca como tratadas; as do próprio Revit seguem com o Revit.
    /// </remarks>
    public partial class JanelaMackTwin : Window
    {
        private readonly UI.UIApplication _uiApp;
        private readonly DespachanteDoRevit _despachante;
        private readonly RegistroDePlanilhas _registro;
        private readonly MonitorDePlanilhas _monitor;
        private readonly DispatcherTimer _temporizador;
        private readonly List<string> _avisosIniciais = new();
        private readonly ObservableCollection<LinhaDeAmbiente> _linhas = new();

        private ConjuntoDeAmbientes? _conjunto;
        private int _ultimoTotalDeRooms;
        private string? _ultimoTituloDoModelo;
        private bool _atualizando;
        private bool _atualizacaoAdiada;
        private string? _assinaturaExibida;
        private CancellationTokenSource? _cancelamento;

        /// <summary>O estado global: true = Editar, false = Visualizar.</summary>
        private bool ModoEdicao => InterruptorDeEdicao.IsChecked == true;

        // ── Construtor ───────────────────────────────────────────────────

        public JanelaMackTwin(UI.UIApplication uiApp, DespachanteDoRevit despachante)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _despachante = despachante ?? throw new ArgumentNullException(nameof(despachante));
            _registro = RegistroDePlanilhas.Carregar(_avisosIniciais);
            _monitor = new MonitorDePlanilhas(_registro);

            _temporizador = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(ConfiguracaoDaPlanilhaGoogle.IntervaloDeAtualizacaoEmSegundos),
            };
            _temporizador.Tick += Temporizador_Tick;

            ListaDeAmbientes.ItemsSource = _linhas;
            InicializarLegenda();

            _despachante.Falhou += DespachanteFalhou;
            _uiApp.SelectionChanged += SelecaoNoRevitAlterada;
            Dispatcher.UnhandledException += DispatcherFalhou;

            Loaded += JanelaCarregada;
            Closed += JanelaFechada;
            StateChanged += JanelaVisibilidadeAlterada;
            IsVisibleChanged += (remetente, mudanca) => JanelaVisibilidadeAlterada(remetente, EventArgs.Empty);
        }

        private async void JanelaCarregada(object sender, RoutedEventArgs evento)
        {
            _temporizador.Start();
            await AtualizarAsync();
        }

        private void JanelaFechada(object? sender, EventArgs evento)
        {
            _temporizador.Stop();
            _cancelamento?.Cancel();
            _uiApp.SelectionChanged -= SelecaoNoRevitAlterada;
            _despachante.Falhou -= DespachanteFalhou;
            Dispatcher.UnhandledException -= DispatcherFalhou;
        }

        // ── Estado global: Visualizar / Editar ───────────────────────────

        private void InterruptorDeEdicao_Alterado(object sender, RoutedEventArgs evento)
        {
            bool editar = ModoEdicao;

            TextoTituloDeSalas.Text = editar ? "SALAS  ·  MODO EDIÇÃO" : "SALAS";
            TextoDicaDeEdicao.Visibility = editar ? Visibility.Visible : Visibility.Collapsed;
            BotaoAdicionarSala.Visibility = editar ? Visibility.Visible : Visibility.Collapsed;
            TextoEditar.Foreground = Paleta.Pincel(editar ? Paleta.Marca : Paleta.Texto3);
            TextoEditar.FontWeight = editar ? FontWeights.Bold : FontWeights.Normal;

            ReexibirConjunto();
        }

        // ── Ciclo de atualização ─────────────────────────────────────────

        private bool EstaVisivel => IsVisible && WindowState != WindowState.Minimized;

        private async void Temporizador_Tick(object? sender, EventArgs evento)
        {
            if (CaixaAuto.IsChecked != true)
                return;

            if (!EstaVisivel)
            {
                // Ninguém está olhando: o ciclo fica devido e roda quando a janela voltar.
                _atualizacaoAdiada = true;
                return;
            }

            await AtualizarAsync();
        }

        private async void JanelaVisibilidadeAlterada(object? sender, EventArgs evento)
        {
            if (_atualizacaoAdiada && EstaVisivel)
            {
                _atualizacaoAdiada = false;
                await AtualizarAsync();
            }
        }

        private async void BotaoAtualizar_Click(object sender, RoutedEventArgs evento)
        {
            await AtualizarAsync();
        }

        /// <summary>Baixa as planilhas e, pelo despachante, casa as salas com o modelo e exibe.</summary>
        private async Task AtualizarAsync()
        {
            if (_atualizando)
                return;

            _atualizando = true;
            ExibirOcupado(true);
            _cancelamento = new CancellationTokenSource();

            try
            {
                var conjunto = await _monitor.AtualizarAsync(_cancelamento.Token);

                // Ler os Rooms exige a API do Revit: vai pelo despachante, que chama de volta
                // na thread da interface, em contexto válido.
                _despachante.Executar(app => CasarEExibir(app, conjunto));
            }
            catch (OperationCanceledException)
            {
                // Janela fechada durante o download.
            }
            catch (Exception excecao)
            {
                Informar("Falha na atualização: " + excecao.Message);
            }
            finally
            {
                _atualizando = false;
                ExibirOcupado(false);
            }
        }

        private void CasarEExibir(UI.UIApplication app, ConjuntoDeAmbientes conjunto)
        {
            if (!IsLoaded)
                return; // fechada enquanto o Revit estava ocupado

            int casados = 0;
            int rooms = 0;
            string? erroDeCasamento = null;
            var uiDocumento = app.ActiveUIDocument;

            if (uiDocumento != null && !uiDocumento.Document.IsFamilyDocument)
            {
                try
                {
                    var seletor = new SeletorDeAmbiente(uiDocumento.Document);
                    rooms = seletor.ColetarRooms().Count;
                    casados = seletor.CasarComOModelo(conjunto);
                }
                catch (Exception excecao)
                {
                    erroDeCasamento = excecao.Message;
                }
            }

            _conjunto = conjunto;
            _ultimoTotalDeRooms = rooms;
            _ultimoTituloDoModelo = uiDocumento?.Document.Title;

            ExibirConjunto(conjunto, casados, rooms, _ultimoTituloDoModelo, erroDeCasamento);
        }

        // ── Exibição do conjunto ─────────────────────────────────────────

        /// <summary>Refaz a lista com o conjunto já carregado — sem baixar nada.</summary>
        private void ReexibirConjunto()
        {
            if (_conjunto == null)
                return;

            int casados = _conjunto.Ambientes.Count(ambiente => ambiente.CasadoComOModelo);
            ExibirConjunto(_conjunto, casados, _ultimoTotalDeRooms, _ultimoTituloDoModelo, null);
        }

        private void ExibirConjunto(
            ConjuntoDeAmbientes conjunto, int casados, int rooms, string? tituloDoModelo, string? erroDeCasamento)
        {
            TimeSpan? janela = JanelaSelecionada;
            DateTime agora = DateTime.Now;
            bool editar = ModoEdicao;

            // Linhas atualizadas em lugar: a lista mantém seleção, rolagem e itens visuais.
            var existentes = _linhas.ToDictionary(linha => linha.Codigo, StringComparer.Ordinal);
            var vistos = new HashSet<string>(StringComparer.Ordinal);
            int posicao = 0;

            foreach (var ambiente in conjunto.Ambientes)
            {
                var resumo = CalculadorDeResumoDoAmbiente.Resumir(ambiente, janela, agora);
                var planilhas = _registro.PlanilhasDaSala(ambiente.Codigo, conjunto.SalasPorPlanilha);

                if (existentes.TryGetValue(ambiente.Codigo, out var linha))
                {
                    linha.Atualizar(ambiente, resumo, planilhas, editar, janela, agora);

                    int atual = _linhas.IndexOf(linha);
                    if (atual != posicao)
                        _linhas.Move(atual, posicao);
                }
                else
                {
                    _linhas.Insert(posicao, new LinhaDeAmbiente(ambiente, resumo, planilhas, editar, janela, agora));
                }

                vistos.Add(ambiente.Codigo);
                posicao++;
            }

            for (int indice = _linhas.Count - 1; indice >= 0; indice--)
            {
                if (!vistos.Contains(_linhas[indice].Codigo))
                    _linhas.RemoveAt(indice);
            }

            TextoStatus.Text = string.Format(CultureInfo.CurrentCulture,
                "Atualizado às {0:HH:mm:ss}{1}",
                conjunto.AtualizadoEm,
                conjunto.PlanilhasComFalha > 0
                    ? $" — {conjunto.PlanilhasComFalha} planilha(s) falharam, veja os avisos."
                    : string.Empty);

            TextoResumoDoCiclo.Text = string.Format(CultureInfo.CurrentCulture,
                "{0} sala(s), {1} sensor(es), {2:N0} leitura(s).",
                conjunto.Ambientes.Count, conjunto.TotalDeSensores, conjunto.TotalDeLeituras);

            TextoAvisos.Text = string.Join(Environment.NewLine, _avisosIniciais.Concat(conjunto.Avisos));
            AcrescentarAviso(MontarTextoDeCasamento(conjunto, casados, rooms, tituloDoModelo, erroDeCasamento));

            // Sensores fora do padrão já vêm nos avisos do agrupador; uma planilha que não
            // alimenta sala alguma não tem onde aparecer na lista, então avisa-se aqui.
            var planilhasOrfas = _registro.PlanilhasSemSala(conjunto.SalasPorPlanilha);

            if (planilhasOrfas.Count > 0)
            {
                AcrescentarAviso(
                    "Planilha(s) sem sala (nenhum HOSTNAME no padrão, ou download com falha): " +
                    string.Join(", ", planilhasOrfas.Select(p => $"\"{p.Nome}\"")) +
                    ". Ligue \"Editar\" e cadastre-a(s) de novo a partir da sala certa.");
            }

            if (conjunto.PlanilhasComFalha > 0 || planilhasOrfas.Count > 0 || conjunto.SensoresSemSala.Count > 0)
                ExpansorDeAvisos.IsExpanded = true;

            ExibirSalaSeMudou();
        }

        /// <summary>Garante uma sala selecionada e só refaz cartões e gráfico se ela mudou.</summary>
        private void ExibirSalaSeMudou()
        {
            if (ListaDeAmbientes.SelectedItem is not LinhaDeAmbiente linha)
            {
                if (_linhas.Count > 0)
                    ListaDeAmbientes.SelectedItem = _linhas[0]; // dispara ExibirSala
                else
                    ExibirSala(null);

                return;
            }

            if (linha.Assinatura != _assinaturaExibida)
                ExibirSala(linha);
        }

        private static string MontarTextoDeCasamento(
            ConjuntoDeAmbientes conjunto, int casados, int rooms, string? tituloDoModelo, string? erro)
        {
            if (erro != null)
                return "Não foi possível ler os Rooms do modelo: " + erro;

            if (tituloDoModelo == null)
                return "Nenhum modelo aberto: as salas são exibidas só a partir das planilhas.";

            var naoCasadas = conjunto.Ambientes
                .Where(ambiente => !ambiente.CasadoComOModelo)
                .Select(ambiente => ambiente.Codigo)
                .ToList();

            string texto =
                $"Modelo \"{tituloDoModelo}\": {rooms} Room(s) colocado(s); {casados} de {conjunto.Ambientes.Count} " +
                "sala(s) das planilhas casaram pelo número (ou nome) do Room.";

            if (naoCasadas.Count > 0)
                texto += $" Sem Room correspondente: {string.Join(", ", naoCasadas)} — numere o Room com esse código para casar.";

            return texto;
        }

        // ── Sala selecionada: as três visualizações ──────────────────────

        private void ListaDeAmbientes_SelectionChanged(object sender, SelectionChangedEventArgs evento)
        {
            ExibirSala(ListaDeAmbientes.SelectedItem as LinhaDeAmbiente);
        }

        private void ExibirSala(LinhaDeAmbiente? linha)
        {
            PainelDeCartoes.Children.Clear();
            PainelDoGrafico.Content = null;
            _assinaturaExibida = linha?.Assinatura;

            if (linha == null)
            {
                TextoTituloDaSala.Text = _conjunto == null ? "Selecione uma sala" : "Nenhuma sala ainda";

                if (_conjunto != null)
                    AcrescentarAviso("Nenhuma sala ainda: ligue \"Editar\" e use \"+ Adicionar sala\", ou cadastre uma planilha " +
                                     "cujos sensores sigam o padrão MMCCT<sala>-<nnn> (ex.: MMCCTLAB001-001).");

                return;
            }

            TextoTituloDaSala.Text = linha.Titulo;

            // (a) cartões ao vivo — um por sensor da sala
            foreach (var sensor in linha.Ambiente.Sensores)
                PainelDeCartoes.Children.Add(new CartaoDeSensor(sensor));

            // (b) gráficos — as leituras da sala no período, sobre o eixo do período inteiro
            try
            {
                TimeSpan? janela = JanelaSelecionada;
                DateTime agora = DateTime.Now;
                var todas = linha.Ambiente.Leituras;
                var noPeriodo = CalculadorDeResumoDoAmbiente.FiltrarJanela(todas, janela, agora);

                DateTime inicioDoEixo, fimDoEixo;

                if (janela.HasValue)
                {
                    inicioDoEixo = agora - janela.Value;
                    fimDoEixo = agora;
                }
                else if (todas.Count > 0)
                {
                    inicioDoEixo = todas[0].Timestamp;
                    fimDoEixo = todas[todas.Count - 1].Timestamp;
                }
                else
                {
                    inicioDoEixo = agora.AddHours(-1);
                    fimDoEixo = agora;
                }

                var balde = AgregadorDeSerie.BaldeRecomendado(fimDoEixo - inicioDoEixo);
                PainelDoGrafico.Content = new ControleGraficoDeSerieTemporal(noPeriodo, inicioDoEixo, fimDoEixo, balde);
            }
            catch (Exception excecao)
            {
                PainelDoGrafico.Content = new TextBlock
                {
                    Text = "Não foi possível montar o gráfico: " + excecao.Message,
                    TextWrapping = TextWrapping.Wrap,
                };
                AcrescentarAviso("Gráfico: " + excecao.Message);
            }

            // (c) no modelo — os botões da aba usam a seleção corrente; nada a montar aqui.
        }

        // ── Período (botões segmentados) ─────────────────────────────────

        /// <summary>
        /// O período escolhido nos botões: horas no <c>Tag</c> do botão marcado; <c>Tag</c>
        /// vazio = tudo. Contado do relógio de agora para trás.
        /// </summary>
        private TimeSpan? JanelaSelecionada
        {
            get
            {
                foreach (var filho in PainelDePeriodos.Children)
                {
                    if (filho is RadioButton { IsChecked: true } botao)
                    {
                        return int.TryParse(botao.Tag as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int horas)
                            ? TimeSpan.FromHours(horas)
                            : null;
                    }
                }

                return CalculadorDeResumoDoAmbiente.JanelaPadrao;
            }
        }

        private void Periodo_Checked(object sender, RoutedEventArgs evento)
        {
            // O período muda o resumo (e a cor) de todas as salas: refaz as linhas sem baixar nada.
            ReexibirConjunto();
        }

        /// <summary>
        /// Os resumos já calculados para a lista, por código de sala — para pintar e exportar
        /// sem resumir tudo de novo.
        /// </summary>
        private Func<Ambiente, ResumoDoAmbiente> ResumidorAtual()
        {
            var resumos = _linhas.ToDictionary(linha => linha.Codigo, linha => linha.Resumo, StringComparer.Ordinal);
            TimeSpan? janela = JanelaSelecionada;
            DateTime agora = DateTime.Now;

            return ambiente => resumos.TryGetValue(ambiente.Codigo, out var resumo)
                ? resumo
                : CalculadorDeResumoDoAmbiente.Resumir(ambiente, janela, agora);
        }

        // ── Legenda ──────────────────────────────────────────────────────

        private void InicializarLegenda()
        {
            var estados = new[]
            {
                ClassificacaoDeConforto.Confortavel,
                ClassificacaoDeConforto.AbaixoDaFaixa,
                ClassificacaoDeConforto.AcimaDaFaixa,
                ClassificacaoDeConforto.SemDados,
            };

            foreach (var estado in estados)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };

                item.Children.Add(new Ellipse
                {
                    Width = 11,
                    Height = 11,
                    Fill = Paleta.Pincel(Paleta.CorDeConforto(estado)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                });

                item.Children.Add(new TextBlock
                {
                    Text = ClassificadorDeConforto.Descrever(estado),
                    VerticalAlignment = VerticalAlignment.Center,
                });

                PainelDeLegenda.Children.Add(item);
            }
        }

        // ── Utilitários ──────────────────────────────────────────────────

        private void Informar(string mensagem)
        {
            TextoStatus.Text = mensagem;
            AcrescentarAviso(mensagem);
        }

        private void AcrescentarAviso(string mensagem)
        {
            string prefixo = TextoAvisos.Text.Length == 0 ? string.Empty : Environment.NewLine;
            TextoAvisos.AppendText($"{prefixo}[{DateTime.Now:HH:mm:ss}] {mensagem}");
            TextoAvisos.ScrollToEnd();
        }

        private void DespachanteFalhou(Exception excecao)
        {
            Informar("Falha no Revit: " + excecao.Message);
        }

        /// <summary>
        /// Intercepta exceções não tratadas da thread de interface que tenham origem no MackTwin,
        /// para que um erro do painel não derrube o Revit. As demais seguem para o Revit.
        /// </summary>
        private void DispatcherFalhou(object sender, DispatcherUnhandledExceptionEventArgs evento)
        {
            string rastro = evento.Exception.ToString();

            if (!rastro.Contains("MackTwin", StringComparison.Ordinal))
                return;

            evento.Handled = true;

            try
            {
                AcrescentarAviso("Erro interno do painel (capturado, o Revit segue): " + evento.Exception.Message);
                ExpansorDeAvisos.IsExpanded = true;
                TextoStatus.Text = "Erro interno do painel — veja os avisos.";
            }
            catch (Exception)
            {
                // Se nem o aviso pôde ser exibido, o importante já foi feito: o Revit não cai.
            }
        }

        private void ExibirOcupado(bool ocupado)
        {
            Spinner.Visibility = ocupado ? Visibility.Visible : Visibility.Collapsed;
            BotaoAtualizar.IsEnabled = !ocupado;
        }
    }
}
