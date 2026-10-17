namespace MackTwin.Dominio
{
    /// <summary>
    /// As partes do HOSTNAME de um sensor, já separadas: <c>MMCCTLAB001-001</c> →
    /// instituição MM, unidade CCT, sala LAB001, dispositivo 001.
    /// </summary>
    /// <remarks>
    /// Produzida por <see cref="Parsing.AnalisadorDeHostname"/>. <see cref="CodigoDaSala"/> já
    /// vem normalizado (<see cref="CodigoDeSala.Normalizar"/>); <see cref="NumeroDoDispositivo"/>
    /// fica como texto para preservar os zeros à esquerda que o firmware grava.
    /// </remarks>
    public sealed class IdentificacaoDeSensor
    {
        /// <summary>O HOSTNAME completo, como veio da planilha (aparado).</summary>
        public string Hostname { get; init; } = string.Empty;

        /// <summary>Sigla da instituição (ex.: MM = Mackenzie).</summary>
        public string Instituicao { get; init; } = string.Empty;

        /// <summary>Sigla da unidade (ex.: CCT).</summary>
        public string Unidade { get; init; } = string.Empty;

        /// <summary>Código da sala, normalizado (ex.: LAB001).</summary>
        public string CodigoDaSala { get; init; } = string.Empty;

        /// <summary>Número do dispositivo dentro da sala, como texto (ex.: "001").</summary>
        public string NumeroDoDispositivo { get; init; } = string.Empty;

        public override string ToString() => $"{Unidade} / {CodigoDaSala} / dispositivo {NumeroDoDispositivo}";
    }
}
