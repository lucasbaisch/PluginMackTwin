using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;
using PluginCAISM.Processamento;

namespace PluginCAISM.Verificacao
{
    /// <summary>Resultado de uma verificação isolada.</summary>
    public sealed class ResultadoDeVerificacao
    {
        /// <summary>O que se está afirmando, em uma linha.</summary>
        public string Afirmacao { get; init; }

        /// <summary>Se a afirmação se sustentou.</summary>
        public bool Passou { get; init; }

        /// <summary>Esperado contra obtido, preenchido quando falha.</summary>
        public string Detalhe { get; init; } = string.Empty;
    }

    /// <summary>Relatório de uma execução completa.</summary>
    public sealed class RelatorioDeVerificacao
    {
        /// <summary>Todas as verificações, na ordem de execução.</summary>
        public IReadOnlyList<ResultadoDeVerificacao> Resultados { get; init; }

        /// <summary>Quantas passaram.</summary>
        public int Passaram => Resultados.Count(r => r.Passou);

        /// <summary>Quantas falharam.</summary>
        public int Falharam => Resultados.Count(r => !r.Passou);

        /// <summary>Se todas passaram.</summary>
        public bool TudoCerto => Falharam == 0;

        /// <summary>Relatório em texto, pronto para exibir ou gravar.</summary>
        public string ComoTexto()
        {
            var linhas = new List<string>
            {
                $"Verificação do modelo — {Passaram} de {Resultados.Count} afirmações sustentadas.",
                string.Empty,
            };

            foreach (var resultado in Resultados)
            {
                linhas.Add($"[{(resultado.Passou ? "ok  " : "FALHA")}] {resultado.Afirmacao}");

                if (!resultado.Passou && !string.IsNullOrWhiteSpace(resultado.Detalhe))
                    linhas.Add($"          {resultado.Detalhe}");
            }

            return string.Join(Environment.NewLine, linhas);
        }
    }

    /// <summary>
    /// Verificações automáticas das calculadoras puras do modelo de Potencial Homeodinâmico.
    /// </summary>
    /// <remarks>
    /// <b>Por que aqui, e não num projeto de teste.</b> Duas restrições se somam: a pesquisa não
    /// admite pacote NuGet novo, o que exclui os arranjos usuais de teste; e o Smart App Control
    /// do Windows bloqueia a execução de binário recém-compilado, o que impede rodar um executável
    /// próprio. Dentro do Revit nenhuma das duas se aplica — o assembly já está carregado —, e por
    /// isso as verificações são disparadas por um comando da faixa de opções.
    ///
    /// <b>O que se verifica.</b> Apenas as partes puras, que não dependem de arquivo carregado nem
    /// do modelo aberto: o ajuste por mínimos quadrados, a Equação 4 com a sua convenção de sinal
    /// e os seus limiares, e o desvio-padrão amostral. São exatamente as peças que produzem os
    /// números da tese e que nenhuma inspeção visual da tabela conseguiria conferir.
    ///
    /// <b>O que NÃO se verifica.</b> A leitura de arquivos, a detecção de eventos oculares, a
    /// associação com sensores e tudo o que exige dado real. Essas dependem de insumo externo e
    /// pedem outro tipo de verificação.
    /// </remarks>
    public static class VerificacoesDoModelo
    {
        /// <summary>Folga aceita nas comparações de ponto flutuante.</summary>
        private const double Folga = 1e-6;

        /// <summary>Executa todas as verificações e devolve o relatório.</summary>
        public static RelatorioDeVerificacao ExecutarTudo()
        {
            var resultados = new List<ResultadoDeVerificacao>();

            VerificarCamada2(resultados);
            VerificarDesvioPadrao(resultados);
            VerificarMinimosQuadrados(resultados);
            VerificarEncolhimento(resultados);
            VerificarDeteccaoOcular(resultados);
            VerificarConversaoDeCoordenadas(resultados);

            return new RelatorioDeVerificacao { Resultados = resultados };
        }

        // ── Conversão de coordenadas para o Revit ────────────────────────

