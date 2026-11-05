using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.UI.Screens;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// The in-game news site: a browser-style window opened from the toolbar in every scene.
    /// IMGUI for v1, so it needs no asset bundles and works with any UI scale mod.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class NewsWindow : MonoBehaviour
    {
        enum Page { Front, Outlet, Article, Kerbnet, Tracker, Agency, Settings, Search }

        static bool visible;           // remembered across scenes
        static Rect rect = new Rect(160, 80, 820, 600);
        static Page page = Page.Front;
        static string outletId = "herald";
        static string articleId;

        ApplicationLauncherButton button;
        Texture2D icon;
        Vector2 scroll;
        string search = "";
        string loreDraft = "";
        bool showKey;
        string testResult = "";
        bool testing;
        int windowId;
        const string LockId = "KerbalNewsWindowLock";

        GUIStyle h1, h2, h3, small, body, link, chip, dot, boxed, mast;
        bool stylesReady;

        NewsEngine Engine { get { return NewsSystem.Instance != null ? NewsSystem.Instance.Engine : null; } }
        static double Now { get { return Planetarium.GetUniversalTime(); } }

        void Start()
        {
            windowId = GetHashCode();
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Add(RemoveButton);
            if (ApplicationLauncher.Ready) AddButton();
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Remove(RemoveButton);
            RemoveButton(GameScenes.MAINMENU);
            InputLockManager.RemoveControlLock(LockId);
        }

        void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null) return;
            icon = GameDatabase.Instance.GetTexture("KerbalNews/Icons/button", false) ?? FallbackIcon();
            button = ApplicationLauncher.Instance.AddModApplication(
                () => visible = true, () => visible = false, null, null, null, null,
                ApplicationLauncher.AppScenes.SPACECENTER | ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW |
                ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.TRACKSTATION,
                icon);
            if (visible) button.SetTrue(false);
        }

        void RemoveButton(GameScenes s)
        {
            if (button != null && ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(button);
            button = null;
        }

        static Texture2D FallbackIcon()
        {
            var t = new Texture2D(38, 38, TextureFormat.ARGB32, false);
            for (int x = 0; x < 38; x++)
                for (int y = 0; y < 38; y++)
                    t.SetPixel(x, y, (x > 6 && x < 31 && y > 8 && y < 30) ? (y > 24 ? new Color(0.85f, 0.2f, 0.15f) : Color.white) : new Color(0, 0, 0, 0));
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------------ styles

        void InitStyles()
        {
            if (stylesReady) return;
            var skin = HighLogic.Skin != null ? HighLogic.Skin : GUI.skin;
            mast = new GUIStyle(skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, richText = true };
            h1 = new GUIStyle(skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = true, richText = true };
            h2 = new GUIStyle(skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = true, richText = true };
            h3 = new GUIStyle(skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = true, richText = true };
            small = new GUIStyle(skin.label) { fontSize = 11, wordWrap = true, richText = true };
            small.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            body = new GUIStyle(skin.label) { fontSize = 13, wordWrap = true, richText = false };
            link = new GUIStyle(skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = true, richText = true };
            link.hover.textColor = new Color(1f, 0.85f, 0.5f);
            chip = new GUIStyle(skin.button) { fontSize = 11 };
            dot = new GUIStyle(skin.label) { fontSize = 14, richText = true };
            boxed = new GUIStyle(skin.box) { padding = new RectOffset(8, 8, 6, 6) };
            stylesReady = true;
        }

        // ------------------------------------------------------------------ window

        void OnGUI()
        {
            if (!visible || Engine == null || !NewsSystem.Instance.HasGame)
            {
                InputLockManager.RemoveControlLock(LockId);
                return;
            }
            InitStyles();
            var skin = GUI.skin;
            if (HighLogic.Skin != null) GUI.skin = HighLogic.Skin;
            int unread = Engine.UnreadCount(Now);
            rect = GUILayout.Window(windowId, rect, DrawWindow, "Kerbal News Network" + (unread > 0 ? "  (" + unread + " unread)" : ""));
            rect.x = Mathf.Clamp(rect.x, 0, Screen.width - 100);
            rect.y = Mathf.Clamp(rect.y, 0, Screen.height - 60);
            GUI.skin = skin;

            // Keep clicks and scrolling over the window from reaching the game.
            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (rect.Contains(mouse))
                InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS | ControlTypes.KSC_ALL | ControlTypes.EDITOR_SOFT_LOCK | ControlTypes.TRACKINGSTATION_ALL, LockId);
            else
                InputLockManager.RemoveControlLock(LockId);
        }

        void DrawWindow(int id)
        {
            DrawAddressBar();
            GUILayout.BeginHorizontal();
            Tab("Front page", Page.Front);
            Tab("Outlets", Page.Outlet);
            Tab("Kerbnet", Page.Kerbnet);
            Tab("Universe", Page.Tracker);
            Tab("Search", Page.Search);
            Tab("Agency", Page.Agency);
            Tab("Settings", Page.Settings);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(26))) { visible = false; if (button != null) button.SetFalse(false); }
            GUILayout.EndHorizontal();

            switch (page)
            {
                case Page.Front: DrawFront(); break;
                case Page.Outlet: DrawOutlet(); break;
                case Page.Article: DrawArticle(); break;
                case Page.Kerbnet: DrawKerbnet(); break;
                case Page.Tracker: DrawTracker(); break;
                case Page.Agency: DrawAgency(); break;
                case Page.Settings: DrawSettings(); break;
                case Page.Search: DrawSearch(); break;
            }
            DrawStatusBar();
            DrawResizeHandle();
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        void DrawAddressBar()
        {
            string url = "kerbalnews.kb/";
            if (page == Page.Outlet) { var o = Outlets.Get(outletId); url = o != null ? o.Domain : url; }
            else if (page == Page.Article && articleId != null) { var a = Find(articleId); if (a != null) url = (Outlets.Get(a.Outlet) != null ? Outlets.Get(a.Outlet).Domain : "") + "/" + Slug(a.Headline); }
            else if (page != Page.Front) url += page.ToString().ToLowerInvariant();
            GUILayout.BeginHorizontal(boxed);
            GUILayout.Label("<b>●</b>  " + url, small);
            GUILayout.FlexibleSpace();
            GUILayout.Label(new KspClock().FormatDate(Now), small);
            GUILayout.EndHorizontal();
        }

        void Tab(string label, Page p)
        {
            bool on = page == p || (p == Page.Outlet && page == Page.Article);
            if (GUILayout.Toggle(on, label, chip) && !on) { page = p; scroll = Vector2.zero; }
        }

        void DrawStatusBar()
        {
            var e = Engine;
            string s = e.Settings.CanCallAi ? e.Settings.Preset.Name : "Offline templates";
            s += "  |  " + e.Status;
            if (e.PendingCount > 0) s += "  |  " + e.PendingCount + " stories in the works";
            if (e.LastError != "") s += "  |  <color=#ff8a80>" + Clip(e.LastError, 120) + "</color>";
            GUILayout.Label(s, small);
        }

        Vector2 resizeStart;
        bool resizing;

        void DrawResizeHandle()
        {
            var handle = new Rect(rect.width - 18, rect.height - 18, 16, 16);
            GUI.Label(handle, "◢", small);
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && handle.Contains(ev.mousePosition)) { resizing = true; resizeStart = ev.mousePosition; ev.Use(); }
            if (resizing && ev.type == EventType.MouseDrag)
            {
                rect.width = Mathf.Clamp(rect.width + ev.mousePosition.x - resizeStart.x, 560, Screen.width);
                rect.height = Mathf.Clamp(rect.height + ev.mousePosition.y - resizeStart.y, 380, Screen.height);
                resizeStart = ev.mousePosition;
                ev.Use();
            }
            if (ev.type == EventType.MouseUp) resizing = false;
        }

        // ------------------------------------------------------------------ pages

        List<Article> Visible() { return Engine.State.PublishedArticles(Now).ToList(); }
        Article Find(string id) { return Engine.State.Articles.FirstOrDefault(a => a.Id == id); }

        void Open(Article a)
        {
            articleId = a.Id;
            a.Read = true;
            page = Page.Article;
            scroll = Vector2.zero;
        }

        void DrawFront()
        {
            var st = Engine.State;
            var all = Visible();
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#ffd27f>KERBAL NEWS NETWORK</color>", mast);
            GUILayout.FlexibleSpace();
            if (all.Any(a => !a.Read) && GUILayout.Button("Mark all read", chip)) Engine.MarkAllRead(Now);
            GUILayout.EndHorizontal();
            DrawMood(st.Mood);

            scroll = GUILayout.BeginScrollView(scroll);
            if (all.Count == 0)
            {
                GUILayout.Label("No news yet. Launch something: the press will report on it about a day later.", body);
                if (Engine.PendingCount > 0) GUILayout.Label(Engine.PendingCount + " stories are being written.", small);
            }
            else
            {
                // Top story: the biggest thing in the last few days.
                double day = new KspClock().DaySeconds;
                var top = all.Where(a => Now - a.PublishedUT < 3 * day).OrderByDescending(a => a.Significance).ThenByDescending(a => a.PublishedUT).FirstOrDefault() ?? all[0];
                GUILayout.BeginVertical(boxed);
                GUILayout.Label(OutletTag(top.Outlet) + "  <i>" + Kind(top.Kind) + "</i>", small);
                if (GUILayout.Button(top.Headline, h1)) Open(top);
                if (top.Subhead != "") GUILayout.Label(top.Subhead, body);
                GUILayout.Label(Excerpt(top.Body, 260), small);
                GUILayout.EndVertical();
                GUILayout.Space(6);
                GUILayout.Label("Latest", h3);
                foreach (var a in all.Where(x => x != top).Take(40)) Headline(a);
            }
            GUILayout.EndScrollView();
        }

        void DrawMood(Mood m)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Public excitement " + Bar(m.Excitement) + "   Trust " + Bar(m.Trust), small);
            var c = m.Concerns.Where(kv => kv.Value >= 10).OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key).ToArray();
            if (c.Length > 0) GUILayout.Label("   Worried about: " + string.Join(", ", c), small);
            GUILayout.EndHorizontal();
        }

        static string Bar(double v)
        {
            int n = (int)Math.Round(v / 10);
            string col = v >= 60 ? "#8bc34a" : v >= 35 ? "#ffc107" : "#ef5350";
            return "<color=" + col + ">" + new string('█', Math.Max(0, n)) + "</color><color=#555555>" + new string('█', Math.Max(0, 10 - n)) + "</color> " + v.ToString("0");
        }

        void Headline(Article a)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(a.Read ? "  " : "<color=#ffd27f>●</color>", dot, GUILayout.Width(16));
            GUILayout.BeginVertical();
            if (GUILayout.Button(a.Headline, link)) Open(a);
            GUILayout.Label(OutletTag(a.Outlet) + "  " + new KspClock().FormatDate(a.PublishedUT) + "  " + Kind(a.Kind) + (a.Comments.Count > 0 ? "  " + a.Comments.Count + " comments" : ""), small);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        static string OutletTag(string id)
        {
            var o = Outlets.Get(id);
            if (o == null) return id;
            return "<color=" + Lighten(o.Color) + "><b>" + o.Name + "</b></color>";
        }

        /// <summary>Outlet colors are print colors; brighten them for a dark window.</summary>
        static string Lighten(string hex)
        {
            Color c;
            if (!ColorUtility.TryParseHtmlString(hex, out c)) return "#ffffff";
            c = Color.Lerp(c, Color.white, 0.45f);
            return "#" + ColorUtility.ToHtmlStringRGB(c);
        }

        static string Kind(string k)
        {
            switch (k)
            {
                case "analysis": return "Analysis";
                case "opinion": return "Opinion";
                case "feature": return "Feature";
                case "expert": return "Expert view";
                case "statement": return "Official statement";
                default: return "News";
            }
        }

        void DrawOutlet()
        {
            GUILayout.BeginHorizontal();
            foreach (var o in Outlets.All)
                if (GUILayout.Toggle(outletId == o.Id, o.Name, chip) && outletId != o.Id) { outletId = o.Id; scroll = Vector2.zero; }
            GUILayout.EndHorizontal();
            var outlet = Outlets.Get(outletId);
            if (outlet == null) return;
            GUILayout.Label("<color=" + Lighten(outlet.Color) + ">" + outlet.Name.ToUpperInvariant() + "</color>", mast);
            GUILayout.Label(outlet.Style, small);
            scroll = GUILayout.BeginScrollView(scroll);
            var list = Visible().Where(a => a.Outlet == outletId).ToList();
            if (list.Count == 0) GUILayout.Label("Nothing published here yet.", body);
            foreach (var a in list.Take(60)) Headline(a);
            GUILayout.EndScrollView();
        }

        void DrawArticle()
        {
            var a = articleId != null ? Find(articleId) : null;
            if (a == null || a.PublishedUT > Now) { page = Page.Front; return; }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< Back", chip, GUILayout.Width(70))) { page = Page.Front; return; }
            GUILayout.FlexibleSpace();
            GUILayout.Label("Written by " + (a.Model == "" ? (a.Offline ? "offline templates" : "AI") : a.Model), small);
            if (GUILayout.Button("Rewrite with current model", chip)) { Engine.Rewrite(a.StoryId, Now); page = Page.Front; return; }
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(OutletTag(a.Outlet) + "  ·  " + Kind(a.Kind), small);
            GUILayout.Label(a.Headline, h1);
            if (a.Subhead != "") GUILayout.Label(a.Subhead, h3);
            GUILayout.Label((a.Reporter != "" ? "By " + a.Reporter + "  ·  " : "") + new KspClock().FormatDate(a.PublishedUT), small);
            GUILayout.Space(6);
            foreach (var para in a.Body.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                GUILayout.Label(para.Trim(), body);
                GUILayout.Space(4);
            }
            if (a.Tags.Count > 0) GUILayout.Label("Tags: " + string.Join(", ", a.Tags.ToArray()), small);

            var related = Engine.State.PublishedArticles(Now).Where(x => x.StoryId == a.StoryId && x.Id != a.Id).ToList();
            if (related.Count > 0)
            {
                GUILayout.Space(8);
                GUILayout.Label("Other coverage of this story", h3);
                foreach (var r in related) Headline(r);
            }

            GUILayout.Space(8);
            GUILayout.Label("Comments (" + a.Comments.Count + ")", h2);
            foreach (var c in a.Comments)
            {
                GUILayout.BeginVertical(boxed);
                GUILayout.Label("<b>" + Esc(c.Author) + "</b>  <color=#999999>▲ " + c.Likes + "</color>", small);
                GUILayout.Label(c.Text, body);
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
        }

        void DrawKerbnet()
        {
            GUILayout.Label("<color=#7fc8ff>Kerbnet</color>  <size=11>what the public is saying</size>", mast);
            scroll = GUILayout.BeginScrollView(scroll);
            var posts = Engine.State.Social.Where(p => p.UT <= Now).OrderByDescending(p => p.UT).Take(120).ToList();
            if (posts.Count == 0) GUILayout.Label("Quiet on Kerbnet. Big news gets people talking.", body);
            foreach (var p in posts)
            {
                GUILayout.BeginVertical(boxed);
                GUILayout.Label("<b>" + Esc(p.Handle) + "</b>" + (p.Persona != "" ? "  <color=#999999>" + Esc(p.Persona) + "</color>" : "") + "  <color=#777777>" + new KspClock().FormatDate(p.UT) + "</color>", small);
                GUILayout.Label(p.Text, body);
                GUILayout.Label("♥ " + Compact(p.Likes) + "    ⟲ " + Compact(p.Reshares), small);
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
        }

        void DrawTracker()
        {
            var d = Snapshots.Digest(Engine.State);
            GUILayout.Label("State of the universe", h2);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.BeginHorizontal();
            Stat("Satellites", d.Satellites);
            Stat("Stations", d.Stations);
            Stat("Bases", d.Bases);
            Stat("Kerbals in space", d.CrewInSpace);
            Stat("Tracked debris", d.Debris);
            Stat("Lost crew", d.CrewLost);
            GUILayout.EndHorizontal();
            if (d.CrewInSpaceNames.Count > 0) GUILayout.Label("In space now: " + string.Join(", ", d.CrewInSpaceNames.ToArray()), body);
            if (d.DebrisByBody.Count > 0)
                GUILayout.Label("Debris by body: " + string.Join(", ", d.DebrisByBody.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value).ToArray()), body);
            var hist = Engine.State.DigestHistory;
            if (hist.Count > 1)
            {
                GUILayout.Space(6);
                GUILayout.Label("Debris over time  " + Spark(hist.Select(h => (double)h.Debris).ToList()), small);
                GUILayout.Label("Satellites over time  " + Spark(hist.Select(h => (double)h.Satellites).ToList()), small);
            }
            if (Engine.State.Records.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label("Firsts and records", h3);
                foreach (var r in Engine.State.Records) GUILayout.Label("• " + r, body);
            }
            if (Engine.State.MemorySummary != "")
            {
                GUILayout.Space(6);
                GUILayout.Label("The story so far (archive summary)", h3);
                GUILayout.Label(Engine.State.MemorySummary, body);
            }
            GUILayout.EndScrollView();
        }

        void Stat(string label, int v)
        {
            GUILayout.BeginVertical(boxed, GUILayout.MinWidth(100));
            GUILayout.Label("<size=20><b>" + v + "</b></size>", h2);
            GUILayout.Label(label, small);
            GUILayout.EndVertical();
        }

        static string Spark(List<double> vals)
        {
            const string bars = "▁▂▃▄▅▆▇█";
            var v = vals.Skip(Math.Max(0, vals.Count - 40)).ToList();
            double max = Math.Max(1, v.Max());
            return new string(v.Select(x => bars[(int)Math.Round(x / max * 7)]).ToArray()) + "  (now " + v.Last() + ")";
        }

        void DrawSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search the archive:", GUILayout.Width(130));
            search = GUILayout.TextField(search);
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);
            var q = search.Trim().ToLowerInvariant();
            var list = Visible().Where(a => q == "" || a.Headline.ToLowerInvariant().Contains(q) || a.Body.ToLowerInvariant().Contains(q)
                || a.Tags.Any(t => t.Contains(q)) || Outlets.NameOf(a.Outlet).ToLowerInvariant().Contains(q)).Take(100).ToList();
            GUILayout.Label(list.Count + " results", small);
            foreach (var a in list) Headline(a);
            GUILayout.EndScrollView();
        }

        void DrawAgency()
        {
            var st = Engine.State;
            var p = st.Profile;
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("Agency profile", h2);
            GUILayout.Label("Everything here is treated by the press as fixed fact. It is stored in this save.", small);
            p.Name = Field("Agency name", p.Name);
            p.ShortName = Field("Short name", p.ShortName);
            p.Founding = Area("Founding story", p.Founding);
            p.Mission = Area("Mission statement and goals", p.Mission);
            p.Politics = Area("Funding and politics", p.Politics);
            p.Reputation = Area("Public reputation so far", p.Reputation);
            p.KeyPeople = Area("Key people", p.KeyPeople);
            p.WorldLore = Area("World lore", p.WorldLore);
            p.HouseRules = Area("House rules for the press", p.HouseRules);

            GUILayout.Space(8);
            GUILayout.Label("Tone", h2);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Silliness " + st.Silliness + " / 10", GUILayout.Width(120));
            st.Silliness = Mathf.RoundToInt(GUILayout.HorizontalSlider(st.Silliness, 0, 10));
            GUILayout.EndHorizontal();
            GUILayout.Label(PromptBuilder.SillinessText(st.Silliness), small);

            GUILayout.Space(8);
            GUILayout.Label("Lore journal", h2);
            GUILayout.Label("Dated entries become canon from today on.", small);
            foreach (var l in st.Lore.OrderBy(l => l.UT).ToList())
            {
                GUILayout.BeginHorizontal(boxed);
                GUILayout.Label(new KspClock().FormatDate(l.UT) + ": " + l.Text, body);
                if (GUILayout.Button("Remove", chip, GUILayout.Width(70))) st.Lore.Remove(l);
                GUILayout.EndHorizontal();
            }
            loreDraft = GUILayout.TextArea(loreDraft, GUILayout.MinHeight(44));
            if (GUILayout.Button("Add entry dated today", chip) && loreDraft.Trim() != "")
            {
                st.Lore.Add(new LoreEntry { UT = Now, Text = loreDraft.Trim() });
                loreDraft = "";
            }
            GUILayout.EndScrollView();
        }

        string Field(string label, string v)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(170));
            v = GUILayout.TextField(v ?? "");
            GUILayout.EndHorizontal();
            return v;
        }

        string Area(string label, string v)
        {
            GUILayout.Label(label, h3);
            return GUILayout.TextArea(v ?? "", GUILayout.MinHeight(40));
        }

        void DrawSettings()
        {
            var g = NewsSystem.Settings;
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("AI provider", h2);
            GUILayout.Label("Shared by all saves. Keys are stored in GameData/KerbalNews/PluginData/secrets.cfg, never in your save.", small);
            bool offline = GUILayout.Toggle(g.OfflineMode, " Offline mode (template articles, no AI calls)");
            if (offline != g.OfflineMode) { g.OfflineMode = offline; NewsSystem.Instance.SaveSettings(); }

            int perRow = 4, i = 0;
            GUILayout.BeginHorizontal();
            foreach (var p in Providers.All)
            {
                if (i > 0 && i % perRow == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                if (GUILayout.Toggle(g.ProviderId == p.Id, p.Name, chip) && g.ProviderId != p.Id) { g.ProviderId = p.Id; testResult = ""; NewsSystem.Instance.SaveSettings(); }
                i++;
            }
            GUILayout.EndHorizontal();

            var preset = g.Preset;
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("API key", GUILayout.Width(120));
            string key = g.Key;
            string newKey = showKey ? GUILayout.TextField(key) : GUILayout.PasswordField(key, '*');
            showKey = GUILayout.Toggle(showKey, "show", GUILayout.Width(60));
            GUILayout.EndHorizontal();
            if (newKey != key) g.Keys[g.ProviderId] = newKey.Trim();
            if (!preset.NeedsKey) GUILayout.Label("No key needed for this provider.", small);
            else if (preset.KeyHint != "") GUILayout.Label("Looks like " + preset.KeyHint, small);

            g.BaseUrls[g.ProviderId] = OverrideField("Endpoint", g.BaseUrls, g.ProviderId, preset.BaseUrl);
            g.WriterModels[g.ProviderId] = OverrideField("Writer model", g.WriterModels, g.ProviderId, preset.WriterModel);
            g.CheapModels[g.ProviderId] = OverrideField("Cheap model", g.CheapModels, g.ProviderId, preset.CheapModel);
            GUILayout.Label("The writer model handles big stories. The cheap model handles briefs, quiet-day features and archive summaries. Leave a field empty for the default shown.", small);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Max output tokens", GUILayout.Width(120));
            int mt;
            if (int.TryParse(GUILayout.TextField(g.MaxTokens.ToString(CultureInfo.InvariantCulture), GUILayout.Width(80)), out mt)) g.MaxTokens = Mathf.Clamp(mt, 1000, 32000);
            GUILayout.EndHorizontal();
            g.NotifyOnPublish = GUILayout.Toggle(g.NotifyOnPublish, " Show a message when news is published");
            g.GenerateDuringWarp = GUILayout.Toggle(g.GenerateDuringWarp, " Allow AI calls during time warp");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save settings", chip)) { NewsSystem.Instance.SaveSettings(); testResult = "Saved."; }
            GUI.enabled = !testing;
            if (GUILayout.Button(testing ? "Testing..." : "Test connection", chip))
            {
                NewsSystem.Instance.SaveSettings();
                testing = true;
                testResult = "";
                Engine.TestConnection((ok, msg) => { testing = false; testResult = (ok ? "<color=#8bc34a>" : "<color=#ff8a80>") + Esc(msg) + "</color>"; });
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (testResult != "") GUILayout.Label(testResult, small);

            GUILayout.Space(10);
            GUILayout.Label("Testing tools", h2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Publish pending news now", chip)) testResult = Engine.SkipDelay(Now) + " stories moved up.";
            GUILayout.EndHorizontal();
            GUILayout.Label("Normally news arrives about one Kerbin day after an event. This skips the wait.", small);
            GUILayout.EndScrollView();
        }

        string OverrideField(string label, Dictionary<string, string> d, string id, string def)
        {
            string cur;
            d.TryGetValue(id, out cur);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(120));
            cur = GUILayout.TextField(cur ?? "");
            GUILayout.Label("default: " + def, small, GUILayout.Width(230));
            GUILayout.EndHorizontal();
            return cur.Trim();
        }

        // ------------------------------------------------------------------ helpers

        static string Excerpt(string s, int n)
        {
            s = (s ?? "").Replace("\n", " ");
            return Esc(s.Length <= n ? s : s.Substring(0, n).TrimEnd() + "...");
        }

        static string Clip(string s, int n) { return s.Length <= n ? s : s.Substring(0, n) + "..."; }

        /// <summary>Rich-text labels would interpret model output like "&lt;b&gt;"; neutralize angle brackets.</summary>
        static string Esc(string s) { return (s ?? "").Replace("<", "‹").Replace(">", "›"); }

        static string Compact(int n)
        {
            if (n >= 1000000) return (n / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (n >= 1000) return (n / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return n.ToString(CultureInfo.InvariantCulture);
        }

        static string Slug(string s)
        {
            var chars = (s ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
            var slug = new string(chars);
            while (slug.Contains("--")) slug = slug.Replace("--", "-");
            slug = slug.Trim('-');
            return slug.Length > 48 ? slug.Substring(0, 48) : slug;
        }
    }
}
