# Interface/JanelaPluginCAISM.xaml.cs

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PluginCAISM.Dominio;
using PluginCAISM.Interface.Modelos;
using PluginCAISM.Interface.Controles;
using PluginCAISM.Parsing;
using PluginCAISM.Processamento;
using PluginCAISM.Revit;
using WpfVisibility = System.Windows.Visibility;

namespace PluginCAISM.Interface
{
    public partial class JanelaPluginCAISM : Window
    {
        private readonly Document _documento;
        private readonly UIDocument _uiDocumento;

        private readonly AnalisadorCsvSessao _analisadorSessao;
        private readonly AnalisadorExcelCortisol _analisadorCortisol;
        private readonly AnalisadorCsvSensor _analisadorSensor;
        private readonly ConstruturDeSessoes _construtor;
        private readonly CalculadorDeMetricasPorObjeto _calculadorDeMetricas;

        private ConjuntoDeSessoes _conjuntoDeSessoes;
        private Dictionary<string, double> _deltasDeCortisol;
        private Dictionary<DateTime, Dictionary<string, string>> _indiceDeSensores;

        private string _caminhoSessoes;
        private string _caminhoCortisol;
        private string _caminhoSensores;

        private static readonly string[] OpcoesDeMovimentoDoOlhar = { "(Todos)", "Fixação", "Saccade" };
        private static readonly string[] OpcoesDeDiametroPupilar = { "(Todos)", "Dilatada", "Contraída" };
        private static readonly string[] OpcoesDeDeltaCortisol = { "(Todos)", "Positivo (> 0)", "Negativo (< 0)", "Neutro (= 0)" };

        public JanelaPluginCAISM(Document documento, UIDocument uiDocumento)
        {
            InitializeComponent();
            _documento = documento;
            _uiDocumento = uiDocumento;

            var detector = new DetectorDeMovimentoOcular();
            _analisadorSessao = new AnalisadorCsvSessao();
            _analisadorCortisol = new AnalisadorExcelCortisol();
            _analisadorSensor = new AnalisadorCsvSensor();
            _construtor = new ConstruturDeSessoes(detector);
            _calculadorDeMetricas = new CalculadorDeMetricasPorObjeto();

            InicializarFiltrosComOpcoesPadrao();
        }

        private void InicializarFiltrosComOpcoesPadrao()
        {
            FiltroMovimentoDoOlhar.ItemsSource = OpcoesDeMovimentoDoOlhar;
            FiltroMovimentoDoOlhar.SelectedIndex = 0;

            FiltroDiametroPupilar.ItemsSource = OpcoesDeDiametroPupilar;
            FiltroDiametroPupilar.SelectedIndex = 0;

            FiltroDeltaCortisol.ItemsSource = OpcoesDeDeltaCortisol;
            FiltroDeltaCortisol.SelectedIndex = 0;
        }

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
            string caminho = SelecionarArquivo("CSV de Sensores|*.csv");
            if (caminho == null) return;

            _caminhoSensores = caminho;
            AtualizarStatusBotao(TextoStatusSensores, BotaoCarregarSensores, caminho, "Carregando sensores...");

            await CarregarSensoresEmBackground();
        }

        private async Task CarregarSessoesEmBackground()
        {
            ExibirSpinner(true);
            try
            {
                var avisos = new List<string>();
                var linhasBrutas = await Task.Run(() => _analisadorSessao.Analisar(_caminhoSessoes, avisos));

                RecontruirConjuntoDeSessoes(linhasBrutas);
                AtualizarFiltrosDeScenario();
                AtualizarInterface();

                if (avisos.Any())
                    ExibirAvisosDeCarregamento("Sessões carregadas com avisos", avisos);

                AtualizarStatusBotao(TextoStatusSessoes, BotaoCarregarSessoes, _caminhoSessoes, null);
            }
            catch (Exception excecao)
            {
                ExibirErroDeCarregamento("Erro ao carregar sessões", excecao.Message);
                TextoStatusSessoes.Text = "Erro no carregamento";
            }
            finally
            {
                ExibirSpinner(false);
            }
        }

        private async Task CarregarCortisolEmBackground()
        {
            ExibirSpinner(true);
            try
            {
                _deltasDeCortisol = await Task.Run(() => _analisadorCortisol.Analisar(_caminhoCortisol));

                if (_conjuntoDeSessoes != null)
                    RecontruirConjuntoDeSessoes(null);

                FiltroDeltaCortisol.IsEnabled = true;
                AtualizarInterface();
                AtualizarStatusBotao(TextoStatusCortisol, BotaoCarregarCortisol, _caminhoCortisol, null);
            }
            catch (Exception excecao)
            {
                ExibirErroDeCarregamento("Erro ao carregar cortisol", excecao.Message);
                TextoStatusCortisol.Text = "Erro no carregamento";
            }
            finally
            {
                ExibirSpinner(false);
            }
        }

        private async Task CarregarSensoresEmBackground()
        {
            ExibirSpinner(true);
            try
            {
                _indiceDeSensores = await Task.Run(() => _analisadorSensor.Analisar(_caminhoSensores));

                if (_conjuntoDeSessoes != null)
                    RecontruirConjuntoDeSessoes(null);

                AtualizarInterface();
                AtualizarStatusBotao(TextoStatusSensores, BotaoCarregarSensores, _caminhoSensores, null);
            }
            catch (Exception excecao)
            {
                ExibirErroDeCarregamento("Erro ao carregar sensores", excecao.Message);
                TextoStatusSensores.Text = "Erro no carregamento";
            }
            finally
            {
                ExibirSpinner(false);
            }
        }

        private void RecontruirConjuntoDeSessoes(List<LinhaDeSessao> novasLinhas)
        {
            if (novasLinhas == null && _caminhoSessoes != null)
            {
                var avisos = new List<string>();
                novasLinhas = _analisadorSessao.Analisar(_caminhoSessoes, avisos);
            }

            if (novasLinhas == null)
                return;

            _conjuntoDeSessoes = _construtor.Construir(novasLinhas, _deltasDeCortisol, _indiceDeSensores);
        }

        private void FiltroAlterado(object sender, SelectionChangedEventArgs evento)
        {
            AtualizarInterface();
        }

        private void AtualizarInterface()
        {
            if (_conjuntoDeSessoes == null)
                return;

            var sessoesFiltradas = AplicarFiltros(_conjuntoDeSessoes.Todas);
            AtualizarGraficosPorObjeto(sessoesFiltradas);
            AtualizarTabelaDeEventos(sessoesFiltradas);
            BotaoGerarVistaRevit.IsEnabled = sessoesFiltradas.Any();
        }

        private IEnumerable<Sessao> AplicarFiltros(IReadOnlyList<Sessao> todasAsSessoes)
        {
            var sessoesFiltradas = todasAsSessoes.AsEnumerable();

            sessoesFiltradas = AplicarFiltroDeCenario(sessoesFiltradas);
            sessoesFiltradas = AplicarFiltroDeDeltaCortisol(sessoesFiltradas);

            return sessoesFiltradas;
        }

        private IEnumerable<Sessao> AplicarFiltroDeCenario(IEnumerable<Sessao> sessoes)
        {
            string cenarioSelecionado = FiltroCenario.SelectedItem as string;

            if (string.IsNullOrEmpty(cenarioSelecionado) || cenarioSelecionado == "(Todos)")
                return sessoes;

            return sessoes.Where(sessao => sessao.CenarioSelecionado == cenarioSelecionado);
        }

        private IEnumerable<Sessao> AplicarFiltroDeDeltaCortisol(IEnumerable<Sessao> sessoes)
        {
            string opcaoSelecionada = FiltroDeltaCortisol.SelectedItem as string;

            if (string.IsNullOrEmpty(opcaoSelecionada) || opcaoSelecionada == "(Todos)")
                return sessoes;

            if (opcaoSelecionada == "Positivo (> 0)")
                return sessoes.Where(s => !double.IsNaN(s.DeltaCortisol) && s.DeltaCortisol > 0);

            if (opcaoSelecionada == "Negativo (< 0)")
                return sessoes.Where(s => !double.IsNaN(s.DeltaCortisol) && s.DeltaCortisol < 0);

            if (opcaoSelecionada == "Neutro (= 0)")
                return sessoes.Where(s => !double.IsNaN(s.DeltaCortisol) && s.DeltaCortisol == 0);

            return sessoes;
        }

        private void AtualizarGraficosPorObjeto(IEnumerable<Sessao> sessoesFiltradas)
        {
            PainelDeGraficosPorObjeto.Items.Clear();

            var metricasPorObjeto = _calculadorDeMetricas.Calcular(sessoesFiltradas);
            var tipoDeEventoFiltrado = ObterTipoDeEventoSelecionado();
            var tendenciaFiltrada = ObterTendenciaPupilarSelecionada();

            foreach (var metrica in metricasPorObjeto)
            {
                bool objetoPassaNoFiltro = VerificarSeFiltrosDeEventoAplicamAoObjeto(
                    sessoesFiltradas, metrica.NomeDoObjeto, tipoDeEventoFiltrado, tendenciaFiltrada);

                if (!objetoPassaNoFiltro)
                    continue;

                var controleDeGrafico = new ControleGraficoPorObjeto(metrica);
                PainelDeGraficosPorObjeto.Items.Add(controleDeGrafico);
            }
        }

