using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    public static class EventTypes
    {
        public const string Launch = "launch";
        public const string Orbit = "orbit";
        public const string Soi = "soi";
        public const string Landing = "landing";
        public const string Splashdown = "splashdown";
        public const string Docking = "docking";
        public const string Failure = "failure";
        public const string CrewKilled = "crewKilled";
        public const string Recovery = "recovery";
        public const string Milestone = "milestone";
        public const string ContractDone = "contractDone";
        public const string ContractFailed = "contractFailed";
        public const string ContractAccepted = "contractAccepted";
        public const string PressRelease = "pressRelease";
        public const string Livestream = "livestream";
    }

    /// <summary>One thing that happened in the save, captured the moment it happened.</summary>
    public class NewsEvent
    {
        public string Type;
        public double UT;
        public string VesselId = "";
        public string VesselName = "";
        public string Body = "";
        public string Situation = "";
        public string Detail = "";
        public List<string> Crew = new List<string>();
        public bool Crewed { get { return Crew.Count > 0; } }
        /// <summary>0-100, set by the significance model when the event is ingested.</summary>
        public double Significance;

        public void Save(DataNode n)
        {
            n.Add("type", Type);
            n.Add("ut", UT);
            n.Add("vesselId", VesselId);
            n.AddText("vesselName", VesselName);
            n.Add("body", Body);
            n.Add("situation", Situation);
            n.AddText("detail", Detail);
            foreach (var c in Crew) n.AddText("crew", c);
            n.Add("significance", Significance);
        }

        public static NewsEvent Load(DataNode n)
        {
            var e = new NewsEvent
            {
                Type = n.Get("type", ""),
                UT = n.GetDouble("ut"),
                VesselId = n.Get("vesselId", ""),
                VesselName = n.GetText("vesselName"),
                Body = n.Get("body", ""),
                Situation = n.Get("situation", ""),
                Detail = n.GetText("detail"),
                Significance = n.GetDouble("significance"),
            };
            foreach (var c in n.GetAll("crew")) e.Crew.Add(TextCodec.Decode(c));
            return e;
        }
    }

    /// <summary>What the public could see of a craft: specs captured at launch.</summary>
    public class CraftSnapshot
    {
        public string VesselName = "";
        public string VesselType = "";
        public int Parts;
        public double MassTons;
        public double HeightM;
        public int Stages;
        public double DeltaV;
        public int CrewCapacity;
        public string Engines = "";
        public string LaunchSite = "";
        public string Description = "";
        public List<string> Crew = new List<string>();

        public void Save(DataNode n)
        {
            n.AddText("name", VesselName);
            n.Add("type", VesselType);
            n.Add("parts", Parts);
            n.Add("mass", MassTons);
            n.Add("height", HeightM);
            n.Add("stages", Stages);
            n.Add("dv", DeltaV);
            n.Add("crewCap", CrewCapacity);
            n.AddText("engines", Engines);
            n.AddText("site", LaunchSite);
            n.AddText("desc", Description);
            foreach (var c in Crew) n.AddText("crew", c);
        }

        public static CraftSnapshot Load(DataNode n)
        {
            if (n == null) return null;
            var s = new CraftSnapshot
            {
                VesselName = n.GetText("name"),
                VesselType = n.Get("type", ""),
                Parts = n.GetInt("parts"),
                MassTons = n.GetDouble("mass"),
                HeightM = n.GetDouble("height"),
                Stages = n.GetInt("stages"),
                DeltaV = n.GetDouble("dv"),
                CrewCapacity = n.GetInt("crewCap"),
                Engines = n.GetText("engines"),
                LaunchSite = n.GetText("site"),
                Description = n.GetText("desc"),
            };
            foreach (var c in n.GetAll("crew")) s.Crew.Add(TextCodec.Decode(c));
            return s;
        }
    }

    public static class StoryStatus
    {
        public const string Pending = "pending";
        public const string Published = "published";
    }

    /// <summary>
    /// A group of related events (usually one mission) that becomes one round of coverage.
    /// </summary>
    public class Story
    {
        public string Id;
        public string Kind = "mission"; // mission, milestone, contract, filler
        public string VesselId = "";
        public string Topic = "";       // for filler: what to write about
        /// <summary>A special feature: "retrospective" (an anniversary) or "review" (year in review). Empty for normal stories.</summary>
        public string Special = "";
        public string ReleaseId = "";   // for release: the press release it answers
        public string FillerOutlet = "";
        /// <summary>For a tip (Kind "tip"): extra outlets beyond FillerOutlet, the article kind, and a fallback headline and body for offline mode.</summary>
        public string TipKind = "";
        public string TipHeadline = "";
        public string TipBody = "";
        public string DealId = "";
        public double CreatedUT;
        public double PublishUT;
        public string Status = StoryStatus.Pending;
        public int Attempts;
        public bool MoodApplied;
        /// <summary>Telemetry was kept silent: the press gets no specs, only what is visible.</summary>
        public bool Silent;
        /// <summary>Description of the press photo to generate (not saved; a reload simply skips the photo).</summary>
        public string ImagePrompt = "";
        public double Significance;
        public CraftSnapshot Craft;
        public List<NewsEvent> Events = new List<NewsEvent>();

        public bool HasEvent(string type) { return Events.Any(e => e.Type == type); }
        public string Title
        {
            get
            {
                if (Kind == "filler") return "Feature: " + Topic;
                if (Kind == "tip") return "Tip: " + (TipHeadline != "" ? TipHeadline : Topic);
                if (Kind == "release") return "Press release: " + Topic;
                var name = Craft != null && Craft.VesselName != "" ? Craft.VesselName : (Events.Count > 0 ? Events[0].VesselName : "");
                return (name == "" ? Kind : name) + " (" + string.Join(", ", Events.Select(e => e.Type).Distinct().ToArray()) + ")";
            }
        }

        public void Save(DataNode n)
        {
            n.Add("id", Id);
            n.Add("kind", Kind);
            n.Add("vesselId", VesselId);
            n.AddText("topic", Topic);
            if (ReleaseId != "") n.Add("release", ReleaseId);
            n.Add("fillerOutlet", FillerOutlet);
            if (Special != "") n.Add("special", Special);
            if (Kind == "tip") { n.Add("tipKind", TipKind); n.AddText("tipHeadline", TipHeadline); n.AddText("tipBody", TipBody); n.Add("deal", DealId); }
            n.Add("created", CreatedUT);
            n.Add("publish", PublishUT);
            n.Add("status", Status);
            n.Add("attempts", Attempts);
            n.Add("moodApplied", MoodApplied);
            n.Add("silent", Silent);
            n.Add("significance", Significance);
            if (Craft != null) Craft.Save(n.AddNode("CRAFT"));
            foreach (var e in Events) e.Save(n.AddNode("EVENT"));
        }

        public static Story Load(DataNode n)
        {
            var s = new Story
            {
                Id = n.Get("id", ""),
                Kind = n.Get("kind", "mission"),
                VesselId = n.Get("vesselId", ""),
                Topic = n.GetText("topic"),
                ReleaseId = n.Get("release", ""),
                FillerOutlet = n.Get("fillerOutlet", ""),
                Special = n.Get("special", ""),
                TipKind = n.Get("tipKind", ""),
                TipHeadline = n.GetText("tipHeadline"),
                TipBody = n.GetText("tipBody"),
                DealId = n.Get("deal", ""),
                CreatedUT = n.GetDouble("created"),
                PublishUT = n.GetDouble("publish"),
                Status = n.Get("status", StoryStatus.Pending),
                Attempts = n.GetInt("attempts"),
                MoodApplied = n.GetBool("moodApplied"),
                Silent = n.GetBool("silent"),
                Significance = n.GetDouble("significance"),
                Craft = CraftSnapshot.Load(n.GetNode("CRAFT")),
            };
            foreach (var en in n.GetNodes("EVENT")) s.Events.Add(NewsEvent.Load(en));
            return s;
        }
    }

    public class Comment
    {
        public string Id = "";
        public string Author = "";
        public string Text = "";
        public int Likes;
        /// <summary>Id of the comment this answers, or "" for a top-level comment.</summary>
        public string ReplyTo = "";
        public double UT;
        /// <summary>Written by the player, either as the agency's official account or under an anonymous handle.</summary>
        public bool Player, Agency;
        /// <summary>The player's own vote: -1, 0 or 1.</summary>
        public int Vote;

        public void Save(DataNode n)
        {
            n.AddText("author", Author); n.AddText("text", Text); n.Add("likes", Likes);
            if (Id != "") n.Add("id", Id);
            if (ReplyTo != "") n.Add("replyTo", ReplyTo);
            if (UT != 0) n.Add("ut", UT);
            if (Player) n.Add("player", true);
            if (Agency) n.Add("agency", true);
            if (Vote != 0) n.Add("vote", Vote);
        }

        public static Comment Load(DataNode n)
        {
            return new Comment
            {
                Author = n.GetText("author"), Text = n.GetText("text"), Likes = n.GetInt("likes"),
                Id = n.Get("id", ""), ReplyTo = n.Get("replyTo", ""), UT = n.GetDouble("ut"),
                Player = n.GetBool("player"), Agency = n.GetBool("agency"), Vote = n.GetInt("vote"),
            };
        }

        /// <summary>Orders a flat list into threads (parents first, replies under them) with their depth.</summary>
        public static List<KeyValuePair<Comment, int>> Threaded(List<Comment> list)
        {
            var result = new List<KeyValuePair<Comment, int>>();
            var ids = new HashSet<string>(list.Where(c => c.Id != "").Select(c => c.Id));
            var done = new HashSet<Comment>();
            Action<Comment, int> add = null;
            add = (c, d) =>
            {
                if (!done.Add(c)) return;
                result.Add(new KeyValuePair<Comment, int>(c, d));
                if (c.Id == "") return;
                foreach (var r in list.Where(x => x.ReplyTo == c.Id)) add(r, Math.Min(d + 1, 4));
            };
            foreach (var c in list.Where(c => c.ReplyTo == "" || !ids.Contains(c.ReplyTo))) add(c, 0);
            foreach (var c in list) add(c, 0);
            return result;
        }
    }

    /// <summary>A thread on Snackboard, the in-universe community forum.</summary>
    public class ForumThread
    {
        public string Id = "";
        public string Board = "general";
        public string Title = "";
        public string Body = "";
        public string Author = "";
        public string StoryId = "";
        public string Kind = "post";    // post, ama
        public double UT;
        public int Score;
        public bool Player, Agency;
        public int Vote;
        /// <summary>A gallery file shown with the post (a spotter's photo), or "".</summary>
        public string Image = "";
        /// <summary>A town hall whose two days are over.</summary>
        public bool Closed;
        public List<Comment> Comments = new List<Comment>();

        public void Save(DataNode n)
        {
            if (Image != "") n.Add("image", Image);
            if (Closed) n.Add("closed", true);
            n.Add("id", Id); n.Add("board", Board); n.AddText("title", Title); n.AddText("body", Body); n.AddText("author", Author);
            n.Add("story", StoryId); n.Add("kind", Kind); n.Add("ut", UT); n.Add("score", Score);
            n.Add("player", Player); n.Add("agency", Agency); n.Add("vote", Vote);
            foreach (var c in Comments) c.Save(n.AddNode("COMMENT"));
        }

        public static ForumThread Load(DataNode n)
        {
            var t = new ForumThread
            {
                Id = n.Get("id", ""), Board = n.Get("board", "general"), Title = n.GetText("title"), Body = n.GetText("body"), Author = n.GetText("author"),
                StoryId = n.Get("story", ""), Kind = n.Get("kind", "post"), UT = n.GetDouble("ut"), Score = n.GetInt("score"),
                Player = n.GetBool("player"), Agency = n.GetBool("agency"), Vote = n.GetInt("vote"), Image = n.Get("image", ""), Closed = n.GetBool("closed"),
            };
            foreach (var cn in n.GetNodes("COMMENT")) t.Comments.Add(Comment.Load(cn));
            return t;
        }
    }

    /// <summary>An official statement the player writes in the press room. Outlets react the next day.</summary>
    public class PressRelease
    {
        public string Id = "";
        public string Type = "announcement";   // announcement, update, incident, teaser
        public string Title = "";
        public string Body = "";
        public string VesselId = "";
        public string VesselName = "";
        public List<string> Shots = new List<string>();
        public double UT;
        public string StoryId = "";

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.Add("type", Type); n.AddText("title", Title); n.AddText("body", Body); n.Add("vessel", VesselId); n.AddText("vesselName", VesselName);
            n.Add("ut", UT); n.Add("story", StoryId);
            foreach (var x in Shots) n.Add("shot", x);
        }

        public static PressRelease Load(DataNode n)
        {
            var r = new PressRelease { Id = n.Get("id", ""), Type = n.Get("type", "announcement"), Title = n.GetText("title"), Body = n.GetText("body"), VesselId = n.Get("vessel", ""), VesselName = n.GetText("vesselName"), UT = n.GetDouble("ut"), StoryId = n.Get("story", "") };
            foreach (var x in n.GetAll("shot")) r.Shots.Add(x);
            return r;
        }
    }

    /// <summary>A screenshot in the player's gallery. The picture lives in the save folder; this is its record.</summary>
    public class GalleryShot
    {
        public string Id = "";
        public string File = "";
        public double UT;
        public string VesselId = "", VesselName = "", Body = "", Situation = "";
        public string Caption = "";
        public bool Public;
        public double ReleasedUT;
        /// <summary>"" for the agency's own pictures, "press" for long-lens shots by outside photographers.</summary>
        public string Source = "";
        /// <summary>For press shots: pad, liftoff, ascent, explosion or recovery.</summary>
        public string Kind = "";
        public string Credit = "";
        public bool IsPress { get { return Source == "press"; } }
        public List<Comment> Comments = new List<Comment>();

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.Add("file", File); n.Add("ut", UT); n.Add("vessel", VesselId); n.AddText("vesselName", VesselName); n.Add("body", Body); n.Add("situation", Situation);
            n.AddText("caption", Caption); n.Add("public", Public); n.Add("released", ReleasedUT);
            if (Source != "") { n.Add("source", Source); n.Add("kind", Kind); n.AddText("credit", Credit); }
            foreach (var c in Comments) c.Save(n.AddNode("COMMENT"));
        }

        public static GalleryShot Load(DataNode n)
        {
            var g = new GalleryShot { Id = n.Get("id", ""), File = n.Get("file", ""), UT = n.GetDouble("ut"), VesselId = n.Get("vessel", ""), VesselName = n.GetText("vesselName"), Body = n.Get("body", ""), Situation = n.Get("situation", ""), Caption = n.GetText("caption"), Public = n.GetBool("public"), ReleasedUT = n.GetDouble("released"), Source = n.Get("source", ""), Kind = n.Get("kind", ""), Credit = n.GetText("credit") };
            foreach (var cn in n.GetNodes("COMMENT")) g.Comments.Add(Comment.Load(cn));
            return g;
        }
    }

    /// <summary>A pending request for the community to react to something the player posted. Saved, so a reload keeps it.</summary>
    public class CommunityJob
    {
        public string Target = "";     // "a:<articleId>" or "t:<threadId>"
        public string CommentId = "";  // the player's comment, or "" for a thread's opening post
        public string Mode = "react";  // react, ama
        public int Count = 3;
        public int Attempts;
        public double UT;

        public void Save(DataNode n) { n.Add("target", Target); n.Add("comment", CommentId); n.Add("mode", Mode); n.Add("count", Count); n.Add("attempts", Attempts); n.Add("ut", UT); }
        public static CommunityJob Load(DataNode n)
        {
            return new CommunityJob { Target = n.Get("target", ""), CommentId = n.Get("comment", ""), Mode = n.Get("mode", "react"), Count = n.GetInt("count", 3), Attempts = n.GetInt("attempts"), UT = n.GetDouble("ut") };
        }
    }

    public class Article
    {
        public string Id;
        public string StoryId = "";
        public string Outlet = "";
        public string Kind = "news"; // news, analysis, opinion, feature, expert, statement
        public string Headline = "";
        public string Subhead = "";
        public string Body = "";
        public string Reporter = "";
        public List<string> Tags = new List<string>();
        public double PublishedUT;
        public double Significance;
        public bool Read;
        public bool Offline;
        public string Model = "";
        /// <summary>File name of the generated press photo under PluginData/Images, or "".</summary>
        public string Image = "";
        /// <summary>Taken down by the outlet (paid off, or retracted after a legal threat). The page says so; lists skip it.</summary>
        public bool Removed;
        public string RemovedNote = "";
        /// <summary>A stub in the long-term archive: only the opening lines were kept.</summary>
        public bool Archived;
        public List<Comment> Comments = new List<Comment>();

        public void Save(DataNode n)
        {
            n.Add("id", Id);
            n.Add("story", StoryId);
            n.Add("outlet", Outlet);
            n.Add("kind", Kind);
            n.AddText("headline", Headline);
            n.AddText("subhead", Subhead);
            n.AddText("body", Body);
            n.AddText("reporter", Reporter);
            n.AddText("tags", string.Join(",", Tags.ToArray()));
            n.Add("ut", PublishedUT);
            n.Add("significance", Significance);
            n.Add("read", Read);
            n.Add("offline", Offline);
            n.AddText("model", Model);
            if (Image != "") n.Add("image", Image);
            if (Removed) { n.Add("removed", true); n.AddText("removedNote", RemovedNote); }
            if (Archived) n.Add("archived", true);
            foreach (var c in Comments) c.Save(n.AddNode("COMMENT"));
        }

        public static Article Load(DataNode n)
        {
            var a = new Article
            {
                Id = n.Get("id", ""),
                StoryId = n.Get("story", ""),
                Outlet = n.Get("outlet", ""),
                Kind = n.Get("kind", "news"),
                Headline = n.GetText("headline"),
                Subhead = n.GetText("subhead"),
                Body = n.GetText("body"),
                Reporter = n.GetText("reporter"),
                PublishedUT = n.GetDouble("ut"),
                Significance = n.GetDouble("significance"),
                Read = n.GetBool("read"),
                Offline = n.GetBool("offline"),
                Model = n.GetText("model"),
                Image = n.Get("image", ""),
                Removed = n.GetBool("removed"),
                Archived = n.GetBool("archived"),
                RemovedNote = n.GetText("removedNote"),
            };
            foreach (var t in n.GetText("tags").Split(',')) if (t.Trim() != "") a.Tags.Add(t.Trim());
            foreach (var cn in n.GetNodes("COMMENT")) a.Comments.Add(Comment.Load(cn));
            return a;
        }
    }

    /// <summary>A post on Kerbnet, the in-universe social network.</summary>
    public class SocialPost
    {
        public string StoryId = "";
        public string Handle = "";
        public string Persona = ""; // enthusiast, skeptic, scientist, ...
        public string Text = "";
        public double UT;
        public int Likes;
        public int Reshares;

        public void Save(DataNode n)
        {
            n.Add("story", StoryId);
            n.AddText("handle", Handle);
            n.Add("persona", Persona);
            n.AddText("text", Text);
            n.Add("ut", UT);
            n.Add("likes", Likes);
            n.Add("reshares", Reshares);
        }

        public static SocialPost Load(DataNode n)
        {
            return new SocialPost
            {
                StoryId = n.Get("story", ""),
                Handle = n.GetText("handle"),
                Persona = n.Get("persona", ""),
                Text = n.GetText("text"),
                UT = n.GetDouble("ut"),
                Likes = n.GetInt("likes"),
                Reshares = n.GetInt("reshares"),
            };
        }
    }

    public class AgencyProfile
    {
        public string Name = "Kerbal Space Center";
        public string ShortName = "KSC";
        public string Founding = "";
        public string Mission = "";
        public string Politics = "";
        public string Reputation = "";
        public string KeyPeople = "Gene Kerman (Flight Director), Wernher von Kerman (Chief Engineer), Mortimer Kerman (Finance), Walt Kerman (Public Relations)";
        public string WorldLore = "";
        public string HouseRules = "";

        public void Save(DataNode n)
        {
            n.AddText("name", Name);
            n.AddText("short", ShortName);
            n.AddText("founding", Founding);
            n.AddText("mission", Mission);
            n.AddText("politics", Politics);
            n.AddText("reputation", Reputation);
            n.AddText("people", KeyPeople);
            n.AddText("world", WorldLore);
            n.AddText("rules", HouseRules);
        }

        public static AgencyProfile Load(DataNode n)
        {
            var p = new AgencyProfile();
            if (n == null) return p;
            p.Name = n.GetText("name", p.Name);
            p.ShortName = n.GetText("short", p.ShortName);
            p.Founding = n.GetText("founding");
            p.Mission = n.GetText("mission");
            p.Politics = n.GetText("politics");
            p.Reputation = n.GetText("reputation");
            p.KeyPeople = n.GetText("people", p.KeyPeople);
            p.WorldLore = n.GetText("world");
            p.HouseRules = n.GetText("rules");
            return p;
        }
    }

    public class LoreEntry
    {
        public double UT;
        public string Text = "";
    }

    /// <summary>A sample of the visible state of the universe.</summary>
    public class WorldDigest
    {
        public double UT;
        public int Satellites;   // probes and relays in orbit
        public int Stations;
        public int Bases;
        public int CrewedVessels;
        public int Debris;
        public int CrewInSpace;
        public int CrewTotal;
        public int CrewLost;
        public Dictionary<string, int> DebrisByBody = new Dictionary<string, int>();
        public Dictionary<string, int> VesselsByBody = new Dictionary<string, int>();
        public List<string> CrewInSpaceNames = new List<string>();
        public List<string> Records = new List<string>();
        public string Career = ""; // funds/reputation summary, empty in sandbox

        public void Save(DataNode n)
        {
            n.Add("ut", UT);
            n.Add("sats", Satellites);
            n.Add("stations", Stations);
            n.Add("bases", Bases);
            n.Add("crewedVessels", CrewedVessels);
            n.Add("debris", Debris);
            n.Add("crewInSpace", CrewInSpace);
            n.Add("crewTotal", CrewTotal);
            n.Add("crewLost", CrewLost);
        }

        public static WorldDigest Load(DataNode n)
        {
            return new WorldDigest
            {
                UT = n.GetDouble("ut"),
                Satellites = n.GetInt("sats"),
                Stations = n.GetInt("stations"),
                Bases = n.GetInt("bases"),
                CrewedVessels = n.GetInt("crewedVessels"),
                Debris = n.GetInt("debris"),
                CrewInSpace = n.GetInt("crewInSpace"),
                CrewTotal = n.GetInt("crewTotal"),
                CrewLost = n.GetInt("crewLost"),
            };
        }
    }

    /// <summary>A vessel whose launch the press covered, so later events can be tied back to it.</summary>
    public class TrackedVessel
    {
        public string VesselId;
        public double LaunchUT;
        public CraftSnapshot Craft;
        public List<string> History = new List<string>(); // short public memory, e.g. "reached orbit"
        /// <summary>Dedup flags such as "orbit:Mun" so bouncing landings are reported once.</summary>
        public List<string> Flags = new List<string>();
    }

    /// <summary>A named reporter's relationship with the agency: positive is goodwill, negative is a grudge.</summary>
    public class ReporterTie
    {
        public string Outlet = "", Name = "";
        public int Score;
        public List<string> Notes = new List<string>();

        public string Feeling
        {
            get { return Score >= 5 ? "a close, friendly contact" : Score >= 2 ? "warm toward the agency" : Score <= -5 ? "holds a serious grudge" : Score <= -2 ? "cool and suspicious" : "neutral"; }
        }

        public void Save(DataNode n)
        {
            n.Add("outlet", Outlet); n.AddText("name", Name); n.Add("score", Score);
            foreach (var x in Notes) n.AddText("note", x);
        }

        public static ReporterTie Load(DataNode n)
        {
            var t = new ReporterTie { Outlet = n.Get("outlet", ""), Name = n.GetText("name"), Score = n.GetInt("score") };
            foreach (var x in n.GetAll("note")) t.Notes.Add(TextCodec.Decode(x));
            return t;
        }
    }

    /// <summary>The whole news world. Lives only inside the save file.</summary>
    public class NewsState
    {
        public int NextId = 1;
        public List<Story> Stories = new List<Story>();
        public List<Article> Articles = new List<Article>();
        public List<SocialPost> Social = new List<SocialPost>();
        public Mood Mood = new Mood();
        public AgencyProfile Profile = new AgencyProfile();
        public List<LoreEntry> Lore = new List<LoreEntry>();
        public List<WorldDigest> DigestHistory = new List<WorldDigest>();
        public Dictionary<string, double> Novelty = new Dictionary<string, double>();
        public Dictionary<string, TrackedVessel> Tracked = new Dictionary<string, TrackedVessel>();
        public Dictionary<string, List<string>> Reporters = new Dictionary<string, List<string>>();
        public double LastStoryUT = -1;
        public double LastFillerUT = -1;
        public double LastMoodDecayUT = -1;
        public int Silliness;
        public bool RumorsEnabled = true;
        /// <summary>Vessels whose telemetry the agency keeps silent (persistentId). Saved per save, so it rolls back too.</summary>
        public HashSet<string> SilentVessels = new HashSet<string>();
        /// <summary>New launches start silent instead of sharing telemetry.</summary>
        public bool SilentByDefault;
        /// <summary>Vessels explicitly set to share even when SilentByDefault is on.</summary>
        public HashSet<string> SharedVessels = new HashSet<string>();

        public bool IsSilent(string vesselId)
        {
            if (string.IsNullOrEmpty(vesselId)) return false;
            if (SilentVessels.Contains(vesselId)) return true;
            return SilentByDefault && !SharedVessels.Contains(vesselId);
        }

        public void SetSilent(string vesselId, bool silent)
        {
            if (string.IsNullOrEmpty(vesselId)) return;
            if (silent) { SilentVessels.Add(vesselId); SharedVessels.Remove(vesselId); }
            else { SilentVessels.Remove(vesselId); SharedVessels.Add(vesselId); }
        }
        /// <summary>Running archive summary of older news and lore (context compression).</summary>
        public List<string> Records = new List<string>();
        public string MemorySummary = "";
        public double MemoryUpToUT = -1;
        public string ProfileSummary = "";
        public string ProfileSummaryHash = "";

        public List<ForumThread> Forum = new List<ForumThread>();
        public List<PressRelease> Releases = new List<PressRelease>();
        public List<GalleryShot> Gallery = new List<GalleryShot>();
        /// <summary>Finished livestreams: highlights for the archive and the press.</summary>
        public List<StreamArchive> Streams = new List<StreamArchive>();
        public List<Poll> Polls = new List<Poll>();
        public List<CommunityJob> CommunityJobs = new List<CommunityJob>();
        /// <summary>The personal assistant's chat, and the paper trail of everything it did.</summary>
        public List<AssistantMessage> AssistantChat = new List<AssistantMessage>();
        public List<Deal> Deals = new List<Deal>();
        /// <summary>Standing orders the director gave the assistant; it follows them in every request.</summary>
        public List<LoreEntry> Orders = new List<LoreEntry>();
        /// <summary>Fame of every kerbal who has flown, by name.</summary>
        public Dictionary<string, CrewFame> Crew = new Dictionary<string, CrewFame>();
        /// <summary>Science results that reached Kerbin, for the journal.</summary>
        public List<ScienceResult> Science = new List<ScienceResult>();
        /// <summary>Kerbals booked for interviews and briefings.</summary>
        public List<CrewAppearance> Appearances = new List<CrewAppearance>();
        /// <summary>Anniversaries already marked ("storyId:years"), the last year that got a review, and the last daily check.</summary>
        public HashSet<string> Anniversaries = new HashSet<string>();
        public int LastReviewYear = -1;
        public double LastAnniversaryCheckUT = -1;
        /// <summary>How named reporters feel about the agency after its dealings with them.</summary>
        public List<ReporterTie> Ties = new List<ReporterTie>();
        /// <summary>A PR firm the assistant hired, and until when.</summary>
        public string PrFirm = "";
        public double PrFirmUntilUT = -1;
        /// <summary>The handle the player last used to post anonymously.</summary>
        public string AnonHandle = "";

        public string NewId(string prefix) { return prefix + (NextId++); }

        public IEnumerable<Article> PublishedArticles(double ut)
        {
            return Articles.Where(a => a.PublishedUT <= ut && !a.Removed).OrderByDescending(a => a.PublishedUT);
        }

        public const int MaxArticles = 600;
        public const int MaxSocial = 400;
        public const int MaxCommentsPerArticle = 20;
        public const int MaxCommentsKept = 80;
        public const int MaxForumThreads = 250;
        public const int MaxDigestHistory = 200;

        /// <summary>Keeps the save from growing without bound.</summary>
        public void Prune()
        {
            foreach (var a in Articles)
                if (a.Comments.Count > MaxCommentsKept) a.Comments.RemoveRange(0, a.Comments.Count - MaxCommentsKept);
            if (Forum.Count > MaxForumThreads)
            {
                var keepT = new HashSet<ForumThread>(Forum.Where(t => t.Player).Concat(Forum.OrderByDescending(t => t.UT).Take(MaxForumThreads / 2)));
                Forum = Forum.Where(t => keepT.Contains(t)).Concat(Forum.Where(t => !keepT.Contains(t)).OrderByDescending(t => t.Score).Take(Math.Max(0, MaxForumThreads - keepT.Count))).ToList();
            }
            if (Articles.Count > MaxArticles)
            {
                // Drop the least significant old articles first, never the most recent 100.
                var keep = new HashSet<Article>(Articles.OrderByDescending(a => a.PublishedUT).Take(100));
                var removable = Articles.Where(a => !keep.Contains(a)).OrderBy(a => a.Significance).ThenBy(a => a.PublishedUT).Take(Articles.Count - MaxArticles).ToList();
                foreach (var a in removable) { Articles.Remove(a); Archive.Add(ToStub(a)); }
                if (Archive.Count > MaxArchive) Archive.RemoveRange(0, Archive.Count - MaxArchive);
            }
            if (Social.Count > MaxSocial) Social = Social.OrderByDescending(p => p.UT).Take(MaxSocial).ToList();
            if (DigestHistory.Count > MaxDigestHistory) DigestHistory.RemoveRange(0, DigestHistory.Count - MaxDigestHistory);
            // Published stories only need to stay while articles reference them (for "related" and memory).
            var referenced = new HashSet<string>(Articles.Select(a => a.StoryId));
            Stories.RemoveAll(s => s.Status == StoryStatus.Published && !referenced.Contains(s.Id));
        }

        /// <summary>Articles dropped from the full archive, kept small (opening lines, no comments) so search still finds every story.</summary>
        public List<Article> Archive = new List<Article>();
        public const int MaxArchive = 4000;

        Article ToStub(Article a)
        {
            var st = Stories.FirstOrDefault(x => x.Id == a.StoryId);
            if (st != null)
                foreach (var t in st.Events.Select(e => e.VesselName).Concat(st.Events.SelectMany(e => e.Crew)).Where(t => t != "").Distinct().Take(6))
                    if (!a.Tags.Contains(t.ToLowerInvariant())) a.Tags.Add(t.ToLowerInvariant());
            string first = a.Body.Split(new[] { "\n\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).FirstOrDefault(x => x != "") ?? "";
            a.Body = first.Length > 320 ? first.Substring(0, 320) + "..." : first;
            a.Comments.Clear();
            a.Image = "";
            a.Archived = true;
            return a;
        }

        public void Save(DataNode n)
        {
            Prune();
            n.Add("version", 1);
            n.Add("nextId", NextId);
            n.Add("lastStory", LastStoryUT);
            n.Add("lastFiller", LastFillerUT);
            n.Add("lastMoodDecay", LastMoodDecayUT);
            n.Add("silliness", Silliness);
            n.Add("rumors", RumorsEnabled);
            n.Add("silentDefault", SilentByDefault);
            foreach (var v in SilentVessels) n.Add("silentVessel", v);
            foreach (var v in SharedVessels) n.Add("sharedVessel", v);
            n.AddText("memory", MemorySummary);
            foreach (var r in Records) n.AddText("record", r);
            n.Add("memoryUpTo", MemoryUpToUT);
            n.AddText("profileSummary", ProfileSummary);
            n.Add("profileHash", ProfileSummaryHash);
            Mood.Save(n.AddNode("MOOD"));
            Profile.Save(n.AddNode("PROFILE"));
            var lore = n.AddNode("LORE");
            foreach (var l in Lore) { var ln = lore.AddNode("ENTRY"); ln.Add("ut", l.UT); ln.AddText("text", l.Text); }
            var nov = n.AddNode("NOVELTY");
            foreach (var kv in Novelty) nov.AddText("k", kv.Key + "|" + kv.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            var tr = n.AddNode("TRACKED");
            foreach (var t in Tracked.Values)
            {
                var tn = tr.AddNode("VESSEL");
                tn.Add("id", t.VesselId);
                tn.Add("launch", t.LaunchUT);
                foreach (var h in t.History) tn.AddText("history", h);
                foreach (var f in t.Flags) tn.AddText("flag", f);
                if (t.Craft != null) t.Craft.Save(tn.AddNode("CRAFT"));
            }
            var rep = n.AddNode("REPORTERS");
            foreach (var kv in Reporters) foreach (var r in kv.Value) rep.AddText(kv.Key, r);
            var dig = n.AddNode("DIGESTS");
            foreach (var d in DigestHistory) d.Save(dig.AddNode("DIGEST"));
            var st = n.AddNode("STORIES");
            foreach (var s in Stories) s.Save(st.AddNode("STORY"));
            var ar = n.AddNode("ARTICLES");
            foreach (var a in Articles) a.Save(ar.AddNode("ARTICLE"));
            var arc = n.AddNode("ARCHIVE");
            foreach (var a in Archive) a.Save(arc.AddNode("ARTICLE"));
            var so = n.AddNode("SOCIAL");
            foreach (var p in Social) p.Save(so.AddNode("POST"));
            n.AddText("anonHandle", AnonHandle);
            var fo = n.AddNode("FORUM");
            foreach (var t in Forum) t.Save(fo.AddNode("THREAD"));
            var pr = n.AddNode("RELEASES");
            foreach (var r in Releases) r.Save(pr.AddNode("RELEASE"));
            var ga = n.AddNode("GALLERY");
            foreach (var g in Gallery) g.Save(ga.AddNode("SHOT"));
            var lv = n.AddNode("STREAMS");
            foreach (var a in Streams) a.Save(lv.AddNode("STREAM"));
            var pl = n.AddNode("POLLS");
            foreach (var p in Polls) p.Save(pl.AddNode("POLL"));
            var jo = n.AddNode("COMMUNITYJOBS");
            foreach (var j in CommunityJobs) j.Save(jo.AddNode("JOB"));
            var asn = n.AddNode("ASSISTANT");
            foreach (var m in AssistantChat.Skip(Math.Max(0, AssistantChat.Count - MaxAssistantMessages))) m.Save(asn.AddNode("MSG"));
            var ord = n.AddNode("ORDERS");
            foreach (var o in Orders) { var on = ord.AddNode("ORDER"); on.Add("ut", o.UT); on.AddText("text", o.Text); }
            var aps = n.AddNode("APPEARANCES");
            foreach (var ap in Appearances.Skip(Math.Max(0, Appearances.Count - 60))) ap.Save(aps.AddNode("APPEARANCE"));
            var sci = n.AddNode("SCIENCE");
            foreach (var r in Science) r.Save(sci.AddNode("RESULT"));
            var crew = n.AddNode("CREW");
            foreach (var c in Crew.Values) c.Save(crew.AddNode("KERBAL"));
            foreach (var a in Anniversaries) n.Add("anniversary", a);
            n.Add("lastReviewYear", LastReviewYear);
            n.Add("lastAnniversaryCheck", LastAnniversaryCheckUT);
            var ties = n.AddNode("TIES");
            foreach (var t in Ties) t.Save(ties.AddNode("TIE"));
            n.AddText("prFirm", PrFirm);
            n.Add("prFirmUntil", PrFirmUntilUT);
            var dealNode = n.AddNode("DEALS");
            foreach (var d in Deals) d.Save(dealNode.AddNode("DEAL"));
        }

        public const int MaxAssistantMessages = 120;

        public static NewsState Load(DataNode n)
        {
            var s = new NewsState();
            if (n == null) return s;
            s.NextId = Math.Max(1, n.GetInt("nextId", 1));
            s.LastStoryUT = n.GetDouble("lastStory", -1);
            s.LastFillerUT = n.GetDouble("lastFiller", -1);
            s.LastMoodDecayUT = n.GetDouble("lastMoodDecay", -1);
            s.Silliness = Math.Max(0, Math.Min(10, n.GetInt("silliness")));
            s.RumorsEnabled = n.GetBool("rumors", true);
            s.SilentByDefault = n.GetBool("silentDefault");
            foreach (var v in n.GetAll("silentVessel")) s.SilentVessels.Add(v);
            foreach (var v in n.GetAll("sharedVessel")) s.SharedVessels.Add(v);
            s.MemorySummary = n.GetText("memory");
            foreach (var r in n.GetAll("record")) s.Records.Add(TextCodec.Decode(r));
            s.MemoryUpToUT = n.GetDouble("memoryUpTo", -1);
            s.ProfileSummary = n.GetText("profileSummary");
            s.ProfileSummaryHash = n.Get("profileHash", "");
            s.Mood = Mood.Load(n.GetNode("MOOD"));
            s.Profile = AgencyProfile.Load(n.GetNode("PROFILE"));
            var lore = n.GetNode("LORE");
            if (lore != null) foreach (var ln in lore.GetNodes("ENTRY")) s.Lore.Add(new LoreEntry { UT = ln.GetDouble("ut"), Text = ln.GetText("text") });
            var nov = n.GetNode("NOVELTY");
            if (nov != null)
                foreach (var raw in nov.GetAll("k"))
                {
                    var t = TextCodec.Decode(raw);
                    int bar = t.LastIndexOf('|');
                    double v;
                    if (bar > 0 && double.TryParse(t.Substring(bar + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) s.Novelty[t.Substring(0, bar)] = v;
                }
            var tr = n.GetNode("TRACKED");
            if (tr != null)
                foreach (var tn in tr.GetNodes("VESSEL"))
                {
                    var t = new TrackedVessel { VesselId = tn.Get("id", ""), LaunchUT = tn.GetDouble("launch"), Craft = CraftSnapshot.Load(tn.GetNode("CRAFT")) };
                    foreach (var h in tn.GetAll("history")) t.History.Add(TextCodec.Decode(h));
                    foreach (var f in tn.GetAll("flag")) t.Flags.Add(TextCodec.Decode(f));
                    if (t.VesselId != "") s.Tracked[t.VesselId] = t;
                }
            var rep = n.GetNode("REPORTERS");
            if (rep != null)
                foreach (var kv in rep.Values)
                {
                    List<string> l;
                    if (!s.Reporters.TryGetValue(kv.Key, out l)) s.Reporters[kv.Key] = l = new List<string>();
                    l.Add(TextCodec.Decode(kv.Value));
                }
            var dig = n.GetNode("DIGESTS");
            if (dig != null) foreach (var dn in dig.GetNodes("DIGEST")) s.DigestHistory.Add(WorldDigest.Load(dn));
            var st = n.GetNode("STORIES");
            if (st != null) foreach (var sn in st.GetNodes("STORY")) s.Stories.Add(Story.Load(sn));
            var ar = n.GetNode("ARTICLES");
            if (ar != null) foreach (var an in ar.GetNodes("ARTICLE")) s.Articles.Add(Article.Load(an));
            var arc = n.GetNode("ARCHIVE");
            if (arc != null) foreach (var an in arc.GetNodes("ARTICLE")) s.Archive.Add(Article.Load(an));
            var so = n.GetNode("SOCIAL");
            if (so != null) foreach (var pn in so.GetNodes("POST")) s.Social.Add(SocialPost.Load(pn));
            s.AnonHandle = n.GetText("anonHandle");
            var fo = n.GetNode("FORUM");
            if (fo != null) foreach (var tn in fo.GetNodes("THREAD")) s.Forum.Add(ForumThread.Load(tn));
            var pr = n.GetNode("RELEASES");
            if (pr != null) foreach (var rn in pr.GetNodes("RELEASE")) s.Releases.Add(PressRelease.Load(rn));
            var ga = n.GetNode("GALLERY");
            if (ga != null) foreach (var gn in ga.GetNodes("SHOT")) s.Gallery.Add(GalleryShot.Load(gn));
            var lv = n.GetNode("STREAMS");
            if (lv != null) foreach (var an in lv.GetNodes("STREAM")) s.Streams.Add(StreamArchive.Load(an));
            var pl = n.GetNode("POLLS");
            if (pl != null) foreach (var pn in pl.GetNodes("POLL")) s.Polls.Add(Poll.Load(pn));
            var jo = n.GetNode("COMMUNITYJOBS");
            if (jo != null) foreach (var jn in jo.GetNodes("JOB")) s.CommunityJobs.Add(CommunityJob.Load(jn));
            var asn = n.GetNode("ASSISTANT");
            if (asn != null) foreach (var mn in asn.GetNodes("MSG")) s.AssistantChat.Add(AssistantMessage.Load(mn));
            var ord = n.GetNode("ORDERS");
            if (ord != null) foreach (var on in ord.GetNodes("ORDER")) s.Orders.Add(new LoreEntry { UT = on.GetDouble("ut"), Text = on.GetText("text") });
            var apNode = n.GetNode("APPEARANCES");
            if (apNode != null) foreach (var an2 in apNode.GetNodes("APPEARANCE")) s.Appearances.Add(CrewAppearance.Load(an2));
            var sciNode = n.GetNode("SCIENCE");
            if (sciNode != null) foreach (var rn in sciNode.GetNodes("RESULT")) s.Science.Add(ScienceResult.Load(rn));
            var crewNode = n.GetNode("CREW");
            if (crewNode != null) foreach (var cn in crewNode.GetNodes("KERBAL")) { var c = CrewFame.Load(cn); if (c.Name != "") s.Crew[c.Name] = c; }
            foreach (var an in n.GetAll("anniversary")) s.Anniversaries.Add(an);
            s.LastReviewYear = n.GetInt("lastReviewYear", -1);
            s.LastAnniversaryCheckUT = n.GetDouble("lastAnniversaryCheck", -1);
            var ties = n.GetNode("TIES");
            if (ties != null) foreach (var tn in ties.GetNodes("TIE")) s.Ties.Add(ReporterTie.Load(tn));
            s.PrFirm = n.GetText("prFirm");
            s.PrFirmUntilUT = n.GetDouble("prFirmUntil", -1);
            var dealNode = n.GetNode("DEALS");
            if (dealNode != null) foreach (var x in dealNode.GetNodes("DEAL")) s.Deals.Add(Deal.Load(x));
            return s;
        }
    }
}
