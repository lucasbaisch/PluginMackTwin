using System.Collections.Generic;

namespace MackTwin.Dominio
{
    /// <summary>
    /// Um dispositivo físico (um HOSTNAME) e tudo o que ele mediu.
    /// </summary>
    public sealed class Sensor
    {
        /// <summary>HOSTNAME, chave do sensor (ex.: MMCCTLAB001-001).</summary>
        public string Hostname { get; init; } = string.Empty;

        /// <summary>Último endereço físico visto nas leituras, ou vazio.</summary>
        public string EnderecoMac { get; set; } = string.Empty;

        /// <summary>
        /// Número do dispositivo dentro da sala, como texto para preservar os zeros à esquerda
        /// ("001"), ou null quando o HOSTNAME não segue o padrão.
        /// </summary>
        public string? NumeroDoDispositivo { get; init; }

        /// <summary>Leituras em ordem cronológica crescente.</summary>
        public List<Leitura> Leituras { get; } = new();

        /// <summary>
        /// Código normalizado da sala a que o sensor foi atribuído (pela convenção do HOSTNAME
        /// ou por cadastro), ou vazio se não identificado.
        /// </summary>
        public string CodigoDaSala { get; set; } = string.Empty;

        /// <summary>A leitura mais recente, ou null se não há nenhuma.</summary>
        public Leitura? UltimaLeitura => Leituras.Count == 0 ? null : Leituras[Leituras.Count - 1];

        /// <summary>Rótulo curto para a interface: o número do dispositivo, ou o HOSTNAME inteiro.</summary>
        public string Rotulo => NumeroDoDispositivo == null ? Hostname : $"Dispositivo {NumeroDoDispositivo}";
    }
}