        private bool VerificarSeFiltrosDeEventoAplicamAoObjeto(
            IEnumerable<Sessao> sessoes,
            string nomeDoObjeto,
            string tipoDeEvento,
            string tendenciaPupilar)
        {
            if (tipoDeEvento == "(Todos)" && tendenciaPupilar == "(Todos)")
                return true;

            bool temFixacoes = sessoes.Any(s => s.Fixacoes.Any(f => f.NomesDeObjetos.Contains(nomeDoObjeto)));
            bool temSaccades = sessoes.Any(s => s.Saccades.Any(sc => sc.NomesDeObjetos.Contains(nomeDoObjeto)));

            if (tipoDeEvento == "Fixação" && !temFixacoes)
                return false;

            if (tipoDeEvento == "Saccade" && !temSaccades)
                return false;

            if (tendenciaPupilar != "(Todos)")
            {
                var tendencia = tendenciaPupilar == "Dilatada"
                    ? TendenciaPupilar.Dilatada
                    : TendenciaPupilar.Contraida;

                bool temFixacaoComTendencia = sessoes.Any(s =>
                    s.ObterFixacoesPorTendenciaPupilar(tendencia)
                     .Any(f => f.NomesDeObjetos.Contains(nomeDoObjeto)));

                if (!temFixacaoComTendencia)
                    return false;
            }

            return true;
        }

        private void AtualizarTabelaDeEventos(IEnumerable<Sessao> sessoesFiltradas)
        {
            var tipoDeEvento = ObterTipoDeEventoSelecionado();
            var tendencia = ObterTendenciaPupilarSelecionada();
            var linhasDaTabela = new List<LinhaDeEventoParaTabela>();

            foreach (var sessao in sessoesFiltradas)
            {
                if (tipoDeEvento != "Saccade")
                    AdicionarFixacoesNaTabela(sessao, tendencia, linhasDaTabela);

                if (tipoDeEvento != "Fixação")
                    AdicionarSaccadesNaTabela(sessao, linhasDaTabela);
            }

            TabelaDeEventos.ItemsSource = linhasDaTabela;
        }

        private void AdicionarFixacoesNaTabela(Sessao sessao, string tendenciaSelecionada, List<LinhaDeEventoParaTabela> linhas)
        {
            var fixacoes = sessao.Fixacoes;

            if (tendenciaSelecionada == "Dilatada")
                fixacoes = sessao.ObterFixacoesPorTendenciaPupilar(TendenciaPupilar.Dilatada).ToList();
            else if (tendenciaSelecionada == "Contraída")
                fixacoes = sessao.ObterFixacoesPorTendenciaPupilar(TendenciaPupilar.Contraida).ToList();

            foreach (var fixacao in fixacoes)
                linhas.Add(new LinhaDeEventoParaTabela(fixacao));
        }

        private void AdicionarSaccadesNaTabela(Sessao sessao, List<LinhaDeEventoParaTabela> linhas)
        {
            foreach (var saccade in sessao.Saccades)
                linhas.Add(new LinhaDeEventoParaTabela(saccade));
        }

        private void AtualizarFiltrosDeScenario()
        {
            if (_conjuntoDeSessoes == null)
                return;

            var opcoes = new List<string> { "(Todos)" };
            opcoes.AddRange(_conjuntoDeSessoes.ObterTodosOsCenarios());

            FiltroCenario.ItemsSource = opcoes;
            FiltroCenario.SelectedIndex = 0;
            FiltroCenario.IsEnabled = true;
            FiltroMovimentoDoOlhar.IsEnabled = true;
            FiltroDiametroPupilar.IsEnabled = true;
        }

