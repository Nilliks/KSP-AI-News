using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    /// <summary>
    /// The news cycle: events come in, are grouped into stories, wait about a Kerbin day,
    /// then are written (by AI or templates) and published into the save.
    /// Game-independent so it can be tested outside KSP.
    /// </summary>
    public partial class NewsEngine
    {
        public NewsState State { get; private set; }

        /// <summary>
        /// Swap in a freshly loaded state (scene change, quickload, revert). A request already
        /// in flight is kept: when it returns it is applied only if the same story is still
        /// pending in the new state, so reverted stories are dropped and the rest are not wasted.
        /// </summary>
        public void SetState(NewsState s)
        {
            State = s ?? new NewsState();
            lastVisibleUT = -1;
            try { HealNames(); } catch (Exception) { }
        }

        Story FindLive(Story s)
        {
            return State.Stories.FirstOrDefault(x => x.Id == s.Id && x.CreatedUT == s.CreatedUT && x.Status == StoryStatus.Pending
                && x.Kind == s.Kind && x.VesselId == s.VesselId);
        }

        bool IsInFlight(Story s) { return inFlight != null && s.Id == inFlight.Id && s.CreatedUT == inFlight.CreatedUT; }
        public GlobalSettings Settings;
        public IHttpTransport Transport;
        public IGameClock Clock;
        /// <summary>Supplies the current world digest; may return null.</summary>
        public Func<WorldDigest> Digest = () => null;
        /// <summary>Raised when articles become visible to the reader (their publish time passed).</summary>
        public event Action<List<Article>> ArticlesPublished;

        public string Status = "Idle";
        public string LastError = "";
        public bool Busy { get { return inFlight != null || compressing; } }

        Story inFlight;
        bool compressing;
        double retryAfterReal;
        double lastVisibleUT = -1;
        public double FillerQuietDays = 3;
        public double FillerMinGapDays = 1;
        const int MaxAttempts = 3;

        public NewsEngine(NewsState state, GlobalSettings settings, IHttpTransport transport, IGameClock clock)
        {
            State = state;
            Settings = settings;
            Transport = transport;
            Clock = clock;
            Outlets.IsOn = id => Settings.SiteOn(id);
        }

        double Day { get { return Clock.DaySeconds; } }

        // ------------------------------------------------------------------ ingest

        public void TrackLaunch(string vesselId, CraftSnapshot craft, double ut)
        {
            if (string.IsNullOrEmpty(vesselId)) return;
            State.Tracked[vesselId] = new TrackedVessel { VesselId = vesselId, LaunchUT = ut, Craft = craft };
        }

        public bool IsTracked(string vesselId) { return vesselId != null && State.Tracked.ContainsKey(vesselId); }

        public TrackedVessel Tracked(string vesselId)
        {
            TrackedVessel t;
            return vesselId != null && State.Tracked.TryGetValue(vesselId, out t) ? t : null;
        }

        /// <summary>Record an event. Returns the story it joined.</summary>
        public Story Ingest(NewsEvent e)
        {
            if (Stream != null && e != null && e.VesselId != "" && e.VesselId == Stream.VesselId && e.Type != EventTypes.Livestream)
                StreamMoment(StreamMomentText(e), lastRealTime);
            if (e == null || string.IsNullOrEmpty(e.Type)) return null;
            if (HoldForSide(e)) return null;
            try { if (SuppressEvent(e)) return null; } catch { }
            // A silent vessel only makes news through what anyone can see from the ground.
            if (State.IsSilent(e.VesselId) && !VisibleFromGround(e)) return null;
            string key = Significance.NoveltyKey(e);
            double seen;
            State.Novelty.TryGetValue(key, out seen);
            e.Significance = Significance.Score(e, seen);
            e.Significance = Math.Min(100, e.Significance + FameBoost(e));
            State.Novelty[key] = seen + 1;
            RecordCrew(e);
            RecordIncident(e);
            AnnotateContractEvent(e);

            Story story = null;
            if (!string.IsNullOrEmpty(e.VesselId))
                story = State.Stories.FirstOrDefault(s => s.Status == StoryStatus.Pending && s.Kind == "mission" && s.VesselId == e.VesselId && !IsInFlight(s));
            if (story == null && e.Type == EventTypes.Milestone)
            {
                // A "first" usually belongs to whatever mission caused it.
                story = State.Stories.Where(s => s.Status == StoryStatus.Pending && s.Kind == "mission" && !IsInFlight(s) && e.UT - s.CreatedUT < Day)
                    .OrderByDescending(s => s.CreatedUT).FirstOrDefault();
            }
            if (story == null)
            {
                story = new Story
                {
                    Id = State.NewId("s"),
                    Kind = string.IsNullOrEmpty(e.VesselId) ? (e.Type.StartsWith("contract") ? "contract" : "milestone") : "mission",
                    VesselId = e.VesselId ?? "",
                    CreatedUT = e.UT,
                };
                var tv = Tracked(e.VesselId);
                if (tv != null) story.Craft = tv.Craft;
                State.Stories.Add(story);
            }
            story.Events.Add(e);
            if (e.Type == EventTypes.Milestone && e.Detail != "")
            {
                string rec = e.Detail + (e.VesselName != "" ? " (" + e.VesselName + ", " + Clock.FormatDate(e.UT) + ")" : " (" + Clock.FormatDate(e.UT) + ")");
                if (!State.Records.Any(r => r.StartsWith(e.Detail + " ("))) State.Records.Add(rec);
            }
            Reschedule(story);
            return story;
        }

        /// <summary>Launches, explosions, deaths and returns home cannot be hidden, and amateur trackers
        /// can spot a new object in orbit of Kerbin. Everything else needs telemetry.</summary>
        public static bool VisibleFromGround(NewsEvent e)
        {
            switch (e.Type)
            {
                case EventTypes.Orbit:
                    return e.Body == "Kerbin";
                case EventTypes.Launch:
                case EventTypes.Failure:
                case EventTypes.CrewKilled:
                case EventTypes.Recovery:
                    return true;
                case EventTypes.Splashdown:
                case EventTypes.Landing:
                    return e.Body == "Kerbin";
                default:
                    return false;
            }
        }

        /// <summary>Applies the vessel's current telemetry setting right before a story is written.</summary>
        void ApplyPrivacy(Story s)
        {
            s.Silent = s.Kind == "mission" && State.IsSilent(s.VesselId);
            if (!s.Silent) return;
            s.Events.RemoveAll(e => !VisibleFromGround(e));
            s.Significance = Significance.StoryScore(s);
        }

        void Reschedule(Story s)
        {
            s.Significance = Significance.StoryScore(s);
            var plan = CoveragePlan.For(s);
            s.PublishUT = s.CreatedUT + plan.EarliestDelay * Day;
        }

        // ------------------------------------------------------------------ tick

        /// <summary>
        /// Call every frame or so. allowCalls is false during time warp or when the host wants quiet.
        /// realTime is wall-clock seconds, used for retry backoff.
        /// </summary>
        public void Tick(double ut, double realTime, bool allowCalls)
        {
            FillerQuietDays = Settings.QuietDays;
            StreamInterval = Settings.StreamSeconds;
            bool side = InSide();
            if (side) allowCalls = false;   // FMRS side timeline: nothing done here survives
            else FlushSide();
            allowCalls = WarpGate(ut, realTime, allowCalls);
            if (State.LastStoryUT < 0) State.LastStoryUT = ut;
            if (State.LastMoodDecayUT < 0) State.LastMoodDecayUT = ut;
            if (lastVisibleUT < 0) lastVisibleUT = ut;

            double days = (ut - State.LastMoodDecayUT) / Day;
            if (days >= 0.25)
            {
                State.Mood.Decay(days);
                Significance.DecayNovelty(State.Novelty, days);
                DecayFame(days);
                State.LastMoodDecayUT = ut;
                SampleDigest(ut);
            }
            else if (days < 0) State.LastMoodDecayUT = ut; // time went backwards (shouldn't after a proper load)

            FireVisible(ut);
            TickCommunity(ut, realTime, allowCalls);
            TickImages(allowCalls);
            TickTownHalls(ut);
            TickAssistant(ut);
            TickAnniversaries(ut);
            TickWorld(ut, allowCalls);
            TickAppearances(ut);
            if (State.TieIns.Count > 0 && State.TieIns.Any(t => t.Open)) TickTieIns(ut);
            MaybePoll(ut);

            if (inFlight != null || compressing || realTime < retryAfterReal) return;

            if (MaybeCompress(ut, realTime, allowCalls)) return;

            var due = State.Stories.Where(s => s.Status == StoryStatus.Pending && s.PublishUT <= ut).OrderBy(s => s.PublishUT).FirstOrDefault();
            if (due == null)
            {
                if (allowCalls) MaybeQueueFiller(ut);   // nothing gets queued for days skipped in a warp
                return;
            }
            ApplyPrivacy(due);
            var plan = PlanFor(due);
            if (plan.IsEmpty || (due.Kind != "filler" && due.Kind != "tip" && due.Events.Count == 0))
            {
                // Too small for anyone to cover. It still becomes part of the public memory.
                Finalize(due, new GeneratedCoverage(), plan, ut);
                return;
            }
            if (!Settings.CanCallAi)
            {
                Finalize(due, OfflineWriter.Write(due, plan, State, Clock), plan, ut);
                return;
            }
            if (!allowCalls) { Status = "Waiting for time warp to end"; return; }
            Generate(due, plan, ut, realTime);
        }

        /// <summary>The first of these outlets that is turned on, or the first turned-on outlet at all, or null.</summary>
        public string FirstOutletOn(params string[] prefs)
        {
            foreach (var p in prefs) if (Settings.SiteOn(p)) return p;
            var any = Outlets.Enabled.FirstOrDefault(o => !o.IsOfficial);
            return any != null ? any.Id : null;
        }

        CoveragePlan PlanFor(Story s)
        {
            CoveragePlan plan;
            if (s.Kind == "filler") plan = CoveragePlan.ForFiller(s.FillerOutlet == "" ? "herald" : s.FillerOutlet);
            else if (s.Kind == "tip") plan = CoveragePlan.ForTip(s);
            else if (s.Kind == "release") { var r = ReleaseOf(s); plan = CoveragePlan.ForRelease(s, r != null ? r.Type : "announcement"); }
            else plan = CoveragePlan.For(s);
            return ApplyUsage(plan);
        }

        /// <summary>Order in which outlets keep their place when the usage settings cap how many cover a story.</summary>
        static readonly string[] OutletPriority = { "herald", "observer", "kraken", "inquirer", "kscwatch", "science", "munmonthly", "council" };

        /// <summary>The player's API-usage settings: fewer outlets, shorter pieces, fewer comments and posts (or more).</summary>
        CoveragePlan ApplyUsage(CoveragePlan plan)
        {
            var g = Settings;
            // Turned-off sites write nothing.
            plan.Outlets.RemoveAll(o => !g.SiteOn(o.OutletId));
            if (!g.SiteOn("kerbnet")) plan.SocialPosts = 0;
            plan.Forum = g.SiteOn("snackboard");
            if (g.MaxOutlets > 0 && plan.Outlets.Count > g.MaxOutlets)
                plan.Outlets = plan.Outlets.OrderBy(o => { int i = Array.IndexOf(OutletPriority, o.OutletId); return i < 0 ? 99 : i; }).Take(g.MaxOutlets).ToList();
            if (Math.Abs(g.AmountScale - 1) > 0.01)
            {
                foreach (var o in plan.Outlets)
                {
                    o.Words = Math.Max(60, (int)Math.Round(o.Words * g.AmountScale));
                    o.Comments = Math.Max(1, (int)Math.Round(o.Comments * g.AmountScale));
                }
                if (plan.SocialPosts > 0) plan.SocialPosts = Math.Max(1, (int)Math.Round(plan.SocialPosts * g.AmountScale));
            }
            return plan;
        }

        void Generate(Story story, CoveragePlan plan, double ut, double realTime)
        {
            inFlight = story;
            story.Attempts++;
            Status = "Writing coverage: " + story.Title;
            var preset = Settings.Preset;
            bool cheap = PlanTierCheap(plan);
            string model = cheap ? Settings.CheapModel : Settings.WriterModel;
            string system = PromptBuilder.System(State, Settings.ModsText);
            string user = PromptBuilder.User(story, plan, State, Digest(), Clock, ut, WantsImage(story, plan), Settings.KtvOn);
            var req = Providers.Build(preset, Settings.BaseUrl, Settings.Key, model, system, user, Settings.MaxTokens, preset.JsonMode);
            req.TimeoutSeconds = Settings.TimeoutSeconds;
            Transport.Send(req, res =>
            {
                // The save may have been reloaded while we waited; only write into a story that still exists.
                inFlight = null;
                story = FindLive(story);
                if (story == null) return;
                string text, err;
                GeneratedCoverage cov = null;
                if (Providers.TryExtractText(preset.Kind, res, out text, out err))
                    ResponseParser.TryParse(text, plan, out cov, out err);
                if (cov != null)
                {
                    LastError = "";
                    Finalize(story, cov, plan, ut);
                    return;
                }
                LastError = err ?? "Unknown error";
                if (story.Attempts >= MaxAttempts || IsPermanent(res))
                {
                    Status = "AI unavailable, used offline templates";
                    Finalize(story, OfflineWriter.Write(story, plan, State, Clock), plan, ut);
                }
                else
                {
                    Status = "Retrying soon: " + LastError;
                    retryAfterReal = realTime + 20 * story.Attempts;
                }
            });
        }

        static bool IsPermanent(HttpResult r)
        {
            // Bad key or bad request will not fix itself by retrying.
            return r.Error == null && (r.Status == 400 || r.Status == 401 || r.Status == 403 || r.Status == 404);
        }

        bool PlanTierCheap(CoveragePlan plan)
        {
            switch (Settings.WriterFrom)
            {
                case "brief": return false;
                case "major": return plan.Tier != "major" && plan.Tier != "historic";
                default: return plan.Tier == "brief" || plan.Tier == "feature";
            }
        }

        void Finalize(Story story, GeneratedCoverage cov, CoveragePlan plan, double ut)
        {
            bool rewritten = story.MoodApplied;
            var rng = new Random(OfflineWriter.StableHash(story.Id));
            string model = cov.Articles.Count > 0 && cov.Articles[0].Offline ? "offline templates" : Settings.Preset.Id + " / " + (PlanTierCheap(plan) ? Settings.CheapModel : Settings.WriterModel);
            foreach (var a in cov.Articles)
            {
                a.Model = model;
                var assign = plan.Outlets.FirstOrDefault(o => o.OutletId == a.Outlet);
                double delay = story.Kind == "filler" || story.Kind == "tip" ? 0 : (assign != null ? assign.DelayDays : 1);
                a.Id = State.NewId("a");
                a.StoryId = story.Id;
                a.Significance = story.Significance;
                // Publish on the outlet's own schedule, with a little jitter, never before generation.
                double when = story.CreatedUT + delay * Day + rng.NextDouble() * 0.1 * Day;
                a.PublishedUT = story.Kind == "filler" ? ut : Math.Max(when, story.PublishUT);
                // A late answer (time warp, slow AI) is dated in the past; still announce it.
                if (a.PublishedUT <= lastVisibleUT) late.Add(a);
                if (story.HasEvent(EventTypes.CrewKilled) && !a.Tags.Contains("crew loss")) a.Tags.Add("crew loss");
                if (story.HasEvent(EventTypes.Failure) && !a.Tags.Contains("failure")) a.Tags.Add("failure");
                if (a.Reporter != "")
                {
                    List<string> reps;
                    if (!State.Reporters.TryGetValue(a.Outlet, out reps)) State.Reporters[a.Outlet] = reps = new List<string>();
                    if (!reps.Contains(a.Reporter) && reps.Count < 4) reps.Add(a.Reporter);
                }
                State.Articles.Add(a);
            }
            double first = cov.Articles.Count > 0 ? cov.Articles.Min(a => a.PublishedUT) : ut;
            for (int i = 0; i < cov.Social.Count; i++)
            {
                var p = cov.Social[i];
                p.StoryId = story.Id;
                // Social chatter spreads out over the following day.
                p.UT = first + (i / (double)Math.Max(1, cov.Social.Count)) * Day + rng.NextDouble() * 0.05 * Day;
                State.Social.Add(p);
            }
            if (cov.Forum != null && story.Kind != "filler" && Settings.SiteOn("snackboard"))
            {
                var t = cov.Forum;
                t.Id = State.NewId("t");
                t.StoryId = story.Id;
                t.UT = first + 0.1 * Day;
                foreach (var c in t.Comments) { c.Id = State.NewId("c"); c.UT = t.UT; }
                for (int i = 0; i < t.Comments.Count; i++)
                {
                    var c = t.Comments[i];
                    int k;
                    if (c.ReplyTo.StartsWith("#")) c.ReplyTo = int.TryParse(c.ReplyTo.Substring(1), out k) && k >= 0 && k < i ? t.Comments[k].Id : "";
                }
                State.Forum.Add(t);
            }
            foreach (var a in cov.Articles) EnsureIds(a.Comments);
            string shot = ShotFor(story);
            if (shot != null) foreach (var a in cov.Articles) if (a.Image == "") a.Image = shot;
            if (shot == null && cov.Articles.Count > 0 && !rewritten && WantsImage(story, plan)) QueueImage(story, cov.ImagePrompt);
            if (!rewritten && cov.Articles.Count > 0)
            {
                var seg = !Settings.KtvOn ? null : cov.Tv ?? (story.Significance >= 45 ? OfflineWriter.TvFrom(cov.Articles.OrderByDescending(a => a.Significance).First()) : null);
                if (seg != null)
                {
                    seg.StoryId = story.Id;
                    seg.UT = first;
                    seg.Significance = story.Significance;
                    seg.Image = shot ?? "";
                    State.Tv.RemoveAll(x => x.StoryId == story.Id);
                    State.Tv.Add(seg);
                    if (State.Tv.Count > NewsState.MaxTv * 2) State.Tv.RemoveRange(0, State.Tv.Count - NewsState.MaxTv);
                }
            }
            story.Status = StoryStatus.Published;
            if (story.Kind == "filler") State.LastFillerUT = ut;
            else State.LastStoryUT = Math.Max(State.LastStoryUT, story.CreatedUT);
            if (!story.MoodApplied) State.Mood.ApplyStory(story, ut);
            story.MoodApplied = true;
            var tv = Tracked(story.VesselId);
            if (tv != null && !rewritten)
                foreach (var e in story.Events)
                    tv.History.Add(Clock.FormatDate(e.UT) + ": " + e.Type + (e.Body != "" ? " (" + e.Body + ")" : ""));
            if (tv != null && tv.History.Count > 12) tv.History.RemoveRange(0, tv.History.Count - 12);
            if (Status.StartsWith("Writing")) Status = "Idle";
            if (!rewritten) ConsiderTieIn(story, cov.Articles, ut);
            State.Prune();
        }

        readonly List<Article> late = new List<Article>();

        void FireVisible(double ut)
        {
            if (ut <= lastVisibleUT) { lastVisibleUT = ut; return; }
            var fresh = State.Articles.Where(a => (a.PublishedUT > lastVisibleUT || late.Contains(a)) && a.PublishedUT <= ut).ToList();
            late.Clear();
            lastVisibleUT = ut;
            if (fresh.Count > 0 && ArticlesPublished != null) ArticlesPublished(fresh);
        }

        void SampleDigest(double ut)
        {
            var last = State.DigestHistory.LastOrDefault();
            if (last != null && ut - last.UT < Day) return;
            var d = Digest();
            if (d == null) return;
            d.UT = ut;
            State.DigestHistory.Add(d);
        }

        // ------------------------------------------------------------------ filler

        static readonly string[] FillerOutlets = { "herald", "kraken", "observer", "kscwatch", "munmonthly", "inquirer", "herald", "kraken" };

        void MaybeQueueFiller(double ut)
        {
            double quiet = (ut - State.LastStoryUT) / Day;
            if (FillerQuietDays <= 0) return;
            double sinceFiller = State.LastFillerUT < 0 ? double.MaxValue : (ut - State.LastFillerUT) / Day;
            if (quiet < FillerQuietDays || sinceFiller < FillerMinGapDays) return;
            if (State.Stories.Any(s => s.Status == StoryStatus.Pending)) return;
            int n = State.Articles.Count(a => a.Kind == "feature" || a.Kind == "opinion");
            string outlet = null;
            for (int i = 0; i < FillerOutlets.Length && outlet == null; i++)
            {
                string o = FillerOutlets[(n + i) % FillerOutlets.Length];
                if (Settings.SiteOn(o)) outlet = o;
            }
            if (outlet == null) return;   // every feature-writing outlet is turned off
            string topic = FillerTopic(outlet, n, ut);
            State.Stories.Add(new Story
            {
                Id = State.NewId("s"),
                Kind = "filler",
                Topic = topic,
                FillerOutlet = outlet,
                CreatedUT = ut,
                PublishUT = ut,
                Significance = 20,
            });
            // Mark it now so a failure to publish cannot spawn duplicates.
            State.LastFillerUT = ut;
        }

        string FillerTopic(string outlet, int n, double ut)
        {
            var d = Digest();
            var options = new List<string>();
            if (d != null && d.Debris > 0) options.Add("The growing amount of orbital debris (" + d.Debris + " tracked objects) and whether the agency is doing enough about it.");
            if (d != null && d.CrewInSpaceNames.Count > 0) options.Add("Life aboard for the kerbonauts currently in space: " + string.Join(", ", d.CrewInSpaceNames.Take(4).ToArray()) + ".");
            var big = State.Articles.Where(a => a.Kind == "news" && a.Significance >= 50 && ut - a.PublishedUT > 10 * Day).OrderByDescending(a => a.Significance).FirstOrDefault();
            if (big != null) options.Add("A look back at \"" + big.Headline + "\" (" + Clock.FormatDate(big.PublishedUT) + ") and what has changed since.");
            options.Add("Speculation about what the agency will attempt next, based on its track record so far.");
            options.Add("Opinion: is the space program worth the money and risk? Weigh the record so far.");
            if (d != null && d.Satellites + d.Stations > 0) options.Add("How the agency's " + d.Satellites + " satellites and " + d.Stations + " stations affect daily life on Kerbin.");
            if (State.Profile.Mission != "") options.Add("Progress check against the agency's stated goals: " + State.Profile.Mission);
            if (outlet == "kraken")
            {
                var rumors = new List<string>
                {
                    "A wild rumor that the agency is secretly building something on the Mun, based on 'blurry photos' and an 'insider'.",
                    "A conspiracy theory that one of the agency's famous moments was staged, and 'the evidence they hope you missed'.",
                    "Sightings of the Kraken in orbit, and what the agency is 'hiding' about lost hardware.",
                    "A supposed feud between kerbonauts, sourced to 'a cafeteria insider'.",
                    "A 'leaked memo' about the agency's next secret mission.",
                };
                var star = Celebrity(n);
                if (star != null && n % 3 == 0)
                {
                    string[] gossip =
                    {
                        "Celebrity gossip about " + star.Name + ", " + star.Tier + ": a 'source close to the crew' on their love life, diet or feud with another kerbonaut.",
                        "Has fame gone to " + star.Name + "'s head? 'Insiders' claim diva demands at the space center.",
                        star.Name + " spotted somewhere they shouldn't be the night before training, according to a blurry photo.",
                    };
                    string g = gossip[(n / 3) % gossip.Length];
                    star.Scandals.Add(ResponseParser.Clip(g, 140));
                    if (star.Scandals.Count > 6) star.Scandals.RemoveAt(0);
                    return g;
                }
                if (State.Tracked.Values.Any(t => State.IsSilent(t.VesselId)))
                    rumors.Add("What the agency is hiding about the vessel it launched without telling anyone, with theories about where it's really going.");
                return rumors[n % rumors.Count];
            }
            if (outlet == "kscwatch") return options.FirstOrDefault(o => o.Contains("debris") || o.Contains("money")) ?? options[0];
            if (outlet == "munmonthly") return options.FirstOrDefault(o => o.Contains("Life aboard") || o.Contains("look back")) ?? options[n % options.Count];
            return options[n % options.Count];
        }

        // ------------------------------------------------------------------ context compression

        /// <summary>Returns true if it started (or finished) a compression step this tick.</summary>
        bool MaybeCompress(double ut, double realTime, bool allowCalls)
        {
            bool memory = Compressor.MemoryNeedsCompression(State, ut);
            bool profile = Compressor.ProfileNeedsSummary(State);
            if (!memory && !profile) return false;
            if (!Settings.CanCallAi)
            {
                if (memory) Compressor.CompressOffline(State, Compressor.CutPoint(State, ut), Clock);
                if (profile) { State.ProfileSummary = ""; State.ProfileSummaryHash = Compressor.ProfileHash(State.Profile); }
                return false; // offline work is instant; carry on with the tick
            }
            if (!allowCalls) return true;

            var preset = Settings.Preset;
            string system, user;
            double cut = Compressor.CutPoint(State, ut);
            string hash = Compressor.ProfileHash(State.Profile);
            if (profile) { system = Compressor.ProfileSystemPrompt(); user = Compressor.ProfileUserPrompt(State); }
            else { system = Compressor.MemorySystemPrompt(); user = Compressor.MemoryUserPrompt(State, cut, Clock); }
            var req = Providers.Build(preset, Settings.BaseUrl, Settings.Key, Settings.CheapModel, system, user, 2000, preset.JsonMode);
            req.TimeoutSeconds = Settings.TimeoutSeconds;
            compressing = true;
            Status = profile ? "Summarizing the agency profile" : "Compressing the news archive";
            double upTo = State.MemoryUpToUT;
            Transport.Send(req, res =>
            {
                compressing = false;
                // After a reload, apply only if the new state is still at the same point.
                if (profile ? Compressor.ProfileHash(State.Profile) != hash : State.MemoryUpToUT != upTo || Compressor.CutPoint(State, ut) < cut) { Status = "Idle"; return; }
                string text, err, summary = null;
                bool ok = Providers.TryExtractText(preset.Kind, res, out text, out err) && Compressor.TryReadSummary(text, out summary);
                if (profile)
                {
                    // On failure, mark it done anyway (raw profile is clipped) so we don't retry every tick.
                    State.ProfileSummary = ok ? summary : "";
                    State.ProfileSummaryHash = hash;
                }
                else if (ok) Compressor.ApplyMemory(State, cut, summary);
                else Compressor.CompressOffline(State, cut, Clock);
                if (!ok) LastError = "Compression: " + (err ?? "bad summary");
                Status = "Idle";
            });
            return true;
        }

        // ------------------------------------------------------------------ player tools

        /// <summary>
        /// Throw away a story's coverage and write it again with the current provider and model.
        /// Used to compare models. Mood is not applied twice.
        /// </summary>
        public bool Rewrite(string storyId, double ut)
        {
            var story = State.Stories.FirstOrDefault(s => s.Id == storyId);
            if (story == null || story == inFlight) return false;
            State.Articles.RemoveAll(a => a.StoryId == storyId);
            State.Social.RemoveAll(p => p.StoryId == storyId);
            story.Status = StoryStatus.Pending;
            story.Attempts = 0;
            story.PublishUT = Math.Min(story.PublishUT, ut);
            retryAfterReal = 0;
            return true;
        }

        /// <summary>Publish everything that is due right now (debug and "catch up" button).</summary>
        public int PendingCount { get { return State.Stories.Count(s => s.Status == StoryStatus.Pending); } }

        /// <summary>Testing aid: make every pending story due now instead of a day later.</summary>
        public int SkipDelay(double ut)
        {
            int n = 0;
            foreach (var s in State.Stories.Where(s => s.Status == StoryStatus.Pending))
            {
                // Move the story back far enough that every outlet's own delay has passed too.
                var plan = PlanFor(s);
                double maxDelay = plan.IsEmpty ? 1 : plan.Outlets.Max(o => o.DelayDays);
                double shift = s.CreatedUT + (maxDelay + 0.11) * Day - ut;
                if (shift <= 0) continue;
                s.CreatedUT -= shift;
                s.PublishUT = ut;
                foreach (var e in s.Events) e.UT -= shift;
                n++;
            }
            return n;
        }

        /// <summary>Sends a tiny request to check the key and model. Calls back with a readable result.</summary>
        public void TestConnection(Action<bool, string> done)
        {
            var preset = Settings.Preset;
            if (preset.NeedsKey && Settings.Key == "") { done(false, "No API key entered for " + preset.Name + "."); return; }
            var req = Providers.Build(preset, Settings.BaseUrl, Settings.Key, Settings.CheapModel,
                "You are a connectivity check. Reply with JSON only.", "Reply with this JSON object exactly: {\"ok\":true,\"headline\":\"Kerbin Daily Herald online\"}", 200, preset.JsonMode);
            req.TimeoutSeconds = Math.Min(60, Settings.TimeoutSeconds);
            Transport.Send(req, res =>
            {
                string text, err;
                if (!Providers.TryExtractText(preset.Kind, res, out text, out err)) { done(false, err); return; }
                object root;
                var json = ResponseParser.ExtractJson(text);
                bool ok = json != null && Json.TryParse(json, out root) && Json.Get(root, "ok") is bool;
                done(true, ok ? "Connected to " + preset.Name + " (" + Settings.CheapModel + ")." : "Connected, but the reply was not clean JSON: " + text.Substring(0, Math.Min(120, text.Length)));
            });
        }

        /// <summary>Asks the provider which models this key can use, and remembers the list.</summary>
        public void FetchModels(Action<bool, string> done)
        {
            var preset = Settings.Preset;
            if (preset.NeedsKey && Settings.Key == "") { done(false, "Enter an API key first."); return; }
            var req = Providers.BuildListModels(preset, Settings.BaseUrl, Settings.Key);
            string id = preset.Id;
            Transport.Send(req, res =>
            {
                if (!res.Ok) { done(false, res.Error ?? ("HTTP " + res.Status + " listing models")); return; }
                var models = Providers.ParseModelList(preset.Kind, res.Body);
                if (models.Count == 0) { done(false, "The provider returned no models."); return; }
                Settings.FetchedModels[id] = string.Join(" ", models.ToArray());
                done(true, models.Count + " models available from " + preset.Name + ".");
            });
        }

        public void MarkAllRead(double ut)
        {
            foreach (var a in State.Articles) if (a.PublishedUT <= ut) a.Read = true;
        }

        public int UnreadCount(double ut)
        {
            return State.Articles.Count(a => !a.Read && a.PublishedUT <= ut);
        }
    }
}

