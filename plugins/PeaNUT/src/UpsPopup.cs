using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;
using vShell.Ui;

namespace VShellPeanut
{
    /// <summary>The popup a UPS tile opens: every UPS (or the one picked) with its readings, grouped by PeaNUT server.</summary>
    public static class UpsPopup
    {
        const double Width = 320;

        public static FrameworkElement Build(Theme t, string pick)
        {
            var root = new StackPanel { Margin = new Thickness(14, 12, 14, 12), Width = Width };
            var snap = Store.Snapshot();
            if (snap.Count == 0)
            {
                root.Children.Add(Text(t, "No PeaNUT server yet - add one in Settings > Plugins > PeaNUT.", t.FgDim, 12.5, false));
                return root;
            }

            bool first = true;
            foreach (var r in snap)
            {
                var ups = pick.Length == 0 ? r.Ups : r.Ups.Where(u => u.Key == pick).ToList();
                if (pick.Length > 0 && ups.Count == 0) continue;

                if (!first) root.Children.Add(Rule(t));
                first = false;

                if (snap.Count > 1 || !r.Ok || ups.Count == 0)
                    root.Children.Add(ServerHeader(t, r));

                if (!r.Ok && r.Checked != DateTime.MinValue)
                    root.Children.Add(Text(t, r.Error, StatusColors.Bad, 12, false, new Thickness(0, 2, 0, 6)));
                else if (r.Checked == DateTime.MinValue)
                    root.Children.Add(Text(t, "Checking…", t.FgDim, 12, false, new Thickness(0, 2, 0, 6)));
                else if (ups.Count == 0)
                    root.Children.Add(Text(t, "PeaNUT has no UPS - add a NUT server in PeaNUT's settings.", t.FgDim, 12, false, new Thickness(0, 2, 0, 6)));

                for (int i = 0; i < ups.Count; i++)
                {
                    if (i > 0) root.Children.Add(new Border { Height = 10 });
                    root.Children.Add(UpsCard(t, ups[i]));
                }

                if (snap.Count == 1 && r.Ok) root.Children.Add(OpenLink(t, r.Server, new Thickness(0, 10, 0, 0)));
            }

            if (first)
                root.Children.Add(Text(t, "The UPS this module shows isn't there now - pick another in the module's settings.", t.FgDim, 12.5, false));
            return root;
        }

        static FrameworkElement ServerHeader(Theme t, ServerResult r)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Text(t, r.Server.Shown, t.FgDim, 11.5, true);
            g.Children.Add(name);
            var open = OpenLink(t, r.Server, new Thickness(8, 0, 0, 0));
            Grid.SetColumn(open, 1);
            g.Children.Add(open);
            return g;
        }

        static FrameworkElement UpsCard(Theme t, UpsReading u)
        {
            var card = new StackPanel();

            // Name, and the status on the right
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = ModulePics.Dot(UpsModule.Colour(u, t), 9);
            dot.Margin = new Thickness(0, 0, 8, 0);
            dot.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(dot);
            var name = Text(t, u.Name, t.Fg, 13.5, true);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, 1);
            head.Children.Add(name);
            var status = Text(t, u.StatusText, UpsModule.Colour(u, t), 12, false);
            status.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(status, 2);
            head.Children.Add(status);
            card.Children.Add(head);

            // Battery meter
            double charge = u.Charge;
            if (!double.IsNaN(charge))
            {
                var meter = new MetricView
                {
                    GraphStyle = "Meter",
                    Value = Math.Max(0, Math.Min(100, charge)),
                    Fill = t.MainAccent,
                    Track = t.Track,
                    WarnAt = 25,
                    WarnBelow = true,
                    WarnFill = StatusColors.Bad,
                    GraphW = Width,
                    GraphH = 8
                };
                meter.Margin = new Thickness(0, 8, 0, 6);
                meter.HorizontalAlignment = HorizontalAlignment.Left;
                card.Children.Add(meter);
            }

            // Readings, two columns of label / value
            var rows = new List<KeyValuePair<string, string>>();
            Add(rows, "Battery", Fmt.Pct(u.Charge));
            Add(rows, "Runtime", Fmt.Runtime(u.RuntimeSeconds));
            Add(rows, "Load", Fmt.Pct(u.Load));
            Add(rows, "Power", Fmt.Watts(u.Watts));
            Add(rows, "Input", Fmt.Volts(u.Num("input.voltage")));
            Add(rows, "Output", Fmt.Volts(u.Num("output.voltage")));
            Add(rows, "Battery V", Fmt.Volts(u.Num("battery.voltage")));
            Add(rows, "Temperature", Fmt.Temp(u.Num("ups.temperature")));
            card.Children.Add(Pairs(t, rows));

            var foot = new List<string>();
            if (u.Model.Length > 0) foot.Add(u.Model);
            foot.Add(u.Device + (u.NutServer.Length > 0 ? " on " + u.NutServer : ""));
            if (u.Stale) foot.Add("Last reading " + TimeText.Ago(u.At));
            var ft = Text(t, string.Join("  ·  ", foot), t.FgDim, 11, false, new Thickness(0, 6, 0, 0));
            ft.TextWrapping = TextWrapping.Wrap;
            card.Children.Add(ft);
            return card;
        }

        static void Add(List<KeyValuePair<string, string>> rows, string label, string value)
        {
            if (value != "-") rows.Add(new KeyValuePair<string, string>(label, value));
        }

        static FrameworkElement Pairs(Theme t, List<KeyValuePair<string, string>> rows)
        {
            var g = new Grid();
            for (int c = 0; c < 4; c++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = c % 2 == 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
            int lines = (rows.Count + 1) / 2;
            for (int i = 0; i < lines; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < rows.Count; i++)
            {
                int row = i / 2, col = (i % 2) * 2;
                var l = Text(t, rows[i].Key, t.FgDim, 12, false, new Thickness(0, 2, 10, 2));
                var v = Text(t, rows[i].Value, t.Fg, 12, false, new Thickness(0, 2, col == 0 ? 16 : 0, 2));
                Grid.SetRow(l, row); Grid.SetColumn(l, col);
                Grid.SetRow(v, row); Grid.SetColumn(v, col + 1);
                g.Children.Add(l);
                g.Children.Add(v);
            }
            return g;
        }

        static FrameworkElement OpenLink(Theme t, PeanutServer s, Thickness margin)
        {
            var link = Text(t, "Open PeaNUT ›", t.MainAccent, 12, false, margin);
            link.Cursor = Cursors.Hand;
            link.HorizontalAlignment = HorizontalAlignment.Right;
            link.ToolTip = s.BaseUrl;
            string url = s.BaseUrl;
            link.MouseLeftButtonUp += delegate (object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                NoteRender.Open(url);
            };
            return link;
        }

        static FrameworkElement Rule(Theme t)
        {
            return new Border { Height = 1, Background = t.Track, Margin = new Thickness(0, 12, 0, 12), Opacity = 0.7 };
        }

        static TextBlock Text(Theme t, string text, Brush fg, double size, bool bold, Thickness? margin = null)
        {
            var tb = new TextBlock
            {
                Text = text ?? "",
                Foreground = fg,
                FontSize = size,
                FontFamily = t.Font,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
            };
            if (margin.HasValue) tb.Margin = margin.Value;
            if (text != null && text.Length > 40) tb.TextWrapping = TextWrapping.Wrap;
            return tb;
        }
    }
}
