using System;
using System.IO;
using System.Linq;
using MackTwin.Configuracao;
using UI = Autodesk.Revit.UI;
using RevitExcecoes = Autodesk.Revit.Exceptions;
using Midia = System.Windows.Media;
using Imagens = System.Windows.Media.Imaging;

namespace MackTwin
{
    /// <summary>
    /// Monta a aba própria do plugin na ribbon do Revit, com um botão que dispara
    /// <see cref="ComandoPrincipalMackTwin"/>.
    /// </summary>
    /// <remarks>
    /// Registrado no <c>MackTwin.addin</c> como <c>Type="Application"</c>. É o único
    /// tipo de manifesto que cria interface: um <c>Type="Command"</c> sozinho aparece
    /// apenas em Add-Ins &gt; External Tools.
    ///
    /// <b>Não participa do fluxo do AddinManager.</b> O AddinManager executa comandos
    /// diretamente, sem passar por <see cref="OnStartup"/>, então a ribbon só existe em
    /// instalações feitas pelo MSI (ou com o .addin copiado à mão).
    ///
    /// As aliases de namespace acompanham a convenção do projeto: este arquivo mistura a
    /// API do Revit com WPF, e os tipos de imagem só não colidem porque estão qualificados.
    /// </remarks>
    public class AplicacaoRibbonMackTwin : UI.IExternalApplication
    {
        /// <summary>
        /// Chamado pelo Revit uma vez, na inicialização, antes de qualquer documento abrir.
        /// </summary>
        /// <remarks>
        /// Devolver <see cref="UI.Result.Failed"/> aqui faz o Revit exibir um aviso e
        /// desabilitar o addin na sessão. Por isso qualquer falha vira diálogo explicativo:
        /// sem isso o sintoma visível seria apenas a aba não aparecer.
        /// </remarks>
        public UI.Result OnStartup(UI.UIControlledApplication aplicacao)
        {
            try
            {
                var painel = ObterOuCriarPainel(aplicacao);
                AcrescentarBotaoPrincipal(painel);
                AcrescentarBotaoDeVerificacao(painel);
                return UI.Result.Succeeded;
            }
            catch (Exception excecao)
            {
                UI.TaskDialog.Show(
                    "Plug-in MackTwin",
                    "Não foi possível criar a aba do plugin na ribbon.\n\n" + excecao.Message);
                return UI.Result.Failed;
            }
        }

        /// <summary>Nada a desfazer: a ribbon é destruída junto com a sessão do Revit.</summary>
        public UI.Result OnShutdown(UI.UIControlledApplication aplicacao)
        {
            return UI.Result.Succeeded;
        }

        // ── Ribbon ───────────────────────────────────────────────────────

        /// <summary>
        /// Devolve o painel do plugin, criando a aba e o painel se ainda não existirem.
        /// </summary>
        /// <remarks>
        /// Tanto <c>CreateRibbonTab</c> quanto <c>CreateRibbonPanel</c> lançam quando o nome
        /// já está em uso, e isso acontece de verdade: se o mesmo .addin for carregado duas
        /// vezes (uma cópia esquecida na pasta de addins de outro usuário, por exemplo), a
        /// segunda passagem derrubaria a montagem inteira. Reaproveitar o que já existe
        /// torna o método idempotente.
        /// </remarks>
        private static UI.RibbonPanel ObterOuCriarPainel(UI.UIControlledApplication aplicacao)
        {
            try
            {
                aplicacao.CreateRibbonTab(ConfiguracaoDaRibbon.NomeDaAba);
            }
            catch (RevitExcecoes.ArgumentException)
            {
                // A aba já existe — segue para reaproveitá-la.
            }

            var painelExistente = aplicacao
                .GetRibbonPanels(ConfiguracaoDaRibbon.NomeDaAba)
                .FirstOrDefault(p => p.Name == ConfiguracaoDaRibbon.NomeDoPainel);

            return painelExistente
                ?? aplicacao.CreateRibbonPanel(
                       ConfiguracaoDaRibbon.NomeDaAba,
                       ConfiguracaoDaRibbon.NomeDoPainel);
        }

