using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;
using PluginCAISM.Interface.Modelos;

namespace PluginCAISM.Interface.Exportacao
{
    /// <summary>
    /// Contexto de filtragem vigente quando a tabela foi exportada.
    /// </summary>
    /// <remarks>
    /// Vai impresso no cabeçalho porque a mesma tabela produz números diferentes conforme os
    /// filtros: restringir a sessão ou o participante muda o ESCOPO da estimativa, não só o
    /// recorte da exibição. Um PDF sem essa informação não é reproduzível.
    /// </remarks>
    public sealed class ContextoDaExportacao
    {
        /// <summary>Cenário selecionado, ou o rótulo de "todos".</summary>
        public string Cenario { get; init; }

        /// <summary>Sessão selecionada, ou o rótulo de "todas".</summary>
        public string Sessao { get; init; }

        // O campo Participante foi REMOVIDO junto com o dropdown correspondente da aba.

        /// <summary>Sessões que sobreviveram aos filtros e alimentaram a estimativa.</summary>
        public int QuantidadeDeSessoes { get; init; }

        /// <summary>
        /// Cenários efetivamente abrangidos pela exportação, quando o filtro está em "todos".
        /// </summary>
        /// <remarks>
        /// "Todos os Cenários" não diz QUAIS cenários entraram, e o leitor do PDF não tem como
        /// descobrir: dois relatórios com o mesmo rótulo podem cobrir conjuntos diferentes, se
        /// entre um e outro o usuário carregou outros arquivos. A lista resolve isso.
        /// </remarks>
        public IReadOnlyList<string> CenariosIncluidos { get; init; }

        /// <summary>
        /// Períodos do dia abrangidos pela exportação, quando o filtro de sessão está em "todas".
        /// Vazio quando a convenção de nome não permite identificá-los.
        /// </summary>
        public IReadOnlyList<string> PeriodosIncluidos { get; init; }

        /// <summary>
        /// Ressalvas do ajuste da Camada 1 na estimativa que produziu esta tabela.
        /// </summary>
        /// <remarks>
        /// Vão impressas porque um relatório arquivado é lido meses depois, fora do contexto em
        /// que foi gerado. Sem elas, o PDF afirma um índice sem dizer quais termos da Equação 3
        /// entraram no ajuste que o produziu — e dois relatórios com a mesma aparência podem vir
        /// de modelos diferentes.
        /// </remarks>
        public IReadOnlyList<string> AvisosDaCamada1 { get; init; }
    }

    /// <summary>
    /// Exporta a tabela de Potencial Homeodinâmico para PDF, com cabeçalho de identificação da
    /// pesquisa.
    /// </summary>
    public static class ExportadorDaTabelaDePotencial
    {
        private static readonly CultureInfo Cultura = CultureInfo.CurrentCulture;

        /// <summary>
        /// Colunas impressas — as MESMAS da tabela da aba, na mesma ordem.
        /// </summary>
        /// <remarks>
        /// O relatório e a tela têm de mostrar a mesma coisa: quem confere um contra o outro não
        /// deve encontrar coluna que só existe num dos dois. Sempre que uma coluna entrar ou sair
        /// do <c>DataGrid</c> em JanelaPluginCAISM.xaml, esta lista tem de acompanhar.
        ///
        /// Os títulos são os da tela, com duas exceções ditadas pelo formato: "PH (−1 a +1)" usa
        /// o hífen comum porque o sinal de menos U+2212 não existe em WinAnsi, e "Diâmetro pupilar"
        /// vira "Diâmetro (mm)" para caber na largura da coluna — a unidade, que na tela está
        /// implícita, aqui fica explícita.
        ///
        /// As larguras somam a largura útil da página A4 com as margens do documento (511 pt),
        /// contando 3 pt de intervalo entre colunas. Alterar uma exige recompor as demais.
        /// </remarks>
        private static readonly ColunaDoPdf[] Colunas =
        {
            new() { Titulo = "Elemento",       X =   0, Largura = 88 },
            new() { Titulo = "Cenário",        X =  91, Largura = 62 },
            new() { Titulo = "Potencial",      X = 156, Largura = 46 },
            new() { Titulo = "PH (-1 a +1)",   X = 205, Largura = 44, Alinhamento = AlinhamentoDaColuna.Direita },
            new() { Titulo = "Diâmetro (mm)",  X = 252, Largura = 54, Alinhamento = AlinhamentoDaColuna.Direita },
            new() { Titulo = "Temperatura",    X = 309, Largura = 50, Alinhamento = AlinhamentoDaColuna.Direita },
            new() { Titulo = "Umidade",        X = 362, Largura = 42, Alinhamento = AlinhamentoDaColuna.Direita },
            new() { Titulo = "Ruído",          X = 407, Largura = 44, Alinhamento = AlinhamentoDaColuna.Direita },
            // Alinhada à esquerda como "Potencial": a célula é rótulo — "DETECTADO" ou
            // "NÃO DETECTADO" —, e não número.
            new() { Titulo = "Presença de CO", X = 454, Largura = 57 },
        };

