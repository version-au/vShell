using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;
using vShell.Ui;

namespace ClaudeUsage
{
    /// <summary>Times and percents as the user has them (12/24-hour from vShell).</summary>
    public static class Fmt
    {
        public static string Pct(double p) { return Math.Round(p).ToString("0") + "%"; }

        /// <summary>"3:15 PM" today, "Tomorrow 3:15 PM", or "Fri 3:15 PM".</summary>
        public static string When(DateTime utc)
        {
            DateTime local = utc.ToLocalTime();
            DateTime today = DateTime.Now.Date;
            string clock = TimeText.Clock(local);
            if (local.Date == today) return clock;
            if (local.Date == today.AddDays(1)) return "tomorrow " + clock;
            if (local.Date < today.AddDays(7)) return local.ToString("ddd") + " " + clock;
            return local.ToString("d MMM") + " " + clock;
        }

        /// <summary>"in 2h 10m", "in 3d 4h", "in 12m", "now".</summary>
        public static string In(DateTime utc)
        {
            TimeSpan d = utc - DateTime.UtcNow;
            if (d <= TimeSpan.Zero) return "now";
            if (d.TotalMinutes < 1) return "in under a minute";
            if (d.TotalHours < 1) return "in " + (int)Math.Ceiling(d.TotalMinutes) + "m";
            if (d.TotalDays < 1) return "in " + (int)d.TotalHours + "h " + d.Minutes + "m";
            return "in " + (int)d.TotalDays + "d " + d.Hours + "h";
        }

        /// <summary>Short reset for the bar: "3:15 PM" today, else "Fri".</summary>
        public static string ShortWhen(DateTime utc)
        {
            DateTime local = utc.ToLocalTime();
            if (local.Date == DateTime.Now.Date) return TimeText.Clock(local);
            if (local.Date < DateTime.Now.Date.AddDays(7)) return local.ToString("ddd");
            return local.ToString("d MMM");
        }
    }

    /// <summary>One tile per limit (5h, week, …): a dot coloured by how close it is, the percent, and a meter.</summary>
    public sealed class UsageModule : TilesModule
    {
        string _shownKeys = "";        // which limits the tiles are for (+ layout options)
        List<Limit> _shown = new List<Limit>();
        Snapshot _drawnFor;            // what the tiles were last drawn from (vShell calls Update every second; the numbers change every few minutes)
        string _drawnSig = "";
        PopupWindow _pop;
        Snapshot _popFor;

        public UsageModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg) { Build(); }

        SettingsBag B { get { return Cfg.Bag; } }
        bool ShowSession { get { return B.Get("ShowSession", true); } }
        bool ShowWeekly { get { return B.Get("ShowWeekly", true); } }
        bool ShowModels { get { return B.Get("ShowModels", false); } }
        bool ShowExtra { get { return B.Get("ShowExtra", false); } }
        bool ShowNames { get { return B.Get("ShowNames", true); } }
        bool ShowReset { get { return B.Get("ShowReset", false); } }
        bool ShowMeter { get { return B.Get("ShowMeter", true); } }
        bool Ring { get { return B.Get("MeterStyle", "Bar") == "Ring"; } }
        double WarnAt { get { return B.Get("WarnAt", 80.0); } }
        double MeterScale { get { return Math.Max(0.5, Math.Min(3, B.Get("MeterSize", 100.0) / 100)); } }
        string FrontText { get { return (Cfg.Title ?? "").Trim(); } }

        List<Limit> Visible(Snapshot s)
        {
            var list = new List<Limit>();
            foreach (Limit l in s.Limits)
            {
                if (l.Session && !ShowSession) continue;
                if (l.Weekly && !ShowWeekly) continue;
                if (l.Model && !ShowModels) continue;
                if (l.Kind == "extra" && !ShowExtra) continue;
                list.Add(l);
            }
            return list;
        }

        string KeysOf(List<Limit> list)
        {
            var parts = new List<string>();
            foreach (Limit l in list) parts.Add(l.Key);
            return string.Join(",", parts) + "|" + ShowMeter + "|" + Ring + "|" + MeterScale + "|" + FrontText;
        }

