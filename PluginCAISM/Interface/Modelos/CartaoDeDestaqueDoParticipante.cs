using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using PluginCAISM.Configuracao;
using PluginCAISM.Dominio;

namespace PluginCAISM.Interface.Modelos
{
    /// <summary>
    /// Um dos cartões da faixa de destaques no topo da aba de Dados do Participante.
    /// </summary>
    /// <remarks>
    /// A faixa responde de relance "quem é este participante", para que trocar de formulário não
    /// obrigue a recompor o retrato mentalmente a cada vez. Ela NÃO muda com o formulário
    /// selecionado — descreve a pessoa, não o instrumento — e por isso é o único lugar da aba onde
    /// dados de planilhas diferentes aparecem lado a lado.
    /// </remarks>
    public class CartaoDeDestaqueDoParticipante
    {
        /// <summary>Pictograma do cartão.</summary>
        public Geometry Icone { get; init; }

        /// <summary>Nome do dado, em tom secundário e caixa alta.</summary>
        public string Rotulo { get; init; }

        /// <summary>Valor, em destaque.</summary>
        public string Valor { get; init; } = "—";

        /// <summary>
        /// Qualificação do valor — a faixa da OMS para o IMC, a faixa de estresse para o IEEE.
        /// Vazia nos cartões que são apenas descritivos.
        /// </summary>
        public string Qualificador { get; init; } = string.Empty;

        /// <summary>Se há qualificador a exibir.</summary>
        public bool TemQualificador => !string.IsNullOrWhiteSpace(Qualificador);

        /// <summary>Cor de fundo da etiqueta do qualificador.</summary>
        public Brush FundoDoQualificador { get; init; } = PincelNeutroSuave;

        /// <summary>Cor do texto do qualificador.</summary>
        public Brush CorDoQualificador { get; init; } = PincelNeutro;

        // Paleta das etiquetas. Verde/âmbar/vermelho seguem a mesma leitura já usada na tabela de
        // potencial: quanto mais quente, mais desfavorável.
        private static readonly Brush PincelNeutro      = Congelar(0x7A, 0x83, 0x92);
        private static readonly Brush PincelNeutroSuave = Congelar(0xF0, 0xF2, 0xF5);
        private static readonly Brush PincelBom         = Congelar(0x1B, 0x7A, 0x50);
        private static readonly Brush PincelBomSuave    = Congelar(0xE6, 0xF4, 0xEC);
        private static readonly Brush PincelAtencao     = Congelar(0xB5, 0x6B, 0x0E);
        private static readonly Brush PincelAtencaoSuave = Congelar(0xFD, 0xF3, 0xE3);
        private static readonly Brush PincelGrave       = Congelar(0xB3, 0x38, 0x2F);
        private static readonly Brush PincelGraveSuave  = Congelar(0xFB, 0xEC, 0xEB);

        private static Brush Congelar(byte r, byte g, byte b)
        {
            var pincel = new SolidColorBrush(Color.FromRgb(r, g, b));
            pincel.Freeze();
            return pincel;
        }

        /// <summary>
        /// Monta a faixa de destaques de um participante.
        /// </summary>
        /// <param name="respostas">Respostas do participante; null devolve lista vazia.</param>
        /// <param name="registroDeCortisol">Coletas de cortisol, quando houver.</param>
        public static List<CartaoDeDestaqueDoParticipante> Construir(
            RespostasDeFormularioPorParticipante respostas,
            Parsing.RegistroDeCortisol registroDeCortisol)
        {
            if (respostas == null)
                return new List<CartaoDeDestaqueDoParticipante>();

            return new List<CartaoDeDestaqueDoParticipante>
            {
                ConstruirCartaoSimples(respostas, IconesDeGrupo.Perfil, "GÊNERO",
                    MapeamentoDeColunasDeFormulario.Genero),

                ConstruirCartaoSimples(respostas, IconesDeGrupo.Relogio, "IDADE",
                    MapeamentoDeColunasDeFormulario.Idade),

                ConstruirCartaoDeImc(respostas),

                ConstruirCartaoSimples(respostas, IconesDeGrupo.Maleta, "SEÇÃO",
                    MapeamentoDeColunasDeFormulario.DivisaoDeEnfermagem),

                ConstruirCartaoDeIeee(respostas),

                ConstruirCartaoDeCortisol(registroDeCortisol),
            };
        }

        // Os seis cartões próprios do HMD foram REMOVIDOS: a categoria voltou a exibir a mesma
        // faixa de destaques das demais, porque o participante passou a ser o mesmo dos dois lados.
        // Os dados da coleta voltaram ao formato de tabela.

        /// <summary>
        /// Monta um cartão por objeto mais observado, com o ícone do tipo de elemento.
        /// </summary>
        /// <remarks>
        /// Os objetos saíram da célula de texto da tabela e viraram cartões lado a lado: a leitura
        /// que interessa é comparativa — qual elemento reteve mais o olhar —, e uma lista numerada
        /// dentro de uma célula não deixa comparar de relance.
        ///
        /// O percentual entra como qualificador, na mesma etiqueta que os demais cartões usam para
        /// a faixa do IMC e do IEEE.
        /// </remarks>
        public static List<CartaoDeDestaqueDoParticipante> ConstruirObjetosMaisObservados(
            ResumoDoHmdDoParticipante resumo)
        {
            if (resumo?.ObjetosMaisObservados == null)
                return new List<CartaoDeDestaqueDoParticipante>();

            var cultura = CultureInfo.CurrentCulture;
            var cartoes = new List<CartaoDeDestaqueDoParticipante>();
            int posicao = 0;

            foreach (var objeto in resumo.ObjetosMaisObservados)
            {
                posicao++;

                cartoes.Add(new CartaoDeDestaqueDoParticipante
                {
                    Icone  = IconesDeGrupo.ObterPorObjeto(objeto.Nome),
                    Rotulo = $"{posicao}º MAIS OBSERVADO",
                    Valor  = objeto.Nome,
                    Qualificador = $"{objeto.PercentualDeAmostras.ToString("N1", cultura)}% do olhar",
                    FundoDoQualificador = PincelNeutroSuave,
                    CorDoQualificador   = PincelNeutro,
                });
            }

            return cartoes;
        }

