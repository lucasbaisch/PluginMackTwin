using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;
using PluginCAISM.Parsing;

namespace PluginCAISM.Processamento
{
    /// <summary>
    /// Deriva os escores dos formulários (Arquivo 4) a partir das respostas brutas lidas pelo
    /// <see cref="AnalisadorExcelFormularios"/>.
    /// </summary>
    /// <remarks>
    /// Calculadora pura: não lê arquivos, não altera estado e não depende da interface.
    ///
    /// Todos os escores dependem de mapeamentos declarados em
    /// <see cref="ComposicaoDeEscoresDeFormulario"/> e
    /// <see cref="MapeamentoDeColunasDeFormulario"/>. Enquanto esses mapeamentos estiverem
    /// vazios, os escores saem como double.NaN — deliberadamente, para que a falta de
    /// configuração apareça na interface em vez de virar um zero plausível.
    ///
    /// O escore total do IEEE é LIDO da coluna TOTAL da planilha, não somado aqui; a severidade
    /// do SBS é calculada por <see cref="CalculadorDeIndicadoresDeSbs"/>, que aplica a escala
    /// ordinal aos rótulos de frequência.
    /// </remarks>
    public class CalculadorDeEscoresDeFormulario
    {
        /// <summary>
        /// Calcula os escores de todas as respostas fornecidas.
        /// </summary>
        /// <param name="respostasBrutas">Linhas lidas da planilha de formulários.</param>
        /// <returns>Uma <see cref="RespostasDeFormularioPorParticipante"/> por linha.</returns>
        public List<RespostasDeFormularioPorParticipante> Calcular(
            IEnumerable<RespostaBrutaDeFormulario> respostasBrutas)
        {
            return respostasBrutas
                .Select(ConstruirRespostasDoParticipante)
                .ToList();
        }

        private RespostasDeFormularioPorParticipante ConstruirRespostasDoParticipante(
            RespostaBrutaDeFormulario respostaBruta)
        {
            var campos = respostaBruta.Campos ?? new Dictionary<string, string>();

            double escoreIeee = LerValorNumerico(campos, MapeamentoDeColunasDeFormulario.EscoreIeeeTotal);

            return new RespostasDeFormularioPorParticipante
            {
                IdentificacaoManual         = respostaBruta.IdentificacaoManual,
                EscoreIeeeTotal             = escoreIeee,
                ClassificacaoIeee           = ClassificarEscoreIeee(escoreIeee),
                Idade                       = LerValorNumerico(campos, MapeamentoDeColunasDeFormulario.Idade),
                Peso                        = LerValorNumerico(campos, MapeamentoDeColunasDeFormulario.Peso),
                RespostasBrutas             = campos,
                OrdemDasColunas             = respostaBruta.OrdemDasColunas ?? new List<string>(),
                NumeroDaLinhaOriginal       = respostaBruta.NumeroDaLinhaOriginal,
            };
        }

        /// <summary>
        /// Classifica o escore total do IEEE em uma das três faixas da §5.5.2: estresse baixo
        /// (34–68), intermediário (69–102) ou alto (103–170). Retorna string vazia se o escore
        /// for NaN.
        /// </summary>
        private string ClassificarEscoreIeee(double escore)
        {
            var faixa = ClassificadorDeFaixaDeEstresseIeee.Classificar(escore);

            return faixa.HasValue
                ? ClassificadorDeFaixaDeEstresseIeee.ObterRotulo(faixa.Value)
                : string.Empty;
        }

        // SomarItens foi REMOVIDO: os seus dois chamadores — os escores por domínio A–E e a
        // contagem de sintomas SBS — saíram do projeto, e uma soma genérica sem consumidor
        // convidaria ao retorno do mesmo erro (somar rótulos ordinais como se fossem números).

        /// <summary>
        /// Lê o valor numérico da coluna localizada entre os nomes candidatos.
        /// A correspondência fica em <see cref="LocalizadorDeColunaDeFormulario"/>, compartilhada
        /// com a tabela-síntese para que as duas usem exatamente a mesma regra.
        /// O parse usa <see cref="CultureInfo.InvariantCulture"/>, coerente com o texto produzido
        /// pelo <see cref="AnalisadorExcelFormularios"/>.
        /// </summary>
        private double LerValorNumerico(Dictionary<string, string> campos, string[] nomesCandidatos)
        {
            if (!LocalizadorDeColunaDeFormulario.TentarLocalizar(campos, nomesCandidatos, out _, out string valor))
                return double.NaN;

            return TentarConverter(valor, out double numero) ? numero : double.NaN;
        }

        private static bool TentarConverter(string texto, out double valor)
        {
            valor = double.NaN;

            if (string.IsNullOrWhiteSpace(texto))
                return false;

            return double.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out valor);
        }
    }
}