        protected override void BuildTiles()
        {
            _shown = Visible(Plugin.Current);
            _shownKeys = KeysOf(_shown);
            _drawnFor = null;      // new tiles: fill them in on this Update whatever was drawn before
            double scale = MeterScale;
            foreach (Limit l in _shown)
            {
                Tile t = AddTile(true, ShowMeter, delegate { ToggleCard(); });
                if (t.Meter == null) continue;
                if (Ring)
                {
                    // Like vShell's drive ring: the ring first (86% of the bar's height, times the size setting) with the percent inside it,
                    // the name beside it, and no dot - the ring itself turns amber / red.
                    double hh = Math.Max(16, Bar.HostHeight);
                    double d = Math.Max(12, Math.Min(hh, Math.Round(hh * 0.86 * scale)));
                    var m = t.Meter;
                    m.GraphStyle = "Ring";
                    m.GraphW = m.GraphH = d;
                    m.CenterSize = Math.Max(6, d * 0.3);
                    m.Face = new Typeface(T.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                    m.CenterBrush = T.Fg;
                    m.Margin = new Thickness(0);
                    var row = t.Root.Child as StackPanel;
                    if (row != null) { row.Children.Remove(m); row.Children.Insert(0, m); }
                    t.Text.Margin = new Thickness(7, 0, 0, 0);
                    if (t.Dot != null) t.Dot.Visibility = Visibility.Collapsed;
                }
                else
                {
                    t.Meter.GraphW = Math.Round(t.Meter.GraphW * scale);
                    t.Meter.GraphH = Math.Min(Bar.HostHeight, Math.Round(t.Meter.GraphH * scale));
                }
            }
            // Nothing to show yet (checking, not signed in, …): one tile of our own for the message, so a click still opens the card.
            if (_shown.Count == 0) AddTile(true, false, delegate { ToggleCard(); });
            AddFront(Tiles[0]);
        }

        /// <summary>The module's text in front (vShell's Title setting), before the first tile's dot.</summary>
        void AddFront(Tile t)
        {
            string text = FrontText;
            var row = t.Root.Child as StackPanel;
            if (text.Length == 0 || row == null) return;
            row.Children.Insert(0, new TextBlock
            {
                Text = text, Foreground = T.Fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            });
        }

        public override void Update()
        {
            if (Plugin.Poll != null) Plugin.Poll.Want();
            Snapshot s = Plugin.Current;
            List<Limit> vis = Visible(s);
            if (KeysOf(vis) != _shownKeys) { Rebuild(); return; }

            if (_shown.Count == 0)
            {
                if (Tiles.Count == 0) return;
                string msg, tip;
                if (s.Loading) { msg = "Claude …"; tip = "Checking your Claude usage"; }
                else if (!s.SignedIn) { msg = "Claude: sign in"; tip = s.Error; }
                else if (s.Error.Length > 0) { msg = "Claude: " + (s.HasData ? "no limits" : "can't check"); tip = s.Error; }
                else { msg = "Claude: no limits"; tip = "Anthropic sent no limits you've chosen to show"; }
                if (Drawn(s, msg + "|" + tip)) return;
                SetDot(Tiles[0], s.Error.Length > 0 && !s.Loading ? StatusColors.Warn : T.FgDim);
                SetText(Tiles[0], msg, T.FgDim, tip + "\nClick for details");
                RefreshCard(s);
                return;
            }

            DateTime now = DateTime.UtcNow;
            bool stale = s.Error.Length > 0 && (now - s.FetchedUtc) > TimeSpan.FromMinutes(30);
            // Only redraw when something shown can have changed: a new answer, a limit's window ending, the minute (the "checked 2m ago"
            // and reset texts), or a look setting. Otherwise every second would measure the text and repaint the meters for nothing.
            var sig = new StringBuilder();
            sig.Append(now.Ticks / TimeSpan.TicksPerMinute).Append('|').Append(stale).Append('|').Append(ShowNames).Append(ShowReset).Append(WarnAt);
            foreach (Limit l in vis) sig.Append('|').Append(l.Key).Append('=').Append(Math.Round(l.Now(now))).Append(l.HasReset(now) ? 'r' : '-');
            if (Drawn(s, sig.ToString())) return;
            for (int i = 0; i < Tiles.Count && i < _shown.Count; i++)
            {
                // Take the newest numbers for this limit (the tile list only changes when the set does).
                Limit l = Find(vis, _shown[i].Key) ?? _shown[i];
                double pct = l.Now(now);
                Brush state = pct >= 100 ? StatusColors.Bad : pct >= WarnAt ? StatusColors.Warn : StatusColors.Good;
                SetDot(Tiles[i], stale ? T.FgDim : state);

                var m = Tiles[i].Meter;
                bool ring = m != null && Ring;      // the percent goes inside the ring, so the text is just the name (and reset)
                var bits = new List<string>();
                if (ShowNames) bits.Add(l.Short);
                if (!ring) bits.Add(Fmt.Pct(pct));
                string text = string.Join(" ", bits);
                if (ShowReset && l.ResetsUtc.HasValue && !l.HasReset(now)) text += (text.Length > 0 ? " · " : "") + Fmt.ShortWhen(l.ResetsUtc.Value);
                SetText(Tiles[i], text, stale ? T.FgDim : T.Fg, Tip(l, s, now));
                Tiles[i].Text.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

                if (m != null)
                {
                    string center = ring ? Fmt.Pct(pct) : m.Center;
                    Brush centerBrush = ring ? (stale ? T.FgDim : T.Fg) : m.CenterBrush;
                    Brush warnFill = pct >= 100 ? StatusColors.Bad : StatusColors.Warn;
                    bool changed = m.Value != pct || m.WarnAt != WarnAt || m.WarnBelow || !ReferenceEquals(m.WarnFill, warnFill)
                                   || m.Center != center || !ReferenceEquals(m.CenterBrush, centerBrush);
                    if (changed)
                    {
                        m.Center = center;
                        m.CenterBrush = centerBrush;
                        m.Value = pct;
                        m.WarnAt = WarnAt;
                        m.WarnBelow = false;
                        m.WarnFill = warnFill;
                        m.InvalidateVisual();
                    }
                }
            }
            RefreshCard(s);
        }

        /// <summary>True when the tiles already show this (same answer, same texts): nothing to draw. Else remembers it as drawn.</summary>
        bool Drawn(Snapshot s, string sig)
        {
            if (ReferenceEquals(s, _drawnFor) && sig == _drawnSig) { RefreshCard(s); return true; }
            _drawnFor = s; _drawnSig = sig;
            return false;
        }

        static Limit Find(List<Limit> list, string key)
        {
            foreach (Limit l in list) if (l.Key == key) return l;
            return null;
        }

        static string Tip(Limit l, Snapshot s, DateTime now)
        {
            string t = "Claude " + l.Name.ToLowerInvariant() + ": " + Fmt.Pct(l.Now(now)) + " used";
            if (l.HasReset(now)) t += "\nReset at " + Fmt.When(l.ResetsUtc.Value) + " - checking the new window";
            else if (l.ResetsUtc.HasValue) t += "\nResets " + Fmt.When(l.ResetsUtc.Value) + " (" + Fmt.In(l.ResetsUtc.Value) + ")";
            if (l.Active) t += "\nThis is the limit you're on now";
            if (s.Error.Length > 0) t += "\n\n" + s.Error + "\nLast checked " + TimeText.Ago(s.FetchedUtc);
            else t += "\nChecked " + TimeText.Ago(s.FetchedUtc);
            return t;
        }

        // ---------- the card ----------

        void ToggleCard()
        {
            if (_pop != null && _pop.IsOpen) { _pop.CloseSafe(); _pop = null; return; }
            Snapshot s = Plugin.Current;
            if (!s.HasData || DateTime.UtcNow - s.FetchedUtc > TimeSpan.FromMinutes(1)) Plugin.RefreshNow();
            _popFor = s;
            _pop = new PopupWindow(Mgr, Bar, this, T, new Border());
            if (Bar.IsWidget) _pop.UseWidgetStyle(T, Mgr.Settings.WidgetStyle);
            _pop.SetContent(Card(s));      // after UseWidgetStyle, so ContentRoom is right
            Mgr.ShowPopup(_pop);
        }

        void RefreshCard(Snapshot s)
        {
            if (_pop == null || !_pop.IsOpen || ReferenceEquals(s, _popFor)) return;
            _popFor = s;
            _pop.SetContent(Card(s));
        }

        FrameworkElement Card(Snapshot s)
        {
            DateTime now = DateTime.UtcNow;
            const double W = 290;
            var root = new StackPanel { Width = W, Margin = new Thickness(14, 12, 14, 12) };

            var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };
            var plan = Label(s.Plan, T.FgDim, T.FontSize, false);
            DockPanel.SetDock(plan, Dock.Right);
            plan.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(plan);
            head.Children.Add(Label("Claude usage", T.Fg, T.FontSize + 2, true));
            root.Children.Add(head);

            if (s.Limits.Count == 0)
            {
                string why = s.Loading ? "Checking…" : s.Error.Length > 0 ? s.Error : "Anthropic sent no limits for this sign-in.";
                var t = Label(why, T.FgDim, T.FontSize, false);
                t.TextWrapping = TextWrapping.Wrap;
                root.Children.Add(t);
            }

            foreach (Limit l in s.Limits)
            {
                double pct = l.Now(now);
                Brush state = pct >= 100 ? StatusColors.Bad : pct >= WarnAt ? StatusColors.Warn : T.MainAccent;

                var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 3) };
                var num = Label(Fmt.Pct(pct), pct >= WarnAt ? state : T.Fg, T.FontSize + 1, true);
                DockPanel.SetDock(num, Dock.Right);
                row.Children.Add(num);
                var name = new StackPanel { Orientation = Orientation.Horizontal };
                name.Children.Add(Label(l.Name, T.Fg, T.FontSize, false));
                if (l.Active)
                {
                    var now2 = Label("  now", T.MainAccent, T.FontSize - 1.5, false);
                    now2.VerticalAlignment = VerticalAlignment.Center;
                    now2.ToolTip = "The limit you're on now";
                    name.Children.Add(now2);
                }
                row.Children.Add(name);
                root.Children.Add(row);

