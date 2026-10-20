using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using MackTwin.Configuracao;
using MackTwin.Dominio;

namespace MackTwin.Revit
{
    /// <summary>O que a geração da folha produziu.</summary>
    public sealed class ResultadoDaFolha
    {
        /// <summary>As folhas criadas, uma por nível com Rooms.</summary>
        public IReadOnlyList<ViewSheet> Folhas { get; init; } = Array.Empty<ViewSheet>();

        /// <summary>Quantas plantas foram criadas (duas por nível).</summary>
        public int Plantas { get; init; }

        /// <summary>Observações (sem carimbo, sem esquema de base…), ou null.</summary>
        public string? Aviso { get; init; }
    }

    /// <summary>
    /// Monta o relatório gráfico para impressão: para cada nível com salas, uma folha com duas
    /// plantas lado a lado — à esquerda os Rooms coloridos pelo estado da TEMPERATURA, à direita
    /// pelo da UMIDADE —, cada planta com a sua legenda de cores, e um cabeçalho com a data.
    /// </summary>
    /// <remarks>
    /// <b>Duas plantas, duas grandezas.</b> Uma só cor por sala esconderia qual grandeza está
    /// fora da faixa; lado a lado, a leitura é imediata. Cada planta recebe o esquema de cores
    /// da sua grandeza (<see cref="EsquemaDeCoresDeConforto"/>) e uma legenda nativa
    /// (<see cref="ColorFillLegend"/>), que o Revit mantém sincronizada com o esquema.
    ///
    /// <b>Plantas novas, sem template.</b> Duplicadas de uma planta existente do nível (mesmo
    /// recorte e escala) ou criadas do zero se não houver; o template é removido para que a
    /// visibilidade de Ambientes e o esquema de cores não sejam travados por ele. Os Rooms
    /// ganham etiqueta para que o papel diga qual sala é qual.
    ///
    /// <b>Encaixe na folha.</b> A folha usa o primeiro carimbo do projeto (ou A3 paisagem, sem
    /// carimbo, se não houver). Cada viewport é medida depois de colocada e a escala da planta é
    /// aumentada até caber na sua metade. Tudo numa transação, dentro de um contexto válido da API.
    /// </remarks>
    public sealed class GeradorDeFolhaDeImpressao
    {
        private const double MargemDaFolhaEmPes = 15.0 / 304.8;
        private const double FolgaDaLegendaEmPes = 3.0;

        private readonly Document _documento;

        /// <summary>Cria o gerador sobre o documento aberto.</summary>
        public GeradorDeFolhaDeImpressao(Document documento)
        {
            _documento = documento ?? throw new ArgumentNullException(nameof(documento));
        }

