using System;
using System.Collections.Generic;
using System.Linq;
using KerbalNews;

/// <summary>News from the KSP Mining mod: sizes, firsts, the save-file memo, and the path into stories and roundups.</summary>
public static class MiningTests
{
    const double Day = 21600;
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    static MiningCatalog Catalog()
    {
        var c = new MiningCatalog();
        c.Ores.Add(new MiningOre { Id = "Platinoid", Title = "Platinoid", Tier = 1, Raw = "KSPM_PlatinoidRegolith", Refined = "KSPM_PGMConcentrate", Ingot = "KSPM_PlatinumIngot", RawValue = 6000, RefinedValue = 20400, IngotValue = 51000, Bodies = { "Mun", "Minmus" }, About = "Every wedding ring on Kerbin starts as grey dirt on the Mun.", Names = { { "KSPM_PlatinoidRegolith", "Platinoid Regolith" }, { "KSPM_PlatinumIngot", "Platinum Ingot" } } });
        c.Ores.Add(new MiningOre { Id = "Iridium", Title = "Iridium", Tier = 2, Raw = "KSPM_IridiumOre", Refined = "KSPM_IridiumSponge", Ingot = "KSPM_IridiumIngot", RawValue = 18000, RefinedValue = 61200, IngotValue = 153000, Bodies = { "Duna", "Ike", "Gilly" }, Names = { { "KSPM_IridiumOre", "Iridium Ore" } } });
        c.Ores.Add(new MiningOre { Id = "Gold", Title = "Gold", Tier = 4, Raw = "KSPM_GoldOre", Refined = "KSPM_GoldDust", Ingot = "KSPM_GoldBar", RawValue = 60000, RefinedValue = 204000, IngotValue = 510000, Bodies = { "Vall", "Laythe" }, Names = { { "KSPM_GoldOre", "Gold Ore" } } });
        return c;
    }

    static List<MiningCargo> Cargo(string res, double t, double price) { return new List<MiningCargo> { new MiningCargo { Resource = res, Tonnes = t, Value = t * price } }; }

    public static void TestMiningRecoverySizesFollowTheScale()
    {
        var cat = Catalog();
        var memo = new Dictionary<string, string>();
        MiningNews.Baseline(memo, cat, new string[0], 0);
        var first = MiningNews.Recovery(memo, cat, "Mun Hauler", Cargo("KSPM_PlatinoidRegolith", 12.4, 6000), new[] { "Kerbin", "Mun" }, 100);
        Assert(first.Magnitude == 5, "the first off-world ore ever is historic: " + first.Magnitude);
        Assert(first.Text.Contains("12.4 t of Platinoid Regolith") && first.Text.Contains("74,400 funds") && first.Text.Contains("from the Mun"), "exact figures and origin: " + first.Text);
        Assert(first.Text.Contains("wedding ring"), "the ore's lore rides along for reactions");
        Assert(first.Text.Contains("No other space program"), "rivals never mined first");

        var again = MiningNews.Recovery(memo, cat, "Mun Hauler II", Cargo("KSPM_PlatinoidRegolith", 5, 6000), null, 200);
        Assert(again.Magnitude == 1, "a routine cargo is small: " + again.Magnitude);

        var record = MiningNews.Recovery(memo, cat, "Mun Hauler III", Cargo("KSPM_PlatinoidRegolith", 14, 6000), null, 300);
        Assert(record.Magnitude == 2 && record.Text.Contains("74,400 funds"), "a record haul names the old record: " + record.Text);
        var bigRecord = MiningNews.Recovery(memo, cat, "Mun Hauler IV", Cargo("KSPM_PlatinoidRegolith", 40, 6000), null, 400);
        Assert(bigRecord.Magnitude == 3, "doubling the record is a real story");

        var iridium = MiningNews.Recovery(memo, cat, "Duna Hauler", Cargo("KSPM_IridiumOre", 1, 18000), new[] { "Ike" }, 500);
        Assert(iridium.Magnitude == 3 && iridium.Headline.Contains("iridium") && iridium.Body == "Ike", "a new low-tier ore: " + iridium.Magnitude + " " + iridium.Headline);
        var gold = MiningNews.Recovery(memo, cat, "Jool Hauler", Cargo("KSPM_GoldOre", 1, 60000), null, 600);
        Assert(gold.Magnitude == 4 && gold.Text.Contains("Vall and Laythe"), "a new high-tier ore is major: " + gold.Magnitude + " " + gold.Text);
        Assert(MiningNews.Recovery(memo, cat, "x", Cargo("LiquidFuel", 5, 1), null, 700) == null, "other resources are not ore");
    }

