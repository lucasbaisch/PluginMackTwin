using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackTwin.Configuracao;
using MackTwin.Dominio;
using MackTwin.Parsing;

namespace MackTwin.Processamento
{
    /// <summary>
    /// Um ciclo de atualização: baixa todas as planilhas ativas, junta as leituras e as agrupa
    /// em salas. Guarda o último conjunto para a interface consultar entre ciclos.
    /// </summary>
    /// <remarks>
    /// Não tem relógio próprio: quem decide QUANDO atualizar é a janela (um
    /// <c>DispatcherTimer</c> de <see cref="ConfiguracaoDaPlanilhaGoogle.IntervaloDeAtualizacaoEmSegundos"/>),
    /// porque é ela que sabe se está visível e se o usuário pediu pausa. Este objeto só sabe
    /// COMO atualizar, e por isso pode ser exercitado sem interface.
    ///
    /// Uma planilha que falha não derruba as outras: o erro vira aviso, a contagem vai para
    /// <see cref="ConjuntoDeAmbientes.PlanilhasComFalha"/> e as salas das planilhas que
    /// responderam são exibidas normalmente.
    /// </remarks>
    public sealed class MonitorDePlanilhas
    {
        private readonly LeitorDePlanilhaGoogle _leitor = new();

        /// <summary>O cadastro de planilhas que este monitor percorre.</summary>
        public RegistroDePlanilhas Registro { get; }

        /// <summary>Resultado do último ciclo, ou null antes do primeiro.</summary>
        public ConjuntoDeAmbientes? UltimoConjunto { get; private set; }

        /// <summary>Quando o último ciclo terminou, ou null.</summary>
        public DateTime? UltimaAtualizacao { get; private set; }

        /// <summary>Avisos do último ciclo.</summary>
        public IReadOnlyList<string> UltimosAvisos { get; private set; } = Array.Empty<string>();

        /// <summary>Cria o monitor sobre um cadastro já carregado.</summary>
        public MonitorDePlanilhas(RegistroDePlanilhas registro)
        {
            Registro = registro ?? throw new ArgumentNullException(nameof(registro));
        }

        /// <summary>
        /// Executa um ciclo completo e devolve o conjunto de salas. Cancelamento interrompe o
        /// download em curso e propaga <see cref="OperationCanceledException"/>.
        /// </summary>
        public async Task<ConjuntoDeAmbientes> AtualizarAsync(CancellationToken cancelamento = default)
        {
            var avisos = new List<string>();
            var leituras = new List<Leitura>();
            int falhas = 0;

            var ativas = Registro.Ativas.ToList();

            if (ativas.Count == 0)
                avisos.Add("Nenhuma planilha ativa cadastrada. Use \"Adicionar planilha\" para começar.");

            foreach (var planilha in ativas)
            {
                try
                {
                    avisos.Add($"— {planilha.Nome}");
                    leituras.AddRange(await _leitor.BaixarELerAsync(planilha.Url, avisos, cancelamento).ConfigureAwait(false));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception excecao)
                {
                    falhas++;
                    avisos.Add($"Planilha \"{planilha.Nome}\" falhou: {excecao.Message}");
                }
            }

            var conjunto = AgrupadorDeAmbientes.Agrupar(leituras, avisos);
            conjunto.PlanilhasComFalha = falhas;

            UltimoConjunto = conjunto;
            UltimaAtualizacao = DateTime.Now;
            UltimosAvisos = avisos;

            return conjunto;
        }
    }
}