        /// <summary>Corpo da tabela: nove colunas não cabem em 9 pt na largura útil do A4.</summary>
        private const double TamanhoDaTabela = 7.5;

        /// <summary>Quantos elementos entram na lista dos mais observados.</summary>
        private const int QuantidadeDeElementosMaisObservados = 5;

        /// <summary>
        /// Gera o PDF no caminho indicado.
        /// </summary>
        /// <param name="linhas">Linhas da tabela, na ordem em que aparecem na tela.</param>
        /// <param name="contexto">Filtros vigentes, impressos no cabeçalho.</param>
        /// <param name="caminho">Arquivo de destino.</param>
        public static void Exportar(
            IReadOnlyList<LinhaDeObjetoParaTabela> linhas,
            ContextoDaExportacao contexto,
            string caminho)
        {
            var documento = new DocumentoPdf();

            EscreverCabecalho(documento);
            EscreverInformacoes(documento, contexto, linhas?.Count ?? 0);
            EscreverCondicoesDoAjuste(documento, contexto);
            EscreverAnalisePrevia(documento, linhas);
            EscreverTabela(documento, linhas);
            EscreverRodape(documento);

            documento.Salvar(caminho);
        }

        /// <summary>
        /// Identificação da pesquisa. Mantido inalterado: é o que dá procedência ao documento.
        /// </summary>
        private static void EscreverCabecalho(DocumentoPdf documento)
        {
            documento.EscreverLinha(CabecalhoDaExportacao.Universidade, 9);
            documento.EscreverParagrafo(CabecalhoDaExportacao.Faculdade, 8);
            documento.Espacar(6);

            documento.EscreverParagrafo(CabecalhoDaExportacao.TituloDaTese, 12, negrito: true);
            documento.Espacar(3);
            documento.EscreverLinha(CabecalhoDaExportacao.Autor, 9);
            documento.EscreverLinha(CabecalhoDaExportacao.Orientacao, 9);

            documento.LinhaHorizontal();
            documento.Espacar(4);

            documento.EscreverLinha(
                "Tabela de Potencial Homeodinâmico por elemento projetual", 12, negrito: true);
            documento.EscreverLinha(CabecalhoDaExportacao.NomeDoPlugin, 8);
            documento.Espacar(8);
        }

