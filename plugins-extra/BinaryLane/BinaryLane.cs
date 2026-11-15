using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using vShell.Core;

namespace vShell.BinaryLane
{
    /// <summary>The BinaryLane connection and its alerts (Settings > Plugins > BinaryLane), kept under Integrations["binarylane"].</summary>
    sealed class BlSettings
    {
        readonly SettingsBag _b;
        public BlSettings(BarSettings s) { _b = s.Integration("binarylane"); }
        public static BlSettings Of(BarSettings s) { return new BlSettings(s); }

        /// <summary>API token from mPanel (Account > API Info). Stored encrypted (TokenPlain is the real one).</summary>
        public string Token { get { return _b.Get("Token", ""); } set { _b.Set("Token", Secret.Protect((value ?? "").Trim())); } }
        public string TokenPlain { get { return _b.GetSecret("Token"); } }

        /// <summary>Alert when the credit left drops below an amount (AU$).</summary>
        public bool LowCreditOn { get { return _b.Get("LowCreditOn", true); } set { _b.Set("LowCreditOn", value); } }
        public double LowCredit { get { return Math.Max(0, _b.Get("LowCredit", 20.0)); } set { _b.Set("LowCredit", Math.Max(0, value)); } }
        /// <summary>Alert when the credit left lasts fewer than this many days.</summary>
        public bool DaysOn { get { return _b.Get("DaysOn", true); } set { _b.Set("DaysOn", value); } }
        public double Days { get { return Math.Max(1, _b.Get("Days", 7.0)); } set { _b.Set("Days", Math.Max(1, value)); } }
        /// <summary>What you spend a month (AU$) for the days left; 0 = worked out from your servers.</summary>
        public double MonthlySpend { get { return Math.Max(0, _b.Get("MonthlySpend", 0.0)); } set { _b.Set("MonthlySpend", Math.Max(0, value)); } }
        /// <summary>Count charges not invoiced yet as already spent.</summary>
        public bool CountUnbilled { get { return _b.Get("CountUnbilled", true); } set { _b.Set("CountUnbilled", value); } }

        public bool NotifyBilling { get { return _b.Get("NotifyBilling", true); } set { _b.Set("NotifyBilling", value); } }
        public bool NotifyTransfer { get { return _b.Get("NotifyTransfer", true); } set { _b.Set("NotifyTransfer", value); } }
        public double TransferPercent { get { return Math.Max(1, Math.Min(100, _b.Get("TransferPercent", 90.0))); } set { _b.Set("TransferPercent", value); } }
        public bool NotifyAlerts { get { return _b.Get("NotifyAlerts", true); } set { _b.Set("NotifyAlerts", value); } }

        /// <summary>What has been said and still applies (one key a line), so a restart doesn't say it again.</summary>
        public string Said { get { return _b.Get("Said", ""); } set { _b.Set("Said", value ?? ""); } }
    }

    /// <summary>A BinaryLane module's settings (cfg.Bag).</summary>
    sealed class BlView
    {
        readonly SettingsBag _b;
        public BlView(ModuleConfig cfg) { _b = cfg.Bag; }

        /// <summary>Credit, Days left or Both.</summary>
        public string Show { get { return _b.Get("Show", "Both"); } set { _b.Set("Show", value ?? "Both"); } }
        public bool Icon { get { return _b.Get("Icon", true); } set { _b.Set("Icon", value); } }
    }

    sealed class BlServer
    {
        public long Id;
        public string Name = "", Status = "";
        /// <summary>What it costs a month (AU$): its size plus extra memory, disk, IPv4 and transfer - not backups.</summary>
        public double Monthly;
    }

    sealed class BlTransfer
    {
        public long ServerId;
        public double UsedGb, IncludedGb;
        public DateTime PeriodEnd;
        public double Percent { get { return IncludedGb > 0 ? UsedGb / IncludedGb * 100 : 0; } }
    }

    sealed class BlInvoice
    {
        public long Id;
        public string Number = "";
        public double Amount;
        public DateTime Due;
    }

    sealed class BlAlert
    {
        public long ServerId;
        public string Type = "", Name = "", Unit = "";
        public double Value, Current;
        public string Raised = "";
    }

    sealed class BlSnapshot
    {
        public DateTime Fetched = DateTime.MinValue;
        public string Error = "";
        public string Status = "";
        public double Credit, Unbilled;
        public List<BlServer> Servers = new List<BlServer>();
        public List<BlTransfer> Transfers = new List<BlTransfer>();
        public List<BlInvoice> Failed = new List<BlInvoice>();
        public List<BlAlert> Alerts = new List<BlAlert>();
        /// <summary>Which parts were read this time (a part that failed keeps what it said before).</summary>
        public bool ServersOk, TransfersOk, FailedOk, AlertsOk;
        public bool Ok { get { return Fetched != DateTime.MinValue; } }