        /// <summary>
        /// Acrescenta ao painel o botão que abre a janela principal do plugin.
        /// </summary>
        /// <remarks>
        /// O Revit resolve a classe do comando por reflexão, a partir do caminho do assembly
        /// e do nome completo do tipo. Ambos saem de <c>typeof</c> e não de literais:
        /// renomear a classe ou o namespace passa a quebrar a compilação, em vez de produzir
        /// um botão que só falha quando alguém clica nele.
        /// </remarks>
        private static void AcrescentarBotaoPrincipal(UI.RibbonPanel painel)
        {
            string caminhoDoAssembly = typeof(ComandoPrincipalMackTwin).Assembly.Location;
            string pastaDoPlugin = Path.GetDirectoryName(caminhoDoAssembly) ?? string.Empty;

            var dadosDoBotao = new UI.PushButtonData(
                ConfiguracaoDaRibbon.NomeInternoDoBotao,
                ConfiguracaoDaRibbon.TextoDoBotao,
                caminhoDoAssembly,
                typeof(ComandoPrincipalMackTwin).FullName)
            {
                ToolTip = ConfiguracaoDaRibbon.DicaDoBotao,
                LongDescription = ConfiguracaoDaRibbon.DescricaoLongaDoBotao,
            };

            // O ícone é acessório: um botão sem imagem continua funcionando, e derrubar a
            // aba inteira porque o PNG sumiu seria desproporcional.
            try
            {
                dadosDoBotao.LargeImage = CarregarIcone(
                    pastaDoPlugin, ConfiguracaoDaRibbon.LarguraDoIconeGrande);
                dadosDoBotao.Image = CarregarIcone(
                    pastaDoPlugin, ConfiguracaoDaRibbon.LarguraDoIconePequeno);
            }
            catch (Exception)
            {
                // Botão sem ícone.
            }

            painel.AddItem(dadosDoBotao);
        }

        /// <summary>
        /// Acrescenta o botão que roda as verificações automáticas do modelo.
        /// </summary>
        /// <remarks>
        /// Fica ao lado do botão principal, e não escondido: uma verificação que ninguém encontra
        /// não é executada. Sem ícone próprio de propósito — o Revit reserva menos espaço para o
        /// botão, o que já o distingue do principal na leitura da faixa.
        /// </remarks>
        private static void AcrescentarBotaoDeVerificacao(UI.RibbonPanel painel)
        {
            var dadosDoBotao = new UI.PushButtonData(
                ConfiguracaoDaRibbon.NomeInternoDoBotaoDeVerificacao,
                ConfiguracaoDaRibbon.TextoDoBotaoDeVerificacao,
                typeof(ComandoDeVerificacaoMackTwin).Assembly.Location,
                typeof(ComandoDeVerificacaoMackTwin).FullName)
            {
                ToolTip = ConfiguracaoDaRibbon.DicaDoBotaoDeVerificacao,
                LongDescription = ConfiguracaoDaRibbon.DescricaoLongaDoBotaoDeVerificacao,
            };

            painel.AddItem(dadosDoBotao);
        }

        // ── Ícone ────────────────────────────────────────────────────────

        /// <summary>
        /// Carrega o ícone do botão, já decodificado na largura final.
        /// </summary>
        /// <remarks>
        /// Mesma cascata do hitpoint.rfa, e pelo mesmo motivo:
        /// 1. arquivo em <c>Assets\icone-macktwin.png</c> ao lado do DLL — o caso da instalação
        ///    pelo MSI;
        /// 2. recurso embutido no assembly — o caso em que a pasta <c>Assets\</c> não
        ///    acompanha o DLL.
        /// </remarks>
        private static Midia.ImageSource CarregarIcone(string pastaDoPlugin, int larguraEmPixels)
        {
            string caminhoEmDisco = Path.Combine(
                pastaDoPlugin ?? string.Empty,
                ConfiguracaoDaRibbon.CaminhoRelativoDoIcone);

            if (File.Exists(caminhoEmDisco))
            {
                using var arquivo = File.OpenRead(caminhoEmDisco);
                return DecodificarPng(arquivo, larguraEmPixels);
            }

            using var recurso = typeof(AplicacaoRibbonMackTwin).Assembly
                .GetManifestResourceStream(ConfiguracaoDaRibbon.NomeDoRecursoDoIcone);

            if (recurso == null)
                throw new InvalidOperationException(
                    "Recurso embutido '" + ConfiguracaoDaRibbon.NomeDoRecursoDoIcone +
                    "' não encontrado no assembly. Verifique se '" +
                    ConfiguracaoDaRibbon.NomeDoArquivoDoIcone +
                    "' está marcado como EmbeddedResource no .csproj.");

            return DecodificarPng(recurso, larguraEmPixels);
        }

        /// <summary>
        /// Decodifica um PNG a partir de um stream.
        /// </summary>
        /// <remarks>
        /// <c>CacheOption = OnLoad</c> precisa ser definido ANTES de <c>StreamSource</c>: é o
        /// que faz o WPF ler o stream inteiro durante <c>EndInit</c>, em vez de guardar uma
        /// referência preguiçosa a um stream que este método fecha ao retornar.
        ///
        /// <c>Freeze</c> desliga a afinidade de thread do objeto. A ribbon é montada em
        /// <c>OnStartup</c>, que não roda necessariamente na mesma thread de UI que depois
        /// desenha o botão.
        /// </remarks>
        private static Midia.ImageSource DecodificarPng(Stream origem, int larguraEmPixels)
        {
            var imagem = new Imagens.BitmapImage();

            imagem.BeginInit();
            imagem.CacheOption = Imagens.BitmapCacheOption.OnLoad;
            imagem.StreamSource = origem;
            imagem.DecodePixelWidth = larguraEmPixels;
            imagem.EndInit();
            imagem.Freeze();

            return imagem;
        }
    }
}
