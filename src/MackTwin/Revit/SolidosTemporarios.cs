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
    /// <summary>
    /// Cores temporárias: um sólido com a forma de cada sala, colorido por sobreposição gráfica
    /// na vista, sem gravar parâmetro nenhum nos Rooms. Somem ao fechar o plug-in.
    /// </summary>
    /// <remarks>
    /// <b>Por que sólidos.</b> O preenchimento de um Room não obedece a sobreposição gráfica —
    /// só a esquema de cores, que exige parâmetro. Um <see cref="DirectShape"/> (Modelo
    /// genérico) com o volume do Room obedece: padrão de superfície sólido na cor do estado em
    /// 3D, padrão de corte na planta. Nenhum dado do modelo muda; só nascem elementos
    /// descartáveis, que <see cref="RemoverTodos"/> apaga — e a janela chama isso ao fechar.
    ///
    /// <b>Como são reconhecidos.</b> <c>ApplicationId</c> = "MackTwin" e o nome
    /// <c>MackTwin~temp~{vista}~{código}</c>; assim um conjunto deixado por uma sessão
    /// interrompida ainda é encontrado e removido, e <see cref="Recolorir"/> sabe em que vista
    /// pintar cada um a cada ciclo de atualização.
    ///
    /// Todos os métodos abrem a sua transação; chamar em contexto válido da API.
    /// </remarks>
    public static class SolidosTemporarios
    {
        private const string IdDaAplicacao = "MackTwin";
        private const string PrefixoDoNome = "MackTwin~temp~";
        private const int Transparencia = 20;

        /// <summary>Se há sólidos temporários do plug-in no documento.</summary>
        public static bool Existem(Document documento) => Coletar(documento).Any();

        /// <summary>
        /// Apaga os sólidos anteriores e cria um por sala casada, colorido na vista pelo estado
        /// atual. Devolve quantos foram criados.
        /// </summary>
        public static int Criar(Document documento, View vista, ConjuntoDeAmbientes conjunto, Func<Ambiente, ResumoDoAmbiente> resumir)
        {
            var solido = EsquemaDeCoresDeConforto.LocalizarPadraoSolido(documento);
            int criados = 0;

            using var transacao = new Transaction(documento, ConfiguracaoDePinturaDeAmbientes.NomeDaTransacaoDeTemporarios);
            transacao.Start();

            GeradorDeVistaRevit.GarantirCalculoDeVolumes(documento);
            Apagar(documento, Coletar(documento));

            foreach (var ambiente in conjunto.Ambientes)
            {
                if (!ambiente.IdDoElementoNoModelo.HasValue)
                    continue;

                if (documento.GetElement(new ElementId(ambiente.IdDoElementoNoModelo.Value)) is not Room room)
                    continue;

                var solidos = GeometriaDoRoom.Solidos(room);

                if (solidos.Count == 0)
                    continue;

                var forma = DirectShape.CreateElement(documento, new ElementId(BuiltInCategory.OST_GenericModel));
                forma.ApplicationId = IdDaAplicacao;
                forma.ApplicationDataId = ambiente.Codigo;
                forma.SetShape(solidos.Cast<GeometryObject>().ToList());
                forma.Name = MontarNome(vista.Id, ambiente.Codigo);

                var cor = PintorDeAmbientes.ParaCorDoRevit(CoresDeConforto.Para(resumir(ambiente).ClassificacaoGeral));
                vista.SetElementOverrides(forma.Id, PintorDeAmbientes.MontarSobreposicao(cor, solido, Transparencia));
                criados++;
            }

            transacao.Commit();
            return criados;
        }

        /// <summary>
        /// Atualiza a cor dos sólidos existentes, cada um na vista em que foi criado. Sem sólidos,
        /// não abre transação. Devolve quantos foram recoloridos.
        /// </summary>
        public static int Recolorir(Document documento, ConjuntoDeAmbientes conjunto, Func<Ambiente, ResumoDoAmbiente> resumir)
        {
            var formas = Coletar(documento).ToList();

            if (formas.Count == 0)
                return 0;

            var ambientes = conjunto.Ambientes.ToDictionary(ambiente => ambiente.Codigo, StringComparer.Ordinal);
            var solido = EsquemaDeCoresDeConforto.LocalizarPadraoSolido(documento);
            int recoloridos = 0;

            using var transacao = new Transaction(documento, ConfiguracaoDePinturaDeAmbientes.NomeDaTransacaoDeTemporarios);
            transacao.Start();

            foreach (var forma in formas)
            {
                if (!LerNome(forma.Name, out var idDaVista, out string codigo))
                    continue;

                if (documento.GetElement(idDaVista) is not View vista || !ambientes.TryGetValue(codigo, out var ambiente))
                    continue;

                var cor = PintorDeAmbientes.ParaCorDoRevit(CoresDeConforto.Para(resumir(ambiente).ClassificacaoGeral));

                try
                {
                    vista.SetElementOverrides(forma.Id, PintorDeAmbientes.MontarSobreposicao(cor, solido, Transparencia));
                    recoloridos++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // Vista que não aceita mais sobreposição (template aplicado depois).
                }
            }

            transacao.Commit();
            return recoloridos;
        }

        /// <summary>Apaga todos os sólidos temporários do documento. Devolve quantos.</summary>
        public static int RemoverTodos(Document documento)
        {
            var formas = Coletar(documento).ToList();

            if (formas.Count == 0)
                return 0;

            using var transacao = new Transaction(documento, ConfiguracaoDePinturaDeAmbientes.NomeDaTransacaoDeTemporarios);
            transacao.Start();
            int removidos = Apagar(documento, formas);
            transacao.Commit();
            return removidos;
        }

        private static int Apagar(Document documento, IEnumerable<DirectShape> formas)
        {
            int removidos = 0;

            foreach (var forma in formas.ToList())
            {
                try
                {
                    documento.Delete(forma.Id);
                    removidos++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // Já apagado ou travado por outro usuário.
                }
            }

            return removidos;
        }

        private static IEnumerable<DirectShape> Coletar(Document documento)
        {
            return new FilteredElementCollector(documento)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Where(forma => forma.ApplicationId == IdDaAplicacao && forma.Name.StartsWith(PrefixoDoNome, StringComparison.Ordinal));
        }

        private static string MontarNome(ElementId idDaVista, string codigo)
        {
            return PrefixoDoNome + idDaVista.Value.ToString(CultureInfo.InvariantCulture) + "~" + codigo;
        }

        private static bool LerNome(string nome, out ElementId idDaVista, out string codigo)
        {
            idDaVista = ElementId.InvalidElementId;
            codigo = string.Empty;

            if (!nome.StartsWith(PrefixoDoNome, StringComparison.Ordinal))
                return false;

            var partes = nome.Substring(PrefixoDoNome.Length).Split('~', 2);

            if (partes.Length != 2 || !long.TryParse(partes[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long valor))
                return false;

            idDaVista = new ElementId(valor);
            codigo = partes[1];
            return true;
        }
    }
}
