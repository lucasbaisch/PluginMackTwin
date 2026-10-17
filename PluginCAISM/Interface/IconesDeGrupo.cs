using System.Collections.Generic;
using System.Windows.Media;
using PluginCAISM.Configuracao;

namespace PluginCAISM.Interface
{
    /// <summary>
    /// Pictogramas dos grupos da aba de Dados do Participante.
    /// </summary>
    /// <remarks>
    /// <b>São geometrias, e não caracteres de fonte de ícones.</b> Segoe MDL2 / Segoe Fluent Icons
    /// resolveriam com menos código, mas os pontos de código ficam na área de uso privado do
    /// Unicode: numa máquina sem a fonte — ou com outra versão dela — o glifo vira um retângulo
    /// vazio, e nada no build acusa. Emoji tem o problema oposto: o WPF não renderiza fontes
    /// coloridas, então sairia um desenho monocromático imprevisível, e coloridos ficariam
    /// infantis para uma tela de análise. Um <see cref="Geometry"/> desenha igual em qualquer
    /// instalação.
    ///
    /// Todos os tracados estão em uma caixa de 24×24 e são preenchidos (sem contorno), para
    /// escalarem sem depender da espessura da linha.
    /// </remarks>
    public static class IconesDeGrupo
    {
        // Congelados: são compartilhados por todos os cartões e nunca mudam. Freeze dispensa o
        // WPF de rastrear alterações e permite reuso entre threads.
        private static Geometry Desenhar(string tracado)
        {
            var geometria = Geometry.Parse(tracado);
            geometria.Freeze();
            return geometria;
        }

        /// <summary>Pessoa: caracterização do participante.</summary>
        public static readonly Geometry Perfil = Desenhar(
            "M12,12 C14.2,12 16,10.2 16,8 C16,5.8 14.2,4 12,4 C9.8,4 8,5.8 8,8 " +
            "C8,10.2 9.8,12 12,12 Z M12,14 C7.6,14 4,16.7 4,20 L4,21 L20,21 L20,20 C20,16.7 16.4,14 12,14 Z");

        /// <summary>Coração: questões de saúde da mulher.</summary>
        public static readonly Geometry Coracao = Desenhar(
            "M12,21 C12,21 3.5,14.9 3.5,9.2 C3.5,6.2 5.9,3.8 8.9,3.8 C10.5,3.8 11.5,4.7 12,5.4 " +
            "C12.5,4.7 13.5,3.8 15.1,3.8 C18.1,3.8 20.5,6.2 20.5,9.2 C20.5,14.9 12,21 12,21 Z");

        /// <summary>Maleta: vínculo, jornada e férias.</summary>
        public static readonly Geometry Maleta = Desenhar(
            "M9.5,3 L14.5,3 C15.3,3 16,3.7 16,4.5 L16,6 L20,6 C20.6,6 21,6.4 21,7 L21,19 " +
            "C21,19.6 20.6,20 20,20 L4,20 C3.4,20 3,19.6 3,19 L3,7 C3,6.4 3.4,6 4,6 L8,6 " +
            "L8,4.5 C8,3.7 8.7,3 9.5,3 Z M10,4.8 L10,6 L14,6 L14,4.8 Z");

        /// <summary>Xícara: hábitos de consumo e atividade física.</summary>
        public static readonly Geometry Xicara = Desenhar(
            "M4,5 L16,5 L16,8 L18.5,8 C20,8 21,9.2 21,10.8 C21,12.6 19.7,14 18,14 L16,14 " +
            "C15.8,16.8 13.4,19 10,19 C6.5,19 4,16.5 4,13 Z M16,10 L16,12 L18,12 " +
            "C18.6,12 19,11.5 19,10.9 C19,10.4 18.7,10 18.2,10 Z M4,20.5 L16,20.5 L16,22 L4,22 Z");

        /// <summary>Cruz: histórico clínico.</summary>
        public static readonly Geometry Cruz = Desenhar(
            "M10,3 L14,3 L14,9 L20,9 L20,13 L14,13 L14,19 L10,19 L10,13 L4,13 L4,9 L10,9 Z");

        /// <summary>Lua: sono e medicação para dormir.</summary>
        public static readonly Geometry Lua = Desenhar(
            "M20.5,14.5 C19.3,15.2 17.9,15.6 16.4,15.6 C11.8,15.6 8.1,11.9 8.1,7.3 " +
            "C8.1,5.8 8.5,4.4 9.2,3.2 C5.6,4.4 3,7.8 3,11.8 C3,16.9 7.1,21 12.2,21 " +
            "C16.2,21 19.6,18.4 20.5,14.5 Z");

