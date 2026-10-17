using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        public override string ToString() => Nome;
    }

    /// <summary>
    /// Cadastro das planilhas que alimentam o painel, persistido em JSON na pasta de dados do
    /// usuário (<see cref="ConfiguracaoDeArmazenamento"/>).
    /// </summary>
    /// <remarks>
    /// Começa com a planilha do DHT22 e cresce pela própria interface do plug-in, à medida que
    /// outros sensores — em outras planilhas ou pastas — forem instalados. Usa
    /// <c>System.Text.Json</c>, que já vem no .NET 8: nenhum pacote novo.
    ///
    /// A leitura é tolerante: arquivo ausente, vazio ou corrompido devolve o cadastro padrão e
    /// registra um aviso, em vez de derrubar a abertura da janela. O cadastro em disco só é
    /// sobrescrito por uma ação explícita do usuário (adicionar, remover, ativar/desativar).
    /// </remarks>
    public sealed class RegistroDePlanilhas
    {
        private static readonly JsonSerializerOptions Opcoes = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>Todas as planilhas cadastradas, ativas ou não, na ordem de cadastro.</summary>
        public List<PlanilhaRegistrada> Planilhas { get; set; } = new();

        /// <summary>As planilhas que entram no ciclo de atualização.</summary>
        [JsonIgnore]
        public IEnumerable<PlanilhaRegistrada> Ativas =>
            Planilhas.Where(p => p.Ativa && !string.IsNullOrWhiteSpace(p.Url));

        /// <summary>Cadastro inicial: só a planilha do DHT22.</summary>
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

                if (registro == null || registro.Planilhas.Count == 0)
                {
                    avisos?.Add($"Cadastro de planilhas vazio em {caminho}; usando o padrão.");
                    return Padrao();
                }

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

        /// <summary>Acrescenta uma planilha e grava. A validação da URL é do chamador.</summary>
        public PlanilhaRegistrada Adicionar(string nome, string url)
        {
            var planilha = new PlanilhaRegistrada
            {
                Nome = string.IsNullOrWhiteSpace(nome) ? $"Planilha {Planilhas.Count + 1}" : nome.Trim(),
                Url = url.Trim(),
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
    }
}
