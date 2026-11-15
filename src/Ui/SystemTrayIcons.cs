using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Windows 11's own tray icons - volume, network, power / battery, the "microphone in use" indicator and Windows Security - belong to Explorer's new
    /// taskbar, which ignores clicks forwarded from another tray. vShell answers them itself: a left click opens the volume mixer or the
    /// matching Windows page, a right click a menu of the related settings.
    /// </summary>
    public static class SystemTrayIcons
    {
        enum Kind { None, Volume, Network, Power, Microphone, Security }

        static Kind KindOf(TrayIcon ic)
        {
            if (ic == null) return Kind.None;
            // Windows Security (SecurityHealthSystray) ignores clicks from another tray too
            if ((ic.Key ?? "").IndexOf("\\securityhealthsystray.exe#", StringComparison.OrdinalIgnoreCase) >= 0) return Kind.Security;
            if (ic.Guid == TrayHost.VolumeGuid) return Kind.Volume;
            if (ic.Guid == TrayHost.NetworkGuid) return Kind.Network;
            if (ic.Guid == TrayHost.PowerGuid) return Kind.Power;
            bool explorer = (ic.Key ?? "").IndexOf("\\explorer.exe#", StringComparison.OrdinalIgnoreCase) >= 0;
            string tip = (ic.Tip ?? "").ToLowerInvariant();
            if (explorer && (tip.Contains("microphone") || tip.Contains("mikrofon") || tip.Contains("micro"))) return Kind.Microphone;
            if (explorer && (tip.Contains("speakers") || tip.Contains("headphones") || tip.Contains("volume"))) return Kind.Volume;
            return Kind.None;
        }

        /// <summary>Only on Windows 11, where these icons don't answer clicks from another tray - and always vShell's own network icon.</summary>
        public static bool Handles(TrayIcon ic)
        {
            if (ic != null && ic.Key == TrayHost.OwnNetworkKey) return true;
            return Environment.OSVersion.Version.Build >= 22000 && KindOf(ic) != Kind.None;
        }

        /// <summary>A click on one of Windows' own icons. Returns false when it is not one of them (forward it to its app as usual).</summary>
        public static bool Click(TrayIcon ic, bool right, FrameworkElement at)
        {
            if (!Handles(ic)) return false;
            Kind k = KindOf(ic);
            try
            {
                if (right) { Menu(k, at); return true; }
                if (ic.Key == TrayHost.OwnNetworkKey) return true;      // vShell's own network icon: only its right-click menu
                switch (k)
                {
                    case Kind.Volume: VolumeMixer.Toggle(at, null); break;
                    case Kind.Network: Open("ms-availablenetworks:"); break;
                    case Kind.Power: Open("ms-settings:batterysaver"); break;
                    case Kind.Microphone: Open("ms-settings:privacy-microphone"); break;
                    case Kind.Security: Open("windowsdefender:"); break;
                }
            }
            catch (Exception ex) { Log.Write("System tray icon: " + ex.Message); }
            return true;
        }

        static void Menu(Kind k, FrameworkElement at)
        {
            var m = new ContextMenu();
            switch (k)
            {
                case Kind.Volume:
                    var mixer = new MenuItem { Header = "Volume mixer", FontWeight = FontWeights.SemiBold };
                    mixer.Click += delegate { OpenMixer(at); };
                    m.Items.Add(mixer);
                    m.Items.Add(new Separator());
                    Add(m, "Windows volume mixer", "ms-settings:apps-volume");
                    Add(m, "Sound settings", "ms-settings:sound");
                    Add(m, "Sound devices (classic)", "mmsys.cpl");
                    break;
                case Kind.Network:
                    Add(m, "Available networks", "ms-availablenetworks:");
                    Add(m, "Network and internet settings", "ms-settings:network");
                    Add(m, "Network connections", "ncpa.cpl");
                    break;
                case Kind.Power:
                    Add(m, "Power and battery settings", "ms-settings:batterysaver");
                    Add(m, "Power options (classic)", "powercfg.cpl");
                    break;
                case Kind.Microphone:
                    Add(m, "Which apps use the microphone", "ms-settings:privacy-microphone");
                    Add(m, "Sound settings (input)", "ms-settings:sound");
                    break;
                case Kind.Security:
                    var dash = new MenuItem { Header = "Open Windows Security", FontWeight = FontWeights.SemiBold };
                    dash.Click += delegate { Open("windowsdefender:"); };
                    m.Items.Add(dash);
                    m.Items.Add(new Separator());
                    Add(m, "Virus & threat protection", "windowsdefender://threat");
                    Add(m, "Firewall & network protection", "windowsdefender://network");
                    Add(m, "App & browser control", "windowsdefender://appbrowser");
                    Add(m, "Device security", "windowsdefender://devicesecurity");
                    Add(m, "Protection history", "windowsdefender://history");
                    break;
            }
            m.PlacementTarget = at;
            m.Placement = PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        /// <summary>The volume mixer, opened once the menu it was picked from has closed (else the menu closing could close it again).</summary>
        static void OpenMixer(FrameworkElement at)
        {
            at.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(delegate { VolumeMixer.Toggle(at, null); }));
        }

        static void Add(ContextMenu m, string text, string target)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += delegate { Open(target); };
            m.Items.Add(mi);
        }

        static void Open(string target)
        {
            try
            {
                if (target.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase)) Process.Start(new ProcessStartInfo("control.exe", target) { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Write("Open " + target + ": " + ex.Message); }
        }
    }
}
