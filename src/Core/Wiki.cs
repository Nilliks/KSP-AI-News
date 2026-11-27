using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>One flight of a craft on its Kerbopedia page.</summary>
    public class WikiFlight
    {
        public string VesselId = "", VesselName = "";
        public double LaunchUT = -1;
        public List<string> Crew = new List<string>();
        /// <summary>The worst or latest thing that happened: "In flight", "Reached orbit", "Landed on the Mun", "Recovered", "Failure", "Lost with crew".</summary>
        public string Outcome = "In flight";
        public int Rank;

        public void Save(DataNode n)
        {
            n.Add("id", VesselId); n.AddText("name", VesselName); n.Add("launch", LaunchUT); n.AddText("outcome", Outcome); n.Add("rank", Rank);
            foreach (var c in Crew) n.AddText("crew", c);
        }

        public static WikiFlight Load(DataNode n)
        {
            var f = new WikiFlight { VesselId = n.Get("id", ""), VesselName = n.GetText("name"), LaunchUT = n.GetDouble("launch", -1), Outcome = n.GetText("outcome", "In flight"), Rank = n.GetInt("rank") };
            foreach (var c in n.GetAll("crew")) f.Crew.Add(TextCodec.Decode(c));
            return f;
        }
    }

    public class WikiSection
    {
        public string Heading = "", Text = "";
    }

    /// <summary>A line in a page's "View history".</summary>
    public class WikiRevision
    {
        public double UT;
        public string By = "", Note = "";
    }

    /// <summary>
    /// A Kerbopedia article: a craft, a kerbal, a place the agency reached, the agency itself, or a topic the director asked for.
    /// The facts are kept here for good (stories and articles get pruned); the prose is written by the AI when something big happens,
    /// and drawn from the facts with templates when there is no AI text yet.
    /// </summary>
    public class WikiPage
    {
        public string Slug = "", Kind = "craft", Title = "";
        public string Lead = "";
        public List<WikiSection> Sections = new List<WikiSection>();
        /// <summary>When the AI last wrote the prose, or -1 (the page shows template text).</summary>
        public double WrittenUT = -1;
        public double CreatedUT, UpdatedUT;
        /// <summary>Not saved: a page the AI skipped waits until then before it is asked for again.</summary>
        public double RetryUT;
        /// <summary>How much has happened since the prose was written (story significance added up).</summary>
        public double Pending;
        /// <summary>Dated lines of what happened, oldest first.</summary>
        public List<string> Facts = new List<string>();
        public List<WikiFlight> Flights = new List<WikiFlight>();
        public CraftSnapshot Craft;
        /// <summary>Article ids cited at the bottom, oldest first.</summary>
        public List<string> Refs = new List<string>();
        /// <summary>The director's standing wishes for this page; every later rewrite keeps to them.</summary>
        public List<string> Notes = new List<string>();
        public List<WikiRevision> History = new List<WikiRevision>();
        public bool Requested;

        public void Save(DataNode n)
        {
            n.Add("slug", Slug); n.Add("kind", Kind); n.AddText("title", Title); n.AddText("lead", Lead);
            n.Add("written", WrittenUT); n.Add("created", CreatedUT); n.Add("updated", UpdatedUT); n.Add("pending", Pending); n.Add("requested", Requested);
            foreach (var s in Sections) { var sn = n.AddNode("SECTION"); sn.AddText("h", s.Heading); sn.AddText("t", s.Text); }
            foreach (var f in Facts) n.AddText("fact", f);
            foreach (var f in Flights) f.Save(n.AddNode("FLIGHT"));
            if (Craft != null) Craft.Save(n.AddNode("CRAFT"));
            foreach (var r in Refs) n.Add("ref", r);
            foreach (var x in Notes) n.AddText("note", x);
            foreach (var h in History) { var hn = n.AddNode("REV"); hn.Add("ut", h.UT); hn.AddText("by", h.By); hn.AddText("note", h.Note); }
        }

        public static WikiPage Load(DataNode n)
        {
            var p = new WikiPage
            {
                Slug = n.Get("slug", ""), Kind = n.Get("kind", "craft"), Title = n.GetText("title"), Lead = n.GetText("lead"),
                WrittenUT = n.GetDouble("written", -1), CreatedUT = n.GetDouble("created"), UpdatedUT = n.GetDouble("updated"), Pending = n.GetDouble("pending"), Requested = n.GetBool("requested"),
                Craft = CraftSnapshot.Load(n.GetNode("CRAFT")),
            };
            foreach (var sn in n.GetNodes("SECTION")) p.Sections.Add(new WikiSection { Heading = sn.GetText("h"), Text = sn.GetText("t") });
            foreach (var f in n.GetAll("fact")) p.Facts.Add(TextCodec.Decode(f));
            foreach (var fn in n.GetNodes("FLIGHT")) p.Flights.Add(WikiFlight.Load(fn));
            p.Refs.AddRange(n.GetAll("ref"));
            foreach (var x in n.GetAll("note")) p.Notes.Add(TextCodec.Decode(x));
            foreach (var hn in n.GetNodes("REV")) p.History.Add(new WikiRevision { UT = hn.GetDouble("ut"), By = hn.GetText("by"), Note = hn.GetText("note") });
            return p;
        }

        public void Revise(double ut, string by, string note)
        {
            History.Add(new WikiRevision { UT = ut, By = by, Note = note });
            if (History.Count > 40) History.RemoveRange(0, History.Count - 40);
            UpdatedUT = Math.Max(UpdatedUT, ut);
        }
    }

    public partial class NewsEngine
    {
        public const int MaxWikiFacts = 60, MaxWikiRefs = 12, MaxWikiFlights = 40;
        /// <summary>Story significance a page must gather before the AI rewrites it (a first page is written straight away).</summary>
        public const double WikiRewriteAt = 60;
        public bool WikiBusy;
        double nextWikiUT = -1;
        int wikiFailures;

        public static string WikiSlug(string title)
        {
            var sb = new StringBuilder();
            foreach (char ch in (title ?? "").Trim())
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            return sb.ToString().Trim('_');
        }

        public WikiPage WikiGet(string slugOrTitle)
        {
            if (string.IsNullOrEmpty(slugOrTitle)) return null;
            WikiPage p;
            if (State.Wiki.TryGetValue(slugOrTitle, out p)) return p;
            if (State.Wiki.TryGetValue(WikiSlug(slugOrTitle), out p)) return p;
            string q = slugOrTitle.Trim();
            return State.Wiki.Values.FirstOrDefault(x => x.Title.Equals(q, StringComparison.OrdinalIgnoreCase))
                ?? (q.Length >= 3 ? State.Wiki.Values.Where(x => x.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(x => x.Title.Length).FirstOrDefault() : null);
        }

        WikiPage WikiEnsure(string kind, string title, double ut)
        {
            string slug = WikiSlug(title);
            if (slug == "") return null;
            WikiPage p;
            if (!State.Wiki.TryGetValue(slug, out p))
            {
                p = new WikiPage { Slug = slug, Kind = kind, Title = title.Trim(), CreatedUT = ut, UpdatedUT = ut };
                p.Revise(ut, "Kerbopedia", "Page created");
                State.Wiki[slug] = p;
            }
            return p;
        }

        static void AddFact(WikiPage p, string line)
        {
            if (p.Facts.Contains(line)) return;
            p.Facts.Add(line);
            if (p.Facts.Count > MaxWikiFacts) p.Facts.RemoveRange(0, p.Facts.Count - MaxWikiFacts);
        }

        static void AddRefs(WikiPage p, IEnumerable<Article> arts)
        {
            foreach (var a in arts.OrderByDescending(x => x.Significance).Take(3))
                if (!p.Refs.Contains(a.Id)) p.Refs.Add(a.Id);
            if (p.Refs.Count > MaxWikiRefs) p.Refs.RemoveRange(0, p.Refs.Count - MaxWikiRefs);
        }

        /// <summary>A plain sentence for one event, for the facts list.</summary>
        public string WikiEventLine(NewsEvent e)
        {
            string when = Clock.FormatDate(e.UT) + ": ";
            string body = e.Body != "" ? e.Body : "Kerbin";
            string d = (e.Detail ?? "").Trim();
            string what;
            switch (e.Type)
            {
                case EventTypes.Launch: what = e.VesselName + " launched" + (d != "" ? " (" + d + ")" : ""); break;
                case EventTypes.Orbit: what = e.VesselName + " reached orbit around " + body; break;
                case EventTypes.Soi: what = e.VesselName + " arrived at " + body; break;
                case EventTypes.Landing: what = e.VesselName + " landed on " + body + (d != "" ? " (" + d + ")" : ""); break;
                case EventTypes.Splashdown: what = e.VesselName + " splashed down on " + body; break;
                case EventTypes.Docking: what = e.VesselName + " docked" + (d != "" ? " with " + d : ""); break;
                case EventTypes.Failure: what = e.VesselName + " failed" + (d != "" ? ": " + d : ""); break;
                case EventTypes.CrewKilled: what = (e.Crew.Count > 0 ? string.Join(", ", e.Crew.ToArray()) : "Crew") + " lost aboard " + e.VesselName; break;
                case EventTypes.Recovery: what = e.VesselName + " was recovered" + (d != "" ? " (" + d + ")" : ""); break;
                case EventTypes.Milestone: what = d != "" ? d : e.VesselName + " reached a milestone"; break;
                default: return null;
            }
            if (e.Crew.Count > 0 && e.Type != EventTypes.CrewKilled) what += ", crew " + string.Join(", ", e.Crew.ToArray());
            return when + what;
        }

        static int OutcomeRank(NewsEvent e, out string outcome)
        {
            string body = e.Body != "" ? e.Body : "Kerbin";
            switch (e.Type)
            {
                case EventTypes.CrewKilled: outcome = "Lost with crew"; return 6;
                case EventTypes.Failure: outcome = "Failure"; return 5;
                case EventTypes.Recovery: outcome = "Recovered"; return 4;
                case EventTypes.Splashdown: outcome = "Splashed down"; return 3;
                case EventTypes.Landing: outcome = body == "Kerbin" ? "Landed" : "Landed on " + body; return body == "Kerbin" ? 3 : 4;
                case EventTypes.Docking: outcome = "Docked"; return 2;
                case EventTypes.Soi: outcome = "Reached " + body; return body == "Kerbin" ? 1 : 3;
                case EventTypes.Orbit: outcome = body == "Kerbin" ? "Reached orbit" : "Orbited " + body; return body == "Kerbin" ? 2 : 3;
                default: outcome = null; return 0;
            }
        }

        /// <summary>Called once for each published mission story: keeps the craft, crew, place and agency pages up to date.</summary>
        void WikiOnStory(Story story, List<Article> arts, double ut)
        {
            if (story.Kind != "mission" && story.Kind != "milestone") return;
            if (story.Events.Count == 0) return;
            var tv = Tracked(story.VesselId);
            var craft = story.Craft ?? (tv != null ? tv.Craft : null);
            string name = craft != null && craft.VesselName != "" ? craft.VesselName : story.Events.Select(e => e.VesselName).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
            var lines = story.Events.Select(WikiEventLine).Where(l => l != null).ToList();
            double sig = Math.Max(5, story.Significance);
            var touched = new List<WikiPage>();

            if (name != "")
            {
                var p = WikiEnsure("craft", name, ut);
                if (p != null)
                {
                    if (craft != null) p.Craft = craft;
                    var fl = p.Flights.FirstOrDefault(f => f.VesselId == story.VesselId && story.VesselId != "");
                    if (fl == null)
                    {
                        fl = new WikiFlight { VesselId = story.VesselId, VesselName = name, LaunchUT = tv != null ? tv.LaunchUT : story.Events.Min(e => e.UT) };
                        p.Flights.Add(fl);
                        if (p.Flights.Count > MaxWikiFlights) p.Flights.RemoveAt(0);
                    }
                    foreach (var c in story.Events.SelectMany(e => e.Crew).Concat(craft != null ? craft.Crew : new List<string>()).Distinct())
                        if (!fl.Crew.Contains(c)) fl.Crew.Add(c);
                    foreach (var e in story.Events)
                    {
                        string oc;
                        int rank = OutcomeRank(e, out oc);
                        if (oc != null && rank >= fl.Rank) { fl.Rank = rank; fl.Outcome = oc; }
                    }
                    touched.Add(p);
                }
            }
            foreach (var c in story.Events.SelectMany(e => e.Crew).Distinct())
            {
                var p = WikiEnsure("crew", c, ut);
                if (p != null) touched.Add(p);
            }
            foreach (var b in story.Events.Where(e => e.Body != "" && e.Body != "Kerbin" && (e.Type == EventTypes.Orbit || e.Type == EventTypes.Soi || e.Type == EventTypes.Landing || e.Type == EventTypes.Splashdown || e.Type == EventTypes.CrewKilled || e.Type == EventTypes.Failure)).Select(e => e.Body).Distinct())
            {
                var p = WikiEnsure("place", b, ut);
                if (p != null) touched.Add(p);
            }
            var ag = WikiEnsure("agency", AgencyName, ut);
            foreach (var p in touched.Distinct())
            {
                var mine = p.Kind == "crew" ? lines.Where(l => l.Contains(p.Title) || story.Events.Any(e => e.Crew.Contains(p.Title))).ToList()
                    : p.Kind == "place" ? story.Events.Where(e => e.Body == p.Title).Select(WikiEventLine).Where(l => l != null).ToList() : lines;
                foreach (var l in mine) AddFact(p, l);
                AddRefs(p, arts);
                p.Pending += sig;
                p.UpdatedUT = ut;
            }
            // The agency page keeps only the big moments.
            if (ag != null && story.Significance >= 45)
            {
                foreach (var l in lines.Take(2)) AddFact(ag, l);
                AddRefs(ag, arts.Where(a => a.Significance >= 45));
                ag.Pending += sig * 0.5;
                ag.UpdatedUT = ut;
            }
        }

        // ------------------------------------------------------------------ writing

        /// <summary>A save from before Kerbopedia: builds pages from the stories still kept and every tracked craft.</summary>
        void WikiSeed(double ut)
        {
            State.WikiSeeded = true;
            foreach (var st in State.Stories.Where(x => x.Status == StoryStatus.Published && x.Events.Count > 0).OrderBy(x => x.Events.Min(e => e.UT)))
                WikiOnStory(st, State.Articles.Where(a => a.StoryId == st.Id).ToList(), st.Events.Max(e => e.UT));
            foreach (var tv in State.Tracked.Values.Where(t => t.Craft != null && t.Craft.VesselName != "").OrderBy(t => t.LaunchUT))
            {
                var p = WikiEnsure("craft", tv.Craft.VesselName, tv.LaunchUT);
                if (p == null) continue;
                if (p.Craft == null) p.Craft = tv.Craft;
                if (!p.Flights.Any(f => f.VesselId == tv.VesselId))
                {
                    var fl = new WikiFlight { VesselId = tv.VesselId, VesselName = tv.Craft.VesselName, LaunchUT = tv.LaunchUT };
                    fl.Crew.AddRange(tv.Craft.Crew);
                    if (tv.History.Count > 0) fl.Outcome = "Flown";
                    p.Flights.Add(fl);
                }
                if (p.Facts.Count == 0) foreach (var h in tv.History) AddFact(p, h);
            }
        }

        void TickWiki(double ut, bool allowCalls)
        {
            if (!State.WikiSeeded) WikiSeed(ut);
            if (WikiBusy || !Settings.SiteOn("wiki") || !Settings.AiWrites(GlobalSettings.AiMost) || !allowCalls || InSide()) return;
            if (ut < nextWikiUT) return;
            nextWikiUT = ut + 0.25 * Day;
            // A page waits until the news has been quiet for a day, so it cites the coverage and one call covers a whole mission.
            var due = State.Wiki.Values.Where(p => WikiDue(p) && (p.Requested || ut - p.UpdatedUT >= Day && ut >= p.RetryUT)).OrderByDescending(p => p.Requested).ThenByDescending(p => p.Pending).Take(5).ToList();
            if (due.Count > 0) WriteWiki(due, ut, "");
        }

        static bool WikiDue(WikiPage p)
        {
            if (p.Requested) return true;
            bool hasFacts = p.Facts.Count > 0 || p.Flights.Count > 0 || p.Kind == "agency" && p.Refs.Count > 0;
            return hasFacts && (p.WrittenUT < 0 ? p.Pending >= 20 : p.Pending >= WikiRewriteAt);
        }

        /// <summary>Asks the AI to (re)write a page now, optionally with the director's instruction. False when the AI is off or busy.</summary>
        public bool WikiRewrite(string page, string instruction, double ut)
        {
            var p = WikiGet(page);
            if (p == null || WikiBusy || !Settings.CanCallAi) return false;
            WriteWiki(new List<WikiPage> { p }, ut, instruction ?? "");
            return true;
        }

        string WikiFactsText(WikiPage p)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PAGE " + p.Slug + " | " + p.Kind + " | title: " + p.Title);
            if (p.Kind == "craft" && p.Craft != null)
            {
                var c = p.Craft;
                sb.AppendLine("Specs: " + c.VesselType + ", " + c.Parts + " parts, " + c.MassTons.ToString("0.#", AInv) + " t, " + c.HeightM.ToString("0.#", AInv) + " m tall, " + c.Stages + " stages, "
                    + Math.Round(c.DeltaV).ToString(AInv) + " m/s delta-v, " + c.CrewCapacity + " seats" + (c.Engines != "" ? ", engines: " + c.Engines : "") + (c.Description != "" ? ". Builder's notes: " + ResponseParser.Clip(c.Description, 300) : ""));
            }
            if (p.Flights.Count > 0)
                sb.AppendLine("Flights: " + string.Join("; ", p.Flights.Select((f, i) => "#" + (i + 1) + " " + Clock.FormatDate(f.LaunchUT) + (f.Crew.Count > 0 ? ", crew " + string.Join(", ", f.Crew.ToArray()) : ", uncrewed") + ", " + f.Outcome).ToArray()));
            if (p.Kind == "crew")
            {
                CrewFame k;
                if (State.Crew.TryGetValue(p.Title, out k))
                    sb.AppendLine("Kerbal: " + (k.Trait != "" ? k.Trait : "astronaut") + ", level " + k.Level + ", " + k.Flights + " flights" + (k.Dead ? ", died " + Clock.FormatDate(k.DiedUT) + (k.DiedOn != "" ? " on " + k.DiedOn : "") : "")
                        + (k.Highlights.Count > 0 ? ". Highlights: " + string.Join("; ", k.Highlights.ToArray()) : "") + (k.Badass ? ". Known for nerves of steel." : ""));
            }
            if (p.Kind == "craft")
            {
                var looks = State.Gallery.Where(g => g.Look != "" && (g.Public || g.IsPress) && g.VesselName.Equals(p.Title, StringComparison.OrdinalIgnoreCase)).OrderByDescending(g => g.UT).Take(2).Select(g => g.Look).ToList();
                if (looks.Count > 0) sb.AppendLine("Published photos show: " + string.Join(" | ", looks.ToArray()) + " (you may describe its appearance in Design).");
            }
            if (p.Facts.Count > 0) { sb.AppendLine("What happened:"); foreach (var f in p.Facts.Skip(Math.Max(0, p.Facts.Count - 30))) sb.AppendLine("- " + f); }
            var refs = WikiRefArticles(p);
            if (refs.Count > 0) { sb.AppendLine("Press coverage (cite as [n]):"); for (int i = 0; i < refs.Count; i++) sb.AppendLine("[" + (i + 1) + "] " + Outlets.NameOf(refs[i].Outlet) + ", " + Clock.FormatDate(refs[i].PublishedUT) + ": " + refs[i].Headline + (refs[i].Subhead != "" ? ". " + refs[i].Subhead : "")); }
            if (p.Notes.Count > 0) { sb.AppendLine("THE DIRECTOR'S WISHES FOR THIS PAGE (always keep to them):"); foreach (var x in p.Notes) sb.AppendLine("- " + x); }
            if (p.WrittenUT >= 0)
            {
                sb.AppendLine("CURRENT TEXT (revise and extend it; keep what is still right):");
                sb.AppendLine("Lead: " + p.Lead);
                foreach (var s in p.Sections) sb.AppendLine("## " + s.Heading + "\n" + s.Text);
            }
            return sb.ToString();
        }

        public List<Article> WikiRefArticles(WikiPage p)
        {
            var now = lastVisibleUT > 0 ? lastVisibleUT : double.MaxValue;
            return p.Refs.Select(id => State.Articles.FirstOrDefault(a => a.Id == id) ?? State.Archive.FirstOrDefault(a => a.Id == id))
                .Where(a => a != null && !a.Removed && a.PublishedUT <= now).ToList();
        }

        void WriteWiki(List<WikiPage> pages, double ut, string instruction)
        {
            WikiBusy = true;
            var sb = new StringBuilder();
            sb.AppendLine("You write articles for Kerbopedia, the free encyclopedia of the Kerbin space age, in the world of Kerbal Space Program (KSP1). Write like a good encyclopedia: neutral, factual, past tense for events, no hype, no second person.");
            sb.AppendLine("RULES:");
            sb.AppendLine("- Use ONLY the facts, specs and press coverage given. Never invent flights, numbers, names, dates or results. You may explain what the facts mean and how the press reacted.");
            sb.AppendLine("- Cite press coverage with its number in square brackets, like [2], right after the sentence it supports.");
            sb.AppendLine("- Kerbal names end in Kerman. Everyone in this world is a kerbal: say kerbal spaceflight, never human. Rival programs only ever work in orbit around Kerbin and never beat " + AgencyName + " to anything.");
            sb.AppendLine("- Lead: 2-4 sentences that say what the subject is and why it matters. Then 2-5 sections with short headings (for a craft: Design, Flight history, Reception; for a kerbal: Career, Missions, Public image; for a place: Exploration, Missions; for the agency: History, Programs, Public image). Each section 1-3 short paragraphs separated by blank lines. Skip a section when there is nothing to say.");
            sb.AppendLine("- Follow the director's wishes for a page when given, as long as they don't contradict the facts.");
            string canon = PromptBuilder.ProfileText(State);
            if (canon != "") sb.AppendLine(canon);
            sb.AppendLine("Reply with JSON only: {\"pages\":[{\"page\":\"slug\",\"lead\":\"...\",\"sections\":[{\"heading\":\"...\",\"text\":\"...\"}],\"change\":\"what you changed, a few words\"}]} with one entry per page.");
            var u = new StringBuilder();
            u.AppendLine("Date: " + Clock.FormatDate(ut));
            if (instruction != "") u.AppendLine("THE DIRECTOR ASKS: " + instruction);
            foreach (var p in pages) { u.AppendLine(); u.Append(WikiFactsText(p)); }
            AskJson(Settings.Writer, sb.ToString(), u.ToString(), Math.Min(4000, 300 + pages.Count * 900), 0, (root, err, text, res) =>
            {
                WikiBusy = false;
                var arr = root != null ? Json.Arr(Json.Get(root, "pages")) : null;
                if (arr == null)
                {
                    // A bad reply is not retried every few hours: the pages wait a day (and longer after repeated failures).
                    if (err != null) LastError = err;
                    wikiFailures++;
                    nextWikiUT = ut + Math.Min(8, Math.Pow(2, wikiFailures - 1)) * Day;
                    foreach (var p in pages) p.Requested = false;
                    return;
                }
                wikiFailures = 0;
                foreach (var p in pages)
                {
                    var o = arr.FirstOrDefault(x => Json.Str(x, "page", "") == p.Slug) ?? (pages.Count == 1 && arr.Count > 0 ? arr[0] : null);
                    p.Requested = false;
                    if (o == null) { p.RetryUT = ut + Day; continue; }   // left out of the reply: try again in a day, not in six hours
                    string lead = Json.Str(o, "lead", "").Trim();
                    var secs = (Json.Arr(Json.Get(o, "sections")) ?? new List<object>())
                        .Select(s => new WikiSection { Heading = ResponseParser.Clip(Json.Str(s, "heading", "").Trim(), 80), Text = ResponseParser.Clip(Json.Str(s, "text", "").Trim(), 3000) })
                        .Where(s => s.Heading != "" && s.Text != "").Take(6).ToList();
                    if (lead == "") continue;
                    bool first = p.WrittenUT < 0;   // the page was created earlier from the template; this is its first real article
                    p.Lead = ResponseParser.Clip(lead, 1500);
                    p.Sections = secs;
                    p.WrittenUT = ut;
                    p.Pending = 0;
                    string ch = ResponseParser.Clip(Json.Str(o, "change", "").Trim(), 120);
                    p.Revise(ut, instruction != "" ? "An account at " + AgencyShort : "Kerbopedia editors", first && instruction == "" ? "Expanded from a stub into a full article" : ch != "" ? ch : instruction != "" ? ResponseParser.Clip(instruction, 120) : "Updated");
                }
            });
        }

        string AgencyShort { get { return State.Profile.ShortName != "" ? State.Profile.ShortName : "the agency"; } }

        // ------------------------------------------------------------------ the director's edits

        /// <summary>The assistant's edit_wiki: replaces the lead or sections the director wants changed, and remembers a standing wish.</summary>
        public string WikiEdit(Dictionary<string, object> a, double ut)
        {
            string which = Arg(a, "page");
            var p = WikiGet(which);
            string lead = Arg(a, "lead").Trim(), remember = Arg(a, "remember").Trim(), note = Arg(a, "note").Trim(), rewrite = Arg(a, "rewrite").Trim();
            var secs = Json.Arr(Json.Get(a, "sections")) ?? new List<object>();
            if (p == null)
            {
                if (which.Trim() == "" || lead == "" && secs.Count == 0 && rewrite == "") return "I couldn't find a Kerbopedia page called \"" + which + "\".";
                p = WikiEnsure("topic", ResponseParser.Clip(which.Trim(), 80), ut);
                if (p == null) return "That page name doesn't work.";
            }
            var changed = new List<string>();
            if (lead != "") { p.Lead = ResponseParser.Clip(lead, 1500); changed.Add("the opening"); }
            foreach (var s in secs)
            {
                string h = Json.Str(s, "heading", "").Trim(), t = Json.Str(s, "text", "").Trim();
                if (h == "") continue;
                int i = p.Sections.FindIndex(x => x.Heading.Equals(h, StringComparison.OrdinalIgnoreCase));
                if (t == "") { if (i >= 0) { p.Sections.RemoveAt(i); changed.Add("removed " + h); } continue; }
                if (i >= 0) p.Sections[i].Text = ResponseParser.Clip(t, 3000);
                else p.Sections.Add(new WikiSection { Heading = ResponseParser.Clip(h, 80), Text = ResponseParser.Clip(t, 3000) });
                changed.Add(h);
            }
            if (changed.Count > 0 && p.WrittenUT < 0)
            {
                // The page had template text until now: keep the template's other parts as written text, so nothing disappears.
                if (p.Lead == "") p.Lead = WikiTemplateLead(p);
                p.WrittenUT = ut;
            }
            if (remember != "" && !p.Notes.Contains(remember)) { p.Notes.Add(ResponseParser.Clip(remember, 300)); if (p.Notes.Count > 10) p.Notes.RemoveAt(0); }
            string by = "An account at " + AgencyShort;
            if (changed.Count > 0) p.Revise(ut, by, note != "" ? ResponseParser.Clip(note, 120) : "Edited " + string.Join(", ", changed.ToArray()));
            string result = changed.Count > 0 ? "Edited the Kerbopedia page \"" + p.Title + "\" (" + string.Join(", ", changed.ToArray()) + ")." : "";
            if (rewrite != "")
            {
                bool started = WikiRewrite(p.Slug, rewrite, ut);
                result += (result != "" ? " " : "") + (started ? "Kerbopedia's writers are rewriting \"" + p.Title + "\" as asked; it updates in a minute or so." : Settings.CanCallAi ? "Kerbopedia's writers are busy; ask again in a moment." : "The AI is off, so the page can't be rewritten for you; give me the new text instead.");
            }
            if (remember != "") result += (result != "" ? " " : "") + "Future updates to the page will keep to: " + remember;
            return result != "" ? result : "Nothing to change on \"" + p.Title + "\".";
        }

        /// <summary>The assistant's wiki lookup: the page list, or one page's text.</summary>
        public string WikiLookup(string page)
        {
            var p = WikiGet(page);
            var sb = new StringBuilder();
            if (p == null)
            {
                if (State.Wiki.Count == 0) return "Kerbopedia has no pages yet; they appear after the first mission is covered.";
                sb.AppendLine("Kerbopedia pages (slug | kind | title):");
                foreach (var x in State.Wiki.Values.OrderBy(x => x.Kind).ThenBy(x => x.Title).Take(60)) sb.AppendLine(x.Slug + " | " + x.Kind + " | " + x.Title);
                return sb.ToString();
            }
            sb.AppendLine("Page " + p.Slug + " (" + p.Kind + "): " + p.Title + (p.WrittenUT < 0 ? " [template text so far]" : ""));
            sb.AppendLine("Lead: " + (p.Lead != "" ? p.Lead : WikiTemplateLead(p)));
            foreach (var s in (p.WrittenUT < 0 ? WikiTemplateSections(p) : p.Sections)) sb.AppendLine("## " + s.Heading + "\n" + s.Text);
            if (p.Notes.Count > 0) sb.AppendLine("Standing wishes: " + string.Join("; ", p.Notes.ToArray()));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ template text (no AI yet)

        public string WikiTemplateLead(WikiPage p)
        {
            string ag = AgencyName;
            switch (p.Kind)
            {
                case "craft":
                {
                    var c = p.Craft;
                    string type = c != null && c.VesselType != "" ? c.VesselType.ToLowerInvariant() : "craft";
                    bool crewed = p.Flights.Any(f => f.Crew.Count > 0);
                    var first = p.Flights.OrderBy(f => f.LaunchUT).FirstOrDefault();
                    // Worded from the bank, but seeded by the title so the page reads the same every time it is opened.
                    int seed = 17; foreach (char ch in p.Title) seed = unchecked(seed * 31 + ch);
                    var lv = new Dictionary<string, string> { { "title", p.Title }, { "agency", ag }, { "kind", (crewed ? "crewed " : "") + (type == "ship" ? "spacecraft" : type) } };
                    string s = Phrases.Rule("wk_craft_lead", lv, new Random(seed));
                    if (s == "") s = p.Title + " is a " + (crewed ? "crewed " : "") + (type == "ship" ? "spacecraft" : type) + " built by " + ag + ".";
                    if (first != null) s += " It first flew on " + Clock.FormatDate(first.LaunchUT) + (first.Crew.Count > 0 ? " with " + string.Join(", ", first.Crew.ToArray()) + " aboard" : "") + ".";
                    if (p.Flights.Count > 1) s += " It has flown " + p.Flights.Count + " times.";
                    return s;
                }
                case "crew":
                {
                    CrewFame k;
                    State.Crew.TryGetValue(p.Title, out k);
                    string trait = k != null && k.Trait != "" ? k.Trait.ToLowerInvariant() : "astronaut";
                    string s = p.Title + " is a kerbal " + trait + " with " + ag + ".";
                    if (k != null && k.Flights > 0) s += " " + (k.Flights == 1 ? "They have flown once" : "They have flown " + k.Flights + " times") + (k.FirstFlightUT >= 0 ? ", first on " + Clock.FormatDate(k.FirstFlightUT) : "") + ".";
                    if (k != null && k.Dead) s = s.Replace(" is a ", " was a ").Replace("They have flown", "They flew") + " They died on " + Clock.FormatDate(k.DiedUT) + (k.DiedOn != "" ? " aboard " + k.DiedOn : "") + ".";
                    return s;
                }
                case "place":
                {
                    string first = p.Facts.FirstOrDefault() ?? "";
                    int c = first.IndexOf(": ", StringComparison.Ordinal);
                    return p.Title + " is a body in the Kerbol system." + (first != "" ? " " + ag + " first reached it on " + (c > 0 ? first.Substring(0, c) : Clock.FormatDate(p.CreatedUT)) + "." : "");
                }
                case "agency":
                {
                    var pr = State.Profile;
                    string s = ag + (pr.ShortName != "" && pr.ShortName != ag ? " (" + pr.ShortName + ")" : "") + " is Kerbin's space agency.";
                    if (pr.Mission != "") s += " " + pr.Mission.Trim();
                    return s;
                }
                default: return p.Title + " is a subject covered by Kerbopedia.";
            }
        }

        public List<WikiSection> WikiTemplateSections(WikiPage p)
        {
            var list = new List<WikiSection>();
            if (p.Kind == "craft" && p.Craft != null)
            {
                var c = p.Craft;
                string t = p.Title + " has " + c.Parts + " parts and stands " + c.HeightM.ToString("0.#", AInv) + " m tall, with a launch mass of " + c.MassTons.ToString("0.#", AInv) + " t across " + c.Stages + " stage" + (c.Stages == 1 ? "" : "s") + ".";
                if (c.Engines != "") t += " Engines: " + c.Engines + ".";
                if (c.Description != "") t += "\n\n" + c.Description.Trim();
                list.Add(new WikiSection { Heading = "Design", Text = t });
            }
            if (p.Kind == "agency")
            {
                var pr = State.Profile;
                if (pr.Founding != "") list.Add(new WikiSection { Heading = "History", Text = pr.Founding.Trim() });
                if (pr.KeyPeople != "") list.Add(new WikiSection { Heading = "Leadership", Text = pr.KeyPeople.Trim() });
            }
            if (p.Facts.Count > 0)
                list.Add(new WikiSection { Heading = p.Kind == "crew" ? "Career" : p.Kind == "place" ? "Exploration" : p.Kind == "agency" ? "Milestones" : "Flight history", Text = string.Join("\n", p.Facts.Select(f => f + ".").ToArray()) });
            return list;
        }
    }
}
