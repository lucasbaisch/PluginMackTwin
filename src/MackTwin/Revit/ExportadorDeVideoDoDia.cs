using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MackTwin.Configuracao;
using MackTwin.Dominio;
using MackTwin.Exportacao;
using MackTwin.Processamento;

namespace MackTwin.Revit
{
    /// <summary>O que a exportação do vídeo produziu.</summary>
    public sealed class ResultadoDaExportacaoDeVideo
    {
        /// <summary>Pasta datada com o GIF e os quadros PNG.</summary>
        public string Pasta { get; init; } = string.Empty;

        /// <summary>Caminho do GIF animado.</summary>
        public string CaminhoDoGif { get; init; } = string.Empty;

        /// <summary>Quantos quadros foram gerados.</summary>
        public int Quadros { get; init; }

        /// <summary>A vista 3D que serviu de câmera.</summary>
        public View3D Vista { get; init; } = null!;

        /// <summary>Primeiro e último instante do vídeo.</summary>
        public DateTime Inicio { get; init; }

        /// <summary>Último instante do vídeo.</summary>
        public DateTime Fim { get; init; }

        /// <summary>Observações sobre a vista (volumes ligados, esquema), ou null.</summary>
        public string? Aviso { get; init; }
    }

    /// <summary>
    /// Exporta o "estudo de conforto" do último dia: a vista 3D de conforto fotografada a cada
    /// hora das últimas 24 h, com os Rooms na cor do estado que tinham naquele instante —
    /// um GIF animado em laço e os quadros PNG, cada um carimbado com data, hora e legenda.
    /// </summary>
    /// <remarks>
    /// É o equivalente do "Exportar › Estudo solar" nativo, trocando a posição do sol pelo
    /// estado dos sensores. O mecanismo é o mesmo da pintura: a cada quadro os parâmetros dos
    /// Rooms recebem o estado daquele instante (<see cref="CalculadorDeResumoDoAmbiente.ResumirEm"/>),
    /// a transação é confirmada, a vista é exportada como PNG pela API e a imagem é carimbada.
    /// Ao fim, os Rooms voltam ao estado ATUAL, para que a vista 3D não fique mostrando o
    /// passado.
    ///
    /// A exportação de imagem precisa de uma vista ativa e de nenhuma transação aberta; por
    /// isso a vista gerada é ativada antes e cada quadro é confirmado antes de fotografar.
    /// Deve rodar dentro de um contexto válido da API, na thread da interface.
    /// </remarks>
    public sealed class ExportadorDeVideoDoDia
    {
        private readonly UIDocument _uiDocumento;
        private readonly Document _documento;

        /// <summary>Cria o exportador sobre o documento ativo.</summary>
        public ExportadorDeVideoDoDia(UIDocument uiDocumento)
        {
            _uiDocumento = uiDocumento ?? throw new ArgumentNullException(nameof(uiDocumento));
            _documento = uiDocumento.Document;
        }

