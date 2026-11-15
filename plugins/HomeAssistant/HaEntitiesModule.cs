using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using vShell.Core;

namespace vShell.Ui
{
    public sealed class HomeAssistantModule : TilesModule
    {
        public HomeAssistantModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg) { Build(); }

        protected override void BuildTiles()
        {
            foreach (HaEntityRef r in HaView.Of(Cfg).Entities)
            {
                HaEntityRef cap = r;
                Action click = null;
                if (r.Clickable && HaClient.ClickService(r.EntityId, "") != null)
                {
                    click = delegate
                    {
                        HaState s = HaClient.Get(cap.EntityId);
                        var svc = HaClient.ClickService(cap.EntityId, s == null ? "" : s.State);
                        if (svc.HasValue) HaClient.CallService(svc.Value.Key, svc.Value.Value, cap.EntityId);
                    };
                }
                Tile t = AddTile(false, r.GraphStyle != null && r.GraphStyle != "None", click);
                if (t.Meter != null)
                {
                    t.Meter.GraphStyle = r.GraphStyle;
                    t.Meter.GraphW = Math.Max(20, Math.Min(600, r.GraphWidth * Cfg.ShownGraphScale));
                    t.Meter.GraphH = Math.Max(6, Bar.HostHeight * Math.Max(10, Math.Min(100, r.GraphHeightPercent)) / 100.0);
                    if (!string.IsNullOrWhiteSpace(r.GraphColor)) t.Meter.Fill = T.Resolve(r.GraphColor, T.MainAccent);
                    // threshold in the entity's unit, converted to the graph's percent
                    double span = r.GraphMax - r.GraphMin;
                    t.Meter.WarnFill = T.Warn(r.WarnColor);
                    t.Meter.WarnAt = r.WarnOn && Math.Abs(span) > 1e-9 ? Math.Max(0, Math.Min(100, (r.WarnAt - r.GraphMin) / span * 100.0)) : -1;
                    t.Meter.WarnBelow = r.WarnBelow;
                    t.Meter.Blend = r.WarnOn && Math.Abs(span) > 1e-9 ? Theme.WarnFade : 0;
                    t.Meter.ApplyLook(Cfg, T);      // the module's graph look (Settings > Graphs)
                    t.Meter.Data = Mgr.HistoryFor(Cfg.Id + "." + r.EntityId);
                    if (t.Meter.Data.Count == 0) Backfill(t.Meter, cap);      // a new graph (after a start); a rebuild keeps its samples

                    var row = t.Root.Child as StackPanel;
                    if (row != null)
                    {
                        row.Children.Remove(t.Meter);
                        if (r.GraphAlign == "Left")
                        {
                            t.Meter.Margin = new Thickness(0, 0, 8, 0);
                            row.Children.Insert(0, t.Meter);
                        }
                        else if (Bar.IsWidget && Cfg.TileLayout == "List")
                        {
                            // one entity per row in a widget: pin the graph to the right edge of the row
                            var dock = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
                            t.Root.Child = null;
                            t.Meter.Margin = new Thickness(8, 0, 0, 0);
                            DockPanel.SetDock(t.Meter, Dock.Right);
                            dock.Children.Add(t.Meter);
                            dock.Children.Add(row);
                            t.Root.Child = dock;
                        }
                        else row.Children.Add(t.Meter);
                    }
                }
            }
        }

        // ---- a new graph starts with the last couple of minutes from Home Assistant's history, not empty ----
        /// <summary>The graphs take a sample a second and keep the last 120 (History's size).</summary>
        const int GraphSamples = 120;
        /// <summary>Graphs whose history is being read: their live samples wait (they'd come before the old ones), at most 8 seconds.</summary>
        readonly Dictionary<MetricView, DateTime> _filling = new Dictionary<MetricView, DateTime>();

        async void Backfill(MetricView meter, HaEntityRef r)
        {
            if (!HaClient.Configured) return;
            _filling[meter] = DateTime.UtcNow;
            DateTime now = DateTime.UtcNow, from = now.AddSeconds(-GraphSamples);
            List<KeyValuePair<DateTime, string>> states = await HaClient.HistoryAsync(r.EntityId, from);
            if (!_filling.Remove(meter) || meter.Data == null || meter.Data.Count > 0 || states.Count == 0) return;      // (removed = the wait ran out: live samples started)
            // one sample per second up to now: each the state it had then (the last change at or before it)
            int k = 0;
            DateTime end = DateTime.UtcNow;
            int slots = Math.Min(GraphSamples, (int)Math.Round((end - from).TotalSeconds));
            for (int i = 0; i < slots; i++)
            {
                DateTime at = end.AddSeconds(i - slots + 1);
                while (k + 1 < states.Count && states[k + 1].Key <= at) k++;
                if (states[k].Key > at) continue;        // before the first known state
                double pct;
                if (GraphPercent(new HaState { EntityId = r.EntityId, State = states[k].Value }, r, out pct)) meter.Data.Add(pct);
            }
            meter.InvalidateVisual();
        }

