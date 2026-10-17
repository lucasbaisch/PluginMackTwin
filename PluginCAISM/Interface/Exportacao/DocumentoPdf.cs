using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PluginCAISM.Interface.Exportacao
{
    /// <summary>Alinhamento de uma coluna de tabela no PDF.</summary>
    public enum AlinhamentoDaColuna
    {
        /// <summary>Texto: encosta à esquerda da coluna.</summary>
        Esquerda,

        /// <summary>Número: encosta à direita, para que as casas decimais fiquem alinhadas.</summary>
        Direita,
    }

    /// <summary>Definição de uma coluna da tabela impressa.</summary>
    public sealed class ColunaDoPdf
    {
        /// <summary>Texto do cabeçalho.</summary>
        public string Titulo { get; init; }

        /// <summary>Posição da borda esquerda da coluna, em pontos, a partir da margem.</summary>
        public double X { get; init; }

        /// <summary>Largura disponível, em pontos.</summary>
        public double Largura { get; init; }

        /// <summary>Como o conteúdo se posiciona dentro da largura.</summary>
        public AlinhamentoDaColuna Alinhamento { get; init; } = AlinhamentoDaColuna.Esquerda;
    }

    /// <summary>
    /// Gerador de PDF mínimo, suficiente para relatórios de texto e tabela.
    /// </summary>
    /// <remarks>
    /// Escreve o formato PDF 1.4 diretamente, sem biblioteca externa — a pesquisa não admite novos
    /// pacotes NuGet, e as alternativas do próprio Windows não servem: o WPF exporta XPS, não PDF, e
    /// a impressora "Microsoft Print to PDF" exigiria diálogo de impressão a cada exportação.
    ///
    /// Usa apenas as fontes base-14 (Helvetica e Helvetica-Bold), que todo leitor de PDF possui, de
    /// modo que nada precisa ser incorporado ao arquivo.
    ///
    /// <b>Limite de caracteres:</b> a codificação é WinAnsi, que cobre o português acentuado por
    /// inteiro mas não cobre grego nem subscritos. Ver <see cref="ConverterParaWinAnsi"/>.
    /// </remarks>
    public sealed class DocumentoPdf
    {
        // A4 em pontos tipográficos (1/72"), arredondado.
        private const double LarguraDaPagina = 595;
        private const double AlturaDaPagina  = 842;
        private const double Margem          = 42;

        private const string FonteNormal  = "F1";
        private const string FonteNegrito = "F2";

        private readonly List<StringBuilder> _paginas = new();
        private StringBuilder _paginaAtual;
        private double _y;

        /// <summary>Largura útil entre as margens, em pontos.</summary>
        public double LarguraUtil => LarguraDaPagina - 2 * Margem;

        /// <summary>Cria o documento já com a primeira página aberta.</summary>
        public DocumentoPdf()
        {
            NovaPagina();
        }

        /// <summary>Abre uma página nova e leva o cursor para o topo dela.</summary>
        public void NovaPagina()
        {
            _paginaAtual = new StringBuilder();
            _paginas.Add(_paginaAtual);
            _y = AlturaDaPagina - Margem;
        }

        /// <summary>
        /// Garante espaço vertical para o que vem a seguir, abrindo página nova se necessário.
        /// </summary>
        /// <returns>true se uma página nova foi aberta.</returns>
        public bool GarantirEspaco(double alturaNecessaria)
        {
            if (_y - alturaNecessaria >= Margem)
                return false;

            NovaPagina();
            return true;
        }

        /// <summary>Avança o cursor sem escrever nada.</summary>
        public void Espacar(double pontos) => _y -= pontos;

        /// <summary>
        /// Escreve uma linha de texto e avança o cursor.
        /// </summary>
        public void EscreverLinha(string texto, double tamanho = 10, bool negrito = false,
                                  double espacamentoExtra = 0)
        {
            GarantirEspaco(tamanho + 2 + espacamentoExtra);
            _y -= tamanho + 2;
            DesenharTexto(texto ?? string.Empty, Margem, _y, tamanho, negrito);
            _y -= espacamentoExtra;
        }

        /// <summary>
        /// Escreve um parágrafo quebrando o texto na largura útil.
        /// </summary>
        public void EscreverParagrafo(string texto, double tamanho = 9, bool negrito = false)
        {
            foreach (var linha in QuebrarEmLinhas(texto ?? string.Empty, LarguraUtil, tamanho))
                EscreverLinha(linha, tamanho, negrito);
        }

        /// <summary>Traça uma linha horizontal de margem a margem.</summary>
        public void LinhaHorizontal(double espessura = 0.6, double cinza = 0.75)
        {
            GarantirEspaco(6);
            _y -= 4;
            _paginaAtual.Append(Numero(cinza)).Append(" G ")
                        .Append(Numero(espessura)).Append(" w ")
                        .Append(Numero(Margem)).Append(' ').Append(Numero(_y)).Append(" m ")
                        .Append(Numero(LarguraDaPagina - Margem)).Append(' ').Append(Numero(_y))
                        .Append(" l S\n");
            _y -= 2;
        }

        /// <summary>
        /// Escreve uma linha de tabela. Células mais largas que a coluna são truncadas com
        /// reticências, nunca invadem a coluna vizinha.
        /// </summary>
        public void EscreverLinhaDeTabela(IReadOnlyList<ColunaDoPdf> colunas,
                                          IReadOnlyList<string> celulas,
                                          double tamanho = 9, bool negrito = false)
        {
            GarantirEspaco(tamanho + 4);
            _y -= tamanho + 3;

            for (int i = 0; i < colunas.Count && i < celulas.Count; i++)
            {
                var coluna = colunas[i];
                string texto = TruncarNaLargura(celulas[i] ?? string.Empty, coluna.Largura, tamanho);

                double x = coluna.Alinhamento == AlinhamentoDaColuna.Direita
                    ? Margem + coluna.X + coluna.Largura - MedirLargura(texto, tamanho)
                    : Margem + coluna.X;

                DesenharTexto(texto, x, _y, tamanho, negrito);
            }
        }

        /// <summary>Grava o documento no caminho indicado.</summary>
        public void Salvar(string caminho)
        {
            File.WriteAllBytes(caminho, Montar());
        }

        // ── Montagem do arquivo ──────────────────────────────────────────

        /// <summary>
        /// Monta os bytes do PDF: catálogo, árvore de páginas, as duas fontes, e um par
        /// página/conteúdo para cada página escrita.
        /// </summary>
        /// <remarks>
        /// A tabela xref exige o deslocamento em bytes de cada objeto, por isso o arquivo é montado
        /// em um <see cref="MemoryStream"/> e os deslocamentos são anotados durante a escrita.
        /// </remarks>
        private byte[] Montar()
        {
            var saida = new MemoryStream();
            var deslocamentos = new List<long>();

            void Escrever(string texto)
            {
                var bytes = ConverterParaWinAnsi(texto);
                saida.Write(bytes, 0, bytes.Length);
            }

            void IniciarObjeto(int numero)
            {
                deslocamentos.Add(saida.Length);
                Escrever($"{numero} 0 obj\n");
            }

            // 1 catálogo · 2 páginas · 3 e 4 fontes · depois 2 objetos por página.
            int primeiroObjetoDePagina = 5;
            int totalDeObjetos = 4 + 2 * _paginas.Count;

            Escrever("%PDF-1.4\n");

            IniciarObjeto(1);
            Escrever("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            IniciarObjeto(2);
            var filhos = new StringBuilder();
            for (int i = 0; i < _paginas.Count; i++)
                filhos.Append($"{primeiroObjetoDePagina + 2 * i} 0 R ");
            Escrever($"<< /Type /Pages /Kids [{filhos.ToString().TrimEnd()}] /Count {_paginas.Count} >>\nendobj\n");

            IniciarObjeto(3);
            Escrever("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

            IniciarObjeto(4);
            Escrever("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

            for (int i = 0; i < _paginas.Count; i++)
            {
                int numeroDaPagina   = primeiroObjetoDePagina + 2 * i;
                int numeroDoConteudo = numeroDaPagina + 1;

                IniciarObjeto(numeroDaPagina);
                Escrever($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {LarguraDaPagina} {AlturaDaPagina}] " +
                         $"/Resources << /Font << /{FonteNormal} 3 0 R /{FonteNegrito} 4 0 R >> >> " +
                         $"/Contents {numeroDoConteudo} 0 R >>\nendobj\n");

                string conteudo = _paginas[i].ToString();
                int comprimento = ConverterParaWinAnsi(conteudo).Length;

                IniciarObjeto(numeroDoConteudo);
                Escrever($"<< /Length {comprimento} >>\nstream\n{conteudo}endstream\nendobj\n");
            }

            long inicioDaXref = saida.Length;
            Escrever($"xref\n0 {totalDeObjetos + 1}\n");
            Escrever("0000000000 65535 f \n");
            foreach (var deslocamento in deslocamentos)
                Escrever($"{deslocamento.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");

            Escrever($"trailer\n<< /Size {totalDeObjetos + 1} /Root 1 0 R >>\n" +
                     $"startxref\n{inicioDaXref}\n%%EOF\n");

            return saida.ToArray();
        }

        // ── Desenho ──────────────────────────────────────────────────────

        private void DesenharTexto(string texto, double x, double y, double tamanho, bool negrito)
        {
            _paginaAtual.Append("BT /").Append(negrito ? FonteNegrito : FonteNormal).Append(' ')
                        .Append(Numero(tamanho)).Append(" Tf 0 g ")
                        .Append(Numero(x)).Append(' ').Append(Numero(y)).Append(" Td (")
                        .Append(EscaparTexto(texto)).Append(") Tj ET\n");
        }

        private static string Numero(double valor) =>
            valor.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>Escapa os três caracteres que delimitam literais de texto em PDF.</summary>
        private static string EscaparTexto(string texto)
        {
            return texto.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }

        // ── Medição e quebra ─────────────────────────────────────────────

        /// <summary>
        /// Larguras de glifo da Helvetica, em milésimos de em, para os caracteres em que a
        /// aproximação não serve.
        /// </summary>
        /// <remarks>
        /// Os dígitos da Helvetica têm todos a mesma largura (556), o que torna o alinhamento à
        /// direita das colunas numéricas exato — que é justamente onde a precisão importa. Para
        /// letras basta a aproximação, porque essas colunas são alinhadas à esquerda.
        /// </remarks>
        private static double LarguraDoCaractere(char caractere)
        {
            if (caractere >= '0' && caractere <= '9') return 0.556;

            switch (caractere)
            {
                case ' ': case ',': case '.': case ':': case ';': return 0.278;
                case '-': case '(': case ')':                     return 0.333;
                case '%':                                          return 0.889;
                case 'i': case 'l': case 'j': case 'I':            return 0.222;
                case 'm': case 'M': case 'W': case 'w':            return 0.833;
                default:                                           return 0.53;
            }
        }

        private static double MedirLargura(string texto, double tamanho)
        {
            double soma = 0;
            foreach (char caractere in texto)
                soma += LarguraDoCaractere(caractere);
            return soma * tamanho;
        }

        private static string TruncarNaLargura(string texto, double largura, double tamanho)
        {
            if (MedirLargura(texto, tamanho) <= largura)
                return texto;

            var recorte = new StringBuilder();
            double acumulado = MedirLargura("...", tamanho);

            foreach (char caractere in texto)
            {
                double proxima = LarguraDoCaractere(caractere) * tamanho;
                if (acumulado + proxima > largura) break;
                acumulado += proxima;
                recorte.Append(caractere);
            }

            return recorte.ToString().TrimEnd() + "...";
        }

        private static IEnumerable<string> QuebrarEmLinhas(string texto, double largura, double tamanho)
        {
            var linha = new StringBuilder();

            foreach (var palavra in texto.Split(' '))
            {
                string candidata = linha.Length == 0 ? palavra : linha + " " + palavra;

                if (MedirLargura(candidata, tamanho) > largura && linha.Length > 0)
                {
                    yield return linha.ToString();
                    linha.Clear().Append(palavra);
                }
                else
                {
                    linha.Clear().Append(candidata);
                }
            }

            if (linha.Length > 0)
                yield return linha.ToString();
        }

        // ── Codificação ──────────────────────────────────────────────────

        /// <summary>
        /// Converte texto para os bytes da codificação WinAnsi usada pelas fontes do documento.
        /// </summary>
        /// <remarks>
        /// Não se usa <c>Encoding.GetEncoding(1252)</c>: no .NET 8 a página de código 1252 só
        /// existe com o pacote System.Text.Encoding.CodePages, e a pesquisa não admite pacote novo.
        /// A conversão é feita à mão — para os caracteres 0x00–0xFF o WinAnsi coincide com o
        /// Latin-1, o que cobre todo o português acentuado.
        ///
        /// Fora disso há uma tabela curta de transliteração: os símbolos matemáticos da tese (ν, σ,
        /// Δ, subscritos) não existem em WinAnsi e sairiam como caractere de substituição. Preferiu-se
        /// grafá-los por extenso — "nu_e", "sigma" — a exibir sujeira no arquivo exportado.
        /// </remarks>
        internal static byte[] ConverterParaWinAnsi(string texto)
        {
            var bytes = new List<byte>(texto.Length);

            foreach (char caractere in texto)
            {
                switch (caractere)
                {
                    // Travessão e meia-risca têm posição própria no WinAnsi.
                    case '—': bytes.Add(0x97); continue;
                    case '–': bytes.Add(0x96); continue;
                    case '’': bytes.Add(0x27); continue;
                    case '“': case '”': bytes.Add(0x22); continue;

                    // Símbolos do modelo matemático, grafados por extenso.
                    case 'ν': AdicionarTexto(bytes, "nu");    continue; // ν
                    case 'σ': AdicionarTexto(bytes, "sigma"); continue; // σ
                    case 'Δ': AdicionarTexto(bytes, "d");     continue; // Δ
                    case '≤': AdicionarTexto(bytes, "<=");    continue;
                    case '≥': AdicionarTexto(bytes, ">=");    continue;
                    case '≈': bytes.Add((byte)'~');           continue;

                    // Subscritos e o acento circunflexo combinante do ν̂.
                    case 'ₑ': bytes.Add((byte)'e'); continue;
                    case '₉': bytes.Add((byte)'9'); continue;
                    case '₅': bytes.Add((byte)'5'); continue;
                    case '̂': continue;
                }

                if (caractere <= 0xFF)
                    bytes.Add((byte)caractere);
                else
                    bytes.Add((byte)'?');
            }

            return bytes.ToArray();
        }

        private static void AdicionarTexto(List<byte> destino, string texto)
        {
            foreach (char caractere in texto)
                destino.Add((byte)caractere);
        }
    }
}
