using KSP.Localization;

namespace KerbalNews.KSP
{
    /// <summary>Stock craft carry localization tags as names (#autoLOC_501232 is "Kerbal X"). Resolve them before
    /// anything is written, shown or saved.</summary>
    static class Names
    {
        public static string Of(Vessel v) { return v == null ? "" : Of(v.vesselName); }

        public static string Of(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("#")) return name ?? "";
            try { var s = Localizer.Format(name); return string.IsNullOrEmpty(s) ? name : s; }
            catch { return name; }
        }
    }
}