        /// <summary>0..100 for the entity's graph: numbers are scaled between GraphMin and GraphMax; on/off count as 100/0.</summary>
        static bool GraphPercent(HaState s, HaEntityRef r, out double pct)
        {
            pct = 0;
            if (s.Unavailable) return false;
            double v;
            if (s.TryNumber(out v))
            {
                double span = r.GraphMax - r.GraphMin;
                if (Math.Abs(span) < 1e-9) span = 1;
                pct = Math.Max(0, Math.Min(100, (v - r.GraphMin) / span * 100.0));
                return true;
            }
            if (s.State == "on" || s.State == "open" || s.State == "unlocked" || s.State == "home") { pct = 100; return true; }
            if (s.State == "off" || s.State == "closed" || s.State == "locked" || s.State == "not_home") { pct = 0; return true; }
            return false;
        }

        /// <summary>Number with its unit; numbers are shown with the entity's decimals, otherwise at most two.</summary>
        public static string Format(HaState s, HaEntityRef r)
        {
            if (s.Unavailable) return "n/a";
            string unit = r.Unit.Length > 0 ? r.Unit : s.Unit;
            double v;
            if (s.TryNumber(out v))
            {
                string num = v.ToString(r.Decimals >= 0 ? "F" + r.Decimals : "0.##", CultureInfo.CurrentCulture);
                string sep = (unit.Length == 0 || unit[0] == '%' || unit[0] == '°') ? "" : " ";
                return num + sep + unit;
            }
            string t = s.State.Replace('_', ' ');
            return t.Length > 0 ? char.ToUpperInvariant(t[0]) + t.Substring(1) : t;
        }

        public override void Update()
        {
            if (HaView.Of(Cfg).Entities.Count == 0)
            {
                SetText(Tiles[0], "Home Assistant: add entities in Settings", T.FgDim, null, false);
                return;
            }
            if (!HaClient.Configured)
            {
                SetText(Tiles[0], "Home Assistant: set the address and token in Settings > Plugins", T.FgDim, null, false);
                return;
            }
            for (int i = 0; i < Tiles.Count && i < HaView.Of(Cfg).Entities.Count; i++)
            {
                HaEntityRef r = HaView.Of(Cfg).Entities[i];
                HaState s = HaClient.Get(r.EntityId);
                string name = r.Label.Length > 0 ? r.Label : (s != null && s.Name.Length > 0 ? s.Name : r.EntityId);
                if (s == null)
                {
                    SetText(Tiles[i], (Cfg.TileNames ? name + " " : "") + "…", T.FgDim, r.EntityId + (HaClient.LastError.Length > 0 ? "\n" + HaClient.LastError : ""), false);
                    continue;
                }
                string val = Format(s, r);
                Brush fg = s.Unavailable ? T.FgDim : (s.State == "on" ? T.MainAccent : T.Fg);
                double num;
                // an automatic threshold comes from the graph range, so it only applies when the entity has a graph
                bool warnable = r.WarnOn && (!r.WarnAuto || (!string.IsNullOrEmpty(r.GraphStyle) && r.GraphStyle != "None"));
                if (!s.Unavailable && warnable && s.TryNumber(out num) && Theme.Crossed(num, true, r.WarnAt, r.WarnBelow)) fg = T.Warn(r.WarnColor);
                string tip = (s.Name.Length > 0 ? s.Name + "\n" : "") + r.EntityId + ": " + s.State + (s.Unit.Length > 0 ? " " + s.Unit : "") +
                             (Tiles[i].Click != null ? "\nClick to toggle" : "");
                // while Home Assistant is down or starting up the value is "n/a" and the name can fall back to the long entity id:
                // don't let that set the width for good - the next real value starts it afresh
                bool settled = !s.Unavailable && (r.Label.Length > 0 || s.Name.Length > 0 || !Cfg.TileNames);
                SetText(Tiles[i], Cfg.TileNames ? name + " " + val : val, fg, tip, settled);
                if (Tiles[i].Meter != null)
                {
                    double pct;
                    if (GraphPercent(s, r, out pct))
                    {
                        Tiles[i].Meter.Value = pct;
                        DateTime since;
                        bool filling = _filling.TryGetValue(Tiles[i].Meter, out since) && (DateTime.UtcNow - since).TotalSeconds < 8;
                        if (!filling) _filling.Remove(Tiles[i].Meter);
                        if (Tiles[i].Meter.Data != null && !filling) Tiles[i].Meter.Data.Add(pct);
                    }
                    Tiles[i].Meter.InvalidateVisual();
                }
            }
        }
    }

    // =====================================================================
    // Service status
    // =====================================================================
}
