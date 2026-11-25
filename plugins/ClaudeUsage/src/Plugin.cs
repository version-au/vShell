using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("claudeusage", "Claude usage",
    Description = "Your Claude plan's usage limits - the 5-hour session and the week - on a bar or widget, with a heads-up before you hit one. Reads the sign-in Claude Code keeps on this PC.",
    Glyph = "\uEC4A", Group = "Servers & network", Author = "version", Adds = "notifications", Types = "ClaudeUsage")]

namespace ClaudeUsage
{
    public sealed class Plugin : IPlugin
    {
        public const string Id = "claudeusage";
        public const string Source = "Claude usage";
        public const string Glyph = "\uEC4A";
        public const string KindClose = "ClaudeUsage.Close";
        public const string KindReached = "ClaudeUsage.Reached";
        public const string KindReset = "ClaudeUsage.Reset";

        /// <summary>The last answer. Swapped (never changed) on the UI thread.</summary>
        public static Snapshot Current = Snapshot.Empty;
        public static Poller Poll;
        static Options _opt = new Options();
        static BarSettings _settings;
        static PluginHost _host;
        static Dispatcher _ui;
        static DateTime _lastManual = DateTime.MinValue;

        public void Load(PluginHost host)
        {
            _host = host;
            _ui = Application.Current != null ? Application.Current.Dispatcher : Dispatcher.CurrentDispatcher;
            Current = Snapshot.Empty;

            host.AddModule(new PluginModule
            {
                Type = "ClaudeUsage", Display = "Claude usage", ShortName = "Claude", Glyph = Glyph, Group = "Info",
                Blurb = "How much of your Claude plan's 5-hour and weekly limits you've used, and when they reset.",
                OnBar = true, InWidget = true, HasPopup = true,
                Needs = delegate (BarSettings s) { return UsageClient.SignedIn(OptionsFrom(s)) ? null : "Sign in to Claude Code on this PC (or set its folder in Settings > Plugins > Claude usage)"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new UsageModule(mgr, h, cfg); },
                Settings = UsageModule.BuildSettings,
                BarPicture = Pictures.Bar,
                WidgetPicture = Pictures.Widget
            });

            host.AddIntegration("Claude usage", BuildConnection);
            host.SetUpWhen(delegate (BarSettings s) { return UsageClient.SignedIn(OptionsFrom(s)); });
            host.OnSettings(OnSettings);

            host.AddNotifyKind(KindClose, NotifyLevel.Important);
            host.AddNotifyKind(KindReached, NotifyLevel.Alert);
            host.AddNotifyKind(KindReset, NotifyLevel.Normal);
            host.AddNotifySource(Source);
            host.AddNotifications(BuildNotifications);
            host.AddWatcher(Source, WatchResets);

            host.AddCommands(Source, Commands);
            host.OnPaletteOpening(delegate (BarSettings s)
            {
                if (!Current.HasData || DateTime.UtcNow - Current.FetchedUtc > TimeSpan.FromMinutes(2)) Poll?.PollNow();
            });

            // Fast while a Claude usage module is on screen, slower otherwise (for the notifications). Started by the first OnSettings,
            // so the first poll already has the settings (a custom folder) rather than the defaults.
            Poll = new Poller(Source, PollOnce, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(6));
        }

        public void Unload()
        {
            if (Poll != null) { Poll.Stop(); Poll.Dispose(); Poll = null; }
            Current = Snapshot.Empty;
        }

        // ---------- settings ----------

        public static SettingsBag Bag(BarSettings s) { return s.Integration(Id); }

        static Options OptionsFrom(BarSettings s)
        {
            if (s == null) return _opt;
            SettingsBag b = Bag(s);
            return new Options
            {
                Folder = b.Get("Folder", ""),
                Renew = b.Get("Renew", true),
                UserAgent = b.Get("UserAgent", "")
            };
        }

        static void OnSettings(BarSettings s)
        {
            _settings = s;
            Options next = OptionsFrom(s);
            bool changed = next.Folder != _opt.Folder || next.UserAgent != _opt.UserAgent;
            _opt = next;
            if (Poll == null) return;
            if (!Poll.Running) Poll.Start();        // (its first poll is straight away)
            else if (changed) Poll.PollNow();
        }

