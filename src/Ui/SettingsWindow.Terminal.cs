using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>The Terminal module's settings (bars only): the drop-down's keys, place, size and first profile. The terminal itself is
    /// vTerm: its profiles, text, colours and saved connections are in vTerm's Options.</summary>
    public partial class SettingsWindow
    {
        void TerminalProps(StackPanel p, ModuleConfig cfg, ModulePane pane)
        {
            Action again = delegate { Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); })); };
            string exe = VTerm.Exe;
            if (exe == null)
            {
                Note(p, "The terminal is vTerm, a vSuite app of its own - it isn't installed yet. Install it from Settings > Updates > vSuite apps; " +
                        "the first time it starts it brings over the profiles and saved connections vShell's terminal had.");
                var get = Btn("Go to vSuite apps", delegate { ShowUpdates(); });
                get.HorizontalAlignment = HorizontalAlignment.Left;
                Place(p, get);
            }

            List<TermProfile> profiles = VTerm.Profiles();
            var names = profiles.ConvertAll(delegate (TermProfile x) { return x.Name; }).ToArray();
            ComboRow(p, "New tabs start with", names,
                     delegate { string n = cfg.TermProfile; return Array.IndexOf(names, n) >= 0 ? n : (names.Length > 0 ? names[0] : ""); },
                     delegate (string v) { cfg.TermProfile = v; });
            Check(p, "Attach it to the bar (drops down flush from the bar, as wide as the bar)", delegate { return cfg.TermAttach; }, delegate (bool v) { cfg.TermAttach = v; again(); });
            SliderRow(p, !cfg.TermAttach ? "Width (% of the monitor)" : "Width (% of the monitor - at most the bar's straight edge)", 20, 100,
                      delegate { return cfg.TermWidth; }, delegate (double v) { cfg.TermWidth = (int)v; });
            SliderRow(p, "Height (% of the monitor)", 15, 95, delegate { return cfg.TermHeight; }, delegate (double v) { cfg.TermHeight = (int)v; });
            Check(p, "Hide it when you click another window", delegate { return cfg.TermHideOnLeave; }, delegate (bool v) { cfg.TermHideOnLeave = v; });
            ComboRow(p, "Show / hide it from anywhere with", ModuleTypes.TermHotkeys, delegate { return cfg.TermHotkey; }, delegate (string v) { cfg.TermHotkey = v; again(); });
            Check(p, "Hide the button (only the keys open it)", delegate { return cfg.TermHideButton; }, delegate (bool v) { cfg.TermHideButton = v; again(); });
            if (cfg.TermHideButton)
                Note(p, cfg.TermHotkey == "Off"
                    ? "The button still shows: pick keys above first, or there'd be no way to open the terminal."
                    : "The module takes no room on the bar - " + cfg.TermHotkey + " still drops the terminal down from it.");
            else TitleRow(p, cfg, "");
            Note(p, "Click the module (or press the keys) and vTerm's terminal drops down from this bar; again and it hides. Hiding keeps your shells " +
                    "running. Attached to the bar it takes the bar's corners, outline and blur; otherwise your widget style's.");
            Note(p, "The terminal is vTerm, a vSuite app of its own: its profiles, text, colours and saved SSH connections are in vTerm's Options " +
                    "(its icon at the top left of a vTerm window). A tab can move into a vTerm window of its own and back (right-click the tab).");
        }
    }
}
