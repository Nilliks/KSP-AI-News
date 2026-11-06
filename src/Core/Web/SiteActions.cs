using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews.Web
{
    /// <summary>What the browser should do after a page action.</summary>
    public class ActionResult
    {
        /// <summary>Go to this URL (adds a history entry). Null means stay on the page.</summary>
        public string Navigate;
        /// <summary>Re-render the current page, keeping the scroll position.</summary>
        public bool Refresh = true;
    }

    /// <summary>
    /// Handles the page's "action:..." links and data-action buttons. Kept out of the KSP layer so it is unit-tested.
    /// Form values come from the page's inputs, keyed by name.
    /// </summary>
    public partial class Sites
    {
        /// <summary>Called after settings change, so they are written to disk.</summary>
        public Action SaveSettings = () => { };
        /// <summary>Raised when something finishes in the background (a connection test, a model list) and the page should refresh.</summary>
        public Action Changed = () => { };
        public bool Busy;

        public ActionResult Handle(string action, IDictionary<string, string> form)
        {
            form = form ?? new Dictionary<string, string>();
            var r = new ActionResult();
            if (action.StartsWith("action:")) action = action.Substring(7);
            string arg = "";
            int slash = action.IndexOf('/');
            string verb = slash < 0 ? action : action.Substring(0, slash);
            if (slash >= 0) arg = Uri.UnescapeDataString(action.Substring(slash + 1));
            var g = Engine.Settings;

            switch (verb)
            {
                case "search":
                    r.Navigate = Home + "/search?q=" + Uri.EscapeDataString(Get(form, "q").Trim());
                    break;
                case "rewrite":
                    TestResult = Engine.Rewrite(arg, Now()) ? "" : TestResult;
                    break;
                case "silliness":
                    int lvl;
                    if (int.TryParse(arg, NumberStyles.Integer, Inv, out lvl)) State.Silliness = Math.Max(0, Math.Min(10, lvl));
                    break;
                case "remove-lore":
                    int li;
                    if (int.TryParse(arg, NumberStyles.Integer, Inv, out li) && li >= 0 && li < State.Lore.Count) State.Lore.RemoveAt(li);
                    break;
                case "add-lore":
                    string lore = Get(form, "lore").Trim();
                    if (lore != "") State.Lore.Add(new LoreEntry { UT = Now(), Text = lore });
                    break;
                case "save-profile":
                    var p = State.Profile;
                    p.Name = Get(form, "name", p.Name).Trim();
                    p.ShortName = Get(form, "short", p.ShortName).Trim();
                    p.Founding = Get(form, "founding", p.Founding);
                    p.Mission = Get(form, "mission", p.Mission);
                    p.Politics = Get(form, "politics", p.Politics);
                    p.Reputation = Get(form, "reputation", p.Reputation);
                    p.KeyPeople = Get(form, "people", p.KeyPeople);
                    p.WorldLore = Get(form, "world", p.WorldLore);
                    p.HouseRules = Get(form, "rules", p.HouseRules);
                    break;
                case "provider":
                    if (Providers.All.Any(x => x.Id == arg)) { g.ProviderId = arg; TestResult = ""; FetchResult = ""; SaveSettings(); }
                    break;
                case "writer":
                    g.WriterModels[g.ProviderId] = arg; SaveSettings();
                    break;
                case "cheap":
                    g.CheapModels[g.ProviderId] = arg; SaveSettings();
                    break;
                case "toggle-offline": g.OfflineMode = !g.OfflineMode; SaveSettings(); break;
                case "toggle-notify": g.NotifyOnPublish = !g.NotifyOnPublish; SaveSettings(); break;
                case "toggle-warp": g.GenerateDuringWarp = !g.GenerateDuringWarp; SaveSettings(); break;
                case "toggle-pressphotos": g.PressPhotos = !g.PressPhotos; SaveSettings(); break;
                case "usage": g.ApplyUsage(arg); SaveSettings(); break;
                case "usage-quiet":
                case "usage-amount":
                case "usage-outlets":
                case "usage-writer":
                case "usage-stream":
                case "toggle-retro":
                {
                    double v;
                    double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                    if (verb == "usage-quiet") g.QuietDays = Math.Max(0, v);
                    else if (verb == "usage-amount") g.AmountScale = Math.Max(0.3, Math.Min(2, v));
                    else if (verb == "usage-outlets") g.MaxOutlets = Math.Max(0, (int)v);
                    else if (verb == "usage-writer") g.WriterFrom = arg == "major" || arg == "brief" ? arg : "story";
                    else if (verb == "usage-stream") g.StreamSeconds = Math.Max(10, v);
                    else g.Retrospectives = !g.Retrospectives;
                    g.Usage = g.UsageMatch;
                    SaveSettings();
                    break;
                }
                case "toggle-crewchat": g.CrewChatOn = !g.CrewChatOn; SaveSettings(); break;
                case "toggle-ktv": g.KtvOn = !g.KtvOn; g.Usage = g.UsageMatch; SaveSettings(); break;
                case "toggle-mods-context": g.ModsInContext = !g.ModsInContext; SaveSettings(); break;
                case "mod-toggle":
                    foreach (var md in g.Mods) if (string.Equals(md.Folder, arg, StringComparison.OrdinalIgnoreCase)) md.Include = !md.Include;
                    SaveSettings();
                    break;
                case "save-mod-notes":
                    for (int mi = 0; mi < g.Mods.Count; mi++)
                    {
                        string mk = "note" + mi.ToString(CultureInfo.InvariantCulture);
                        if (form.ContainsKey(mk)) g.Mods[mi].Note = ResponseParser.Clip(form[mk].Trim(), 200);
                    }
                    SaveSettings();
                    break;
                case "rescan-mods": modScanNote = RescanMods(); SaveSettings(); break;
                case "toggle-assistant": g.AssistantOn = !g.AssistantOn; SaveSettings(); break;
                case "toggle-assistant-confirm": g.AssistantConfirm = !g.AssistantConfirm; SaveSettings(); break;
                case "assistant-send":
                    Engine.AssistantSend(Get(form, "ask"), Now());
                    break;
                case "assistant-ask":
                    Engine.AssistantSend(arg, Now());
                    break;
                case "assistant-yes": Engine.AssistantDecide(arg, true, Now()); break;
                case "assistant-no": Engine.AssistantDecide(arg, false, Now()); break;
                case "assistant-clear": Engine.AssistantClear(); break;
                case "eng-send": Engine.EngSend(Get(form, "ask"), Now()); break;
                case "eng-ask": Engine.EngSend(arg, Now()); break;
                case "eng-clear": Engine.EngClear(); break;
                case "eng-plan": Engine.EngPlanMission(Get(form, "mission"), Now()); break;
                case "eng-investigate": Engine.EngInvestigate(arg, Now()); break;
                case "eng-share": Engine.EngShareFindings(arg, Now()); break;
                case "stream-chat":
                {
                    int k;
                    if (int.TryParse(arg, out k)) { g.StreamChatPer10Min = Math.Max(10, Math.Min(600, k)); SaveSettings(); }
                    break;
                }
                case "toggle-site":
                    if (SiteCatalog.Get(arg) != null) { g.SetSite(arg, !g.SiteOn(arg)); SaveSettings(); }
                    break;
                case "toggle-tieins": g.ContractTieIns = !g.ContractTieIns; SaveSettings(); break;
                case "toggle-engineering": g.EngineeringOn = !g.EngineeringOn; SaveSettings(); break;
                case "assistant-unorder": Engine.RemoveOrder(arg); break;
                case "assistant-order":
                    string order = Get(form, "order").Trim();
                    if (order != "") State.Orders.Add(new LoreEntry { UT = Now(), Text = ResponseParser.Clip(order, 300) });
                    break;
                case "scale":
                    float cur = g.UiScale > 0.4f ? g.UiScale : UiScale();
                    if (arg == "auto") g.UiScale = 0;
                    else g.UiScale = (float)Math.Round(Math.Max(0.6f, Math.Min(4f, cur + (arg == "+" ? 0.1f : -0.1f))), 1);
                    SaveSettings();
                    break;
                case "save-settings":
                case "test-connection":
                    ApplySettingsForm(g, form);
                    SaveSettings();
                    if (verb == "save-settings") { TestResult = "Saved."; TestOk = true; }
                    else if (!Busy)
                    {
                        Busy = true;
                        TestResult = "Testing " + g.Preset.Name + " with " + g.WriterModel + "...";
                        TestOk = true;
                        Engine.TestConnection((ok, msg) => { Busy = false; TestOk = ok; TestResult = msg; Changed(); });
                    }
                    break;
                case "fetch-models":
                    if (!Busy)
                    {
                        Busy = true;
                        FetchResult = "Asking " + g.Preset.Name + "...";
                        Engine.FetchModels((ok, msg) => { Busy = false; FetchResult = msg; if (ok) SaveSettings(); Changed(); });
                    }
                    break;
                case "publish-now":
                    TestResult = Engine.SkipDelay(Now()) + " stories moved up. They will be written on the next tick.";
                    TestOk = true;
                    break;
                case "mark-read":
                    Engine.MarkAllRead(Now());
                    break;
                case "privacy":
                    int s2 = arg.LastIndexOf('/');
                    if (s2 > 0) State.SetSilent(arg.Substring(0, s2), arg.Substring(s2 + 1) == "silent");
                    break;
                case "privacy-default":
                    State.SilentByDefault = arg == "silent";
                    break;
                default:
                    var cr = HandleCommunity(verb, arg, form) ?? HandlePress(verb, arg, form) ?? HandleLive(verb, arg, form);
                    if (cr != null) return cr;
                    r.Refresh = false;
                    break;
            }
            return r;
        }

        static void ApplySettingsForm(GlobalSettings g, IDictionary<string, string> form)
        {
            string id = g.ProviderId;
            string key = Get(form, "key").Trim();
            if (key != "")
            {
                g.Keys[id] = key;
                g.OfflineMode = false;   // pasting a key means "use AI"
            }
            if (form.ContainsKey("writer")) g.WriterModels[id] = form["writer"].Trim();
            if (form.ContainsKey("cheap")) g.CheapModels[id] = form["cheap"].Trim();
            if (form.ContainsKey("baseUrl")) g.BaseUrls[id] = form["baseUrl"].Trim();
            int mt;
            if (int.TryParse(Get(form, "maxTokens").Trim(), NumberStyles.Integer, Inv, out mt)) g.MaxTokens = Math.Max(1000, Math.Min(32000, mt));
        }

        static string Get(IDictionary<string, string> form, string k, string def = "")
        {
            string v;
            return form.TryGetValue(k, out v) && v != null ? v : def;
        }
    }
}
