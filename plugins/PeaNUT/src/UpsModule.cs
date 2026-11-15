using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using vShell.Core;
using vShell.Ui;

namespace VShellPeanut
{
    /// <summary>
    /// The "UPS (PeaNUT)" module: one tile per UPS - a status dot, the name and a reading, and optionally a battery meter.
    /// Click a tile for the popup with everything PeaNUT knows about each UPS.
    /// </summary>
    public sealed class UpsModule : TilesModule
    {
        public const string TypeName = "PeaNUT";
        public static readonly string[] ShowChoices = { "Charge", "Charge and runtime", "Runtime", "Load", "Power", "Status" };

        // Module settings, kept in cfg.Bag
        public const string KeyUps = "Ups";          // "" = every UPS, else UpsReading.Key
        public const string KeyShow = "Show";
        public const string KeyMeter = "Meter";

        readonly List<UpsReading> _shown = new List<UpsReading>();
        string _builtFor = null;
        int _seen = -1;
        string _front = null;
        PopupWindow _popup;
        int _popupVersion = -1;

        public UpsModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg)
        {
            Build();
        }

        string Pick => Cfg.Bag.Get(KeyUps, "");
        string Show => Cfg.Bag.Get(KeyShow, "Charge");
        bool WithMeter => Cfg.Bag.Get(KeyMeter, false);

        List<UpsReading> Wanted()
        {
            var all = Store.AllUps();
            string pick = Pick;
            if (pick.Length == 0) return all;
            return all.Where(u => u.Key == pick).ToList();
        }

        protected override void BuildTiles()
        {
            _shown.Clear();
            _shown.AddRange(Wanted());
            _builtFor = Signature(_shown);
            _seen = -1;   // new tiles: fill them in on this Update, whatever the store's version
            foreach (var u in _shown)
                AddTile(true, WithMeter, TogglePopup);
        }

        static string Signature(List<UpsReading> list)
        {
            return string.Join("\n", list.Select(u => u.Key));
        }

        public override void Update()
        {
            if (!ModuleVisibility.Covered(Bar)) Store.Want();

            var now = Wanted();
            if (Signature(now) != _builtFor)
            {
                Rebuild();
                return;
            }
            int version = Store.Version;
            string front = Cfg.Title ?? "";
            // Nothing new from the store (its version goes up with every answer and settings change): nothing to redraw - also with no
            // UPS to show, where the message only depends on the store too.
            if (version == _seen && front == _front && Tiles.Count > 0) { RefreshPopup(); return; }
            _seen = version;
            _front = front;
            _shown.Clear();
            _shown.AddRange(now);

            if (_shown.Count == 0)
            {
                SetText(Tiles[0], Front(EmptyText()), T.FgDim, EmptyTip(), false);
                if (Tiles[0].Dot != null) Tiles[0].Dot.Fill = T.FgDim;
                return;
            }

            string show = Show;
            for (int i = 0; i < Tiles.Count && i < _shown.Count; i++)
            {
                var u = _shown[i];
                var tile = Tiles[i];
                if (tile.Dot != null) tile.Dot.Fill = Colour(u, T);
                string value = Fmt.Value(u, show);
                string text = Cfg.TileNames ? u.Name + " " + value : value;
                if (i == 0) text = Front(text);   // the module's "text in front" goes before the first tile only
                SetText(tile, text, T.Fg, Tip(u), true);
                if (tile.Meter != null)
                {
                    double c = u.Charge;
                    tile.Meter.Value = double.IsNaN(c) ? 0 : Math.Max(0, Math.Min(100, c));
                    tile.Meter.Fill = FillBrush();
                    tile.Meter.WarnAt = 25;
                    tile.Meter.WarnBelow = true;
                    tile.Meter.WarnFill = StatusColors.Bad;
                    tile.Meter.InvalidateVisual();
                }
            }
            RefreshPopup();
        }

        string Front(string text)
        {
            string title = (Cfg.Title ?? "").Trim();
            return title.Length == 0 ? text : title + " " + text;
        }

        string EmptyText()
        {
            var servers = Store.Servers;
            if (servers.Length == 0) return "UPS: add a PeaNUT server in Settings > Plugins";
            var snap = Store.Snapshot();
            if (snap.Count == 0) return "UPS: every PeaNUT server is turned off";
            if (Pick.Length > 0 && snap.Any(r => r.Ok)) return "UPS: the one picked isn't there - pick another in the module's settings";
            if (snap.All(r => r.Checked == DateTime.MinValue)) return "UPS…";
            var bad = snap.FirstOrDefault(r => !r.Ok && r.Checked != DateTime.MinValue);
            if (bad != null) return "UPS: can't reach " + bad.Server.Shown;
            return "UPS: PeaNUT has no UPS";
        }

        string EmptyTip()
        {
            var lines = new List<string>();
            foreach (var r in Store.Snapshot())
            {
                if (r.Checked == DateTime.MinValue) lines.Add(r.Server.Shown + ": checking…");
                else if (!r.Ok) lines.Add(r.Server.Shown + ": " + r.Error);
                else lines.Add(r.Server.Shown + ": no UPS (" + r.Server.BaseUrl + ")");
            }
            return lines.Count == 0 ? null : string.Join("\n", lines);
        }

        public static Brush Colour(UpsReading u, Theme t)
        {
            switch (u.Severity)
            {
                case 0: return StatusColors.Good;
                case 1: return StatusColors.Warn;
                case 2: return StatusColors.Bad;
                default: return t.FgDim;
            }
        }

        static string Tip(UpsReading u)
        {
            var lines = new List<string> { u.Name + " - " + u.StatusText };
            if (!double.IsNaN(u.Charge)) lines.Add("Battery " + Fmt.Pct(u.Charge));
            if (!double.IsNaN(u.RuntimeSeconds)) lines.Add("Runtime " + Fmt.Runtime(u.RuntimeSeconds));
            if (!double.IsNaN(u.Load)) lines.Add("Load " + Fmt.Pct(u.Load));
            if (!double.IsNaN(u.Watts)) lines.Add("Power " + Fmt.Watts(u.Watts));
            if (!double.IsNaN(u.Num("input.voltage"))) lines.Add("Input " + Fmt.Volts(u.Num("input.voltage")));
            if (u.Stale) lines.Add("Last reading " + TimeText.Ago(u.At));
            lines.Add("Click for more");
            return string.Join("\n", lines);
        }

        void TogglePopup()
        {
            if (_popup != null && _popup.IsOpen)
            {
                _popup.CloseSafe();
                _popup = null;
                return;
            }
            Store.PollNow();
            _popupVersion = Store.Version;
            _popup = new PopupWindow(Mgr, Bar, this, T, UpsPopup.Build(T, Pick));
            Mgr.ShowPopup(_popup);
        }

        void RefreshPopup()
        {
            if (_popup == null || !_popup.IsOpen) return;
            int v = Store.Version;
            if (v == _popupVersion) return;
            _popupVersion = v;
            _popup.SetContent(UpsPopup.Build(T, Pick));
        }
    }
}
