using System;
using System.Collections.Generic;
using System.Linq;

namespace PluginCAISM.Configuracao
{
    /// <summary>
    /// Grupos temáticos em que as questões são reunidas na aba de Dados do Participante.
    /// </summary>
    /// <remarks>
    /// Os nomes são constantes, e não literais soltos, porque servem a duas coisas ao mesmo tempo:
    /// são o título exibido no cabeçalho recolhível e são a chave que liga cada questão ao seu
    /// ícone em <see cref="Interface.IconesDeGrupo"/>. Um erro de digitação em qualquer uma das
    /// pontas deixaria o grupo sem ícone e sem ordem definida.
    /// </remarks>
    public static class GruposDeQuestoes
    {
        public const string Perfil            = "Perfil";
        public const string SaudeDaMulher     = "Saúde da mulher";
        public const string Trabalho          = "Trabalho";
        public const string Habitos           = "Hábitos";
        public const string Saude             = "Saúde";
        public const string Sono              = "Sono";
        public const string ExperienciaEmRv   = "Experiência em RV";

        public const string UsoDaSala         = "Uso da sala de descanso";
        public const string ConfortoAmbiental = "Conforto ambiental";
        public const string ContatoComExterno = "Contato com o exterior";
        public const string Natureza          = "Natureza";

        public const string Sintomas          = "Sintomas";
        public const string Escores           = "Escores";

        // Bloco genérico de itens, hoje usado só pelo formulário de domínios de estresse
        // ocupacional. Os 34 itens do IEEE deixaram de usá-lo — passaram aos cinco domínios abaixo.
        public const string Itens             = "Itens do instrumento";

        // ── Domínios do Índice de Estresse em Enfermeiros ────────────────
        // Os 34 itens do instrumento deixaram de formar um bloco único ("Itens do instrumento") e
        // passaram a se distribuir por estes cinco domínios.
        public const string IeeeDemandas      = "Demandas e sobrecargas de trabalho";
        public const string IeeeCondicoes     = "Condições e organização do trabalho";
        public const string IeeeRelacoes      = "Relacionamento interpessoal e clima";
        public const string IeeeAssistencia   = "Assistência e carga emocional";
        public const string IeeeAutonomia     = "Autonomia, gestão e interface do trabalho e da vida";
        public const string Cortisol          = "Coletas de cortisol";
        public const string ColetaEmRv        = "Coleta em realidade virtual";
        public const string RastreamentoOcular = "Rastreamento ocular";
        public const string Outros            = "Outras respostas";

        /// <summary>
        /// Ordem em que os grupos aparecem na tela.
        /// </summary>
        /// <remarks>
        /// Fixa, e não alfabética: a leitura desce do que caracteriza a pessoa para o que ela
        /// relata sobre trabalho e saúde. Um grupo ausente desta lista vai para o fim, o que
        /// mantém a tela funcional se alguém acrescentar uma questão e esquecer de ordená-la.
        /// </remarks>
        public static readonly string[] Ordem =
        {
            Escores,
            Perfil,
            Trabalho,
            Habitos,
            Saude,
            SaudeDaMulher,
            Sono,
            ExperienciaEmRv,
            UsoDaSala,
            ConfortoAmbiental,
            ContatoComExterno,
            Natureza,
            Sintomas,
            Cortisol,
            ColetaEmRv,
            RastreamentoOcular,
            IeeeDemandas,
            IeeeCondicoes,
            IeeeRelacoes,
            IeeeAssistencia,
            IeeeAutonomia,
            Outros,
        };

        /// <summary>
        /// Descrição de cada domínio do Índice de Estresse em Enfermeiros, exibida sob o título do
        /// bloco.
        /// </summary>
        /// <remarks>
        /// Os domínios não são um agrupamento visual: dizem o que cada conjunto de itens mede, e a
        /// descrição é o que permite ler a resposta de um item sabendo a que dimensão ele pertence.
        /// </remarks>
        public static readonly (string Grupo, string Descricao)[] DescricoesDeDominio =
        {
            (IeeeDemandas,
                "Avaliação da carga de trabalho quantitativa e qualitativa: excesso de tarefas, " +
                "pressão de tempo, dimensionamento insuficiente de pessoal e acúmulo de funções, " +
                "mensurando a frequência com que a sobrecarga é percebida como estressora."),

            (IeeeCondicoes,
                "Investigação das condições físico-estruturais do ambiente (recursos materiais, " +
                "espaço, equipamentos) e da organização institucional (escalas, turnos, normas e " +
                "fluxos), identificando fatores organizacionais geradores de estresse."),

            (IeeeRelacoes,
                "Levantamento da qualidade das relações com equipe, chefias, pacientes e " +
                "acompanhantes, bem como do clima laboral, delimitando conflitos e tensões " +
                "interpessoais como fontes de estresse."),

            (IeeeAssistencia,
                "Caracterização das demandas emocionais inerentes ao cuidado direto, como o " +
                "enfrentamento do sofrimento, da dor e da morte do paciente, avaliando o desgaste " +
                "afetivo associado à assistência."),

            (IeeeAutonomia,
                "Verificação do grau de autonomia decisória, do estilo de gestão e da interface " +
                "entre demandas laborais e pessoais, identificando a repercussão do trabalho " +
                "sobre o equilíbrio com a vida privada."),
        };

