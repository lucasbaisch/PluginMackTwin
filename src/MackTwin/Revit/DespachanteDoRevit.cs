using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace MackTwin.Revit
{
    /// <summary>
    /// Leva ações da janela (modeless) para dentro do contexto da API do Revit.
    /// </summary>
    /// <remarks>
    /// O Revit só aceita chamadas à sua API dentro de um contexto válido — um comando, um evento
    /// ou um <see cref="ExternalEvent"/>. A janela do painel fica aberta enquanto o usuário
    /// trabalha no modelo, logo um clique nela acontece FORA desse contexto, e pintar uma sala
    /// direto do handler do botão lançaria exceção. A solução canônica é esta: a ação entra numa
    /// fila, <see cref="ExternalEvent.Raise"/> pede a vez, e o Revit chama
    /// <see cref="Execute"/> quando estiver ocioso, já no contexto certo e na thread da interface.
    ///
    /// Deve ser criado dentro de um contexto válido (o <c>Execute</c> do comando que abre a
    /// janela), porque <see cref="ExternalEvent.Create"/> tem a mesma exigência.
    /// </remarks>
    public sealed class DespachanteDoRevit : IExternalEventHandler
    {
        private readonly ConcurrentQueue<Action<UIApplication>> _fila = new();
        private readonly ExternalEvent _evento;

        /// <summary>Disparado, na thread da interface, quando uma ação lança exceção.</summary>
        public event Action<Exception>? Falhou;

        /// <summary>Cria o despachante. Chamar de dentro de um contexto válido da API.</summary>
        public DespachanteDoRevit()
        {
            _evento = ExternalEvent.Create(this);
        }

        /// <summary>Agenda uma ação para rodar no contexto da API, na próxima oportunidade.</summary>
        public void Executar(Action<UIApplication> acao)
        {
            _fila.Enqueue(acao ?? throw new ArgumentNullException(nameof(acao)));
            _evento.Raise();
        }

        /// <summary>Chamado pelo Revit: esvazia a fila, isolando a falha de uma ação das demais.</summary>
        public void Execute(UIApplication aplicacao)
        {
            while (_fila.TryDequeue(out var acao))
            {
                try
                {
                    acao(aplicacao);
                }
                catch (Exception excecao)
                {
                    Falhou?.Invoke(excecao);
                }
            }
        }

        public string GetName() => "MackTwin — ações no Revit";
    }
}
