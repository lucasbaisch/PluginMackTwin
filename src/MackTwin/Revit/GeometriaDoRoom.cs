using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace MackTwin.Revit
{
    /// <summary>
    /// Geometria derivada de um Room: o volume fechado (para os sólidos temporários) e a face do
    /// piso (para o mapa de calor).
    /// </summary>
    /// <remarks>
    /// O caminho principal é <see cref="Room.ClosedShell"/>, que já traz o volume calculado pelo
    /// Revit (exige "Áreas e volumes" ligado). Quando ele vem vazio — projeto em "Somente áreas",
    /// sala sem limite superior —, o volume é reconstruído por extrusão dos segmentos de
    /// contorno pela altura não limitada da sala. O piso é sempre a extrusão fina do contorno,
    /// porque é um plano simples e previsível para o AVF amostrar.
    /// </remarks>
    public static class GeometriaDoRoom
    {
        private const double VolumeMinimo = 1e-6;
        private const double AlturaPadraoEmPes = 3.0 / 0.3048;

        /// <summary>Espessura da "lâmina" de piso usada pelo mapa de calor.</summary>
        public const double EspessuraDaLaminaEmPes = 0.02;

        /// <summary>Quanto a lâmina do mapa de calor flutua acima do piso, para não ser engolida por ele.</summary>
        public const double FlutuacaoDaLaminaEmPes = 0.03;

        /// <summary>Os sólidos do Room, clonados (prontos para um DirectShape). Vazio se nada deu certo.</summary>
        public static IList<Solid> Solidos(Room room)
        {
            var solidos = new List<Solid>();

            try
            {
                foreach (var objeto in room.ClosedShell)
                {
                    if (objeto is Solid solido && solido.Volume > VolumeMinimo)
                        solidos.Add(SolidUtils.Clone(solido));
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Sem casca fechada: cai para a extrusão.
            }

            if (solidos.Count > 0)
                return solidos;

            var contornos = Contornos(room);

            if (contornos.Count == 0)
                return solidos;

            double altura = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT)?.AsDouble() ?? 0;

            if (altura <= 0)
                altura = AlturaPadraoEmPes;

            try
            {
                solidos.Add(GeometryCreationUtilities.CreateExtrusionGeometry(contornos, XYZ.BasisZ, altura));
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Contorno que a extrusão não aceita (auto-interseção); a sala fica sem sólido.
            }

            return solidos;
        }

        /// <summary>Os laços de contorno do Room (acabamento), na cota do piso da sala.</summary>
        public static IList<CurveLoop> Contornos(Room room)
        {
            var lacos = new List<CurveLoop>();
            var opcoes = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish,
            };

            IList<IList<BoundarySegment>>? segmentosPorLaco;

            try
            {
                segmentosPorLaco = room.GetBoundarySegments(opcoes);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return lacos;
            }

            if (segmentosPorLaco == null)
                return lacos;

            foreach (var segmentos in segmentosPorLaco)
            {
                try
                {
                    var laco = CurveLoop.Create(segmentos.Select(segmento => segmento.GetCurve()).ToList());

                    if (!laco.IsOpen())
                        lacos.Add(laco);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // Laço aberto ou inválido: ignorado.
                }
            }

            return lacos;
        }

        /// <summary>
        /// A face superior de uma lâmina fina com a forma do piso da sala, flutuando um pouco acima
        /// dele — a superfície sobre a qual o mapa de calor é desenhado. Null se não há contorno.
        /// </summary>
        public static Face? FaceDoPiso(Room room)
        {
            var contornos = Contornos(room);

            if (contornos.Count == 0)
                return null;

            try
            {
                var lamina = GeometryCreationUtilities.CreateExtrusionGeometry(contornos, XYZ.BasisZ, EspessuraDaLaminaEmPes);
                var erguida = SolidUtils.CreateTransformed(lamina, Transform.CreateTranslation(new XYZ(0, 0, FlutuacaoDaLaminaEmPes)));

                foreach (Face face in erguida.Faces)
                {
                    if (face is PlanarFace plana && plana.FaceNormal.Z > 0.9)
                        return face;
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Contorno que a extrusão não aceita.
            }

            return null;
        }
    }
}
