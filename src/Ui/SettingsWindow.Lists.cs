using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Settings > Bars and Settings > Widgets: a row for each bar / widget with a short summary (a widget's has its Show switch), each
    /// opening its own page with tabs; then what every bar / widget shares (the style, and the widgets' snapping and gaps).
    /// </summary>
    public sealed partial class SettingsWindow
    {
        /// <summary>The tab a bar's page shows: Layout, Look or Modules.</summary>
        string _barTab = "Layout";
        /// <summary>The tab a widget's page shows: General, Layout, Look, Slide away or Modules.</summary>
        string _widTab = "General";

        // the sidebar's glyphs for bars, widgets and Appearance
        static readonly string BarGlyph = char.ConvertFromUtf32(0xE7C4), WidgetGlyph = char.ConvertFromUtf32(0xE8A9), StyleGlyph = char.ConvertFromUtf32(0xE790);

        void OpenBar(BarPlacement b)
        {
            _selBar = b;
            LoadBar();
            Navigate(PgBar);
        }

        void OpenWidget(WidgetSettings w)
        {
            _selWidget = w;
            LoadWidget();
            Navigate(PgWidget);
        }

        void AddBarAndOpen()
        {
            _mgr.AddBar();
            if (S.Bars.Count > 0) OpenBar(S.Bars[S.Bars.Count - 1]);
        }

        void AddWidgetAndOpen()
        {
            OpenWidget(_mgr.AddWidget());
        }

        /// <summary>A list page again (a switch changed what a row says), keeping the scroll position.</summary>
        void RebuildList(string key)
        {
            if (_page != key) return;
            double at = _content.VerticalOffset;
            if (key == PgBars) BuildBarsList(); else BuildWidgetsList();
            _content.ScrollToVerticalOffset(at);
        }

        // ---- names and summaries ----
        static string BarTitle(BarPlacement b)
        {
            return char.ToUpperInvariant(b.Edge[0]) + b.Edge.Substring(1).ToLowerInvariant() + " bar";
        }

        /// <summary>"Monitor 2", or "Main monitor" for the primary one (empty = the primary).</summary>
        static string MonitorText(string device, List<MonitorInfo> mons)
        {
            for (int i = 0; i < mons.Count; i++)
                if (string.Equals(mons[i].DeviceName, device, StringComparison.OrdinalIgnoreCase)) return "Monitor " + (i + 1);
            return "Main monitor";
        }

        /// <summary>Said of a bar or widget whose monitor is turned off or unplugged.</summary>
        const string AwayText = "Its monitor isn't connected - hidden until it's back";

        /// <summary>An amber dot: its monitor isn't connected.</summary>
        static Border AwayDot()
        {
            Border d = Dot(StatusColors.Warn);
            d.ToolTip = AwayText;
            return d;
        }

        string BarSummary(BarPlacement b, List<MonitorInfo> mons)
        {
            var parts = new List<string>();
            parts.Add(!MonitorHelper.IsConnected(b.Monitor, b.MonitorKey) ? AwayText : MonitorText(b.Monitor, mons));
            parts.Add(Styles.LayoutOf(b));
            parts.Add(b.BarHeight + " px");
            parts.Add(b.Modules.Count == 1 ? "1 module" : b.Modules.Count + " modules");
            if (b.ReserveSpace) parts.Add("reserves space");
            parts.Add(b.FollowStyle ? "follows the bar style" : "its own look");
            return string.Join("  ·  ", parts);
        }

        string WidgetSummary(WidgetSettings w, List<MonitorInfo> mons, bool withMonitor)
        {
            var parts = new List<string>();
            if (!w.Enabled) parts.Add("Hidden");
            if (withMonitor) parts.Add(!MonitorHelper.IsConnected(w.Monitor, w.MonitorKey) ? AwayText : MonitorText(w.Monitor, mons));
            parts.Add(w.ShowTabs ? "Tabs" : w.Direction == "Grid" ? "Grid, " + w.GridColumns + " columns" : w.Direction == "Horizontal" ? "Row" : "Stack");
            parts.Add(w.Modules.Count == 1 ? "1 module" : w.Modules.Count + " modules");
            if (w.Width > 0 || w.Height > 0) parts.Add((w.Width > 0 ? w.Width.ToString() : "auto") + " × " + (w.Height > 0 ? w.Height.ToString() : "auto"));
            if (w.AutoHide)
            {
                WidgetWindow hw = _mgr.WidgetWindowFor(w);
                List<string> ways = hw != null ? hw.HideWays() : new List<string>();
                parts.Add(ways.Count == 0 ? "slides away (not at a screen edge now)"
                          : "slides away at the " + WidgetWindow.EdgeName(ways.Contains(w.AutoHideWay) ? w.AutoHideWay : ways[0]) + " edge");
            }
            if (w.AlwaysOnTop) parts.Add("above other windows");
            if (w.Locked) parts.Add("locked");
            if (!w.FollowStyle) parts.Add("its own look");
            return string.Join("  ·  ", parts);
        }

        static string StyleText(string name) { return string.IsNullOrEmpty(name) ? "Your own" : name; }

        // =================================================================
        // Bars
        // =================================================================
        void BuildBarsList()
        {
            StackPanel p = Page(PgBars);
            p.Children.Clear();
            _cardOf.Remove(p);
            _notesAsInfo.Add(p);
            bool old = _loading;
            _loading = true;
            try
            {
                PageHeader(p, "Bars", "The bars along your screen edges - click one for its settings", Btn("+ Add bar", AddBarAndOpen, true));
                var mons = MonitorHelper.GetAll();
                // the bars on connected monitors, then those whose monitor is turned off or unplugged (hidden meanwhile)
                var shown = new List<BarPlacement>();
                var away = new List<BarPlacement>();
                foreach (BarPlacement b in S.Bars) (MonitorHelper.IsConnected(b.Monitor, b.MonitorKey) ? shown : away).Add(b);
                if (shown.Count > 0 || away.Count == 0) Header(p, "Your bars");
                if (S.Bars.Count == 0) Place(p, Txt("No bars yet - + Add bar puts one on your main monitor.", "vsDim", 12.5, false));
                foreach (BarPlacement b0 in shown)
                {
                    BarPlacement b = b0;
                    Border dot = Dot(StatusColors.Good);
                    dot.ToolTip = "Showing";
                    LinkRow(p, BarGlyph, BarTitle(b), BarSummary(b, mons), dot, delegate { OpenBar(b); });
                }
                if (away.Count > 0)
                {
                    Header(p, "Monitor not connected");
                    foreach (BarPlacement b0 in away)
                    {
                        BarPlacement b = b0;
                        LinkRow(p, BarGlyph, BarTitle(b), BarSummary(b, mons), AwayDot(), delegate { OpenBar(b); });
                    }
                    Place(p, Txt("These come back when their monitor does. To use one now, open it and pick another Monitor.", "vsDim", 12, false));
                }

                Header(p, "Shared by every bar");
                LinkRow(p, StyleGlyph, "Bar style", StyleText(S.BarStyleName) + "  ·  the look of the bars that follow it", null,
                        delegate { OpenLook("barstyle", PgBars); });
                Place(p, Txt("Drag a bar by an empty area to move it to another edge or monitor.", "vsDim", 12, false));
            }
            finally { _loading = old; }
        }

        // =================================================================
        // Widgets
        // =================================================================
        void BuildWidgetsList()
        {
            StackPanel p = Page(PgWidgets);
            p.Children.Clear();
            _cardOf.Remove(p);
            _notesAsInfo.Add(p);
            bool old = _loading;
            _loading = true;
            try
            {
                PageHeader(p, "Widgets", "Boxes on the desktop that hold modules - click one for its settings", Btn("+ Add widget", AddWidgetAndOpen, true));
                var mons = MonitorHelper.GetAll();

                // a card per monitor (one card when there's one monitor); widgets whose monitor is unplugged last
                const string unplugged = "Monitor not connected";
                var groups = new List<KeyValuePair<string, List<WidgetSettings>>>();
                foreach (WidgetSettings w in S.Widgets)
                {
                    string g = !MonitorHelper.IsConnected(w.Monitor, w.MonitorKey) ? unplugged
                             : mons.Count <= 1 ? "Your widgets" : MonitorText(w.Monitor, mons);
                    int at = groups.FindIndex(delegate (KeyValuePair<string, List<WidgetSettings>> x) { return x.Key == g; });
                    if (at < 0) { groups.Add(new KeyValuePair<string, List<WidgetSettings>>(g, new List<WidgetSettings>())); at = groups.Count - 1; }
                    groups[at].Value.Add(w);
                }
                groups.Sort(delegate (KeyValuePair<string, List<WidgetSettings>> a, KeyValuePair<string, List<WidgetSettings>> b)
                {
                    bool aa = a.Key == unplugged, bb = b.Key == unplugged;
                    return aa != bb ? (aa ? 1 : -1) : string.CompareOrdinal(a.Key, b.Key);
                });
                if (groups.Count == 0)
                {
                    Header(p, "Your widgets");
                    Place(p, Txt("No widgets yet - + Add widget puts an empty one on your desktop.", "vsDim", 12.5, false));
                }
                foreach (KeyValuePair<string, List<WidgetSettings>> g in groups)
                {
                    Header(p, g.Key);
                    bool away = g.Key == unplugged;
                    foreach (WidgetSettings w0 in g.Value)
                    {
                        WidgetSettings w = w0;
                        string glyph = w.Modules.Count > 0 ? IconUtil.ModuleGlyph(w.Modules[0].Type) : WidgetGlyph;
                        CheckBox show = Switch(delegate { return w.Enabled; }, delegate (bool v) { w.Enabled = v; }, delegate { RebuildList(PgWidgets); });
                        show.ToolTip = "Show this widget";
                        UIElement end = show;
                        if (away)
                        {
                            // amber dot before the switch: hidden until its monitor is back
                            var both = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                            Border dot = AwayDot();
                            dot.Margin = new Thickness(0, 0, 12, 0);
                            both.Children.Add(dot);
                            both.Children.Add(show);
                            end = both;
                        }
                        LinkRow(p, glyph, w.Name, WidgetSummary(w, mons, false), end, delegate { OpenWidget(w); });
                    }
                    if (away) Place(p, Txt("These come back when their monitor does. To use one now, open it and pick another Monitor.", "vsDim", 12, false));
                }

                Header(p, "Shared by every widget");
                LinkRow(p, StyleGlyph, "Widget style", StyleText(S.WidgetStyleName) + "  ·  corners, outline, shadow and spacing of the widgets that follow it",
                        null, delegate { OpenLook("widgetstyle", PgWidgets); });

                Header(p, "Snapping and gaps");
                Check(p, "Snap widgets while dragging", delegate { return S.WidgetSnap; }, delegate (bool v) { S.WidgetSnap = v; },
                      "While you drag a widget it snaps to the edges of the other widgets, to the gap below from its " +
                      "neighbours, and to the edge gap from the screen edges and the bars - the edges that line up glow. Hold Alt while dropping to place one freely.");
                SliderRow(p, "Gap between widgets", 0, 60, delegate { return S.WidgetGap; }, delegate (double v) { S.WidgetGap = (int)v; },
                          "Measured between the widget boxes, inside their outlines, so a thick outline makes the gap look a little smaller.");
                SliderRow(p, "Gap from the screen edges and bars", 0, 60, delegate { return S.WidgetEdgeGap; }, delegate (double v) { S.WidgetEdgeGap = (int)v; });
                Check(p, "Re-space snapped widgets when the gap changes", delegate { return S.RespaceSnapped; }, delegate (bool v) { S.RespaceSnapped = v; },
                      "What a widget snaps to is remembered: when a widget grows or shrinks (a new style, font, collapsing it …) the widgets snapped to it " +
                      "move with it and keep the gap, and one snapped to the right or bottom screen edge grows towards the left / top. Dragging a widget " +
                      "away lets go. Re-spacing never pushes a widget off the screen.");
                Place(p, Txt("To line several up: Ctrl+click each widget, then right-click one for Align (edges or centres) and Distribute (equal gaps, " +
                             "or exactly the gap above). Drag any of the picked widgets and they all move together; locked ones stay put. " +
                             "Right-click > Clear selection when you're done.", "vsDim", 12, false));
            }
            finally { _loading = old; }
        }
    }
}
