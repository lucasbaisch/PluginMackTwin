using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MackTwin.Dominio;

namespace MackTwin.Configuracao
{
    /// <summary>Uma planilha Google cadastrada como fonte de leituras.</summary>
    public sealed class PlanilhaRegistrada
    {
        /// <summary>Nome exibido na interface.</summary>
        public string Nome { get; set; } = string.Empty;

        /// <summary>Endereço da planilha — de edição ou de exportação; o leitor normaliza.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Se entra no ciclo de atualização. Desativar preserva o cadastro sem baixar.</summary>
        public bool Ativa { get; set; } = true;

        /// <summary>
        /// Código normalizado da sala em que a planilha foi cadastrada, ou null quando a sala é
        /// inferida pelos HOSTNAMEs dos sensores que ela traz.
        /// </summary>
        /// <remarks>
        /// É uma marca de CADASTRO, não um filtro: as leituras continuam sendo distribuídas pela
        /// sala codificada em cada HOSTNAME. Uma planilha marcada como LAB001 que traga um sensor
        /// MMCCTLAB002-001 alimenta LAB002 normalmente.
        /// </remarks>
        public string? Sala { get; set; }

        public override string ToString() => Nome;
    }

    /// <summary>Uma sala cadastrada à mão, antes de (ou sem) qualquer sensor anunciá-la.</summary>
    public sealed class SalaRegistrada
    {
        /// <summary>Código normalizado (ex.: LAB002) — o mesmo do Número do Room e do HOSTNAME.</summary>
        public string Codigo { get; set; } = string.Empty;

        /// <summary>Nome amigável opcional, exibido enquanto a sala não casa com um Room.</summary>
        public string? Nome { get; set; }

        public override string ToString() => Codigo;
    }

    /// <summary>
    /// Cadastro das planilhas que alimentam o painel e das salas criadas à mão, persistido em
    /// JSON na pasta de dados do usuário (<see cref="ConfiguracaoDeArmazenamento"/>).
    /// </summary>
    /// <remarks>
    /// Começa com a planilha do DHT22 e cresce pela própria interface do plug-in, sala por sala.
    /// Uma sala normalmente nasce do HOSTNAME de um sensor; cadastrá-la à mão serve para
    /// prepará-la antes de o sensor existir — e para que o cadastro das planilhas dela tenha
    /// onde morar. Usa <c>System.Text.Json</c>, que já vem no .NET 8: nenhum pacote novo.
    ///
    /// A leitura é tolerante: arquivo ausente, vazio ou corrompido devolve o cadastro padrão e
    /// registra um aviso, em vez de derrubar a abertura da janela. O cadastro em disco só é
    /// sobrescrito por uma ação explícita do usuário.
    /// </remarks>
    public sealed class RegistroDePlanilhas
    {
        private static readonly JsonSerializerOptions Opcoes = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>Todas as planilhas cadastradas, ativas ou não, na ordem de cadastro.</summary>
        public List<PlanilhaRegistrada> Planilhas { get; set; } = new();

        /// <summary>Salas cadastradas à mão, na ordem de cadastro.</summary>
        public List<SalaRegistrada> Salas { get; set; } = new();

        /// <summary>As planilhas que entram no ciclo de atualização.</summary>
        [JsonIgnore]
        public IEnumerable<PlanilhaRegistrada> Ativas =>
            Planilhas.Where(p => p.Ativa && !string.IsNullOrWhiteSpace(p.Url));

        /// <summary>Cadastro inicial: só a planilha do DHT22, sem sala marcada (inferida pelos HOSTNAMEs).</summary>
        public static RegistroDePlanilhas Padrao()
        {
            return new RegistroDePlanilhas
            {
                Planilhas =
                {
                    new PlanilhaRegistrada
                    {
                        Nome = ConfiguracaoDaPlanilhaGoogle.NomePadrao,
                        Url = ConfiguracaoDaPlanilhaGoogle.UrlPadraoDeEdicao,
                    },
                },
            };
        }

        /// <summary>
        /// Carrega o cadastro do disco; na ausência ou em caso de erro, devolve o padrão.
        /// </summary>
        /// <param name="avisos">Recebe a explicação quando o arquivo não pôde ser usado. Pode ser null.</param>
        public static RegistroDePlanilhas Carregar(List<string>? avisos = null)
        {
            string caminho = ConfiguracaoDeArmazenamento.CaminhoDoArquivoDePlanilhas;

            try
            {
                if (!File.Exists(caminho))
                    return Padrao();

                string json = File.ReadAllText(caminho);
                var registro = JsonSerializer.Deserialize<RegistroDePlanilhas>(json, Opcoes);

                if (registro == null || (registro.Planilhas.Count == 0 && registro.Salas.Count == 0))
                {
                    avisos?.Add($"Cadastro de planilhas vazio em {caminho}; usando o padrão.");
                    return Padrao();
                }

                foreach (var planilha in registro.Planilhas)
                    planilha.Sala = NormalizarSala(planilha.Sala);

                registro.Salas = registro.Salas
                    .Select(sala => new SalaRegistrada { Codigo = NormalizarSala(sala.Codigo) ?? string.Empty, Nome = sala.Nome })
                    .Where(sala => sala.Codigo.Length > 0)
                    .GroupBy(sala => sala.Codigo, StringComparer.Ordinal)
                    .Select(grupo => grupo.First())
                    .ToList();

                return registro;
            }
            catch (Exception excecao)
            {
                avisos?.Add($"Não foi possível ler {caminho} ({excecao.Message}); usando o cadastro padrão.");
                return Padrao();
            }
        }

