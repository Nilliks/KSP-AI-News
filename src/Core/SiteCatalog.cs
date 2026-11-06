using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    /// <summary>A website the player can turn off in Settings. Turned off, it leaves every list and link, and the AI stops writing for it.</summary>
    public class SiteInfo
    {
        public string Id, Name, Domain, Group, Description, WhenOff;
    }

    public static class SiteCatalog
    {
        /// <summary>Every site that can be turned off, in the order Settings shows them. The portal, Agency and Settings always stay.</summary>
        public static readonly List<SiteInfo> All = new List<SiteInfo>
        {
            new SiteInfo { Id = "herald", Group = "News", Description = "The main daily paper. Calm, balanced front-page coverage of every big event.", WhenOff = "Other outlets still cover the news." },
            new SiteInfo { Id = "observer", Group = "News", Description = "Aerospace trade journal. Delta-v, part counts and hard-nosed engineering analysis of failures.", WhenOff = "No technical coverage." },
            new SiteInfo { Id = "inquirer", Group = "News", Description = "Tabloid. Dramatic headlines, crew gossip and every disaster on the front page.", WhenOff = "No tabloid stories." },
            new SiteInfo { Id = "kraken", Group = "News", Description = "Clickbait conspiracy site. Mostly wrong rumors and cover-up theories, now and then accidentally right.", WhenOff = "No rumors or leaks." },
            new SiteInfo { Id = "munmonthly", Group = "News", Description = "Long-form magazine. Crew profiles, milestones and retrospectives.", WhenOff = "No magazine features." },
            new SiteInfo { Id = "kscwatch", Group = "News", Description = "Watchdog blog. Keeps count of costs, debris and the safety record.", WhenOff = "No watchdog opinion pieces." },
            new SiteInfo { Id = "science", Group = "News", Description = "Scientists comment on missions beyond Kerbin and what the data could mean.", WhenOff = "No science commentary." },
            new SiteInfo { Id = "council", Group = "News", Description = "Official government statements, only for the biggest news.", WhenOff = "No government statements." },
            new SiteInfo { Id = "kerbnet", Name = "Kerbnet", Domain = "kerbnet.kb", Group = "Community", Description = "Social media. Short public reactions to each story.", WhenOff = "No social posts are written, which makes each story's AI reply a little shorter." },
            new SiteInfo { Id = "snackboard", Name = "Snackboard", Domain = "snackboard.kb", Group = "Community", Description = "Community forum. Public threads about the news, spotter photos, AMAs and town halls.", WhenOff = "No forum threads are written with the news, and spotters stop posting." },
            new SiteInfo { Id = "live", Name = "KNN Live", Domain = "live.kb", Group = "Community", Description = "Launch livestreams with a viewer chat. Uses the AI only while you watch.", WhenOff = "No livestream page." },
            new SiteInfo { Id = "engineering", Name = "Engineering team", Domain = "engineering.kb", Group = "Agency", Description = "Your flight engineers: craft data, design reviews, mission plans and crash investigations. Uses the AI only when you ask.", WhenOff = "The page and its link disappear." },
            new SiteInfo { Id = "pressroom", Name = "Press room", Domain = "pressroom.kb", Group = "Agency", Description = "Your agency's own press releases. No AI cost.", WhenOff = "Only the link disappears; releases still work." },
            new SiteInfo { Id = "gallery", Name = "Gallery", Domain = "gallery.kb", Group = "Agency", Description = "Press photos of your launches and your screenshots. No AI cost.", WhenOff = "Only the link disappears; F8 still opens it." },
            new SiteInfo { Id = "tracker", Name = "Universe tracker", Domain = "tracker.kb", Group = "Agency", Description = "Records, firsts and where everything is. No AI cost.", WhenOff = "Only the link disappears." },
            new SiteInfo { Id = "telemetry", Name = "Telemetry", Domain = "telemetry.kb", Group = "Agency", Description = "Choose which craft share their data with the public. No AI cost.", WhenOff = "Only the link disappears; silent craft stay silent." },
        };

        static SiteCatalog()
        {
            foreach (var s in All)
            {
                var o = Outlets.Get(s.Id);
                if (o == null) continue;
                s.Name = o.Name; s.Domain = o.Domain;
            }
        }

        public static SiteInfo Get(string id) { return All.FirstOrDefault(s => s.Id == id); }
        public static SiteInfo ByDomain(string host) { return All.FirstOrDefault(s => s.Domain == host); }
    }
}