        /// <summary>Óculos de RV: experiência prévia com HMD.</summary>
        public static readonly Geometry OculosDeRv = Desenhar(
            "M3.5,8 L20.5,8 C21.3,8 22,8.7 22,9.5 L22,14.5 C22,15.3 21.3,16 20.5,16 L15.8,16 " +
            "C15.3,16 14.9,15.7 14.6,15.3 L13.3,13.4 C12.7,12.5 11.3,12.5 10.7,13.4 L9.4,15.3 " +
            "C9.1,15.7 8.7,16 8.2,16 L3.5,16 C2.7,16 2,15.3 2,14.5 L2,9.5 C2,8.7 2.7,8 3.5,8 Z");

        /// <summary>Relógio: permanência na sala de descanso.</summary>
        public static readonly Geometry Relogio = Desenhar(
            "M12,3 C7,3 3,7 3,12 C3,17 7,21 12,21 C17,21 21,17 21,12 C21,7 17,3 12,3 Z " +
            "M13,7 L13,12 L17,12 L17,14 L11,14 L11,7 Z");

        /// <summary>Sol: conforto térmico, luminoso e acústico.</summary>
        public static readonly Geometry Sol = Desenhar(
            "M12,7.5 C9.5,7.5 7.5,9.5 7.5,12 C7.5,14.5 9.5,16.5 12,16.5 C14.5,16.5 16.5,14.5 16.5,12 " +
            "C16.5,9.5 14.5,7.5 12,7.5 Z M11,2 L13,2 L13,5 L11,5 Z M11,19 L13,19 L13,22 L11,22 Z " +
            "M2,11 L5,11 L5,13 L2,13 Z M19,11 L22,11 L22,13 L19,13 Z " +
            "M4.2,5.6 L5.6,4.2 L7.8,6.3 L6.3,7.8 Z M16.2,17.7 L17.7,16.2 L19.8,18.4 L18.4,19.8 Z " +
            "M18.4,4.2 L19.8,5.6 L17.7,7.8 L16.2,6.3 Z M6.3,16.2 L7.8,17.7 L5.6,19.8 L4.2,18.4 Z");

        /// <summary>Janela: percepção do exterior a partir da sala.</summary>
        public static readonly Geometry Janela = Desenhar(
            "M4,3 L20,3 C20.6,3 21,3.4 21,4 L21,20 C21,20.6 20.6,21 20,21 L4,21 " +
            "C3.4,21 3,20.6 3,20 L3,4 C3,3.4 3.4,3 4,3 Z M5,5 L5,11 L11,11 L11,5 Z " +
            "M13,5 L13,11 L19,11 L19,5 Z M5,13 L5,19 L11,19 L11,13 Z M13,13 L13,19 L19,19 L19,13 Z");

        /// <summary>Folha: presença de natureza e áreas verdes.</summary>
        public static readonly Geometry Folha = Desenhar(
            "M20,3 C20,3 8,3 5.5,8 C3.5,12 5,16 7,17.5 C9,13 13,10.5 16,9.5 " +
            "C13.5,11.5 10,14.5 8.5,19 L6,21 L8,21 L9.5,19.5 C17,20.5 21,13 20,3 Z");

        /// <summary>Triângulo de atenção: sintomas relatados.</summary>
        public static readonly Geometry Alerta = Desenhar(
            "M12,3 L22,20 L2,20 Z M11,9 L13,9 L13,14 L11,14 Z M11,15.5 L13,15.5 L13,17.5 L11,17.5 Z");

        /// <summary>Barras: escores derivados.</summary>
        public static readonly Geometry Barras = Desenhar(
            "M4,20 L4,13 L8,13 L8,20 Z M10,20 L10,8 L14,8 L14,20 Z M16,20 L16,4 L20,4 L20,20 Z");

        /// <summary>Gota: coletas de cortisol salivar.</summary>
        public static readonly Geometry Gota = Desenhar(
            "M12,2.5 C12,2.5 5,10 5,14.8 C5,18.6 8.1,21.5 12,21.5 C15.9,21.5 19,18.6 19,14.8 " +
            "C19,10 12,2.5 12,2.5 Z");

