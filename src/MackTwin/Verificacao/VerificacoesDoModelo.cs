using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MackTwin.Configuracao;
using MackTwin.Dominio;
using MackTwin.Parsing;
using MackTwin.Processamento;

namespace MackTwin.Verificacao
{
    /// <summary>Resultado de uma verificação isolada.</summary>
    public sealed class ResultadoDeVerificacao
    {
        /// <summary>O que se está afirmando, em uma linha.</summary>
        public string Afirmacao { get; init; } = string.Empty;

        /// <summary>Se a afirmação se sustentou.</summary>
        public bool Passou { get; init; }

        /// <summary>Esperado contra obtido, preenchido quando falha.</summary>
        public string Detalhe { get; init; } = string.Empty;
    }

    /// <summary>Relatório de uma execução completa.</summary>
    public sealed class RelatorioDeVerificacao
    {
        /// <summary>Todas as verificações, na ordem de execução.</summary>
        public IReadOnlyList<ResultadoDeVerificacao> Resultados { get; init; } = Array.Empty<ResultadoDeVerificacao>();

        /// <summary>Quantas passaram.</summary>
        public int Passaram => Resultados.Count(r => r.Passou);

        /// <summary>Quantas falharam.</summary>
        public int Falharam => Resultados.Count(r => !r.Passou);

        /// <summary>Se todas passaram.</summary>
        public bool TudoCerto => Falharam == 0;

        /// <summary>Relatório em texto, pronto para exibir ou gravar.</summary>
        public string ComoTexto()
        {
            var linhas = new List<string>
            {
                $"Verificação do MackTwin — {Passaram} de {Resultados.Count} afirmações sustentadas.",
                string.Empty,
            };

            foreach (var resultado in Resultados)
            {
                linhas.Add($"[{(resultado.Passou ? "ok  " : "FALHA")}] {resultado.Afirmacao}");

                if (!resultado.Passou && !string.IsNullOrWhiteSpace(resultado.Detalhe))
                    linhas.Add($"          {resultado.Detalhe}");
            }

            return string.Join(Environment.NewLine, linhas);
        }
    }

    /// <summary>
    /// Verificações automáticas das partes puras do plug-in: tudo o que converte a planilha em
    /// salas coloridas, sem rede e sem modelo aberto.
    /// </summary>
    /// <remarks>
    /// <b>Por que aqui, e não num projeto de teste.</b> O projeto não admite pacote NuGet novo,
    /// o que exclui os arranjos usuais de teste, e o Smart App Control do Windows bloqueia a
    /// execução de binário recém-compilado. Dentro do Revit nenhuma das duas se aplica — o
    /// assembly já está carregado —, e por isso as verificações são disparadas por um botão.
    ///
    /// <b>O que se verifica.</b> A análise do CSV com a convenção exata da planilha (vírgula
    /// decimal entre aspas, carimbo sem fuso, linhas inválidas e repetidas), a montagem da URL de
    /// exportação, a extração da sala a partir do HOSTNAME, a normalização do código de sala que
    /// casa sensor e Room, o agrupamento em ambientes, as faixas de conforto com os seus extremos
    /// e o resumo por sala com a janela contada da última leitura.
    ///
    /// <b>O que NÃO se verifica.</b> O download (depende de rede), a leitura de Rooms e a pintura
    /// (dependem do Revit). Essas pedem o modelo de teste com salas numeradas.
    /// </remarks>
    public static class VerificacoesDoModelo
    {
        /// <summary>Folga aceita nas comparações de ponto flutuante.</summary>
        private const double Folga = 1e-6;

        private const string Edicao =
            "https://docs.google.com/spreadsheets/d/1-fiPdqoIxaA_eudZa2jkz71lUCgSBbBMcOSLqxwhHo8/edit?gid=0";

        private const string Exportacao =
            "https://docs.google.com/spreadsheets/d/1-fiPdqoIxaA_eudZa2jkz71lUCgSBbBMcOSLqxwhHo8/export?format=csv&gid=0";

