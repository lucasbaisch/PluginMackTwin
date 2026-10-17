using System.Text;

namespace MackTwin.Dominio
{
    /// <summary>
    /// Normalização do código de sala, a chave que casa o sensor (trecho de sala do HOSTNAME)
    /// com o Room do modelo Revit (número ou nome).
    /// </summary>
    /// <remarks>
    /// Os dois lados vêm de pessoas diferentes digitando em sistemas diferentes: o firmware
    /// grava <c>LAB001</c>, o projetista pode numerar o Room como <c>LAB-001</c>, <c>LAB 001</c>
    /// ou <c>lab_001</c>. Comparar só letras e dígitos, em caixa alta, faz todas essas grafias
    /// serem a mesma sala — e é deliberado NÃO ir além disso (não se removem zeros à esquerda,
    /// por exemplo): <c>LAB001</c> e <c>LAB1</c> podem ser salas distintas.
    /// </remarks>
    public static class CodigoDeSala
    {
        /// <summary>Só letras e dígitos, em caixa alta. Texto nulo ou vazio vira string vazia.</summary>
        public static string Normalizar(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return string.Empty;

            var resultado = new StringBuilder(texto.Length);

            foreach (char caractere in texto)
            {
                if (char.IsLetterOrDigit(caractere))
                    resultado.Append(char.ToUpperInvariant(caractere));
            }

            return resultado.ToString();
        }

        /// <summary>Se dois códigos designam a mesma sala. Vazio nunca casa com nada.</summary>
        public static bool SaoIguais(string? primeiro, string? segundo)
        {
            string a = Normalizar(primeiro);
            return a.Length > 0 && a == Normalizar(segundo);
        }
    }
}
