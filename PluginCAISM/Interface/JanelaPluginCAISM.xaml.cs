using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;
using PluginCAISM.Interface.Auditoria;
using PluginCAISM.Interface.Controles;
using PluginCAISM.Interface.Exportacao;
using PluginCAISM.Interface.Modelos;
using PluginCAISM.Parsing;
using PluginCAISM.Processamento;
using PluginCAISM.Revit;
using WpfVisibility = System.Windows.Visibility;
using Button   = System.Windows.Controls.Button;
using Color    = System.Windows.Media.Color;
using Grid     = System.Windows.Controls.Grid;
using Path     = System.IO.Path;

namespace PluginCAISM.Interface
{
    public partial class JanelaPluginCAISM : Window
    {
        private readonly Document _documento;
        private readonly UIDocument _uiDocumento;
        private readonly string _caminhoDoPlugin;

        private readonly AnalisadorCsvSessao    _analisadorSessao;
        private readonly AnalisadorExcelCortisol _analisadorCortisol;
        private readonly AnalisadorDeSensores   _analisadorSensor;
        private readonly AnalisadorExcelFormularios _analisadorFormularios;
        private readonly CalculadorDeEscoresDeFormulario _calculadorDeEscores;
        private readonly CalculadorDeIndicadoresDeSbs _calculadorDeIndicadoresDeSbs = new();
        private readonly ConstruturDeSessoes    _construtor;
        private readonly CalculadorDeMetricasPorObjeto _calculadorDeMetricas;


        private ConjuntoDeSessoes _conjuntoDeSessoes;
        private Dictionary<string, double>                    _deltasDeCortisol;

        // Coletas de cortisol por participante, indexadas por ID_PARTICIPANTE — a MESMA chave da
        // planilha de formulários. Alimentam a opção "Cortisol" da aba de Dados do Participante.
        private Dictionary<string, RegistroDeCortisol>        _registrosDeCortisol = new();
        private Dictionary<DateTime, Dictionary<string, string>> _indiceDeSensores;

        // Linhas do Input 01 já analisadas, mantidas para que o carregamento posterior de cortisol
        // ou de sensores possa reconstruir as sessões sem reler o CSV — que é grande e cujo parse
        // na thread da UI congelava a janela.
        private List<LinhaDeSessao> _linhasDeSessao;

        // Formulários (Arquivo 4): via de ingestão PARALELA. Estes dados são contextualização
        // (§5.3) e não entram no pipeline do índice (ConstruturDeSessoes / cálculo de métricas).
        private List<RespostasDeFormularioPorParticipante> _respostasDeFormularios = new();

        private List<MetricasPorObjeto> _ultimasMetricas = new();

        // Linhas exatamente como estão na tela, guardadas para a exportação: o PDF deve reproduzir
        // o que o usuário vê, inclusive a ordenação, e não recalcular a partir das métricas.
        private List<LinhaDeObjetoParaTabela> _linhasDaTabelaDePotencial = new();

        // ── Auditoria da sessão ──────────────────────────────────────────
        // A tabela de auditoria roda a detecção SEM a janela temporal, para poder mostrar também os
        // eventos que a análise descartou. É uma segunda passada sobre as linhas brutas, cara o
        // bastante para não valer refazer a cada troca de filtro: o resultado por sessão fica em
        // cache e a tabela só é remontada quando o painel está à vista.
        private readonly DetectorDeMovimentoOcular _detectorDeAuditoria = new();

        private readonly Dictionary<Sessao, List<EventoDeMovimentoOcular>> _eventosDeAuditoriaPorSessao = new();

        private List<Sessao> _sessoesDaAuditoria = new();
        private bool _auditoriaDesatualizada = true;

        // Categoria "Dados do HMD - RV": retrato da coleta em RV por participante. Vem do
        // dados_hmd.csv — o mesmo arquivo das sessões —, não da planilha de formulários, e por
        // isso é indexado pelas identificações do HMD (MA001, LF001…).
        private readonly CalculadorDeResumoDoHmd _calculadorDeResumoDoHmd = new();
        private List<ResumoDoHmdDoParticipante> _resumosDoHmd = new();

        // Conjunto que originou _resumosDoHmd, para repopular o seletor quando as sessões forem
        // recarregadas — o resumo é caro o bastante para não valer recalcular a cada troca de aba.
        private ConjuntoDeSessoes _conjuntoUsadoNoResumoDoHmd;

        private string _caminhoSessoes;
        private string _caminhoCortisol;
        private string _caminhoSensores;
        private string _caminhoFormularios;




        // ── Construtor ───────────────────────────────────────────────────

        public JanelaPluginCAISM(Document documento, UIDocument uiDocumento, string caminhoDoPlugin)
        {
            InitializeComponent();
            _documento      = documento;
            _uiDocumento    = uiDocumento;
            _caminhoDoPlugin = caminhoDoPlugin;

            var detector       = new DetectorDeMovimentoOcular();
            _analisadorSessao    = new AnalisadorCsvSessao();
            _analisadorCortisol  = new AnalisadorExcelCortisol();
            _analisadorSensor    = new AnalisadorDeSensores();
            _analisadorFormularios = new AnalisadorExcelFormularios();
            _calculadorDeEscores = new CalculadorDeEscoresDeFormulario();
            _construtor          = new ConstruturDeSessoes(detector);
            _calculadorDeMetricas = new CalculadorDeMetricasPorObjeto();

            RegistrarPinceisDeClassificacaoDePotencial();
            InicializarSeletorDeFormulario();
            InicializarFiltrosDoGrupo();
        }

        /// <summary>
        /// Publica as cores de <see cref="CoresDeClassificacaoDePotencial"/> como recursos da
        /// janela, para que o XAML as referencie por <c>DynamicResource</c>.
        /// </summary>
        /// <remarks>
        /// As cores ficam em <c>Configuracao</c> como texto hexadecimal, e não como pincéis, para
        /// que a camada de configuração não dependa do WPF. A conversão para
        /// <see cref="SolidColorBrush"/> acontece aqui, na camada de apresentação, uma única vez.
        /// </remarks>
        private void RegistrarPinceisDeClassificacaoDePotencial()
        {
            Resources["PincelPotencialRestaurador"] =
                CriarPincel(CoresDeClassificacaoDePotencial.CorDeRestaurador);
            Resources["PincelPotencialEstressor"] =
                CriarPincel(CoresDeClassificacaoDePotencial.CorDeEstressor);
            Resources["PincelPotencialNeutro"] =
                CriarPincel(CoresDeClassificacaoDePotencial.CorDeNeutro);
        }

        // PreencherLegendaDeColunas foi REMOVIDO junto com a legenda textual das colunas da aba de
        // Potencial Homeodinâmico. A legenda de CORES continua, escrita direto no XAML.

