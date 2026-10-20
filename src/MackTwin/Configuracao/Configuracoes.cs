using System;
using System.Globalization;
using System.IO;
using MackTwin.Dominio;

namespace MackTwin.Configuracao
{
    // ── Conforto ambiental ───────────────────────────────────────────────

    /// <summary>
    /// Faixas de conforto das variáveis ambientais medidas pelos sensores, usadas para
    /// classificar cada leitura e cada ambiente (<see cref="Processamento.ClassificadorDeConforto"/>).
    /// </summary>
    /// <remarks>
    /// Temperatura 21–26 °C e umidade relativa 35–65% conforme ABNT NBR 17037:2023. Valores
    /// dentro da faixa, extremos inclusive, são confortáveis. Outra grandeza entra aqui no dia
    /// em que um sensor a medir — não antes.
    /// </remarks>
    public static class FaixasDeConfortoAmbiental
    {
        /// <summary>Temperatura mínima confortável (°C). NBR 17037: 21 °C.</summary>
        public const double TemperaturaMinima = 21.0;

        /// <summary>Temperatura máxima confortável (°C). NBR 17037: 26 °C.</summary>
        public const double TemperaturaMaxima = 26.0;

        /// <summary>Umidade relativa mínima confortável (%). NBR 17037: 35%.</summary>
        public const double UmidadeMinima = 35.0;

        /// <summary>Umidade relativa máxima confortável (%). NBR 17037: 65%.</summary>
        public const double UmidadeMaxima = 65.0;
    }

    /// <summary>
    /// Cores de cada estado de conforto, compartilhadas pela interface (cartões, lista, gráfico)
    /// e pela pintura das salas no modelo Revit — um só lugar, para que a tela e o modelo nunca
    /// digam coisas diferentes com a mesma cor.
    /// </summary>
    /// <remarks>
    /// Verde para dentro da faixa; azul para abaixo (frio/seco) e vermelho para acima
    /// (quente/úmido), porque as duas direções pedem providências opostas e não podem
    /// compartilhar cor; cinza para sala sem leitura. Tons escuros o bastante para servirem
    /// de COR DE TEXTO sobre fundo branco.
    /// </remarks>
    public static class CoresDeConforto
    {
        /// <summary>Verde — dentro da faixa de conforto.</summary>
        public const string Confortavel = "#1B7A50";

        /// <summary>Azul — abaixo da faixa (frio ou seco).</summary>
        public const string AbaixoDaFaixa = "#2F6BD9";

        /// <summary>Vermelho — acima da faixa (quente ou úmido).</summary>
        public const string AcimaDaFaixa = "#B3382F";

        /// <summary>Cinza — sem leitura disponível.</summary>
        public const string SemDados = "#9AA3B2";

        /// <summary>Cor (hexadecimal <c>#RRGGBB</c>) de uma classificação.</summary>
        public static string Para(ClassificacaoDeConforto classificacao)
        {
            return classificacao switch
            {
                ClassificacaoDeConforto.Confortavel => Confortavel,
                ClassificacaoDeConforto.AbaixoDaFaixa => AbaixoDaFaixa,
                ClassificacaoDeConforto.AcimaDaFaixa => AcimaDaFaixa,
                _ => SemDados,
            };
        }