        /// <summary>Executa todas as verificações e devolve o relatório.</summary>
        public static RelatorioDeVerificacao ExecutarTudo()
        {
            var resultados = new List<ResultadoDeVerificacao>();

            VerificarConversaoDeNumeros(resultados);
            VerificarAnaliseDoCsv(resultados);
            VerificarUrlDeExportacao(resultados);
            VerificarHostname(resultados);
            VerificarCodigoDeSala(resultados);
            VerificarAgrupamento(resultados);
            VerificarClassificacao(resultados);
            VerificarResumo(resultados);
            VerificarCores(resultados);

            return new RelatorioDeVerificacao { Resultados = resultados };
        }

        // ── Números com vírgula decimal ──────────────────────────────────

        private static void VerificarConversaoDeNumeros(List<ResultadoDeVerificacao> resultados)
        {
            Afirmar(resultados, "\"24,20\" (vírgula decimal, como a planilha grava) converte em 24,20",
                    UtilitariosDeCsv.TentarConverterNumero("24,20", out double a) && Math.Abs(a - 24.20) < Folga,
                    $"obtido {Formatar(a)}");

            Afirmar(resultados, "\"\\\"76,40\\\"\" (ainda entre aspas) converte em 76,40",
                    UtilitariosDeCsv.TentarConverterNumero("\"76,40\"", out double b) && Math.Abs(b - 76.40) < Folga,
                    $"obtido {Formatar(b)}");

            Afirmar(resultados, "\"24.07\" (ponto decimal) converte em 24,07",
                    UtilitariosDeCsv.TentarConverterNumero("24.07", out double c) && Math.Abs(c - 24.07) < Folga,
                    $"obtido {Formatar(c)}");

            Afirmar(resultados, "\"1.234,56\" (ponto de milhar, vírgula decimal) converte em 1234,56",
                    UtilitariosDeCsv.TentarConverterNumero("1.234,56", out double d) && Math.Abs(d - 1234.56) < Folga,
                    $"obtido {Formatar(d)}");

            Afirmar(resultados, "\"1,234.56\" (vírgula de milhar, ponto decimal) converte em 1234,56",
                    UtilitariosDeCsv.TentarConverterNumero("1,234.56", out double e) && Math.Abs(e - 1234.56) < Folga,
                    $"obtido {Formatar(e)}");

            Afirmar(resultados, "\"-3,5\" negativo converte em -3,5",
                    UtilitariosDeCsv.TentarConverterNumero("-3,5", out double f) && Math.Abs(f + 3.5) < Folga,
                    $"obtido {Formatar(f)}");

            Afirmar(resultados, "\"--\" não é número",
                    !UtilitariosDeCsv.TentarConverterNumero("--", out _));

            Afirmar(resultados, "Texto vazio não é número",
                    !UtilitariosDeCsv.TentarConverterNumero("", out _));

            Afirmar(resultados, "Endereço de rede \"181.213.132.2\" não é número",
                    !UtilitariosDeCsv.TentarConverterNumero("181.213.132.2", out _));
        }

        // ── Análise do CSV da planilha ───────────────────────────────────

