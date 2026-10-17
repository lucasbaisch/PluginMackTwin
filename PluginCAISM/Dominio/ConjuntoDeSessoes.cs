using System;
using System.Collections.Generic;
using System.Linq;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Container raiz de todas as sessões carregadas e processadas pelo plugin.
    /// Fornece métodos de filtro e consulta agregada sobre o conjunto completo de sessões.
    /// </summary>
    /// <remarks>
    /// Construído pelo <see cref="Processamento.ConstruturDeSessoes"/> após o parsing dos três inputs.
    /// Os filtros previstos pelo Requisito 06 são cenário, tipo de evento ocular e tendência
    /// pupilar; os dois últimos são aplicados sobre os eventos, não sobre as sessões.
    /// </remarks>
    public class ConjuntoDeSessoes
    {
        /// <summary>Lista somente-leitura de todas as sessões carregadas.</summary>
        public IReadOnlyList<Sessao> Todas { get; }

        /// <summary>
        /// Inicializa o conjunto com a lista de sessões fornecida.
        /// </summary>
        /// <param name="sessoes">Sessões construídas pelo <see cref="Processamento.ConstruturDeSessoes"/>.</param>
        public ConjuntoDeSessoes(IReadOnlyList<Sessao> sessoes)
        {
            Todas = sessoes;
        }

        /// <summary>
        /// Retorna a lista de nomes de cenários únicos presentes em todas as sessões,
        /// ordenados alfabeticamente.
        /// </summary>
        /// <remarks>
        /// O cenário é o <see cref="Sessao.RotuloDeCenario"/> — ambiente VR + período do dia —, e
        /// não o registro individual de participante e assento. É por isso que um arquivo com 26
        /// participantes × 2 assentos × 2 períodos devolve 2 cenários, e não 104.
        /// </remarks>
        public IEnumerable<string> ObterTodosOsCenarios()
        {
            return Todas.Select(sessao => sessao.RotuloDeCenario).Distinct().OrderBy(nome => nome);
        }

        /// <summary>
        /// Retorna as sessões cujo cenário corresponde ao nome fornecido — todas elas, de todos os
        /// participantes e assentos daquele ambiente e período.
        /// </summary>
        /// <param name="nomeDoCenario">
        /// Nome exato do cenário (<see cref="Sessao.RotuloDeCenario"/>).
        /// Exemplo: "Cenário_Cenario1_Dia".
        /// </param>
        public IEnumerable<Sessao> FiltrarPorCenario(string nomeDoCenario)
        {
            return Todas.Where(sessao => sessao.RotuloDeCenario == nomeDoCenario);
        }

        /// <summary>
        /// Retorna todos os eventos oculares de um tipo específico, opcionalmente filtrados por cenário.
        /// </summary>
        /// <param name="tipoDeEvento">Tipo de evento: Fixacao ou Saccade.</param>
        /// <param name="nomeDoCenario">Se fornecido, filtra apenas as sessões do cenário especificado.</param>
        public IEnumerable<EventoDeMovimentoOcular> ObterTodosOsEventos(
            TipoDeEventoOcular tipoDeEvento,
            string nomeDoCenario = null)
        {
            var sessoesFiltradas = nomeDoCenario != null
                ? FiltrarPorCenario(nomeDoCenario)
                : Todas;

            if (tipoDeEvento == TipoDeEventoOcular.Fixacao)
                return sessoesFiltradas.SelectMany(sessao => sessao.Fixacoes);

            return sessoesFiltradas.SelectMany(sessao => sessao.Saccades);
        }
    }
}