        /// <summary>Descrição do grupo, ou vazio quando não houver.</summary>
        public static string ObterDescricao(string grupo)
        {
            foreach (var (nome, descricao) in DescricoesDeDominio)
            {
                if (string.Equals(nome, grupo, System.StringComparison.Ordinal))
                    return descricao;
            }

            return string.Empty;
        }

        /// <summary>Posição do grupo na <see cref="Ordem"/>; grupos desconhecidos vão para o fim.</summary>
        public static int ObterPosicao(string grupo)
        {
            int posicao = Array.IndexOf(Ordem, grupo);
            return posicao < 0 ? Ordem.Length : posicao;
        }
    }

    /// <summary>
    /// Correspondência entre o cabeçalho de uma coluna da planilha e o rótulo curto exibido.
    /// </summary>
    public sealed class DeParaDeQuestao
    {
        /// <summary>
        /// Trecho que identifica a coluna no arquivo, procurado por conteúdo e sem diferenciar
        /// maiúsculas.
        /// </summary>
        public string TrechoDoCabecalho { get; }

        /// <summary>Rótulo curto exibido no lugar do enunciado inteiro.</summary>
        public string RotuloCurto { get; }

        /// <summary>Grupo temático em que a questão é exibida.</summary>
        public string Grupo { get; }

        /// <summary>
        /// Formato numérico aplicado à resposta, ou null para exibi-la como texto.
        /// </summary>
        /// <remarks>
        /// Existe porque a planilha guarda números com toda a precisão da conta que os gerou: o
        /// IMC chega como <c>23.73323840037973</c>, que não é um dado — é um resíduo de divisão.
        /// Respostas que não convertem para número são exibidas como estão, sem erro.
        /// </remarks>
        public string FormatoNumerico { get; }

        /// <summary>Unidade acrescentada ao valor, com o espaço já incluído. Ex.: <c>" kg"</c>.</summary>
        public string Sufixo { get; }

        /// <summary>
        /// Se a resposta é um número de série de data do Excel e deve ser convertida.
        /// </summary>
        /// <remarks>
        /// As datas escritas pelo formulário chegam como o serial do Excel — "45722" no lugar de
        /// uma data de início de férias. Sem esta conversão a resposta é ilegível.
        /// </remarks>
        public bool EhDataSerial { get; }

        public DeParaDeQuestao(
            string trechoDoCabecalho,
            string rotuloCurto,
            string grupo,
            string formatoNumerico = null,
            string sufixo = "",
            bool ehDataSerial = false)
        {
            TrechoDoCabecalho = trechoDoCabecalho;
            RotuloCurto       = rotuloCurto;
            Grupo             = grupo;
            FormatoNumerico   = formatoNumerico;
            Sufixo            = sufixo ?? string.Empty;
            EhDataSerial      = ehDataSerial;
        }

        /// <summary>
        /// Aplica à resposta o formato declarado para esta questão.
        /// </summary>
        /// <remarks>
        /// O número é lido em cultura invariante — é assim que a planilha grava, com ponto
        /// decimal — e escrito na cultura da máquina, que é como o usuário lê. Resposta vazia ou
        /// não numérica atravessa sem alteração: o de-para embeleza o que consegue e nunca esconde
        /// o que não entendeu.
        /// </remarks>
        public string FormatarResposta(string valorBruto)
        {
            if (string.IsNullOrWhiteSpace(valorBruto))
                return valorBruto;

            string texto = valorBruto.Trim();

            if (!double.TryParse(texto, System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture, out double numero))
            {
                return texto;
            }

            if (EhDataSerial)
                return ConverterSerialDoExcel(numero) ?? texto;

            if (FormatoNumerico == null)
                return texto + Sufixo;

            return numero.ToString(FormatoNumerico,
                                   System.Globalization.CultureInfo.CurrentCulture) + Sufixo;
        }

