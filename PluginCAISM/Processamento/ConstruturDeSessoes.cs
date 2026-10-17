using System;
using System.Collections.Generic;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;
using PluginCAISM.Parsing;

namespace PluginCAISM.Processamento
{
    /// <summary>
    /// Constrói o <see cref="ConjuntoDeSessoes"/> a partir dos dados brutos produzidos
    /// pelos três parsers (Input 01, 02 e 03).
    /// </summary>
    /// <remarks>
    /// Responsabilidades desta classe:
    /// 1. Agrupar as <see cref="LinhaDeSessao"/> por chave de sessão
    ///    (IdentificacaoManual + NomeDaSessao + CenarioSelecionado + AssentoSelecionado).
    /// 2. Enriquecer cada linha com os dados de sensores pelo timestamp (merge com Input 03).
    /// 3. Associar o DeltaCortisol pela identificação do participante — a chave do arquivo
    ///    de cortisol —, com retaguarda pelo NomeDaSessao (join com Input 02).
    /// 4. Construir os objetos <see cref="Sessao"/> com a função detectora injetada.
    /// 5. Calcular a <see cref="Sessao.BaselinePupilar"/> de cada sessão a partir da janela
    ///    pré-estímulo 00:00–01:00 das suas próprias amostras.
    /// </remarks>
    public class ConstruturDeSessoes
    {
        private readonly DetectorDeMovimentoOcular _detector;

        /// <summary>
        /// Inicializa o construtor com o detector de eventos oculares a ser injetado nas sessões.
        /// </summary>
        /// <param name="detector">Instância do detector de movimento ocular.</param>
        public ConstruturDeSessoes(DetectorDeMovimentoOcular detector)
        {
            _detector = detector;
        }

        /// <summary>
        /// Constrói o conjunto de sessões a partir das linhas brutas e dos dados complementares.
        /// </summary>
        /// <param name="linhasBrutas">
        /// Linhas brutas do Input 01, ordenadas por timestamp crescente,
        /// produzidas pelo <see cref="AnalisadorCsvSessao"/>.
        /// </param>
        /// <param name="deltasDeCortisol">
        /// Dicionário de delta de cortisol por ID_PARTICIPANTE, produzido pelo <see cref="AnalisadorExcelCortisol"/>.
        /// Pode ser null ou vazio se o Input 02 não estiver carregado.
        /// </param>
        /// <param name="indiceDeSensores">
        /// Índice de dados de sensores por timestamp, produzido pelo <see cref="AnalisadorDeSensores"/>,
        /// que lê tanto o .csv nativo do módulo de monitoramento quanto a planilha .xlsx.
        /// Pode ser null ou vazio se o Input 03 não estiver carregado.
        /// </param>
        /// <returns>Conjunto de sessões com delta de cortisol e baseline pupilar atribuídos.</returns>
        public ConjuntoDeSessoes Construir(
            List<LinhaDeSessao> linhasBrutas,
            Dictionary<string, double> deltasDeCortisol,
            Dictionary<DateTime, Dictionary<string, string>> indiceDeSensores)
        {
            EnriquecerLinhasComDadosDeSensores(linhasBrutas, indiceDeSensores);

            var gruposDeSessao = AgruparPorChaveDeSessao(linhasBrutas);
            var sessoes = ConstruirSessoesDosGrupos(gruposDeSessao, deltasDeCortisol);

            return new ConjuntoDeSessoes(sessoes);
        }