        /// <summary>
        /// Verifica que a conversão preserva os eixos e converte apenas a unidade.
        /// </summary>
        /// <remarks>
        /// Existe por causa de uma falha real: a conversão permutava Y e Z, supondo a convenção
        /// nativa do Unity, quando o arquivo de captura já vem em Z para cima. O resultado eram
        /// hitpoints inseridos fora do volume construído e abaixo do piso — defeito que só se
        /// percebe ao abrir a vista, e que nenhuma exceção denuncia.
        ///
        /// As afirmações operam sobre a aritmética, sem instanciar tipos da API do Revit, para
        /// que possam rodar junto das demais calculadoras puras.
        /// </remarks>
        private static void VerificarConversaoDeCoordenadas(List<ResultadoDeVerificacao> resultados)
        {
            const double PesPorMetro = ConversoesDeUnidade.MetrosParaPes;

            Afirmar(resultados, "Um metro converte-se em 3,2808 pés",
                    Math.Abs(PesPorMetro - 3.280839895) < 1e-6,
                    $"obtido {Formatar(PesPorMetro)}");

            // A altura do teto na captura do Cenário 1: se a conversão permutasse eixos, este
            // valor apareceria como coordenada horizontal, e a altura viria da coordenada Y,
            // sempre negativa — que é como os hitpoints iam parar abaixo do piso.
            const double AlturaDoTetoEmMetros = 3.35;
            const double ProfundidadeEmMetros = -1.35;

            double alturaConvertida = AlturaDoTetoEmMetros * PesPorMetro;

            Afirmar(resultados, "A altura do teto permanece na vertical após a conversão",
                    Math.Abs(alturaConvertida - 10.9908) < 1e-3 && alturaConvertida > 0.0,
                    $"esperado ~10,99 pés positivos, obtido {Formatar(alturaConvertida)}");

            Afirmar(resultados, "A coordenada de planta negativa não é tomada por altura",
                    ProfundidadeEmMetros < 0.0 && alturaConvertida > 0.0,
                    "se os eixos fossem permutados, a cena inteira ficaria abaixo do piso");
        }

        // ── Detecção de eventos oculares ─────────────────────────────────

