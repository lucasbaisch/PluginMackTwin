using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Processamento
{
    /// <summary>
    /// Calcula as métricas por ELEMENTO — objeto virtual × cenário — a partir do conjunto de
    /// sessões filtradas, aplicando o Modelo de Potencial Homeodinâmico da §5.2.
    /// </summary>
    /// <remarks>
    /// Para cada elemento FIXADO nas sessões, produz um <see cref="MetricasPorObjeto"/> completo;
    /// para cada objeto observado sem fixação válida, produz uma linha de INVENTÁRIO
    /// (Neutro, medidas em travessão) — ver <see cref="ConstruirInventarioSemFixacao"/>.
    /// O registro completo traz:
    /// - PH_e = tanh(−ν̂_e / σ_ΔD), no intervalo (−1, +1): positivo = restaurador, negativo =
    ///   estressor, zero = neutro (Equação 4).
    /// - Classificação por afirmabilidade: IC 85% de ν̂_e abaixo de zero, acima de zero, ou
    ///   contendo zero (Quadro 41).
    /// - ν̂_e, se(ν̂_e) e z, guardados para auditoria.
    /// - Métricas descritivas: duração média das fixações, diâmetro pupilar absoluto médio, ΔD
    ///   médio e sub-índice de conforto ambiental.
    ///
    /// <b>Origem dos efeitos.</b> A Camada 1 roda dentro do plugin, em
    /// <see cref="EstimadorDeEfeitoDeElemento"/>, sobre as próprias sessões carregadas. Não há
    /// importação de efeitos externos: o índice é calculado inteiramente aqui.
    ///
    /// <b>A normalização min-max ponderada foi REMOVIDA.</b> Ela combinava duração de
    /// fixação, ΔD e conforto ambiental por média ponderada; não era a Equação 4, e coexistir com
    /// ela produziria dois índices concorrentes com o mesmo nome. O sub-índice ambiental continua
    /// sendo calculado e exibido, mas como informação descritiva ao lado do PH_e — no MODELO, as
    /// ambientais entram como covariáveis da Equação 3, descontadas na Camada 1, e não como
    /// componente do índice.
    ///
    /// Convenções fisiológicas da §5.2:
    /// - Correção subtrativa: tudo opera sobre ΔD = D_t − baseline da sessão, e não sobre o
    ///   diâmetro absoluto (ver <see cref="Sessao.BaselinePupilar"/>).
    /// - Orientação invertida da pupila: ΔD negativo (constrição) indica potencial restaurador,
    ///   conforme os Quadros 36 e 40; daí o sinal negativo dentro da tangente hiperbólica.
    ///
    /// As faixas ambientais estão em <see cref="FaixasDeConfortoAmbiental"/> e
    /// <see cref="MapeamentoDeColunasDeSensor"/>; os parâmetros do modelo, em
    /// <see cref="ParametrosDaCamada2"/>.
    /// </remarks>
    // O código está conforme ao §5.3 do TextoFinal de 31/08/2026: Equação 3 com efeitos
    // aleatórios encolhidos na Camada 1 e Equação 4 com denominador τ̂_ν na Camada 2. A única
    // pendência de reconciliação restante é DE TEXTO: o Requisito Funcional 05 (Quadro 40) ainda
    // descreve a normalização min-max ponderada e a "faixa de indiferença", mecanismos removidos —
    // deve passar a descrever as duas camadas, como o próprio §5.3 já faz.
    public class CalculadorDeMetricasPorObjeto
    {
        private readonly EstimadorDeEfeitoDeElemento _estimador = new();

        /// <summary>
        /// Avisos produzidos pela Camada 1 na última chamada a <see cref="Calcular"/>.
        /// </summary>
        /// <remarks>
        /// <b>Precisam chegar a quem lê a tabela.</b> A Camada 1 decide sozinha quais regressores
        /// da Equação 3 entram no ajuste: um regressor sem leitura em alguma fixação, ou constante
        /// na amostra, fica de fora. Quando isso acontece, o modelo estimado não é a Equação 3, e
        /// sim a Equação 3 menos aqueles termos — mas a tabela sai idêntica, colorida do mesmo
        /// modo, sem nada que denuncie a diferença.
        ///
        /// O caso extremo está entre esses avisos: o ajuste falhar por inteiro e ν̂_e passar a ser
        /// a média do ΔD bruto, sem desconto de covariável alguma.
        ///
        /// Lista vazia quando não houve o que relatar. Nunca nula.
        /// </remarks>
        public IReadOnlyList<string> AvisosDaCamada1 { get; private set; } = new List<string>();

        /// <summary>
        /// Agrupa uma fixação com a linha bruta que a originou, a baseline pupilar da sessão a que
        /// pertence — para que o ΔD possa ser calculado por sessão — e o cenário dessa sessão, que
        /// compõe a identidade do elemento.
        /// </summary>
        private readonly struct FixacaoComContexto
        {
            public Fixacao Fixacao { get; }
            public LinhaDeSessao LinhaOrigem { get; }
            public double BaselinePupilar { get; }
            public string Cenario { get; }

            public FixacaoComContexto(
                Fixacao fixacao, LinhaDeSessao linhaOrigem, double baselinePupilar, string cenario)
            {
                Fixacao = fixacao;
                LinhaOrigem = linhaOrigem;
                BaselinePupilar = baselinePupilar;
                Cenario = cenario;
            }

            /// <summary>
            /// Diâmetro pupilar médio bienocular desta fixação, em mm, ou NaN se ambos os olhos
            /// não tiverem medida válida.
            /// </summary>
            public double DiametroMedio => CalcularDiametroMedio();

            /// <summary>
            /// ΔD desta fixação em mm: diâmetro médio menos a baseline da sessão.
            /// Retorna NaN se o diâmetro ou a baseline não forem válidos.
            /// </summary>
            public double DeltaPupilar
            {
                get
                {
                    double diametro = DiametroMedio;

                    if (double.IsNaN(diametro) || double.IsNaN(BaselinePupilar))
                        return double.NaN;

                    return diametro - BaselinePupilar;
                }
            }

            private double CalcularDiametroMedio()
            {
                bool esquerdaValida = !double.IsNaN(Fixacao.DiametroMedianoPupilaEsquerda)
                    && Fixacao.DiametroMedianoPupilaEsquerda > 0;
                bool direitaValida = !double.IsNaN(Fixacao.DiametroMedianoPupilaDireita)
                    && Fixacao.DiametroMedianoPupilaDireita > 0;

                if (esquerdaValida && direitaValida)
                    return (Fixacao.DiametroMedianoPupilaEsquerda + Fixacao.DiametroMedianoPupilaDireita) / 2.0;

                if (esquerdaValida)
                    return Fixacao.DiametroMedianoPupilaEsquerda;

                if (direitaValida)
                    return Fixacao.DiametroMedianoPupilaDireita;

                return double.NaN;
            }
        }

        /// <summary>
        /// Calcula as métricas para todos os objetos presentes nas sessões fornecidas, ordenando o
        /// resultado por potencial decrescente.
        /// </summary>
        /// <param name="sessoes">Sessões filtradas a serem analisadas.</param>
        /// <returns>
        /// Lista de <see cref="MetricasPorObjeto"/> ordenada por
        /// <see cref="MetricasPorObjeto.PotencialHomeodinamico"/> decrescente, um registro por
        /// objeto × cenário. Elementos sem estimativa (PH_e = NaN) vão para o fim.
        /// </returns>
        public List<MetricasPorObjeto> Calcular(IEnumerable<Sessao> sessoes)
        {
            var sessoesMaterializadas = sessoes?.ToList() ?? new List<Sessao>();

            var efeitos = _estimador.Estimar(sessoesMaterializadas);
            AvisosDaCamada1 = efeitos?.Avisos ?? new List<string>();

            var todasAsFixacoes = ColetarTodasAsFixacoes(sessoesMaterializadas);
            var fixacoesPorElemento = AgruparFixacoesPorElemento(todasAsFixacoes);

            var metricas = fixacoesPorElemento
                .Select(par => ConstruirMetricasParaElemento(par.Key, par.Value, efeitos))
                .ToList();

            metricas.AddRange(ConstruirInventarioSemFixacao(sessoesMaterializadas, metricas));

            return metricas
                .OrderByDescending(metrica => double.IsNaN(metrica.PotencialHomeodinamico)
                    ? double.NegativeInfinity
                    : metrica.PotencialHomeodinamico)
                .ToList();
        }

        /// <summary>
        /// Linhas de inventário: objetos que receberam amostras de olhar nas sessões, mas nenhuma
        /// fixação válida — e que por isso não têm métrica calculável.
        /// </summary>
        /// <remarks>
        /// Sem estas linhas, a tabela silencia os objetos apenas "passados de olhar" e o leitor
        /// não distingue "não foi observado o bastante" de "não existe na cena". Cada linha sai
        /// como <see cref="ClassificacaoDePotencial.Neutro"/> — a semântica já definida de
        /// AUSÊNCIA DE DADO — com travessão em todas as medidas (NaN) e zero fixações. Nenhum
        /// número estatístico muda: estas linhas não passam pela Camada 1 nem pela Camada 2, não
        /// entram no PH por participante (que filtra PH = NaN) e não geram hitpoint no modelo
        /// (hitpoints nascem de fixações).
        /// </remarks>
        private static List<MetricasPorObjeto> ConstruirInventarioSemFixacao(
            List<Sessao> sessoes, List<MetricasPorObjeto> metricasExistentes)
        {
            var nomesCobertos = new HashSet<string>(
                metricasExistentes.Select(m => m.NomeDoObjeto), StringComparer.OrdinalIgnoreCase);

            var cenariosPorObjeto = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var sessao in sessoes)
            {
                if (sessao.DadosBrutos == null)
                    continue;

                foreach (var linha in sessao.DadosBrutos)
                {
                    string nome = linha.NomeDoObjeto;

                    if (string.IsNullOrWhiteSpace(nome) || nomesCobertos.Contains(nome))
                        continue;

                    if (!cenariosPorObjeto.TryGetValue(nome, out var cenarios))
                    {
                        cenarios = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                        cenariosPorObjeto[nome] = cenarios;
                    }

                    if (!string.IsNullOrWhiteSpace(sessao.RotuloDeCenario))
                        cenarios.Add(sessao.RotuloDeCenario);
                }
            }

            return cenariosPorObjeto
                .Select(par => new MetricasPorObjeto
                {
                    NomeDoObjeto              = par.Key,
                    Cenario                   = string.Join("; ", par.Value),
                    DuracaoMediaDasFixacoesMs = double.NaN,
                    TemperaturaMediaCelsius   = double.NaN,
                    UmidadeMediaPercentual    = double.NaN,
                    RuidoMedioDb              = double.NaN,
                    DiametroPupilaMedioMm     = double.NaN,
                    DeltaPupilarMedioMm       = double.NaN,
                    PontuacaoAmbiental        = double.NaN,
                    MediasDosValoresDeSensores = new Dictionary<string, double>(),
                    // PotencialHomeodinamico, efeitos e z ficam nos padrões (NaN) e a
                    // ClassificacaoPH no padrão Neutro; contagens ficam em zero.
                })
                .ToList();
        }

        private List<FixacaoComContexto> ColetarTodasAsFixacoes(IEnumerable<Sessao> sessoes)
        {
            var resultado = new List<FixacaoComContexto>();

            foreach (var sessao in sessoes)
            {
                foreach (var fixacao in sessao.Fixacoes)
                {
                    var linhaRepresentativa = ObterLinhaRepresentativaDaFixacao(fixacao, sessao.DadosBrutos);
                    resultado.Add(new FixacaoComContexto(
                        fixacao, linhaRepresentativa, sessao.BaselinePupilar, sessao.RotuloDeCenario));
                }
            }

            return resultado;
        }

        private LinhaDeSessao ObterLinhaRepresentativaDaFixacao(Fixacao fixacao, IReadOnlyList<LinhaDeSessao> dadosBrutos)
        {
            if (fixacao.IndicesLinhasOriginais.Count == 0)
                return null;

            int indice = fixacao.IndicesLinhasOriginais[0];
            return dadosBrutos.FirstOrDefault(linha => linha.IndiceLinhaOriginal == indice);
        }

        /// <summary>
        /// Agrupa as fixações por ELEMENTO, isto é, por objeto × cenário.
        /// </summary>
        /// <remarks>
        /// O cenário entra na chave porque a §5.2 estima efeito de elemento, e o mesmo objeto pode
        /// responder de forma diferente em cenários diferentes. Com um cenário filtrado o resultado
        /// é idêntico ao agrupamento só por objeto; com "Todos os Cenários", separa o que antes era
        /// misturado numa média sem sentido.
        ///
        /// Uma fixação que abrange vários objetos entra no grupo de cada um deles.
        /// </remarks>
        /// <summary>
        /// Agrupa as fixações por ELEMENTO, isto é, por nome de objeto.
        /// </summary>
        /// <remarks>
        /// O cenário saiu da chave: a Equação 3 o declara como efeito fixo β_S, descontado no
        /// ajuste da Camada 1, e o Quadro 38 indexa ν_e por <c>nomeDoObjeto</c>. Manter o cenário
        /// na chave descontaria o mesmo efeito duas vezes — por coeficiente e por estratificação.
        ///
        /// Em consequência, cada elemento passa a ter UM índice, e a coluna CENÁRIO da tabela
        /// deixou de compor a identidade da linha: ela agora enumera os cenários em que o elemento
        /// foi observado.
        /// </remarks>
        private Dictionary<string, List<FixacaoComContexto>> AgruparFixacoesPorElemento(
            List<FixacaoComContexto> fixacoesComContexto)
        {
            var grupos = new Dictionary<string, List<FixacaoComContexto>>(StringComparer.OrdinalIgnoreCase);

            foreach (var fixacaoComContexto in fixacoesComContexto)
            {
                foreach (var nomeDoObjeto in fixacaoComContexto.Fixacao.NomesDeObjetos)
                {
                    if (string.IsNullOrWhiteSpace(nomeDoObjeto))
                        continue;

                    string chave = nomeDoObjeto;

                    if (!grupos.TryGetValue(chave, out var lista))
                    {
                        lista = new List<FixacaoComContexto>();
                        grupos[chave] = lista;
                    }

                    lista.Add(fixacaoComContexto);
                }
            }

            return grupos;
        }

        /// <summary>
        /// Monta as métricas de um elemento: as descritivas, a partir das suas fixações, e as do
        /// modelo, aplicando a Camada 2 sobre o efeito estimado ou importado.
        /// </summary>
        /// <remarks>
        /// A pontuação ambiental continua sendo calculada e exibida como informação descritiva ao
        /// lado do PH_e, mas não é componente do índice. O papel das variáveis ambientais no
        /// MODELO é outro: temperatura, umidade, ruído e CO entram como covariáveis da Equação 3
        /// na Camada 1, onde são DESCONTADAS do ΔD antes da estimação de ν̂_e — descontar uma
        /// influência e somá-la ao índice são coisas diferentes.
        /// </remarks>
        private MetricasPorObjeto ConstruirMetricasParaElemento(
            string chaveDoElemento,
            List<FixacaoComContexto> fixacoesDoElemento,
            ResultadoDaCamada1 efeitos)
        {
            var primeira = fixacoesDoElemento[0];
            string nomeDoObjeto = ObterNomeDoObjetoDaChave(chaveDoElemento, primeira);

            // O elemento pode ter sido observado em mais de um cenário. A célula enumera todos,
            // em vez de exibir o primeiro — que seria arbitrário e daria a entender que a
            // estimativa se restringe a ele.
            string cenario = string.Join("; ", fixacoesDoElemento
                .Select(f => f.Cenario)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase));

            double duracaoMedia = fixacoesDoElemento.Average(f => f.Fixacao.DuracaoEmMilissegundos);
            double diametroMedio = CalcularMediaIgnorandoNaN(fixacoesDoElemento.Select(f => f.DiametroMedio));
            double deltaPupilarMedio = CalcularMediaIgnorandoNaN(fixacoesDoElemento.Select(f => f.DeltaPupilar));
            var mediasDeSensores = CalcularMediasDeSensores(fixacoesDoElemento);
            double pontuacaoAmbiental = PontuarConfortoAmbiental(
                mediasDeSensores, ResolverLimiteDeRuido(fixacoesDoElemento));
            var ambiente = ResumirAmbienteDoElemento(fixacoesDoElemento);

            var camada2 = AplicarCamada2(nomeDoObjeto, cenario, efeitos);

            return new MetricasPorObjeto
            {
                NomeDoObjeto               = nomeDoObjeto,
                Cenario                    = cenario,
                DuracaoMediaDasFixacoesMs  = duracaoMedia,
                TemperaturaMediaCelsius    = ambiente.Temperatura,
                UmidadeMediaPercentual     = ambiente.Umidade,
                RuidoMedioDb               = ambiente.Ruido,
                QuantidadeDeFixacoesComGasDetectado    = ambiente.FixacoesComGasDetectado,
                QuantidadeDeFixacoesComLeituraAmbiental = ambiente.FixacoesComLeitura,
                DiametroPupilaMedioMm      = diametroMedio,
                DeltaPupilarMedioMm        = deltaPupilarMedio,
                PontuacaoAmbiental         = pontuacaoAmbiental,
                PotencialHomeodinamico     = camada2.Potencial,
                ClassificacaoPH            = camada2.Classificacao,
                EfeitoNuMm                 = camada2.EfeitoNuMm,
                ErroPadraoDoEfeitoMm       = camada2.ErroPadraoMm,
                EstatisticaZ               = camada2.EstatisticaZ,
                QuantidadeDeObservacoes    = camada2.QuantidadeDeObservacoes,
                MediasDosValoresDeSensores = mediasDeSensores
            };
        }

        /// <summary>Ambiente vigente durante as fixações de um elemento.</summary>
        private readonly struct ResumoAmbiental
        {
            public double Temperatura { get; init; }
            public double Umidade { get; init; }
            public double Ruido { get; init; }
            public int FixacoesComGasDetectado { get; init; }
            public int FixacoesComLeitura { get; init; }
        }

        /// <summary>
        /// Resume as condições ambientais vigentes durante as fixações do elemento.
        /// </summary>
        /// <remarks>
        /// Cada fixação carrega a leitura de sensor vigente no seu instante — a mais recente igual
        /// ou anterior, associada em <see cref="ConstruturDeSessoes"/>. Aqui essas leituras são
        /// resumidas por elemento.
        ///
        /// As três grandezas contínuas — temperatura (coluna D), umidade (E) e ruído (G) — são
        /// médias de todas as fixações com leitura. A presença de gás (coluna F) é contada à parte,
        /// por ser um indicador de dois estados e não uma medida.
        ///
        /// <b>O ruído não é mais condicionado ao sensor de gás.</b> Enquanto a coluna F era tida
        /// por detecção de ruído, fazia sentido só promediar o nível sonoro quando ela acusava —
        /// sem detecção o valor seria o piso do aparelho. Identificada como coluna de gás, o
        /// condicionamento passou a filtrar o nível sonoro por uma grandeza que nada tem a ver
        /// com ele, e foi removido.
        /// </remarks>
        private static ResumoAmbiental ResumirAmbienteDoElemento(
            List<FixacaoComContexto> fixacoesDoElemento)
        {
            var temperaturas = new List<double>();
            var umidades = new List<double>();
            var ruidos = new List<double>();
            int comGasDetectado = 0;
            int comLeitura = 0;

            foreach (var fixacao in fixacoesDoElemento)
            {
                var campos = fixacao.LinhaOrigem?.CamposSensor;

                if (campos == null || campos.Count == 0)
                    continue;

                comLeitura++;

                if (TentarLerNumeroDaLeitura(campos, MapeamentoDeColunasDeSensor.Temperatura, out double temperatura))
                    temperaturas.Add(temperatura);

                if (TentarLerNumeroDaLeitura(campos, MapeamentoDeColunasDeSensor.Umidade, out double umidade))
                    umidades.Add(umidade);

                if (TentarLerNumeroDaLeitura(campos, MapeamentoDeColunasDeSensor.Ruido, out double ruido))
                    ruidos.Add(ruido);

                if (HouvePresencaDeGas(campos))
                    comGasDetectado++;
            }

            return new ResumoAmbiental
            {
                Temperatura             = temperaturas.Any() ? temperaturas.Average() : double.NaN,
                Umidade                 = umidades.Any() ? umidades.Average() : double.NaN,
                Ruido                   = ruidos.Any() ? ruidos.Average() : double.NaN,
                FixacoesComGasDetectado = comGasDetectado,
                FixacoesComLeitura      = comLeitura,
            };
        }

        /// <summary>
        /// Lê um número de uma leitura BRUTA de sensor, localizando a coluna por conteúdo.
        /// </summary>
        /// <remarks>
        /// Distinta de <c>TentarObterValorSensor</c>, que opera sobre as médias já convertidas em
        /// número. Aqui os valores ainda são texto, porque a detecção de ruído precisa ser lida
        /// leitura a leitura, e não sobre uma média.
        /// </remarks>
        private static bool TentarLerNumeroDaLeitura(
            IReadOnlyDictionary<string, string> campos,
            IReadOnlyList<string> candidatos,
            out double valor)
        {
            foreach (var candidato in candidatos)
            {
                foreach (var campo in campos)
                {
                    if (!NomeDeColunaCorresponde(campo.Key, candidato))
                        continue;

                    if (TentarConverterNumero(campo.Value, out valor))
                        return true;
                }
            }

            valor = double.NaN;
            return false;
        }

        /// <summary>
        /// Compara um cabeçalho lido com um nome de coluna esperado, por igualdade exata,
        /// ignorando caixa e espaços nas pontas.
        /// </summary>
        /// <remarks>
        /// Substituiu a busca por trecho de string. Ver <see cref="MapeamentoDeColunasDeSensor"/>:
        /// o cabeçalho do Input 03 é fixo, e "CO" como trecho casaria com qualquer cabeçalho que
        /// contivesse essas duas letras.
        /// </remarks>
        internal static bool NomeDeColunaCorresponde(string nomeLido, string nomeEsperado)
        {
            if (string.IsNullOrWhiteSpace(nomeLido) || string.IsNullOrWhiteSpace(nomeEsperado))
                return false;

            return string.Equals(nomeLido.Trim(), nomeEsperado.Trim(),
                                 StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Converte o texto de uma célula em número, aceitando ponto ou vírgula como separador
        /// decimal.
        /// </summary>
        /// <remarks>
        /// O Excel grava conforme a localidade da máquina em que a planilha foi salva, e o mesmo
        /// arquivo pode chegar com "23.5" ou "23,5". Tentar as duas evita que a leitura dependa
        /// da configuração regional de quem exportou.
        /// </remarks>
        internal static bool TentarConverterNumero(string texto, out double valor)
        {
            valor = double.NaN;

            if (string.IsNullOrWhiteSpace(texto))
                return false;

            return double.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out valor)
                || double.TryParse(texto, NumberStyles.Any, CultureInfo.CurrentCulture, out valor);
        }

        /// <summary>
        /// Diz se a leitura vigente registrou presença de CO (coluna "CO" do arquivo de sensores,
        /// 1 = detectado, 0 = não detectado; arquivos com legenda no cabeçalho usam
        /// 0,162 = detectado e 0,83 = não detectado).
        /// </summary>
        /// <remarks>
        /// Ausência de coluna e valor 0 são ambos <see langword="false"/> aqui, mas o chamador os
        /// distingue por outra via: <c>FixacoesComLeitura</c> conta as fixações que tinham QUALQUER
        /// leitura ambiental, e é isso que separa "não detectado" de "não medido" na tabela.
        ///
        /// A interpretação dos dois estados está em <see cref="ParametrosDePresencaDeGas"/>.
        /// </remarks>
        private static bool HouvePresencaDeGas(IReadOnlyDictionary<string, string> campos)
        {
            return TentarLerNumeroDaLeitura(
                       campos, MapeamentoDeColunasDeSensor.PresencaDeCo, out double indicador)
                   && ParametrosDePresencaDeGas.IndicaPresenca(indicador);
        }

        /// <summary>
        /// Recupera o nome do objeto a partir da chave composta, procurando entre os objetos da
        /// primeira fixação do grupo — a chave pode conter o separador e o cenário.
        /// </summary>
        private static string ObterNomeDoObjetoDaChave(string chave, FixacaoComContexto fixacao)
        {
            foreach (string nome in fixacao.Fixacao.NomesDeObjetos)
            {
                if (string.Equals(ResultadoDaCamada1.ComporChave(nome, fixacao.Cenario), chave,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    return nome;
                }
            }

            return chave;
        }

        /// <summary>Saída da Camada 2 para um elemento.</summary>
        private readonly struct ResultadoDaCamada2
        {
            public double Potencial { get; init; }
            public ClassificacaoDePotencial Classificacao { get; init; }
            public double EfeitoNuMm { get; init; }
            public double ErroPadraoMm { get; init; }
            public double EstatisticaZ { get; init; }
            public int QuantidadeDeObservacoes { get; init; }
        }

        /// <summary>
        /// Aplica a Equação 4 ao elemento: índice PH_e = tanh(−ν̂_e/τ̂_ν) e classificação pelo
        /// IC de 85% do efeito encolhido.
        /// </summary>
        /// <remarks>
        /// Um elemento sem efeito correspondente sai como
        /// <see cref="ClassificacaoDePotencial.Neutro"/> e PH_e = NaN. Acontece quando nenhuma
        /// das suas fixações tem ΔD válido — sessão sem baseline pupilar, ou sem diâmetro medido.
        /// </remarks>
        // APRESENTAÇÃO ADOTADA (revisão de 01/09/2026): elementos sem estimativa ficam NEUTRO —
        // cinza no modelo, "Neutro" na tabela, ao fim da ordenação por não terem índice. A
        // categoria "Sem pontuação" foi suprimida: o Quadro de classificação do TextoFinal
        // declara TRÊS classes, e uma quarta apenas no código fazia o artefato afirmar mais do
        // que a tese descreve. A distinção também não se sustentava no critério — se Neutro é
        // "evidência insuficiente para afirmar a direção", nenhuma evidência é o caso extremo
        // disso. A informação não se perde: a coluna de fixações mostra zero, as de índice e de
        // ambiente trazem travessão, e os avisos da Camada 1 reportam sessões sem baseline ou
        // sem diâmetro medido, que é onde o diagnóstico pertence.
        private static ResultadoDaCamada2 AplicarCamada2(
            string nomeDoObjeto, string cenario, ResultadoDaCamada1 efeitos)
        {
            // O cenário não entra na busca: a Camada 1 indexa ν̂_e por nome de objeto, porque o
            // cenário já foi descontado como efeito fixo β_S no ajuste. O parâmetro permanece na
            // assinatura para que a chamada continue legível ao lado da linha da tabela.
            if (efeitos == null || !efeitos.TentarObterEfeito(nomeDoObjeto, null, out var efeito))
            {
                return new ResultadoDaCamada2
                {
                    Potencial     = double.NaN,
                    Classificacao = ClassificacaoDePotencial.Neutro,
                    EfeitoNuMm    = double.NaN,
                    ErroPadraoMm  = double.NaN,
                    EstatisticaZ  = double.NaN,
                };
            }

            return new ResultadoDaCamada2
            {
                // O denominador é τ̂_ν, a dispersão dos efeitos entre elementos (§5.3.2) — não o
                // σ_ΔD descritivo que o resultado também carrega.
                Potencial = CalculadorDaCamada2.CalcularPotencialHomeodinamico(
                                efeito.EfeitoNuMm, efeitos.DispersaoDosEfeitosMm),
                Classificacao = CalculadorDaCamada2.Classificar(efeito.EfeitoNuMm, efeito.ErroPadraoMm),
                EfeitoNuMm    = efeito.EfeitoNuMm,
                ErroPadraoMm  = efeito.ErroPadraoMm,
                EstatisticaZ  = CalculadorDaCamada2.CalcularRazaoDeSignificancia(
                                    efeito.EfeitoNuMm, efeito.ErroPadraoMm),
                QuantidadeDeObservacoes = efeito.QuantidadeDeObservacoes,
            };
        }

        /// <summary>
        /// Calcula o sub-índice de conforto ambiental em [0, 1] a partir das médias dos sensores,
        /// usando as faixas da Tabela 5 (§3.1.5) do TextoFinal.
        /// Cada parâmetro localizado é pontuado por sua faixa de conforto; o sub-índice é a média
        /// dos parâmetros encontrados. Retorna 0,5 (neutro) quando nenhum é localizado.
        /// </summary>
        /// <param name="ruidoMaximo">
        /// Limite de conforto de ruído aplicável ao elemento — diurno (50 dB) ou noturno (45 dB),
        /// resolvido por <see cref="ResolverLimiteDeRuido"/> a partir dos cenários observados.
        /// </param>
        private double PontuarConfortoAmbiental(
            IReadOnlyDictionary<string, double> mediasDeSensores, double ruidoMaximo)
        {
            if (mediasDeSensores == null || mediasDeSensores.Count == 0)
                return 0.5;

            double margem = LimiaresDePotencialRestaurador.FracaoDeMargemDeDesconfortoAmbiental;
            var pontuacoes = new List<double>();

            if (TentarObterValorSensor(mediasDeSensores, MapeamentoDeColunasDeSensor.Temperatura, out double temperatura))
                pontuacoes.Add(PontuarBandaDupla(
                    temperatura,
                    FaixasDeConfortoAmbiental.TemperaturaMinima,
                    FaixasDeConfortoAmbiental.TemperaturaMaxima,
                    margem));

            if (TentarObterValorSensor(mediasDeSensores, MapeamentoDeColunasDeSensor.Umidade, out double umidade))
                pontuacoes.Add(PontuarBandaDupla(
                    umidade,
                    FaixasDeConfortoAmbiental.UmidadeMinima,
                    FaixasDeConfortoAmbiental.UmidadeMaxima,
                    margem));

            if (TentarObterValorSensor(mediasDeSensores, MapeamentoDeColunasDeSensor.Ruido, out double ruido))
                pontuacoes.Add(PontuarBandaDupla(
                    ruido,
                    FaixasDeConfortoAmbiental.RuidoMinimo,
                    ruidoMaximo,
                    margem));

            // O CO NÃO entra aqui. A coluna é um indicador de presença (0/1), não uma concentração
            // em ppm: pontuá-la pela faixa 0–35 punha os dois estados dentro da faixa confortável,
            // de modo que o termo valia 1,0 sempre e só diluía os outros três. A presença de gás é
            // reportada como rótulo na tabela e entra no ajuste da Camada 1 como covariável
            // binária, que é onde ela tem significado.

            return pontuacoes.Any() ? pontuacoes.Average() : 0.5;
        }

        /// <summary>
        /// Resolve o limite de conforto de ruído do elemento pelo período dos cenários em que ele
        /// foi observado: noturno (45 dB, ABNT NBR 10151) quando TODOS os rótulos de cenário
        /// terminam em "Noite"; diurno (50 dB) nos demais casos.
        /// </summary>
        /// <remarks>
        /// O período é o último segmento do rótulo "cena_período" ("Cenário_01_Dia"), convenção
        /// confirmada nas capturas definitivas (cenario_1/2.csv). A regra é de UNANIMIDADE:
        /// leituras mistas dia/noite, ou rótulos sem a convenção, caem no limite diurno — o menos
        /// restritivo —, porque aplicar 45 dB a médias que incluem leituras diurnas afirmaria
        /// desconforto que os dados não sustentam.
        /// </remarks>
        private static double ResolverLimiteDeRuido(List<FixacaoComContexto> fixacoesDoElemento)
        {
            bool haCenario = false;

            foreach (var fixacao in fixacoesDoElemento)
            {
                string cenario = fixacao.Cenario;

                if (string.IsNullOrWhiteSpace(cenario))
                    continue;

                haCenario = true;
                int ultimoSeparador = cenario.LastIndexOf('_');

                string periodo = ultimoSeparador >= 0 && ultimoSeparador < cenario.Length - 1
                    ? cenario.Substring(ultimoSeparador + 1).Trim()
                    : null;

                if (!string.Equals(periodo, "Noite", StringComparison.OrdinalIgnoreCase))
                    return FaixasDeConfortoAmbiental.RuidoMaximoDiurno;
            }

            return haCenario
                ? FaixasDeConfortoAmbiental.RuidoMaximoNoturno
                : FaixasDeConfortoAmbiental.RuidoMaximoDiurno;
        }

        /// <summary>
        /// Tenta localizar o valor de um parâmetro ambiental testando, em ordem, os nomes de coluna
        /// candidatos declarados em <see cref="MapeamentoDeColunasDeSensor"/>.
        /// Para cada candidato faz correspondência exata e, na ausência, correspondência por
        /// conteúdo (case-insensitive), para tolerar cabeçalhos como "Temperatura (°C)".
        /// </summary>
        private static bool TentarObterValorSensor(
            IReadOnlyDictionary<string, double> medias, string[] nomesCandidatos, out double valor)
        {
            valor = double.NaN;

            if (nomesCandidatos == null)
                return false;

            foreach (string nomeCandidato in nomesCandidatos)
            {
                foreach (var par in medias)
                {
                    if (NomeDeColunaCorresponde(par.Key, nomeCandidato) && !double.IsNaN(par.Value))
                    {
                        valor = par.Value;
                        return true;
                    }
                }
            }

            valor = double.NaN;
            return false;
        }

        /// <summary>
        /// Pontua um valor por faixa de conforto de dois lados: 1,0 dentro de [minimo, maximo],
        /// decaindo linearmente até 0,0 a uma distância de (largura da faixa × fracaoDeMargem)
        /// além de cada borda.
        /// </summary>
        private static double PontuarBandaDupla(double valor, double minimo, double maximo, double fracaoDeMargem)
        {
            if (double.IsNaN(valor))
                return 0.5;

            if (valor >= minimo && valor <= maximo)
                return 1.0;

            double largura = maximo - minimo;
            double margem = largura * fracaoDeMargem;

            if (margem <= 0.0)
                return 0.0;

            double distancia = valor < minimo ? (minimo - valor) : (valor - maximo);
            double pontuacao = 1.0 - (distancia / margem);
            return Math.Max(0.0, Math.Min(1.0, pontuacao));
        }

        private Dictionary<string, double> CalcularMediasDeSensores(List<FixacaoComContexto> fixacoesDoObjeto)
        {
            var agrupadoPorSensor = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

            foreach (var fixacaoComContexto in fixacoesDoObjeto)
            {
                if (fixacaoComContexto.LinhaOrigem == null)
                    continue;

                foreach (var (nomeSensor, valorTexto) in fixacaoComContexto.LinhaOrigem.CamposSensor)
                {
                    if (!double.TryParse(valorTexto, NumberStyles.Any, CultureInfo.InvariantCulture, out double valorNumerico))
                        continue;

                    if (!agrupadoPorSensor.TryGetValue(nomeSensor, out var lista))
                    {
                        lista = new List<double>();
                        agrupadoPorSensor[nomeSensor] = lista;
                    }

                    lista.Add(valorNumerico);
                }
            }

            return agrupadoPorSensor.ToDictionary(
                par => par.Key,
                par => par.Value.Average());
        }

        /// <summary>
        /// Média dos valores da sequência que não são NaN. Retorna NaN se não houver nenhum válido.
        /// </summary>
        private static double CalcularMediaIgnorandoNaN(IEnumerable<double> valores)
        {
            var valoresValidos = valores.Where(valor => !double.IsNaN(valor)).ToList();

            if (!valoresValidos.Any())
                return double.NaN;

            return valoresValidos.Average();
        }

    }
}
