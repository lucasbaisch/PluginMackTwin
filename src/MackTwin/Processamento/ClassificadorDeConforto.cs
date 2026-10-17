using System.Collections.Generic;
using MackTwin.Configuracao;
using MackTwin.Dominio;

namespace MackTwin.Processamento
{
    /// <summary>
    /// Posiciona um valor em relação à sua faixa de conforto e combina classificações de várias
    /// grandezas num estado único para a sala.
    /// </summary>
    /// <remarks>
    /// Cálculo puro, sem estado: é o que a verificação do modelo exercita diretamente. Os
    /// limites vêm de <see cref="FaixasDeConfortoAmbiental"/> e os extremos pertencem à faixa
    /// (21,0 °C é confortável; 20,9 °C não).
    /// </remarks>
    public static class ClassificadorDeConforto
    {
        /// <summary>Classifica um valor contra [mínimo, máximo]. NaN é <see cref="ClassificacaoDeConforto.SemDados"/>.</summary>
        public static ClassificacaoDeConforto Classificar(double valor, double minimo, double maximo)
        {
            if (double.IsNaN(valor))
                return ClassificacaoDeConforto.SemDados;

            if (valor < minimo)
                return ClassificacaoDeConforto.AbaixoDaFaixa;

            if (valor > maximo)
                return ClassificacaoDeConforto.AcimaDaFaixa;

            return ClassificacaoDeConforto.Confortavel;
        }

        /// <summary>Classifica uma temperatura em °C.</summary>
        public static ClassificacaoDeConforto ClassificarTemperatura(double graus)
        {
            return Classificar(graus,
                FaixasDeConfortoAmbiental.TemperaturaMinima,
                FaixasDeConfortoAmbiental.TemperaturaMaxima);
        }

        /// <summary>Classifica uma umidade relativa em %.</summary>
        public static ClassificacaoDeConforto ClassificarUmidade(double percentual)
        {
            return Classificar(percentual,
                FaixasDeConfortoAmbiental.UmidadeMinima,
                FaixasDeConfortoAmbiental.UmidadeMaxima);
        }

        /// <summary>
        /// Pior caso entre várias classificações: qualquer "acima" prevalece, depois qualquer
        /// "abaixo", depois "confortável"; só é "sem dados" se todas forem.
        /// </summary>
        /// <remarks>
        /// "Acima" antes de "abaixo" é uma escolha de exibição, não de gravidade: quando as duas
        /// grandezas apontam para lados opostos a sala está desconfortável de qualquer modo, e a
        /// cor precisa ser determinística. O detalhe por grandeza continua visível nos cartões.
        /// </remarks>
        public static ClassificacaoDeConforto Combinar(IEnumerable<ClassificacaoDeConforto> classificacoes)
        {
            bool algumaAcima = false;
            bool algumaAbaixo = false;
            bool algumaConfortavel = false;

            foreach (var classificacao in classificacoes)
            {
                switch (classificacao)
                {
                    case ClassificacaoDeConforto.AcimaDaFaixa: algumaAcima = true; break;
                    case ClassificacaoDeConforto.AbaixoDaFaixa: algumaAbaixo = true; break;
                    case ClassificacaoDeConforto.Confortavel: algumaConfortavel = true; break;
                }
            }

            if (algumaAcima) return ClassificacaoDeConforto.AcimaDaFaixa;
            if (algumaAbaixo) return ClassificacaoDeConforto.AbaixoDaFaixa;
            if (algumaConfortavel) return ClassificacaoDeConforto.Confortavel;
            return ClassificacaoDeConforto.SemDados;
        }

        /// <summary>Variante com argumentos soltos, para o chamador que tem duas ou três grandezas.</summary>
        public static ClassificacaoDeConforto Combinar(params ClassificacaoDeConforto[] classificacoes)
        {
            return Combinar((IEnumerable<ClassificacaoDeConforto>)classificacoes);
        }

        /// <summary>Texto curto da classificação, para rótulos e relatórios.</summary>
        public static string Descrever(ClassificacaoDeConforto classificacao)
        {
            return classificacao switch
            {
                ClassificacaoDeConforto.Confortavel => "Confortável",
                ClassificacaoDeConforto.AbaixoDaFaixa => "Abaixo da faixa",
                ClassificacaoDeConforto.AcimaDaFaixa => "Acima da faixa",
                _ => "Sem dados",
            };
        }
    }
}
