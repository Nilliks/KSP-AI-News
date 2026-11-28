using System;

namespace KerbalNews
{
    /// <summary>
    /// A breadcrumb of what the mod is doing on the game's main thread right now. Setting it costs nothing; a watchdog
    /// in the KSP layer reads it when the game stops drawing frames, so a freeze names the code that was running.
    /// </summary>
    public static class Crumb
    {
        public static volatile string In = "";
        /// <summary>Stopwatch ticks of the last frame the mod saw; 0 while it is not watching.</summary>
        public static long Beat;

        public static string Enter(string what) { string was = In; In = was == "" ? what : was + " > " + what; return was; }
        public static void Exit(string was) { In = was; }

        public static void Run(string what, Action a) { string was = Enter(what); try { a(); } finally { Exit(was); } }
        public static T Run<T>(string what, Func<T> f) { string was = Enter(what); try { return f(); } finally { Exit(was); } }
    }
}
