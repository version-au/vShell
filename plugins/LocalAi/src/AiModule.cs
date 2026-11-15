using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

namespace LocalAi
{
    /// <summary>Numbers as they're shown: "41k", "14 tok/s", "8k".</summary>
    static class Fmt
    {
        public static string K(long n) { return n >= 1000000 ? (n / 1e6).ToString("0.#") + "M" : n >= 1000 ? (n / 1e3).ToString(n >= 10000 ? "0" : "0.#") + "k" : n.ToString(); }
        public static string Ctx(int n) { return n >= 1024 ? Math.Round(n / 1024.0) + "k" : n > 0 ? n.ToString() : ""; }
        /// <summary>A model's name without the file kind ("Qwen3.5-4B-GGUF" -> "Qwen3.5-4B").</summary>
        public static string Short(string model) { return System.Text.RegularExpressions.Regex.Replace(model ?? "", @"[-_.](GGUF|gguf|Q\d_K_[A-Z]+|Q\d_\d|FLM|onnx|Hybrid|NPU)$", ""); }
        public static string Tps(double t) { return t >= 10 ? Math.Round(t) + " tok/s" : t.ToString("0.0") + " tok/s"; }
    }

    // =====================================================================================
    // The run box: !ai alone -> your servers; !ai a question -> Enter asks the main server, the answer under the box
    // =====================================================================================
    static class RunBox
    {
        sealed class State { public int Gen; public CancellationTokenSource Cancel; }

        /// <summary>The run box action's kind - also what AddBang is given, so the run box finds these rows.</summary>
        public const string Kind = "LocalAi";

        public static RunAction Parse(string key, string q, BarSettings s)
        {
            AiSettings c = AiSettings.Of(s);
            if (!c.Configured || c.Bang.Length == 0 || key != c.Bang) return null;
            string model = Plugin.ModelFor(c);
            return new RunAction
            {
                Kind = Kind, Query = q,
                Display = q.Length == 0 ? "Local AI: your servers" : "Ask " + (model.Length > 0 ? Fmt.Short(model) : "your AI") + ": " + q
            };
        }

        public static void Rows(RunPanel box, RunAction a)
        {
            State st = box.PluginState.TryGetValue("localai", out object o) ? (State)o : null;
            if (st == null) { st = new State(); box.PluginState["localai"] = st; }
            if (a.Query.Length == 0)
            {
                if (Plugin.Poll != null) Plugin.Poll.Want();
                foreach (ServerState s in Plugin.Current)
                    box.AddRow(Plugin.Glyph, s.Name + "  ·  " + (s.Online ? (s.Loaded.Count > 0 ? Fmt.Short(s.Loaded[0].Name) + (s.Busy ? "  ·  answering" : "  ·  idle") : "no model loaded") : "offline"),
                               s.KindName, delegate { }, null);
                box.Status(Plugin.Current.Count == 0 ? "Looking at your AI server…" : "Type a question after !" + AiSettings.Of(box.Settings).Bang + " and press Enter");
                return;
            }
            string q = a.Query;
            box.AddRow(Plugin.Glyph, a.Display, "Enter", delegate { box.Remember(box.Text); Ask(box, st, q); }, null);
            box.Select(0);
        }

