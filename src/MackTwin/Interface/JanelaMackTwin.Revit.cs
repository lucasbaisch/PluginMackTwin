using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB.Architecture;
using MackTwin.Configuracao;
using MackTwin.Dominio;
using MackTwin.Exportacao;
using MackTwin.Interface.Modelos;
using MackTwin.Processamento;
using MackTwin.Revit;
using EventosDaUI = Autodesk.Revit.UI.Events;

namespace MackTwin.Interface
{
    /// <summary>
    /// A parte da janela que toca o modelo: a seleção vinda do Revit e as ações da aba
    /// "Modelo" (pintar, limpar, vista 3D, localizar, diagnóstico, vídeo do último dia, folha).
    /// </summary>
    /// <remarks>
    /// Cada ação entra no Revit pelo <see cref="DespachanteDoRevit"/>, que a executa em contexto
    /// válido e na thread da interface — por isso as mensagens podem ir direto para os controles.
    /// O resumo usado ao pintar e exportar é o que a lista já calculou (<see cref="ResumidorAtual"/>),
    /// sem resumir as salas de novo.
    /// </remarks>
    public partial class JanelaMackTwin
    {
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

        // ── Ações no modelo ──────────────────────────────────────────────

        private void BotaoPintar_Click(object sender, RoutedEventArgs evento)
        {
            var conjunto = _conjunto;

            if (conjunto == null)
            {
                Informar("Aguarde a primeira atualização.");
                return;
            }

            if (!conjunto.Ambientes.Any(ambiente => ambiente.CasadoComOModelo))
            {
                Informar("Nenhuma sala das planilhas casou com um Room do modelo. Numere o Room com o código " +
                         "da sala (ex.: LAB001 para o sensor MMCCTLAB001-001), clique em \"Atualizar agora\" e tente de novo.");
                Abas.SelectedIndex = 2;
                return;
            }

            var resumir = ResumidorAtual();

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

                var pintura = PintorDeAmbientes.Pintar(uiDocumento.Document, vista, conjunto, resumir);
                uiDocumento.RefreshActiveView();

                string observacao = pintura.Aviso != null
                    ? " " + pintura.Aviso
                    : $" Esquema de cores \"{EsquemaDeCoresDeConforto.NomeDoEsquema(GrandezaDeConforto.Geral)}\" aplicado à vista; " +
                      $"o estado de cada sala está nos parâmetros \"MackTwin Conforto/Temperatura/Umidade\" do Room.";

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

            var resumir = ResumidorAtual();

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
                    conjunto, resumir, out int pintados, out bool volumesAtivados, out string? avisoDaPintura);
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

        // ── Linha do tempo ───────────────────────────────────────────────

        private DispatcherTimer? _temporizadorDaLinhaDoTempo;

        /// <summary>Posição do deslizador que significa "agora" (as 24 anteriores são horas cheias passadas).</summary>
        private const int PosicaoDoAgora = ConfiguracaoDeExportacao.QuantidadeDeQuadros;

        /// <summary>
        /// O usuário arrastou o deslizador: atualiza o rótulo na hora e agenda a repintura do
        /// modelo para daqui a pouco — repintar a cada pixel de arrasto afogaria o Revit em
        /// eventos externos; com o atraso, só a posição em que a mão parou é aplicada.
        /// </summary>
        private void DeslizadorDoTempo_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> evento)
        {
            if (TextoDoInstante == null)
                return; // disparo durante a construção do XAML

            int posicao = PosicaoSelecionada();
            bool passado = posicao < PosicaoDoAgora;

            TextoDoInstante.Text = passado
                ? LinhaDoTempoDoVideo.Instantes(DateTime.Now)[posicao].ToString("dd/MM HH:mm", CultureInfo.CurrentCulture)
                : "Agora";
            TextoDoInstante.Foreground = Recursos.Paleta.Pincel(passado ? Recursos.Paleta.Marca : Recursos.Paleta.Texto);

            _temporizadorDaLinhaDoTempo ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _temporizadorDaLinhaDoTempo.Tick -= LinhaDoTempo_Tick;
            _temporizadorDaLinhaDoTempo.Tick += LinhaDoTempo_Tick;
            _temporizadorDaLinhaDoTempo.Stop();
            _temporizadorDaLinhaDoTempo.Start();
        }

        private void LinhaDoTempo_Tick(object? sender, EventArgs evento)
        {
            _temporizadorDaLinhaDoTempo?.Stop();
            AplicarInstanteDaLinhaDoTempo();
        }

        private void BotaoVoltarAoAgora_Click(object sender, RoutedEventArgs evento)
        {
            DeslizadorDoTempo.Value = PosicaoDoAgora;
        }

        private int PosicaoSelecionada()
        {
            return Math.Clamp((int)Math.Round(DeslizadorDoTempo.Value), 0, PosicaoDoAgora);
        }