        /// <summary>Olho: medições de rastreamento ocular.</summary>
        public static readonly Geometry Olho = Desenhar(
            "M12,5 C6.5,5 2.5,9.5 1,12 C2.5,14.5 6.5,19 12,19 C17.5,19 21.5,14.5 23,12 " +
            "C21.5,9.5 17.5,5 12,5 Z M12,8.5 C13.9,8.5 15.5,10.1 15.5,12 C15.5,13.9 13.9,15.5 12,15.5 " +
            "C10.1,15.5 8.5,13.9 8.5,12 C8.5,10.1 10.1,8.5 12,8.5 Z");

        /// <summary>Documento: lista de itens de um instrumento.</summary>
        public static readonly Geometry Documento = Desenhar(
            "M6,3 L18,3 C18.6,3 19,3.4 19,4 L19,20 C19,20.6 18.6,21 18,21 L6,21 " +
            "C5.4,21 5,20.6 5,20 L5,4 C5,3.4 5.4,3 6,3 Z M7.5,7 L16.5,7 L16.5,8.6 L7.5,8.6 Z " +
            "M7.5,11 L16.5,11 L16.5,12.6 L7.5,12.6 Z M7.5,15 L13,15 L13,16.6 L7.5,16.6 Z");

        /// <summary>Interrogação: respostas sem grupo mapeado.</summary>
        public static readonly Geometry Interrogacao = Desenhar(
            "M12,3 C7,3 3,7 3,12 C3,17 7,21 12,21 C17,21 21,17 21,12 C21,7 17,3 12,3 Z " +
            "M11,16.5 L13,16.5 L13,18.5 L11,18.5 Z M12,5.8 C13.9,5.8 15.4,7.3 15.4,9.1 " +
            "C15.4,11.4 13,11.5 13,14 L11,14 C11,10.8 13.4,10.9 13.4,9.1 C13.4,8.3 12.8,7.7 12,7.7 " +
            "C11.2,7.7 10.6,8.3 10.6,9.1 L8.6,9.1 C8.6,7.3 10.1,5.8 12,5.8 Z");

        private static readonly Dictionary<string, Geometry> PorGrupo = new()
        {
            [GruposDeQuestoes.Perfil]            = Perfil,
            [GruposDeQuestoes.SaudeDaMulher]     = Coracao,
            [GruposDeQuestoes.Trabalho]          = Maleta,
            [GruposDeQuestoes.Habitos]           = Xicara,
            [GruposDeQuestoes.Saude]             = Cruz,
            [GruposDeQuestoes.Sono]              = Lua,
            [GruposDeQuestoes.ExperienciaEmRv]   = OculosDeRv,
            [GruposDeQuestoes.UsoDaSala]         = Relogio,
            [GruposDeQuestoes.ConfortoAmbiental] = Sol,
            [GruposDeQuestoes.ContatoComExterno] = Janela,
            [GruposDeQuestoes.Natureza]          = Folha,
            [GruposDeQuestoes.Sintomas]          = Alerta,
            [GruposDeQuestoes.Escores]           = Barras,
            [GruposDeQuestoes.Cortisol]          = Gota,
            [GruposDeQuestoes.ColetaEmRv]        = Olho,
            [GruposDeQuestoes.Itens]             = Documento,

            // Os cinco domínios do Índice de Estresse em Enfermeiros.
            [GruposDeQuestoes.IeeeDemandas]      = Barras,
            [GruposDeQuestoes.IeeeCondicoes]     = Maleta,
            [GruposDeQuestoes.IeeeRelacoes]      = Perfil,
            [GruposDeQuestoes.IeeeAssistencia]   = Coracao,
            [GruposDeQuestoes.IeeeAutonomia]     = Relogio,

            [GruposDeQuestoes.Outros]            = Interrogacao,
        };

        // ── Objetos da cena de realidade virtual ─────────────────────────
        // Usados nos cartões de "Objetos mais observados". Cada um representa uma família de
        // elementos projetuais, escolhida pelo nome do objeto no CSV.

        /// <summary>Parede: retângulo com fiadas, para superfícies verticais.</summary>
        public static readonly Geometry Parede = Desenhar(
            "M3,4 L21,4 L21,20 L3,20 Z M3,9.3 L21,9.3 M3,14.6 L21,14.6 M8.5,4 L8.5,9.3 " +
            "M15.5,9.3 L15.5,14.6 M8.5,14.6 L8.5,20");

        /// <summary>Chão: plano em perspectiva.</summary>
        public static readonly Geometry Chao = Desenhar(
            "M2,18 L12,12 L22,18 L12,21 Z M12,12 L12,21");

