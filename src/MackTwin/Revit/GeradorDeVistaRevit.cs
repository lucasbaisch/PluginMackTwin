using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using MackTwin.Configuracao;
using MackTwin.Dominio;

namespace MackTwin.Revit
{
    /// <summary>
    /// Cria a vista 3D de conforto: uma isométrica nova, sem template, com os Rooms visíveis e
    /// pintados pelo estado de cada sala.
    /// </summary>
    /// <remarks>
    /// Nasce sem template de propósito: um template que inclua "V/G Overrides Model" entre as
    /// propriedades controladas trava as sobreposições, e a pintura simplesmente não aparece,
    /// sem erro nenhum. Como a vista é gerada pelo plug-in para um fim específico, não há o que
    /// um template do projeto tenha a dizer sobre ela.
    ///
    /// Nome: <c>MackTwin_Conforto_v{NN}_{aaaa-MM-dd_HHmm}</c>, com a próxima versão livre — duas
    /// gerações no mesmo minuto não colidem, e a versão conta quantas vezes a vista foi gerada
    /// mesmo quando só a última sobrevive no modelo.
    ///
    /// Deve rodar dentro de um contexto válido da API.
    /// </remarks>
    public sealed class GeradorDeVistaRevit
    {
        private readonly Document _documento;

        /// <summary>Cria o gerador sobre o documento aberto.</summary>
        public GeradorDeVistaRevit(Document documento)
        {
            _documento = documento ?? throw new ArgumentNullException(nameof(documento));
        }

        /// <summary>Gera a vista e pinta as salas casadas. Devolve a vista criada.</summary>
        /// <param name="classificar">Como obter o estado da sala — normalmente o resumo na janela escolhida.</param>
        /// <param name="pintados">Quantas salas receberam cor.</param>
        /// <exception cref="InvalidOperationException">O modelo não tem tipo de vista 3D.</exception>
        public View3D GerarVistaDeConforto(
            ConjuntoDeAmbientes conjunto,
            Func<Ambiente, ClassificacaoDeConforto> classificar,
            out int pintados)
        {
            var tipoDaVista = LocalizarTipoDaVista3D()
                ?? throw new InvalidOperationException("O modelo não tem um tipo de vista 3D disponível.");

            View3D vista;

            using (var transacao = new Transaction(_documento, ConfiguracaoDePinturaDeAmbientes.NomeDaTransacaoDeVista))
            {
                transacao.Start();
                vista = CriarVista3D(tipoDaVista, MontarNomeDaVista());
                PintorDeAmbientes.ExibirCategoriaDeRooms(_documento, vista);
                PrepararParaConforto(vista);
                transacao.Commit();
            }

            pintados = PintorDeAmbientes.Pintar(_documento, vista, conjunto, classificar);
            return vista;
        }

        // ── Configuração gráfica da vista de conforto ────────────────────

        /// <summary>
        /// Deixa a vista pronta para mostrar os Rooms coloridos sem que o usuário mexa em nada.
        /// </summary>
        /// <remarks>
        /// Três coisas escondem a cor numa 3D recém-criada: o estilo <i>Aramado</i> não desenha
        /// padrões de superfície (é neles que a cor vai); telhados e forros cobrem as salas por
        /// cima; e paredes e pisos opacos as cercam. Logo: estilo sombreado com arestas, telhados
        /// e forros ocultos, e o envoltório (paredes, pisos, portas, janelas, painéis de cortina)
        /// semitransparente, para que os volumes das salas apareçam através dele.
        ///
        /// Tudo por categoria e só nesta vista: nada muda no modelo nem nas outras vistas.
        /// Precisa de transação ativa.
        /// </remarks>
        private static void PrepararParaConforto(View3D vista)
        {
            vista.DisplayStyle = DisplayStyle.ShadingWithEdges;
            vista.DetailLevel = ViewDetailLevel.Medium;

            foreach (var categoria in new[] { BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Ceilings })
                TentarOcultar(vista, new ElementId(categoria));

            var fantasma = new OverrideGraphicSettings();
            fantasma.SetSurfaceTransparency(ConfiguracaoDePinturaDeAmbientes.TransparenciaDoEnvoltorio);

            var envoltorio = new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_CurtainWallPanels,
                BuiltInCategory.OST_CurtainWallMullions,
            };

