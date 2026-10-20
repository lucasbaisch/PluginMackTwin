using System;
using System.Collections.Generic;

namespace MackTwin.Dominio
{
    /// <summary>
    /// O resultado de um ciclo de atualização: todas as salas e sensores reconhecidos nas
    /// planilhas, mais os sensores que não puderam ser associados a sala alguma.
    /// </summary>
    /// <remarks>
    /// Os totais são calculados uma vez por quem monta o conjunto
    /// (<see cref="Processamento.AgrupadorDeAmbientes"/>) e ficam fixos: o conjunto é imutável
    /// depois de montado, e o status, o diagnóstico e os avisos os leem várias vezes por ciclo.
    /// </remarks>
    public sealed class ConjuntoDeAmbientes
    {
        /// <summary>Salas em ordem de código.</summary>
        public IReadOnlyList<Ambiente> Ambientes { get; init; } = Array.Empty<Ambiente>();

        /// <summary>Sensores cujo HOSTNAME não segue o padrão e, por isso, não têm sala.</summary>
        public IReadOnlyList<Sensor> SensoresSemSala { get; init; } = Array.Empty<Sensor>();

        /// <summary>Quando o conjunto foi montado (hora local desta máquina).</summary>
        public DateTime AtualizadoEm { get; init; } = DateTime.Now;

        /// <summary>Avisos acumulados no ciclo: downloads, linhas descartadas, sensores sem sala.</summary>
        public IReadOnlyList<string> Avisos { get; init; } = Array.Empty<string>();

        /// <summary>Quantas planilhas cadastradas falharam ao baixar neste ciclo.</summary>
        public int PlanilhasComFalha { get; set; }

        /// <summary>Total de leituras em todas as salas e sensores sem sala.</summary>
        public int TotalDeLeituras { get; init; }

        /// <summary>Total de sensores reconhecidos, com ou sem sala.</summary>
        public int TotalDeSensores { get; init; }

        /// <summary>
        /// Por URL de planilha cadastrada, os códigos de sala que as suas leituras alimentaram
        /// neste ciclo — é o que permite dizer "esta planilha é da sala LAB001" sem cadastro.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> SalasPorPlanilha { get; set; } =
            new Dictionary<string, IReadOnlyCollection<string>>();
    }
}
