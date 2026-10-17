using System;
using System.Globalization;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;
using PluginCAISM.Parsing;
using PluginCAISM.Processamento;

namespace PluginCAISM.Interface.Controles
{
    /// <summary>
    /// Dados de um participante disponíveis para os gráficos de dispersão da aba de Dados do Grupo.
    /// </summary>
    /// <param name="Respostas">Respostas de formulário do participante.</param>
    /// <param name="Cortisol">
    /// Coletas de cortisol do participante, ou <c>null</c> quando o arquivo da Etapa 3 não foi
    /// carregado ou não há registro para esta identificação.
    /// </param>
    /// <param name="IndicadoresDeSbs">
    /// Indicadores de Sick Building Syndrome do participante, ou <c>null</c> quando não calculados.
    /// Vêm de <see cref="Processamento.CalculadorDeIndicadoresDeSbs"/>, que faz a limpeza da escala
    /// ordinal da Q37 — a severidade não é lida da planilha diretamente.
    /// </param>
    public readonly record struct DadosDoParticipante(
        RespostasDeFormularioPorParticipante Respostas,
        RegistroDeCortisol Cortisol,
        IndicadoresDeSbs IndicadoresDeSbs);

    /// <summary>
    /// Faixa de classificação de um índice, desenhada como banda de fundo no eixo e listada na
    /// legenda do gráfico.
    /// </summary>
    /// <remarks>
    /// Existe para que o gráfico mostre onde estão os pontos de corte do instrumento, e não apenas
    /// a nuvem de pontos. Sem elas, saber que um participante tem IEEE 95 não diz se isso é baixo,
    /// intermediário ou alto — a informação fica só na cabeça de quem conhece a escala.
    /// </remarks>
    public sealed class FaixaDeReferencia
    {
        /// <summary>Início da faixa, no valor da variável.</summary>
        public double Inicio { get; init; }

        /// <summary>Fim da faixa, no valor da variável.</summary>
        public double Fim { get; init; }

        /// <summary>Rótulo exibido na legenda. Exemplo: "Estresse baixo (34–68)".</summary>
        public string Rotulo { get; init; } = string.Empty;

        /// <summary>Cor de preenchimento da banda, em hexadecimal.</summary>
        public string CorHexadecimal { get; init; } = "#F0F2F5";
    }

    /// <summary>
    /// Descreve uma variável plotável em um eixo dos gráficos de dispersão do grupo: como lê-la de
    /// um participante e como desenhar o eixo correspondente.
    /// </summary>
    /// <remarks>
    /// É o que permite o gráfico da aba ser único e dinâmico: cada eixo recebe um destes descritores,
    /// escolhido pelo usuário numa lista. Acrescentar uma variável plotável é escrever um descritor e
    /// incluí-lo em <see cref="VariaveisDoGrupo.Selecionaveis"/> — nenhuma classe nova.
    /// </remarks>
    public sealed class VariavelDoGrupo
    {
        /// <summary>Nome curto, usado no título do cartão ("IEEE total × IMC").</summary>
        public string NomeCurto { get; init; } = string.Empty;

        /// <summary>Título completo do eixo, com unidade quando houver.</summary>
        public string TituloDoEixo { get; init; } = string.Empty;

        /// <summary>
        /// Como a variável é chamada na mensagem de descarte ("2 sem IMC informado").
        /// </summary>
        public string NomeNoDescarte { get; init; } = string.Empty;

        /// <summary>
        /// Lê o valor da variável para um participante. Deve devolver NaN quando o dado está
        /// ausente, vazio, "-" ou não interpretável — o ponto é então descartado.
        /// </summary>
        public Func<DadosDoParticipante, double> Ler { get; init; } = _ => double.NaN;

        /// <summary>Limite inferior fixo do eixo; <c>null</c> deixa que os dados o definam.</summary>
        public double? MinimoFixo { get; init; }

        /// <summary>Limite superior fixo do eixo; <c>null</c> deixa que os dados o definam.</summary>
        public double? MaximoFixo { get; init; }

