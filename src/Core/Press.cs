using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    /// <summary>The press room and the screenshot gallery: the agency's own voice, and its pictures.</summary>
    public partial class NewsEngine
    {
        public static readonly string[] ReleaseTypes = { "announcement", "update", "incident", "teaser" };

        /// <summary>Publishes a press release. The press reacts about a day later, through the normal news cycle.</summary>
        public PressRelease PublishRelease(string type, string title, string body, string vesselId, IEnumerable<string> shots, double ut)
        {
            title = (title ?? "").Trim();
            body = (body ?? "").Trim();
            if (title == "" && body == "") return null;
            if (Array.IndexOf(ReleaseTypes, type) < 0) type = "announcement";
            if (title == "") title = ResponseParser.Clip(body.Replace("\n", " "), 80);
            var tv = Tracked(vesselId);
            var r = new PressRelease
            {
                Id = State.NewId("pr"), Type = type, Title = ResponseParser.Clip(title, 200), Body = ResponseParser.Clip(body, 6000), UT = ut,
                VesselId = tv != null ? vesselId : "", VesselName = tv != null && tv.Craft != null ? tv.Craft.VesselName : "",
            };
            foreach (var id in shots ?? new string[0])
            {
                var shot = State.Gallery.FirstOrDefault(g => g.Id == id);
                if (shot == null) continue;
                r.Shots.Add(id);
                if (!shot.Public) { shot.Public = true; shot.ReleasedUT = ut; }
            }
            State.Releases.Add(r);

            var story = new Story
            {
                Id = State.NewId("s"), Kind = "release", Topic = r.Title, ReleaseId = r.Id, VesselId = r.VesselId, CreatedUT = ut,
                Craft = tv != null && !State.IsSilent(r.VesselId) ? tv.Craft : null,
            };
            story.Events.Add(new NewsEvent { Type = EventTypes.PressRelease, UT = ut, VesselId = r.VesselId, VesselName = r.VesselName, Detail = r.Title, Situation = type });
            story.Significance = ReleaseSignificance(r);
            story.PublishUT = ut + 0.6 * Day;
            r.StoryId = story.Id;
            State.Stories.Add(story);
            return r;
        }

        /// <summary>Incidents and announcements about big missions draw more attention than routine updates.</summary>
        double ReleaseSignificance(PressRelease r)
        {
            double sig = r.Type == "incident" ? 45 : r.Type == "teaser" ? 32 : r.Type == "announcement" ? 30 : 24;
            if (r.VesselId != "")
            {
                var recent = State.Articles.Where(a => State.Stories.Any(s => s.Id == a.StoryId && s.VesselId == r.VesselId)).Select(a => a.Significance).DefaultIfEmpty(0).Max();
                sig += recent * 0.25;
            }
            sig += State.Mood.Excitement / 12;
            if (r.Shots.Count > 0) sig += 4;
            return Math.Min(75, sig);
        }

        public PressRelease ReleaseOf(Story s) { return s.ReleaseId != "" ? State.Releases.FirstOrDefault(r => r.Id == s.ReleaseId) : null; }

        // ------------------------------------------------------------ gallery

        public GalleryShot AddShot(string file, double ut, string vesselId, string vesselName, string body, string situation)
        {
            var g = new GalleryShot { Id = State.NewId("g"), File = file, UT = ut, VesselId = vesselId ?? "", VesselName = vesselName ?? "", Body = body ?? "", Situation = situation ?? "" };
            State.Gallery.Add(g);
            return g;
        }

        // ------------------------------------------------------------ press photographers

        public const int MaxPressShotsPerVessel = 8;
        static readonly string[] Spotters = { "MunGazer", "LongLensLinda", "CausewayCam", "PadWatcherPete", "BayBeachBill" };

        /// <summary>Raised with the file name when a press shot is dropped, so the game can delete the picture.</summary>
        public event Action<string> PressShotRemoved;

        public bool HasPressShot(string vesselId, string kind)
        {
            return State.Gallery.Any(g => g.IsPress && g.VesselId == vesselId && g.Kind == kind);
        }

        /// <summary>
        /// Records a long-lens photo taken by an outside photographer. It is public at once: the press may run it, and a
        /// spotter posts the best moments (the pad, liftoff, an explosion) on Snackboard. Silent craft are photographed
        /// too, because all of this is visible from the ground.
        /// </summary>
        public GalleryShot AddPressShot(string file, double ut, string vesselId, string vesselName, string body, string situation, string kind, string credit)
        {
            var g = AddShot(file, ut, vesselId, vesselName, body, situation);
            g.Source = "press"; g.Kind = kind ?? ""; g.Credit = credit ?? ""; g.Public = true; g.ReleasedUT = ut;
            g.Caption = PressCaption(g);
            if ((kind == "pad" || kind == "liftoff" || kind == "explosion") && Settings.SiteOn("snackboard") && !State.Forum.Any(t => t.Image != "" && State.Gallery.Any(x => x.File == t.Image && x.VesselId == vesselId && x.Kind == kind)))
                SpotterThread(g, ut);
            // Keep a few per vessel: the first of each kind, then the newest.
            var mine = State.Gallery.Where(x => x.IsPress && x.VesselId == vesselId).OrderBy(x => x.UT).ToList();
            while (mine.Count > MaxPressShotsPerVessel)
            {
                var drop = mine.FirstOrDefault(x => x.Kind == "ascent" && x != g && !State.Forum.Any(t => t.Image == x.File)) ?? mine.First(x => x != g);
                mine.Remove(drop);
                State.Gallery.Remove(drop);
                if (PressShotRemoved != null) PressShotRemoved(drop.File);
            }
            return g;
        }

        static string PressCaption(GalleryShot g)
        {
            string v = g.VesselName != "" ? g.VesselName : "The rocket";
            switch (g.Kind)
            {
                case "pad": return v + " on the launch pad";
                case "liftoff": return v + " clears the tower";
                case "ascent": return v + " climbs away from Kerbin";
                case "explosion": return v + " breaks apart";
                case "recovery": return v + " back on Kerbin";
                default: return v;
            }
        }

        void SpotterThread(GalleryShot g, double ut)
        {
            var rng = new Random(OfflineWriter.StableHash(g.Id));
            string who = Spotters[rng.Next(Spotters.Length)];
            string v = g.VesselName != "" ? g.VesselName : "the new rocket";
            bool silent = State.IsSilent(g.VesselId);
            string title;
            switch (g.Kind)
            {
                case "pad": title = silent ? "Something new is on the pad and " + AgencyName + " isn't saying what. Long lens shot" : v + " is on the pad. Got a shot from the causeway"; break;
                case "liftoff": title = "Liftoff! Caught " + v + " clearing the tower"; break;
                default: title = "Just watched " + v + " come apart. I got the moment on camera"; break;
            }
            var t = new ForumThread
            {
                Id = State.NewId("t"), Board = "launches", Title = title, Author = who, UT = ut, Image = g.File,
                Body = (g.Credit != "" ? g.Credit + ". " : "") + (g.Kind == "explosion" ? "Hope nobody was aboard." : silent ? "Anyone know what it is?" : "Not bad for a hobby lens."),
                Score = 40 + rng.Next(160) + (g.Kind == "explosion" ? 300 : 0),
            };
            State.Forum.Add(t);
            Queue(new CommunityJob { Target = "t:" + t.Id, Mode = "react", Count = 3, UT = ut });
            if (CommunityChanged != null) CommunityChanged();
        }

        /// <summary>Makes a screenshot public. The community comments on it, and the press may use it.</summary>
        public bool ReleaseShot(string id, string caption, double ut)
        {
            var g = State.Gallery.FirstOrDefault(x => x.Id == id);
            if (g == null) return false;
            if (caption != null) g.Caption = ResponseParser.Clip(caption.Trim(), 300);
            if (g.Public) return true;
            g.Public = true;
            g.ReleasedUT = ut;
            Queue(new CommunityJob { Target = "g:" + g.Id, Mode = "react", Count = 4, UT = ut });
            return true;
        }

        public GalleryShot ShotOf(string target) { return target.StartsWith("g:") ? State.Gallery.FirstOrDefault(g => g.Id == target.Substring(2)) : null; }

        /// <summary>How well a picture suits a story: the explosion for a failure, liftoff for a launch.</summary>
        static int PhotoFit(Story story, GalleryShot g)
        {
            bool failed = story.HasEvent(EventTypes.Failure) || story.HasEvent(EventTypes.CrewKilled);
            if (failed) return g.Kind == "explosion" ? 5 : g.Kind == "liftoff" ? 3 : 1;
            if (story.HasEvent(EventTypes.Splashdown) || story.HasEvent(EventTypes.Recovery)) return g.Kind == "recovery" ? 5 : g.Source == "" ? 4 : 1;
            if (story.HasEvent(EventTypes.Launch)) return g.Kind == "liftoff" ? 5 : g.Kind == "ascent" ? 4 : g.Kind == "pad" ? 3 : 2;
            return g.Source == "" ? 4 : g.Kind == "explosion" ? 0 : 2;
        }

        /// <summary>The picture for a story: a released gallery shot of the vessel, newest first, taken before the news.</summary>
        string ShotFor(Story story)
        {
            var rel = ReleaseOf(story);
            if (rel != null && rel.Shots.Count > 0)
            {
                var g = State.Gallery.FirstOrDefault(x => x.Id == rel.Shots[0]);
                if (g != null) return "gallery:" + g.File;
            }
            if (story.VesselId == "") return null;
            var shot = State.Gallery.Where(x => x.Public && x.VesselId == story.VesselId && x.ReleasedUT <= story.PublishUT + Day)
                .OrderByDescending(x => PhotoFit(story, x)).ThenByDescending(x => x.UT).FirstOrDefault();
            return shot != null ? "gallery:" + shot.File : null;
        }
    }
}
