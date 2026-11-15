using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Terminal (bars only): a button that drops vTerm's terminal down from the bar - vTerm, a vSuite app of its own, owns the window and
    /// the shells (VTerm). Its keys (Ctrl+Alt+T by default) show / hide it from anywhere.
    /// </summary>
    public sealed class TerminalModule : Border
    {
        readonly BarManager _mgr;
        readonly IModuleHost _host;
        readonly ModuleConfig _cfg;
        readonly Theme _t;

        public ModuleConfig Config { get { return _cfg; } }
        public IModuleHost Host { get { return _host; } }

        public TerminalModule(BarManager mgr, IModuleHost host, ModuleConfig cfg)
        {
            _mgr = mgr; _host = host; _cfg = cfg;
            _t = host.Theme.ForModule(cfg);
            Background = Brushes.Transparent;
            VerticalAlignment = VerticalAlignment.Center;

            // a button - or nothing at all when only the keys open it (the drop-down still comes from this bar)
            if (cfg.TermHideButton && cfg.TermHotkey != "Off")
            {
                Visibility = Visibility.Collapsed;
                return;
            }
            Padding = new Thickness(6, 0, 6, 0);
            Cursor = Cursors.Hand;
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock
            {
                Text = "", FontFamily = IconUtil.GlyphFont, FontSize = _t.FontSize * 1.1, Foreground = _t.Fg, VerticalAlignment = VerticalAlignment.Center
            });
            string title = cfg.Title ?? "";
            if (title.Length > 0)
                row.Children.Add(new TextBlock { Text = " " + title, Foreground = _t.FgDim, FontFamily = _t.Font, FontSize = _t.FontSize, VerticalAlignment = VerticalAlignment.Center });
            Child = row;
            ToolTip = "Terminal (vTerm)" + (cfg.TermHotkey != "Off" ? " - click or " + cfg.TermHotkey : " - click to show / hide");
            CornerRadius = new CornerRadius(6);
            MouseEnter += delegate { Background = _t.Hover; };
            MouseLeave += delegate { Background = Brushes.Transparent; };
            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; Toggle(); };
        }

        /// <summary>Show vTerm's drop-down from this bar, or hide it.</summary>
        public void Toggle()
        {
            try { VTerm.Send(_mgr, VTerm.DropFor(_mgr, _host as BarWindow, _cfg, "toggle")); }
            catch (Exception ex) { Log.Write("Terminal: " + ex); }
        }
    }
}
