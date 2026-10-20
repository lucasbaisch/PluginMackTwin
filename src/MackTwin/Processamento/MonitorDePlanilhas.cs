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
    /// em salas.
    /// </summary>
    /// <remarks>
    /// Não tem relógio próprio: quem decide QUANDO atualizar é a janela (um
    /// <c>DispatcherTimer</c> de <see cref="ConfiguracaoDaPlanilhaGoogle.IntervaloDeAtualizacaoEmSegundos"/>),
    /// porque é ela que sabe se está visível e se o usuário pediu pausa. Este objeto só sabe
    /// COMO atualizar, e por isso pode ser exercitado sem interface.
    ///
    /// O <see cref="LeitorDePlanilhaGoogle"/> é um só e vive entre ciclos: é nele que fica o
    /// cache que torna os ciclos seguintes incrementais.
    ///
    /// Uma planilha que falha não derruba as outras: o erro vira aviso, a contagem vai para
    /// <see cref="ConjuntoDeAmbientes.PlanilhasComFalha"/> e as salas das planilhas que
    /// responderam são exibidas normalmente. Para cada planilha o monitor anota que salas ela
    /// alimentou (<see cref="ConjuntoDeAmbientes.SalasPorPlanilha"/>) — pelos HOSTNAMEs
    /// distintos, não por leitura: são poucos sensores e milhares de leituras.
    /// </remarks>
    public sealed class MonitorDePlanilhas
    {
        private readonly LeitorDePlanilhaGoogle _leitor = new();
        private readonly RegistroDePlanilhas _registro;

        /// <summary>Cria o monitor sobre um cadastro já carregado.</summary>
        public MonitorDePlanilhas(RegistroDePlanilhas registro)
        {
            _registro = registro ?? throw new ArgumentNullException(nameof(registro));
        }

        /// <summary>
        /// Executa um ciclo completo e devolve o conjunto de salas. Cancelamento interrompe o
        /// download em curso e propaga <see cref="OperationCanceledException"/>.
        /// </summary>
        public async Task<ConjuntoDeAmbientes> AtualizarAsync(CancellationToken cancelamento = default)
        {
            var avisos = new List<string>();
            var leituras = new List<Leitura>();
            var salasPorPlanilha = new Dictionary<string, IReadOnlyCollection<string>>();
            int falhas = 0;

            var ativas = _registro.Ativas.ToList();
            var codigosCadastrados = _registro.CodigosDasSalas;

            if (ativas.Count == 0)
                avisos.Add("Nenhuma planilha ativa cadastrada. Ligue \"Editar\" e use \"+ adicionar\" na sala.");

            foreach (var planilha in ativas)
            {
                try
                {
                    avisos.Add($"— {planilha.Nome}" + (planilha.Sala != null ? $" (sala {planilha.Sala})" : string.Empty));

                    var daPlanilha = await _leitor.BaixarELerAsync(planilha.Url, avisos, cancelamento).ConfigureAwait(false);
                    leituras.AddRange(daPlanilha);

                    salasPorPlanilha[planilha.Url] = daPlanilha
                        .Select(leitura => leitura.Hostname)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(hostname => ResolvedorDeSala.Resolver(hostname, codigosCadastrados))
                        .Where(codigo => codigo.Length > 0)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
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

            var conjunto = AgrupadorDeAmbientes.Agrupar(
                leituras, avisos, _registro.Salas.Select(sala => (sala.Codigo, sala.Nome)));
            conjunto.PlanilhasComFalha = falhas;
            conjunto.SalasPorPlanilha = salasPorPlanilha;

            return conjunto;
        }
    }
}
