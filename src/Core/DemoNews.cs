using System.Linq;

namespace KerbalNews
{
    /// <summary>
    /// A small, believable news history for the self-test and screenshots: a launch, a Mun landing,
    /// a failure, a contract, comments from the player and a Snackboard thread. Written offline, no AI calls.
    /// </summary>
    public static class DemoNews
    {
        public static NewsState Build(IGameClock clock, out double now)
        {
            var s = new NewsState();
            s.Profile.Name = "Kerbin Space Agency";
            s.Profile.ShortName = "KSA";
            s.Profile.Mission = "Put kerbals on every world we can reach, and bring them home.";
            s.Lore.Add(new LoreEntry { UT = 5, Text = "The agency was founded after the Great Snack Shortage." });
            var eng = new NewsEngine(s, new GlobalSettings { OfflineMode = true }, null, clock);
            double day = clock.DaySeconds;
            eng.Tick(0, 0, true);

            eng.TrackLaunch("demo1", new CraftSnapshot { VesselName = "Mun Arrow", VesselType = "Ship", Parts = 46, MassTons = 58, HeightM = 24, Stages = 3, DeltaV = 5200, Engines = "LV-T45 x3, LV-909 x1", CrewCapacity = 1, LaunchSite = "LaunchPad", Crew = { "Jebediah Kerman" } }, 60);
            Ev(eng, EventTypes.Launch, 60, "demo1", "Mun Arrow", "Kerbin", "LaunchPad", "Jebediah Kerman");
            Ev(eng, EventTypes.Orbit, 400, "demo1", "Mun Arrow", "Kerbin", "", "Jebediah Kerman");
            Ev(eng, EventTypes.Landing, 30000, "demo1", "Mun Arrow", "Mun", "", "Jebediah Kerman");
            Ev(eng, EventTypes.Milestone, 30001, "demo1", "Mun Arrow", "Mun", "First crewed landing on the Mun");
            eng.Tick(day * 3, 1, true);

            eng.TrackLaunch("demo2", new CraftSnapshot { VesselName = "Big Bertha", Parts = 120, MassTons = 210, HeightM = 48, Stages = 4, LaunchSite = "LaunchPad" }, day * 4);
            Ev(eng, EventTypes.Launch, day * 4, "demo2", "Big Bertha", "Kerbin", "LaunchPad");
            Ev(eng, EventTypes.Failure, day * 4 + 50, "demo2", "Big Bertha", "Kerbin", "exploded 50 seconds after liftoff");
            eng.Tick(day * 7, 2, true);

            eng.Ingest(new NewsEvent { Type = EventTypes.ContractDone, UT = day * 8, Detail = "Test the LV-T30 engine in flight" });
            eng.Tick(day * 10, 3, true);

            var shot = eng.AddShot("demo-mun-arrow.jpg", 30500, "demo1", "Mun Arrow", "Mun", "LANDED");
            eng.ReleaseShot(shot.Id, "Jeb's view from the Mun", day * 8);
            eng.AddShot("demo-pad.jpg", day * 3.9, "demo2", "Big Bertha", "Kerbin", "PRELAUNCH");
            eng.PublishRelease("incident", "Statement on the loss of Big Bertha", "Big Bertha was lost 50 seconds into its first test flight. No crew were aboard. The investigation has begun and we will share what we find.", "demo2", null, day * 8);
            eng.Tick(day * 10, 3.5, true);

            now = day * 10;
            var art = s.Articles.OrderByDescending(a => a.Significance).First();
            eng.PlayerComment("a:" + art.Id, "Thank you all for following along. Jeb says the view is worth every snack.", true, "", "", now);
            eng.Tick(now + 1, 4, true);
            eng.PlayerThread("ama", "We just landed on the Mun. Ask us anything!", "The Mun Arrow team is here for the next hour.", true, "", true, 4, now);
            eng.Tick(now + 2, 5, true);
            eng.MarkAllRead(day * 4);
            return s;
        }

        static void Ev(NewsEngine eng, string type, double ut, string id, string name, string body, string detail, params string[] crew)
        {
            var e = new NewsEvent { Type = type, UT = ut, VesselId = id, VesselName = name, Body = body, Detail = detail };
            e.Crew.AddRange(crew);
            eng.Ingest(e);
        }
    }
}
