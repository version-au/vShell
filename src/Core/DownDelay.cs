using System;
using System.Collections.Generic;

namespace vShell.Core
{
    /// <summary>What happened to a "down" notice when the thing came back.</summary>
    [PluginApi]
    public enum UpResult
    {
        /// <summary>Nothing was waiting or said about it.</summary>
        None,
        /// <summary>It came back before the wait was over: the down notice was never shown (a blip / quick restart).</summary>
        Cancelled,
        /// <summary>The down notice was shown: say it's back.</summary>
        WasNotified
    }

    /// <summary>
    /// "Wait before saying something is down": a down notice is held for a number of seconds and only shown if the thing is still down
    /// then. If it comes back first the notice is dropped (and so is the "back" one). Used by Service status, Proxmox and Komodo.
    /// </summary>
    [PluginApi]
    public static class DownDelay
    {
        sealed class Pending { public DateTime Due; public Action Post; }
        static readonly object Gate = new object();
        static readonly Dictionary<string, Pending> Waiting = new Dictionary<string, Pending>();
        static readonly HashSet<string> Said = new HashSet<string>();

        /// <summary>Something is down. seconds &lt;= 0 = say it now. Ignored while it is already waiting or was already said.</summary>
        public static void Down(string key, int seconds, Action post)
        {
            bool now = false;
            lock (Gate)
            {
                if (Said.Contains(key) || Waiting.ContainsKey(key)) return;
                if (seconds <= 0) { Said.Add(key); now = true; }
                else Waiting[key] = new Pending { Due = DateTime.UtcNow.AddSeconds(seconds), Post = post };
            }
            if (now) Run(post);
        }

        /// <summary>It's back (or running again).</summary>
        public static UpResult Up(string key)
        {
            lock (Gate)
            {
                if (Waiting.Remove(key)) return UpResult.Cancelled;
                if (Said.Remove(key)) return UpResult.WasNotified;
                return UpResult.None;
            }
        }

        /// <summary>Whether a down notice for it is waiting or was shown.</summary>
        public static bool IsDown(string key)
        {
            lock (Gate) return Waiting.ContainsKey(key) || Said.Contains(key);
        }

        /// <summary>Show the notices whose wait is over (called every few seconds).</summary>
        public static void Tick()
        {
            var due = new List<Action>();
            lock (Gate)
            {
                DateTime now = DateTime.UtcNow;
                var keys = new List<string>();
                foreach (var kv in Waiting) if (now >= kv.Value.Due) keys.Add(kv.Key);
                foreach (string k in keys) { due.Add(Waiting[k].Post); Waiting.Remove(k); Said.Add(k); }
            }
            foreach (Action a in due) Run(a);
        }

        static void Run(Action a)
        {
            try { a(); } catch (Exception ex) { Log.Write("Down notice: " + ex.Message); }
        }
    }
}
