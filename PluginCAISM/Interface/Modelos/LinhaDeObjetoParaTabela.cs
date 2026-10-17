using System.Collections.Generic;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Interface.Modelos
{
    /// <summary>
    /// Modelo de apresentação para uma linha da tabela de objetos da aba de Potencial
    /// Homeodinâmico (§5.5.1).
    /// </summary>
    /// <remarks>
    /// Achata <see cref="MetricasPorObjeto"/> em uma linha exibível e expõe a
    /// <see cref="Classificacao"/> para que o DataGrid pinte o fundo conforme o Quadro 41:
    /// verde para restaurador, vermelho para estressor, cinza para neutro e sem pontuação.
    /// A cor em si fica no XAML, via gatilho sobre <see cref="Classificacao"/> — a camada de
    /// apresentação não recalcula nada.
    /// </remarks>
    public class LinhaDeObjetoParaTabela
    {
        /// <summary>Nome do objeto virtual. Exemplo: "Parede: Rippas".</summary>
        public string NomeDoObjeto { get; }

        /// <summary>Classificação de potencial, usada pelo XAML para escolher a cor de fundo.</summary>
        public ClassificacaoDePotencial Classificacao { get; }

        /// <summary>Rótulo legível da classificação: "Restaurador", "Estressor", "Neutro"…</summary>
        public string ClassificacaoRotulo { get; }

        /// <summary>Cenário em que o elemento foi apurado. Compõe a identidade da linha.</summary>
        public string Cenario { get; }

        /// <summary>PH_e — índice de Potencial Homeodinâmico, no intervalo (−1, +1).</summary>
        public double Pontuacao { get; }

        /// <summary>
        /// ν̂_e — efeito de elemento, em mm de ΔD, ou NaN quando não estimado. Exposto para
        /// auditoria do índice.
        /// </summary>
        public double EfeitoNuMm { get; }

        // A razão de significância z deixou de ser exposta aqui porque a coluna correspondente saiu
        // da tabela. Ela continua sendo calculada em MetricasPorObjeto.EstatisticaZ e é o que decide
        // a Classificacao — apenas não é mais exibida.

        /// <summary>Duração média das fixações no objeto, em milissegundos.</summary>
        public double DuracaoMediaMs { get; }

        /// <summary>ΔD pupilar médio em relação à linha de base da sessão, em milímetros.</summary>
        public double DeltaPupilarMm { get; }

        /// <summary>
        /// Diâmetro pupilar ABSOLUTO médio nas fixações do elemento, em milímetros.
        /// </summary>
        /// <remarks>
        /// Exposto como teste de sanidade do confundidor de luminância descrito no §3.6.4 do
        /// TextoFinal. Sem fotometria da cena, o reflexo fotomotor se aloja no efeito de elemento:
        /// objetos claros induzem constrição e tendem a sair "restauradores" por brilho, não por
        /// restauração.
        ///
        /// O diâmetro absoluto é o proxy disponível dentro do próprio dado. Se os elementos de
        /// menor diâmetro forem sistematicamente os mesmos classificados como restauradores, isso
        /// é evidência do confundidor — e a pergunta deixa de depender de instrumentação que o HMD
        /// não entrega.
        /// </remarks>
        public double DiametroPupilarMm { get; }

        /// <summary>
        /// <see cref="Pontuacao"/> já formatada, com travessão quando não há índice.
        /// </summary>
        /// <remarks>
        /// Os objetos observados sem nenhuma fixação válida não têm índice a exibir. Mostrar o
        /// "NaN" cru numa coluna de resultado sugere erro de cálculo onde há apenas ausência de
        /// medida, e destoa das colunas ambientais, que já usam o travessão para o mesmo caso.
        ///
        /// A ordenação da coluna permanece NUMÉRICA: o <c>SortMemberPath</c> declarado no XAML
        /// aponta para <see cref="Pontuacao"/>, de modo que ordenar por PH continua a produzir a
        /// sequência do mais restaurador ao mais estressor, e não a ordem alfabética do texto.
        /// </remarks>
        public string PontuacaoTexto => FormatarMedida(Pontuacao, "N3", string.Empty);

        /// <summary>
        /// <see cref="DiametroPupilarMm"/> já formatado, com travessão quando não há medida.
        /// </summary>
        public string DiametroPupilarTexto => FormatarMedida(DiametroPupilarMm, "N3", string.Empty);

        // ── Condições ambientais vigentes durante as fixações ─────────────
        // Vêm da leitura de sensor mais recente igual ou anterior a cada fixação. Já formatadas:
        // a tabela precisa distinguir "medido" de "sem leitura", e um NaN cru na célula não faz
        // essa distinção para quem lê.

        /// <summary>Temperatura média do ar, em °C, ou travessão quando não houve leitura.</summary>
        public string Temperatura { get; }

        /// <summary>Umidade relativa média, em %, ou travessão quando não houve leitura.</summary>
        public string Umidade { get; }

        /// <summary>
        /// Nível sonoro médio, em dB, das fixações com leitura ambiental vigente. Travessão quando
        /// nenhuma delas teve leitura.
        /// </summary>
        public string Ruido { get; }

        /// <summary>
        /// "DETECTADO" quando o sensor acusou gás em ao menos uma fixação do elemento,
        /// "NÃO DETECTADO" quando houve leitura e nenhuma acusou, e travessão quando não houve
        /// leitura ambiental alguma.
        /// </summary>
        public string PresencaDeGas { get; }

        // A pontuação ambiental deixou de ser exposta aqui: a coluna AMBIENTAL saiu da tabela
        // porque o ambiente não entra no índice sob o modelo em duas camadas.

        /// <summary>Número de fixações que incidiram no objeto.</summary>
        public int QuantidadeDeFixacoes { get; }

        private LinhaDeObjetoParaTabela(MetricasPorObjeto metrica, int quantidadeDeFixacoes)
        {
            NomeDoObjeto = metrica.NomeDoObjeto;
            Cenario = metrica.Cenario;
            DuracaoMediaMs = metrica.DuracaoMediaDasFixacoesMs;
            DeltaPupilarMm = metrica.DeltaPupilarMedioMm;
            DiametroPupilarMm = metrica.DiametroPupilaMedioMm;

            // A contagem do próprio elemento (objeto × cenário) tem precedência sobre a contagem
            // recebida, que é por nome de objeto e somaria cenários diferentes.
            QuantidadeDeFixacoes = metrica.QuantidadeDeObservacoes > 0
                ? metrica.QuantidadeDeObservacoes
                : quantidadeDeFixacoes;

            Pontuacao = metrica.PotencialHomeodinamico;
            EfeitoNuMm = metrica.EfeitoNuMm;

            Temperatura = FormatarMedida(metrica.TemperaturaMediaCelsius, "N1", " °C");
            Umidade     = FormatarMedida(metrica.UmidadeMediaPercentual, "N1", "%");

            // O ruído é a média das leituras vigentes, como temperatura e umidade: as três vêm de
            // colunas contínuas do arquivo de sensores e seguem a mesma regra de ausência.
            Ruido = FormatarMedida(metrica.RuidoMedioDb, "N1", " dB");

            // A presença de gás é binária: o sensor é um indicador de dois estados, e exibir uma
            // proporção sugeria uma graduação de concentração que ele não mede.
            //
            // Os três casos são distintos e a coluna precisa distingui-los: sem nenhuma leitura
            // ambiental não há o que afirmar, e escrever "NÃO DETECTADO" ali seria dar por medido
            // o que não foi. Esse caso recebe o mesmo travessão das demais colunas de ambiente.
            PresencaDeGas =
                metrica.QuantidadeDeFixacoesComLeituraAmbiental == 0 ? SemMedida
                : metrica.QuantidadeDeFixacoesComGasDetectado > 0 ? RotulosDaInterface.GasDetectado
                : RotulosDaInterface.GasNaoDetectado;

            // A classificação já foi resolvida no calculador, pela afirmabilidade do IC 85% de ν̂_e.
            // A camada de apresentação não reclassifica.
            Classificacao = metrica.ClassificacaoPH;
            ClassificacaoRotulo = ClassificadorDePotencialDeHitpoint.ObterRotulo(Classificacao);
        }

        /// <summary>Marca de ausência de medida, exibida na célula.</summary>
        private const string SemMedida = "—";

        /// <summary>
        /// Formata uma medida ambiental, devolvendo travessão quando não há leitura.
        /// </summary>
        private static string FormatarMedida(double valor, string formato, string unidade)
        {
            return double.IsNaN(valor)
                ? SemMedida
                : valor.ToString(formato, System.Globalization.CultureInfo.CurrentCulture) + unidade;
        }

        /// <summary>
        /// Constrói as linhas da tabela a partir das métricas por objeto, ordenadas do mais
        /// restaurador ao mais estressor.
        /// </summary>
        /// <param name="metricas">Métricas por objeto do cenário selecionado.</param>
        /// <param name="fixacoesPorObjeto">
        /// Contagem de fixações por nome de objeto, exibida na coluna de fixações. Objetos de
        /// inventário (observados sem formar fixação válida) não constam do dicionário e saem
        /// com zero — nenhuma linha é ocultada por contagem.
        /// </param>
        public static List<LinhaDeObjetoParaTabela> Construir(
            IEnumerable<MetricasPorObjeto> metricas,
            IReadOnlyDictionary<string, int> fixacoesPorObjeto)
        {
            if (metricas == null)
                return new List<LinhaDeObjetoParaTabela>();

            return metricas
                .Select(metrica =>
                {
                    int quantidade = 0;
                    fixacoesPorObjeto?.TryGetValue(metrica.NomeDoObjeto, out quantidade);
                    return new LinhaDeObjetoParaTabela(metrica, quantidade);
                })
                // NaN em OrderByDescending fica ANTES de tudo, o que poria os elementos sem
                // estimativa no topo da tabela. Empurramos para o fim explicitamente.
                .OrderByDescending(linha => double.IsNaN(linha.Pontuacao)
                    ? double.NegativeInfinity
                    : linha.Pontuacao)
                .ToList();
        }
    }
}
