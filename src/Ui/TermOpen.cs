using System;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Was vShell's built-in terminal view (until 0.38.194 - the terminal is vTerm now). Kept, empty, only so that TermOpen.Terminal's
    /// plugin API line stays the same; nothing makes one.
    /// </summary>
    public sealed class TerminalView
    {
        TerminalView() { }
    }

    /// <summary>
    /// "Open shell" from other modules (a Proxmox node, a Komodo server or container, Linux Updates' hosts): an SSH tab in vTerm's
    /// drop-down from the first bar with a Terminal module (or the first bar). vTerm uses its saved connection for that host (by host,
    /// then name); with none, it offers to make one (filled in) or connect without saving.
    /// </summary>
    [PluginApi]
    public static class TermOpen
    {
        /// <summary>Shows vTerm's drop-down (since 0.38.194 the terminal is vTerm, in another process: there's no terminal to hand back - null).</summary>
        public static TerminalView Terminal(BarManager mgr)
        {
            VTerm.Send(mgr, VTerm.DropForFirst(mgr, "show"));
            return null;
        }

        /// <summary>
        /// An SSH tab to this host (name = what to call a new connection, user = its default user); remote = a command to run there
        /// (null = the login shell).
        /// </summary>
        public static void Host(BarManager mgr, string host, string name, string user, string remote)
        {
            Host(mgr, host, name, user, remote, null);
        }

        /// <summary>As above; why = the reason there's no address (shown when there's no host and no saved connection by that name).</summary>
        public static void Host(BarManager mgr, string host, string name, string user, string remote, string why)
        {
            vSuite.TermRequest r = VTerm.DropForFirst(mgr, "open-host");
            r.Host = host ?? "";
            r.Name = name;
            r.User = user;
            r.Remote = remote;
            r.Why = why;
            VTerm.Send(mgr, r);
        }

        /// <summary>The command for a shell inside a Docker container (bash when it has it, else sh).</summary>
        public static string DockerShell(string container)
        {
            string n = (container ?? "").Replace("'", "");
            return "docker exec -it " + n + " sh -c 'command -v bash >/dev/null && exec bash || exec sh'";
        }
    }
}
