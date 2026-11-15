using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace vShell.Core
{
    /// <summary>
    /// A plugin's own settings, kept in settings.json without vShell knowing them: a module's (ModuleConfig.Extra) and an integration's
    /// (BarSettings.Integration("komodo")). Values are read with the plugin's default (a missing or unreadable value gives the default),
    /// so the plugin owns its defaults and clean-up. Safe to read from a poller while Settings writes it.
    /// </summary>
    [JsonConverter(typeof(SettingsBag.Converter))]
    [PluginApi]
    public sealed class SettingsBag
    {
        readonly Dictionary<string, JsonElement> _v = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        /// <summary>Values handed out with Live: the same object every time, written back when saved.</summary>
        readonly Dictionary<string, object> _live = new Dictionary<string, object>(StringComparer.Ordinal);

        public int Count { get { lock (_v) { var k = new HashSet<string>(_v.Keys); k.UnionWith(_live.Keys); return k.Count; } } }

        public bool Has(string key) { lock (_v) return _v.ContainsKey(key) || _live.ContainsKey(key); }

        /// <summary>
        /// A value kept as an object you change in place (a list of printers, each edited by the settings page): the same object every
        /// time, and whatever it holds is saved - no Set needed. make = the value when there's none yet (or it can't be read).
        /// </summary>
        public T Live<T>(string key, Func<T> make) where T : class
        {
            lock (_v)
            {
                object o;
                if (_live.TryGetValue(key, out o) && o is T) return (T)o;
                T v = null;
                JsonElement e;
                if (_v.TryGetValue(key, out e) && e.ValueKind != JsonValueKind.Null) try { v = e.Deserialize<T>(); } catch { }
                if (v == null) v = make();
                _live[key] = v;
                _v.Remove(key);
                return v;
            }
        }

        public T Get<T>(string key, T def)
        {
            JsonElement e;
            lock (_v)
            {
                object o;
                if (_live.TryGetValue(key, out o)) e = JsonSerializer.SerializeToElement(o);
                else if (!_v.TryGetValue(key, out e)) return def;
            }
            try
            {
                if (e.ValueKind == JsonValueKind.Null) return def;
                T v = e.Deserialize<T>();
                return v == null ? def : v;
            }
            catch { return def; }      // another type than the plugin reads now: its default
        }

        public void Set<T>(string key, T value)
        {
            JsonElement e = JsonSerializer.SerializeToElement(value);
            lock (_v) { _v[key] = e; _live.Remove(key); }
        }

        public void Remove(string key) { lock (_v) { _v.Remove(key); _live.Remove(key); } }

        /// <summary>A secret: stored encrypted (Secret.Protect); Get gives the stored (encrypted) text, GetSecret the plain one.</summary>
        public string GetSecret(string key) { return Secret.Unprotect(Get(key, "")); }
        public void SetSecret(string key, string plain) { Set(key, string.IsNullOrEmpty(plain) ? "" : Secret.Protect(plain)); }

        /// <summary>Takes a value as it was read from the file (moving an old setting in).</summary>
        public void Put(string key, JsonElement e) { lock (_v) { _v[key] = e.Clone(); _live.Remove(key); } }

        public sealed class Converter : JsonConverter<SettingsBag>
        {
            public override SettingsBag Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var b = new SettingsBag();
                if (reader.TokenType != JsonTokenType.StartObject) { reader.Skip(); return b; }
                using (JsonDocument d = JsonDocument.ParseValue(ref reader))
                    foreach (JsonProperty p in d.RootElement.EnumerateObject()) b._v[p.Name] = p.Value.Clone();
                return b;
            }

            public override void Write(Utf8JsonWriter writer, SettingsBag value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                lock (value._v)
                {
                    foreach (KeyValuePair<string, JsonElement> kv in value._v)
                    {
                        writer.WritePropertyName(kv.Key);
                        kv.Value.WriteTo(writer);
                    }
                    foreach (KeyValuePair<string, object> kv in value._live)
                    {
                        writer.WritePropertyName(kv.Key);
                        JsonSerializer.Serialize(writer, kv.Value, kv.Value.GetType(), options);
                    }
                }
                writer.WriteEndObject();
            }
        }
    }
}