        /// <summary>
        /// Converte um serial de data do Excel em data legível, ou null se estiver fora de faixa.
        /// </summary>
        /// <remarks>
        /// A época é 1899-12-30, e não 1900-01-01: o Excel trata 1900 como bissexto por
        /// compatibilidade com o Lotus 1-2-3, e a base deslocada em dois dias é o que reproduz a
        /// conta dele para qualquer data posterior a fevereiro de 1900.
        /// </remarks>
        private static string ConverterSerialDoExcel(double serial)
        {
            // Fora desta faixa é quase certo que o número não é uma data: 1 é 31/12/1899 e
            // 60000 já passa de 2064.
            if (serial < 1 || serial > 60000)
                return null;

            var epoca = new System.DateTime(1899, 12, 30);
            return epoca.AddDays(serial).ToString("dd/MM/yyyy",
                                                  System.Globalization.CultureInfo.CurrentCulture);
        }
    }

    /// <summary>
    /// De-para entre os enunciados das planilhas de formulários e os rótulos curtos da aba de
    /// Dados do Participante.
    /// </summary>
    /// <remarks>
    /// <b>Por que existe.</b> Os cabeçalhos do arquivo são os enunciados completos do formulário,
    /// com instrução ao respondente embutida — "QUESTÃO 4] Qual o seu gênero? Caso a sua opção não
    /// conste nos itens abaixo, favor selecionar “Outro”". Exibidos como estão, 33 deles formam uma
    /// parede de texto em que a resposta, que é o que interessa, fica perdida. Aqui cada um vira o
    /// substantivo que a questão de fato mede: "Gênero".
    ///
    /// <b>Como a correspondência é feita.</b> Pelo marcador <c>QUESTÃO N]</c>, incluindo o colchete
    /// de fechamento — sem ele "QUESTÃO 3" casaria também com as questões 30 a 38. As colunas sem
    /// numeração são identificadas por um trecho discriminante do próprio enunciado. A busca é por
    /// conteúdo porque os cabeçalhos reais trazem espaço inicial (coluna L) e quebra de linha no
    /// meio (colunas X e AE), e uma comparação exata não sobreviveria a isso.
    ///
    /// <b>O que acontece com uma coluna não mapeada.</b> Ela é exibida com o enunciado original, no
    /// grupo <see cref="GruposDeQuestoes.Outros"/> — nada some da tela por falta de mapeamento.
    /// Acrescentar uma questão nova é acrescentar uma linha aqui.
    /// </remarks>
    public static class DeParaDeQuestoes
    {
        /// <summary>
        /// Planilha <c>a_recrutamento</c>: caracterização do participante. As questões 37 e 38
        /// não entram — são exibidas no formulário de Sick Building Syndrome, com tratamento
        /// próprio (ver <see cref="RotulosDeSintomas"/>).
        /// </summary>
        public static readonly DeParaDeQuestao[] Recrutamento =
        {
            new("IMC",           "IMC",                              GruposDeQuestoes.Perfil,
                formatoNumerico: "N1"),
            new("QUESTÃO 4]",    "Gênero",                           GruposDeQuestoes.Perfil),
            new("QUESTÃO 5]",    "Idade",                            GruposDeQuestoes.Perfil,
                sufixo: " anos"),
            new("QUESTÃO 6]",    "Estatura",                         GruposDeQuestoes.Perfil,
                formatoNumerico: "N2", sufixo: " m"),
            new("QUESTÃO 7]",    "Peso",                             GruposDeQuestoes.Perfil,
                formatoNumerico: "N0", sufixo: " kg"),
            new("QUESTÃO 9]",    "Estado civil",                     GruposDeQuestoes.Perfil),
            new("QUESTÃO 10]",   "Filhos ou dependentes",            GruposDeQuestoes.Perfil),
            new("QUESTÃO 11]",   "Idade dos filhos ou dependentes",  GruposDeQuestoes.Perfil),

            new("QUESTÃO 8]",    "Gravidez",                         GruposDeQuestoes.SaudeDaMulher),
            new("QUESTÃO 12]",   "Menopausa",                        GruposDeQuestoes.SaudeDaMulher),
            new("QUESTÃO 13]",   "Ciclo menstrual",                  GruposDeQuestoes.SaudeDaMulher),
            new("QUESTÃO 14]",   "Uso de anticoncepcional",          GruposDeQuestoes.SaudeDaMulher),
            new("QUESTÃO 15]",   "Tipo de anticoncepcional",         GruposDeQuestoes.SaudeDaMulher),
            new("QUESTÃO 16]",   "Tempo de uso",                     GruposDeQuestoes.SaudeDaMulher),

            new("QUESTÃO 17]",   "Seção na Divisão de Enfermagem",   GruposDeQuestoes.Trabalho),
            new("QUESTÃO 18]",   "Vínculo com o CAISM",              GruposDeQuestoes.Trabalho),
            new("QUESTÃO 19]",   "Outra fonte de renda",             GruposDeQuestoes.Trabalho),
            new("QUESTÃO 23]",   "Trabalho doméstico por semana",    GruposDeQuestoes.Trabalho),
            new("QUESTÃO 24]",   "Início das últimas férias",        GruposDeQuestoes.Trabalho,
                ehDataSerial: true),
            new("QUESTÃO 25]",   "Duração das últimas férias",       GruposDeQuestoes.Trabalho,
                formatoNumerico: "N0", sufixo: " dias"),

            new("QUESTÃO 20]",   "Atividade física",                 GruposDeQuestoes.Habitos),
            new("QUESTÃO 21]",   "Tabagismo",                        GruposDeQuestoes.Habitos),
            new("QUESTÃO 22]",   "Bebida alcoólica",                 GruposDeQuestoes.Habitos),
            new("QUESTÃO 34]",   "Consumo de café",                  GruposDeQuestoes.Habitos),
            new("QUESTÃO 35]",   "Xícaras de café por dia",          GruposDeQuestoes.Habitos,
                formatoNumerico: "N0"),

            new("QUESTÃO 26]",   "Uso contínuo de medicamentos",     GruposDeQuestoes.Saude),
            new("QUESTÃO 27]",   "Doença crônica diagnosticada",     GruposDeQuestoes.Saude),
            new("QUESTÃO 28]",   "Anomalia de visão",                GruposDeQuestoes.Saude),
            new("QUESTÃO 30]",   "Outras condições diagnosticadas",  GruposDeQuestoes.Saude),

            new("QUESTÃO 31]",   "Medicamento para dormir",          GruposDeQuestoes.Sono),
            new("QUESTÃO 32]",   "Frequência de uso",                GruposDeQuestoes.Sono),
            new("QUESTÃO 33]",   "Há quanto tempo usa",              GruposDeQuestoes.Sono),

            new("QUESTÃO 36]",   "Imersão anterior em RV com HMD",   GruposDeQuestoes.ExperienciaEmRv),
        };