        private static void VerificarAnaliseDoCsv(List<ResultadoDeVerificacao> resultados)
        {
            // Convenção exata da planilha do DHT22: BOM, CRLF, decimais com vírgula entre aspas,
            // mais uma duplicata (mesmo instante e sensor), uma linha com data inválida e uma
            // leitura sem umidade.
            const string csv =
                "﻿TIMESTAMP,HOSTNAME,MAC ADRESS,TEMPERATURA,UMIDADE\r\n" +
                "2026-10-15 12:22:24,MMCCTLAB001-001,EC:64:C9:CD:C2:C7,\"24,20\",\"76,40\"\r\n" +
                "2026-10-15 12:21:39,MMCCTLAB001-001,EC:64:C9:CD:C2:C7,\"24,10\",\"76,90\"\r\n" +
                "2026-10-15 12:22:24,MMCCTLAB001-001,EC:64:C9:CD:C2:C7,\"99,00\",\"99,00\"\r\n" +
                "isto não é data,MMCCTLAB001-001,EC:64:C9:CD:C2:C7,\"24,00\",\"70,00\"\r\n" +
                "2026-10-15 12:23:09,MMCCTLAB002-001,AA:BB:CC:DD:EE:FF,\"27,50\",\r\n";

            var avisos = new List<string>();
            var leituras = LeitorDePlanilhaGoogle.AnalisarCsv(csv, avisos);

            Afirmar(resultados, "O CSV da planilha produz 3 leituras válidas (1 duplicata e 1 linha inválida fora)",
                    leituras.Count == 3, $"obtido {leituras.Count}");

            if (leituras.Count == 3)
            {
                Afirmar(resultados, "As leituras saem em ordem cronológica, mesmo vindo fora de ordem",
                        leituras[0].Timestamp == new DateTime(2026, 10, 15, 12, 21, 39)
                        && leituras[2].Timestamp == new DateTime(2026, 10, 15, 12, 23, 9));

                var segunda = leituras[1];

                Afirmar(resultados, "\"24,20\" entre aspas vira 24,20 °C",
                        Math.Abs(segunda.Temperatura - 24.20) < Folga, $"obtido {Formatar(segunda.Temperatura)}");

                Afirmar(resultados, "\"76,40\" entre aspas vira 76,40 %",
                        Math.Abs(segunda.Umidade - 76.40) < Folga, $"obtido {Formatar(segunda.Umidade)}");

                Afirmar(resultados, "Na duplicata (mesmo instante e sensor) prevalece a primeira leitura",
                        Math.Abs(segunda.Temperatura - 24.20) < Folga && Math.Abs(segunda.Temperatura - 99.0) > 1.0);

                Afirmar(resultados, "HOSTNAME e endereço MAC são preservados como texto",
                        segunda.Hostname == "MMCCTLAB001-001" && segunda.EnderecoMac == "EC:64:C9:CD:C2:C7",
                        $"obtido \"{segunda.Hostname}\" / \"{segunda.EnderecoMac}\"");

                Afirmar(resultados, "O carimbo é lido sem fuso, como hora local do dispositivo",
                        segunda.Timestamp.Kind == DateTimeKind.Unspecified && segunda.Timestamp.Second == 24);

                var terceira = leituras[2];

                Afirmar(resultados, "Umidade vazia vira NaN sem derrubar a linha (temperatura 27,50 mantida)",
                        !terceira.TemUmidade && terceira.TemTemperatura && Math.Abs(terceira.Temperatura - 27.5) < Folga);
            }

            Afirmar(resultados, "A linha com data inválida é contada nos avisos",
                    avisos.Any(a => a.StartsWith("1 linha(s) descartada", StringComparison.Ordinal)),
                    string.Join(" | ", avisos));

            Afirmar(resultados, "A duplicata é contada nos avisos",
                    avisos.Any(a => a.StartsWith("1 leitura(s) repetida", StringComparison.Ordinal)),
                    string.Join(" | ", avisos));

            var comPontoEVirgula = LeitorDePlanilhaGoogle.AnalisarCsv(
                "TIMESTAMP;HOSTNAME;TEMPERATURA;UMIDADE\n2026-01-02 03:04:05;MMCCTLAB003-001;22,5;50,0\n");

            Afirmar(resultados, "Delimitador ponto e vírgula também é reconhecido (sem coluna de MAC)",
                    comPontoEVirgula.Count == 1
                    && Math.Abs(comPontoEVirgula[0].Temperatura - 22.5) < Folga
                    && comPontoEVirgula[0].EnderecoMac.Length == 0);

            Afirmar(resultados, "Cabeçalho em caixa baixa e com espaços (\" timestamp \") é reconhecido",
                    LeitorDePlanilhaGoogle.AnalisarCsv(" timestamp , hostname ,temperatura,umidade\n2026-01-02 03:04:05,MMCCTLAB003-001,22,50\n").Count == 1);

            Afirmar(resultados, "Resposta HTML (planilha não pública) é recusada com explicação",
                    Lanca<InvalidOperationException>(() => LeitorDePlanilhaGoogle.AnalisarCsv("<!DOCTYPE html><html><head>Login</head></html>")));

            Afirmar(resultados, "Texto vazio é recusado",
                    Lanca<InvalidOperationException>(() => LeitorDePlanilhaGoogle.AnalisarCsv("")));

            Afirmar(resultados, "Cabeçalho sem TIMESTAMP/HOSTNAME é recusado",
                    Lanca<InvalidOperationException>(() => LeitorDePlanilhaGoogle.AnalisarCsv("A,B,C\n1,2,3\n")));

            Afirmar(resultados, "Planilha só com cabeçalho produz zero leituras, sem erro",
                    LeitorDePlanilhaGoogle.AnalisarCsv("TIMESTAMP,HOSTNAME,TEMPERATURA,UMIDADE\n").Count == 0);
        }