        /// <summary>Gera as folhas. Devolve vazio (com aviso) se o modelo não tem Rooms colocados.</summary>
        public ResultadoDaFolha Gerar(ConjuntoDeAmbientes conjunto, Func<Ambiente, ResumoDoAmbiente> resumir, DateTime agora)
        {
            var roomsPorNivel = ColetarRooms()
                .Where(room => room.LevelId != ElementId.InvalidElementId)
                .GroupBy(room => room.LevelId)
                .Select(grupo => (Nivel: (Level)_documento.GetElement(grupo.Key), Rooms: grupo.ToList()))
                .Where(par => par.Nivel != null)
                .OrderBy(par => par.Nivel.Elevation)
                .ToList();

            if (roomsPorNivel.Count == 0)
                return new ResultadoDaFolha { Aviso = "O modelo não tem Rooms colocados em nenhum nível." };

            var solido = EsquemaDeCoresDeConforto.LocalizarPadraoSolido(_documento);
            var tipoDePlanta = LocalizarTipoDeVista(ViewFamily.FloorPlan)
                ?? throw new InvalidOperationException("O modelo não tem um tipo de vista de planta.");
            var carimbo = LocalizarCarimbo();
            var avisos = new List<string>();

            if (carimbo == null)
                avisos.Add("O projeto não tem família de carimbo carregada: a folha saiu sem carimbo, em A3.");

            var folhas = new List<ViewSheet>();
            int plantas = 0;

            using var transacao = new Transaction(_documento, ConfiguracaoDeExportacao.NomeDaTransacaoDeFolha);
            transacao.Start();

            var idsDosParametros = EsquemaDeCoresDeConforto.GarantirParametros(_documento);
            EsquemaDeCoresDeConforto.RotularRooms(_documento, conjunto, resumir);

            var esquemas = new Dictionary<GrandezaDeConforto, ColorFillScheme?>
            {
                [GrandezaDeConforto.Temperatura] = EsquemaDeCoresDeConforto.GarantirEsquema(
                    _documento, GrandezaDeConforto.Temperatura, idsDosParametros[GrandezaDeConforto.Temperatura], solido),
                [GrandezaDeConforto.Umidade] = EsquemaDeCoresDeConforto.GarantirEsquema(
                    _documento, GrandezaDeConforto.Umidade, idsDosParametros[GrandezaDeConforto.Umidade], solido),
            };

            if (esquemas.Values.Any(esquema => esquema == null))
            {
                avisos.Add("O projeto não tem nenhum esquema de cores de Ambientes para servir de base; as plantas saíram sem cor. " +
                           "Crie um qualquer em Arquitetura › Ambiente e Área › Esquemas de cores e gere de novo.");
            }

            string carimboDeTempo = agora.ToString(ConfiguracaoDeNomeDeVista.FormatoDoCarimboDeTempo, CultureInfo.InvariantCulture);
            var numerosEmUso = new HashSet<string>(
                new FilteredElementCollector(_documento).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(folha => folha.SheetNumber),
                StringComparer.OrdinalIgnoreCase);
            var nomesEmUso = new HashSet<string>(
                new FilteredElementCollector(_documento).OfClass(typeof(View)).Cast<View>().Select(vista => vista.Name),
                StringComparer.OrdinalIgnoreCase);

            int sequencia = 1;

            foreach (var (nivel, rooms) in roomsPorNivel)
            {
                var plantaDeTemperatura = MontarPlanta(nivel, rooms, tipoDePlanta, GrandezaDeConforto.Temperatura,
                    esquemas[GrandezaDeConforto.Temperatura], carimboDeTempo, nomesEmUso);
                var plantaDeUmidade = MontarPlanta(nivel, rooms, tipoDePlanta, GrandezaDeConforto.Umidade,
                    esquemas[GrandezaDeConforto.Umidade], carimboDeTempo, nomesEmUso);
                plantas += 2;

                var folha = ViewSheet.Create(_documento, carimbo?.Id ?? ElementId.InvalidElementId);
                folha.SheetNumber = ProximoNumeroLivre(numerosEmUso, ref sequencia);
                folha.Name = $"MackTwin — Conforto {agora:dd/MM/yyyy HH:mm} — {nivel.Name}";

                _documento.Regenerate();

                var (largura, altura) = MedirFolha(folha);
                double larguraDisponivel = largura * ConfiguracaoDeExportacao.FracaoDaLarguraPorPlanta;
                double alturaDisponivel = altura * ConfiguracaoDeExportacao.FracaoDaAlturaParaPlantas;
                double centroY = altura * 0.47;

                ColocarNaFolha(folha, plantaDeTemperatura, new XYZ(largura * 0.27, centroY, 0), larguraDisponivel, alturaDisponivel);
                ColocarNaFolha(folha, plantaDeUmidade, new XYZ(largura * 0.73, centroY, 0), larguraDisponivel, alturaDisponivel);

                EscreverCabecalho(folha, nivel, agora, largura, altura, conjunto);

                folhas.Add(folha);
            }

            transacao.Commit();

            return new ResultadoDaFolha
            {
                Folhas = folhas,
                Plantas = plantas,
                Aviso = avisos.Count == 0 ? null : string.Join(" ", avisos),
            };
        }