        /// <summary>
        /// Planilha <c>c_percepção_ambiental</c>: percepção da sala de descanso. As colunas não são
        /// numeradas, então o trecho discriminante vem do próprio enunciado.
        /// </summary>
        public static readonly DeParaDeQuestao[] PercepcaoAmbiental =
        {
            new("costuma fazer durante seu tempo",  "O que faz na sala",              GruposDeQuestoes.UsoDaSala),
            new("Quanto tempo você passa",          "Tempo de permanência",           GruposDeQuestoes.UsoDaSala),

            new("sensação térmica",                 "Sensação térmica",               GruposDeQuestoes.ConfortoAmbiental),
            new("percebe a luz do Sol",             "Luz do Sol",                     GruposDeQuestoes.ConfortoAmbiental),
            new("iluminação artificial",            "Iluminação artificial",          GruposDeQuestoes.ConfortoAmbiental),
            new("as janelas da sala",               "Influência das janelas",         GruposDeQuestoes.ConfortoAmbiental),
            new("Quais cheiros",                    "Cheiros percebidos",             GruposDeQuestoes.ConfortoAmbiental),
            new("Quais barulhos",                   "Barulhos percebidos",            GruposDeQuestoes.ConfortoAmbiental),

            new("passagem do tempo",                "Percebe a passagem do tempo",    GruposDeQuestoes.ContatoComExterno),
            new("como está o tempo lá fora",        "Percebe o tempo lá fora",        GruposDeQuestoes.ContatoComExterno),
            new("conectado ao céu",                 "Sensação de conexão com o céu",  GruposDeQuestoes.ContatoComExterno),

            new("presença de plantas",              "Plantas e natureza no CAISM",    GruposDeQuestoes.Natureza),
            new("contato visual com elementos",     "Vista de natureza pela janela",  GruposDeQuestoes.Natureza),
            new("em relação às áreas verdes",       "Relação com as áreas verdes",    GruposDeQuestoes.Natureza),
        };

