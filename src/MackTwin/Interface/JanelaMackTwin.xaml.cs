using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Autodesk.Revit.DB.Architecture;
using MackTwin.Configuracao;
using MackTwin.Dominio;
using MackTwin.Interface.Controles;
using MackTwin.Interface.Modelos;
using MackTwin.Processamento;
using MackTwin.Revit;
using DB = Autodesk.Revit.DB;
using UI = Autodesk.Revit.UI;
using EventosDaUI = Autodesk.Revit.UI.Events;

namespace MackTwin.Interface
{
    /// <summary>
    /// O painel de sensores por ambiente: lista de salas à esquerda, três visualizações da sala
    /// escolhida no centro (cartões ao vivo, série temporal, a sala no modelo) e o cadastro de
    /// planilhas à direita.
    /// </summary>
    /// <remarks>
    /// <b>Dois relógios, duas direções.</b> O <see cref="DispatcherTimer"/> puxa as planilhas a
    /// cada minuto e refaz a lista; o evento <c>SelectionChanged</c> do Revit empurra para o
    /// painel a sala que o usuário clicou no modelo. Nenhum dos dois roda em contexto da API,
    /// então o que precisa do Revit (ler Rooms, pintar, gerar vista) entra pelo
    /// <see cref="DespachanteDoRevit"/>, que executa na thread da interface — por isso os
    /// handlers podem tocar os controles diretamente.
    ///
    /// <b>A janela de tempo é uma só</b> para as três visualizações: a cor da lista, a
    /// classificação do cabeçalho, o gráfico e a pintura no modelo usam o mesmo resumo,
    /// recalculado quando a janela muda — sem baixar nada de novo.
    ///
    /// <b>Rede de segurança.</b> Uma exceção não tratada na thread de interface derruba o Revit
    /// inteiro — aconteceu com o gráfico. <see cref="DispatcherFalhou"/> intercepta as que vêm
    /// do MackTwin, registra nos avisos e marca como tratadas; as do próprio Revit seguem com o
    /// Revit.
    /// </remarks>
    public partial class JanelaMackTwin : Window
    {
        private sealed class OpcaoDeJanela
        {
            public string Rotulo { get; init; } = string.Empty;
            public TimeSpan? Janela { get; init; }
            public override string ToString() => Rotulo;
        }

        private static readonly OpcaoDeJanela[] OpcoesDeJanela =
        {
            new() { Rotulo = "Última hora", Janela = TimeSpan.FromHours(1) },
            new() { Rotulo = "Últimas 6 horas", Janela = TimeSpan.FromHours(6) },
            new() { Rotulo = "Últimas 24 horas", Janela = TimeSpan.FromHours(24) },
            new() { Rotulo = "Últimos 7 dias", Janela = TimeSpan.FromDays(7) },
            new() { Rotulo = "Tudo", Janela = null },
        };

        private readonly UI.UIApplication _uiApp;
        private readonly DespachanteDoRevit _despachante;
        private readonly RegistroDePlanilhas _registro;
        private readonly MonitorDePlanilhas _monitor;
        private readonly DispatcherTimer _temporizador;
        private readonly List<string> _avisosIniciais = new();

        private ConjuntoDeAmbientes? _conjunto;
        private List<LinhaDeAmbiente> _linhas = new();
        private int _ultimoTotalDeRooms;
        private string? _ultimoTituloDoModelo;
        private bool _atualizando;
        private bool _carregandoPlanilhas;
        private CancellationTokenSource? _cancelamento;

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

            InicializarSeletorDeJanela();
            InicializarLegenda();
            ExibirPlanilhas();

            _despachante.Falhou += DespachanteFalhou;
            _uiApp.SelectionChanged += SelecaoNoRevitAlterada;
            Dispatcher.UnhandledException += DispatcherFalhou;

            Loaded += JanelaCarregada;
            Closed += JanelaFechada;
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

        // ── Ciclo de atualização ─────────────────────────────────────────

