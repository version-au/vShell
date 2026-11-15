using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace VShellPeanut
{
    /// <summary>One PeaNUT server the user added. Kept in the integration's settings ("peanut" / "Servers").</summary>
    public sealed class PeanutServer
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string User { get; set; } = "";
        /// <summary>Encrypted with vShell's Secret.Protect - never the plain password.</summary>
        public string Password { get; set; } = "";
        public bool IgnoreCert { get; set; }
        public bool Enabled { get; set; } = true;

        [JsonIgnore]
        public string Shown => Name.Trim().Length > 0 ? Name.Trim() : HostOf(Url);

        [JsonIgnore]
        public string BaseUrl => Normalize(Url);

        public PeanutServer Copy()
        {
            return new PeanutServer
            {
                Id = Id, Name = Name, Url = Url, User = User, Password = Password,
                IgnoreCert = IgnoreCert, Enabled = Enabled
            };
        }

        /// <summary>"192.168.1.5:8080" -> "http://192.168.1.5:8080"; drops a trailing slash and a pasted "/api/..." tail.</summary>
        public static string Normalize(string url)
        {
            string u = (url ?? "").Trim();
            if (u.Length == 0) return "";
            if (u.IndexOf("://", StringComparison.Ordinal) < 0) u = "http://" + u;
            int api = u.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
            if (api > 0) u = u.Substring(0, api);
            return u.TrimEnd('/');
        }

        public static string HostOf(string url)
        {
            string n = Normalize(url);
            if (n.Length == 0) return "PeaNUT";
            try { return new Uri(n).Host; } catch { return n; }
        }
    }

    /// <summary>A name the user gave one UPS. Kept in the integration's settings ("peanut" / "Renames").</summary>
    public sealed class UpsRename
    {
        /// <summary>The UPS's <see cref="UpsReading.Key"/>.</summary>
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Its automatic name when it was last seen - to show in the list while it isn't there.</summary>
        public string Auto { get; set; } = "";
    }

    /// <summary>One UPS as PeaNUT reported it, with the NUT variables it sent.</summary>
    public sealed class UpsReading
    {
        public string ServerId = "";
        public string ServerName = "";
        /// <summary>The NUT server (host:port) PeaNUT got it from - PeaNUT can watch several.</summary>
        public string NutServer = "";
        /// <summary>The NUT device name (ups.conf section name), e.g. "ups".</summary>
        public string Device = "";
        /// <summary>Shown name - filled in by the store once it knows how many UPSes the server has.</summary>
        public string Name = "";
        /// <summary>The name it gets when the user hasn't renamed it.</summary>
        public string AutoName = "";
        /// <summary>True when the server couldn't be reached and this is the last good reading.</summary>
        public bool Stale;
        public DateTime At;
        public Dictionary<string, string> Vars = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Key => ServerId + "|" + NutServer + "|" + Device;

        public string Var(string name)
        {
            string v;
            return Vars.TryGetValue(name, out v) ? v : "";
        }

        public double Num(string name)
        {
            double d;
            string v = Var(name).Trim();
            return v.Length > 0 && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : double.NaN;
        }

        public string StatusRaw => Var("ups.status").Trim();

        public bool Has(string flag)
        {
            foreach (string f in StatusRaw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (string.Equals(f, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public bool Known => StatusRaw.Length > 0;
        public bool Online => Has("OL");
        public bool OnBattery => Has("OB");
        public bool LowBattery => Has("LB");
        public bool Charging => Has("CHRG");
        public bool ReplaceBattery => Has("RB");
        public bool Overloaded => Has("OVER");
        public bool Bypass => Has("BYPASS");
        public bool Off => Has("OFF");
        public bool ForcedShutdown => Has("FSD");
        public bool Calibrating => Has("CAL");
        /// <summary>PeaNUT is up but its NUT server can't reach this UPS (PeaNUT reports the status "Device Unreachable").</summary>
        public bool Unreachable => StatusRaw.Equals("Device Unreachable", StringComparison.OrdinalIgnoreCase);

        public double Charge => Num("battery.charge");
        public double RuntimeSeconds => Num("battery.runtime");
        public double Load => Num("ups.load");

        /// <summary>Real power in watts: ups.realpower, else load % of ups.realpower.nominal; NaN when the UPS gives neither.</summary>
        public double Watts
        {
            get
            {
                double w = Num("ups.realpower");
                if (!double.IsNaN(w)) return w;
                double nom = Num("ups.realpower.nominal");
                double load = Load;
                if (!double.IsNaN(nom) && !double.IsNaN(load)) return Math.Round(nom * load / 100.0);
                return double.NaN;
            }
        }

        public string Model
        {
            get
            {
                string mfr = Var("device.mfr").Trim();
                if (mfr.Length == 0) mfr = Var("ups.mfr").Trim();
                string model = Var("device.model").Trim();
                if (model.Length == 0) model = Var("ups.model").Trim();
                if (mfr.Length > 0 && model.StartsWith(mfr, StringComparison.OrdinalIgnoreCase)) mfr = "";
                return (mfr + " " + model).Trim();
            }
        }

        /// <summary>How bad it is: 0 = fine (on mains), 1 = needs attention (on battery, replace battery, bypass), 2 = bad (low battery, overload, off, FSD, unreachable), -1 = no reading.</summary>
        public int Severity
        {
            get
            {
                if (Stale) return 2;
                if (!Known) return -1;
                if (Unreachable) return 2;
                if (LowBattery || ForcedShutdown || Overloaded || Off) return 2;
                if (OnBattery || ReplaceBattery || Bypass) return 1;
                return 0;
            }
        }

        public string StatusText
        {
            get
            {
                if (Stale) return "Can't reach PeaNUT";
                if (!Known) return "No status";
                if (Unreachable) return "NUT can't reach it";
                if (ForcedShutdown) return "Shutting down";
                if (Off) return "Off";
                if (OnBattery && LowBattery) return "On battery - low";
                if (LowBattery) return "Battery low";
                if (OnBattery) return "On battery";
                if (Overloaded) return "Overloaded";
                if (Bypass) return "On bypass";
                if (ReplaceBattery) return "Replace battery";
                if (Calibrating) return "Calibrating";
                if (Online && Charging) return "Online - charging";
                if (Online) return "Online";
                return StatusRaw;
            }
        }
    }

    /// <summary>The last check of one PeaNUT server.</summary>
    public sealed class ServerResult
    {
        public PeanutServer Server;
        public bool Ok;
        public string Error = "";
        public DateTime Checked = DateTime.MinValue;
        public List<UpsReading> Ups = new List<UpsReading>();
    }

    public static class Fmt
    {
        public static string Pct(double v)
        {
            return double.IsNaN(v) ? "-" : Math.Round(v).ToString("0", CultureInfo.CurrentCulture) + "%";
        }

        public static string Runtime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) return "-";
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes.ToString("00") + "m";
            if (t.TotalMinutes >= 1) return (int)t.TotalMinutes + "m";
            return (int)t.TotalSeconds + "s";
        }

        public static string Watts(double w)
        {
            return double.IsNaN(w) ? "-" : Math.Round(w).ToString("0", CultureInfo.CurrentCulture) + " W";
        }

        public static string Volts(double v)
        {
            return double.IsNaN(v) ? "-" : Math.Round(v).ToString("0", CultureInfo.CurrentCulture) + " V";
        }

        public static string Temp(double c)
        {
            return double.IsNaN(c) ? "-" : c.ToString("0.#", CultureInfo.CurrentCulture) + " °C";
        }

        /// <summary>What a tile shows for a UPS, by the module's "Show" choice.</summary>
        public static string Value(UpsReading u, string show)
        {
            if (u.Stale) return "offline";
            switch (show)
            {
                case "Status": return u.StatusText;
                case "Runtime": return Runtime(u.RuntimeSeconds);
                case "Load": return Pct(u.Load);
                case "Power": return Watts(u.Watts);
                case "Charge and runtime":
                    {
                        string rt = Runtime(u.RuntimeSeconds);
                        return rt == "-" ? Pct(u.Charge) : Pct(u.Charge) + " · " + rt;
                    }
                default: return Pct(u.Charge);
            }
        }

        /// <summary>A one-line summary for notifications and the palette: "87% · 23m left · 31% load".</summary>
        public static string Summary(UpsReading u)
        {
            var parts = new List<string>();
            if (!double.IsNaN(u.Charge)) parts.Add(Pct(u.Charge));
            if (!double.IsNaN(u.RuntimeSeconds)) parts.Add(Runtime(u.RuntimeSeconds) + " left");
            if (!double.IsNaN(u.Load)) parts.Add(Pct(u.Load) + " load");
            return string.Join(" · ", parts);
        }
    }
}
