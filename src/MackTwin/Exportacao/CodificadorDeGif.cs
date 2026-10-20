using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MackTwin.Configuracao;

namespace MackTwin.Exportacao
{
    /// <summary>Um quadro já reduzido à paleta do GIF.</summary>
    public sealed class QuadroDeGif
    {
        /// <summary>Largura em pixels.</summary>
        public int Largura { get; init; }

        /// <summary>Altura em pixels.</summary>
        public int Altura { get; init; }

        /// <summary>Um índice de paleta por pixel, linha a linha.</summary>
        public byte[] Indices { get; init; } = Array.Empty<byte>();

        /// <summary>Quanto tempo o quadro fica na tela, em centésimos de segundo.</summary>
        public int AtrasoEmCentesimos { get; init; }
    }

    /// <summary>
    /// Grava um GIF89a animado (laço infinito) a partir de quadros de mesmo tamanho, com paleta
    /// global fixa e compressão LZW — sem dependência externa.
    /// </summary>
    /// <remarks>
    /// <b>Por que um codificador próprio.</b> O <c>GifBitmapEncoder</c> do WPF grava vários
    /// quadros, mas sem a extensão de laço e sem tempo por quadro: o resultado não anima em
    /// navegador. Remendar os bytes dele é mais frágil do que escrever o formato, que é curto.
    ///
    /// <b>Paleta fixa</b> de 6 × 7 × 6 níveis (252 cores, extremos exatos: branco é branco) mais
    /// as quatro cores de conforto em posições reservadas, para que legenda e carimbo saiam com a
    /// cor exata do painel. Boa para vistas sombreadas do modelo; não é um quantizador para fotos.
    ///
    /// <b>LZW</b> com código mínimo de 8 bits, clear em 256, fim em 257, tabela até 4096 códigos
    /// e aumento de largura quando o próximo código a atribuir passa de 2^n — a mesma regra que
    /// os decodificadores de GIF esperam (o decodificador atribui um código atrasado em relação
    /// ao codificador, por isso o "+1" no teste).
    /// </remarks>
    public static class CodificadorDeGif
    {
        private const int NiveisDeVermelho = 6;
        private const int NiveisDeVerde = 7;
        private const int NiveisDeAzul = 6;
        private const int CoresUniformes = NiveisDeVermelho * NiveisDeVerde * NiveisDeAzul; // 252
        private const int TamanhoDaPaleta = 256;

        private const int CodigoDeLimpeza = 256;
        private const int CodigoDeFim = 257;
        private const int PrimeiroCodigoLivre = 258;
        private const int LarguraMinimaDoCodigo = 8;
        private const int MaximoDeCodigos = 4096;

        /// <summary>As cores reservadas (índices 252–255), na ordem das classificações.</summary>
        private static readonly string[] CoresReservadas =
        {
            CoresDeConforto.Confortavel,
            CoresDeConforto.AbaixoDaFaixa,
            CoresDeConforto.AcimaDaFaixa,
            CoresDeConforto.SemDados,
        };

        private static readonly byte[] Paleta = MontarPaleta();

        /// <summary>Índice de paleta mais próximo de uma cor RGB.</summary>
        public static byte IndiceDaCor(byte r, byte g, byte b)
        {
            for (int indice = 0; indice < CoresReservadas.Length; indice++)
            {
                int posicao = (CoresUniformes + indice) * 3;

                if (Paleta[posicao] == r && Paleta[posicao + 1] == g && Paleta[posicao + 2] == b)
                    return (byte)(CoresUniformes + indice);
            }

            int nivelR = (r * (NiveisDeVermelho - 1) + 127) / 255;
            int nivelG = (g * (NiveisDeVerde - 1) + 127) / 255;
            int nivelB = (b * (NiveisDeAzul - 1) + 127) / 255;

            return (byte)((nivelR * NiveisDeVerde + nivelG) * NiveisDeAzul + nivelB);
        }

