using System;
using System.Collections.Generic;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Processamento
{
    /// <summary>
    /// Detecta eventos de <see cref="Fixacao"/> e <see cref="Saccade"/> a partir de uma sequência
    /// ordenada de <see cref="LinhaDeSessao"/>, agrupando frames consecutivos onde o olhar
    /// permanece espacialmente estável dentro da tolerância definida.
    /// </summary>
    /// <remarks>
    /// Esta classe é stateless: todos os métodos são funcionalmente puros em relação
    /// ao estado da instância e podem ser reutilizados para múltiplas sessões.
    ///
    /// Definição de estabilidade: dois frames consecutivos são considerados estáveis
    /// se a distância euclidiana 3D entre os pontos de incidência for ≤ tolerância
    /// em pelo menos um dos olhos (esquerdo OR direito). Isso tolera ruído em um
    /// dos olhos quando o outro permanece fixo.
    ///
    /// Grupos com duração fora das faixas de fixação (≥ 300 ms) e saccade (30–80 ms)
    /// são descartados silenciosamente. A faixa de saccade segue a Figura 46 do TextoFinal e a
    /// fixação usa o piso da janela focal de 300–500 ms (§5.1), sem teto na detecção.
    ///
    /// Apenas linhas cujo tempo decorrido desde o início da sessão esteja entre
    /// <see cref="ParametrosDeDeteccaoOcular.JanelaTemporalInicioMs"/> (2 min) e
    /// <see cref="ParametrosDeDeteccaoOcular.JanelaTemporalFimMs"/> (4 min) são consideradas.
    /// </remarks>
    public class DetectorDeMovimentoOcular
    {
        /// <summary>
        /// Detecta fixações e saccades na lista de linhas fornecida.
        /// </summary>
        /// <param name="linhasDaSessao">
        /// Linhas brutas da sessão, ordenadas por timestamp crescente.
        /// Linhas com <see cref="LinhaDeSessao.DadosOcularesValidos"/> igual a false
        /// são ignoradas no agrupamento mas não removidas da lista original.
        /// </param>
        /// <returns>Tupla com a lista de fixações e a lista de saccades detectados.</returns>
        /// <remarks>
        /// A assinatura tem UM parâmetro de propósito: é convertida em grupo de métodos para a
        /// <c>Func</c> detectora da <see cref="Dominio.Sessao"/> pelo
        /// <see cref="ConstruturDeSessoes"/>, e um segundo parâmetro — ainda que opcional —
        /// quebraria a conversão.
        /// </remarks>
        public (List<Fixacao> fixacoes, List<Saccade> saccades) Detectar(
            IReadOnlyList<LinhaDeSessao> linhasDaSessao)
        {
            return Detectar(linhasDaSessao, aplicarJanelaTemporal: true);
        }

        /// <summary>
        /// Detecta fixações e saccades ao longo da sessão INTEIRA, sem restringir à janela dos
        /// minutos 2–4.
        /// </summary>
        /// <remarks>
        /// Existe para a TABELA DE AUDITORIA, que precisa mostrar também os eventos descartados
        /// pela janela — sem eles não haveria como distinguir "não houve evento" de "houve evento,
        /// mas fora do intervalo considerado". <b>Nenhum cálculo do índice usa este caminho.</b>
        /// </remarks>
        public (List<Fixacao> fixacoes, List<Saccade> saccades) DetectarNaSessaoInteira(
            IReadOnlyList<LinhaDeSessao> linhasDaSessao)
        {
            return Detectar(linhasDaSessao, aplicarJanelaTemporal: false);
        }

        private (List<Fixacao> fixacoes, List<Saccade> saccades) Detectar(
            IReadOnlyList<LinhaDeSessao> linhasDaSessao,
            bool aplicarJanelaTemporal)
        {
            var linhasValidas = FiltrarLinhasValidas(linhasDaSessao);

            if (linhasValidas.Count < 2)
                return (new List<Fixacao>(), new List<Saccade>());

            var linhasNaJanela = aplicarJanelaTemporal
                ? FiltrarPorJanelaDeSessao(linhasValidas)
                : linhasValidas;

            if (linhasNaJanela.Count < 2)
                return (new List<Fixacao>(), new List<Saccade>());

            // O intervalo típico é medido sobre a janela efetivamente analisada, e serve tanto
            // para decidir o que é lacuna quanto para completar a duração do último intervalo de
            // cada grupo.
            double intervaloTipicoMs = CalcularIntervaloMedianoMs(linhasNaJanela);

            var grupos = AgruparLinhasEstaveisConsecutivas(linhasNaJanela, intervaloTipicoMs);
            return ClassificarGrupos(grupos, intervaloTipicoMs);
        }

        /// <summary>Intervalo mediano entre amostras consecutivas, em ms.</summary>
        private double CalcularIntervaloMedianoMs(List<LinhaDeSessao> linhas)
        {
            var intervalos = new List<double>(Math.Max(0, linhas.Count - 1));

            for (int indice = 1; indice < linhas.Count; indice++)
            {
                double intervalo = (linhas[indice].Timestamp - linhas[indice - 1].Timestamp).TotalMilliseconds;

                if (intervalo > 0)
                    intervalos.Add(intervalo);
            }

            if (intervalos.Count == 0)
                return 0.0;

            intervalos.Sort();
            return CalcularMediana(intervalos);
        }

        /// <summary>
        /// Instante que serve de zero ao relógio da sessão: o timestamp da primeira linha com
        /// dados oculares válidos — a MESMA referência de <see cref="FiltrarPorJanelaDeSessao"/>.
        /// </summary>
        /// <remarks>
        /// Exposto para que a tabela de auditoria exiba o tempo relativo e decida o que ficou
        /// dentro da janela usando o mesmo zero que a detecção usa. Devolve null quando não há
        /// nenhuma linha com dados oculares válidos.
        /// </remarks>
        public DateTime? ObterInicioDaReferenciaTemporal(IReadOnlyList<LinhaDeSessao> linhasDaSessao)
        {
            var linhasValidas = FiltrarLinhasValidas(linhasDaSessao);
            return linhasValidas.Count == 0 ? (DateTime?)null : linhasValidas[0].Timestamp;
        }

        private List<LinhaDeSessao> FiltrarLinhasValidas(IReadOnlyList<LinhaDeSessao> linhas)
        {
            return linhas.Where(linha => linha.DadosOcularesValidos).ToList();
        }

        private List<LinhaDeSessao> FiltrarPorJanelaDeSessao(List<LinhaDeSessao> linhasValidas)
        {
            var inicioSessao = linhasValidas[0].Timestamp;
            return linhasValidas
                .Where(linha =>
                {
                    double decorridoMs = (linha.Timestamp - inicioSessao).TotalMilliseconds;
                    return decorridoMs >= ParametrosDeDeteccaoOcular.JanelaTemporalInicioMs
                        && decorridoMs <= ParametrosDeDeteccaoOcular.JanelaTemporalFimMs;
                })
                .ToList();
        }

        /// <summary>
        /// Agrupa amostras consecutivas que descrevem o mesmo olhar parado.
        /// </summary>
        /// <remarks>
        /// Três condições precisam valer simultaneamente para que uma amostra continue no grupo:
        /// <list type="number">
        /// <item>o passo em relação à amostra anterior é pequeno (velocidade);</item>
        /// <item>o ponto está próximo do centro corrente do grupo (dispersão), o que impede que
        /// uma sucessão de passos pequenos percorra uma distância grande;</item>
        /// <item>não houve lacuna temporal relevante desde a amostra anterior, o que impede que o
        /// grupo atravesse uma piscada — as amostras inválidas foram removidas antes daqui, e sem
        /// esta condição as duas bordas da piscada apareceriam como vizinhas.</item>
        /// </list>
        /// </remarks>
        private List<List<LinhaDeSessao>> AgruparLinhasEstaveisConsecutivas(
            List<LinhaDeSessao> linhasValidas, double intervaloTipicoMs)
        {
            var grupos = new List<List<LinhaDeSessao>>();
            var grupoAtual = new List<LinhaDeSessao> { linhasValidas[0] };

            // Com intervalo típico indefinido (sessão de uma amostra só), nenhuma lacuna quebra
            // o grupo — o critério de posição decide sozinho.
            double lacunaMaximaMs = intervaloTipicoMs > 0
                ? intervaloTipicoMs * ParametrosDeDeteccaoOcular.MultiplicadorDeLacunaQueQuebraFixacao
                : double.MaxValue;

            for (int indice = 1; indice < linhasValidas.Count; indice++)
            {
                var linhaAnterior = linhasValidas[indice - 1];
                var linhaAtual = linhasValidas[indice];

                double lacunaMs = (linhaAtual.Timestamp - linhaAnterior.Timestamp).TotalMilliseconds;

                bool continua = OlharEstaEstavel(linhaAnterior, linhaAtual)
                             && PontoCabeNaDispersaoDoGrupo(grupoAtual, linhaAtual)
                             && lacunaMs <= lacunaMaximaMs;

                if (continua)
                {
                    grupoAtual.Add(linhaAtual);
                }
                else
                {
                    grupos.Add(grupoAtual);
                    grupoAtual = new List<LinhaDeSessao> { linhaAtual };
                }
            }

            grupos.Add(grupoAtual);
            return grupos;
        }

        /// <summary>
        /// Diz se a amostra está perto o bastante do centro corrente do grupo.
        /// </summary>
        /// <remarks>
        /// O centro é a média dos pontos já aceitos, calculada olho a olho e apenas sobre os olhos
        /// com medida válida — pela mesma razão exposta em
        /// <see cref="CalcularPontoMedioValido"/>. Basta um dos olhos caber no raio, como já
        /// acontece no critério de passo.
        ///
        /// O raio é ANGULAR (1° de ângulo visual projetado pela distância observador–alvo,
        /// I-DT na parametrização de Holmqvist et al. citada no §3.1.4), com fallback métrico
        /// quando a posição do observador não está disponível — ver
        /// <see cref="ResolverRaioDeDispersao"/>.
        /// </remarks>
        private bool PontoCabeNaDispersaoDoGrupo(List<LinhaDeSessao> grupoAtual, LinhaDeSessao linhaAtual)
        {
            var centro = CalcularPontoMedioValido(grupoAtual);

            if (double.IsNaN(centro.X))
                return true;   // sem centro estimável, o critério de passo decide sozinho

            var ponto = CalcularPontoMedioValido(new List<LinhaDeSessao> { linhaAtual });

            if (double.IsNaN(ponto.X))
                return true;

            double raio = ResolverRaioDeDispersao(linhaAtual, centro.X, centro.Y, centro.Z);

            return CalcularDistanciaEuclidiana3D(
                centro.X, centro.Y, centro.Z, ponto.X, ponto.Y, ponto.Z) <= raio;
        }

        private bool OlharEstaEstavel(LinhaDeSessao linhaAnterior, LinhaDeSessao linhaAtual)
        {
            // A tolerância de passo é o raio de dispersão dividido pela razão configurada (3):
            // angular quando a distância do alvo é computável, métrica no fallback. Sem isso, o
            // passo métrico fixo quebraria em paredes distantes fixações que o critério angular
            // aceitaria — o portão de passo vetaria antes de a dispersão ser consultada.
            var referencia = CalcularPontoMedioValido(new List<LinhaDeSessao> { linhaAnterior });

            double tolerancia = double.IsNaN(referencia.X)
                ? ParametrosDeDeteccaoOcular.ToleranciaDeEstabilidadeMetros
                : ResolverRaioDeDispersao(linhaAnterior, referencia.X, referencia.Y, referencia.Z)
                  / ParametrosDeDeteccaoOcular.RazaoEntreRaioDeDispersaoEPasso;

            double distanciaEsquerda = CalcularDistanciaEuclidiana3D(
                linhaAnterior.PontoDeIncidenciaEsquerdoX, linhaAnterior.PontoDeIncidenciaEsquerdoY, linhaAnterior.PontoDeIncidenciaEsquerdoZ,
                linhaAtual.PontoDeIncidenciaEsquerdoX, linhaAtual.PontoDeIncidenciaEsquerdoY, linhaAtual.PontoDeIncidenciaEsquerdoZ);

            double distanciaDireita = CalcularDistanciaEuclidiana3D(
                linhaAnterior.PontoDeIncidenciaDireitoX, linhaAnterior.PontoDeIncidenciaDireitoY, linhaAnterior.PontoDeIncidenciaDireitoZ,
                linhaAtual.PontoDeIncidenciaDireitoX, linhaAtual.PontoDeIncidenciaDireitoY, linhaAtual.PontoDeIncidenciaDireitoZ);

            return distanciaEsquerda <= tolerancia
                || distanciaDireita <= tolerancia;
        }

        /// <summary>
        /// Raio de dispersão aplicável à amostra: 1° de ângulo visual projetado pela distância
        /// entre o observador (<c>playerPosition</c>) e o ponto de referência do alvo.
        /// </summary>
        /// <remarks>
        /// Fallback para <see cref="ParametrosDeDeteccaoOcular.RaioMaximoDeDispersaoMetros"/>
        /// quando a posição do observador é NaN ou (0,0,0) — a mesma convenção "origem do mundo
        /// = sem rastreamento" usada para os pontos de incidência. Vale para capturas antigas sem
        /// as colunas de playerPosition e para as amostras sintéticas da verificação, que assim
        /// continuam exercitando os limiares métricos históricos.
        /// </remarks>
        private double ResolverRaioDeDispersao(
            LinhaDeSessao linha, double alvoX, double alvoY, double alvoZ)
        {
            double px = linha.PosicaoJogadorX;
            double py = linha.PosicaoJogadorY;
            double pz = linha.PosicaoJogadorZ;

            bool posicaoIndisponivel =
                double.IsNaN(px) || double.IsNaN(py) || double.IsNaN(pz)
                || (px == 0.0 && py == 0.0 && pz == 0.0);

            if (posicaoIndisponivel)
                return ParametrosDeDeteccaoOcular.RaioMaximoDeDispersaoMetros;

            double distancia = CalcularDistanciaEuclidiana3D(px, py, pz, alvoX, alvoY, alvoZ);

            if (double.IsNaN(distancia) || distancia <= 0.0)
                return ParametrosDeDeteccaoOcular.RaioMaximoDeDispersaoMetros;

            return distancia * ParametrosDeDeteccaoOcular.RaioPorMetroDeDistancia;
        }

        private double CalcularDistanciaEuclidiana3D(
            double x1, double y1, double z1,
            double x2, double y2, double z2)
        {
            double deltaX = x2 - x1;
            double deltaY = y2 - y1;
            double deltaZ = z2 - z1;
            return Math.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);
        }

        private (List<Fixacao>, List<Saccade>) ClassificarGrupos(
            List<List<LinhaDeSessao>> grupos, double intervaloTipicoDaSessaoMs)
        {
            var fixacoes = new List<Fixacao>();
            var saccades = new List<Saccade>();

            foreach (var grupo in grupos)
            {
                double duracaoMs = CalcularDuracaoEmMs(grupo, intervaloTipicoDaSessaoMs);

                if (EhFixacao(duracaoMs))
                    fixacoes.Add(ConstruirFixacao(grupo, duracaoMs));
                else if (EhSaccade(duracaoMs))
                    saccades.Add(ConstruirSaccade(grupo, duracaoMs));
            }

            return (fixacoes, saccades);
        }

        /// <summary>
        /// Duração do evento em milissegundos, cobrindo também o intervalo da última amostra.
        /// </summary>
        /// <remarks>
        /// <b>A diferença entre a primeira e a última amostra mede um intervalo a menos do que o
        /// grupo dura.</b> N amostras cobrem N intervalos de amostragem, e essa subtração conta
        /// N−1: toda fixação saía sistematicamente um intervalo mais curta, e um grupo de uma
        /// única amostra saía com duração zero, como se não tivesse existido.
        ///
        /// O viés não era inofensivo, porque a classificação é por faixa de duração. Com piso de
        /// fixação em 300 ms e amostragem de ~65 ms, um grupo de cinco amostras — 325 ms reais —
        /// era medido em 260 ms e descartado: não chegava a fixação e estava acima do teto dos
        /// trechos curtos, de modo que sumia sem deixar rastro.
        ///
        /// O intervalo que falta é estimado pela MEDIANA dos intervalos do próprio grupo, e não
        /// por uma constante: a taxa de amostragem difere entre arquivos e oscila dentro de uma
        /// mesma sessão. Com uma amostra só não há intervalo a estimar, e recorre-se à mediana da
        /// sessão inteira, recebida por parâmetro.
        /// </remarks>
        private double CalcularDuracaoEmMs(List<LinhaDeSessao> grupo, double intervaloTipicoDaSessaoMs)
        {
            double extensao = (grupo.Last().Timestamp - grupo.First().Timestamp).TotalMilliseconds;

            if (grupo.Count < 2)
                return intervaloTipicoDaSessaoMs;

            var intervalos = new List<double>(grupo.Count - 1);

            for (int indice = 1; indice < grupo.Count; indice++)
            {
                double intervalo = (grupo[indice].Timestamp - grupo[indice - 1].Timestamp).TotalMilliseconds;

                if (intervalo > 0)
                    intervalos.Add(intervalo);
            }

            if (intervalos.Count == 0)
                return extensao + intervaloTipicoDaSessaoMs;

            intervalos.Sort();

            return extensao + CalcularMediana(intervalos);
        }

        private bool EhFixacao(double duracaoMs)
        {
            return duracaoMs >= ParametrosDeDeteccaoOcular.DuracaoMinimaDeFixacaoMs;
        }

        private bool EhSaccade(double duracaoMs)
        {
            return duracaoMs >= ParametrosDeDeteccaoOcular.DuracaoMinimaDeSaccadeMs
                && duracaoMs <= ParametrosDeDeteccaoOcular.DuracaoMaximaDeSaccadeMs;
        }

        private Fixacao ConstruirFixacao(List<LinhaDeSessao> grupo, double duracaoMs)
        {
            var dadosBase = ConstruirDadosBaseDoEvento(grupo, duracaoMs);
            var tendencia = DeterminarTendenciaPupilar(grupo);

            return new Fixacao
            {
                TimestampInicio              = dadosBase.timestampInicio,
                TimestampFim                 = dadosBase.timestampFim,
                DuracaoEmMilissegundos       = duracaoMs,
                PontoMedioDeIncidenciaX      = dadosBase.pontoMedioX,
                PontoMedioDeIncidenciaY      = dadosBase.pontoMedioY,
                PontoMedioDeIncidenciaZ      = dadosBase.pontoMedioZ,
                NomesDeObjetos               = dadosBase.nomesDeObjetos,
                IndicesLinhasOriginais       = dadosBase.indices,
                DiametroMedianoPupilaEsquerda  = dadosBase.diametroMedianoEsquerdo,
                DiametroMedianoPupilaDireita   = dadosBase.diametroMedianoDireito,
                TendenciaPupilar             = tendencia
            };
        }

        private Saccade ConstruirSaccade(List<LinhaDeSessao> grupo, double duracaoMs)
        {
            var dadosBase = ConstruirDadosBaseDoEvento(grupo, duracaoMs);

            return new Saccade
            {
                TimestampInicio              = dadosBase.timestampInicio,
                TimestampFim                 = dadosBase.timestampFim,
                DuracaoEmMilissegundos       = duracaoMs,
                PontoMedioDeIncidenciaX      = dadosBase.pontoMedioX,
                PontoMedioDeIncidenciaY      = dadosBase.pontoMedioY,
                PontoMedioDeIncidenciaZ      = dadosBase.pontoMedioZ,
                NomesDeObjetos               = dadosBase.nomesDeObjetos,
                IndicesLinhasOriginais       = dadosBase.indices,
                DiametroMedianoPupilaEsquerda  = dadosBase.diametroMedianoEsquerdo,
                DiametroMedianoPupilaDireita   = dadosBase.diametroMedianoDireito
            };
        }

        private (DateTime timestampInicio, DateTime timestampFim,
                 double pontoMedioX, double pontoMedioY, double pontoMedioZ,
                 HashSet<string> nomesDeObjetos, List<int> indices,
                 double diametroMedianoEsquerdo, double diametroMedianoDireito)
            ConstruirDadosBaseDoEvento(List<LinhaDeSessao> grupo, double duracaoMs)
        {
            return (
                timestampInicio:       grupo.First().Timestamp,
                timestampFim:          grupo.Last().Timestamp,
                pontoMedioX:           CalcularPontoMedioX(grupo),
                pontoMedioY:           CalcularPontoMedioY(grupo),
                pontoMedioZ:           CalcularPontoMedioZ(grupo),
                nomesDeObjetos:        ExtrairNomesDeObjetos(grupo),
                indices:               ExtrairIndices(grupo),
                diametroMedianoEsquerdo: CalcularDiametroMedianoPupila(grupo, linha => linha.DiametroPupilaEsquerda),
                diametroMedianoDireito:  CalcularDiametroMedianoPupila(grupo, linha => linha.DiametroPupilaDireita)
            );
        }

        private double CalcularPontoMedioX(List<LinhaDeSessao> grupo) => CalcularPontoMedioValido(grupo).X;

        private double CalcularPontoMedioY(List<LinhaDeSessao> grupo) => CalcularPontoMedioValido(grupo).Y;

        private double CalcularPontoMedioZ(List<LinhaDeSessao> grupo) => CalcularPontoMedioValido(grupo).Z;

        /// <summary>
        /// Ponto de incidência representativo do grupo, pela MEDIANA das amostras com medida
        /// válida. Devolve NaN em todas as coordenadas quando nenhuma amostra é aproveitável.
        /// </summary>
        /// <remarks>
        /// <b>Um olho sem rastreamento reporta a origem do mundo, não ausência.</b> Uma amostra é
        /// aceita quando PELO MENOS UM dos olhos tem ponto não nulo — mas a versão anterior
        /// promediava sempre os dois. Com o olho esquerdo perdido em (0,0,0) e o direito a três
        /// metros, a média resultava a um metro e meio, deslocada na direção da origem.
        ///
        /// Isso não era um detalhe estatístico: esta coordenada é exatamente onde a esfera do
        /// hitpoint é inserida no modelo Revit. Um ponto contaminado põe a esfera sobre a
        /// geometria errada, e o Revit não tem como acusar nada.
        ///
        /// A regra passou a ser a mesma já aplicada ao diâmetro pupilar: os dois olhos quando os
        /// dois valem, o olho válido quando só um vale, a amostra fora quando nenhum vale. E a
        /// mediana em lugar da média, pelo mesmo motivo — uma amostra espúria na transição de uma
        /// piscada desloca a média e não desloca a mediana.
        /// </remarks>
        private (double X, double Y, double Z) CalcularPontoMedioValido(List<LinhaDeSessao> grupo)
        {
            var xs = new List<double>(grupo.Count);
            var ys = new List<double>(grupo.Count);
            var zs = new List<double>(grupo.Count);

            foreach (var linha in grupo)
            {
                bool esquerdoValido = !PontoEhNulo(
                    linha.PontoDeIncidenciaEsquerdoX, linha.PontoDeIncidenciaEsquerdoY, linha.PontoDeIncidenciaEsquerdoZ);

                bool direitoValido = !PontoEhNulo(
                    linha.PontoDeIncidenciaDireitoX, linha.PontoDeIncidenciaDireitoY, linha.PontoDeIncidenciaDireitoZ);

                if (esquerdoValido && direitoValido)
                {
                    xs.Add((linha.PontoDeIncidenciaEsquerdoX + linha.PontoDeIncidenciaDireitoX) / 2.0);
                    ys.Add((linha.PontoDeIncidenciaEsquerdoY + linha.PontoDeIncidenciaDireitoY) / 2.0);
                    zs.Add((linha.PontoDeIncidenciaEsquerdoZ + linha.PontoDeIncidenciaDireitoZ) / 2.0);
                }
                else if (esquerdoValido)
                {
                    xs.Add(linha.PontoDeIncidenciaEsquerdoX);
                    ys.Add(linha.PontoDeIncidenciaEsquerdoY);
                    zs.Add(linha.PontoDeIncidenciaEsquerdoZ);
                }
                else if (direitoValido)
                {
                    xs.Add(linha.PontoDeIncidenciaDireitoX);
                    ys.Add(linha.PontoDeIncidenciaDireitoY);
                    zs.Add(linha.PontoDeIncidenciaDireitoZ);
                }
            }

            if (xs.Count == 0)
                return (double.NaN, double.NaN, double.NaN);

            xs.Sort(); ys.Sort(); zs.Sort();

            return (CalcularMediana(xs), CalcularMediana(ys), CalcularMediana(zs));
        }

        /// <summary>
        /// Diz se um ponto de incidência é a origem exata, que o rastreador escreve quando perde
        /// o olho — e que é indistinguível, no arquivo, de uma medida legítima na origem.
        /// </summary>
        private static bool PontoEhNulo(double x, double y, double z)
        {
            return x == 0.0 && y == 0.0 && z == 0.0;
        }

        private HashSet<string> ExtrairNomesDeObjetos(List<LinhaDeSessao> grupo)
        {
            return new HashSet<string>(
                grupo.Select(linha => linha.NomeDoObjeto).Where(nome => !string.IsNullOrWhiteSpace(nome)));
        }

        private List<int> ExtrairIndices(List<LinhaDeSessao> grupo)
        {
            return grupo.Select(linha => linha.IndiceLinhaOriginal).ToList();
        }

        /// <summary>
        /// Resume o diâmetro pupilar de um evento pela MEDIANA das amostras válidas do intervalo.
        /// </summary>
        /// <remarks>
        /// A mediana, e não a média, é o que o Quadro 36 do TextoFinal especifica para a variável
        /// resposta: "a mediana de D no intervalo t (fixando o elemento e) menos a baseline do
        /// participante". A escolha tem efeito: dentro de uma fixação de 300–500 ms o rastreador
        /// produz amostras espúrias em transições de piscada, e a mediana as descarta, enquanto a
        /// média as incorpora.
        ///
        /// É também a mesma estatística já usada na linha de base
        /// (<see cref="ConstruturDeSessoes"/>), o que mantém as duas pontas do ΔD coerentes: mediana
        /// da fixação menos mediana do repouso.
        /// </remarks>
        // As propriedades destinatárias chamavam-se DiametroMedioPupila* — nome herdado de
        // quando o resumo era a média — e foram renomeadas para DiametroMedianoPupila* em
        // 31/08/2026, com autorização do autor, para que o nome não induza a erro de leitura.
        private double CalcularDiametroMedianoPupila(List<LinhaDeSessao> grupo, Func<LinhaDeSessao, double> seletorDeDiametro)
        {
            var valoresValidos = grupo
                .Select(seletorDeDiametro)
                .Where(diametro => diametro > 0)
                .OrderBy(diametro => diametro)
                .ToList();

            if (!valoresValidos.Any())
                return double.NaN;

            return CalcularMediana(valoresValidos);
        }

        /// <summary>Mediana de uma lista JÁ ORDENADA; média dos dois centrais quando a contagem é par.</summary>
        private static double CalcularMediana(List<double> valoresOrdenados)
        {
            int meio = valoresOrdenados.Count / 2;

            return valoresOrdenados.Count % 2 == 1
                ? valoresOrdenados[meio]
                : (valoresOrdenados[meio - 1] + valoresOrdenados[meio]) / 2.0;
        }

        private TendenciaPupilar DeterminarTendenciaPupilar(List<LinhaDeSessao> grupo)
        {
            int numeroDeBordas = Math.Min(
                ParametrosDeDeteccaoOcular.NumeroDeBordasParaTendenciaPupilar,
                grupo.Count / 2);

            if (numeroDeBordas < 1)
                return TendenciaPupilar.Estavel;

            var linhasDoInicio = ObterLinhasComPupilaValida(grupo.Take(numeroDeBordas));
            var linhasDoFim = ObterLinhasComPupilaValida(grupo.TakeLast(numeroDeBordas));

            if (!linhasDoInicio.Any() || !linhasDoFim.Any())
                return TendenciaPupilar.Estavel;

            double pupilaNaInicio = linhasDoInicio.Average(linha => linha.DiametroPupilaMedio);
            double pupilaNaFim = linhasDoFim.Average(linha => linha.DiametroPupilaMedio);
            double variacao = pupilaNaFim - pupilaNaInicio;

            // Apenas a direção da variação é classificada aqui; a leitura fisiológica
            // (constrição = restaurador) está documentada em TendenciaPupilar.
            if (variacao > LimiaresDePotencialRestaurador.LimiarDeDilatacaoPupilarMm)
                return TendenciaPupilar.Dilatada;

            if (variacao < -LimiaresDePotencialRestaurador.LimiarDeDilatacaoPupilarMm)
                return TendenciaPupilar.Contraida;

            return TendenciaPupilar.Estavel;
        }

        private IEnumerable<LinhaDeSessao> ObterLinhasComPupilaValida(IEnumerable<LinhaDeSessao> linhas)
        {
            return linhas.Where(linha => !double.IsNaN(linha.DiametroPupilaMedio));
        }
    }
}
