using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using MackTwin.Configuracao;
using MackTwin.Dominio;

namespace MackTwin.Revit
{
    /// <summary>
    /// Pinta os Rooms de uma vista com a cor do estado de conforto da sala — o prédio de relance.
    /// </summary>
    /// <remarks>
    /// Usa <see cref="OverrideGraphicSettings"/> por elemento, e não cor de material nem esquema
    /// de cores: a pintura é da VISTA, não do modelo, e some com <see cref="Limpar"/> sem deixar
    /// rastro no projeto. Preenchimento sólido de superfície e de corte, para que funcione tanto
    /// na planta quanto na 3D.
    ///
    /// <b>Onde a cor aparece.</b> Na planta, o Room só é visível pela subcategoria
    /// "Preenchimento interno" (Interior Fill), que a maioria dos templates deixa DESLIGADA —
    /// e então a sobreposição é aplicada mas nada muda na tela. Por isso
    /// <see cref="ExibirCategoriaDeRooms"/> liga a categoria Rooms E todas as suas subcategorias
    /// na vista antes de pintar. Uma vista com template que controle visibilidade ou
    /// sobreposições recusa essas mudanças; é o caso que "Gerar vista 3D" contorna, porque a
    /// vista gerada nasce sem template.
    ///
    /// Deve rodar dentro de um contexto válido da API.
    /// </remarks>
    public static class PintorDeAmbientes
    {
        private static readonly ViewType[] TiposAceitos =
        {
            ViewType.FloorPlan,
            ViewType.CeilingPlan,
            ViewType.AreaPlan,
            ViewType.ThreeD,
            ViewType.Section,
            ViewType.Elevation,
        };

        /// <summary>Se a vista é de um tipo em que a pintura faz sentido e não é um template.</summary>
        public static bool VistaAceitaPintura(View? vista)
        {
            return vista != null && !vista.IsTemplate && TiposAceitos.Contains(vista.ViewType);
        }

        /// <summary>
        /// Pinta, na vista, cada ambiente casado com um Room. Devolve quantos foram pintados.
        /// </summary>
        /// <param name="classificar">Como obter o estado da sala — normalmente o resumo na janela escolhida.</param>
        public static int Pintar(
            Document documento,
            View vista,
            ConjuntoDeAmbientes conjunto,
            Func<Ambiente, ClassificacaoDeConforto> classificar)
        {
            var solido = LocalizarPadraoSolido(documento);
            int pintados = 0;

            using var transacao = new Transaction(documento, ConfiguracaoDePinturaDeAmbientes.NomeDaTransacaoDePintura);
            transacao.Start();

            ExibirCategoriaDeRooms(documento, vista);

            foreach (var ambiente in conjunto.Ambientes)
            {
                if (!ambiente.IdDoElementoNoModelo.HasValue)
                    continue;

                var id = new ElementId(ambiente.IdDoElementoNoModelo.Value);

                if (documento.GetElement(id) == null)
                    continue;

                var cor = ParaCorDoRevit(CoresDeConforto.Para(classificar(ambiente)));
                vista.SetElementOverrides(id, MontarSobreposicao(cor, solido));
                pintados++;
            }

            transacao.Commit();
            return pintados;
        }

        /// <summary>Remove as sobreposições dos elementos informados. Devolve quantos foram limpos.</summary>
        public static int Limpar(Document documento, View vista, IEnumerable<long> idsDosElementos)
        {
            int limpos = 0;

            using var transacao = new Transaction(documento, ConfiguracaoDePinturaDeAmbientes.NomeDaTransacaoDeLimpeza);
            transacao.Start();

            foreach (long valor in idsDosElementos)
            {
                var id = new ElementId(valor);

                if (documento.GetElement(id) == null)
                    continue;

                vista.SetElementOverrides(id, new OverrideGraphicSettings());
                limpos++;
            }

            transacao.Commit();
            return limpos;
        }

        /// <summary>
        /// Liga, na vista, a categoria Rooms e todas as suas subcategorias (inclusive o
        /// "Preenchimento interno", por onde a cor aparece na planta). Precisa de transação ativa.
        /// Devolve quantas categorias foram de fato ligadas.
        /// </summary>
        public static int ExibirCategoriaDeRooms(Document documento, View vista)
        {
            var categoria = Category.GetCategory(documento, BuiltInCategory.OST_Rooms);

            if (categoria == null)
                return 0;

            int ligadas = TentarExibir(vista, categoria.Id) ? 1 : 0;

            foreach (Category subcategoria in categoria.SubCategories)
            {
                if (TentarExibir(vista, subcategoria.Id))
                    ligadas++;
            }

            return ligadas;
        }

        private static bool TentarExibir(View vista, ElementId idDaCategoria)
        {
            try
            {
                if (vista.CanCategoryBeHidden(idDaCategoria) && vista.GetCategoryHidden(idDaCategoria))
                {
                    vista.SetCategoryHidden(idDaCategoria, false);
                    return true;
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Categoria não controlável nesta vista.
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Visibilidade controlada por template de vista.
            }

            return false;
        }

        /// <summary>Cor do Revit a partir do hexadecimal <c>#RRGGBB</c> da configuração.</summary>
        public static Color ParaCorDoRevit(string hexadecimal)
        {
            var (r, g, b) = CoresDeConforto.ParaRgb(hexadecimal);
            return new Color(r, g, b);
        }

        private static OverrideGraphicSettings MontarSobreposicao(Color cor, FillPatternElement? solido)
        {
            var sobreposicao = new OverrideGraphicSettings();

            sobreposicao.SetSurfaceForegroundPatternVisible(true);
            sobreposicao.SetSurfaceForegroundPatternColor(cor);
            sobreposicao.SetSurfaceBackgroundPatternVisible(true);
            sobreposicao.SetSurfaceBackgroundPatternColor(cor);
            sobreposicao.SetCutForegroundPatternVisible(true);
            sobreposicao.SetCutForegroundPatternColor(cor);
            sobreposicao.SetCutBackgroundPatternVisible(true);
            sobreposicao.SetCutBackgroundPatternColor(cor);
            sobreposicao.SetProjectionLineColor(cor);
            sobreposicao.SetCutLineColor(cor);
            sobreposicao.SetSurfaceTransparency(ConfiguracaoDePinturaDeAmbientes.TransparenciaDaSuperficie);

            if (solido != null)
            {
                sobreposicao.SetSurfaceForegroundPatternId(solido.Id);
                sobreposicao.SetSurfaceBackgroundPatternId(solido.Id);
                sobreposicao.SetCutForegroundPatternId(solido.Id);
                sobreposicao.SetCutBackgroundPatternId(solido.Id);
            }

            return sobreposicao;
        }

        private static FillPatternElement? LocalizarPadraoSolido(Document documento)
        {
            return new FilteredElementCollector(documento)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(padrao => padrao.GetFillPattern().IsSolidFill);
        }
    }
}