        // ── Plantas ──────────────────────────────────────────────────────

        private ViewPlan MontarPlanta(
            Level nivel,
            List<Room> rooms,
            ViewFamilyType tipoDePlanta,
            GrandezaDeConforto grandeza,
            ColorFillScheme? esquema,
            string carimboDeTempo,
            HashSet<string> nomesEmUso)
        {
            var modelo = LocalizarPlantaDoNivel(nivel);
            ViewPlan planta = modelo != null
                ? (ViewPlan)_documento.GetElement(modelo.Duplicate(ViewDuplicateOption.Duplicate))
                : ViewPlan.Create(_documento, tipoDePlanta.Id, nivel.Id);

            if (planta.ViewTemplateId != ElementId.InvalidElementId)
                planta.ViewTemplateId = ElementId.InvalidElementId;

            planta.Name = NomeLivre($"{ConfiguracaoDeNomeDeVista.Prefixo}_Folha_{EsquemaDeCoresDeConforto.Descrever(grandeza)}_{nivel.Name}_{carimboDeTempo}", nomesEmUso);
            planta.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.Set($"{EsquemaDeCoresDeConforto.Descrever(grandeza)} — {nivel.Name}");

            PintorDeAmbientes.ExibirCategoriaDeRooms(_documento, planta);

            if (esquema != null)
                EsquemaDeCoresDeConforto.AplicarAVista(planta, esquema);

            EtiquetarRooms(planta, rooms);

            _documento.Regenerate();

            if (esquema != null)
                ColocarLegenda(planta, rooms);

            return planta;
        }

        private ViewPlan? LocalizarPlantaDoNivel(Level nivel)
        {
            return new FilteredElementCollector(_documento)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(vista => !vista.IsTemplate && vista.ViewType == ViewType.FloorPlan && vista.GenLevel?.Id == nivel.Id)
                .OrderByDescending(vista => vista.Name.Length == 0 ? 0 : 1)
                .FirstOrDefault();
        }

        private void EtiquetarRooms(ViewPlan planta, List<Room> rooms)
        {
            foreach (var room in rooms)
            {
                if (room.Location is not LocationPoint ponto)
                    continue;

                try
                {
                    _documento.Create.NewRoomTag(new LinkElementId(room.Id), new UV(ponto.Point.X, ponto.Point.Y), planta.Id);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // Sem tipo de etiqueta de ambiente carregado: a planta sai sem etiquetas.
                    return;
                }
            }
        }

        /// <summary>A legenda nativa, no canto superior direito das salas do nível.</summary>
        private void ColocarLegenda(ViewPlan planta, List<Room> rooms)
        {
            double maximoX = double.NegativeInfinity, maximoY = double.NegativeInfinity;

            foreach (var room in rooms)
            {
                var caixa = room.get_BoundingBox(planta);

                if (caixa == null)
                    continue;

                maximoX = Math.Max(maximoX, caixa.Max.X);
                maximoY = Math.Max(maximoY, caixa.Max.Y);
            }

            if (double.IsInfinity(maximoX))
                return;

            try
            {
                ColorFillLegend.Create(_documento, planta.Id, new ElementId(BuiltInCategory.OST_Rooms),
                    new XYZ(maximoX + FolgaDaLegendaEmPes, maximoY, 0));
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // A vista recusou a legenda; a planta continua colorida.
            }
        }

        // ── Folha ────────────────────────────────────────────────────────

