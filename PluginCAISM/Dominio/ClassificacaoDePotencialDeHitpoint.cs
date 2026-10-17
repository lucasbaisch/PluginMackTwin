using System.Collections.Generic;
using System.Linq;
using PluginCAISM.Configuracao;

namespace PluginCAISM.Dominio
{
    /// <summary>
    /// Classificação de potencial homeodinâmico de um objeto, conforme a Equação 4 (§5.2.2) do
    /// TextoFinal.
    /// </summary>
    /// <remarks>
    /// O critério é UM SÓ: a posição do intervalo de confiança de 85% de ν̂_e em relação ao zero,
    /// IC₈₅% = ν̂_e ± 1,44·se(ν̂_e). Nem a duração da fixação nem as variáveis ambientais entram na
    /// decisão — as ambientais são descontadas dentro da Camada 1 (Equação 3), e a duração não
    /// participa de nenhuma das duas camadas.
    /// </remarks>
    // RESOLVIDO pela revisão do TextoFinal de 31/08/2026: o Quadro 41 foi reescrito sem o termo
    // γ_e e sem a "Equação 5" — o Neutro passou a ser definido exatamente como aqui ("não
    // corresponde a uma faixa fixa do índice... precisão insuficiente"), e as condições
    // ambientais que o quadro lista são CARACTERÍSTICAS interpretativas de cada classe, não
    // condições adicionais de classificação: no modelo, as ambientais são covariáveis descontadas
    // na Camada 1 (Equação 3), e o rótulo decide-se apenas pelo IC de 85% do efeito encolhido.
    // O código e o quadro passaram a dizer a mesma coisa.
    public enum ClassificacaoDePotencial
    {
        /// <summary>
        /// Restaurador — hitpoint verde. Todo o IC₈₅% de ν̂_e está abaixo de zero (z &lt; −1,44):
        /// a evidência sustenta constrição pupilar atribuível ao elemento. Corresponde a PH_e &gt; 0.
        /// </summary>
        Restaurador,

        /// <summary>
        /// Estressor — hitpoint vermelho. Todo o IC₈₅% de ν̂_e está acima de zero (z &gt; +1,44):
        /// a evidência sustenta dilatação pupilar. Corresponde a PH_e &lt; 0.
        /// </summary>
        Estressor,

        /// <summary>
        /// Neutro — hitpoint cinza. O IC₈₅% contém o zero, OU não há estimativa a examinar. Não
        /// indica ausência de efeito, mas insuficiência de evidência para afirmá-lo, e deve ser
        /// lido à luz do poder estatístico disponível: um elemento com |PH_e| alto pode ser
        /// Neutro se se(ν̂_e) for elevado, o que tipicamente decorre de poucas observações.
        /// </summary>
        /// <remarks>
        /// <b>Absorve o antigo "Sem pontuação"</b> (revisão de 01/09/2026). Havia uma quarta
        /// classe para o objeto sem métrica calculada — tocado pelo olhar, mas sem nenhuma
        /// fixação válida —, distinta do Neutro por ser ausência de dado e não resultado de
        /// análise. Duas razões a suprimiram. A primeira é que o TextoFinal declara TRÊS classes
        /// no Quadro de classificação, e uma quarta categoria só no código fazia o artefato
        /// afirmar mais do que a tese descreve. A segunda é que a distinção não se sustenta no
        /// próprio critério: se o Neutro significa "evidência insuficiente para afirmar a
        /// direção", nenhuma evidência é o caso extremo disso, e não uma categoria à parte.
        ///
        /// A informação não se perde: a coluna de fixações da tabela mostra zero para esses
        /// objetos, e as colunas de índice e de ambiente trazem travessão, de modo que a
        /// ausência de observação continua legível sem exigir um rótulo próprio.
        /// </remarks>
        Neutro
    }

    /// <summary>
    /// Resolve a classificação de potencial homeodinâmico de um objeto e o TIPO da família
    /// <c>hitpoint.rfa</c> correspondente.
    /// </summary>
    /// <remarks>
    /// Regra pura, sem dependência da API do Revit: recebe classificações e devolve nome de tipo de
    /// família. A cor propriamente dita é o material do tipo, definido no <c>hitpoint.rfa</c>; os
    /// nomes dos tipos estão em <see cref="ConfiguracaoDaFamiliaHitpoint"/>.
    ///
    /// A CLASSIFICAÇÃO em si não é feita aqui: vem do IC₈₅% de ν̂_e, em
    /// <see cref="Processamento.CalculadorDaCamada2.Classificar"/>. Esta classe só traduz
    /// classificação em tipo de família e em rótulo.
    ///
    /// <b>São três classes, e três tipos de família</b>: a correspondência entre classificação e
    /// tipo é um-para-um, sem casos a agrupar. O objeto sem estimativa — tocado pelo olhar, mas
    /// sem fixação válida — recebe Neutro, por ser o caso extremo de evidência insuficiente.
    /// </remarks>
    public static class ClassificadorDePotencialDeHitpoint
    {
        /// <summary>
        /// Devolve o nome do tipo da família correspondente a uma classificação.
        /// </summary>
        public static string ObterNomeDoTipoDeFamilia(ClassificacaoDePotencial classificacao)
        {
            switch (classificacao)
            {
                case ClassificacaoDePotencial.Restaurador:
                    return ConfiguracaoDaFamiliaHitpoint.NomeDoTipoPotencialRestaurador;

                case ClassificacaoDePotencial.Estressor:
                    return ConfiguracaoDaFamiliaHitpoint.NomeDoTipoPotencialEstressor;

                case ClassificacaoDePotencial.Neutro:
                default:
                    return ConfiguracaoDaFamiliaHitpoint.NomeDoTipoPotencialNeutro;
            }
        }

