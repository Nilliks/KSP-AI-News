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
            var shot = State.Gallery.Where(x => x.Public && x.VesselId == story.VesselId && x.ReleasedUT <= story.PublishUT + Day).OrderByDescending(x => x.UT).FirstOrDefault();
            return shot != null ? "gallery:" + shot.File : null;
        }
    }
}
