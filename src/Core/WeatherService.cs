using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace vShell.Core
{
    [PluginApi]
    public enum WeatherKind { Unknown, Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Sleet, Snow, Hail, Thunder, Windy }

    [PluginApi]
    public sealed class WeatherDay
    {
        public DateTime Date;
        public WeatherKind Kind;
        public double High = double.NaN, Low = double.NaN;
        /// <summary>Chance of rain in percent, or -1 when not known.</summary>
        public int RainChance = -1;
    }

    /// <summary>One weather reading (a snapshot; replaced, never changed).</summary>
    [PluginApi]
    public sealed class WeatherReport
    {
        public string Place = "";
        public string Source = "";
        public WeatherKind Kind;
        public bool IsDay = true;
        public string Condition = "";
        public double Temp = double.NaN, Feels = double.NaN, Humidity = double.NaN, Wind = double.NaN;
        public string TempUnit = "°C", WindUnit = "km/h";
        public List<WeatherDay> Days = new List<WeatherDay>();
        public DateTime Fetched;
        /// <summary>Why the last update failed ("" = fine). The previous values are kept.</summary>
        public string Error = "";
    }

    public sealed class GeoPlace
    {
        public string Name = "", Region = "", Country = "";
        public double Lat, Lon;
        public string Display
        {
            get
            {
                var parts = new List<string> { Name };
                if (Region.Length > 0 && Region != Name) parts.Add(Region);
                if (Country.Length > 0) parts.Add(Country);
                return string.Join(", ", parts);
            }
        }
    }

    /// <summary>
    /// Another place the Weather module can get its weather from (a plugin's: Home Assistant's weather entities), next to Open-Meteo.
    /// The module's WeatherSource is the Id; the source keeps its own settings (WeatherEntity, or its bag).
    /// </summary>
    [PluginApi]
    public interface IWeatherSource
    {
        /// <summary>The WeatherSource value it answers to ("HomeAssistant").</summary>
        string Id { get; }
        /// <summary>Shown in the module settings ("Home Assistant").</summary>
        string Name { get; }
        /// <summary>The module has what this source needs (an entity, a connection).</summary>
        bool IsSetUp(ModuleConfig c);
        /// <summary>What reports are shared by (modules with the same key get the same report).</summary>
        string Key(ModuleConfig c);
        /// <summary>Minutes between reads.</summary>
        double Minutes(ModuleConfig c);
        /// <summary>Read it now (throws with a message on failure).</summary>
        Task<WeatherReport> FetchAsync(ModuleConfig c);
        /// <summary>What to call it while there's no report yet (the entity).</summary>
        string Place(ModuleConfig c);
        /// <summary>What the module says while it isn't set up ("Pick a weather entity").</summary>
        string SetUpText { get; }
    }

    /// <summary>
    /// Weather for the Weather module, from Open-Meteo (free, no account) or a plugin's source (Home Assistant). Modules call Get(cfg)
    /// as often as they like; it returns the cached report and refreshes it in the background when it is due (Open-Meteo every
    /// cfg.WeatherMinutes, Home Assistant every minute). Reports are shared by every module that shows the same place / entity.
    /// </summary>
    public static class WeatherService
    {
        sealed class Entry
        {
            public WeatherReport Report;
            public DateTime LastTry = DateTime.MinValue;
            public bool Busy;
        }

        static readonly object Gate = new object();
        static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>();
        static HttpClient _http;

        static HttpClient Http
        {
            get
            {
                if (_http == null)
                {
                    _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                    _http.DefaultRequestHeaders.UserAgent.ParseAdd("vShell/1.0 (desktop weather module)");
                }
                return _http;
            }
        }

        /// <summary>The plugins' weather sources (Home Assistant), next to Open-Meteo.</summary>
        public static readonly List<IWeatherSource> Sources = new List<IWeatherSource>();

        /// <summary>The module's source when it isn't Open-Meteo (null = Open-Meteo, or a source whose plugin isn't added).</summary>
        public static IWeatherSource SourceOf(ModuleConfig c)
        {
            if (c.WeatherSource == "OpenMeteo") return null;
            lock (Sources) return Sources.Find(delegate (IWeatherSource x) { return x.Id == c.WeatherSource; });
        }

        /// <summary>The module asks for a source that isn't there (its plugin isn't added).</summary>
        public static bool SourceMissing(ModuleConfig c) { return c.WeatherSource != "OpenMeteo" && SourceOf(c) == null; }

        static string Key(ModuleConfig c)
        {
            IWeatherSource src = SourceOf(c);
            if (src != null) return src.Id + "|" + src.Key(c);
            return "om|" + c.WeatherLat.ToString("0.###", CultureInfo.InvariantCulture) + "|" + c.WeatherLon.ToString("0.###", CultureInfo.InvariantCulture) +
                   "|" + (c.WeatherFahrenheit ? "F" : "C");
        }

        /// <summary>true when the module has what it needs (a place, or what its source needs - an entity and a connection).</summary>
        public static bool IsSetUp(ModuleConfig c)
        {
            if (c.WeatherSource != "OpenMeteo") { IWeatherSource src = SourceOf(c); return src != null && src.IsSetUp(c); }
            return !string.IsNullOrWhiteSpace(c.WeatherPlace);
        }

        /// <summary>The latest report for this module's settings, or null while the first one is being fetched.</summary>
        public static WeatherReport Get(ModuleConfig c)
        {
            if (!IsSetUp(c)) return null;
            string key = Key(c);
            Entry e;
            bool start = false;
            lock (Gate)
            {
                if (!Cache.TryGetValue(key, out e)) { e = new Entry(); Cache[key] = e; }
                IWeatherSource own = SourceOf(c);
                double every = own != null ? own.Minutes(c) : Math.Max(5, c.WeatherMinutes);
                bool due = e.Report == null || (DateTime.UtcNow - e.Report.Fetched).TotalMinutes >= every;
                bool retryOk = (DateTime.UtcNow - e.LastTry).TotalSeconds >= (e.Report == null || e.Report.Error.Length == 0 ? 0 : 60);
                if (due && retryOk && !e.Busy)
                {
                    e.Busy = true;
                    e.LastTry = DateTime.UtcNow;
                    start = true;
                }
            }
            if (start)
            {
                IWeatherSource src = SourceOf(c);
                string place = src != null ? src.Place(c) : c.WeatherPlace;
                double lat = c.WeatherLat, lon = c.WeatherLon;
                bool f = c.WeatherFahrenheit;
                Task.Run(async delegate
                {
                    WeatherReport r = null;
                    string err = "";
                    try { r = src != null ? await src.FetchAsync(c) : await FetchOpenMeteo(lat, lon, f, place); }
                    catch (Exception ex) { err = ex.Message; Log.Write("Weather (" + place + "): " + ex.Message); }
                    lock (Gate)
                    {
                        e.Busy = false;
                        if (r != null) e.Report = r;
                        else if (e.Report != null)
                        {
                            // keep the old values, note the problem; try again in a minute
                            e.Report.Error = err;
                        }
                        else e.Report = new WeatherReport { Place = place, Error = err, Fetched = DateTime.MinValue };
                    }
                });
            }
            lock (Gate) return e.Report;
        }

        /// <summary>Forget cached reports so the next Get fetches again (after the settings changed).</summary>
        [PluginApi] public static void Refresh(ModuleConfig c)
        {
            lock (Gate) Cache.Remove(Key(c));
        }

        // =================================================================
        // Open-Meteo
        // =================================================================
        static string Num(double v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }

        static async Task<WeatherReport> FetchOpenMeteo(double lat, double lon, bool fahrenheit, string place)
        {
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + Num(lat) + "&longitude=" + Num(lon) +
                         "&current=temperature_2m,apparent_temperature,relative_humidity_2m,is_day,weather_code,wind_speed_10m" +
                         "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
                         "&timezone=auto&forecast_days=7" + (fahrenheit ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "");
            string json = await Http.GetStringAsync(url);
            var r = new WeatherReport { Place = place, Source = "Open-Meteo", Fetched = DateTime.UtcNow };
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement, cur, units, daily;
                if (root.TryGetProperty("current", out cur))
                {
                    r.Temp = D(cur, "temperature_2m");
                    r.Feels = D(cur, "apparent_temperature");
                    r.Humidity = D(cur, "relative_humidity_2m");
                    r.Wind = D(cur, "wind_speed_10m");
                    r.IsDay = D(cur, "is_day") != 0;
                    int code = (int)D(cur, "weather_code");
                    r.Kind = FromWmo(code);
                    r.Condition = WmoText(code);
                }
                if (root.TryGetProperty("current_units", out units))
                {
                    string tu = S(units, "temperature_2m"), wu = S(units, "wind_speed_10m");
                    if (tu.Length > 0) r.TempUnit = tu;
                    if (wu.Length > 0) r.WindUnit = wu;
                }
                if (root.TryGetProperty("daily", out daily))
                {
                    JsonElement times, codes, maxs, mins, rain;
                    if (daily.TryGetProperty("time", out times) && times.ValueKind == JsonValueKind.Array)
                    {
                        daily.TryGetProperty("weather_code", out codes);
                        daily.TryGetProperty("temperature_2m_max", out maxs);
                        daily.TryGetProperty("temperature_2m_min", out mins);
                        daily.TryGetProperty("precipitation_probability_max", out rain);
                        int n = times.GetArrayLength();
                        for (int i = 0; i < n; i++)
                        {
                            DateTime d;
                            if (!DateTime.TryParse(times[i].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                            double rc = At(rain, i);
                            r.Days.Add(new WeatherDay
                            {
                                Date = d.Date,
                                Kind = FromWmo((int)At(codes, i)),
                                High = At(maxs, i),
                                Low = At(mins, i),
                                RainChance = double.IsNaN(rc) ? -1 : (int)Math.Round(rc)
                            });
                        }
                    }
                }
            }
            return r;
        }

        /// <summary>Find places by name (Open-Meteo's free geocoding).</summary>
        public static async Task<List<GeoPlace>> SearchPlaces(string name)
        {
            var list = new List<GeoPlace>();
            name = (name ?? "").Trim();
            if (name.Length < 2) return list;
            string json = await Http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?count=10&language=en&format=json&name=" + Uri.EscapeDataString(name));
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement results;
                if (!doc.RootElement.TryGetProperty("results", out results) || results.ValueKind != JsonValueKind.Array) return list;
                foreach (JsonElement e in results.EnumerateArray())
                    list.Add(new GeoPlace { Name = S(e, "name"), Region = S(e, "admin1"), Country = S(e, "country"), Lat = D(e, "latitude"), Lon = D(e, "longitude") });
            }
            return list;
        }

        static WeatherKind FromWmo(int code)
        {
            if (code == 0) return WeatherKind.Clear;
            if (code == 1 || code == 2) return WeatherKind.PartlyCloudy;
            if (code == 3) return WeatherKind.Cloudy;
            if (code == 45 || code == 48) return WeatherKind.Fog;
            if (code >= 51 && code <= 55) return WeatherKind.Drizzle;
            if (code == 56 || code == 57 || code == 66 || code == 67) return WeatherKind.Sleet;
            if ((code >= 61 && code <= 65) || (code >= 80 && code <= 82)) return WeatherKind.Rain;
            if ((code >= 71 && code <= 77) || code == 85 || code == 86) return WeatherKind.Snow;
            if (code >= 95) return WeatherKind.Thunder;
            return WeatherKind.Unknown;
        }

        static string WmoText(int code)
        {
            switch (code)
            {
                case 0: return "Clear";
                case 1: return "Mostly clear";
                case 2: return "Partly cloudy";
                case 3: return "Overcast";
                case 45: case 48: return "Fog";
                case 51: case 53: case 55: return "Drizzle";
                case 56: case 57: return "Freezing drizzle";
                case 61: return "Light rain";
                case 63: return "Rain";
                case 65: return "Heavy rain";
                case 66: case 67: return "Freezing rain";
                case 71: return "Light snow";
                case 73: return "Snow";
                case 75: return "Heavy snow";
                case 77: return "Snow grains";
                case 80: return "Light showers";
                case 81: return "Showers";
                case 82: return "Heavy showers";
                case 85: case 86: return "Snow showers";
                case 95: return "Thunderstorm";
                case 96: case 99: return "Thunderstorm with hail";
                default: return "";
            }
        }

        // ---- JSON helpers ----------------------------------------------------------------------------------
        static double D(JsonElement e, string name) { return JsonValue.Num(e, name); }

        static string S(JsonElement e, string name) { return JsonValue.Str(e, name); }

        static double At(JsonElement arr, int i)
        {
            if (arr.ValueKind != JsonValueKind.Array || i >= arr.GetArrayLength()) return double.NaN;
            JsonElement v = arr[i];
            return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
        }
    }
}