        public string ServerName(long id)
        {
            foreach (BlServer s in Servers) if (s.Id == id) return s.Name;
            return "server " + id;
        }
    }

    /// <summary>The numbers worked out from a snapshot and the settings: credit left, monthly spend, days left.</summary>
    struct BlMoney
    {
        public double Left, Monthly;
        public bool MonthlyGuessed;
        /// <summary>Days the credit left lasts (PositiveInfinity = nothing is being spent).</summary>
        public double Days;

        public static BlMoney Of(BlSnapshot snap, BlSettings c)
        {
            var m = new BlMoney { Left = snap.Credit - (c.CountUnbilled ? snap.Unbilled : 0) };
            if (c.MonthlySpend > 0) m.Monthly = c.MonthlySpend;
            else
            {
                m.MonthlyGuessed = true;
                foreach (BlServer s in snap.Servers) m.Monthly += s.Monthly;
            }
            double perDay = m.Monthly * 12 / 365.25;
            m.Days = perDay > 0 ? Math.Max(0, m.Left) / perDay : double.PositiveInfinity;
            return m;
        }

        public string DaysText
        {
            get
            {
                if (double.IsPositiveInfinity(Days)) return "no spend";
                if (Days < 1) return "under a day";
                return Math.Floor(Days).ToString("0", CultureInfo.CurrentCulture) + (Math.Floor(Days) == 1 ? " day" : " days");
            }
        }
    }

    /// <summary>
    /// Talks to the BinaryLane API (api.binarylane.com.au, a Bearer token). Polled in the background: every 2 minutes while a module shows
    /// it, every 15 minutes otherwise (credit doesn't move fast). Snapshots are swapped in on the UI thread, where the alerts are worked out.
    /// </summary>
    static class BlClient
    {
        public const string Api = "https://api.binarylane.com.au";
        public const string Panel = "https://home.binarylane.com.au/";

        static readonly object Gate = new object();
        static BlSnapshot _snap = new BlSnapshot();
        static Poller _poll;
        static BarSettings _s;
        static string _configuredFor;

        /// <summary>A new snapshot (on the UI thread).</summary>
        public static event Action<BlSnapshot> Changed;

        public static bool Configured(BarSettings s) { return s != null && BlSettings.Of(s).Token.Length > 0; }

        public static void Configure(BarSettings s)
        {
            _s = s;
            string id = BlSettings.Of(s).Token;
            if (_configuredFor != null && _configuredFor != id) lock (Gate) _snap = new BlSnapshot();      // another account
            _configuredFor = id;
            if (!Configured(s)) return;
            if (_poll == null)
            {
                _poll = new Poller("BinaryLane", Poll, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(15));
                _poll.Start();
            }
            else _poll.PollNow();
        }

        public static void Stop()
        {
            if (_poll != null) { _poll.Stop(); _poll = null; }
            lock (Gate) _snap = new BlSnapshot();
            _s = null;
            _configuredFor = null;
        }

        /// <summary>The latest snapshot (asking for it keeps the faster polling going).</summary>
        public static BlSnapshot Latest
        {
            get
            {
                if (_poll != null) _poll.Want();
                lock (Gate) return _snap;
            }
        }

        public static void PollNow() { if (_poll != null) _poll.PollNow(); }

