using System;
using System.Collections.Generic;
using System.Linq;

namespace MackTwin.Dominio
{
    /// <summary>
    /// O resultado de um ciclo de atualização: todas as salas e sensores reconhecidos nas
    /// planilhas, mais os sensores que não puderam ser associados a sala alguma.
    /// </summary>
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

        /// <summary>
        /// Por URL de planilha cadastrada, os códigos de sala que as suas leituras alimentaram
        /// neste ciclo — é o que permite dizer "esta planilha é da sala LAB001" sem cadastro.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> SalasPorPlanilha { get; set; } =
            new Dictionary<string, IReadOnlyCollection<string>>();

        /// <summary>Total de leituras em todas as salas e sensores sem sala.</summary>
        public int TotalDeLeituras =>
            Ambientes.Sum(ambiente => ambiente.Sensores.Sum(sensor => sensor.Leituras.Count))
            + SensoresSemSala.Sum(sensor => sensor.Leituras.Count);

        /// <summary>Total de sensores reconhecidos, com ou sem sala.</summary>
        public int TotalDeSensores =>
            Ambientes.Sum(ambiente => ambiente.Sensores.Count) + SensoresSemSala.Count;

        /// <summary>Localiza a sala pelo código, em qualquer grafia aceita por <see cref="CodigoDeSala"/>.</summary>
        public Ambiente? Localizar(string? codigo)
        {
            string normalizado = CodigoDeSala.Normalizar(codigo);

            return normalizado.Length == 0
                ? null
                : Ambientes.FirstOrDefault(ambiente => ambiente.Codigo == normalizado);
        }
    }
}
