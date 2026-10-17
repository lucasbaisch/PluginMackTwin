using Autodesk.Revit.DB;
using PluginCAISM.Configuracao;

namespace PluginCAISM.Revit
{
    /// <summary>
    /// Converte as coordenadas gravadas pelo registrador de realidade virtual para o sistema de
    /// coordenadas do Revit.
    /// </summary>
    /// <remarks>
    /// <b>Só há conversão de UNIDADE: metros para pés.</b> Os eixos não são permutados, porque o
    /// arquivo de captura já vem em convenção Z para cima, a mesma do Revit — X e Y descrevem a
    /// planta e Z, a altura.
    ///
    /// <b>Como isso foi determinado</b> (01/09/2026). A versão anterior pressupunha a convenção
    /// nativa do Unity, com Y para cima, e aplicava o mapeamento (X, Y, Z) → (X, Z, Y). O
    /// resultado eram hitpoints lançados fora do volume construído e abaixo do piso. A inspeção
    /// dos dados desfez a suposição:
    /// <list type="bullet">
    /// <item>nos pontos de incidência, o objeto "TETO" apresenta terceira coordenada constante em
    /// 3,35 m, e o "ENCANAMENTO", imediatamente abaixo dele, entre 3,16 e 3,34 m — valores que só
    /// fazem sentido como ALTURA;</item>
    /// <item>a terceira coordenada da posição do observador varia de 0,91 a 1,30 m, que é a altura
    /// dos olhos de uma pessoa sentada;</item>
    /// <item>a segunda coordenada é sempre negativa (de −4,19 a −0,26 m), de sorte que, tomada
    /// como altura, poria toda a cena abaixo do piso.</item>
    /// </list>
    ///
    /// Se um registrador futuro gravar em convenção Y para cima, é AQUI que a permuta deve ser
    /// reintroduzida — e a verificação acima é o teste que decide qual convenção o arquivo traz.
    /// </remarks>
    public class ConversorDeCoordenadas
    {
        /// <summary>
        /// Converte um ponto 3D das coordenadas do ambiente virtual (metros) para coordenadas do
        /// Revit (pés), preservando os eixos.
        /// </summary>
        /// <param name="x">Coordenada X no espaço virtual (metros, planta).</param>
        /// <param name="y">Coordenada Y no espaço virtual (metros, planta).</param>
        /// <param name="z">Coordenada Z no espaço virtual (metros, altura).</param>
        /// <returns>Ponto em coordenadas Revit (pés).</returns>
        public XYZ ConverterParaRevit(double x, double y, double z)
        {
            return new XYZ(
                x * ConversoesDeUnidade.MetrosParaPes,
                y * ConversoesDeUnidade.MetrosParaPes,
                z * ConversoesDeUnidade.MetrosParaPes);
        }

        /// <summary>
        /// Converte uma distância de metros para pés.
        /// </summary>
        /// <param name="distanciaEmMetros">Distância em metros.</param>
        /// <returns>Distância equivalente em pés.</returns>
        public double ConverterMetrosParaPes(double distanciaEmMetros)
        {
            return distanciaEmMetros * ConversoesDeUnidade.MetrosParaPes;
        }
    }
}