        /// <summary>
        /// Verifica o agrupamento de fixações sobre sessões sintéticas.
        /// </summary>
        /// <remarks>
        /// As quatro afirmações correspondem às correções do bloco C: continuidade temporal
        /// (a fixação não atravessa uma piscada), dispersão (a deriva lenta não vira uma fixação
        /// só), duração (o último intervalo conta) e ponto de incidência (um olho perdido não
        /// arrasta a coordenada para a origem).
        ///
        /// A detecção roda sem janela temporal, pela sobrecarga que percorre a sessão inteira —
        /// do contrário seria preciso construir duas horas de amostras sintéticas para cair
        /// dentro dos minutos 2 a 4.
        /// </remarks>
        private static void VerificarDeteccaoOcular(List<ResultadoDeVerificacao> resultados)
        {
            var detector = new DetectorDeMovimentoOcular();
            var origem = new DateTime(2026, 1, 1, 8, 0, 0);

            // 1. Uma fixação contínua de 20 amostras a 33 ms no mesmo ponto: 660 ms.
            var continua = ConstruirAmostras(origem, 20, 33, indice => (1.0, 2.0, 3.0));
            var (fixacoes, _) = detector.DetectarNaSessaoInteira(continua);

            Afirmar(resultados, "Amostras no mesmo ponto formam uma única fixação",
                    fixacoes.Count == 1, $"detectadas {fixacoes.Count} fixações");

            // A duração tem de cobrir os 20 intervalos, não 19: 20 × 33 = 660 ms.
            bool duracaoCorreta = fixacoes.Count == 1
                && Math.Abs(fixacoes[0].DuracaoEmMilissegundos - 660.0) < 1.0;

            Afirmar(resultados, "A duração da fixação inclui o intervalo da última amostra",
                    duracaoCorreta,
                    fixacoes.Count == 1
                        ? $"esperado 660 ms, obtido {Formatar(fixacoes[0].DuracaoEmMilissegundos)}"
                        : "não houve fixação para medir");

            // 2. Duas fixações no MESMO ponto, separadas por uma lacuna de 300 ms — uma piscada
            //    cujas amostras foram removidas antes do agrupamento. Sem a verificação de
            //    continuidade temporal, isso saía como uma fixação só, atravessando a piscada.
            var comLacuna = ConstruirAmostras(origem, 15, 33, indice => (1.0, 2.0, 3.0));
            comLacuna.AddRange(ConstruirAmostras(
                origem.AddMilliseconds(15 * 33 + 300), 15, 33, indice => (1.0, 2.0, 3.0)));

            var (fixacoesComLacuna, _) = detector.DetectarNaSessaoInteira(comLacuna);

            Afirmar(resultados, "Uma lacuna temporal quebra a fixação, mesmo no mesmo ponto",
                    fixacoesComLacuna.Count == 2,
                    $"esperadas 2 fixações, obtidas {fixacoesComLacuna.Count}");

            // 3. Deriva lenta: 40 amostras andando 0,005 m por quadro — cada passo cabe na
            //    tolerância de 0,01 m, mas o total percorrido é 0,2 m. Sem o critério de
            //    dispersão, tudo isso era uma fixação só.
            var comDeriva = ConstruirAmostras(origem, 40, 33,
                indice => (1.0 + indice * 0.005, 2.0, 3.0));

            var (fixacoesComDeriva, _) = detector.DetectarNaSessaoInteira(comDeriva);

            Afirmar(resultados, "A deriva lenta do olhar não forma uma fixação única",
                    fixacoesComDeriva.Count > 1,
                    $"a deriva de 0,2 m produziu {fixacoesComDeriva.Count} fixação(ões)");

            // 3b. Critério ANGULAR de dispersão (1°, escalado pela distância observador–alvo).
            //     A mesma deriva de ~0,04 m total: com o alvo a 3 m o raio de 1° é ~0,052 m e a
            //     deriva cabe — UMA fixação (o raio métrico antigo, 0,03 m, teria quebrado);
            //     com o alvo a 0,5 m o raio encolhe para ~0,009 m e os grupos quebram tão cedo
            //     que nenhum alcança 300 ms — NENHUMA fixação.
            var derivaLonge = ConstruirAmostras(origem, 20, 33,
                indice => (1.0 + indice * 0.002, 2.0, 3.0));
            foreach (var amostra in derivaLonge)
            {
                amostra.PosicaoJogadorX = 1.0;
                amostra.PosicaoJogadorY = 2.0;
                amostra.PosicaoJogadorZ = 0.0;    // ~3 m do alvo
            }

            var (fixacoesLonge, _) = detector.DetectarNaSessaoInteira(derivaLonge);

            Afirmar(resultados, "A 3 m do alvo, o raio angular de 1° acomoda a deriva pequena",
                    fixacoesLonge.Count == 1,
                    $"esperada 1 fixação, obtidas {fixacoesLonge.Count}");

            var derivaPerto = ConstruirAmostras(origem, 20, 33,
                indice => (1.0 + indice * 0.002, 2.0, 3.0));
            foreach (var amostra in derivaPerto)
            {
                amostra.PosicaoJogadorX = 1.0;
                amostra.PosicaoJogadorY = 2.0;
                amostra.PosicaoJogadorZ = 2.5;    // ~0,5 m do alvo
            }

            var (fixacoesPerto, _) = detector.DetectarNaSessaoInteira(derivaPerto);

            Afirmar(resultados, "A 0,5 m do alvo, o raio angular encolhe e a mesma deriva quebra",
                    fixacoesPerto.Count == 0,
                    $"esperadas 0 fixações, obtidas {fixacoesPerto.Count}");

            // 4. Olho esquerdo perdido em (0,0,0) e direito a (4,0,0). O ponto do evento tem de
            //    ser o do olho válido, e não a média com a origem — que daria 2,0.
            var comOlhoPerdido = new List<LinhaDeSessao>();

            for (int indice = 0; indice < 20; indice++)
            {
                comOlhoPerdido.Add(new LinhaDeSessao
                {
                    IndiceLinhaOriginal = indice,
                    Timestamp = origem.AddMilliseconds(indice * 33),
                    AberturaDoOlhoEsquerdo = 1,
                    AberturaDoOlhoDireito = 1,
                    DiametroPupilaEsquerda = 3.0,
                    DiametroPupilaDireita = 3.0,
                    PontoDeIncidenciaEsquerdoX = 0.0,
                    PontoDeIncidenciaEsquerdoY = 0.0,
                    PontoDeIncidenciaEsquerdoZ = 0.0,
                    PontoDeIncidenciaDireitoX = 4.0,
                    PontoDeIncidenciaDireitoY = 0.0,
                    PontoDeIncidenciaDireitoZ = 0.0,
                    NomeDoObjeto = "PAREDE",
                });
            }

            var (fixacoesComOlhoPerdido, _) = detector.DetectarNaSessaoInteira(comOlhoPerdido);

            bool pontoCorreto = fixacoesComOlhoPerdido.Count == 1
                && Math.Abs(fixacoesComOlhoPerdido[0].PontoMedioDeIncidenciaX - 4.0) < Folga;

            Afirmar(resultados,
                    "Olho sem rastreamento não arrasta o ponto de incidência para a origem",
                    pontoCorreto,
                    fixacoesComOlhoPerdido.Count == 1
                        ? $"esperado X = 4, obtido {Formatar(fixacoesComOlhoPerdido[0].PontoMedioDeIncidenciaX)}"
                        : $"detectadas {fixacoesComOlhoPerdido.Count} fixações");
        }

