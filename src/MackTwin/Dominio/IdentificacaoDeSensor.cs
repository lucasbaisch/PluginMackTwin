namespace MackTwin.Dominio
{
    /// <summary>
    /// O que o painel usa do HOSTNAME de um sensor: <c>MMCCTLAB001-001</c> → sala LAB001,
    /// dispositivo 001.
    /// </summary>
    /// <remarks>
    /// Produzida por <see cref="Parsing.AnalisadorDeHostname"/>, que valida o nome inteiro
    /// (instituição e unidade inclusive) mas só guarda o que é consumido. <see cref="CodigoDaSala"/>
    /// já vem normalizado (<see cref="CodigoDeSala.Normalizar"/>); <see cref="NumeroDoDispositivo"/>
    /// fica como texto para preservar os zeros à esquerda que o firmware grava.
    /// </remarks>
    public sealed class IdentificacaoDeSensor
    {
        /// <summary>O HOSTNAME completo, como veio da planilha (aparado).</summary>
        public string Hostname { get; init; } = string.Empty;

        /// <summary>Código da sala, normalizado (ex.: LAB001).</summary>
        public string CodigoDaSala { get; init; } = string.Empty;

        /// <summary>Número do dispositivo dentro da sala, como texto (ex.: "001").</summary>
        public string NumeroDoDispositivo { get; init; } = string.Empty;

        public override string ToString() => $"{CodigoDaSala} / dispositivo {NumeroDoDispositivo}";
    }
}
