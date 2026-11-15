using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// The Weather module's "Home Assistant" source: a weather.* entity (WeatherEntity) instead of Open-Meteo - offered while this plugin
    /// is added. Read every minute; the forecast from the entity (Home Assistant's weather.get_forecasts).
    /// </summary>
    sealed class HaWeather : IWeatherSource
    {
        public string Id { get { return "HomeAssistant"; } }
        public string Name { get { return "Home Assistant"; } }
        public string SetUpText { get { return "Pick a weather entity"; } }
        public bool IsSetUp(ModuleConfig c) { return !string.IsNullOrWhiteSpace(c.WeatherEntity) && HaClient.Configured; }
        public string Key(ModuleConfig c) { return c.WeatherEntity ?? ""; }
        public double Minutes(ModuleConfig c) { return 1; }
        public string Place(ModuleConfig c) { return c.WeatherEntity ?? ""; }
        public Task<WeatherReport> FetchAsync(ModuleConfig c) { return FetchHa(c.WeatherEntity); }

        // =================================================================
        // Home Assistant
        // =================================================================
        static async Task<WeatherReport> FetchHa(string entity)
        {
            string json = await HaClient.GetStateJsonAsync(entity);
            var r = new WeatherReport { Source = "Home Assistant", Fetched = DateTime.UtcNow };
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement, at;
                string state = JsonValue.Str(root, "state");
                if (state == "unavailable" || state == "unknown") throw new InvalidOperationException(entity + " is " + state + " in Home Assistant");
                r.Kind = FromHa(state);
                r.IsDay = state != "clear-night";
                r.Condition = HaText(state);
                if (root.TryGetProperty("attributes", out at) && at.ValueKind == JsonValueKind.Object)
                {
                    r.Place = JsonValue.Str(at, "friendly_name");
                    r.Temp = JsonValue.Num(at, "temperature");
                    r.Feels = JsonValue.Num(at, "apparent_temperature");
                    r.Humidity = JsonValue.Num(at, "humidity");
                    r.Wind = JsonValue.Num(at, "wind_speed");
                    string tu = JsonValue.Str(at, "temperature_unit"), wu = JsonValue.Str(at, "wind_speed_unit");
                    if (tu.Length > 0) r.TempUnit = tu;
                    if (wu.Length > 0) r.WindUnit = wu;
                    JsonElement fc;
                    if (at.TryGetProperty("forecast", out fc) && fc.ValueKind == JsonValueKind.Array) ReadHaForecast(fc, r.Days, false);
                }
                if (r.Place.Length == 0) r.Place = entity;
            }
            // Home Assistant 2024.3+ no longer puts the forecast in the attributes: ask the weather.get_forecasts service
            if (r.Days.Count == 0)
            {
                foreach (string type in new[] { "daily", "twice_daily" })
                {
                    try
                    {
                        string body = "{\"entity_id\":\"" + entity.Replace("\"", "") + "\",\"type\":\"" + type + "\"}";
                        string resp = await HaClient.PostJsonAsync("/api/services/weather/get_forecasts?return_response", body);
                        using (JsonDocument doc = JsonDocument.Parse(resp))
                        {
                            JsonElement root = doc.RootElement, sr, ent, fc;
                            if (!root.TryGetProperty("service_response", out sr)) sr = root;
                            if (sr.ValueKind == JsonValueKind.Object && sr.TryGetProperty(entity, out ent) && ent.TryGetProperty("forecast", out fc) && fc.ValueKind == JsonValueKind.Array)
                                ReadHaForecast(fc, r.Days, type == "twice_daily");
                        }
                        if (r.Days.Count > 0) break;
                    }
                    catch (Exception ex) { Log.Write("Weather forecast (" + entity + ", " + type + "): " + ex.Message); }
                }
            }
            return r;
        }

        static void ReadHaForecast(JsonElement fc, List<WeatherDay> days, bool twiceDaily)
        {
            foreach (JsonElement e in fc.EnumerateArray())
            {
                DateTimeOffset dto;
                if (!DateTimeOffset.TryParse(JsonValue.Str(e, "datetime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out dto)) continue;
                DateTime date = dto.ToLocalTime().Date;
                double hi = JsonValue.Num(e, "temperature"), lo = JsonValue.Num(e, "templow"), rain = JsonValue.Num(e, "precipitation_probability");
                WeatherKind kind = FromHa(JsonValue.Str(e, "condition"));
                WeatherDay d = days.Find(delegate (WeatherDay x) { return x.Date == date; });
                if (d == null)
                {
                    d = new WeatherDay { Date = date, Kind = kind, High = hi, Low = lo, RainChance = double.IsNaN(rain) ? -1 : (int)Math.Round(rain) };
                    days.Add(d);
                    continue;
                }
                if (!twiceDaily) continue;
                // twice daily: day and night parts of the same date -> high / low of both, the daytime condition
                if (!double.IsNaN(hi)) { d.High = double.IsNaN(d.High) ? hi : Math.Max(d.High, hi); d.Low = double.IsNaN(d.Low) ? hi : Math.Min(d.Low, hi); }
                if (!double.IsNaN(lo)) d.Low = double.IsNaN(d.Low) ? lo : Math.Min(d.Low, lo);
                if (!double.IsNaN(rain)) d.RainChance = Math.Max(d.RainChance, (int)Math.Round(rain));
            }
            days.Sort(delegate (WeatherDay a, WeatherDay b) { return a.Date.CompareTo(b.Date); });
            if (days.Count > 7) days.RemoveRange(7, days.Count - 7);
        }

        static WeatherKind FromHa(string condition)
        {
            switch ((condition ?? "").ToLowerInvariant())
            {
                case "sunny": case "clear-night": return WeatherKind.Clear;
                case "partlycloudy": return WeatherKind.PartlyCloudy;
                case "cloudy": return WeatherKind.Cloudy;
                case "fog": return WeatherKind.Fog;
                case "rainy": case "pouring": return WeatherKind.Rain;
                case "snowy": return WeatherKind.Snow;
                case "snowy-rainy": return WeatherKind.Sleet;
                case "hail": return WeatherKind.Hail;
                case "lightning": case "lightning-rainy": return WeatherKind.Thunder;
                case "windy": case "windy-variant": return WeatherKind.Windy;
                default: return WeatherKind.Unknown;
            }
        }

        static string HaText(string condition)
        {
            switch ((condition ?? "").ToLowerInvariant())
            {
                case "sunny": return "Sunny";
                case "clear-night": return "Clear";
                case "partlycloudy": return "Partly cloudy";
                case "cloudy": return "Cloudy";
                case "fog": return "Fog";
                case "rainy": return "Rain";
                case "pouring": return "Heavy rain";
                case "snowy": return "Snow";
                case "snowy-rainy": return "Sleet";
                case "hail": return "Hail";
                case "lightning": return "Thunderstorm";
                case "lightning-rainy": return "Thunderstorm with rain";
                case "windy": case "windy-variant": return "Windy";
                case "exceptional": return "Exceptional";
                default: return condition ?? "";
            }
        }

        // =================================================================
        // The Weather module's settings with this source: the entity
        // =================================================================
        public static void Rows(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            if (!HaClient.Configured)
                p.Note("Set up the Home Assistant connection first (Plugins page in this window), then come back here to pick the entity.");
            var combo = new ComboBox { MinWidth = 300 };
            var ids = new List<string>();
            var hint = p.Txt("", "vsDim", 12);
            hint.Margin = new Thickness(0, 6, 0, 0);
            bool filling = false;
            Action fill = async delegate
            {
                hint.Text = "Loading weather entities…";
                try
                {
                    List<HaState> all = await HaClient.ListAsync();
                    filling = true;
                    combo.Items.Clear();
                    ids.Clear();
                    foreach (HaState st in all)
                    {
                        if (!st.EntityId.StartsWith("weather.", StringComparison.OrdinalIgnoreCase)) continue;
                        ids.Add(st.EntityId);
                        combo.Items.Add((st.Name.Length > 0 ? st.Name + "   " : "") + "(" + st.EntityId + ")");
                    }
                    if (cfg.WeatherEntity.Length > 0 && ids.IndexOf(cfg.WeatherEntity) < 0)
                    {
                        ids.Add(cfg.WeatherEntity);
                        combo.Items.Add(cfg.WeatherEntity + "   (not found right now)");
                    }
                    combo.SelectedIndex = ids.IndexOf(cfg.WeatherEntity);
                    hint.Text = ids.Count == 0 ? "Home Assistant has no weather entities. Add a weather integration (e.g. Met.no or the BOM integration) in Home Assistant."
                                               : ids.Count + " weather entit" + (ids.Count == 1 ? "y" : "ies") + " found.";
                }
                catch (Exception ex) { hint.Text = "Could not read the entities: " + ex.Message; }
                finally { filling = false; }
            };
            combo.SelectionChanged += delegate
            {
                if (filling || p.Loading) return;
                int i = combo.SelectedIndex;
                if (i < 0 || i >= ids.Count) return;
                cfg.WeatherEntity = ids[i];
                WeatherService.Refresh(cfg);
                p.Changed();
            };
            var refresh = new Button { Content = "Refresh", Margin = new Thickness(6, 0, 0, 0) };
            refresh.Click += delegate { fill(); };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(combo);
            line.Children.Add(refresh);
            var block = new StackPanel();
            block.Children.Add(line);
            block.Children.Add(hint);
            p.Row("Weather entity", block, true);
            if (HaClient.Configured) fill();
            p.Note("Temperatures use the entity's own units. The forecast comes from the entity (Home Assistant's weather.get_forecasts).");
        }
    }
}