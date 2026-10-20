namespace MackTwin.Dominio
{
    /// <summary>
    /// Por qual grandeza uma sala é colorida: o estado combinado, só a temperatura ou só a
    /// umidade.
    /// </summary>
    /// <remarks>
    /// O painel e a vista 3D usam o estado geral (pior caso). A folha de impressão mostra as
    /// duas grandezas lado a lado, cada uma com a sua legenda — por isso cada uma tem, no Revit,
    /// o seu parâmetro de Room e o seu esquema de cores.
    /// </remarks>
    public enum GrandezaDeConforto
    {
        /// <summary>Pior caso entre temperatura e umidade.</summary>
        Geral,

        /// <summary>Só a temperatura.</summary>
        Temperatura,

        /// <summary>Só a umidade relativa.</summary>
        Umidade,
    }
}
