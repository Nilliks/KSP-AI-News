using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>Plays the announcer: 16-bit PCM from ElevenLabs (or a saved replay clip) through one 2D audio source.</summary>
    public static class AnnouncerAudio
    {
        static AudioSource src;

        static AudioSource Src
        {
            get
            {
                if (src == null)
                {
                    var go = new GameObject("KerbalNewsAnnouncer");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    src = go.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.spatialBlend = 0f;
                    src.loop = false;
                }
                return src;
            }
        }

        public static void Play(byte[] pcm, int rate)
        {
            if (pcm == null || pcm.Length < 4) return;
            try
            {
                int n = pcm.Length / 2;
                var data = new float[n];
                for (int i = 0; i < n; i++) data[i] = (short)(pcm[2 * i] | pcm[2 * i + 1] << 8) / 32768f;
                var clip = AudioClip.Create("KerbalNewsVoice", n, 1, rate, false);
                clip.SetData(data, 0);
                var s = Src;
                var old = s.clip;
                s.Stop();
                s.clip = clip;
                s.volume = Volume;
                s.Play();
                if (old != null) UnityEngine.Object.Destroy(old);
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Announcer audio: " + e.Message); }
        }

        public static void Stop()
        {
            if (src != null) src.Stop();
        }

        static float Volume { get { var g = NewsSystem.Settings; return g != null ? (float)(g.VoiceVolume * g.StreamVolume) : 0.8f; } }

        /// <summary>The player's volume slider moved: the line playing now follows it.</summary>
        public static void ApplyVolume() { if (src != null) src.volume = Volume; }
    }

    /// <summary>The stream's sound bed: control-room ambience on a loop, and crowd and launch-roar effects, one source each.</summary>
    public static class StreamSounds
    {
        static readonly Dictionary<string, AudioSource> sources = new Dictionary<string, AudioSource>();
        static readonly Dictionary<string, float> levels = new Dictionary<string, float>();
        static float Master { get { var g = NewsSystem.Settings; return g != null ? (float)g.StreamVolume : 1f; } }

        public static void ApplyVolume()
        {
            foreach (var kv in sources) { float l; if (kv.Value != null && levels.TryGetValue(kv.Key, out l)) kv.Value.volume = l * Master; }
        }
        static GameObject host;

        static AudioSource Src(string channel)
        {
            AudioSource s;
            if (sources.TryGetValue(channel, out s) && s != null) return s;
            if (host == null) { host = new GameObject("KerbalNewsSounds"); UnityEngine.Object.DontDestroyOnLoad(host); }
            s = host.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            sources[channel] = s;
            return s;
        }

        public static void Play(string channel, byte[] pcm, bool loop, double volume)
        {
            if (pcm == null || pcm.Length < 4) return;
            try
            {
                int n = pcm.Length / 2;
                var data = new float[n];
                for (int i = 0; i < n; i++) data[i] = (short)(pcm[2 * i] | pcm[2 * i + 1] << 8) / 32768f;
                var clip = AudioClip.Create("KerbalNewsSound", n, 1, Voice.Rate, false);
                clip.SetData(data, 0);
                var s = Src(channel);
                var old = s.clip;
                s.Stop();
                s.clip = clip;
                s.loop = loop;
                levels[channel] = (float)Math.Max(0, Math.Min(1, volume));
                s.volume = levels[channel] * Master;
                s.Play();
                if (old != null) UnityEngine.Object.Destroy(old);
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Stream sound: " + e.Message); }
        }

        public static void Stop(string channel)
        {
            foreach (var kv in sources)
                if (kv.Value != null && (channel == null || kv.Key == channel)) kv.Value.Stop();
        }
    }

    /// <summary>
    /// Replays on disk, beside each save (saves/&lt;save&gt;/KerbalNews/replays/&lt;folder&gt;): index.json, f00001.jpg frames and
    /// v001.wav announcer clips. Also the cache of short announcer clips (GameData/KerbalNews/PluginData/VoiceCache).
    /// </summary>
    public static class ReplayStore
    {
        static string SavesRoot { get { return Path.Combine(KSPUtil.ApplicationRootPath, "saves"); } }
        static string SaveDir { get { return Path.Combine(SavesRoot, (HighLogic.SaveFolder ?? "default") + "/KerbalNews/replays"); } }
        static string CacheDir { get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalNews/PluginData/VoiceCache"); } }

        public static string FolderPath(string folder) { return Path.Combine(SaveDir, Path.GetFileName(folder ?? "x")); }

        public static void SaveFile(string folder, string file, byte[] bytes)
        {
            if (bytes == null) return;
            string dir = FolderPath(folder), path = Path.Combine(dir, Path.GetFileName(file));
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Directory.CreateDirectory(dir); File.WriteAllBytes(path, bytes); }
                catch (Exception e) { Debug.LogWarning("[KerbalNews] Could not save replay file " + file + ": " + e.Message); }
            });
        }

        public static void SaveIndex(string folder, string json)
        {
            try
            {
                string dir = FolderPath(folder);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "index.json"), json);
                usedAt = -100;
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Could not save replay index: " + e.Message); }
        }

        /// <summary>Replay folders beside the current save that have an index, so they can be watched.</summary>
        public static List<string> Folders()
        {
            try
            {
                if (!Directory.Exists(SaveDir)) return new List<string>();
                return new DirectoryInfo(SaveDir).GetDirectories().Where(d => File.Exists(Path.Combine(d.FullName, "index.json"))).Select(d => d.Name).ToList();
            }
            catch (Exception) { return new List<string>(); }
        }

        public static string LoadIndex(string folder)
        {
            try
            {
                string p = Path.Combine(FolderPath(folder), "index.json");
                return File.Exists(p) ? File.ReadAllText(p) : null;
            }
            catch (Exception) { return null; }
        }

        static IEnumerable<DirectoryInfo> AllReplays() { return AllReplays(SavesRoot); }

        static IEnumerable<DirectoryInfo> AllReplays(string savesRoot)
        {
            if (!Directory.Exists(savesRoot)) yield break;
            foreach (var save in new DirectoryInfo(savesRoot).GetDirectories())
            {
                var r = new DirectoryInfo(Path.Combine(save.FullName, "KerbalNews/replays"));
                if (!r.Exists) continue;
                foreach (var d in r.GetDirectories()) yield return d;
            }
        }

        static long Size(DirectoryInfo d)
        {
            try { return d.GetFiles().Sum(f => f.Length); } catch (Exception) { return 0; }
        }

        static long used;
        static float usedAt = -100;

        static volatile bool counting;

        /// <summary>
        /// The space every replay takes, as last counted. Counting reads the size of every frame file, which took most
        /// of a second on the Settings page, so it runs off the game's thread and the page shows the last answer.
        /// </summary>
        public static long DiskUse()
        {
            if (counting || Time.realtimeSinceStartup - usedAt < 30) return used;
            usedAt = Time.realtimeSinceStartup;
            counting = true;
            string root = SavesRoot;   // read here: game paths belong to the game's thread
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                long n = 0;
                try { n = AllReplays(root).Sum(Size); } catch (Exception) { n = used; }
                used = n;
                counting = false;
            });
            return used;
        }

        /// <summary>Deletes the oldest replays (across all saves) until they fit in the space allowed. Never the one recording.</summary>
        public static void Prune(int maxGb, string recording)
        {
            try
            {
                long cap = (long)Math.Max(1, maxGb) << 30;
                var all = AllReplays().Select(d => new { d, size = Size(d), at = d.LastWriteTimeUtc }).OrderBy(x => x.at).ToList();
                long total = all.Sum(x => x.size);
                foreach (var x in all)
                {
                    if (total <= cap) break;
                    if (x.d.Name == recording) continue;
                    try { x.d.Delete(true); total -= x.size; Debug.Log("[KerbalNews] Deleted old stream replay " + x.d.Name + " to stay under " + maxGb + " GB"); }
                    catch (Exception e) { Debug.LogWarning("[KerbalNews] Could not delete replay " + x.d.Name + ": " + e.Message); }
                }
                usedAt = -100;
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Replay clean-up: " + e.Message); }
        }

        // ---- frames

        /// <summary>The stream's current video frame, saved as the replay's next frame without holding the game up for it.</summary>
        public static void SaveFrame(string folder, int n)
        {
            var rt = StreamDriver.Frame;
            if (rt == null) return;
            string path = Path.Combine(FolderPath(folder), Path.GetFileName(NewsEngine.FrameFile(n)));
            FrameGrab.Grab(rt, (px, w, h) => FrameGrab.SaveJpg(px, w, h, 70, path));
        }

        /// <summary>What the replay player shows; the browser points a RawImage at it and it is reloaded in place.</summary>
        public static Texture2D Texture { get; private set; }
        static string shownFolder = "";
        static int shown = -1;

        public static void EnsureTexture()
        {
            if (Texture == null)
            {
                Texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                Texture.SetPixel(0, 0, Color.black); Texture.SetPixel(1, 0, Color.black); Texture.SetPixel(0, 1, Color.black); Texture.SetPixel(1, 1, Color.black);
                Texture.Apply();
            }
        }

        public static void ShowFrame(string folder, int n)
        {
            EnsureTexture();
            if (n == shown && folder == shownFolder) return;
            shown = n; shownFolder = folder;
            if (n <= 0) return;
            try
            {
                string p = Path.Combine(FolderPath(folder), NewsEngine.FrameFile(n));
                if (File.Exists(p)) ImageConversion.LoadImage(Texture, File.ReadAllBytes(p));
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Replay frame load: " + e.Message); }
        }

        public static void PlayVoice(string folder, string file)
        {
            try
            {
                string p = Path.Combine(FolderPath(folder), Path.GetFileName(file));
                if (File.Exists(p)) AnnouncerAudio.Play(Voice.FromWav(File.ReadAllBytes(p)), Voice.Rate);
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Replay voice: " + e.Message); }
        }

        // ---- voice cache

        static string BankRoot { get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalNews/Voice"); } }
        static Dictionary<string, string> bank;

        /// <summary>Clips that ship with the mod: GameData/KerbalNews/Voice/&lt;voice-model-style&gt;/&lt;key&gt;.pcm, found once at startup.</summary>
        static Dictionary<string, string> Bank
        {
            get
            {
                if (bank != null) return bank;
                bank = new Dictionary<string, string>();
                try
                {
                    if (Directory.Exists(BankRoot))
                        foreach (var f in Directory.GetFiles(BankRoot, "*.pcm", SearchOption.AllDirectories)) bank[Path.GetFileNameWithoutExtension(f)] = f;
                    if (bank.Count > 0) Debug.Log("[KerbalNews] " + bank.Count + " announcer lines ship with the mod");
                }
                catch (Exception e) { Debug.LogWarning("[KerbalNews] Voice bank: " + e.Message); }
                return bank;
            }
        }

        /// <summary>A recorded clip: this player's cache first, then the lines that ship with the mod.</summary>
        public static byte[] CacheGet(string key)
        {
            try
            {
                string name = Path.GetFileName(key);
                string p = Path.Combine(CacheDir, name + ".pcm");
                if (File.Exists(p)) return File.ReadAllBytes(p);
                string b;
                return Bank.TryGetValue(name, out b) && File.Exists(b) ? File.ReadAllBytes(b) : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Copies recorded clips into GameData/KerbalNews/Voice/&lt;name&gt; with an index of their words, ready to ship.</summary>
        public static void ExportBank(string name, List<KeyValuePair<string, string>> lines)
        {
            try
            {
                string dir = Path.Combine(BankRoot, Path.GetFileName(name));
                Directory.CreateDirectory(dir);
                var index = new System.Text.StringBuilder();
                int n = 0;
                foreach (var kv in lines)
                {
                    string src = Path.Combine(CacheDir, Path.GetFileName(kv.Key) + ".pcm");
                    if (!File.Exists(src)) continue;
                    File.Copy(src, Path.Combine(dir, Path.GetFileName(kv.Key) + ".pcm"), true);
                    index.AppendLine(kv.Key + "\t" + kv.Value);
                    n++;
                }
                File.WriteAllText(Path.Combine(dir, "lines.txt"), index.ToString());
                bank = null;
                Debug.Log("[KerbalNews] Exported " + n + " announcer lines to " + dir);
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Voice bank export: " + e.Message); }
        }

        public static void CachePut(string key, byte[] pcm)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllBytes(Path.Combine(CacheDir, Path.GetFileName(key) + ".pcm"), pcm);
                // Keep the cache small: past 60 MB the oldest clips go.
                var files = new DirectoryInfo(CacheDir).GetFiles("*.pcm").OrderBy(f => f.LastWriteTimeUtc).ToList();
                long total = files.Sum(f => f.Length);
                foreach (var f in files)
                {
                    if (total < 60L << 20) break;
                    total -= f.Length;
                    f.Delete();
                }
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Voice cache: " + e.Message); }
        }
    }
}
