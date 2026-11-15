namespace vShell.Core
{
    /// <summary>
    /// vshell.log - the suite's shared log (vSuite.Core's Log): lines are queued and written together every half second (callers - often
    /// the UI thread - never wait for the disk), the same line again within a few seconds is counted instead of written, and past 1 MB
    /// the log moves to vshell.old.log. This class keeps vShell's own name for it, which plugins use (plugin API).
    /// </summary>
    [PluginApi]
    public static class Log
    {
        /// <summary>Thread-safe; keeps the log to about 1 MB (plus one older file).</summary>
        public static void Write(string msg) { vSuite.Log.Write(msg); }

        /// <summary>Write what's queued now (also on exit).</summary>
        public static void Flush() { vSuite.Log.Flush(); }
    }
}