        /// <summary>Folga aplicada além dos valores observados quando o limite não é fixo.</summary>
        public double Margem { get; init; } = 1.0;

        /// <summary>
        /// Se verdadeiro, o eixo sempre inclui o zero e recebe uma linha de referência ali.
        /// </summary>
        /// <remarks>
        /// Usado pelas variáveis que assumem valores negativos — hoje só o delta de cortisol —,
        /// em que o sinal é a informação principal e se perderia sem a marca do zero.
        /// </remarks>
        public bool LinhaDeReferenciaEmZero { get; init; }

        /// <summary>Passo forçado entre marcas do eixo; <c>null</c> deixa o LiveCharts decidir.</summary>
        public double? PassoDoSeparador { get; init; }

        /// <summary>Formatação do rótulo das marcas; <c>null</c> usa o padrão numérico.</summary>
        public Func<double, string> FormatarRotulo { get; init; }

        /// <summary>
        /// Verdadeiro quando a variável assume poucos valores distintos, fazendo os pontos se
        /// empilharem. Reduz a opacidade dos marcadores para revelar a sobreposição.
        /// </summary>
        public bool ValoresDiscretos { get; init; }

        /// <summary>
        /// Faixas de classificação do índice, desenhadas como bandas de fundo quando a variável
        /// está no eixo Y. Vazio para as variáveis sem pontos de corte definidos.
        /// </summary>
        public FaixaDeReferencia[] FaixasDeReferencia { get; init; }
            = System.Array.Empty<FaixaDeReferencia>();
    }

    /// <summary>
    /// Catálogo das variáveis usadas nos gráficos de dispersão da aba de Dados do Grupo.
    /// </summary>
    public static class VariaveisDoGrupo
    {
        // ── Cores das bandas de referência ───────────────────────────────
        // Mesma leitura já usada nos cartões de destaque: verde favorável, âmbar intermediário,
        // vermelho desfavorável. Tons muito claros, porque a banda é FUNDO — os pontos precisam
        // continuar legíveis sobre ela.
        private const string CorFaixaFavoravel     = "#E6F4EC";
        private const string CorFaixaIntermediaria = "#FDF3E3";
        private const string CorFaixaDesfavoravel  = "#FBECEB";

        /// <summary>Escore total do IEEE (coluna AJ de b_ieee, faixa 34–170).</summary>
        public static VariavelDoGrupo Ieee { get; } = new()
        {
            NomeCurto      = "IEEE total",
            TituloDoEixo   = "IEEE total (34–170)",
            NomeNoDescarte = "escore do IEEE",
            Ler            = d => d.Respostas.EscoreIeeeTotal,
            MinimoFixo     = LimiaresDeClassificacaoIeee.EscoreMinimo,
            MaximoFixo     = LimiaresDeClassificacaoIeee.EscoreMaximo,

            FaixasDeReferencia = new[]
            {
                new FaixaDeReferencia
                {
                    Inicio = LimiaresDeClassificacaoIeee.EscoreMinimo,
                    Fim    = LimiaresDeClassificacaoIeee.FimDaFaixaBaixa,
                    Rotulo = LimiaresDeClassificacaoIeee.RotuloDaFaixaBaixa,
                    CorHexadecimal = CorFaixaFavoravel,
                },
                new FaixaDeReferencia
                {
                    Inicio = LimiaresDeClassificacaoIeee.FimDaFaixaBaixa,
                    Fim    = LimiaresDeClassificacaoIeee.FimDaFaixaIntermediaria,
                    Rotulo = LimiaresDeClassificacaoIeee.RotuloDaFaixaIntermediaria,
                    CorHexadecimal = CorFaixaIntermediaria,
                },
                new FaixaDeReferencia
                {
                    Inicio = LimiaresDeClassificacaoIeee.FimDaFaixaIntermediaria,
                    Fim    = LimiaresDeClassificacaoIeee.EscoreMaximo,
                    Rotulo = LimiaresDeClassificacaoIeee.RotuloDaFaixaAlta,
                    CorHexadecimal = CorFaixaDesfavoravel,
                },
            },
        };

