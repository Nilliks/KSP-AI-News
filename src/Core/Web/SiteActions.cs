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
                case "mail-search": case "mail-reply": case "mail-send": case "mail-quick": case "mail-assist": case "mail-auto": case "mail-read":
                case "mail-delete": case "mail-restore": case "mail-empty-trash":
                    KmailAction(verb, arg, form, r);
                    break;
                case "wiki-search":
                    r.Navigate = WikiHost + "/search?q=" + Uri.EscapeDataString(Get(form, "wq").Trim());
                    break;
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
                case "gallery-key":
                    if (arg == "off") g.GalleryKey = ""; else if (GlobalSettings.GalleryKeys.Contains(arg)) g.GalleryKey = arg;
                    SaveSettings();
                    break;
                case "usage-clear":
                    if (arg == "cancel") { ConfirmUsageClear = false; break; }
                    if (arg != "yes") { ConfirmUsageClear = true; break; }
                    ConfirmUsageClear = false;
                    {
                        string since = DateTime.Now.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
                        g.UsageAll.Clear(since); g.UsageSession.Clear(since); State.Usage.Clear(since);
                    }
                    SaveSettings();
                    break;
                case "reset-settings":
                    if (arg == "cancel") { ConfirmReset = false; break; }
                    if (!ConfirmReset) { ConfirmReset = true; break; }
                    ConfirmReset = false;
                    g.ResetToDefaults();
                    Engine.ResetVoice();
                    SaveSettings();
                    TestResult = "Every setting is back to how the mod ships. Your keys, and which service and model does each job, were kept."; TestOk = true;
                    break;
                case "toggle-offline": g.OfflineMode = !g.OfflineMode; SaveSettings(); break;
                case "toggle-notify": g.NotifyOnPublish = !g.NotifyOnPublish; SaveSettings(); break;
                case "toggle-warp": g.GenerateDuringWarp = !g.GenerateDuringWarp; SaveSettings(); break;
                case "toggle-tune": ShowTune = !ShowTune; break;
                case "toggle-mailpopups": g.MailPopups = !g.MailPopups; SaveSettings(); break;
                case "toggle-spellcheck": g.SpellCheck = !g.SpellCheck; SaveSettings(); break;
                case "toggle-mail-auto": Engine.State.MailAuto = !Engine.State.MailAuto; break;
                case "news-delay":
                {
                    double d;
                    if (double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) g.NewsDelay = Math.Max(0.2, Math.Min(4, d));
                    SaveSettings();
                    break;
                }
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
                case "ai-amount":
                {
                    int w;
                    if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out w)) g.ApplyAiAmount(w);
                    SaveSettings();
                    break;
                }
                case "usage-mail":
                {
                    int w;
                    if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out w)) g.MailLevel = Math.Max(0, Math.Min(3, w));
                    SaveSettings();
                    break;
                }
                case "usage-world":
                {
                    int w;
                    if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out w)) g.WorldNews = Math.Max(0, Math.Min(3, w));
                    g.Usage = g.UsageMatch;
                    SaveSettings();
                    break;
                }
                case "toggle-world-flavor": g.WorldFlavor = !g.WorldFlavor; SaveSettings(); break;
                case "toggle-world-rivals": g.WorldRivals = !g.WorldRivals; SaveSettings(); break;
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
                case "eng-tab": EngTab = arg; EngMenu = ""; break;
                case "eng-menu": EngMenu = EngMenu == arg ? "" : arg; break;
                case "eng-level": if (arg == "short" || arg == "normal" || arg == "teach") State.EngLevel = arg; EngMenu = ""; break;
                case "eng-saved": { List<string[]> saved = null; int at; try { saved = Engine.EngineeringSaved(); } catch { } if (saved != null && int.TryParse(arg, out at) && at >= 0 && at < saved.Count && Engine.EngOpenSaved(saved[at][2])) EngTab = "overview"; break; }
                case "eng-live": Engine.EngPinned = null; break;
                case "eng-notes": EngNotesFilter = Uri.UnescapeDataString(arg); EngNoteEdit = ""; break;
                case "eng-note-add": Engine.EngRemember("note", "", Get(form, "notetext"), Now(), true); break;
                case "eng-note-pin": { var note = State.EngNotes.FirstOrDefault(x => x.Id == arg); if (note != null) note.Pinned = !note.Pinned; break; }
                case "eng-note-del": State.EngNotes.RemoveAll(x => x.Id == arg); break;
                case "eng-note-edit": EngNoteEdit = EngNoteEdit == arg ? "" : arg; break;
                case "eng-note-save": { var note = State.EngNotes.FirstOrDefault(x => x.Id == arg); string t = Get(form, "noteedit").Trim(); if (note != null && t != "") note.Text = ResponseParser.Clip(t, 700); EngNoteEdit = ""; break; }
                case "eng-meet-len": { int n; if (int.TryParse(arg, out n)) EngMeetLen = Math.Max(1, Math.Min(100, n)); break; }
                case "eng-meet-new": EngMeetView = "new"; break;
                case "eng-meet-view": EngMeetView = arg; break;
                case "eng-meet-start": case "eng-meet-follow":
                {
                    int n; if (int.TryParse(Get(form, "calls").Trim(), out n) && n >= 1) EngMeetLen = Math.Min(100, n);
                    var prev = action == "eng-meet-follow" ? State.EngMeetings.FirstOrDefault(x => x.Id == EngMeetView) ?? State.EngMeetings.LastOrDefault() : null;
                    var mt = Engine.EngMeetingStart(Get(form, "objective"), EngMeetLen, Now(), prev != null ? prev.Id : "");
                    if (mt != null) EngMeetView = mt.Id;
                    break;
                }
                case "eng-meet-say": Engine.EngMeetingSay(Get(form, "meetsay"), Now()); break;
                case "eng-meet-stop": Engine.EngMeetingStop(Now()); break;
                case "eng-meet-decide": Engine.EngMeetingDecide(arg, Now()); break;
                case "eng-meet-tick": { int cut = arg.LastIndexOf('/'); int i; if (cut > 0 && int.TryParse(arg.Substring(cut + 1), out i)) Engine.EngMeetingTick(arg.Substring(0, cut), i); break; }
                case "eng-snap": { var snap = Engine.EngTakeSnapshot(Get(form, "snapname")); if (snap != null) { EngCompareId = snap.Id; EngTab = "compare"; } break; }
                case "eng-snap-del": State.EngSnaps.RemoveAll(x => x.Id == arg); if (EngCompareId == arg) EngCompareId = ""; break;
                case "eng-compare": EngCompareId = arg; EngTab = "compare"; break;
                case "eng-margin": { int pct; if (int.TryParse(arg, out pct) && pct >= 0 && pct <= 100) State.EngMargin = pct; EngMenu = ""; break; }
                case "eng-pick": { int cut = arg.IndexOf('/'); if (cut > 0) EngPick(arg.Substring(0, cut), Uri.UnescapeDataString(arg.Substring(cut + 1))); EngMenu = ""; Engine.EngNoteMission(Now()); break; }
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
                case "zoom":
                    g.PageZoom = arg == "reset" ? 1f : (float)Math.Round(Math.Max(0.6f, Math.Min(2.5f, g.PageZoom + (arg == "+" ? 0.1f : -0.1f))), 1);
                    SaveSettings();
                    break;
                case "scale":
                    float cur = g.UiScale > 0.4f ? g.UiScale : UiScale();
                    if (arg == "auto") g.UiScale = 0;
                    else g.UiScale = (float)Math.Round(Math.Max(0.6f, Math.Min(4f, cur + (arg == "+" ? 0.1f : -0.1f))), 1);
                    SaveSettings();
                    break;
                case "save-settings":
                    ApplySettingsForm(g, form);
                    SaveSettings();
                    TestResult = "Saved."; TestOk = true;
                    break;
                case "publish-now":
                    TestResult = Engine.SkipDelay(Now()) + " stories moved up. They will be written on the next tick.";
                    TestOk = true;
                    break;
                case "mark-read":
                    Engine.MarkAllRead(Now());
                    TestResult = "Everything is marked read."; TestOk = true;
                    break;
                case "privacy":
                    int s2 = arg.LastIndexOf('/');
                    if (s2 > 0) State.SetSilent(arg.Substring(0, s2), arg.Substring(s2 + 1) == "silent");
                    break;
                case "telemetry-remove":
                {
                    var tv = State.Tracked.Values.FirstOrDefault(t => t.VesselId == arg);
                    if (tv != null && !tv.Flags.Contains("hidden")) tv.Flags.Add("hidden");   // off the list; its news history stays
                    break;
                }
                case "privacy-default":
                    State.SilentByDefault = arg == "silent";
                    break;
                default:
                    var cr = HandleServices(verb, arg, form) ?? HandleCommunity(verb, arg, form) ?? HandlePress(verb, arg, form) ?? HandleLive(verb, arg, form) ?? HandleScience(verb, arg, form) ?? HandleVoice(verb, arg, form);
                    if (cr != null) return cr;
                    r.Refresh = false;
                    break;
            }
            return r;
        }

        static void ApplySettingsForm(GlobalSettings g, IDictionary<string, string> form)
        {
            // The Advanced numbers. Keys, addresses and models are saved by their own cards and jobs (SitesServices).
            int mt;
            if (int.TryParse(Get(form, "maxTokens").Trim(), NumberStyles.Integer, Inv, out mt)) g.MaxTokens = Math.Max(1000, Math.Min(32000, mt));
            if (int.TryParse(Get(form, "timeout").Trim(), NumberStyles.Integer, Inv, out mt)) g.TimeoutSeconds = Math.Max(20, Math.Min(900, mt));
            if (int.TryParse(Get(form, "localContext").Trim(), NumberStyles.Integer, Inv, out mt)) g.LocalContext = Math.Max(2048, Math.Min(262144, mt));
            if (int.TryParse(Get(form, "localTimeout").Trim(), NumberStyles.Integer, Inv, out mt)) g.LocalTimeoutSeconds = Math.Max(60, Math.Min(3600, mt));
        }

        static string Get(IDictionary<string, string> form, string k, string def = "")
        {
            string v;
            return form.TryGetValue(k, out v) && v != null ? v : def;
        }
    }
}