        /// <summary>
        /// Associa a cada amostra ocular a leitura ambiental do SEGUNDO em que ela ocorreu.
        /// </summary>
        /// <remarks>
        /// <b>A junção não pode ser por igualdade de instante.</b> As duas grades têm
        /// granularidades incompatíveis: o rastreador grava a cada ~65 ms, com milissegundos, e o
        /// sensor grava em segundos cheios. Exigir igualdade ao milissegundo casava 1 amostra em
        /// 53.071 — praticamente nada, e por acaso.
        ///
        /// A chave é o instante truncado ao segundo, dos dois lados. Uma leitura ambiental descreve
        /// o segundo em que foi tomada, e é a esse segundo que a amostra ocular se associa.
        ///
        /// A <see cref="ParametrosDeAssociacaoDeSensor.ToleranciaEmSegundos"/> admite aceitar a
        /// leitura mais próxima dentro de uma janela, para o caso de o sensor não gravar em todos
        /// os segundos. Com tolerância zero, só vale o mesmo segundo exato.
        /// </remarks>
        private void EnriquecerLinhasComDadosDeSensores(
            List<LinhaDeSessao> linhasBrutas,
            Dictionary<DateTime, Dictionary<string, string>> indiceDeSensores)
        {
            if (indiceDeSensores == null || indiceDeSensores.Count == 0)
                return;

            // Índice por segundo: várias leituras podem cair no mesmo segundo; prevalece a primeira.
            var porSegundo = new Dictionary<DateTime, KeyValuePair<DateTime, Dictionary<string, string>>>();

            foreach (var leitura in indiceDeSensores)
            {
                var segundo = TruncarAoSegundo(leitura.Key);

                if (!porSegundo.ContainsKey(segundo))
                    porSegundo[segundo] = leitura;
            }

            var segundosOrdenados = porSegundo.Keys.ToArray();
            Array.Sort(segundosOrdenados);

            int tolerancia = ParametrosDeAssociacaoDeSensor.ToleranciaEmSegundos;

            foreach (var linha in linhasBrutas)
            {
                var segundoDaAmostra = TruncarAoSegundo(linha.Timestamp);

                if (!TentarLocalizarLeitura(porSegundo, segundosOrdenados, segundoDaAmostra,
                                            tolerancia, out var leitura))
                {
                    continue;
                }

                linha.CamposSensor = leitura.Value;
                linha.InstanteDaLeituraDeSensor = leitura.Key;
            }
        }

        /// <summary>Descarta a fração de segundo, preservando data e hora.</summary>
        private static DateTime TruncarAoSegundo(DateTime instante)
        {
            return new DateTime(instante.Year, instante.Month, instante.Day,
                                instante.Hour, instante.Minute, instante.Second);
        }

        /// <summary>
        /// Procura a leitura do segundo informado; não havendo, a mais próxima dentro da tolerância.
        /// </summary>
        private static bool TentarLocalizarLeitura(
            Dictionary<DateTime, KeyValuePair<DateTime, Dictionary<string, string>>> porSegundo,
            DateTime[] segundosOrdenados,
            DateTime segundoDaAmostra,
            int toleranciaEmSegundos,
            out KeyValuePair<DateTime, Dictionary<string, string>> leitura)
        {
            if (porSegundo.TryGetValue(segundoDaAmostra, out leitura))
                return true;

            if (toleranciaEmSegundos <= 0)
                return false;

            // Sem leitura no segundo exato, avalia os vizinhos imediatos — o anterior e o
            // seguinte — e fica com o mais próximo que ainda caiba na tolerância.
            int posicao = Array.BinarySearch(segundosOrdenados, segundoDaAmostra);

            if (posicao >= 0)
                return porSegundo.TryGetValue(segundosOrdenados[posicao], out leitura);

            posicao = ~posicao;

            var candidatos = new List<DateTime>(2);
            if (posicao - 1 >= 0) candidatos.Add(segundosOrdenados[posicao - 1]);
            if (posicao < segundosOrdenados.Length) candidatos.Add(segundosOrdenados[posicao]);

            DateTime? melhor = null;
            double menorDistancia = double.MaxValue;

            foreach (var candidato in candidatos)
            {
                double distancia = Math.Abs((candidato - segundoDaAmostra).TotalSeconds);

                if (distancia <= toleranciaEmSegundos && distancia < menorDistancia)
                {
                    menorDistancia = distancia;
                    melhor = candidato;
                }
            }

            if (melhor.HasValue)
                return porSegundo.TryGetValue(melhor.Value, out leitura);

            leitura = default;
            return false;
        }

