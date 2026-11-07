using System;
using System.Collections.Generic;

namespace KerbalNews
{
    /// <summary>
    /// A model running on the player's own computer (Ollama, LM Studio, llama.cpp, KoboldCpp, Jan...) answers one request at
    /// a time and much more slowly than a cloud service. Requests to it wait their turn here instead of piling up on the
    /// server and timing out, and get a longer time limit. Everything else (cloud providers, image services) passes straight through.
    /// </summary>
    public class LocalQueueTransport : IHttpTransport
    {
        readonly IHttpTransport inner;
        readonly Func<string> localRoot;   // the text model's address when it is local, else null
        readonly Func<int> localTimeout;
        readonly Queue<KeyValuePair<HttpRequestSpec, Action<HttpResult>>> waiting = new Queue<KeyValuePair<HttpRequestSpec, Action<HttpResult>>>();
        bool busy;
        public int Waiting { get { return waiting.Count; } }
        public IHttpTransport Inner { get { return inner; } }

        public LocalQueueTransport(IHttpTransport inner, Func<string> localRoot, Func<int> localTimeout)
        {
            this.inner = inner; this.localRoot = localRoot; this.localTimeout = localTimeout;
        }

        bool IsLocalRequest(HttpRequestSpec r)
        {
            string root = null;
            try { root = localRoot(); } catch (Exception) { }
            if (string.IsNullOrEmpty(root) || r == null || r.Url == null) return false;
            Uri a, b;
            return Uri.TryCreate(root, UriKind.Absolute, out a) && Uri.TryCreate(r.Url, UriKind.Absolute, out b) && a.Host == b.Host && a.Port == b.Port;
        }

        public void Send(HttpRequestSpec r, Action<HttpResult> done)
        {
            if (!IsLocalRequest(r)) { inner.Send(r, done); return; }
            int t = 600;
            try { t = localTimeout(); } catch (Exception) { }
            if (r.Method != "GET") r.TimeoutSeconds = Math.Max(r.TimeoutSeconds, t);
            waiting.Enqueue(new KeyValuePair<HttpRequestSpec, Action<HttpResult>>(r, done));
            Pump();
        }

        void Pump()
        {
            if (busy || waiting.Count == 0) return;
            busy = true;
            var next = waiting.Dequeue();
            inner.Send(next.Key, res =>
            {
                busy = false;
                try { next.Value(res); }
                finally { Pump(); }
            });
        }
    }
}
