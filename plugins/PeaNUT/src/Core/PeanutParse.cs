using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VShellPeanut
{
    /// <summary>
    /// Reads PeaNUT's REST API. GET /api/v1/devices?meta=true gives every UPS on every NUT server PeaNUT watches,
    /// each as a flat object of NUT variables ("battery.charge": 100, "ups.status": "OL") plus
    /// "peanut.device_id" (the NUT device name) and "peanut.server" (host:port of the NUT server).
    /// </summary>
    public static class PeanutParse
    {
        public const string DevicesPath = "/api/v1/devices?meta=true";

        public static string DevicesUrl(PeanutServer s)
        {
            return s.BaseUrl + DevicesPath;
        }

        public static string BasicAuth(string user, string password)
        {
            if (string.IsNullOrEmpty(user)) return null;
            return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + (password ?? "")));
        }

        public static List<UpsReading> Devices(string json, PeanutServer server, DateTime at)
        {
            var list = new List<UpsReading>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                    throw new FormatException("PeaNUT answered, but not with a list of devices - is this the PeaNUT address?");
                int i = 0;
                foreach (JsonElement e in root.EnumerateArray())
                {
                    i++;
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var u = new UpsReading { ServerId = server.Id, ServerName = server.Shown, At = at };
                    foreach (JsonProperty p in e.EnumerateObject())
                        u.Vars[p.Name] = Text(p.Value);
                    u.Device = u.Var("peanut.device_id");
                    u.NutServer = u.Var("peanut.server");
                    // Older PeaNUT (no ?meta): fall back to something stable.
                    if (u.Device.Length == 0) u.Device = u.Var("device.name");
                    if (u.Device.Length == 0) u.Device = "ups" + i.ToString(CultureInfo.InvariantCulture);
                    // With ?meta on /devices/{id} PeaNUT gives "host:port/name"; on /devices just the name. Keep the name.
                    int slash = u.Device.LastIndexOf('/');
                    if (slash >= 0 && slash < u.Device.Length - 1) u.Device = u.Device.Substring(slash + 1);
                    list.Add(u);
                }
            }
            return list;
        }

        public static string Text(JsonElement v)
        {
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString() ?? "";
                case JsonValueKind.Number: return v.GetRawText();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                case JsonValueKind.Null:
                case JsonValueKind.Undefined: return "";
                default: return v.GetRawText();
            }
        }

        /// <summary>Plain words for an HTTP answer that isn't 200.</summary>
        public static string HttpError(HttpStatusCode code)
        {
            int c = (int)code;
            if (c == 401 || c == 403) return "PeaNUT turned the username or password down (" + c + ")";
            if (c == 404) return "No PeaNUT API at this address (404) - check the address, and the base path if PeaNUT has one";
            if (c >= 500) return "PeaNUT had a problem (" + c + ") - is it connected to its NUT server?";
            return "PeaNUT answered " + c + " " + code;
        }

        /// <summary>
        /// Gives each UPS its shown name. One UPS on the server: the server's name. More: the NUT device name
        /// (or its device.description), with the NUT host only when two share a device name, and the server's
        /// name in front when there's more than one PeaNUT server. A name the user gave a UPS (by its Key) wins, as typed.
        /// </summary>
        public static void Name(List<UpsReading> ups, PeanutServer server, bool manyServers, IDictionary<string, string> renames)
        {
            AutoName(ups, server, manyServers);
            foreach (var u in ups)
            {
                string mine;
                u.Name = renames != null && renames.TryGetValue(u.Key, out mine) ? mine : u.AutoName;
            }
        }

        static void AutoName(List<UpsReading> ups, PeanutServer server, bool manyServers)
        {
            if (ups.Count == 1) { ups[0].AutoName = server.Shown; return; }
            var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in ups) count[u.Device] = count.TryGetValue(u.Device, out int n) ? n + 1 : 1;
            foreach (var u in ups)
            {
                string label = u.Var("device.description").Trim();
                if (label.Length == 0) label = u.Device;
                if (count[u.Device] > 1 && u.NutServer.Length > 0)
                {
                    string host = u.NutServer;
                    int colon = host.LastIndexOf(':');
                    if (colon > 0) host = host.Substring(0, colon);
                    label = label + " (" + host + ")";
                }
                u.AutoName = manyServers ? server.Shown + " " + label : label;
            }
        }
    }
}