        private Dictionary<ChaveDeSessao, List<LinhaDeSessao>> AgruparPorChaveDeSessao(
            List<LinhaDeSessao> linhasBrutas)
        {
            var grupos = new Dictionary<ChaveDeSessao, List<LinhaDeSessao>>();

            foreach (var linha in linhasBrutas)
            {
                var chave = new ChaveDeSessao(
                    linha.IdentificacaoManual,
                    linha.NomeDaSessao,
                    linha.CenarioSelecionado,
                    linha.AssentoSelecionado);

                if (!grupos.TryGetValue(chave, out var lista))
                {
                    lista = new List<LinhaDeSessao>();
                    grupos[chave] = lista;
                }

                lista.Add(linha);
            }

            return grupos;
        }

        private List<Sessao> ConstruirSessoesDosGrupos(
            Dictionary<ChaveDeSessao, List<LinhaDeSessao>> gruposDeSessao,
            Dictionary<string, double> deltasDeCortisol)
        {
            var sessoes = new List<Sessao>();

            foreach (var (chave, linhas) in gruposDeSessao)
            {
                var sessao = ConstruirSessao(chave, linhas, deltasDeCortisol);
                sessoes.Add(sessao);
            }

            return sessoes;
        }

        private Sessao ConstruirSessao(
            ChaveDeSessao chave,
            List<LinhaDeSessao> linhas,
            Dictionary<string, double> deltasDeCortisol)
        {
            var sessao = new Sessao(
                identificacaoManual: chave.IdentificacaoManual,
                nomeDaSessao: chave.NomeDaSessao,
                cenarioSelecionado: chave.CenarioSelecionado,
                assentoSelecionado: chave.AssentoSelecionado,
                dadosBrutos: linhas,
                funcaoDetectora: _detector.Detectar);

            sessao.DeltaCortisol = ObterDeltaDeCortisol(
                chave.IdentificacaoManual, chave.NomeDaSessao, deltasDeCortisol);
            sessao.BaselinePupilar = CalcularBaselinePupilar(linhas);
            return sessao;
        }

        /// <summary>
        /// Calcula a linha de base pupilar de uma sessão: mediana do
        /// <see cref="LinhaDeSessao.DiametroPupilaMedio"/> das amostras válidas da janela
        /// pré-estímulo 00:00–01:00, contada a partir do timestamp da primeira amostra.
        /// </summary>
        /// <param name="linhas">
        /// Linhas da sessão, ordenadas por timestamp crescente. As amostras do primeiro minuto
        /// permanecem disponíveis aqui mesmo que a detecção de eventos use apenas os minutos 2–4.
        /// </param>
        /// <returns>
        /// A mediana em milímetros, ou double.NaN se não houver amostra pupilar válida na janela.
        /// </returns>
        private double CalcularBaselinePupilar(List<LinhaDeSessao> linhas)
        {
            if (linhas.Count == 0)
                return double.NaN;

            // MESMO zero que a detecção de eventos usa: a primeira linha com dados oculares
            // válidos, e não a primeira linha da sessão. As duas janelas — a linha de base
            // (00:00–01:00) e a de detecção (02:00–04:00) — descrevem uma linha do tempo contínua
            // e precisam partir do mesmo instante. Quando a sessão começa com alguns segundos de
            // linhas inválidas, o que é comum enquanto a pessoa acomoda o dispositivo, os dois
            // relógios ficavam defasados por esses segundos e ninguém percebia: os dois números
            // continuavam parecendo certos.
            var inicioSessao = _detector.ObterInicioDaReferenciaTemporal(linhas) ?? linhas[0].Timestamp;

            var diametrosNaJanela = linhas
                .Where(linha =>
                {
                    double decorridoMs = (linha.Timestamp - inicioSessao).TotalMilliseconds;
                    return decorridoMs >= LimiaresDePotencialRestaurador.JanelaDeBaselinePupilarInicioMs
                        && decorridoMs <= LimiaresDePotencialRestaurador.JanelaDeBaselinePupilarFimMs;
                })
                .Select(linha => linha.DiametroPupilaMedio)
                .Where(diametro => !double.IsNaN(diametro))
                .OrderBy(diametro => diametro)
                .ToList();

            if (diametrosNaJanela.Count == 0)
                return double.NaN;

            return CalcularMedianaDeListaOrdenada(diametrosNaJanela);
        }

