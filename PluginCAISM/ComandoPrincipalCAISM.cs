using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using DB = Autodesk.Revit.DB;
using UI = Autodesk.Revit.UI;
using PluginCAISM.Interface;

namespace PluginCAISM
{
    /// <summary>
    /// Ponto de entrada do plugin CAISM no Revit. Implementa <see cref="UI.IExternalCommand"/>
    /// e é ativado a partir do painel de ferramentas ou de um botão na ribbon do Revit.
    /// </summary>
    /// <remarks>
    /// Antes de abrir a janela principal, este comando verifica:
    /// - O documento ativo não é um documento de família.
    /// - A vista ativa não é do tipo Schedule, Legend ou DrawingSheet.
    ///
    /// Se as condições forem satisfeitas, instancia e exibe <see cref="JanelaPluginCAISM"/>
    /// via ShowDialog(), bloqueando o Revit até o fechamento da janela.
    /// </remarks>
    [Transaction(TransactionMode.Manual)]
    public class ComandoPrincipalCAISM : UI.IExternalCommand
    {
        private static readonly DB.ViewType[] TiposDeVistaInvalidos =
        {
            DB.ViewType.Schedule,
            DB.ViewType.Legend,
            DB.ViewType.DrawingSheet
        };

        /// <summary>
        /// Executa o comando principal do plugin CAISM.
        /// </summary>
        /// <param name="dadosDoComando">Dados do contexto de execução fornecidos pelo Revit.</param>
        /// <param name="mensagemDeErro">Mensagem de erro preenchida em caso de falha.</param>
        /// <param name="elementosComErro">Conjunto de elementos com erros (não utilizado).</param>
        /// <returns>
        /// <see cref="UI.Result.Succeeded"/> se a janela for aberta com sucesso;
        /// <see cref="UI.Result.Cancelled"/> se uma pré-condição não for satisfeita;
        /// <see cref="UI.Result.Failed"/> em caso de exceção não tratada.
        /// </returns>
        public UI.Result Execute(UI.ExternalCommandData dadosDoComando, ref string mensagemDeErro, DB.ElementSet elementosComErro)
        {
            var uiDocumento = dadosDoComando.Application.ActiveUIDocument;
            var documento = uiDocumento.Document;
            var vistaAtiva = uiDocumento.ActiveView;

            if (documento.IsFamilyDocument)
            {
                UI.TaskDialog.Show("Documento Inválido", "O plugin não pode ser usado no Editor de Famílias.");
                return UI.Result.Cancelled;
            }

            if (TipoDeVistaEhInvalido(vistaAtiva.ViewType))
            {
                UI.TaskDialog.Show("Vista Inválida", "Mude para uma planta ou vista 3D antes de usar o plugin CAISM.");
                return UI.Result.Cancelled;
            }

            try
            {
                string caminhoDoPlugin = ResolverCaminhoDoPlugin(dadosDoComando.Application);
                var janela = new JanelaPluginCAISM(documento, uiDocumento, caminhoDoPlugin);
                janela.ShowDialog();
                return UI.Result.Succeeded;
            }
            catch (Exception excecao)
            {
                mensagemDeErro = excecao.Message;
                return UI.Result.Failed;
            }
        }

        private bool TipoDeVistaEhInvalido(DB.ViewType tipoDeVista)
        {
            return Array.IndexOf(TiposDeVistaInvalidos, tipoDeVista) >= 0;
        }

        /// <summary>
        /// Resolve o caminho da pasta de instalação do plugin.
        /// </summary>
        /// <remarks>
        /// O Revit (a partir de certas versões) copia o DLL do addin para uma pasta temporária
        /// (<c>%TEMP%\RevitAddins\{nome}-Parsing-{timestamp}\</c>) antes de analisá-lo.
        /// Nesse caso, <c>Assembly.Location</c> aponta para a cópia temporária que não contém
        /// a subpasta <c>Assets\</c>. A estratégia em cascata é:
        ///
        /// 1. <c>typeof(ComandoPrincipalCAISM).Assembly.Location</c> — válido quando não é pasta temporária.
        /// 2. <c>ManifestModule.FullyQualifiedName</c> — retorna caminho físico mesmo em assemblies
        ///    carregados via stream, se não for pasta temporária.
        /// 3. Busca por <c>PluginCAISM.dll</c> nas pastas de addins do Revit
        ///    (<c>CurrentUserAddinsLocation</c> e <c>AllUsersAddinsLocation</c>).
        /// 4. <c>AppDomain.CurrentDomain.BaseDirectory</c> como último recurso.
        /// </remarks>
        private static string ResolverCaminhoDoPlugin(UI.UIApplication uiApp)
        {
            // Tentativa 1: Location do assembly desta classe, descartando pastas temporárias.
            string location = typeof(ComandoPrincipalCAISM).Assembly.Location;
            if (!string.IsNullOrEmpty(location) && !EhPastaTemporariaRevit(location))
                return Path.GetDirectoryName(location);

            // Tentativa 2: FullyQualifiedName do módulo (caminho físico mesmo quando Location é vazio).
            string modulePath = typeof(ComandoPrincipalCAISM).Assembly.ManifestModule.FullyQualifiedName;
            if (!string.IsNullOrEmpty(modulePath) && File.Exists(modulePath) && !EhPastaTemporariaRevit(modulePath))
                return Path.GetDirectoryName(modulePath);

            // Tentativa 3: pesquisar PluginCAISM.dll nas pastas de addins registradas pelo Revit.
            var pastasCandidatas = new[]
            {
                uiApp?.Application?.CurrentUserAddinsLocation,
                uiApp?.Application?.AllUsersAddinsLocation,
            };

            foreach (var pasta in pastasCandidatas)
            {
                if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta))
                    continue;

                string dll = Directory
                    .EnumerateFiles(pasta, "PluginCAISM.dll", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (dll != null)
                    return Path.GetDirectoryName(dll);
            }

            // Último recurso.
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>
        /// Retorna <see langword="true"/> se o caminho estiver dentro da pasta temporária
        /// usada pelo Revit para cópia de addins durante a análise.
        /// Exemplo de padrão: <c>%TEMP%\RevitAddins\PluginCAISM-Parsing-…\</c>
        /// </summary>
        private static bool EhPastaTemporariaRevit(string caminho)
        {
            string temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return caminho.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
        }
    }
}
