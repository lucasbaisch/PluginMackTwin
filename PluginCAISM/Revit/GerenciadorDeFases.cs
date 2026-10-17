using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using PluginCAISM.Dominio;

namespace PluginCAISM.Revit
{
    /// <summary>
    /// Gerencia a criação e localização de fases de projeto Revit correspondentes às sessões.
    /// </summary>
    /// <remarks>
    /// Cada sessão recebe uma fase dedicada com nome no formato "{CenarioSelecionado}_{IdentificacaoManual}",
    /// por exemplo "Cenario2_Dia_MA001". Isso permite filtrar e visualizar os hitpoints
    /// de cada sessão de forma independente no modelo Revit.
    ///
    /// Todas as operações de criação devem ocorrer dentro de uma Transaction ativa.
    /// Esta classe não gerencia transações — a responsabilidade é do chamador.
    /// </remarks>
    public class GerenciadorDeFases
    {
        private readonly Document _documento;

        /// <summary>
        /// Inicializa o gerenciador com o documento Revit ativo.
        /// </summary>
        /// <param name="documento">Documento Revit no qual as fases serão criadas.</param>
        public GerenciadorDeFases(Document documento)
        {
            _documento = documento;
        }

        /// <summary>
        /// Garante a existência das fases correspondentes a todas as sessões fornecidas,
        /// criando apenas as que ainda não existem no documento.
        /// </summary>
        /// <param name = "sessoes" > Sessões para as quais as fases devem ser garantidas.</param>
        /// <returns>
        /// Dicionário mapeando o nome da fase para o objeto<see cref = "Phase" /> correspondente.
        /// </ returns >
        //public Dictionary<string, Phase> GarantirFasesDasSessoes(IEnumerable<Sessao> sessoes)
        //{
        //    var fasesExistentes = CarregarFasesExistentes();
        //    var resultado = new Dictionary<string, Phase>();

        //    foreach (var sessao in sessoes)
        //    {
        //        string nomeDaFase = sessao.NomeDeFaseRevit;

                //if (!fasesExistentes.TryGetValue(nomeDaFase, out Phase fase))
                //    fase = CriarFase(nomeDaFase, fasesExistentes);

                //resultado[nomeDaFase] = fase;
        //    }

        //    return resultado;
        //}

        /// <summary>
        /// Localiza uma fase existente pelo nome.
        /// Retorna null se a fase não for encontrada.
        /// </summary>
        /// <param name="nomeDaFase">Nome exato da fase.</param>
        public Phase LocazalizarFasePorNome(string nomeDaFase)
        {
            return new FilteredElementCollector(_documento)
                .OfClass(typeof(Phase))
                .Cast<Phase>()
                .FirstOrDefault(fase => fase.Name == nomeDaFase);
        }

        private Dictionary<string, Phase> CarregarFasesExistentes()
        {
            return new FilteredElementCollector(_documento)
                .OfClass(typeof(Phase))
                .Cast<Phase>()
                .ToDictionary(fase => fase.Name, fase => fase);
        }

        //private Phase CriarFase(string nomeDaFase, Dictionary<string, Phase> fasesExistentes)
        //{
        //    PhaseArray fasesAtuais = _documento.Phases;
        //    _documento.Phases.get_Item(fasesAtuais.Size - 1);
        //    Phase novaFase = _documento.GetElement(_documento.Phases.get_Item(fasesAtuais.Size - 1).Id) as Phase;
        //    var novaFaseInserida = _documento.Phases.get_Item(0); // placeholder
        //    Phase faseCriada = Phase.Create(_documento, nomeDaFase);

        //    fasesExistentes[nomeDaFase] = faseCriada;
        //    return faseCriada;
        //}
    }
}
