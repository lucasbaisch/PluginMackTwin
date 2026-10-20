using System.Collections.Generic;
using MackTwin.Dominio;
using MackTwin.Parsing;

namespace MackTwin.Processamento
{
    /// <summary>
    /// Decide a que sala um sensor pertence a partir do seu HOSTNAME — a única regra, usada por
    /// quem agrupa leituras e por quem anota que salas cada planilha alimenta.
    /// </summary>
    /// <remarks>
    /// Duas convenções convivem: (1) o padrão <c>MMCCT&lt;sala&gt;-&lt;nnn&gt;</c>, de que se
    /// extrai o trecho de sala (<c>MMCCTLAB001-001</c> → LAB001); (2) o HOSTNAME inteiro como
    /// código de um ambiente cadastrado à mão (<c>BR_UPM-CPS-EDU_T00-FABLAB-MAMB-TU-SHT40_2026</c>
    /// → o ambiente cujo código, normalizado, é esse). Sem casamento, o sensor fica sem sala.
    /// </remarks>
    public static class ResolvedorDeSala
    {
        /// <summary>
        /// Código normalizado da sala do sensor, ou vazio se o HOSTNAME não segue o padrão nem é
        /// o código de uma sala cadastrada.
        /// </summary>
        /// <param name="hostname">Valor da coluna HOSTNAME.</param>
        /// <param name="codigosCadastrados">Códigos normalizados das salas cadastradas à mão. Pode ser null.</param>
        public static string Resolver(string? hostname, IReadOnlySet<string>? codigosCadastrados)
        {
            string pelaConvencao = AnalisadorDeHostname.ExtrairCodigoDaSala(hostname);

            if (pelaConvencao.Length > 0)
                return pelaConvencao;

            string inteiro = CodigoDeSala.Normalizar(hostname);

            return inteiro.Length > 0 && codigosCadastrados != null && codigosCadastrados.Contains(inteiro)
                ? inteiro
                : string.Empty;
        }
    }
}
