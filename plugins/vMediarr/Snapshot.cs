using System;
using System.Collections.Generic;
using System.Text.Json;

namespace VMediarr
{
    /// <summary>One download in vMediarr's queue (/api/v1/overview).</summary>
    public sealed class VmDownload
    {
        public string Title = "", Release = "", State = "", TimeLeft = "", Message = "", Link = "";
        public double Progress;
        public bool Problem { get { return State == "failed" || State == "importFailed"; } }
    }

    /// <summary>An episode coming in the next week.</summary>
    public sealed class VmComing
    {
        public string Title = "", Episode = "", Name = "", Link = "";
        public DateTime AtUtc;
    }

    /// <summary>What vMediarr said last (swapped whole on the UI thread, never changed).</summary>
    public sealed class VmSnapshot
    {
        public bool Ok, Checked;
        public string Error = "", Version = "";
        public int Movies, Series, Downloading, Problems, RequestsWaiting;
        public long LastEvent;
        public List<VmDownload> Queue = new List<VmDownload>();
        public List<VmComing> ComingUp = new List<VmComing>();
        public DateTime AtUtc = DateTime.UtcNow;

        public static VmSnapshot Parse(string json)
        {
            var s = new VmSnapshot { Ok = true, Checked = true };
            using (var d = JsonDocument.Parse(json))
            {
                var r = d.RootElement;
                s.Version = Str(r, "version");
                s.Movies = Int(r, "movies");
                s.Series = Int(r, "series");
                s.Downloading = Int(r, "downloading");
                s.Problems = Int(r, "problems");
                s.RequestsWaiting = Int(r, "requestsWaiting");
                s.LastEvent = r.TryGetProperty("lastEvent", out var le) && le.ValueKind == JsonValueKind.Number ? le.GetInt64() : 0;
                if (r.TryGetProperty("queue", out var q))
                    foreach (var g in q.EnumerateArray())
                        s.Queue.Add(new VmDownload
                        {
                            Title = Str(g, "title"), Release = Str(g, "release"), State = Str(g, "state"), TimeLeft = Str(g, "timeLeft"), Message = Str(g, "message"), Link = Str(g, "link"),
                            Progress = g.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0,
                        });
                if (r.TryGetProperty("comingUp", out var c))
                    foreach (var e in c.EnumerateArray())
                        s.ComingUp.Add(new VmComing
                        {
                            Title = Str(e, "title"), Episode = Str(e, "episode"), Name = Str(e, "name"), Link = Str(e, "link"),
                            AtUtc = e.TryGetProperty("at", out var at) && at.ValueKind == JsonValueKind.String && DateTime.TryParse(at.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var t) ? t : DateTime.MinValue,
                        });
            }
            return s;
        }

        static string Str(JsonElement e, string n) { return e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : ""; }
        static int Int(JsonElement e, string n) { return e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0; }
    }

    /// <summary>Something vMediarr says happened (/api/v1/events).</summary>
    public sealed class VmEvent
    {
        public long Id;
        public string Kind = "", Title = "", Detail = "", Look = "", Link = "";
        public bool ToVShell;

        public static List<VmEvent> ParseList(string json)
        {
            var list = new List<VmEvent>();
            using (var d = JsonDocument.Parse(json))
                foreach (var e in d.RootElement.GetProperty("events").EnumerateArray())
                    list.Add(new VmEvent
                    {
                        Id = e.GetProperty("id").GetInt64(), Kind = S(e, "kind"), Title = S(e, "title"), Detail = S(e, "detail"), Look = S(e, "look"), Link = S(e, "link"),
                        ToVShell = !e.TryGetProperty("toVShell", out var tv) || tv.ValueKind != JsonValueKind.False,
                    });
            return list;
        }

        static string S(JsonElement e, string n) { return e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : ""; }
    }
}