        // ── URL de exportação ────────────────────────────────────────────

        private static void VerificarUrlDeExportacao(List<ResultadoDeVerificacao> resultados)
        {
            Afirmar(resultados, "O endereço de edição vira a URL de exportação CSV da aba 0",
                    LeitorDePlanilhaGoogle.MontarUrlDeExportacaoCsv(Edicao) == Exportacao,
                    $"obtido {LeitorDePlanilhaGoogle.MontarUrlDeExportacaoCsv(Edicao)}");

            Afirmar(resultados, "A própria URL de exportação é aceita e devolvida igual",
                    LeitorDePlanilhaGoogle.MontarUrlDeExportacaoCsv(Exportacao) == Exportacao);

            Afirmar(resultados, "O gid após '#' (edit#gid=123) é respeitado",
                    LeitorDePlanilhaGoogle.MontarUrlDeExportacaoCsv(
                        "https://docs.google.com/spreadsheets/d/1-fiPdqoIxaA_eudZa2jkz71lUCgSBbBMcOSLqxwhHo8/edit#gid=123")
                        .EndsWith("gid=123", StringComparison.Ordinal));

            Afirmar(resultados, "Sem gid, assume-se a aba 0",
                    LeitorDePlanilhaGoogle.MontarUrlDeExportacaoCsv(
                        "https://docs.google.com/spreadsheets/d/1-fiPdqoIxaA_eudZa2jkz71lUCgSBbBMcOSLqxwhHo8/edit")
                        .EndsWith("gid=0", StringComparison.Ordinal));

            Afirmar(resultados, "Espaços nas pontas do endereço são tolerados",
                    LeitorDePlanilhaGoogle.MontarUrlDeExportacaoCsv("  " + Edicao + "  ") == Exportacao);

            Afirmar(resultados, "Endereço que não é de planilha Google é recusado",
                    Lanca<ArgumentException>(() => LeitorDePlanilhaGoogle.MontarUrlDeExportacaoCsv("https://exemplo.com/planilha")));

            Afirmar(resultados, "Endereço vazio é recusado",
                    Lanca<ArgumentException>(() => LeitorDePlanilhaGoogle.MontarUrlDeExportacaoCsv("")));
        }

        // ── HOSTNAME → sala ──────────────────────────────────────────────

        private static void VerificarHostname(List<ResultadoDeVerificacao> resultados)
        {
            bool reconhecido = AnalisadorDeHostname.TentarIdentificar("MMCCTLAB001-001", out var id);

            Afirmar(resultados, "MMCCTLAB001-001 é reconhecido", reconhecido);

            if (reconhecido && id != null)
            {
                Afirmar(resultados, "MMCCTLAB001-001 → instituição MM", id.Instituicao == "MM", $"obtido {id.Instituicao}");
                Afirmar(resultados, "MMCCTLAB001-001 → unidade CCT", id.Unidade == "CCT", $"obtido {id.Unidade}");
                Afirmar(resultados, "MMCCTLAB001-001 → sala LAB001", id.CodigoDaSala == "LAB001", $"obtido {id.CodigoDaSala}");
                Afirmar(resultados, "MMCCTLAB001-001 → dispositivo \"001\" (zeros preservados)",
                        id.NumeroDoDispositivo == "001", $"obtido {id.NumeroDoDispositivo}");
            }

            Afirmar(resultados, "Caixa baixa (mmcctlab001-001) é aceita e normalizada para LAB001",
                    AnalisadorDeHostname.ExtrairCodigoDaSala("mmcctlab001-001") == "LAB001");

            Afirmar(resultados, "Espaços nas pontas são tolerados",
                    AnalisadorDeHostname.TentarIdentificar(" MMCCTLAB001-002 ", out var comEspacos)
                    && comEspacos.NumeroDoDispositivo == "002");

            Afirmar(resultados, "Um segundo sensor da mesma sala (MMCCTLAB001-002) aponta para LAB001",
                    AnalisadorDeHostname.ExtrairCodigoDaSala("MMCCTLAB001-002") == "LAB001");

            Afirmar(resultados, "Outra sala (MMCCTSALA12-003) → SALA12",
                    AnalisadorDeHostname.ExtrairCodigoDaSala("MMCCTSALA12-003") == "SALA12");

            Afirmar(resultados, "Sem número de dispositivo (MMCCTLAB001) não é reconhecido",
                    !AnalisadorDeHostname.TentarIdentificar("MMCCTLAB001", out _));

            Afirmar(resultados, "Nome fora do padrão (SENSOR-X) não é reconhecido",
                    !AnalisadorDeHostname.TentarIdentificar("SENSOR-X", out _));

            Afirmar(resultados, "Vazio e nulo não são reconhecidos",
                    !AnalisadorDeHostname.TentarIdentificar("", out _) && !AnalisadorDeHostname.TentarIdentificar(null, out _));
        }