        /// <summary>Poltrona: assento com encosto e braços.</summary>
        public static readonly Geometry Poltrona = Desenhar(
            "M5,11 L5,7 C5,6 5.7,5.5 6.5,5.5 L17.5,5.5 C18.3,5.5 19,6 19,7 L19,11 " +
            "M4,11 L20,11 L20,16 L4,16 Z M5.5,16 L5.5,19 M18.5,16 L18.5,19");

        /// <summary>Luminária: foco de luz do teto.</summary>
        public static readonly Geometry Luminaria = Desenhar(
            "M12,3 L12,6 M7,12 L17,12 L14.5,6.5 L9.5,6.5 Z M9,15 L15,15 M10.5,18 L13.5,18");

        /// <summary>Quadro: moldura na parede.</summary>
        public static readonly Geometry Quadro = Desenhar(
            "M4,5 L20,5 L20,17 L4,17 Z M6.5,14.5 L10,10.5 L13,13.5 L15.5,11 L18,14.5 Z");

        /// <summary>Ventilador: pás em rotação.</summary>
        public static readonly Geometry Ventilador = Desenhar(
            "M12,10.5 C13,10.5 13.5,11 13.5,12 C13.5,13 13,13.5 12,13.5 C11,13.5 10.5,13 10.5,12 " +
            "C10.5,11 11,10.5 12,10.5 Z M12,10.5 C12,7 10,4 7,4.5 C5,5 5.5,9 9,10.5 Z " +
            "M13.5,12 C16.5,10.5 20,11 20.5,14 C21,16 17,16.5 13.5,13.5 Z " +
            "M10.5,13.5 C10,17 7,20 4.5,18.5 C3,17.5 5.5,14.5 9,13.5 Z");

        /// <summary>Objeto genérico: cubo, para o que não se enquadra nos demais.</summary>
        public static readonly Geometry Cubo = Desenhar(
            "M12,2.5 L21,7.2 L21,16.8 L12,21.5 L3,16.8 L3,7.2 Z M3,7.2 L12,12 L21,7.2 M12,12 L12,21.5");

        /// <summary>Cubos empilhados: contagem de elementos distintos.</summary>
        public static readonly Geometry Camadas = Desenhar(
            "M12,3 L21,7.5 L12,12 L3,7.5 Z M3,12 L12,16.5 L21,12 M3,16.5 L12,21 L21,16.5");

        /// <summary>Mapa: cenários percorridos.</summary>
        public static readonly Geometry Mapa = Desenhar(
            "M3,6 L9,3.5 L15,6 L21,3.5 L21,18 L15,20.5 L9,18 L3,20.5 Z M9,3.5 L9,18 M15,6 L15,20.5");

        /// <summary>
        /// Ícone do objeto da cena, escolhido pelo nome que o CSV traz.
        /// </summary>
        /// <remarks>
        /// A correspondência é por trecho do nome, e na ordem em que os testes aparecem: os nomes
        /// reais do arquivo são compostos ("Parede: Lateral Janela" contém tanto "Parede" quanto
        /// "Janela"), e o primeiro termo é o que define a família do elemento.
        ///
        /// Um nome não reconhecido recebe o cubo — nenhum objeto fica sem ícone.
        /// </remarks>
        public static Geometry ObterPorObjeto(string nomeDoObjeto)
        {
            if (string.IsNullOrWhiteSpace(nomeDoObjeto))
                return Cubo;

            var nome = nomeDoObjeto.Trim();

            foreach (var (trecho, icone) in new (string, Geometry)[]
                     {
                         ("Parede",     Parede),
                         ("Chão",       Chao),
                         ("Piso",       Chao),
                         ("Poltrona",   Poltrona),
                         ("Cadeira",    Poltrona),
                         ("Sofá",       Poltrona),
                         ("Painel LED", Luminaria),
                         ("Luz",        Luminaria),
                         ("Lâmpada",    Luminaria),
                         ("Teto",       Luminaria),
                         ("Janela",     Janela),
                         ("Fora",       Folha),
                         ("Planta",     Folha),
                         ("Jabuticab",  Folha),
                         ("Manga",      Folha),
                         ("Quadro",     Quadro),
                         ("Decoração",  Quadro),
                         ("Ventilador", Ventilador),
                     })
            {
                if (nome.IndexOf(trecho, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return icone;
            }

            return Cubo;
        }

        /// <summary>
        /// Ícone do grupo informado; a interrogação quando o grupo não tem ícone próprio.
        /// </summary>
        public static Geometry ObterPorGrupo(string grupo)
        {
            return grupo != null && PorGrupo.TryGetValue(grupo, out var icone)
                ? icone
                : Interrogacao;
        }
    }
}