        /// <summary>
        /// Decompõe <c>#RRGGBB</c> em componentes, para montar cores do WPF e do Revit sem
        /// repetir a análise do hexadecimal nos dois lugares.
        /// </summary>
        public static (byte R, byte G, byte B) ParaRgb(string hexadecimal)
        {
            string texto = hexadecimal.TrimStart('#');

            if (texto.Length != 6)
                throw new ArgumentException($"Cor inválida: \"{hexadecimal}\". Esperado #RRGGBB.", nameof(hexadecimal));

            return (
                byte.Parse(texto.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(texto.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(texto.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
    }

    // ── Fonte de dados: planilha Google ─────────────────────────────────

    /// <summary>
    /// Convenções da planilha Google alimentada pelos sensores e do nome que identifica cada
    /// dispositivo.
    /// </summary>
    /// <remarks>
    /// <b>Formato observado</b> (planilha do DHT22, outubro de 2026): cabeçalho
    /// <c>TIMESTAMP,HOSTNAME,MAC ADRESS,TEMPERATURA,UMIDADE</c>; delimitador vírgula; decimais
    /// com VÍRGULA entre aspas (<c>"24,20"</c>); carimbo <c>yyyy-MM-dd HH:mm:ss</c> sem fuso;
    /// uma leitura a cada ~45 s. A planilha é pública e o Google a entrega como CSV pela URL de
    /// exportação, sem credencial — o leitor só precisa seguir o redirecionamento.
    ///
    /// <b>O HOSTNAME codifica a sala.</b> <c>MMCCTLAB001-001</c> lê-se Mackenzie (MM) / CCT /
    /// sala LAB001 / dispositivo 001. É esse trecho de sala que casa o sensor com o Room do
    /// modelo Revit, sem cadastro manual: um sensor novo que siga o padrão cai sozinho na sala
    /// certa. A grafia "ADRESS" é a da planilha e é aceita ao lado da correta.
    /// </remarks>
    public static class ConfiguracaoDaPlanilhaGoogle
    {
        /// <summary>Nome amigável da planilha cadastrada por padrão.</summary>
        public const string NomePadrao = "DHT22 — CCT Mackenzie Campinas";

        /// <summary>Endereço de edição da planilha padrão (o leitor deriva dele a URL de exportação).</summary>
        public const string UrlPadraoDeEdicao =
            "https://docs.google.com/spreadsheets/d/1-fiPdqoIxaA_eudZa2jkz71lUCgSBbBMcOSLqxwhHo8/edit?gid=0";

        /// <summary>Intervalo entre atualizações automáticas, em segundos.</summary>
        /// <remarks>
        /// Sessenta segundos: o sensor grava a cada ~45 s, então um ciclo por minuto perde no
        /// máximo uma leitura de atraso e não martela o Google.
        /// </remarks>
        public const int IntervaloDeAtualizacaoEmSegundos = 60;

        /// <summary>Tempo máximo de espera por um download, em segundos.</summary>
        public const int TempoLimiteDeDownloadEmSegundos = 30;

        /// <summary>Quantas linhas iniciais são inspecionadas à procura do cabeçalho.</summary>
        public const int MaximoDeLinhasParaProcurarCabecalho = 20;

        /// <summary>Nomes aceitos para a coluna de data e hora.</summary>
        public static readonly string[] ColunaTimestamp = { "TIMESTAMP", "DATA", "DATA/HORA", "DATETIME" };

        /// <summary>Nomes aceitos para a coluna de identificação do dispositivo.</summary>
        public static readonly string[] ColunaHostname = { "HOSTNAME", "HOST", "DISPOSITIVO", "SENSOR" };

        /// <summary>Nomes aceitos para a coluna de endereço físico (a planilha grafa "ADRESS").</summary>
        public static readonly string[] ColunaEnderecoMac = { "MAC ADRESS", "MAC ADDRESS", "MAC" };

        /// <summary>Nomes aceitos para a coluna de temperatura (°C).</summary>
        public static readonly string[] ColunaTemperatura = { "TEMPERATURA", "TEMPERATURE", "TEMP" };

        /// <summary>Nomes aceitos para a coluna de umidade relativa (%).</summary>
        public static readonly string[] ColunaUmidade = { "UMIDADE", "HUMIDITY", "UMID" };

        /// <summary>Formatos de data e hora tentados, do mais específico ao mais frouxo.</summary>
        public static readonly string[] FormatosDeTimestamp =
        {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm",
            "dd/MM/yyyy HH:mm:ss",
        };

        /// <summary>
        /// Padrão do HOSTNAME: instituição (2 letras) + unidade (3 letras) + sala (letras e
        /// dígitos) + "-" + número do dispositivo. Ex.: <c>MMCCTLAB001-001</c>.
        /// </summary>
        public const string PadraoDeHostname =
            @"^(?<instituicao>[A-Z]{2})(?<unidade>[A-Z]{3})(?<sala>[A-Z]+\d+)-(?<dispositivo>\d+)$";

        /// <summary>Janela padrão da série temporal exibida, em horas, contada da última leitura.</summary>
        public const int JanelaPadraoDaSerieEmHoras = 6;

        /// <summary>
        /// A cada quantos ciclos o leitor volta a baixar a planilha INTEIRA em vez de só as
        /// linhas novas. O incremento é o caminho normal (barato); a recarga completa corrige
        /// edições e exclusões feitas à mão na planilha, que o incremento não enxerga.
        /// </summary>
        public const int CiclosEntreRecargasCompletas = 30;
    }

    /// <summary>
    /// Onde o plug-in guarda o que o usuário cadastra (planilhas e sensores), fora do modelo
    /// Revit e fora da pasta de instalação — que o MSI sobrescreve a cada versão.
    /// </summary>
    public static class ConfiguracaoDeArmazenamento
    {
        /// <summary>Subpasta em %APPDATA%.</summary>
        public const string NomeDaPasta = "MackTwin";

        /// <summary>Arquivo JSON com as planilhas cadastradas.</summary>
        public const string NomeDoArquivoDePlanilhas = "planilhas.json";

        /// <summary>Pasta de dados do usuário: <c>%APPDATA%\MackTwin</c>.</summary>
        public static string PastaDeDados => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), NomeDaPasta);

        /// <summary>Caminho completo do cadastro de planilhas.</summary>
        public static string CaminhoDoArquivoDePlanilhas => Path.Combine(PastaDeDados, NomeDoArquivoDePlanilhas);
    }

    // ── Revit ───────────────────────────────────────────────────────────

    /// <summary>
    /// Composição do nome das vistas 3D geradas: <c>MackTwin_Conforto_v{NN}_{aaaa-MM-dd_HHmm}</c>.
    /// </summary>
    /// <remarks>
    /// O <b>vNN</b> diz qual é a mais recente sem exigir leitura de data; o <b>carimbo</b>
    /// registra quando a figura foi produzida. Os dois dígitos da versão fazem a ordenação
    /// alfabética do Project Browser coincidir com a cronológica.
    /// </remarks>
    public static class ConfiguracaoDeNomeDeVista
    {
        /// <summary>Prefixo de toda vista criada pelo plug-in.</summary>
        public const string Prefixo = "MackTwin";

        /// <summary>Assunto da vista de conforto.</summary>
        public const string AssuntoConforto = "Conforto";

        /// <summary>Marcador que precede o número da versão.</summary>
        public const string PrefixoDeVersao = "v";

        /// <summary>Formato do número de versão: dois dígitos com zero à esquerda.</summary>
        public const string FormatoDaVersao = "00";

        /// <summary>Formato do carimbo de tempo (ISO, sem segundos — a versão já garante unicidade).</summary>
        public const string FormatoDoCarimboDeTempo = "yyyy-MM-dd_HHmm";

        /// <summary>Teto de tentativas ao procurar um número de versão livre.</summary>
        public const int LimiteDeTentativasDeVersao = 999;
    }

    /// <summary>Nomes das transações e parâmetros da pintura das salas no modelo.</summary>
    public static class ConfiguracaoDePinturaDeAmbientes
    {
        /// <summary>Transação que aplica as cores de conforto aos Rooms da vista ativa.</summary>
        public const string NomeDaTransacaoDePintura = "MackTwin — Pintar ambientes por conforto";

        /// <summary>Transação que remove as sobreposições gráficas aplicadas pelo plug-in.</summary>
        public const string NomeDaTransacaoDeLimpeza = "MackTwin — Limpar pintura dos ambientes";

        /// <summary>Transação que cria a vista 3D de conforto.</summary>
        public const string NomeDaTransacaoDeVista = "MackTwin — Gerar vista 3D de conforto";

        /// <summary>
        /// Transparência (0–100) aplicada a paredes, pisos, portas e janelas na vista 3D de
        /// conforto, para que os volumes coloridos das salas fiquem visíveis de fora.
        /// </summary>
        public const int TransparenciaDoEnvoltorio = 70;
    }

    // ── Ribbon ──────────────────────────────────────────────────────────

    /// <summary>
    /// Configurações da aba e dos botões que o plug-in cria na ribbon do Revit.
    /// </summary>
    /// <remarks>
    /// Quem monta a ribbon é <see cref="AplicacaoRibbonMackTwin"/>, registrado no
    /// <c>MackTwin.addin</c> como <c>Type="Application"</c>. Estas constantes não afetam o
    /// desenvolvimento via AddinManager, que executa o comando diretamente.
    /// </remarks>
    public static class ConfiguracaoDaRibbon
    {
        /// <summary>Nome da aba própria do plug-in na ribbon.</summary>
        public const string NomeDaAba = "MackTwin";

        /// <summary>Nome do painel dentro da aba.</summary>
        public const string NomeDoPainel = "Sensores";

        /// <summary>
        /// Identificador interno do botão principal. Precisa ser único entre todos os addins
        /// carregados; o Revit rejeita o painel inteiro em caso de colisão.
        /// </summary>
        public const string NomeInternoDoBotao = "BotaoPrincipalMackTwin";

        /// <summary>Rótulo do botão principal. A quebra de linha é intencional.</summary>
        public const string TextoDoBotao = "Painel de\nSensores";

        /// <summary>Tooltip curto do botão principal.</summary>
        public const string DicaDoBotao = "Abre o painel de sensores por ambiente.";

        /// <summary>Descrição longa do botão principal.</summary>
        public const string DescricaoLongaDoBotao =
            "Lê as planilhas dos sensores do CCT Mackenzie Campinas, associa cada sensor à sua " +
            "sala pelo nome do dispositivo e mostra as leituras em cartões, série temporal e " +
            "sobre o próprio modelo. Requer uma planta ou vista 3D ativa.";

        /// <summary>Identificador interno do botão de verificação. Único, como o principal.</summary>
        public const string NomeInternoDoBotaoDeVerificacao = "BotaoVerificacaoMackTwin";

        /// <summary>Rótulo do botão que roda as verificações.</summary>
        public const string TextoDoBotaoDeVerificacao = "Verificar\nmodelo";

        /// <summary>Tooltip curto do botão de verificação.</summary>
        public const string DicaDoBotaoDeVerificacao =
            "Confere a leitura da planilha, a identificação das salas e as faixas de conforto.";

        /// <summary>Descrição longa do botão de verificação.</summary>
        public const string DescricaoLongaDoBotaoDeVerificacao =
            "Executa as verificações automáticas das partes puras do plug-in: análise do CSV com " +
            "vírgula decimal, extração da sala a partir do HOSTNAME, casamento sala↔sensor, " +
            "faixas de conforto e resumo por ambiente. Não altera o modelo aberto nem acessa a rede.";

        /// <summary>
        /// Nome do arquivo do ícone do botão: recorte do círculo-M do logo Mackenzie, 64 px,
        /// gerado a partir de <c>logo-mackenzie.png</c>. Sem espaços no nome, para que o
        /// nome do recurso embutido e o caminho em disco sejam idênticos ao que está aqui.
        /// </summary>
        public const string NomeDoArquivoDoIcone = "icone-macktwin.png";

        /// <summary>Caminho do ícone relativo à pasta de instalação do plug-in.</summary>
        public const string CaminhoRelativoDoIcone = @"Assets\" + NomeDoArquivoDoIcone;

        /// <summary>
        /// Nome do recurso embutido correspondente, usado quando o arquivo em disco não existe
        /// (caso do AddinManager, que copia só o DLL para %TEMP%). O MSBuild preserva o espaço do
        /// nome do arquivo ao gerar o nome do recurso.
        /// </summary>
        public const string NomeDoRecursoDoIcone = "MackTwin.Assets." + NomeDoArquivoDoIcone;

        /// <summary>Largura, em pixels, do ícone grande do botão.</summary>
        public const int LarguraDoIconeGrande = 32;

        /// <summary>Largura, em pixels, do ícone pequeno do botão.</summary>
        public const int LarguraDoIconePequeno = 16;
    }
}