        /// <summary>Delta de cortisol S − B em ng/ml (coluna T de CORTISOL_ngml).</summary>
        public static VariavelDoGrupo DeltaDeCortisol { get; } = new()
        {
            NomeCurto      = "Delta de cortisol",
            TituloDoEixo   = "Delta de cortisol S − B (ng/ml)",
            NomeNoDescarte = "delta de cortisol",
            Ler            = d => d.Cortisol == null ? double.NaN : d.Cortisol.Delta,
            Margem         = 5.0,
            LinhaDeReferenciaEmZero = true,
        };

        /// <summary>Índice de massa corporal (coluna F de a_recrutamento).</summary>
        public static VariavelDoGrupo Imc { get; } = new()
        {
            NomeCurto      = "IMC",
            TituloDoEixo   = "IMC (kg/m²)",
            NomeNoDescarte = "IMC",
            Ler            = d => LerNumero(d.Respostas, FaixasDeImc.NomesDaColuna),
            Margem         = 2.0,
        };

        /// <summary>
        /// Severidade de Sick Building Syndrome: soma ordinal dos 14 itens da Q37 de a_recrutamento.
        /// </summary>
        /// <remarks>
        /// Vem pronta de <see cref="Processamento.CalculadorDeIndicadoresDeSbs"/>, que aplica a
        /// escala ordinal e a normalização de caixa — inclusive a armadilha de "MUITO FREQUENTE"
        /// conter "FREQUENTE". Não é lida da planilha aqui.
        /// </remarks>
        public static VariavelDoGrupo SickBuildingSyndrome { get; } = new()
        {
            NomeCurto      = "Sick Building Syndrome",
            TituloDoEixo   = $"Severidade de SBS (0–{EscalaOrdinalDoSbs.SeveridadeMaxima})",
            NomeNoDescarte = "severidade de SBS",
            Ler            = d => d.IndicadoresDeSbs == null
                                      ? double.NaN
                                      : d.IndicadoresDeSbs.Severidade,
            MinimoFixo     = 0,
            MaximoFixo     = EscalaOrdinalDoSbs.SeveridadeMaxima,
        };

        /// <summary>Idade em anos (coluna C de a_recrutamento).</summary>
        /// <remarks>
        /// Valor EXATO, não a faixa de cinco anos usada no gráfico de idade anterior. Com o gráfico
        /// dinâmico as cinco variáveis são tratadas de forma uniforme, e agrupar só a idade a tornaria
        /// o único eixo com escala diferente das demais.
        /// </remarks>
        // RESOLVIDO pelo TextoFinal de 31/08/2026, §5.6 (aba de Dados do Grupo): "o eixo
        // horizontal oferece exclusivamente características do indivíduo — IDADE, índice de
        // massa corporal, seção de atuação..." — o texto nomeia a variável como idade, não como
        // faixa etária, e idade exata é o que está implementado. A opção por faixas de cinco
        // anos continua pronta em LeitorDeIdade.ObterInicioDaFaixa/ObterRotuloDaFaixa caso o
        // texto venha a mudar.
        public static VariavelDoGrupo Idade { get; } = new()
        {
            NomeCurto      = "Idade",
            TituloDoEixo   = "Idade (anos)",
            NomeNoDescarte = "idade",
            Ler            = d => LeitorDeIdade.LerIdadeEmAnos(
                                      LerTexto(d.Respostas, MapeamentoDeColunasDeFormulario.Idade)),
            Margem         = 2.0,
        };

        // ── Características do indivíduo (eixo X) ────────────────────────

        /// <summary>Horas semanais de trabalho doméstico (Questão 23), em três faixas.</summary>
        public static VariavelDoGrupo HorasDeTrabalhoDomestico { get; } = new()
        {
            NomeCurto      = "Trabalho doméstico",
            TituloDoEixo   = "Horas de trabalho doméstico por semana",
            NomeNoDescarte = "horas de trabalho doméstico",
            Ler            = d => LeitorDeVariaveisCategoricas.LerTrabalhoDomestico(
                                      LerTexto(d.Respostas, MapeamentoDeColunasDeFormulario.HorasDeTrabalhoDomestico)),
            MinimoFixo     = -0.5,
            MaximoFixo     = LeitorDeVariaveisCategoricas.FaixasDeTrabalhoDomestico.Length - 0.5,
            PassoDoSeparador = 1,
            FormatarRotulo = LeitorDeVariaveisCategoricas.ObterRotuloDeTrabalhoDomestico,
            ValoresDiscretos = true,
        };