        private void BotaoGerarVistaRevit_Click(object sender, RoutedEventArgs evento)
        {
            if (_conjuntoDeSessoes == null)
                return;

            var sessoesFiltradas = AplicarFiltros(_conjuntoDeSessoes.Todas).ToList();

            if (!sessoesFiltradas.Any())
            {
                MessageBox.Show("Nenhuma sessão encontrada com os filtros atuais.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string caminhoDoPastaDoPlugin = ObterCaminhoDaPastaDoPlugin();
                var gerador = new GeradorDeVistaRevit(_documento, _uiDocumento);
                var resultado = gerador.Gerar(sessoesFiltradas, caminhoDoPastaDoPlugin);

                ExibirResumoDeGeracaoDeVista(resultado);
            }
            catch (Exception excecao)
            {
                MessageBox.Show(
                    $"Erro ao gerar vista Revit:\n{excecao.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExibirResumoDeGeracaoDeVista(ResultadoDaGeracaoDeVista resultado)
        {
            string mensagem = $"Vista '{resultado.VistaCriada?.Name}' criada com sucesso.\n" +
                              $"{resultado.NumeroDeSessoesProcessadas} sessão(ões) processada(s).\n" +
                              $"{resultado.NumeroDeFasesCriadas} fase(s) criada(s).";

            if (resultado.Avisos.Any())
                mensagem += $"\n\nAvisos ({resultado.Avisos.Count}):\n" + string.Join("\n", resultado.Avisos.Take(5));

            MessageBox.Show(mensagem, "Vista Revit Gerada", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private string ObterCaminhoDaPastaDoPlugin()
        {
            return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        private string SelecionarArquivo(string filtro)
        {
            var dialogo = new OpenFileDialog { Filter = filtro, Title = "Selecionar arquivo" };
            return dialogo.ShowDialog() == true ? dialogo.FileName : null;
        }

        private void AtualizarStatusBotao(TextBlock textoStatus, Button botao, string caminho, string mensagemCarregando)
        {
            if (mensagemCarregando != null)
            {
                textoStatus.Text = mensagemCarregando;
                botao.Content = mensagemCarregando;
            }
            else
            {
                string nomeDoArquivo = Path.GetFileName(caminho);
                textoStatus.Text = $"{nomeDoArquivo} ✓";
                botao.Content = $"Recarregar: {nomeDoArquivo}";
            }
        }

        private void ExibirSpinner(bool visivel)
        {
            TextoSpinner.Visibility = visivel ? WpfVisibility.Visible : WpfVisibility.Collapsed;
        }

        private void ExibirErroDeCarregamento(string titulo, string mensagem)
        {
            MessageBox.Show(mensagem, titulo, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ExibirAvisosDeCarregamento(string titulo, List<string> avisos)
        {
            string mensagem = string.Join("\n", avisos.Take(10));
            if (avisos.Count > 10)
                mensagem += $"\n... e mais {avisos.Count - 10} avisos.";

            MessageBox.Show(mensagem, titulo, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private string ObterTipoDeEventoSelecionado()
        {
            return FiltroMovimentoDoOlhar.SelectedItem as string ?? "(Todos)";
        }

        private string ObterTendenciaPupilarSelecionada()
        {
            return FiltroDiametroPupilar.SelectedItem as string ?? "(Todos)";
        }
    }
}
```


# Interface/Modelos/LinhaDeEventoParaTabela.cs

```csharp
using System;
using System.Linq;
using PluginCAISM.Dominio;

namespace PluginCAISM.Interface.Modelos
{
    public class LinhaDeEventoParaTabela
    {
        public string TipoDoEvento { get; }

        public double DuracaoMs { get; }

        public DateTime TimestampInicio { get; }

        public DateTime TimestampFim { get; }

        public string NomesDeObjetos { get; }

        public double PontoX { get; }

        public double PontoY { get; }

        public double PontoZ { get; }

        public double DiametroPupilaEsquerda { get; }

        public double DiametroPupilaDireita { get; }

        public string TendenciaPupilar { get; }

        public LinhaDeEventoParaTabela(EventoDeMovimentoOcular evento)
        {
            TipoDoEvento          = evento is Fixacao ? "Fixação" : "Saccade";
            DuracaoMs             = evento.DuracaoEmMilissegundos;
            TimestampInicio       = evento.TimestampInicio;
            TimestampFim          = evento.TimestampFim;
            NomesDeObjetos        = string.Join(", ", evento.NomesDeObjetos.OrderBy(nome => nome));
            PontoX                = evento.PontoMedioDeIncidenciaX;
            PontoY                = evento.PontoMedioDeIncidenciaY;
            PontoZ                = evento.PontoMedioDeIncidenciaZ;
            DiametroPupilaEsquerda = evento.DiametroMedioPupilaEsquerda;
            DiametroPupilaDireita  = evento.DiametroMedioPupilaDireita;
            TendenciaPupilar      = evento is Fixacao fixacao
                ? fixacao.TendenciaPupilar.ToString()
                : "N/A";
        }
    }
}
```


# Parsing/AnalisadorCsvSensor.cs

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PluginCAISM.Parsing
{
    public class AnalisadorCsvSensor
    {
        private const string NomeDaColunaDeTimestamp = "timeStamp";
        private const string FormatoDeTimestamp = "yyyy-MM-dd HH:mm:ss.fff";

        public Dictionary<DateTime, Dictionary<string, string>> Analisar(string caminhoDoCsv)
        {
            var linhas = LerLinhasDoArquivo(caminhoDoCsv);

            if (linhas.Count < 2)
                return new Dictionary<DateTime, Dictionary<string, string>>();

            var cabecalhos = linhas[0];
            int indiceDeTimestamp = EncontrarIndiceDeTimestamp(cabecalhos);

            if (indiceDeTimestamp < 0)
                return new Dictionary<DateTime, Dictionary<string, string>>();

            return ConstruirIndice(linhas, cabecalhos, indiceDeTimestamp);
        }

        private List<string[]> LerLinhasDoArquivo(string caminhoDoCsv)
        {
            var linhas = new List<string[]>();

            using var leitor = new StreamReader(caminhoDoCsv, Encoding.UTF8);
            string linhaAtual;
            while ((linhaAtual = leitor.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(linhaAtual))
                    linhas.Add(SepararCamposRespeitandoAspas(linhaAtual));
            }

            return linhas;
        }

        private int EncontrarIndiceDeTimestamp(string[] cabecalhos)
        {
            for (int indice = 0; indice < cabecalhos.Length; indice++)
            {
                if (string.Equals(cabecalhos[indice].Trim(), NomeDaColunaDeTimestamp, StringComparison.OrdinalIgnoreCase))
                    return indice;
            }

            return -1;
        }

        private Dictionary<DateTime, Dictionary<string, string>> ConstruirIndice(
            List<string[]> linhas,
            string[] cabecalhos,
            int indiceDeTimestamp)
        {
            var indice = new Dictionary<DateTime, Dictionary<string, string>>();

            for (int numeroLinha = 1; numeroLinha < linhas.Count; numeroLinha++)
            {
                var campos = linhas[numeroLinha];

                if (!TentarObterTimestamp(campos, indiceDeTimestamp, out DateTime timestamp))
                    continue;

                var camposDaLinha = ExtrairCamposDaLinha(campos, cabecalhos, indiceDeTimestamp);
                indice[timestamp] = camposDaLinha;
            }

            return indice;
        }

        private bool TentarObterTimestamp(string[] campos, int indiceDeTimestamp, out DateTime timestamp)
        {
            if (indiceDeTimestamp >= campos.Length)
            {
                timestamp = default;
                return false;
            }

            return DateTime.TryParseExact(
                campos[indiceDeTimestamp].Trim(),
                FormatoDeTimestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out timestamp);
        }

        private Dictionary<string, string> ExtrairCamposDaLinha(
            string[] campos,
            string[] cabecalhos,
            int indiceDeTimestamp)
        {
            var camposDaLinha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int indice = 0; indice < cabecalhos.Length; indice++)
            {
                if (indice == indiceDeTimestamp)
                    continue;

                string nomeDaColuna = cabecalhos[indice].Trim();
                string valor = indice < campos.Length ? campos[indice].Trim() : string.Empty;
                camposDaLinha[nomeDaColuna] = valor;
            }

            return camposDaLinha;
        }

        private string[] SepararCamposRespeitandoAspas(string linha)
        {
            var campos = new List<string>();
            var campoAtual = new StringBuilder();
            bool dentroDeAspas = false;

            foreach (char caractere in linha)
            {
                if (caractere == '"')
                {
                    dentroDeAspas = !dentroDeAspas;
                }
                else if (caractere == ',' && !dentroDeAspas)
                {
                    campos.Add(campoAtual.ToString());
                    campoAtual.Clear();
                }
                else
                {
                    campoAtual.Append(caractere);
                }
            }

            campos.Add(campoAtual.ToString());
            return campos.ToArray();
        }
    }
}
```


# Parsing/AnalisadorCsvSessao.cs

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PluginCAISM.Dominio;

namespace PluginCAISM.Parsing
{
    public class AnalisadorCsvSessao
    {
        private static readonly string[] CabecalhosObrigatorios =
        {
            "timeStamp", "identificacaoManual", "nomeDaSessao", "selectedScene",
            "selectedSeat", "nomeDoObjeto", "left_PupilDiametre", "right_PupilDiametre",
            "leftHitPoint_X", "leftHitPoint_Y", "leftHitPoint_Z",
            "rightHitPoint_X", "rightHitPoint_Y", "rightHitPoint_Z",
            "left_EyeOpened", "right_EyeOpened"
        };

        private const string FormatoDeTimestamp = "yyyy-MM-dd HH:mm:ss.fff";

        public List<LinhaDeSessao> Analisar(string caminhoDoCsv, List<string> avisosDeLinhasIgnoradas)
        {
            var todasAsLinhas = LerLinhasDoArquivo(caminhoDoCsv);
            var mapeamentoDeColunas = ExtrairMapeamentoDeColunas(todasAsLinhas[0]);

            ValidarCabecalhosObrigatorios(mapeamentoDeColunas);

            var linhasAnalisadas = AnalisarLinhasDeConteudo(todasAsLinhas, mapeamentoDeColunas, avisosDeLinhasIgnoradas);

            return linhasAnalisadas.OrderBy(linha => linha.Timestamp).ToList();
        }

        private List<string[]> LerLinhasDoArquivo(string caminhoDoCsv)
        {
            var linhas = new List<string[]>();

            using var leitor = new StreamReader(caminhoDoCsv, Encoding.UTF8);
            string linhaAtual;
            while ((linhaAtual = leitor.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(linhaAtual))
                    linhas.Add(SepararCamposRespeitandoAspas(linhaAtual));
            }

            return linhas;
        }

        private Dictionary<string, int> ExtrairMapeamentoDeColunas(string[] cabecalho)
        {
            var mapeamento = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int indice = 0; indice < cabecalho.Length; indice++)
                mapeamento[cabecalho[indice].Trim()] = indice;

            return mapeamento;
        }

        private void ValidarCabecalhosObrigatorios(Dictionary<string, int> mapeamentoDeColunas)
        {
            var cabecalhosAusentes = CabecalhosObrigatorios
                .Where(cabecalho => !mapeamentoDeColunas.ContainsKey(cabecalho))
                .ToList();

            if (cabecalhosAusentes.Any())
                throw new InvalidOperationException(
                    $"O CSV de sessões não contém os seguintes cabeçalhos obrigatórios: {string.Join(", ", cabecalhosAusentes)}");
        }

        private List<LinhaDeSessao> AnalisarLinhasDeConteudo(
            List<string[]> todasAsLinhas,
            Dictionary<string, int> mapeamentoDeColunas,
            List<string> avisosDeLinhasIgnoradas)
        {
            var resultado = new List<LinhaDeSessao>();

            for (int numeroLinha = 1; numeroLinha < todasAsLinhas.Count; numeroLinha++)
            {
                var campos = todasAsLinhas[numeroLinha];
                var linhaOuNulo = TentarAnalisarLinha(campos, mapeamentoDeColunas, numeroLinha, avisosDeLinhasIgnoradas);

                if (linhaOuNulo != null)
                    resultado.Add(linhaOuNulo);
            }

            return resultado;
        }

        private LinhaDeSessao TentarAnalisarLinha(
            string[] campos,
            Dictionary<string, int> mapeamentoDeColunas,
            int numeroLinha,
            List<string> avisosDeLinhasIgnoradas)
        {
            var valorTimestamp = LerCampo(campos, mapeamentoDeColunas, "timeStamp");

            if (!DateTime.TryParseExact(valorTimestamp, FormatoDeTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime timestamp))
            {
                avisosDeLinhasIgnoradas.Add($"Linha {numeroLinha}: timestamp inválido '{valorTimestamp}'. Linha ignorada.");
                return null;
            }

            return ConstruirLinhaDeSessao(campos, mapeamentoDeColunas, numeroLinha - 1, timestamp);
        }

        private LinhaDeSessao ConstruirLinhaDeSessao(
            string[] campos,
            Dictionary<string, int> mapeamentoDeColunas,
            int indiceOriginal,
            DateTime timestamp)
        {
            return new LinhaDeSessao
            {
                IndiceLinhaOriginal            = indiceOriginal,
                Timestamp                      = timestamp,
                IdentificacaoManual            = LerCampo(campos, mapeamentoDeColunas, "identificacaoManual"),
                NomeDaSessao                   = LerCampo(campos, mapeamentoDeColunas, "nomeDaSessao"),
                CenarioSelecionado             = LerCampo(campos, mapeamentoDeColunas, "selectedScene"),
                AssentoSelecionado             = LerCampo(campos, mapeamentoDeColunas, "selectedSeat"),
                NomeDoObjeto                   = LerCampo(campos, mapeamentoDeColunas, "nomeDoObjeto"),
                DirecaoOlharEsquerdoX          = LerDouble(campos, mapeamentoDeColunas, "left_EyeGaze_X"),
                DirecaoOlharDireitoX           = LerDouble(campos, mapeamentoDeColunas, "right_EyeGaze_X"),
                DirecaoOlharEsquerdoY          = LerDouble(campos, mapeamentoDeColunas, "left_EyeGaze_Y"),
                DirecaoOlharDireitoY           = LerDouble(campos, mapeamentoDeColunas, "right_EyeGaze_Y"),
                DirecaoOlharEsquerdoZ          = LerDouble(campos, mapeamentoDeColunas, "left_EyeGaze_Z"),
                DirecaoOlharDireitoZ           = LerDouble(campos, mapeamentoDeColunas, "right_EyeGaze_Z"),
                OrientacaoOlhoEsquerdoX        = LerDouble(campos, mapeamentoDeColunas, "left_EyeOrientation_X"),
                OrientacaoOlhoDireitoX         = LerDouble(campos, mapeamentoDeColunas, "right_EyeOrientation_X"),
                OrientacaoOlhoEsquerdoY        = LerDouble(campos, mapeamentoDeColunas, "left_EyeOrientation_Y"),
                OrientacaoOlhoDireitoY         = LerDouble(campos, mapeamentoDeColunas, "right_EyeOrientation_Y"),
                OrientacaoOlhoEsquerdoZ        = LerDouble(campos, mapeamentoDeColunas, "left_EyeOrientation_Z"),
                OrientacaoOlhoDireitoZ         = LerDouble(campos, mapeamentoDeColunas, "right_EyeOrientation_Z"),
                OrientacaoOlhoEsquerdoW        = LerDouble(campos, mapeamentoDeColunas, "left_EyeOrientation_W"),
                OrientacaoOlhoDireitoW         = LerDouble(campos, mapeamentoDeColunas, "right_EyeOrientation_W"),
                DiametroPupilaEsquerda         = LerDouble(campos, mapeamentoDeColunas, "left_PupilDiametre"),
                DiametroPupilaDireita          = LerDouble(campos, mapeamentoDeColunas, "right_PupilDiametre"),
                OlhoEsquerdoAberto             = LerInt(campos, mapeamentoDeColunas, "left_EyeOpened"),
                OlhoDireitoAberto              = LerInt(campos, mapeamentoDeColunas, "right_EyeOpened"),
                PosicaoJogadorX                = LerDouble(campos, mapeamentoDeColunas, "playerPosition_x"),
                PosicaoJogadorY                = LerDouble(campos, mapeamentoDeColunas, "playerPosition_y"),
                PosicaoJogadorZ                = LerDouble(campos, mapeamentoDeColunas, "playerPosition_z"),
                RotacaoJogadorX                = LerDouble(campos, mapeamentoDeColunas, "playerRotation_x"),
                RotacaoJogadorY                = LerDouble(campos, mapeamentoDeColunas, "playerRotation_y"),
                RotacaoJogadorZ                = LerDouble(campos, mapeamentoDeColunas, "playerRotation_z"),
                RotacaoJogadorW                = LerDouble(campos, mapeamentoDeColunas, "playerRotation_w"),
                PontoDeIncidenciaEsquerdoX     = LerDouble(campos, mapeamentoDeColunas, "leftHitPoint_X"),
                PontoDeIncidenciaEsquerdoY     = LerDouble(campos, mapeamentoDeColunas, "leftHitPoint_Y"),
                PontoDeIncidenciaEsquerdoZ     = LerDouble(campos, mapeamentoDeColunas, "leftHitPoint_Z"),
                PontoDeIncidenciaDireitoX      = LerDouble(campos, mapeamentoDeColunas, "rightHitPoint_X"),
                PontoDeIncidenciaDireitoY      = LerDouble(campos, mapeamentoDeColunas, "rightHitPoint_Y"),
                PontoDeIncidenciaDireitoZ      = LerDouble(campos, mapeamentoDeColunas, "rightHitPoint_Z"),
                NomeDispositivo                = LerCampo(campos, mapeamentoDeColunas, "htcDeviceName")
            };
        }

        private string LerCampo(string[] campos, Dictionary<string, int> mapeamento, string nomeDaColuna)
        {
            if (!mapeamento.TryGetValue(nomeDaColuna, out int indice) || indice >= campos.Length)
                return string.Empty;

            return campos[indice].Trim();
        }

        private double LerDouble(string[] campos, Dictionary<string, int> mapeamento, string nomeDaColuna)
        {
            var valor = LerCampo(campos, mapeamento, nomeDaColuna);

            if (double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out double resultado))
                return resultado;

            return 0.0;
        }

        private int LerInt(string[] campos, Dictionary<string, int> mapeamento, string nomeDaColuna)
        {
            var valor = LerCampo(campos, mapeamento, nomeDaColuna);

            if (int.TryParse(valor, out int resultado))
                return resultado;

            return 0;
        }

        private string[] SepararCamposRespeitandoAspas(string linha)
        {
            var campos = new List<string>();
            var campoAtual = new StringBuilder();
            bool dentroDeAspas = false;

            foreach (char caractere in linha)
            {
                if (caractere == '"')
                {
                    dentroDeAspas = !dentroDeAspas;
                }
                else if (caractere == ',' && !dentroDeAspas)
                {
                    campos.Add(campoAtual.ToString());
                    campoAtual.Clear();
                }
                else
                {
                    campoAtual.Append(caractere);
                }
            }

            campos.Add(campoAtual.ToString());
            return campos.ToArray();
        }
    }
}
```


# Parsing/AnalisadorExcelCortisol.cs

```csharp
using System;
using System.Collections.Generic;
using OfficeOpenXml;

namespace PluginCAISM.Parsing
{
    public class AnalisadorExcelCortisol
    {
        private const int IndiceColunaNomeDaSessao = 1;
        private const int IndiceColunaValorBasal = 2;
        private const int PrimeiraColunaDeMedicaoAdicional = 3;

        public Dictionary<string, double> Analisar(string caminhoDoExcel)
        {
            using var pacote = AbrirArquivoExcel(caminhoDoExcel);
            var planilha = ObterPrimeiraPlanilha(pacote);

            ValidarPlanilha(planilha);

            return ExtrairDeltasDeCortisol(planilha);
        }

        private ExcelPackage AbrirArquivoExcel(string caminhoDoExcel)
        {
            var streamDeArquivo = new System.IO.FileStream(
                caminhoDoExcel,
                System.IO.FileMode.Open,
                System.IO.FileAccess.Read,
                System.IO.FileShare.ReadWrite);

            return new ExcelPackage(streamDeArquivo);
        }

        private ExcelWorksheet ObterPrimeiraPlanilha(ExcelPackage pacote)
        {
            if (pacote.Workbook.Worksheets.Count == 0)
                throw new InvalidOperationException("O arquivo Excel de cortisol não contém nenhuma planilha.");

            return pacote.Workbook.Worksheets[0];
        }

        private void ValidarPlanilha(ExcelWorksheet planilha)
        {
            if (planilha.Dimension == null)
                throw new InvalidOperationException("A planilha de cortisol está vazia.");

            var primeiroValorColunaA = planilha.Cells[2, IndiceColunaNomeDaSessao].Text;
            if (string.IsNullOrWhiteSpace(primeiroValorColunaA))
                throw new InvalidOperationException(
                    "A coluna A do arquivo de cortisol não contém valores de nomeDaSessao na linha 2.");
        }

        private Dictionary<string, double> ExtrairDeltasDeCortisol(ExcelWorksheet planilha)
        {
            var deltasPorSessao = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            int totalDeLinhas = planilha.Dimension.Rows;
            int totalDeColunas = planilha.Dimension.Columns;

            for (int numeroLinha = 2; numeroLinha <= totalDeLinhas; numeroLinha++)
            {
                var nomeDaSessao = planilha.Cells[numeroLinha, IndiceColunaNomeDaSessao].Text?.Trim();

                if (string.IsNullOrWhiteSpace(nomeDaSessao))
                    continue;

                double deltaCortisol = CalcularDelta(planilha, numeroLinha, totalDeColunas);
                deltasPorSessao[nomeDaSessao] = deltaCortisol;
            }

            return deltasPorSessao;
        }

        private double CalcularDelta(ExcelWorksheet planilha, int numeroLinha, int totalDeColunas)
        {
            double valorBasal = LerValorNumerico(planilha, numeroLinha, IndiceColunaValorBasal);

            if (double.IsNaN(valorBasal))
                return double.NaN;

            double valorFinal = EncontrarUltimoValorNumericoDaLinha(planilha, numeroLinha, totalDeColunas);

            if (double.IsNaN(valorFinal))
                return double.NaN;

            return valorFinal - valorBasal;
        }

        private double EncontrarUltimoValorNumericoDaLinha(ExcelWorksheet planilha, int numeroLinha, int totalDeColunas)
        {
            for (int indiceColuna = totalDeColunas; indiceColuna >= PrimeiraColunaDeMedicaoAdicional; indiceColuna--)
            {
                double valor = LerValorNumerico(planilha, numeroLinha, indiceColuna);

                if (!double.IsNaN(valor))
                    return valor;
            }

            return double.NaN;
        }

        private double LerValorNumerico(ExcelWorksheet planilha, int numeroLinha, int indiceColuna)
        {
            var celula = planilha.Cells[numeroLinha, indiceColuna];

            if (celula.Value == null || string.IsNullOrWhiteSpace(celula.Text))
                return double.NaN;

            if (double.TryParse(celula.Text,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double valor))
            {
                return valor;
            }

            return double.NaN;
        }
    }
}
```


# Processamento/CalculadorDeMetricasPorObjeto.cs

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Processamento
{
    public class CalculadorDeMetricasPorObjeto
    {
        public List<MetricasPorObjeto> Calcular(IEnumerable<Sessao> sessoes)
        {
            var todasAsFixacoes = ColetarTodasAsFixacoes(sessoes);
            var fixacoesPorObjeto = AgruparFixacoesPorObjeto(todasAsFixacoes);

            double pupilaMinima = EncontrarPupilaMinima(fixacoesPorObjeto);
            double pupilaMaxima = EncontrarPupilaMaxima(fixacoesPorObjeto);

            var metricas = fixacoesPorObjeto
                .Select(par => ConstruirMetricasParaObjeto(par.Key, par.Value, pupilaMinima, pupilaMaxima))
                .OrderByDescending(metrica => metrica.PontuacaoPotencialRestaurador)
                .ToList();

            return metricas;
        }

        private List<(Fixacao fixacao, LinhaDeSessao linhaOrigem)> ColetarTodasAsFixacoes(IEnumerable<Sessao> sessoes)
        {
            var resultado = new List<(Fixacao, LinhaDeSessao)>();

            foreach (var sessao in sessoes)
            {
                foreach (var fixacao in sessao.Fixacoes)
                {
                    var linhaRepresentativa = ObterLinhaRepresentativaDaFixacao(fixacao, sessao.DadosBrutos);
                    resultado.Add((fixacao, linhaRepresentativa));
                }
            }

            return resultado;
        }

        private LinhaDeSessao ObterLinhaRepresentativaDaFixacao(Fixacao fixacao, IReadOnlyList<LinhaDeSessao> dadosBrutos)
        {
            if (fixacao.IndicesLinhasOriginais.Count == 0)
                return null;

            int indice = fixacao.IndicesLinhasOriginais[0];
            return dadosBrutos.FirstOrDefault(linha => linha.IndiceLinhaOriginal == indice);
        }

        private Dictionary<string, List<(Fixacao fixacao, LinhaDeSessao linhaOrigem)>> AgruparFixacoesPorObjeto(
            List<(Fixacao fixacao, LinhaDeSessao linhaOrigem)> fixacoesComOrigem)
        {
            var grupos = new Dictionary<string, List<(Fixacao, LinhaDeSessao)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (fixacao, linhaOrigem) in fixacoesComOrigem)
            {
                foreach (var nomeDoObjeto in fixacao.NomesDeObjetos)
                {
                    if (!grupos.TryGetValue(nomeDoObjeto, out var lista))
                    {
                        lista = new List<(Fixacao, LinhaDeSessao)>();
                        grupos[nomeDoObjeto] = lista;
                    }

                    lista.Add((fixacao, linhaOrigem));
                }
            }

            return grupos;
        }

        private double EncontrarPupilaMinima(Dictionary<string, List<(Fixacao, LinhaDeSessao)>> fixacoesPorObjeto)
        {
            var valoresValidos = ExtrairValoresPupilaresDasFixacoes(fixacoesPorObjeto);
            return valoresValidos.Any() ? valoresValidos.Min() : 0.0;
        }

        private double EncontrarPupilaMaxima(Dictionary<string, List<(Fixacao, LinhaDeSessao)>> fixacoesPorObjeto)
        {
            var valoresValidos = ExtrairValoresPupilaresDasFixacoes(fixacoesPorObjeto);
            return valoresValidos.Any() ? valoresValidos.Max() : 1.0;
        }

        private List<double> ExtrairValoresPupilaresDasFixacoes(
            Dictionary<string, List<(Fixacao fixacao, LinhaDeSessao linha)>> fixacoesPorObjeto)
        {
            return fixacoesPorObjeto.Values
                .SelectMany(lista => lista)
                .Select(par => (par.fixacao.DiametroMedioPupilaEsquerda + par.fixacao.DiametroMedioPupilaDireita) / 2.0)
                .Where(valor => !double.IsNaN(valor) && valor > 0)
                .ToList();
        }

        private MetricasPorObjeto ConstruirMetricasParaObjeto(
            string nomeDoObjeto,
            List<(Fixacao fixacao, LinhaDeSessao linhaOrigem)> fixacoesDoObjeto,
            double pupilaMinima,
            double pupilaMaxima)
        {
            double duracaoMedia = fixacoesDoObjeto.Average(par => par.fixacao.DuracaoEmMilissegundos);
            double pupilaMedia = CalcularPupilaMediaDasFixacoes(fixacoesDoObjeto);
            double pontuacao = CalcularPontuacaoPotencialRestaurador(duracaoMedia, pupilaMedia, pupilaMinima, pupilaMaxima);
            var mediasDeSensores = CalcularMediasDeSensores(fixacoesDoObjeto);

            return new MetricasPorObjeto
            {
                NomeDoObjeto                  = nomeDoObjeto,
                DuracaoMediaDasFixacoesMs     = duracaoMedia,
                DiametroPupilaMedioMm         = pupilaMedia,
                PontuacaoPotencialRestaurador = pontuacao,
                MediasDosValoresDeSensores    = mediasDeSensores
            };
        }

        private double CalcularPupilaMediaDasFixacoes(
            List<(Fixacao fixacao, LinhaDeSessao linhaOrigem)> fixacoesDoObjeto)
        {
            var valoresValidos = fixacoesDoObjeto
                .Select(par => (par.fixacao.DiametroMedioPupilaEsquerda + par.fixacao.DiametroMedioPupilaDireita) / 2.0)
                .Where(valor => !double.IsNaN(valor) && valor > 0)
                .ToList();

            if (!valoresValidos.Any())
                return double.NaN;

            return valoresValidos.Average();
        }

        private double CalcularPontuacaoPotencialRestaurador(
            double duracaoMedia,
            double pupilaMedia,
            double pupilaMinima,
            double pupilaMaxima)
        {
            double pontuacaoDeDuracao = NormalizarComClamp(
                duracaoMedia,
                LimiaresDePotencialRestaurador.DuracaoMinimaConfortavelDeFixacaoMs,
                LimiaresDePotencialRestaurador.DuracaoMaximaConfortavelDeFixacaoMs);

            double pontuacaoDePupila = double.IsNaN(pupilaMedia)
                ? 0.0
                : NormalizarComClamp(pupilaMedia, pupilaMinima, pupilaMaxima);

            return LimiaresDePotencialRestaurador.PesoDaDuracaoDeFixacao * pontuacaoDeDuracao
                + LimiaresDePotencialRestaurador.PesoDoDiametroPupilar * pontuacaoDePupila;
        }

        private Dictionary<string, double> CalcularMediasDeSensores(
            List<(Fixacao fixacao, LinhaDeSessao linhaOrigem)> fixacoesDoObjeto)
        {
            var agrupadoPorSensor = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (_, linhaOrigem) in fixacoesDoObjeto)
            {
                if (linhaOrigem == null)
                    continue;

                foreach (var (nomeSensor, valorTexto) in linhaOrigem.CamposSensor)
                {
                    if (!double.TryParse(valorTexto, NumberStyles.Any, CultureInfo.InvariantCulture, out double valorNumerico))
                        continue;

                    if (!agrupadoPorSensor.TryGetValue(nomeSensor, out var lista))
                    {
                        lista = new List<double>();
                        agrupadoPorSensor[nomeSensor] = lista;
                    }

                    lista.Add(valorNumerico);
                }
            }

            return agrupadoPorSensor.ToDictionary(
                par => par.Key,
                par => par.Value.Average());
        }

        private static double NormalizarComClamp(double valor, double minimo, double maximo)
        {
            if (maximo == minimo)
                return 1.0;

            double normalizado = (valor - minimo) / (maximo - minimo);
            return Math.Max(0.0, Math.Min(1.0, normalizado));
        }
    }
}
```


# Processamento/ConstruturDeSessoes.cs

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using PluginCAISM.Dominio;
using PluginCAISM.Parsing;

namespace PluginCAISM.Processamento
{
    public class ConstruturDeSessoes
    {
        private readonly DetectorDeMovimentoOcular _detector;

        public ConstruturDeSessoes(DetectorDeMovimentoOcular detector)
        {
            _detector = detector;
        }

        public ConjuntoDeSessoes Construir(
            List<LinhaDeSessao> linhasBrutas,
            Dictionary<string, double> deltasDeCortisol,
            Dictionary<DateTime, Dictionary<string, string>> indiceDeSensores)
        {
            EnriquecerLinhasComDadosDeSensores(linhasBrutas, indiceDeSensores);

            var gruposDeSessao = AgruparPorChaveDeSessao(linhasBrutas);
            var sessoes = ConstruirSessoesDosGrupos(gruposDeSessao, deltasDeCortisol);

            var conjuntoDeSessoes = new ConjuntoDeSessoes(sessoes);
            conjuntoDeSessoes.CalcularFatoresRestauradores();
            return conjuntoDeSessoes;
        }

        private void EnriquecerLinhasComDadosDeSensores(
            List<LinhaDeSessao> linhasBrutas,
            Dictionary<DateTime, Dictionary<string, string>> indiceDeSensores)
        {
            if (indiceDeSensores == null || indiceDeSensores.Count == 0)
                return;

            foreach (var linha in linhasBrutas)
            {
                if (indiceDeSensores.TryGetValue(linha.Timestamp, out var camposDeSensor))
                    linha.CamposSensor = camposDeSensor;
            }
        }

        private Dictionary<ChaveDeSessao, List<LinhaDeSessao>> AgruparPorChaveDeSessao(
            List<LinhaDeSessao> linhasBrutas)
        {
            var grupos = new Dictionary<ChaveDeSessao, List<LinhaDeSessao>>();

            foreach (var linha in linhasBrutas)
            {
                var chave = new ChaveDeSessao(
                    linha.IdentificacaoManual,
                    linha.NomeDaSessao,
                    linha.CenarioSelecionado,
                    linha.AssentoSelecionado);

                if (!grupos.TryGetValue(chave, out var lista))
                {
                    lista = new List<LinhaDeSessao>();
                    grupos[chave] = lista;
                }

                lista.Add(linha);
            }

            return grupos;
        }

        private List<Sessao> ConstruirSessoesDosGrupos(
            Dictionary<ChaveDeSessao, List<LinhaDeSessao>> gruposDeSessao,
            Dictionary<string, double> deltasDeCortisol)
        {
            var sessoes = new List<Sessao>();

            foreach (var (chave, linhas) in gruposDeSessao)
            {
                var sessao = ConstruirSessao(chave, linhas, deltasDeCortisol);
                sessoes.Add(sessao);
            }

            return sessoes;
        }

        private Sessao ConstruirSessao(
            ChaveDeSessao chave,
            List<LinhaDeSessao> linhas,
            Dictionary<string, double> deltasDeCortisol)
        {
            var sessao = new Sessao(
                identificacaoManual: chave.IdentificacaoManual,
                nomeDaSessao: chave.NomeDaSessao,
                cenarioSelecionado: chave.CenarioSelecionado,
                assentoSelecionado: chave.AssentoSelecionado,
                dadosBrutos: linhas,
                funcaoDetectora: _detector.Detectar);

            sessao.DeltaCortisol = ObterDeltaDeCortisol(chave.NomeDaSessao, deltasDeCortisol);
            return sessao;
        }

        private double ObterDeltaDeCortisol(
            string nomeDaSessao,
            Dictionary<string, double> deltasDeCortisol)
        {
            if (deltasDeCortisol != null && deltasDeCortisol.TryGetValue(nomeDaSessao, out double delta))
                return delta;

            return double.NaN;
        }

        private readonly struct ChaveDeSessao : IEquatable<ChaveDeSessao>
        {
            public string IdentificacaoManual { get; }
            public string NomeDaSessao { get; }
            public string CenarioSelecionado { get; }
            public string AssentoSelecionado { get; }

            public ChaveDeSessao(string identificacaoManual, string nomeDaSessao, string cenarioSelecionado, string assentoSelecionado)
            {
                IdentificacaoManual = identificacaoManual;
                NomeDaSessao = nomeDaSessao;
                CenarioSelecionado = cenarioSelecionado;
                AssentoSelecionado = assentoSelecionado;
            }

            public bool Equals(ChaveDeSessao outro)
            {
                return IdentificacaoManual == outro.IdentificacaoManual
                    && NomeDaSessao == outro.NomeDaSessao
                    && CenarioSelecionado == outro.CenarioSelecionado
                    && AssentoSelecionado == outro.AssentoSelecionado;
            }

            public override bool Equals(object obj) => obj is ChaveDeSessao outro && Equals(outro);

            public override int GetHashCode()
            {
                return HashCode.Combine(IdentificacaoManual, NomeDaSessao, CenarioSelecionado, AssentoSelecionado);
            }
        }
    }
}
```


# Processamento/DetectorDeMovimentoOcular.cs

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Processamento
{
    public class DetectorDeMovimentoOcular
    {
        public (List<Fixacao> fixacoes, List<Saccade> saccades) Detectar(IReadOnlyList<LinhaDeSessao> linhasDaSessao)
        {
            var linhasValidas = FiltrarLinhasValidas(linhasDaSessao);

            if (linhasValidas.Count < 2)
                return (new List<Fixacao>(), new List<Saccade>());

            var grupos = AgruparLinhasEstaveisConsecutivas(linhasValidas);
            return ClassificarGrupos(grupos);
        }

        private List<LinhaDeSessao> FiltrarLinhasValidas(IReadOnlyList<LinhaDeSessao> linhas)
        {
            return linhas.Where(linha => linha.DadosOcularesValidos).ToList();
        }

        private List<List<LinhaDeSessao>> AgruparLinhasEstaveisConsecutivas(List<LinhaDeSessao> linhasValidas)
        {
            var grupos = new List<List<LinhaDeSessao>>();
            var grupoAtual = new List<LinhaDeSessao> { linhasValidas[0] };

            for (int indice = 1; indice < linhasValidas.Count; indice++)
            {
                var linhaAnterior = linhasValidas[indice - 1];
                var linhaAtual = linhasValidas[indice];

                if (OlharEstaEstavel(linhaAnterior, linhaAtual))
                {
                    grupoAtual.Add(linhaAtual);
                }
                else
                {
                    grupos.Add(grupoAtual);
                    grupoAtual = new List<LinhaDeSessao> { linhaAtual };
                }
            }

            grupos.Add(grupoAtual);
            return grupos;
        }

        private bool OlharEstaEstavel(LinhaDeSessao linhaAnterior, LinhaDeSessao linhaAtual)
        {
            double distanciaEsquerda = CalcularDistanciaEuclidiana3D(
                linhaAnterior.PontoDeIncidenciaEsquerdoX, linhaAnterior.PontoDeIncidenciaEsquerdoY, linhaAnterior.PontoDeIncidenciaEsquerdoZ,
                linhaAtual.PontoDeIncidenciaEsquerdoX, linhaAtual.PontoDeIncidenciaEsquerdoY, linhaAtual.PontoDeIncidenciaEsquerdoZ);

            double distanciaDireita = CalcularDistanciaEuclidiana3D(
                linhaAnterior.PontoDeIncidenciaDireitoX, linhaAnterior.PontoDeIncidenciaDireitoY, linhaAnterior.PontoDeIncidenciaDireitoZ,
                linhaAtual.PontoDeIncidenciaDireitoX, linhaAtual.PontoDeIncidenciaDireitoY, linhaAtual.PontoDeIncidenciaDireitoZ);

            return distanciaEsquerda <= ParametrosDeDeteccaoOcular.ToleranciaDeEstabilidadeMetros
                || distanciaDireita <= ParametrosDeDeteccaoOcular.ToleranciaDeEstabilidadeMetros;
        }

        private double CalcularDistanciaEuclidiana3D(
            double x1, double y1, double z1,
            double x2, double y2, double z2)
        {
            double deltaX = x2 - x1;
            double deltaY = y2 - y1;
            double deltaZ = z2 - z1;
            return Math.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);
        }

        private (List<Fixacao>, List<Saccade>) ClassificarGrupos(List<List<LinhaDeSessao>> grupos)
        {
            var fixacoes = new List<Fixacao>();
            var saccades = new List<Saccade>();

            foreach (var grupo in grupos)
            {
                double duracaoMs = CalcularDuracaoEmMs(grupo);

                if (EhFixacao(duracaoMs))
                    fixacoes.Add(ConstruirFixacao(grupo, duracaoMs));
                else if (EhSaccade(duracaoMs))
                    saccades.Add(ConstruirSaccade(grupo, duracaoMs));
            }

            return (fixacoes, saccades);
        }

        private double CalcularDuracaoEmMs(List<LinhaDeSessao> grupo)
        {
            return (grupo.Last().Timestamp - grupo.First().Timestamp).TotalMilliseconds;
        }

        private bool EhFixacao(double duracaoMs)
        {
            return duracaoMs >= ParametrosDeDeteccaoOcular.DuracaoMinimaDeFixacaoMs
                && duracaoMs <= ParametrosDeDeteccaoOcular.DuracaoMaximaDeFixacaoMs;
        }

        private bool EhSaccade(double duracaoMs)
        {
            return duracaoMs >= ParametrosDeDeteccaoOcular.DuracaoMinimaDeSaccadeMs
                && duracaoMs <= ParametrosDeDeteccaoOcular.DuracaoMaximaDeSaccadeMs;
        }

        private Fixacao ConstruirFixacao(List<LinhaDeSessao> grupo, double duracaoMs)
        {
            var dadosBase = ConstruirDadosBaseDoEvento(grupo, duracaoMs);
            var tendencia = DeterminarTendenciaPupilar(grupo);

            return new Fixacao
            {
                TimestampInicio              = dadosBase.timestampInicio,
                TimestampFim                 = dadosBase.timestampFim,
                DuracaoEmMilissegundos       = duracaoMs,
                PontoMedioDeIncidenciaX      = dadosBase.pontoMedioX,
                PontoMedioDeIncidenciaY      = dadosBase.pontoMedioY,
                PontoMedioDeIncidenciaZ      = dadosBase.pontoMedioZ,
                NomesDeObjetos               = dadosBase.nomesDeObjetos,
                IndicesLinhasOriginais       = dadosBase.indices,
                DiametroMedioPupilaEsquerda  = dadosBase.diametroMedioEsquerdo,
                DiametroMedioPupilaDireita   = dadosBase.diametroMedioDireito,
                TendenciaPupilar             = tendencia
            };
        }

        private Saccade ConstruirSaccade(List<LinhaDeSessao> grupo, double duracaoMs)
        {
            var dadosBase = ConstruirDadosBaseDoEvento(grupo, duracaoMs);

            return new Saccade
            {
                TimestampInicio              = dadosBase.timestampInicio,
                TimestampFim                 = dadosBase.timestampFim,
                DuracaoEmMilissegundos       = duracaoMs,
                PontoMedioDeIncidenciaX      = dadosBase.pontoMedioX,
                PontoMedioDeIncidenciaY      = dadosBase.pontoMedioY,
                PontoMedioDeIncidenciaZ      = dadosBase.pontoMedioZ,
                NomesDeObjetos               = dadosBase.nomesDeObjetos,
                IndicesLinhasOriginais       = dadosBase.indices,
                DiametroMedioPupilaEsquerda  = dadosBase.diametroMedioEsquerdo,
                DiametroMedioPupilaDireita   = dadosBase.diametroMedioDireito
            };
        }

        private (DateTime timestampInicio, DateTime timestampFim,
                 double pontoMedioX, double pontoMedioY, double pontoMedioZ,
                 HashSet<string> nomesDeObjetos, List<int> indices,
                 double diametroMedioEsquerdo, double diametroMedioDireito)
            ConstruirDadosBaseDoEvento(List<LinhaDeSessao> grupo, double duracaoMs)
        {
            return (
                timestampInicio:       grupo.First().Timestamp,
                timestampFim:          grupo.Last().Timestamp,
                pontoMedioX:           CalcularPontoMedioX(grupo),
                pontoMedioY:           CalcularPontoMedioY(grupo),
                pontoMedioZ:           CalcularPontoMedioZ(grupo),
                nomesDeObjetos:        ExtrairNomesDeObjetos(grupo),
                indices:               ExtrairIndices(grupo),
                diametroMedioEsquerdo: CalcularDiametroMedioPupila(grupo, linha => linha.DiametroPupilaEsquerda),
                diametroMedioDireito:  CalcularDiametroMedioPupila(grupo, linha => linha.DiametroPupilaDireita)
            );
        }

        private double CalcularPontoMedioX(List<LinhaDeSessao> grupo)
        {
            return grupo.Average(linha => (linha.PontoDeIncidenciaEsquerdoX + linha.PontoDeIncidenciaDireitoX) / 2.0);
        }

        private double CalcularPontoMedioY(List<LinhaDeSessao> grupo)
        {
            return grupo.Average(linha => (linha.PontoDeIncidenciaEsquerdoY + linha.PontoDeIncidenciaDireitoY) / 2.0);
        }

        private double CalcularPontoMedioZ(List<LinhaDeSessao> grupo)
        {
            return grupo.Average(linha => (linha.PontoDeIncidenciaEsquerdoZ + linha.PontoDeIncidenciaDireitoZ) / 2.0);
        }

        private HashSet<string> ExtrairNomesDeObjetos(List<LinhaDeSessao> grupo)
        {
            return new HashSet<string>(
                grupo.Select(linha => linha.NomeDoObjeto).Where(nome => !string.IsNullOrWhiteSpace(nome)));
        }

        private List<int> ExtrairIndices(List<LinhaDeSessao> grupo)
        {
            return grupo.Select(linha => linha.IndiceLinhaOriginal).ToList();
        }

        private double CalcularDiametroMedioPupila(List<LinhaDeSessao> grupo, Func<LinhaDeSessao, double> seletorDeDiametro)
        {
            var valoresValidos = grupo
                .Select(seletorDeDiametro)
                .Where(diametro => diametro > 0)
                .ToList();

            if (!valoresValidos.Any())
                return double.NaN;

            return valoresValidos.Average();
        }

        private TendenciaPupilar DeterminarTendenciaPupilar(List<LinhaDeSessao> grupo)
        {
            int numeroDeBordas = Math.Min(
                ParametrosDeDeteccaoOcular.NumeroDeBordasParaTendenciaPupilar,
                grupo.Count / 2);

            if (numeroDeBordas < 1)
                return TendenciaPupilar.Estavel;

            var linhasDoInicio = ObterLinhasComPupilaValida(grupo.Take(numeroDeBordas));
            var linhasDoFim = ObterLinhasComPupilaValida(grupo.TakeLast(numeroDeBordas));

            if (!linhasDoInicio.Any() || !linhasDoFim.Any())
                return TendenciaPupilar.Estavel;

            double pupilaNaInicio = linhasDoInicio.Average(linha => linha.DiametroPupilaMedio);
            double pupilaNaFim = linhasDoFim.Average(linha => linha.DiametroPupilaMedio);
            double variacao = pupilaNaFim - pupilaNaInicio;

            if (variacao > LimiaresDePotencialRestaurador.LimiarDeDilatacaoPupilarMm)
                return TendenciaPupilar.Dilatada;

            if (variacao < -LimiaresDePotencialRestaurador.LimiarDeDilatacaoPupilarMm)
                return TendenciaPupilar.Contraida;

            return TendenciaPupilar.Estavel;
        }

        private IEnumerable<LinhaDeSessao> ObterLinhasComPupilaValida(IEnumerable<LinhaDeSessao> linhas)
        {
            return linhas.Where(linha => !double.IsNaN(linha.DiametroPupilaMedio));
        }
    }
}
```


# Revit/ConversorDeCoordenadas.cs

```csharp
using Autodesk.Revit.DB;
using PluginCAISM.Configuracao;

namespace PluginCAISM.Revit
{
    public class ConversorDeCoordenadas
    {
        public XYZ ConverterParaRevit(double unityX, double unityY, double unityZ)
        {
            double revitX = unityX * ConversoesDeUnidade.MetrosParaPes;
            double revitY = unityZ * ConversoesDeUnidade.MetrosParaPes;
            double revitZ = unityY * ConversoesDeUnidade.MetrosParaPes;

            return new XYZ(revitX, revitY, revitZ);
        }

        public double ConverterMetrosParaPes(double distanciaEmMetros)
        {
            return distanciaEmMetros * ConversoesDeUnidade.MetrosParaPes;
        }
    }
}
```


# Revit/GeradorDeVistaRevit.cs

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Revit
{
    public class ResultadoDaGeracaoDeVista
    {
        public View3D VistaCriada { get; init; }

        public int NumeroDeSessoesProcessadas { get; init; }

        public int NumeroDeFasesCriadas { get; init; }

        public IReadOnlyList<string> Avisos { get; init; }
    }

    public class GeradorDeVistaRevit
    {
        private readonly Document _documento;
        private readonly UIDocument _uiDocumento;
        private readonly InsersorDaFamiliaHitpoint _insersor;

        public GeradorDeVistaRevit(Document documento, UIDocument uiDocumento)
        {
            _documento = documento;
            _uiDocumento = uiDocumento;
            _insersor = new InsersorDaFamiliaHitpoint(documento, new ConversorDeCoordenadas());
        }

        public ResultadoDaGeracaoDeVista Gerar(IEnumerable<Sessao> sessoes, string caminhoDaPastaDoPlugin)
        {
            var listaDeSessoes = sessoes.ToList();
            var avisos = new List<string>();

            GarantirFamiliaCarregada(caminhoDaPastaDoPlugin);

            int fasesAntes = ContarFasesExistentes();

            var instanciasInseridas = ExecutarTransacaoDeInsercaoDeHitpoints(listaDeSessoes, avisos);

            var vista = ExecutarTransacaoDeGeracaoDeVista(listaDeSessoes, instanciasInseridas);

            if (vista != null)
                _uiDocumento.ActiveView = vista;

            return new ResultadoDaGeracaoDeVista
            {
                VistaCriada                  = vista,
                NumeroDeSessoesProcessadas   = instanciasInseridas.Count,
                Avisos                       = avisos
            };
        }

        private void GarantirFamiliaCarregada(string caminhoDaPastaDoPlugin)
        {
            if (_insersor.VerificarFamiliaCarregada())
                return;

            string caminhoDaFamilia = System.IO.Path.Combine(
                caminhoDaPastaDoPlugin,
                ConfiguracaoDaFamiliaHitpoint.NomeDoArquivoDaFamilia);

            if (!System.IO.File.Exists(caminhoDaFamilia))
                throw new InvalidOperationException(
                    $"A família '{ConfiguracaoDaFamiliaHitpoint.NomeDoArquivoDaFamilia}' não foi encontrada. " +
                    $"Verifique se o arquivo está na pasta do plugin: {caminhoDaPastaDoPlugin}");

            bool carregou = _insersor.TentarCarregarFamilia(caminhoDaFamilia);

            if (!carregou)
                throw new InvalidOperationException(
                    $"Não foi possível carregar a família '{ConfiguracaoDaFamiliaHitpoint.NomeDoArquivoDaFamilia}' " +
                    $"a partir de: {caminhoDaFamilia}");
        }


        private List<FamilyInstance> ExecutarTransacaoDeInsercaoDeHitpoints(
            List<Sessao> sessoes,
            List<string> avisos)
        {
            using var transacao = new Transaction(_documento, "CAISM — Inserir HitPoints");
            transacao.Start();
            var instanciasInseridas = _insersor.InserirHitpoints(sessoes, avisos);
            transacao.Commit();
            return instanciasInseridas;
        }

        private View3D ExecutarTransacaoDeGeracaoDeVista(List<Sessao> sessoes, List<FamilyInstance> instanciasInseridas)
        {
            var tipoDaVista = LocalizarTipoDaVista3D();

            if (tipoDaVista == null)
                return null;

            string nomeDaVista = GerarNomeDaVista(sessoes);

            using var transacao = new Transaction(_documento, "CAISM — Gerar Vista 3D");
            transacao.Start();
            var vista = CriarVista3D(tipoDaVista, nomeDaVista);
            ConfigurarSectionBox(vista, instanciasInseridas);
            transacao.Commit();

            return vista;
        }

        private ViewFamilyType LocalizarTipoDaVista3D()
        {
            return new FilteredElementCollector(_documento)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(tipo => tipo.ViewFamily == ViewFamily.ThreeDimensional);
        }

        private string GerarNomeDaVista(List<Sessao> sessoes)
        {
            string nomeDoCenario = sessoes.FirstOrDefault()?.CenarioSelecionado ?? "Multiplos";
            string timestampAtual = DateTime.Now.ToString("yyyyMMdd_HHmm");
            return $"{ConfiguracaoDaFamiliaHitpoint.PrefixoDeNomeDeVista}_{nomeDoCenario}_{timestampAtual}";
        }

        private View3D CriarVista3D(ViewFamilyType tipoDaVista, string nomeDaVista)
        {
            var vista = View3D.CreateIsometric(_documento, tipoDaVista.Id);
            vista.Name = nomeDaVista;
            return vista;
        }

        private void ConfigurarSectionBox(View3D vista, List<FamilyInstance> instanciasInseridas)
        {
            if (!instanciasInseridas.Any())
                return;

            var boundingBox = CalcularBoundingBoxDasInstancias(instanciasInseridas);
            double margemEmPes = new ConversorDeCoordenadas()
                .ConverterMetrosParaPes(ConversoesDeUnidade.MargemDaSectionBoxMetros);

            var sectionBox = new BoundingBoxXYZ
            {
                Min = new XYZ(boundingBox.Min.X - margemEmPes, boundingBox.Min.Y - margemEmPes, boundingBox.Min.Z - margemEmPes),
                Max = new XYZ(boundingBox.Max.X + margemEmPes, boundingBox.Max.Y + margemEmPes, boundingBox.Max.Z + margemEmPes)
            };

            vista.SetSectionBox(sectionBox);
            vista.IsSectionBoxActive = true;
        }

        private BoundingBoxXYZ CalcularBoundingBoxDasInstancias(List<FamilyInstance> instancias)
        {
            var pontos = instancias
                .Select(instancia => (instancia.Location as LocationPoint)?.Point)
                .Where(ponto => ponto != null)
                .ToList();

            double minX = pontos.Min(p => p.X);
            double minY = pontos.Min(p => p.Y);
            double minZ = pontos.Min(p => p.Z);
            double maxX = pontos.Max(p => p.X);
            double maxY = pontos.Max(p => p.Y);
            double maxZ = pontos.Max(p => p.Z);

            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };
        }

        private int ContarFasesExistentes()
        {
            return new FilteredElementCollector(_documento)
                .OfClass(typeof(Phase))
                .GetElementCount();
        }
    }
}
```


# Revit/GerenciadorDeFases.cs

```csharp
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using PluginCAISM.Dominio;

namespace PluginCAISM.Revit
{
    public class GerenciadorDeFases
    {
        private readonly Document _documento;

        public GerenciadorDeFases(Document documento)
        {
            _documento = documento;
        }


        public Phase LocazalizarFasePorNome(string nomeDaFase)
        {
            return new FilteredElementCollector(_documento)
                .OfClass(typeof(Phase))
                .Cast<Phase>()
                .FirstOrDefault(fase => fase.Name == nomeDaFase);
        }

        private Dictionary<string, Phase> CarregarFasesExistentes()
        {
            return new FilteredElementCollector(_documento)
                .OfClass(typeof(Phase))
                .Cast<Phase>()
                .ToDictionary(fase => fase.Name, fase => fase);
        }


    }
}
```


# Revit/InsersorDaFamiliaHitpoint.cs

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;
using Autodesk.Revit.DB.Structure;

namespace PluginCAISM.Revit
{
    public class InsersorDaFamiliaHitpoint
    {
        private readonly Document _documento;
        private readonly ConversorDeCoordenadas _conversor;

        public InsersorDaFamiliaHitpoint(Document documento, ConversorDeCoordenadas conversor)
        {
            _documento = documento;
            _conversor = conversor;
        }

        public bool VerificarFamiliaCarregada()
        {
            return LocalizarSimboloDaFamilia() != null;
        }

        public bool TentarCarregarFamilia(string caminhoDoArquivoRfa)
        {
            return _documento.LoadFamily(caminhoDoArquivoRfa, out _);
        }

        public List<FamilyInstance> InserirHitpoints(
            IEnumerable<Sessao> sessoes,
            List<string> avisosDeInsercao)
        {
            var simbolo = LocalizarSimboloDaFamilia();

            if (simbolo == null)
                throw new InvalidOperationException(
                    $"A família '{ConfiguracaoDaFamiliaHitpoint.NomeDoArquivoDaFamilia}' não está carregada no projeto.");

            AtivarSimboloSeNecessario(simbolo);

            var instanciasInseridas = new List<FamilyInstance>();

            foreach (var sessao in sessoes)
            {
                var instancia = TentarInserirHitpointDaSessao(sessao, simbolo, avisosDeInsercao);

                if (instancia != null)
                    instanciasInseridas.Add(instancia);
            }

            return instanciasInseridas;
        }

        private FamilyInstance TentarInserirHitpointDaSessao(
            Sessao sessao,
            FamilySymbol simbolo,
            List<string> avisosDeInsercao)
        {
            var ultimaLinha = sessao.ObterUltimaLinhaComPupilaValida();

            if (ultimaLinha == null)
            {
                avisosDeInsercao.Add($"Sessão '{sessao.NomeDeFaseRevit}': sem dado pupilar válido. Hitpoint não inserido.");
                return null;
            }


            XYZ pontoDeInsercao = CalcularPontoDeInsercao(ultimaLinha);
            FamilyInstance instancia = InserirInstancia(simbolo, pontoDeInsercao);
            DefinirFatorRestaurador(instancia, sessao.FatorRestaurador, sessao.NomeDeFaseRevit, avisosDeInsercao);

            return instancia;
        }

        private XYZ CalcularPontoDeInsercao(Dominio.LinhaDeSessao linha)
        {
            double pontoX = (linha.PontoDeIncidenciaEsquerdoX + linha.PontoDeIncidenciaDireitoX) / 2.0;
            double pontoY = (linha.PontoDeIncidenciaEsquerdoY + linha.PontoDeIncidenciaDireitoY) / 2.0;
            double pontoZ = (linha.PontoDeIncidenciaEsquerdoZ + linha.PontoDeIncidenciaDireitoZ) / 2.0;

            return _conversor.ConverterParaRevit(pontoX, pontoY, pontoZ);
        }

        private FamilyInstance InserirInstancia(FamilySymbol simbolo, XYZ pontoDeInsercao)
        {
            var instancia = _documento.Create.NewFamilyInstance(
                pontoDeInsercao,
                simbolo,
                StructuralType.NonStructural);

            return instancia;
        }

        private void DefinirFatorRestaurador(
            FamilyInstance instancia,
            double fatorRestaurador,
            string nomeDaSessao,
            List<string> avisos)
        {
            if (double.IsNaN(fatorRestaurador))
            {
                avisos.Add($"Sessão '{nomeDaSessao}': FatorRestaurador é NaN. Parâmetro não definido.");
                return;
            }

            var parametro = instancia.LookupParameter(ConfiguracaoDaFamiliaHitpoint.NomeDoParametroFatorRestaurador);

            if (parametro == null || parametro.IsReadOnly)
            {
                avisos.Add($"Sessão '{nomeDaSessao}': parâmetro '{ConfiguracaoDaFamiliaHitpoint.NomeDoParametroFatorRestaurador}' não encontrado ou somente leitura.");
                return;
            }

            parametro.Set(fatorRestaurador);
        }

        private void AtivarSimboloSeNecessario(FamilySymbol simbolo)
        {
            if (!simbolo.IsActive)
                simbolo.Activate();
        }

        private FamilySymbol LocalizarSimboloDaFamilia()
        {
            return new FilteredElementCollector(_documento)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(simbolo => simbolo.FamilyName == ObterNomeDaFamiliasSemExtensao());
        }

        private string ObterNomeDaFamiliasSemExtensao()
        {
            return System.IO.Path.GetFileNameWithoutExtension(ConfiguracaoDaFamiliaHitpoint.NomeDoArquivoDaFamilia);
        }
    }
}
```

