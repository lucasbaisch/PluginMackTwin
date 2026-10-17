using System.Collections.Generic;
using System.Linq;

namespace MackTwin.Dominio
{
    /// <summary>
    /// Uma sala do CCT e os sensores instalados nela.
    /// </summary>
    /// <remarks>
    /// Nasce das planilhas (todo HOSTNAME identificável cria ou alimenta um ambiente) e, quando
    /// há modelo aberto, é casada com um Room pelo código (<see cref="CodigoDeSala"/>). O vínculo
    /// com o modelo é guardado como o valor numérico do <c>ElementId</c>, e não como o tipo do
    /// Revit, para que o domínio continue verificável fora do Revit.
    /// </remarks>
    public sealed class Ambiente
    {
        /// <summary>Código normalizado da sala (ex.: LAB001). Chave do ambiente.</summary>
        public string Codigo { get; init; } = string.Empty;

        /// <summary>Sensores da sala, na ordem de HOSTNAME.</summary>
        public List<Sensor> Sensores { get; } = new();

        /// <summary>Valor do ElementId do Room casado no modelo, ou null se não casou.</summary>
        public long? IdDoElementoNoModelo { get; set; }

        /// <summary>Nome do Room no modelo (ex.: "Laboratório de Informática"), se casou.</summary>
        public string? NomeNoModelo { get; set; }

        /// <summary>Número do Room no modelo, como digitado pelo projetista, se casou.</summary>
        public string? NumeroNoModelo { get; set; }

        /// <summary>Se há um Room correspondente no modelo aberto.</summary>
        public bool CasadoComOModelo => IdDoElementoNoModelo.HasValue;

        /// <summary>Todas as leituras de todos os sensores, em ordem cronológica.</summary>
        public IEnumerable<Leitura> Leituras => Sensores
            .SelectMany(sensor => sensor.Leituras)
            .OrderBy(leitura => leitura.Timestamp);

        /// <summary>A leitura mais recente entre todos os sensores, ou null.</summary>
        public Leitura? UltimaLeitura => Sensores
            .Select(sensor => sensor.UltimaLeitura)
            .Where(leitura => leitura != null)
            .OrderByDescending(leitura => leitura!.Timestamp)
            .FirstOrDefault();

        /// <summary>Título para a interface: código e, se houver, o nome no modelo.</summary>
        public string Titulo => string.IsNullOrWhiteSpace(NomeNoModelo)
            ? Codigo
            : $"{Codigo} — {NomeNoModelo}";

        public override string ToString() => Titulo;
    }
}