        /// <summary>Outra fonte de renda além do CAISM (Questão 19).</summary>
        public static VariavelDoGrupo OutraFonteDeRenda { get; } = new()
        {
            NomeCurto      = "Outra fonte de renda",
            TituloDoEixo   = "Possui outra fonte de renda",
            NomeNoDescarte = "resposta sobre outra fonte de renda",
            Ler            = d => LeitorDeVariaveisCategoricas.LerOutraFonteDeRenda(
                                      LerTexto(d.Respostas, MapeamentoDeColunasDeFormulario.OutraFonteDeRenda)),
            MinimoFixo     = -0.5,
            MaximoFixo     = 1.5,
            PassoDoSeparador = 1,
            FormatarRotulo = LeitorDeVariaveisCategoricas.ObterRotuloDeOutraFonteDeRenda,
            ValoresDiscretos = true,
        };

        /// <summary>Seção de atuação na Divisão de Enfermagem (Questão 17).</summary>
        /// <remarks>
        /// Variável NOMINAL: a posição no eixo é um lugar estável, não uma ordem. Distância entre
        /// duas seções não significa nada, e por isso nenhuma estatística de associação linear ou
        /// monotônica se aplica a este eixo.
        /// </remarks>
        public static VariavelDoGrupo SecaoDeTrabalho { get; } = new()
        {
            NomeCurto      = "Seção de trabalho",
            TituloDoEixo   = "Seção na Divisão de Enfermagem",
            NomeNoDescarte = "seção de trabalho",
            Ler            = d => LeitorDeVariaveisCategoricas.LerSecaoDeTrabalho(
                                      LerTexto(d.Respostas, MapeamentoDeColunasDeFormulario.DivisaoDeEnfermagem)),
            MinimoFixo     = -0.5,
            MaximoFixo     = LeitorDeVariaveisCategoricas.SecoesDeTrabalho.Length - 0.5,
            PassoDoSeparador = 1,
            FormatarRotulo = LeitorDeVariaveisCategoricas.ObterRotuloDeSecaoDeTrabalho,
            ValoresDiscretos = true,
        };

        // ── Índices de estresse (eixo Y) ─────────────────────────────────

        /// <summary>Cortisol basal, em ng/ml (coluna B do arquivo de cortisol).</summary>
        public static VariavelDoGrupo CortisolBasal { get; } = new()
        {
            NomeCurto      = "Cortisol basal",
            TituloDoEixo   = "Cortisol basal (ng/ml)",
            NomeNoDescarte = "cortisol basal",
            Ler            = d => d.Cortisol == null ? double.NaN : d.Cortisol.ValorBasal,
            Margem         = 2.0,
        };

        /// <summary>Cortisol final, em ng/ml (coluna S do arquivo de cortisol).</summary>
        public static VariavelDoGrupo CortisolFinal { get; } = new()
        {
            NomeCurto      = "Cortisol final",
            TituloDoEixo   = "Cortisol final (ng/ml)",
            NomeNoDescarte = "cortisol final",
            Ler            = d => d.Cortisol == null ? double.NaN : d.Cortisol.ValorFinal,
            Margem         = 2.0,
        };

