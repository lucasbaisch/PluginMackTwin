using PluginCAISM.Configuracao;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Faixas de classificação do escore total do IEEE, conforme a §5.5.2 do TextoFinal.
    /// </summary>
    public enum FaixaDeEstresseIeee
    {
        /// <summary>Estresse baixo: escore de 34 a 68.</summary>
        Baixo,

        /// <summary>Estresse intermediário: escore de 69 a 102.</summary>
        Intermediario,

        /// <summary>Estresse alto: escore de 103 a 170.</summary>
        Alto
    }

    /// <summary>
    /// Classifica um escore total do IEEE em uma das três faixas de
    /// <see cref="LimiaresDeClassificacaoIeee"/>.
    /// </summary>
    /// <remarks>
    /// Regra pura, usada tanto na tabela-síntese de cada participante quanto no segundo filtro da
    /// aba de dados do grupo.
    /// </remarks>
    public static class ClassificadorDeFaixaDeEstresseIeee
    {
        /// <summary>
        /// Classifica o escore informado.
        /// </summary>
        /// <param name="escore">Escore total do IEEE.</param>
        /// <returns>
        /// A faixa correspondente, ou null quando o escore é NaN (participante sem escore
        /// calculado).
        /// </returns>
        public static FaixaDeEstresseIeee? Classificar(double escore)
        {
            if (double.IsNaN(escore))
                return null;

            if (escore <= LimiaresDeClassificacaoIeee.FimDaFaixaBaixa)
                return FaixaDeEstresseIeee.Baixo;

            if (escore <= LimiaresDeClassificacaoIeee.FimDaFaixaIntermediaria)
                return FaixaDeEstresseIeee.Intermediario;

            return FaixaDeEstresseIeee.Alto;
        }

        /// <summary>
        /// Legenda das três faixas, montada a partir dos cortes configurados, para ser exibida
        /// como referência ao lado do escore.
        /// </summary>
        /// <remarks>
        /// Derivada de <see cref="LimiaresDeClassificacaoIeee"/> em vez de escrita à mão: se um
        /// corte mudar, a legenda acompanha.
        /// </remarks>
        public static string ObterLegendaDasFaixas()
        {
            double minimo = LimiaresDeClassificacaoIeee.EscoreMinimo;
            double fimBaixa = LimiaresDeClassificacaoIeee.FimDaFaixaBaixa;
            double fimIntermediaria = LimiaresDeClassificacaoIeee.FimDaFaixaIntermediaria;
            double maximo = LimiaresDeClassificacaoIeee.EscoreMaximo;

            return $"Referência: baixo {minimo:N0}–{fimBaixa:N0} · " +
                   $"intermediário {fimBaixa + 1:N0}–{fimIntermediaria:N0} · " +
                   $"alto {fimIntermediaria + 1:N0}–{maximo:N0}";
        }

        /// <summary>Nome curto da faixa, sem o intervalo. Ex.: "estresse baixo".</summary>
        public static string ObterNomeCurto(FaixaDeEstresseIeee faixa)
        {
            switch (faixa)
            {
                case FaixaDeEstresseIeee.Baixo:         return "estresse baixo";
                case FaixaDeEstresseIeee.Intermediario: return "estresse intermediário";
                case FaixaDeEstresseIeee.Alto:
                default:                                 return "estresse alto";
            }
        }

        /// <summary>Rótulo legível de uma faixa, usado nos seletores e nas tabelas.</summary>
        public static string ObterRotulo(FaixaDeEstresseIeee faixa)
        {
            switch (faixa)
            {
                case FaixaDeEstresseIeee.Baixo:
                    return LimiaresDeClassificacaoIeee.RotuloDaFaixaBaixa;

                case FaixaDeEstresseIeee.Intermediario:
                    return LimiaresDeClassificacaoIeee.RotuloDaFaixaIntermediaria;

                case FaixaDeEstresseIeee.Alto:
                default:
                    return LimiaresDeClassificacaoIeee.RotuloDaFaixaAlta;
            }
        }
    }
}