        /// <summary>
        /// Retorna a mediana de uma lista já ordenada de forma crescente.
        /// Para um número par de elementos, retorna a média dos dois centrais.
        /// </summary>
        private static double CalcularMedianaDeListaOrdenada(List<double> valoresOrdenados)
        {
            int meio = valoresOrdenados.Count / 2;

            if (valoresOrdenados.Count % 2 == 1)
                return valoresOrdenados[meio];

            return (valoresOrdenados[meio - 1] + valoresOrdenados[meio]) / 2.0;
        }

        /// <summary>
        /// Localiza o delta de cortisol da sessão: primeiro pela identificação do participante,
        /// depois pelo nome da sessão.
        /// </summary>
        /// <remarks>
        /// <b>A chave primária é o PARTICIPANTE, não a sessão.</b> O Requisito 01 (Quadro 40 do
        /// TextoFinal) define o delta de cortisol como "a coleta basal e a final de cada
        /// participante", e é assim que o <see cref="Parsing.AnalisadorExcelCortisol"/> indexa o
        /// arquivo: por ID_PARTICIPANTE. A versão anterior buscava apenas por
        /// <c>NomeDaSessao</c> — que nos dados reais carrega o cenário e o período
        /// ("Cenário_01_Dia"), nunca um ID —, de modo que <see cref="Sessao.DeltaCortisol"/> saía
        /// NaN em todas as sessões, silenciosamente.
        ///
        /// A busca por nome de sessão foi mantida como retaguarda para um arquivo de cortisol que
        /// venha indexado por código de sessão, formato que a documentação antiga previa.
        /// </remarks>
        private double ObterDeltaDeCortisol(
            string identificacaoManual,
            string nomeDaSessao,
            Dictionary<string, double> deltasDeCortisol)
        {
            if (deltasDeCortisol == null)
                return double.NaN;

            if (!string.IsNullOrWhiteSpace(identificacaoManual)
                && deltasDeCortisol.TryGetValue(identificacaoManual.Trim(), out double deltaDoParticipante))
            {
                return deltaDoParticipante;
            }

            if (!string.IsNullOrWhiteSpace(nomeDaSessao)
                && deltasDeCortisol.TryGetValue(nomeDaSessao, out double deltaDaSessao))
            {
                return deltaDaSessao;
            }

            return double.NaN;
        }

        private readonly struct ChaveDeSessao : IEquatable<ChaveDeSessao>
        {
            public string IdentificacaoManual { get; }
            public string NomeDaSessao { get; }
            public string CenarioSelecionado { get; }
            public string AssentoSelecionado { get; }

            public ChaveDeSessao(string identificacaoManual, string nomeDaSessao, string cenarioSelecionado, string assentoSelecionado)
            {
                IdentificacaoManual = identificacaoManual;
                NomeDaSessao = nomeDaSessao;
                CenarioSelecionado = cenarioSelecionado;
                AssentoSelecionado = assentoSelecionado;
            }

            public bool Equals(ChaveDeSessao outro)
            {
                return IdentificacaoManual == outro.IdentificacaoManual
                    && NomeDaSessao == outro.NomeDaSessao
                    && CenarioSelecionado == outro.CenarioSelecionado
                    && AssentoSelecionado == outro.AssentoSelecionado;
            }

            public override bool Equals(object obj) => obj is ChaveDeSessao outro && Equals(outro);

            public override int GetHashCode()
            {
                return HashCode.Combine(IdentificacaoManual, NomeDaSessao, CenarioSelecionado, AssentoSelecionado);
            }
        }
    }
}
