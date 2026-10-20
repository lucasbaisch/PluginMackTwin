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
        /// sala que cada HOSTNAME indica (<see cref="Processamento.ResolvedorDeSala"/>).
        /// </remarks>
        public string? Sala { get; set; }

        public override string ToString() => Nome;
    }

    /// <summary>Um ambiente cadastrado à mão: nome (que casa com o Room) e código (o HOSTNAME do sensor).</summary>
    public sealed class SalaRegistrada
    {
        /// <summary>Código normalizado — chave da sala; igual ao HOSTNAME do sensor, normalizado.</summary>
        public string Codigo { get; set; } = string.Empty;

        /// <summary>Código como foi digitado (ex.: BR_UPM-CPS-EDU_T00-FABLAB-MAMB-TU-SHT40_2026), para exibição.</summary>
        public string CodigoExibido { get; set; } = string.Empty;

        /// <summary>Nome do ambiente — é o que casa com o Número ou Nome do Room no modelo.</summary>
        public string? Nome { get; set; }

        public override string ToString() => string.IsNullOrWhiteSpace(Nome) ? CodigoExibido : Nome;
    }

    /// <summary>
    /// Cadastro das planilhas que alimentam o painel e dos ambientes criados à mão, persistido
    /// em JSON na pasta de dados do usuário (<see cref="ConfiguracaoDeArmazenamento"/>).
    /// </summary>
    /// <remarks>
    /// Começa com a planilha do DHT22 e cresce pela própria interface do plug-in, sala por sala.
    /// Um ambiente cadastrado à mão tem um <b>nome</b>, que é como ele casa com o Room do
    /// modelo, e um <b>código</b>, que é o HOSTNAME do sensor na planilha: as leituras desse
    /// sensor vão para esse ambiente. Usa <c>System.Text.Json</c>, que já vem no .NET 8.
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

        /// <summary>Ambientes cadastrados à mão, na ordem de cadastro.</summary>
        public List<SalaRegistrada> Salas { get; set; } = new();

        /// <summary>As planilhas que entram no ciclo de atualização.</summary>
        [JsonIgnore]
        public IEnumerable<PlanilhaRegistrada> Ativas =>
            Planilhas.Where(p => p.Ativa && !string.IsNullOrWhiteSpace(p.Url));

        /// <summary>Códigos normalizados das salas cadastradas — o que o resolvedor de sala consulta.</summary>
        [JsonIgnore]
        public IReadOnlySet<string> CodigosDasSalas =>
            new HashSet<string>(Salas.Select(sala => sala.Codigo), StringComparer.Ordinal);

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
                    .Select(sala => new SalaRegistrada
                    {
                        Codigo = NormalizarSala(sala.Codigo) ?? NormalizarSala(sala.CodigoExibido) ?? string.Empty,
                        CodigoExibido = string.IsNullOrWhiteSpace(sala.CodigoExibido) ? sala.Codigo : sala.CodigoExibido.Trim(),
                        Nome = string.IsNullOrWhiteSpace(sala.Nome) ? null : sala.Nome.Trim(),
                    })
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

        // ── Ambientes cadastrados à mão ──────────────────────────────────

        /// <summary>
        /// Cadastra um ambiente pelo código (o HOSTNAME do sensor, em qualquer grafia) e grava.
        /// Se já existe, só atualiza o nome. Devolve o ambiente cadastrado.
        /// </summary>
        /// <exception cref="ArgumentException">Código vazio.</exception>
        public SalaRegistrada AdicionarSala(string codigo, string? nome = null)
        {
            string normalizado = NormalizarSala(codigo)
                ?? throw new ArgumentException("Informe o código do ambiente (o HOSTNAME do sensor na planilha).", nameof(codigo));

            string? nomeAparado = string.IsNullOrWhiteSpace(nome) ? null : nome.Trim();
            var existente = Salas.FirstOrDefault(sala => sala.Codigo == normalizado);

            if (existente != null)
            {
                if (nomeAparado != null)
                    existente.Nome = nomeAparado;
            }
            else
            {
                existente = new SalaRegistrada { Codigo = normalizado, CodigoExibido = codigo.Trim(), Nome = nomeAparado };
                Salas.Add(existente);
            }

            Salvar();
            return existente;
        }

        /// <summary>Remove um ambiente cadastrado à mão e grava. Devolve false se não estava no cadastro.</summary>
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

        // ── Planilhas por sala ───────────────────────────────────────────

        /// <summary>
        /// As planilhas de uma sala: as marcadas com ela no cadastro, mais as sem marca cujos
        /// sensores alimentam essa sala.
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
        /// uma que falhou ao baixar ou cujos HOSTNAMEs não são reconhecidos.
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
