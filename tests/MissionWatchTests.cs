using System;
using System.Collections.Generic;
using System.Linq;
using KerbalNews;

public static class MissionWatchTests
{
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }
    const double Day = 21600;

    static HttpResult Reply(string text)
    {
        var resp = new Dictionary<string, object> { { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } } };
        return new HttpResult { Status = 200, Body = Json.Write(resp) };
    }

    static Transit Probe(double arrivalDays, string id = "v1", string name = "Duna Express", string dest = "Duna")
    {
        return new Transit { Id = id, Name = name, Kind = "Probe", From = "Sun", Dest = dest, ArrivalUT = arrivalDays * Day, LaunchUT = 1, DistKm = 4200000, HomeKm = 9100000, PeKm = 80 };
    }

    public static void TestMissionWatchFindsTheArrival()
    {
        // A craft flying straight past a world: it crosses the 10,000 km sphere where the geometry says, however far out it starts.
        Func<double, double[]> body = t => new[] { 0.0, 0, 0 };
        Func<double, double[]> craft = t => new[] { -1e9 + 1000 * t, 5e6, 0 };
        double at = SkyMath.FirstWithin(craft, body, 1e7, 1000, 0, 2e6);
        double want = (1e9 - Math.Sqrt(1e14 - 2.5e13)) / 1000;
        Assert(Math.Abs(at - want) < 2, "crossing time: " + at + " vs " + want);
        Assert(double.IsNaN(SkyMath.FirstWithin(t => new[] { -1e9 + 1000 * t, 2e7, 0 }, body, 1e7, 1000, 0, 2e6)), "a miss is no arrival");
        Assert(double.IsNaN(SkyMath.FirstWithin(craft, body, 1e7, 1000, 0, 5e5)), "not within the time looked at");
        Assert(double.IsNaN(SkyMath.FirstWithin(t => new[] { 1000 * t, 0, 0 }, body, 1e7, 1000, 0, 2e6)), "already inside is not an arrival");
        Assert(double.IsNaN(SkyMath.FirstWithin(craft, body, 1e7, 1000, 0, 2e6, 5e8)), "a craft outside its parent's reach is not followed");
        // Both moving: a world coming the other way halves the time.
        double both = SkyMath.FirstWithin(t => new[] { -1e9 + 1000 * t, 0, 0 }, t => new[] { 1e9 - 1000 * t, 0, 0 }, 1e7, 2000, 0, 2e6);
        Assert(Math.Abs(both - (2e9 - 1e7) / 2000) < 2, "two movers: " + both);

        double mu = 3.0e11, r = 1e7;
        Assert(Math.Abs(SkyMath.PeriapsisRadius(new[] { r, 0, 0 }, new[] { 0, Math.Sqrt(mu / r), 0 }, mu) - r) < 1, "a circular path stays at its height");
        Assert(SkyMath.PeriapsisRadius(new[] { r, 0, 0 }, new[] { -500.0, 0, 0 }, mu) < 1, "falling straight in meets the center");
        // A flyby: energy and angular momentum give the same closest approach as the textbook hyperbola.
        double vinf = 800, b = 3e6, far = 1e9;
        double rp = SkyMath.PeriapsisRadius(new[] { -far, b, 0 }, new[] { Math.Sqrt(vinf * vinf + 2 * mu / Math.Sqrt(far * far + b * b)), 0, 0 }, mu);
        double a = mu / (vinf * vinf), wantRp = Math.Sqrt(a * a + b * b) - a;
        Assert(Math.Abs(rp - wantRp) / wantRp < 0.01, "flyby closest approach: " + rp + " vs " + wantRp);
    }

    public static void TestMissionWatchCountsDownInTheRoundup()
    {
        var s = new NewsState();
        var g = new GlobalSettings { OfflineMode = true, WorldFlavor = false };
        var eng = new NewsEngine(s, g, new CoreTests.FakeTransport(), new SimpleClock(Day));
        var world = new List<WorldFact>();
        var transit = new List<Transit>();
        bool readable = true;
        eng.WorldScan = ut => world.ToList();
        eng.Transits = ut => readable ? transit.ToList() : null;
        eng.Tick(0, 0, true);

        // 140 days out: one piece waits, at the 200-day mark, and it never becomes a stand-alone story (that would cost a call).
        transit.Add(Probe(140.3));
        eng.Tick(0.3 * Day, 1, true);
        var w = s.WorldPending.Single(f => f.Kind == "watch");
        Assert(w.Key == "watch:Duna:v1:200" && w.Sig <= 3 && w.Body == "Duna" && w.Due == 140.3 * Day, "queued at the mark: " + w.Key + " sig " + w.Sig);
        Assert(w.Sig == 3 && w.Text.Contains("Nothing built by") && w.Text.Contains("an uncrewed probe"), "a first visit matters more: " + w.Text);
        eng.Tick(1.0 * Day, 2, true);
        Assert(s.WorldPending.Count(f => f.Kind == "watch") == 1, "the same mark is not queued twice");

        // The piece is written days later with the time left as of that moment.
        eng.Tick(3.5 * Day, 3, true);
        var art = s.Articles.Single(x => x.Tags.Contains("mission watch"));
        Assert(art.Headline == "Duna Express is 137 days from Duna", "time left is as of writing: " + art.Headline);
        Assert(art.Body.Contains("137 days from Duna") && art.Body.Contains("Year 1, Day 141") && art.Body.Contains("4.2 million km") && art.Body.Contains("80 km above"), "the facts are in: " + art.Body);
        Assert(!art.Body.Contains("MISSION WATCH") && !art.Body.Contains("{left}") && !art.Subhead.Contains("MISSION WATCH"), "no note to the writer in print: " + art.Body);
        Assert(art.Tags.Contains("world") && art.Tags.Contains("Duna"), "filed as background news about Duna");
        Assert(!s.Stories.Any(st => st.Kind == "tip"), "never a stand-alone story");
        Assert(!s.WorldPending.Any(f => f.Kind == "watch"), "nothing left waiting");

        // Nothing more until the next mark; then a new piece. A course correction that moves the date updates a waiting piece.
        eng.Tick(30 * Day, 4, true);
        Assert(s.Articles.Count(x => x.Tags.Contains("mission watch")) == 1, "quiet between marks");
        eng.Tick(41 * Day, 5, true);
        Assert(s.Articles.Any(x => x.Tags.Contains("mission watch") && x.Headline == "Duna Express is 99 days from Duna"), "the next mark gets its piece: " + string.Join(" | ", s.Articles.Where(x => x.Tags.Contains("mission watch")).Select(x => x.Headline).ToArray()));
        eng.ScanTransits(95 * Day);
        w = s.WorldPending.Single(f => f.Kind == "watch");
        Assert(w.Key == "watch:Duna:v1:50", "next mark: " + w.Key);
        transit[0] = Probe(138.0);
        eng.ScanTransits(95.5 * Day);
        Assert(s.WorldPending.Single(f => f.Kind == "watch").Due == 138.0 * Day, "a waiting piece follows a course correction");

        // When the game can't be read, a waiting piece stays; when the path no longer leads there, it goes.
        readable = false;
        eng.ScanTransits(95.6 * Day);
        Assert(s.WorldPending.Any(f => f.Kind == "watch"), "not readable: leave it alone");
        readable = true;
        transit.Clear();
        eng.ScanTransits(95.7 * Day);
        Assert(!s.WorldPending.Any(f => f.Kind == "watch"), "no encounter any more: nothing to count down to");

        // A piece that would run after the arrival is dropped instead.
        transit.Add(Probe(100, "v2", "Late One", "Eve"));
        eng.ScanTransits(97.5 * Day);
        Assert(s.WorldPending.Any(f => f.Key == "watch:Eve:v2:5"), "final approach queued");
        transit.Clear(); readable = false;
        Assert(eng.PickWorld(98.5 * Day).All(f => f.Kind != "watch") && !s.WorldPending.Any(f => f.Kind == "watch"), "too close to arrival: the arrival story takes over");
        readable = true;

        // Final approach wording, saved and loaded with its date.
        transit.Add(Probe(109, "v1"));
        eng.ScanTransits(101 * Day);
        w = s.WorldPending.Single(f => f.Kind == "watch");
        Assert(w.Key == "watch:Duna:v1:10" && w.Headline.Contains("final approach") && w.Sig == 3, "final approach: " + w.Headline);
        var n = new DataNode("KERBALNEWS"); s.Save(n);
        var back = NewsState.Load(n);
        Assert(back.WorldPending.Single(f => f.Kind == "watch").Due == 109 * Day && back.WorldSeen.ContainsKey("watch:Duna:v1:200"), "the countdown survives a save");
        var picked = eng.PickWorld(102 * Day).Single(f => f.Kind == "watch");
        Assert(picked.Headline == "Duna Express on final approach: 7 days to Duna", "resolved headline: " + picked.Headline);
    }

    public static void TestMissionWatchFleetsCrewHomecomingAndSilence()
    {
        var s = new NewsState();
        var g = new GlobalSettings { OfflineMode = true, WorldFlavor = false };
        var eng = new NewsEngine(s, g, new CoreTests.FakeTransport(), new SimpleClock(Day));
        var transit = new List<Transit>();
        eng.WorldScan = ut => new List<WorldFact>();
        eng.Transits = ut => transit.ToList();
        eng.Tick(0, 0, true);

        // A fleet to one world is one story, led by the first to arrive. Another destination gets its own.
        var crewed = Probe(90, "c1", "Duna Hab"); crewed.Kind = "Ship"; crewed.Crew = new List<string> { "Valentina Kerman", "Bob Kerman" };
        transit.Add(crewed);
        transit.Add(Probe(80, "p1", "Duna Scout"));
        transit.Add(Probe(84, "p2", "Duna Relay"));
        var home = Probe(40, "h1", "Eve Return", "Kerbin"); home.Homecoming = true; home.From = "Sun";
        transit.Add(home);
        var secret = Probe(30, "x1", "Quiet Bird", "Jool");
        transit.Add(secret);
        s.SetSilent("x1", true);
        eng.ScanTransits(1 * Day);
        var watch = s.WorldPending.Where(f => f.Kind == "watch").ToList();
        Assert(watch.Count == 2, "one piece per destination, none for a silent craft: " + string.Join(", ", watch.Select(f => f.Key).ToArray()));
        var duna = watch.Single(f => f.Body == "Duna");
        Assert(duna.Key == "watch:Duna:p1:100" && duna.Text.Contains("\"Duna Hab\" (crewed), about 10 days behind") && duna.Text.Contains("\"Duna Relay\", about 4 days behind"), "the fleet rides along: " + duna.Text);
        var hc = watch.Single(f => f.Body == "Kerbin");
        Assert(hc.Headline == "Eve Return is {left} from home" && hc.Text.Contains("on its way home") && !hc.Text.Contains("Nothing built by"), "homecoming reads as one: " + hc.Text);

        // Only one countdown per roundup, the most pressing first; the other waits its turn.
        var pick = eng.PickWorld(2 * Day);
        Assert(pick.Count(f => f.Kind == "watch") == 1 && s.WorldPending.Count(f => f.Kind == "watch") == 1, "one countdown per roundup");

        // A crewed lead craft: named crew, and the arrival story says the press had been counting down.
        var s2 = new NewsState();
        var eng2 = new NewsEngine(s2, new GlobalSettings { OfflineMode = true, WorldFlavor = false }, new CoreTests.FakeTransport(), new SimpleClock(Day));
        var t2 = new List<Transit> { crewed };
        eng2.WorldScan = ut => new List<WorldFact>();
        eng2.Transits = ut => t2.ToList();
        eng2.Tick(0, 0, true);
        eng2.Tick(0.3 * Day, 1, true);
        eng2.Tick(3.5 * Day, 2, true);
        var art = s2.Articles.Single(x => x.Tags.Contains("mission watch"));
        Assert(art.Body.Contains("a crewed spacecraft carrying Valentina Kerman, Bob Kerman"), "crew named: " + art.Body);
        var other = new NewsEvent { Type = EventTypes.Soi, UT = 90 * Day, VesselId = "zz", VesselName = "Someone Else", Body = "Duna" };
        eng2.Ingest(other);
        Assert(!other.Detail.Contains("counting down"), "only the craft that was watched");
        var arrive = new NewsEvent { Type = EventTypes.Soi, UT = 90 * Day, VesselId = "c1", VesselName = "Duna Hab", Body = "Duna", Crew = { "Valentina Kerman" } };
        eng2.Ingest(arrive);
        Assert(arrive.Detail.Contains("The press had been counting down to this arrival since Year 1, Day 4"), "the arrival knows it was awaited: " + arrive.Detail);
        var again = new NewsEvent { Type = EventTypes.Soi, UT = 400 * Day, VesselId = "c1", VesselName = "Duna Hab", Body = "Duna" };
        eng2.Ingest(again);
        Assert(!again.Detail.Contains("counting down"), "said once");
    }

    public static void TestMissionWatchCostsNoExtraCall()
    {
        string sent = null; int calls = 0;
        var t = new CoreTests.FakeTransport { Responder = r => { calls++; sent = r.Body; return Reply("{\"stories\":[{\"item\":1,\"headline\":\"Jool Explorer: the long wait\",\"subhead\":\"y\",\"body\":\"Still a long way to go.\"},{\"item\":2,\"headline\":\"Junk falls\",\"subhead\":\"x\",\"body\":\"It burns.\"}]}"); } };
        var s = new NewsState();
        var g = CoreTests.OnlineSettings(); g.WorldFlavor = false;
        var eng = new NewsEngine(s, g, t, new SimpleClock(Day));
        var world = new List<WorldFact>();
        var transit = new List<Transit>();
        eng.WorldScan = ut => world.ToList();
        eng.Transits = ut => transit.ToList();
        eng.Tick(0, 0, true);
        world.Add(new WorldFact { Key = "reentry:debris:1", Kind = "safety", Sig = 1, Headline = "Space junk coming down", Text = "3 pieces of debris will burn up." });
        transit.Add(Probe(1100, "j1", "Jool Explorer", "Jool"));
        eng.Tick(0.3 * Day, 1, true);
        int before = calls;
        eng.Tick(3.5 * Day, 2, true);
        Assert(calls - before == 1, "the countdown rides in the roundup's one call: " + (calls - before));
        Assert(sent.Contains("MISSION WATCH") && sent.Contains("kind: watch") && sent.Contains("about 1,096 days (2.6 years) from Jool") && !sent.Contains("{left}"), "the writer gets the countdown facts: " + sent);
        Assert(sent.Contains("Never say what will happen on arrival"), "with the rule for countdown pieces");
        var art = s.Articles.Single(x => x.Headline == "Jool Explorer: the long wait");
        Assert(art.Tags.Contains("mission watch") && art.Tags.Contains("Jool") && !art.Offline, "the AI story is filed under mission watch");
        // With background news off, nothing is read at all.
        g.WorldNews = 0;
        transit[0] = Probe(900, "j1", "Jool Explorer", "Jool");
        eng.Tick(210 * Day, 3, true);
        Assert(!s.WorldSeen.ContainsKey("watch:Jool:j1:1000"), "off means no countdowns");
    }
}