        /// <summary>Grava o cadastro, criando a pasta de dados se preciso.</summary>
        public void Salvar()
        {
            Directory.CreateDirectory(ConfiguracaoDeArmazenamento.PastaDeDados);
            File.WriteAllText(
                ConfiguracaoDeArmazenamento.CaminhoDoArquivoDePlanilhas,
                JsonSerializer.Serialize(this, Opcoes));
        }

        // ── Planilhas ────────────────────────────────────────────────────

        /// <summary>Acrescenta uma planilha, marcada com a sala informada (ou sem marca), e grava.</summary>
        public PlanilhaRegistrada Adicionar(string nome, string url, string? sala = null)
        {
            var planilha = new PlanilhaRegistrada
            {
                Nome = string.IsNullOrWhiteSpace(nome) ? $"Planilha {Planilhas.Count + 1}" : nome.Trim(),
                Url = url.Trim(),
                Sala = NormalizarSala(sala),
            };

            Planilhas.Add(planilha);
            Salvar();
            return planilha;
        }

        /// <summary>Remove uma planilha e grava. Devolve false se ela não estava no cadastro.</summary>
        public bool Remover(PlanilhaRegistrada planilha)
        {
            bool removida = Planilhas.Remove(planilha);

            if (removida)
                Salvar();

            return removida;
        }

        // ── Salas cadastradas à mão ──────────────────────────────────────

        /// <summary>
        /// Cadastra uma sala pelo código (qualquer grafia) e grava. Se já existe, só atualiza o
        /// nome. Devolve a sala cadastrada.
        /// </summary>
        /// <exception cref="ArgumentException">Código vazio.</exception>
        public SalaRegistrada AdicionarSala(string codigo, string? nome = null)
        {
            string normalizado = NormalizarSala(codigo)
                ?? throw new ArgumentException("Informe o código da sala (ex.: LAB002).", nameof(codigo));

            string? nomeAparado = string.IsNullOrWhiteSpace(nome) ? null : nome.Trim();
            var existente = Salas.FirstOrDefault(sala => sala.Codigo == normalizado);

            if (existente != null)
            {
                if (nomeAparado != null)
                    existente.Nome = nomeAparado;
            }
            else
            {
                existente = new SalaRegistrada { Codigo = normalizado, Nome = nomeAparado };
                Salas.Add(existente);
            }

            Salvar();
            return existente;
        }

        /// <summary>Remove uma sala cadastrada à mão e grava. Devolve false se não estava no cadastro.</summary>
        public bool RemoverSala(string? codigo)
        {
            string? normalizado = NormalizarSala(codigo);

            if (normalizado == null)
                return false;

            int removidas = Salas.RemoveAll(sala => sala.Codigo == normalizado);

            if (removidas > 0)
                Salvar();

            return removidas > 0;
        }

        /// <summary>Se a sala foi cadastrada à mão.</summary>
        public bool SalaCadastrada(string? codigo)
        {
            string? normalizado = NormalizarSala(codigo);
            return normalizado != null && Salas.Any(sala => sala.Codigo == normalizado);
        }

        // ── Planilhas por sala ───────────────────────────────────────────

        /// <summary>
        /// As planilhas de uma sala: as marcadas com ela no cadastro, mais as sem marca cujos
        /// sensores (pelo HOSTNAME) alimentam essa sala.
        /// </summary>
        /// <param name="codigo">Código da sala, em qualquer grafia aceita por <see cref="CodigoDeSala"/>.</param>
        /// <param name="salasDetectadas">
        /// Por URL de planilha, os códigos de sala encontrados nas leituras do último ciclo
        /// (<see cref="ConjuntoDeAmbientes.SalasPorPlanilha"/>). Pode ser null antes do primeiro ciclo.
        /// </param>
        public IReadOnlyList<PlanilhaRegistrada> PlanilhasDaSala(
            string? codigo,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>>? salasDetectadas)
        {
            string normalizado = CodigoDeSala.Normalizar(codigo);

            if (normalizado.Length == 0)
                return Array.Empty<PlanilhaRegistrada>();

            // p.Sala já está normalizado: Carregar e Adicionar garantem isso na entrada.
            return Planilhas
                .Where(p => p.Sala == normalizado
                            || (p.Sala == null && AlimentaASala(p, normalizado, salasDetectadas)))
                .ToList();
        }

        /// <summary>
        /// As planilhas sem marca de sala que também não alimentam sala alguma — por exemplo,
        /// uma que falhou ao baixar ou cujos HOSTNAMEs não seguem o padrão.
        /// </summary>
        public IReadOnlyList<PlanilhaRegistrada> PlanilhasSemSala(
            IReadOnlyDictionary<string, IReadOnlyCollection<string>>? salasDetectadas)
        {
            return Planilhas
                .Where(p => p.Sala == null
                            && !(salasDetectadas != null
                                 && salasDetectadas.TryGetValue(p.Url, out var salas)
                                 && salas.Count > 0))
                .ToList();
        }

        /// <summary>Código de sala normalizado, ou null quando vazio.</summary>
        public static string? NormalizarSala(string? sala)
        {
            string normalizado = CodigoDeSala.Normalizar(sala);
            return normalizado.Length == 0 ? null : normalizado;
        }

        private static bool AlimentaASala(
            PlanilhaRegistrada planilha,
            string codigo,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>>? salasDetectadas)
        {
            return salasDetectadas != null
                   && salasDetectadas.TryGetValue(planilha.Url, out var salas)
                   && salas.Contains(codigo);
        }
    }
}