    public static void TestMiningBaselineTreatsAnExistingCareerAsOldNews()
    {
        var cat = Catalog();
        var memo = new Dictionary<string, string>();
        var log = new List<MiningSale> { new MiningSale { UT = 50, Resource = "KSPM_PlatinoidRegolith", Tonnes = 4, Funds = 22000 } };
        MiningNews.Baseline(memo, cat, new[] { "KSPM_PlatinoidRegolith" }, 1500000);
        MiningNews.BaselineSales(memo, log);
        MiningNews.Arrivals(memo, new Dictionary<string, double> { { "KSPM_PlatinoidRegolith", 6 } }, null);
        Assert(MiningNews.NewSales(memo, log).Count == 0, "sales already in the log are not reported");
        Assert(MiningNews.Earnings(memo, 1500000) == null, "a milestone already passed is not announced");
        Assert(MiningNews.Arrivals(memo, new Dictionary<string, double> { { "KSPM_PlatinoidRegolith", 6 } }, null).Count == 0, "what is already in the warehouse did not just arrive");
        var r = MiningNews.Recovery(memo, cat, "Hauler", Cargo("KSPM_PlatinoidRegolith", 1, 6000), null, 100);
        Assert(r.Magnitude == 1, "no false 'first ever' in a career that already mined: " + r.Magnitude);
        Assert(MiningNews.Earnings(memo, 10000000).Magnitude == 2, "the next milestone still counts");
    }

    public static void TestMiningWarehouseAndSalesDiffs()
    {
        var cat = Catalog();
        var memo = new Dictionary<string, string>();
        MiningNews.Baseline(memo, cat, new string[0], 0);
        MiningNews.BaselineSales(memo, null);
        var a = MiningNews.Arrivals(memo, new Dictionary<string, double> { { "KSPM_PlatinoidRegolith", 10 } }, null);
        Assert(a.Count == 1 && Math.Abs(a["KSPM_PlatinoidRegolith"] - 10) < 1e-9, "ten tonnes arrived");
        // Four sold and two more recovered in the same look: holdings 10 -> 8.
        var log = new List<MiningSale> { new MiningSale { UT = 900, Resource = "KSPM_PlatinoidRegolith", Tonnes = 4, Funds = 22544, PriceDrop = 0.09 } };
        var fresh = MiningNews.NewSales(memo, log);
        Assert(fresh.Count == 1 && MiningNews.NewSales(memo, log).Count == 0, "a sale is seen once");
        a = MiningNews.Arrivals(memo, new Dictionary<string, double> { { "KSPM_PlatinoidRegolith", 8 } }, new Dictionary<string, double> { { "KSPM_PlatinoidRegolith", 4 } });
        Assert(Math.Abs(a["KSPM_PlatinoidRegolith"] - 2) < 1e-9, "sold tonnes are added back");
        Assert(MiningNews.Arrivals(memo, new Dictionary<string, double>(), new Dictionary<string, double> { { "KSPM_PlatinoidRegolith", 8 } }).Count == 0, "selling out is not an arrival");
        Assert(!memo.Keys.Any(k => k.StartsWith("mining:inv:")), "sold-out goods leave the memo");

        var items = MiningNews.Sales(memo, cat, fresh, 900, Day);
        Assert(items.Count == 1 && items[0].Magnitude == 1 && items[0].Text.Contains("22,544 funds"), "a small sale is a small item");
        var more = MiningNews.Sales(memo, cat, new List<MiningSale> { new MiningSale { UT = 950, Resource = "KSPM_PlatinoidRegolith", Tonnes = 1, Funds = 5000 } }, 950, Day);
        Assert(more[0].Key == items[0].Key && more[0].Text.Contains("5 t") && more[0].Text.Contains("27,544 funds"), "same-day sales add up in one item: " + more[0].Text);
        var big = MiningNews.Sales(memo, cat, new List<MiningSale> { new MiningSale { UT = 990, Resource = "KSPM_IridiumOre", Tonnes = 20, Funds = 300000, PriceDrop = 0.31 } }, 990, Day);
        Assert(big[0].Magnitude == 2 && big[0].Headline.Contains("31%"), "flooding the market is a story: " + big[0].Headline);
    }