        // ── Código de sala (chave sensor ↔ Room) ────────────────────────

        private static void VerificarCodigoDeSala(List<ResultadoDeVerificacao> resultados)
        {
            Afirmar(resultados, "\"LAB-001\" normaliza para LAB001", CodigoDeSala.Normalizar("LAB-001") == "LAB001");
            Afirmar(resultados, "\" lab 001 \" normaliza para LAB001", CodigoDeSala.Normalizar(" lab 001 ") == "LAB001");
            Afirmar(resultados, "\"Lab_001.\" normaliza para LAB001", CodigoDeSala.Normalizar("Lab_001.") == "LAB001");
            Afirmar(resultados, "Nulo normaliza para vazio", CodigoDeSala.Normalizar(null).Length == 0);

            Afirmar(resultados, "Room \"lab-001\" casa com o sensor LAB001",
                    CodigoDeSala.SaoIguais("LAB001", "lab-001"));

            Afirmar(resultados, "LAB001 e LAB1 são salas distintas (zeros à esquerda contam)",
                    !CodigoDeSala.SaoIguais("LAB001", "LAB1"));

            Afirmar(resultados, "Vazio nunca casa com nada, nem com vazio",
                    !CodigoDeSala.SaoIguais("", "") && !CodigoDeSala.SaoIguais("LAB001", ""));
        }

        // ── Agrupamento em ambientes ─────────────────────────────────────

