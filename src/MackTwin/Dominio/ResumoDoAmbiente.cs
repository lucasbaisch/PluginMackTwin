using System;

namespace MackTwin.Dominio
{
    /// <summary>Estatística de uma grandeza (temperatura ou umidade) numa janela de tempo.</summary>
    public sealed class EstatisticaDaGrandeza
    {
        /// <summary>Último valor medido, ou NaN.</summary>
        public double Ultimo { get; init; } = double.NaN;

        /// <summary>Média dos valores válidos da janela, ou NaN se não há nenhum.</summary>
        public double Media { get; init; } = double.NaN;

        /// <summary>Mínimo da janela, ou NaN.</summary>
        public double Minimo { get; init; } = double.NaN;

        /// <summary>Máximo da janela, ou NaN.</summary>
        public double Maximo { get; init; } = double.NaN;

        /// <summary>Quantas leituras válidas entraram na estatística.</summary>
        public int Quantidade { get; init; }

        /// <summary>Classificação do ÚLTIMO valor — é o estado atual que a cor comunica.</summary>
        public ClassificacaoDeConforto Classificacao { get; init; } = ClassificacaoDeConforto.SemDados;

        /// <summary>Se há ao menos um valor válido.</summary>
        public bool TemDados => Quantidade > 0;
    }

    /// <summary>
    /// O que o painel diz de uma sala: última leitura, estatísticas da janela e a classificação
    /// geral que pinta o cartão e o Room.
    /// </summary>
    public sealed class ResumoDoAmbiente
    {
        /// <summary>Código da sala resumida.</summary>
        public string Codigo { get; init; } = string.Empty;

        /// <summary>Leitura mais recente, ou null se a sala não tem leituras.</summary>
        public Leitura? UltimaLeitura { get; init; }

        /// <summary>Estatística de temperatura na janela.</summary>
        public EstatisticaDaGrandeza Temperatura { get; init; } = new();

        /// <summary>Estatística de umidade na janela.</summary>
        public EstatisticaDaGrandeza Umidade { get; init; } = new();

        /// <summary>
        /// Pior caso entre as grandezas, pela última leitura: é o que define a cor da sala.
        /// </summary>
        public ClassificacaoDeConforto ClassificacaoGeral { get; init; } = ClassificacaoDeConforto.SemDados;

        /// <summary>Total de leituras consideradas (todas as grandezas, após o filtro de janela).</summary>
        public int TotalDeLeituras { get; init; }

        /// <summary>Início da janela considerada, ou null se vazia.</summary>
        public DateTime? Inicio { get; init; }

        /// <summary>Fim da janela considerada (a última leitura), ou null se vazia.</summary>
        public DateTime? Fim { get; init; }

        /// <summary>Quantos sensores a sala tem.</summary>
        public int QuantidadeDeSensores { get; init; }
    }
}