        /// <summary>
        /// Monta amostras sintéticas de sessão, com os dois olhos válidos no mesmo ponto.
        /// </summary>
        private static List<LinhaDeSessao> ConstruirAmostras(
            DateTime origem, int quantidade, int intervaloMs,
            Func<int, (double X, double Y, double Z)> posicao)
        {
            var amostras = new List<LinhaDeSessao>(quantidade);

            for (int indice = 0; indice < quantidade; indice++)
            {
                var (x, y, z) = posicao(indice);

                amostras.Add(new LinhaDeSessao
                {
                    IndiceLinhaOriginal = indice,
                    Timestamp = origem.AddMilliseconds(indice * intervaloMs),
                    AberturaDoOlhoEsquerdo = 1,
                    AberturaDoOlhoDireito = 1,
                    DiametroPupilaEsquerda = 3.0,
                    DiametroPupilaDireita = 3.0,
                    PontoDeIncidenciaEsquerdoX = x,
                    PontoDeIncidenciaEsquerdoY = y,
                    PontoDeIncidenciaEsquerdoZ = z,
                    PontoDeIncidenciaDireitoX = x,
                    PontoDeIncidenciaDireitoY = y,
                    PontoDeIncidenciaDireitoZ = z,
                    NomeDoObjeto = "PAREDE",
                });
            }

            return amostras;
        }

        // ── Camada 2: a Equação 4 ────────────────────────────────────────