        private void ColocarNaFolha(ViewSheet folha, ViewPlan planta, XYZ centro, double larguraDisponivel, double alturaDisponivel)
        {
            if (!Viewport.CanAddViewToSheet(_documento, folha.Id, planta.Id))
                return;

            var viewport = Viewport.Create(_documento, folha.Id, planta.Id, centro);
            _documento.Regenerate();

            var (largura, altura) = MedirViewport(viewport);
            double fator = Math.Max(largura / larguraDisponivel, altura / alturaDisponivel);

            if (fator > 1.0)
            {
                try
                {
                    planta.Scale = (int)Math.Ceiling(planta.Scale * fator);
                    _documento.Regenerate();
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // Escala travada; a planta fica do tamanho que der.
                }
            }

            viewport.SetBoxCenter(centro);
        }

        private void EscreverCabecalho(ViewSheet folha, Level nivel, DateTime agora, double largura, double altura, ConjuntoDeAmbientes conjunto)
        {
            var tipoDeTexto = _documento.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

            if (tipoDeTexto == ElementId.InvalidElementId)
                return;

            string texto =
                $"MackTwin — Gêmeo Digital Mackenzie · Relatório de conforto ambiental · {nivel.Name}\n" +
                $"Estado em {agora:dd/MM/yyyy HH:mm} · {conjunto.Ambientes.Count(a => a.CasadoComOModelo)} sala(s) com sensor · " +
                $"Faixas NBR 17037: {FaixasDeConfortoAmbiental.TemperaturaMinima:0}–{FaixasDeConfortoAmbiental.TemperaturaMaxima:0} °C, " +
                $"{FaixasDeConfortoAmbiental.UmidadeMinima:0}–{FaixasDeConfortoAmbiental.UmidadeMaxima:0} % UR\n" +
                "À esquerda, cor pela TEMPERATURA; à direita, pela UMIDADE. Cada planta traz a própria legenda.";

            try
            {
                TextNote.Create(_documento, folha.Id, new XYZ(MargemDaFolhaEmPes, altura - MargemDaFolhaEmPes, 0), texto, tipoDeTexto);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Folha sem cabeçalho; as plantas continuam lá.
            }
        }

        private (double Largura, double Altura) MedirFolha(ViewSheet folha)
        {
            var contorno = folha.Outline;
            double largura = contorno.Max.U - contorno.Min.U;
            double altura = contorno.Max.V - contorno.Min.V;

            if (largura < 0.1 || altura < 0.1)
                return (ConfiguracaoDeExportacao.LarguraA3EmPes, ConfiguracaoDeExportacao.AlturaA3EmPes);

            return (largura, altura);
        }

        private static (double Largura, double Altura) MedirViewport(Viewport viewport)
        {
            var contorno = viewport.GetBoxOutline();
            return (contorno.MaximumPoint.X - contorno.MinimumPoint.X, contorno.MaximumPoint.Y - contorno.MinimumPoint.Y);
        }

        // ── Apoio ────────────────────────────────────────────────────────

        private IEnumerable<Room> ColetarRooms()
        {
            return new FilteredElementCollector(_documento)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(room => room.Location != null);
        }

        private ViewFamilyType? LocalizarTipoDeVista(ViewFamily familia)
        {
            return new FilteredElementCollector(_documento)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(tipo => tipo.ViewFamily == familia);
        }

        private FamilySymbol? LocalizarCarimbo()
        {
            return new FilteredElementCollector(_documento)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .OfType<FamilySymbol>()
                .OrderBy(tipo => tipo.FamilyName, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();
        }

        private static string ProximoNumeroLivre(HashSet<string> emUso, ref int sequencia)
        {
            string numero;

            do
            {
                numero = ConfiguracaoDeExportacao.PrefixoDoNumeroDaFolha + sequencia.ToString("00", CultureInfo.InvariantCulture);
                sequencia++;
            }
            while (emUso.Contains(numero));

            emUso.Add(numero);
            return numero;
        }

        private static string NomeLivre(string base_, HashSet<string> emUso)
        {
            string nome = base_;
            int sufixo = 2;

            while (emUso.Contains(nome))
                nome = $"{base_} ({sufixo++})";

            emUso.Add(nome);
            return nome;
        }
    }
}
