using System;
using System.Collections.Generic;
using MackTwin.Configuracao;

namespace MackTwin.Exportacao
{
    /// <summary>Os instantes que viram quadros do vídeo do último dia.</summary>
    /// <remarks>
    /// Ancorados na hora cheia: o último quadro é a hora cheia mais recente (14:00 se agora são
    /// 14:37), e os anteriores recuam de hora em hora. Horas cheias são o que o olho lê no
    /// carimbo de cada quadro; um passo contado de 14:37 para trás daria 13:37, 12:37… e o
    /// vídeo pareceria desalinhado do relógio.
    /// </remarks>
    public static class LinhaDoTempoDoVideo
    {
        /// <summary>Passo entre quadros.</summary>
        public static readonly TimeSpan Passo = TimeSpan.FromHours(ConfiguracaoDeExportacao.PassoEmHoras);

        /// <summary>Quão velha uma leitura pode ser e ainda representar a sala no quadro.</summary>
        public static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(ConfiguracaoDeExportacao.ToleranciaDoQuadroEmMinutos);

        /// <summary>
        /// Os instantes dos quadros, do mais antigo ao mais recente, terminando na hora cheia que
        /// precede (ou coincide com) <paramref name="agora"/>.
        /// </summary>
        public static IReadOnlyList<DateTime> Instantes(DateTime agora, int quantidade = ConfiguracaoDeExportacao.QuantidadeDeQuadros)
        {
            var ultimo = new DateTime(agora.Year, agora.Month, agora.Day, agora.Hour, 0, 0, agora.Kind);
            var instantes = new List<DateTime>(quantidade);

            for (int indice = quantidade - 1; indice >= 0; indice--)
                instantes.Add(ultimo - TimeSpan.FromTicks(Passo.Ticks * indice));

            return instantes;
        }
    }
}