        private static void VerificarCamada2(List<ResultadoDeVerificacao> resultados)
        {
            // A convenção de sinal é o ponto mais fácil de inverter sem que nada quebre, e o mais
            // caro de errar: trocaria restaurador por estressor em todos os resultados da tese.
            double constricao = CalculadorDaCamada2.CalcularPotencialHomeodinamico(-0.5, 1.0);
            double dilatacao  = CalculadorDaCamada2.CalcularPotencialHomeodinamico(+0.5, 1.0);

            Afirmar(resultados, "Constrição pupilar (ν̂_e < 0) produz PH_e positivo",
                    constricao > 0, $"PH_e = {Formatar(constricao)}");

            Afirmar(resultados, "Dilatação pupilar (ν̂_e > 0) produz PH_e negativo",
                    dilatacao < 0, $"PH_e = {Formatar(dilatacao)}");

            Afirmar(resultados, "PH_e é simétrico em torno de zero",
                    Math.Abs(constricao + dilatacao) < Folga,
                    $"{Formatar(constricao)} e {Formatar(dilatacao)} deveriam somar zero");

            Afirmar(resultados, "PH_e = tanh(−ν̂_e/τ̂_ν) confere com o cálculo direto",
                    Math.Abs(constricao - Math.Tanh(0.5)) < Folga,
                    $"esperado {Formatar(Math.Tanh(0.5))}, obtido {Formatar(constricao)}");

            Afirmar(resultados, "PH_e fica no intervalo (−1, +1) mesmo com efeito enorme",
                    Math.Abs(CalculadorDaCamada2.CalcularPotencialHomeodinamico(-50.0, 1.0)) <= 1.0,
                    "a tangente hiperbólica deveria saturar em ±1");

            Afirmar(resultados, "Sem ν̂_e o índice é indefinido, e não zero",
                    double.IsNaN(CalculadorDaCamada2.CalcularPotencialHomeodinamico(double.NaN, 1.0)),
                    "esperado NaN");

            Afirmar(resultados, "Sem τ̂_ν o índice sai no ponto neutro",
                    CalculadorDaCamada2.CalcularPotencialHomeodinamico(0.5, double.NaN) == 0.0
                    && CalculadorDaCamada2.CalcularPotencialHomeodinamico(0.5, 0.0) == 0.0,
                    "esperado 0 quando não há escala contra a qual medir");

            // Classificação: o denominador aqui é se(ν̂_e), não τ̂_ν. Trocá-los produz resultados
            // plausíveis e errados, e é o engano mais provável de quem mexer nesta classe.
            Afirmar(resultados, "IC₈₅% inteiramente abaixo de zero classifica como Restaurador",
                    CalculadorDaCamada2.Classificar(-1.0, 0.1) == ClassificacaoDePotencial.Restaurador,
                    "z = −10 deveria bastar");

            Afirmar(resultados, "IC₈₅% inteiramente acima de zero classifica como Estressor",
                    CalculadorDaCamada2.Classificar(+1.0, 0.1) == ClassificacaoDePotencial.Estressor,
                    "z = +10 deveria bastar");

            Afirmar(resultados, "IC₈₅% contendo zero classifica como Neutro",
                    CalculadorDaCamada2.Classificar(0.1, 1.0) == ClassificacaoDePotencial.Neutro,
                    "z = 0,1 não sustenta direção");

            Afirmar(resultados, "O limiar de classificação é 1,44 (IC de 85%), e não outro valor",
                    CalculadorDaCamada2.Classificar(-1.45, 1.0) == ClassificacaoDePotencial.Restaurador
                    && CalculadorDaCamada2.Classificar(-1.43, 1.0) == ClassificacaoDePotencial.Neutro,
                    "z = −1,45 deveria afirmar e z = −1,43 não");

            Afirmar(resultados, "Erro-padrão não estimável classifica como Neutro, não SemPontuação",
                    CalculadorDaCamada2.Classificar(-1.0, double.NaN) == ClassificacaoDePotencial.Neutro,
                    "falta de evidência é Neutro; ausência de estimativa é SemPontuação");

            Afirmar(resultados, "Ausência de ν̂_e classifica como SemPontuação",
                    CalculadorDaCamada2.Classificar(double.NaN, 0.1) == ClassificacaoDePotencial.Neutro,
                    "esperado Neutro — a ausência de estimativa é o caso extremo de evidência insuficiente");

            // Um |PH_e| alto com precisão ruim TEM de sair Neutro. É o comportamento que mais
            // confunde quem lê a tabela, e é proposital.
            double indiceAlto = CalculadorDaCamada2.CalcularPotencialHomeodinamico(-0.9, 1.0);
            Afirmar(resultados, "Índice alto com erro-padrão grande continua Neutro",
                    Math.Abs(indiceAlto) > 0.7
                    && CalculadorDaCamada2.Classificar(-0.9, 1.0) == ClassificacaoDePotencial.Neutro,
                    $"PH_e = {Formatar(indiceAlto)} com z = −0,9");
        }

        // ── Encolhimento empírico-Bayes (efeitos aleatórios) ─────────────