        /// <summary>Gera a vista, fotografa os 24 quadros, grava GIF e PNGs e restaura o estado atual.</summary>
        /// <param name="resumirAtual">O resumo atual de cada sala, para restaurar ao fim.</param>
        /// <param name="agora">Instante de referência (o último quadro é a hora cheia anterior).</param>
        /// <param name="progresso">Recebe uma linha por quadro, para a interface mostrar andamento.</param>
        public ResultadoDaExportacaoDeVideo Exportar(
            ConjuntoDeAmbientes conjunto,
            Func<Ambiente, ResumoDoAmbiente> resumirAtual,
            DateTime agora,
            Action<string>? progresso)
        {
            var instantes = LinhaDoTempoDoVideo.Instantes(agora);
            string pasta = Path.Combine(ConfiguracaoDeExportacao.PastaDeSaida, $"Video_{agora:yyyy-MM-dd_HHmm}");
            string pastaTemporaria = Path.Combine(pasta, ".revit");

            Directory.CreateDirectory(pastaTemporaria);

            progresso?.Invoke("Gerando a vista 3D de conforto para o vídeo…");

            var gerador = new GeradorDeVistaRevit(_documento);
            var vista = gerador.GerarVistaDeConforto(conjunto, resumirAtual, out _, out bool volumesAtivados, out string? avisoDaPintura);

            _uiDocumento.ActiveView = vista;

            var quadros = new List<QuadroDeGif>(instantes.Count);
            var cultura = CultureInfo.CurrentCulture;

            try
            {
                for (int indice = 0; indice < instantes.Count; indice++)
                {
                    var instante = instantes[indice];
                    int numero = indice + 1;

                    progresso?.Invoke($"Vídeo do último dia: quadro {numero}/{instantes.Count} — {instante:dd/MM HH:mm}…");

                    int comDados = RotularNoInstante(conjunto, instante);

                    string prefixo = Path.Combine(pastaTemporaria, $"quadro_{numero:00}");
                    string bruto = ExportarPng(vista, prefixo);

                    string titulo = instante.ToString("dd/MM/yyyy  HH:mm", cultura);
                    string subtitulo = $"MackTwin · estado de conforto das salas · {comDados} de {conjunto.Ambientes.Count} sala(s) com leitura na hora anterior";

                    var carimbado = CarimbadorDeQuadro.Carimbar(CarimbadorDeQuadro.Carregar(bruto), titulo, subtitulo);

                    CarimbadorDeQuadro.SalvarPng(carimbado, Path.Combine(pasta, $"quadro_{numero:00}_{instante:yyyy-MM-dd_HH}h.png"));

                    int atraso = indice == instantes.Count - 1
                        ? ConfiguracaoDeExportacao.AtrasoDoUltimoQuadroEmCentesimos
                        : ConfiguracaoDeExportacao.AtrasoEntreQuadrosEmCentesimos;

                    quadros.Add(CarimbadorDeQuadro.ParaQuadroDeGif(carimbado, atraso));
                }
            }
            finally
            {
                RestaurarEstadoAtual(conjunto, resumirAtual);
                ApagarPastaTemporaria(pastaTemporaria);
            }

            progresso?.Invoke("Gravando o GIF animado…");

            string caminhoDoGif = Path.Combine(pasta, ConfiguracaoDeExportacao.NomeDoGif);

            using (var arquivo = File.Create(caminhoDoGif))
                CodificadorDeGif.Gravar(quadros, arquivo);

            string? aviso = null;

            if (volumesAtivados)
                aviso = "O cálculo de volumes do projeto foi LIGADO (Áreas e volumes): sem ele o Revit não desenha Rooms em 3D.";

            if (avisoDaPintura != null)
                aviso = (aviso == null ? string.Empty : aviso + " ") + avisoDaPintura;

            return new ResultadoDaExportacaoDeVideo
            {
                Pasta = pasta,
                CaminhoDoGif = caminhoDoGif,
                Quadros = quadros.Count,
                Vista = vista,
                Inicio = instantes[0],
                Fim = instantes[instantes.Count - 1],
                Aviso = aviso,
            };
        }

        /// <summary>Grava nos Rooms o estado que cada sala tinha no instante. Devolve quantas tinham leitura.</summary>
        private int RotularNoInstante(ConjuntoDeAmbientes conjunto, DateTime instante)
        {
            var resumos = conjunto.Ambientes.ToDictionary(
                ambiente => ambiente.Codigo,
                ambiente => CalculadorDeResumoDoAmbiente.ResumirEm(ambiente, instante, LinhaDoTempoDoVideo.Tolerancia),
                StringComparer.Ordinal);

            using var transacao = new Transaction(_documento, ConfiguracaoDeExportacao.NomeDaTransacaoDeQuadro);
            transacao.Start();
            EsquemaDeCoresDeConforto.RotularRooms(_documento, conjunto, ambiente => resumos[ambiente.Codigo]);
            transacao.Commit();

            return resumos.Values.Count(resumo => resumo.TotalDeLeituras > 0);
        }

        private void RestaurarEstadoAtual(ConjuntoDeAmbientes conjunto, Func<Ambiente, ResumoDoAmbiente> resumirAtual)
        {
            using var transacao = new Transaction(_documento, ConfiguracaoDeExportacao.NomeDaTransacaoDeRestauracao);
            transacao.Start();
            EsquemaDeCoresDeConforto.RotularRooms(_documento, conjunto, resumirAtual);
            transacao.Commit();
        }

        /// <summary>
        /// Exporta a vista ativa como PNG com o prefixo dado e devolve o caminho do arquivo que o
        /// Revit criou (ele compõe o nome; para a vista corrente é <c>prefixo.png</c>).
        /// </summary>
        private string ExportarPng(View3D vista, string prefixo)
        {
            string pasta = Path.GetDirectoryName(prefixo)!;
            var antes = new HashSet<string>(Directory.GetFiles(pasta, "*.png"), StringComparer.OrdinalIgnoreCase);

            var opcoes = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = prefixo,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_150,
                ZoomType = ZoomFitType.FitToPage,
                FitDirection = FitDirectionType.Horizontal,
                PixelSize = ConfiguracaoDeExportacao.LarguraDoQuadroEmPixels,
            };

            opcoes.SetViewsAndSheets(new List<ElementId> { vista.Id });
            _documento.ExportImage(opcoes);

            string esperado = prefixo + ".png";

            if (File.Exists(esperado))
                return esperado;

            return Directory.GetFiles(pasta, "*.png").FirstOrDefault(arquivo => !antes.Contains(arquivo))
                ?? throw new InvalidOperationException("O Revit não gerou a imagem do quadro.");
        }

        private static void ApagarPastaTemporaria(string pasta)
        {
            try
            {
                if (Directory.Exists(pasta))
                    Directory.Delete(pasta, recursive: true);
            }
            catch (IOException)
            {
                // Pasta de trabalho; se ficar, não atrapalha.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