        static void BuildConnection(SettingsWindow.SettingsPage p)
        {
            SettingsBag b = Bag(p.Settings);
            p.Header("Claude sign-in");
            p.Note("vShell reads the sign-in Claude Code keeps on this PC and asks Anthropic for your plan's usage - the same thing Claude Code's /usage shows. It's an unofficial part of Anthropic's service, so it can change without notice. Your sign-in only ever goes to api.anthropic.com.");
            p.Text("Claude Code folder", delegate { return b.Get("Folder", ""); },
                delegate (string v) { b.Set("Folder", (v ?? "").Trim()); },
                "Empty = " + UsageClient.DefaultFolder() + ". For Claude Code in WSL, something like \\\\wsl.localhost\\Ubuntu\\home\\you\\.claude");
            p.Button("Find it", "Look for Claude Code (also in WSL)", delegate { FindFolder(p); });
            p.Check("Renew the sign-in when it runs out", delegate { return b.Get("Renew", true); },
                delegate (bool v) { b.Set("Renew", v); });
            p.Note("Claude Code's sign-in only lasts a few hours. With this on, vShell renews it the way Claude Code does and saves it back to Claude Code's file (only if Claude Code hasn't changed it meanwhile), so the module keeps working when Claude Code isn't running. Off: it waits for Claude Code to renew it.");
            p.Test(delegate { }, TestAsync);

            p.Section("Advanced");
            p.Text("User agent", delegate { return b.Get("UserAgent", ""); },
                delegate (string v) { b.Set("UserAgent", (v ?? "").Trim()); },
                "Empty = " + UsageClient.DefaultAgent + ". Anthropic turns other agents away (HTTP 403); change it only if that starts happening.");
        }

        static async Task<KeyValuePair<bool, string>> TestAsync()
        {
            Options o = OptionsFrom(_settings);
            Snapshot s = await UsageClient.FetchAsync(o, null);
            if (!s.HasData) return new KeyValuePair<bool, string>(false, s.Error);
            Publish(s);
            string plan = s.Plan.Length > 0 ? s.Plan + " - " : "";
            return new KeyValuePair<bool, string>(true, "Signed in. " + plan + Summary(s));
        }