    public static void TestMiningDrillBarsEarningsAndPrices()
    {
        var cat = Catalog();
        var memo = new Dictionary<string, string>();
        MiningNews.Baseline(memo, cat, new string[0], 0);
        var d = MiningNews.Drill(memo, cat, "KSPM_PlatinoidRegolith", "Minmus", "Rig One");
        Assert(d != null && d.Magnitude == 2 && d.Body == "Minmus" && d.Text.Contains("first mining the agency has reported"), "first drill: " + (d != null ? d.Text : "null"));
        Assert(MiningNews.Drill(memo, cat, "KSPM_PlatinoidRegolith", "Minmus", "Rig One") == null, "told once per ore and world");
        var d2 = MiningNews.Drill(memo, cat, "KSPM_PlatinoidRegolith", "Mun", "Rig Two");
        Assert(d2 != null && !d2.Text.Contains("anywhere beyond Kerbin"), "a second world is its own smaller first");
        // A hauler that never landed anywhere still gets the right origin from the last dig.
        var r = MiningNews.Recovery(memo, cat, "Hauler", Cargo("KSPM_PlatinoidRegolith", 2, 6000), null, 10);
        Assert(r.Body == "Mun", "origin falls back to where the ore was last dug: " + r.Body);

        Assert(MiningNews.Bars(memo, cat, "KSPM_PlatinoidRegolith", "Mun", "Rig") == null, "raw ore is not a bar");
        var b = MiningNews.Bars(memo, cat, "KSPM_PlatinumIngot", "Mun", "Rig Two");
        Assert(b != null && b.Magnitude == 2 && b.Text.Contains("Platinum Ingot") && b.Text.Contains("51,000 funds"), "first bars: " + (b != null ? b.Text : "null"));
        Assert(MiningNews.Bars(memo, cat, "KSPM_PlatinumIngot", "Mun", "Rig Two") == null, "once");

        Assert(MiningNews.Earnings(memo, 50000) == null, "below the first mark");
        Assert(MiningNews.Earnings(memo, 120000).Magnitude == 1, "100 thousand is a small note");
        Assert(MiningNews.Earnings(memo, 130000) == null, "once per mark");
        Assert(MiningNews.Earnings(memo, 1200000).Magnitude == 2, "a million is a story");

        var px = new Dictionary<string, double> { { "Platinoid", 6000 }, { "Iridium", 18000 } };
        Assert(MiningNews.Prices(memo, cat, px, Day * 10, Day) == null, "the first look only remembers");
        px["Iridium"] = 14000; px["Platinoid"] = 6300;
        Assert(MiningNews.Prices(memo, cat, px, Day * 12, Day) == null, "not within the week");
        var p = MiningNews.Prices(memo, cat, px, Day * 17.5, Day);
        Assert(p != null && p.Magnitude == 1 && p.Headline.Contains("Iridium slides 22%") && !p.Text.Contains("Platinoid"), "only real moves are reported: " + (p != null ? p.Headline + " / " + p.Text : "null"));

        Assert(MiningNews.CargoLoss(cat, Cargo("KSPM_IridiumOre", 14, 18000)).Contains("14 t of Iridium Ore, worth about 252,000 funds"), "crash line");
        Assert(MiningNews.CargoLoss(cat, Cargo("LiquidFuel", 14, 1)) == "", "no ore, no line");
    }

    static NewsEngine Offline(NewsState s, CoreTests.FakeTransport t)
    {
        return new NewsEngine(s, new GlobalSettings { OfflineMode = true }, t, new SimpleClock(Day));
    }