                root.Children.Add(ModulePics.Meter(T, W, 6, Math.Max(0, Math.Min(1, pct / 100)), state));

                string when;
                if (l.HasReset(now)) when = "Reset " + Fmt.When(l.ResetsUtc.Value) + " - checking the new window";
                else if (l.ResetsUtc.HasValue) when = "Resets " + Fmt.When(l.ResetsUtc.Value) + " · " + Fmt.In(l.ResetsUtc.Value);
                else when = "";
                if (when.Length > 0)
                {
                    var w = Label(when, T.FgDim, T.FontSize - 1, false);
                    w.Margin = new Thickness(0, 3, 0, 4);
                    root.Children.Add(w);
                }
            }

            string foot;
            Brush footBrush = T.FgDim;
            if (s.Error.Length > 0 && s.HasData) { foot = s.Error + "\nLast checked " + TimeText.Ago(s.FetchedUtc); footBrush = StatusColors.Warn; }
            else if (s.HasData) foot = "Checked " + TimeText.Ago(s.FetchedUtc);
            else foot = "";
            if (foot.Length > 0 && s.Limits.Count > 0)
            {
                var f = Label(foot, footBrush, T.FontSize - 1, false);
                f.TextWrapping = TextWrapping.Wrap;
                f.Margin = new Thickness(0, 10, 0, 0);
                root.Children.Add(f);
            }

            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var refresh = Pills.Make(T, "\uE72C", "Refresh", "Check again now", false, delegate { Plugin.RefreshNow(); }, false);   // the card redraws when the new numbers come in
            refresh.Margin = new Thickness(0, 0, 6, 0);
            buttons.Children.Add(refresh);
            buttons.Children.Add(Pills.Make(T, "\uE8A7", "Usage page", UsageClient.UsagePage, false, delegate
            {
                if (_pop != null) _pop.CloseSafe();
                NoteRender.Open(UsageClient.UsagePage);
            }, false));
            root.Children.Add(buttons);

            var scroll = new ScrollViewer
            {
                Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            if (_pop != null) scroll.MaxHeight = _pop.ContentRoom();
            WidgetWindow.StyleScrollbars(scroll);
            return scroll;
        }

        TextBlock Label(string text, Brush b, double size, bool bold)
        {
            return new TextBlock
            {
                Text = text ?? "", Foreground = b, FontFamily = T.Font, FontSize = Math.Max(9, size),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        // ---------- the module's settings ----------

        public static void BuildSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            SettingsBag b = cfg.Bag;
            p.Header("Limits to show");
            p.Check("5-hour session", delegate { return b.Get("ShowSession", true); }, delegate (bool v) { b.Set("ShowSession", v); });
            p.Check("Weekly (all models)", delegate { return b.Get("ShowWeekly", true); }, delegate (bool v) { b.Set("ShowWeekly", v); });
            p.Check("Weekly per model (when your plan has them)", delegate { return b.Get("ShowModels", false); }, delegate (bool v) { b.Set("ShowModels", v); });
            p.Check("Extra usage (when it's turned on)", delegate { return b.Get("ShowExtra", false); }, delegate (bool v) { b.Set("ShowExtra", v); });

            p.Header("Look");
            p.Title(cfg, "Claude");
            p.Check("Names (5h, Wk)", delegate { return b.Get("ShowNames", true); }, delegate (bool v) { b.Set("ShowNames", v); });
            p.Check("Meters", delegate { return b.Get("ShowMeter", true); }, delegate (bool v) { b.Set("ShowMeter", v); });
            p.Combo("Meter style", new[] { "Bar", "Ring" }, delegate { return b.Get("MeterStyle", "Bar"); }, delegate (string v) { b.Set("MeterStyle", v); });
            p.Slider("Meter size (%)", 50, 300, delegate { return b.Get("MeterSize", 100.0); }, delegate (double v) { b.Set("MeterSize", Math.Round(v / 5) * 5); });
            p.Check("When it resets", delegate { return b.Get("ShowReset", false); }, delegate (bool v) { b.Set("ShowReset", v); });
            p.Slider("Amber from (% used)", 50, 99, delegate { return b.Get("WarnAt", 80.0); }, delegate (double v) { b.Set("WarnAt", Math.Round(v)); });
            p.Note("The dot is green, amber from this point, and red when a limit is reached (with the ring, the ring itself changes colour and shows the percent inside). Click the module for every limit and when each resets.");
        }
    }

    /// <summary>The gallery pictures.</summary>
    public static class Pictures
    {
        public static FrameworkElement Bar(Theme t)
        {
            return ModulePics.Row(10, new UIElement[]
            {
                Tile(t, "5h 42%", 0.42, StatusColors.Good),
                Tile(t, "Wk 84%", 0.84, StatusColors.Warn)
            });
        }

        public static FrameworkElement Widget(Theme t)
        {
            return ModulePics.Col(8, new UIElement[]
            {
                Tile(t, "5h 42%", 0.42, StatusColors.Good),
                Tile(t, "Wk 84%", 0.84, StatusColors.Warn),
                Tile(t, "Opus 12%", 0.12, StatusColors.Good)
            });
        }

        static UIElement Tile(Theme t, string text, double frac, Brush dot)
        {
            var label = ModulePics.T(t, text, t.Fg, 12, false);
            label.VerticalAlignment = VerticalAlignment.Center;
            var meter = ModulePics.Meter(t, 30, 5, frac, frac >= 0.8 ? StatusColors.Warn : t.MainAccent);
            meter.VerticalAlignment = VerticalAlignment.Center;
            var d = ModulePics.Dot(dot, 7);
            d.VerticalAlignment = VerticalAlignment.Center;
            return ModulePics.Row(5, new UIElement[] { d, label, meter });
        }
    }
}