        /// <summary>A cor de um índice da paleta, para verificação.</summary>
        public static (byte R, byte G, byte B) CorDoIndice(byte indice)
        {
            return (Paleta[indice * 3], Paleta[indice * 3 + 1], Paleta[indice * 3 + 2]);
        }

        /// <summary>
        /// Reduz pixels BGRA (4 bytes por pixel, linha a linha) a índices da paleta.
        /// </summary>
        public static byte[] Quantizar(byte[] bgra, int largura, int altura, int passoDaLinha)
        {
            var indices = new byte[largura * altura];

            for (int y = 0; y < altura; y++)
            {
                int origem = y * passoDaLinha;
                int destino = y * largura;

                for (int x = 0; x < largura; x++, origem += 4)
                    indices[destino + x] = IndiceDaCor(bgra[origem + 2], bgra[origem + 1], bgra[origem]);
            }

            return indices;
        }

        /// <summary>Grava os quadros como um GIF animado em laço infinito.</summary>
        /// <exception cref="ArgumentException">Nenhum quadro, ou quadros de tamanhos diferentes.</exception>
        public static void Gravar(IReadOnlyList<QuadroDeGif> quadros, Stream destino)
        {
            if (quadros.Count == 0)
                throw new ArgumentException("O GIF precisa de pelo menos um quadro.", nameof(quadros));

            int largura = quadros[0].Largura;
            int altura = quadros[0].Altura;

            foreach (var quadro in quadros)
            {
                if (quadro.Largura != largura || quadro.Altura != altura || quadro.Indices.Length != largura * altura)
                    throw new ArgumentException("Todos os quadros do GIF precisam ter o mesmo tamanho.", nameof(quadros));
            }

            var escritor = new BinaryWriter(destino, Encoding.ASCII, leaveOpen: true);

            // Cabeçalho e descritor lógico de tela: paleta global de 256 cores (2^(7+1)).
            escritor.Write(Encoding.ASCII.GetBytes("GIF89a"));
            escritor.Write((ushort)largura);
            escritor.Write((ushort)altura);
            escritor.Write((byte)0xF7); // paleta global presente, 8 bits por cor, 256 entradas
            escritor.Write((byte)0);    // cor de fundo
            escritor.Write((byte)0);    // proporção de pixel padrão
            escritor.Write(Paleta);

            // Extensão de aplicação NETSCAPE2.0: laço infinito.
            escritor.Write((byte)0x21);
            escritor.Write((byte)0xFF);
            escritor.Write((byte)0x0B);
            escritor.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            escritor.Write((byte)0x03);
            escritor.Write((byte)0x01);
            escritor.Write((ushort)0);  // 0 = repetir para sempre
            escritor.Write((byte)0x00);

            foreach (var quadro in quadros)
            {
                // Extensão de controle gráfico: tempo do quadro, sem transparência, sem descartar.
                escritor.Write((byte)0x21);
                escritor.Write((byte)0xF9);
                escritor.Write((byte)0x04);
                escritor.Write((byte)0x04); // disposição 1 (manter), sem transparência
                escritor.Write((ushort)Math.Clamp(quadro.AtrasoEmCentesimos, 2, ushort.MaxValue));
                escritor.Write((byte)0);
                escritor.Write((byte)0x00);

                // Descritor de imagem: quadro inteiro, sem paleta local, sem entrelaçamento.
                escritor.Write((byte)0x2C);
                escritor.Write((ushort)0);
                escritor.Write((ushort)0);
                escritor.Write((ushort)largura);
                escritor.Write((ushort)altura);
                escritor.Write((byte)0x00);

                escritor.Write((byte)LarguraMinimaDoCodigo);
                escritor.Flush();
                ComprimirLzw(quadro.Indices, destino);
            }

            escritor.Write((byte)0x3B); // fim do arquivo
            escritor.Flush();
        }

        // ── LZW ──────────────────────────────────────────────────────────

