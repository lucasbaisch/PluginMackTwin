using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Interface.Modelos
{
    /// <summary>
    /// Um bloco recolhível da aba de Dados do Participante: um grupo temático de questões com o
    /// seu ícone, o seu cabeçalho e as suas linhas.
    /// </summary>
    /// <remarks>
    /// <b>Por que substituiu a tabela plana.</b> A aba exibia uma linha por questão, com o
    /// enunciado inteiro no lugar do rótulo — 33 linhas de texto corrido só em a_recrutamento, em
    /// que a resposta, que é o que se quer ler, ficava encoberta pela pergunta. O grupo faz três
    /// coisas que a tabela não fazia: encurta o rótulo (o de-para), tira do rótulo o que é comum a
    /// todas as linhas e sobe para o cabeçalho, e permite recolher o que não está em uso.
    ///
    /// <b>O que sobe para o cabeçalho.</b> Nas questões 37 e 38 o enunciado repetia "Nos últimos
    /// seis (6) meses…" catorze vezes. Esse trecho virou o <see cref="Complemento"/> do título, e
    /// "frequência" e "é em decorrência do trabalho" viraram os cabeçalhos das duas colunas de
    /// valor — a informação continua na tela, uma vez em vez de vinte e oito.
    /// </remarks>
    public class GrupoDeFormularioParaTela
    {
        // A constante LimiteDeItensParaNascerAberto foi REMOVIDA junto com o Expander: ela decidia
        // quais grupos nasciam recolhidos, e não há mais grupo recolhido — todos ficam abertos.

        /// <summary>Pictograma do grupo.</summary>
        public Geometry Icone { get; init; }

        /// <summary>Nome do grupo, exibido em destaque no cabeçalho.</summary>
        public string Titulo { get; init; }

        /// <summary>
        /// Trecho comum a todas as linhas, exibido em tom secundário ao lado do título.
        /// Vazio quando não há nada a fatorar.
        /// </summary>
        public string Complemento { get; init; } = string.Empty;

        /// <summary>Se o <see cref="Complemento"/> deve ocupar espaço na tela.</summary>
        public bool TemComplemento => !string.IsNullOrWhiteSpace(Complemento);

        /// <summary>Cabeçalho da coluna de valores. Vazio esconde a faixa de cabeçalho.</summary>
        public string CabecalhoDeValor { get; init; } = string.Empty;

        /// <summary>Cabeçalho da segunda coluna de valores, usada apenas pelos sintomas.</summary>
        public string CabecalhoDeComplemento { get; init; } = string.Empty;

        /// <summary>Se as linhas deste grupo exibem a segunda coluna de valores.</summary>
        public bool MostrarSegundoValor { get; init; }

        /// <summary>Se a faixa de cabeçalho das colunas deve aparecer.</summary>
        public bool MostrarCabecalhoDeColunas => !string.IsNullOrWhiteSpace(CabecalhoDeValor);

        // A propriedade Expandido foi REMOVIDA junto com o Expander do bloco: os grupos passaram a
        // ficar sempre abertos, e não há mais estado de aberto/fechado a governar.

        /// <summary>
        /// O que o grupo mede, exibido em linha própria sob o título. Vazio nos grupos que não
        /// têm descrição — hoje só os cinco domínios do IEEE a possuem.
        /// </summary>
        public string Descricao { get; init; } = string.Empty;

        /// <summary>Se há descrição a exibir.</summary>
        public bool TemDescricao => !string.IsNullOrWhiteSpace(Descricao);

        /// <summary>Linhas do grupo, na ordem de origem.</summary>
        public IReadOnlyList<LinhaDeFormularioParaTabela> Itens { get; init; }
            = new List<LinhaDeFormularioParaTabela>();

        /// <summary>Contagem exibida à direita do cabeçalho.</summary>
        public string Contagem => Itens.Count == 1 ? "1 item" : $"{Itens.Count} itens";

        /// <summary>
        /// Reúne as linhas de um formulário nos seus grupos temáticos, na ordem de
        /// <see cref="GruposDeQuestoes.Ordem"/>.
        /// </summary>
        /// <param name="linhas">Linhas já construídas por <see cref="LinhaDeFormularioParaTabela"/>.</param>
        /// <param name="formulario">
        /// Formulário selecionado, usado apenas para decidir os cabeçalhos das colunas de valor —
        /// só o de Sick Building Syndrome tem duas.
        /// </param>
        /// <remarks>
        /// A ordem DENTRO de cada grupo é a de chegada, que é a ordem das colunas na planilha. Um
        /// grupo com um único item continua sendo um grupo: quebrar a regra para economizar um
        /// cabeçalho faria "Experiência em RV" aparecer solta, sem o seu ícone e sem o seu nome.
        /// </remarks>
        public static List<GrupoDeFormularioParaTela> Agrupar(
            IEnumerable<LinhaDeFormularioParaTabela> linhas,
            TipoDeFormulario formulario)
        {
            var materializadas = linhas?.ToList() ?? new List<LinhaDeFormularioParaTabela>();

            if (!materializadas.Any())
                return new List<GrupoDeFormularioParaTela>();

            bool ehSbs = formulario == TipoDeFormulario.SindromeDoEdificioDoente;

            var agrupadas = materializadas
                .GroupBy(linha => linha.Grupo ?? GruposDeQuestoes.Outros)
                .OrderBy(grupo => GruposDeQuestoes.ObterPosicao(grupo.Key))
                .ToList();

            return agrupadas
                .Select(grupo =>
                {
                    var itens = grupo.ToList();

                    return new GrupoDeFormularioParaTela
                    {
                        Titulo      = grupo.Key,
                        Icone       = IconesDeGrupo.ObterPorGrupo(grupo.Key),
                        Itens       = itens,
                        Descricao   = GruposDeQuestoes.ObterDescricao(grupo.Key),
                        Complemento = ehSbs ? RotulosDeSintomas.PeriodoDeReferencia : string.Empty,

                        CabecalhoDeValor       = ehSbs ? RotulosDeSintomas.CabecalhoDeFrequencia : string.Empty,
                        CabecalhoDeComplemento = ehSbs ? RotulosDeSintomas.CabecalhoDeAtribuicao : string.Empty,
                        MostrarSegundoValor    = ehSbs,
                    };
                })
                .ToList();
        }
    }
}