        /// <summary>
        /// Bloco INFORMAÇÕES: o recorte a que a tabela se refere.
        /// </summary>
        /// <remarks>
        /// Os rótulos "Todos os Cenários" e "Todas as Sessões" não bastam sozinhos, porque não
        /// dizem QUAIS entraram — e a mesma tabela produz números diferentes conforme o conjunto,
        /// já que restringir o escopo reestima ν̂_e, se(ν̂_e) e σ_ΔD. Por isso, quando o filtro está
        /// em "todos", o que foi efetivamente abrangido vai entre parênteses ao lado do rótulo.
        /// </remarks>
        private static void EscreverInformacoes(
            DocumentoPdf documento, ContextoDaExportacao contexto, int quantidadeDeLinhas)
        {
            documento.EscreverLinha("INFORMAÇÕES", 10, negrito: true);
            documento.Espacar(2);

            if (contexto != null)
            {
                documento.EscreverLinha(
                    $"Cenário: {Qualificar(contexto.Cenario, contexto.CenariosIncluidos)}", 9);
                documento.EscreverLinha(
                    $"Sessão: {Qualificar(contexto.Sessao, contexto.PeriodosIncluidos)}", 9);
                documento.EscreverLinha(
                    $"Sessões que alimentaram a estimativa: {contexto.QuantidadeDeSessoes}", 9);
            }

            documento.EscreverLinha($"Elementos na tabela: {quantidadeDeLinhas}", 9);
            documento.EscreverLinha(
                $"Exportado em {DateTime.Now.ToString("dd/MM/yyyy HH:mm", Cultura)}", 9);

            documento.Espacar(10);
        }

        /// <summary>
        /// Acrescenta ao rótulo, entre parênteses, o que ele abrange — nada quando a lista está
        /// vazia ou quando ela apenas repete o próprio rótulo (filtro em um item só).
        /// </summary>
        private static string Qualificar(string rotulo, IReadOnlyList<string> abrangidos)
        {
            if (abrangidos == null || abrangidos.Count == 0)
                return rotulo ?? "—";

            if (abrangidos.Count == 1 &&
                string.Equals(abrangidos[0], rotulo, StringComparison.OrdinalIgnoreCase))
                return rotulo;

            return $"{rotulo} ({string.Join("; ", abrangidos)})";
        }

        /// <summary>
        /// Bloco CONDIÇÕES DO AJUSTE: sob que modelo os índices desta tabela foram estimados.
        /// </summary>
        /// <remarks>
        /// Omitido quando não há ressalva alguma — o silêncio significa que a Camada 1 correu como
        /// especificada, e um bloco permanente dizendo isso treinaria o leitor a ignorá-lo.
        /// </remarks>
        private static void EscreverCondicoesDoAjuste(
            DocumentoPdf documento, ContextoDaExportacao contexto)
        {
            var avisos = contexto?.AvisosDaCamada1;

            if (avisos == null || avisos.Count == 0)
                return;

            documento.EscreverLinha("CONDIÇÕES DO AJUSTE (CAMADA 1)", 10, negrito: true);
            documento.Espacar(2);

            foreach (var aviso in avisos)
                documento.EscreverParagrafo("— " + aviso, 9);

            documento.Espacar(10);
        }

        /// <summary>
        /// Bloco ANÁLISE ESTATÍSTICA PRÉVIA: o retrato do conjunto antes da tabela linha a linha.
        /// </summary>
        /// <remarks>
        /// Responde de relance às duas perguntas que a tabela só responde depois de lida inteira —
        /// onde o olhar se concentrou e como as classificações se repartem.
        ///
        /// <b>A participação é sobre fixações, não sobre tempo.</b> Um elemento pode reter poucas
        /// fixações longas e aparecer abaixo de outro com muitas fixações curtas; a coluna de
        /// duração média da tela é que distingue os dois casos.
        ///
        /// Os "sem pontuação" são contados à parte, e não somados aos neutros: são ausência de
        /// estimativa, não resultado de análise. Omiti-los faria as três contagens não fecharem
        /// com o total de elementos da tabela, o que pareceria erro de cálculo.
        /// </remarks>
        private static void EscreverAnalisePrevia(
            DocumentoPdf documento, IReadOnlyList<LinhaDeObjetoParaTabela> linhas)
        {
            if (linhas == null || linhas.Count == 0)
                return;

            documento.EscreverLinha("ANÁLISE ESTATÍSTICA PRÉVIA", 10, negrito: true);
            documento.Espacar(2);

            long totalDeFixacoes = linhas.Sum(linha => (long)linha.QuantidadeDeFixacoes);

            documento.EscreverLinha(
                $"Elementos projetuais mais observados (participação nas {totalDeFixacoes} fixações):", 9);

            var maisObservados = linhas
                .OrderByDescending(linha => linha.QuantidadeDeFixacoes)
                .ThenBy(linha => linha.NomeDoObjeto, StringComparer.CurrentCultureIgnoreCase)
                .Take(QuantidadeDeElementosMaisObservados)
                .ToList();

            int posicao = 0;
            foreach (var linha in maisObservados)
            {
                posicao++;

                // Sem fixação alguma não há participação a informar: 0/0 seria NaN impresso.
                string participacao = totalDeFixacoes > 0
                    ? (100.0 * linha.QuantidadeDeFixacoes / totalDeFixacoes)
                        .ToString("N1", Cultura) + "%"
                    : "—";

                documento.EscreverLinha(
                    $"     {posicao}. {linha.NomeDoObjeto} — {participacao} " +
                    $"({linha.QuantidadeDeFixacoes} fixações)", 9);
            }

            documento.Espacar(4);

            int restauradores = ContarPor(linhas, ClassificacaoDePotencial.Restaurador);
            int neutros       = ContarPor(linhas, ClassificacaoDePotencial.Neutro);
            int estressores   = ContarPor(linhas, ClassificacaoDePotencial.Estressor);

            documento.EscreverLinha($"Quantidade de elementos Restauradores: {restauradores}", 9);
            documento.EscreverLinha($"Quantidade de elementos Neutros: {neutros}", 9);
            documento.EscreverLinha($"Quantidade de elementos Estressores: {estressores}", 9);

            documento.Espacar(10);
        }

