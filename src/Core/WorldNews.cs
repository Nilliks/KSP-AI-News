using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>
    /// Something happening in the world that is not one of the agency's own missions: an asteroid found near Kerbin, a transfer
    /// window opening, debris coming down, a kerbonaut's hundredth day in space, a part maker bragging. The KSP layer reads them
    /// from the game; the engine adds a little invented flavor (weather, politics, culture, rival programs).
    /// </summary>
    public class WorldFact
    {
        /// <summary>Unique per happening, so it is reported once (e.g. "window:Duna:412").</summary>
        public string Key = "";
        /// <summary>sky, safety, crew, company, record, tech, base, probe, mod, or flavor kinds: weather, politics, culture, rival.</summary>
        public string Kind = "";
        public string Headline = "";
        /// <summary>The facts, in plain sentences. The writer may use nothing else.</summary>
        public string Text = "";
        public string Body = "";
        /// <summary>1 small, 3 notable, 5 huge.</summary>
        public int Sig = 1;
        public double UT;

        public void Save(DataNode n)
        {
            n.AddText("key", Key); n.Add("kind", Kind); n.AddText("headline", Headline); n.AddText("text", Text); n.Add("body", Body); n.Add("sig", Sig); n.Add("ut", UT);
        }

        public static WorldFact Load(DataNode n)
        {
            return new WorldFact { Key = n.GetText("key"), Kind = n.Get("kind", ""), Headline = n.GetText("headline"), Text = n.GetText("text"), Body = n.Get("body", ""), Sig = n.GetInt("sig", 1), UT = n.GetDouble("ut") };
        }
    }

    public partial class NewsEngine
    {
        /// <summary>Set by the KSP layer: reads the world (asteroids, orbits, crew, tech...). It may keep its own notes in State.WorldMemo.</summary>
        public Func<double, List<WorldFact>> WorldScan;   // null: no game attached (tests), so no background news
        public bool WorldBusy { get; private set; }
        double nextWorldScanUT = -1;
        public static readonly string[] Rivals = { "Kerbin Orbital Industries", "the Northern Rocketry Collective" };

        /// <summary>Days between background roundups, and stories per roundup, for the "how much" setting (1 a little, 2 normal, 3 lots).</summary>
        public double WorldIntervalDays { get { return Settings.WorldNews >= 3 ? 1.5 : Settings.WorldNews == 2 ? 3 : 6; } }
        public int WorldBatch { get { return Settings.WorldNews >= 3 ? 5 : Settings.WorldNews == 2 ? 3 : 2; } }

        /// <summary>Where each kind of story runs, best first. The first one turned on gets it.</summary>
        static readonly Dictionary<string, string[]> WorldDesks = new Dictionary<string, string[]>
        {
            { "sky", new[] { "observer", "science", "herald" } },
            { "safety", new[] { "observer", "kscwatch", "herald" } },
            { "crew", new[] { "munmonthly", "herald" } },
            { "company", new[] { "herald", "observer" } },
            { "record", new[] { "herald", "munmonthly" } },
            { "tech", new[] { "observer", "herald" } },
            { "base", new[] { "munmonthly", "observer", "herald" } },
            { "probe", new[] { "science", "observer", "herald" } },
            { "mod", new[] { "observer", "science", "herald" } },
            { "weather", new[] { "herald" } },
            { "politics", new[] { "herald", "council", "kscwatch" } },
            { "culture", new[] { "munmonthly", "inquirer", "herald" } },
            { "rival", new[] { "observer", "kraken", "herald" } },
            { "dream", new[] { "munmonthly", "science", "observer", "herald" } },
        };

        public string WorldOutlet(WorldFact f)
        {
            string[] desks;
            if (!WorldDesks.TryGetValue(f.Kind, out desks)) desks = new[] { "herald" };
            // A little variety: a rumor about a rival sometimes lands at the Kraken, a big sky event at the Herald.
            int h = Math.Abs(OfflineWriter.StableHash(f.Key));
            if (f.Kind == "rival" && h % 3 == 0 && Settings.SiteOn("kraken")) return "kraken";
            if (f.Sig >= 4 && Settings.SiteOn("herald") && h % 2 == 0) return "herald";
            foreach (var d in desks) if (Settings.SiteOn(d)) return d;
            return null;
        }

        /// <summary>True once the agency has put something in orbit: rivals never get there first.</summary>
        public bool AgencyReachedOrbit()
        {
            if (State.Records.Any(r => r.IndexOf("orbit", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            var d = Digest();
            return d != null && d.Satellites + d.Stations + d.CrewedVessels > 0;
        }

        void TickWorld(double ut, bool allowCalls)
        {
            if (WorldScan == null || Settings.WorldNews <= 0 || InSide()) return;
            if (ut >= nextWorldScanUT)
            {
                nextWorldScanUT = ut + 0.25 * Day;
                ScanWorld(ut);
            }
            if (WorldBusy) return;
            // Something big goes out on its own, with full coverage (one story, one call).
            var big = State.WorldPending.Where(f => f.Sig >= 4).OrderByDescending(f => f.Sig).FirstOrDefault();
            if (big != null && (State.LastWorldTipUT < 0 || ut - State.LastWorldTipUT >= Day))
            {
                State.WorldPending.Remove(big);
                string outlets = BigOutlets(big);
                if (outlets != "")
                {
                    Tip(outlets, big.Text, big.Headline, big.Text, 20 + big.Sig * 8, ut + 0.2 * Day, "", "");
                    State.LastWorldTipUT = ut;
                }
            }
            if (State.LastWorldUT < 0) { State.LastWorldUT = ut; return; }
            if (ut - State.LastWorldUT < WorldIntervalDays * Day) return;
            if (Settings.CanCallAi && !allowCalls) return;   // wait for the warp to end; offline costs nothing
            var batch = PickWorld(ut);
            State.LastWorldUT = ut;
            if (batch.Count == 0) return;
            WriteWorld(batch, ut);
        }

        /// <summary>Writes a roundup now from whatever is waiting (self-test and debugging).</summary>
        public bool WorldRoundupNow(double ut)
        {
            if (WorldBusy) return false;
            var b = PickWorld(ut);
            State.LastWorldUT = ut;
            if (b.Count == 0) return false;
            WriteWorld(b, ut);
            return true;
        }

        string BigOutlets(WorldFact f)
        {
            var list = new List<string>();
            string first = WorldOutlet(f);
            if (first != null) list.Add(first);
            foreach (var o in new[] { "herald", "observer", "inquirer", "kraken" })
                if (!list.Contains(o) && Settings.SiteOn(o) && (o != "kraken" || f.Sig >= 5)) list.Add(o);
            return string.Join(",", list.Take(f.Sig >= 5 ? 4 : 3).ToArray());
        }

        /// <summary>Reads the world and keeps what is new. The first scan of a save only learns what is already there.</summary>
        public void ScanWorld(double ut)
        {
            List<WorldFact> facts;
            try { facts = (WorldScan != null ? WorldScan(ut) : null) ?? new List<WorldFact>(); }
            catch (Exception e) { LastError = "World scan: " + e.Message; return; }
            bool baseline = !State.WorldBaselined;
            foreach (var f in facts)
            {
                if (f == null || f.Key == "" || State.WorldSeen.ContainsKey(f.Key)) continue;
                State.WorldSeen[f.Key] = ut;
                if (baseline) continue;
                if (f.UT <= 0) f.UT = ut;
                State.WorldPending.Add(f);
            }
            State.WorldBaselined = true;
            // Old news is no news.
            State.WorldPending.RemoveAll(f => ut - f.UT > 20 * Day);
            if (State.WorldPending.Count > 40) State.WorldPending = State.WorldPending.OrderByDescending(f => f.Sig).ThenByDescending(f => f.UT).Take(40).ToList();
            if (State.WorldSeen.Count > 3000)
                foreach (var k in State.WorldSeen.OrderBy(kv => kv.Value).Take(State.WorldSeen.Count - 2500).Select(kv => kv.Key).ToList()) State.WorldSeen.Remove(k);
        }

        /// <summary>The biggest and freshest game facts, one per kind where possible, plus a little flavor.</summary>
        public List<WorldFact> PickWorld(double ut)
        {
            int n = WorldBatch;
            var pool = State.WorldPending.Where(f => WorldOutlet(f) != null).OrderByDescending(f => f.Sig).ThenByDescending(f => f.UT).ToList();
            var pick = new List<WorldFact>();
            foreach (var f in pool) if (pick.Count < n && !pick.Any(p => p.Kind == f.Kind)) pick.Add(f);
            foreach (var f in pool) if (pick.Count < n && !pick.Contains(f)) pick.Add(f);
            foreach (var f in pick) State.WorldPending.Remove(f);
            // Now and then a speculative piece about somewhere the agency hasn't been yet. It rides along in the same call.
            var dream = Dream(ut, new Random(OfflineWriter.StableHash("dream" + Math.Floor(ut / Day))));
            if (dream != null && WorldOutlet(dream) != null) pick.Add(dream);
            // Flavor fills the gaps: always one when there is no game news, otherwise now and then.
            var rng = new Random(OfflineWriter.StableHash("world" + Math.Floor(ut / Day)));
            if (Settings.WorldFlavor && (pick.Count == 0 || (pick.Count < n && rng.NextDouble() < 0.6)))
            {
                var fl = Flavor(ut, rng);
                if (fl != null && WorldOutlet(fl) != null) pick.Add(fl);
            }
            return pick;
        }

        /// <summary>Invented, harmless world color. Rival programs only ever work in Kerbin orbit, and never ahead of the agency.</summary>
        public WorldFact Flavor(double ut, Random rng)
        {
            var kinds = new List<string> { "weather", "politics", "culture" };
            if (Settings.WorldRivals) { kinds.Add("rival"); kinds.Add("rival"); }
            string kind = kinds[rng.Next(kinds.Count)];
            int day = (int)Math.Floor(ut / Day);
            string key = "flavor:" + kind + ":" + day;
            if (State.WorldSeen.ContainsKey(key)) return null;
            State.WorldSeen[key] = ut;
            var d = Digest();
            var f = new WorldFact { Key = key, Kind = kind, UT = ut, Sig = 1, Body = "Kerbin" };
            switch (kind)
            {
                case "weather":
                {
                    string[] w =
                    {
                        "A storm front is moving up the coast toward the Kerbal Space Center, with high winds forecast for the next two days. Forecasters say any launch would likely be scrubbed.",
                        "Clear, calm skies over the Kerbal Space Center all week. Forecasters call it perfect launch weather.",
                        "Thick morning fog has rolled in over the Kerbal Space Center and the nearby coast. Ground crews say it usually burns off by noon.",
                        "A heat wave on the KSC coast. Pad crews are working early shifts to avoid the midday sun.",
                        "Upper-level winds over the Kerbal Space Center are unusually strong this week, the kind that put stress on a rocket as it climbs.",
                    };
                    f.Text = w[rng.Next(w.Length)];
                    f.Headline = "Weather watch at the Space Center";
                    break;
                }
                case "politics":
                {
                    string[] p =
                    {
                        "The Planetary Council is debating next year's budget, and some members want to cut space spending in favor of roads and schools.",
                        "A Council committee held a hearing on whether the space program delivers enough value for its cost. Members were split.",
                        "Several Council members called for new rules on orbital debris and on who pays to clean it up.",
                        "A Council member proposed a public prize fund for private companies that put satellites in orbit around Kerbin.",
                    };
                    f.Text = p[rng.Next(p.Length)] + (d != null && d.Career != "" ? " For context: " + d.Career : "");
                    f.Headline = "Space spending debated in the Council";
                    break;
                }
                case "culture":
                {
                    var last = State.Articles.Where(a => !a.Removed && a.Kind == "news" && a.Significance >= 40).OrderByDescending(a => a.PublishedUT).FirstOrDefault();
                    string[] c =
                    {
                        "Space-themed toys are selling out in shops across Kerbin this season, with model rockets topping the lists.",
                        "Schools report a jump in kids signing up for rocketry clubs.",
                        "A new film about a fictional mission to the Mun opens this week. Critics call it exciting but say the science is shaky.",
                        "A popular snack brand has launched a 'rocket fuel' flavor. Reviews are mixed.",
                        "An art show in the capital features paintings of Kerbin seen from orbit.",
                    };
                    f.Text = c[rng.Next(c.Length)] + (last != null ? " Many point to the agency's recent news (\"" + last.Headline + "\") as the spark." : "");
                    f.Headline = "Space fever on Kerbin";
                    break;
                }
                default:
                {
                    string rival = Rivals[rng.Next(Rivals.Length)];
                    bool orbit = AgencyReachedOrbit();
                    string[] r = orbit
                        ? new[]
                        {
                            rival + " says it put a small weather satellite into low Kerbin orbit. Independent trackers have not confirmed it yet.",
                            rival + " is rumored to be building a bigger rocket to carry communication satellites to Kerbin orbit. Nothing has flown.",
                            rival + " delayed its next satellite launch to Kerbin orbit after a problem in testing. No details were given.",
                            rival + " signed a deal to launch commercial satellites into Kerbin orbit next year. Analysts say it is years behind the agency.",
                        }
                        : new[]
                        {
                            rival + " tested a small sounding rocket that climbed briefly before falling back. It did not reach space.",
                            rival + " says it hopes to reach Kerbin orbit one day. Critics say it has not flown anything close.",
                            rival + " lost a test rocket a few seconds after launch. Nobody was hurt.",
                        };
                    f.Text = r[rng.Next(r.Length)];
                    f.Headline = "Rival rocket news";
                    break;
                }
            }
            return f;
        }

        void WriteWorld(List<WorldFact> batch, double ut)
        {
            if (!Settings.CanCallAi)
            {
                foreach (var f in batch) AddWorldArticle(f, OfflineWorldHeadline(f), OfflineWorldSub(f), OfflineWorldBody(f), ut, true);
                return;
            }
            WorldBusy = true;
            var outlets = batch.Select(f => WorldOutlet(f)).Distinct().Select(id => Outlets.Get(id)).Where(o => o != null).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("You write short background news for outlets in the world of Kerbal Space Program (KSP1). These stories are not about " + AgencyName + "'s own missions; they are about the world around it.");
            sb.AppendLine(PromptBuilder.SillinessText(State.Silliness));
            sb.AppendLine("RULES:");
            sb.AppendLine("- Use ONLY the facts given for each item. Do not invent missions, discoveries, numbers, names or dates. You may explain what the facts mean and add general background.");
            sb.AppendLine("- Never say " + AgencyName + " did something that is not in the facts.");
            sb.AppendLine("- Items marked SPECULATIVE are dreaming pieces: imagine and reason about what such a trip would take (rockets, windows, years, risks, cost) using real spaceflight thinking and the facts given, but never claim the agency plans it.");
            sb.AppendLine("- Rival space programs (" + string.Join(", ", Rivals) + ") only ever work in orbit around Kerbin. They never fly beyond Kerbin orbit, never beat the agency to anything, and stay small next to it. Keep them vague: no specs, no dates.");
            sb.AppendLine("- Write in each outlet's own voice. 80-220 words per story. Short paragraphs separated by blank lines. Kerbal names end in Kerman. Everyone in this world is a kerbal: say kerbal spaceflight, never human.");
            sb.AppendLine("OUTLETS:");
            foreach (var o in outlets) sb.AppendLine("- " + o.Id + " (" + o.Name + "): " + o.Style + " " + o.Bias);
            string canon = PromptBuilder.ProfileText(State);
            if (canon != "") sb.AppendLine(canon);
            sb.AppendLine("Reply with JSON only: {\"stories\":[{\"item\":1,\"headline\":\"...\",\"subhead\":\"one sentence\",\"body\":\"...\"}]} with one entry per item, in order.");
            var u = new StringBuilder();
            u.AppendLine("Date: " + Clock.FormatDate(ut));
            for (int i = 0; i < batch.Count; i++)
            {
                var f = batch[i];
                u.AppendLine((i + 1) + ". For " + WorldOutlet(f) + " | kind: " + f.Kind + " | importance " + f.Sig + " of 5 | " + (f.Headline != "" ? "working title: " + f.Headline + " | " : "") + "facts: " + f.Text);
            }
            AskJson(Settings.WriterModel, sb.ToString(), u.ToString(), Math.Min(4000, 300 + batch.Count * 450), 0, (root, err, text, res) =>
            {
                WorldBusy = false;
                var arr = root != null ? Json.Arr(Json.Get(root, "stories")) : null;
                for (int i = 0; i < batch.Count; i++)
                {
                    var f = batch[i];
                    var o = arr == null ? null : arr.FirstOrDefault(x => (int)Json.Num(x, "item", -1) == i + 1) ?? (i < arr.Count ? arr[i] : null);
                    string h = o != null ? Json.Str(o, "headline", "").Trim() : "", b = o != null ? Json.Str(o, "body", "").Trim() : "";
                    if (h == "" || b == "") AddWorldArticle(f, OfflineWorldHeadline(f), OfflineWorldSub(f), OfflineWorldBody(f), ut, true);
                    else AddWorldArticle(f, ResponseParser.Clip(h, 200), ResponseParser.Clip(Json.Str(o, "subhead", "").Trim(), 300), ResponseParser.Clip(b, 3000), ut, false);
                }
                if (arr == null && err != null) LastError = err;
            });
        }

        Article AddWorldArticle(WorldFact f, string headline, string sub, string body, double ut, bool offline)
        {
            string outlet = WorldOutlet(f) ?? "herald";
            var def = Outlets.Get(outlet);
            var rng = new Random(OfflineWriter.StableHash(f.Key));
            var a = new Article
            {
                Id = State.NewId("a"), Outlet = outlet, Kind = def != null ? (def.DefaultKind == "rumor" && f.Kind != "rival" ? "news" : def.DefaultKind) : "news",
                Headline = headline, Subhead = sub, Body = body, PublishedUT = ut + (0.05 + rng.NextDouble() * 0.5) * Day,
                Significance = 10 + f.Sig * 8, Offline = offline, Model = offline ? "offline templates" : Settings.Preset.Id + " / " + Settings.WriterModel,
            };
            if (outlet == "council") a.Kind = "statement";
            if (f.Kind == "dream") a.Kind = "feature";
            a.Tags.Add("world");
            a.Tags.Add(WorldTag(f.Kind));
            if (f.Body != "" && f.Body != "Kerbin") a.Tags.Add(f.Body);
            State.Articles.Add(a);
            if (a.PublishedUT <= lastVisibleUT) late.Add(a);
            return a;
        }

        static string WorldTag(string kind)
        {
            switch (kind)
            {
                case "sky": return "sky watch";
                case "safety": return "orbital safety";
                case "crew": return "kerbonauts";
                case "company": return "industry";
                case "record": return "records";
                case "tech": return "technology";
                case "base": return "stations and bases";
                case "probe": return "deep space";
                case "mod": return "space weather";
                case "dream": return "what if";
                case "rival": return "rival programs";
                default: return kind;
            }
        }

        public static string OfflineWorldHeadline(WorldFact f) { return f.Headline != "" ? f.Headline : "In the news: " + WorldTag(f.Kind); }

        static string OfflineWorldSub(WorldFact f)
        {
            if (f.Kind == "dream") return "No one has announced a trip. But it is fun to work out what one would take.";
            int i = f.Text.IndexOf(". ", StringComparison.Ordinal);
            return i > 0 ? f.Text.Substring(0, i + 1) : f.Text;
        }

        static string OfflineWorldBody(WorldFact f)
        {
            string tail;
            switch (f.Kind)
            {
                case "sky": tail = "Amateur astronomers are being urged to look up. Observatories will keep watching and share what they find."; break;
                case "safety": tail = "Tracking stations are watching closely. Experts say most small objects burn up harmlessly, but the growing crowd in orbit worries some."; break;
                case "crew": tail = "Colleagues say the milestone is a credit to years of training, and fans have flooded the agency with messages."; break;
                case "company": tail = "Industry watchers say space work is becoming a real business on Kerbin, and the suppliers want everyone to know it."; break;
                case "record": tail = "Statisticians are already updating the record books."; break;
                case "tech": tail = "Experts are guessing what the agency will build next with it. The agency has not said."; break;
                case "base": tail = "It is a quiet kind of success: no launch, no fireworks, just the work going on day after day."; break;
                case "probe": tail = "Far from home, it is a reminder of how big the system is, and how long the agency's machines keep working."; break;
                case "dream": return "Kerbals have always looked up and wondered. " + DreamOffline(f.Text) + "\n\nNobody at the agency has announced any such plan. But a few years ago, the trips it makes today sounded like dreams too.";
                case "mod": tail = "Mission planners say they take space weather seriously, and crews have shelter plans."; break;
                default: tail = ""; break;
            }
            return f.Text + (tail != "" ? "\n\n" + tail : "");
        }

        /// <summary>The facts of a speculative item without the note to the writer.</summary>
        static string DreamOffline(string text)
        {
            const string end = "Do not claim anything has been decided, built or scheduled.";
            int i = text.IndexOf(end, StringComparison.Ordinal);
            return i >= 0 ? text.Substring(i + end.Length).Trim() : text;
        }

        public static string FmtDays(double days) { return days.ToString(days < 10 ? "0.#" : "0", CultureInfo.InvariantCulture); }
    }
}