        static void FindFolder(SettingsWindow.SettingsPage p)
        {
            SettingsBag b = Bag(p.Settings);
            Window w = p.Window;
            Task.Run(delegate { return UsageClient.FindFolders(); }).ContinueWith(delegate (Task<List<string>> t)
            {
                List<string> found = t.Status == TaskStatus.RanToCompletion ? t.Result : new List<string>();
                if (found.Count == 0)
                {
                    Questions.Tell(w, "No Claude Code sign-in found on this PC or in WSL. Sign in to Claude Code (run claude, then /login) and try again.", "Claude usage");
                    return;
                }
                string pick = found[0];
                if (found.Count > 1)
                {
                    var field = FormField.Combo("Claude Code is signed in in more than one place. Use", found[0], found.ToArray());
                    if (!FormWindow.Ask(w, "Claude usage", new List<FormField> { field }) || string.IsNullOrEmpty(field.Value)) return;
                    pick = field.Value;
                }
                string def = UsageClient.DefaultFolder();
                b.Set("Folder", string.Equals(pick, def, StringComparison.OrdinalIgnoreCase) ? "" : pick);
                p.Changed();
                p.Redraw();
                Questions.Tell(w, "Using Claude Code's sign-in in\n" + pick, "Claude usage");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static void BuildNotifications(SettingsWindow.SettingsPage p)
        {
            SettingsBag b = Bag(p.Settings);
            p.NotifySection("Claude usage", UsageClient.SignedIn(OptionsFrom(p.Settings)), "the Claude sign-in");
            p.Check("A limit is getting close", delegate { return b.Get("NotifyClose", true); }, delegate (bool v) { b.Set("NotifyClose", v); });
            p.Slider("Close means (% used)", 50, 99, delegate { return b.Get("CloseAt", 80.0); }, delegate (double v) { b.Set("CloseAt", Math.Round(v)); });
            p.Check("A limit is reached", delegate { return b.Get("NotifyReached", true); }, delegate (bool v) { b.Set("NotifyReached", v); });
            p.Check("A limit I reached has reset", delegate { return b.Get("NotifyReset", true); }, delegate (bool v) { b.Set("NotifyReset", v); });
            p.Check("Include the per-model weekly limits", delegate { return b.Get("NotifyModels", false); }, delegate (bool v) { b.Set("NotifyModels", v); });
            p.Note("Each limit tells you once per window: once when it gets close, once when it's reached, and once when it resets.");
            p.Kind("Getting close shows as", KindClose);
            p.Kind("Reached shows as", KindReached);
            p.Kind("Reset shows as", KindReset);
        }

        // ---------- polling ----------

        static async Task PollOnce()
        {
            Options o = _opt;
            Snapshot s = await UsageClient.FetchAsync(o, Current).ConfigureAwait(false);
            Publish(s);
        }

        static void Publish(Snapshot s)
        {
            if (_ui == null || _ui.CheckAccess()) Apply(s);
            else _ui.BeginInvoke(new Action(delegate { Apply(s); }));
        }

        static void Apply(Snapshot s)
        {
            Current = s;
            if (s.Error.Length == 0 && s.HasData) CheckLimits(s);
        }

        /// <summary>A refresh someone asked for (card button, palette); at most every 20 seconds.</summary>
        public static bool RefreshNow()
        {
            if (Poll == null) return false;
            if (DateTime.UtcNow - _lastManual < TimeSpan.FromSeconds(20)) return false;
            _lastManual = DateTime.UtcNow;
            Poll.PollNow();
            return true;
        }

        // ---------- notifications ----------

        static bool Watched(Limit l, SettingsBag b)
        {
            if (l.Kind == "extra") return false;
            if (l.Model) return b.Get("NotifyModels", false);
            return true;
        }

        // A window is known by its limit and when it resets. Anthropic's reset time moves by a second or so from one check to the
        // next (24 "getting close" notices for one 5-hour window), so a reset time within half an hour of a marked one is the same
        // window - a new window always resets hours later.
        static readonly long SameWindow = TimeSpan.FromMinutes(30).Ticks;

        static string Period(Limit l) { return l.Key + "|" + (l.ResetsUtc.HasValue ? l.ResetsUtc.Value.Ticks.ToString() : "0"); }

        /// <summary>Whether "what" (close / reached / reset) was already said for this limit's window.</summary>
        static bool Marked(HashSet<string> sent, string what, Limit l)
        {
            string prefix = what + "|" + l.Key + "|";
            if (!l.ResetsUtc.HasValue) return sent.Contains(prefix + "0");
            long at = l.ResetsUtc.Value.Ticks;
            foreach (string k in sent)
            {
                if (!k.StartsWith(prefix, StringComparison.Ordinal)) continue;
                long t;
                if (long.TryParse(k.Substring(prefix.Length), out t) && t != 0 && Math.Abs(t - at) <= SameWindow) return true;
            }
            return false;
        }

        /// <summary>Marks "what" as said for this limit's window; false if it already was.</summary>
        static bool Mark(HashSet<string> sent, string what, Limit l)
        {
            if (Marked(sent, what, l)) return false;
            sent.Add(what + "|" + Period(l));
            return true;
        }

        static HashSet<string> Sent(SettingsBag b)
        {
            return new HashSet<string>((b.Get("Sent", "") ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        static void SaveSent(SettingsBag b, HashSet<string> sent)
        {
            // Forget windows that ended more than 8 days ago.
            long cutoff = DateTime.UtcNow.AddDays(-8).Ticks;
            var keep = sent.Where(delegate (string k)
            {
                int bar = k.LastIndexOf('|');
                long t;
                return bar < 0 || !long.TryParse(k.Substring(bar + 1), out t) || t == 0 || t > cutoff;
            }).ToList();
            b.Set("Sent", string.Join("\n", keep));
            try { _host.Manager.SaveSoon(); } catch { }
        }

        static void CheckLimits(Snapshot s)
        {
            if (_settings == null) return;
            SettingsBag b = Bag(_settings);
            HashSet<string> sent = Sent(b);
            bool dirty = false;
            double closeAt = b.Get("CloseAt", 80.0);
            DateTime now = DateTime.UtcNow;
            foreach (Limit l in s.Limits)
            {
                if (!Watched(l, b) || l.HasReset(now)) continue;
                if (l.Percent >= 100)
                {
                    if (Mark(sent, "reached", l))
                    {
                        dirty = true;
                        Mark(sent, "close", l);
                        if (b.Get("NotifyReached", true))
                            Post(KindReached, "Claude " + l.Name.ToLowerInvariant() + " limit reached", ResetLine(l, true));
                    }
                }
                else if (l.Percent >= closeAt)
                {
                    if (Mark(sent, "close", l))
                    {
                        dirty = true;
                        if (b.Get("NotifyClose", true))
                            Post(KindClose, "Claude " + l.Name.ToLowerInvariant() + " limit at " + Fmt.Pct(l.Percent), ResetLine(l, true));
                    }
                }
            }
            if (dirty) SaveSent(b, sent);
        }

        /// <summary>Every 5 seconds (vShell's notification check): tells you when a limit you reached has reset.</summary>
        static void WatchResets(BarSettings s)
        {
            // vShell runs this on its watcher thread. The "Sent" marks are also written by CheckLimits (UI thread), and saving goes
            // through the manager's UI timer - so do it all on the UI thread, like Publish does.
            if (_ui != null && !_ui.CheckAccess()) { _ui.BeginInvoke(new Action(delegate { WatchResets(s); })); return; }
            Snapshot snap = Current;
            if (s == null || !snap.HasData) return;
            SettingsBag b = Bag(s);
            DateTime now = DateTime.UtcNow;
            HashSet<string> sent = null;
            bool dirty = false, any = false;
            foreach (Limit l in snap.Limits)
            {
                if (!l.HasReset(now) || l.Percent < 100 || !Watched(l, b)) continue;
                any = true;
                if (sent == null) sent = Sent(b);
                if (!Marked(sent, "reached", l)) continue;
                if (Mark(sent, "reset", l))
                {
                    dirty = true;
                    if (b.Get("NotifyReset", true))
                        Post(KindReset, "Claude " + l.Name.ToLowerInvariant() + " limit has reset", "You can use Claude again.");
                }
            }
            if (dirty) SaveSent(b, sent);
            // A window ended: get the new one's numbers (at most once a minute).
            if (any && Poll != null && now - Poll.LastPoll > TimeSpan.FromMinutes(1)) Poll.PollNow();
        }

        static void Post(string kind, string title, string detail)
        {
            try { Notify.Post(Source, title, detail, UsageClient.UsagePage, Notify.KindOf(kind), true, null); }
            catch (Exception ex) { Log.Write("Claude usage: notify failed: " + ex.Message); }
        }

        static string ResetLine(Limit l, bool sentence)
        {
            if (!l.ResetsUtc.HasValue) return "";
            return (sentence ? "Resets " : "resets ") + Fmt.When(l.ResetsUtc.Value) + " (" + Fmt.In(l.ResetsUtc.Value) + ")";
        }

        // ---------- command palette ----------

        public static string Summary(Snapshot s)
        {
            DateTime now = DateTime.UtcNow;
            var parts = new List<string>();
            foreach (Limit l in s.Limits)
                if (!l.Model && l.Kind != "extra") parts.Add(l.Name + " " + Fmt.Pct(l.Now(now)));
            if (parts.Count == 0) foreach (Limit l in s.Limits) parts.Add(l.Name + " " + Fmt.Pct(l.Now(now)));
            return parts.Count == 0 ? "no limits" : string.Join(" · ", parts);
        }

        static void Commands(BarSettings s, List<PaletteCommand> list)
        {
            Snapshot snap = Current;
            string detail = snap.HasData ? Summary(snap) : (snap.Error.Length > 0 ? snap.Error : "Not checked yet");
            list.Add(new PaletteCommand
            {
                Title = "Claude usage", Detail = detail, Glyph = Glyph, Source = Source, Opens = true,
                Words = "claude usage limits anthropic plan 5-hour weekly session",
                Run = delegate (double n) { NoteRender.Open(UsageClient.UsagePage); return Task.FromResult<string>(null); }
            });
            list.Add(new PaletteCommand
            {
                Title = "Refresh Claude usage", Detail = snap.HasData ? "Checked " + TimeText.Ago(snap.FetchedUtc) : "", Glyph = "\uE72C",
                Source = Source, Words = "claude usage refresh update check",
                Run = delegate (double n)
                {
                    return Task.FromResult(RefreshNow() ? PaletteCommand.Worked("Refreshing Claude usage")
                                                       : PaletteCommand.Worked("Claude usage was just refreshed"));
                }
            });
        }
    }
}