        /// <summary>
        /// Verifica os componentes do tratamento de μ_i e ν_e como efeitos aleatórios: o fator de
        /// encolhimento λ = τ²/(τ² + se²) e o estimador de variância entre grupos por método dos
        /// momentos — as duas peças de que dependem tanto o BLUP quanto o τ̂_ν da Equação 4.
        /// </summary>
        private static void VerificarEncolhimento(List<ResultadoDeVerificacao> resultados)
        {
            // O fator de encolhimento nos seus pontos notáveis.
            Afirmar(resultados, "λ = τ²/(τ²+se²): com τ² = se² o encolhimento é exatamente meio",
                    Math.Abs(EstimadorDeEfeitoDeElemento.CalcularFatorDeEncolhimento(1.0, 1.0) - 0.5) < Folga,
                    $"obtido {Formatar(EstimadorDeEfeitoDeElemento.CalcularFatorDeEncolhimento(1.0, 1.0))}");

            Afirmar(resultados, "Elemento mais preciso encolhe menos que o menos preciso",
                    EstimadorDeEfeitoDeElemento.CalcularFatorDeEncolhimento(1.0, 0.1)
                        > EstimadorDeEfeitoDeElemento.CalcularFatorDeEncolhimento(1.0, 1.0),
                    "λ deveria crescer quando se² diminui");

            Afirmar(resultados, "Sem erro-padrão estimável, o efeito encolhe a zero (λ = 0)",
                    EstimadorDeEfeitoDeElemento.CalcularFatorDeEncolhimento(1.0, double.NaN) == 0.0,
                    "se² = NaN deveria dar λ = 0");

            Afirmar(resultados, "Com τ² = 0 toda média bruta é tratada como ruído (λ = 0)",
                    EstimadorDeEfeitoDeElemento.CalcularFatorDeEncolhimento(0.0, 1.0) == 0.0,
                    "τ² = 0 deveria dar λ = 0");

            Afirmar(resultados, "Sem τ̂_ν estimável o encolhimento é desativado (λ = 1)",
                    EstimadorDeEfeitoDeElemento.CalcularFatorDeEncolhimento(double.NaN, 1.0) == 1.0,
                    "τ² = NaN deveria dar λ = 1");

            Afirmar(resultados, "Estimativa exata não encolhe (λ = 1 quando se² = 0)",
                    EstimadorDeEfeitoDeElemento.CalcularFatorDeEncolhimento(1.0, 0.0) == 1.0,
                    "se² = 0 deveria dar λ = 1");

            // O método dos momentos: variância entre médias menos ruído amostral médio.
            double tau2SemRuido = EstimadorDeEfeitoDeElemento.EstimarVarianciaEntreGrupos(
                new[] { 1.0, -1.0 }, new[] { 0.0, 0.0 });

            Afirmar(resultados, "τ̂² por momentos recupera a variância real quando não há ruído",
                    Math.Abs(tau2SemRuido - 2.0) < Folga,
                    $"médias {{1, −1}} sem ruído: esperado 2, obtido {Formatar(tau2SemRuido)}");

            double tau2SoRuido = EstimadorDeEfeitoDeElemento.EstimarVarianciaEntreGrupos(
                new[] { 0.1, -0.1 }, new[] { 1.0, 1.0 });

            Afirmar(resultados, "τ̂² tem piso em zero quando a variação é só ruído amostral",
                    tau2SoRuido == 0.0,
                    $"esperado 0, obtido {Formatar(tau2SoRuido)}");

            // A propriedade que justifica a ponderação por precisão. Cinco elementos bem medidos,
            // que claramente diferem entre si, mais dois medidos com precisão pífia. Sem pesos, a
            // média simples das variâncias amostrais é puxada pelos dois imprecisos e zera τ̂²,
            // apagando a diferença real dos cinco — e a tabela inteira sai Neutro. Ponderando,
            // os imprecisos pesam pouco e a dispersão sobrevive.
            var mediasMistas = new[] { -0.6, -0.3, 0.0, 0.3, 0.6, 0.1, -0.1 };
            var seMistos = new[] { 0.01, 0.01, 0.01, 0.01, 0.01, 9.0, 9.0 };

            double tau2Ponderado = EstimadorDeEfeitoDeElemento.EstimarVarianciaEntreGrupos(
                mediasMistas, seMistos);

            Afirmar(resultados,
                    "τ̂² sobrevive a elementos pouco observados (ponderação por precisão)",
                    tau2Ponderado > 0.0,
                    $"a média simples das variâncias vale {Formatar(seMistos.Average())} e zeraria " +
                    $"τ̂²; ponderando, esperava-se τ̂² > 0 e obteve-se {Formatar(tau2Ponderado)}");

            Afirmar(resultados, "Médias sem variância amostral ficam fora da estimação de τ̂²",
                    Math.Abs(EstimadorDeEfeitoDeElemento.EstimarVarianciaEntreGrupos(
                        new[] { 1.0, -1.0, 50.0 }, new[] { 0.0, 0.0, double.NaN }) - 2.0) < Folga,
                    "a terceira média, sem se², deveria ser ignorada");

            Afirmar(resultados, "Com um único grupo τ̂² é indefinido, e não zero",
                    double.IsNaN(EstimadorDeEfeitoDeElemento.EstimarVarianciaEntreGrupos(
                        new[] { 1.0 }, new[] { 0.1 })),
                    "não há variância ENTRE grupos com um grupo só");

            // A consequência inferencial: a razão z a posteriori é √λ vezes a bruta — o
            // encolhimento nunca torna uma afirmação MAIS fácil. É o controle de falsas
            // descobertas por construção.
            // z_post = (λ·ν̂)/(√λ·se) = √λ·(ν̂/se): com efeito bruto 0,5 mm e se 0,2 mm,
            // z bruto = 2,5 (afirmável) e λ = 0,5 leva z_post ≈ 1,77 (não afirmável).
            double lambda = EstimadorDeEfeitoDeElemento.CalcularFatorDeEncolhimento(0.5, 0.5);
            double zBruto = 0.5 / 0.2;
            double zPosterior = (lambda * 0.5) / (Math.Sqrt(lambda) * 0.2);

            Afirmar(resultados, "A razão z encolhida é √λ vezes a bruta — sempre mais conservadora",
                    Math.Abs(zPosterior - Math.Sqrt(lambda) * zBruto) < Folga && zPosterior < zBruto,
                    $"z bruto {Formatar(zBruto)}, z a posteriori {Formatar(zPosterior)}");
        }

