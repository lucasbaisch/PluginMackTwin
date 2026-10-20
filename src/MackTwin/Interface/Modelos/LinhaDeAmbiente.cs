using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;
using MackTwin.Configuracao;
using MackTwin.Dominio;
using MackTwin.Interface.Recursos;
using MackTwin.Processamento;

namespace MackTwin.Interface.Modelos
{
    /// <summary>
    /// Uma sala na lista da janela: o ambiente, o seu resumo, as suas planilhas e os
    /// textos/cores prontos para o <c>DataTemplate</c>.
    /// </summary>
    /// <remarks>
    /// A linha é <b>atualizada em lugar</b> a cada ciclo, em vez de recriada: a lista mantém a
    /// seleção, a rolagem e os itens visuais, e só o que mudou é redesenhado — é para isso que
    /// ela implementa <see cref="INotifyPropertyChanged"/>. <see cref="Assinatura"/> resume o
    /// que afeta cartões e gráfico, para que a janela só os reconstrua quando a sala exibida
    /// realmente mudou. <see cref="ModoEdicao"/> é o estado global copiado para dentro da linha:
    /// é o que os gatilhos do template combinam com "selecionado" para mostrar o cadastro.
    /// </remarks>
    public sealed class LinhaDeAmbiente : INotifyPropertyChanged
    {
        private static readonly PropertyChangedEventArgs TudoMudou = new(string.Empty);

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>A sala.</summary>
        public Ambiente Ambiente { get; private set; }

        /// <summary>O resumo na janela de tempo escolhida.</summary>
        public ResumoDoAmbiente Resumo { get; private set; }

        /// <summary>As planilhas desta sala (marcadas com ela ou que a alimentam).</summary>
        public IReadOnlyList<PlanilhaRegistrada> Planilhas { get; private set; }

        /// <summary>Se a janela está em modo Editar.</summary>
        public bool ModoEdicao { get; private set; }

        /// <summary>Código da sala (chave).</summary>
        public string Codigo => Ambiente.Codigo;

        /// <summary>Título: código e, se casou, o nome no modelo.</summary>
        public string Titulo => Ambiente.Titulo;

        /// <summary>Última temperatura e umidade, com a hora.</summary>
        public string Detalhe { get; private set; } = string.Empty;

        /// <summary>"Room nº X no modelo" ou "sem Room correspondente".</summary>
        public string Casamento { get; private set; } = string.Empty;

        /// <summary>Pincel da classificação geral, para o indicador colorido.</summary>
        public Brush Cor { get; private set; } = Brushes.Transparent;

        /// <summary>Texto da classificação geral.</summary>
        public string Classificacao => ClassificadorDeConforto.Descrever(Resumo.ClassificacaoGeral);

        /// <summary>"nenhuma planilha", "1 planilha" ou "N planilhas" — rodapé do cartão não selecionado.</summary>
        public string ResumoDePlanilhas => Planilhas.Count switch
        {
            0 => "nenhuma planilha",
            1 => "1 planilha",
            var n => $"{n} planilhas",
        };

        /// <summary>Rótulo do botão de cadastro, com o nome da sala para não haver dúvida.</summary>
        public string RotuloDeAdicionar => $"+  Adicionar planilha a {Codigo}";

        /// <summary>
        /// O que define cartões e gráfico desta sala: última leitura, sensores, janela e Room.
        /// Igual entre dois ciclos = nada a redesenhar.
        /// </summary>
        public string Assinatura { get; private set; } = string.Empty;

        public LinhaDeAmbiente(
            Ambiente ambiente,
            ResumoDoAmbiente resumo,
            IReadOnlyList<PlanilhaRegistrada> planilhas,
            bool modoEdicao,
            TimeSpan? janela)
        {
            Ambiente = ambiente;
            Resumo = resumo;
            Planilhas = planilhas;
            Atualizar(ambiente, resumo, planilhas, modoEdicao, janela);
        }

        /// <summary>Troca o conteúdo da linha e avisa os bindings — sem recriar o item visual.</summary>
        public void Atualizar(
            Ambiente ambiente,
            ResumoDoAmbiente resumo,
            IReadOnlyList<PlanilhaRegistrada> planilhas,
            bool modoEdicao,
            TimeSpan? janela)
        {
            Ambiente = ambiente;
            Resumo = resumo;
            Planilhas = planilhas;
            ModoEdicao = modoEdicao;

            Detalhe = MontarDetalhe(resumo);
            Casamento = ambiente.CasadoComOModelo
                ? $"Room {ambiente.NumeroNoModelo} no modelo"
                : "sem Room correspondente no modelo";
            Cor = Paleta.Pincel(Paleta.CorDeConforto(resumo.ClassificacaoGeral));

            Assinatura = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}",
                ambiente.Codigo,
                ambiente.UltimaLeitura?.Timestamp.Ticks ?? 0,
                ambiente.Sensores.Count,
                janela?.Ticks ?? -1,
                ambiente.IdDoElementoNoModelo ?? 0);

            PropertyChanged?.Invoke(this, TudoMudou);
        }

        private static string MontarDetalhe(ResumoDoAmbiente resumo)
        {
            if (resumo.UltimaLeitura == null)
                return "sem leituras";

            var cultura = CultureInfo.CurrentCulture;
            string temperatura = resumo.Temperatura.TemDados
                ? string.Format(cultura, "{0:0.0} °C", resumo.Temperatura.Ultimo)
                : "— °C";
            string umidade = resumo.Umidade.TemDados
                ? string.Format(cultura, "{0:0} %", resumo.Umidade.Ultimo)
                : "— %";

            return string.Format(cultura, "{0} · {1} · {2:dd/MM HH:mm}",
                temperatura, umidade, resumo.UltimaLeitura.Timestamp);
        }

        public override string ToString() => Titulo;
    }
}