        private static int ContarPor(
            IReadOnlyList<LinhaDeObjetoParaTabela> linhas, ClassificacaoDePotencial classificacao)
        {
            return linhas.Count(linha => linha.Classificacao == classificacao);
        }

        private static void EscreverTabela(
            DocumentoPdf documento, IReadOnlyList<LinhaDeObjetoParaTabela> linhas)
        {
            EscreverCabecalhoDaTabela(documento);

            if (linhas == null || linhas.Count == 0)
            {
                documento.Espacar(4);
                documento.EscreverParagrafo(
                    "Nenhum elemento foi estimado com os filtros vigentes. Nas sessões abaixo de " +
                    "dois minutos não há fixação detectada, porque a janela de detecção começa aos " +
                    "dois minutos de imersão.", 9);
                return;
            }

            foreach (var linha in linhas)
            {
                // Ao virar a página, o cabeçalho da tabela é repetido: sem isso as colunas
                // numéricas ficam sem identificação da segunda página em diante.
                if (documento.GarantirEspaco(14))
                    EscreverCabecalhoDaTabela(documento);

                // Temperatura, umidade, ruído e presença de CO já vêm formatados do modelo de
                // linha, com o travessão de "sem leitura" — a mesma célula que a tela exibe.
                documento.EscreverLinhaDeTabela(Colunas, new[]
                {
                    linha.NomeDoObjeto,
                    linha.Cenario,
                    linha.ClassificacaoRotulo,
                    Formatar(linha.Pontuacao, "N3"),
                    Formatar(linha.DiametroPupilarMm, "N3"),
                    linha.Temperatura,
                    linha.Umidade,
                    linha.Ruido,
                    linha.PresencaDeGas,
                }, TamanhoDaTabela);
            }
        }

        private static void EscreverCabecalhoDaTabela(DocumentoPdf documento)
        {
            documento.Espacar(2);
            documento.EscreverLinhaDeTabela(
                Colunas, Colunas.Select(coluna => coluna.Titulo).ToList(),
                TamanhoDaTabela, negrito: true);
            documento.LinhaHorizontal(0.8, 0.4);
        }

        private static void EscreverRodape(DocumentoPdf documento)
        {
            documento.Espacar(10);
            documento.LinhaHorizontal();
            documento.Espacar(2);
            documento.EscreverParagrafo(CabecalhoDaExportacao.NotaDeRodape, 8);
        }

        /// <summary>Formata um número, devolvendo travessão quando não há valor.</summary>
        private static string Formatar(double valor, string formato) =>
            double.IsNaN(valor) ? "—" : valor.ToString(formato, Cultura);
    }
}