        private static void VerificarAgrupamento(List<ResultadoDeVerificacao> resultados)
        {
            var leituras = new List<Leitura>
            {
                Leitura("2026-10-15 10:00:00", "MMCCTLAB001-001", 24, 50),
                Leitura("2026-10-15 09:00:00", "MMCCTLAB001-001", 23, 55), // fora de ordem
                Leitura("2026-10-15 10:00:30", "MMCCTLAB001-002", 25, 52),
                Leitura("2026-10-15 10:01:00", "MMCCTLAB002-001", 28, 70),
                Leitura("2026-10-15 10:02:00", "ESTRANHO-01", 20, 40),
            };

            var avisos = new List<string>();
            var conjunto = AgrupadorDeAmbientes.Agrupar(leituras, avisos);

            Afirmar(resultados, "Quatro sensores com três padrões de sala produzem 2 ambientes",
                    conjunto.Ambientes.Count == 2, $"obtido {conjunto.Ambientes.Count}");

            var lab001 = conjunto.Localizar("LAB001");

            Afirmar(resultados, "LAB001 reúne os dois sensores -001 e -002 sem cadastro manual",
                    lab001 != null && lab001.Sensores.Count == 2, $"obtido {lab001?.Sensores.Count}");

            Afirmar(resultados, "Localizar aceita qualquer grafia do código (\"lab-001\")",
                    conjunto.Localizar("lab-001") == lab001);

            Afirmar(resultados, "O sensor fora do padrão vai para \"sem sala\", sem ser perdido",
                    conjunto.SensoresSemSala.Count == 1 && conjunto.SensoresSemSala[0].Hostname == "ESTRANHO-01"
                    && conjunto.SensoresSemSala[0].Leituras.Count == 1);

            var sensor001 = lab001?.Sensores.FirstOrDefault(s => s.Hostname == "MMCCTLAB001-001");

            Afirmar(resultados, "As leituras de cada sensor ficam em ordem cronológica",
                    sensor001 != null && sensor001.Leituras[0].Timestamp.Hour == 9 && sensor001.Leituras[1].Timestamp.Hour == 10);

            Afirmar(resultados, "A última leitura da sala é a mais recente entre todos os seus sensores (10:00:30, do -002)",
                    lab001?.UltimaLeitura != null && lab001.UltimaLeitura.Timestamp == new DateTime(2026, 10, 15, 10, 0, 30));

            Afirmar(resultados, "Os ambientes saem em ordem de código", conjunto.Ambientes[0].Codigo == "LAB001" && conjunto.Ambientes[1].Codigo == "LAB002");

            Afirmar(resultados, "Totais: 4 sensores e 5 leituras",
                    conjunto.TotalDeSensores == 4 && conjunto.TotalDeLeituras == 5,
                    $"obtido {conjunto.TotalDeSensores} / {conjunto.TotalDeLeituras}");

            Afirmar(resultados, "O sensor sem sala gera aviso explicativo",
                    avisos.Any(a => a.Contains("ESTRANHO-01", StringComparison.Ordinal) && a.Contains("padrão", StringComparison.Ordinal)));

            Afirmar(resultados, "Sem leituras, o conjunto é vazio e não falha",
                    AgrupadorDeAmbientes.Agrupar(Array.Empty<Leitura>()).Ambientes.Count == 0);
        }

        // ── Faixas de conforto ───────────────────────────────────────────

        private static void VerificarClassificacao(List<ResultadoDeVerificacao> resultados)
        {
            Afirmar(resultados, "20,9 °C está abaixo da faixa",
                    ClassificadorDeConforto.ClassificarTemperatura(20.9) == ClassificacaoDeConforto.AbaixoDaFaixa);
            Afirmar(resultados, "21,0 °C (extremo inferior) é confortável",
                    ClassificadorDeConforto.ClassificarTemperatura(21.0) == ClassificacaoDeConforto.Confortavel);
            Afirmar(resultados, "23,5 °C é confortável",
                    ClassificadorDeConforto.ClassificarTemperatura(23.5) == ClassificacaoDeConforto.Confortavel);
            Afirmar(resultados, "26,0 °C (extremo superior) é confortável",
                    ClassificadorDeConforto.ClassificarTemperatura(26.0) == ClassificacaoDeConforto.Confortavel);
            Afirmar(resultados, "26,1 °C está acima da faixa",
                    ClassificadorDeConforto.ClassificarTemperatura(26.1) == ClassificacaoDeConforto.AcimaDaFaixa);
            Afirmar(resultados, "Temperatura NaN é \"sem dados\"",
                    ClassificadorDeConforto.ClassificarTemperatura(double.NaN) == ClassificacaoDeConforto.SemDados);

            Afirmar(resultados, "34,9 % está abaixo da faixa",
                    ClassificadorDeConforto.ClassificarUmidade(34.9) == ClassificacaoDeConforto.AbaixoDaFaixa);
            Afirmar(resultados, "35 % e 65 % (extremos) são confortáveis",
                    ClassificadorDeConforto.ClassificarUmidade(35.0) == ClassificacaoDeConforto.Confortavel
                    && ClassificadorDeConforto.ClassificarUmidade(65.0) == ClassificacaoDeConforto.Confortavel);
            Afirmar(resultados, "65,1 % está acima da faixa",
                    ClassificadorDeConforto.ClassificarUmidade(65.1) == ClassificacaoDeConforto.AcimaDaFaixa);
            Afirmar(resultados, "76,4 % (leitura real da planilha) está acima da faixa",
                    ClassificadorDeConforto.ClassificarUmidade(76.4) == ClassificacaoDeConforto.AcimaDaFaixa);

            Afirmar(resultados, "Confortável + acima → acima",
                    ClassificadorDeConforto.Combinar(ClassificacaoDeConforto.Confortavel, ClassificacaoDeConforto.AcimaDaFaixa) == ClassificacaoDeConforto.AcimaDaFaixa);
            Afirmar(resultados, "Abaixo + confortável → abaixo",
                    ClassificadorDeConforto.Combinar(ClassificacaoDeConforto.AbaixoDaFaixa, ClassificacaoDeConforto.Confortavel) == ClassificacaoDeConforto.AbaixoDaFaixa);
            Afirmar(resultados, "Abaixo + acima → acima (determinístico)",
                    ClassificadorDeConforto.Combinar(ClassificacaoDeConforto.AbaixoDaFaixa, ClassificacaoDeConforto.AcimaDaFaixa) == ClassificacaoDeConforto.AcimaDaFaixa);
            Afirmar(resultados, "Sem dados + confortável → confortável (a grandeza ausente não penaliza)",
                    ClassificadorDeConforto.Combinar(ClassificacaoDeConforto.SemDados, ClassificacaoDeConforto.Confortavel) == ClassificacaoDeConforto.Confortavel);
            Afirmar(resultados, "Sem dados + sem dados → sem dados",
                    ClassificadorDeConforto.Combinar(ClassificacaoDeConforto.SemDados, ClassificacaoDeConforto.SemDados) == ClassificacaoDeConforto.SemDados);
            Afirmar(resultados, "Combinar de lista vazia → sem dados",
                    ClassificadorDeConforto.Combinar(Array.Empty<ClassificacaoDeConforto>()) == ClassificacaoDeConforto.SemDados);
        }