namespace KerbalNews
{
    public partial class NewsEngine
    {
        /// <summary>Set by the KSP layer: turns a stock localization tag ("#autoLOC_501232") into its name ("Kerbal X").</summary>
        public Func<string, string> LocalizeTag = t => t;
        static readonly System.Text.RegularExpressions.Regex LocTag = new System.Text.RegularExpressions.Regex(@"#autoLOC_\d+");

        string Heal(string text) { return string.IsNullOrEmpty(text) || text.IndexOf("#autoLOC_", StringComparison.Ordinal) < 0 ? text : LocTag.Replace(text, m => LocalizeTag(m.Value)); }
        void HealAll(List<Comment> cs) { if (cs != null) foreach (var c in cs) c.Text = Heal(c.Text); }

        /// <summary>Older saves stored stock craft names as raw localization tags, which then showed up in posts and headlines.
        /// Replaces them everywhere with the real name. Returns how many texts changed.</summary>
        public int HealNames()
        {
            int before = Count();
            foreach (var a in State.Articles.Concat(State.Archive)) { a.Headline = Heal(a.Headline); a.Subhead = Heal(a.Subhead); a.Body = Heal(a.Body); HealAll(a.Comments); }
            foreach (var p in State.Social) p.Text = Heal(p.Text);
            foreach (var t in State.Forum) { t.Title = Heal(t.Title); t.Body = Heal(t.Body); HealAll(t.Comments); }
            foreach (var g in State.Gallery) { g.VesselName = Heal(g.VesselName); g.Caption = Heal(g.Caption); HealAll(g.Comments); }
            foreach (var s in State.Stories) { s.Topic = Heal(s.Topic); foreach (var e in s.Events) { e.VesselName = Heal(e.VesselName); e.Detail = Heal(e.Detail); } }
            foreach (var tv in State.Tracked.Values) { if (tv.Craft != null) tv.Craft.VesselName = Heal(tv.Craft.VesselName); for (int i = 0; i < tv.History.Count; i++) tv.History[i] = Heal(tv.History[i]); }
            foreach (var inc in State.Incidents) inc.VesselName = Heal(inc.VesselName);
            return before - Count();
        }

        int Count()
        {
            int n = 0;
            Func<string, int> c = x => x != null && x.IndexOf("#autoLOC_", StringComparison.Ordinal) >= 0 ? 1 : 0;
            foreach (var a in State.Articles.Concat(State.Archive)) n += c(a.Headline) + c(a.Body) + a.Comments.Sum(x => c(x.Text));
            foreach (var p in State.Social) n += c(p.Text);
            foreach (var t in State.Forum) n += c(t.Title) + c(t.Body) + t.Comments.Sum(x => c(x.Text));
            foreach (var s in State.Stories) n += s.Events.Sum(e => c(e.VesselName));
            return n;
        }
    }
}
