using System.Collections.Generic;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Interface.Modelos
{
    /// <summary>
    /// Modelo de apresentação para uma linha da tabela-síntese de formulários na aba de
    /// contextualização (§5.5.1).
    /// </summary>
    /// <remarks>
    /// Achata <see cref="RespostasDeFormularioPorParticipante"/> em pares questão/resposta
    /// adequados ao databinding do DataGrid WPF, no mesmo espírito de
    /// <see cref="LinhaDeEventoParaTabela"/> — que continua servindo exclusivamente à tabela de
    /// eventos da aba de operação.
    ///
    /// Cada instância representa UMA linha da tabela: um item do formulário selecionado.
    /// </remarks>
    public class LinhaDeFormularioParaTabela
    {
        /// <summary>Nome do item: questão da planilha ou escore derivado.</summary>
        public string Item { get; }

        /// <summary>Valor correspondente, já formatado para exibição. "—" quando ausente.</summary>
        public string Valor { get; }

        /// <summary>
        /// Segundo valor da linha, usado apenas no formulário de Sick Building Syndrome, onde cada
        /// linha pareia a frequência do sintoma (Q37) com a atribuição ao trabalho (Q38).
        /// Vazio nos demais formulários, em que a terceira coluna fica oculta.
        /// </summary>
        public string ValorComplementar { get; private set; } = string.Empty;

        /// <summary>
        /// Natureza da linha: "Resposta" para respostas brutas da planilha e "Escore" para
        /// valores derivados pelo <see cref="Processamento.CalculadorDeEscoresDeFormulario"/>.
        /// </summary>
        public string Natureza { get; }

        /// <summary>
        /// Grupo temático em que a linha é exibida, conforme
        /// <see cref="Configuracao.GruposDeQuestoes"/>.
        /// </summary>
        /// <remarks>
        /// Vem do de-para em <see cref="Configuracao.DeParaDeQuestoes"/> para as respostas de
        /// planilha, e é fixado na construção para as linhas derivadas (escores, cortisol, HMD).
        /// É o que permite ao <see cref="GrupoDeFormularioParaTela"/> montar os blocos recolhíveis
        /// sem reinterpretar os enunciados.
        /// </remarks>
        public string Grupo { get; }

        /// <summary>
        /// Enunciado completo da questão, preservado para a dica de contexto.
        /// </summary>
        /// <remarks>
        /// O rótulo exibido é curto de propósito, mas a redação original faz diferença na hora de
        /// interpretar uma resposta ambígua — "Atividade física" esconde que a pergunta pedia a
        /// quantidade de dias por semana no campo "Outro". Guardar o enunciado permite mostrá-lo
        /// como tooltip, sem devolvê-lo à tela.
        /// Vazio quando o rótulo JÁ é o texto de origem, para não repetir a mesma frase na dica.
        /// </remarks>
        public string EnunciadoOriginal { get; }

        private LinhaDeFormularioParaTabela(
            string item,
            string valor,
            string natureza,
            string grupo = Configuracao.GruposDeQuestoes.Outros,
            string enunciadoOriginal = null)
        {
            Item = item;
            Valor = string.IsNullOrWhiteSpace(valor) ? "—" : valor;
            Natureza = natureza;
            Grupo = grupo;
            EnunciadoOriginal = string.Equals(enunciadoOriginal?.Trim(), item)
                ? string.Empty
                : enunciadoOriginal ?? string.Empty;
        }

        /// <summary>
        /// Devolve uma cópia desta linha atribuída a outro grupo.
        /// </summary>
        /// <remarks>
        /// Necessário porque o grupo dos itens do IEEE só é conhecido DEPOIS da leitura da
        /// planilha: quem monta as linhas percorre as colunas sem saber o enunciado de cada uma, e
        /// o domínio é decidido a partir dele.
        /// </remarks>
        internal LinhaDeFormularioParaTabela ComGrupo(string grupo)
        {
            return new LinhaDeFormularioParaTabela(Item, Valor, Natureza, grupo, EnunciadoOriginal)
            {
                ValorComplementar = ValorComplementar,
            };
        }

        /// <summary>
        /// Constrói as linhas da tabela-síntese para um participante e um formulário.
        /// </summary>
        /// <param name="respostas">Respostas e escores do participante selecionado.</param>
        /// <param name="formulario">Formulário selecionado no segundo seletor.</param>
        /// <returns>Linhas prontas para exibição; lista vazia se não houver dados.</returns>
        /// <param name="registroDeCortisol">
        /// Coletas de cortisol do participante, usadas apenas quando o formulário selecionado é
        /// <see cref="TipoDeFormulario.Cortisol"/>. Pode ser null.
        /// </param>
        public static List<LinhaDeFormularioParaTabela> Construir(
            RespostasDeFormularioPorParticipante respostas,
            TipoDeFormulario formulario,
            Parsing.RegistroDeCortisol registroDeCortisol = null)
        {
            if (respostas == null)
                return new List<LinhaDeFormularioParaTabela>();

            switch (formulario)
            {
                case TipoDeFormulario.Cortisol:
                    return ConstruirParaCortisol(registroDeCortisol);

                case TipoDeFormulario.Sintese:
                    return ConstruirParaSintese(respostas, registroDeCortisol);

                case TipoDeFormulario.Recrutamento:
                    return ConstruirParaRecrutamento(respostas);

                case TipoDeFormulario.PercepcaoAmbiental:
                    return ConstruirParaPlanilha(
                        respostas, "c_percepção_ambiental", DeParaDeQuestoes.PercepcaoAmbiental);

                case TipoDeFormulario.Ieee:
                    return ConstruirParaIeee(respostas);

                // O ramo EstresseOcupacional foi REMOVIDO. Ele montava uma tabela a partir de
                // escores por domínio A–E cuja composição nunca foi configurada: os cinco saíam
                // NaN e as colunas de cada domínio vinham vazias. A decomposição do IEEE por
                // domínio existe e funciona — é feita por DominiosDoIeee, dentro da categoria
                // Ieee, associando cada um dos 34 itens ao seu domínio pelo enunciado.

                case TipoDeFormulario.SindromeDoEdificioDoente:
                    return ConstruirParaSbs(respostas);

                case TipoDeFormulario.Demografia:
                    return ConstruirParaDemografia(respostas);

                case TipoDeFormulario.TodasAsRespostas:
                default:
                    return ConstruirParaTodasAsRespostas(respostas);
            }
        }

        /// <summary>
        /// Lista todas as respostas provenientes de uma planilha específica do arquivo, usando o
        /// prefixo que o <see cref="Parsing.AnalisadorExcelFormularios"/> aplica aos nomes de
        /// coluna. O prefixo é removido na exibição.
        /// </summary>
        /// <remarks>
        /// As linhas seguem a ORDEM DAS COLUNAS da planilha (esquerda para a direita), percorrendo
        /// <see cref="RespostasDeFormularioPorParticipante.OrdemDasColunas"/>. Nenhuma ordenação
        /// alfabética é aplicada: a tabela deve espelhar o arquivo.
        /// </remarks>
        /// <param name="dePara">
        /// Tabela de rótulos curtos da planilha. Null exibe os enunciados como estão — é o caso do
        /// <c>b_ieee</c>, cujos cabeçalhos já são afirmações curtas ("Fazer um trabalho
        /// repetitivo") e não ganhariam nada com um apelido.
        /// </param>
        private static List<LinhaDeFormularioParaTabela> ConstruirParaPlanilha(
            RespostasDeFormularioPorParticipante respostas,
            string nomeDaPlanilha,
            DeParaDeQuestao[] dePara = null,
            string grupoPadrao = GruposDeQuestoes.Outros)
        {
            if (respostas.RespostasBrutas == null)
                return new List<LinhaDeFormularioParaTabela>();

            string prefixo = nomeDaPlanilha + Parsing.AnalisadorExcelFormularios.SeparadorDePlanilha;

            var colunasNaOrdem = respostas.OrdemDasColunas != null && respostas.OrdemDasColunas.Count > 0
                ? (IEnumerable<string>)respostas.OrdemDasColunas
                : respostas.RespostasBrutas.Keys;

            var linhas = new List<LinhaDeFormularioParaTabela>();

            foreach (string coluna in colunasNaOrdem)
            {
                if (!coluna.StartsWith(prefixo, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                // A coluna de identificação é a chave, já exibida no título da tabela: listá-la
                // como resposta só ocuparia a primeira linha de cada formulário.
                if (EhColunaDeIdentificacao(coluna))
                    continue;

                string enunciado = coluna.Substring(prefixo.Length);

                // Coluna sem cabeçalho não tem o que rotular nem o que dizer — a b_ieee traz uma
                // (AK) logo depois do TOTAL, e ela virava uma linha em branco na tela.
                if (string.IsNullOrWhiteSpace(enunciado))
                    continue;

                respostas.RespostasBrutas.TryGetValue(coluna, out string valor);

                var (rotulo, grupo, valorFormatado) = dePara != null
                    ? DeParaDeQuestoes.Traduzir(enunciado, valor, dePara)
                    : (enunciado.Trim(), grupoPadrao, valor);

                linhas.Add(new LinhaDeFormularioParaTabela(
                    rotulo, valorFormatado, "Resposta", grupo, enunciado));
            }

            return linhas;
        }

        private static List<LinhaDeFormularioParaTabela> ConstruirParaIeee(
            RespostasDeFormularioPorParticipante respostas)
        {
            var linhas = new List<LinhaDeFormularioParaTabela>
            {
                // Sem casas decimais: o total é a soma de 34 respostas inteiras, e "47,00"
                // sugeriria uma precisão que o instrumento não tem.
                new LinhaDeFormularioParaTabela("Escore total",
                    double.IsNaN(respostas.EscoreIeeeTotal) ? "—" : respostas.EscoreIeeeTotal.ToString("N0"),
                    "Escore", GruposDeQuestoes.Escores),
                new LinhaDeFormularioParaTabela("Classificação", respostas.ClassificacaoIeee,
                    "Escore", GruposDeQuestoes.Escores),
                new LinhaDeFormularioParaTabela(
                    "Faixa possível",
                    $"{LimiaresDeClassificacaoIeee.EscoreMinimo:N0}–{LimiaresDeClassificacaoIeee.EscoreMaximo:N0}",
                    "Referência", GruposDeQuestoes.Escores),
            };

            // Os 34 itens deixaram de formar um bloco único e passaram a se distribuir pelos cinco
            // domínios do instrumento, para que a leitura de cada resposta venha acompanhada da
            // dimensão que ela mede.
            // A coluna TOTAL fica de fora — já é a primeira linha, como "Escore total"; repetida
            // aqui apareceria como um 35º item do instrumento, que ela não é.
            var itens = ConstruirParaPlanilha(
                    respostas, "b_ieee", dePara: null, grupoPadrao: GruposDeQuestoes.Outros)
                .Where(linha => !string.Equals(linha.Item, "TOTAL",
                                               System.StringComparison.OrdinalIgnoreCase));

            foreach (var item in itens)
                linhas.Add(item.ComGrupo(DominiosDoIeee.ObterDominio(item.Item)));

            return linhas;
        }

        /// <summary>
        /// Lista os 14 itens do Q37 (frequência do sintoma) seguidos dos 14 do Q38 (atribuição ao
        /// trabalho), sem a linha agregada de contagem de sintomas.
        /// </summary>
        /// <remarks>
        /// A severidade agregada do SBS não é somada aqui: ela vem de
        /// <see cref="Processamento.CalculadorDeIndicadoresDeSbs"/>, que aplica a escala ordinal
        /// aos rótulos de frequência do arquivo, e alimenta os gráficos da aba de Dados do Grupo.
        /// </remarks>
        private static List<LinhaDeFormularioParaTabela> ConstruirParaSbs(
            RespostasDeFormularioPorParticipante respostas)
        {
            var itensQ37 = ComposicaoDeEscoresDeFormulario.ItensDoSbsQ37;
            var itensQ38 = ComposicaoDeEscoresDeFormulario.ItensDoSbsQ38;

            var linhas = new List<LinhaDeFormularioParaTabela>();

            for (int indice = 0; indice < itensQ37.Length; indice++)
            {
                // O pareamento é POSICIONAL: o i-ésimo item da Q37 corresponde ao i-ésimo da Q38,
                // porque as duas questões percorrem os mesmos 14 sintomas na mesma ordem — o que
                // na planilha equivale a "coluna da Q38 = coluna da Q37 + 14" (AI→AW … AV→BJ).
                LocalizadorDeColunaDeFormulario.TentarLocalizarChave(
                    respostas.RespostasBrutas, itensQ37[indice], out string chaveQ37);

                string valorQ37 = null;
                if (chaveQ37 != null)
                    respostas.RespostasBrutas.TryGetValue(chaveQ37, out valorQ37);

                string valorQ38 = null;
                if (indice < itensQ38.Length
                    && LocalizadorDeColunaDeFormulario.TentarLocalizarChave(
                        respostas.RespostasBrutas, itensQ38[indice], out string chaveQ38))
                {
                    respostas.RespostasBrutas.TryGetValue(chaveQ38, out valorQ38);
                }

                // O rótulo é só o nome do sintoma. O resto do enunciado — o período de referência e
                // a pergunta — é idêntico nas catorze linhas e sobe para o cabeçalho do grupo.
                var linha = new LinhaDeFormularioParaTabela(
                    RotulosDeSintomas.Extrair(chaveQ37 ?? itensQ37[indice]),
                    RotulosDeSintomas.NormalizarFrequencia(valorQ37),
                    "Resposta", GruposDeQuestoes.Sintomas,
                    chaveQ37);

                linha.ValorComplementar = ResumirAtribuicaoAoTrabalho(valorQ38);

                linhas.Add(linha);
            }

            return linhas;
        }

        /// <summary>
        /// Reduz a resposta da Q38 a "Sim", "Não" ou travessão.
        /// </summary>
        /// <remarks>
        /// A célula traz a alternativa inteira do formulário — "NÃO É EM É DECORRÊNCIA DO MEU
        /// TRABALHO NO CAISM UNICAMP", com o erro de digitação e tudo. Catorze dessas, em caixa
        /// alta, ocupavam mais espaço do que os sintomas que qualificavam, para dizer um único bit.
        /// A pergunta já está no cabeçalho da coluna; aqui basta a resposta.
        ///
        /// A classificação é a MESMA do <see cref="Processamento.CalculadorDeIndicadoresDeSbs"/>,
        /// e não uma segunda leitura do texto: tela e gráfico têm de concordar sobre o que é uma
        /// atribuição ao trabalho.
        /// </remarks>
        private static string ResumirAtribuicaoAoTrabalho(string respostaBruta)
        {
            bool? atribui = Processamento.CalculadorDeIndicadoresDeSbs.ClassificarAtribuicao(respostaBruta);

            return atribui switch
            {
                true  => "Sim",
                false => "Não",
                _     => "—",
            };
        }

        /// <summary>
        /// Lista as respostas de <c>a_recrutamento</c> EXCETO as questões 37 e 38, que passaram a
        /// ser exibidas no formulário de Sick Building Syndrome.
        /// </summary>
        private static List<LinhaDeFormularioParaTabela> ConstruirParaRecrutamento(
            RespostasDeFormularioPorParticipante respostas)
        {
            return ConstruirParaPlanilha(respostas, "a_recrutamento", DeParaDeQuestoes.Recrutamento)
                .Where(linha => !EhQuestaoDeSbs(linha.Item) && !EhQuestaoDeSbs(linha.EnunciadoOriginal))
                .ToList();
        }

        /// <summary>
        /// Indica se uma coluna é a de identificação do participante, em qualquer das planilhas.
        /// </summary>
        private static bool EhColunaDeIdentificacao(string nomeDaColuna)
        {
            return MapeamentoDeColunasDeFormulario.IdentificacaoDoParticipante.Any(
                candidato => !string.IsNullOrWhiteSpace(candidato)
                          && nomeDaColuna.IndexOf(candidato, System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Indica se o nome de uma questão pertence ao Q37 ou ao Q38 (os 14 sintomas de SBS).
        /// </summary>
        private static bool EhQuestaoDeSbs(string nomeDaQuestao)
        {
            if (string.IsNullOrWhiteSpace(nomeDaQuestao))
                return false;

            return nomeDaQuestao.IndexOf("QUESTÃO 37", System.StringComparison.OrdinalIgnoreCase) >= 0
                || nomeDaQuestao.IndexOf("QUESTÃO 38", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Monta a síntese cruzada do participante: demografia, psicometria, cortisol,
        /// concordância fisiologia ↔ percepção e as ressalvas que afetam a leitura do cortisol.
        /// </summary>
        private static List<LinhaDeFormularioParaTabela> ConstruirParaSintese(
            RespostasDeFormularioPorParticipante respostas,
            Parsing.RegistroDeCortisol registroDeCortisol)
        {
            var linhas = new List<LinhaDeFormularioParaTabela>();

            // Idade: faixa etária em texto, exibida como está.
            LocalizadorDeColunaDeFormulario.TentarLocalizar(
                respostas.RespostasBrutas, MapeamentoDeColunasDeFormulario.Idade, out _, out string idade);
            linhas.Add(new LinhaDeFormularioParaTabela(
                "Idade", idade, "Resposta", GruposDeQuestoes.Perfil));

            // IMC: valor numérico + faixa da OMS.
            linhas.Add(ConstruirLinhaDeImc(respostas));

            // Índice de Estresse em Enfermeiros: TOTAL da coluna AJ de b_ieee (34–170) e a faixa
            // correspondente. A legenda das três faixas vem numa linha própria de referência, em
            // vez de emendada no rótulo: o bloco de grupos quebra o texto e não precisa mais
            // comprimir tudo numa célula estreita.
            var faixaDoIeee = ClassificadorDeFaixaDeEstresseIeee.Classificar(respostas.EscoreIeeeTotal);

            linhas.Add(new LinhaDeFormularioParaTabela(
                "Índice de Estresse em Enfermeiros",
                faixaDoIeee.HasValue
                    ? $"{respostas.EscoreIeeeTotal:N0} — {ClassificadorDeFaixaDeEstresseIeee.ObterNomeCurto(faixaDoIeee.Value)}"
                    : "—",
                "Escore", GruposDeQuestoes.Escores));

            linhas.Add(new LinhaDeFormularioParaTabela(
                "Faixas de referência",
                ClassificadorDeFaixaDeEstresseIeee.ObterLegendaDasFaixas().Replace("Referência: ", string.Empty),
                "Referência", GruposDeQuestoes.Escores));

            // Caracterização, na ordem das colunas de origem em a_recrutamento.
            linhas.Add(ConstruirLinhaDeResposta(
                respostas, "Estado civil", MapeamentoDeColunasDeFormulario.EstadoCivil,
                GruposDeQuestoes.Perfil));
            linhas.Add(ConstruirLinhaDeResposta(
                respostas, "Menopausa", MapeamentoDeColunasDeFormulario.Menopausa,
                GruposDeQuestoes.SaudeDaMulher));
            linhas.Add(ConstruirLinhaDeResposta(
                respostas, "Seção na Divisão de Enfermagem",
                MapeamentoDeColunasDeFormulario.DivisaoDeEnfermagem, GruposDeQuestoes.Trabalho));

            // Lista CONFIRMADA COMPLETA pelo autor em 31/08/2026: são só as três linhas acima
            // (colunas H, K e P de a_recrutamento) — o item vazio ao final do pedido original
            // era o fim da lista, não um item perdido. Acrescentar uma futura é uma linha em
            // MapeamentoDeColunasDeFormulario mais uma chamada a ConstruirLinhaDeResposta aqui.

            return linhas;
        }

        /// <summary>
        /// Monta uma linha de síntese que apenas exibe a resposta do participante, sem derivar nada.
        /// </summary>
        /// <remarks>
        /// Resposta ausente aparece como travessão, e não em branco, para distinguir "não respondeu"
        /// de "coluna não localizada" — as duas situações produziriam célula vazia.
        /// </remarks>
        private static LinhaDeFormularioParaTabela ConstruirLinhaDeResposta(
            RespostasDeFormularioPorParticipante respostas, string rotulo, string[] nomesDaColuna,
            string grupo)
        {
            LocalizadorDeColunaDeFormulario.TentarLocalizar(
                respostas.RespostasBrutas, nomesDaColuna, out string chave, out string valor);

            return new LinhaDeFormularioParaTabela(
                rotulo, string.IsNullOrWhiteSpace(valor) ? "—" : valor, "Resposta", grupo, chave);
        }

        /// <summary>
        /// Constrói as dez linhas da categoria "Dados do HMD - RV" a partir do resumo de um
        /// participante de realidade virtual.
        /// </summary>
        /// <remarks>
        /// Caminho separado do <see cref="Construir"/> porque a origem é outra: estes valores vêm
        /// do <c>dados_hmd.csv</c> e de um participante que não é o do seletor do topo da aba.
        ///
        /// A natureza de todas as linhas é "Medição", e não "Resposta" ou "Escore": não é o que o
        /// participante declarou nem pontuação derivada de instrumento — é o que o aparelho
        /// registrou.
        /// </remarks>
        /// <param name="resumo">Resumo do participante; null devolve lista vazia.</param>
        public static List<LinhaDeFormularioParaTabela> ConstruirParaHmd(
            ResumoDoHmdDoParticipante resumo)
        {
            if (resumo == null)
                return new List<LinhaDeFormularioParaTabela>();

            const string medicao = "Medição";

            // Duas tabelas: a extensão da coleta e o que o rastreamento registrou dentro dela.
            const string coleta        = GruposDeQuestoes.ColetaEmRv;
            const string rastreamento  = GruposDeQuestoes.RastreamentoOcular;

            return new List<LinhaDeFormularioParaTabela>
            {
                new("Sessões registradas",
                    resumo.QuantidadeDeSessoes.ToString(Cultura), medicao, coleta),

                new("Cenários visitados",
                    resumo.QuantidadeDeCenarios.ToString(Cultura), medicao, coleta),

                new("Distribuição entre cenários",
                    FormatarPermanenciaPorCenario(resumo.PermanenciaPorCenario), medicao, coleta),

                new("Tempo total em RV",
                    FormatarNumero(resumo.TempoTotalEmRvMinutos, "N1", " min"), medicao, coleta),

                new("Fixações detectadas",
                    resumo.QuantidadeDeFixacoes.ToString(Cultura), medicao, rastreamento),

                new("Duração média das fixações",
                    FormatarNumero(resumo.DuracaoMediaDasFixacoesMs, "N0", " ms"), medicao, rastreamento),

                new("Objetos distintos observados",
                    resumo.QuantidadeDeObjetosObservados.ToString(Cultura), medicao, rastreamento),
            };

            // "Objetos mais observados" deixou de ser linha de tabela: virou faixa de cartões, um
            // por elemento, com o ícone do tipo — ver
            // CartaoDeDestaqueDoParticipante.ConstruirObjetosMaisObservados. A leitura que
            // interessa é comparativa, e uma lista numerada dentro de uma célula não deixa
            // comparar de relance.
        }

        /// <summary>
        /// Formata a permanência por cenário, um cenário por linha dentro da mesma célula.
        /// </summary>
        /// <remarks>
        /// Traz o tempo E o percentual: só o percentual esconderia que dois participantes com a
        /// mesma repartição relativa podem ter durações totais muito diferentes, e é a duração
        /// absoluta da sessão que decide se há fixação detectável.
        /// </remarks>
        private static string FormatarPermanenciaPorCenario(
            IReadOnlyList<PermanenciaEmCenario> permanencias)
        {
            if (permanencias == null || !permanencias.Any())
                return "—";

            return string.Join(System.Environment.NewLine, permanencias.Select(permanencia =>
                $"{permanencia.Cenario} — {permanencia.TempoMinutos.ToString("N1", Cultura)} min" +
                $" ({permanencia.PercentualDoTempo.ToString("N0", Cultura)}%," +
                $" {permanencia.QuantidadeDeSessoes} sessão(ões))"));
        }

        /// <summary>
        /// Formata o ranking de objetos como uma lista numerada em linhas dentro da mesma célula.
        /// </summary>
        /// <remarks>
        /// Uma linha por objeto, e não tudo emendado: os nomes chegam a 25 caracteres e cinco
        /// deles numa linha só ficariam ilegíveis na largura da coluna.
        ///
        /// O percentual acompanha o nome porque a ordem sozinha engana — em RB001 o primeiro
        /// colocado tem 28% e em MA001 tem 13%; sem o número, os dois pareceriam o mesmo tipo de
        /// concentração do olhar.
        /// </remarks>
        private static string FormatarObjetosMaisObservados(
            IReadOnlyList<ObjetoObservado> objetos)
        {
            if (objetos == null || !objetos.Any())
                return "—";

            return string.Join(System.Environment.NewLine, objetos.Select((objeto, indice) =>
                $"{indice + 1}. {objeto.Nome} — {objeto.PercentualDeAmostras.ToString("N1", Cultura)}%"));
        }

        private static readonly System.Globalization.CultureInfo Cultura =
            System.Globalization.CultureInfo.CurrentCulture;

        /// <summary>
        /// Formata um valor com sufixo de unidade, devolvendo travessão quando não há medida.
        /// </summary>
        private static string FormatarNumero(double valor, string formato, string sufixo)
        {
            return double.IsNaN(valor) ? "—" : valor.ToString(formato, Cultura) + sufixo;
        }

        /// <summary>Classifica o IMC do participante nas faixas da OMS.</summary>
        private static LinhaDeFormularioParaTabela ConstruirLinhaDeImc(
            RespostasDeFormularioPorParticipante respostas)
        {
            if (!LocalizadorDeColunaDeFormulario.TentarLocalizar(
                    respostas.RespostasBrutas, FaixasDeImc.NomesDaColuna, out _, out string texto)
                || !double.TryParse(texto, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out double imc))
            {
                return new LinhaDeFormularioParaTabela(
                    "IMC", "—", "Resposta", GruposDeQuestoes.Perfil);
            }

            return new LinhaDeFormularioParaTabela(
                "IMC", $"{imc:N1} — {ClassificarImc(imc)}", "Escore", GruposDeQuestoes.Perfil);
        }

        /// <summary>Faixa da OMS correspondente a um valor de IMC.</summary>
        public static string ClassificarImc(double imc)
        {
            if (imc < FaixasDeImc.LimiteDeBaixoPeso)       return "baixo peso";
            if (imc <= FaixasDeImc.LimiteDePesoNormal)     return "normal";
            if (imc <= FaixasDeImc.LimiteDeSobrepeso)      return "sobrepeso";
            if (imc <= FaixasDeImc.LimiteDeObesidadeGrauI) return "obesidade grau I";
            if (imc <= FaixasDeImc.LimiteDeObesidadeGrauII) return "obesidade grau II";
            return "obesidade grau III";
        }

        /// <summary>
        /// Monta a tabela de cortisol do participante: as duas coletas usadas e o delta
        /// (coluna S − coluna B).
        /// </summary>
        private static List<LinhaDeFormularioParaTabela> ConstruirParaCortisol(
            Parsing.RegistroDeCortisol registro)
        {
            if (registro == null)
            {
                return new List<LinhaDeFormularioParaTabela>
                {
                    new LinhaDeFormularioParaTabela(
                        "Cortisol",
                        "Sem dados para este participante — verifique se o arquivo de cortisol foi carregado",
                        "Referência", GruposDeQuestoes.Cortisol),
                };
            }

            return new List<LinhaDeFormularioParaTabela>
            {
                new LinhaDeFormularioParaTabela(
                    registro.RotuloDoValorBasal ?? "Coleta basal (coluna B)",
                    Formatar(registro.ValorBasal), "Resposta", GruposDeQuestoes.Cortisol),
                new LinhaDeFormularioParaTabela(
                    registro.RotuloDoValorFinal ?? "Coleta final (coluna S)",
                    Formatar(registro.ValorFinal), "Resposta", GruposDeQuestoes.Cortisol),
                new LinhaDeFormularioParaTabela(
                    "Delta de cortisol (S − B)", Formatar(registro.Delta), "Escore",
                    GruposDeQuestoes.Cortisol),
            };
        }

        private static List<LinhaDeFormularioParaTabela> ConstruirParaDemografia(
            RespostasDeFormularioPorParticipante respostas)
        {
            return new List<LinhaDeFormularioParaTabela>
            {
                new LinhaDeFormularioParaTabela("Identificação", respostas.IdentificacaoManual,
                    "Resposta", GruposDeQuestoes.Perfil),
                new LinhaDeFormularioParaTabela("Idade", Formatar(respostas.Idade),
                    "Resposta", GruposDeQuestoes.Perfil),
                new LinhaDeFormularioParaTabela("Peso", Formatar(respostas.Peso),
                    "Resposta", GruposDeQuestoes.Perfil),
            };
        }

        private static List<LinhaDeFormularioParaTabela> ConstruirParaTodasAsRespostas(
            RespostasDeFormularioPorParticipante respostas)
        {
            if (respostas.RespostasBrutas == null)
                return new List<LinhaDeFormularioParaTabela>();

            return respostas.RespostasBrutas
                .OrderBy(par => par.Key)
                .Select(par => new LinhaDeFormularioParaTabela(
                    par.Key, par.Value, "Resposta", GruposDeQuestoes.Outros))
                .ToList();
        }

        /// <summary>
        /// Recupera as respostas brutas das colunas indicadas. Quando a composição do domínio
        /// ainda não foi configurada, devolve uma linha explicando a ausência em vez de uma
        /// tabela silenciosamente vazia.
        /// </summary>
        private static List<LinhaDeFormularioParaTabela> ObterRespostasDasColunas(
            RespostasDeFormularioPorParticipante respostas, string[] nomesDasColunas)
        {
            var linhas = new List<LinhaDeFormularioParaTabela>();

            if (nomesDasColunas == null || nomesDasColunas.Length == 0)
            {
                linhas.Add(new LinhaDeFormularioParaTabela(
                    "(itens não configurados)",
                    "Preencher ComposicaoDeEscoresDeFormulario",
                    "Configuração", GruposDeQuestoes.Itens));
                return linhas;
            }

            foreach (string nomeDaColuna in nomesDasColunas)
            {
                // Usa a MESMA regra de correspondência do cálculo dos escores (exata e depois por
                // conteúdo): a configuração guarda apenas um trecho do enunciado da questão, então
                // uma busca exata aqui não encontraria nada.
                string valor = null;
                string rotulo = nomeDaColuna;

                if (LocalizadorDeColunaDeFormulario.TentarLocalizarChave(
                        respostas.RespostasBrutas, nomeDaColuna, out string chaveReal))
                {
                    rotulo = chaveReal;
                    respostas.RespostasBrutas.TryGetValue(chaveReal, out valor);
                }

                linhas.Add(new LinhaDeFormularioParaTabela(
                    rotulo, valor, "Resposta", GruposDeQuestoes.Itens, chaveReal));
            }

            return linhas;
        }

        private static string Formatar(double valor)
        {
            return double.IsNaN(valor) ? "—" : valor.ToString("N2");
        }
    }
}