    public static void TestMiningFirstOreMakesFrontPagesAndSmallSalesWaitForTheRoundup()
    {
        var cat = Catalog();
        var s = new NewsState();
        var t = new CoreTests.FakeTransport { Responder = CoreTests.AnthropicReply };
        var eng = Offline(s, t);
        eng.WorldScan = ut => new List<WorldFact>();
        eng.Tick(0, 0, true);
        MiningNews.Baseline(s.WorldMemo, cat, new string[0], 0);
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        eng.Ingest(new NewsEvent { Type = EventTypes.Recovery, UT = 5000, VesselId = "v1", VesselName = "Mun Hauler", Body = "Kerbin" });
        var item = MiningNews.Recovery(s.WorldMemo, cat, "Mun Hauler", Cargo("KSPM_PlatinoidRegolith", 12.4, 6000), new[] { "Mun" }, 5001);
        var story = eng.ReportMining(item, "v1", "Mun Hauler", null, 5001);
        Assert(story != null && story.Events.Count == 2 && story.Significance >= 80, "joins the mission's story and makes it historic: " + (story != null ? story.Significance.ToString() : "null"));

        // A routine sale costs no story of its own.
        var sale = MiningNews.Sales(s.WorldMemo, cat, new List<MiningSale> { new MiningSale { UT = 5100, Resource = "KSPM_PlatinoidRegolith", Tonnes = 2, Funds = 11000 } }, 5100, Day);
        Assert(eng.ReportMining(sale[0], "", "", null, 5100) == null && s.WorldPending.Count == 1 && s.WorldPending[0].Kind == "market", "a small sale waits for the roundup");
        var sale2 = MiningNews.Sales(s.WorldMemo, cat, new List<MiningSale> { new MiningSale { UT = 5200, Resource = "KSPM_PlatinoidRegolith", Tonnes = 1, Funds = 5000 } }, 5200, Day);
        eng.ReportMining(sale2[0], "", "", null, 5200);
        Assert(s.WorldPending.Count == 1 && s.WorldPending[0].Text.Contains("3 t"), "the day's sales stay one item: " + s.WorldPending[0].Text);

        eng.Tick(Day * 4, 1, true);
        for (int i = 0; i < 20; i++) eng.Tick(Day * 4 + i, 2 + i, true);
        Assert(t.Sent.Count == 0, "offline: no calls");
        var arts = s.Articles.Where(a => a.Headline.IndexOf("ore", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        Assert(arts.Count >= 3, "wide coverage of the first ore: " + arts.Count + " of " + s.Articles.Count);
        Assert(arts.Any(a => a.Outlet == "council") && arts.Any(a => a.Outlet == "science"), "officials and scientists weigh in: " + string.Join(",", arts.Select(a => a.Outlet).ToArray()));
        Assert(arts.All(a => a.Body.Contains("74,400 funds")), "every piece carries the exchange's own figure");
        Assert(s.Mood.Trust >= 50, "good news does not hurt trust");

        // The save remembers what was told, and a rollback forgets it.
        var node = new DataNode("KERBALNEWS"); s.Save(node);
        var back = NewsState.Load(node);
        Assert(MiningNews.Recovery(back.WorldMemo, cat, "Hauler 2", Cargo("KSPM_PlatinoidRegolith", 1, 6000), null, 9000).Magnitude == 1, "told firsts survive a save");
        var blank = new NewsState();
        MiningNews.Baseline(blank.WorldMemo, cat, new string[0], 0);
        Assert(MiningNews.Recovery(blank.WorldMemo, cat, "Hauler", Cargo("KSPM_PlatinoidRegolith", 1, 6000), null, 1).Magnitude == 5, "an older save has not heard of it");
    }

    public static void TestMiningRoundupAndSilentVessels()
    {
        var cat = Catalog();
        var s = new NewsState();
        var eng = Offline(s, new CoreTests.FakeTransport());
        eng.WorldScan = ut => new List<WorldFact>();
        eng.Tick(0, 0, true);
        MiningNews.Baseline(s.WorldMemo, cat, new string[0], 0);
        var px = new Dictionary<string, double> { { "Iridium", 18000 } };
        MiningNews.Prices(s.WorldMemo, cat, px, 0, Day);
        px["Iridium"] = 22000;
        eng.ReportMining(MiningNews.Prices(s.WorldMemo, cat, px, Day * 8, Day), "", "", null, Day * 8);
        Assert(s.WorldPending.Count == 1, "a price move waits for the roundup");
        Assert(eng.WorldRoundupNow(Day * 8), "roundup runs");
        var a = s.Articles.FirstOrDefault(x => x.Tags.Contains("markets"));
        Assert(a != null && a.Headline.Contains("Iridium climbs 22%") && (a.Outlet == "herald" || a.Outlet == "observer"), "market wrap lands on a business-minded desk: " + (a != null ? a.Outlet + " " + a.Headline : "none"));

        // A craft with telemetry off: its drill stays secret, its cargo at the Space Center does not.
        eng.TrackLaunch("q1", CoreTests.Craft(), Day * 9);
        s.SilentVessels.Add("q1");
        var drill = MiningNews.Drill(s.WorldMemo, cat, "KSPM_IridiumOre", "Ike", "Quiet Rig");
        Assert(eng.ReportMining(drill, "q1", "Quiet Rig", null, Day * 9) == null, "a silent rig's drilling is not news");
        var rec = MiningNews.Recovery(s.WorldMemo, cat, "Quiet Rig", Cargo("KSPM_IridiumOre", 3, 18000), null, Day * 9 + 10);
        Assert(eng.ReportMining(rec, "q1", "Quiet Rig", null, Day * 9 + 10) != null, "ore unloaded at the Space Center is public");
    }
}