        static async Task Poll()
        {
            BarSettings s = _s;
            if (s == null || !Configured(s)) return;
            BlSnapshot old;
            lock (Gate) old = _snap;
            BlSnapshot snap;
            try { snap = await ReadAll(BlSettings.Of(s).TokenPlain, old).ConfigureAwait(false); }
            catch (Exception ex)
            {
                string msg = Why(ex);
                if (old.Error != msg) Log.Write("BinaryLane: " + msg);
                snap = new BlSnapshot
                {
                    Error = msg, Fetched = old.Fetched, Status = old.Status, Credit = old.Credit, Unbilled = old.Unbilled,
                    Servers = old.Servers, Transfers = old.Transfers, Failed = old.Failed, Alerts = old.Alerts
                };
            }
            System.Windows.Application app = System.Windows.Application.Current;
            if (app == null) return;
            await app.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_s == null) return;      // removed meanwhile
                lock (Gate) _snap = snap;
                Action<BlSnapshot> c = Changed;
                if (c != null) try { c(snap); } catch (Exception ex) { Log.Write("BinaryLane: " + ex.Message); }
            }));
        }

        // =================================================================
        // Reading
        // =================================================================
        static async Task<BlSnapshot> ReadAll(string token, BlSnapshot old)
        {
            var snap = new BlSnapshot();
            // the balance is the one that must work; the rest keep what they had when they fail
            using (JsonDocument d = await Get(token, "/v2/customers/my/balance").ConfigureAwait(false))
            {
                JsonElement b = Obj(d.RootElement, "balance");
                snap.Credit = Zero(JsonValue.Num(b, "available_credit"));
                snap.Unbilled = Zero(JsonValue.Num(b, "unbilled_total"));
            }
            try
            {
                using (JsonDocument d = await Get(token, "/v2/account").ConfigureAwait(false))
                    snap.Status = JsonValue.Str(Obj(d.RootElement, "account"), "status");
            }
            catch (Exception ex) { snap.Status = old.Status; Log.Write("BinaryLane: account: " + Why(ex)); }

            try
            {
                using (JsonDocument d = await Get(token, "/v2/servers?per_page=200").ConfigureAwait(false))
                    foreach (JsonElement e in Arr(d.RootElement, "servers"))
                        snap.Servers.Add(new BlServer
                        {
                            Id = (long)Zero(JsonValue.Num(e, "id")), Name = JsonValue.Str(e, "name"), Status = JsonValue.Str(e, "status"), Monthly = Cost(e)
                        });
                snap.ServersOk = true;
            }
            catch (Exception ex) { snap.Servers = old.Servers; Log.Write("BinaryLane: servers: " + Why(ex)); }

            try
            {
                using (JsonDocument d = await Get(token, "/v2/data_usages/current?per_page=200").ConfigureAwait(false))
                    foreach (JsonElement e in Arr(d.RootElement, "data_usages"))
                        snap.Transfers.Add(new BlTransfer
                        {
                            ServerId = (long)Zero(JsonValue.Num(e, "server_id")),
                            UsedGb = Zero(JsonValue.Num(e, "current_transfer_usage_gigabytes")),
                            IncludedGb = Zero(JsonValue.Num(e, "transfer_gigabytes")),
                            PeriodEnd = Date(JsonValue.Str(e, "transfer_period_end"))
                        });
                snap.TransfersOk = true;
            }
            catch (Exception ex) { snap.Transfers = old.Transfers; Log.Write("BinaryLane: data transfer: " + Why(ex)); }

            try
            {
                using (JsonDocument d = await Get(token, "/v2/customers/my/unpaid-payment-failed-invoices").ConfigureAwait(false))
                    foreach (JsonElement e in Arr(d.RootElement, "invoices"))
                        snap.Failed.Add(new BlInvoice
                        {
                            Id = (long)Zero(JsonValue.Num(e, "invoice_id")), Number = JsonValue.Str(e, "invoice_number"),
                            Amount = Zero(JsonValue.Num(e, "amount")), Due = Date(JsonValue.Str(e, "date_due"))
                        });
                snap.FailedOk = true;
            }
            catch (Exception ex) { snap.Failed = old.Failed; Log.Write("BinaryLane: invoices: " + Why(ex)); }

            try
            {
                // the servers with a threshold alert raised now, then which of their alerts it is
                var ids = new List<long>();
                using (JsonDocument d = await Get(token, "/v2/servers/threshold_alerts").ConfigureAwait(false))
                    foreach (JsonElement e in Arr(d.RootElement, "server_ids"))
                        if (e.ValueKind == JsonValueKind.Number) ids.Add(e.GetInt64());
                foreach (long id in ids)
                    using (JsonDocument d = await Get(token, "/v2/servers/" + id + "/threshold_alerts").ConfigureAwait(false))
                        foreach (JsonElement e in Arr(d.RootElement, "threshold_alerts"))
                        {
                            if (!JsonValue.Bool(e, "enabled")) continue;
                            string raised = JsonValue.Str(e, "last_raised"), cleared = JsonValue.Str(e, "last_cleared");
                            if (raised.Length == 0 || (cleared.Length > 0 && Date(cleared) >= Date(raised))) continue;
                            snap.Alerts.Add(new BlAlert
                            {
                                ServerId = id, Type = JsonValue.Str(e, "alert_type"), Name = JsonValue.Str(e, "name"), Unit = JsonValue.Str(e, "unit"),
                                Value = Zero(JsonValue.Num(e, "value")), Current = JsonValue.Num(e, "current_value"), Raised = raised
                            });
                        }
                snap.AlertsOk = true;
            }
            catch (Exception ex) { snap.Alerts = old.Alerts; Log.Write("BinaryLane: threshold alerts: " + Why(ex)); }

            snap.Fetched = DateTime.UtcNow;
            return snap;
        }

        /// <summary>A server's monthly cost: its size, plus memory / disk / IPv4 / transfer above what the size includes.</summary>
        static double Cost(JsonElement server)
        {
            JsonElement size = Obj(server, "size"), opt = Obj(size, "options"), sel = Obj(server, "selected_size_options");
            double cost = Zero(JsonValue.Num(size, "price_monthly"));
            cost += Extra(sel, "memory", size, "memory", opt, "memory_cost_per_additional_megabyte", 1);
            cost += Extra(sel, "disk", size, "disk", opt, "disk_cost_per_additional_gigabyte", 1);
            cost += Extra(sel, "transfer", size, "transfer", opt, "transfer_cost_per_additional_gigabyte", 1000);    // TB -> GB
            double ips = JsonValue.Num(sel, "ipv4_addresses");
            if (!double.IsNaN(ips) && ips > 1) cost += (ips - 1) * Zero(JsonValue.Num(opt, "ipv4_addresses_cost_per_address"));
            return cost;
        }

        static double Extra(JsonElement sel, string selName, JsonElement size, string sizeName, JsonElement opt, string costName, double scale)
        {
            double have = JsonValue.Num(sel, selName), incl = JsonValue.Num(size, sizeName), per = JsonValue.Num(opt, costName);
            if (double.IsNaN(have) || double.IsNaN(incl) || double.IsNaN(per) || have <= incl) return 0;
            return (have - incl) * scale * per;
        }

        static async Task<JsonDocument> Get(string token, string path)
        {
            HttpClient h = PluginHttp.Client("BinaryLane", false, 20, true);
            using (var req = new HttpRequestMessage(HttpMethod.Get, Api + path))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.Accept.ParseAdd("application/json");
                using (HttpResponseMessage r = await h.SendAsync(req).ConfigureAwait(false))
                {
                    string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!r.IsSuccessStatusCode) throw new Exception(Fail((int)r.StatusCode, body));
                    return JsonDocument.Parse(body);
                }
            }
        }

        static string Fail(int code, string body)
        {
            if (code == 401) return "BinaryLane didn't accept the API token (401) - make a new one in mPanel under Account > API Info";
            if (code == 403) return "The API token can't read this (403)";
            if (code == 429) return "BinaryLane says too many requests (429) - it tries again later";
            string msg = body != null && body.Length > 0 && body.Length < 200 && !body.TrimStart().StartsWith("<") ? ": " + body.Trim() : "";
            return "BinaryLane answered " + code + msg;
        }

        static string Why(Exception ex)
        {
            if (ex is TaskCanceledException) return "BinaryLane didn't answer in time";
            if (ex is HttpRequestException && ex.InnerException != null) return ex.InnerException.Message;
            return ex.Message;
        }

        /// <summary>Test connection: the account and credit, or why it failed.</summary>
        public static async Task<KeyValuePair<bool, string>> TestAsync(BarSettings s)
        {
            try
            {
                if (!Configured(s)) return new KeyValuePair<bool, string>(false, "Enter the API token first.");
                BlSettings c = BlSettings.Of(s);
                BlSnapshot snap = await ReadAll(c.TokenPlain, new BlSnapshot()).ConfigureAwait(false);
                BlMoney m = BlMoney.Of(snap, c);
                PollNow();
                return new KeyValuePair<bool, string>(true,
                    "Connected - " + (snap.Servers.Count == 1 ? "1 server" : snap.Servers.Count + " servers") +
                    ", credit " + Money(snap.Credit) + ", not invoiced yet " + Money(snap.Unbilled) +
                    (m.Monthly > 0 ? ", about " + Money(m.Monthly) + " a month (" + m.DaysText + " left)" : "") + ".");
            }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, Why(ex)); }
        }

        // ---- helpers ----
        public static string Money(double v)
        {
            return (v < 0 ? "-$" : "$") + Math.Abs(v).ToString("#,0.00", CultureInfo.CurrentCulture);
        }

        static double Zero(double v) { return double.IsNaN(v) ? 0 : v; }

        static DateTime Date(string s)
        {
            DateTime d;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d) ? d : DateTime.MinValue;
        }

        static JsonElement Obj(JsonElement e, string name)
        {
            JsonElement v;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Object ? v : default(JsonElement);
        }

        static IEnumerable<JsonElement> Arr(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Array)
                foreach (JsonElement i in v.EnumerateArray()) yield return i;
        }
    }
}
