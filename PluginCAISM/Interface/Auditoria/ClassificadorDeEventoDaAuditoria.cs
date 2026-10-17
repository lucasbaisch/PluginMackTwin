using System;
using System.Collections.Generic;
using System.Linq;
using PluginCAISM.Dominio;
using PluginCAISM.Processamento;

namespace PluginCAISM.Interface.Auditoria
{
    /// <summary>
    /// Classifica CADA FIXAÇÃO ISOLADAMENTE, para a tabela de auditoria — e só para ela.
    /// </summary>
    /// <remarks>
    /// <b>Isto não é a Equação 4, e não substitui nada.</b> A classificação oficial de potencial
    /// homeodinâmico é por ELEMENTO (objeto × cenário): a Camada 1
    /// (<see cref="EstimadorDeEfeitoDeElemento"/>) tira a MÉDIA dos ΔD centrados de todas as
    /// fixações daquele elemento, somadas as sessões e os participantes, e a Camada 2
    /// (<see cref="CalculadorDaCamada2.Classificar"/>) decide pelo IC₈₅% dessa média. Uma média
    /// tem um só resultado, então toda fixação sobre o mesmo objeto sai da mesma cor — que é
    /// exatamente o que a auditoria não consegue mostrar.
    ///
    /// Uma fixação sozinha NÃO TEM intervalo de confiança: não há n, não há erro-padrão, não há o
    /// que afirmar estatisticamente. Logo, o que esta classe devolve é outra grandeza, DESCRITIVA
    /// e não inferencial: onde o ΔD daquela fixação caiu em relação à dispersão da amostra.
    /// Serve para ler a sessão evento a evento; não serve para concluir sobre o elemento.
    ///
    /// A regra, em três passos que espelham a Camada 1 para que as duas leituras sejam
    /// comparáveis:
    /// <list type="number">
    /// <item>ΔD = diâmetro médio da fixação − baseline pupilar da sessão;</item>
    /// <item>ΔD_c = ΔD − média dos ΔD daquele participante (remove a assinatura do indivíduo,
    /// sem a qual quem tem pupila naturalmente maior sairia vermelho de ponta a ponta);</item>
    /// <item>z = ΔD_c ÷ desvio-padrão dos ΔD_c da amostra. Abaixo de −<see cref="LimiteEmDesviosPadrao"/>
    /// é constrição (Restaurador); acima de +<see cref="LimiteEmDesviosPadrao"/> é dilatação
    /// (Estressor); entre os dois, Neutro.</item>
    /// </list>
    ///
    /// O limiar é convenção, não resultado: 1,44 aqui não teria o sentido que tem na Equação 4
    /// (lá incide sobre um ERRO-PADRÃO, aqui incidiria sobre o desvio-padrão de uma única
    /// observação) e deixaria ~5% dos eventos coloridos. Um desvio-padrão mantém a maioria neutra
    /// e destaca as duas caudas, que é o que uma auditoria precisa mostrar. Mexer em
    /// <see cref="LimiteEmDesviosPadrao"/> é o único ajuste previsto.
    ///
    /// <b>A escala vem das fixações que a análise usa</b> (<see cref="Sessao.Fixacoes"/>, já
    /// restritas à janela temporal), não das que a auditoria exibe — a tabela mostra também os
    /// eventos descartados pela janela, e deixá-los mover a régua faria a mesma fixação mudar de
    /// cor conforme o filtro. Os eventos fora da janela são medidos contra essa régua e continuam
    /// esmaecidos.
    /// </remarks>
    public sealed class ClassificadorDeEventoDaAuditoria
    {
        /// <summary>Quantos desvios-padrão separam Neutro de Restaurador/Estressor.</summary>
        public const double LimiteEmDesviosPadrao = 1.0;

        private readonly Dictionary<string, double> _mediaDeltaPorParticipante =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        private double _desvioPadraoDosDeltasCentrados = double.NaN;

        /// <summary>Desvio-padrão dos ΔD centrados, a régua da classificação. NaN se não estimável.</summary>
        public double DesvioPadraoDosDeltasCentrados => _desvioPadraoDosDeltasCentrados;