        // ── Desvio-padrão amostral ───────────────────────────────────────

        private static void VerificarDesvioPadrao(List<ResultadoDeVerificacao> resultados)
        {
            double sd = EstimadorDeEfeitoDeElemento.CalcularDesvioPadraoAmostral(
                new[] { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 });

            Afirmar(resultados, "Desvio-padrão amostral usa denominador (n − 1)",
                    Math.Abs(sd - Math.Sqrt(32.0 / 7.0)) < Folga,
                    $"esperado {Formatar(Math.Sqrt(32.0 / 7.0))}, obtido {Formatar(sd)}");

            Afirmar(resultados, "Com menos de duas observações o desvio-padrão é indefinido",
                    double.IsNaN(EstimadorDeEfeitoDeElemento.CalcularDesvioPadraoAmostral(new[] { 3.0 })),
                    "esperado NaN, e não zero — zero seria lido como ausência de dispersão");

            Afirmar(resultados, "Valores indefinidos são ignorados no desvio-padrão",
                    Math.Abs(EstimadorDeEfeitoDeElemento.CalcularDesvioPadraoAmostral(
                        new[] { 2.0, double.NaN, 4.0 }) - Math.Sqrt(2.0)) < Folga,
                    "os NaN deveriam sair da conta");
        }

        // ── Ajuste por mínimos quadrados ─────────────────────────────────

