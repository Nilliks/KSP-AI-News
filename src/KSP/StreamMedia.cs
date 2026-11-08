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
                s.volume = NewsSystem.Settings != null ? (float)NewsSystem.Settings.VoiceVolume : 0.8f;
                s.Play();
                if (old != null) UnityEngine.Object.Destroy(old);
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Announcer audio: " + e.Message); }
        }

        public static void Stop()
        {
            if (src != null) src.Stop();
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

        static IEnumerable<DirectoryInfo> AllReplays()
        {
            if (!Directory.Exists(SavesRoot)) yield break;
            foreach (var save in new DirectoryInfo(SavesRoot).GetDirectories())
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

        public static long DiskUse()
        {
            if (Time.realtimeSinceStartup - usedAt < 10) return used;
            usedAt = Time.realtimeSinceStartup;
            try { used = AllReplays().Sum(Size); } catch (Exception) { used = 0; }
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

        static Texture2D grab;

        /// <summary>The stream's current video frame, saved as the replay's next frame.</summary>
        public static void SaveFrame(string folder, int n)
        {
            var rt = StreamDriver.Frame;
            if (rt == null) return;
            var prev = RenderTexture.active;
            try
            {
                if (grab == null) grab = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                grab.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                grab.Apply();
                SaveFile(folder, NewsEngine.FrameFile(n), ImageConversion.EncodeToJPG(grab, 70));
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Replay frame: " + e.Message); }
            finally { RenderTexture.active = prev; }
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

        public static byte[] CacheGet(string key)
        {
            try
            {
                string p = Path.Combine(CacheDir, Path.GetFileName(key) + ".pcm");
                return File.Exists(p) ? File.ReadAllBytes(p) : null;
            }
            catch (Exception) { return null; }
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
