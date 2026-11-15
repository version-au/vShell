using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The command palette's commands that need the UI: SSH to a saved terminal connection, vShell's own windows, the PC (lock, sleep,
    /// restart ...), sound and media. The integrations' commands are in Core (PaletteSources).
    /// </summary>
    public static class PaletteUi
    {
        static Task<string> Done() { return Task.FromResult<string>(null); }

        public static void Install(BarManager mgr)
        {
            Palette.Register("Terminal", delegate (BarSettings s, List<PaletteCommand> into) { Terminal(mgr, s, into); });
            Palette.Register("vShell", delegate (BarSettings s, List<PaletteCommand> into) { Shell(mgr, s, into); });
        }

        static void Terminal(BarManager mgr, BarSettings s, List<PaletteCommand> into)
        {
            // vTerm's saved connections (the terminal is vTerm now): an SSH tab in its drop-down
            foreach (TermConnection c0 in TermSsh.All(s))
            {
                TermConnection c = c0;
                if (string.IsNullOrWhiteSpace(c.Host)) continue;
                into.Add(new PaletteCommand
                {
                    Title = "SSH to " + c.Shown, Detail = "Terminal · " + c.Target + (c.Port != 22 ? ":" + c.Port : ""), Glyph = "", Source = "Terminal",
                    Words = c.Host + " " + c.User + " " + c.Folder + " ssh shell connect open console", Opens = true,
                    Run = delegate
                    {
                        TermOpen.Host(mgr, c.Host, c.Shown, c.User, null);
                        return Done();
                    }
                });
            }
        }

        static void Shell(BarManager mgr, BarSettings s, List<PaletteCommand> into)
        {
            const string V = "vShell", Pc = "PC", Snd = "Sound";
            into.Add(new PaletteCommand { Title = "Open vShell settings", Detail = V, Glyph = "", Source = V, Words = "options preferences configure", Opens = true, Run = delegate { mgr.OpenSettings(null); return Done(); } });
            into.Add(new PaletteCommand { Title = "Open a terminal", Detail = V, Glyph = "", Source = V, Words = "terminal shell console cmd powershell", Opens = true, Run = delegate { TermOpen.Terminal(mgr); return Done(); } });
            into.Add(new PaletteCommand { Title = "Mark all notifications read", Detail = V, Glyph = "", Source = V, Words = "notifications clear read", Run = delegate { Notify.MarkAllRead(); return Done(); } });

            into.Add(new PaletteCommand { Title = "Lock the PC", Detail = Pc, Glyph = "", Source = Pc, Words = "lock computer screen windows", Opens = true, Run = delegate { PowerActions.Lock(); return Done(); } });
            into.Add(new PaletteCommand { Title = "Sleep", Detail = Pc, Glyph = "", Source = Pc, Words = "sleep suspend computer windows", Ask = true, Opens = true, Run = delegate { PowerActions.Sleep(); return Done(); } });
            into.Add(new PaletteCommand { Title = "Sign out", Detail = Pc, Glyph = "", Source = Pc, Words = "sign out log off logout computer windows", Ask = true, Opens = true, Run = delegate { PowerActions.SignOut(); return Done(); } });
            into.Add(new PaletteCommand { Title = "Restart the PC", Detail = Pc, Glyph = "", Source = Pc, Words = "restart reboot computer windows", Ask = true, Opens = true, Run = delegate { PowerActions.Restart(); return Done(); } });
            into.Add(new PaletteCommand { Title = "Shut down the PC", Detail = Pc, Glyph = "", Source = Pc, Words = "shut down shutdown power off turn off computer windows", Ask = true, Opens = true, Run = delegate { PowerActions.ShutDown(); return Done(); } });

            float vol;
            bool muted;
            string now = Audio.TryGet(out vol, out muted) ? " · " + (muted ? "muted" : Math.Round(vol * 100) + "%") : "";
            into.Add(new PaletteCommand { Title = "Mute the sound", Detail = Snd + now, Glyph = "", Source = Snd, Words = "mute volume audio silence quiet", Boost = muted ? 0 : 1, Run = delegate { Audio.SetMute(true); return Done(); } });
            into.Add(new PaletteCommand { Title = "Unmute the sound", Detail = Snd + now, Glyph = "", Source = Snd, Words = "unmute volume audio sound on", Boost = muted ? 1 : 0, Run = delegate { Audio.SetMute(false); return Done(); } });
            into.Add(new PaletteCommand
            {
                Title = "Set the volume to {n}%", Detail = Snd + now, Glyph = "", Source = Snd, Words = "volume audio sound loudness", Number = "a volume (0-100%)", Min = 0, Max = 100,
                Run = delegate (double n) { Audio.SetVolume((float)(n / 100.0)); return Done(); }
            });
            into.Add(new PaletteCommand { Title = "Play / pause", Detail = "Media", Glyph = "", Source = "Media", Words = "media music song play pause resume", Run = delegate { MediaSession.Send("toggle"); return Done(); } });
            into.Add(new PaletteCommand { Title = "Next track", Detail = "Media", Glyph = "", Source = "Media", Words = "media music song skip next", Run = delegate { MediaSession.Send("next"); return Done(); } });
            into.Add(new PaletteCommand { Title = "Previous track", Detail = "Media", Glyph = "", Source = "Media", Words = "media music song back previous", Run = delegate { MediaSession.Send("previous"); return Done(); } });
        }
    }
}