        /// <summary>
        /// Percentual de sintomas de SBS que o participante atribui ao trabalho (Questão 38).
        /// </summary>
        /// <remarks>
        /// Diferente da severidade: mede ATRIBUIÇÃO CAUSAL, não intensidade. Dois participantes com
        /// a mesma severidade podem divergir por inteiro em quanto responsabilizam o edifício.
        /// </remarks>
        public static VariavelDoGrupo PercentualDeSbsAtribuidoAoTrabalho { get; } = new()
        {
            NomeCurto      = "SBS atribuído ao trabalho",
            TituloDoEixo   = "Sintomas de SBS atribuídos ao trabalho (%)",
            NomeNoDescarte = "atribuição de SBS ao trabalho",
            Ler            = d => d.IndicadoresDeSbs == null
                                      ? double.NaN
                                      : d.IndicadoresDeSbs.PercentualAtribuidoAoTrabalho * 100.0,
            MinimoFixo     = 0,
            MaximoFixo     = 100,
            PassoDoSeparador = 25,
        };

        /// <summary>
        /// Variáveis oferecidas no eixo X: características do indivíduo.
        /// </summary>
        public static readonly VariavelDoGrupo[] SelecionaveisEmX =
        {
            Idade,
            Imc,
            SecaoDeTrabalho,
            HorasDeTrabalhoDomestico,
            OutraFonteDeRenda,
        };

        /// <summary>
        /// Variáveis oferecidas no eixo Y: índices de estresse.
        /// </summary>
        // DECISÃO CONSOLIDADA (TextoFinal de 31/08/2026): só o IEEE tem faixas estabelecidas
        // (34–68 baixo, 69–102 intermediário, 103–170 alto) e só ele desenha bandas de fundo.
        // Para o delta de cortisol, o requisito funcional dos gráficos de dispersão do grupo
        // (Quadro 40) prescreve exatamente o que está implementado — "linha de referência em
        // zero nos gráficos de delta" — e nenhum ponto de corte adicional. SBS, percentual atribuído ao trabalho e cortisol basal/final ficam
        // sem banda porque não há corte definido na tese nem na literatura citada — inventá-los
        // faria o gráfico afirmar uma classificação que a pesquisa não sustenta. Se algum corte
        // for publicado, o mecanismo já existe em FaixaDeReferencia.
        // Sick Building Syndrome e o percentual atribuído ao trabalho foram RETIRADOS desta lista.
        // Os descritores continuam definidos e válidos — seguem em Selecionaveis, que é a ordem da
        // paleta —, apenas não são mais oferecidos como eixo.
        public static readonly VariavelDoGrupo[] SelecionaveisEmY =
        {
            Ieee,
            DeltaDeCortisol,
            CortisolBasal,
            CortisolFinal,
        };

        /// <summary>
        /// União das duas listas, preservada para quem precise percorrer todas as variáveis.
        /// </summary>
        /// <remarks>
        /// Os dois eixos deixaram de oferecer os mesmos itens: X passou a listar características do
        /// indivíduo e Y, índices de estresse. Esta lista continua sendo a ordem que define a cor de
        /// cada combinação em <see cref="PaletaDoGraficoDinamicoDoGrupo.ObterCor"/>.
        /// </remarks>
        public static readonly VariavelDoGrupo[] Selecionaveis =
        {
            Ieee,
            SickBuildingSyndrome,
            DeltaDeCortisol,
            Imc,
            Idade,
            SecaoDeTrabalho,
            HorasDeTrabalhoDomestico,
            OutraFonteDeRenda,
            CortisolBasal,
            CortisolFinal,
            PercentualDeSbsAtribuidoAoTrabalho,
        };

        /// <summary>Lê uma coluna como texto bruto; devolve vazio quando a coluna não existe.</summary>
        private static string LerTexto(
            RespostasDeFormularioPorParticipante respostas, string[] nomesDaColuna)
        {
            LocalizadorDeColunaDeFormulario.TentarLocalizar(
                respostas.RespostasBrutas, nomesDaColuna, out _, out string texto);

            return texto;
        }

        /// <summary>
        /// Lê uma coluna como número. Devolve NaN quando ausente, vazia ou não numérica — os
        /// valores chegam já normalizados em cultura invariante pelo analisador de Excel.
        /// </summary>
        private static double LerNumero(
            RespostasDeFormularioPorParticipante respostas, string[] nomesDaColuna)
        {
            string texto = LerTexto(respostas, nomesDaColuna);

            return double.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out double valor)
                ? valor
                : double.NaN;
        }
    }
}