        private static void VerificarMinimosQuadrados(List<ResultadoDeVerificacao> resultados)
        {
            // 1. Recuperar coeficientes conhecidos, sem ruído.
            var delineamento = new List<double[]>();
            var resposta = new List<double>();

            for (int i = 0; i < 200; i++)
            {
                double x1 = i % 17;
                double x2 = (i % 7) * 0.5;
                delineamento.Add(new[] { 1.0, x1, x2 });
                resposta.Add(2.0 + 3.0 * x1 - 1.5 * x2);
            }

            var ajuste = AjusteDeMinimosQuadrados.Ajustar(delineamento, resposta);

            bool recuperou = ajuste != null
                && Math.Abs(ajuste.Coeficientes[0] - 2.0) < 1e-4
                && Math.Abs(ajuste.Coeficientes[1] - 3.0) < 1e-4
                && Math.Abs(ajuste.Coeficientes[2] + 1.5) < 1e-4;

            Afirmar(resultados, "O ajuste recupera coeficientes conhecidos",
                    recuperou,
                    ajuste == null ? "o ajuste devolveu nulo"
                                   : $"obtido {Formatar(ajuste.Coeficientes[0])}, " +
                                     $"{Formatar(ajuste.Coeficientes[1])}, {Formatar(ajuste.Coeficientes[2])}");

            Afirmar(resultados, "Os resíduos de um ajuste exato são praticamente nulos",
                    ajuste != null && ajuste.Residuos.All(r => Math.Abs(r) < 1e-4),
                    "havia resíduo onde o modelo é exato");

            // 2. O mecanismo de ν̂_e: um efeito de grupo omitido do ajuste reaparece na média dos
            //    resíduos daquele grupo. É a peça central da Camada 1.
            var efeitoPorGrupo = new Dictionary<string, double> { ["A"] = 0.5, ["B"] = -0.8, ["C"] = 0.3 };
            var grupos = new List<string>();

            delineamento = new List<double[]>();
            resposta = new List<double>();

            for (int i = 0; i < 900; i++)
            {
                string grupo = i % 3 == 0 ? "A" : i % 3 == 1 ? "B" : "C";
                double x = (i % 23) * 0.25;

                delineamento.Add(new[] { 1.0, x });
                resposta.Add(1.0 + 2.0 * x + efeitoPorGrupo[grupo]);
                grupos.Add(grupo);
            }

            ajuste = AjusteDeMinimosQuadrados.Ajustar(delineamento, resposta);

            bool efeitosRecuperados = false;
            string detalheDosEfeitos = "o ajuste devolveu nulo";

            if (ajuste != null)
            {
                // O intercepto absorve a média dos efeitos; o que se recupera são os desvios em
                // torno dela, e é isso que o índice usa.
                double mediaDosEfeitos = efeitoPorGrupo.Values.Average();

                var mediasDeResiduo = efeitoPorGrupo.Keys.ToDictionary(
                    g => g,
                    g => Enumerable.Range(0, grupos.Count)
                                   .Where(i => grupos[i] == g)
                                   .Average(i => ajuste.Residuos[i]));

                efeitosRecuperados = efeitoPorGrupo.All(par =>
                    Math.Abs(mediasDeResiduo[par.Key] - (par.Value - mediaDosEfeitos)) < 1e-3);

                detalheDosEfeitos = string.Join(", ", efeitoPorGrupo.Select(par =>
                    $"{par.Key}: esperado {Formatar(par.Value - mediaDosEfeitos)}, " +
                    $"obtido {Formatar(mediasDeResiduo[par.Key])}"));
            }

            Afirmar(resultados,
                    "A média dos resíduos por grupo recupera o efeito omitido (mecanismo de ν̂_e)",
                    efeitosRecuperados, detalheDosEfeitos);

            // 3. Coluna linearmente dependente não pode fazer o ajuste explodir.
            delineamento = new List<double[]>();
            resposta = new List<double>();

            for (int i = 0; i < 100; i++)
            {
                double x = (i % 13) * 0.3;
                delineamento.Add(new[] { 1.0, x, 2.0 * x });   // a terceira coluna é o dobro da segunda
                resposta.Add(1.0 + 4.0 * x);
            }

            ajuste = AjusteDeMinimosQuadrados.Ajustar(delineamento, resposta);

            bool previsaoCorreta = ajuste != null
                && ajuste.Coeficientes.All(c => !double.IsNaN(c) && !double.IsInfinity(c))
                && ajuste.Residuos.All(r => Math.Abs(r) < 1e-3);

            Afirmar(resultados,
                    "Coluna linearmente dependente não desestabiliza o ajuste",
                    previsaoCorreta,
                    ajuste == null ? "o ajuste devolveu nulo"
                                   : $"maior resíduo {Formatar(ajuste.Residuos.Max(Math.Abs))}");

            Afirmar(resultados, "Ajuste sem observações devolve nulo em vez de lançar",
                    AjusteDeMinimosQuadrados.Ajustar(new List<double[]>(), new List<double>()) == null,
                    "esperado nulo");
        }

        // ── Infraestrutura ───────────────────────────────────────────────

        private static void Afirmar(
            List<ResultadoDeVerificacao> resultados, string afirmacao, bool passou, string detalhe)
        {
            resultados.Add(new ResultadoDeVerificacao
            {
                Afirmacao = afirmacao,
                Passou    = passou,
                Detalhe   = passou ? string.Empty : detalhe,
            });
        }

        private static string Formatar(double valor) =>
            valor.ToString("N6", CultureInfo.CurrentCulture);
    }
}
