using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews
{
    /// <summary>What the craft is doing this instant, read by the KSP layer a few times a second for the on-screen stats.</summary>
    public class LiveNumbers
    {
        public double UT, Speed, Altitude, Apoapsis, Periapsis, Throttle, FuelPct = -1, Q, AtmosphereTop = 70000;
        public int Stage;
        public string Situation = "";
    }

    /// <summary>One milestone on the bottom strip.</summary>
    public class OverlayMilestone
    {
        public string Label = "";
        public bool Done;
    }

    /// <summary>Everything the stats overlay draws, as display text.</summary>
    public class OverlayModel
    {
        public string Vessel = "", Camera = "", Clock = "", Speed = "", Altitude = "", Apoapsis = "", Stage = "", Throttle = "";
        public bool Hold, Scrubbed, ShowApoapsis;
        /// <summary>0..1, or -1 when the craft carries no fuel.</summary>
        public double Fuel = -1;
        public List<OverlayMilestone> Milestones = new List<OverlayMilestone>();
    }

    /// <summary>Remembers what the overlay has seen during one stream: stages at liftoff, the peak of dynamic pressure, liftoff time.</summary>
    public class OverlayMemory
    {
        public int StagesAtLiftoff = -1;
        public double MaxQ, LiftoffUT = -1;
        public bool MaxQPassed, CutoffSeen, OrbitSeen;
    }

    public static class StreamOverlay
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static OverlayModel Build(StreamSession st, LiveNumbers n, double countLeft)
        {
            var m = new OverlayModel();
            if (st == null) return m;
            var mem = st.Overlay;
            m.Vessel = (st.VesselName ?? "").ToUpperInvariant();
            m.Camera = CameraLabel(st.Camera);
            bool lifted = st.Moments.Any(x => x.StartsWith("liftoff", StringComparison.Ordinal));
            if (n == null) n = new LiveNumbers();

            if (lifted)
            {
                if (mem.LiftoffUT < 0) mem.LiftoffUT = n.UT;
                if (mem.StagesAtLiftoff < 0) mem.StagesAtLiftoff = n.Stage;
                if (n.Q > mem.MaxQ) mem.MaxQ = n.Q;
                if (!mem.MaxQPassed && mem.MaxQ > 2 && n.Q < mem.MaxQ * 0.85) mem.MaxQPassed = true;
                if (!mem.CutoffSeen && n.Throttle < 0.01 && n.Apoapsis > n.AtmosphereTop) mem.CutoffSeen = true;
                if (!mem.OrbitSeen && (n.Periapsis > n.AtmosphereTop || n.Situation == "ORBITING")) { mem.OrbitSeen = true; mem.CutoffSeen = true; }
            }

            m.Clock = Clock(st, n.UT, countLeft);
            m.Hold = st.Countdown == "hold";
            m.Scrubbed = st.Countdown == "scrubbed";

            m.Speed = n.Speed.ToString("N0", Inv);
            m.Altitude = Math.Abs(n.Altitude) >= 1000 ? (n.Altitude / 1000).ToString("N1", Inv) + " km" : n.Altitude.ToString("N0", Inv) + " m";
            m.ShowApoapsis = lifted && n.Apoapsis > 1000;
            m.Apoapsis = (n.Apoapsis / 1000).ToString("N1", Inv) + " km";

            m.Stage = StageText(st, n.Stage).ToUpperInvariant();
            m.Throttle = "THROTTLE " + Math.Round(Math.Max(0, Math.Min(1, n.Throttle)) * 100).ToString(Inv) + "%";
            m.Fuel = n.FuelPct < 0 ? -1 : Math.Max(0, Math.Min(1, n.FuelPct / 100));

            // Milestones: liftoff, max Q, up to three stagings, engine cutoff, orbit.
            m.Milestones.Add(new OverlayMilestone { Label = "LIFTOFF", Done = lifted });
            m.Milestones.Add(new OverlayMilestone { Label = "MAX Q", Done = mem.MaxQPassed });
            // Before liftoff the first staging is the launch itself, so the same stages are counted either way and the strip never changes.
            int stagings = Math.Max(0, Math.Min(3, lifted ? mem.StagesAtLiftoff : n.Stage - 1));
            for (int i = 1; i <= stagings; i++)
                m.Milestones.Add(new OverlayMilestone { Label = "STAGE " + (i + 1), Done = lifted && n.Stage <= mem.StagesAtLiftoff - i });
            m.Milestones.Add(new OverlayMilestone { Label = "ENGINE CUTOFF", Done = mem.CutoffSeen });
            m.Milestones.Add(new OverlayMilestone { Label = "ORBIT", Done = mem.OrbitSeen });
            return m;
        }

        /// <summary>The broadcast clock, shared by the page and the overlay: the countdown, then time since liftoff.</summary>
        public static string Clock(StreamSession st, double ut, double countLeft)
        {
            bool lifted = st.Lifted;
            if (!double.IsNaN(countLeft) && !lifted) return NewsEngine.CountClock(countLeft);
            if (lifted) return "T+" + Hms(ut - (st.Overlay.LiftoffUT >= 0 ? st.Overlay.LiftoffUT : ut));
            return "ON THE PAD";
        }

        /// <summary>"Stage 2 of 8", counting up from liftoff, the way a broadcast says it (KSP's own stage numbers count down).</summary>
        public static string StageText(StreamSession st, int kspStage)
        {
            if (!st.Lifted || st.Overlay.StagesAtLiftoff < 0) return "On the pad";
            int total = Math.Max(1, st.Overlay.StagesAtLiftoff + 1);
            int now = Math.Max(1, Math.Min(total, st.Overlay.StagesAtLiftoff - kspStage + 1));
            return "Stage " + now + " of " + total;
        }

        /// <summary>"Hull camera: looking aft" becomes "ONBOARD · LOOKING AFT"; ground cameras keep their name.</summary>
        public static string CameraLabel(string cam)
        {
            if (string.IsNullOrEmpty(cam)) return "";
            if (cam.StartsWith("Hull camera", StringComparison.OrdinalIgnoreCase))
            {
                int c = cam.IndexOf(':');
                return "ONBOARD" + (c > 0 ? " · " + cam.Substring(c + 1).Trim().ToUpperInvariant() : "");
            }
            return cam.ToUpperInvariant();
        }

        static string Hms(double s)
        {
            s = Math.Max(0, s);
            var t = TimeSpan.FromSeconds(Math.Floor(s));
            return ((int)t.TotalHours).ToString("00", Inv) + ":" + t.Minutes.ToString("00", Inv) + ":" + t.Seconds.ToString("00", Inv);
        }
    }
}
