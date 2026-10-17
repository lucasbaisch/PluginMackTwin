namespace PluginCAISM.Dominio
{
    /// <summary>
    /// O que fazer quando o cenário já tem uma geração anterior no documento Revit.
    /// </summary>
    /// <remarks>
    /// A pergunta existe porque gerar de novo não cria só uma vista: cada geração insere um
    /// conjunto novo de hitpoints no MODELO, e uma vista 3D mostra tudo que está no modelo. Sem
    /// esta decisão, a segunda geração deixaria duas esferas empilhadas em cada coordenada — e,
    /// se a classificação tivesse mudado entre as duas, as duas cores no mesmo ponto.
    /// </remarks>
    public enum DecisaoDeGeracaoDeVista
    {
        /// <summary>
        /// Apaga a vista anterior do cenário e os hitpoints dela antes de gerar. É o padrão: no
        /// uso corrente, regerar quase sempre significa corrigir, não acumular.
        /// </summary>
        Atualizar,

        /// <summary>
        /// Mantém tudo e cria uma versão nova. As esferas anteriores permanecem no modelo,
        /// sobrepostas às novas.
        /// </summary>
        NovaVersao,

        /// <summary>Não gera nada.</summary>
        Cancelar
    }
}
