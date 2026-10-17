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
    /// na planta (onde o Room aparece pelo "Interior Fill") quanto na 3D (onde o Revit 2023+
    /// desenha Rooms como volumes).
    ///
    /// Uma vista com template que controle "V/G Overrides Model" recusa as sobreposições; é
    /// o caso que a mensagem de erro explica e que "Gerar vista 3D" contorna, porque a vista
    /// gerada nasce sem template.
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

            ExibirCategoriaDeRooms(vista);

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
        /// Garante que a categoria Rooms esteja visível na vista. Precisa de transação ativa.
        /// </summary>
        public static void ExibirCategoriaDeRooms(View vista)
        {
            var categoria = new ElementId(BuiltInCategory.OST_Rooms);

            try
            {
                if (vista.CanCategoryBeHidden(categoria))
                    vista.SetCategoryHidden(categoria, false);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // A categoria não é controlável nesta vista (template, ou tipo de vista sem Rooms).
            }
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
            sobreposicao.SetCutForegroundPatternVisible(true);
            sobreposicao.SetCutForegroundPatternColor(cor);
            sobreposicao.SetProjectionLineColor(cor);
            sobreposicao.SetSurfaceTransparency(ConfiguracaoDePinturaDeAmbientes.TransparenciaDaSuperficie);

            if (solido != null)
            {
                sobreposicao.SetSurfaceForegroundPatternId(solido.Id);
                sobreposicao.SetCutForegroundPatternId(solido.Id);
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