        // ── Resumo por ambiente ──────────────────────────────────────────

        private static void VerificarResumo(List<ResultadoDeVerificacao> resultados)
        {
            const string host = "MMCCTLAB001-001";
            var ambiente = new Ambiente { Codigo = "LAB001" };
            var sensor = new Sensor { Hostname = host };
            sensor.Leituras.Add(Leitura("2026-10-15 00:00:00", host, 20, 40));
            sensor.Leituras.Add(Leitura("2026-10-15 05:00:00", host, 24, 50));
            sensor.Leituras.Add(Leitura("2026-10-15 10:00:00", host, 27, 70));
            ambiente.Sensores.Add(sensor);

            var seisHoras = CalculadorDeResumoDoAmbiente.Resumir(ambiente, TimeSpan.FromHours(6));

            Afirmar(resultados, "A janela de 6 h conta da ÚLTIMA leitura para trás: 2 das 3 leituras entram",
                    seisHoras.TotalDeLeituras == 2, $"obtido {seisHoras.TotalDeLeituras}");

            Afirmar(resultados, "Temperatura na janela: última 27, média 25,5, mín 24, máx 27",
                    Math.Abs(seisHoras.Temperatura.Ultimo - 27) < Folga
                    && Math.Abs(seisHoras.Temperatura.Media - 25.5) < Folga
                    && Math.Abs(seisHoras.Temperatura.Minimo - 24) < Folga
                    && Math.Abs(seisHoras.Temperatura.Maximo - 27) < Folga,
                    $"obtido {Formatar(seisHoras.Temperatura.Ultimo)} / {Formatar(seisHoras.Temperatura.Media)} / " +
                    $"{Formatar(seisHoras.Temperatura.Minimo)} / {Formatar(seisHoras.Temperatura.Maximo)}");

            Afirmar(resultados, "A classificação é pela ÚLTIMA leitura (27 °C → acima), não pela média (25,5 → confortável)",
                    seisHoras.Temperatura.Classificacao == ClassificacaoDeConforto.AcimaDaFaixa);

            Afirmar(resultados, "Umidade na janela: última 70 → acima",
                    Math.Abs(seisHoras.Umidade.Ultimo - 70) < Folga && seisHoras.Umidade.Classificacao == ClassificacaoDeConforto.AcimaDaFaixa);

            Afirmar(resultados, "Classificação geral da sala: acima",
                    seisHoras.ClassificacaoGeral == ClassificacaoDeConforto.AcimaDaFaixa);

            Afirmar(resultados, "Início e fim da janela são a primeira e a última leitura consideradas",
                    seisHoras.Inicio == new DateTime(2026, 10, 15, 5, 0, 0) && seisHoras.Fim == new DateTime(2026, 10, 15, 10, 0, 0));

            Afirmar(resultados, "O resumo registra 1 sensor na sala", seisHoras.QuantidadeDeSensores == 1);

            var tudo = CalculadorDeResumoDoAmbiente.Resumir(ambiente, null);

            Afirmar(resultados, "Janela nula considera tudo: 3 leituras, mínimos 20 °C e 40 %",
                    tudo.TotalDeLeituras == 3
                    && Math.Abs(tudo.Temperatura.Minimo - 20) < Folga
                    && Math.Abs(tudo.Umidade.Minimo - 40) < Folga);

            var vazio = CalculadorDeResumoDoAmbiente.Resumir(new Ambiente { Codigo = "VAZIO" }, null);

            Afirmar(resultados, "Sala sem leituras: sem última leitura, sem dados, classificação \"sem dados\"",
                    vazio.UltimaLeitura == null && !vazio.Temperatura.TemDados && vazio.TotalDeLeituras == 0
                    && vazio.ClassificacaoGeral == ClassificacaoDeConforto.SemDados);

            var soTemperatura = new Ambiente { Codigo = "SOTEMP" };
            var sensorParcial = new Sensor { Hostname = "MMCCTSOTEMP-001" };
            sensorParcial.Leituras.Add(Leitura("2026-10-15 10:00:00", "MMCCTSOTEMP-001", 22, double.NaN));
            soTemperatura.Sensores.Add(sensorParcial);
            var parcial = CalculadorDeResumoDoAmbiente.Resumir(soTemperatura, null);

            Afirmar(resultados, "Sensor só de temperatura: umidade \"sem dados\" e geral pela temperatura (22 °C → confortável)",
                    parcial.Umidade.Classificacao == ClassificacaoDeConforto.SemDados
                    && parcial.ClassificacaoGeral == ClassificacaoDeConforto.Confortavel);
        }

