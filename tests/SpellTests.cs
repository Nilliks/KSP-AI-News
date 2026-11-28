using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

/// <summary>Spell check in the text boxes: the bundled lists, what is skipped, and the suggestions.</summary>
public static partial class WebTests
{
    static SpellCheck spellForTests;
    static SpellCheck Speller()
    {
        if (spellForTests != null) return spellForTests;
        var sc = new SpellCheck();
        sc.LoadFile("GameData/KerbalNews/Dictionary/en.txt", false);
        sc.LoadFile("GameData/KerbalNews/Dictionary/kerbal.txt", true);
        return spellForTests = sc;
    }

    static string Flagged(SpellCheck sc, string text, int skip = -1)
    {
        return string.Join(",", sc.Check(text, skip).Select(m => text.Substring(m.Start, m.Length)).ToArray());
    }

    public static void TestSpellCheckFlagsTypos()
    {
        var sc = Speller();
        if (sc.Count < 80000) throw new Exception("word list too small: " + sc.Count);
        Eq("recieve,teh", Flagged(sc, "We will recieve teh payload tomorrow."), "typos");
        Eq("", Flagged(sc, "Jebediah Kerman's capsule reached Mun orbit; Kerbals cheered at the KSC."), "game words");
        Eq("", Flagged(sc, "Aerobraking at Duna, then a Hohmann transfer to Ike. Apoapsis 80 km, periapsis 72km."), "spaceflight words");
        Eq("", Flagged(sc, "Don't panic, it's the rocket's fault, isn't it? The crews' quarters."), "contractions and possessives");
        Eq("", Flagged(sc, "Visit kerbalnews.kb or mail director@agency, the Mk1 pod, LV-T45, NASA, KTV, vesselName, x"), "codes, addresses, acronyms");
        Eq("", Flagged(sc, "The Kerbinian press loved the Dunas."), "game word endings");
        Eq("", Flagged(sc, "The colour and the color"), "british and american");
    }

    public static void TestSpellCheckSkipsWordBeingTyped()
    {
        var sc = Speller();
        Eq("", Flagged(sc, "Launch the rok", 14), "caret at the end of the word");
        Eq("rok", Flagged(sc, "Launch the rok ", 15), "flagged once the caret moves on");
        Eq("rok", Flagged(sc, "Launch the rok"), "flagged when not typing");
    }

    public static void TestSpellCheckGameAndUserWords()
    {
        var sc = new SpellCheck();
        sc.LoadList(new[] { "@10", "the", "launch", "rocket" }, false);
        Eq("Zorblax", Flagged(sc, "the Zorblax rocket"), "unknown name");
        sc.AddGameWord("Zorblax Mk2 Heavy Lifter");
        Eq("", Flagged(sc, "the Zorblax rocket"), "craft name learned");
        Eq("", Flagged(sc, "the Zorblaxes"), "plural of a game word");
        Eq("Zorblax", sc.Suggest("zorblx").FirstOrDefault(), "suggested with its own capitals");
        sc.Ignore("Quuxly");
        Eq("", Flagged(sc, "the quuxly rocket"), "ignored");
    }

    public static void TestSpellCheckSuggestions()
    {
        var sc = Speller();
        Eq("receive", sc.Suggest("recieve").FirstOrDefault(), "transposed letters");
        Eq("The", sc.Suggest("Teh").FirstOrDefault(), "keeps the capital");
        Eq("Kerbin", sc.Suggest("Kerbn").FirstOrDefault(), "game word");
        if (!sc.Suggest("rocekt").Contains("rocket")) throw new Exception("rocekt: " + string.Join(",", sc.Suggest("rocekt").ToArray()));
        if (!sc.Suggest("accomodation").Contains("accommodation")) throw new Exception("accomodation: " + string.Join(",", sc.Suggest("accomodation").ToArray()));
        if (!sc.Suggest("trajectroy").Contains("trajectory")) throw new Exception("trajectroy");
    }

    public static void TestSpellCheckIsFast()
    {
        var sc = Speller();
        var text = string.Join(" ", Enumerable.Repeat("The Kerbal Space Program launched its rokcet towards the Mun yesterday, and the press was delighted.", 40).ToArray());
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 20; i++) sc.Check(text);
        double perCheck = sw.Elapsed.TotalMilliseconds / 20;
        if (perCheck > 5) throw new Exception("checking a 640-word box took " + perCheck.ToString("0.0") + " ms");
        sw = Stopwatch.StartNew();
        sc.Suggest("acommodatoin");
        if (sw.Elapsed.TotalMilliseconds > 400) throw new Exception("suggestions took " + sw.Elapsed.TotalMilliseconds.ToString("0") + " ms");
    }

    public static void TestSpellCheckAttribute()
    {
        var root = Html.Parse("<input name=a><input name=b spellcheck=\"false\"><input name=c type=password><textarea name=d></textarea>");
        Css.Apply(root, Css.Parse(""));
        var page = Layout.Run(root, 600, CommunityTests.FakeMeasure);
        Eq("True,False,False,True", string.Join(",", page.Inputs.Select(i => i.SpellCheck.ToString()).ToArray()), "spellcheck attribute");
    }
}
