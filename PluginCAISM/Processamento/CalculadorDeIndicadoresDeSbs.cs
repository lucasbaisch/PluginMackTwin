using System;
using System.Collections.Generic;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Processamento
{
    /// <summary>
    /// Indicadores de Sick Building Syndrome de um participante, derivados das questões 37 e 38.
    /// </summary>
    public class IndicadoresDeSbs
    {
        /// <summary>Identificação do participante.</summary>
        public string IdentificacaoManual { get; init; }

        /// <summary>
        /// Severidade: soma ordinal das respostas de frequência da Q37, na faixa 0–56.
        /// </summary>
        public int Severidade { get; init; }

        /// <summary>Número de sintomas que o participante atribui ao trabalho (Q38).</summary>
        public int SintomasAtribuidosAoTrabalho { get; init; }

        /// <summary>
        /// Número de sintomas com resposta efetiva na Q38 — afirmativa ou negativa. Serve de
        /// denominador do percentual; "-" e células vazias não contam.
        /// </summary>
        public int SintomasComRespostaNaQ38 { get; init; }

        /// <summary>
        /// Percentual de sintomas atribuídos ao trabalho, em [0, 1].
        /// NaN quando não há nenhuma resposta efetiva na Q38.
        /// </summary>
        public double PercentualAtribuidoAoTrabalho =>
            SintomasComRespostaNaQ38 > 0
                ? (double)SintomasAtribuidosAoTrabalho / SintomasComRespostaNaQ38
                : double.NaN;
    }

    /// <summary>
    /// Calcula a severidade de Sick Building Syndrome (Q37) e a atribuição dos sintomas ao
    /// trabalho (Q38) a partir das respostas de formulário.
    /// </summary>
    /// <remarks>
    /// Calculadora pura: não lê arquivos e não depende da interface. A escala ordinal, as
    /// políticas para respostas ausentes e os termos da Q38 estão em
    /// <see cref="EscalaOrdinalDoSbs"/> e <see cref="AtribuicaoDoSintomaAoTrabalho"/>.
    /// </remarks>
    public class CalculadorDeIndicadoresDeSbs
    {
        /// <summary>
        /// Calcula os indicadores de todos os participantes informados.
        /// </summary>
        public List<IndicadoresDeSbs> Calcular(
            IEnumerable<RespostasDeFormularioPorParticipante> respostas)
        {
            return respostas
                .Where(r => !string.IsNullOrWhiteSpace(r.IdentificacaoManual))
                .Select(CalcularDeUmParticipante)
                .ToList();
        }

        private IndicadoresDeSbs CalcularDeUmParticipante(RespostasDeFormularioPorParticipante respostas)
        {
            var itensQ37 = ComposicaoDeEscoresDeFormulario.ItensDoSbsQ37;
            var itensQ38 = ComposicaoDeEscoresDeFormulario.ItensDoSbsQ38;

            int severidade = 0;
            int atribuidos = 0;
            int comResposta = 0;

            for (int indice = 0; indice < itensQ37.Length; indice++)
            {
                severidade += MapearFrequencia(LerResposta(respostas, itensQ37[indice]));

                if (indice >= itensQ38.Length)
                    continue;

                var atribuicao = ClassificarAtribuicao(LerResposta(respostas, itensQ38[indice]));

                if (atribuicao == null)
                    continue;

                comResposta++;
                if (atribuicao.Value)
                    atribuidos++;
            }

            return new IndicadoresDeSbs
            {
                IdentificacaoManual         = respostas.IdentificacaoManual,
                Severidade                  = severidade,
                SintomasAtribuidosAoTrabalho = atribuidos,
                SintomasComRespostaNaQ38    = comResposta,
            };
        }

        private static string LerResposta(
            RespostasDeFormularioPorParticipante respostas, string trechoDaColuna)
        {
            LocalizadorDeColunaDeFormulario.TentarLocalizar(
                respostas.RespostasBrutas, new[] { trechoDaColuna }, out _, out string valor);

            return valor;
        }

        /// <summary>
        /// Converte uma resposta de frequência da Q37 em valor ordinal.
        /// </summary>
        /// <remarks>
        /// A normalização (trim + maiúsculas) resolve as variações de caixa do arquivo real.
        /// Respostas fora da escala base que ainda assim indicam sintoma presente têm valor
        /// próprio; ausentes e ambíguas caem na política configurada.
        /// </remarks>
        public static int MapearFrequencia(string resposta)
        {
            string normalizada = Normalizar(resposta);

            if (string.IsNullOrEmpty(normalizada)
                || EscalaOrdinalDoSbs.RespostasAmbiguas.Contains(normalizada))
            {
                return EscalaOrdinalDoSbs.ValorParaRespostaAusenteOuAmbigua;
            }

            foreach (var (textoForaDaEscala, valorForaDaEscala) in EscalaOrdinalDoSbs.RespostasForaDaEscalaBase)
            {
                if (normalizada == Normalizar(textoForaDaEscala))
                    return valorForaDaEscala;
            }

            // "MUITO FREQUENTE" contém "FREQUENTE": a comparação precisa ser por igualdade, não
            // por conteúdo, senão a resposta de maior severidade seria lida como a menor.
            foreach (var (texto, valor) in EscalaOrdinalDoSbs.MapaBase)
            {
                if (normalizada == Normalizar(texto))
                    return valor;
            }

            return EscalaOrdinalDoSbs.ValorParaRespostaAusenteOuAmbigua;
        }

        /// <summary>
        /// Classifica uma resposta da Q38.
        /// </summary>
        /// <returns>
        /// true quando o participante atribui o sintoma ao trabalho, false quando nega, e null
        /// quando não há resposta efetiva ("-", vazio) — caso em que o item não entra no
        /// denominador do percentual.
        /// </returns>
        public static bool? ClassificarAtribuicao(string resposta)
        {
            string normalizada = Normalizar(resposta);

            if (string.IsNullOrEmpty(normalizada))
                return null;

            if (!normalizada.Contains(AtribuicaoDoSintomaAoTrabalho.TermoDeResposta))
                return null;

            // Contém "DECORRÊNCIA" E NÃO contém "NÃO". Testar por "É EM DECORRÊNCIA" seria errado:
            // essa expressão é substring de "NÃO É EM DECORRÊNCIA".
            return !normalizada.Contains(AtribuicaoDoSintomaAoTrabalho.TermoDeNegacao);
        }

        private static string Normalizar(string texto)
        {
            return string.IsNullOrWhiteSpace(texto)
                ? string.Empty
                : texto.Trim().ToUpperInvariant();
        }
    }
}
