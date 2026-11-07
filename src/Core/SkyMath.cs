using System;

namespace KerbalNews
{
    /// <summary>Small orbital helpers for background news (transfer windows, eclipses). Vectors are double[3] so this is testable without KSP.</summary>
    public static class SkyMath
    {
        public static double Dot(double[] a, double[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
        public static double[] Cross(double[] a, double[] b) { return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] }; }
        public static double Len(double[] a) { return Math.Sqrt(Dot(a, a)); }
        public static double[] Sub(double[] a, double[] b) { return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] }; }
        public static double Angle(double[] a, double[] b)
        {
            double la = Len(a), lb = Len(b);
            if (la <= 0 || lb <= 0) return 0;
            return Math.Acos(Math.Max(-1, Math.Min(1, Dot(a, b) / (la * lb))));
        }

        /// <summary>
        /// Seconds until the next Hohmann transfer window from the home planet to a target planet, both on near-circular orbits
        /// around the same star. rHome/rTarget: positions relative to the star; normal: the home orbit's normal (direction of motion
        /// is counter-clockwise around it); aHome/aTarget: semi-major axes; mu: the star's gravitational parameter.
        /// </summary>
        public static double SecondsToWindow(double[] rHome, double[] rTarget, double[] normal, double aHome, double aTarget, double mu)
        {
            if (aHome <= 0 || aTarget <= 0 || mu <= 0 || Math.Abs(aHome - aTarget) < 1) return double.NaN;
            double nHome = Math.Sqrt(mu / (aHome * aHome * aHome)), nTarget = Math.Sqrt(mu / (aTarget * aTarget * aTarget));
            double tTransfer = Math.PI * Math.Sqrt(Math.Pow(aHome + aTarget, 3) / (8 * mu));
            double ideal = Math.PI - nTarget * tTransfer;                       // target's lead over home at departure
            double now = Math.Atan2(Dot(Cross(rHome, rTarget), Unit(normal)), Dot(rHome, rTarget));
            double rate = nTarget - nHome;                                      // how fast the lead changes
            double synodic = 2 * Math.PI / Math.Abs(rate);
            double t = Wrap(ideal - now) / rate;
            t %= synodic;
            if (t < 0) t += synodic;
            return t;
        }

        /// <summary>Seconds between two windows for these orbits.</summary>
        public static double Synodic(double aHome, double aTarget, double mu)
        {
            double nHome = Math.Sqrt(mu / (aHome * aHome * aHome)), nTarget = Math.Sqrt(mu / (aTarget * aTarget * aTarget));
            return 2 * Math.PI / Math.Abs(nTarget - nHome);
        }

        static double[] Unit(double[] a) { double l = Len(a); return l <= 0 ? new double[] { 0, 0, 1 } : new[] { a[0] / l, a[1] / l, a[2] / l }; }
        static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

        /// <summary>
        /// Seen from the planet's center: is the moon in front of the star (solar eclipse) or in the planet's shadow (lunar eclipse)?
        /// Returns "solar", "lunar" or "". Positions are relative to the planet.
        /// </summary>
        public static string Eclipse(double[] toStar, double starRadius, double[] toMoon, double moonRadius, double planetRadius)
        {
            double ds = Len(toStar), dm = Len(toMoon);
            if (ds <= 0 || dm <= 0) return "";
            double sep = Angle(toStar, toMoon);
            // Solar: the moon's disk overlaps the star's disk.
            if (sep < Math.Asin(Math.Min(1, moonRadius / dm)) + Math.Asin(Math.Min(1, starRadius / ds))) return "solar";
            // Lunar: the moon sits in the planet's umbra, which narrows with distance from the planet.
            double umbra = planetRadius - dm * (starRadius - planetRadius) / ds;
            if (umbra > 0 && Math.PI - sep < Math.Atan((umbra + moonRadius) / dm)) return "lunar";
            return "";
        }
    }
}