            foreach (var categoria in envoltorio)
                TentarSobrepor(vista, new ElementId(categoria), fantasma);
        }

        private static void TentarOcultar(View vista, ElementId idDaCategoria)
        {
            try
            {
                if (vista.CanCategoryBeHidden(idDaCategoria))
                    vista.SetCategoryHidden(idDaCategoria, true);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Categoria inexistente ou não controlável nesta vista.
            }
        }

        private static void TentarSobrepor(View vista, ElementId idDaCategoria, OverrideGraphicSettings sobreposicao)
        {
            try
            {
                vista.SetCategoryOverrides(idDaCategoria, sobreposicao);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Categoria inexistente ou sem sobreposição possível nesta vista.
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Sobreposições controladas por template (não é o caso da vista gerada).
            }
        }

        // ── Nome da vista ────────────────────────────────────────────────

        private string MontarNomeDaVista()
        {
            string carimbo = DateTime.Now.ToString(ConfiguracaoDeNomeDeVista.FormatoDoCarimboDeTempo);

            var nomesEmUso = new HashSet<string>(
                new FilteredElementCollector(_documento)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Select(vista => vista.Name),
                StringComparer.OrdinalIgnoreCase);

            int versao = ObterMaiorVersaoExistente() + 1;
            int limite = versao + ConfiguracaoDeNomeDeVista.LimiteDeTentativasDeVersao;
            string nome;

            do
            {
                nome = ComporNomeDaVista(versao, carimbo);
                versao++;
            }
            while (nomesEmUso.Contains(nome) && versao < limite);

            return nome;
        }

        /// <summary>Monta <c>MackTwin_Conforto_vNN_carimbo</c>.</summary>
        public static string ComporNomeDaVista(int versao, string carimbo)
        {
            return $"{ConfiguracaoDeNomeDeVista.Prefixo}_{ConfiguracaoDeNomeDeVista.AssuntoConforto}_" +
                   $"{ConfiguracaoDeNomeDeVista.PrefixoDeVersao}" +
                   $"{versao.ToString(ConfiguracaoDeNomeDeVista.FormatoDaVersao)}_{carimbo}";
        }

        /// <summary>Maior versão já usada entre as vistas 3D de conforto, ou 0.</summary>
        private int ObterMaiorVersaoExistente()
        {
            string padrao = "^"
                + Regex.Escape($"{ConfiguracaoDeNomeDeVista.Prefixo}_{ConfiguracaoDeNomeDeVista.AssuntoConforto}_")
                + Regex.Escape(ConfiguracaoDeNomeDeVista.PrefixoDeVersao)
                + @"(?<versao>\d+)_";

            int maior = 0;

            foreach (var vista in new FilteredElementCollector(_documento).OfClass(typeof(View3D)).Cast<View3D>())
            {
                var casamento = Regex.Match(vista.Name, padrao, RegexOptions.IgnoreCase);

                if (casamento.Success
                    && int.TryParse(casamento.Groups["versao"].Value, out int versao)
                    && versao > maior)
                {
                    maior = versao;
                }
            }

            return maior;
        }

        // ── Utilitários de vista ─────────────────────────────────────────

        private ViewFamilyType? LocalizarTipoDaVista3D()
        {
            return new FilteredElementCollector(_documento)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(tipo => tipo.ViewFamily == ViewFamily.ThreeDimensional);
        }

        /// <summary>Isométrica sem caixa de corte e sem template. Precisa de transação ativa.</summary>
        private View3D CriarVista3D(ViewFamilyType tipoDaVista, string nomeDaVista)
        {
            var vista = View3D.CreateIsometric(_documento, tipoDaVista.Id);
            vista.Name = nomeDaVista;

            if (vista.ViewTemplateId != ElementId.InvalidElementId)
                vista.ViewTemplateId = ElementId.InvalidElementId;

            vista.IsSectionBoxActive = false;
            return vista;
        }
    }
}
