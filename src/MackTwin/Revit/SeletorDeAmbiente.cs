using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using MackTwin.Dominio;

namespace MackTwin.Revit
{
    /// <summary>Um Room do modelo, reduzido ao que o casamento precisa.</summary>
    public sealed class RoomDoModelo
    {
        /// <summary>Identificador do elemento.</summary>
        public ElementId Id { get; init; } = ElementId.InvalidElementId;

        /// <summary>Parâmetro "Number" do Room, como digitado.</summary>
        public string Numero { get; init; } = string.Empty;

        /// <summary>Parâmetro "Name" do Room.</summary>
        public string Nome { get; init; } = string.Empty;

        /// <summary>Número normalizado — a chave preferencial de casamento.</summary>
        public string CodigoDoNumero => CodigoDeSala.Normalizar(Numero);

        /// <summary>Nome normalizado — a chave alternativa.</summary>
        public string CodigoDoNome => CodigoDeSala.Normalizar(Nome);
    }

    /// <summary>
    /// Liga as salas das planilhas aos Rooms do modelo e localiza a sala que o usuário
    /// selecionou no Revit.
    /// </summary>
    /// <remarks>
    /// O casamento é pelo código normalizado (<see cref="CodigoDeSala"/>): primeiro contra o
    /// NÚMERO do Room — é o campo que o projetista usa para codificar salas, e é curto —, depois
    /// contra o NOME, para o modelo em que alguém escreveu "LAB001" no nome e deixou o número em
    /// branco. Nenhum cadastro manual: a planilha e o modelo já trazem a chave.
    ///
    /// Rooms não colocados (sem <c>Location</c>) são ignorados: existem na tabela do projeto mas
    /// não têm geometria para pintar.
    ///
    /// Todos os métodos que leem o documento devem rodar dentro de um contexto válido da API
    /// (comando, evento ou <see cref="DespachanteDoRevit"/>).
    /// </remarks>
    public sealed class SeletorDeAmbiente
    {
        private readonly Document _documento;

        /// <summary>Cria o seletor sobre o documento aberto.</summary>
        public SeletorDeAmbiente(Document documento)
        {
            _documento = documento ?? throw new ArgumentNullException(nameof(documento));
        }

        /// <summary>Todos os Rooms colocados do modelo.</summary>
        public IReadOnlyList<RoomDoModelo> ColetarRooms()
        {
            return new FilteredElementCollector(_documento)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(room => room.Location != null)
                .Select(room => new RoomDoModelo
                {
                    Id = room.Id,
                    Numero = room.Number ?? string.Empty,
                    Nome = NomeDoRoom(room),
                })
                .ToList();
        }

        /// <summary>
        /// Preenche, em cada ambiente do conjunto, o vínculo com o Room correspondente (ou o
        /// limpa, se não há). Devolve quantos casaram.
        /// </summary>
        public int CasarComOModelo(ConjuntoDeAmbientes conjunto)
        {
            var rooms = ColetarRooms();
            int casados = 0;

            foreach (var ambiente in conjunto.Ambientes)
            {
                var room = rooms.FirstOrDefault(r => r.CodigoDoNumero == ambiente.Codigo)
                           ?? rooms.FirstOrDefault(r => r.CodigoDoNome == ambiente.Codigo);

                if (room == null)
                {
                    ambiente.IdDoElementoNoModelo = null;
                    ambiente.NomeNoModelo = null;
                    ambiente.NumeroNoModelo = null;
                    continue;
                }

                ambiente.IdDoElementoNoModelo = room.Id.Value;
                ambiente.NomeNoModelo = room.Nome;
                ambiente.NumeroNoModelo = room.Numero;
                casados++;
            }

            return casados;
        }

        /// <summary>Nome do Room pelo parâmetro ROOM_NAME, que é o campo "Name" da tabela.</summary>
        public static string NomeDoRoom(Room room)
        {
            return room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? string.Empty;
        }

        /// <summary>Código normalizado do número do Room — a chave de casamento.</summary>
        public static string CodigoDoRoom(Room room)
        {
            return CodigoDeSala.Normalizar(room.Number);
        }

        /// <summary>Leva a vista até o elemento e o deixa selecionado.</summary>
        public static void MostrarNoModelo(UIDocument uiDocumento, long idDoElemento)
        {
            var id = new ElementId(idDoElemento);
            uiDocumento.ShowElements(id);
            uiDocumento.Selection.SetElementIds(new List<ElementId> { id });
        }
    }
}
