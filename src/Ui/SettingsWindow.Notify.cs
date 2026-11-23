using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Settings > Notifications: a row each for how they look, quiet times, the history and every source, with a short summary; each opens
    /// its own page (a plugin's opens that plugin's Notifications tab).
    /// </summary>
    public sealed partial class SettingsWindow
    {
        /// <summary>The part of Notifications whose page shows: cards, quiet, history, listener, services, system or weather.</summary>
        string _notifyPart = "cards";

        void BuildNotificationsPage(StackPanel p)
        {
            _notesAsInfo.Add(p);
            PageHeader(p, "Notifications", "How notifications look and when they show, and what sends them");

            Header(p, "Cards and history");
            LinkRow(p, "", "Cards", CardsSummary(), null, delegate { OpenNotifyPart("cards"); });
            LinkRow(p, "", "Quiet times", QuietSummary(), null, delegate { OpenNotifyPart("quiet"); });
            LinkRow(p, "", "History", HistorySummary(), null, delegate { OpenNotifyPart("history"); });
            LinkRow(p, "", "The same thing, once", SameSummary(), Switch(delegate { return S.NotifySameOnce; }, delegate (bool v) { S.NotifySameOnce = v; }, RebuildNotifications),
                    delegate { OpenNotifyPart("same"); });

            Header(p, "What sends them");
            Action again = RebuildNotifications;
            LinkRow(p, "", "Service status", ServicesSummary(), Switch(delegate { return S.NotifyServices; }, delegate (bool v) { S.NotifyServices = v; }, again),
                    delegate { OpenNotifyPart("services"); });
            LinkRow(p, "", "System", SystemSummary(), null, delegate { OpenNotifyPart("system"); });
            LinkRow(p, "", "Weather", WeatherSummary(), Switch(delegate { return S.NotifyRain; }, delegate (bool v) { S.NotifyRain = v; }, again),
                    delegate { OpenNotifyPart("weather"); });
            LinkRow(p, "", "Sent to vShell", ListenerSummary(), Switch(delegate { return S.NotifyListen; }, SetListen, again),
                    delegate { OpenNotifyPart("listener"); });

            Header(p, "From plugins");
            List<PluginInfo> plugins = PluginManager.WithNotifications();
            if (plugins.Count == 0) Place(p, Txt("Plugins you add (Settings > Plugins) can send notifications too - Komodo, Downloads, 3D printers …", "vsDim", 12.5, false));
            foreach (PluginInfo info in plugins)
            {
                string id = info.Id;
                bool? setUp = PluginManager.IsSetUp(id);
                LinkRow(p, info.Glyph, info.Name, setUp == false ? "Not set up yet - fill in its connection first" : "Its settings are on its page in Plugins",
                        null, delegate { OpenPlugin(id, "Notifications", PgNotify); });
            }

            Header(p, "Try them");
            var tests = new WrapPanel();
            tests.Children.Add(Btn("Test: normal", delegate
            {
                Notify.Post("vShell", "This is a normal notification", "It goes after its time. Click it to open the history.", "vshell:history", NotifyLevel.Normal);
            }));
            var imp = Btn("Test: important", delegate
            {
                Notify.Post("vShell", "This one is important", "It stays until you close it (when Important ones stay is on).", "vshell:history", NotifyLevel.Important);
            });
            imp.Margin = new Thickness(8, 0, 0, 0);
            tests.Children.Add(imp);
            var al = Btn("Test: alert", delegate
            {
                Notify.Post("vShell", "This is an alert", "Errors, something down, too hot. It stays until you close it (when Alerts stay is on).", "vshell:history", NotifyLevel.Alert);
            });
            al.Margin = new Thickness(8, 0, 0, 0);
            tests.Children.Add(al);
            var hist = Btn("Open the history", delegate { NotifyHistoryWindow.ShowNear(_mgr); });
            hist.Margin = new Thickness(8, 0, 0, 0);
            tests.Children.Add(hist);
            Place(p, tests);
        }

        /// <summary>The Notifications page again (a switch changed what a summary says), keeping the scroll position.</summary>
        void RebuildNotifications()
        {
            StackPanel np = Page(PgNotify);
            if (_page != PgNotify) return;
            double at = _content.VerticalOffset;
            np.Children.Clear();
            _cardOf.Remove(np);
            bool o = _loading;
            _loading = true;
            try { BuildNotificationsPage(np); } finally { _loading = o; }
            _content.ScrollToVerticalOffset(at);
        }

        void OpenNotifyPart(string part)
        {
            _notifyPart = part;
            Navigate(PgNotifyPart);
        }

        // ---- the summaries under each row ----
        string CardsSummary()
        {
            string mon = S.NotifyMonitor == "Mouse" ? "the mouse's monitor" : S.NotifyMonitor == "Main" || string.IsNullOrEmpty(S.NotifyMonitor) ? "main monitor" : "one monitor";
            return S.NotifyCorner + "  ·  " + mon + "  ·  " + S.NotifySeconds + " s  ·  " + (S.NotifyGroup == "One card" ? "one card" : "a card per source") +
                   (S.NotifySound ? "  ·  sound" : "");
        }

        string QuietSummary()
        {
            var parts = new List<string>();
            if (S.NotifyHoldFullScreen) parts.Add("full-screen apps");
            if (S.NotifyQuiet) parts.Add(ClockText(S.NotifyQuietFrom) + " – " + ClockText(S.NotifyQuietTo));
            if (S.NotifyFollowWindows) parts.Add("Do not disturb");
            return parts.Count == 0 ? "Off - they always show" : "Held during " + string.Join(", ", parts);
        }

        /// <summary>"23:00" as the user's clock (11:00 PM unless Use 24-hour is on).</summary>
        static string ClockText(string hhmm)
        {
            TimeSpan t;
            return Notify.ParseTime(hhmm, out t) ? TimeText.Clock(DateTime.Today.Add(t)) : hhmm;
        }

        string HistorySummary()
        {
            return "Keeps " + S.NotifyHistoryMax + "  ·  " +
                   (S.NotifyCollapseMinutes > 0 ? "repeats within " + S.NotifyCollapseMinutes + " min count as one" : "repeats kept separate");
        }

        string SameSummary()
        {
            if (!S.NotifySameOnce) return "Off - each app's notification is its own";
            return "One entry when apps tell you about the same thing  ·  within " + S.NotifyJoinMinutes + " min" + (S.NotifyBackCloses ? "  ·  back up closes it" : "") +
                   (S.NotifyBlipSeconds > 0 ? "  ·  blips under " + Notify.Span(TimeSpan.FromSeconds(S.NotifyBlipSeconds)) + " skipped" : "");
        }

        string ServicesSummary()
        {
            if (!S.NotifyServices) return "Off";
            return "A service goes down or comes back" + (S.NotifySvcDelay > 0 ? "  ·  after " + S.NotifySvcDelay + " s" : "") +
                   "  ·  " + Notify.KindOf("Services.Down");
        }

        string SystemSummary()
        {
            var parts = new List<string>();
            if (S.NotifyCpuTemp > 0) parts.Add("CPU " + S.NotifyCpuTemp + " °C");
            if (S.NotifyGpuTemp > 0) parts.Add("GPU " + S.NotifyGpuTemp + " °C");
            if (S.NotifyDiskFull > 0) parts.Add("storage " + S.NotifyDiskFull + " %");
            if (S.NotifyMemory > 0) parts.Add("memory " + S.NotifyMemory + " %");
            if (S.NotifyBattery > 0) parts.Add("battery " + S.NotifyBattery + " %");
            return parts.Count == 0 ? "Off" : string.Join("  ·  ", parts);
        }

        string WeatherSummary()
        {
            if (!S.NotifyRain) return "Off";
            ModuleConfig place = NotifyWatch.RainPlace(S);
            return "Rain within the hour (" + S.NotifyRainChance + " % or more)" + (place != null ? "  ·  " + place.WeatherPlace : "  ·  needs a Weather module with a place");
        }

        string ListenerSummary()
        {
            if (!S.NotifyListen) return "Off - let Home Assistant (or anything on your network) send them to this PC";
            if (NotifyListener.Problem.Length > 0) return NotifyListener.Problem;
            return "On port " + S.NotifyListenPort;
        }

        /// <summary>Turning the listener on makes its token the first time.</summary>
        void SetListen(bool v)
        {
            S.NotifyListen = v;
            if (v && Secret.Unprotect(S.NotifyListenToken).Length == 0) S.NotifyListenToken = Secret.Protect(NotifyListener.NewToken());
        }

        // =================================================================
        // One part's page
        // =================================================================
        void BuildNotifyPart()
        {
            StackPanel p = Page(PgNotifyPart);
            p.Children.Clear();
            _cardOf.Remove(p);
            _notesAsInfo.Add(p);
            string title;
            switch (_notifyPart)
            {
                case "quiet": title = "Quiet times"; break;
                case "history": title = "History"; break;
                case "listener": title = "Sent to vShell"; break;
                case "services": title = "Service status"; break;
                case "system": title = "System"; break;
                case "weather": title = "Weather"; break;
                case "same": title = "The same thing, once"; break;
                default: title = "Cards"; break;
            }
            PageHeader(p, title, "Notifications  ›  " + title, Btn("‹  Back", delegate { Navigate(PgNotify); }));
            bool o = _loading;
            _loading = true;
            try
            {
                switch (_notifyPart)
                {
                    case "quiet": BuildNotifyQuiet(p); break;
                    case "history": BuildNotifyHistory(p); break;
                    case "listener": BuildNotifyListener(p); break;
                    case "services": BuildNotifyServices(p); break;
                    case "system": BuildNotifySystem(p); break;
                    case "weather": BuildNotifyWeather(p); break;
                    case "same": BuildNotifySame(p); break;
                    default: BuildNotifyCards(p); break;
                }
            }
            finally { _loading = o; }
        }

        // ---- look and place ----
        void BuildNotifyCards(StackPanel p)
        {
            Header(p, "Where");
            ComboRow(p, "Corner", ModuleTypes.NotifyCorners, delegate { return S.NotifyCorner; }, delegate (string v) { S.NotifyCorner = v; });
            {
                // the main monitor, each monitor by number, or the one the mouse is on
                var mons = MonitorHelper.GetAll();
                var values = new List<string> { "Main" };
                var keys = new List<string> { "" };
                var cb = new ComboBox { MinWidth = 260 };
                cb.Items.Add("Main monitor");
                for (int i = 0; i < mons.Count; i++)
                {
                    values.Add(mons[i].DeviceName);
                    keys.Add(mons[i].Key ?? "");
                    cb.Items.Add("Monitor " + (i + 1) + (mons[i].IsPrimary ? " (main)" : "") + "  ·  " + mons[i].Bounds.Width + " × " + mons[i].Bounds.Height);
                }
                values.Add("Mouse");
                keys.Add("");
                cb.Items.Add("The monitor the mouse is on");
                int sel = -1;
                MonitorInfo chosen = S.NotifyMonitor == "Main" || S.NotifyMonitor == "Mouse" ? null : MonitorHelper.Find(S.NotifyMonitor, S.NotifyMonitorKey);
                if (chosen != null)
                    for (int i = 0; i < mons.Count; i++)
                        if (mons[i].Key == chosen.Key && mons[i].DeviceName == chosen.DeviceName) { sel = i + 1; break; }
                if (sel <= 0) sel = values.FindIndex(delegate (string v) { return string.Equals(v, S.NotifyMonitor, StringComparison.OrdinalIgnoreCase); });
                if (sel < 0) { values.Insert(values.Count - 1, S.NotifyMonitor); keys.Insert(keys.Count - 1, S.NotifyMonitorKey ?? ""); cb.Items.Insert(cb.Items.Count - 1, "A monitor that isn't connected (the main one is used)"); sel = values.Count - 2; }
                cb.SelectedIndex = sel;
                cb.SelectionChanged += delegate
                {
                    if (_loading || cb.SelectedIndex < 0) return;
                    S.NotifyMonitor = values[cb.SelectedIndex];
                    S.NotifyMonitorKey = keys[cb.SelectedIndex];
                    _mgr.SettingsChanged();
                    NotifyCards.Relayout();
                };
                Place(p, Row("Monitor", cb, false, "The main monitor, a particular one, or the monitor the mouse is on when the notification comes in."));
            }
            ComboRow(p, "Cards", ModuleTypes.NotifyGroups, delegate { return S.NotifyGroup; }, delegate (string v) { S.NotifyGroup = v; },
                     "By source = a card for each source (Downloads, Komodo …) stacked; One card = everything together on one card.");

            Header(p, "How long");
            SliderRow(p, "Seconds a card stays (longer while the mouse is over it)", 2, 60, delegate { return S.NotifySeconds; }, delegate (double v) { S.NotifySeconds = (int)v; },
                      "Three kinds: Normal (green) goes after its time; Important (yellow) and Alert (red - errors, something down, too hot) can stay until you close them.");
            Check(p, "Important ones stay on screen until closed", delegate { return S.NotifyKeepImportant; }, delegate (bool v) { S.NotifyKeepImportant = v; });
            Check(p, "Alerts stay on screen until closed", delegate { return S.NotifyKeepAlerts; }, delegate (bool v) { S.NotifyKeepAlerts = v; });

            Header(p, "Look and sound");
            Check(p, "Background effect behind the cards and the history", delegate { return S.NotifyBackdrop; }, delegate (bool v) { S.NotifyBackdrop = v; },
                  "When Appearance has a background effect (Blur or Frosted), the cards and the history window get it too. Off = the plain, almost " +
                  "solid background. The bell's list follows the setting in the Notifications module.");
            Check(p, "Play the Windows notification sound", delegate { return S.NotifySound; }, delegate (bool v) { S.NotifySound = v; });
        }

        static string FormatTime(TimeSpan t) { return ((int)t.TotalHours % 24).ToString("00") + ":" + t.Minutes.ToString("00"); }

        // ---- quiet times ----
        void BuildNotifyQuiet(StackPanel p)
        {
            Header(p, "Hold them");
            Check(p, "While a full-screen app (game, video) is in front", delegate { return S.NotifyHoldFullScreen; }, delegate (bool v) { S.NotifyHoldFullScreen = v; },
                  "Held notifications show when the quiet time ends (the newest six; the rest are in the history). They are always added to the history straight away.");
            Check(p, "When Windows says not to disturb (Do not disturb, presentation mode)", delegate { return S.NotifyFollowWindows; }, delegate (bool v) { S.NotifyFollowWindows = v; },
                  "Windows doesn't tell every program about Do not disturb, so this may not catch every case.");
            Header(p, "Quiet hours");
            Check(p, "Quiet hours", delegate { return S.NotifyQuiet; }, delegate (bool v) { S.NotifyQuiet = v; });
            TextRow(p, "From (for example 23:00)", delegate { return S.NotifyQuietFrom; }, delegate (string v) { TimeSpan t; S.NotifyQuietFrom = Notify.ParseTime(v, out t) ? FormatTime(t) : S.NotifyQuietFrom; });
            TextRow(p, "Until (for example 07:00)", delegate { return S.NotifyQuietTo; }, delegate (string v) { TimeSpan t; S.NotifyQuietTo = Notify.ParseTime(v, out t) ? FormatTime(t) : S.NotifyQuietTo; });
        }

        // ---- history ----
        void BuildNotifyHistory(StackPanel p)
        {
            Header(p, "History");
            SliderRow(p, "How many to keep", 10, 5000, delegate { return S.NotifyHistoryMax; }, delegate (double v) { S.NotifyHistoryMax = (int)v; });
            OffSliderRow(p, "Count repeats as one within (minutes)", 1, 120, 10, delegate { return S.NotifyCollapseMinutes; }, delegate (double v) { S.NotifyCollapseMinutes = (int)v; },
                         "The same notification again (same source, title and level) within that time is one entry with a count, e.g. \"Backup failed ×3\" - " +
                         "its card is updated instead of another one popping up. vDrop notifications always stay separate (each has its own read state there).");
            var hist = new WrapPanel();
            hist.Children.Add(Btn("Open the history", delegate { NotifyHistoryWindow.ShowNear(_mgr); }));
            var clear = Btn("Clear the history", delegate
            {
                if (MessageBox.Show(this, "Remove every notification from the history?", "Notifications", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) Notify.Clear();
            });
            clear.Margin = new Thickness(8, 0, 0, 0);
            hist.Children.Add(clear);
            Place(p, hist);
            Note(p, "Add the Notifications module to a bar for a bell with the number of new ones (click it for the history), or to a widget for the list.");
        }

        // ---- the same thing, once: apps telling you about the same machine / app join one entry ----
        static readonly string[] JoinChoices = { "2 minutes", "5 minutes", "10 minutes", "15 minutes", "30 minutes" };
        static readonly string[] BlipChoices = { "Off", "30 seconds", "1 minute", "2 minutes", "5 minutes" };
        static readonly int[] BlipSeconds = { 0, 30, 60, 120, 300 };

        void BuildNotifySame(StackPanel p)
        {
            Header(p, "The same thing, once");
            Check(p, "Join notifications about the same thing", delegate { return S.NotifySameOnce; }, delegate (bool v) { S.NotifySameOnce = v; },
                  "When several apps tell you about the same machine or app (nuc01 down from Uptime Kuma, vDeploy and Proxmox), you get one entry that grows: " +
                  "the first one pops up, the others join it quietly - no new card, no sound. Click it in the list for what each app said.");
            ComboRow(p, "Join them when they come within", JoinChoices,
                     delegate { return S.NotifyJoinMinutes + " minutes"; },
                     delegate (string v) { int n; if (int.TryParse(v.Split(' ')[0], out n)) S.NotifyJoinMinutes = n; },
                     "An outage still going is always joined, however long ago it started; this is for apps that are slow to notice, or late to say it's back.");
            Check(p, "Back up closes it", delegate { return S.NotifyBackCloses; }, delegate (bool v) { S.NotifyBackCloses = v; },
                  "One green \"nuc01 is back · down 4 min\" (marked read, no sound) instead of a \"back up\" from each app. Off: each app's own \"back up\" too.");
            ComboRow(p, "A new outage's card waits", BlipChoices,
                     delegate { int i = Array.IndexOf(BlipSeconds, S.NotifyBlipSeconds); return i >= 0 ? BlipChoices[i] : BlipChoices[0]; },
                     delegate (string v) { int i = Array.IndexOf(BlipChoices, v); S.NotifyBlipSeconds = i >= 0 ? BlipSeconds[i] : 0; },
                     "Back before then: no card at all, only \"nuc01 dropped out for 40 s\" in the history. Most plugins already wait before saying something is down " +
                     "(their own \"Wait before saying it's down\"), so this is off unless you want more.");

            Header(p, "These are the same");
            Note(p, "vShell learns most of these by itself: from your plugins (each knows a machine's name and its address) and from DNS (local names only - " +
                    "a public name like radarr.example.com can point at your proxy, shared by every site). Add the ones it can't work out: names and addresses, " +
                    "separated by commas.");
            var list = new StackPanel();
            Action fill = null;
            fill = delegate
            {
                list.Children.Clear();
                List<NotifyNames.Thing> things = NotifyNames.All();
                if (things.Count == 0) list.Children.Add(Txt("Nothing yet - it fills in as your plugins check things.", "vsDim", 12.5, false));
                foreach (NotifyNames.Thing th in things)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                    var who = Txt(th.Mine ? "Added by you" : "From " + string.Join(", ", th.From), "vsDim", 12, false);
                    who.Width = 170; who.TextTrimming = TextTrimming.CharacterEllipsis; who.ToolTip = string.Join(", ", th.From);
                    DockPanel.SetDock(who, Dock.Right);
                    if (th.Mine)
                    {
                        NotifyNames.Thing mine = th;
                        var del = Btn("Remove", delegate
                        {
                            // the lines of yours with any of its names
                            S.NotifySame.RemoveAll(delegate (string line)
                            {
                                foreach (string n in line.Split(',')) if (mine.Names.Exists(delegate (string x) { return NotifyNames.Key(x) == NotifyNames.Key(n); })) return true;
                                return false;
                            });
                            _mgr.SettingsChanged();
                            fill();
                        });
                        del.Margin = new Thickness(8, 0, 0, 0);
                        DockPanel.SetDock(del, Dock.Right);
                        row.Children.Add(del);
                    }
                    row.Children.Add(who);
                    var name = Txt(NotifyNames.Shown(th), "vsFg", 12.5, true);
                    name.Width = 150; name.TextTrimming = TextTrimming.CharacterEllipsis;
                    DockPanel.SetDock(name, Dock.Left);
                    row.Children.Add(name);
                    var also = Txt(string.Join("  ·  ", th.Names.FindAll(delegate (string x) { return x != NotifyNames.Shown(th); })), "vsDim", 12, false);
                    also.TextTrimming = TextTrimming.CharacterEllipsis;
                    also.ToolTip = string.Join("\n", th.Names);
                    row.Children.Add(also);
                    list.Children.Add(row);
                }
            };
            fill();
            Place(p, list);
            var add = new TextBox { MinWidth = 320 };
            var addRow = new StackPanel { Orientation = Orientation.Horizontal };
            addRow.Children.Add(add);
            var addBtn = Btn("Add", delegate
            {
                var names = new List<string>();
                foreach (string n in add.Text.Split(',')) if (n.Trim().Length > 0) names.Add(n.Trim());
                if (names.Count < 2) return;
                S.NotifySame.Add(string.Join(", ", names));
                add.Text = "";
                _mgr.SettingsChanged();
                fill();
            }, true);
            addBtn.Margin = new Thickness(8, 0, 0, 0);
            addRow.Children.Add(addBtn);
            Place(p, Row("Add (for example: printer, 192.168.2.40, Bambu X1C)", addRow, true));
            var tools = new WrapPanel();
            tools.Children.Add(Btn("Look again now", delegate { fill(); }));
            var forget = Btn("Forget what was learned", delegate { NotifyNames.Forget(); fill(); });
            forget.Margin = new Thickness(8, 0, 0, 0);
            tools.Children.Add(forget);
            Place(p, tools);

            Header(p, "Which apps join in");
            List<string> sources = NotifyNames.Sources();
            if (sources.Count == 0)
                Note(p, "Plugins that say what their notifications are about show here once they've sent one or checked their things: Uptime Kuma, vDeploy, Proxmox, vProxy, UniFi, PeaNUT …");
            else
            {
                Note(p, "Untick one to keep its notifications apart from the others.");
                foreach (string src in sources)
                {
                    string source = src;
                    Check(p, source, delegate { return !S.NotifyApart.Contains(source); },
                          delegate (bool v) { S.NotifyApart.Remove(source); if (!v) S.NotifyApart.Add(source); });
                }
            }
        }

        /// <summary>
        /// A plugin's notifications on its page's Notifications tab: a card with this title. ok = its connection is set up; when it isn't,
        /// a line says so (the settings can be filled in anyway).
        /// </summary>
        void NotifySection(StackPanel p, string title, bool ok, string what)
        {
            Header(p, title);
            if (ok) return;
            string who = string.IsNullOrEmpty(what) ? "It" : char.ToUpperInvariant(what[0]) + what.Substring(1);
            Place(p, Txt(who + " isn't set up yet - fill in Connection first. These work once it is.", "vsAccent", 12.5, false));
        }

        /// <summary>"Wait before saying it's down": a down notice is held this long and dropped if it's back by then.</summary>
        void DelayRow(StackPanel p, Func<int> get, Action<int> set)
        {
            SliderRow(p, "Wait before saying it's down (seconds) - 0 = straight away", 0, 600, delegate { return get(); }, delegate (double v) { set((int)v); },
                      "If it's back up before the wait is over, nothing is said - no down and no back notification (a quick restart or a blip).");
        }

        // ---- Normal / Important / Alert ----
        /// <summary>
        /// The kind of one sort of notification (Notify.Kinds) as joined buttons in its kind's colour (green / yellow / red). Important ones and
        /// alerts stay on screen when the "stay until closed" settings are on.
        /// </summary>
        void KindPick(StackPanel p, string label, string key)
        {
            Place(p, Row(label, Segmented(new[] { "Normal", "Important", "Alert" },
                delegate { return Notify.KindOf(key).ToString(); },
                delegate (string v)
                {
                    NotifyLevel level = v == "Alert" ? NotifyLevel.Alert : v == "Important" ? NotifyLevel.Important : NotifyLevel.Normal;
                    if (S.NotifyKinds == null) S.NotifyKinds = new Dictionary<string, string>();
                    NotifyLevel def;
                    if (Notify.Kinds.TryGetValue(key, out def) && def == level) S.NotifyKinds.Remove(key);     // back to its default
                    else S.NotifyKinds[key] = level.ToString();
                },
                delegate (string v) { return StatusColors.Of(v == "Alert" ? NotifyLevel.Alert : v == "Important" ? NotifyLevel.Important : NotifyLevel.Normal); })));
        }

        // ---- sent to vShell (Home Assistant's rest_command, or anything on the network): the listener ----
        void BuildNotifyListener(StackPanel p)
        {
            Header(p, "Listening");
            var tokenBox = new TextBox { IsReadOnly = true, Width = 260, Text = Secret.Unprotect(S.NotifyListenToken) };
            Check(p, "Let Home Assistant (or anything on your network) send notifications to this PC", delegate { return S.NotifyListen; }, delegate (bool v)
            {
                SetListen(v);
                tokenBox.Text = Secret.Unprotect(S.NotifyListenToken);
            }, "Windows may ask once whether vShell may accept connections - allow it on private networks.");
            TextRow(p, "Port", delegate { return S.NotifyListenPort.ToString(CultureInfo.InvariantCulture); },
                    delegate (string v) { int n; if (int.TryParse(v, out n)) S.NotifyListenPort = Math.Max(1024, Math.Min(65535, n)); });
            var tokenRow = new StackPanel { Orientation = Orientation.Horizontal };
            tokenRow.Children.Add(tokenBox);
            var copy = Btn("Copy", delegate { try { Clipboard.SetText(tokenBox.Text); } catch { } });
            copy.Margin = new Thickness(6, 0, 0, 0);
            tokenRow.Children.Add(copy);
            var fresh = Btn("New token", delegate
            {
                string t = NotifyListener.NewToken();
                S.NotifyListenToken = Secret.Protect(t);
                tokenBox.Text = t;
                _mgr.SettingsChanged();
            });
            fresh.Margin = new Thickness(6, 0, 0, 0);
            tokenRow.Children.Add(fresh);
            Place(p, Row("Token (senders must give it)", tokenRow, false, "Stored encrypted for your Windows account. Making a new one stops the old one working."));
            var state = Txt(NotifyListener.Problem.Length > 0 ? NotifyListener.Problem : NotifyListener.Listening ? "Listening on port " + S.NotifyListenPort + "." : "Off.", "vsDim", 12.5, false);
            state.TextWrapping = TextWrapping.Wrap;
            Place(p, state);

            Header(p, "Sending them");
            string ip = ThisPcAddress();
            var example = new TextBox
            {
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5, AcceptsReturn = true,
                Text = "# configuration.yaml\n" +
                       "rest_command:\n" +
                       "  vshell:\n" +
                       "    url: \"http://" + ip + ":" + S.NotifyListenPort + "/notify\"\n" +
                       "    method: POST\n" +
                       "    headers:\n" +
                       "      Authorization: \"Bearer YOUR-TOKEN\"\n" +
                       "    content_type: \"application/json\"\n" +
                       "    payload: '{\"title\": \"{{ title }}\", \"message\": \"{{ message }}\", \"level\": \"{{ level | default(\"normal\") }}\"}'\n\n" +
                       "# in an automation:\n" +
                       "action: rest_command.vshell\n" +
                       "data:\n" +
                       "  title: \"Front door opened\"\n" +
                       "  message: \"While nobody is home\"\n" +
                       "  level: alert          # normal, important or alert"
            };
            Place(p, Row("Home Assistant set-up", example, true,
                         "Send {\"title\", \"message\"} (optionally \"url\" - a web page to open on click, \"level\": normal / important / alert, and \"source\" - the name shown, " +
                         "Home Assistant when left out) to http://this-pc:port/notify."));
        }

        static string ThisPcAddress()
        {
            try
            {
                foreach (IPAddress a in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) return a.ToString();
            }
            catch { }
            return "this-pc";
        }

        // ---- service status ----
        void BuildNotifyServices(StackPanel p)
        {
            Header(p, "Service status");
            Check(p, "A service goes down (and when it's back up)", delegate { return S.NotifyServices; }, delegate (bool v) { S.NotifyServices = v; },
                  "Every service in your Service status modules is watched, at each module's own check interval. It only counts as down after two failed checks in a row, " +
                  "and only once it has been up since vShell started.");
            DelayRow(p, delegate { return S.NotifySvcDelay; }, delegate (int v) { S.NotifySvcDelay = v; });
            KindPick(p, "Down shows as", "Services.Down");
            KindPick(p, "Back up shows as", "Services.Up");
        }

        // ---- system ----
        void BuildNotifySystem(StackPanel p)
        {
            Header(p, "Too hot");
            OffSliderRow(p, "CPU hotter than (°C)", 1, 110, 90, delegate { return S.NotifyCpuTemp; }, delegate (double v) { S.NotifyCpuTemp = (int)v; },
                         "The CPU temperature comes from HWiNFO (\"Report value in Gadget\" on for it) or LibreHardwareMonitor - as picked in a sensor's settings.");
            OffSliderRow(p, "GPU hotter than (°C)", 1, 110, 85, delegate { return S.NotifyGpuTemp; }, delegate (double v) { S.NotifyGpuTemp = (int)v; },
                         "The GPU's temperature comes from the NVIDIA driver, or HWiNFO / LibreHardwareMonitor for other cards.");
            SliderRow(p, "… for at least (seconds)", 0, 300, delegate { return S.NotifyTempSeconds; }, delegate (double v) { S.NotifyTempSeconds = (int)v; });
            Header(p, "Running low");
            OffSliderRow(p, "Storage fuller than (%)", 1, 100, 95, delegate { return S.NotifyDiskFull; }, delegate (double v) { S.NotifyDiskFull = (int)v; });
            OffSliderRow(p, "Memory in use over (%) for a minute", 1, 100, 90, delegate { return S.NotifyMemory; }, delegate (double v) { S.NotifyMemory = (int)v; });
            OffSliderRow(p, "Battery below (%) - laptops", 1, 90, 15, delegate { return S.NotifyBattery; }, delegate (double v) { S.NotifyBattery = (int)v; });
            Header(p, "Warnings");
            SliderRow(p, "The same warning at most every (minutes)", 1, 120, delegate { return S.NotifySysCooldown; }, delegate (double v) { S.NotifySysCooldown = (int)v; });
            KindPick(p, "Warnings show as", "System.Warning");
        }

        // ---- weather ----
        void BuildNotifyWeather(StackPanel p)
        {
            Header(p, "Rain");
            ModuleConfig place = NotifyWatch.RainPlace(S);
            Check(p, "Rain is expected within the hour (while it's dry now)", delegate { return S.NotifyRain; }, delegate (bool v) { S.NotifyRain = v; },
                  (place != null ? "For " + place.WeatherPlace + " (the first Weather module using Open-Meteo). " : "") +
                  "Checked every 15 minutes, at most one notification every 3 hours. Storm and other official warnings: use a Home Assistant rule on your warnings sensor.");
            if (place == null) Place(p, Txt("Add a Weather module with a place (Open-Meteo) first - its place is used.", "vsAccent", 12.5, false));
            SliderRow(p, "Rain chance that counts (%)", 10, 100, delegate { return S.NotifyRainChance; }, delegate (double v) { S.NotifyRainChance = (int)v; });
            KindPick(p, "Show as", "Weather.Rain");
        }
    }
}
