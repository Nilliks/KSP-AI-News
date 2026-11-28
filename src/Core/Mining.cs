using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>One ore from the KSP Mining mod's catalog, copied into plain data so the core never touches that mod.</summary>
    public class MiningOre
    {
        public string Id = "", Title = "", Raw = "", Refined = "", Ingot = "", IngotUnit = "bar";
        public int Tier = 1;
        public List<string> Bodies = new List<string>();
        /// <summary>List price per tonne of each good.</summary>
        public double RawValue, RefinedValue, IngotValue;
        /// <summary>The mining mod's own flavour text about the ore (who wants it and why). Treated as fact.</summary>
        public string About = "";
        /// <summary>Display names of the goods, by resource name.</summary>
        public Dictionary<string, string> Names = new Dictionary<string, string>();
    }

    public class MiningCatalog
    {
        public List<MiningOre> Ores = new List<MiningOre>();

        public MiningOre OreOf(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return null;
            return Ores.FirstOrDefault(o => o.Raw == resource || o.Refined == resource || o.Ingot == resource);
        }

        public MiningOre ById(string id) { return Ores.FirstOrDefault(o => o.Id == id); }

        /// <summary>raw, refined or ingot ("" for a resource that is not an ore).</summary>
        public string Kind(string resource)
        {
            var o = OreOf(resource);
            return o == null ? "" : o.Raw == resource ? "raw" : o.Refined == resource ? "refined" : "ingot";
        }

        public double ListPrice(string resource)
        {
            var o = OreOf(resource);
            return o == null ? 0 : o.Raw == resource ? o.RawValue : o.Refined == resource ? o.RefinedValue : o.IngotValue;
        }

        public string Name(string resource)
        {
            var o = OreOf(resource);
            string n;
            if (o != null && o.Names.TryGetValue(resource, out n) && n != "") return n;
            return o != null ? o.Title + (o.Raw == resource ? " ore" : o.Refined == resource ? " (refined)" : " " + o.IngotUnit + "s") : resource ?? "";
        }
    }

    /// <summary>Some amount of one good, with what it is worth at today's exchange price.</summary>
    public class MiningCargo
    {
        public string Resource = "";
        public double Tonnes, Value;
    }

    public class MiningSale
    {
        public double UT, Tonnes, Funds;
        public string Resource = "";
        /// <summary>How far this sale pushed the good's price down, 0 to 1 (0 when unknown).</summary>
        public double PriceDrop;
    }

    /// <summary>
    /// Something the press could report about mining. Magnitude follows the mod's 1-5 scale: 1 rides along in the
    /// background news roundup, 2 and up become a story of their own.
    /// </summary>
    public class MiningItem
    {
        public int Magnitude = 1;
        /// <summary>recovery, drill, smelt, sale, earnings or prices.</summary>
        public string Situation = "";
        public string Headline = "";
        /// <summary>The facts in plain sentences. Every number comes from the game.</summary>
        public string Text = "";
        public string Body = "";
        /// <summary>Unique per happening; a later item with the same key replaces one still waiting.</summary>
        public string Key = "";
    }

    /// <summary>
    /// Turns what the KSP Mining mod reports (ore brought home, sales on its exchange, drills and smelters at work)
    /// into news items. Pure logic: the KSP layer reads the other mod and feeds plain numbers in. What has already
    /// been reported is kept in the save (NewsState.WorldMemo), so a rollback forgets it with everything else.
    /// </summary>
    public static class MiningNews
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const string Exchange = "KERBEX";
        static readonly double[] EarningSteps = { 100000, 1000000, 10000000, 100000000 };

        /// <summary>Where a magnitude lands on the 0-100 significance scale.</summary>
        public static double Weight(int magnitude)
        {
            switch (magnitude)
            {
                case 5: return 88;
                case 4: return 66;
                case 3: return 46;
                case 2: return 28;
                default: return 8;
            }
        }

        public static string Get(Dictionary<string, string> memo, string key) { string v; return memo.TryGetValue("mining:" + key, out v) ? v : ""; }
        public static void Set(Dictionary<string, string> memo, string key, string value) { memo["mining:" + key] = value; }
        public static double Num(Dictionary<string, string> memo, string key) { double d; return double.TryParse(Get(memo, key), NumberStyles.Float, Inv, out d) ? d : 0; }
        public static void Set(Dictionary<string, string> memo, string key, double value) { Set(memo, key, value.ToString("R", Inv)); }

        /// <summary>
        /// What has arrived in the warehouse since the last look: today's holdings against the holdings remembered in
        /// the save, with anything sold in between added back. Remembers today's holdings for next time.
        /// </summary>
        public static Dictionary<string, double> Arrivals(Dictionary<string, string> memo, IDictionary<string, double> held, IDictionary<string, double> soldSince)
        {
            var arrived = new Dictionary<string, double>();
            held = held ?? new Dictionary<string, double>();
            foreach (var kv in held)
            {
                double sold = 0;
                if (soldSince != null) soldSince.TryGetValue(kv.Key, out sold);
                double delta = kv.Value - Num(memo, "inv:" + kv.Key) + sold;
                if (delta > 0.001) arrived[kv.Key] = delta;
            }
            foreach (var k in memo.Keys.Where(k => k.StartsWith("mining:inv:")).ToList()) if (!held.ContainsKey(k.Substring(11))) memo.Remove(k);
            foreach (var kv in held) Set(memo, "inv:" + kv.Key, kv.Value);
            return arrived;
        }

        /// <summary>Sales newer than the last one already seen. Remembers the newest.</summary>
        public static List<MiningSale> NewSales(Dictionary<string, string> memo, IEnumerable<MiningSale> log)
        {
            double last = Get(memo, "saleUT") == "" ? double.MinValue : Num(memo, "saleUT");
            var fresh = (log ?? new MiningSale[0]).Where(s => s != null && s.UT > last).OrderBy(s => s.UT).ToList();
            if (fresh.Count > 0) Set(memo, "saleUT", fresh[fresh.Count - 1].UT);
            return fresh;
        }

        public static string Tonnes(double t) { return (t >= 100 ? Math.Round(t).ToString("#,0", Inv) : t >= 10 ? t.ToString("0.#", Inv) : t.ToString("0.##", Inv)) + " t"; }
        public static string Funds(double f) { return Math.Round(f).ToString("#,0", Inv) + " funds"; }
        static string Pct(double fraction) { return Math.Round(Math.Abs(fraction) * 100).ToString("0", Inv) + "%"; }

        static string Join(IList<string> parts)
        {
            if (parts.Count == 0) return "";
            if (parts.Count == 1) return parts[0];
            return string.Join(", ", parts.Take(parts.Count - 1).ToArray()) + " and " + parts[parts.Count - 1];
        }

        static string The(string body) { return body == "Mun" ? "the Mun" : body; }

        public static string CargoList(MiningCatalog cat, IEnumerable<MiningCargo> cargo)
        {
            return Join(cargo.Where(c => c.Tonnes > 0).OrderByDescending(c => c.Value).Select(c => Tonnes(c.Tonnes) + " of " + cat.Name(c.Resource)).ToList());
        }

        public static bool Baselined(Dictionary<string, string> memo) { return Get(memo, "init") != ""; }

        /// <summary>
        /// The first time a save is seen with the mining mod: whatever is already in the warehouse or the sales log
        /// is old news, so its firsts are marked as told and the earnings count starts from today's total.
        /// </summary>
        public static void Baseline(Dictionary<string, string> memo, MiningCatalog cat, IEnumerable<string> knownResources, double earned)
        {
            if (Baselined(memo)) return;
            Set(memo, "init", "1");
            bool any = false;
            foreach (var r in knownResources ?? new string[0])
            {
                var o = cat.OreOf(r);
                if (o == null) continue;
                any = true;
                Set(memo, "ore:" + o.Id, "1");
                if (cat.Kind(r) == "ingot") Set(memo, "bars:" + o.Id, "1");
            }
            if (any || earned > 0) Set(memo, "first", "1");
            Set(memo, "earnedStep", EarningSteps.Count(s => earned >= s));
        }

        /// <summary>Part of the baseline: sales already in the log are old news.</summary>
        public static void BaselineSales(Dictionary<string, string> memo, IEnumerable<MiningSale> log)
        {
            var all = (log ?? new MiningSale[0]).Where(s => s != null).ToList();
            Set(memo, "saleUT", all.Count > 0 ? all.Max(s => s.UT) : -1e300);
        }

        /// <summary>Remembers where an ore was last seen being dug, so a later cargo can say where it came from.</summary>
        static string Source(Dictionary<string, string> memo, MiningOre o, IEnumerable<string> visited)
        {
            var hit = (visited ?? new string[0]).FirstOrDefault(b => o.Bodies.Contains(b));
            if (hit != null) return hit;
            string last = Get(memo, "src:" + o.Id);
            if (last != "") return last;
            return o.Bodies.Count == 1 ? o.Bodies[0] : "";
        }

        static string Found(MiningOre o) { return o.Bodies.Count > 0 ? " It is found on " + Join(o.Bodies.Select(The).ToList()) + "." : ""; }
        static string Lore(MiningOre o) { return o.About != "" ? " Background on " + o.Title.ToLowerInvariant() + ": " + o.About : ""; }

        /// <summary>Ore arriving in the warehouse on Kerbin. One item: the biggest thing true about this cargo.</summary>
        public static MiningItem Recovery(Dictionary<string, string> memo, MiningCatalog cat, string vesselName, IList<MiningCargo> cargo, IEnumerable<string> visited, double ut)
        {
            var goods = (cargo ?? new List<MiningCargo>()).Where(c => c != null && c.Tonnes > 0 && cat.OreOf(c.Resource) != null).ToList();
            if (goods.Count == 0) return null;
            double tonnes = goods.Sum(c => c.Tonnes), value = goods.Sum(c => c.Value);
            string who = string.IsNullOrEmpty(vesselName) ? "An agency vessel" : "\"" + vesselName + "\"";
            var ores = goods.Select(c => cat.OreOf(c.Resource)).Distinct().OrderByDescending(o => o.Tier).ToList();
            var newOres = ores.Where(o => Get(memo, "ore:" + o.Id) == "").ToList();
            bool firstEver = Get(memo, "first") == "";
            double best = Num(memo, "best");
            var top = newOres.FirstOrDefault() ?? ores[0];
            string from = Source(memo, top, visited);

            var sb = new StringBuilder();
            sb.Append(who + " brought " + CargoList(cat, goods) + " back to Kerbin" + (from != "" ? " from " + The(from) : "") + ". ");
            sb.Append("At today's " + Exchange + " prices the cargo is worth about " + Funds(value) + ". It is now in the agency's warehouse and has not been sold yet.");

            var item = new MiningItem { Situation = "recovery", Body = from, Key = "mining:recovery:" + Math.Floor(ut) };
            if (firstEver)
            {
                item.Magnitude = 5;
                item.Headline = "First ore from another world reaches Kerbin";
                sb.Append(" It is the first ore from another world ever delivered to Kerbin. No other space program has mined anything off Kerbin.");
                sb.Append(Lore(top));
            }
            else if (newOres.Count > 0)
            {
                item.Magnitude = top.Tier >= 3 ? 4 : 3;
                item.Headline = "First " + top.Title.ToLowerInvariant() + " brought home" + (from != "" ? " from " + The(from) : "");
                sb.Append(" It is the first " + Join(newOres.Select(o => o.Title.ToLowerInvariant()).ToList()) + " ever brought back to Kerbin." + Found(top));
                sb.Append(Lore(top));
            }
            else if (best > 0 && value > best)
            {
                item.Magnitude = value >= best * 2 ? 3 : 2;
                item.Headline = "Record ore cargo lands: " + Funds(value);
                sb.Append(" It is the most valuable ore cargo the agency has ever brought home; the record before was about " + Funds(best) + ".");
            }
            else
            {
                item.Magnitude = 1;
                item.Headline = "Ore cargo of " + Tonnes(tonnes) + " arrives at the Space Center";
            }
            item.Text = sb.ToString();

            Set(memo, "first", "1");
            foreach (var o in ores) Set(memo, "ore:" + o.Id, "1");
            if (value > best) Set(memo, "best", value);
            return item;
        }

        /// <summary>A drill bringing up ore on a world for the first time. Null when that ore and world were already reported.</summary>
        public static MiningItem Drill(Dictionary<string, string> memo, MiningCatalog cat, string resource, string body, string vesselName)
        {
            var o = cat.OreOf(resource);
            if (o == null || string.IsNullOrEmpty(body)) return null;
            Set(memo, "src:" + o.Id, body);
            string key = "drill:" + o.Id + ":" + body;
            if (Get(memo, key) != "") return null;
            Set(memo, key, "1");
            bool firstAnywhere = Get(memo, "drillAny") == "";
            Set(memo, "drillAny", "1");
            string who = string.IsNullOrEmpty(vesselName) ? "An agency rig" : "\"" + vesselName + "\"";
            return new MiningItem
            {
                Magnitude = 2, Situation = "drill", Body = body, Key = "mining:" + key,
                Headline = "Drilling begins on " + The(body),
                Text = who + " has started drilling " + cat.Name(o.Raw) + " on " + The(body) + ". It is the first time " + o.Title.ToLowerInvariant() + " has been mined there"
                    + (firstAnywhere ? ", and the first mining the agency has reported anywhere beyond Kerbin" : "") + ". Nothing has been brought back to Kerbin from this dig yet."
                    + " " + cat.Name(o.Raw) + " lists at about " + Funds(o.RawValue) + " a tonne on " + Exchange + "." + Lore(o),
            };
        }

        /// <summary>The first bars (or boules, cells) of an ore cast in a smelter. Null when already reported.</summary>
        public static MiningItem Bars(Dictionary<string, string> memo, MiningCatalog cat, string resource, string body, string vesselName)
        {
            var o = cat.OreOf(resource);
            if (o == null || cat.Kind(resource) != "ingot") return null;
            string key = "bars:" + o.Id;
            if (Get(memo, key) != "") return null;
            Set(memo, key, "1");
            string who = string.IsNullOrEmpty(vesselName) ? "An agency smelter" : "The smelter aboard \"" + vesselName + "\"";
            string where = string.IsNullOrEmpty(body) ? "" : body == "Kerbin" ? " on Kerbin" : " at " + The(body);
            return new MiningItem
            {
                Magnitude = 2, Situation = "smelt", Body = body ?? "", Key = "mining:" + key,
                Headline = "First " + cat.Name(o.Ingot) + " cast" + (where != "" && body != "Kerbin" ? " off Kerbin" : ""),
                Text = who + " has cast the agency's first " + cat.Name(o.Ingot) + where + ". Buyers pay more for cast metal than for refined ore: it lists at about "
                    + Funds(o.IngotValue) + " a tonne on " + Exchange + "." + Lore(o),
            };
        }

        /// <summary>
        /// New sales on the exchange. A sale that knocks a price down by a tenth or more is a market story; the rest
        /// add up into one small item per good per day.
        /// </summary>
        public static List<MiningItem> Sales(Dictionary<string, string> memo, MiningCatalog cat, IList<MiningSale> sales, double ut, double daySeconds)
        {
            var items = new List<MiningItem>();
            if (sales == null) return items;
            long day = (long)Math.Floor(ut / Math.Max(1, daySeconds));
            foreach (var g in sales.Where(s => s != null && s.Tonnes > 0 && cat.OreOf(s.Resource) != null).GroupBy(s => s.Resource))
            {
                var o = cat.OreOf(g.Key);
                double t = g.Sum(s => s.Tonnes), f = g.Sum(s => s.Funds), drop = g.Max(s => s.PriceDrop);
                string good = cat.Name(g.Key);
                if (drop >= 0.10)
                {
                    items.Add(new MiningItem
                    {
                        Magnitude = 2, Situation = "sale", Key = "mining:bigsale:" + g.Key + ":" + day,
                        Headline = o.Title + " price falls " + Pct(drop) + " after agency sale",
                        Text = "The agency sold " + Tonnes(t) + " of " + good + " on " + Exchange + " for " + Funds(f) + ". The sale was big enough to push the price of " + good
                            + " down by about " + Pct(drop) + ". Traders expect it to recover over the coming weeks as buyers work through the supply.",
                    });
                    continue;
                }
                // Routine sales: one running total per good per day.
                string mk = "day:" + g.Key;
                var parts = Get(memo, mk).Split('|');
                double dt = 0, df = 0;
                if (parts.Length == 3 && parts[0] == day.ToString(Inv)) { double.TryParse(parts[1], NumberStyles.Float, Inv, out dt); double.TryParse(parts[2], NumberStyles.Float, Inv, out df); }
                dt += t; df += f;
                Set(memo, mk, day.ToString(Inv) + "|" + dt.ToString("R", Inv) + "|" + df.ToString("R", Inv));
                items.Add(new MiningItem
                {
                    Magnitude = 1, Situation = "sale", Key = "mining:sale:" + g.Key + ":" + day,
                    Headline = "Agency sells " + Tonnes(dt) + " of " + good,
                    Text = "The agency sold " + Tonnes(dt) + " of " + good + " on " + Exchange + " for " + Funds(df) + ". The sale was small enough that the price barely moved.",
                });
            }
            return items;
        }

        /// <summary>Total income from ore passing 100 thousand, a million, ten million...</summary>
        public static MiningItem Earnings(Dictionary<string, string> memo, double earned)
        {
            int step = EarningSteps.Count(s => earned >= s), told = (int)Num(memo, "earnedStep");
            if (step <= told) return null;
            Set(memo, "earnedStep", step);
            double mark = EarningSteps[step - 1];
            return new MiningItem
            {
                Magnitude = mark >= 1000000 ? 2 : 1, Situation = "earnings", Key = "mining:earned:" + step,
                Headline = "Agency's ore income passes " + Funds(mark),
                Text = "The agency has now earned more than " + Funds(mark) + " from selling ore mined on other worlds (" + Funds(earned) + " in all). All of it was sold on the open " + Exchange + " exchange.",
            };
        }

        /// <summary>
        /// The weekly look at the exchange: any ore whose price moved 15% or more since the last look makes one small
        /// market item. Call once a day or so with today's raw price per tonne by ore id.
        /// </summary>
        public static MiningItem Prices(Dictionary<string, string> memo, MiningCatalog cat, IDictionary<string, double> rawPrices, double ut, double daySeconds)
        {
            if (rawPrices == null || rawPrices.Count == 0) return null;
            double day = ut / Math.Max(1, daySeconds), last = Num(memo, "pxDay");
            bool first = Get(memo, "pxDay") == "";
            if (!first && day - last < 7) return null;
            var moves = new List<KeyValuePair<MiningOre, double>>();
            foreach (var o in cat.Ores)
            {
                double now;
                if (!rawPrices.TryGetValue(o.Id, out now) || now <= 0) continue;
                double then = Num(memo, "px:" + o.Id);
                if (!first && then > 0 && Math.Abs(now / then - 1) >= 0.15) moves.Add(new KeyValuePair<MiningOre, double>(o, now / then - 1));
                Set(memo, "px:" + o.Id, now);
            }
            Set(memo, "pxDay", day);
            if (moves.Count == 0) return null;
            moves = moves.OrderByDescending(m => Math.Abs(m.Value)).ToList();
            var lines = moves.Select(m => m.Key.Title + " " + (m.Value > 0 ? "rose " : "fell ") + Pct(m.Value) + " to about " + Funds(rawPrices[m.Key.Id]) + " a tonne").ToList();
            var lead = moves[0];
            return new MiningItem
            {
                Magnitude = 1, Situation = "prices", Key = "mining:prices:" + Math.Floor(day),
                Headline = lead.Key.Title + (lead.Value > 0 ? " climbs " : " slides ") + Pct(lead.Value) + " on " + Exchange,
                Text = "On the " + Exchange + " commodities exchange over the past week: " + Join(lines) + ". These are raw ore prices. Ordinary swings in demand move them from week to week; nothing the agency did is behind this move.",
            };
        }

        /// <summary>One sentence for a crash story when the lost vessel was carrying ore ("" when it was not).</summary>
        public static string CargoLoss(MiningCatalog cat, IList<MiningCargo> cargo)
        {
            var goods = (cargo ?? new List<MiningCargo>()).Where(c => c != null && c.Tonnes >= 0.01 && cat.OreOf(c.Resource) != null).ToList();
            if (goods.Count == 0) return "";
            return "It was carrying " + CargoList(cat, goods) + ", worth about " + Funds(goods.Sum(c => c.Value)) + " on " + Exchange + ". The cargo was lost with it.";
        }
    }

    public partial class NewsEngine
    {
        /// <summary>
        /// Files a mining item where its size puts it: 2 and up becomes a news event (joining the vessel's mission
        /// story when there is one), 1 waits for the next background roundup and costs no call of its own.
        /// </summary>
        public Story ReportMining(MiningItem item, string vesselId, string vesselName, IEnumerable<string> crew, double ut)
        {
            if (item == null || item.Text == "") return null;
            if (item.Magnitude >= 2)
            {
                var e = new NewsEvent
                {
                    Type = EventTypes.Mining, UT = ut, VesselId = vesselId ?? "", VesselName = vesselName ?? "", Body = item.Body ?? "",
                    Situation = item.Situation, Detail = item.Text, Title = item.Headline, Weight = MiningNews.Weight(item.Magnitude),
                };
                if (crew != null) e.Crew.AddRange(crew);
                return Ingest(e);
            }
            if (Settings.WorldNews <= 0 || InSide()) return null;
            State.WorldPending.RemoveAll(f => f.Key == item.Key);
            State.WorldPending.Add(new WorldFact { Key = item.Key, Kind = "market", Headline = item.Headline, Text = item.Text, Body = item.Body ?? "", Sig = 1, UT = ut });
            State.WorldSeen[item.Key] = ut;
            return null;
        }
    }
}