        // ── Cores ────────────────────────────────────────────────────────

        private static void VerificarCores(List<ResultadoDeVerificacao> resultados)
        {
            Afirmar(resultados, "Confortável é verde #1B7A50",
                    CoresDeConforto.Para(ClassificacaoDeConforto.Confortavel) == "#1B7A50");

            Afirmar(resultados, "Cada estado tem cor própria (quatro cores distintas)",
                    new[]
                    {
                        CoresDeConforto.Para(ClassificacaoDeConforto.Confortavel),
                        CoresDeConforto.Para(ClassificacaoDeConforto.AbaixoDaFaixa),
                        CoresDeConforto.Para(ClassificacaoDeConforto.AcimaDaFaixa),
                        CoresDeConforto.Para(ClassificacaoDeConforto.SemDados),
                    }.Distinct().Count() == 4);

            var (r, g, b) = CoresDeConforto.ParaRgb("#1B7A50");

            Afirmar(resultados, "#1B7A50 decompõe em (27, 122, 80)", r == 27 && g == 122 && b == 80, $"obtido ({r}, {g}, {b})");

            Afirmar(resultados, "Hexadecimal malformado é recusado",
                    Lanca<ArgumentException>(() => CoresDeConforto.ParaRgb("zzz")));
        }

        // ── Infraestrutura ───────────────────────────────────────────────

        private static void Afirmar(List<ResultadoDeVerificacao> resultados, string afirmacao, bool condicao, string detalhe = "")
        {
            resultados.Add(new ResultadoDeVerificacao
            {
                Afirmacao = afirmacao,
                Passou = condicao,
                Detalhe = condicao ? string.Empty : detalhe,
            });
        }

        private static bool Lanca<TExcecao>(Action acao) where TExcecao : Exception
        {
            try
            {
                acao();
                return false;
            }
            catch (TExcecao)
            {
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Leitura Leitura(string timestamp, string hostname, double temperatura, double umidade)
        {
            return new Leitura
            {
                Timestamp = DateTime.ParseExact(timestamp, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Hostname = hostname,
                Temperatura = temperatura,
                Umidade = umidade,
            };
        }

        private static string Formatar(double valor)
        {
            return valor.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