        /// <summary>
        /// Localiza a entrada do de-para correspondente a um cabeçalho, ou null.
        /// </summary>
        public static DeParaDeQuestao Localizar(string cabecalho, DeParaDeQuestao[] tabela)
        {
            if (string.IsNullOrWhiteSpace(cabecalho))
                return null;

            return (tabela ?? Array.Empty<DeParaDeQuestao>()).FirstOrDefault(
                entrada => cabecalho.IndexOf(entrada.TrechoDoCabecalho,
                                             StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Traduz o cabeçalho e a resposta de uma coluna para exibição.
        /// </summary>
        /// <param name="cabecalho">Cabeçalho como está na planilha, já sem o prefixo da aba.</param>
        /// <param name="valorBruto">Resposta como está na célula.</param>
        /// <param name="tabela">De-para da planilha correspondente.</param>
        /// <returns>
        /// O rótulo curto, o grupo e a resposta formatada; ou o próprio cabeçalho,
        /// <see cref="GruposDeQuestoes.Outros"/> e a resposta intacta se nada corresponder.
        /// </returns>
        public static (string Rotulo, string Grupo, string Valor) Traduzir(
            string cabecalho, string valorBruto, DeParaDeQuestao[] tabela)
        {
            var entrada = Localizar(cabecalho, tabela);

            return entrada != null
                ? (entrada.RotuloCurto, entrada.Grupo, entrada.FormatarResposta(valorBruto))
                : (cabecalho?.Trim(), GruposDeQuestoes.Outros, valorBruto);
        }
    }

    /// <summary>
    /// Rótulos dos 14 sintomas de Sick Building Syndrome (questões 37 e 38).
    /// </summary>
    /// <remarks>
    /// Os sintomas não precisam de de-para: o nome de cada um já está entre colchetes no fim do
    /// cabeçalho. O que sobra do enunciado — "Nos últimos seis (6) meses, você teve algum dos
    /// sintomas relacionados abaixo? Por favor, assinale a frequência" — é <b>idêntico nas catorze
    /// linhas</b>, e por isso sobe para o cabeçalho do grupo em vez de ser repetido em cada uma.
    /// </remarks>
    public static class RotulosDeSintomas
    {
        /// <summary>Complemento do título do grupo, comum às catorze linhas.</summary>
        public const string PeriodoDeReferencia = "nos últimos seis (6) meses";

        /// <summary>Cabeçalho da coluna de frequência (Q37).</summary>
        public const string CabecalhoDeFrequencia = "FREQUÊNCIA";

        /// <summary>Cabeçalho da coluna de atribuição ao trabalho (Q38).</summary>
        public const string CabecalhoDeAtribuicao = "É DO TRABALHO?";

        /// <summary>
        /// Extrai o nome do sintoma do cabeçalho e o devolve em caixa de frase.
        /// </summary>
        /// <remarks>
        /// A planilha grava os sintomas em caixa alta ("DIFICULDADE DE CONCENTRAÇÃO"). Catorze
        /// linhas em caixa alta cansam a leitura e não acrescentam informação — o instrumento não
        /// distingue nada por caixa. Sem colchetes no cabeçalho, o texto volta como está.
        /// </remarks>
        public static string Extrair(string cabecalhoDaQuestao)
        {
            var correspondencia = System.Text.RegularExpressions.Regex.Match(
                cabecalhoDaQuestao ?? string.Empty, @"\[([^\]]+)\]");

            if (!correspondencia.Success)
                return cabecalhoDaQuestao;

            string sintoma = correspondencia.Groups[1].Value.Trim();

            return EmCaixaDeFrase(sintoma);
        }

        /// <summary>
        /// Normaliza a frequência declarada ("OCASIONAL", "frEQUENTE") para caixa de frase.
        /// </summary>
        /// <remarks>
        /// Pelo mesmo motivo dos rótulos: a caixa alta é do arquivo, não do instrumento, e o
        /// arquivo real já mistura "FREQUENTE" com "frEQUENTE". A escala em si continua sendo lida
        /// pela <see cref="EscalaOrdinalDoSbs"/>, que normaliza por conta própria — esta função é
        /// só de exibição.
        /// </remarks>
        public static string NormalizarFrequencia(string resposta)
        {
            return string.IsNullOrWhiteSpace(resposta) ? resposta : EmCaixaDeFrase(resposta.Trim());
        }

        private static string EmCaixaDeFrase(string texto)
        {
            return string.IsNullOrEmpty(texto)
                ? texto
                : char.ToUpperInvariant(texto[0]) + texto.Substring(1).ToLowerInvariant();
        }
    }
}
