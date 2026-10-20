using System;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB.Architecture;
using MackTwin.Interface.Modelos;
using MackTwin.Revit;
using EventosDaUI = Autodesk.Revit.UI.Events;

namespace MackTwin.Interface
{
    /// <summary>
    /// A parte da janela que toca o modelo: a seleção vinda do Revit e as ações da aba
    /// "No modelo" (pintar, limpar, vista 3D, localizar, diagnóstico).
    /// </summary>
    /// <remarks>
    /// Cada ação entra no Revit pelo <see cref="DespachanteDoRevit"/>, que a executa em contexto
    /// válido e na thread da interface — por isso as mensagens podem ir direto para os controles.
    /// A classificação usada ao pintar é a que a lista já calculou (<see cref="ClassificadorAtual"/>),
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

            var classificar = ClassificadorAtual();

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

                var pintura = PintorDeAmbientes.Pintar(uiDocumento.Document, vista, conjunto, classificar);
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

            var classificar = ClassificadorAtual();

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
                    conjunto, classificar, out int pintados, out bool volumesAtivados, out string? avisoDaPintura);
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
    }
}