        static async void Ask(RunPanel box, State st, string q)
        {
            int gen = ++st.Gen;
            if (st.Cancel != null) try { st.Cancel.Cancel(); } catch { }
            st.Cancel = new CancellationTokenSource();
            box.Hold();
            string model = Fmt.Short(Plugin.ModelFor(AiSettings.Of(box.Settings)));
            box.Status("Asking " + (model.Length > 0 ? model : "your AI") + "… (a local model can take a while)");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string answer;
            try { answer = await Plugin.AskAsync(q, st.Cancel.Token); }
            catch (Exception ex)
            {
                if (gen == st.Gen) box.Status("Local AI: " + (ex is OperationCanceledException ? "no answer in time" : ex.Message));
                return;
            }
            if (gen != st.Gen || !box.Held) return;
            box.Status(model + "  ·  " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
            Theme t = box.Theme;
            var text = new TextBlock { Text = answer, TextWrapping = TextWrapping.Wrap, Foreground = t.Fg, FontFamily = t.Font, FontSize = t.FontSize, Margin = new Thickness(8, 6, 8, 4), LineHeight = t.FontSize * 1.4 };
            var scroll = new ScrollViewer { Content = text, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            WidgetWindow.StyleScrollbars(scroll);
            box.AddElement(scroll);
            var copy = Pills.Make(t, "", "Copy", "Copy the answer", false, delegate { try { Clipboard.SetText(answer); } catch { } }, false);
            copy.HorizontalAlignment = HorizontalAlignment.Left;
            copy.Margin = new Thickness(8, 4, 8, 6);
            box.AddElement(copy);
        }
    }

    // =====================================================================================
    // The module: on a bar the main server's model and state; in a widget a card per server; click (bar) -> the cards with Load / Unload
    // =====================================================================================
    public sealed class AiModule : Border, IMultiRow
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;
        readonly DispatcherTimer _timer;
        PopupWindow _pop;
        string _shown = "", _popShown = "";

        public int RowCount { get { return _host.IsWidget ? 0 : 1; } }      // a widget's card sizes itself

        SettingsBag B { get { return _cfg.Bag; } }
        bool ShowModel { get { return B.Get("ShowModel", true); } }
        bool ShowState { get { return B.Get("ShowState", true); } }
        bool ShowSpeed { get { return B.Get("ShowSpeed", false); } }
        bool ShowAll { get { return B.Get("ShowAll", true); } }
        bool ShowChart { get { return B.Get("ShowChart", true); } }
        bool ShowModels { get { return B.Get("ShowModels", false); } }

        public AiModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            VerticalAlignment = host.IsWidget ? VerticalAlignment.Top : VerticalAlignment.Center;
            Padding = host.IsWidget ? new Thickness(4, 2, 4, 2) : new Thickness(6, 0, 6, 0);
            if (!host.IsWidget)
            {
                Cursor = Cursors.Hand;
                MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
                MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; TogglePopup(); };
            }
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += delegate { Refresh(); };
            Loaded += delegate { Refresh(); _timer.Start(); };
            Unloaded += delegate { _timer.Stop(); };
            Refresh();
        }

        void Refresh()
        {
            if (Plugin.Poll != null && IsVisible) Plugin.Poll.Want();
            List<ServerState> all = Plugin.Current;
            string sig = Sig(all) + "|" + (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute);
            if (_pop != null && _pop.IsOpen)
            {
                string ps = sig + "|" + string.Join(",", Plugin.Busy);
                if (ps != _popShown) { _popShown = ps; _pop.SetContent(Cards(all, true)); }
            }
            if (sig == _shown) return;
            _shown = sig;
            Child = _host.IsWidget ? Cards(all, false) : BarView(all);
            _host.ContentChanged();
        }

        string Sig(List<ServerState> all)
        {
            var b = new StringBuilder(ShowModel + "|" + ShowState + "|" + ShowSpeed + "|" + ShowAll + "|" + ShowChart + "|" + ShowModels + "|" + _cfg.Title);
            foreach (ServerState s in all)
            {
                b.Append('|').Append(s.Config.Id).Append(s.Online).Append(s.Kind).Append(s.Error).Append(s.Busy).Append(Math.Round(s.Tps)).Append(s.Requests).Append(s.Speed.Count).Append(s.Models.Count);
                foreach (LoadedModel l in s.Loaded) b.Append(l.Name).Append(l.Ctx);
                foreach (ModelInfo m in s.Models) b.Append(m.Loaded ? '+' : '-');
            }
            return b.ToString();
        }

