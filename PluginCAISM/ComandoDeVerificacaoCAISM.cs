using System;
using System.IO;
using Autodesk.Revit.Attributes;
using UI = Autodesk.Revit.UI;
using PluginCAISM.Verificacao;

namespace PluginCAISM
{
    /// <summary>
    /// Executa as verificações automáticas das calculadoras do modelo e mostra o resultado.
    /// </summary>
    /// <remarks>
    /// Não toca no documento nem na vista: só roda cálculo puro e exibe um relatório. Por isso a
    /// transação é somente leitura.
    ///
    /// A razão de ser um comando do Revit, e não um projeto de teste, está em
    /// <see cref="VerificacoesDoModelo"/>: a pesquisa não admite pacote NuGet novo e o Smart App
    /// Control do Windows bloqueia binário recém-compilado. Dentro do Revit nenhuma das duas
    /// restrições se aplica.
    /// </remarks>
    [Transaction(TransactionMode.ReadOnly)]
    public class ComandoDeVerificacaoCAISM : UI.IExternalCommand
    {
        /// <summary>Executa a suíte e apresenta o relatório.</summary>
        public UI.Result Execute(
            UI.ExternalCommandData dadosDoComando,
            ref string mensagemDeErro,
            Autodesk.Revit.DB.ElementSet elementosComErro)
        {
            try
            {
                var relatorio = VerificacoesDoModelo.ExecutarTudo();
                string texto = relatorio.ComoTexto();
                string caminho = GravarCopia(texto);

                var dialogo = new UI.TaskDialog("Verificação do modelo")
                {
                    MainInstruction = relatorio.TudoCerto
                        ? $"As {relatorio.Passaram} afirmações se sustentaram."
                        : $"{relatorio.Falharam} de {relatorio.Resultados.Count} afirmações FALHARAM.",

                    MainContent = relatorio.TudoCerto
                        ? "As calculadoras do Modelo de Potencial Homeodinâmico se comportam como " +
                          "especificado: convenção de sinal, limiar de 1,44, ajuste por mínimos " +
                          "quadrados e recuperação do efeito de elemento."
                        : "Os índices exibidos pelo plug-in não são confiáveis enquanto isso não " +
                          "for resolvido. Veja o detalhe abaixo.",

                    ExpandedContent = texto,
                    MainIcon = relatorio.TudoCerto
                        ? UI.TaskDialogIcon.TaskDialogIconInformation
                        : UI.TaskDialogIcon.TaskDialogIconError,
                };

                if (caminho != null)
                    dialogo.FooterText = "Cópia do relatório: " + caminho;

                dialogo.Show();

                return UI.Result.Succeeded;
            }
            catch (Exception excecao)
            {
                mensagemDeErro = "Falha ao executar as verificações: " + excecao.Message;
                return UI.Result.Failed;
            }
        }

        /// <summary>
        /// Grava o relatório em arquivo, para poder ser anexado a um registro de execução.
        /// </summary>
        /// <returns>O caminho gravado, ou null se não foi possível gravar.</returns>
        /// <remarks>
        /// A falha aqui é deliberadamente silenciosa: o relatório já está na tela, e não faria
        /// sentido transformar a verificação num erro por causa da cópia em disco.
        /// </remarks>
        private static string GravarCopia(string texto)
        {
            try
            {
                string caminho = Path.Combine(
                    Path.GetTempPath(),
                    $"CAISM_Verificacao_{DateTime.Now:yyyy-MM-dd_HHmm}.txt");

                File.WriteAllText(caminho, texto);
                return caminho;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
