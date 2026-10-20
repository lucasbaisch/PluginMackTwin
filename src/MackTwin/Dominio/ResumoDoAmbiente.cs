namespace MackTwin.Dominio
{
    /// <summary>O estado de uma grandeza (temperatura ou umidade) numa janela de tempo.</summary>
    public sealed class EstatisticaDaGrandeza
    {
        /// <summary>Último valor medido na janela, ou NaN.</summary>
        public double Ultimo { get; init; } = double.NaN;

        /// <summary>Quantas leituras válidas há na janela.</summary>
        public int Quantidade { get; init; }

        /// <summary>Classificação do ÚLTIMO valor — é o estado atual que a cor comunica.</summary>
        public ClassificacaoDeConforto Classificacao { get; init; } = ClassificacaoDeConforto.SemDados;

        /// <summary>Se há ao menos um valor válido.</summary>
        public bool TemDados => Quantidade > 0;
    }

    /// <summary>
    /// O que o painel diz de uma sala: última leitura, estado de cada grandeza na janela e a
    /// classificação geral que pinta o cartão e o Room.
    /// </summary>
    public sealed class ResumoDoAmbiente
    {
        /// <summary>Leitura mais recente da sala (de qualquer época), ou null se não há leituras.</summary>
        public Leitura? UltimaLeitura { get; init; }

        /// <summary>Temperatura na janela.</summary>
        public EstatisticaDaGrandeza Temperatura { get; init; } = new();

        /// <summary>Umidade na janela.</summary>
        public EstatisticaDaGrandeza Umidade { get; init; } = new();

        /// <summary>
        /// Pior caso entre as grandezas, pela última leitura: é o que define a cor da sala.
        /// </summary>
        public ClassificacaoDeConforto ClassificacaoGeral { get; init; } = ClassificacaoDeConforto.SemDados;

        /// <summary>Total de leituras na janela.</summary>
        public int TotalDeLeituras { get; init; }

        /// <summary>A classificação pedida: geral, da temperatura ou da umidade.</summary>
        public ClassificacaoDeConforto ClassificacaoDe(GrandezaDeConforto grandeza)
        {
            return grandeza switch
            {
                GrandezaDeConforto.Temperatura => Temperatura.Classificacao,
                GrandezaDeConforto.Umidade => Umidade.Classificacao,
                _ => ClassificacaoGeral,
            };
        }
    }
}
