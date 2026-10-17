using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Revit
{
    /// <summary>
    /// Gerencia a carga e inserção de instâncias da família <c>hitpoint.rfa</c> no modelo Revit,
    /// posicionando um hitpoint para cada <see cref="Fixacao"/> detectada em um cenário,
    /// parametrizando cada instância com os dados da fixação correspondente.
    /// </summary>
    /// <remarks>
    /// Mapeamento de coordenadas (tratado por <see cref="ConversorDeCoordenadas"/>):
    ///   Unity X → Revit X (horizontal)
    ///   Unity Z → Revit Y (horizontal)
    ///   Unity Y → Revit Z (elevação / Base Offset)
    ///
    /// Parâmetros preenchidos em cada instância:
    ///   "Cenário"                   → nome do cenário VR
    ///   "Raio"                      → constante <see cref="ConfiguracaoDaFamiliaHitpoint.RaioFixo"/>
    ///   "Dilatação Média da Pupila" → média bienocular do diâmetro pupilar (mm)
    ///   "Duração do FIxation"       → duração em ms
    ///
    /// A diferenciação visual entre hitpoints é feita por COR e não por tamanho (Quadro 40):
    /// verde para objetos de potencial Alto, vermelho para Baixo, cinza sem pontuação. A cor vem
    /// do MATERIAL do tipo da família: para cada fixação escolhe-se o <see cref="FamilySymbol"/>
    /// correspondente à sua classificação. Por ser propriedade do elemento, a cor vale em
    /// qualquer vista e estilo visual, aparece em tabelas e filtros e sobrevive à exportação —
    /// ao contrário de um override de vista.
    /// "Duração do FIxation" e "Dilatação Média da Pupila" seguem gravados como metadados
    /// informativos, mas não determinam mais o tamanho da esfera.
    ///
    /// <b>Todos os métodos de inserção devem ser chamados dentro de uma Transaction ativa.</b>
    /// <see cref="CarregarOuRecarregarFamilia"/> é a exceção: roda FORA de transação.
    /// </remarks>
    public class InsersorDaFamiliaHitpoint
    {
        private readonly Document _documento;
        private readonly ConversorDeCoordenadas _conversor;

        /// <summary>
        /// Inicializa o insersor com o documento Revit ativo e o conversor de coordenadas.
        /// </summary>
        public InsersorDaFamiliaHitpoint(Document documento, ConversorDeCoordenadas conversor)
        {
            _documento = documento;
            _conversor = conversor;
        }

        // ── Carga da família ─────────────────────────────────────────────

        /// <summary>
        /// Carrega ou recarrega a família <c>hitpoint.rfa</c> a partir do caminho fornecido,
        /// sobrescrevendo qualquer versão já presente no documento.
        /// </summary>
        /// <param name="caminhoDoArquivoRfa">Caminho completo do arquivo .rfa no disco.</param>
        /// <returns>
        /// Os <see cref="FamilySymbol"/> da família indexados pelo nome do tipo. Ainda NÃO ativados
        /// — a ativação exige transação e fica em <see cref="AtivarSimbolos"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Se o arquivo não existir, se a carga falhar, se a família não contiver tipos, ou se
        /// faltar algum dos tipos por faixa de potencial que o plugin espera.
        /// </exception>
        /// <remarks>
        /// <b>Deve ser chamado dentro de uma Transaction ativa</b>, junto com
        /// <see cref="AtivarSimbolos"/> — é o arranjo da main, o único com histórico de funcionar em
        /// máquinas diferentes.
        ///
        /// Usa <see cref="OpcoesDeCargaDaFamilia"/> para forçar a sobrescrita mesmo quando
        /// a família está em uso, garantindo que o modelo sempre receba a versão do build.
        ///
        /// A validação dos tipos esperados é deliberadamente rígida: sem ela, uma família
        /// desatualizada faria o plugin inserir todas as esferas com o mesmo tipo — e, portanto,
        /// na mesma cor — sem nenhum sinal de erro.
        /// </remarks>
        public Dictionary<string, FamilySymbol> CarregarOuRecarregarFamilia(string caminhoDoArquivoRfa)
        {
            if (!System.IO.File.Exists(caminhoDoArquivoRfa))
                throw new InvalidOperationException(
                    $"Arquivo da família não encontrado: {caminhoDoArquivoRfa}");

            bool carregou = _documento.LoadFamily(
                caminhoDoArquivoRfa, new OpcoesDeCargaDaFamilia(), out Family familia);

            // Quando a família já existe no documento, o Revit chama OnFamilyFound e frequentemente
            // devolve false com familia=null, mesmo tendo sobrescrito. Nesse caso os tipos são
            // procurados no próprio documento.
            var simbolos = familia != null
                ? ColetarSimbolosDaFamilia(familia)
                : LocalizarSimbolosDaFamiliaNoDocumento();

            if (simbolos.Count == 0)
                throw new InvalidOperationException(
                    MontarDiagnosticoDeCargaSemTipos(caminhoDoArquivoRfa, carregou, familia != null));

            ValidarTiposEsperados(simbolos, caminhoDoArquivoRfa);

            return simbolos;
        }

        /// <summary>
        /// Ativa os símbolos que ainda não estão ativos, condição para inserir instâncias deles.
        /// </summary>
        /// <remarks>
        /// <b>Deve ser chamado dentro de uma Transaction ativa</b> — separado de
        /// <see cref="CarregarOuRecarregarFamilia"/> justamente porque aquele NÃO pode rodar dentro
        /// de uma.
        /// </remarks>
        public void AtivarSimbolos(IReadOnlyDictionary<string, FamilySymbol> simbolos)
        {
            if (simbolos == null)
                return;

            foreach (var simbolo in simbolos.Values)
                AtivarSimboloSeNecessario(simbolo);
        }

        private Dictionary<string, FamilySymbol> ColetarSimbolosDaFamilia(Family familia)
        {
            var simbolos = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in familia.GetFamilySymbolIds())
            {
                if (_documento.GetElement(id) is FamilySymbol simbolo)
                    simbolos[simbolo.Name] = simbolo;
            }

            return simbolos;
        }

        /// <summary>
        /// Confere que a família contém todos os tipos por faixa de potencial esperados pelo
        /// plugin e lança uma exceção descritiva se algum faltar.
        /// </summary>
        /// <param name="simbolos">Tipos encontrados, indexados pelo nome.</param>
        /// <param name="caminhoDoArquivoRfa">Caminho do .rfa que o plugin tentou carregar.</param>
        /// <remarks>
        /// A mensagem carrega o CAMINHO porque "quais tipos faltam" não basta para achar a causa:
        /// sem ele não se sabe qual dos vários <c>hitpoint.rfa</c> do disco foi lido — uma cópia
        /// antiga na pasta de saída do build produz exatamente este erro com o arquivo de trabalho
        /// intacto.
        /// </remarks>
        private static void ValidarTiposEsperados(
            Dictionary<string, FamilySymbol> simbolos,
            string caminhoDoArquivoRfa)
        {
            var ausentes = ClassificadorDePotencialDeHitpoint.ObterNomesDeTodosOsTipos()
                .Where(nome => !simbolos.ContainsKey(nome))
                .ToList();

            if (!ausentes.Any())
                return;

            throw new InvalidOperationException(
                $"A família '{ObterNomeDaFamilia()}' não contém o(s) tipo(s): " +
                $"{string.Join(", ", ausentes)}. " +
                $"Tipos encontrados: {string.Join(", ", simbolos.Keys.OrderBy(n => n))}." +
                Environment.NewLine + Environment.NewLine +
                $"Arquivo carregado: {caminhoDoArquivoRfa}" +
                Environment.NewLine + Environment.NewLine +
                "Cada tipo representa uma classificação do Quadro 41 e deve ter seu próprio " +
                "material: verde para 'Potencial Restaurador', vermelho para 'Potencial Estressor' " +
                "e cinza para 'Potencial Neutro'. Se o arquivo acima já tem os três, o modelo ficou " +
                $"com uma versão antiga da família: remova '{ObterNomeDaFamilia()}' pelo Project " +
                "Browser (Famílias → Modelos Genéricos) e gere de novo.");
        }

        // ── Inserção de hitpoints ─────────────────────────────────────────

        /// <summary>
        /// Insere uma instância de hitpoint para cada <see cref="Fixacao"/> do conjunto fornecido,
        /// posicionando-a no ponto médio euclidiano das amostras da fixação e parametrizando
        /// os campos de cenário, raio, dilatação pupilar e duração.
        /// </summary>
        /// <param name="fixacoes">Fixações do cenário (todas as sessões, sem filtro de indivíduo).</param>
        /// <param name="nomeDoCenario">Nome do cenário VR — gravado no parâmetro "Cenário".</param>
        /// <param name="simbolosPorTipo">
        /// Símbolos da família indexados pelo nome do tipo, obtidos por
        /// <see cref="CarregarOuRecarregarFamilia"/>.
        /// </param>
        /// <param name="classificacoesPorObjeto">
        /// Classificação de potencial por nome de objeto
        /// (<see cref="Dominio.MetricasPorObjeto.ClassificacaoPH"/>), usada para escolher o tipo de
        /// cada esfera. Pode ser null ou vazio — nesse caso todas as esferas recebem o tipo
        /// <see cref="ConfiguracaoDaFamiliaHitpoint.NomeDoTipoPotencialNeutro"/>.
        /// </param>
        /// <param name="avisos">
        /// Lista populada com mensagens para fixações cujos parâmetros não puderam ser definidos.
        /// </param>
        /// <returns>Lista das instâncias inseridas com sucesso.</returns>
        /// <remarks>
        /// <b>Deve ser chamado dentro de uma Transaction ativa.</b>
        /// </remarks>
        public List<FamilyInstance> InserirHitpointsDoCenario(
            IEnumerable<Fixacao> fixacoes,
            string nomeDoCenario,
            IReadOnlyDictionary<string, FamilySymbol> simbolosPorTipo,
            IReadOnlyDictionary<string, ClassificacaoDePotencial> classificacoesPorObjeto,
            List<string> avisos)
        {
            var instanciasInseridas = new List<FamilyInstance>();
            int indice = 0;

            foreach (var fixacao in fixacoes)
            {
                indice++;
                try
                {
                    string nomeDoTipo = ClassificadorDePotencialDeHitpoint.ResolverNomeDoTipoDaFixacao(
                        fixacao.NomesDeObjetos, classificacoesPorObjeto);

                    if (!simbolosPorTipo.TryGetValue(nomeDoTipo, out var simbolo))
                    {
                        // Não deveria ocorrer: CarregarOuRecarregarFamilia valida os tipos antes.
                        avisos.Add($"[{nomeDoCenario}] Fixação #{indice}: tipo '{nomeDoTipo}' " +
                                   "não encontrado na família — hitpoint não inserido.");
                        continue;
                    }

                    var instancia = TentarInserirHitpointDaFixacao(fixacao, nomeDoCenario, simbolo, indice, avisos);
                    if (instancia != null)
                        instanciasInseridas.Add(instancia);
                }
                catch (Exception ex)
                {
                    avisos.Add($"[{nomeDoCenario}] Fixação #{indice}: exceção ao inserir — {ex.Message}");
                }
            }

            return instanciasInseridas;
        }

        // ── Implementação privada ─────────────────────────────────────────

        private FamilyInstance TentarInserirHitpointDaFixacao(
            Fixacao fixacao,
            string nomeDoCenario,
            FamilySymbol simbolo,
            int indice,
            List<string> avisos)
        {
            // Ponto médio euclidiano de todas as amostras da fixação, convertido para Revit.
            // A captura já vem em convenção Z para cima, a mesma do Revit: os eixos são
            // preservados e só a unidade muda, de metros para pés. Ver ConversorDeCoordenadas
            // para a evidência que estabeleceu a convenção do arquivo.
            XYZ pontoRevit = _conversor.ConverterParaRevit(
                fixacao.PontoMedioDeIncidenciaX,
                fixacao.PontoMedioDeIncidenciaY,
                fixacao.PontoMedioDeIncidenciaZ);

            var instancia = _documento.Create.NewFamilyInstance(
                pontoRevit,
                simbolo,
                StructuralType.NonStructural);

            DefinirParametros(instancia, fixacao, nomeDoCenario, indice, avisos);
            return instancia;
        }

        private void DefinirParametros(
            FamilyInstance instancia,
            Fixacao fixacao,
            string nomeDoCenario,
            int indice,
            List<string> avisos)
        {
            DefinirParametroTexto(instancia,
                ConfiguracaoDaFamiliaHitpoint.NomeDoParametroCenario,
                nomeDoCenario,
                indice, avisos);

            // Raio constante: a diferenciação visual é por cor (Quadro 40), não por tamanho.
            DefinirParametroNumero(instancia,
                ConfiguracaoDaFamiliaHitpoint.NomeDoParametroRaio,
                ConfiguracaoDaFamiliaHitpoint.RaioFixo,
                indice, avisos);

            double dilatacao = CalcularDilatacaoMedia(
                fixacao.DiametroMedianoPupilaEsquerda,
                fixacao.DiametroMedianoPupilaDireita);

            if (!double.IsNaN(dilatacao))
                DefinirParametroNumero(instancia,
                    ConfiguracaoDaFamiliaHitpoint.NomeDoParametroDilatacaoPupila,
                    dilatacao,
                    indice, avisos);
            else
                avisos.Add($"[{nomeDoCenario}] Fixação #{indice}: sem dado pupilar válido — " +
                           $"'{ConfiguracaoDaFamiliaHitpoint.NomeDoParametroDilatacaoPupila}' não preenchido.");

            DefinirParametroNumero(instancia,
                ConfiguracaoDaFamiliaHitpoint.NomeDoParametroDuracaoFixation,
                fixacao.DuracaoEmMilissegundos,
                indice, avisos);
        }

        private void DefinirParametroTexto(
            FamilyInstance instancia,
            string nomeParametro,
            string valor,
            int indice,
            List<string> avisos)
        {
            var param = instancia.LookupParameter(nomeParametro);
            if (param == null || param.IsReadOnly)
            {
                avisos.Add($"Fixação #{indice}: parâmetro '{nomeParametro}' não encontrado ou somente leitura.");
                return;
            }
            param.Set(valor);
        }

        private void DefinirParametroNumero(
            FamilyInstance instancia,
            string nomeParametro,
            double valor,
            int indice,
            List<string> avisos)
        {
            var param = instancia.LookupParameter(nomeParametro);
            if (param == null || param.IsReadOnly)
            {
                avisos.Add($"Fixação #{indice}: parâmetro '{nomeParametro}' não encontrado ou somente leitura.");
                return;
            }
            param.Set(valor);
        }

        // ── Cálculos auxiliares ──────────────────────────────────────────

        /// <summary>
        /// Calcula a dilatação média bienocular da pupila.
        /// Retorna NaN se ambos os valores forem NaN ou inválidos (≤ 0).
        /// </summary>
        private static double CalcularDilatacaoMedia(double esquerda, double direita)
        {
            bool esqValido = !double.IsNaN(esquerda) && esquerda > 0;
            bool dirValido = !double.IsNaN(direita)  && direita  > 0;

            if (esqValido && dirValido) return (esquerda + direita) / 2.0;
            if (esqValido)             return esquerda;
            if (dirValido)             return direita;
            return double.NaN;
        }

        /// <summary>
        /// Nome que a família tem no documento. O Revit a batiza pelo nome do arquivo carregado,
        /// então <c>hitpoint.rfa</c> vira a família <c>hitpoint</c>.
        /// </summary>
        private static string ObterNomeDaFamilia()
        {
            return System.IO.Path.GetFileNameWithoutExtension(
                ConfiguracaoDaFamiliaHitpoint.NomeDoArquivoDaFamilia);
        }

        /// <summary>
        /// Monta a mensagem do caso em que nenhum tipo foi obtido: o Revit não carregou o arquivo e
        /// tampouco há a família no documento.
        /// </summary>
        /// <remarks>
        /// Os três dados extras não são enfeite: o <b>build do Revit</b> separa "o .rfa veio de um
        /// update mais novo" de "o arquivo está corrompido"; o <b>retorno do LoadFamily</b> e o
        /// <b>familia != null</b> separam "o Revit recusou o arquivo" de "o Revit carregou mas não
        /// devolveu a família, e a busca no documento não a achou" — falhas com a mesma aparência e
        /// correções opostas. Sem eles o erro custa uma sessão de investigação.
        /// </remarks>
        private string MontarDiagnosticoDeCargaSemTipos(
            string caminhoDoArquivoRfa, bool carregou, bool familiaFoiDevolvida)
        {
            return
                "Não foi possível obter nenhum tipo (FamilySymbol) da família " +
                $"'{ObterNomeDaFamilia()}'." +
                Environment.NewLine + Environment.NewLine +
                $"Arquivo: {caminhoDoArquivoRfa}" + Environment.NewLine +
                $"Revit em uso: {_documento.Application.VersionName} " +
                $"(build {_documento.Application.VersionBuild})" + Environment.NewLine +
                $"LoadFamily devolveu: {(carregou ? "sucesso" : "falha")}" + Environment.NewLine +
                $"Family devolvida: {(familiaFoiDevolvida ? "sim" : "não")}" +
                Environment.NewLine + Environment.NewLine +
                "A causa mais comum é o .rfa ter sido salvo num update MAIS NOVO do Revit do que o " +
                "instalado — o Revit recusa esses arquivos em silêncio. Abra o .rfa no Revit acima " +
                "e salve-o por cima de Assets\\hitpoint.rfa, ou atualize o Revit.";
        }

        /// <summary>
        /// Recuperação usada quando <see cref="Document.LoadFamily(string, IFamilyLoadOptions, out Family)"/>
        /// não devolve a família: procura no documento todos os tipos cujo nome de família
        /// corresponda ao arquivo esperado.
        /// </summary>
        private Dictionary<string, FamilySymbol> LocalizarSimbolosDaFamiliaNoDocumento()
        {
            string nomeDaFamilia = ObterNomeDaFamilia();

            var simbolos = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);

            // A comparação é insensível a maiúsculas: o Revit preserva a grafia com que a família
            // foi salva, e uma família gravada como "Hitpoint" deixaria de ser encontrada por uma
            // igualdade ordinal — falha silenciosa que se manifestaria como "nenhum tipo".
            var encontrados = new FilteredElementCollector(_documento)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(s => string.Equals(s.FamilyName, nomeDaFamilia,
                                          StringComparison.OrdinalIgnoreCase));

            foreach (var simbolo in encontrados)
                simbolos[simbolo.Name] = simbolo;

            return simbolos;
        }

        private static void AtivarSimboloSeNecessario(FamilySymbol simbolo)
        {
            if (!simbolo.IsActive)
                simbolo.Activate();
        }

        // ── Opções de carga da família ───────────────────────────────────

        /// <summary>
        /// Implementação de <see cref="IFamilyLoadOptions"/> que sempre sobrescreve
        /// a família no documento, mesmo que esteja em uso, garantindo que a versão
        /// distribuída com o plugin (build) seja a que está no modelo.
        /// </summary>
        private sealed class OpcoesDeCargaDaFamilia : IFamilyLoadOptions
        {
            /// <summary>
            /// Chamado quando a família já existe no documento.
            /// Retorna true (sobrescrever) e mantém os valores de parâmetros existentes.
            /// </summary>
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false; // preserva valores já definidos nos hitpoints
                return true;                      // mas atualiza a geometria/definição da família
            }

            /// <summary>
            /// Chamado quando a família compartilhada já existe no documento.
            /// Força recarregamento a partir do arquivo da família.
            /// </summary>
            public bool OnSharedFamilyFound(
                Family sharedFamily,
                bool familyInUse,
                out FamilySource source,
                out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = false;
                return true;
            }
        }
    }
}
