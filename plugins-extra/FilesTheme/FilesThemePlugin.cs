using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using vShell.Core;
using vShell.Ui;

[assembly: vShell.Core.VShellPlugin("filestheme", "Files theme", Description = "Keeps the Files app (files.community) in your vShell colours: its background, address bar, sidebar and file list follow your theme.",
    Glyph = "", Group = "Apps", Author = "version", Adds = "Files in your vShell colours")]

namespace vShell.FilesTheme
{
    /// <summary>The plugin's settings (Settings > Plugins > Files theme).</summary>
    sealed class FtSettings
    {
        readonly SettingsBag _b;
        public FtSettings(BarSettings s) { _b = s.Integration("filestheme"); }
        public static FtSettings Of(BarSettings s) { return new FtSettings(s); }

        public bool Enabled { get { return _b.Get("Enabled", true); } set { _b.Set("Enabled", value); } }
        /// <summary>Files' backdrop behind the colours: Solid, Mica or Acrylic.</summary>
        public string Backdrop { get { return _b.Get("Backdrop", "Solid"); } set { _b.Set("Backdrop", value ?? "Solid"); } }
        /// <summary>What was last written into Files (Look.Signature), so a theme that didn't change isn't written again.</summary>
        public string Applied { get { return _b.Get("Applied", ""); } set { _b.Set("Applied", value ?? ""); } }
    }

    /// <summary>The Files settings worked out from a vShell theme.</summary>
    sealed class Look
    {
        public readonly SortedDictionary<string, object> Values = new SortedDictionary<string, object>(StringComparer.Ordinal);

        public static Look From(BarSettings s)
        {
            FtSettings f = FtSettings.Of(s);
            Theme t = Theme.From(s, 12, 13);
            Color bg = t.BgColor, fg = t.Fg.Color;
            // the second colour: Background 2 with a gradient, otherwise the background a little toward the text colour
            Color bg2 = t.Gradient ? t.Bg2Color : Mix(bg, fg, 0.08);
            double lum = (0.299 * fg.R + 0.587 * fg.G + 0.114 * fg.B) / 255.0;
            var l = new Look();
            l.Values["AppThemeMode"] = lum > 0.5 ? "Dark" : "Light";      // light text = dark mode
            l.Values["AppThemeBackdropMaterial"] = f.Backdrop == "Mica" ? 1 : f.Backdrop == "Acrylic" ? 3 : 0;
            l.Values["AppThemeBackgroundColor"] = Hex(bg);
            l.Values["AppThemeSidebarBackgroundColor"] = "#00000000";     // the sidebar sits on the window's background, like a bar
            l.Values["AppThemeAddressBarBackgroundColor"] = Hex(Opaque(bg2));
            l.Values["AppThemeToolbarBackgroundColor"] = Hex(Opaque(bg2));
            l.Values["AppThemeFileAreaBackgroundColor"] = Hex(Opaque(Mix(bg, bg2, 0.5)));
            l.Values["AppThemeFileAreaSecondaryBackgroundColor"] = Hex(Opaque(Mix(bg, bg2, 0.2)));
            l.Values["AppThemeInfoPaneBackgroundColor"] = Hex(Opaque(Mix(bg, bg2, 0.5)));
            l.Values["AppThemeFontFamily"] = "Segoe UI";
            return l;
        }

