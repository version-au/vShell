using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace vShell.Core
{
    /// <summary>
    /// A saved SSH connection. The terminal is vTerm now (0.38.194): vTerm keeps the saved connections. vShell's settings still carry the
    /// ones saved before (TermConnections), which vTerm brings over the first time it starts.
    /// </summary>
    public sealed class TermConnection
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Folder { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 22;
        public string User { get; set; } = "";
        /// <summary>"Password", "Key" (KeyFile) or "Agent".</summary>
        public string Auth { get; set; } = "Password";
        public string KeyFile { get; set; } = "";
        /// <summary>The password / key passphrase, encrypted (Secret.Protect).</summary>
        public string Secret { get; set; } = "";
        public string Jump { get; set; } = "";
        public string Command { get; set; } = "";
        public string Color { get; set; } = "";
        public string SftpPath { get; set; } = "";
        public string HostKey { get; set; } = "";

        /// <summary>user@host</summary>
        public string Target { get { return (string.IsNullOrWhiteSpace(User) ? "" : User.Trim() + "@") + (Host ?? "").Trim(); } }
        public string Shown { get { return string.IsNullOrWhiteSpace(Name) ? Target : Name; } }

        public void Normalize()
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            Name = Name ?? ""; Folder = (Folder ?? "").Trim(); Host = (Host ?? "").Trim(); User = (User ?? "").Trim();
            if (Port <= 0 || Port > 65535) Port = 22;
            if (Auth != "Key" && Auth != "Agent") Auth = "Password";
            KeyFile = KeyFile ?? ""; Secret = Secret ?? ""; Jump = (Jump ?? "").Trim(); Command = Command ?? ""; Color = Color ?? "";
            SftpPath = SftpPath ?? ""; HostKey = HostKey ?? "";
        }
    }

    /// <summary>The saved SSH connections, as vTerm has them (its settings.json) - vShell's own older list until vTerm has started once.</summary>
    public static class TermSsh
    {
        static List<TermConnection> _last;

        /// <summary>
        /// Every saved connection: vTerm's (vSuite.Core's VTermConnections reads its file, again when it changes - shared with vPad), else
        /// the ones in vShell's settings.
        /// </summary>
        public static List<TermConnection> All(BarSettings s)
        {
            try
            {
                List<TermConnection> list = vSuite.VTermConnections.Read<TermConnection>();
                if (list != null)
                {
                    if (!ReferenceEquals(list, _last))
                    {
                        list.RemoveAll(delegate (TermConnection x) { return string.IsNullOrWhiteSpace(x.Host); });
                        foreach (TermConnection x in list) x.Normalize();
                        _last = list;
                    }
                    return list;
                }
            }
            catch (Exception ex) { Log.Write("vTerm's connections: " + ex.Message); if (_last != null) return _last; }
            return s != null && s.TermConnections != null ? s.TermConnections : new List<TermConnection>();
        }

        /// <summary>A connection for this host already saved (by host / name, case-insensitive), or null.</summary>
        [PluginApi] public static TermConnection Find(BarSettings s, string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return null;
            host = host.Trim();
            List<TermConnection> all = All(s);
            foreach (TermConnection c in all) if (string.Equals(c.Host, host, StringComparison.OrdinalIgnoreCase)) return c;
            foreach (TermConnection c in all) if (string.Equals(c.Name, host, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }
    }
}