        TextBlock T(string text, Brush fg, double size, bool bold = false)
        {
            return new TextBlock
            {
                Text = text ?? "", Foreground = fg, FontFamily = _t.Font, FontSize = Math.Max(8, size), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        /// <summary>A dot: green idle, the accent while answering, red offline, dim before the first look.</summary>
        Brush DotOf(ServerState s)
        {
            if (s == null || !s.Checked) return _t.FgDim;
            if (!s.Online) return StatusColors.Bad;
            return s.Busy ? (Brush)_t.MainAccent : StatusColors.Good;
        }

        // ---------- on a bar ----------
        FrameworkElement BarView(List<ServerState> all)
        {
            ServerState s = all.FirstOrDefault(x => x.Config.Main);
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = DotOf(s), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            string title = (_cfg.Title ?? "").Trim();
            if (title.Length > 0) row.Children.Add(T(title + " ", _t.FgDim, _t.FontSize));
            var bits = new List<string>();
            Brush fg = _t.Fg;
            if (s == null || !s.Checked) bits.Add("AI …");
            else if (!s.Online) { bits.Add(ShowModel ? "AI offline" : "offline"); fg = StatusColors.Bad; }
            else
            {
                if (ShowModel) bits.Add(s.Loaded.Count > 0 ? Fmt.Short(s.Loaded[0].Name) : "no model");
                if (ShowState) bits.Add(s.Busy ? "answering" : "idle");
                if (ShowSpeed && s.HasSpeed) bits.Add(Fmt.Tps(s.Tps));
                if (bits.Count == 0) bits.Add("AI");
            }
            row.Children.Add(T(string.Join(" · ", bits), fg, _t.FontSize));
            ToolTip = Tip(all);
            return row;
        }

        string Tip(List<ServerState> all)
        {
            if (all.Count == 0) return "Local AI: looking…";
            var l = new List<string>();
            foreach (ServerState s in all)
            {
                l.Add(s.Name + (s.KindName.Length > 0 ? " (" + s.KindName + (s.Version.Length > 0 ? " " + s.Version : "") + ")" : "") + ": " +
                      (!s.Online ? "offline - " + s.Error : (s.Loaded.Count > 0 ? string.Join(", ", s.Loaded.Select(x => x.Name)) : "no model loaded") + (s.Busy ? ", answering" : ", idle")));
                if (s.Online && s.HasSpeed) l.Add("  Last answer: " + Fmt.Tps(s.Tps) + (s.TtftSeconds > 0 ? ", " + s.TtftSeconds.ToString("0.0") + " s to the first word" : ""));
            }
            l.Add("Click for more (load or unload a model)");
            return string.Join("\n", l);
        }

        // ---------- the cards: a widget, and the bar's popup ----------
        FrameworkElement Cards(List<ServerState> all, bool popup)
        {
            var root = new StackPanel { Width = popup ? 320 : double.NaN, Margin = popup ? new Thickness(14, 12, 14, 12) : new Thickness(0) };
            if (all.Count == 0) root.Children.Add(T(AiSettings.Of(_mgr.Settings).Configured ? "Looking at your AI server…" : "Set up your AI server in Settings > Plugins > Local AI", _t.FgDim, _t.FontSize));
            IEnumerable<ServerState> list = ShowAll || popup ? all : all.Where(x => x.Config.Main);
            bool first = true;
            foreach (ServerState s in list)
            {
                if (!first) root.Children.Add(new Border { Height = 1, Background = Theme.Faded(_t.Fg, 15), Margin = new Thickness(0, 10, 0, 10) });
                first = false;
                root.Children.Add(Card(s, popup));
            }
            if (popup)
            {
                var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
                var refresh = Pills.Make(_t, "", "Refresh", "Look again now", false, delegate { Plugin.RefreshNow(); }, false);
                refresh.Margin = new Thickness(0, 0, 6, 0);
                buttons.Children.Add(refresh);
                buttons.Children.Add(Pills.Make(_t, "", "Settings", "Settings > Plugins > Local AI", false, delegate { if (_pop != null) _pop.CloseSafe(); _host.OpenSettings(); }, false));
                root.Children.Add(buttons);
                var scroll = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                if (_pop != null) scroll.MaxHeight = _pop.ContentRoom();
                WidgetWindow.StyleScrollbars(scroll);
                return scroll;
            }
            return root;
        }

        FrameworkElement Card(ServerState s, bool popup)
        {
            double fs = _host.IsWidget && _cfg.ShownFontSize > 0 ? _cfg.ShownFontSize : _t.FontSize;
            var st = new StackPanel();
            // the server, what it is, and its state
            var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            var state = new StackPanel { Orientation = Orientation.Horizontal };
            state.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = DotOf(s), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 5, 0) });
            state.Children.Add(T(!s.Checked ? "…" : !s.Online ? "offline" : s.Busy ? "answering" : "idle", !s.Online && s.Checked ? StatusColors.Bad : _t.FgDim, fs - 1));
            DockPanel.SetDock(state, Dock.Right);
            head.Children.Add(state);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(T(s.Name, _t.Fg, fs + 1, true));
            if (s.KindName.Length > 0) name.Children.Add(T("  " + s.KindName + (s.Version.Length > 0 ? " " + s.Version : ""), _t.FgDim, fs - 1.5));
            head.Children.Add(name);
            st.Children.Add(head);
            if (!s.Checked) { st.Children.Add(T("Looking…", _t.FgDim, fs - 1)); return st; }
            if (!s.Online)
            {
                var err = T(s.Error + "  ·  since " + TimeText.Clock(s.SinceUtc.ToLocalTime()), StatusColors.Bad, fs - 1);
                err.TextWrapping = TextWrapping.Wrap;
                st.Children.Add(err);
                return st;
            }
            // what's loaded
            if (s.Loaded.Count == 0)
                st.Children.Add(T(s.Kind == "openai" ? s.Models.Count + " model" + (s.Models.Count == 1 ? "" : "s") + " (it doesn't say which is loaded)" : "No model loaded", _t.FgDim, fs - 0.5));
            foreach (LoadedModel l in s.Loaded)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                line.Children.Add(T(Fmt.Short(l.Name), _t.Fg, fs, true));
                var extra = new List<string>();
                if (l.Ctx > 0) extra.Add("ctx " + Fmt.Ctx(l.Ctx));
                if (l.Device.Length > 0) extra.Add(l.Device);
                if (l.SizeGb > 0) extra.Add(l.SizeGb.ToString("0.0") + " GB");
                if (extra.Count > 0) line.Children.Add(T("   " + string.Join(" · ", extra), _t.FgDim, fs - 1.5));
                line.ToolTip = l.Name;
                st.Children.Add(line);
            }
            // speed: the recent answers as a chart, and the last one's
            if (s.HasSpeed || s.Speed.Count > 0)
            {
                var sp = new DockPanel { Margin = new Thickness(0, 6, 0, 2) };
                var num = T(s.HasSpeed ? Fmt.Tps(s.Tps) + (s.TtftSeconds > 0 ? "  ·  " + s.TtftSeconds.ToString("0.0") + " s to the first word" : "") : "", _t.FgDim, fs - 1.5);
                if (ShowChart && s.Speed.Count > 1)
                {
                    double[] vals = s.Speed.ToArray();
                    var spark = ModulePics.Spark(110, 20, _t.MainAccent, vals, true);
                    spark.Margin = new Thickness(0, 0, 8, 0);
                    spark.ToolTip = "The last " + vals.Length + " answers' speed (tokens a second): " + string.Join(", ", vals.Select(v => v.ToString("0.#")));
                    DockPanel.SetDock(spark, Dock.Left);
                    sp.Children.Add(spark);
                }
                sp.Children.Add(num);
                st.Children.Add(sp);
            }
            if (s.HasTotals && s.Requests > 0)
                st.Children.Add(T(s.Requests + " answer" + (s.Requests == 1 ? "" : "s") + " · " + Fmt.K(s.TokensIn) + " in · " + Fmt.K(s.TokensOut) + " out, since it started", _t.FgDim, fs - 1.5));
            var hw = new List<string>();
            if (s.Hardware.Length > 0) hw.Add(s.Hardware);
            if (s.StorageTotal > 0) hw.Add(Plugin.Gb(s.StorageFree) + " free for models");
            if (hw.Count > 0)
            {
                var h = T(string.Join("  ·  ", hw), s.StorageTotal > 0 && s.StorageFree < s.StorageTotal / 10 ? StatusColors.Warn : _t.FgDim, fs - 1.5);
                h.TextWrapping = TextWrapping.Wrap;
                st.Children.Add(h);
            }
            // its models, with Load / Unload (the popup, or a widget with "its models")
            if ((popup || ShowModels) && s.Models.Count > 0)
            {
                st.Children.Add(T("Models", _t.Fg, fs - 0.5, true));
                ((TextBlock)st.Children[st.Children.Count - 1]).Margin = new Thickness(0, 8, 0, 2);
                foreach (ModelInfo m in s.Models)
                {
                    var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2) };
                    if (s.CanLoad)
                    {
                        ServerState server = s;
                        string model = m.Id;
                        bool busy = Plugin.Busy.Contains(s.Config.Id + "|" + m.Id), load = !m.Loaded;
                        var pill = Pills.Make(_t, busy ? "" : load ? "" : "", busy ? (load ? "Loading…" : "Unloading…") : load ? "Load" : "Unload",
                                              load ? "Load it - " + (s.Kind == "lemonade" ? "Lemonade unloads the one loaded" : "it stays in memory an hour") : "Free its memory",
                                              false, delegate
                                              {
                                                  if (busy) return;
                                                  LoadClick(server, model, load);
                                              }, false);
                        pill.Margin = new Thickness(8, 0, 0, 0);
                        DockPanel.SetDock(pill, Dock.Right);
                        line.Children.Add(pill);
                    }
                    var n = T(Fmt.Short(m.Id) + (m.SizeGb > 0 ? "   " + m.SizeGb.ToString("0.0") + " GB" : ""), m.Loaded ? _t.Fg : _t.FgDim, fs - 1, m.Loaded);
                    n.ToolTip = m.Id;
                    line.Children.Add(n);
                    st.Children.Add(line);
                }
                if (!s.CanLoad) st.Children.Add(T(s.KindName + " loads a model when it's asked - no Load / Unload from here.", _t.FgDim, fs - 2));
            }
            if (popup) st.Children.Add(T("Checked " + TimeText.Ago(s.CheckedUtc), _t.FgDim, fs - 2));
            ((FrameworkElement)st.Children[st.Children.Count - 1]).Margin = new Thickness(0, 4, 0, 0);
            return st;
        }

        async void LoadClick(ServerState s, string model, bool load)
        {
            string err = await Plugin.LoadAsync(s, model, load);
            if (err != null) Questions.Tell(null, "Couldn't " + (load ? "load " : "unload ") + model + ": " + err, Plugin.Source);
        }

        void TogglePopup()
        {
            if (_pop != null && _pop.IsOpen) { _pop.CloseSafe(); _pop = null; return; }
            Plugin.RefreshNow();
            _pop = new PopupWindow(_mgr, _host, this, _t, new Border());
            if (_mgr.Settings.WidgetStyle != null) _pop.UseWidgetStyle(_t, _mgr.Settings.WidgetStyle);
            _popShown = "";
            _pop.SetContent(Cards(Plugin.Current, true));
            _mgr.ShowPopup(_pop);
        }

        // ---------- the module's settings ----------
        public static void BuildSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            SettingsBag b = cfg.Bag;
            if (!p.IsWidget)
            {
                p.Header("On the bar");
                p.Check("The model that's loaded", delegate { return b.Get("ShowModel", true); }, delegate (bool v) { b.Set("ShowModel", v); });
                p.Check("Idle / answering", delegate { return b.Get("ShowState", true); }, delegate (bool v) { b.Set("ShowState", v); });
                p.Check("The last answer's speed", delegate { return b.Get("ShowSpeed", false); }, delegate (bool v) { b.Set("ShowSpeed", v); });
                p.Title(cfg, "");
                p.Note("The dot is green when it's idle, the accent colour while it's answering and red when it's offline. Click it for every server, their models (Load / Unload) and speed.");
            }
            else
            {
                p.Header("In the widget");
                p.Check("Every server (off: only the main one)", delegate { return b.Get("ShowAll", true); }, delegate (bool v) { b.Set("ShowAll", v); });
                p.Check("The recent answers' speed as a chart", delegate { return b.Get("ShowChart", true); }, delegate (bool v) { b.Set("ShowChart", v); });
                p.Check("Its models, with Load / Unload", delegate { return b.Get("ShowModels", false); }, delegate (bool v) { b.Set("ShowModels", v); });
                p.Note("Speed and totals come from Lemonade itself; for Ollama and LM Studio, from the questions asked from vShell's run box.");
            }
        }
    }

    /// <summary>The gallery pictures.</summary>
    public static class Pictures
    {
        public static FrameworkElement Bar(Theme t)
        {
            return ModulePics.Row(6, new UIElement[] { ModulePics.Dot(StatusColors.Good, 7), ModulePics.T(t, "Qwen3.5-4B · idle", t.Fg, 12.5) });
        }

        public static FrameworkElement Widget(Theme t)
        {
            return ModulePics.Col(4, new UIElement[]
            {
                ModulePics.Row(6, new UIElement[] { ModulePics.T(t, "lemonade", t.Fg, 12, true), ModulePics.T(t, "Lemonade 11.9", t.FgDim, 10), ModulePics.Dot(StatusColors.Good, 6) }),
                ModulePics.Row(6, new UIElement[] { ModulePics.T(t, "Qwen3.5-4B", t.Fg, 11, true), ModulePics.T(t, "ctx 8k · GPU", t.FgDim, 10) }),
                ModulePics.Row(6, new UIElement[] { ModulePics.Spark(70, 14, t.MainAccent, ModulePics.Wave1, true), ModulePics.T(t, "14 tok/s", t.FgDim, 10) })
            });
        }
    }
}