        public string Signature
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var kv in Values) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
                return sb.ToString();
            }
        }

        static Color Opaque(Color c) { return Color.FromArgb(255, c.R, c.G, c.B); }
        static Color Mix(Color a, Color b, double k)
        {
            return Color.FromArgb(a.A, (byte)Math.Round(a.R + (b.R - a.R) * k), (byte)Math.Round(a.G + (b.G - a.G) * k), (byte)Math.Round(a.B + (b.B - a.B) * k));
        }
        static string Hex(Color c) { return "#" + c.A.ToString("X2") + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
    }

    /// <summary>The installed Files app: where its settings are, whether it's running, closing and starting it.</summary>
    static class FilesApp
    {
        static string PackagesDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages"); } }

        /// <summary>Files' package family name ("Files_1y0xx7n9077q4"), or null when it isn't installed (or hasn't been run yet).</summary>
        public static string Family()
        {
            try
            {
                if (!Directory.Exists(PackagesDir)) return null;
                foreach (string d in Directory.GetDirectories(PackagesDir, "Files_*"))
                    if (File.Exists(SettingsFile(Path.GetFileName(d)))) return Path.GetFileName(d);
            }
            catch (Exception ex) { Log.Write("Files theme: looking for Files: " + ex.Message); }
            return null;
        }

        public static string SettingsFile(string family) { return Path.Combine(PackagesDir, family, "LocalState", "settings", "user_settings.json"); }

        /// <summary>Files is running (its window, or in the background with its tray icon).</summary>
        public static bool Running()
        {
            Process[] ps = Process.GetProcessesByName("Files");
            foreach (Process p in ps) p.Dispose();
            return ps.Length > 0;
        }

        /// <summary>Put the colours into Files' settings file (everything else in it stays as it is).</summary>
        public static void Write(string family, Look look)
        {
            string path = SettingsFile(family);
            JsonObject root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            foreach (var kv in look.Values)
                root[kv.Key] = kv.Value is int ? System.Text.Json.Nodes.JsonValue.Create((int)kv.Value) : System.Text.Json.Nodes.JsonValue.Create((string)kv.Value);
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }

        /// <summary>Close Files (politely first; it saves its tabs), waiting until it has gone. Runs off the UI thread.</summary>
        public static void Close()
        {
            foreach (Process p in Process.GetProcessesByName("Files"))
                using (p) { try { p.CloseMainWindow(); } catch { } }
            for (int i = 0; i < 30 && Running(); i++) System.Threading.Thread.Sleep(100);
            // still there (it keeps running in the background with its tray icon): end it
            foreach (Process p in Process.GetProcessesByName("Files"))
                using (p) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
        }

        public static void Start(string family)
        {
            Process.Start(new ProcessStartInfo("shell:AppsFolder\\" + family + "!App") { UseShellExecute = true });
        }
    }

    /// <summary>
    /// The Files theme plugin: whenever the vShell colours change, they're written into Files' settings. Files reads them only when it
    /// starts and writes its whole settings file back when it closes, so while it's running a notification offers to restart it (click
    /// it); otherwise they're written as soon as Files isn't running.
    /// </summary>
    public sealed class FilesThemePlugin : IPlugin
    {
        const string RestartLink = "vshell:files-restart";
        BarManager _mgr;
        BarSettings _s;
        DispatcherTimer _settle, _wait;
        bool _notified, _busy;

        public void Load(PluginHost host)
        {
            _mgr = host.Manager;
            _settle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };       // colours being picked: wait until they settle
            _settle.Tick += delegate { _settle.Stop(); Check(); };
            _wait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };          // waiting for Files to close
            _wait.Tick += delegate { Check(); };

            host.AddIntegration("Files theme", Section);
            host.SetUpWhen(delegate (BarSettings s) { return FilesApp.Family() != null; });
            host.AddNotifyKind("FilesTheme.Ready", NotifyLevel.Normal);
            host.AddNotifySource("Files theme");
            host.AddCardLinks(delegate (string url)
            {
                if (url != RestartLink) return false;
                Restart();
                return true;
            });
            host.OnSettings(delegate (BarSettings s)
            {
                _s = s;
                _settle.Stop();
                _settle.Start();
            });
        }

        public void Unload()
        {
            if (_settle != null) _settle.Stop();
            if (_wait != null) _wait.Stop();
        }

        /// <summary>Write the colours if they changed and Files isn't running; while it is, offer a restart and keep checking.</summary>
        void Check()
        {
            if (_s == null || _busy) return;
            FtSettings f = FtSettings.Of(_s);
            string family = f.Enabled ? FilesApp.Family() : null;
            Look look = Look.From(_s);
            if (family == null || look.Signature == f.Applied) { Done(); return; }
            if (!FilesApp.Running()) { Apply(family, look); return; }
            if (!_wait.IsEnabled) _wait.Start();
            if (_notified) return;
            _notified = true;
            Notify.Post("Files theme", "Files has new colours waiting",
                        "Click to restart Files so it uses your vShell theme. Otherwise it changes the next time Files isn't running.",
                        RestartLink, Notify.KindOf("FilesTheme.Ready"));
        }

        void Apply(string family, Look look)
        {
            try
            {
                FilesApp.Write(family, look);
                FtSettings.Of(_s).Applied = look.Signature;
                _mgr.SaveSoon();
                Log.Write("Files theme: colours written into Files (" + family + ")");
            }
            catch (Exception ex) { Log.Write("Files theme: writing Files' settings: " + ex.Message); }
            Done();
        }

        void Done()
        {
            _wait.Stop();
            _notified = false;
        }

        /// <summary>Close Files, write the colours and start it again (the notification's click, or Apply now).</summary>
        void Restart()
        {
            string family = FilesApp.Family();
            if (_s == null || family == null || _busy) return;
            _busy = true;
            Dispatcher ui = Application.Current.Dispatcher;
            Task.Run(delegate
            {
                bool was = FilesApp.Running();
                FilesApp.Close();
                ui.BeginInvoke(new Action(delegate
                {
                    _busy = false;
                    Apply(family, Look.From(_s));
                    if (was)
                        try { FilesApp.Start(family); }
                        catch (Exception ex) { Log.Write("Files theme: starting Files: " + ex.Message); }
                }));
            });
        }

        // =================================================================
        // Settings > Plugins > Files theme
        // =================================================================
        void Section(SettingsWindow.SettingsPage p)
        {
            FtSettings f = FtSettings.Of(p.Settings);
            string family = FilesApp.Family();
            p.Header("Files theme");
            p.Note(family != null
                ? "Files is installed. Its background, address bar, toolbar, sidebar and file list follow your vShell colours, and it uses dark or light mode to match your text colour."
                : "Files (files.community) isn't installed, or hasn't been opened yet. Install it from the Microsoft Store, open it once, then come back here.");
            p.Check("Keep Files in my vShell colours", delegate { return f.Enabled; }, delegate (bool v) { f.Enabled = v; if (v) f.Applied = ""; });
            p.Combo("Backdrop behind Files' colours", new[] { "Solid", "Mica", "Acrylic" }, delegate { return f.Backdrop; }, delegate (string v) { f.Backdrop = v; });
            p.Note("Solid shows your colours as they are. Mica and Acrylic let the wallpaper tint through where your background colour is see-through.");
            p.Note("Files reads its colours when it starts. When you change your theme while Files is open, a notification offers to restart it. " +
                   "Files keeps its own accent colour (the Windows one) and rounded corners - it has no settings for those.");
            if (family != null)
                p.Add(p.Btn("Apply to Files now", delegate { f.Applied = ""; _s = p.Settings; Restart(); }));
        }
    }
}
