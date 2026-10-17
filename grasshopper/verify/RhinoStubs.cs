// Minimal stand-ins for the Rhino.Geometry types the simulation core uses, so the
// component's "custom additional code" can be compiled and exercised outside Rhino.
// This file is NOT part of the Grasshopper deliverable.
using System;

namespace Rhino.Geometry
{
    /// <summary>
    /// What a script component's "Domain" type hint hands the script. Only the members the
    /// simulation core reads are here.
    /// </summary>
    public struct Interval
    {
        public double T0, T1;

        public Interval(double t0, double t1) { T0 = t0; T1 = t1; }

        public double Min { get { return Math.Min(T0, T1); } }
        public double Max { get { return Math.Max(T0, T1); } }
        public double Length { get { return T1 - T0; } }

        public override string ToString()
        {
            return T0.ToString("0.###") + " to " + T1.ToString("0.###");
        }
    }

    public struct Vector3d
    {
        public double X, Y, Z;

        public Vector3d(double x, double y, double z) { X = x; Y = y; Z = z; }

        public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }

        public static Vector3d operator +(Vector3d a, Vector3d b)
        {
            return new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Vector3d operator -(Vector3d a, Vector3d b)
        {
            return new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static Vector3d operator *(double s, Vector3d v)
        {
            return new Vector3d(v.X * s, v.Y * s, v.Z * s);
        }

        public static Vector3d operator *(Vector3d v, double s)
        {
            return new Vector3d(v.X * s, v.Y * s, v.Z * s);
        }

        public override string ToString()
        {
            return X.ToString("F4") + "," + Y.ToString("F4") + "," + Z.ToString("F4");
        }
    }

    public struct Point3d
    {
        public double X, Y, Z;

        public Point3d(double x, double y, double z) { X = x; Y = y; Z = z; }

        public override string ToString()
        {
            return X.ToString("F4") + "," + Y.ToString("F4") + "," + Z.ToString("F4");
        }
    }
}
