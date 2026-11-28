using System;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Copies a picture off the graphics card without stopping the game for it. Texture2D.ReadPixels makes the game wait
    /// until the card has finished everything it was doing, which is a small hitch every time; the stream took one for
    /// every replay frame and one a second to check the picture. An asynchronous readback asks for the copy and gets it
    /// a frame or two later. JPEG encoding and the file write then happen on a worker thread.
    ///
    /// Not every graphics back end supports this, and they don't all agree on which way up the copy comes back, so the
    /// first asynchronous copy is checked against an ordinary one of the same frame. If they match (either way up) the
    /// answer is remembered; if they don't, or anything fails, the ordinary copy is used from then on.
    /// </summary>
    static class FrameGrab
    {
        static bool? flipped;         // null until the first asynchronous copy has been checked
        static bool off, encodeOnMain;
        static int errors;
        static Texture2D reader, encoder;

        public static int AsyncGrabs { get; private set; }
        public static int SyncGrabs { get; private set; }
        public static int FramesWritten;
        /// <summary>One line for the log and the self-test.</summary>
        public static string State
        {
            get
            {
                return (UseAsync ? "async readback" + (flipped == null ? " (not checked yet)" : flipped.Value ? " (rows flipped)" : "") : "ordinary readback")
                    + ", " + AsyncGrabs + " async and " + SyncGrabs + " ordinary copies, " + FramesWritten + " frames written" + (encodeOnMain ? ", encoding on the main thread" : "");
            }
        }

        static bool UseAsync { get { return !off && SystemInfo.supportsAsyncGPUReadback; } }

        /// <summary>
        /// Hands over the texture's pixels as RGBA bytes, rows running bottom to top (as ReadPixels gives them). The
        /// callback runs on the game's thread: at once when the ordinary copy is used, a frame or two later otherwise.
        /// A copy that fails is simply not delivered.
        /// </summary>
        public static void Grab(RenderTexture rt, Action<byte[], int, int> done)
        {
            if (rt == null || done == null) return;
            int w = rt.width, h = rt.height;
            if (!UseAsync) { Deliver(done, Sync(rt), w, h); return; }
            // Until the orientation is known, take an ordinary copy of the same frame to compare with (and to deliver).
            byte[] reference = flipped == null ? Sync(rt) : null;
            try
            {
                AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
                {
                    try
                    {
                        byte[] data = null;
                        if (!req.hasError)
                        {
                            var native = req.GetData<byte>();
                            if (native.Length == w * h * 4) data = native.ToArray();
                        }
                        if (data == null)
                        {
                            // A scene change can drop a request; a run of failures means it doesn't work here.
                            if (++errors >= 3) TurnOff("three copies in a row failed");
                            if (reference != null) Deliver(done, reference, w, h);
                            return;
                        }
                        errors = 0;
                        AsyncGrabs++;
                        if (reference != null)
                        {
                            if (flipped == null && !off) Check(data, reference, w, h);
                            Deliver(done, reference, w, h);
                            return;
                        }
                        if (flipped == null) return;   // asked for before the check finished: skip rather than guess
                        if (flipped.Value) FlipRows(data, w, h);
                        Deliver(done, data, w, h);
                    }
                    catch (Exception e) { TurnOff(e.Message); }
                });
            }
            catch (Exception e)
            {
                TurnOff(e.Message);
                Deliver(done, reference ?? Sync(rt), w, h);
            }
        }

        static void Deliver(Action<byte[], int, int> done, byte[] px, int w, int h)
        {
            if (px == null || px.Length != w * h * 4) return;
            try { done(px, w, h); }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Frame copy: " + e.Message); }
        }

        static void TurnOff(string why)
        {
            if (off) return;
            off = true;
            Debug.Log("[KerbalNews] Frame copies: background readback is off (" + why + "), using the ordinary one.");
        }

        /// <summary>The ordinary, waiting copy.</summary>
        static byte[] Sync(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            try
            {
                if (reader != null && (reader.width != rt.width || reader.height != rt.height)) { UnityEngine.Object.Destroy(reader); reader = null; }
                if (reader == null) reader = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                reader.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                SyncGrabs++;
                return reader.GetRawTextureData();
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Frame copy: " + e.Message); return null; }
            finally { RenderTexture.active = prev; }
        }

        /// <summary>Compares the asynchronous copy with the ordinary one of the same frame, the right way up and upside down.</summary>
        static void Check(byte[] data, byte[] reference, int w, int h)
        {
            if (reference.Length != data.Length) { TurnOff("the copy has a different size"); return; }
            double straight, upside, detail;
            Compare(data, reference, w, h, out straight, out upside, out detail);
            // A picture with nothing in it (all black, all white) looks the same both ways up: check again next time.
            if (detail < 4) return;
            if (straight < 6 && straight < upside * 0.5) flipped = false;
            else if (upside < 6 && upside < straight * 0.5) flipped = true;
            else { TurnOff("the copy doesn't match the ordinary one: difference " + straight.ToString("0.0") + " straight, " + upside.ToString("0.0") + " flipped"); return; }
            Debug.Log("[KerbalNews] Frame copies: background readback works here" + (flipped.Value ? " (rows come back flipped, handled)" : "") + ".");
        }

        /// <summary>
        /// Mean difference per byte between two RGBA pictures, row for row and with one of them upside down, over a
        /// coarse sample. Detail is how much the reference differs from its own upside-down self: near zero for a flat picture.
        /// </summary>
        public static void Compare(byte[] a, byte[] b, int w, int h, out double straight, out double upside, out double detail)
        {
            long s = 0, u = 0, d = 0; int n = 0;
            for (int y = 0; y < h; y += 3)
            {
                int row = y * w * 4, mirror = (h - 1 - y) * w * 4;
                for (int x = 0; x < w; x += 7)
                    for (int c = 0; c < 3; c++)
                    {
                        int i = row + x * 4 + c, j = mirror + x * 4 + c;
                        s += Math.Abs(a[i] - b[i]);
                        u += Math.Abs(a[j] - b[i]);
                        d += Math.Abs(b[j] - b[i]);
                        n++;
                    }
            }
            if (n == 0) { straight = upside = detail = 0; return; }
            straight = s / (double)n; upside = u / (double)n; detail = d / (double)n;
        }

        public static void FlipRows(byte[] px, int w, int h)
        {
            int stride = w * 4;
            var tmp = new byte[stride];
            for (int y = 0; y < h / 2; y++)
            {
                int top = y * stride, bottom = (h - 1 - y) * stride;
                Buffer.BlockCopy(px, top, tmp, 0, stride);
                Buffer.BlockCopy(px, bottom, px, top, stride);
                Buffer.BlockCopy(tmp, 0, px, bottom, stride);
            }
        }

        /// <summary>Average brightness (0-255) from a coarse grid of pixels.</summary>
        public static float Brightness(byte[] px)
        {
            if (px == null || px.Length < 4) return 128;
            double sum = 0; int n = 0;
            for (int i = 0; i + 2 < px.Length; i += 97 * 4) { sum += (px[i] + px[i + 1] + px[i + 2]) / 3.0; n++; }
            return n == 0 ? 128 : (float)(sum / n);
        }

        /// <summary>Encodes RGBA pixels as a JPEG and writes the file, both on a worker thread.</summary>
        public static void SaveJpg(byte[] px, int w, int h, int quality, string path)
        {
            if (px == null) return;
            if (encodeOnMain) { Write(path, EncodeOnMain(px, w, h, quality)); return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                byte[] jpg;
                try { jpg = ImageConversion.EncodeArrayToJPG(px, GraphicsFormat.R8G8B8A8_SRGB, (uint)w, (uint)h, 0, quality); }
                catch (Exception e)
                {
                    // This one frame is lost; later ones are encoded the old way.
                    if (!encodeOnMain) Debug.Log("[KerbalNews] Frame copies: JPEG encoding off the main thread isn't available (" + e.Message + ").");
                    encodeOnMain = true;
                    return;
                }
                WriteNow(path, jpg);
            });
        }

        /// <summary>A picture being encoded and written on a worker thread. Ok is only meaningful once Done.</summary>
        public class JpgJob { public volatile bool Done; public bool Ok; }

        /// <summary>
        /// Encodes a readable RGB24 texture's pixels and writes the file on a worker thread. Returns null when that
        /// isn't available here, and the caller encodes the old way.
        /// </summary>
        public static JpgJob SaveRgbJpg(Texture2D tex, int quality, string path)
        {
            if (encodeOnMain || tex == null) return null;
            byte[] px;
            int w = tex.width, h = tex.height;
            try { px = tex.GetRawTextureData(); } catch (Exception) { return null; }
            if (px == null || px.Length != w * h * 3) return null;
            var job = new JpgJob();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var jpg = ImageConversion.EncodeArrayToJPG(px, GraphicsFormat.R8G8B8_SRGB, (uint)w, (uint)h, 0, quality);
                    if (jpg != null && jpg.Length > 0)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path, jpg);
                        job.Ok = true;
                    }
                }
                catch (Exception e)
                {
                    if (!encodeOnMain) Debug.Log("[KerbalNews] Frame copies: JPEG encoding off the main thread isn't available (" + e.Message + ").");
                    encodeOnMain = true;
                }
                finally { job.Done = true; }
            });
            return job;
        }

        static byte[] EncodeOnMain(byte[] px, int w, int h, int quality)
        {
            try
            {
                if (encoder != null && (encoder.width != w || encoder.height != h)) { UnityEngine.Object.Destroy(encoder); encoder = null; }
                if (encoder == null) encoder = new Texture2D(w, h, TextureFormat.RGBA32, false);
                encoder.LoadRawTextureData(px);
                encoder.Apply();
                return ImageConversion.EncodeToJPG(encoder, quality);
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Frame encode: " + e.Message); return null; }
        }

        static void Write(string path, byte[] bytes)
        {
            if (bytes == null) return;
            ThreadPool.QueueUserWorkItem(_ => WriteNow(path, bytes));
        }

        static void WriteNow(string path, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, bytes);
                Interlocked.Increment(ref FramesWritten);
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Could not save frame " + Path.GetFileName(path) + ": " + e.Message); }
        }
    }
}
