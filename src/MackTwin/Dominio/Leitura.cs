using System;

namespace MackTwin.Dominio
{
    /// <summary>
    /// Uma linha da planilha: o que um sensor mediu num instante.
    /// </summary>
    /// <remarks>
    /// Medições ausentes ou ilegíveis chegam como <see cref="double.NaN"/>, e não como null: é a
    /// convenção que o LiveCharts entende como lacuna na linha, e evita o tipo anulável que a
    /// versão 0.9.7 da biblioteca não sabe plotar. O carimbo não tem fuso — é a hora local do
    /// dispositivo, e é assim que se exibe.
    /// </remarks>
    public sealed class Leitura
    {
        /// <summary>Instante da medição (hora local do dispositivo, sem fuso).</summary>
        public DateTime Timestamp { get; init; }

        /// <summary>HOSTNAME do sensor que mediu.</summary>
        public string Hostname { get; init; } = string.Empty;

        /// <summary>Endereço físico do sensor, se a planilha o traz.</summary>
        public string EnderecoMac { get; init; } = string.Empty;

        /// <summary>Temperatura do ar em °C, ou NaN se ausente.</summary>
        public double Temperatura { get; init; } = double.NaN;

        /// <summary>Umidade relativa em %, ou NaN se ausente.</summary>
        public double Umidade { get; init; } = double.NaN;

        /// <summary>Se há temperatura válida.</summary>
        public bool TemTemperatura => !double.IsNaN(Temperatura);

        /// <summary>Se há umidade válida.</summary>
        public bool TemUmidade => !double.IsNaN(Umidade);
    }
}
