using System;
using System.Collections.Generic;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Representa um evento de movimento ocular detectado a partir de uma sequência
    /// de linhas consecutivas de <see cref="LinhaDeSessao"/> cujo ponto de incidência
    /// permanece espacialmente estável dentro de uma tolerância definida.
    /// </summary>
    /// <remarks>
    /// A detecção de eventos é realizada por <see cref="Processamento.DetectorDeMovimentoOcular"/>.
    /// A estabilidade é avaliada pela distância euclidiana 3D entre pontos de incidência
    /// consecutivos. Um evento é descartado se sua duração não se enquadrar nas faixas
    /// de <see cref="Fixacao"/> (≥ 300 ms) nem de <see cref="Saccade"/> (30–80 ms).
    /// Apenas linhas entre os minutos 2 e 4 da sessão são consideradas na detecção.
    /// </remarks>
    public abstract class EventoDeMovimentoOcular
    {
        /// <summary>Timestamp da primeira linha do grupo que originou este evento.</summary>
        public DateTime TimestampInicio { get; init; }

        /// <summary>Timestamp da última linha do grupo que originou este evento.</summary>
        public DateTime TimestampFim { get; init; }

        /// <summary>
        /// Duração do evento em milissegundos.
        /// Calculado como (TimestampFim - TimestampInicio).TotalMilliseconds.
        /// </summary>
        public double DuracaoEmMilissegundos { get; init; }

        /// <summary>
        /// Componente X da posição média do ponto de incidência durante o evento.
        /// Calculada como a média aritmética de (pontoEsquerdo + pontoDireito) / 2
        /// para todas as linhas do grupo, em metros (espaço virtual Unity).
        /// </summary>
        public double PontoMedioDeIncidenciaX { get; init; }

        /// <summary>Componente Y da posição média do ponto de incidência (metros, espaço virtual Unity).</summary>
        public double PontoMedioDeIncidenciaY { get; init; }

        /// <summary>Componente Z da posição média do ponto de incidência (metros, espaço virtual Unity).</summary>
        public double PontoMedioDeIncidenciaZ { get; init; }

        /// <summary>
        /// Conjunto de nomes de objetos virtuais (<see cref="LinhaDeSessao.NomeDoObjeto"/>)
        /// presentes nas linhas que compõem este evento.
        /// </summary>
        public HashSet<string> NomesDeObjetos { get; init; }

        /// <summary>
        /// Índices originais das linhas do CSV (<see cref="LinhaDeSessao.IndiceLinhaOriginal"/>)
        /// que compõem este evento, para rastreabilidade.
        /// </summary>
        public List<int> IndicesLinhasOriginais { get; init; }

        /// <summary>
        /// MEDIANA dos diâmetros da pupila esquerda nas linhas deste evento,
        /// excluindo valores 0.0 (inválidos). Retorna NaN se todos os valores forem inválidos.
        /// </summary>
        public double DiametroMedianoPupilaEsquerda { get; init; }

        /// <summary>
        /// MEDIANA dos diâmetros da pupila direita nas linhas deste evento,
        /// excluindo valores 0.0 (inválidos). Retorna NaN se todos os valores forem inválidos.
        /// </summary>
        public double DiametroMedianoPupilaDireita { get; init; }
    }

    /// <summary>
    /// Evento de fixação ocular: período em que o olhar permanece estável sobre
    /// um ponto no espaço virtual por uma duração ≥ 300 ms.
    /// </summary>
    /// <remarks>
    /// A janela de fixação focal adotada é 300–500 ms (§5.1 do TextoFinal), aplicada como piso
    /// de DETECÇÃO (<see cref="Configuracao.ParametrosDeDeteccaoOcular.DuracaoMinimaDeFixacaoMs"/>).
    /// A detecção não aplica teto: fixações acima de 500 ms continuam a ser detectadas e entram
    /// no cálculo como qualquer outra, porque no modelo em duas camadas a duração é critério de
    /// detecção, não sinal — o índice deriva do ΔD pupilar (Equações 3 e 4).
    ///
    /// Fixações sustentadas acompanhadas de CONSTRIÇÃO pupilar em relação à linha de base
    /// pré-estímulo (ΔD negativo) indicam redução da ativação autonômica e potencial restaurador
    /// do elemento observado (§5.2, Quadros 36 e 40; Teoria da Restauração da Atenção — Kaplan).
    /// </remarks>
    public class Fixacao : EventoDeMovimentoOcular
    {
        /// <summary>
        /// Tendência de variação do diâmetro pupilar ao longo desta fixação.
        /// Calculada comparando a média pupilar das primeiras e últimas amostras do evento.
        /// </summary>
        public TendenciaPupilar TendenciaPupilar { get; init; }
    }

    /// <summary>
    /// Evento sacádico: movimento rápido do olhar entre dois pontos,
    /// com duração entre 30 ms e 80 ms (Figura 46 do TextoFinal).
    /// </summary>
    /// <remarks>
    /// Saccades frequentes acompanhados de fixações curtas e de DILATAÇÃO pupilar indicam
    /// esforço de absorção de informação, carga cognitiva elevada e potencial estressor do
    /// ambiente (§5.2, Quadros 36 e 40).
    /// </remarks>
    public class Saccade : EventoDeMovimentoOcular
    {
    }

    /// <summary>
    /// Tendência de variação do diâmetro pupilar durante uma fixação,
    /// determinada pela comparação entre o valor pupilar médio no início e no fim do evento.
    /// </summary>
    /// <remarks>
    /// A leitura fisiológica segue a §5.2 (Quadros 36 e 40) do TextoFinal: a constrição pupilar
    /// indica redução da ativação autonômica e potencial restaurador, enquanto a dilatação indica
    /// esforço cognitivo e potencial estressor.
    /// </remarks>
    public enum TendenciaPupilar
    {
        /// <summary>
        /// O diâmetro pupilar aumentou ao longo da fixação (acima do limiar definido em
        /// <see cref="Configuracao.LimiaresDePotencialRestaurador.LimiarDeDilatacaoPupilarMm"/>).
        /// Associado a esforço cognitivo, carga atencional e potencial ESTRESSOR.
        /// </summary>
        Dilatada,

        /// <summary>
        /// O diâmetro pupilar diminuiu ao longo da fixação (abaixo do limiar negativo).
        /// Associado a relaxamento, predomínio parassimpático e potencial RESTAURADOR.
        /// </summary>
        Contraida,

        /// <summary>
        /// O diâmetro pupilar não variou significativamente durante a fixação,
        /// ou os dados pupilares eram insuficientes para classificação.
        /// </summary>
        Estavel
    }

    /// <summary>Tipo de evento de movimento ocular para uso em filtros e consultas.</summary>
    public enum TipoDeEventoOcular
    {
        /// <summary>Evento de fixação (duração ≥ 300 ms).</summary>
        Fixacao,

        /// <summary>Evento sacádico (duração 30–80 ms).</summary>
        Saccade
    }
}