        private async void Temporizador_Tick(object? sender, EventArgs evento)
        {
            if (CaixaAuto.IsChecked == true)
                await AtualizarAsync();
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

        private void ExibirConjunto(
            ConjuntoDeAmbientes conjunto, int casados, int rooms, string? tituloDoModelo, string? erroDeCasamento)
        {
            string? codigoSelecionado = (ListaDeAmbientes.SelectedItem as LinhaDeAmbiente)?.Codigo;
            TimeSpan? janela = JanelaSelecionada;

            _linhas = conjunto.Ambientes
                .Select(ambiente => new LinhaDeAmbiente(ambiente, CalculadorDeResumoDoAmbiente.Resumir(ambiente, janela)))
                .ToList();

            ListaDeAmbientes.ItemsSource = _linhas;
            ListaDeSensoresSemSala.ItemsSource = conjunto.SensoresSemSala.Select(DescreverSensorSemSala).ToList();

            TextoStatus.Text = string.Format(CultureInfo.CurrentCulture,
                "Atualizado às {0:HH:mm:ss} — {1} sala(s), {2} sensor(es), {3:N0} leitura(s).{4}",
                conjunto.AtualizadoEm, conjunto.Ambientes.Count, conjunto.TotalDeSensores, conjunto.TotalDeLeituras,
                conjunto.PlanilhasComFalha > 0
                    ? $" {conjunto.PlanilhasComFalha} planilha(s) falharam — veja os avisos."
                    : string.Empty);

            TextoCasamento.Text = MontarTextoDeCasamento(conjunto, casados, rooms, tituloDoModelo, erroDeCasamento);

            TextoAvisos.Text = string.Join(Environment.NewLine, _avisosIniciais.Concat(conjunto.Avisos));
            TextoAvisos.ScrollToEnd();

            if (conjunto.PlanilhasComFalha > 0)
                ExpansorDeAvisos.IsExpanded = true;

            var linha = _linhas.FirstOrDefault(l => l.Codigo == codigoSelecionado) ?? _linhas.FirstOrDefault();
            ListaDeAmbientes.SelectedItem = linha; // dispara ExibirSala pelo SelectionChanged

            if (linha == null)
                ExibirSala(null);
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

        private static string DescreverSensorSemSala(Sensor sensor)
        {
            return sensor.UltimaLeitura == null
                ? $"{sensor.Hostname} — sem leituras"
                : string.Format(CultureInfo.CurrentCulture, "{0} — última {1:dd/MM HH:mm}",
                    sensor.Hostname, sensor.UltimaLeitura.Timestamp);
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

            if (linha == null)
            {
                TextoTituloDaSala.Text = _conjunto == null ? "Selecione uma sala" : "Nenhuma sala reconhecida";
                TextoResumoDaSala.Text = _conjunto == null
                    ? "Aguardando a primeira atualização…"
                    : "Nenhum HOSTNAME das planilhas segue o padrão de sala (ex.: MMCCTLAB001-001). " +
                      "Veja \"Sensores sem sala\".";
                return;
            }

            TextoTituloDaSala.Text = linha.Titulo;
            TextoResumoDaSala.Text = MontarTextoDoResumo(linha);

            // (a) cartões ao vivo — um por sensor da sala
            foreach (var sensor in linha.Ambiente.Sensores)
                PainelDeCartoes.Children.Add(new CartaoDeSensor(sensor));

            // (b) série temporal — todas as leituras da sala dentro da janela
            try
            {
                var todas = linha.Ambiente.Leituras.ToList();
                var naJanela = CalculadorDeResumoDoAmbiente.FiltrarJanela(todas, linha.Ambiente.UltimaLeitura, JanelaSelecionada);
                PainelDoGrafico.Content = new ControleGraficoDeSerieTemporal(naJanela);
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

        private static string MontarTextoDoResumo(LinhaDeAmbiente linha)
        {
            var resumo = linha.Resumo;
            var cultura = CultureInfo.CurrentCulture;

            if (resumo.UltimaLeitura == null)
                return "Sem leituras. " + linha.Casamento + ".";

            string temperatura = resumo.Temperatura.TemDados
                ? string.Format(cultura, "temperatura {0:0.0} °C (média {1:0.0}, mín {2:0.0}, máx {3:0.0})",
                    resumo.Temperatura.Ultimo, resumo.Temperatura.Media, resumo.Temperatura.Minimo, resumo.Temperatura.Maximo)
                : "temperatura sem dados";

            string umidade = resumo.Umidade.TemDados
                ? string.Format(cultura, "umidade {0:0} % (média {1:0}, mín {2:0}, máx {3:0})",
                    resumo.Umidade.Ultimo, resumo.Umidade.Media, resumo.Umidade.Minimo, resumo.Umidade.Maximo)
                : "umidade sem dados";

            return string.Format(cultura,
                "{0}: {1}; {2}. {3} leitura(s) de {4} sensor(es), de {5:dd/MM HH:mm} a {6:dd/MM HH:mm}. {7}.",
                linha.Classificacao, temperatura, umidade,
                resumo.TotalDeLeituras, resumo.QuantidadeDeSensores, resumo.Inicio, resumo.Fim,
                linha.Casamento);
        }

        // ── Janela de tempo ──────────────────────────────────────────────

        private TimeSpan? JanelaSelecionada =>
            SeletorDeJanela.SelectedItem is OpcaoDeJanela opcao
                ? opcao.Janela
                : CalculadorDeResumoDoAmbiente.JanelaPadrao;

        private void InicializarSeletorDeJanela()
        {
            SeletorDeJanela.ItemsSource = OpcoesDeJanela;
            SeletorDeJanela.SelectedItem =
                OpcoesDeJanela.FirstOrDefault(opcao => opcao.Janela == CalculadorDeResumoDoAmbiente.JanelaPadrao)
                ?? OpcoesDeJanela[1];
        }

        private void SeletorDeJanela_SelectionChanged(object sender, SelectionChangedEventArgs evento)
        {
            if (_conjunto == null)
                return;

            // A janela muda o resumo (e a cor) de todas as salas: refaz as linhas sem baixar nada.
            int casados = _conjunto.Ambientes.Count(ambiente => ambiente.CasadoComOModelo);
            ExibirConjunto(_conjunto, casados, _ultimoTotalDeRooms, _ultimoTituloDoModelo, null);
        }

        private static ClassificacaoDeConforto Classificar(Ambiente ambiente, TimeSpan? janela)
        {
            return CalculadorDeResumoDoAmbiente.Resumir(ambiente, janela).ClassificacaoGeral;
        }

        // ── Seleção vinda do Revit ───────────────────────────────────────

        /// <summary>
        /// O usuário clicou num Room no modelo: seleciona a sala correspondente no painel.
        /// Roda em contexto da API (é um evento do Revit), então pode ler o documento.
        /// </summary>
        private void SelecaoNoRevitAlterada(object? sender, EventosDaUI.SelectionChangedEventArgs evento)
        {
            if (_linhas.Count == 0)
                return;

            try
            {
                var documento = evento.GetDocument();

                foreach (var id in evento.GetSelectedElements())
                {
                    if (documento.GetElement(id) is not Room room)
                        continue;

                    var linha = _linhas.FirstOrDefault(l => l.Ambiente.IdDoElementoNoModelo == id.Value)
                                ?? _linhas.FirstOrDefault(l => l.Codigo == SeletorDeAmbiente.CodigoDoRoom(room));

                    if (linha != null)
                    {
                        ListaDeAmbientes.SelectedItem = linha;
                        ListaDeAmbientes.ScrollIntoView(linha);
                    }
                    else
                    {
                        Informar($"O Room {room.Number} selecionado no modelo não tem sensor nas planilhas.");
                    }

                    return;
                }
            }
            catch (Exception excecao)
            {
                AcrescentarAviso("Seleção no modelo: " + excecao.Message);
            }
        }

        // ── Ações no modelo (via despachante) ────────────────────────────

        private void BotaoPintar_Click(object sender, RoutedEventArgs evento)
        {
            var conjunto = _conjunto;

            if (conjunto == null)
            {
                Informar("Aguarde a primeira atualização.");
                return;
            }

            int casados = conjunto.Ambientes.Count(ambiente => ambiente.CasadoComOModelo);

            if (casados == 0)
            {
                Informar("Nenhuma sala das planilhas casou com um Room do modelo. Numere o Room com o código " +
                         "da sala (ex.: LAB001 para o sensor MMCCTLAB001-001), clique em \"Atualizar agora\" e tente de novo.");
                Abas.SelectedIndex = 2;
                return;
            }

            TimeSpan? janela = JanelaSelecionada;

            _despachante.Executar(app =>
            {
                var uiDocumento = app.ActiveUIDocument;

                if (uiDocumento == null)
                {
                    Informar("Abra um modelo para pintar as salas.");
                    return;
                }

                var vista = uiDocumento.ActiveView;

                if (!PintorDeAmbientes.VistaAceitaPintura(vista))
                {
                    Informar($"A vista ativa (\"{vista.Name}\", {vista.ViewType}) não aceita pintura. Use uma planta, corte ou vista 3D.");
                    return;
                }

                var pintura = PintorDeAmbientes.Pintar(uiDocumento.Document, vista, conjunto, ambiente => Classificar(ambiente, janela));
                uiDocumento.RefreshActiveView();

                string observacao = pintura.Aviso != null
                    ? " " + pintura.Aviso
                    : $" Esquema de cores \"{EsquemaDeCoresDeConforto.NomeDoEsquema}\" aplicado à vista; o estado de cada sala está no parâmetro \"{EsquemaDeCoresDeConforto.NomeDoParametro}\" do Room.";

                Informar($"{pintura.Pintados} sala(s) pintada(s) em \"{vista.Name}\".{observacao}");
            });
        }

        private void BotaoLimpar_Click(object sender, RoutedEventArgs evento)
        {
            var conjunto = _conjunto;

            if (conjunto == null)
            {
                Informar("Aguarde a primeira atualização.");
                return;
            }

            var ids = conjunto.Ambientes
                .Where(ambiente => ambiente.IdDoElementoNoModelo.HasValue)
                .Select(ambiente => ambiente.IdDoElementoNoModelo!.Value)
                .ToList();

            _despachante.Executar(app =>
            {
                var uiDocumento = app.ActiveUIDocument;

                if (uiDocumento == null)
                {
                    Informar("Abra um modelo para limpar a pintura.");
                    return;
                }

                var vista = uiDocumento.ActiveView;

                if (!PintorDeAmbientes.VistaAceitaPintura(vista))
                {
                    Informar($"A vista ativa (\"{vista.Name}\") não tem pintura a limpar.");
                    return;
                }

                int limpos = PintorDeAmbientes.Limpar(uiDocumento.Document, vista, ids);
                uiDocumento.RefreshActiveView();
                Informar($"Pintura removida de {limpos} sala(s) em \"{vista.Name}\".");
            });
        }

        private void BotaoVista3D_Click(object sender, RoutedEventArgs evento)
        {
            var conjunto = _conjunto;

            if (conjunto == null)
            {
                Informar("Aguarde a primeira atualização.");
                return;
            }

            TimeSpan? janela = JanelaSelecionada;

            _despachante.Executar(app =>
            {
                var uiDocumento = app.ActiveUIDocument;

                if (uiDocumento == null)
                {
                    Informar("Abra um modelo para gerar a vista 3D.");
                    return;
                }

                var gerador = new GeradorDeVistaRevit(uiDocumento.Document);
                var vista = gerador.GerarVistaDeConforto(
                    conjunto, ambiente => Classificar(ambiente, janela),
                    out int pintados, out bool volumesAtivados, out string? avisoDaPintura);
                uiDocumento.ActiveView = vista;
                uiDocumento.RefreshActiveView();

                string observacao = string.Empty;

                if (volumesAtivados)
                    observacao += " O cálculo de volumes do projeto foi LIGADO (Áreas e volumes): sem ele o Revit não desenha Rooms em 3D.";

                if (avisoDaPintura != null)
                    observacao += " " + avisoDaPintura;

                Informar($"Vista \"{vista.Name}\" criada com {pintados} sala(s) pintada(s).{observacao}");
            });
        }

        private void BotaoMostrar_Click(object sender, RoutedEventArgs evento)
        {
            if (ListaDeAmbientes.SelectedItem is not LinhaDeAmbiente linha)
            {
                Informar("Selecione uma sala na lista.");
                return;
            }

            if (!linha.Ambiente.IdDoElementoNoModelo.HasValue)
            {
                Informar($"A sala {linha.Codigo} não tem Room correspondente no modelo aberto.");
                return;
            }

            long id = linha.Ambiente.IdDoElementoNoModelo.Value;

            _despachante.Executar(app =>
            {
                var uiDocumento = app.ActiveUIDocument;

                if (uiDocumento == null)
                {
                    Informar("Abra um modelo para localizar a sala.");
                    return;
                }

                SeletorDeAmbiente.MostrarNoModelo(uiDocumento, id);
            });
        }

        private void BotaoDiagnostico_Click(object sender, RoutedEventArgs evento)
        {
            var conjunto = _conjunto;

            _despachante.Executar(app =>
            {
                var uiDocumento = app.ActiveUIDocument;

                if (uiDocumento == null)
                {
                    Informar("Abra um modelo para diagnosticar.");
                    return;
                }

                string relatorio = DiagnosticoDoModelo.Gerar(uiDocumento, conjunto);

                AcrescentarAviso(relatorio);
                ExpansorDeAvisos.IsExpanded = true;

                try
                {
                    Clipboard.SetText(relatorio);
                    TextoStatus.Text = "Diagnóstico registrado nos avisos e copiado para a área de transferência.";
                }
                catch (Exception)
                {
                    TextoStatus.Text = "Diagnóstico registrado nos avisos.";
                }
            });
        }

        // ── Planilhas ────────────────────────────────────────────────────

        private void ExibirPlanilhas()
        {
            _carregandoPlanilhas = true;
            ListaDePlanilhas.ItemsSource = null;
            ListaDePlanilhas.ItemsSource = _registro.Planilhas.ToList();
            _carregandoPlanilhas = false;
        }

        private void PlanilhaAtivaAlterada(object sender, RoutedEventArgs evento)
        {
            if (_carregandoPlanilhas)
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

        private async void BotaoAdicionarPlanilha_Click(object sender, RoutedEventArgs evento)
        {
            var dialogo = new JanelaDeCadastroDePlanilha { Owner = this };

            if (dialogo.ShowDialog() != true)
                return;

            try
            {
                _registro.Adicionar(dialogo.Nome, dialogo.Url);
            }
            catch (Exception excecao)
            {
                MessageBox.Show(this, excecao.Message, "Cadastro de planilha", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ExibirPlanilhas();
            await AtualizarAsync();
        }

        private async void BotaoRemoverPlanilha_Click(object sender, RoutedEventArgs evento)
        {
            if (ListaDePlanilhas.SelectedItem is not PlanilhaRegistrada planilha)
            {
                MessageBox.Show(this, "Selecione a planilha a remover.", "Remover planilha", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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

            ExibirPlanilhas();
            await AtualizarAsync();
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
                var (r, g, b) = CoresDeConforto.ParaRgb(CoresDeConforto.Para(estado));

                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };

                item.Children.Add(new Ellipse
                {
                    Width = 11,
                    Height = 11,
                    Fill = new SolidColorBrush(Color.FromRgb(r, g, b)),
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
