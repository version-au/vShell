using System;
using System.Globalization;

namespace vShell.Core
{
    /// <summary>
    /// Every time vShell writes outside the Clock module, the same way: "2:05 PM" (or "14:05" with 24-hour times on), "Today 2:05 PM",
    /// "Yesterday 2:05 PM", "Mon 2:05 PM" this week, "12 Mar 2:05 PM" (and the year when it isn't this year); and how long ago: "just now",
    /// "5 min ago", "3 hr ago", then the day and time.
    /// </summary>
    [PluginApi]
    public static class TimeText
    {
        /// <summary>Settings > Appearance > Times (false = 12-hour, the default).</summary>
        public static bool Use24;

        /// <summary>"2:05 PM" / "14:05" (local time).</summary>
        public static string Clock(DateTime local, bool seconds = false)
        {
            string f = Use24 ? (seconds ? "HH:mm:ss" : "HH:mm") : (seconds ? "h:mm:ss tt" : "h:mm tt");
            return local.ToString(f, CultureInfo.InvariantCulture);
        }

        /// <summary>"Today", "Yesterday", "Mon" (this week), "12 Mar", "12 Mar 2025" (local time).</summary>
        public static string Day(DateTime local)
        {
            DateTime today = DateTime.Today;
            int days = (today - local.Date).Days;
            if (days == 0) return "Today";
            if (days == 1) return "Yesterday";
            if (days > 1 && days < 7) return local.ToString("ddd", CultureInfo.CurrentCulture);
            return local.ToString(local.Year == today.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
        }

        /// <summary>"Today 2:05 PM", "Yesterday 2:05 PM", "Mon 2:05 PM", "12 Mar 2:05 PM" (local time).</summary>
        public static string Stamp(DateTime local, bool seconds = false) { return Day(local) + " " + Clock(local, seconds); }

        /// <summary>A full date and time for tooltips: "Monday 12 March 2026, 2:05 PM".</summary>
        public static string Full(DateTime local, bool seconds = false)
        {
            return local.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture) + ", " + Clock(local, seconds);
        }

        /// <summary>"just now", "5 min ago", "3 hr ago" (today), then Stamp: "Yesterday 2:05 PM" … (from a UTC time).</summary>
        public static string Ago(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "never";
            TimeSpan d = DateTime.UtcNow - utc;
            DateTime local = utc.ToLocalTime();
            if (d.TotalMinutes < 1) return "just now";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + " min ago";
            if (d.TotalHours < 24 && local.Date == DateTime.Today) return (int)d.TotalHours + " hr ago";
            return Stamp(local);
        }

        /// <summary>Ago from a local time.</summary>
        public static string AgoLocal(DateTime local) { return local == DateTime.MinValue ? "never" : Ago(local.ToUniversalTime()); }
    }
}
