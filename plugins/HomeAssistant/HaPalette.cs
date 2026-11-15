using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>The command palette's Home Assistant commands: lights, switches, scenes, scripts, covers, locks, buttons, climate - from the full entity list.</summary>
    static class HaPalette
    {
        static Task<string> Done() { return Task.FromResult<string>(null); }

        // =================================================================
        // Home Assistant: lights, switches, scenes, scripts, covers, locks, buttons, climate - from the full entity list
        // =================================================================
        static List<HaState> _haList;
        static DateTime _haAt = DateTime.MinValue;
        static bool _haBusy;

        /// <summary>Read Home Assistant's entity list when it's a minute old (in the background).</summary>
        public static void Warm()
        {
            if (!HaClient.Configured || _haBusy || (DateTime.UtcNow - _haAt).TotalSeconds < 60) return;
            _haBusy = true;
            Task.Run(async delegate
            {
                try
                {
                    List<HaState> l = await HaClient.ListAsync();
                    _haList = l;
                    _haAt = DateTime.UtcNow;
                    Palette.RaiseChanged();
                }
                catch (Exception ex) { Log.Write("Palette: Home Assistant entities: " + ex.Message); _haAt = DateTime.UtcNow.AddSeconds(-45); }
                finally { _haBusy = false; }
            });
        }

        static void Call(string domain, string service, string entity, Dictionary<string, object> data)
        {
            HaClient.CallService(domain, service, entity, data);
        }

        public static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            List<HaState> list = _haList;
            if (!HaClient.Configured || list == null) return;
            const string Src = "Home Assistant";
            foreach (HaState st0 in list)
            {
                // the newest state when a module watches it (the list is up to a minute old)
                HaState st = HaClient.Peek(st0.EntityId) ?? st0;
                string id = st.EntityId, d = HaClient.Domain(id);
                string name = st.Name.Length > 0 ? st.Name : id;
                if (st.State == "unavailable") continue;
                string words = id.Replace('_', ' ').Replace('.', ' ');
                string now = st.State.Length > 0 ? " · " + st.State.Replace('_', ' ') : "";
                switch (d)
                {
                    case "light": case "switch": case "fan": case "input_boolean": case "automation": case "humidifier":
                        {
                            bool on = st.State == "on";
                            string glyph = d == "light" ? "" : d == "fan" ? "" : d == "automation" ? "" : "";
                            into.Add(new PaletteCommand
                            {
                                Title = "Turn on " + name, Detail = Src + now, Glyph = glyph, Source = Src, Words = words + " switch on enable start " + d,
                                Boost = on ? 0 : 1, Run = delegate { Call("homeassistant", "turn_on", id, null); return Done(); }
                            });
                            into.Add(new PaletteCommand
                            {
                                Title = "Turn off " + name, Detail = Src + now, Glyph = glyph, Source = Src, Words = words + " switch off disable stop " + d,
                                Boost = on ? 1 : 0, Run = delegate { Call("homeassistant", "turn_off", id, null); return Done(); }
                            });
                            if (d == "light")
                                into.Add(new PaletteCommand
                                {
                                    Title = "Set " + name + " to {n}%", Detail = Src + now, Glyph = glyph, Source = Src, Words = words + " brightness dim percent",
                                    Number = "a brightness (0-100%)", Min = 0, Max = 100,
                                    Run = delegate (double n)
                                    {
                                        if (n <= 0) Call("light", "turn_off", id, null);
                                        else Call("light", "turn_on", id, new Dictionary<string, object> { { "brightness_pct", (int)Math.Round(n) } });
                                        return Done();
                                    }
                                });
                            break;
                        }
                    case "scene": case "script":
                        into.Add(new PaletteCommand
                        {
                            Title = (d == "scene" ? "Scene: " : "Run script: ") + name, Detail = Src, Glyph = "", Source = Src, Words = words + " run activate start " + d,
                            Run = delegate { Call(d, "turn_on", id, null); return Done(); }
                        });
                        break;
                    case "button": case "input_button":
                        into.Add(new PaletteCommand
                        {
                            Title = "Press " + name, Detail = Src, Glyph = "", Source = Src, Words = words + " button push",
                            Run = delegate { Call(d, "press", id, null); return Done(); }
                        });
                        break;
                    case "cover":
                        into.Add(new PaletteCommand
                        {
                            Title = "Open " + name, Detail = Src + now, Glyph = "", Source = Src, Words = words + " cover blind garage up raise",
                            Boost = st.State == "closed" ? 1 : 0, Run = delegate { Call("cover", "open_cover", id, null); return Done(); }
                        });
                        into.Add(new PaletteCommand
                        {
                            Title = "Close " + name, Detail = Src + now, Glyph = "", Source = Src, Words = words + " cover blind garage down lower shut",
                            Boost = st.State == "open" ? 1 : 0, Run = delegate { Call("cover", "close_cover", id, null); return Done(); }
                        });
                        break;
                    case "lock":
                        into.Add(new PaletteCommand
                        {
                            Title = "Lock " + name, Detail = Src + now, Glyph = "", Source = Src, Words = words + " lock door",
                            Boost = st.State == "unlocked" ? 1 : 0, Run = delegate { Call("lock", "lock", id, null); return Done(); }
                        });
                        into.Add(new PaletteCommand
                        {
                            Title = "Unlock " + name, Detail = Src + now, Glyph = "", Source = Src, Words = words + " unlock door open", Ask = true,
                            Run = delegate { Call("lock", "unlock", id, null); return Done(); }
                        });
                        break;
                    case "climate":
                        {
                            double lo, hi;
                            if (!st.AttrNum("min_temp", out lo)) lo = 7;
                            if (!st.AttrNum("max_temp", out hi)) hi = 35;
                            double cur;
                            string at = st.AttrNum("current_temperature", out cur) ? " · " + cur.ToString("0.#", CultureInfo.CurrentCulture) + "° inside" : "";
                            string cw = words + " climate ac aircon air conditioner thermostat heating cooling hvac";
                            into.Add(new PaletteCommand
                            {
                                Title = "Set " + name + " to {n}°", Detail = Src + now + at, Glyph = "", Source = Src, Words = cw + " temperature temp degrees",
                                Number = "a temperature", Min = lo, Max = hi,
                                Run = delegate (double n) { Call("climate", "set_temperature", id, new Dictionary<string, object> { { "temperature", n } }); return Done(); }
                            });
                            foreach (string mode in st.AttrList("hvac_modes"))
                            {
                                string md = mode;
                                bool off = md == "off";
                                into.Add(new PaletteCommand
                                {
                                    Title = off ? "Turn off " + name : name + ": " + md.Replace('_', ' '), Detail = Src + now + at, Glyph = "", Source = Src,
                                    Words = cw + " mode " + (off ? "stop" : "turn on start " + (md == "heat" ? "heater warm" : md == "cool" ? "cold" : md == "fan_only" ? "fan" : md == "dry" ? "dehumidify" : "")),
                                    Boost = st.State == md ? 0 : 1,
                                    Run = delegate { Call("climate", "set_hvac_mode", id, new Dictionary<string, object> { { "hvac_mode", md } }); return Done(); }
                                });
                            }
                            break;
                        }
                }
            }
        }
    }
}