        /// <summary>
        /// Repinta a vista ativa com o estado que as salas tinham na hora escolhida — ou com o
        /// estado atual, na última posição. Só o modelo muda; lista e cartões continuam ao vivo.
        /// </summary>
        private void AplicarInstanteDaLinhaDoTempo()
        {
            var conjunto = _conjunto;

            if (conjunto == null)
            {
                Informar("Aguarde a primeira atualização para usar a linha do tempo.");
                return;
            }

            int posicao = PosicaoSelecionada();
            Func<Ambiente, ResumoDoAmbiente> resumir;
            string descricao;

            if (posicao >= PosicaoDoAgora)
            {
                resumir = ResumidorAtual();
                descricao = "agora (ao vivo)";
            }
            else
            {
                var instante = LinhaDoTempoDoVideo.Instantes(DateTime.Now)[posicao];
                var resumos = conjunto.Ambientes.ToDictionary(
                    ambiente => ambiente.Codigo,
                    ambiente => CalculadorDeResumoDoAmbiente.ResumirEm(ambiente, instante, LinhaDoTempoDoVideo.Tolerancia),
                    StringComparer.Ordinal);

                resumir = ambiente => resumos.TryGetValue(ambiente.Codigo, out var resumo)
                    ? resumo
                    : CalculadorDeResumoDoAmbiente.ResumirEm(ambiente, instante, LinhaDoTempoDoVideo.Tolerancia);
                descricao = instante.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
            }

            _despachante.Executar(app =>
            {
                var uiDocumento = app.ActiveUIDocument;

                if (uiDocumento == null)
                    return;

                var vista = uiDocumento.ActiveView;

                if (!PintorDeAmbientes.VistaAceitaPintura(vista))
                {
                    TextoStatus.Text = $"A vista ativa (\"{vista.Name}\") não aceita pintura; abra uma planta ou vista 3D para usar a linha do tempo.";
                    return;
                }

                var pintura = PintorDeAmbientes.Pintar(uiDocumento.Document, vista, conjunto, resumir);
                uiDocumento.RefreshActiveView();

                TextoStatus.Text = pintura.Aviso ?? $"Modelo mostrando o estado de {descricao} em \"{vista.Name}\".";
            });
        }

        // ── Exportação ───────────────────────────────────────────────────

        private void BotaoExportarVideo_Click(object sender, RoutedEventArgs evento)
        {
            var conjunto = _conjunto;

            if (conjunto == null)
            {
                Informar("Aguarde a primeira atualização.");
                return;
            }

            if (!conjunto.Ambientes.Any(ambiente => ambiente.CasadoComOModelo))
            {
                Informar("Nenhuma sala das planilhas casou com um Room do modelo; o vídeo sairia todo cinza. Case as salas antes.");
                return;
            }

            var resumir = ResumidorAtual();
            var agora = DateTime.Now;

            _despachante.Executar(app =>
            {
                var uiDocumento = app.ActiveUIDocument;

                if (uiDocumento == null)
                {
                    Informar("Abra um modelo para exportar o vídeo.");
                    return;
                }

                ExibirOcupado(true);

                try
                {
                    var exportador = new ExportadorDeVideoDoDia(uiDocumento);
                    var resultado = exportador.Exportar(conjunto, resumir, agora, MostrarProgresso);

                    uiDocumento.RefreshActiveView();

                    string observacao = resultado.Aviso == null ? string.Empty : " " + resultado.Aviso;

                    Informar($"Vídeo do último dia exportado: {resultado.Quadros} quadros de {resultado.Inicio:dd/MM HH:mm} a " +
                             $"{resultado.Fim:dd/MM HH:mm} (passo de 1 h) em \"{resultado.Pasta}\" — GIF animado e quadros PNG. " +
                             $"Câmera: vista \"{resultado.Vista.Name}\".{observacao}");

                    AbrirPasta(resultado.Pasta);
                }
                finally
                {
                    ExibirOcupado(false);
                }
            });
        }

        private void BotaoGerarFolha_Click(object sender, RoutedEventArgs evento)
        {
            var conjunto = _conjunto;

            if (conjunto == null)
            {
                Informar("Aguarde a primeira atualização.");
                return;
            }

            var resumir = ResumidorAtual();
            var agora = DateTime.Now;

            _despachante.Executar(app =>
            {
                var uiDocumento = app.ActiveUIDocument;

                if (uiDocumento == null)
                {
                    Informar("Abra um modelo para gerar a folha.");
                    return;
                }

                ExibirOcupado(true);

                try
                {
                    MostrarProgresso("Montando as plantas de temperatura e umidade e a folha…");

                    var gerador = new GeradorDeFolhaDeImpressao(uiDocumento.Document);
                    var resultado = gerador.Gerar(conjunto, resumir, agora);

                    if (resultado.Folhas.Count == 0)
                    {
                        Informar(resultado.Aviso ?? "Nenhuma folha foi gerada.");
                        return;
                    }

                    uiDocumento.ActiveView = resultado.Folhas[0];
                    uiDocumento.RefreshActiveView();

                    string numeros = string.Join(", ", resultado.Folhas.Select(folha => folha.SheetNumber));
                    string observacao = resultado.Aviso == null ? string.Empty : " " + resultado.Aviso;

                    Informar($"{resultado.Folhas.Count} folha(s) gerada(s) ({numeros}) com {resultado.Plantas} planta(s): temperatura à esquerda, " +
                             $"umidade à direita, cada uma com a sua legenda. Imprima por Arquivo › Imprimir ou exporte em PDF.{observacao}");
                }
                finally
                {
                    ExibirOcupado(false);
                }
            });
        }

        /// <summary>
        /// Atualiza o status e força o WPF a desenhar: a exportação roda na thread da interface,
        /// dentro do evento externo, e sem isso a janela congelaria sem mostrar o andamento.
        /// </summary>
        private void MostrarProgresso(string mensagem)
        {
            TextoStatus.Text = mensagem;
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        }

        private void AbrirPasta(string pasta)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{pasta}\"")
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception excecao)
            {
                AcrescentarAviso("Não foi possível abrir a pasta: " + excecao.Message);
            }
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
    }
}