        private static CartaoDeDestaqueDoParticipante ConstruirCartaoSimples(
            RespostasDeFormularioPorParticipante respostas,
            Geometry icone, string rotulo, string[] nomesDaColuna)
        {
            LocalizadorDeColunaDeFormulario.TentarLocalizar(
                respostas.RespostasBrutas, nomesDaColuna, out _, out string valor);

            return new CartaoDeDestaqueDoParticipante
            {
                Icone  = icone,
                Rotulo = rotulo,
                Valor  = string.IsNullOrWhiteSpace(valor) ? "—" : valor.Trim(),
            };
        }

        private static CartaoDeDestaqueDoParticipante ConstruirCartaoDeImc(
            RespostasDeFormularioPorParticipante respostas)
        {
            bool temValor = LocalizadorDeColunaDeFormulario.TentarLocalizar(
                                respostas.RespostasBrutas, FaixasDeImc.NomesDaColuna,
                                out _, out string texto)
                         && double.TryParse(texto, NumberStyles.Any,
                                            CultureInfo.InvariantCulture, out double _);

            double imc = double.NaN;
            if (temValor)
                double.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out imc);

            if (double.IsNaN(imc))
            {
                return new CartaoDeDestaqueDoParticipante
                {
                    Icone = IconesDeGrupo.Coracao, Rotulo = "IMC", Valor = "—",
                };
            }

            string faixa = LinhaDeFormularioParaTabela.ClassificarImc(imc);
            bool saudavel = imc >= FaixasDeImc.LimiteDeBaixoPeso
                         && imc <= FaixasDeImc.LimiteDePesoNormal;
            bool grave = imc > FaixasDeImc.LimiteDeSobrepeso;

            return new CartaoDeDestaqueDoParticipante
            {
                Icone               = IconesDeGrupo.Coracao,
                Rotulo              = "IMC",
                Valor               = imc.ToString("N1", CultureInfo.CurrentCulture),
                Qualificador        = faixa,
                CorDoQualificador   = saudavel ? PincelBom : grave ? PincelGrave : PincelAtencao,
                FundoDoQualificador = saudavel ? PincelBomSuave : grave ? PincelGraveSuave : PincelAtencaoSuave,
            };
        }

        private static CartaoDeDestaqueDoParticipante ConstruirCartaoDeIeee(
            RespostasDeFormularioPorParticipante respostas)
        {
            var faixa = ClassificadorDeFaixaDeEstresseIeee.Classificar(respostas.EscoreIeeeTotal);

            if (!faixa.HasValue)
            {
                return new CartaoDeDestaqueDoParticipante
                {
                    Icone = IconesDeGrupo.Barras, Rotulo = "IEEE", Valor = "—",
                };
            }

            return new CartaoDeDestaqueDoParticipante
            {
                Icone        = IconesDeGrupo.Barras,
                Rotulo       = "IEEE",
                Valor        = respostas.EscoreIeeeTotal.ToString("N0", CultureInfo.CurrentCulture),
                Qualificador = ClassificadorDeFaixaDeEstresseIeee.ObterNomeCurto(faixa.Value)
                                   .Replace("estresse ", string.Empty),

                CorDoQualificador = faixa.Value switch
                {
                    FaixaDeEstresseIeee.Baixo         => PincelBom,
                    FaixaDeEstresseIeee.Intermediario => PincelAtencao,
                    _                                  => PincelGrave,
                },

                FundoDoQualificador = faixa.Value switch
                {
                    FaixaDeEstresseIeee.Baixo         => PincelBomSuave,
                    FaixaDeEstresseIeee.Intermediario => PincelAtencaoSuave,
                    _                                  => PincelGraveSuave,
                },
            };
        }

        /// <summary>
        /// Cartão do delta de cortisol (coleta final − basal).
        /// </summary>
        /// <remarks>
        /// O sinal é que informa, não a magnitude: delta negativo indica queda do cortisol ao longo
        /// da exposição, que é o resultado esperado de um ambiente restaurador. Por isso a etiqueta
        /// diz "queda"/"alta" e não repete o número já exibido.
        /// </remarks>
        private static CartaoDeDestaqueDoParticipante ConstruirCartaoDeCortisol(
            Parsing.RegistroDeCortisol registro)
        {
            if (registro == null || double.IsNaN(registro.Delta))
            {
                return new CartaoDeDestaqueDoParticipante
                {
                    Icone = IconesDeGrupo.Gota, Rotulo = "Δ CORTISOL", Valor = "—",
                };
            }

            bool queda = registro.Delta < 0;

            return new CartaoDeDestaqueDoParticipante
            {
                Icone               = IconesDeGrupo.Gota,
                Rotulo              = "Δ CORTISOL",
                Valor               = registro.Delta.ToString("N2", CultureInfo.CurrentCulture),
                Qualificador        = queda ? "queda" : "alta",
                CorDoQualificador   = queda ? PincelBom : PincelAtencao,
                FundoDoQualificador = queda ? PincelBomSuave : PincelAtencaoSuave,
            };
        }
    }
}
