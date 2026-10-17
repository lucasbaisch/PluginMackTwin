using System;
using System.Collections.Generic;
using PluginCAISM.Configuracao;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Representa uma única amostra de rastreamento ocular coletada pelo dispositivo HTC VIVE Focus Vision,
    /// correspondendo a uma linha do CSV de entrada (Input 01), enriquecida com os dados
    /// ambientais do sensor (Input 03) pelo mesmo timestamp.
    /// </summary>
    /// <remarks>
    /// O CSV pode estar em ordem cronológica decrescente; a reordenação por timestamp crescente
    /// é responsabilidade do <see cref="Parsing.AnalisadorCsvSessao"/>.
    /// Valores 0.0 em diâmetro pupilar são considerados inválidos e devem ser ignorados
    /// em cálculos analíticos, mas a linha permanece no conjunto de dados brutos.
    /// leftHitPoint e rightHitPoint frequentemente têm os mesmos valores — isso é normal
    /// e indica que ambos os olhos convergem no mesmo ponto da geometria virtual.
    /// </remarks>
    public class LinhaDeSessao
    {
        /// <summary>Índice da linha no CSV original (base 0, após o cabeçalho).</summary>
        public int IndiceLinhaOriginal { get; set; }

        /// <summary>Momento exato da amostra. Formato original: "yyyy-MM-dd HH:mm:ss.fff".</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>Identificador manual do participante. Exemplo: "MA001".</summary>
        public string IdentificacaoManual { get; set; }

        /// <summary>Código da sessão. Chave de junção com o Input 02 (cortisol). Exemplo: "2195-02".</summary>
        public string NomeDaSessao { get; set; }

        /// <summary>Nome do cenário de realidade virtual. Exemplo: "Cenario2_Dia".</summary>
        public string CenarioSelecionado { get; set; }

        /// <summary>Assento do participante na sala. Exemplo: "A4".</summary>
        public string AssentoSelecionado { get; set; }

        /// <summary>Nome do objeto virtual onde o olhar incidiu. Exemplo: "Parede: Rippas".</summary>
        public string NomeDoObjeto { get; set; }

        /// <summary>Componente X do vetor de direção do olhar esquerdo (normalizado).</summary>
        public double DirecaoOlharEsquerdoX { get; set; }

        /// <summary>Componente X do vetor de direção do olhar direito (normalizado).</summary>
        public double DirecaoOlharDireitoX { get; set; }

        /// <summary>Componente Y do vetor de direção do olhar esquerdo (normalizado).</summary>
        public double DirecaoOlharEsquerdoY { get; set; }

        /// <summary>Componente Y do vetor de direção do olhar direito (normalizado).</summary>
        public double DirecaoOlharDireitoY { get; set; }

        /// <summary>Componente Z do vetor de direção do olhar esquerdo (normalizado).</summary>
        public double DirecaoOlharEsquerdoZ { get; set; }

        /// <summary>Componente Z do vetor de direção do olhar direito (normalizado).</summary>
        public double DirecaoOlharDireitoZ { get; set; }

        /// <summary>Componente X do quaternion de orientação do olho esquerdo.</summary>
        public double OrientacaoOlhoEsquerdoX { get; set; }

        /// <summary>Componente X do quaternion de orientação do olho direito.</summary>
        public double OrientacaoOlhoDireitoX { get; set; }

        /// <summary>Componente Y do quaternion de orientação do olho esquerdo.</summary>
        public double OrientacaoOlhoEsquerdoY { get; set; }

        /// <summary>Componente Y do quaternion de orientação do olho direito.</summary>
        public double OrientacaoOlhoDireitoY { get; set; }

        /// <summary>Componente Z do quaternion de orientação do olho esquerdo.</summary>
        public double OrientacaoOlhoEsquerdoZ { get; set; }

        /// <summary>Componente Z do quaternion de orientação do olho direito.</summary>
        public double OrientacaoOlhoDireitoZ { get; set; }

        /// <summary>Componente W do quaternion de orientação do olho esquerdo.</summary>
        public double OrientacaoOlhoEsquerdoW { get; set; }

        /// <summary>Componente W do quaternion de orientação do olho direito.</summary>
        public double OrientacaoOlhoDireitoW { get; set; }

        /// <summary>Diâmetro da pupila esquerda em milímetros. Valor 0.0 indica dado ausente/inválido.</summary>
        public double DiametroPupilaEsquerda { get; set; }

        /// <summary>Diâmetro da pupila direita em milímetros. Valor 0.0 indica dado ausente/inválido.</summary>
        public double DiametroPupilaDireita { get; set; }

        /// <summary>
        /// Abertura do olho esquerdo, CONTÍNUA em [0, 1] — não é um sinalizador.
        /// </summary>
        /// <remarks>
        /// A coluna <c>left_EyeOpened</c> das capturas HTC é um sinal contínuo: cerca de 77 % das
        /// amostras valem exatamente 1, ~17 % caem em [0,90, 1,00) e o restante em [0,55, 0,90]
        /// — olhos abertos, apenas não completamente. Só as piscadas descem abaixo de 0,5.
        /// Lê-la como inteiro descartava como "olho fechado" quase um quarto das linhas; ver
        /// <see cref="DadosOcularesValidos"/> para o limiar que separa piscada de olho aberto.
        /// </remarks>
        public double AberturaDoOlhoEsquerdo { get; set; }

        /// <summary>Abertura do olho direito, contínua em [0, 1]. Ver <see cref="AberturaDoOlhoEsquerdo"/>.</summary>
        public double AberturaDoOlhoDireito { get; set; }

        /// <summary>Coordenada X da posição do jogador no espaço virtual Unity (metros).</summary>
        public double PosicaoJogadorX { get; set; }

        /// <summary>Coordenada Y da posição do jogador no espaço virtual Unity (metros).</summary>
        public double PosicaoJogadorY { get; set; }

        /// <summary>Coordenada Z da posição do jogador no espaço virtual Unity (metros).</summary>
        public double PosicaoJogadorZ { get; set; }

        /// <summary>Componente X do quaternion de rotação do jogador.</summary>
        public double RotacaoJogadorX { get; set; }

        /// <summary>Componente Y do quaternion de rotação do jogador.</summary>
        public double RotacaoJogadorY { get; set; }

        /// <summary>Componente Z do quaternion de rotação do jogador.</summary>
        public double RotacaoJogadorZ { get; set; }

        /// <summary>Componente W do quaternion de rotação do jogador.</summary>
        public double RotacaoJogadorW { get; set; }

        /// <summary>Coordenada X do ponto de incidência do olhar esquerdo na geometria virtual (metros).</summary>
        public double PontoDeIncidenciaEsquerdoX { get; set; }

        /// <summary>Coordenada Y do ponto de incidência do olhar esquerdo na geometria virtual (metros).</summary>
        public double PontoDeIncidenciaEsquerdoY { get; set; }

        /// <summary>Coordenada Z do ponto de incidência do olhar esquerdo na geometria virtual (metros).</summary>
        public double PontoDeIncidenciaEsquerdoZ { get; set; }

        /// <summary>Coordenada X do ponto de incidência do olhar direito na geometria virtual (metros).</summary>
        public double PontoDeIncidenciaDireitoX { get; set; }

        /// <summary>Coordenada Y do ponto de incidência do olhar direito na geometria virtual (metros).</summary>
        public double PontoDeIncidenciaDireitoY { get; set; }

        /// <summary>Coordenada Z do ponto de incidência do olhar direito na geometria virtual (metros).</summary>
        public double PontoDeIncidenciaDireitoZ { get; set; }

        /// <summary>
        /// String de metadados do dispositivo HTC VIVE. Armazenada sem processamento.
        /// Exemplo: "Modelo: HTC VIVE Focus Vision | OS: Android OS 12 / API-32 (...)".
        /// </summary>
        public string NomeDispositivo { get; set; }

        /// <summary>
        /// Campos ambientais provenientes do Input 03, fundidos por timestamp.
        /// As chaves são os nomes das colunas do CSV de sensores (temperatura, umidade, CO₂, ruído, etc.).
        /// Permanece vazio se não houver correspondência de timestamp no Input 03.
        /// </summary>
        public Dictionary<string, string> CamposSensor { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Registro de data e hora da leitura ambiental associada a esta amostra — a vigente, isto
        /// é, a mais recente igual ou anterior ao <see cref="Timestamp"/>. Vale
        /// <see cref="DateTime.MinValue"/> quando não há leitura vigente.
        /// </summary>
        /// <remarks>
        /// Guardado para que a defasagem entre a medição ambiental e a amostra ocular seja
        /// auditável: uma leitura de 40 s antes descreve a mesma condição; uma de horas antes, não.
        /// </remarks>
        public DateTime InstanteDaLeituraDeSensor { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Indica se os dados oculares desta linha são válidos para detecção de eventos.
        /// Uma linha é inválida se ambos os olhos estão fechados ou se ambos os pontos
        /// de incidência são zero (dado ausente do dispositivo).
        /// </summary>
        /// <remarks>
        /// "Fechado" é abertura abaixo de
        /// <see cref="Configuracao.ParametrosDeDeteccaoOcular.LimiarDeOlhoAberto"/>, e não
        /// "diferente de 1": a coluna é contínua, e exigir o valor exato 1 rejeitava também os
        /// olhos 90 % abertos. O limiar de 0,5 é o que separa piscada de olho aberto nas
        /// capturas reais, em que nenhuma amostra de piscada passa de 0,35 e nenhuma amostra
        /// normal fica abaixo de 0,55.
        /// </remarks>
        public bool DadosOcularesValidos =>
            (OlhoEstaAberto(AberturaDoOlhoEsquerdo) || OlhoEstaAberto(AberturaDoOlhoDireito)) &&
            !AmbosOsPontosDeIncidenciaSaoZero();

        /// <summary>
        /// Se uma abertura corresponde a olho aberto. Um valor ausente chega como 0,0 do parser
        /// e é lido como fechado, que é a leitura conservadora.
        /// </summary>
        private static bool OlhoEstaAberto(double abertura)
        {
            return abertura >= ParametrosDeDeteccaoOcular.LimiarDeOlhoAberto;
        }

        /// <summary>
        /// Média dos diâmetros pupilares válidos desta linha.
        /// Retorna NaN se ambos os valores forem zero ou inválidos.
        /// </summary>
        public double DiametroPupilaMedio => CalcularDiametroPupilaMedio();

        private bool AmbosOsPontosDeIncidenciaSaoZero()
        {
            bool incidenciaEsquerdaZero = PontoDeIncidenciaEsquerdoX == 0
                && PontoDeIncidenciaEsquerdoY == 0
                && PontoDeIncidenciaEsquerdoZ == 0;

            bool incidenciaDireitaZero = PontoDeIncidenciaDireitoX == 0
                && PontoDeIncidenciaDireitoY == 0
                && PontoDeIncidenciaDireitoZ == 0;

            return incidenciaEsquerdaZero && incidenciaDireitaZero;
        }

        private double CalcularDiametroPupilaMedio()
        {
            bool esquerdaValida = DiametroPupilaEsquerda > 0;
            bool direitaValida = DiametroPupilaDireita > 0;

            if (esquerdaValida && direitaValida)
                return (DiametroPupilaEsquerda + DiametroPupilaDireita) / 2.0;

            if (esquerdaValida)
                return DiametroPupilaEsquerda;

            if (direitaValida)
                return DiametroPupilaDireita;

            return double.NaN;
        }
    }
}
