using System;
using System.Collections.Generic;
using System.Globalization;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>The Home Assistant notification rules: when an entity does something, show a notification (checked with the other notification sources).</summary>
    static class HaRules
    {
        sealed class RuleState { public bool Seen, Match, Fired; public DateTime Since; public string LastState; }
        static readonly Dictionary<string, RuleState> Rules = new Dictionary<string, RuleState>();

        /// <summary>Does the state meet the rule (Changes is handled by the caller).</summary>
        public static bool Matches(HaNotifyRule r, string state)
        {
            switch (r.When)
            {
                case "Above":
                case "Below":
                    {
                        double v, limit;
                        if (!double.TryParse(state, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
                        if (!double.TryParse(r.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out limit)) return false;
                        return r.When == "Above" ? v > limit : v < limit;
                    }
                default:
                    return string.Equals((state ?? "").Trim(), r.Value.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string Fill(HaNotifyRule r, HaState st)
        {
            string name = st != null && st.Name.Length > 0 ? st.Name : r.Entity;
            string msg = r.Message.Trim().Length > 0 ? r.Message : "{name} is {state}" + (st != null && st.Unit.Length > 0 ? " {unit}" : "");
            return msg.Replace("{name}", name).Replace("{state}", st != null ? st.State : "").Replace("{unit}", st != null ? st.Unit : "").Replace("{entity}", r.Entity);
        }

        public static void Watch(BarSettings s)
        {
            List<HaNotifyRule> rules = HaSettings.Of(s).Rules;
            if (!HaClient.Configured || rules.Count == 0) return;
            var keep = new HashSet<string>();
            for (int i = 0; i < rules.Count; i++)
            {
                HaNotifyRule r = rules[i];
                if (!r.Enabled || r.Entity.Length == 0) continue;
                string key = i + "|" + r.Entity + "|" + r.When + "|" + r.Value + "|" + r.ForMinutes;
                keep.Add(key);
                HaState st = HaClient.Get(r.Entity);       // keeps it fetched / live
                if (st == null || st.Unavailable) continue;
                RuleState rs;
                lock (Rules) if (!Rules.TryGetValue(key, out rs)) { rs = new RuleState(); Rules[key] = rs; }
                if (r.When == "Changes")
                {
                    if (rs.Seen && st.State != rs.LastState) Fire(s, r, st);
                    rs.Seen = true;
                    rs.LastState = st.State;
                    continue;
                }
                bool m = Matches(r, st.State);
                if (!rs.Seen)
                {
                    // the first look: what is true already doesn't notify (only a change into it does)
                    rs.Seen = true; rs.Match = m; rs.Fired = m; rs.Since = DateTime.UtcNow; rs.LastState = st.State;
                    continue;
                }
                if (m && !rs.Match) { rs.Since = DateTime.UtcNow; rs.Fired = false; }
                rs.Match = m;
                rs.LastState = st.State;
                if (!m) { rs.Fired = false; continue; }
                if (!rs.Fired && (DateTime.UtcNow - rs.Since).TotalMinutes >= r.ForMinutes)
                {
                    rs.Fired = true;
                    Fire(s, r, st);
                }
            }
            lock (Rules)
            {
                var gone = new List<string>();
                foreach (string k in Rules.Keys) if (!keep.Contains(k)) gone.Add(k);
                foreach (string k in gone) Rules.Remove(k);
            }
        }

        static void Fire(BarSettings s, HaNotifyRule r, HaState st)
        {
            string url = r.Click == "Toggle" ? "vshell:ha-toggle:" + r.Entity : r.Click == "Open Home Assistant" ? HaClient.Url + "/history?entity_id=" + Uri.EscapeDataString(r.Entity) : "";
            string name = st.Name.Length > 0 ? st.Name : r.Entity;
            Notify.Post("Home Assistant", Fill(r, st), name + (st.Unit.Length > 0 ? " · " + st.State + " " + st.Unit : " · " + st.State), url, r.LevelKind);
        }
    }
}
