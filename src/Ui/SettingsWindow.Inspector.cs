using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>The module inspector: the settings of the module selected on a bar or widget (ShowModuleProps).</summary>
    public partial class SettingsWindow
    {
        // =================================================================
        // Inspector: properties of the selected module
        // =================================================================
        ModulePane _propsPane;      // the module settings being shown (a style change re-shows them)

        /// <summary>Module types that open a popup (they get the "Background effect behind its popup" setting).</summary>
        static readonly string[] PopupModules =
        {
            ModuleTypes.StartMenu, ModuleTypes.Launcher, ModuleTypes.Tray, ModuleTypes.Clock, ModuleTypes.Weather, ModuleTypes.NowPlaying,
            ModuleTypes.Notifications,
            ModuleTypes.RunBox, ModuleTypes.Terminal
        };

        void ShowModuleProps(ModulePane pane)
        {
            if (pane == null) return;
            _propsPane = pane;
            var p = pane.Props;
            p.Children.Clear();
            _cardOf.Remove(p);
            var cfg = pane.Selected();
            if (cfg == null)
            {
                Section(p, "Settings");
                Place(p, Txt("Add a module from the gallery below.", "vsDim", 12.5, false));
                return;
            }

            Section(p, "Settings");
            // Build 2: the text size (bars and widgets) and, on a bar, the zone come first (not for modules showing no text)
            if (ShowsText(cfg))
            {
                Place(p, Row("Text size", Segmented(new[] { "S", "M", "L" }, delegate { return string.IsNullOrEmpty(cfg.TextSize) ? "S" : cfg.TextSize; }, delegate (string v) { cfg.TextSize = v; }), false,
                        "S = one line, small (12).\nM = two lines where the module has them (14).\nL = one line, large (18).\n\nEvery font is drawn to take the same room, so changing the font " +
                        "or theme doesn't change a module's width. M needs a bar about 33 tall and L about 25 - on a slimmer bar they show as S. When a bar is too full, graphs get " +
                        "shorter first (down to 20 %) and then text steps down; they come back when there's room. In a widget, \"bar height\" is the module height, " +
                        "and the stepping down happens when the widget has a fixed width / height that's too small."));
                if (cfg.Type == ModuleTypes.StartMenu) Note(p, "Text size (S / M / L) sets the size of the text in the menu.");
            }
            if (!pane.IsWidget)
            {
                BarPlacement owner = null;
                foreach (BarPlacement b in S.Bars) if (b.Modules != null && b.Modules.Contains(cfg)) owner = b;
                if (owner != null && owner.Zones && owner.FullWidth)
                    ComboRow(p, "Zone", ModuleTypes.Zones, delegate { return string.IsNullOrEmpty(cfg.Zone) ? "Left" : cfg.Zone; }, delegate (string v)
                    {
                        cfg.Zone = v;
                        Dispatcher.BeginInvoke(new Action(delegate { pane.Refresh(cfg); }));     // the module list shows it in its new zone
                    });
            }
            switch (cfg.Type)
            {
                case ModuleTypes.Taskbar:
                    TaskbarProps(pane, p, cfg);
                    break;

                case ModuleTypes.Tray:
                    Note(p, "Shows the apps' notification icons - vShell takes them by standing in front of Explorer's tray.");
                    Check(p, "Group icons behind an arrow - drag an icon out of the popup onto the bar to keep it visible, onto the arrow to hide it again",
                          delegate { return cfg.GroupIcons; }, delegate (bool v) { cfg.GroupIcons = v; });
                    ListButton(p, "Un-pin all icons", delegate { cfg.PinnedKeys.Clear(); _mgr.SettingsChanged(); });
                    Check(p, "Network icon", delegate { return cfg.TrayNetwork; }, delegate (bool v) { cfg.TrayNetwork = v; },
                          "Wi-Fi with its signal, wired, or not connected - hover for the network's name, click for the networks Windows can see, right-click " +
                          "for network settings. Windows 11 keeps its own network icon in Explorer's taskbar, so vShell draws this one (not when Windows' " +
                          "own shows here). It's pinned / hidden and moved like the other icons.");
                    ComboRow(p, "Icon colours", ModuleTypes.TrayIconColours, delegate { return cfg.TrayIconColour; }, delegate (string v)
                    {
                        cfg.TrayIconColour = v;
                        Dispatcher.BeginInvoke(new Action(delegate { pane.Refresh(cfg); }));     // the colour / strength rows show / hide
                    }, "Original: each app's own colours. Tint: a theme colour laid over them, so they lean towards your theme but keep some of their own " +
                       "colours (badges stay readable).");
                    if (cfg.TrayIconColour == "Tint")
                    {
                        ComboRow(p, "Colour", ModuleTypes.TrayTintColours, delegate { return cfg.TrayTintColour; }, delegate (string v) { cfg.TrayTintColour = v; });
                        SliderRow(p, "Tint strength (%)", 10, 90, delegate { return cfg.TrayTint; }, delegate (double v) { cfg.TrayTint = (int)v; });
                    }
                    Note(p, "Drag an icon along the bar to move it - a thin line shows where it goes. The order is kept (the hidden icons' popup follows it too).");
                    ListButton(p, "Back to Windows' order", delegate { cfg.TrayOrder.Clear(); _mgr.SettingsChanged(); });
                    break;

                case ModuleTypes.StartMenu:
                    SliderRow(p, "Icon size", 16, 48, delegate { return cfg.IconSize; }, delegate (double v) { cfg.IconSize = (int)v; });
                    Check(p, "Open with the Windows key - a lone press opens and closes the menu; Win+E, Win+R and the rest still work", delegate { return cfg.UseWinKey; }, delegate (bool v) { cfg.UseWinKey = v; });
                    Section(p, "Icon");
                    StartIconPicker(p, pane, cfg);
                    if (cfg.StartIcon == "Custom")
                    {
                        IconPickRow(p, "Custom icon", delegate { return cfg.IconPath; }, delegate (string v) { cfg.IconPath = v; });
                        Note(p, "An image (.ico, .png, .jpg, .bmp, .gif) or a program / DLL to take the icon from. Until one is set, the vShell mark shows.");
                    }
                    else Note(p, "Drawn in your theme's Main Accent, Secondary Accent and text colours, so it follows the theme.");
                    Section(p, "Style");
                    ComboRow(p, "Style", ModuleTypes.StartStyleNames,
                             delegate { return ModuleTypes.StartStyleNames[Math.Max(0, Array.IndexOf(ModuleTypes.StartStyles, cfg.StartStyle))]; },
                             delegate (string v)
                             {
                                 int i = Array.IndexOf(ModuleTypes.StartStyleNames, v);
                                 cfg.StartStyle = ModuleTypes.StartStyles[Math.Max(0, i)];
                                 Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));     // the parts that apply change
                             });
                    Note(p, cfg.StartStyle == "Grid" ? "Your pinned programs six to a row, with All programs behind a button; Recent files (when on) in two columns underneath."
                          : cfg.StartStyle == "Panes" ? "Pinned, Recent files (when on), All programs and your pinned groups on the left - what you pick shows on the right."
                          : cfg.StartStyle == "Cascade" ? "Your pins at the top, then Programs, Recent files and Power - pointing at one opens it beside the menu (Programs › a folder › the program). No search box: type a letter to jump to it."
                          : "One list - Pinned and Recent files (when on), then every program - with a strip on the left for your folders, Settings and Power.");
                    Section(p, "In the menu");
                    bool classic = cfg.StartStyle == "Classic";
                    if (classic)
                    {
                        Note(p, "All off is the plain menu: search and every program A-Z. Turn on the parts you want.");
                        Check(p, "Pinned - your own shortcuts and groups at the top", delegate { return cfg.StartPinned; }, delegate (bool v)
                        {
                            cfg.StartPinned = v;
                            Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));     // Edit pinned… shows / hides
                        });
                    }
                    if (!classic || cfg.StartPinned)
                    {
                        ListButton(p, "Edit pinned…", delegate
                        {
                            var dlg = new LauncherEditorWindow(cfg.Entries, delegate { _mgr.SettingsChanged(); }, "Start menu: pinned shortcuts and groups");
                            dlg.Owner = this;
                            dlg.ShowDialog();
                        });
                        Note(p, "Right-click a program in the menu to pin it (or a pinned one to unpin it). Here you can add groups, reorder and rename - the same editor as the Application launcher's. Groups can hold groups.");
                    }
                    Check(p, "Recent files", delegate { return cfg.StartRecent; }, delegate (bool v)
                    {
                        cfg.StartRecent = v;
                        Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));
                    });
                    if (cfg.StartRecent)
                    {
                        SliderRow(p, "How many", 3, 20, delegate { return cfg.StartRecentCount; }, delegate (double v) { cfg.StartRecentCount = (int)v; });
                        Note(p, "The files you opened lately, newest first - the same list as Explorer's Recent.");
                    }
                    Check(p, "Show folders", delegate { return cfg.StartFolders; }, delegate (bool v) { cfg.StartFolders = v; });
                    Note(p, "The programs in a Start Menu folder sit behind a folder row - click one to open it. Off = every program in one A-Z list.");
                    Section(p, "About the menu");
                    Note(p, "The menu lists the programs in your Start Menu folders and your Store apps (the Store list takes a few seconds the first time). " +
                            "Type to search, use Up / Down and Enter, or click; Backspace goes back out of an open folder or group. The power button has lock, sleep, sign out, restart and shut down. " +
                            "The Windows key cannot be seen while an administrator (elevated) program has focus. With several bars the menu opens on the monitor the mouse is on.");
                    break;

                case ModuleTypes.Launcher:
                    if (pane.IsWidget)
                    {
                        var arrange = new[] { "One row", "Columns", "Rows" };
                        ComboRow(p, "Arrange", arrange, delegate { return cfg.LaunchLayout == "Columns" ? "Columns" : cfg.LaunchLayout == "Rows" ? "Rows" : "One row"; },
                                 delegate (string v)
                                 {
                                     cfg.LaunchLayout = v == "Columns" ? "Columns" : v == "Rows" ? "Rows" : "Row";
                                     Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));
                                 });
                        if (cfg.LaunchLayout != "Row")
                            SliderRow(p, cfg.LaunchLayout == "Rows" ? "Rows" : "Columns", 1, 12, delegate { return cfg.LaunchCount; }, delegate (double v) { cfg.LaunchCount = (int)v; });
                        Note(p, "One row puts the shortcuts side by side in the widget's module height. Columns lays them out in a grid that many across (as many rows as it needs, the widget " +
                                "grows with it); Rows that many down. Every cell is the same size.");
                    }
                    SliderRow(p, "Icon size", 16, 48, delegate { return cfg.IconSize; }, delegate (double v) { cfg.IconSize = (int)v; });
                    Check(p, "Highlight box behind the icon under the mouse", delegate { return cfg.HoverBox; }, delegate (bool v) { cfg.HoverBox = v; });
                    Check(p, "Show names next to shortcut icons", delegate { return cfg.ShowLabels; }, delegate (bool v)
                    {
                        cfg.ShowLabels = v;
                        Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));     // dock animations on / greyed, Text size shows / hides
                    });
                    Check(p, "Show names next to group icons", delegate { return cfg.ShowGroupLabels; }, delegate (bool v)
                    {
                        cfg.ShowGroupLabels = v;
                        Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));
                    });
                    ListButton(p, "Edit shortcuts and groups…", delegate
                    {
                        var dlg = new LauncherEditorWindow(cfg.Entries, delegate { _mgr.SettingsChanged(); });
                        dlg.Owner = this;
                        dlg.ShowDialog();
                    });
                    ListButton(p, "Import pins from the Windows taskbar…", delegate
                    {
                        bool replace;
                        List<WindowsPin> chosen = ImportPinsWindow.Ask(this, delegate (WindowsPin wp) { return cfg.Entries.Exists(delegate (LauncherEntry x) { return wp.Matches(x); }); }, "launcher", out replace);
                        if (chosen == null) return;
                        if (replace) cfg.Entries.RemoveAll(delegate (LauncherEntry x) { return !x.IsGroup; });
                        foreach (WindowsPin wp in chosen)
                            if (!cfg.Entries.Exists(delegate (LauncherEntry x) { return wp.Matches(x); })) cfg.Entries.Add(wp.ToEntry());
                        _mgr.SettingsChanged();
                    });
                    Note(p, "Tip: you can also drop files or shortcuts straight onto the launcher (on a bar or in a widget) - where you drop them is where they go - " +
                            "and drag a shortcut along it to move it. Replace keeps your groups.");
                    DockAnimations(pane, p, cfg, "Turn off the names next to shortcut and group icons (icons only) to use these.");
                    break;

                case ModuleTypes.Cpu:
                case ModuleTypes.Memory:
                case ModuleTypes.Gpu:
                    if (cfg.Type == ModuleTypes.Cpu)
                    {
                        Check(p, "Measure like Task Manager", delegate { return cfg.CpuTaskManager; }, delegate (bool v) { cfg.CpuTaskManager = v; });
                        Note(p, "On = the same figure as Task Manager (\"% Processor Utility\", which counts turbo boost - often higher on modern CPUs). " +
                                "Off = the time the processor is busy, as vShell measured it before.");
                        var smooth = new[] { "Off", "3 seconds", "5 seconds", "10 seconds" };
                        var secs = new[] { 0, 3, 5, 10 };
                        ComboRow(p, "Smooth the load over", smooth, delegate { int i = Array.IndexOf(secs, cfg.CpuSmooth); return smooth[i < 0 ? 0 : i]; },
                                 delegate (string v) { int i = Array.IndexOf(smooth, v); cfg.CpuSmooth = secs[i < 0 ? 0 : i]; });
                        Note(p, "The average of the last few seconds, so the number and graph don't jump about every second.");
                        Check(p, "Show the clock speed (\"CPU 34% · 4.5 GHz\")", delegate { return cfg.CpuShowSpeed; }, delegate (bool v) { cfg.CpuShowSpeed = v; });
                    }
                    if (cfg.Type == ModuleTypes.Memory)
                        Check(p, "Show used / total GB instead of %", delegate { return cfg.ShowDetail; }, delegate (bool v) { cfg.ShowDetail = v; });
                    if (cfg.Type == ModuleTypes.Gpu)
                    {
                        Check(p, "Also show VRAM in use", delegate { return cfg.ShowDetail; }, delegate (bool v) { cfg.ShowDetail = v; });
                        SliderRow(p, "GPU adapter", 1, 4, delegate { return cfg.GpuIndex; }, delegate (double v) { cfg.GpuIndex = (int)v; });
                    }
                    Section(p, "Temperature");
                    Check(p, "Show temperature", delegate { return cfg.ShowTemp; }, delegate (bool v) { cfg.ShowTemp = v; });
                    SensorPicker(p, "Temperature source", delegate { return cfg.TempSensor; }, delegate (string v) { cfg.TempSensor = v; }, true, true);
                    Note(p, cfg.Type == ModuleTypes.Gpu
                        ? "Automatic: NVIDIA GPUs are read from the driver; any other GPU needs HWiNFO (the sensor ticked \"Report value in Gadget\") or LibreHardwareMonitor - pick which below."
                        : "CPU and memory temperatures come from HWiNFO or LibreHardwareMonitor (Windows has no reliable built-in source) - pick which below. HWiNFO: in its Sensors window tick \"Report value in Gadget\" for the reading you want.");
                    // the style first in the Graph section, as in the other graph modules
                    GraphSettings(p, cfg, true, delegate
                    {
                        if (cfg.Type == ModuleTypes.Cpu && cfg.Style == "Cores")
                            Note(p, "Cores: a small bar per thread (logical processor), filling up with its load - one maxed-out core shows even when the " +
                                    "average is low. Meter width sets how wide the whole row of bars is.");
                    });
                    if (!GaugeFace.IsGauge(cfg.Style))
                        Note(p, "Bar and line graphs are as tall as the percentage above and stay centred in the bar; the meter has a fixed thickness.");
                    break;

                case ModuleTypes.Clock:
                    {
                        // friendly names, stored as the key
                        var layoutBox = new ComboBox { MinWidth = 220 };
                        foreach (string n in ModuleTypes.ClockLayoutNames) layoutBox.Items.Add(n);
                        layoutBox.SelectedIndex = Math.Max(0, Array.IndexOf(ModuleTypes.ClockLayouts, cfg.ClockLayout == "TimeOverDate" ? "OneLine" : cfg.ClockLayout));
                        layoutBox.SelectionChanged += delegate
                        {
                            if (_loading || layoutBox.SelectedIndex < 0) return;
                            cfg.ClockLayout = ModuleTypes.ClockLayouts[layoutBox.SelectedIndex];
                            _mgr.SettingsChanged();
                        };
                        Place(p, Row("Layout", layoutBox));
                        Check(p, "24-hour time (off = 12-hour with AM/PM)", delegate { return cfg.Use24Hour; }, delegate (bool v) { cfg.Use24Hour = v; });
                        Check(p, "Show seconds", delegate { return cfg.ShowSeconds; }, delegate (bool v) { cfg.ShowSeconds = v; });
                        ComboRow(p, "Date format", ModuleTypes.DateStyles, delegate { return cfg.DateStyle; }, delegate (string v) { cfg.DateStyle = v; });
                        Note(p, "Short uses your Windows date format, Medium is like \"Sun 11 Oct\", Long like \"Sunday, 11 October 2026\", ISO like \"2026-10-11\".");
                        Section(p, "Colours");
                        Check(p, "Date in the Secondary Accent colour", delegate { return cfg.SecondaryDate; }, delegate (bool v) { cfg.SecondaryDate = v; });
                        Section(p, "Calendar");
                        Check(p, "Click the clock for the month calendar", delegate { return cfg.ClockCalendar; }, delegate (bool v) { cfg.ClockCalendar = v; });
                        ComboRow(p, "Weeks start on", new[] { "Windows", "Monday", "Sunday" }, delegate { return cfg.ClockWeekStart; }, delegate (string v) { cfg.ClockWeekStart = v; });
                        Check(p, "Show week numbers", delegate { return cfg.ClockWeekNumbers; }, delegate (bool v) { cfg.ClockWeekNumbers = v; });
                        Note(p, "Windows = your Windows setting for the first day of the week. Click the date at the top of the calendar to come back to today; scroll over the days to change month.");
                        break;
                    }

                case ModuleTypes.Hwinfo:
                    SensorPicker(p, "Sensor", delegate { return cfg.SensorKey; }, delegate (string v) { cfg.SensorKey = v; }, false, false);
                    TextRow(p, "Title (empty = the sensor's own name)", delegate { return cfg.SensorTitle; }, delegate (string v) { cfg.SensorTitle = v; });
                    TextRow(p, "Unit (empty = the sensor's own unit)", delegate { return cfg.SensorUnit; }, delegate (string v) { cfg.SensorUnit = v; });
                    SliderRow(p, "Decimals", 0, 4, delegate { return cfg.Decimals; }, delegate (double v) { cfg.Decimals = (int)v; });
                    GraphSettings(p, cfg, true, delegate
                    {
                        NumberRow(p, "Graph range: empty at", delegate { return cfg.RangeMin; }, delegate (double v) { cfg.RangeMin = v; cfg.SyncWarn(); if (_refreshWarn != null) _refreshWarn(); });
                        NumberRow(p, "Graph range: full at", delegate { return cfg.RangeMax; }, delegate (double v) { cfg.RangeMax = v; cfg.SyncWarn(); if (_refreshWarn != null) _refreshWarn(); });
                    }, "the sensor's unit, e.g. 80 for 80 °C");
                    break;

                case ModuleTypes.Network:
                    {
                        // the adapters that are up now (and the chosen one, even when it isn't)
                        List<KeyValuePair<string, string>> nics = NetworkSampler.Interfaces();
                        var shown = new List<string> { "All with internet (automatic)" };
                        var names = new List<string> { "" };
                        foreach (var kv in nics) { shown.Add(kv.Value); names.Add(kv.Key); }
                        if (cfg.NetInterface.Length > 0 && !names.Exists(delegate (string n) { return string.Equals(n, cfg.NetInterface, StringComparison.OrdinalIgnoreCase); }))
                        { shown.Add(cfg.NetInterface + " (not connected now)"); names.Add(cfg.NetInterface); }
                        ComboRow(p, "Interface", shown.ToArray(), delegate
                        {
                            int i = names.FindIndex(delegate (string n) { return string.Equals(n, cfg.NetInterface, StringComparison.OrdinalIgnoreCase); });
                            return shown[Math.Max(0, i)];
                        }, delegate (string v) { int i = shown.IndexOf(v); cfg.NetInterface = i > 0 ? names[i] : ""; });
                        Note(p, "Automatic adds up the adapters that have a gateway (your Ethernet / Wi-Fi - not VPNs or virtual switches, so nothing is counted twice). " +
                                "Pick one to watch only that adapter - a second network card, Wi-Fi, a VPN or a virtual switch.");
                    }
                    Check(p, "Show the Wi-Fi name and signal", delegate { return cfg.ShowWifi; }, delegate (bool v) { cfg.ShowWifi = v; });
                    Check(p, "Show speeds in Mbit/s instead of MB/s", delegate { return cfg.UseBits; }, delegate (bool v) { cfg.UseBits = v; });
                    TitleRow(p, cfg, "");
                    Note(p, "The graph follows the download speed. Wi-Fi details come from Windows (netsh) and only appear when you are on Wi-Fi.");
                    GraphSettings(p, cfg, true, delegate
                    {
                        Check(p, "Automatic scale", delegate { return cfg.AutoScale; }, delegate (bool v)
                        {
                            cfg.AutoScale = v;
                            Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));     // the full scale shows / hides
                        }, "The top of the graph follows the fastest speed it shows, rounded up (1, 2, 5, 10, 20 …) - a burst fills it, and it " +
                           "drops back once the burst has scrolled off. The tooltip shows the scale. The warning colour is off while it's on.");
                        if (!cfg.AutoScale)
                            NumberRow(p, "Graph full scale (Mbit/s)", delegate { return cfg.RangeMax; }, delegate (double v) { cfg.RangeMax = Math.Max(1, v); });
                    });
                    break;

                case ModuleTypes.Disk:
                    ComboRow(p, "Show", ModuleTypes.DiskModes, delegate { return cfg.DiskMode; }, delegate (string v) { cfg.DiskMode = v; });
                    TextRow(p, "Drive letter (empty = Windows drive)", delegate { return cfg.DiskDrive; }, delegate (string v) { cfg.DiskDrive = v; });
                    Check(p, "More detail - Space: used / total GB instead of %.  Activity: also read and write speed", delegate { return cfg.ShowDetail; }, delegate (bool v) { cfg.ShowDetail = v; });
                    TitleRow(p, cfg, "Disk");
                    GraphSettings(p, cfg, true, null);
                    break;

                case ModuleTypes.Drives:
                    ComboRow(p, "Style", DrivesModule.Styles, delegate { return cfg.Bag.Get("drives.style", "Bars"); }, delegate (string v)
                    {
                        cfg.Bag.Set("drives.style", v);
                        Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));     // the ring size shows / hides
                    });
                    if (cfg.Bag.Get("drives.style", "Bars") == "Ring")
                        NumberRow(p, "Ring size (% of the row height)", delegate { return cfg.Bag.Get("drives.ringsize", 86.0); },
                                  delegate (double v) { cfg.Bag.Set("drives.ringsize", Math.Max(30, Math.Min(100, v))); });
                    Check(p, "Show network drives", delegate { return cfg.Bag.Get("drives.network", true); }, delegate (bool v) { cfg.Bag.Set("drives.network", v); });
                    Check(p, "Show how much space is free", delegate { return cfg.Bag.Get("drives.text", true); }, delegate (bool v) { cfg.Bag.Set("drives.text", v); });
                    ComboRow(p, "Open drives with", DrivesModule.OpenWith, delegate { return cfg.Bag.Get("drives.open", "Automatic"); }, delegate (string v) { cfg.Bag.Set("drives.open", v); });
                    Note(p, "Automatic: vXplorer when it's installed, otherwise File Explorer. Click a drive to open it; in a widget, click \"This PC\" for all of them. " +
                            "The bar turns to the warning colour when a drive is over 90% full.");
                    TitleRow(p, cfg, "This PC");
                    break;

                case ModuleTypes.Battery:
                    Check(p, "Hide this module on a PC without a battery", delegate { return cfg.HideIfNone; }, delegate (bool v) { cfg.HideIfNone = v; });
                    TitleRow(p, cfg, "BAT");
                    Note(p, "A + after the percentage means it is charging.");
                    GraphSettings(p, cfg, true, null);
                    break;

                case ModuleTypes.Volume:
                    {
                        Action again = delegate { Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); })); };
                        bool mixerHere = pane.IsWidget && cfg.VolumeMixerInWidget;
                        if (pane.IsWidget)
                        {
                            Check(p, "Show the volume mixer in the widget (off = the volume as text / meter)", delegate { return cfg.VolumeMixerInWidget; },
                                  delegate (bool v) { cfg.VolumeMixerInWidget = v; again(); });
                            if (mixerHere)
                            {
                                Note(p, "The widget shows the mixer itself: the output device, the master volume and a slider per app. It reads the sound " +
                                        "levels while the widget can be seen (not while it's covered or the PC is locked).");
                                ComboRow(p, "Layout", new[] { "Rows", "Columns" }, delegate { return cfg.VolumeMixerLayout; }, delegate (string v) { cfg.VolumeMixerLayout = v; });
                                Note(p, "Rows = a slider across per app, one under the other. Columns = upright sliders side by side, like a mixing desk " +
                                        "(an app on another device has that device's name under its own).");
                            }
                        }
                        if (mixerHere)
                        {
                            // the mixer in a widget: tick the devices to show, each with its own volume (none = the default output device)
                            Section(p, "Sound devices");
                            var connected = Audio.Devices();
                            var known = new List<VolumeDeviceItem>();
                            foreach (AudioDevice d in connected) known.Add(new VolumeDeviceItem { Id = d.Id, Name = d.Name });
                            foreach (VolumeDeviceItem d in cfg.VolumeMixerDevices)
                                if (!known.Exists(delegate (VolumeDeviceItem k) { return k.Id == d.Id; })) known.Add(new VolumeDeviceItem { Id = d.Id, Name = d.Name });
                            foreach (VolumeDeviceItem d in known)
                            {
                                VolumeDeviceItem item = d;
                                bool there = connected.Exists(delegate (AudioDevice c) { return c.Id == item.Id; });
                                Check(p, (item.Name.Length > 0 ? item.Name : "A device") + (there ? "" : " (not connected)"),
                                      delegate { return cfg.VolumeMixerDevices.Exists(delegate (VolumeDeviceItem x) { return x.Id == item.Id; }); },
                                      delegate (bool on)
                                      {
                                          cfg.VolumeMixerDevices.RemoveAll(delegate (VolumeDeviceItem x) { return x.Id == item.Id; });
                                          if (on)
                                          {
                                              // kept in the order listed here (Windows' order), so the rows don't shuffle as you tick them
                                              var ordered = new List<VolumeDeviceItem>();
                                              foreach (VolumeDeviceItem k in known)
                                                  if (k.Id == item.Id || cfg.VolumeMixerDevices.Exists(delegate (VolumeDeviceItem x) { return x.Id == k.Id; }))
                                                      ordered.Add(new VolumeDeviceItem { Id = k.Id, Name = k.Name });
                                              cfg.VolumeMixerDevices = ordered;
                                          }
                                          again();
                                      });
                            }
                            Note(p, cfg.VolumeMixerDevices.Count == 0
                                ? "None ticked: the mixer shows the default output device (whichever Windows plays on). Tick devices to show each with its own volume."
                                : "Each ticked device has its own volume (its short name, mute, level). Turn off \"Show apps\" below for just the devices.");
                        }
                        // which device: the default output (follows Windows' choice), or one picked - a Volume module per device on the bar
                        else
                        {
                            const string DefaultItem = "Default output device";
                            var names = new List<string> { DefaultItem };
                            var ids = new List<string> { "" };
                            foreach (AudioDevice d in Audio.Devices()) { names.Add(d.Name); ids.Add(d.Id); }
                            if (cfg.VolumeDeviceId.Length > 0 && !ids.Contains(cfg.VolumeDeviceId))
                            {
                                names.Add((cfg.VolumeDeviceName.Length > 0 ? cfg.VolumeDeviceName : "A device") + " (not connected)");
                                ids.Add(cfg.VolumeDeviceId);
                            }
                            ComboRow(p, "Sound device", names.ToArray(), delegate { int i = ids.IndexOf(cfg.VolumeDeviceId); return names[i < 0 ? 0 : i]; },
                                     delegate (string v)
                                     {
                                         int i = names.IndexOf(v);
                                         cfg.VolumeDeviceId = i <= 0 ? "" : ids[i];
                                         cfg.VolumeDeviceName = i <= 0 ? "" : v.EndsWith(" (not connected)") ? cfg.VolumeDeviceName : v;
                                     });
                            Note(p, "Default = whichever device Windows plays on (it follows when you switch). Pick a device to always show and change that one - " +
                                    "add more Volume modules to show several devices side by side (Wave Link's Game, Browser, Media …).");
                        }
                        if (!mixerHere)
                        {
                            Section(p, "On the " + (pane.IsWidget ? "widget" : "bar"));
                            Check(p, "Show a speaker icon - changes with the volume, crossed out when muted", delegate { return cfg.ShowIcon; }, delegate (bool v) { cfg.ShowIcon = v; });
                            ComboRow(p, "Style", new[] { "Text", "Meter" }, delegate { return cfg.Style == "Text" ? "Text" : "Meter"; }, delegate (string v) { cfg.Style = v; again(); });
                            if (cfg.Style != "Text")
                            {
                                Note(p, "Click or drag along the meter to set the volume there (clicking elsewhere on the module does what \"Clicking it\" says).");
                                Check(p, "Show text next to the meter", delegate { return cfg.ShowText; }, delegate (bool v) { cfg.ShowText = v; });
                                SliderRow(p, "Meter width", 20, 300, delegate { return cfg.GraphWidth; }, delegate (double v) { cfg.GraphWidth = (int)v; });
                                ColorRow(p, "Meter colour", delegate { return cfg.Color; }, delegate (string v) { cfg.Color = v; }, true, false);
                            }
                            ComboRow(p, "Show the sound device's name", ModuleTypes.VolumeDevices, delegate { return cfg.VolumeDevice; }, delegate (string v) { cfg.VolumeDevice = v; });
                            Note(p, "Short = its first word (\"Speakers\", Wave Link's \"System\"); Full = the whole name. Off = only in the tooltip.");
                            Check(p, "Show the sound level - a line under it that moves with what's playing", delegate { return cfg.VolumeLevel; }, delegate (bool v) { cfg.VolumeLevel = v; });
                            ComboRow(p, "When muted, show", ModuleTypes.VolumeMutedStyles, delegate { return cfg.VolumeMuted; }, delegate (string v) { cfg.VolumeMuted = v; });
                            Note(p, "Text = \"muted\"; Icon = only the crossed-out speaker (needs the speaker icon); Dim = the module faded.");
                            TitleRow(p, cfg, "");

                            Section(p, "Mouse");
                            ComboRow(p, "Clicking it", ModuleTypes.VolumeClicks, delegate { return cfg.VolumeClick; }, delegate (string v) { cfg.VolumeClick = v; again(); });
                            Note(p, "Mixer = the volume mixer (each app, the output device, mute); Mute = mute / unmute; Settings = Windows' sound settings.");
                            Check(p, "Middle-click mutes / unmutes", delegate { return cfg.VolumeMiddleMute; }, delegate (bool v) { cfg.VolumeMiddleMute = v; });
                            SliderRow(p, "Percent per scroll step", 1, 20, delegate { return cfg.VolumeStep; }, delegate (double v) { cfg.VolumeStep = (int)v; });
                        }
                        if (mixerHere || cfg.VolumeClick == "Mixer")
                        {
                            Section(p, "Volume mixer");
                            Check(p, "Show apps", delegate { return cfg.VolumeShowApps; }, delegate (bool v) { cfg.VolumeShowApps = v; again(); });
                            if (cfg.VolumeShowApps)
                            {
                                Check(p, "Only apps playing right now", delegate { return cfg.VolumePlayingOnly; }, delegate (bool v) { cfg.VolumePlayingOnly = v; });
                                Note(p, "Off = every app on the mixer's device(s) (like Windows' mixer), plus apps playing on your other devices. On = only the " +
                                        "ones playing - once one plays it stays listed until the mixer closes.");
                            }
                            else Note(p, "Only the device volumes - the apps aren't listed (or read).");
                            Check(p, "Show the sound levels (a line beside each slider)", delegate { return cfg.VolumeMeters; }, delegate (bool v) { cfg.VolumeMeters = v; });
                            if (!(mixerHere && cfg.VolumeMixerDevices.Count > 0))
                                Check(p, "Show the output device at the top (click it to pick another)", delegate { return cfg.VolumeDevicePicker; }, delegate (bool v) { cfg.VolumeDevicePicker = v; });
                            Check(p, "Show the Sound settings / Windows volume mixer links", delegate { return cfg.VolumeLinks; }, delegate (bool v) { cfg.VolumeLinks = v; });
                        }
                        break;
                    }

                case ModuleTypes.Uptime:
                    TitleRow(p, cfg, "Up");
                    ComboRow(p, "Counts from", ModuleTypes.UpFroms, delegate { return cfg.UpFrom; }, delegate (string v) { cfg.UpFrom = v; });
                    Note(p, "Last restart: how long Windows has been running (sleep included). Awake time: the same without the time asleep. " +
                            "Last wake-up: since it last woke from sleep. Sign-in: since you signed in. With Fast Startup on, shutting down " +
                            "doesn't reset the uptime (only Restart does) - Sign-in does start again each time.");
                    ComboRow(p, "Format", ModuleTypes.UpFormats, delegate { return cfg.UpFormat; }, delegate (string v) { cfg.UpFormat = v; });
                    Note(p, "Short: 3d 4h. Long: 3 days, 4 hours. Decimal: 3.2d.");
                    Check(p, "Show minutes too (once it's a day or more)", delegate { return cfg.UpMinutes; }, delegate (bool v) { cfg.UpMinutes = v; });
                    Check(p, "Two lines", delegate { return cfg.UpTwoLines; }, delegate (bool v) { cfg.UpTwoLines = v; },
                          "On a bar: the time with \"since …\" under it, sized to fit the bar. In a widget: a big number with \"since …\" under it.");
                    Check(p, "Dot when Windows needs a restart", delegate { return cfg.UpPendingDot; }, delegate (bool v) { cfg.UpPendingDot = v; },
                          "When Windows Update (or an update being installed) is waiting for a restart. Click the module for the details and a Restart button.");
                    Note(p, "Hover for the start time; click for the details (last restart, sleep, sign-in, Fast Startup) with Sleep and Restart buttons.");
                    break;

                case ModuleTypes.Processes:
                    ComboRow(p, "Sort by", ModuleTypes.ProcSorts, delegate { return cfg.ProcSort; }, delegate (string v)
                    {
                        bool redo = (v == "Gpu") != (cfg.ProcSort == "Gpu");
                        cfg.ProcSort = v;
                        if (redo) Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));     // the GPU note shows / hides
                    });
                    if (cfg.ProcSort == "Gpu")
                        Note(p, "Shows each program's busiest GPU engine, like Task Manager's GPU column.");
                    SliderRow(p, "Programs shown", 1, 5, delegate { return cfg.ProcShow; }, delegate (double v) { cfg.ProcShow = (int)v; });
                    ComboRow(p, "Layout (when more than one)", ModuleTypes.TileLayouts, delegate { return cfg.TileLayout; }, delegate (string v) { cfg.TileLayout = v; });
                    SliderRow(p, "List line spacing", 0, 30, delegate { return cfg.ProcSpacing; }, delegate (double v) { cfg.ProcSpacing = (int)v; });
                    SliderRow(p, "Programs listed in the tooltip", 3, 20, delegate { return cfg.ProcCount; }, delegate (double v) { cfg.ProcCount = (int)v; });
                    Check(p, "Fixed width - stops the modules next to it moving as the program names change", delegate { return cfg.ProcWidth > 0; }, delegate (bool v)
                    {
                        cfg.ProcWidth = v ? 160 : 0;
                        Dispatcher.BeginInvoke(new Action(delegate { ShowModuleProps(pane); }));     // the width slider
                    });
                    if (cfg.ProcWidth > 0)
                        SliderRow(p, "Width", 40, 600, delegate { return cfg.ProcWidth; }, delegate (double v) { cfg.ProcWidth = (int)v; });
                    TitleRow(p, cfg, "Top");
                    Note(p, "Shows the busiest program(s); all processes of one program are added together. \"Row\" puts them side by side; \"List\" puts each on its own line - " +
                            "then text size and line spacing let you fit them (a bar needs a small size). Hover for the longer list, click to open Task Manager.");
                    break;

                case ModuleTypes.Label:
                    TextRow(p, "Text", delegate { return cfg.LabelText; }, delegate (string v) { cfg.LabelText = v; });
                    Check(p, "Bold", delegate { return cfg.LabelBold; }, delegate (bool v) { cfg.LabelBold = v; });
                    ColorRow(p, "Text colour", delegate { return cfg.Color; }, delegate (string v) { cfg.Color = v; }, true, false);
                    Note(p, "A plain piece of text: a heading in a widget, a name for the modules next to it, or a small note on a bar.");
                    break;

                case ModuleTypes.Notes:
                    NotesProps(p, cfg);
                    break;

                case ModuleTypes.RunBox:
                    RunBoxProps(p, cfg, pane);
                    break;

                case ModuleTypes.Terminal:
                    TerminalProps(p, cfg, pane);
                    break;

                case ModuleTypes.Services:
                    ComboRow(p, "Layout", ModuleTypes.TileLayouts, delegate { return cfg.TileLayout; }, delegate (string v) { cfg.TileLayout = v; });
                    Check(p, "Show names next to the dots", delegate { return cfg.TileNames; }, delegate (bool v) { cfg.TileNames = v; });
                    SliderRow(p, "Check every (seconds)", 5, 300, delegate { return cfg.CheckSeconds; }, delegate (double v) { cfg.CheckSeconds = (int)v; });
                    ListButton(p, "Choose what to watch… (" + cfg.Checks.Count + ")", delegate { EditServices(cfg); });
                    Note(p, "Green = up, yellow = slow (over 1.5 s), red = down, grey = not checked yet. A web address counts as up unless the server answers with an error (5xx). " +
                            "Hover a dot for the details; click a web address to open it.");
                    break;

                case ModuleTypes.Weather:
                    WeatherProps(pane, p, cfg);
                    break;

                case ModuleTypes.Notifications:
                    {
                        ComboRow(p, "Show", ModuleTypes.NotifySources, delegate { return cfg.NfSource; }, delegate (string v) { cfg.NfSource = v; });
                        SliderRow(p, "How many", 1, 50, delegate { return cfg.NfCount; }, delegate (double v) { cfg.NfCount = (int)v; });
                        if (!pane.IsWidget)
                        {
                            Check(p, "Badge with the number not read yet", delegate { return cfg.NfBadge; }, delegate (bool v) { cfg.NfBadge = v; });
                            Check(p, "Hide the bell when the history is empty", delegate { return cfg.NfHideEmpty; }, delegate (bool v) { cfg.NfHideEmpty = v; });
                            Check(p, "The badge glows while an Alert is unread (and bounces when one arrives)", delegate { return cfg.NfPulse; }, delegate (bool v) { cfg.NfPulse = v; });
                            Check(p, "Closing the list marks them read (off = use ✓ on each, or Mark all read)", delegate { return cfg.NfReadOnClose; }, delegate (bool v) { cfg.NfReadOnClose = v; });
                            TitleRow(p, cfg, "");
                        }
                        Note(p, (pane.IsWidget ? "" : "On a bar: a bell with the number of new notifications; click it for the history (opening it marks them read). ") +
                                "Click a notification to open what it is about, right-click to remove it. How they look, quiet times and what sends them: Settings > Notifications.");
                        break;
                    }

                case ModuleTypes.NowPlaying:
                    Check(p, "Show the album art", delegate { return cfg.MediaShowArt; }, delegate (bool v) { cfg.MediaShowArt = v; });
                    Check(p, "Show previous / play-pause / next buttons", delegate { return cfg.MediaShowControls; }, delegate (bool v) { cfg.MediaShowControls = v; });
                    SliderRow(p, "Text width", 60, 500, delegate { return cfg.MediaWidth; }, delegate (double v) { cfg.MediaWidth = (int)v; });
                    Check(p, "Scroll long titles back and forth - off = cut off with \u2026", delegate { return cfg.MediaScroll; }, delegate (bool v) { cfg.MediaScroll = v; });
                    Check(p, "Hide when nothing is playing", delegate { return cfg.HideIfNone; }, delegate (bool v) { cfg.HideIfNone = v; });
                    ComboRow(p, "Progress bar", ModuleTypes.MediaProgressModes, delegate { return cfg.MediaProgress; }, delegate (string v) { cfg.MediaProgress = v; });
                    Check(p, "Show the time (1:23 / 4:56)", delegate { return cfg.MediaShowTimes; }, delegate (bool v) { cfg.MediaShowTimes = v; });
                    Note(p, "Shows what Windows' media controls show (Spotify, browsers, Plex, Windows Media Player, ...). Click the art or title for a bigger card. " +
                            "Click or drag on the progress bar to jump, when the player allows it. The progress bar needs the player to tell Windows where it is - " +
                            "for the Jellyfin apps set up Jellyfin under Plugins. If Windows' media sessions are not available the buttons send the keyboard media keys.");
                    break;

                case ModuleTypes.Spacer:
                    SliderRow(p, "Width (0 = flexible)", 0, 400, delegate { return cfg.SpacerWidth; }, delegate (double v) { cfg.SpacerWidth = (int)v; });
                    Note(p, "A flexible spacer pushes the modules after it to the right edge of a full-width bar.");
                    break;

                default:
                    {
                        PluginModule pm = PluginManager.ModuleOf(cfg.Type);
                        if (pm != null && pm.Settings != null) pm.Settings(new SettingsPage(this, p, pane), cfg);
                        else if (pm == null && PluginStore.OwnerOf(cfg.Type) != null)
                            Note(p, "This module comes from the " + PluginStore.OwnerOf(cfg.Type).Name + " plugin, which isn't added. Add it on the Plugins page " +
                                    "(Add or remove plugins, at the top) and the module is back as it was.");
                        break;
                    }
            }

            // Build 2: no font size per module - its Text size (S / M / L) is at the top

            // modules with a popup (menu, list, calendar, drop-down): the bar's / widget's blur / frosted behind it, or not
            PluginModule popupOf = PluginManager.ModuleOf(cfg.Type);
            if (Array.IndexOf(PopupModules, cfg.Type) >= 0 || (popupOf != null && popupOf.HasPopup))
            {
                Section(p, "Popup");
                Check(p, cfg.Type == ModuleTypes.RunBox ? "Background effect behind the drop-down and the quick run box" :
                         cfg.Type == ModuleTypes.Terminal ? "Background effect behind the terminal" :
                         cfg.Type == ModuleTypes.StartMenu ? "Background effect behind the start menu" : "Background effect behind its popup",
                      delegate { return cfg.PopupBackdrop; }, delegate (bool v) { cfg.PopupBackdrop = v; });
                Note(p, "When the " + (pane.IsWidget ? "widget" : "bar") + " has a background effect (Blur or Frosted - its own, or the one in Appearance), " +
                        "the popup gets it too, with the theme's background opacity over it. Turn this off for the plain, almost solid popup background.");
            }

            if (pane.IsWidget && _selWidget != null && _selWidget.ShowTabs && _selWidget.Modules.Contains(cfg))
            {
                Section(p, "Tab");
                TextRow(p, "Tab name (empty = automatic)", delegate { return cfg.TabName; }, delegate (string v) { cfg.TabName = v.Trim(); });
            }
            if (cfg.Type != ModuleTypes.Spacer && !pane.IsWidget)
            {
                Section(p, "Separator");
                Check(p, "Show separator", delegate { return cfg.ShowSeparator; }, delegate (bool v) { cfg.ShowSeparator = v; });
                Note(p, "Change separator style in bar look settings.");
            }
        }

    }
}
