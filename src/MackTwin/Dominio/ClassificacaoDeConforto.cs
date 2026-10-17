namespace MackTwin.Dominio
{
    /// <summary>
    /// Posição de uma leitura — ou de um ambiente — em relação à faixa de conforto.
    /// </summary>
    /// <remarks>
    /// Abaixo e acima são estados distintos, e não um "fora da faixa" único, porque pedem
    /// providências opostas (aquecer/umidificar contra resfriar/desumidificar) e recebem cores
    /// diferentes. <see cref="SemDados"/> cobre a sala sem sensor ou sem leitura válida.
    /// </remarks>
    public enum ClassificacaoDeConforto
    {
        /// <summary>Nenhuma leitura válida para classificar.</summary>
        SemDados = 0,

        /// <summary>Dentro da faixa, extremos inclusive.</summary>
        Confortavel,

        /// <summary>Abaixo do mínimo da faixa (frio ou seco).</summary>
        AbaixoDaFaixa,

        /// <summary>Acima do máximo da faixa (quente ou úmido).</summary>
        AcimaDaFaixa,
    }
}
