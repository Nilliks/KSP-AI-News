using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    public class OutletDef
    {
        public string Id;
        public string Name;
        public string Domain;
        public string Style;
        public string Bias;
        public string Covers;
        public string Color = "#1f3b5a";
        /// <summary>Minimum story significance for this outlet to cover a normal story.</summary>
        public double MinSignificance = 30;
        /// <summary>Lower threshold when the story involves failure or danger.</summary>
        public double MinSignificanceFailure = 30;
        /// <summary>Days after the event that this outlet publishes.</summary>
        public double DelayDays = 1.0;
        public string DefaultKind = "news";
        /// <summary>Only covers stories that touch these event types (empty: any).</summary>
        public List<string> OnlyFor = new List<string>();
        /// <summary>Writes filler/feature pieces in quiet periods.</summary>
        public bool WritesFiller = true;
        public bool IsOfficial;
    }

    public class CommenterDef
    {
        public string Handle;
        public string Persona;
    }

    public static class Outlets
    {
        public static readonly List<OutletDef> All = new List<OutletDef>
        {
            new OutletDef
            {
                Id = "herald", Name = "Kerbin Daily Herald", Domain = "herald.kb", Color = "#1f3b5a",
                Style = "Mainstream national newspaper. Balanced, clear, AP style. Inverted pyramid, quotes officials.",
                Bias = "Neutral, slightly proud of the space program.",
                Covers = "All major events, front-page news.",
                MinSignificance = 15, MinSignificanceFailure = 15, DelayDays = 1.0,
            },
            new OutletDef
            {
                Id = "observer", Name = "Orbital Observer", Domain = "orbitalobserver.kb", Color = "#2d5d3a",
                Style = "Aerospace trade journal. Technical, numbers-heavy, cites masses, delta-v, orbits and part counts.",
                Bias = "Pro-engineering. Admires clean design, harsh on sloppy design and spin.",
                Covers = "Craft specs, orbits, debris, records, engineering analysis of failures.",
                MinSignificance = 30, MinSignificanceFailure = 25, DelayDays = 1.1, DefaultKind = "analysis",
            },
            new OutletDef
            {
                Id = "inquirer", Name = "The Kerbal Inquirer", Domain = "inquirer.kb", Color = "#a3201c",
                Style = "Tabloid. Punchy, dramatic headlines, short paragraphs, anonymous 'insiders', exclamation marks.",
                Bias = "Skeptical of spending, loves disasters, gossip and crew drama. Mocks corporate euphemisms.",
                Covers = "Failures, crew drama, rumors, celebrity kerbals.",
                MinSignificance = 55, MinSignificanceFailure = 20, DelayDays = 0.4,
            },
            new OutletDef
            {
                Id = "kraken", Name = "The Daily Kraken", Domain = "dailykraken.kb", Color = "#7a1fa2",
                Style = "Extreme clickbait gossip and conspiracy site. ALL-CAPS teaser headlines (\"You won't BELIEVE...\", \"What KSC doesn't want you to know\"), breathless short paragraphs, unnamed 'insiders' and 'leaked memos', wild theories (cover-ups, secret Mun bases, the Kraken, crew feuds, staged footage). Funny, never cruel about the dead.",
                Bias = "Wants clicks, not truth. Mostly wrong or wildly exaggerated, but now and then accidentally right. Never admits a mistake; takes credit when anything it said comes true.",
                Covers = "Rumors, leaks, secrecy, failures, anything it can spin.",
                MinSignificance = 40, MinSignificanceFailure = 20, DelayDays = 0.6, DefaultKind = "rumor",
            },
            new OutletDef
            {
                Id = "munmonthly", Name = "Mun Monthly", Domain = "munmonthly.kb", Color = "#5b3f86",
                Style = "Long-form magazine. Human interest, evocative, profiles the people behind the mission.",
                Bias = "Romantic about exploration.",
                Covers = "Crew profiles, milestones, retrospectives.",
                MinSignificance = 75, MinSignificanceFailure = 85, DelayDays = 2.0, DefaultKind = "feature",
            },
            new OutletDef
            {
                Id = "kscwatch", Name = "KSC Watch", Domain = "kscwatch.kb", Color = "#8a6d1d",
                Style = "Independent watchdog blog and citizen journalism. First person, pointed, keeps tallies.",
                Bias = "Watchdog. Counts debris, failures and costs. Fair but unimpressed by hype.",
                Covers = "Budget, safety record, debris, accountability.",
                MinSignificance = 70, MinSignificanceFailure = 30, DelayDays = 1.3, DefaultKind = "opinion",
            },
            new OutletDef
            {
                Id = "science", Name = "Kerbin Science Review", Domain = "kscireview.kb", Color = "#11657a",
                Style = "Scientific community digest: short expert commentary, quotes from named researchers at Kerbin universities, notes on what the data could mean, cautious about overclaiming.",
                Bias = "Curious and precise. Excited by new data, skeptical of hype.",
                Covers = "Science results, exploration of new bodies, records, theoretical debate.",
                MinSignificance = 60, MinSignificanceFailure = 80, DelayDays = 2.0, DefaultKind = "expert",
                OnlyFor = new List<string> { EventTypes.Soi, EventTypes.Landing, EventTypes.Milestone, EventTypes.Orbit, EventTypes.CrewKilled, EventTypes.Docking },
            },
            new OutletDef
            {
                Id = "council", Name = "Planetary Council Press Office", Domain = "council.gov.kb", Color = "#444444",
                Style = "Official government statement. Formal, measured, a few short paragraphs, attributed to the Council Chair or a minister. May announce reviews, funding debates or congratulations.",
                Bias = "Political. Supportive after triumphs, cautious and accountable after disasters.",
                Covers = "Only the biggest news: historic firsts and loss of crew.",
                MinSignificance = 85, MinSignificanceFailure = 85, DelayDays = 0.8, DefaultKind = "statement",
                WritesFiller = false, IsOfficial = true,
            },
        };

        public static readonly List<CommenterDef> Commenters = new List<CommenterDef>
        {
            new CommenterDef { Handle = "RetiredRocketeer_Bob", Persona = "retired KSC engineer, precise, nostalgic, corrects technical errors" },
            new CommenterDef { Handle = "JebFan4Ever", Persona = "Jebediah superfan, all caps when excited" },
            new CommenterDef { Handle = "TaxpayerTed", Persona = "budget hawk, asks what everything costs" },
            new CommenterDef { Handle = "TruthOrbiter", Persona = "conspiracy theorist who thinks landings are staged in the SPH" },
            new CommenterDef { Handle = "MunGazer", Persona = "amateur astronomer, tracks vessels with a backyard telescope" },
            new CommenterDef { Handle = "SnackHatchSally", Persona = "KSC cafeteria worker, insider gossip, warm" },
            new CommenterDef { Handle = "Dr_Linmy_Kerman", Persona = "university astrophysicist, measured, cites data" },
            new CommenterDef { Handle = "ConcernedMom_Kerbin", Persona = "worried about crew safety and debris falling on houses" },
            new CommenterDef { Handle = "DebrisCounter", Persona = "keeps a running tally of orbital junk, dry humor" },
            new CommenterDef { Handle = "FirstTimeReader", Persona = "new to space news, asks simple questions" },
        };

        public static OutletDef Get(string id)
        {
            return All.FirstOrDefault(o => o.Id == id);
        }

        public static string NameOf(string id)
        {
            var o = Get(id);
            return o != null ? o.Name : id;
        }
    }
}
