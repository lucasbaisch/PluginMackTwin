using System;
using System.Linq;

namespace PluginCAISM.Configuracao
{
    /// <summary>
    /// Distribui os 34 itens do Índice de Estresse em Enfermeiros pelos cinco domínios do
    /// instrumento.
    /// </summary>
    /// <remarks>
    /// A correspondência é feita por CONTEÚDO do enunciado, e não por posição da coluna: a ordem
    /// das colunas do <c>b_ieee</c> não é garantia, e uma planilha reordenada silenciosamente
    /// trocaria os itens de domínio.
    ///
    /// Um item que não case com nenhum trecho cai em
    /// <see cref="GruposDeQuestoes.Outros"/> — nada desaparece da tela por falta de mapeamento.
    /// </remarks>
    // RESOLVIDO pelo TextoFinal de 31/08/2026, §4.2: as Tabelas 20 a 24 publicam a chave
    // item→domínio dos 34 itens (A: Demandas e Sobrecarga, 8 itens; B: Condições e Organização
    // do Trabalho, 10; C: Relacionamento Interpessoal e Clima, 5; D: Assistência e Carga
    // Emocional, 6; E: Autonomia, Gestão e Interface Trabalho-Vida, 5). O Mapa abaixo TRANSCREVE
    // essas tabelas — a distribuição anterior, derivada do enunciado, foi substituída. As
    // divergências corrigidas: "trabalho repetitivo" e "desgaste emocional" pertencem a A (não a
    // B/D); "esforço físico", "procedimentos rápidos", "falta de profissionais", "equipe sem
    // preparo" e "Política Institucional" pertencem a B; "ausência de espaço para compartilhar"
    // pertence a C; "grande número de pacientes" e "familiares de pacientes" pertencem a D.
    // Nota: a prosa do §4.2 diverge das próprias tabelas em dois itens ("grande número de
    // pacientes" e "desgaste emocional" são citados na descrição de outro domínio); as TABELAS,
    // que listam os itens um a um com suas estatísticas, prevalecem sobre a prosa.
    public static class DominiosDoIeee
    {
        /// <summary>
        /// Trecho identificador do item e o domínio a que pertence, na ordem de teste.
        /// </summary>
        public static readonly (string Trecho, string Dominio)[] Mapa =
        {
            // ── Domínio A — Demandas e Sobrecarga (Tabela 20, 8 itens) ───
            ("Executar diferente tarefas ao mesmo tempo",        GruposDeQuestoes.IeeeDemandas),
            ("Resolver imprevistos",                             GruposDeQuestoes.IeeeDemandas),
            ("Fazer um trabalho repetitivo",                     GruposDeQuestoes.IeeeDemandas),
            ("Sentir desgaste emocional",                        GruposDeQuestoes.IeeeDemandas),
            ("Prazo insuficiente",                               GruposDeQuestoes.IeeeDemandas),
            ("Cumprir uma carga horária maior",                  GruposDeQuestoes.IeeeDemandas),
            ("Executar funções além das atribuições",            GruposDeQuestoes.IeeeDemandas),
            ("Ser designado para atividades fora da área",       GruposDeQuestoes.IeeeDemandas),

            // ── Domínio B — Condições e Organização do Trabalho (Tabela 21, 10 itens) ──
            ("Falta de profissionais na equipe",                 GruposDeQuestoes.IeeeCondicoes),
            ("Executar procedimentos rápidos",                   GruposDeQuestoes.IeeeCondicoes),
            ("Lidar com diferenças entre protocolos",            GruposDeQuestoes.IeeeCondicoes),
            ("Atuar em equipe com profissionais sem preparo",    GruposDeQuestoes.IeeeCondicoes),
            ("Fazer esforço físico",                             GruposDeQuestoes.IeeeCondicoes),
            ("Trabalhar em condições físicas inadequadas",       GruposDeQuestoes.IeeeCondicoes),
            ("Interferência da Política Institucional",          GruposDeQuestoes.IeeeCondicoes),
            ("Trabalhar em horário noturno",                     GruposDeQuestoes.IeeeCondicoes),
            ("Insuficiência de materiais",                       GruposDeQuestoes.IeeeCondicoes),
            ("Fazer turnos alternados",                          GruposDeQuestoes.IeeeCondicoes),

            // ── Domínio C — Relacionamento Interpessoal e Clima (Tabela 22, 5 itens) ──
            ("Relacionar-se com a equipe médica",                GruposDeQuestoes.IeeeRelacoes),
            ("Ausência de espaço no ambiente de trabalho",       GruposDeQuestoes.IeeeRelacoes),
            ("Relacionar-se com os colegas enfermeiros",         GruposDeQuestoes.IeeeRelacoes),
            ("Relacionar-se com a chefia",                       GruposDeQuestoes.IeeeRelacoes),
            ("Trabalhar em clima de competitividade",            GruposDeQuestoes.IeeeRelacoes),

            // ── Domínio D — Assistência e Carga Emocional (Tabela 23, 6 itens) ──
            // O trecho "grande número de pacientes" omite o início do enunciado de propósito:
            // a Tabela 23 escreve "Atender A um grande número..." e casa com as duas grafias.
            ("grande número de pacientes",                       GruposDeQuestoes.IeeeAssistencia),
            ("Atender familiares de pacientes",                  GruposDeQuestoes.IeeeAssistencia),
            ("Sentir-se impotente",                              GruposDeQuestoes.IeeeAssistencia),
            ("Prestar assistência a pacientes graves",           GruposDeQuestoes.IeeeAssistencia),
            ("Prestar assistência ao paciente",                  GruposDeQuestoes.IeeeAssistencia),
            ("Impossibilidade de prestar assistência",           GruposDeQuestoes.IeeeAssistencia),

            // ── Domínio E — Autonomia, Gestão e Interface Trabalho-Vida (Tabela 24, 5 itens) ──
            ("Falta de autonomia",                               GruposDeQuestoes.IeeeAutonomia),
            ("Conciliar as questões profissionais",              GruposDeQuestoes.IeeeAutonomia),
            ("Dificuldade em equilibrar trabalho e vida",        GruposDeQuestoes.IeeeAutonomia),
            ("Administrar ou supervisionar o trabalho",          GruposDeQuestoes.IeeeAutonomia),
            ("Realizar atividades profissionais em casa",        GruposDeQuestoes.IeeeAutonomia),
        };

        /// <summary>
        /// Domínio do item cujo enunciado é informado; <see cref="GruposDeQuestoes.Outros"/>
        /// quando nenhum trecho corresponde.
        /// </summary>
        /// <remarks>
        /// A ordem do <see cref="Mapa"/> importa em um caso: "Prestar assistência a pacientes
        /// graves" precisa ser testado antes de "Prestar assistência ao paciente", que é prefixo
        /// do primeiro e o capturaria.
        /// </remarks>
        public static string ObterDominio(string enunciado)
        {
            if (string.IsNullOrWhiteSpace(enunciado))
                return GruposDeQuestoes.Outros;

            foreach (var (trecho, dominio) in Mapa)
            {
                if (enunciado.IndexOf(trecho, StringComparison.OrdinalIgnoreCase) >= 0)
                    return dominio;
            }

            return GruposDeQuestoes.Outros;
        }

        /// <summary>Quantidade de itens declarados para um domínio.</summary>
        public static int ContarItens(string dominio) =>
            Mapa.Count(par => string.Equals(par.Dominio, dominio, StringComparison.Ordinal));
    }
}