        private static SolidColorBrush CriarPincel(string hexadecimal)
        {
            var pincel = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexadecimal));
            pincel.Freeze();
            return pincel;
        }

        // ── Carregamento de arquivos ─────────────────────────────────────

        private async void BotaoCarregarSessoes_Click(object sender, RoutedEventArgs evento)
        {
            string caminho = SelecionarArquivo("CSV de Sessões|*.csv");
            if (caminho == null) return;
            _caminhoSessoes = caminho;
            AtualizarStatusBotao(TextoStatusSessoes, BotaoCarregarSessoes, caminho, "Carregando sessões...");
            await CarregarSessoesEmBackground();
        }

        private async void BotaoCarregarCortisol_Click(object sender, RoutedEventArgs evento)
        {
            string caminho = SelecionarArquivo("Excel de Cortisol|*.xlsx");
            if (caminho == null) return;
            _caminhoCortisol = caminho;
            AtualizarStatusBotao(TextoStatusCortisol, BotaoCarregarCortisol, caminho, "Carregando cortisol...");
            await CarregarCortisolEmBackground();
        }

        private async void BotaoCarregarSensores_Click(object sender, RoutedEventArgs evento)
        {
            // O .csv é o formato nativo do módulo de monitoramento e vem primeiro no filtro; o
            // .xlsx segue aceito, para os arquivos que já passaram pelo Excel.
            string caminho = SelecionarArquivo(AnalisadorDeSensores.FiltroDeArquivo);
            if (caminho == null) return;
            _caminhoSensores = caminho;
            AtualizarStatusBotao(TextoStatusSensores, BotaoCarregarSensores, caminho, "Carregando sensores...");
            await CarregarSensoresEmBackground();
        }

        private async void BotaoCarregarFormularios_Click(object sender, RoutedEventArgs evento)
        {
            string caminho = SelecionarArquivo("Excel de Formulários|*.xlsx");
            if (caminho == null) return;
            _caminhoFormularios = caminho;
            AtualizarStatusBotao(TextoStatusFormularios, BotaoCarregarFormularios, caminho, "Carregando formulários...");
            await CarregarFormulariosEmBackground();
        }

        private async Task CarregarSessoesEmBackground()
        {
            ExibirSpinner(true);
            try
            {
                var avisos       = new List<string>();
                var linhasBrutas = await Task.Run(() => _analisadorSessao.Analisar(_caminhoSessoes, avisos));
                RecontruirConjuntoDeSessoes(linhasBrutas);

                // AtualizarFiltrosDeScenario define SelectedIndex = 0 no FiltroCenario, o que
                // dispara FiltroAlterado → Dispatcher.BeginInvoke(AtualizarInterface).
                // Não chamamos AtualizarInterface() diretamente aqui para evitar duplo disparo:
                // a chamada via Dispatcher já garante que a UI está num estado consistente.
                AtualizarFiltrosDeScenario();

                if (avisos.Any()) ExibirAvisosDeCarregamento("Sessões carregadas com avisos", avisos);
                AtualizarStatusBotao(TextoStatusSessoes, BotaoCarregarSessoes, _caminhoSessoes, null);
            }
            catch (Exception ex)
            {
                ExibirErroDeCarregamento("Erro ao carregar sessões", ex.Message);
                MarcarStatusComoErro(TextoStatusSessoes);
            }
            finally { ExibirSpinner(false); }
        }

        private async Task CarregarCortisolEmBackground()
        {
            ExibirSpinner(true);
            try
            {
                _registrosDeCortisol = await Task.Run(
                    () => _analisadorCortisol.AnalisarRegistros(_caminhoCortisol));

                _deltasDeCortisol = _registrosDeCortisol.ToDictionary(
                    par => par.Key, par => par.Value.Delta, StringComparer.OrdinalIgnoreCase);

                if (_conjuntoDeSessoes != null) RecontruirConjuntoDeSessoes(null);
                AtualizarAbaDeContextualizacao();
                // Usamos Dispatcher para consistência com os outros carregamentos
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(AtualizarInterface));
                AtualizarStatusBotao(TextoStatusCortisol, BotaoCarregarCortisol, _caminhoCortisol, null);
            }
            catch (Exception ex)
            {
                ExibirErroDeCarregamento("Erro ao carregar cortisol", ex.Message);
                MarcarStatusComoErro(TextoStatusCortisol);
            }
            finally { ExibirSpinner(false); }
        }

        private async Task CarregarSensoresEmBackground()
        {
            ExibirSpinner(true);
            try
            {
                var avisos = new List<string>();
                _indiceDeSensores = await Task.Run(() => _analisadorSensor.Analisar(_caminhoSensores, avisos));
                if (_conjuntoDeSessoes != null) RecontruirConjuntoDeSessoes(null);
                // Usamos Dispatcher para consistência com os outros carregamentos
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(AtualizarInterface));
                AtualizarStatusBotao(TextoStatusSensores, BotaoCarregarSensores, _caminhoSensores, null);
                TextoStatusSensores.Text += $" — {_indiceDeSensores.Count} leitura(s)";

                if (avisos.Any())
                    ExibirAvisosDeCarregamento("Sensores carregados", avisos);
            }
            catch (Exception ex)
            {
                ExibirErroDeCarregamento("Erro ao carregar sensores", ex.Message);
                MarcarStatusComoErro(TextoStatusSensores);
            }
            finally { ExibirSpinner(false); }
        }

        /// <summary>
        /// Carrega o arquivo de formulários (Arquivo 4) fora da thread da UI e deriva os escores.
        /// </summary>
        /// <remarks>
        /// Via de ingestão PARALELA: os formulários são dados de contextualização (§5.3) e não
        /// passam pelo <see cref="ConstruturDeSessoes"/> nem alteram o
        /// <see cref="ConjuntoDeSessoes"/>, por isso este carregamento não reconstrói as sessões.
        /// Alimenta exclusivamente a aba de contextualização.
        /// </remarks>
        private async Task CarregarFormulariosEmBackground()
        {
            ExibirSpinner(true);
            try
            {
                var avisos = new List<string>();

                var respostasBrutas = await Task.Run(
                    () => _analisadorFormularios.Analisar(_caminhoFormularios, avisos));

                _respostasDeFormularios = await Task.Run(
                    () => _calculadorDeEscores.Calcular(respostasBrutas));

                AtualizarStatusBotao(TextoStatusFormularios, BotaoCarregarFormularios, _caminhoFormularios, null);
                TextoStatusFormularios.Text += $" — {_respostasDeFormularios.Count} resposta(s)";

                AtualizarAbaDeContextualizacao();

                if (avisos.Any())
                    ExibirAvisosDeCarregamento("Formulários carregados com avisos", avisos);
            }
            catch (Exception ex)
            {
                ExibirErroDeCarregamento("Erro ao carregar formulários", ex.Message);
                MarcarStatusComoErro(TextoStatusFormularios);
            }
            finally { ExibirSpinner(false); }
        }

        // ── Aba de contextualização (§5.5.1) ─────────────────────────────

        private void InicializarSeletorDeFormulario()
        {
            SeletorDeFormulario.ItemsSource = new[]
            {
                new OpcaoDeFormulario(RotulosDeFormulario.Sintese,              TipoDeFormulario.Sintese),
                new OpcaoDeFormulario(RotulosDeFormulario.Recrutamento,         TipoDeFormulario.Recrutamento),
                new OpcaoDeFormulario(RotulosDeFormulario.SickBuildingSyndrome, TipoDeFormulario.SindromeDoEdificioDoente),
                new OpcaoDeFormulario(RotulosDeFormulario.Ieee,                 TipoDeFormulario.Ieee),
                new OpcaoDeFormulario(RotulosDeFormulario.PercepcaoAmbiental,   TipoDeFormulario.PercepcaoAmbiental),
                new OpcaoDeFormulario(RotulosDeFormulario.Cortisol,             TipoDeFormulario.Cortisol),
                new OpcaoDeFormulario(RotulosDeFormulario.DadosDoHmd,           TipoDeFormulario.DadosDoHmd),
            };
            SeletorDeFormulario.DisplayMemberPath = "Rotulo";
            SeletorDeFormulario.SelectedIndex = 0;
        }

        /// <summary>
        /// Popula os dois seletores de eixo da aba de Dados do Grupo (§5.5.2).
        /// </summary>
        /// <remarks>
        /// A aba deixou de ter filtros de subconjunto — classificação de PH_e e faixa de estresse do
        /// IEEE saíram —, então estas são as únicas listas suspensas dela: o gráfico usa todos os
        /// participantes carregados e só muda de eixos.
        /// </remarks>
        private void InicializarFiltrosDoGrupo()
        {
            // As listas deixaram de ser iguais: X oferece características do indivíduo e Y, índices
            // de estresse. Como os conjuntos são disjuntos, não há mais o risco de X = Y produzir a
            // diagonal degenerada, e os dois seletores podem começar no primeiro item.
            SeletorDoEixoX.ItemsSource = VariaveisDoGrupo.SelecionaveisEmX;
            SeletorDoEixoX.DisplayMemberPath = nameof(VariavelDoGrupo.NomeCurto);
            SeletorDoEixoX.SelectedIndex = 0;

            SeletorDoEixoY.ItemsSource = VariaveisDoGrupo.SelecionaveisEmY;
            SeletorDoEixoY.DisplayMemberPath = nameof(VariavelDoGrupo.NomeCurto);
            SeletorDoEixoY.SelectedIndex = 0;

            SeletorDoEixoX.IsEnabled = true;
            SeletorDoEixoY.IsEnabled = true;
        }

        /// <summary>
        /// Repovoa a aba de contextualização: seletor de participantes, tabela-síntese e
        /// dispersões.
        /// </summary>
        private void AtualizarAbaDeContextualizacao()
        {
            bool temFormularios = _respostasDeFormularios.Any();

            TextoPlaceholderContextualizacao.Visibility = temFormularios
                ? WpfVisibility.Collapsed
                : WpfVisibility.Visible;
            TextoPlaceholderParticipante.Visibility = temFormularios
                ? WpfVisibility.Collapsed
                : WpfVisibility.Visible;

            SeletorDeParticipante.IsEnabled = temFormularios;

            // "Dados do HMD - RV" não depende da planilha de formulários: sai do dados_hmd.csv.
            // Por isso a lista de categorias fica habilitada quando há sessões, mesmo sem
            // formulário carregado — do contrário a categoria seria inalcançável.
            bool temSessoes = _conjuntoDeSessoes?.Todas.Any() == true;
            SeletorDeFormulario.IsEnabled = temFormularios || temSessoes;

            if (!temFormularios)
            {
                SeletorDeParticipante.ItemsSource = null;
                PainelDeDispersoes.Children.Clear();

                // A tabela ainda tem o que mostrar se a categoria escolhida for a do HMD; nas
                // demais, este mesmo caminho a esvazia por falta de respostas.
                AtualizarTabelaDeFormularios();
                return;
            }

            AtualizarSeletorDeParticipantes();
            AtualizarTabelaDeFormularios();
            AtualizarGraficosDeDispersao();
        }

        private void AtualizarSeletorDeParticipantes()
        {
            string selecaoAnterior = SeletorDeParticipante.SelectedItem as string;

            var participantes = _respostasDeFormularios
                .Select(resposta => string.IsNullOrWhiteSpace(resposta.IdentificacaoManual)
                    ? $"(linha {resposta.NumeroDaLinhaOriginal})"
                    : resposta.IdentificacaoManual)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(nome => nome, StringComparer.OrdinalIgnoreCase)
                .ToList();

            SeletorDeParticipante.ItemsSource = participantes;
            SeletorDeParticipante.SelectedItem = selecaoAnterior != null && participantes.Contains(selecaoAnterior)
                ? selecaoAnterior
                : participantes.FirstOrDefault();
        }

        private void SelecaoDeContextualizacaoAlterada(object sender, SelectionChangedEventArgs evento)
        {
            // Dispara durante InitializeComponent, antes de a lista existir.
            if (ListaDeGruposDoFormulario == null) return;

            AtualizarTabelaDeFormularios();
        }

        private void AtualizarTabelaDeFormularios()
        {
            if (SeletorDeFormulario.SelectedItem is not OpcaoDeFormulario opcao)
            {
                LimparPainelDoFormulario();
                return;
            }

            var respostasSelecionadas = ObterRespostasDoParticipanteSelecionado();

            if (respostasSelecionadas == null)
            {
                LimparPainelDoFormulario();
                return;
            }

            _registrosDeCortisol.TryGetValue(
                respostasSelecionadas.IdentificacaoManual ?? string.Empty, out var registroDeCortisol);

            // A categoria do HMD tem outra ORIGEM de dado — o rastreamento, não a planilha —, mas o
            // MESMO participante: sai por um caminho separado, já com o participante resolvido.
            if (opcao.Tipo == TipoDeFormulario.DadosDoHmd)
            {
                ExibirDadosDoHmd(opcao, respostasSelecionadas, registroDeCortisol);
                return;
            }

            var linhas = LinhaDeFormularioParaTabela.Construir(
                respostasSelecionadas, opcao.Tipo, registroDeCortisol);

            ListaDeGruposDoFormulario.ItemsSource =
                GrupoDeFormularioParaTela.Agrupar(linhas, opcao.Tipo);

            // A faixa de destaques descreve o PARTICIPANTE, não o formulário: é remontada aqui só
            // porque é aqui que se sabe qual participante está selecionado, e não muda de conteúdo
            // ao trocar de categoria.
            FaixaDeDestaques.ItemsSource = CartaoDeDestaqueDoParticipante.Construir(
                respostasSelecionadas, registroDeCortisol);

            // Cartões de objeto pertencem só à categoria do HMD.
            FaixaDeObjetosObservados.ItemsSource = null;

            if (TextoTituloDoFormulario != null)
                TextoTituloDoFormulario.Text =
                    $"{opcao.Rotulo} — participante {respostasSelecionadas.IdentificacaoManual}";

            AtualizarGraficosDeCortisol(opcao.Tipo, registroDeCortisol);
            AtualizarDeltaCortisolDoParticipante(registroDeCortisol);
        }

        /// <summary>
        /// Esvazia o painel do formulário quando não há participante ou categoria selecionada.
        /// </summary>
        /// <remarks>
        /// Os destaques saem junto com os grupos: eles descrevem um participante, e deixá-los na
        /// tela depois que a seleção se perdeu mostraria o retrato de alguém que não está mais
        /// escolhido.
        /// </remarks>
        private void LimparPainelDoFormulario()
        {
            ListaDeGruposDoFormulario.ItemsSource = null;
            FaixaDeDestaques.ItemsSource          = null;
            FaixaDeObjetosObservados.ItemsSource  = null;
        }

        // ── Categoria "Dados do HMD - RV" ────────────────────────────────

        /// <summary>
        /// Recalcula os resumos de RV quando o conjunto de sessões muda.
        /// </summary>
        /// <remarks>
        /// O cálculo percorre todas as fixações de todas as sessões, e por isso não é refeito a
        /// cada troca de categoria — só quando o conjunto carregado é outro.
        /// </remarks>
        private void GarantirResumosDoHmd()
        {
            if (ReferenceEquals(_conjuntoUsadoNoResumoDoHmd, _conjuntoDeSessoes))
                return;

            _resumosDoHmd = _calculadorDeResumoDoHmd.Calcular(_conjuntoDeSessoes?.Todas);
            _conjuntoUsadoNoResumoDoHmd = _conjuntoDeSessoes;
        }

        /// <summary>
        /// Preenche a aba com o resumo de RV do participante escolhido no seletor do topo.
        /// </summary>
        /// <remarks>
        /// A categoria deixou de ter seletor próprio: os arquivos de cenário trazem os mesmos 26
        /// identificadores dos formulários, de modo que o participante escolhido no alto da aba
        /// governa também estes dados. Quando o identificador não existe no rastreamento, a
        /// categoria informa isso em vez de exibir dado de outra pessoa.
        /// </remarks>
        private void ExibirDadosDoHmd(
            OpcaoDeFormulario opcao,
            RespostasDeFormularioPorParticipante respostas,
            Parsing.RegistroDeCortisol registroDeCortisol)
        {
            GarantirResumosDoHmd();

            var resumo = _resumosDoHmd?.FirstOrDefault(item => string.Equals(
                item.IdentificacaoManual, respostas?.IdentificacaoManual,
                StringComparison.OrdinalIgnoreCase));

            ListaDeGruposDoFormulario.ItemsSource = GrupoDeFormularioParaTela.Agrupar(
                LinhaDeFormularioParaTabela.ConstruirParaHmd(resumo), opcao.Tipo);

            // A faixa de destaques é a MESMA das demais categorias: descreve o participante, e o
            // participante agora é o mesmo dos dois lados.
            FaixaDeDestaques.ItemsSource = CartaoDeDestaqueDoParticipante.Construir(
                respostas, registroDeCortisol);

            // Os objetos mais observados ficam ABAIXO das tabelas, em cartões próprios.
            FaixaDeObjetosObservados.ItemsSource =
                CartaoDeDestaqueDoParticipante.ConstruirObjetosMaisObservados(resumo);

            if (TextoTituloDoFormulario != null)
            {
                TextoTituloDoFormulario.Text = resumo != null
                    ? $"{opcao.Rotulo} — participante {resumo.IdentificacaoManual}"
                    : $"{opcao.Rotulo} — sem registro de rastreamento para o participante " +
                      $"{respostas?.IdentificacaoManual}";
            }

            // A categoria não tem gráfico de cortisol; o painel precisa voltar ao estado neutro
            // para não exibir dado alheio.
            AtualizarGraficosDeCortisol(opcao.Tipo, null);
        }

        /// <summary>
        /// Preenche os seis gráficos lineares de cortisol abaixo da tabela — um por sessão de
        /// coleta —, cada um com três pontos: ao acordar, +30 min e antes de dormir.
        /// </summary>
        /// <remarks>
        /// Os gráficos aparecem apenas no formulário "Níveis de cortisol"; nos demais o painel
        /// fica vazio. O eixo Y é compartilhado pelos seis, indo de zero ao maior valor das
        /// dezoito medições do participante com folga, para que as sessões sejam comparáveis
        /// entre si a olho nu.
        /// </remarks>
        // DISPOSIÇÃO ADOTADA: o TextoFinal (Requisito da aba, §5.6: "seis gráficos lineares das
        // sessões de coleta de cortisol") fixa a quantidade e o tipo, e é silente quanto ao
        // resto. Duas colunas fazem cada LINHA da grade ser uma semana (início | final), na
        // mesma ordem das colunas da planilha. O eixo Y compartilhado é deliberado: com escala
        // própria por gráfico, todas as curvas pareceriam iguais e a comparação entre sessões —
        // que é o propósito dos seis lado a lado — ficaria impossível a olho nu.
        private void AtualizarGraficosDeCortisol(
            TipoDeFormulario tipo, RegistroDeCortisol registro)
        {
            if (PainelDeGraficosDeCortisol == null) return;

            PainelDeGraficosDeCortisol.Children.Clear();

            if (tipo != TipoDeFormulario.Cortisol || registro == null || !registro.Sessoes.Any())
                return;

            double maximo = registro.Sessoes
                .SelectMany(s => new[] { s.AoAcordar, s.TrintaMinutos, s.AntesDeDormir })
                .Where(v => !double.IsNaN(v))
                .DefaultIfEmpty(0)
                .Max();

            double limiteSuperior = maximo > 0 ? maximo * 1.1 : 1.0;

            PainelDeGraficosDeCortisol.Children.Add(new TextBlock
            {
                Text = "Cortisol por sessão de coleta (ng/ml)",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x22, 0x30)),
                Margin = new Thickness(0, 0, 0, 8),
            });

            var grade = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };

            // A construção dos gráficos é protegida porque uma exceção do LiveCharts sobe pelo
            // pipeline do WPF e derruba o processo hospedeiro — ou seja, fecha o Revit inteiro,
            // com perda do trabalho do usuário. Preferimos degradar para uma mensagem.
            try
            {
                foreach (var sessao in registro.Sessoes)
                    grade.Children.Add(new ControleGraficoDeCortisol(sessao, limiteSuperior));

                PainelDeGraficosDeCortisol.Children.Add(grade);
            }
            catch (Exception ex)
            {
                PainelDeGraficosDeCortisol.Children.Add(new TextBlock
                {
                    Text = $"Não foi possível desenhar os gráficos de cortisol: {ex.Message}",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
                });
            }
        }

        /// <summary>
        /// Exibe o delta de cortisol do participante selecionado (§5.5.3).
        /// </summary>
        /// <remarks>
        /// A planilha de cortisol é indexada por ID_PARTICIPANTE — a mesma chave usada pelo
        /// seletor desta aba —, portanto a associação é direta.
        /// </remarks>
        // RESOLVIDO: o pipeline passou a usar a mesma chave desta aba. O ConstruturDeSessoes
        // associa o cortisol por Sessao.IdentificacaoManual — o ID_PARTICIPANTE que indexa o
        // arquivo, conforme o Requisito 01 ("de cada participante") —, com retaguarda pelo
        // NomeDaSessao. Sessao.DeltaCortisol deixou de sair NaN por chave errada.
        private void AtualizarDeltaCortisolDoParticipante(RegistroDeCortisol registro)
        {
            if (TextoDeltaCortisol == null) return;

            TextoDeltaCortisol.Text = registro != null && !double.IsNaN(registro.Delta)
                ? $"Delta de cortisol: {registro.Delta:N2} ng/ml"
                : "Delta de cortisol: —";
        }

        private RespostasDeFormularioPorParticipante ObterRespostasDoParticipanteSelecionado()
        {
            if (SeletorDeParticipante.SelectedItem is not string identificacaoSelecionada)
                return null;

            return _respostasDeFormularios.FirstOrDefault(resposta =>
                string.Equals(resposta.IdentificacaoManual, identificacaoSelecionada, StringComparison.OrdinalIgnoreCase)
                || $"(linha {resposta.NumeroDaLinhaOriginal})" == identificacaoSelecionada);
        }

        /// <summary>
        /// Reconstrói o ÚNICO gráfico de dispersão da aba de Dados do Grupo, montado a partir das
        /// variáveis escolhidas nos seletores de eixo X e Y.
        /// </summary>
        /// <remarks>
        /// A aba exibe exclusivamente esse gráfico — nenhuma tabela e nenhum gráfico fixo. Os dois
        /// seletores de eixo são as únicas listas da aba: definem O QUE é plotado, e o gráfico usa
        /// TODOS os participantes carregados, sem filtro de subconjunto. Um participante só gera
        /// ponto quando tem as duas variáveis; ausências apenas o descartam.
        ///
        /// A cor sai da combinação de eixos, por
        /// <see cref="PaletaDoGraficoDinamicoDoGrupo.ObterCor"/>, e é estável entre execuções.
        /// </remarks>
        // A seleção X = Y deixou de ser possível: as duas listas oferecem conjuntos disjuntos —
        // características do indivíduo em X, índices de estresse em Y.
        private void AtualizarGraficosDeDispersao()
        {
            PainelDeDispersoes.Children.Clear();

            var respostasFiltradas = _respostasDeFormularios;

            // Os indicadores de SBS vêm calculados daqui, e não lidos da planilha pelo descritor.
            // Continuam sendo produzidos mesmo tendo saído dos seletores de eixo: alimentam a aba
            // de Dados do Participante.
            var indicadoresPorParticipante = _calculadorDeIndicadoresDeSbs
                .Calcular(respostasFiltradas)
                .Where(i => !string.IsNullOrWhiteSpace(i.IdentificacaoManual))
                .GroupBy(i => i.IdentificacaoManual, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // A variável vem do ITEM selecionado, nunca da posição dele na lista. Resolver por
            // índice funcionava enquanto os dois seletores compartilhavam a mesma lista; desde que
            // X e Y passaram a oferecer conjuntos diferentes, a posição de um não corresponde à do
            // outro, e o gráfico plotava a variável errada em silêncio — escolher IMC no eixo X
            // desenhava a severidade de SBS, que ocupava a mesma posição na lista antiga.
            var variavelX = SeletorDoEixoX?.SelectedItem as VariavelDoGrupo
                            ?? VariaveisDoGrupo.SelecionaveisEmX[0];
            var variavelY = SeletorDoEixoY?.SelectedItem as VariavelDoGrupo
                            ?? VariaveisDoGrupo.SelecionaveisEmY[0];

            // A cor da combinação continua vindo da lista unificada, que é a ordem da paleta.
            string cor = PaletaDoGraficoDinamicoDoGrupo.ObterCor(
                Math.Max(0, Array.IndexOf(VariaveisDoGrupo.Selecionaveis, variavelX)),
                Math.Max(0, Array.IndexOf(VariaveisDoGrupo.Selecionaveis, variavelY)),
                VariaveisDoGrupo.Selecionaveis.Length);

            // Uma exceção do LiveCharts sobe pelo pipeline do WPF e derruba o Revit inteiro;
            // preferimos degradar para uma mensagem.
            try
            {
                PainelDeDispersoes.Children.Add(new ControleGraficoDeDispersaoDoGrupo(
                    respostasFiltradas, _registrosDeCortisol, indicadoresPorParticipante,
                    variavelX, variavelY, cor));
            }
            catch (Exception ex)
            {
                PainelDeDispersoes.Children.Add(new TextBlock
                {
                    Text = $"Não foi possível desenhar os gráficos: {ex.Message}",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
                });
            }
        }


        private void AbaDeAnaliseAlterada(object sender, SelectionChangedEventArgs evento)
        {
            // Só reage à troca de abas do próprio TabControl, não aos ComboBox internos.
            if (!ReferenceEquals(evento.OriginalSource, AbasDeAnalise)) return;

            AtualizarVisibilidadeDoBotaoDeAuditoria();

            if (TextoPlaceholderContextualizacao == null) return;

            AtualizarAbaDeContextualizacao();
        }

        /// <summary>Opção do seletor de formulário: rótulo exibido e tipo correspondente.</summary>
        private sealed class OpcaoDeFormulario
        {
            public string Rotulo { get; }
            public TipoDeFormulario Tipo { get; }

            public OpcaoDeFormulario(string rotulo, TipoDeFormulario tipo)
            {
                Rotulo = rotulo;
                Tipo = tipo;
            }
        }



        /// <summary>
        /// Mostra no rodapé da aba sob que condições a Camada 1 estimou os índices da tabela.
        /// </summary>
        /// <remarks>
        /// A primeira linha é o resumo, sempre visível; o detalhe completo vai na dica de
        /// ferramenta, para não roubar espaço da tabela. Sem avisos, o rodapé some — a ausência de
        /// ressalva é informação, e um rodapé permanente dizendo "tudo certo" seria ignorado
        /// depois da terceira vez.
        /// </remarks>
        private void AtualizarRodapeDaCamada1()
        {
            if (RodapeDaCamada1 == null || TextoAvisosDaCamada1 == null) return;

            var avisos = _calculadorDeMetricas.AvisosDaCamada1;

            if (avisos == null || avisos.Count == 0)
            {
                RodapeDaCamada1.Visibility = WpfVisibility.Collapsed;
                TextoAvisosDaCamada1.ToolTip = null;
                return;
            }

            // O aviso de resumo do ajuste começa por "Camada 1:" e é o que interessa de relance;
            // os demais são ressalvas específicas. Sem ele, mostra-se o primeiro da lista.
            string resumo = avisos.FirstOrDefault(a => a.StartsWith("Camada 1:", StringComparison.Ordinal))
                            ?? avisos[0];

            int restantes = avisos.Count - 1;

            TextoAvisosDaCamada1.Text = restantes > 0
                ? $"{resumo}  (+{restantes} ressalva{(restantes > 1 ? "s" : "")} — passe o mouse para ver)"
                : resumo;

            TextoAvisosDaCamada1.ToolTip = string.Join(Environment.NewLine + Environment.NewLine, avisos);
            RodapeDaCamada1.Visibility = WpfVisibility.Visible;
        }

        /// <summary>
        /// Calcula as métricas por elemento das sessões filtradas, degradando para lista vazia se
        /// o cálculo falhar — a aba avisa e continua utilizável.
        /// </summary>
        private List<MetricasPorObjeto> CalcularMetricas(List<Sessao> sessoesFiltradas)
        {
            try
            {
                return _calculadorDeMetricas.Calcular(sessoesFiltradas);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao calcular métricas:\n{ex.Message}", "Aviso",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return new List<MetricasPorObjeto>();
            }
        }

        // ── Aba de Potencial Homeodinâmico: tabela de objetos (§5.5.1) ────

        /// <summary>
        /// Preenche a tabela de objetos do cenário selecionado, com a classificação de potencial
        /// que o XAML usa para pintar o texto de cada linha.
        /// </summary>
        private void AtualizarTabelaDeObjetos(List<MetricasPorObjeto> metricas, List<Sessao> sessoesFiltradas)
        {
            if (TabelaDeObjetos == null) return;

            var fixacoesPorObjeto = ContarFixacoesPorObjeto(sessoesFiltradas);
            var linhas = LinhaDeObjetoParaTabela.Construir(metricas, fixacoesPorObjeto);

            TabelaDeObjetos.ItemsSource = linhas;
            _linhasDaTabelaDePotencial = linhas;

            AtualizarRodapeDaCamada1();

            // Exportar tabela vazia produziria um PDF só com cabeçalho.
            if (BotaoExportarTabela != null)
                BotaoExportarTabela.IsEnabled = linhas.Any();

            if (TextoResumoDeObjetos == null) return;

            if (!linhas.Any())
            {
                TextoResumoDeObjetos.Text = "Selecione um cenário para listar os objetos";
                return;
            }

            int restauradores = linhas.Count(l => l.Classificacao == ClassificacaoDePotencial.Restaurador);
            int estressores = linhas.Count(l => l.Classificacao == ClassificacaoDePotencial.Estressor);
            int neutros = linhas.Count - restauradores - estressores;

            TextoResumoDeObjetos.Text =
                $"{linhas.Count} objeto(s) — {restauradores} restaurador(es), " +
                $"{estressores} estressor(es), {neutros} neutro(s)";
        }

        private static Dictionary<string, int> ContarFixacoesPorObjeto(IEnumerable<Sessao> sessoes)
        {
            var contagem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (sessoes == null) return contagem;

            foreach (var sessao in sessoes)
            {
                foreach (var fixacao in sessao.Fixacoes)
                {
                    foreach (var nome in fixacao.NomesDeObjetos)
                    {
                        contagem.TryGetValue(nome, out int atual);
                        contagem[nome] = atual + 1;
                    }
                }
            }

            return contagem;
        }

        // ── Auditoria da sessão: tabela de eventos detectados ─────────────

        /// <summary>
        /// Alterna, dentro da aba de Potencial Homeodinâmico, entre a tabela de objetos e a tabela
        /// de eventos detectados.
        /// </summary>
        private void AlternarAuditoria(object sender, RoutedEventArgs evento)
        {
            if (PainelAuditoria == null || PainelGraficos == null) return;

            bool exibirAuditoria = PainelAuditoria.Visibility != WpfVisibility.Visible;

            PainelAuditoria.Visibility = exibirAuditoria
                ? WpfVisibility.Visible
                : WpfVisibility.Collapsed;
            PainelGraficos.Visibility = exibirAuditoria
                ? WpfVisibility.Collapsed
                : WpfVisibility.Visible;

            BotaoAuditoria.Content = exibirAuditoria
                ? "Voltar à tabela de objetos"
                : "Eventos Detectados (auditoria)";

            // "Voltar à tabela de objetos" recebe fundo verde; o estado padrão permanece branco.
            BotaoAuditoria.Style = (Style)FindResource(
                exibirAuditoria ? "EstiloBotaoVerde" : "EstiloBotaoSecundario");

            if (exibirAuditoria && _auditoriaDesatualizada)
                AtualizarTabelaDeEventos();
        }

        /// <summary>
        /// O botão de auditoria só existe para a aba de Potencial Homeodinâmico — é lá que está o
        /// painel que ele alterna.
        /// </summary>
        private void AtualizarVisibilidadeDoBotaoDeAuditoria()
        {
            if (BotaoAuditoria == null || AbasDeAnalise == null) return;

            BotaoAuditoria.Visibility =
                ReferenceEquals(AbasDeAnalise.SelectedItem, AbaDePotencialHomeodinamico)
                    ? WpfVisibility.Visible
                    : WpfVisibility.Collapsed;
        }

        /// <summary>
        /// Monta a tabela de auditoria com TODOS os eventos detectados nas sessões filtradas,
        /// inclusive os que a janela temporal de análise descartou.
        /// </summary>
        /// <remarks>
        /// Duas leituras convivem em cada linha: a COR do texto é a classificação DESTA FIXAÇÃO
        /// ISOLADA — não a do objeto, ver <see cref="ClassificadorDeEventoDaAuditoria"/> —, e o
        /// ESMAECIMENTO marca o que ficou fora da janela
        /// <see cref="ParametrosDeDeteccaoOcular.JanelaTemporalInicioMs"/>–<see
        /// cref="ParametrosDeDeteccaoOcular.JanelaTemporalFimMs"/> e, portanto, não entrou em
        /// nenhum cálculo. Saccades saem sempre cinza: não se classifica um movimento.
        /// </remarks>
        private void AtualizarTabelaDeEventos()
        {
            if (TabelaDeEventos == null) return;

            var classificadorDeFixacao = new ClassificadorDeEventoDaAuditoria();
            classificadorDeFixacao.Preparar(_sessoesDaAuditoria);

            var linhas = new List<LinhaDeEventoParaTabela>();

            foreach (var sessao in _sessoesDaAuditoria)
                AdicionarEventosDaSessaoNaTabela(sessao, classificadorDeFixacao, linhas);

            TabelaDeEventos.ItemsSource = linhas;
            _auditoriaDesatualizada = false;

            AtualizarResumoDeEventos(linhas);
        }

        private void AtualizarResumoDeEventos(List<LinhaDeEventoParaTabela> linhas)
        {
            if (TextoResumoDeEventos == null) return;

            if (linhas.Count == 0)
            {
                TextoResumoDeEventos.Text = "Nenhum evento detectado nas sessões filtradas";
                return;
            }

            int considerados = linhas.Count(linha => linha.DentroDaJanelaDeAnalise);
            int descartados  = linhas.Count - considerados;

            double inicioMin = ParametrosDeDeteccaoOcular.JanelaTemporalInicioMs / 60_000.0;
            double fimMin    = ParametrosDeDeteccaoOcular.JanelaTemporalFimMs / 60_000.0;

            // A contagem por classificação é o ponto da tabela: mostra que um mesmo objeto rende
            // fixações dos três tipos, o que a classificação por elemento — uma média — não pode
            // mostrar.
            var fixacoes    = linhas.Where(linha => linha.TipoDoEvento == "Fixação").ToList();
            int restaurador = fixacoes.Count(l => l.Classificacao == ClassificacaoDePotencial.Restaurador);
            int estressor   = fixacoes.Count(l => l.Classificacao == ClassificacaoDePotencial.Estressor);
            int neutro      = fixacoes.Count(l => l.Classificacao == ClassificacaoDePotencial.Neutro);
            int semDado     = fixacoes.Count - restaurador - estressor - neutro;

            TextoResumoDeEventos.Text =
                $"{linhas.Count} evento(s) — {considerados} considerado(s) na janela de " +
                $"{inicioMin:0.##}–{fimMin:0.##} min, {descartados} fora dela (esmaecidos)   |   " +
                $"{fixacoes.Count} fixação(ões), classificadas uma a uma: {restaurador} restaurador(es), " +
                $"{estressor} estressor(es), {neutro} neutra(s)" +
                (semDado > 0 ? $", {semDado} sem ΔD" : string.Empty);
        }

        /// <summary>
        /// Acrescenta à tabela os eventos de uma sessão, ordenados pelo instante de início.
        /// </summary>
        private void AdicionarEventosDaSessaoNaTabela(
            Sessao sessao,
            ClassificadorDeEventoDaAuditoria classificadorDeFixacao,
            List<LinhaDeEventoParaTabela> linhas)
        {
            var inicioDaSessao = _detectorDeAuditoria.ObterInicioDaReferenciaTemporal(sessao.DadosBrutos);
            if (inicioDaSessao == null) return;

            foreach (var evento in ObterEventosDeAuditoria(sessao))
            {
                double decorridoMs = (evento.TimestampInicio - inicioDaSessao.Value).TotalMilliseconds;

                bool dentroDaJanela =
                    decorridoMs >= ParametrosDeDeteccaoOcular.JanelaTemporalInicioMs &&
                    decorridoMs <= ParametrosDeDeteccaoOcular.JanelaTemporalFimMs;

                // Só fixações são classificadas. Um saccade é um movimento: o diâmetro medido
                // durante ele não diz nada sobre elemento nenhum, e pintá-lo daria à tabela uma
                // afirmação que os dados não sustentam.
                bool ehFixacao = evento is Fixacao;

                var classificacao = ehFixacao
                    ? classificadorDeFixacao.Classificar((Fixacao)evento, sessao)
                    : ClassificacaoDePotencial.Neutro;

                double deltaCentrado = ehFixacao
                    ? classificadorDeFixacao.CalcularDeltaCentrado((Fixacao)evento, sessao)
                    : double.NaN;

                linhas.Add(new LinhaDeEventoParaTabela(
                    evento, inicioDaSessao.Value, classificacao, deltaCentrado, dentroDaJanela));
            }
        }

        /// <summary>
        /// Eventos da sessão detectados sem a janela temporal, em ordem cronológica. O resultado
        /// fica em cache: a detecção percorre as linhas brutas inteiras e é a parte cara da tabela.
        /// </summary>
        private List<EventoDeMovimentoOcular> ObterEventosDeAuditoria(Sessao sessao)
        {
            if (_eventosDeAuditoriaPorSessao.TryGetValue(sessao, out var emCache))
                return emCache;

            var (fixacoes, saccades) = _detectorDeAuditoria.DetectarNaSessaoInteira(sessao.DadosBrutos);

            var eventos = fixacoes
                .Cast<EventoDeMovimentoOcular>()
                .Concat(saccades)
                .OrderBy(evento => evento.TimestampInicio)
                .ToList();

            _eventosDeAuditoriaPorSessao[sessao] = eventos;
            return eventos;
        }

        /// <summary>
        /// Indexa a classificação de cada objeto por cenário. A identidade do elemento é
        /// objeto × cenário: um dicionário só por nome perderia a linha do outro cenário — e
        /// <see cref="_ultimasMetricas"/> traz os dois quando o filtro está em "todos os cenários".
        /// </summary>
        /// <remarks>
        /// SEM USO no momento: era por aqui que a auditoria pintava cada evento com a classificação
        /// do OBJETO, a mesma para todas as fixações sobre ele. A tabela passou a classificar cada
        /// fixação isoladamente (<see cref="ClassificadorDeEventoDaAuditoria"/>). Mantido porque
        /// voltar atrás é trocar de volta as duas chamadas em
        /// <see cref="AdicionarEventosDaSessaoNaTabela"/>.
        /// </remarks>
        private Dictionary<string, Dictionary<string, ClassificacaoDePotencial>>
            AgruparClassificacoesDeObjetoPorCenario()
        {
            return _ultimasMetricas
                .GroupBy(metrica => metrica.Cenario ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    porCenario => porCenario.Key,
                    porCenario => porCenario
                        .GroupBy(metrica => metrica.NomeDoObjeto, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            porObjeto => porObjeto.Key,
                            porObjeto => porObjeto.First().ClassificacaoPH,
                            StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
        }

        // ── Aba de Dados do Grupo: filtros e tabela de indivíduos (§5.5.2) ─

        /// <summary>
        /// Trocar qualquer um dos dois filtros substitui o gráfico exibido pelo da nova
        /// combinação. A aba não tem outro conteúdo a atualizar.
        /// </summary>
        private void SelecaoDeGrupoAlterada(object sender, SelectionChangedEventArgs evento)
        {
            if (PainelDeDispersoes == null) return;
            AtualizarGraficosDeDispersao();
        }

        // ── Reconstrução e filtros ───────────────────────────────────────

        /// <summary>
        /// Reconstrói o conjunto de sessões a partir das linhas do Input 01 e dos dados
        /// complementares atualmente carregados.
        /// </summary>
        /// <param name="novasLinhas">
        /// Linhas recém-analisadas do Input 01. Quando null, reutiliza as linhas já analisadas
        /// (<see cref="_linhasDeSessao"/>) — o CSV nunca é relido aqui, pois este método roda na
        /// thread da UI.
        /// </param>
        private void RecontruirConjuntoDeSessoes(List<LinhaDeSessao> novasLinhas)
        {
            if (novasLinhas != null)
                _linhasDeSessao = novasLinhas;

            if (_linhasDeSessao == null) return;
            _conjuntoDeSessoes = _construtor.Construir(_linhasDeSessao, _deltasDeCortisol, _indiceDeSensores);

            // As sessões antigas viraram lixo: o cache da auditoria é indexado por instância.
            _eventosDeAuditoriaPorSessao.Clear();
            _auditoriaDesatualizada = true;
        }

        // Avança da tela de instruções/carregamento para a tela de Análise do Grupo.
        private void Avancar_Click(object sender, RoutedEventArgs evento)
        {
            MostrarAnalise(true);
        }

        // Retorna à tela inicial de instruções, onde os botões de carregamento ficam visíveis.
        private void RecarregarArquivos_Click(object sender, RoutedEventArgs evento)
        {
            MostrarAnalise(false);
        }

        // Alterna entre a tela de instruções/carregamento e a tela de Análise do Grupo.
        // O rodapé de ações só aparece na tela de Análise do Grupo.
        private void MostrarAnalise(bool analise)
        {
            PainelInstrucoes.Visibility = analise
                ? WpfVisibility.Collapsed
                : WpfVisibility.Visible;
            PainelAnalise.Visibility = analise
                ? WpfVisibility.Visible
                : WpfVisibility.Collapsed;
            RodapeAcoes.Visibility = analise
                ? WpfVisibility.Visible
                : WpfVisibility.Collapsed;

            // O rodapé só existe na tela de análise, e o botão de auditoria só em uma das abas.
            if (analise) AtualizarVisibilidadeDoBotaoDeAuditoria();
        }

        /// <summary>
        /// Trocar o cenário repopula a lista de sessões antes de recalcular, porque a lista é
        /// derivada do cenário.
        /// </summary>
        private void CenarioAlterado(object sender, SelectionChangedEventArgs evento)
        {
            if (SeletorDeSessao == null) return;

            AtualizarSeletorDeSessao();
            FiltroAlterado(sender, evento);
        }

        private void FiltroAlterado(object sender, SelectionChangedEventArgs evento)
        {
            // Aguarda o próximo ciclo do dispatcher para garantir que todos os bindings
            // de seleção sejam atualizados antes de recalcular — evita re-entrância.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(AtualizarInterface));
        }

        private void AtualizarInterface()
        {
            if (_conjuntoDeSessoes == null) return;

            // Materializa o resultado dos filtros em uma lista para evitar múltiplas
            // avaliações da query LINQ (cada iteração reconstrói a cadeia de filtros).
            var sessoesFiltradas = AplicarFiltros(_conjuntoDeSessoes.Todas).ToList();

            // "Todos os Cenários" deixou de esconder os resultados: agrega todas as sessões
            // carregadas. A única condição para calcular é haver sessão depois dos filtros.
            _ultimasMetricas = sessoesFiltradas.Any()
                ? CalcularMetricas(sessoesFiltradas)
                : new List<MetricasPorObjeto>();

            // Tabela de objetos por Potencial Homeodinâmico (§5.5.1), na metade de cima da aba.
            AtualizarTabelaDeObjetos(_ultimasMetricas, sessoesFiltradas);

            // A auditoria enxerga as mesmas sessões, mas só é remontada quando está à vista: a
            // detecção sem janela temporal percorre as linhas brutas inteiras.
            _sessoesDaAuditoria = sessoesFiltradas;
            _auditoriaDesatualizada = true;
            if (PainelAuditoria?.Visibility == WpfVisibility.Visible)
                AtualizarTabelaDeEventos();

            // A geração de vistas usa TODAS as sessões do cenário (sem filtros),
            // portanto habilitamos sempre que houver sessões carregadas.
            BotaoGerarVistaRevit.IsEnabled  = _conjuntoDeSessoes.Todas.Any();
        }

        /// <summary>
        /// <summary>
        /// Encadeia os três filtros da aba — Cenário, Sessões e Participante — por E (AND).
        /// Cada opção "Todos…" deixa o seu eixo passar inteiro.
        /// </summary>
        private IEnumerable<Sessao> AplicarFiltros(IReadOnlyList<Sessao> todas)
        {
            var result = todas.AsEnumerable();
            result = AplicarFiltroDeCenario(result);
            result = AplicarFiltroDeSessao(result);
            return result;
        }

        /// <summary>
        /// Restringe as sessões ao cenário selecionado. Com
        /// <see cref="RotulosDaInterface.TodosOsCenarios"/> devolve todas — é o que faz a opção
        /// agregar o arquivo inteiro em vez de esconder os resultados.
        /// </summary>
        private IEnumerable<Sessao> AplicarFiltroDeCenario(IEnumerable<Sessao> sessoes)
        {
            string cenario = FiltroCenario.SelectedItem as string;

            if (string.IsNullOrEmpty(cenario) || cenario == RotulosDaInterface.TodosOsCenarios)
                return sessoes;

            return sessoes.Where(s => s.RotuloDeCenario == cenario);
        }

        /// <summary>
        /// Restringe as sessões à escolhida no seletor. Com
        /// <see cref="RotulosDaInterface.TodasAsSessoes"/> devolve todas.
        /// </summary>
        /// <remarks>
        /// <b>Escolher uma sessão muda o ESCOPO do índice, não só o que se vê.</b> A Camada 1 é
        /// estimada sobre as sessões que sobrevivem aos filtros; restrita a uma, ν̂_e, se(ν̂_e) e
        /// σ_ΔD passam a descrever aquele registro, e não o cenário. Os valores de PH_e exibidos
        /// mudam, e com poucas fixações a classificação tende a Neutro por falta de evidência.
        /// </remarks>
        private IEnumerable<Sessao> AplicarFiltroDeSessao(IEnumerable<Sessao> sessoes)
        {
            string nomeDaSessao = SeletorDeSessao?.SelectedItem as string;

            if (string.IsNullOrEmpty(nomeDaSessao) || nomeDaSessao == RotulosDaInterface.TodasAsSessoes)
                return sessoes;

            return sessoes.Where(s => string.Equals(s.RotuloDeSessao, nomeDaSessao,
                                                    StringComparison.OrdinalIgnoreCase));
        }

        // AplicarFiltroDeParticipante foi REMOVIDO junto com o dropdown "Participante" da aba de
        // Potencial Homeodinâmico. Restam os filtros de Cenário e de Sessões.

        // ── Cenários ─────────────────────────────────────────────────────

        private void AtualizarFiltrosDeScenario()
        {
            if (_conjuntoDeSessoes == null) return;

            var opcoes = new List<string> { RotulosDaInterface.TodosOsCenarios };

            // O dado real traz linhas com selectedScene vazio, que geravam uma opção em branco
            // no menu — indistinguível de um erro de carregamento para quem usa.
            opcoes.AddRange(_conjuntoDeSessoes.ObterTodosOsCenarios()
                                              .Where(c => !string.IsNullOrWhiteSpace(c)));

            FiltroCenario.ItemsSource   = opcoes;
            FiltroCenario.SelectedIndex = 0;
            FiltroCenario.IsEnabled          = true;

            AtualizarSeletorDeSessao();
        }

        // AtualizarFiltroDeParticipante foi REMOVIDO junto com o dropdown "Participante".

        /// <summary>
        /// Repopula a lista "Sessões" com os <see cref="Sessao.RotuloDeSessao"/> das sessões do
        /// cenário selecionado — um item por participante × assento × período.
        /// </summary>
        /// <remarks>
        /// O cenário FILTRA a lista: escolher um cenário reduz as sessões oferecidas às daquele
        /// cenário, e "Todos os Cenários" oferece todas. A primeira opção é sempre
        /// <see cref="RotulosDaInterface.TodasAsSessoes"/>, que não restringe nada — é o default e
        /// mantém a tabela agregando o cenário inteiro.
        /// </remarks>
        private void AtualizarSeletorDeSessao()
        {
            if (_conjuntoDeSessoes == null) return;

            var nomes = new List<string> { RotulosDaInterface.TodasAsSessoes };

            nomes.AddRange(AplicarFiltroDeCenario(_conjuntoDeSessoes.Todas)
                .Select(sessao => sessao.RotuloDeSessao)
                .Where(nome => !string.IsNullOrWhiteSpace(nome))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(nome => nome, StringComparer.OrdinalIgnoreCase));

            SeletorDeSessao.ItemsSource   = nomes;
            SeletorDeSessao.SelectedIndex = 0;
            SeletorDeSessao.IsEnabled     = true;
        }

        // ── Exportar a tabela ────────────────────────────────────────────

        /// <summary>
        /// Exporta em PDF a tabela de Potencial Homeodinâmico como está na tela.
        /// </summary>
        /// <remarks>
        /// Leva junto os três filtros vigentes porque a tabela não é apenas um recorte da
        /// exibição: restringir sessão ou participante muda o ESCOPO da estimativa, e ν̂_e, se(ν̂_e)
        /// e σ_ΔD passam a descrever aquele subconjunto. Um PDF sem o contexto não seria
        /// reproduzível.
        /// </remarks>
        private void BotaoExportarTabela_Click(object sender, RoutedEventArgs evento)
        {
            if (!_linhasDaTabelaDePotencial.Any())
            {
                MessageBox.Show("Não há linhas na tabela para exportar.", "Exportar a tabela",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialogo = new SaveFileDialog
            {
                Title = "Exportar a tabela de Potencial Homeodinâmico",
                Filter = "Documento PDF (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                FileName = MontarNomeDoArquivoExportado(),
            };

            if (dialogo.ShowDialog(this) != true)
                return;

            try
            {
                ExportadorDaTabelaDePotencial.Exportar(
                    _linhasDaTabelaDePotencial, MontarContextoDaExportacao(), dialogo.FileName);

                MessageBox.Show($"Tabela exportada para:\n{dialogo.FileName}", "Exportar a tabela",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao exportar a tabela:\n{ex.Message}", "Erro",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private ContextoDaExportacao MontarContextoDaExportacao()
        {
            var sessoesFiltradas = _conjuntoDeSessoes != null
                ? AplicarFiltros(_conjuntoDeSessoes.Todas).ToList()
                : new List<Sessao>();

            return new ContextoDaExportacao
            {
                Cenario             = FiltroCenario.SelectedItem as string ?? "—",
                Sessao              = SeletorDeSessao.SelectedItem as string ?? "—",
                QuantidadeDeSessoes = sessoesFiltradas.Count,

                CenariosIncluidos = sessoesFiltradas
                    .Select(sessao => sessao.RotuloDeCenario)
                    .Where(rotulo => !string.IsNullOrWhiteSpace(rotulo))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(rotulo => rotulo, StringComparer.OrdinalIgnoreCase)
                    .ToList(),

                PeriodosIncluidos = sessoesFiltradas
                    .Select(sessao => ExtrairPeriodoDoDia(sessao.RotuloDeCenario))
                    .Where(periodo => !string.IsNullOrWhiteSpace(periodo))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(periodo => periodo, StringComparer.OrdinalIgnoreCase)
                    .ToList(),

                // Os mesmos avisos do rodapé da aba: o relatório arquivado precisa dizer sob que
                // modelo os índices que ele imprime foram estimados.
                AvisosDaCamada1 = _calculadorDeMetricas.AvisosDaCamada1?.ToList() ?? new List<string>(),
            };
        }

        /// <summary>
        /// Período do dia de um rótulo de cenário, ou null quando não for identificável.
        /// </summary>
        /// <remarks>
        /// O rótulo de cenário é a coluna <c>nomeDaSessao</c> do CSV, composta pelo registrador
        /// como cena + período separados por sublinhado — "Cenário_01_Dia". O período é o último
        /// segmento.
        ///
        /// A leitura é deliberadamente tolerante: rótulo sem sublinhado, ou cujo último segmento
        /// esteja vazio, devolve null, e o relatório então cai de volta para listar os cenários
        /// por extenso. Preferiu-se isso a impor a convenção, porque o rótulo vem do arquivo de
        /// captura e não é controlado pelo plugin.
        /// </remarks>
        // CONFIRMADO nas capturas definitivas: a coluna nomeDaSessao de cenario_1.csv e
        // cenario_2.csv segue "Cenário_01_Dia" / "Cenário_02_Dia" — cena + período separados por
        // sublinhado, como o gerador sintético. Só há capturas diurnas até aqui; se uma captura
        // futura fugir da convenção, o fallback tolerante abaixo (null → cenários por extenso)
        // continua cobrindo o caso.
        private static string ExtrairPeriodoDoDia(string rotuloDeCenario)
        {
            if (string.IsNullOrWhiteSpace(rotuloDeCenario))
                return null;

            int ultimoSeparador = rotuloDeCenario.LastIndexOf('_');
            if (ultimoSeparador < 0 || ultimoSeparador == rotuloDeCenario.Length - 1)
                return null;

            return rotuloDeCenario.Substring(ultimoSeparador + 1).Trim();
        }

        /// <summary>
        /// Nome sugerido do arquivo, com o cenário e o carimbo de tempo. Os caracteres inválidos em
        /// nome de arquivo são trocados, porque o nome do cenário vem do dado e não é controlado.
        /// </summary>
        private string MontarNomeDoArquivoExportado()
        {
            string cenario = FiltroCenario.SelectedItem as string ?? "cenario";

            foreach (char invalido in Path.GetInvalidFileNameChars())
                cenario = cenario.Replace(invalido, '-');

            return $"PotencialHomeodinamico_{cenario}_{DateTime.Now:yyyy-MM-dd_HHmm}.pdf";
        }

        // ── Gerar Vista Revit ────────────────────────────────────────────

        /// <summary>
        /// Gera as vistas 3D dos cenários que o usuário marcar.
        /// </summary>
        /// <remarks>
        /// A escolha dos cenários é pedida numa janela própria, e não herdada do filtro da tela.
        /// Os filtros respondem "o que eu quero analisar"; esta pergunta é outra — "qual arquivo
        /// Revit está aberto agora" —, e só quem está diante do modelo sabe respondê-la. Os
        /// hitpoints são elementos de MODELO nas coordenadas do HMD: gerados sobre o arquivo
        /// errado, ficam sobre a geometria errada, e o Revit não acusa nada.
        ///
        /// A pergunta vem ANTES de qualquer transação, inclusive antes da carga da família — um
        /// cancelamento aqui não pode deixar rastro no documento.
        /// </remarks>
        private void BotaoGerarVistaRevit_Click(object sender, RoutedEventArgs evento)
        {
            if (_conjuntoDeSessoes == null) return;

            var cenariosAGerar = PerguntarQuaisCenariosGerar();

            if (cenariosAGerar == null || !cenariosAGerar.Any())
                return;

            try
            {
                string pasta    = _caminhoDoPlugin;
                var gerador     = new GeradorDeVistaRevit(_documento, _uiDocumento);

                var jaGerados = gerador.ObterCenariosComGeracaoAnterior(cenariosAGerar);

                // Sem geração anterior não há o que decidir: "Atualizar" não encontra nada para
                // apagar e se comporta como uma primeira geração.
                var decisao = jaGerados.Any()
                    ? PerguntarDecisaoDeGeracao(jaGerados)
                    : DecisaoDeGeracaoDeVista.Atualizar;

                if (decisao == DecisaoDeGeracaoDeVista.Cancelar)
                    return;

                var resultados  = gerador.Gerar(
                    _conjuntoDeSessoes,
                    cenariosAGerar,
                    pasta,
                    MontarClassificacoesPorCenarioEObjeto(cenariosAGerar),
                    decisao);
                ExibirResumoDeGeracaoDeVistas(resultados);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao gerar vista Revit:\n{ex.Message}", "Erro",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Abre a janela de marcação e devolve os cenários escolhidos, ou null se o usuário
        /// cancelar.
        /// </summary>
        /// <remarks>
        /// O cenário do filtro da tela entra pré-marcado por conveniência — é quase sempre o que a
        /// pessoa está olhando —, mas continua desmarcável, e "Todos os Cenários" não pré-marca
        /// nada.
        /// </remarks>
        private IReadOnlyList<string> PerguntarQuaisCenariosGerar()
        {
            var disponiveis = _conjuntoDeSessoes.ObterTodosOsCenarios()
                .Where(nome => !string.IsNullOrWhiteSpace(nome))
                .ToList();

            if (!disponiveis.Any())
            {
                MessageBox.Show(
                    "Nenhum cenário foi encontrado nas sessões carregadas.", "Gerar Vista Revit",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            string cenarioDoFiltro = FiltroCenario.SelectedItem as string;
            var preMarcados = !string.IsNullOrEmpty(cenarioDoFiltro)
                           && cenarioDoFiltro != RotulosDaInterface.TodosOsCenarios
                ? new List<string> { cenarioDoFiltro }
                : new List<string>();

            var janela = new JanelaDeSelecaoDeCenarios(disponiveis, preMarcados) { Owner = this };

            return janela.ShowDialog() == true ? janela.CenariosEscolhidos : null;
        }

        /// <summary>
        /// Pergunta o que fazer com a geração anterior dos cenários informados.
        /// </summary>
        /// <remarks>
        /// Usa o <see cref="TaskDialog"/> do Revit, e não um MessageBox: são três opções com
        /// rótulos próprios e uma explicação por opção, coisa que os botões fixos do MessageBox
        /// não comportam.
        ///
        /// "Atualizar" é o padrão porque, no uso corrente, regerar significa corrigir — mudou o
        /// filtro, entraram mais sessões, a família foi ajustada. Acumular é a exceção, e é
        /// justamente a opção que degrada o modelo; por isso ela é escolha explícita e vem com o
        /// aviso da sobreposição no próprio texto.
        /// </remarks>
        private static DecisaoDeGeracaoDeVista PerguntarDecisaoDeGeracao(
            IReadOnlyList<string> cenariosComGeracaoAnterior)
        {
            string instrucao = cenariosComGeracaoAnterior.Count == 1
                ? $"O cenário '{cenariosComGeracaoAnterior[0]}' já foi gerado neste modelo."
                : $"{cenariosComGeracaoAnterior.Count} dos cenários selecionados já foram gerados " +
                  "neste modelo.";

            var dialogo = new TaskDialog("Gerar Vista Revit")
            {
                MainInstruction   = instrucao,
                MainContent       = string.Join(", ", cenariosComGeracaoAnterior),
                AllowCancellation = true,
                CommonButtons     = TaskDialogCommonButtons.None,
                TitleAutoPrefix   = false,
            };

            dialogo.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                "Atualizar (recomendado)",
                "Apaga a vista anterior desses cenários e os hitpoints dela, e gera do zero. " +
                "O modelo fica com uma leitura só.");

            dialogo.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink2,
                "Manter a anterior e criar nova versão",
                "Não apaga nada. As esferas da geração anterior continuam no modelo, sobrepostas " +
                "às novas — se a classificação mudou, ficam duas cores no mesmo ponto.");

            dialogo.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink3,
                "Cancelar",
                "Não gera nada e não altera o modelo.");

            dialogo.DefaultButton = TaskDialogResult.CommandLink1;

            switch (dialogo.Show())
            {
                case TaskDialogResult.CommandLink1: return DecisaoDeGeracaoDeVista.Atualizar;
                case TaskDialogResult.CommandLink2: return DecisaoDeGeracaoDeVista.NovaVersao;
                default:                            return DecisaoDeGeracaoDeVista.Cancelar;
            }
        }

        /// <summary>
        /// Monta as pontuações de potencial restaurador por cenário e por objeto, usadas para
        /// colorir os hitpoints (Quadro 40).
        /// </summary>
        /// <param name="cenariosAConsiderar">
        /// Cenários a considerar, ou null/vazio para montar as pontuações de todos eles.
        /// </param>
        /// <remarks>
        /// As métricas são recalculadas aqui a partir de TODAS as sessões de cada cenário, em vez
        /// de reaproveitar <see cref="_ultimasMetricas"/>: aquele campo reflete o cenário filtrado
        /// na interface, enquanto a geração de vistas usa o conjunto completo. Reutilizá-lo
        /// coloriria os hitpoints com base em um subconjunto diferente do que foi inserido no
        /// modelo.
        /// </remarks>
        private IReadOnlyDictionary<string, IReadOnlyDictionary<string, ClassificacaoDePotencial>>
            MontarClassificacoesPorCenarioEObjeto(IReadOnlyList<string> cenariosAConsiderar)
        {
            var classificacoesPorCenario =
                new Dictionary<string, IReadOnlyDictionary<string, ClassificacaoDePotencial>>();

            if (_conjuntoDeSessoes == null)
                return classificacoesPorCenario;

            var cenarios = cenariosAConsiderar == null || cenariosAConsiderar.Count == 0
                ? _conjuntoDeSessoes.ObterTodosOsCenarios().ToList()
                : cenariosAConsiderar
                    .Where(nome => !string.IsNullOrWhiteSpace(nome))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            foreach (var nomeDoCenario in cenarios)
            {
                var sessoesDoCenario = _conjuntoDeSessoes.FiltrarPorCenario(nomeDoCenario).ToList();
                var metricas = _calculadorDeMetricas.Calcular(sessoesDoCenario);

                classificacoesPorCenario[nomeDoCenario] = metricas.ToDictionary(
                    metrica => metrica.NomeDoObjeto,
                    metrica => metrica.ClassificacaoPH,
                    StringComparer.OrdinalIgnoreCase);
            }

            return classificacoesPorCenario;
        }

        private void ExibirResumoDeGeracaoDeVistas(List<ResultadoDaGeracaoDeVista> resultados)
        {
            if (!resultados.Any())
            {
                MessageBox.Show("Nenhuma vista foi gerada.", "Aviso",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sb = new StringBuilder();

            sb.AppendLine($"{resultados.Count} vista(s) gerada(s) com sucesso:\n");

            foreach (var r in resultados)
            {
                sb.AppendLine($"  Cenário  : {r.NomeDoCenario}");
                sb.AppendLine($"  Vista    : {r.VistaCriada?.Name ?? "(não criada)"}");
                sb.AppendLine($"  Hitpoints: {r.NumeroDeHitpointsInseridos}");

                // Sem esta linha, uma vista que ficou mostrando as esferas de todos os cenários
                // tem exatamente a mesma aparência, no resumo, de uma vista corretamente isolada.
                sb.AppendLine($"  Ocultados: {r.NumeroDeHitpointsOcultados} hitpoint(s) de " +
                              "outros cenários");
                if (r.NumeroDeHitpointsRemovidos > 0 || r.NumeroDeVistasRemovidas > 0)
                {
                    sb.AppendLine($"  Removidos: {r.NumeroDeHitpointsRemovidos} hitpoint(s) e " +
                                  $"{r.NumeroDeVistasRemovidas} vista(s) da geração anterior");
                }
                if (r.Avisos.Any())
                    sb.AppendLine($"  Avisos   : {r.Avisos.Count}");
                sb.AppendLine();
            }

            int totalAvisos = resultados.Sum(r => r.Avisos.Count);
            if (totalAvisos > 0)
            {
                sb.AppendLine($"── Avisos ({totalAvisos}) ──────────────────");
                foreach (var r in resultados)
                    foreach (var aviso in r.Avisos.Take(5))
                        sb.AppendLine($"[{r.NomeDoCenario}] {aviso}");
                if (totalAvisos > resultados.Count * 5)
                    sb.AppendLine("(apenas os primeiros 5 avisos por cenário foram exibidos)");
            }

            MessageBox.Show(sb.ToString(), "Vistas Revit Geradas",
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Utilitários ──────────────────────────────────────────────────

        private string SelecionarArquivo(string filtro)
        {
            var dlg = new OpenFileDialog { Filter = filtro, Title = "Selecionar arquivo" };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        // ── Cores do status de carregamento ──────────────────────────────
        // O negrito das frases de status é definido uma vez no XAML (FontWeight="Bold" nos quatro
        // TextBlock) e persiste em todos os estados; aqui só a cor muda.
        // Tom ADOTADO: #1B5E20 (verde escuro), escolhido por legibilidade como cor de texto
        // sobre fundo claro — mesma exigência das CoresDeClassificacaoDePotencial. Decisão de
        // apresentação, sem significado além de "carregado com sucesso".

        /// <summary>Cor da frase de status quando o arquivo foi carregado com sucesso.</summary>
        private static readonly SolidColorBrush PincelDeStatusCarregado =
            new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20));

        /// <summary>Cor neutra da frase de status: estado inicial e durante o carregamento.</summary>
        private static readonly SolidColorBrush PincelDeStatusNeutro =
            new SolidColorBrush(Color.FromRgb(0x7A, 0x83, 0x92));

        /// <summary>Cor da frase de status quando o carregamento falha.</summary>
        private static readonly SolidColorBrush PincelDeStatusErro =
            new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

        /// <summary>
        /// Atualiza a frase de status e o rótulo do botão de um dos quatro carregamentos.
        /// </summary>
        /// <param name="texto">TextBlock de status abaixo do botão.</param>
        /// <param name="botao">Botão de carregamento correspondente.</param>
        /// <param name="caminho">Caminho do arquivo carregado.</param>
        /// <param name="carregando">
        /// Mensagem de progresso; quando null, considera-se que o carregamento terminou com
        /// sucesso e a frase fica verde com "✓".
        /// </param>
        private void AtualizarStatusBotao(TextBlock texto, Button botao, string caminho, string carregando)
        {
            if (carregando != null)
            {
                texto.Text   = carregando;
                texto.Foreground = PincelDeStatusNeutro;
                botao.Content = carregando;
            }
            else
            {
                string nome  = Path.GetFileName(caminho);
                texto.Text   = $"{nome} ✓";
                texto.Foreground = PincelDeStatusCarregado;
                botao.Content = $"Recarregar: {nome}";
            }
        }

        /// <summary>
        /// Marca a frase de status como falha, para que um status anteriormente verde não
        /// permaneça verde depois de um carregamento malsucedido.
        /// </summary>
        private void MarcarStatusComoErro(TextBlock texto)
        {
            texto.Text = "Erro no carregamento";
            texto.Foreground = PincelDeStatusErro;
        }

        private void ExibirSpinner(bool visivel)
            => TextoSpinner.Visibility = visivel ? WpfVisibility.Visible : WpfVisibility.Collapsed;

        private void ExibirErroDeCarregamento(string titulo, string msg)
            => MessageBox.Show(msg, titulo, MessageBoxButton.OK, MessageBoxImage.Error);

        private void ExibirAvisosDeCarregamento(string titulo, List<string> avisos)
        {
            string msg = string.Join("\n", avisos.Take(10));
            if (avisos.Count > 10) msg += $"\n... e mais {avisos.Count - 10} avisos.";
            MessageBox.Show(msg, titulo, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

    }
}
