using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Processamento
{
    /// <summary>
    /// Camada 1 do Modelo Estatístico de Potencial Homeodinâmico (§5.3.1): estima o efeito de
    /// elemento ν̂_e e o seu erro-padrão a partir das fixações, em C#, sem biblioteca estatística.
    /// </summary>
    /// <remarks>
    /// A Equação 3 do texto é
    /// <c>ΔD_iet = β_Base·Base_i + s(Gaze_x, Gaze_y) + β_T·Temp + β_H·Umid + β_R·Ruído
    /// + β_C·CO + μ_i + ν_e</c>.
    ///
    /// <b>Sem intercepto e sem efeito fixo de cenário</b> (revisão de 01/09/2026, por decisão do
    /// autor): a parte fixa reduz-se às covariáveis contínuas. Ver
    /// <see cref="DescontarCovariaveis"/> para as duas consequências que isso acarreta.
    ///
    /// O ajuste se dá em estágios sequenciais. No primeiro, a parte FIXA da equação — β_Base,
    /// s(Gaze) e as quatro ambientais — entra numa regressão por mínimos quadrados e é
    /// descontada do ΔD. Nos estágios seguintes, os dois efeitos ALEATÓRIOS são estimados por
    /// método dos momentos com encolhimento empírico-Bayes (BLUP): primeiro μ_i, o efeito de
    /// indivíduo, subtraído dos resíduos; depois ν_e, o efeito de elemento, com a sua variância
    /// entre elementos (τ̂²_ν) estimada e usada tanto no encolhimento quanto como denominador da
    /// Equação 4.
    ///
    /// <b>Correspondência com a Equação 3:</b>
    /// <list type="bullet">
    /// <item><b>β_Base·Base_i</b> — a linha de base entra DUAS vezes, e de propósito: por
    /// subtração, ao formar ΔD = D − Base (Equação 2), e como regressor, que é o que a Equação 3
    /// pede. O coeficiente mede o quanto da linha de base a subtração não removeu.</item>
    /// <item><b>s(Gaze_x, Gaze_y)</b> — termo NÃO LINEAR nas coordenadas do olhar: superfície
    /// quadrática (gx, gy, gx², gy², gx·gy) aprendida dos próprios dados, que desconta o erro de
    /// encurtamento pupilar conforme a direção do olhar (Fink et al., 2024). Aproximação
    /// paramétrica da spline de placa fina, adequada porque a tendência é suave e de baixa
    /// ordem.</item>
    /// <item><b>β_T, β_H, β_R, β_C</b> — as quatro ambientais, lidas da leitura de sensor vigente
    /// no instante da fixação.</item>
    /// <item><b>μ_i</b> — efeito ALEATÓRIO de indivíduo: média dos resíduos por participante,
    /// encolhida por λ_i = τ²_μ/(τ²_μ + se²_i) e subtraída. Ver
    /// <see cref="DescontarEfeitoDeParticipante"/>.</item>
    /// <item><b>ν_e</b> — efeito ALEATÓRIO de elemento: média dos resíduos por
    /// <c>nomeDoObjeto</c>, encolhida por λ_e = τ²_ν/(τ²_ν + se²_e), com IC de 85% explícito.
    /// Ver <see cref="EstimarPorElemento"/>.</item>
    /// <item><b>e_iet</b> — resíduo AR(1): a autocorrelação de defasagem 1 é estimada dentro das
    /// sessões e corrige o tamanho amostral efetivo de se(ν̂_e). Ver
    /// <see cref="EstimarAutocorrelacao"/>.</item>
    /// </list>
    ///
    /// <b>O encolhimento é também o controle de falsas descobertas.</b> Nas comparações
    /// simultâneas entre dezenas de elementos, o partial pooling atenua cada estimativa na
    /// proporção da sua imprecisão, o que controla por construção o excesso de afirmações
    /// espúrias — dispensando correção posterior de multiplicidade (GELMAN; HILL; YAJIMA, 2012,
    /// Journal of Research in Educational Effectiveness).
    ///
    /// <b>Nota de método:</b> a estimação é por método dos momentos em estágios, e não por REML
    /// numa única verossimilhança — a pesquisa não admite biblioteca estatística externa. Os
    /// componentes de variância (τ̂²_μ, τ̂²_ν), os efeitos encolhidos e os ICs são os do modelo
    /// declarado; muda apenas o algoritmo que os obtém.
    /// </remarks>
    public class EstimadorDeEfeitoDeElemento
    {
        /// <summary>Observação elementar: uma fixação com o ΔD e todo o contexto da Equação 3.</summary>
        private sealed class ObservacaoDeFixacao
        {
            public string Participante { get; init; }

            // O campo Cenario foi REMOVIDO com a saída de β_S da Equação 3: ele só alimentava os
            // indicadores de cenário do delineamento. Sem eles, passou a ser escrito e nunca
            // lido, e um dado que ninguém consulta sugere um mecanismo que o modelo não tem.
            // A Sessao permanece, porque a autocorrelação AR(1) é estimada dentro dela.
            public string Sessao { get; init; }
            public DateTime Instante { get; init; }
            public IReadOnlyCollection<string> NomesDeObjetos { get; init; }

            public double DeltaPupilar { get; init; }
            public double LinhaDeBase { get; init; }

            public double OlharX { get; init; }
            public double OlharY { get; init; }

            public double Temperatura { get; init; }
            public double Umidade { get; init; }
            public double Ruido { get; init; }
            public double Gas { get; init; }

            public double Residuo { get; set; }
        }

        /// <summary>
        /// Estima os efeitos de elemento a partir das sessões fornecidas.
        /// </summary>
        /// <param name="sessoes">Sessões já construídas, com baseline pupilar atribuída.</param>
        /// <returns>
        /// Efeitos indexados por nome de elemento, mais o σ_ΔD da amostra. Devolve um resultado
        /// vazio — nunca nulo, nunca exceção — quando não há fixação com ΔD válido.
        /// </returns>
        public ResultadoDaCamada1 Estimar(IEnumerable<Sessao> sessoes)
        {
            var avisos = new List<string>();
            var observacoes = ColetarObservacoes(sessoes);

            if (observacoes.Count == 0)
            {
                avisos.Add("Nenhuma fixação com ΔD válido: sem baseline pupilar ou sem diâmetro " +
                           "medido nas sessões carregadas.");

                return new ResultadoDaCamada1
                {
                    EfeitosPorElemento = new Dictionary<string, EfeitoDeElemento>(StringComparer.OrdinalIgnoreCase),
                    DesvioPadraoDoDeltaPupilarMm = double.NaN,
                    Avisos = avisos,
                };
            }

            // σ_ΔD é o desvio-padrão dos ΔD BRUTOS de toda a amostra — a "variação pupilar na
            // amostra" da Equação 4 —, e não a dispersão dos resíduos. Cada fixação conta uma vez,
            // mesmo quando incide sobre vários objetos, pois é uma só observação.
            double sigmaDeltaD = CalcularDesvioPadraoAmostral(observacoes.Select(o => o.DeltaPupilar));

            if (double.IsNaN(sigmaDeltaD) || sigmaDeltaD <= 0.0)
            {
                avisos.Add("σ_ΔD é zero ou indefinido: todas as fixações têm o mesmo ΔD, ou há uma " +
                           "só observação. Valor apenas descritivo — o índice usa tau_nu.");
            }

            DescontarCovariaveis(observacoes, avisos);
            DescontarEfeitoDeParticipante(observacoes, avisos);

            double autocorrelacao = EstimarAutocorrelacao(observacoes);
            var (efeitos, dispersaoDosEfeitos) = EstimarPorElemento(observacoes, autocorrelacao, avisos);

            return new ResultadoDaCamada1
            {
                EfeitosPorElemento           = efeitos,
                DesvioPadraoDoDeltaPupilarMm = sigmaDeltaD,
                DispersaoDosEfeitosMm        = dispersaoDosEfeitos,
                Avisos                       = avisos,
            };
        }

        // ── Componentes de variância e encolhimento (puros, verificáveis) ──

        /// <summary>
        /// τ̂² — variância ENTRE grupos, por método dos momentos PONDERADO PELA PRECISÃO
        /// (DERSIMONIAN; LAIRD, 1986), com piso em zero.
        /// </summary>
        /// <remarks>
        /// A variância observada entre as médias soma duas parcelas: a variação real entre os
        /// grupos e o ruído amostral de cada média. O estimador isola a primeira comparando a
        /// heterogeneidade observada, Q = Σ wᵢ(ȳᵢ − ȳ_w)², com a que se esperaria se os grupos
        /// fossem idênticos, que é k − 1; a diferença, dividida pela constante de escala
        /// C = Σwᵢ − Σwᵢ²/Σwᵢ, é τ̂². Os pesos wᵢ = 1/se²ᵢ são as precisões, de modo que cada
        /// grupo influi na medida da sua confiabilidade.
        ///
        /// <b>Continua sendo método dos momentos</b> — não há verossimilhança envolvida, e a
        /// descrição do §5.3.1 permanece exata. O que muda é a ponderação.
        ///
        /// <b>E a ponderação é o ponto.</b> A forma não ponderada — variância das médias menos a
        /// MÉDIA SIMPLES das variâncias amostrais — dá o mesmo peso a um elemento observado
        /// duzentas vezes e a outro observado duas. Como se² cresce com 1/n, meia dúzia de
        /// elementos pouco observados basta para inflar aquela média, tornar a subtração negativa
        /// e zerar τ̂² — o que, por sua vez, encolhe TODOS os efeitos a zero e devolve a tabela
        /// inteira como Neutro, apagando inclusive a diferença dos elementos fartamente
        /// observados. Foi o que se observou nos dados da pesquisa, e é a razão da escolha.
        ///
        /// O piso em zero existe porque a diferença pode sair negativa quando a variação real é
        /// pequena — e variância negativa não é estimativa, é ruído.
        ///
        /// Médias sem variância amostral estimável (NaN) ficam fora do cálculo.
        /// </remarks>
        public static double EstimarVarianciaEntreGrupos(
            IReadOnlyList<double> medias, IReadOnlyList<double> varianciasAmostrais)
        {
            var validas = new List<(double Media, double Variancia)>();

            for (int i = 0; i < medias.Count && i < varianciasAmostrais.Count; i++)
            {
                if (!double.IsNaN(medias[i]) && !double.IsNaN(varianciasAmostrais[i]))
                    validas.Add((medias[i], varianciasAmostrais[i]));
            }

            if (validas.Count < ParametrosDaCamada1.MinimoDeElementosParaDispersao)
                return double.NaN;

            // Variância amostral nula produziria peso infinito. O piso mantém a conta finita e,
            // no limite, devolve exatamente a variância das médias — que é o resultado correto
            // quando não há ruído a descontar.
            const double VarianciaMinima = 1e-12;

            double somaDosPesos = 0.0;
            double somaDosPesosAoQuadrado = 0.0;
            double somaPonderada = 0.0;

            foreach (var (media, variancia) in validas)
            {
                double peso = 1.0 / Math.Max(variancia, VarianciaMinima);
                somaDosPesos += peso;
                somaDosPesosAoQuadrado += peso * peso;
                somaPonderada += peso * media;
            }

            if (somaDosPesos <= 0.0 || double.IsNaN(somaDosPesos) || double.IsInfinity(somaDosPesos))
                return double.NaN;

            double mediaPonderada = somaPonderada / somaDosPesos;

            double q = 0.0;

            foreach (var (media, variancia) in validas)
            {
                double peso = 1.0 / Math.Max(variancia, VarianciaMinima);
                double desvio = media - mediaPonderada;
                q += peso * desvio * desvio;
            }

            double escala = somaDosPesos - (somaDosPesosAoQuadrado / somaDosPesos);

            if (escala <= 0.0 || double.IsNaN(escala) || double.IsInfinity(escala))
                return double.NaN;

            return Math.Max(0.0, (q - (validas.Count - 1)) / escala);
        }

        /// <summary>
        /// λ — fator de encolhimento empírico-Bayes: τ² / (τ² + se²).
        /// </summary>
        /// <remarks>
        /// É a razão entre a variação real do grupo e a variação total (real + ruído da própria
        /// estimativa): quanto menos confiável a média bruta, mais forte o puxão para zero.
        /// Casos-limite, todos deliberados:
        /// <list type="bullet">
        /// <item>se² não estimável (NaN) — λ = 0: sem medida de precisão, a estimativa é reduzida
        /// ao que o conjunto informa sobre um elemento qualquer, que é zero;</item>
        /// <item>τ² não estimável (NaN) — λ = 1: sem dispersão de referência, o encolhimento é
        /// desativado em vez de inventado;</item>
        /// <item>τ² = 0 — λ = 0: a variação entre grupos é indistinguível do ruído, e toda média
        /// bruta é tratada como ruído;</item>
        /// <item>se² = 0 — λ = 1: estimativa exata não encolhe.</item>
        /// </list>
        /// </remarks>
        public static double CalcularFatorDeEncolhimento(double tau2, double se2)
        {
            if (double.IsNaN(se2))
                return 0.0;

            if (double.IsNaN(tau2))
                return 1.0;

            if (tau2 <= 0.0)
                return 0.0;

            if (se2 <= 0.0)
                return 1.0;

            return tau2 / (tau2 + se2);
        }

        // ── Efeito aleatório de indivíduo (μ_i) ──────────────────────────

        /// <summary>
        /// Estima μ̂_i por participante com encolhimento empírico-Bayes e o subtrai dos resíduos.
        /// </summary>
        /// <remarks>
        /// É o efeito aleatório de indivíduo da Equação 3: absorve a reatividade pupilar própria
        /// de cada participante para que o efeito de elemento não seja contaminado por QUEM olhou.
        /// A média bruta dos resíduos do participante é encolhida por λ_i = τ²_μ/(τ²_μ + se²_i) —
        /// participantes com poucas fixações contribuem pouco para o próprio desconto, em vez de
        /// arrastá-lo com uma média ruidosa.
        ///
        /// Vem ANTES do estágio de elemento por necessidade, não por conveniência: um participante
        /// de pupila sistematicamente maior que olhou muito para um elemento inflaria a média
        /// daquele elemento se o seu nível individual não fosse removido primeiro.
        /// </remarks>
        private static void DescontarEfeitoDeParticipante(
            List<ObservacaoDeFixacao> observacoes, List<string> avisos)
        {
            var porParticipante = observacoes
                .GroupBy(o => o.Participante ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var estatisticas = new Dictionary<string, (double Media, double Variancia)>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var grupo in porParticipante)
            {
                var residuos = grupo.Select(o => o.Residuo).ToList();
                double media = residuos.Average();
                double desvio = CalcularDesvioPadraoAmostral(residuos);
                double variancia = double.IsNaN(desvio)
                    ? double.NaN
                    : (desvio * desvio) / residuos.Count;

                estatisticas[grupo.Key] = (media, variancia);
            }

            double tau2Mu = EstimarVarianciaEntreGrupos(
                estatisticas.Values.Select(e => e.Media).ToList(),
                estatisticas.Values.Select(e => e.Variancia).ToList());

            if (double.IsNaN(tau2Mu))
            {
                avisos.Add("Efeito de indivíduo (mu_i) não descontado: participantes de menos " +
                           "para estimar a variância entre eles.");
                return;
            }

            foreach (var observacao in observacoes)
            {
                var (media, variancia) = estatisticas[observacao.Participante ?? string.Empty];
                double lambda = CalcularFatorDeEncolhimento(tau2Mu, variancia);
                observacao.Residuo -= lambda * media;
            }

            avisos.Add(string.Format(CultureInfo.CurrentCulture,
                "Efeito aleatório de indivíduo descontado com encolhimento: " +
                "tau_mu = {0:N3} mm, {1} participante(s).",
                Math.Sqrt(tau2Mu), estatisticas.Count));
        }

        // ── Coleta ───────────────────────────────────────────────────────

        /// <summary>
        /// Reduz as sessões a uma observação por fixação com ΔD válido, descartando as demais.
        /// </summary>
        private static List<ObservacaoDeFixacao> ColetarObservacoes(IEnumerable<Sessao> sessoes)
        {
            var observacoes = new List<ObservacaoDeFixacao>();

            if (sessoes == null)
                return observacoes;

            foreach (var sessao in sessoes)
            {
                if (sessao == null || double.IsNaN(sessao.BaselinePupilar))
                    continue;

                var linhasPorIndice = sessao.DadosBrutos?
                    .GroupBy(linha => linha.IndiceLinhaOriginal)
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var fixacao in sessao.Fixacoes)
                {
                    double diametro = ObterDiametroMedioDaFixacao(fixacao);

                    if (double.IsNaN(diametro))
                        continue;

                    var linha = ObterLinhaRepresentativa(fixacao, linhasPorIndice);

                    observacoes.Add(new ObservacaoDeFixacao
                    {
                        Participante   = sessao.IdentificacaoManual,
                        Sessao         = sessao.RotuloDeSessao,
                        Instante       = fixacao.TimestampInicio,
                        NomesDeObjetos = fixacao.NomesDeObjetos?.ToList() ?? new List<string>(),
                        DeltaPupilar   = diametro - sessao.BaselinePupilar,
                        LinhaDeBase    = sessao.BaselinePupilar,
                        OlharX         = MediaBinocular(linha?.DirecaoOlharEsquerdoX, linha?.DirecaoOlharDireitoX),
                        OlharY         = MediaBinocular(linha?.DirecaoOlharEsquerdoY, linha?.DirecaoOlharDireitoY),
                        Temperatura    = LerSensor(linha, MapeamentoDeColunasDeSensor.Temperatura),
                        Umidade        = LerSensor(linha, MapeamentoDeColunasDeSensor.Umidade),
                        Ruido          = LerSensor(linha, MapeamentoDeColunasDeSensor.Ruido),
                        // CO entra na Equação 3 como covariável BINÁRIA (β_C): a normalização
                        // traduz as duas codificações do arquivo (0/1 e a legenda 0,162/0,83)
                        // para 1 = detectado / 0 = não detectado. Ver ParametrosDePresencaDeGas.
                        Gas            = ParametrosDePresencaDeGas.NormalizarParaBinario(
                                             LerSensor(linha, MapeamentoDeColunasDeSensor.PresencaDeCo)),
                    });
                }
            }

            return observacoes;
        }

        private static LinhaDeSessao ObterLinhaRepresentativa(
            Fixacao fixacao, Dictionary<int, LinhaDeSessao> linhasPorIndice)
        {
            if (linhasPorIndice == null || fixacao.IndicesLinhasOriginais == null
                || fixacao.IndicesLinhasOriginais.Count == 0)
            {
                return null;
            }

            return linhasPorIndice.TryGetValue(fixacao.IndicesLinhasOriginais[0], out var linha)
                ? linha
                : null;
        }

        private static double MediaBinocular(double? esquerdo, double? direito)
        {
            if (!esquerdo.HasValue || !direito.HasValue)
                return double.NaN;

            bool e = !double.IsNaN(esquerdo.Value);
            bool d = !double.IsNaN(direito.Value);

            if (e && d) return (esquerdo.Value + direito.Value) / 2.0;
            if (e) return esquerdo.Value;
            if (d) return direito.Value;

            return double.NaN;
        }

        /// <summary>
        /// Lê um valor numérico da leitura de sensor vigente na fixação, localizando a coluna por
        /// conteúdo do cabeçalho. NaN quando não há leitura associada.
        /// </summary>
        private static double LerSensor(LinhaDeSessao linha, IReadOnlyList<string> candidatos)
        {
            var campos = linha?.CamposSensor;

            if (campos == null || campos.Count == 0)
                return double.NaN;

            // Correspondência exata, e não por trecho: ver MapeamentoDeColunasDeSensor. Buscar
            // "CO" como trecho casaria com qualquer cabeçalho que contivesse essas duas letras.
            foreach (string candidato in candidatos)
            {
                foreach (var campo in campos)
                {
                    if (!CalculadorDeMetricasPorObjeto.NomeDeColunaCorresponde(campo.Key, candidato))
                        continue;

                    if (CalculadorDeMetricasPorObjeto.TentarConverterNumero(campo.Value, out double valor))
                        return valor;
                }
            }

            return double.NaN;
        }

        /// <summary>
        /// Diâmetro pupilar médio bienocular da fixação, em mm, ou NaN se nenhum olho tem medida
        /// válida. Mesma regra usada por <see cref="CalculadorDeMetricasPorObjeto"/>.
        /// </summary>
        private static double ObterDiametroMedioDaFixacao(Fixacao fixacao)
        {
            bool esquerdaValida = !double.IsNaN(fixacao.DiametroMedianoPupilaEsquerda)
                               && fixacao.DiametroMedianoPupilaEsquerda > 0;
            bool direitaValida = !double.IsNaN(fixacao.DiametroMedianoPupilaDireita)
                              && fixacao.DiametroMedianoPupilaDireita > 0;

            if (esquerdaValida && direitaValida)
                return (fixacao.DiametroMedianoPupilaEsquerda + fixacao.DiametroMedianoPupilaDireita) / 2.0;

            if (esquerdaValida)
                return fixacao.DiametroMedianoPupilaEsquerda;

            if (direitaValida)
                return fixacao.DiametroMedianoPupilaDireita;

            return double.NaN;
        }

        // ── Estágio 1: desconto das covariáveis da Equação 3 ──────────────

        /// <summary>
        /// Ajusta a parte da Equação 3 anterior a ν_e e guarda os resíduos em cada observação.
        /// </summary>
        /// <remarks>
        /// Um regressor cuja leitura falte em parte das observações não pode ser incluído sem
        /// descartar essas linhas — e descartar fixações por falta de sensor enviesaria a amostra
        /// para os instantes em que o sensor respondeu. A regra adotada é: o regressor só entra se
        /// estiver presente em TODAS as observações e variar; caso contrário fica de fora, com
        /// aviso. Assim a amostra permanece íntegra e o que entrou no ajuste fica declarado.
        /// </remarks>
        private static void DescontarCovariaveis(List<ObservacaoDeFixacao> observacoes, List<string> avisos)
        {
            // Nem o participante nem o cenário entram aqui. μ_i é efeito ALEATÓRIO na Equação 3,
            // estimado com encolhimento em DescontarEfeitoDeParticipante — indicadores fixos o
            // tratariam como β, que não é o que o modelo declara. O cenário deixou de figurar na
            // equação na revisão de 01/09/2026, e por isso não gera mais colunas de delineamento.

            // Descritores das colunas contínuas: nome para o aviso e leitor do valor.
            var continuas = new List<(string Nome, Func<ObservacaoDeFixacao, double> Ler)>
            {
                ("linha de base (β_Base)", o => o.LinhaDeBase),
                ("olhar X",               o => o.OlharX),
                ("olhar Y",               o => o.OlharY),
                ("olhar X²",              o => o.OlharX * o.OlharX),
                ("olhar Y²",              o => o.OlharY * o.OlharY),
                ("olhar X·Y",             o => o.OlharX * o.OlharY),
                ("temperatura (β_T)",     o => o.Temperatura),
                ("umidade (β_H)",         o => o.Umidade),
                ("ruído (β_R)",           o => o.Ruido),
                ("gás (β_C)",             o => o.Gas),
            };

            var aceitas = new List<(string Nome, Func<ObservacaoDeFixacao, double> Ler)>();

            foreach (var coluna in continuas)
            {
                var valores = observacoes.Select(coluna.Ler).ToList();

                if (valores.Any(double.IsNaN))
                {
                    int faltantes = valores.Count(double.IsNaN);
                    avisos.Add($"Regressor \"{coluna.Nome}\" fora do ajuste: sem leitura em " +
                               $"{faltantes} de {valores.Count} fixações.");
                    continue;
                }

                if (valores.Distinct().Count() < 2)
                {
                    avisos.Add($"Regressor \"{coluna.Nome}\" fora do ajuste: constante na amostra, " +
                               "sem variação da qual estimar coeficiente.");
                    continue;
                }

                aceitas.Add(coluna);
            }

            // A parte fixa é composta APENAS pelas covariáveis contínuas aceitas: a linha de
            // base, a superfície do olhar e as quatro ambientais. Não há intercepto nem
            // indicadores de cenário — a Equação 3 foi reescrita em 01/09/2026 sem β₀ e sem β_S,
            // por decisão do autor.
            //
            // Duas consequências ficam registradas aqui, porque não são visíveis no resultado:
            //
            // 1) O cenário deixa de ser descontado. O deslocamento médio de cada cenário passa a
            //    ser absorvido pelos efeitos aleatórios estimados adiante — sobretudo por ν_e,
            //    já que boa parte dos elementos existe em um só cenário. Um elemento de um
            //    cenário mais restaurador tende, portanto, a aparecer como restaurador em parte
            //    por essa razão, e não apenas por si.
            //
            // 2) Nenhuma coluna carrega o nível geral de ΔD. Sem intercepto e sem indicadores,
            //    o ajuste passa pela origem, e a média global do ΔD é absorvida pelas contínuas
            //    — em especial por β_Base, cujos valores ficam longe de zero (diâmetros de ~3 a
            //    4 mm). Em consequência, β_Base deixa de medir apenas o quanto da linha de base
            //    a subtração não removeu: ele acumula também a ancoragem do modelo.
            int totalDeColunas = aceitas.Count;

            var delineamento = new List<double[]>(observacoes.Count);
            var resposta = new List<double>(observacoes.Count);

            foreach (var observacao in observacoes)
            {
                var linha = new double[totalDeColunas];
                int coluna = 0;

                foreach (var continua in aceitas)
                    linha[coluna++] = continua.Ler(observacao);

                delineamento.Add(linha);
                resposta.Add(observacao.DeltaPupilar);
            }

            var ajuste = AjusteDeMinimosQuadrados.Ajustar(delineamento, resposta);

            if (ajuste == null)
            {
                // Sem ajuste possível, o resíduo é o próprio ΔD: nada foi descontado. É o
                // comportamento da versão anterior, e o aviso registra a degradação.
                avisos.Add("Não foi possível ajustar as covariáveis da Equação 3; ν̂_e passa a ser " +
                           "estimado sobre o ΔD sem desconto algum.");

                foreach (var observacao in observacoes)
                    observacao.Residuo = observacao.DeltaPupilar;

                return;
            }

            if (ajuste.ColunasNaoEstimaveis.Count > 0)
            {
                avisos.Add($"{ajuste.ColunasNaoEstimaveis.Count} coluna(s) do ajuste saíram por " +
                           "dependência linear com as demais; o coeficiente correspondente é zero.");
            }

            avisos.Add($"Camada 1: {aceitas.Count} de {continuas.Count} regressores contínuos no " +
                       $"ajuste, sem intercepto e sem efeito fixo de cenário, " +
                       $"sobre {observacoes.Count} fixações.");

            for (int i = 0; i < observacoes.Count; i++)
                observacoes[i].Residuo = ajuste.Residuos[i];
        }

        // O auxiliar NiveisDe foi REMOVIDO com a saída de β_S da Equação 3: ele existia para
        // enumerar os níveis do fator de cenário e eleger o de referência. Sem fator categórico
        // no delineamento, deixou de ter chamador, e um método vivo sem uso sugere um mecanismo
        // que o modelo não tem mais.

        // ── Resíduo AR(1) ────────────────────────────────────────────────

        /// <summary>
        /// Estima a autocorrelação de defasagem 1 dos resíduos, dentro de cada sessão.
        /// </summary>
        /// <remarks>
        /// A Equação 3 declara <c>e_iet</c> como resíduo AR(1), e há boa razão para isso: fixações
        /// vizinhas no tempo compartilham o estado de ativação do participante, de modo que os
        /// resíduos não são independentes. Ignorar essa dependência subestima se(ν̂_e) e leva a
        /// Camada 2 a rotular como Restaurador ou Estressor elementos cuja evidência não sustenta
        /// a afirmação.
        ///
        /// O ajuste misto do texto absorveria a correlação na estimação. Aqui ela é estimada a
        /// posteriori e aplicada como correção do tamanho amostral efetivo — solução usual quando
        /// não se dispõe do ajuste completo, e conservadora, pois só amplia o erro-padrão.
        ///
        /// Os pares atravessam apenas fixações da MESMA sessão e em ordem cronológica: entre
        /// sessões não há continuidade temporal que justifique correlação.
        /// </remarks>
        private static double EstimarAutocorrelacao(List<ObservacaoDeFixacao> observacoes)
        {
            double soma = 0.0;
            double somaDosQuadrados = 0.0;
            int pares = 0;

            foreach (var sessao in observacoes.GroupBy(o => o.Sessao ?? string.Empty,
                                                       StringComparer.OrdinalIgnoreCase))
            {
                var ordenadas = sessao.OrderBy(o => o.Instante).ToList();

                for (int i = 1; i < ordenadas.Count; i++)
                {
                    soma += ordenadas[i].Residuo * ordenadas[i - 1].Residuo;
                    somaDosQuadrados += ordenadas[i - 1].Residuo * ordenadas[i - 1].Residuo;
                    pares++;
                }
            }

            if (pares < ParametrosDaCamada1.MinimoDeParesParaAutocorrelacao || somaDosQuadrados <= 0.0)
                return 0.0;

            double rho = soma / somaDosQuadrados;

            // Fora de [0, 1) a correção do tamanho efetivo não faz sentido. Autocorrelação
            // negativa AUMENTARIA o n efetivo, o que reduziria o erro-padrão — exatamente o que
            // esta correção existe para não fazer.
            if (double.IsNaN(rho) || rho <= 0.0)
                return 0.0;

            return Math.Min(rho, ParametrosDaCamada1.AutocorrelacaoMaxima);
        }

        // ── Efeito aleatório de elemento (ν_e) ───────────────────────────

        /// <summary>
        /// Agrupa as observações por elemento e estima ν̂_e como efeito aleatório: média bruta
        /// dos resíduos, encolhida por λ_e = τ²_ν/(τ²_ν + se²_e), com IC de 85% explícito.
        /// </summary>
        /// <returns>Os efeitos indexados por nome de elemento e τ̂_ν, a dispersão entre eles.</returns>
        /// <remarks>
        /// O agrupamento é por <c>nomeDoObjeto</c>, conforme o Quadro 39 — o cenário já foi
        /// descontado como efeito fixo β_S e o participante como efeito aleatório μ_i, e nenhum
        /// dos dois compõe a identidade do elemento.
        ///
        /// Uma fixação que abrange mais de um objeto contribui para TODOS eles: o agrupamento por
        /// estabilidade do olhar tolera que o ponto de incidência atravesse a fronteira entre
        /// elementos, e não há como atribuí-la a um só sem descartar informação.
        ///
        /// <b>O erro-padrão devolvido é o A POSTERIORI da estimativa encolhida:</b> sob o modelo
        /// normal com variâncias conhecidas, a variância a posteriori do efeito é
        /// (1/τ² + 1/se²)⁻¹ = λ·se², logo se_post = √λ·se. A razão z a posteriori vale portanto
        /// √λ vezes a razão bruta — sempre igual ou mais conservadora, que é exatamente o efeito
        /// de multiplicidade que o partial pooling entrega.
        /// </remarks>
        // Regra ADOTADA para fixação sobre múltiplos objetos: a fixação entra na média de cada
        // objeto que tocou. O TextoFinal não trata do caso; entre as alternativas (primeiro
        // objeto, maior contato, descarte), esta é a única que não descarta informação nem exige
        // um critério de desempate que o dado não oferece. Se o texto vier a especificar outra,
        // este é o único ponto a ajustar.
        private static (Dictionary<string, EfeitoDeElemento> Efeitos, double DispersaoMm) EstimarPorElemento(
            List<ObservacaoDeFixacao> observacoes, double autocorrelacao, List<string> avisos)
        {
            var porElemento = new Dictionary<string, List<ObservacaoDeFixacao>>(StringComparer.OrdinalIgnoreCase);

            foreach (var observacao in observacoes)
            {
                foreach (string nomeDoObjeto in observacao.NomesDeObjetos)
                {
                    if (string.IsNullOrWhiteSpace(nomeDoObjeto))
                        continue;

                    if (!porElemento.TryGetValue(nomeDoObjeto, out var lista))
                    {
                        lista = new List<ObservacaoDeFixacao>();
                        porElemento[nomeDoObjeto] = lista;
                    }

                    lista.Add(observacao);
                }
            }

            if (autocorrelacao > 0.0)
            {
                avisos.Add("Erro-padrão corrigido por autocorrelação AR(1) dos resíduos: " +
                           $"rho = {autocorrelacao.ToString("N3", CultureInfo.CurrentCulture)}.");
            }

            // Primeira passada: médias brutas e variâncias amostrais, insumos de τ̂²_ν.
            var brutos = new Dictionary<string, (double Media, double Se2, int N)>(StringComparer.OrdinalIgnoreCase);

            foreach (var (nomeDoObjeto, lista) in porElemento)
            {
                var residuos = lista.Select(o => o.Residuo).ToList();
                int n = residuos.Count;

                // Tamanho amostral efetivo sob AR(1): n·(1−ρ)/(1+ρ). Com ρ = 0 devolve o próprio n.
                double nEfetivo = n * (1.0 - autocorrelacao) / (1.0 + autocorrelacao);

                double desvio = CalcularDesvioPadraoAmostral(residuos);
                double se2 = !double.IsNaN(desvio) && n >= 2 && nEfetivo >= 1.0
                    ? (desvio * desvio) / nEfetivo
                    : double.NaN;

                brutos[nomeDoObjeto] = (residuos.Average(), se2, n);
            }

            double tau2 = EstimarVarianciaEntreGrupos(
                brutos.Values.Select(b => b.Media).ToList(),
                brutos.Values.Select(b => b.Se2).ToList());

            double tau = double.IsNaN(tau2) ? double.NaN : Math.Sqrt(tau2);

            if (double.IsNaN(tau2))
            {
                avisos.Add("Dispersão entre elementos (tau_nu) não estimável: elementos de menos " +
                           "com erro-padrão definido. Encolhimento desativado e índices no ponto " +
                           "neutro.");
            }
            else if (tau2 <= 0.0)
            {
                avisos.Add("Dispersão entre elementos (tau_nu) igual a zero: a variação entre os " +
                           "elementos é indistinguível do ruído amostral. Todos os efeitos " +
                           "encolhem a zero e nenhum elemento é afirmado.");
            }
            else
            {
                avisos.Add(string.Format(CultureInfo.CurrentCulture,
                    "Efeitos de elemento encolhidos (empírico-Bayes): tau_nu = {0:N3} mm sobre " +
                    "{1} elemento(s). O encolhimento controla as falsas descobertas das " +
                    "comparações simultâneas (Gelman, Hill e Yajima, 2012).",
                    tau, brutos.Count));
            }

            // Segunda passada: encolhe, calcula o erro a posteriori e o IC de 85%.
            var efeitos = new Dictionary<string, EfeitoDeElemento>(StringComparer.OrdinalIgnoreCase);

            foreach (var (nomeDoObjeto, (mediaBruta, se2, n)) in brutos)
            {
                double lambda = CalcularFatorDeEncolhimento(tau2, se2);
                double efeitoEncolhido = lambda * mediaBruta;

                double erroPadraoPosterior = !double.IsNaN(se2) && lambda > 0.0
                    ? Math.Sqrt(lambda * se2)
                    : double.NaN;

                double margem = ParametrosDaCamada2.QuantilNormal85 * erroPadraoPosterior;

                efeitos[ResultadoDaCamada1.ComporChave(nomeDoObjeto, null)] = new EfeitoDeElemento
                {
                    Elemento                = nomeDoObjeto,
                    Cenario                 = null,
                    EfeitoNuMm              = efeitoEncolhido,
                    EfeitoBrutoMm           = mediaBruta,
                    FatorDeEncolhimento     = lambda,
                    ErroPadraoMm            = erroPadraoPosterior,
                    LimiteInferiorIc85Mm    = efeitoEncolhido - margem,
                    LimiteSuperiorIc85Mm    = efeitoEncolhido + margem,
                    QuantidadeDeObservacoes = n,
                };
            }

            return (efeitos, tau);
        }

        /// <summary>
        /// Desvio-padrão amostral, com denominador (n − 1). Devolve NaN com menos de duas
        /// observações — não zero, que seria lido como "sem dispersão" em vez de "não estimável".
        /// </summary>
        public static double CalcularDesvioPadraoAmostral(IEnumerable<double> valores)
        {
            var lista = valores.Where(v => !double.IsNaN(v)).ToList();

            if (lista.Count < 2)
                return double.NaN;

            double media = lista.Average();
            double somaDosQuadrados = lista.Sum(v => (v - media) * (v - media));

            return Math.Sqrt(somaDosQuadrados / (lista.Count - 1));
        }
    }
}