        private static void ComprimirLzw(byte[] indices, Stream destino)
        {
            var bits = new EscritorDeBits(destino);
            var tabela = new Dictionary<int, int>(MaximoDeCodigos);
            int proximoCodigo = PrimeiroCodigoLivre;
            int larguraDoCodigo = LarguraMinimaDoCodigo + 1;

            bits.Escrever(CodigoDeLimpeza, larguraDoCodigo);

            if (indices.Length == 0)
            {
                bits.Escrever(CodigoDeFim, larguraDoCodigo);
                bits.Concluir();
                return;
            }

            int prefixo = indices[0];

            for (int posicao = 1; posicao < indices.Length; posicao++)
            {
                int cor = indices[posicao];
                int chave = (prefixo << 8) | cor;

                if (tabela.TryGetValue(chave, out int codigo))
                {
                    prefixo = codigo;
                    continue;
                }

                bits.Escrever(prefixo, larguraDoCodigo);

                if (proximoCodigo < MaximoDeCodigos)
                {
                    tabela[chave] = proximoCodigo++;

                    if (proximoCodigo == (1 << larguraDoCodigo) + 1 && larguraDoCodigo < 12)
                        larguraDoCodigo++;
                }
                else
                {
                    bits.Escrever(CodigoDeLimpeza, larguraDoCodigo);
                    tabela.Clear();
                    proximoCodigo = PrimeiroCodigoLivre;
                    larguraDoCodigo = LarguraMinimaDoCodigo + 1;
                }

                prefixo = cor;
            }

            bits.Escrever(prefixo, larguraDoCodigo);
            bits.Escrever(CodigoDeFim, larguraDoCodigo);
            bits.Concluir();
        }

        /// <summary>Empacota códigos LSB-first em sub-blocos de até 255 bytes, como o GIF exige.</summary>
        private sealed class EscritorDeBits
        {
            private readonly Stream _destino;
            private readonly byte[] _bloco = new byte[255];
            private int _preenchido;
            private uint _acumulador;
            private int _bitsAcumulados;

            public EscritorDeBits(Stream destino)
            {
                _destino = destino;
            }

            public void Escrever(int codigo, int largura)
            {
                _acumulador |= (uint)codigo << _bitsAcumulados;
                _bitsAcumulados += largura;

                while (_bitsAcumulados >= 8)
                {
                    EmitirByte((byte)(_acumulador & 0xFF));
                    _acumulador >>= 8;
                    _bitsAcumulados -= 8;
                }
            }

            public void Concluir()
            {
                if (_bitsAcumulados > 0)
                    EmitirByte((byte)(_acumulador & 0xFF));

                DespejarBloco();
                _destino.WriteByte(0x00);
            }

            private void EmitirByte(byte valor)
            {
                _bloco[_preenchido++] = valor;

                if (_preenchido == _bloco.Length)
                    DespejarBloco();
            }

            private void DespejarBloco()
            {
                if (_preenchido == 0)
                    return;

                _destino.WriteByte((byte)_preenchido);
                _destino.Write(_bloco, 0, _preenchido);
                _preenchido = 0;
            }
        }

        // ── Paleta ───────────────────────────────────────────────────────

        private static byte[] MontarPaleta()
        {
            var paleta = new byte[TamanhoDaPaleta * 3];
            int posicao = 0;

            for (int nivelR = 0; nivelR < NiveisDeVermelho; nivelR++)
            {
                for (int nivelG = 0; nivelG < NiveisDeVerde; nivelG++)
                {
                    for (int nivelB = 0; nivelB < NiveisDeAzul; nivelB++)
                    {
                        paleta[posicao++] = (byte)(nivelR * 255 / (NiveisDeVermelho - 1));
                        paleta[posicao++] = (byte)(nivelG * 255 / (NiveisDeVerde - 1));
                        paleta[posicao++] = (byte)(nivelB * 255 / (NiveisDeAzul - 1));
                    }
                }
            }

            foreach (string hexadecimal in CoresReservadas)
            {
                var (r, g, b) = CoresDeConforto.ParaRgb(hexadecimal);
                paleta[posicao++] = r;
                paleta[posicao++] = g;
                paleta[posicao++] = b;
            }

            return paleta;
        }
    }
}
