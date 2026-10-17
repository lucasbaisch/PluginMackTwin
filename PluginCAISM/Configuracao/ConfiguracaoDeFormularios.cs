namespace PluginCAISM.Configuracao
{
    /// <summary>
    /// Mapeamento entre os nomes das colunas da planilha de formulários (Arquivo 4) e os campos
    /// consumidos pela aba de contextualização.
    /// </summary>
    /// <remarks>
    /// Cada campo declara uma lista de nomes candidatos, testados em ordem por igualdade exata e
    /// depois por conteúdo (case-insensitive), como já é feito para os sensores em
    /// <see cref="MapeamentoDeColunasDeSensor"/>.
    ///
    /// Os nomes foram extraídos do arquivo real de formulários da pesquisa, planilha
    /// "a_recrutamento". Como a correspondência é por conteúdo, os trechos escolhidos são os
    /// mínimos que identificam cada coluna sem ambiguidade — importante porque os enunciados das
    /// questões 37 e 38 diferem apenas no meio do texto e terminam com o mesmo rótulo entre
    /// colchetes.
    ///
    /// Listas que permanecem vazias são intencionais: o
    /// <see cref="Processamento.CalculadorDeEscoresDeFormulario"/> devolve NaN para o escore
    /// correspondente e a aba de contextualização exibe as respostas brutas sem pontuação —
    /// nenhum valor é inventado.
    /// </remarks>
    public static class MapeamentoDeColunasDeFormulario
    {
        /// <summary>
        /// Nomes candidatos da coluna que identifica o participante.
        /// Na planilha real: coluna A, "ID_PARTICIPANTE", com códigos numéricos de 4 dígitos.
        /// </summary>
        // RESOLVIDO com os dados definitivos (cenario_1.csv e cenario_2.csv): a coluna
        // identificacaoManual traz os MESMOS 26 códigos numéricos do ID_PARTICIPANTE dos
        // formulários e do cortisol — interseção de 100%, verificada elemento a elemento. A
        // correspondência direta que o §5.5 do TextoFinal pressupõe passou a valer.
        //
        // A ressalva vale apenas para o dados_hmd.csv ANTIGO (Teste Piloto), cujos códigos
        // alfabéticos (MA001, LF001…) não cruzam com formulário algum: carregado ele, as
        // respostas e a tabela-síntese funcionam, mas nenhum participante liga ao seu PH.
        //
        // "TESTE PILOTO" é o cabeçalho REAL da coluna A da planilha c_percepção_ambiental, cujas
        // células contêm os mesmos IDs numéricos das outras duas planilhas — rótulo deixado por
        // engano no lugar de "ID_PARTICIPANTE". Aceitá-lo como candidato é o que permite ler esse
        // formulário sem editar a planilha da pesquisa.
        public static readonly string[] IdentificacaoDoParticipante =
        {
            "ID_PARTICIPANTE",
            "TESTE PILOTO",
        };

        /// <summary>Nomes candidatos da coluna com o escore total do IEEE (faixa 34–170).</summary>
        /// <remarks>
        /// Na planilha real é a coluna AJ ("TOTAL") da planilha "b_ieee", calculada como a soma
        /// dos 34 itens de estresse ocupacional (colunas B a AI). O
        /// <see cref="Parsing.AnalisadorExcelFormularios"/> lê todas as planilhas e prefixa os
        /// nomes de coluna com a planilha de origem, por isso o nome completo é
        /// "b_ieee · TOTAL" — o candidato prefixado vem primeiro para evitar qualquer ambiguidade
        /// com uma eventual coluna "TOTAL" de outra planilha.
        /// </remarks>
        public static readonly string[] EscoreIeeeTotal = { "b_ieee · TOTAL", "TOTAL" };

        /// <summary>
        /// Nomes candidatos da coluna de idade do participante.
        /// Na planilha real: "QUESTÃO 5] Qual a sua idade?".
        /// </summary>
        public static readonly string[] Idade = { "Qual a sua idade" };

        /// <summary>
        /// Nomes candidatos da coluna de gênero (coluna B de a_recrutamento).
        /// Na planilha real: "QUESTÃO 4] Qual o seu gênero? …".
        /// </summary>
        public static readonly string[] Genero = { "Qual o seu gênero" };

        /// <summary>
        /// Nomes candidatos da coluna de peso do participante.
        /// Na planilha real: "QUESTÃO 7] Qual seu peso em quilos (kg)?" — valor numérico.
        /// </summary>
        public static readonly string[] Peso = { "Qual seu peso em quilos" };

        /// <summary>
        /// Nomes candidatos da coluna de atividade física (coluna S de a_recrutamento).
        /// Na planilha real: "QUESTÃO 20] Você pratica atividade física? Caso a resposta seja
        /// positiva, coloque a quantidade de dias por semana na opção Outro" — as respostas são
        /// texto livre, interpretadas por <see cref="Dominio.LeitorDeAtividadeFisica"/>.
        /// </summary>
        public static readonly string[] AtividadeFisica = { "pratica atividade física" };

        /// <summary>
        /// Nomes candidatos da coluna de estado civil (coluna H de a_recrutamento).
        /// Na planilha real: "QUESTÃO 9] Qual seu estado Civil:".
        /// </summary>
        public static readonly string[] EstadoCivil = { "Qual seu estado Civil" };

        /// <summary>
        /// Nomes candidatos da coluna de menopausa (coluna K de a_recrutamento).
        /// Na planilha real: "QUESTÃO 12] Você está no período de menopausa?".
        /// </summary>
        public static readonly string[] Menopausa = { "período de menopausa" };

        /// <summary>
        /// Nomes candidatos da coluna de seção na Divisão de Enfermagem (coluna P de a_recrutamento).
        /// Na planilha real: "QUESTÃO 17] Qual seção você atua na Divisão de Enfermagem?".
        /// </summary>
        public static readonly string[] DivisaoDeEnfermagem = { "atua na Divisão de Enfermagem" };

        /// <summary>
        /// Nomes candidatos da coluna de horas semanais de trabalho doméstico (Questão 23).
        /// </summary>
        public static readonly string[] HorasDeTrabalhoDomestico = { "trabalho doméstico" };

        /// <summary>
        /// Nomes candidatos da coluna de outra fonte de renda além do CAISM (Questão 19).
        /// </summary>
        public static readonly string[] OutraFonteDeRenda = { "outra fonte de renda" };
    }

    /// <summary>
    /// Composição dos escores derivados: quais questões da planilha somam em cada domínio de
    /// estresse ocupacional (A–E) e quais compõem os 14 itens do SBS (Q37).
    /// </summary>
    /// <remarks>
    /// Cada array lista os nomes de coluna das questões que compõem o respectivo escore.
    /// Arrays vazios produzem escore NaN, sem inventar composição.
    /// </remarks>
    public static class ComposicaoDeEscoresDeFormulario
    {
        // Os arrays DominioA a DominioE foram REMOVIDOS. Estavam vazios desde sempre, o que fazia
        // os cinco escores por domínio saírem NaN, e a tabela que os exibia era inalcançável pelo
        // menu de formulários. A decomposição do IEEE em domínios é feita por
        // Configuracao.DominiosDoIeee, que associa cada um dos 34 itens ao seu domínio pelo
        // enunciado, e é essa que a aba de Dados do Participante usa.

        /// <summary>
        /// Colunas dos 14 itens do Q37 (SBS). A contagem de sintomas é a soma das frequências
        /// declaradas nestes itens.
        /// </summary>
        /// <remarks>
        /// Os trechos incluem "frequência" porque as questões 37 e 38 terminam com os mesmos
        /// rótulos entre colchetes: buscar apenas "[FADIGA]" casaria com as duas, e qual delas
        /// venceria dependeria da ordem de iteração do dicionário. A Q37 pergunta a frequência do
        /// sintoma; a Q38, se o participante o atribui ao trabalho.
        /// </remarks>
        // As respostas destes 14 itens são RÓTULOS ordinais no arquivo real ("NUNCA", "RARO",
        // "OCASIONAL", "FREQUENTE"), e não números. A lista serve para LOCALIZAR as colunas na
        // tabela do participante; a conversão em número, quando necessária, é feita pela escala
        // ordinal de CalculadorDeIndicadoresDeSbs, e não por soma direta.
        public static readonly string[] ItensDoSbsQ37 =
        {
            "frequência [FADIGA]",
            "frequência [DOR DE CABEÇA]",
            "frequência [SONOLÊNCIA]",
            "frequência [DIFICULDADE DE CONCENTRAÇÃO]",
            "frequência [TONTURA]",
            "frequência [TOSSE]",
            "frequência [COCEIRA]",
            "frequência [PELE RESSECADA]",
            "frequência [FALTA DE AR]",
            "frequência [NÁUSEA/VÔMITO]",
            "frequência [NARIZ ENTUPIDO]",
            "frequência [GARGANTA SECA]",
            "frequência [ERUPÇÃO CUTÂNEA]",
            "frequência [IRRITAÇÃO NOS OLHOS]",
        };

        /// <summary>Número de itens esperados no Q37, conforme o TextoFinal.</summary>
        public const int QuantidadeEsperadaDeItensDoSbs = 14;

        /// <summary>
        /// Colunas dos 14 itens do Q38 (atribuição do sintoma ao trabalho), colunas AW–BJ de
        /// a_recrutamento. Acompanham os itens do Q37 no formulário de Sick Building Syndrome.
        /// </summary>
        /// <remarks>
        /// Os trechos incluem "decorrência do seu trabalho" porque as questões 37 e 38 terminam
        /// com os mesmos rótulos entre colchetes; sem isso a busca casaria com a Q37.
        /// </remarks>
        public static readonly string[] ItensDoSbsQ38 =
        {
            "decorrência do seu trabalho? [FADIGA]",
            "decorrência do seu trabalho? [DOR DE CABEÇA]",
            "decorrência do seu trabalho? [SONOLÊNCIA]",
            "decorrência do seu trabalho? [DIFICULDADE DE CONCENTRAÇÃO]",
            "decorrência do seu trabalho? [TONTURA]",
            "decorrência do seu trabalho? [TOSSE]",
            "decorrência do seu trabalho? [COCEIRA]",
            "decorrência do seu trabalho? [PELE RESSECADA]",
            "decorrência do seu trabalho? [FALTA DE AR]",
            "decorrência do seu trabalho? [NÁUSEA/VÔMITO]",
            "decorrência do seu trabalho? [NARIZ ENTUPIDO]",
            "decorrência do seu trabalho? [GARGANTA SECA]",
            "decorrência do seu trabalho? [ERUPÇÃO CUTÂNEA]",
            "decorrência do seu trabalho? [IRRITAÇÃO NOS OLHOS]",
        };
    }

    /// <summary>
    /// Codificação ordinal das respostas de frequência da Q37 (SBS), usada para calcular a
    /// severidade de Sick Building Syndrome de cada participante.
    /// </summary>
    /// <remarks>
    /// A severidade é a soma dos 14 itens; com a escala base de 0 a 4, a faixa teórica é 0–56.
    /// As respostas são normalizadas (trim + maiúsculas) antes do mapeamento, o que resolve as
    /// variações de caixa encontradas no arquivo real ("frEQUENTE", "raRO").
    /// </remarks>
    public static class EscalaOrdinalDoSbs
    {
        /// <summary>Mapa base das respostas de frequência da Q37.</summary>
        public static readonly (string Resposta, int Valor)[] MapaBase =
        {
            ("NUNCA",           0),
            ("RARO",            1),
            ("OCASIONAL",       2),
            ("FREQUENTE",       3),
            ("MUITO FREQUENTE", 4),
        };

        /// <summary>
        /// Valor atribuído a respostas ausentes ou ambíguas: célula vazia, "-" ou "raro nunca".
        /// </summary>
        // POLÍTICA ADOTADA: 0 ("não reportado"). O instrumento não define tratamento de item em
        // branco, e as alternativas afirmam mais do que os dados: imputar um valor médio inventa
        // sintoma não relatado, e excluir o participante descartaria 26 itens respondidos por
        // causa de poucos ausentes (no arquivo real: 32 células vazias, 2 "-", 1 "raro nunca",
        // diluídos entre participantes). O viés resultante é CONSERVADOR e de direção conhecida —
        // a severidade de quem deixou itens em branco fica sub, nunca superestimada —, o que é
        // preferível a viés de direção desconhecida. Limitação a registrar na leitura das figuras.
        public const int ValorParaRespostaAusenteOuAmbigua = 0;

        /// <summary>
        /// Respostas tratadas como ausentes/ambíguas, além da célula vazia.
        /// </summary>
        public static readonly string[] RespostasAmbiguas = { "-", "RARO NUNCA" };

        /// <summary>
        /// Respostas que indicam sintoma PRESENTE mas em escala diferente da base, que por isso
        /// não podem cair no valor de ausência.
        /// </summary>
        /// <remarks>
        /// "ALGUNS DIAS DA SEMANA" aparece 5 vezes no arquivo real. Mapeá-la para 0 diria que o
        /// participante não tem o sintoma, quando ele o relatou.
        /// </remarks>
        // EQUIVALÊNCIA ADOTADA: 3 (FREQUENTE). "Alguns dias da semana" descreve recorrência
        // semanal — mais que OCASIONAL (2), que não implica regularidade, e menos que MUITO
        // FREQUENTE (4), que sugere quase todos os dias. É equivalência arbitrada (o instrumento
        // não prevê a resposta), mas de erro limitado: as 5 ocorrências do arquivo real deslocam
        // a severidade em no máximo ±1 ponto cada, num teto de 56.
        public static readonly (string Resposta, int Valor)[] RespostasForaDaEscalaBase =
        {
            ("ALGUNS DIAS DA SEMANA", 3),
        };

        /// <summary>Maior severidade teórica: 14 itens × valor máximo da escala.</summary>
        public const int SeveridadeMaxima = 56;
    }

    /// <summary>
    /// Regras de leitura da Q38 (atribuição do sintoma ao trabalho) e faixas de cor do gráfico.
    /// </summary>
    public static class AtribuicaoDoSintomaAoTrabalho
    {
        /// <summary>
        /// Termo que identifica uma resposta efetiva da Q38, seja ela afirmativa ou negativa.
        /// </summary>
        public const string TermoDeResposta = "DECORRÊNCIA";

        /// <summary>
        /// Termo de negação. Sua presença inverte o sentido de <see cref="TermoDeResposta"/>.
        /// </summary>
        /// <remarks>
        /// A verificação NÃO pode ser feita por "contém 'É EM DECORRÊNCIA'": essa expressão é
        /// substring de "NÃO É EM DECORRÊNCIA", de modo que toda negativa seria contada como
        /// afirmativa. A regra correta é conter <see cref="TermoDeResposta"/> e NÃO conter este
        /// termo — o que também cobre a variante com o erro de digitação "NÃO É EM É DECORRÊNCIA"
        /// presente no arquivo real.
        /// </remarks>
        public const string TermoDeNegacao = "NÃO";

        /// <summary>
        /// Faixas de percentual de sintomas atribuídos ao trabalho, com o rótulo e a cor de cada
        /// uma. O limite é o teto da faixa, exclusivo salvo na última.
        /// </summary>
        /// <remarks>
        /// O ScatterSeries do LiveCharts 0.9.7 não faz gradiente contínuo por ponto; a cor é
        /// definida por série. Por isso os participantes são agrupados em faixas, uma série por
        /// faixa, o que também produz a legenda automaticamente.
        /// </remarks>
        // FAIXAS E TONS ADOTADOS: quatro faixas iguais de 25% com escala do amarelo ao
        // vermelho — quanto mais vermelho, maior a parcela de sintomas que o participante
        // atribui ao trabalho. Quartis são a divisão que menos pressupõe para um percentual sem
        // pontos de corte publicados, e a rampa quente é a convenção semafórica usada nas demais
        // telas do plugin. Decisão de apresentação, sem efeito em cálculo algum.
        public static readonly (double LimiteSuperior, string Rotulo, string CorHex)[] Faixas =
        {
            (0.25, "0–25% atribuído ao trabalho",   "#F2C14E"),
            (0.50, "25–50% atribuído ao trabalho",  "#E8913C"),
            (0.75, "50–75% atribuído ao trabalho",  "#D9622B"),
            (1.00, "75–100% atribuído ao trabalho", "#C0392B"),
        };

        /// <summary>Rótulo da série de participantes sem nenhuma resposta efetiva na Q38.</summary>
        public const string RotuloIndeterminado = "Sem resposta na Q38";

        /// <summary>Cor neutra dos participantes sem resposta efetiva na Q38.</summary>
        public const string CorHexIndeterminado = "#9AA3B2";
    }

    /// <summary>
    /// Regras de leitura da resposta de atividade física (coluna S de a_recrutamento, Q20),
    /// convertida em dias por semana.
    /// </summary>
    /// <remarks>
    /// As respostas são texto livre e aparecem em formatos mistos no arquivo real: "NÃO",
    /// "SIM, 3x/semana", "2 dias", "todos os dias", "4x - Musculação", "5". A leitura normaliza
    /// (trim + maiúsculas) e aplica as regras em ordem — negação, expressões por extenso e, por
    /// fim, o primeiro inteiro do texto.
    /// </remarks>
    public static class RegrasDeAtividadeFisica
    {
        /// <summary>Termo que indica ausência de atividade física.</summary>
        public const string TermoDeNegacao = "NÃO";

        /// <summary>Expressões por extenso e o número de dias que representam.</summary>
        /// <remarks>
        /// Avaliadas antes da extração numérica, porque não contêm dígitos.
        /// </remarks>
        public static readonly (string Expressao, int Dias)[] ExpressoesPorExtenso =
        {
            ("TODOS OS DIAS", 7),
            ("UMA VEZ",       1),
            ("UM VEZ",        1),   // grafia encontrada no arquivo real
        };

        /// <summary>Maior número de dias por semana aceito como resposta válida.</summary>
        public const int DiasMaximosPorSemana = 7;
    }

    /// <summary>
    /// Paleta do gráfico dinâmico da aba de Dados do Grupo: uma cor por combinação de eixos.
    /// </summary>
    /// <remarks>
    /// A cor é escolhida de forma DETERMINÍSTICA a partir do par (índice de X, índice de Y), para que
    /// a mesma combinação sempre saia na mesma cor entre execuções — cor aleatória tornaria a leitura
    /// de duas sessões de trabalho incomparável.
    ///
    /// Com cinco variáveis há 25 combinações e a paleta tem 12 cores, então há repetição. A cor
    /// distingue combinações vizinhas na exploração, não identifica uma combinação de forma única —
    /// o título do cartão é que faz isso.
    /// </remarks>
    // ESQUEMA ADOTADO: cor por COMBINAÇÃO (X,Y). A alternativa — cor por variável do eixo X —
    // daria cinco cores só e faria as cinco combinações de um mesmo X parecerem o mesmo
    // gráfico. Decisão de apresentação, determinística entre execuções, sem efeito em cálculo.
    public static class PaletaDoGraficoDinamicoDoGrupo
    {
        /// <summary>Cores disponíveis, em ordem de atribuição.</summary>
        public static readonly string[] Cores =
        {
            "#0E6B6B", "#2F6BD9", "#7A4FD9", "#B3382F",
            "#1B7A50", "#D9871F", "#C2185B", "#00796B",
            "#5D4037", "#455A64", "#8E24AA", "#F57C00",
        };

        /// <summary>
        /// Devolve a cor da combinação de eixos indicada pelos índices das variáveis na lista.
        /// </summary>
        /// <remarks>
        /// O par é achatado em um único índice (X × total + Y) antes do resto, de modo que trocar X
        /// por Y produza cor diferente — são gráficos diferentes.
        /// </remarks>
        public static string ObterCor(int indiceDoEixoX, int indiceDoEixoY, int totalDeVariaveis)
        {
            if (totalDeVariaveis <= 0)
                return Cores[0];

            int combinacao = (indiceDoEixoX * totalDeVariaveis) + indiceDoEixoY;
            return Cores[System.Math.Abs(combinacao) % Cores.Length];
        }
    }

    /// <summary>
    /// Cores auxiliares do gráfico da aba de Dados do Grupo.
    /// </summary>
    /// <remarks>
    /// A cor dos PONTOS não está aqui: sai de <see cref="PaletaDoGraficoDinamicoDoGrupo"/>, por
    /// combinação de eixos.
    /// </remarks>
    public static class EstiloDosGraficosDoGrupo
    {
        /// <summary>Cor da linha de referência em zero, no eixo dos deltas.</summary>
        public const string CorHexDaLinhaDeReferencia = "#C6C6C6";
    }

    /// <summary>
    /// Rótulos de exibição dos formulários no seletor da aba de Dados do Participante.
    /// </summary>
    /// <remarks>
    /// Apenas o texto exibido; o mapeamento para a planilha de origem continua em
    /// <see cref="Dominio.TipoDeFormulario"/> e no adaptador da tabela.
    /// </remarks>
    // NOME MANTIDO por decisão do TextoFinal de 31/08/2026: a Saída IV do §5.6 lista as sete
    // tabelas nominalmente — "Síntese do Participante; DADOS DO PARTICIPANTE, Dados da Síndrome
    // do Edifício Doente; Índice de Estresse em Enfermeiros; Percepção Ambiental; Níveis de
    // Cortisol; Dados de HMD - RV" — usando para a tabela o mesmo nome da aba que a contém.
    // A coincidência é do texto, não um acidente do código; o seletor dá o contexto.
    public static class RotulosDeFormulario
    {
        /// <summary>Planilha a_recrutamento, sem as questões 37 e 38.</summary>
        public const string Recrutamento = "Dados do Participante";

        /// <summary>Questões 37 e 38 da planilha a_recrutamento.</summary>
        public const string SickBuildingSyndrome = "Dados de Sick Building Syndrome";

        /// <summary>Planilha b_ieee.</summary>
        public const string Ieee = "Índice de Estresse em Enfermeiros";

        /// <summary>Planilha c_percepção_ambiental.</summary>
        public const string PercepcaoAmbiental = "Percepção ambiental";

        /// <summary>Coletas de cortisol salivar.</summary>
        public const string Cortisol = "Níveis de cortisol";

        /// <summary>Síntese cruzada de formulários e cortisol.</summary>
        // Nome CONFIRMADO pelo autor em 31/08/2026: "Síntese do Participante" (havia ficado em
        // branco no pedido original).
        public const string Sintese = "Síntese do Participante";

        /// <summary>
        /// Retrato da coleta em RV, do <c>dados_hmd.csv</c> — o mesmo arquivo já carregado para as
        /// sessões, sem upload adicional.
        /// </summary>
        public const string DadosDoHmd = "Dados do HMD - RV";
    }

    /// <summary>
    /// Parâmetros da categoria "Dados do HMD - RV".
    /// </summary>
    public static class ParametrosDoResumoDoHmd
    {
        /// <summary>
        /// Quantos objetos entram na linha "Objetos mais observados".
        /// </summary>
        /// <remarks>
        /// Cinco cobrem entre 48% e 80% da permanência do olhar de cada participante no arquivo
        /// atual — o suficiente para caracterizar o foco sem transformar a linha em lista longa.
        /// </remarks>
        public const int QuantidadeDeObjetosMaisObservados = 5;
    }

    /// <summary>
    /// Faixas de classificação do IMC segundo a Organização Mundial da Saúde.
    /// </summary>
    public static class FaixasDeImc
    {
        /// <summary>Limite superior do baixo peso (exclusivo).</summary>
        public const double LimiteDeBaixoPeso = 18.5;

        /// <summary>Limite superior do peso normal (inclusivo).</summary>
        public const double LimiteDePesoNormal = 24.9;

        /// <summary>Limite superior do sobrepeso (inclusivo).</summary>
        public const double LimiteDeSobrepeso = 29.9;

        /// <summary>Limite superior da obesidade grau I (inclusivo).</summary>
        public const double LimiteDeObesidadeGrauI = 34.9;

        /// <summary>Limite superior da obesidade grau II (inclusivo).</summary>
        public const double LimiteDeObesidadeGrauII = 39.9;

        /// <summary>Nomes candidatos da coluna de IMC (coluna F de a_recrutamento).</summary>
        public static readonly string[] NomesDaColuna = { "IMC" };
    }

    /// <summary>
    /// Regra de agrupamento da idade em faixas, usada no eixo Y do gráfico de IEEE × idade.
    /// </summary>
    /// <remarks>
    /// A faixa de um participante é <c>floor(idade / tamanho) * tamanho</c>: com tamanho 5,
    /// a idade 46 cai em 45 (rotulada "45–49") e a 32 cai em 30 ("30–34").
    /// </remarks>
    public static class FaixaEtaria
    {
        /// <summary>Amplitude de cada faixa etária, em anos.</summary>
        public const int TamanhoEmAnos = 5;

        /// <summary>Menor idade plausível aceita como resposta válida.</summary>
        public const int IdadeMinimaPlausivel = 15;

        /// <summary>Maior idade plausível aceita como resposta válida.</summary>
        public const int IdadeMaximaPlausivel = 100;
    }

    /// <summary>
    /// Limiares de classificação do escore total do IEEE em três faixas.
    /// </summary>
    /// <remarks>
    /// O escore total varia de <see cref="EscoreMinimo"/> a <see cref="EscoreMaximo"/>.
    /// A classificação usa dois pontos de corte, produzindo as faixas
    /// [mínimo, corte1), [corte1, corte2) e [corte2, máximo].
    /// </remarks>
    /// <remarks>
    /// Faixas conforme a §5.5.2 do TextoFinal: estresse baixo (34–68), intermediário (69–102) e
    /// alto (103–170). São usadas tanto na classificação exibida na tabela-síntese quanto no
    /// segundo filtro da aba de dados do grupo.
    /// </remarks>
    public static class LimiaresDeClassificacaoIeee
    {
        /// <summary>Menor escore total possível do IEEE.</summary>
        public const double EscoreMinimo = 34.0;

        /// <summary>Maior escore total possível do IEEE.</summary>
        public const double EscoreMaximo = 170.0;

        /// <summary>
        /// Primeiro ponto de corte: até ele (inclusive), estresse baixo. Valor: 68.
        /// </summary>
        public const double FimDaFaixaBaixa = 68.0;

        /// <summary>
        /// Segundo ponto de corte: até ele (inclusive), estresse intermediário. Valor: 102.
        /// </summary>
        public const double FimDaFaixaIntermediaria = 102.0;

        /// <summary>Rótulo da faixa de estresse baixo (34–68).</summary>
        public const string RotuloDaFaixaBaixa = "Estresse baixo (34–68)";

        /// <summary>Rótulo da faixa de estresse intermediário (69–102).</summary>
        public const string RotuloDaFaixaIntermediaria = "Estresse intermediário (69–102)";

        /// <summary>Rótulo da faixa de estresse alto (103–170).</summary>
        public const string RotuloDaFaixaAlta = "Estresse alto (103–170)";
    }

    /// <summary>
    /// Regra de agregação usada para reduzir as pontuações de potencial restaurador — que são
    /// calculadas POR OBJETO — a um único PH por participante, exigido pelo eixo Y dos gráficos
    /// de dispersão da §5.5.1 (um ponto por indivíduo).
    /// </summary>
    /// <remarks>
    /// <see cref="Dominio.MetricasPorObjeto.PotencialHomeodinamico"/> existe por objeto e
    /// por cenário; não há no TextoFinal definição de um PH por participante.
    /// </remarks>
    // Regra ADOTADA: média simples das pontuações por objeto, sobre TODOS os cenários do
    // participante — a mesma decisão registrada em CalculadorDePotencialPorParticipante. O
    // §5.6.4 manda "reduzir as pontuações por objeto a um PH único por participante" sem
    // especificar a regra nem restringir o escopo; na dúvida, a média simples é a que menos
    // pressupõe: ponderar por fixações daria a objetos muito olhados o poder de dominar o PH, e
    // com o encolhimento da Camada 1 os objetos pouco observados já são puxados ao neutro, o que
    // atenua o problema que a ponderação corrigiria. As alternativas seguem implementadas em
    // RegraDeAgregacaoDePotencial; trocar exige mudar apenas RegraPadrao.
    public static class AgregacaoDePotencialPorParticipante
    {
        /// <summary>Regra atualmente aplicada aos gráficos de dispersão.</summary>
        public const RegraDeAgregacaoDePotencial RegraPadrao =
            RegraDeAgregacaoDePotencial.MediaDasPontuacoesPorObjeto;
    }

    /// <summary>
    /// Formas possíveis de agregar as pontuações por objeto em um PH único por participante.
    /// </summary>
    public enum RegraDeAgregacaoDePotencial
    {
        /// <summary>Média aritmética simples das pontuações de todos os objetos do participante.</summary>
        MediaDasPontuacoesPorObjeto,

        /// <summary>Mediana das pontuações de todos os objetos do participante.</summary>
        MedianaDasPontuacoesPorObjeto,

        /// <summary>
        /// Média das pontuações por objeto ponderada pelo número de fixações registradas em cada
        /// objeto, de modo que objetos mais observados pesem mais.
        /// </summary>
        MediaPonderadaPorFixacoes
    }
}