        /// <summary>
        /// Rótulo legível da classificação, usado na tabela da aba de potencial homeodinâmico.
        /// </summary>
        public static string ObterRotulo(ClassificacaoDePotencial classificacao)
        {
            switch (classificacao)
            {
                case ClassificacaoDePotencial.Restaurador: return "Restaurador";
                case ClassificacaoDePotencial.Estressor:   return "Estressor";
                default:                                   return "Neutro";
            }
        }

        /// <summary>
        /// Devolve os nomes de todos os tipos da família que o plugin espera encontrar,
        /// para validação na carga da família.
        /// </summary>
        public static IReadOnlyList<string> ObterNomesDeTodosOsTipos()
        {
            return new[]
            {
                ConfiguracaoDaFamiliaHitpoint.NomeDoTipoPotencialRestaurador,
                ConfiguracaoDaFamiliaHitpoint.NomeDoTipoPotencialEstressor,
                ConfiguracaoDaFamiliaHitpoint.NomeDoTipoPotencialNeutro,
            };
        }

        /// <summary>
        /// Resolve o tipo da família de uma fixação a partir da classificação dos objetos que ela
        /// observou.
        /// </summary>
        /// <param name="nomesDeObjetos">
        /// Objetos associados à fixação (<see cref="EventoDeMovimentoOcular.NomesDeObjetos"/>).
        /// </param>
        /// <param name="classificacoesPorObjeto">
        /// Classificação por nome de objeto, vinda de <see cref="MetricasPorObjeto.ClassificacaoPH"/>.
        /// </param>
        /// <remarks>
        /// A cor não vem de limiar sobre o índice: vem da classificação, isto é, da afirmabilidade
        /// estatística do IC₈₅% de ν̂_e (Equação 4). Um elemento de |PH_e| alto pode sair cinza.
        /// </remarks>
        public static string ResolverNomeDoTipoDaFixacao(
            IEnumerable<string> nomesDeObjetos,
            IReadOnlyDictionary<string, ClassificacaoDePotencial> classificacoesPorObjeto)
        {
            return ObterNomeDoTipoDeFamilia(
                ResolverClassificacaoDaFixacao(nomesDeObjetos, classificacoesPorObjeto));
        }

        /// <summary>
        /// Resolve a classificação representativa de uma fixação que pode abranger mais de um objeto.
        /// </summary>
        /// <remarks>
        /// Regra: UNANIMIDADE entre os objetos classificados. Se todos concordam, a fixação recebe
        /// essa classificação; se discordam, recebe Neutro, porque a evidência é contraditória e
        /// pintar a fixação de verde ou vermelho afirmaria mais do que os dados sustentam. Objetos
        /// sem classificação são ignorados; se nenhum tem, o resultado é igualmente Neutro.
        /// </remarks>
        // Regra ADOTADA: unanimidade, com discordância resolvida em Neutro. O TextoFinal não
        // trata de fixações sobre múltiplos objetos, e esta é a regra coerente com a semântica
        // que o Quadro 41 dá ao Neutro — insuficiência de evidência para afirmar a direção:
        // evidência contraditória é evidência insuficiente. Maioria simples, primeiro objeto ou
        // maior contato afirmariam mais do que os dados sustentam. Se o texto vier a especificar
        // outra regra, este é o único ponto a ajustar.
        public static ClassificacaoDePotencial ResolverClassificacaoDaFixacao(
            IEnumerable<string> nomesDeObjetos,
            IReadOnlyDictionary<string, ClassificacaoDePotencial> classificacoesPorObjeto)
        {
            if (nomesDeObjetos == null || classificacoesPorObjeto == null)
                return ClassificacaoDePotencial.Neutro;

            var classificacoesConhecidas = nomesDeObjetos
                .Where(nome => !string.IsNullOrWhiteSpace(nome))
                .Where(classificacoesPorObjeto.ContainsKey)
                .Select(nome => classificacoesPorObjeto[nome])
                .Distinct()
                .ToList();

            if (classificacoesConhecidas.Count == 0)
                return ClassificacaoDePotencial.Neutro;

            return classificacoesConhecidas.Count == 1
                ? classificacoesConhecidas[0]
                : ClassificacaoDePotencial.Neutro;
        }

    }
}
