using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using MackTwin.Interface;
using MackTwin.Revit;
using DB = Autodesk.Revit.DB;
using UI = Autodesk.Revit.UI;

namespace MackTwin
{
    /// <summary>
    /// Ponto de entrada do plug-in: abre (ou traz à frente) o painel de sensores.
    /// </summary>
    /// <remarks>
    /// A janela é <b>modeless</b> (<c>Show</c>, não <c>ShowDialog</c>): ela fica aberta enquanto
    /// o usuário navega e clica nas salas do modelo, e é isso que permite "clicar num Room e ver
    /// os sensores dele". Como consequência, o comando retorna imediatamente e a janela passa a
    /// viver fora do contexto da API — tudo o que ela precisar fazer no Revit vai pelo
    /// <see cref="DespachanteDoRevit"/>, criado aqui, dentro do contexto válido.
    ///
    /// Uma instância só: clicar no botão com o painel já aberto apenas o ativa. O despachante
    /// também é único e sobrevive à janela, porque <c>ExternalEvent.Create</c> só pode ser
    /// chamado em contexto válido e não há motivo para recriá-lo.
    /// </remarks>
    [Transaction(TransactionMode.Manual)]
    public class ComandoPrincipalMackTwin : UI.IExternalCommand
    {
        private static JanelaMackTwin? _janela;
        private static DespachanteDoRevit? _despachante;

        /// <summary>Abre o painel, ou o ativa se já estiver aberto.</summary>
        public UI.Result Execute(
            UI.ExternalCommandData dadosDoComando,
            ref string mensagemDeErro,
            DB.ElementSet elementosComErro)
        {
            var uiApp = dadosDoComando.Application;
            var uiDocumento = uiApp.ActiveUIDocument;

            if (uiDocumento == null)
            {
                UI.TaskDialog.Show("MackTwin", "Abra um modelo antes de abrir o painel de sensores.");
                return UI.Result.Cancelled;
            }

            if (uiDocumento.Document.IsFamilyDocument)
            {
                UI.TaskDialog.Show("MackTwin", "O painel não pode ser usado no Editor de Famílias.");
                return UI.Result.Cancelled;
            }

            try
            {
                if (_janela != null && _janela.IsVisible)
                {
                    _janela.Activate();
                    return UI.Result.Succeeded;
                }

                _despachante ??= new DespachanteDoRevit();

                _janela = new JanelaMackTwin(uiApp, _despachante);

                // Dona = janela principal do Revit: o painel minimiza e volta junto com ele e
                // nunca fica atrás dele.
                _ = new WindowInteropHelper(_janela) { Owner = uiApp.MainWindowHandle };

                _janela.Closed += (remetente, evento) => _janela = null;
                _janela.Show();

                return UI.Result.Succeeded;
            }
            catch (Exception excecao)
            {
                mensagemDeErro = excecao.Message;
                return UI.Result.Failed;
            }
        }
    }
}
