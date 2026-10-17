namespace PluginCAISM.Configuracao
{
    /// <summary>
    /// Identificação da pesquisa impressa no cabeçalho dos arquivos exportados.
    /// </summary>
    /// <remarks>
    /// Os valores vêm da folha de rosto do TextoFinal. Ficam aqui, e não no exportador, porque são
    /// dado da pesquisa e não da apresentação — trocar de tese não deve exigir mexer no código que
    /// desenha o PDF.
    /// </remarks>
    public static class CabecalhoDaExportacao
    {
        /// <summary>Instituição, primeira linha do cabeçalho.</summary>
        public const string Universidade = "Universidade Estadual de Campinas";

        /// <summary>Unidade e programa, segunda linha.</summary>
        public const string Faculdade =
            "Faculdade de Engenharia Civil, Arquitetura e Urbanismo — " +
            "Doutorado em Arquitetura, Tecnologia e Cidade";

        /// <summary>Título da tese.</summary>
        public const string TituloDaTese =
            "Biointerfaces Inteligentes Aplicadas ao Monitoramento e à Comunicação Homeodinâmica " +
            "entre a Ambiência Hospitalar e o Organismo Humano";

        /// <summary>Autor.</summary>
        public const string Autor = "Lucas Figueiredo Baisch";

        /// <summary>Orientação.</summary>
        public const string Orientacao = "Orientadora: Profa. Dra. Rachel Zuanon Dias";

        /// <summary>Nome do artefato que produziu o arquivo.</summary>
        public const string NomeDoPlugin = "Plug-in de Potencial Homeodinâmico";

        /// <summary>
        /// Nota metodológica impressa abaixo da tabela, para que o arquivo não circule sem a
        /// ressalva que o Quadro 41 faz sobre o significado de "Neutro".
        /// </summary>
        public const string NotaDeRodape =
            "PH_e = tanh(-nu_e / sigma_dD), no intervalo (-1, +1): positivo indica potencial " +
            "restaurador, negativo indica potencial estressor. A classificação não deriva do " +
            "índice, e sim da posição do intervalo de confiança de 85% de nu_e em relação a zero " +
            "(Equação 4). Neutro sinaliza insuficiência de evidência, não ausência de efeito: um " +
            "elemento de |PH_e| alto pode ser neutro quando estimado com poucas observações.";
    }
}
