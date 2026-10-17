using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using MackTwin.Dominio;

namespace MackTwin.Revit
{
    /// <summary>
    /// Relatório em texto do que o plug-in enxerga no modelo: Rooms, vista ativa, categorias e
    /// o resultado do casamento com as salas das planilhas.
    /// </summary>
    /// <remarks>
    /// Existe para que "não pintou nada" vire uma linha concreta — Room sem volume, categoria
    /// oculta, template na vista, número que não casa. Só lê; não altera nada. Deve rodar
    /// dentro de um contexto válido da API.
    /// </remarks>
    public static class DiagnosticoDoModelo
    {
        private const double PesQuadradosParaMetrosQuadrados = 0.09290304;
        private const double PesCubicosParaMetrosCubicos = 0.0283168466;
        private const double PesParaMetros = 0.3048;

        /// <summary>Monta o relatório do documento ativo.</summary>
        public static string Gerar(UIDocument uiDocumento, ConjuntoDeAmbientes? conjunto)
        {
            var documento = uiDocumento.Document;
            var vista = uiDocumento.ActiveView;
            var cultura = CultureInfo.CurrentCulture;
            var linhas = new List<string>
            {
                $"=== Diagnóstico MackTwin — {DateTime.Now:dd/MM/yyyy HH:mm:ss} ===",
                $"Modelo: \"{documento.Title}\"",
            };

            // ── projeto ──
            var configuracao = AreaVolumeSettings.GetAreaVolumeSettings(documento);
            linhas.Add("Cálculo de volumes do projeto: " + (configuracao.ComputeVolumes ? "LIGADO (áreas e volumes)" : "DESLIGADO (somente áreas) — Rooms não aparecem em 3D"));

            // ── vista ativa ──
            string template = vista.ViewTemplateId == ElementId.InvalidElementId
                ? "nenhum"
                : $"\"{documento.GetElement(vista.ViewTemplateId)?.Name}\"";

            linhas.Add($"Vista ativa: \"{vista.Name}\" — tipo {vista.ViewType}, estilo {vista.DisplayStyle}, template {template}, aceita pintura: {(PintorDeAmbientes.VistaAceitaPintura(vista) ? "sim" : "NÃO")}");

            var categoriaRooms = Category.GetCategory(documento, BuiltInCategory.OST_Rooms);

            if (categoriaRooms != null)
            {
                linhas.Add($"Categoria \"{categoriaRooms.Name}\" na vista: {(vista.GetCategoryHidden(categoriaRooms.Id) ? "OCULTA" : "visível")}");

                foreach (Category subcategoria in categoriaRooms.SubCategories)
                    linhas.Add($"    subcategoria \"{subcategoria.Name}\": {(vista.GetCategoryHidden(subcategoria.Id) ? "OCULTA" : "visível")}");
            }

            // ── rooms ──
            var rooms = new FilteredElementCollector(documento)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .OrderBy(room => room.Number, StringComparer.OrdinalIgnoreCase)
                .ToList();

            linhas.Add($"Rooms no modelo: {rooms.Count}");

            foreach (var room in rooms)
            {
                double area = room.Area * PesQuadradosParaMetrosQuadrados;
                double volume = room.Volume * PesCubicosParaMetrosCubicos;
                double deslocamento = (room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET)?.AsDouble() ?? 0.0) * PesParaMetros;
                string nome = SeletorDeAmbiente.NomeDoRoom(room);

                linhas.Add(string.Format(cultura,
                    "  Room nº \"{0}\" (código {1}), nome \"{2}\": {3}, área {4:0.0} m², volume {5:0.0} m³, desloc. superior {6:0.00} m, nível \"{7}\", id {8}",
                    room.Number, CodigoDeSala.Normalizar(room.Number), nome,
                    room.Location == null ? "NÃO colocado" : "colocado",
                    area, volume, deslocamento, room.Level?.Name, room.Id.Value));

                var sobreposicao = vista.GetElementOverrides(room.Id);
                var cor = sobreposicao.SurfaceForegroundPatternColor;

                linhas.Add(string.Format(cultura,
                    "      sobreposição na vista ativa: padrão de superfície {0}, cor {1}, transparência {2}",
                    sobreposicao.IsSurfaceForegroundPatternVisible ? "visível" : "não visível",
                    cor.IsValid ? $"RGB({cor.Red},{cor.Green},{cor.Blue})" : "nenhuma",
                    sobreposicao.Transparency));
            }

            // ── salas das planilhas ──
            if (conjunto == null)
            {
                linhas.Add("Salas das planilhas: (nenhum ciclo concluído ainda)");
            }
            else
            {
                linhas.Add($"Salas das planilhas: {conjunto.Ambientes.Count} (atualizado às {conjunto.AtualizadoEm:HH:mm:ss})");

                foreach (var ambiente in conjunto.Ambientes)
                {
                    string casamento = ambiente.CasadoComOModelo
                        ? $"casada com Room nº \"{ambiente.NumeroNoModelo}\" (id {ambiente.IdDoElementoNoModelo})"
                        : "SEM Room correspondente";

                    linhas.Add($"  {ambiente.Codigo}: {casamento}; {ambiente.Sensores.Count} sensor(es): {string.Join(", ", ambiente.Sensores.Select(s => s.Hostname))}");
                }

                foreach (var sensor in conjunto.SensoresSemSala)
                    linhas.Add($"  sensor sem sala: {sensor.Hostname}");
            }

            return string.Join(Environment.NewLine, linhas);
        }
    }
}
