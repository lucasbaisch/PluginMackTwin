namespace PluginCAISM.Configuracao
{
    /// <summary>
    /// Limiares e pesos usados no cálculo de potencial restaurador e na detecção
    /// de eventos oculares. Os valores marcados como "PREENCHER COM LITERATURA"
    /// devem ser substituídos por valores encontrados nos artigos científicos identificados.
    /// </summary>
    /// <remarks>
    /// Referências bibliográficas alvo:
    /// - Teoria da Restauração da Atenção (Rachel e Stephen Kaplan)
    /// - Average fixation duration × restorative potential (Science Direct, Frontiers in Neuroscience)
    /// - Pupil diameter × average fixation duration × restorative potential
    /// - Instrumento de avaliação de estresse indicado pela orientadora Dora
    /// </remarks>
    public static class LimiaresDePotencialRestaurador
    {
        /// <summary>
        /// Duração mínima da janela de fixação focal (ms).
        /// Valor conforme §5.1 do TextoFinal: fixação focal = 300–500 ms.
        /// </summary>
        public const double DuracaoMinimaConfortavelDeFixacaoMs = 300.0;

        // DuracaoMaximaConfortavelDeFixacaoMs foi REMOVIDA. Ficara órfã desde a saída da
        // normalização min-max, e uma constante documentada e sem consumidor é uma promessa que o
        // código não cumpre: sugeria que fixações acima de 500 ms recebiam algum tratamento, o que
        // nunca aconteceu.
        //
        // No modelo em duas camadas a duração serve como CRITÉRIO DE DETECÇÃO — o piso de 300 ms
        // em ParametrosDeDeteccaoOcular.DuracaoMinimaDeFixacaoMs — e não como sinal: o índice
        // deriva do ΔD pupilar. O §5.1 do TextoFinal precisa dizer isso, em vez de descrever
        // fixações longas como indicativas de afeto negativo sem especificar tratamento.

        /// <summary>
        /// Limiar de variação pupilar em mm para classificar uma fixação como Dilatada ou Contraída.
        /// Se a pupila variar mais que este valor entre início e fim da fixação, classifica como
        /// Dilatada (positivo) ou Contraída (negativo).
        /// </summary>
        /// <remarks>
        /// A classificação é apenas de direção da variação; a leitura fisiológica está documentada
        /// em <see cref="Dominio.TendenciaPupilar"/> — constrição indica potencial restaurador
        /// (§5.2, Quadros 36 e 40 do TextoFinal).
        /// </remarks>
        public const double LimiarDeDilatacaoPupilarMm = 0.05;

        /// <summary>
        /// Início da janela pré-estímulo (em ms a partir do início da sessão) usada para calcular
        /// a linha de base pupilar de cada sessão. Valor: 0 ms (00:00).
        /// </summary>
        public const double JanelaDeBaselinePupilarInicioMs = 0.0;

        /// <summary>
        /// Fim da janela pré-estímulo (em ms a partir do início da sessão) usada para calcular
        /// a linha de base pupilar de cada sessão. Valor: 60.000 ms (01:00).
        /// </summary>
        /// <remarks>
        /// A baseline é a mediana do diâmetro pupilar médio das amostras válidas desta janela,
        /// conforme a correção subtrativa ΔD = D_t − baseline (§5.2 do TextoFinal). Note que a
        /// detecção de eventos usa a janela dos minutos 2–4
        /// (<see cref="ParametrosDeDeteccaoOcular.JanelaTemporalInicioMs"/>), disjunta desta.
        /// </remarks>
        public const double JanelaDeBaselinePupilarFimMs = 60_000.0;

        // Os três pesos do modelo ponderado (w1 duração, w2 pupila, w3 ambiental) foram REMOVIDOS
        // junto com a normalização min-max que os consumia. O índice passou a ser o PH_e da
        // Equação 4, que não combina sub-índices: deriva do efeito de elemento ν̂_e. Reintroduzi-los
        // exigiria reintroduzir um segundo índice concorrente.

        /// <summary>
        /// Fração da largura de cada faixa confortável usada como margem de decaimento linear
        /// do sub-índice de conforto ambiental fora da faixa (banda dupla). Ex.: 0,5 significa que
        /// a pontuação chega a 0,0 a meia largura da faixa além de cada borda.
        /// CONFIRMAR COM ARTIGOS.
        /// </summary>
        public const double FracaoDeMargemDeDesconfortoAmbiental = 0.5;
    }

    /// <summary>
    /// Faixas de conforto das variáveis ambientais medidas pelo sensor (Input 03), usadas no
    /// cálculo de <see cref="Dominio.MetricasPorObjeto.PontuacaoAmbiental"/>.
    /// </summary>
    /// <remarks>
    /// Valores conforme a Tabela 5 (§3.1.5) do TextoFinal de 31/08/2026:
    /// temperatura 21–26 °C (ABNT NBR 17037:2023), umidade relativa 35–65% (idem),
    /// ruído ≤ 50 dB diurno / ≤ 45 dB noturno (ABNT NBR 10151:2019). Valores dentro da faixa
    /// são considerados confortáveis. As mesmas faixas são repetidas nos §5.6 e §6 do texto.
    /// </remarks>
    // RESOLVIDO pelo TextoFinal de 31/08/2026. As faixas de conforto valem como acima: o texto
    // as declara três vezes com os mesmos números (Tabela 5 do §3.1.5, §5.6 e §6 — "temperatura,
    // de 21 °C a 26 °C, a umidade relativa, de 35% a 65%, o nível de ruído, de até 50 dB(A) no
    // período diurno e 45 dB(A) no noturno"). Os números divergentes ("abaixo de 18 / acima de
    // 25 °C", "acima de 65 dB") aparecem apenas no quadro de CLASSIFICAÇÃO do potencial, como
    // características interpretativas das classes — mesma natureza já resolvida para o Quadro 41
    // em ClassificacaoDePotencialDeHitpoint: não são condições de cálculo, e a classificação
    // decide-se só pelo IC85% do efeito encolhido. A antiga "Tabela 31" citada aqui mudou de
    // conteúdo na renumeração (hoje é a estrutura de diretórios dos dados sintéticos).
    public static class FaixasDeConfortoAmbiental
    {
        /// <summary>Temperatura mínima confortável (°C). Tabela 5: 21 °C.</summary>
        public const double TemperaturaMinima = 21.0;

        /// <summary>Temperatura máxima confortável (°C). Tabela 5: 26 °C.</summary>
        public const double TemperaturaMaxima = 26.0;

        /// <summary>Umidade relativa mínima confortável (%). Tabela 5: 35%.</summary>
        public const double UmidadeMinima = 35.0;

        /// <summary>Umidade relativa máxima confortável (%). Tabela 5: 65%.</summary>
        public const double UmidadeMaxima = 65.0;

        /// <summary>
        /// Nível de ruído mínimo confortável (dB). Não há piso de desconforto na Tabela 5,
        /// portanto 0 dB.
        /// </summary>
        public const double RuidoMinimo = 0.0;

        /// <summary>
        /// Nível de ruído máximo confortável (dB) no período diurno. Tabela 5: ≤ 50 dB.
        /// </summary>
        public const double RuidoMaximoDiurno = 50.0;

        /// <summary>
        /// Nível de ruído máximo confortável (dB) no período noturno. Tabela 5: ≤ 45 dB.
        /// </summary>
        /// <remarks>
        /// Aplicado quando TODOS os cenários em que o elemento foi observado terminam no
        /// período "Noite" (último segmento do rótulo "cena_período", ex.: "Cenário_01_Noite");
        /// havendo qualquer cenário diurno ou período indeterminável, vale o limite diurno.
        /// A regra da unanimidade evita afirmar o limite mais restritivo sobre leituras mistas.
        /// Convenção do rótulo confirmada nas capturas definitivas (cenario_1/2.csv).
        /// </remarks>
        public const double RuidoMaximoNoturno = 45.0;

        // As constantes MonoxidoDeCarbonoMinimo/Maximo (0–35 ppm) foram REMOVIDAS. Estavam sem
        // consumidor, e o TextoFinal de 31/08/2026 encerrou a questão: o módulo de monitoramento
        // usa o sensor MQ7 com SAÍDA DIGITAL por limiar de circuito (HANWEI ELECTRONICS, 2026) —
        // presença/ausência, nunca concentração —, de modo que não há ppm a confrontar com
        // faixa. As referências normativas (ANVISA RE nº 9/2003: ≤ 9 ppm por 8h, ≤ 35 ppm por
        // 1h) seguem citadas na Tabela 5 do texto, que é onde pertencem.
    }

    /// <summary>
    /// Mapeamento entre os nomes dinâmicos das colunas do CSV de sensores (Input 03) e os quatro
    /// parâmetros ambientais avaliados pelo sub-índice de conforto.
    /// </summary>
    /// <remarks>
    /// <b>A correspondência é EXATA</b> (ignorando apenas caixa e espaços nas pontas), e não mais
    /// por trecho de string. O cabeçalho do Input 03 é fixo e conhecido — é o mesmo que o
    /// simulador do Grasshopper escreve, em <c>EnvironmentCsvWriter.ColumnNames</c>:
    ///
    ///   DATA: YYYY-MM-DD HH:MM:SS | DISPOSITIVO | DNS REDE | TEMPO ATIVIDADE | MAC ADDRESS |
    ///   RESET | ALIMENTAÇÃO | TEMPERATURA | UMIDADE | CO | RUÍDO
    ///
    /// A busca por conteúdo existia para tolerar variações como "Temperatura (°C)", mas cobrava
    /// caro: "CO" casa como trecho de qualquer cabeçalho que contenha essas duas letras, o que
    /// tornava a coluna de gás impossível de mapear com segurança. Com o cabeçalho fixo, a
    /// igualdade exata é ao mesmo tempo mais simples e mais correta.
    ///
    /// <b>Consequência:</b> um arquivo cujo cabeçalho fuja desta lista deixa de ser reconhecido,
    /// em vez de casar por acaso. É o comportamento desejado — errar por falta é diagnosticável,
    /// errar por excesso não.
    /// </remarks>
    public static class MapeamentoDeColunasDeSensor
    {
        /// <summary>
        /// Nomes candidatos para a coluna de data/hora, usada como chave de junção com o Input 01.
        /// </summary>
        /// <remarks>
        /// No arquivo real da pesquisa a coluna se chama "DATA: YYYY-MM-DD HH:MM:SS"; "timeStamp"
        /// é mantido por ser o nome usado no CSV de rastreamento ocular e em exportações antigas.
        /// </remarks>
        public static readonly string[] Timestamp =
        {
            "DATA: YYYY-MM-DD HH:MM:SS"
        };

        /// <summary>Nomes candidatos para a coluna de temperatura do ar.</summary>
        /// <remarks>No arquivo real: "TEMPERATURA".</remarks>
        public static readonly string[] Temperatura = { "TEMPERATURA" };

        /// <summary>Nomes candidatos para a coluna de umidade relativa do ar.</summary>
        /// <remarks>No arquivo real: "UMIDADE".</remarks>
        public static readonly string[] Umidade = { "UMIDADE"};

        /// <summary>Nomes candidatos para a coluna de nível de pressão sonora (ruído).</summary>
        /// <remarks>No arquivo real: "RUÍDO (dB)".</remarks>
        public static readonly string[] Ruido = { "RUÍDO" };

        /// <summary>
        /// Nomes candidatos para a coluna de monóxido de carbono, que é um INDICADOR DE PRESENÇA
        /// e não uma concentração: 1 = detectado, 0 = não detectado.
        /// </summary>
        /// <remarks>
        /// Substituiu duas entradas anteriores — <c>MonoxidoDeCarbono</c>, que tratava a coluna
        /// como concentração em ppm, e <c>PresencaDeGas</c>, que procurava o cabeçalho
        /// "Detectado = 0,162 / Não detectado = 0,83" de uma planilha antiga. Nenhuma das duas
        /// casava com o arquivo atual, cujo cabeçalho é apenas "CO": a primeira pontuava 0 e 1
        /// como se fossem ppm dentro da faixa de conforto, e a segunda não encontrava coluna
        /// alguma, de modo que a tabela exibia "NÃO DETECTADO" mesmo com o gás presente.
        ///
        /// Sensor nenhum da pesquisa mede concentração, então não há faixa de conforto a aplicar
        /// aqui: a presença é reportada como rótulo na tabela e entra no ajuste da Camada 1 como
        /// covariável binária.
        /// </remarks>
        public static readonly string[] PresencaDeCo = { "CO" };
    }

    /// <summary>
    /// Interpretação da coluna de presença de gás do arquivo de sensores (coluna F).
    /// </summary>
    /// <remarks>
    /// <b>A coluna é de gás, não de ruído.</b> O nível sonoro está na coluna vizinha, sob o
    /// cabeçalho "RUÍDO (dB)", e é lido por <see cref="MapeamentoDeColunasDeSensor.Ruido"/>.
    ///
    /// Os valores seguem a legenda do próprio cabeçalho: <c>0,162</c> indica presença e
    /// <c>0,83</c> indica ausência. Nas duas primeiras abas do arquivo real <c>0,83</c> aparece em
    /// 100% das 63.746 leituras — ou seja, nenhuma presença de gás foi registrada nelas —, e o
    /// valor <c>0,162</c> não ocorre nenhuma vez.
    ///
    /// Uma versão anterior adotava a leitura inversa, apoiada em que <c>0,83</c> acompanhava um
    /// nível sonoro médio de 76,4 dB contra 30,1 dB (piso do sensor) onde a coluna valia <c>0</c>.
    /// Esse argumento caiu quando a coluna foi identificada como de gás: nível sonoro nada diz
    /// sobre concentração de gás, e a legenda do cabeçalho passou a ser a única evidência.
    /// </remarks>
    // RESOLVIDO pelo TextoFinal de 31/08/2026 (§ do módulo de monitoramento): "o valor 0 indica
    // a não detecção ... e o valor 1 a sua detecção pelo sensor Arduino MQ7", cuja "saída
    // digital opera por comparação com um limiar fixado em circuito (HANWEI ELECTRONICS, 2026)".
    // A variável é, portanto, BINÁRIA por construção do sensor — não há concentração a
    // interpretar. Coexistem duas codificações nos arquivos: 0/1 (a declarada pelo texto) e a
    // legenda 0,162 = detectado / 0,83 = não detectado dos arquivos com legenda no cabeçalho.
    // A interpretação das duas está centralizada em ParametrosDePresencaDeGas.IndicaPresenca —
    // único ponto do código que decide o estado.
    /// <summary>
    /// Regra de associação entre a amostra ocular e a leitura ambiental.
    /// </summary>
    /// <remarks>
    /// A associação é feita pelo SEGUNDO em que a amostra ocorreu: os dois instantes são truncados
    /// ao segundo e comparados. O rastreador grava a cada ~65 ms e o sensor em segundos cheios;
    /// comparar ao milissegundo casava 1 amostra em 53.071.
    /// </remarks>
    public static class ParametrosDeAssociacaoDeSensor
    {
        /// <summary>
        /// Janela, em segundos, para aceitar a leitura mais próxima quando não há leitura no
        /// segundo exato da amostra. Zero exige coincidência de segundo.
        /// </summary>
        // VALOR ADOTADO: 60 s. O §5.6.4 do TextoFinal manda enriquecer cada amostra "com a
        // leitura ambiental do segundo correspondente" e não fixa tolerância; a decisão é,
        // portanto, de protocolo. O sensor real grava a cada ~45 s, então 60 s garante que
        // SEMPRE exista uma leitura vigente dentro da janela (a mais próxima, antes ou depois),
        // enquanto tolerância 0 descartava a maior parte das fixações. As variáveis cobertas —
        // temperatura, umidade, ruído em média de minuto e presença de CO — não mudam de estado
        // em menos de um minuto de forma relevante para a Equação 3, de modo que a leitura
        // vizinha é representativa. No arquivo sintético (uma leitura por segundo) a tolerância
        // não chega a ser usada: o segundo exato sempre existe.
        public const int ToleranciaEmSegundos = 60;
    }

    /// <summary>
    /// Parâmetros da Camada 1 do Modelo Estatístico de Potencial Homeodinâmico (Equação 3, §5.3.1).
    /// </summary>
    public static class ParametrosDaCamada1
    {
        /// <summary>
        /// Mínimo de pares consecutivos exigido para estimar a autocorrelação AR(1) dos resíduos.
        /// </summary>
        /// <remarks>
        /// Com poucos pares a estimativa de rho é instável, e um rho espúrio alto encolheria o
        /// tamanho amostral efetivo o bastante para tornar todos os elementos neutros. Abaixo
        /// deste limite adota-se rho = 0, isto é, nenhuma correção.
        /// </remarks>
        public const int MinimoDeParesParaAutocorrelacao = 30;

        /// <summary>
        /// Teto para a autocorrelação estimada dos resíduos.
        /// </summary>
        /// <remarks>
        /// O tamanho amostral efetivo n·(1−rho)/(1+rho) tende a zero quando rho se aproxima de 1,
        /// o que faria o erro-padrão divergir. O teto mantém a correção conservadora sem torná-la
        /// degenerada: em 0,95 o n efetivo já é cerca de 2,6% do nominal.
        /// </remarks>
        public const double AutocorrelacaoMaxima = 0.95;

        /// <summary>
        /// Mínimo de elementos com erro-padrão estimável para que a dispersão entre elementos
        /// (τ̂_ν) seja calculada.
        /// </summary>
        /// <remarks>
        /// τ̂_ν é a variância dos efeitos ENTRE elementos, estimada por método dos momentos:
        /// variância das médias brutas menos a média das variâncias amostrais. Com um único
        /// elemento não há "entre" a medir; abaixo deste mínimo, τ̂_ν sai indefinido, o
        /// encolhimento é desativado (λ = 1) e o índice degrada para o ponto neutro, com aviso.
        /// </remarks>
        public const int MinimoDeElementosParaDispersao = 2;
    }

    /// <summary>
    /// Interpretação da coluna "CO", que é um indicador de dois estados.
    /// </summary>
    /// <remarks>
    /// Os valores anteriores eram as strings "0.162" (presença) e "0.83" (ausência), herdadas de
    /// uma planilha antiga em que a legenda vinha embutida no cabeçalho. O arquivo atual grava
    /// 0 e 1, e a comparação era feita por texto — de modo que "1" nunca era igual a "0.162" e a
    /// presença jamais era reconhecida.
    ///
    /// A comparação passou a ser NUMÉRICA para não depender de formatação: o Excel pode devolver
    /// a mesma célula como "1", "1,00" ou "1.00" conforme a localidade em que a planilha foi
    /// salva, e as três precisam significar a mesma coisa.
    /// </remarks>
    public static class ParametrosDePresencaDeGas
    {
        /// <summary>Valor da coluna que se interpreta como "há presença de gás".</summary>
        public const double ValorQueIndicaPresenca = 1.0;

        /// <summary>Valor da coluna que se interpreta como "não há presença de gás".</summary>
        public const double ValorQueIndicaAusencia = 0.0;

        /// <summary>
        /// Ponto de corte entre os dois estados na codificação 0/1. Um valor lido acima disto
        /// conta como presença.
        /// </summary>
        /// <remarks>
        /// Existe para que a leitura não dependa de igualdade exata em ponto flutuante. Fica na
        /// metade do intervalo entre os dois estados, de forma que só 0 e 1 exatos ocorrem na
        /// prática e qualquer ruído de arredondamento cai do lado certo.
        /// </remarks>
        public const double LimiarDePresenca = 0.5;

        /// <summary>Valor-legenda de "Detectado" nos arquivos com legenda no cabeçalho.</summary>
        public const double ValorDeLegendaParaPresenca = 0.162;

        /// <summary>Valor-legenda de "Não detectado" nos arquivos com legenda no cabeçalho.</summary>
        public const double ValorDeLegendaParaAusencia = 0.83;

        /// <summary>Tolerância de comparação com os valores-legenda.</summary>
        private const double ToleranciaDeLegenda = 0.01;

        /// <summary>
        /// Interpreta o valor da coluna "CO" como presença (true) ou ausência (false) de gás —
        /// o ÚNICO ponto do código que decide o estado.
        /// </summary>
        /// <remarks>
        /// Reconhece as duas codificações em uso: primeiro os valores-legenda 0,162 (detectado)
        /// e 0,83 (não detectado) dos arquivos com legenda embutida no cabeçalho, comparados com
        /// tolerância; depois a codificação 0/1 declarada pelo TextoFinal para o sensor MQ7, por
        /// limiar em 0,5. A ordem importa: sob o limiar sozinho, 0,83 sairia como presença —
        /// o INVERSO do que a legenda afirma.
        /// </remarks>
        public static bool IndicaPresenca(double valor)
        {
            if (double.IsNaN(valor))
                return false;

            if (System.Math.Abs(valor - ValorDeLegendaParaPresenca) <= ToleranciaDeLegenda)
                return true;

            if (System.Math.Abs(valor - ValorDeLegendaParaAusencia) <= ToleranciaDeLegenda)
                return false;

            return valor > LimiarDePresenca;
        }

        /// <summary>
        /// Normaliza o valor da coluna "CO" para a covariável binária da Camada 1: 1 quando há
        /// presença, 0 quando não há, NaN quando não houve leitura.
        /// </summary>
        /// <remarks>
        /// A Equação 3 usa CO como covariável BINÁRIA (§ do módulo de monitoramento do
        /// TextoFinal). Sem esta normalização, um arquivo codificado como 0,162/0,83 entraria na
        /// regressão como número contínuo — e com o sinal invertido.
        /// </remarks>
        public static double NormalizarParaBinario(double valor)
        {
            if (double.IsNaN(valor))
                return double.NaN;

            return IndicaPresenca(valor) ? ValorQueIndicaPresenca : ValorQueIndicaAusencia;
        }
    }

    /// <summary>
    /// Parâmetros de detecção de eventos oculares (fixações e saccades).
    /// </summary>
    public static class ParametrosDeDeteccaoOcular
    {
        /// <summary>
        /// Dispersão angular máxima intra-fixação, em graus de ângulo visual — o critério do
        /// I-DT na sua formulação original: "dispersão de 1°" (HOLMQVIST et al., 2011, p. 435,
        /// citado no §3.1.4 do TextoFinal).
        /// </summary>
        /// <remarks>
        /// O critério espacial é ANGULAR porque o tremor fisiológico do olho é angular: o mesmo
        /// 1° varre ~1,7 cm num alvo a 1 m e ~5 cm num alvo a 3 m. Um raio métrico fixo quebra
        /// fixações reais em superfícies distantes e aceita deriva em superfícies próximas.
        /// A distância observador–alvo vem de playerPosition e do ponto de incidência, presentes
        /// nas capturas; sem ela, valem os raios métricos de fallback abaixo.
        /// </remarks>
        public const double DispersaoAngularMaximaGraus = 1.0;

        /// <summary>
        /// Metros de raio de dispersão por metro de distância do alvo: tan(1°) ≈ 0,017455.
        /// Pré-calculado porque <c>Math.Tan</c> não é expressão constante.
        /// </summary>
        public const double RaioPorMetroDeDistancia = 0.017455;

        /// <summary>
        /// Razão entre o raio de dispersão do grupo e a tolerância de passo entre quadros
        /// consecutivos. Preserva a proporção histórica dos valores métricos (0,03 m / 0,01 m):
        /// o passo tolera 1/3 da dispersão total, acomodando o micromovimento sem admitir deriva.
        /// </summary>
        public const double RazaoEntreRaioDeDispersaoEPasso = 3.0;

        /// <summary>
        /// FALLBACK da tolerância de passo entre quadros (metros, espaço virtual Unity), usado
        /// quando a distância observador–alvo não é computável (playerPosition ausente ou zerado,
        /// como em capturas antigas e nas amostras sintéticas da verificação). Equivale ao passo
        /// angular a ~1,7 m do alvo.
        /// </summary>
        public const double ToleranciaDeEstabilidadeMetros = 0.01;

        /// <summary>
        /// Distância máxima, em metros, entre um ponto de incidência e o centro corrente do grupo
        /// para que ele continue no mesmo grupo de estabilidade.
        /// </summary>
        /// <remarks>
        /// <b>Converte o detector de "velocidade ingênua" em "dispersão".</b> Sem este limite, só
        /// o passo entre quadros vizinhos é verificado, e um olhar que se desloque pouco a pouco
        /// percorre distâncias grandes sem nunca quebrar o grupo: a 0,9 cm por quadro, sessenta
        /// quadros somam mais de meio metro e continuam sendo, para o algoritmo, uma única
        /// fixação — que pode ter atravessado vários elementos.
        ///
        /// É a verificação que os algoritmos de dispersão (I-DT) fazem sobre o grupo inteiro, e
        /// que distingue fixação de deriva lenta.
        /// </remarks>
        // PAPEL ATUAL: FALLBACK. Desde 31/08/2026 o critério primário de dispersão é ANGULAR
        // (DispersaoAngularMaximaGraus = 1°, escalado pela distância observador–alvo); este raio
        // métrico só é usado quando a distância não é computável — playerPosition ausente ou
        // zerado, caso das capturas antigas e das amostras sintéticas da verificação. O valor
        // 0,03 m é o próprio critério angular congelado na distância típica de observação:
        // 1° a ~1,7 m (tan(1°) ≈ 0,0175 m/m), e 3× a tolerância de passo, como antes.
        public const double RaioMaximoDeDispersaoMetros = 0.03;

        /// <summary>
        /// Múltiplo do intervalo mediano de amostragem do grupo acima do qual uma lacuna temporal
        /// quebra a fixação, mesmo que o olhar esteja no mesmo lugar antes e depois.
        /// </summary>
        /// <remarks>
        /// As amostras inválidas — piscadas, sobretudo — são retiradas antes do agrupamento, o que
        /// torna vizinhas na lista duas amostras separadas por centenas de milissegundos no
        /// relógio. Sem esta verificação, uma piscada no meio de duas fixações no mesmo ponto
        /// produz um único evento que atravessa a piscada e soma as três durações.
        ///
        /// Expresso como múltiplo do intervalo observado, e não em milissegundos fixos, porque a
        /// taxa de amostragem varia entre arquivos: as capturas antigas gravam a cada ~65 ms e as
        /// atuais a cada ~33 ms.
        /// </remarks>
        // VALOR ADOTADO: 2,5. Âncora quantitativa: a prática da área tolera lacunas curtas
        // (~75 ms, tipicamente atribuídas a perda de rastreio) e interrompe o evento nas longas
        // (piscadas duram 100–400 ms). Com a cadência atual de ~33 ms, 2,5× o intervalo mediano
        // corta em ~83 ms — dois quadros perdidos ainda mantêm a fixação, três a quebram, e uma
        // piscada sempre a quebra; nas capturas antigas de ~65 ms o corte fica em ~163 ms,
        // preservando a mesma regra de dois quadros. O TextoFinal não trata de lacunas; a
        // escolha fica registrada aqui como decisão de implementação.
        public const double MultiplicadorDeLacunaQueQuebraFixacao = 2.5;

        /// <summary>
        /// Duração mínima em ms para classificar um evento como Fixação.
        /// Eventos com duração ≥ este valor são fixações. Corresponde ao piso da janela de
        /// fixação focal de 300–500 ms (§5.1 do TextoFinal).
        /// </summary>
        /// <remarks>
        /// A detecção não aplica teto, e não há tratamento diferenciado para fixações longas: a
        /// duração é critério de detecção, não sinal. O índice deriva do ΔD pupilar, de modo que
        /// uma fixação de dois segundos entra no cálculo como qualquer outra.
        /// </remarks>
        public const double DuracaoMinimaDeFixacaoMs = 300.0;

        /// <summary>
        /// Janela de início (em ms a partir do início da sessão) usada no filtro temporal.
        /// Apenas linhas com tempo decorrido ≥ este valor são consideradas na detecção.
        /// </summary>
        public const double JanelaTemporalInicioMs = 120_000.0; // 2 min

        /// <summary>
        /// Janela de fim (em ms a partir do início da sessão) usada no filtro temporal.
        /// Apenas linhas com tempo decorrido ≤ este valor são consideradas na detecção.
        /// </summary>
        public const double JanelaTemporalFimMs = 240_000.0; // 4 min

        /// <summary>
        /// Duração mínima em ms para classificar um evento como Saccade.
        /// Faixa de saccade conforme a Figura 46 do TextoFinal: 30–80 ms.
        /// </summary>
        /// <summary>
        /// Abertura ocular mínima, em [0, 1], para a linha contar como olho aberto.
        /// </summary>
        /// <remarks>
        /// As colunas <c>left/right_EyeOpened</c> são contínuas, não binárias. Nas capturas
        /// reais nenhuma amostra de piscada passa de 0,35 e nenhuma amostra normal fica abaixo
        /// de 0,55, então 0,5 separa as duas populações com folga dos dois lados.
        /// </remarks>
        public const double LimiarDeOlhoAberto = 0.5;

        public const double DuracaoMinimaDeSaccadeMs = 30.0;

        /// <summary>
        /// Duração máxima em ms para classificar um evento como Saccade.
        /// Faixa de saccade conforme a Figura 46 do TextoFinal: 30–80 ms.
        /// </summary>
        public const double DuracaoMaximaDeSaccadeMs = 80.0;

        /// <summary>
        /// Número de amostras nas bordas do grupo (início e fim) usadas para calcular
        /// a tendência pupilar de uma fixação. O valor real usado é min(NumeroDeBordasParaTendenciaPupilar,
        /// totalDeLinhas / 2) para evitar sobreposição em eventos curtos.
        /// </summary>
        public const int NumeroDeBordasParaTendenciaPupilar = 3;
    }

    /// <summary>
    /// Constantes de conversão de coordenadas entre o espaço virtual Unity e o Revit.
    /// </summary>
    /// <remarks>
    /// Unity usa metros, sistema destro com Y para cima (X=direita, Y=cima, Z=frente).
    /// Revit usa pés, sistema destro com Z para cima (X=direita, Y=frente, Z=cima).
    /// O mapeamento de eixos padrão é: Unity(X,Y,Z) → Revit(X, Z, Y).
    /// Se o modelo foi exportado com orientação diferente, ajustar o mapeamento em
    /// <see cref="Revit.ConversorDeCoordenadas"/> e documentar aqui.
    /// </remarks>
    public static class ConversoesDeUnidade
    {
        /// <summary>Fator de conversão de metros (Unity) para pés (Revit). Valor: 1 / 0.3048.</summary>
        public const double MetrosParaPes = 1.0 / 0.3048;

        // MargemDaSectionBoxMetros foi removida: a vista 3D passou a ser gerada sem caixa de
        // corte (§5.5.1), portanto não há mais bounding box a margear.
    }

    /// <summary>
    /// Configurações relacionadas à família Revit de ponto de incidência (hitpoint.rfa).
    /// </summary>
    /// <remarks>
    /// A família é uma Generic Model com os seguintes parâmetros de instância:
    ///   "Cenário"                  — nome do cenário VR (texto)
    ///   "Raio"                     — raio visual: constante <see cref="RaioFixo"/> (número)
    ///   "Dilatação Média da Pupila"— média bienocular do diâmetro pupilar em mm (número)
    ///   "Duração do Fixation"      — duração da fixação em ms (número)
    ///
    /// O que diferencia visualmente os hitpoints é a COR (verde = potencial alto,
    /// vermelho = potencial baixo), conforme o Quadro 40, aplicada por
    /// <see cref="Autodesk.Revit.DB.OverrideGraphicSettings"/> na vista gerada. O raio é
    /// constante e não codifica mais a duração da fixação; "Duração do Fixation" e
    /// "Dilatação Média da Pupila" permanecem como metadados informativos da instância.
    ///
    /// A elevação (Base Offset) é controlada pela coordenada Z do ponto de inserção,
    /// que é derivada do eixo Y do Unity (Unity Y → Revit Z) via ConversorDeCoordenadas.
    /// </remarks>
    public static class ConfiguracaoDaFamiliaHitpoint
    {
        /// <summary>Nome do arquivo da família Revit.</summary>
        /// <remarks>
        /// <b>O nome do arquivo determina o nome da família no modelo.</b> O Revit batiza a família
        /// pelo arquivo carregado, e é por esse nome que
        /// <see cref="Revit.InsersorDaFamiliaHitpoint"/> a procura no documento. Por isso o nome
        /// não muda quando o .rfa é atualizado: renomeá-lo não renomeia a família já presente nos
        /// modelos — faz o Revit CRIAR UMA SEGUNDA, e o modelo passa a exibir as esferas antigas.
        /// Uma versão nova da família entra sobrescrevendo este mesmo arquivo.
        /// </remarks>
        public const string NomeDoArquivoDaFamilia = "hitpoint.rfa";

        /// <summary>
        /// Caminho da família relativo à pasta de instalação do plugin.
        /// O MSBuild copia Assets\hitpoint.rfa para a pasta de saída preservando a estrutura.
        /// </summary>
        /// <remarks>
        /// Derivado de <see cref="NomeDoArquivoDaFamilia"/>, e não escrito à mão: as duas constantes
        /// já divergiram uma vez — o plugin carregava um arquivo e procurava a família por outro
        /// nome —, e a concatenação torna essa classe de erro impossível.
        /// </remarks>
        public const string CaminhoRelativoDaFamilia = @"Assets\" + NomeDoArquivoDaFamilia;

        // ── Nomes dos parâmetros de instância na família ─────────────────

        /// <summary>Parâmetro: nome do cenário de RV ao qual o hitpoint pertence.</summary>
        public const string NomeDoParametroCenario = "Cenário";

        /// <summary>
        /// Parâmetro: raio visual do hitpoint. Recebe sempre <see cref="RaioFixo"/>.
        /// </summary>
        public const string NomeDoParametroRaio = "Raio";

        /// <summary>Parâmetro: dilatação média bienocular da pupila (mm) durante a fixação.</summary>
        public const string NomeDoParametroDilatacaoPupila = "Dilatação Média da Pupila";

        /// <summary>Parâmetro: duração do fixation em milissegundos.</summary>
        public const string NomeDoParametroDuracaoFixation = "Duração do Fixation";

        // ── Tipos da família por classificação de potencial (Quadro 41) ───
        // A cor de cada hitpoint vem do MATERIAL do tipo da família, não de override de vista.
        // Assim ela é uma propriedade real do elemento: sobrevive a qualquer estilo visual
        // (inclusive Realista), a qualquer vista, à aplicação de templates e à exportação, e
        // aparece em tabelas e filtros do Revit.
        //
        // Os tipos já existem no hitpoint.rfa, cada um com seu material:
        //   "Potencial Restaurador" → verde
        //   "Potencial Estressor"   → vermelho
        //   "Potencial Neutro"      → cinza

        /// <summary>
        /// Nome do tipo da família usado para objetos restauradores (material VERDE).
        /// </summary>
        /// <remarks>
        /// Os três nomes abaixo correspondem exatamente aos tipos existentes no
        /// <c>hitpoint.rfa</c> e ao vocabulário do Quadro 41. Se um deles for renomeado na
        /// família, a carga falha com mensagem explícita listando os tipos ausentes — de
        /// propósito, para não inserir esferas na cor errada silenciosamente.
        /// </remarks>
        public const string NomeDoTipoPotencialRestaurador = "Potencial Restaurador";

        /// <summary>
        /// Nome do tipo da família usado para objetos estressores (material VERMELHO).
        /// </summary>
        public const string NomeDoTipoPotencialEstressor = "Potencial Estressor";

        /// <summary>
        /// Nome do tipo da família usado para objetos neutros e para os que não têm pontuação
        /// (material CINZA). As duas situações são visualmente idênticas no modelo; a distinção
        /// existe apenas na análise e na tabela da aba de Potencial Homeodinâmico.
        /// </summary>
        public const string NomeDoTipoPotencialNeutro = "Potencial Neutro";

        // ── Raio ─────────────────────────────────────────────────────────
        // O raio deixou de codificar a duração da fixação: a diferenciação visual passou a ser
        // por cor, via tipo da família (Quadro 40).

        /// <summary>
        /// Raio visual aplicado a TODOS os hitpoints, independentemente da duração da fixação.
        /// Corresponde ao antigo raio base (duração mínima de fixação, 300 ms).
        /// </summary>
        // RESOLVIDO pela revisão do TextoFinal de 31/08/2026: o Requisito 11 passou a descrever
        // exatamente esta implementação — "verde para restaurador, vermelho para estressor e
        // cinza para neutro ou sem pontuação, parametrizar cenário, RAIO CONSTANTE, dilatação
        // pupilar média e duração da fixação". Texto e código dizem a mesma coisa.
        public const double RaioFixo = 0.1;

        /// <summary>Prefixo do nome das vistas 3D geradas pelo plugin.</summary>
        public const string PrefixoDeNomeDeVista = "CAISM";

        // Não existe lista de "famílias obsoletas a remover", nem remoção automática de família
        // alguma: apagar uma família apaga TODOS os hitpoints do modelo, de todos os cenários.
        // Famílias que sobrem de nomes anteriores (ex.: CAISM_hitpoint, de uma tentativa de rename)
        // são removidas pelo usuário, no Project Browser.
    }

    /// <summary>
    /// Composição do nome das vistas 3D geradas:
    /// <c>CAISM_{cenário}_v{NN}_{aaaa-MM-dd_HHmm}</c>.
    /// </summary>
    /// <remarks>
    /// As duas partes respondem a perguntas diferentes e por isso convivem: o <b>vNN</b> diz qual é
    /// a mais recente sem exigir leitura de data (v03 &gt; v02 é auto-evidente), e o <b>carimbo</b>
    /// registra quando aquela figura foi produzida — informação de rastreabilidade da pesquisa.
    ///
    /// Os dois dígitos da versão são o que faz a ordenação alfabética do Project Browser coincidir
    /// com a ordem cronológica: sem o zero à esquerda, "v10" apareceria antes de "v9".
    ///
    /// A versão NÃO é reiniciada quando a geração anterior é apagada — ela conta quantas vezes o
    /// cenário foi gerado, e serve de proveniência mesmo quando só a última sobrevive no modelo.
    /// </remarks>
    public static class ConfiguracaoDeNomeDeVista
    {
        /// <summary>Marcador que precede o número da versão no nome da vista.</summary>
        public const string PrefixoDeVersao = "v";

        /// <summary>
        /// Formato do número de versão. Dois dígitos com zero à esquerda; versões acima de 99
        /// simplesmente ocupam três dígitos e deixam de ordenar — cenário sem relevância prática.
        /// </summary>
        public const string FormatoDaVersao = "00";

        /// <summary>
        /// Formato do carimbo de tempo. ISO (aaaa-MM-dd) para que a ordenação alfabética também
        /// seja cronológica; sem segundos, porque a versão já garante a unicidade.
        /// </summary>
        public const string FormatoDoCarimboDeTempo = "yyyy-MM-dd_HHmm";

        /// <summary>
        /// Teto de tentativas ao procurar um número de versão livre. Existe só para que a busca
        /// por nome disponível não possa virar laço infinito.
        /// </summary>
        public const int LimiteDeTentativasDeVersao = 999;
    }

    /// <summary>
    /// Regras da janela que escolhe os cenários antes de gerar as vistas 3D.
    /// </summary>
    public static class ConfiguracaoDeSelecaoDeCenarios
    {
        /// <summary>
        /// Quantos cenários podem ser marcados de uma vez.
        /// </summary>
        /// <remarks>
        /// Dois, porque é quantos cenários cabem no modelo aberto: o desenho do experimento cruza
        /// cada ambiente VR com os períodos DIA e NOITE, e os dois períodos compartilham a mesma
        /// geometria — logo, o mesmo arquivo Revit. Um terceiro cenário marcado seria
        /// necessariamente de outro ambiente, e os hitpoints dele cairiam sobre a geometria errada.
        /// </remarks>
        public const int LimiteDeCenariosPorGeracao = 2;
    }

    // A classe CoresDeHitpointPorPotencial foi REMOVIDA junto com o classificador por faixa neutra
    // fixa que a consumia (LimiarDePotencialParaCor, MeiaLarguraDaFaixaNeutra e
    // QuantidadeMinimaDeFixacoesParaAfirmar). Aquele critério era um proxy do Quadro 41; o critério
    // do quadro — IC 85% de ν̂_e — está implementado em Processamento.CalculadorDaCamada2, que não
    // usa limiar nenhum sobre o índice. As CORES dos hitpoints seguem sendo os materiais dos tipos
    // da família, editáveis no hitpoint.rfa (ver ConfiguracaoDaFamiliaHitpoint).

    /// <summary>
    /// Rótulos de opções de filtro que precisam ser comparados em código, e por isso não podem
    /// viver soltos como literais espalhados.
    /// </summary>
    public static class RotulosDaInterface
    {
        /// <summary>
        /// Primeira opção do filtro de cenário. Selecioná-la agrega TODAS as sessões carregadas,
        /// em vez de esconder os resultados como fazia o antigo "(Todos)".
        /// </summary>
        public const string TodosOsCenarios = "Todos os Cenários";

        /// <summary>
        /// Primeira opção do filtro de sessões. Selecioná-la não restringe o eixo: a tabela de
        /// PH_e agrega todas as sessões que sobreviveram aos outros dois filtros.
        /// </summary>
        public const string TodasAsSessoes = "Todas as Sessões";

        /// <summary>
        /// Primeira opção do filtro de participante. Selecioná-la não restringe o eixo.
        /// </summary>
        /// <remarks>
        /// Os participantes ofertados são os que existem no <c>dados_hmd.csv</c>
        /// (<see cref="Dominio.Sessao.IdentificacaoManual"/>) — cinco no arquivo atual. É uma
        /// amostra menor e distinta dos 26 respondentes dos formulários e do cortisol, que nunca
        /// entram nesta lista.
        /// </remarks>
        public const string TodosOsParticipantes = "Todos os Participantes";

        /// <summary>
        /// Célula da coluna PRESENÇA DE CO quando o sensor acusou gás em ao menos uma das fixações
        /// do elemento.
        /// </summary>
        /// <remarks>
        /// A coluna deixou de exibir a contagem "N de M". O sensor de gás é um indicador de dois
        /// estados, e uma proporção sugeria uma graduação de concentração que o aparelho não mede.
        /// </remarks>
        public const string GasDetectado = "DETECTADO";

        /// <summary>
        /// Célula da coluna PRESENÇA DE CO quando houve leitura ambiental e o sensor não acusou gás
        /// em nenhuma das fixações do elemento.
        /// </summary>
        public const string GasNaoDetectado = "NÃO DETECTADO";
    }

    /// <summary>
    /// Cores da classificação de potencial homeodinâmico
    /// (<see cref="Dominio.ClassificacaoDePotencial"/>) na interface: verde para Restaurador,
    /// vermelho para Estressor, cinza para Neutro.
    /// </summary>
    /// <remarks>
    /// Uma cor por estado, usada ao mesmo tempo no texto das linhas da tabela e nos pontos das
    /// legendas — para que os dois lugares não divirjam. São mais escuras que tons de preenchimento
    /// porque precisam funcionar como COR DE TEXTO sobre fundo branco, onde um verde ou cinza claro
    /// fica ilegível.
    ///
    /// As cores dos hitpoints no modelo NÃO estão aqui: são os materiais dos tipos da família
    /// <c>hitpoint.rfa</c>, editáveis no próprio arquivo (ver
    /// <see cref="ConfiguracaoDaFamiliaHitpoint"/>).
    /// </remarks>
    // REGRA DE MANUTENÇÃO (permanente, não é pendência): estas cores e os materiais dos três
    // tipos do hitpoint.rfa são mantidos em correspondência MANUALMENTE — o plugin não lê a cor
    // da família nem a escreve, e a família embutida na DLL só muda com recompilação. Se o verde
    // do .rfa for alterado, ajustar CorDeRestaurador aqui para que a legenda continue honesta.
    public static class CoresDeClassificacaoDePotencial
    {
        /// <summary>Verde — potencial restaurador.</summary>
        public const string CorDeRestaurador = "#1B7A50";

        /// <summary>Vermelho — potencial estressor.</summary>
        public const string CorDeEstressor = "#B3382F";

        /// <summary>Cinza — neutro e sem pontuação.</summary>
        /// <remarks>
        /// Os dois estados compartilham a cor, como já acontece nos tipos da família: a distinção
        /// existe na coluna de rótulo da tabela, não na cor.
        /// </remarks>
        public const string CorDeNeutro = "#5C6672";
    }

    // A classe LegendaDasColunasDaTabelaDePotencial foi REMOVIDA em 31/08/2026. Estava SEM
    // CONSUMIDOR — nenhum código ou XAML a lia — e o conteúdo já havia apodrecido: descrevia as
    // colunas "Ruído"/"Detecção" com a semântica antiga (detecção de ruído), anterior à
    // identificação da coluna F como gás e à coluna "Presença de CO" da tabela atual. Uma
    // legenda órfã e defasada é uma promessa que o código não cumpre. Se uma legenda textual
    // voltar a ser necessária, ela deve ser construída a partir das colunas reais do XAML da aba
    // de Potencial Homeodinâmico, e junto de um consumidor que a exiba.

    /// <summary>
    /// Parâmetros da Camada 2 do Modelo Matemático de Potencial Homeodinâmico (Equação 4, §5.2):
    /// índice por elemento e classificação por afirmabilidade estatística.
    /// </summary>
    /// <remarks>
    /// As duas Camadas rodam dentro do plugin: a Camada 1 estima ν̂_e e se(ν̂_e) em dois estágios
    /// (<see cref="Processamento.EstimadorDeEfeitoDeElemento"/>) e a Camada 2 os converte em índice
    /// e classificação (<see cref="Processamento.CalculadorDaCamada2"/>).
    ///
    /// <b>Dois denominadores distintos</b> — a distinção é sutil e trocá-los produz resultados
    /// plausíveis mas errados:
    /// <list type="bullet">
    /// <item>a MAGNITUDE do índice usa τ̂_ν, a dispersão dos efeitos ENTRE os elementos
    /// (§5.3.2 do TextoFinal), que põe cada efeito na escala do quanto os elementos diferem
    /// entre si;</item>
    /// <item>a CLASSIFICAÇÃO usa se(ν̂_e), o erro-padrão a posteriori da estimativa encolhida,
    /// que mede a precisão com que o efeito daquele elemento foi estimado.</item>
    /// </list>
    /// </remarks>
    public static class ParametrosDaCamada2
    {
        /// <summary>
        /// Quantil da normal padrão para intervalo de confiança de 85% (bicaudal), usado na
        /// classificação por afirmabilidade: o IC de ν̂_e é ν̂_e ± 1,44·se(ν̂_e).
        /// </summary>
        /// <remarks>
        /// z₀,₉₂₅ = 1,4395: equivale a exigir |z| &gt; 1,44, com z = ν̂_e / se(ν̂_e). O IC conter
        /// o zero significa insuficiência de evidência — classificação Neutro —, e não ausência
        /// de efeito.
        ///
        /// NÍVEL DECIDIDO PELO AUTOR em 31/08/2026, substituindo o IC de 85% (1,44) da versão
        /// anterior: critério deliberadamente menos exigente, que aceita 15% de erro bicaudal
        /// por elemento em troca de mais elementos com direção afirmada. O encolhimento (λ)
        /// continua ativo e segue sendo o controle de falsas descobertas por construção — o
        /// z comparado ao quantil já é o z encolhido. ATENÇÃO: o TextoFinal ainda declara 95%
        /// (Equação 4, Quadro 41); a revisão do texto deve acompanhá-lo — até lá, esta constante
        /// é o único ponto de divergência deliberada entre código e texto.
        /// </remarks>
        public const double QuantilNormal85 = 1.4395;
    }

    /// <summary>
    /// Configurações da aba e do botão que o plugin cria na ribbon do Revit.
    /// </summary>
    /// <remarks>
    /// Quem monta a ribbon é <see cref="AplicacaoRibbonCAISM"/>, registrado no
    /// <c>PluginCAISM.addin</c> como <c>Type="Application"</c> — um manifesto do tipo
    /// <c>Command</c> sozinho não cria interface, só entrada em Add-Ins &gt; External Tools.
    ///
    /// Estas constantes não afetam o desenvolvimento via AddinManager, que executa o
    /// comando diretamente e nunca chama <c>OnStartup</c>.
    /// </remarks>
    public static class ConfiguracaoDaRibbon
    {
        /// <summary>Nome da aba própria do plugin na ribbon.</summary>
        public const string NomeDaAba = "DASMind";

        /// <summary>Nome do painel dentro da aba.</summary>
        public const string NomeDoPainel = "Análise de Estresse";

        /// <summary>
        /// Identificador interno do botão. Precisa ser único entre todos os addins
        /// carregados; o Revit rejeita o painel inteiro em caso de colisão.
        /// </summary>
        public const string NomeInternoDoBotao = "BotaoPrincipalCAISM";

        /// <summary>
        /// Rótulo exibido no botão. A quebra de linha é intencional: sem ela o Revit
        /// escolhe onde quebrar e alarga o botão.
        /// </summary>
        public const string TextoDoBotao = "Análise\nHomeodinâmica";

        /// <summary>Tooltip curto, exibido ao passar o mouse.</summary>
        public const string DicaDoBotao =
            "Abre o painel de análise de estresse.";

        /// <summary>Descrição longa, exibida no tooltip expandido do Revit.</summary>
        public const string DescricaoLongaDoBotao =
            "Cruza dados de eye-tracking, cortisol e sensores ambientais para gerar " +
            "vistas e tabelas de análise de estresse no modelo aberto. " +
            "Requer uma planta ou vista 3D ativa.";

        /// <summary>Nome do arquivo do ícone do botão.</summary>
        /// <remarks>
        /// É uma cópia de <c>logo caism.png</c>, e não o próprio: o ícone da ribbon é
        /// redimensionado para 32×32, e reaproveitar o logo faria essa mudança atingir
        /// também a tela de instruções, onde ele aparece em tamanho grande.
        /// </remarks>
        /// <summary>Identificador interno do botão de verificação. Único, como o principal.</summary>
        public const string NomeInternoDoBotaoDeVerificacao = "BotaoVerificacaoCAISM";

        /// <summary>Rótulo do botão que roda as verificações do modelo.</summary>
        public const string TextoDoBotaoDeVerificacao = "Verificar\nmodelo";

        /// <summary>Tooltip curto do botão de verificação.</summary>
        public const string DicaDoBotaoDeVerificacao =
            "Confere as calculadoras do índice de Potencial Homeodinâmico.";

        /// <summary>Descrição longa do botão de verificação.</summary>
        public const string DescricaoLongaDoBotaoDeVerificacao =
            "Executa as verificações automáticas do modelo estatístico: convenção de sinal do " +
            "índice, limiar de classificação, ajuste por mínimos quadrados e recuperação do " +
            "efeito de elemento. Não altera o modelo aberto nem depende de arquivo carregado.";

        /// <summary>Nome do arquivo do ícone do botão.</summary>
        public const string NomeDoArquivoDoIcone = "icone caism.png";

        /// <summary>Caminho do ícone relativo à pasta de instalação do plugin.</summary>
        public const string CaminhoRelativoDoIcone = @"Assets\" + NomeDoArquivoDoIcone;

        /// <summary>
        /// Nome do recurso embutido correspondente, usado quando o arquivo em disco não
        /// existe (caso do AddinManager, que copia só o DLL para %TEMP%).
        /// </summary>
        /// <remarks>
        /// O MSBuild preserva o espaço do nome do arquivo ao gerar o nome do recurso, daí
        /// <c>"PluginCAISM.Assets.icone caism.png"</c>. Derivado de
        /// <see cref="NomeDoArquivoDoIcone"/> pelo mesmo motivo de
        /// <see cref="ConfiguracaoDaFamiliaHitpoint.CaminhoRelativoDaFamilia"/>: manter
        /// nome em disco e nome de recurso impossíveis de divergir.
        /// </remarks>
        public const string NomeDoRecursoDoIcone = "PluginCAISM.Assets." + NomeDoArquivoDoIcone;

        /// <summary>Largura, em pixels, do ícone grande do botão (padrão da ribbon do Revit).</summary>
        public const int LarguraDoIconeGrande = 32;

        /// <summary>Largura, em pixels, do ícone pequeno do botão.</summary>
        public const int LarguraDoIconePequeno = 16;
    }
}
