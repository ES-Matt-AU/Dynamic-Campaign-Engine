using System;

namespace CampaignEngine.Core
{
    /// <summary>
    /// A position on the map in metres. X is east and Z is north, matching VTOL VR's world axes.
    /// </summary>
    public struct Vec2
    {
        public double X;
        public double Z;

        public Vec2(double x, double z)
        {
            X = x;
            Z = z;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Z + b.Z);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Z - b.Z);
        public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Z * s);

        public double Length() => Math.Sqrt(X * X + Z * Z);

        public Vec2 Normalised()
        {
            double len = Length();
            return len < 1e-9 ? new Vec2(0, 0) : new Vec2(X / len, Z / len);
        }

        public static double Distance(Vec2 a, Vec2 b) => (a - b).Length();

        public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new Vec2(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);

        public override string ToString() => $"{X / 1000:0.0} km E, {Z / 1000:0.0} km N";
    }
}
