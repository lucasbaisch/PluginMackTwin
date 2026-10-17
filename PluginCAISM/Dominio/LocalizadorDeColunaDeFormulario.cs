using System;
using System.Collections.Generic;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Localiza a coluna de uma planilha de formulários a partir dos nomes candidatos declarados
    /// em <see cref="Configuracao.MapeamentoDeColunasDeFormulario"/> e
    /// <see cref="Configuracao.ComposicaoDeEscoresDeFormulario"/>.
    /// </summary>
    /// <remarks>
    /// Regra única de correspondência: para cada candidato, tenta igualdade exata do nome da
    /// coluna e, na ausência, correspondência por conteúdo (case-insensitive). Isso permite que a
    /// configuração guarde apenas um trecho discriminante em vez do enunciado inteiro da questão.
    ///
    /// Existe em um único lugar de propósito: quando a busca por conteúdo estava implementada
    /// somente no cálculo dos escores, a tabela-síntese — que fazia busca exata — não encontrava
    /// nenhuma das colunas configuradas por trecho e exibia tudo como ausente.
    /// </remarks>
    public static class LocalizadorDeColunaDeFormulario
    {
        /// <summary>
        /// Procura, entre as respostas de um participante, a primeira coluna que corresponda a
        /// algum dos nomes candidatos.
        /// </summary>
        /// <param name="campos">Respostas indexadas pelo nome da coluna.</param>
        /// <param name="nomesCandidatos">Nomes ou trechos de nome a testar, em ordem.</param>
        /// <param name="chaveEncontrada">Nome real da coluna localizada.</param>
        /// <param name="valorEncontrado">Valor da coluna localizada.</param>
        /// <returns>
        /// true se alguma coluna correspondeu e tinha valor não vazio; false caso contrário.
        /// </returns>
        public static bool TentarLocalizar(
            IReadOnlyDictionary<string, string> campos,
            string[] nomesCandidatos,
            out string chaveEncontrada,
            out string valorEncontrado)
        {
            chaveEncontrada = null;
            valorEncontrado = null;

            if (campos == null || nomesCandidatos == null)
                return false;

            foreach (string candidato in nomesCandidatos)
            {
                if (string.IsNullOrWhiteSpace(candidato))
                    continue;

                if (campos.TryGetValue(candidato, out string valorExato) && !string.IsNullOrWhiteSpace(valorExato))
                {
                    chaveEncontrada = candidato;
                    valorEncontrado = valorExato;
                    return true;
                }

                foreach (var par in campos)
                {
                    if (par.Key.IndexOf(candidato, StringComparison.OrdinalIgnoreCase) >= 0
                        && !string.IsNullOrWhiteSpace(par.Value))
                    {
                        chaveEncontrada = par.Key;
                        valorEncontrado = par.Value;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Variante usada quando basta saber o nome real da coluna correspondente a UM candidato,
        /// mesmo que o valor esteja vazio — necessário para a tabela-síntese, que precisa listar o
        /// item ainda que o participante não o tenha respondido.
        /// </summary>
        /// <param name="campos">Respostas indexadas pelo nome da coluna.</param>
        /// <param name="nomeCandidato">Nome ou trecho de nome a testar.</param>
        /// <param name="chaveEncontrada">Nome real da coluna localizada.</param>
        /// <returns>true se alguma coluna correspondeu ao candidato.</returns>
        public static bool TentarLocalizarChave(
            IReadOnlyDictionary<string, string> campos,
            string nomeCandidato,
            out string chaveEncontrada)
        {
            chaveEncontrada = null;

            if (campos == null || string.IsNullOrWhiteSpace(nomeCandidato))
                return false;

            if (campos.ContainsKey(nomeCandidato))
            {
                chaveEncontrada = nomeCandidato;
                return true;
            }

            foreach (var par in campos)
            {
                if (par.Key.IndexOf(nomeCandidato, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    chaveEncontrada = par.Key;
                    return true;
                }
            }

            return false;
        }
    }
}