        /// <summary>
        /// Mede a régua a partir das sessões auditadas. Precisa ser chamado antes de
        /// <see cref="Classificar"/>; sem isso toda fixação sai como
        /// <see cref="ClassificacaoDePotencial.Neutro"/>.
        /// </summary>
        public void Preparar(IEnumerable<Sessao> sessoes)
        {
            _mediaDeltaPorParticipante.Clear();
            _desvioPadraoDosDeltasCentrados = double.NaN;

            if (sessoes == null) return;

            var deltasPorParticipante = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

            foreach (var sessao in sessoes)
            {
                if (sessao == null || double.IsNaN(sessao.BaselinePupilar))
                    continue;

                foreach (var fixacao in sessao.Fixacoes)
                {
                    double delta = CalcularDeltaPupilar(fixacao, sessao);
                    if (double.IsNaN(delta))
                        continue;

                    string participante = sessao.IdentificacaoManual ?? string.Empty;

                    if (!deltasPorParticipante.TryGetValue(participante, out var lista))
                    {
                        lista = new List<double>();
                        deltasPorParticipante[participante] = lista;
                    }

                    lista.Add(delta);
                }
            }

            if (deltasPorParticipante.Count == 0)
                return;

            var centrados = new List<double>();

            foreach (var (participante, deltas) in deltasPorParticipante)
            {
                double media = deltas.Average();
                _mediaDeltaPorParticipante[participante] = media;
                centrados.AddRange(deltas.Select(delta => delta - media));
            }

            _desvioPadraoDosDeltasCentrados =
                EstimadorDeEfeitoDeElemento.CalcularDesvioPadraoAmostral(centrados);
        }

        /// <summary>
        /// ΔD centrado de uma fixação, em mm — o número que a coluna ΔD da auditoria exibe.
        /// NaN quando a sessão não tem baseline ou a fixação não tem diâmetro válido.
        /// </summary>
        public double CalcularDeltaCentrado(Fixacao fixacao, Sessao sessao)
        {
            double delta = CalcularDeltaPupilar(fixacao, sessao);
            if (double.IsNaN(delta))
                return double.NaN;

            string participante = sessao.IdentificacaoManual ?? string.Empty;

            return _mediaDeltaPorParticipante.TryGetValue(participante, out double media)
                ? delta - media
                : double.NaN;
        }

        /// <summary>
        /// Classificação descritiva de UMA fixação. Ver as ressalvas no comentário da classe.
        /// </summary>
        public ClassificacaoDePotencial Classificar(Fixacao fixacao, Sessao sessao)
        {
            double centrado = CalcularDeltaCentrado(fixacao, sessao);

            if (double.IsNaN(centrado) ||
                double.IsNaN(_desvioPadraoDosDeltasCentrados) ||
                _desvioPadraoDosDeltasCentrados <= 0.0)
                return ClassificacaoDePotencial.Neutro;

            double z = centrado / _desvioPadraoDosDeltasCentrados;

            if (z < -LimiteEmDesviosPadrao)
                return ClassificacaoDePotencial.Restaurador;   // constrição

            if (z > LimiteEmDesviosPadrao)
                return ClassificacaoDePotencial.Estressor;     // dilatação

            return ClassificacaoDePotencial.Neutro;
        }

        /// <summary>
        /// ΔD bruto: diâmetro médio da fixação menos a baseline da sessão. Mesma regra de
        /// <see cref="EstimadorDeEfeitoDeElemento"/>, para que as duas leituras partam do mesmo
        /// número.
        /// </summary>
        private static double CalcularDeltaPupilar(Fixacao fixacao, Sessao sessao)
        {
            if (fixacao == null || sessao == null || double.IsNaN(sessao.BaselinePupilar))
                return double.NaN;

            double diametro = ObterDiametroMedioDaFixacao(fixacao);

            return double.IsNaN(diametro) ? double.NaN : diametro - sessao.BaselinePupilar;
        }

        /// <summary>
        /// Diâmetro pupilar médio binocular da fixação, ou NaN se nenhum olho tem medida válida.
        /// </summary>
        private static double ObterDiametroMedioDaFixacao(Fixacao fixacao)
        {
            bool esquerdaValida = !double.IsNaN(fixacao.DiametroMedianoPupilaEsquerda)
                               && fixacao.DiametroMedianoPupilaEsquerda > 0;
            bool direitaValida = !double.IsNaN(fixacao.DiametroMedianoPupilaDireita)
                              && fixacao.DiametroMedianoPupilaDireita > 0;

            if (esquerdaValida && direitaValida)
                return (fixacao.DiametroMedianoPupilaEsquerda + fixacao.DiametroMedianoPupilaDireita) / 2.0;

            if (esquerdaValida)
                return fixacao.DiametroMedianoPupilaEsquerda;

            if (direitaValida)
                return fixacao.DiametroMedianoPupilaDireita;

            return double.NaN;
        }
    }
}
