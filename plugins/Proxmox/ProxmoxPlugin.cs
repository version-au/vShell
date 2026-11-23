using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vShell.Core;

[assembly: vShell.Core.VShellPlugin("proxmox", "Proxmox VE", Description = "Your Proxmox cluster with an API token: a module with nodes, VMs / containers and storage (start, stop, Open shell), notifications and palette commands.",
    Glyph = "\uE7F4", Group = "Servers & network", Author = "version", Adds = "notifications, palette commands, !px in the run box, Open shell", Types = "Proxmox")]

namespace vShell.Ui
{
    /// <summary>
    /// The Proxmox VE plugin: the module (nodes, guests, storage, start / stop, Open shell), the connection (an API token), the notifications
    /// and the palette's start / shut down / reboot / stop commands.
    /// </summary>
    public sealed class ProxmoxPlugin : IPlugin
    {
        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = ModuleTypes.Proxmox, Display = "Proxmox VE", ShortName = "Proxmox", Glyph = "", Group = "Smart home & more",
                Blurb = "nodes, VMs, storage", HasPopup = true,
                Needs = delegate (BarSettings s) { return ProxmoxClient.Configured ? null : "Needs the Proxmox connection (Plugins)"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new ProxmoxModule(mgr, h, cfg); },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = WidgetPic
            });
            host.SetUpWhen(delegate (BarSettings s) { return ProxmoxClient.Configured; });
            host.AddIntegration("Proxmox VE", IntegrationSection);
            host.AddNotifications(NotifySection);
            host.OnSettings(delegate (BarSettings s)
            {
                PvSettings p = PvSettings.Of(s);
                ProxmoxClient.Configure(p.Urls, p.TokenId, p.Secret, p.IgnoreCert, p.PollSeconds);
            });
            host.AddNotifyKind("Proxmox.Problem", NotifyLevel.Alert);
            host.AddNotifyKind("Proxmox.Good", NotifyLevel.Normal);
            host.AddNotifySource("Proxmox");
            host.AddWatcher("Proxmox", Watch);
            host.AddCommands("Proxmox", Commands);
            // !px web: the palette's Proxmox commands for "web" (start / shut down / reboot / stop … - Enter twice for the ones that ask)
            host.AddBang("Proxmox", delegate (string key, string q, BarSettings s)
            {
                if (key != PvSettings.Of(s).Bang || !ProxmoxClient.Configured) return null;
                return new RunAction { Kind = "Palette", Query = "Proxmox " + q, Display = "Proxmox: " + (q.Length == 0 ? "every guest" : q) };
            }, delegate { }, delegate (BarSettings s) { return ProxmoxClient.Configured ? "!" + PvSettings.Of(s).Bang + " Proxmox" : null; });
        }

        public void Unload()
        {
            ProxmoxClient.Configure("", "", "", true, 5);      // no connection: polling stops
            _prev = null;
        }

        // =================================================================
        // Settings
        // =================================================================
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            PvSettings c = PvSettings.Of(p.Settings);
            p.Header("Proxmox VE");
            p.Text("Address - any node of the cluster (for example https://192.168.1.10:8006). List more, separated by commas, and the next is used when one is down",
                   delegate { return c.Urls; }, delegate (string v) { c.Urls = v; });
            p.Text("API token ID (for example root@pam!vshell)", delegate { return c.TokenId; }, delegate (string v) { c.TokenId = v; });
            PasswordBox pw = p.Secret("Token secret", "secret", delegate { return c.Secret; }, delegate (string v) { c.Secret = v; },
                "In Proxmox open Datacenter > Permissions > API Tokens > Add: pick a user (for example root@pam), give the token an ID such as vshell and copy the secret - " +
                "it is only shown once. With \"Privilege Separation\" ticked the token needs its own permissions: add them under Datacenter > Permissions > Add > API Token Permission, " +
                "path /, role PVEAuditor to see the cluster, plus role PVEVMUser on /vms to start and stop guests. To also delete guests (the Proxmox module's " +
                "\"Allow deleting VMs and containers\" setting) give it role PVEVMAdmin on /vms instead of PVEVMUser - it includes start / stop and adds " +
                "VM.Allocate. Open shell (SSH) on a VM asks its QEMU guest agent for the address - PVEVMAdmin covers that; with PVEVMUser add a role with " +
                "VM.GuestAgent.Audit (Proxmox 9) or VM.Monitor (Proxmox 8). Test connection tells you what the token can do. The secret is stored encrypted for your Windows account.");
            p.Check("Accept a self-signed HTTPS certificate (Proxmox's own certificate is self-signed)", delegate { return c.IgnoreCert; }, delegate (bool v) { c.IgnoreCert = v; });
            p.Slider("Check every (seconds)", 2, 60, delegate { return c.PollSeconds; }, delegate (double v) { c.PollSeconds = (int)v; });
            p.Text("Run box keyword (without the !)", delegate { return c.Bang; }, delegate (string v) { c.Bang = v; },
                   "!" + c.Bang + " web - start, shut down, reboot or stop the guests whose name has \"web\" in it (the ones that interrupt need Enter twice). " +
                   "!" + c.Bang + " on its own lists them all.");
            p.Test(delegate
            {
                if (pw.Password.Trim().Length > 0) { c.Secret = Secret.Protect(pw.Password.Trim()); pw.Password = ""; p.Changed(); }
            }, delegate { return ProxmoxClient.TestAsync(c.Urls, c.TokenId, c.Secret, c.IgnoreCert); });
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            PvSettings c = PvSettings.Of(p.Settings);
            p.NotifySection("Proxmox notifications", ProxmoxClient.Configured, "Proxmox");
            p.Check("A node goes offline (and comes back), or Proxmox can't be reached", delegate { return c.NotifyNodes; }, delegate (bool v) { c.NotifyNodes = v; p.Redraw(); });
            p.Check("A VM / container stops or starts (not when you did it from vShell)", delegate { return c.NotifyGuests; }, delegate (bool v) { c.NotifyGuests = v; p.Redraw(); });
            if (c.NotifyNodes || c.NotifyGuests) p.Delay(delegate { return c.NotifyDelay; }, delegate (int v) { c.NotifyDelay = v; });
            p.Slider("Storage is fuller than (%) - 0 = off", 0, 100, delegate { return c.NotifyStorage; }, delegate (double v) { c.NotifyStorage = (int)v; });
            p.Kind("Problems (offline, can't be reached, stopped, storage full) show as", "Proxmox.Problem");
            if (c.NotifyNodes || c.NotifyGuests) p.Kind("Good news (back online, started) shows as", "Proxmox.Good");
            p.Note("Notifications work without a Proxmox module too (vShell checks as often as \"Check every\" on the Connection tab).");
        }

        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            PvView v = new PvView(cfg);
            if (!ProxmoxClient.Configured) p.Note("Set up the Proxmox connection first (Plugins page in this window).");
            p.Combo("Show", PvView.Shows, delegate { return v.Show; }, delegate (string s) { v.Show = s; p.Redraw(); });     // the options below depend on it

            // node: the cluster's nodes as last read, plus whatever is set now
            const string all = "All nodes";
            var names = new List<string> { all };
            PveSnapshot snap = ProxmoxClient.Latest;
            if (snap != null) names.AddRange(snap.NodeNames());
            if (v.Node.Length > 0 && !names.Contains(v.Node)) names.Add(v.Node);
            p.Combo("Node", names.ToArray(), delegate { return v.Node.Length == 0 ? all : v.Node; }, delegate (string s) { v.Node = s == all ? "" : s; });

            if (v.Show == "Guests")
            {
                p.Check("Hide stopped guests" + (p.IsWidget ? "" : " - the bar counts them either way"), delegate { return v.RunningOnly; }, delegate (bool on) { v.RunningOnly = on; });
                p.Check("Show templates", delegate { return v.ShowTemplates; }, delegate (bool on) { v.ShowTemplates = on; });
                if (p.IsWidget)
                    p.Check("Show each guest's CPU and memory", delegate { return v.GuestStats; }, delegate (bool on) { v.GuestStats = on; });
                p.Check("Show each running guest's uptime", delegate { return v.GuestUptime; }, delegate (bool on) { v.GuestUptime = on; });
            }
            else
                p.Combo("Style", PvView.Styles, delegate { return v.Style; }, delegate (string s) { v.Style = s; });
            p.Check("Allow start, shut down, reboot and stop from vShell", delegate { return v.AllowPower; }, delegate (bool on) { v.AllowPower = on; });
            p.Check("Allow deleting VMs and containers from vShell", delegate { return v.AllowDelete; }, delegate (bool on) { v.AllowDelete = on; p.Redraw(); });
            if (v.AllowDelete)
                p.Note("Delete… shows in a guest's menu when it is stopped. vShell asks you to type the guest's ID first, and can also remove it from " +
                       "backup jobs, replication and HA and delete its unattached disks (as Proxmox's own Remove does). Deleting can't be undone. " +
                       "The API token needs the VM.Allocate permission (for example the PVEVMAdmin role) on /vms.");
            if (!p.IsWidget) p.Title(cfg, "");

            string how = p.IsWidget
                ? "Click a guest (in Guests, or in the popup) for Start / Shut down / Reboot / Stop - vShell asks before anything but Start."
                : "Click the module for the whole cluster: nodes, every guest and storage. Click a guest there for Start / Shut down / Reboot / Stop - vShell asks before anything but Start.";
            p.Note("Nodes shows CPU (Main Accent) and memory (Secondary Accent); Storage shows how full each pool is (a shared pool is shown once). " + how +
                   " Starting and stopping needs a token with the PVEVMUser role (VM.PowerMgmt), deleting the PVEVMAdmin role (VM.Allocate) - Test connection " +
                   "on the Plugins page tells you. Right-click a node's name for Open shell (an SSH tab in the terminal). A running guest's menu has " +
                   "Open shell (SSH): a container's opens its console (pct enter, through its node); a VM's uses its saved connection by name, else its " +
                   "address from the QEMU guest agent. Without a saved connection vShell offers to make one.");
        }

        // ---- Add a module pictures ----
        static FrameworkElement BarPic(Theme t)
        {
            Brush fg = t.Fg;
            double fs = 12.5;
            return ModulePics.Row(6, ModulePics.Dot(StatusColors.Good, 9), ModulePics.T(t, "12/14 running", fg, fs), ModulePics.T(t, "⚠ 1", StatusColors.Bad, fs));
        }

        static FrameworkElement WidgetPic(Theme t)
        {
            Brush fg = t.Fg, dim = t.FgDim, acc = t.MainAccent;
            return ModulePics.Col(6, ModulePics.Row(6, ModulePics.Dot(StatusColors.Good, 7), ModulePics.T(t, "pve1", fg, 12, true)), ModulePics.Row(6, ModulePics.T(t, "CPU", dim, 11), ModulePics.Meter(t, 120, 6, 0.34, acc)), ModulePics.Row(6, ModulePics.T(t, "RAM", dim, 11), ModulePics.Meter(t, 120, 6, 0.71, StatusColors.Warn)),
                                  ModulePics.Row(6, ModulePics.Dot(StatusColors.Good, 7), ModulePics.T(t, "pve2", fg, 12, true)), ModulePics.Row(6, ModulePics.T(t, "CPU", dim, 11), ModulePics.Meter(t, 120, 6, 0.12, acc)));
        }

        // =================================================================
        // Notifications: nodes offline / back, guests stopped / started (not by vShell), storage full
        // =================================================================
        static PveSnapshot _prev;
        static readonly HashSet<string> Full = new HashSet<string>();

        static void Watch(BarSettings s)
        {
            PvSettings c = PvSettings.Of(s);
            if (!ProxmoxClient.Configured || !(c.NotifyNodes || c.NotifyGuests || c.NotifyStorage > 0)) { _prev = null; return; }
            PveSnapshot now = ProxmoxClient.Get();        // keeps the polling going
            if (now == null) return;
            string web = ProxmoxClient.WebUrl;
            int wait = c.NotifyDelay;
            if (now.Error.Length > 0)
            {
                if (now.Fetched != DateTime.MinValue && c.NotifyNodes)
                {
                    string err = now.Error;
                    DownDelay.Down("pve|reach", wait, delegate { Notify.Post("Proxmox", "Proxmox can't be reached", err, web, Notify.KindOf("Proxmox.Problem")); });
                }
                return;
            }
            if (DownDelay.Up("pve|reach") == UpResult.WasNotified && c.NotifyNodes)
                Notify.Post("Proxmox", "Proxmox can be reached again", now.ClusterName.Length > 0 ? now.ClusterName : web, web, Notify.KindOf("Proxmox.Good"));
            PveSnapshot prev = _prev;
            if (prev != null && prev.Fetched == now.Fetched) return;
            _prev = now;
            // each node's name and address: other apps' notifications about it join up
            foreach (PveResource x in now.Items) if (x.Type == "node" && x.Name.Length > 0 && x.Ip.Length > 0) Notify.SameThing("Proxmox", x.Name, x.Ip);
            if (prev == null) { foreach (PveResource x in now.Storage(null)) if (c.NotifyStorage > 0 && x.DiskFraction * 100 >= c.NotifyStorage) Full.Add(x.Id); return; }

            var before = new Dictionary<string, PveResource>();
            foreach (PveResource x in prev.Items) before[x.Id] = x;
            foreach (PveResource x in now.Items)
            {
                PveResource b;
                if (!before.TryGetValue(x.Id, out b)) continue;
                if (x.Type == "node" && c.NotifyNodes && b.Status != x.Status)
                {
                    string cluster = now.ClusterName, nodeName = x.Name, nodeIp = x.Ip;
                    if (b.Running && !x.Running)
                        DownDelay.Down("pve|" + x.Id, wait, delegate { Notify.Post("Proxmox", "Node " + nodeName + " is offline", cluster, web, Notify.KindOf("Proxmox.Problem"), NotifyAbout.Down(nodeName, nodeIp)); });
                    else if (!b.Running && x.Running && DownDelay.Up("pve|" + x.Id) != UpResult.Cancelled)
                        Notify.Post("Proxmox", "Node " + nodeName + " is back online", cluster, web, Notify.KindOf("Proxmox.Good"), NotifyAbout.Up(nodeName, nodeIp));
                }
                else if (x.IsGuest && !x.Template && c.NotifyGuests && b.Status != x.Status)
                {
                    DateTime by;
                    bool mine;
                    lock (ProxmoxClient.ByVShell) mine = ProxmoxClient.ByVShell.TryGetValue(x.Id, out by) && (DateTime.UtcNow - by).TotalMinutes < 5;
                    if (mine) continue;
                    string what = x.Kind + " " + x.Label, where = "on " + x.Node, guest = x.Name.Length > 0 ? x.Name : x.Label;
                    if (b.Status == "running" && x.Status == "stopped")
                        DownDelay.Down("pve|" + x.Id, wait, delegate { Notify.Post("Proxmox", what + " stopped", where, web, Notify.KindOf("Proxmox.Problem"), NotifyAbout.Down(guest)); });
                    else if (b.Status != "running" && x.Status == "running" && DownDelay.Up("pve|" + x.Id) != UpResult.Cancelled)
                        Notify.Post("Proxmox", what + " started", where, web, Notify.KindOf("Proxmox.Good"), NotifyAbout.Up(guest));     // a quick restart says nothing
                }
                else if (x.Type == "storage" && c.NotifyStorage > 0)
                {
                    double pct = x.DiskFraction * 100;
                    if (pct >= c.NotifyStorage && Full.Add(x.Id))
                        Notify.Post("Proxmox", "Storage " + x.Name + " is " + Math.Round(pct) + " % full", "on " + x.Node + " · " + ProxmoxClient.Bytes(x.MaxDisk - x.Disk) + " free", web, Notify.KindOf("Proxmox.Problem"));
                    else if (pct < c.NotifyStorage - 2) Full.Remove(x.Id);
                }
            }
        }

        // =================================================================
        // The command palette: start / shut down / reboot / stop VMs and containers
        // =================================================================
        static void Commands(BarSettings s, List<PaletteCommand> into)
        {
            if (!ProxmoxClient.Configured) return;
            PveSnapshot snap = ProxmoxClient.Get();
            if (snap == null) return;
            const string Src = "Proxmox";
            foreach (PveResource g0 in snap.Guests("", false, false))
            {
                PveResource g = g0;
                string what = g.Kind + " " + g.Label, detail = Src + " · " + g.Node + " · " + g.Status;
                string words = g.VmId + " " + g.Name + " " + g.Node + " " + (g.Type == "lxc" ? "container lxc ct" : "vm virtual machine qemu") + " guest";
                Func<string, Func<double, Task<string>>> power = delegate (string action) { return delegate { return ProxmoxClient.PowerAsync(g, action); }; };
                if (g.Status == "running")
                {
                    into.Add(new PaletteCommand { Title = "Shut down " + what, Detail = detail, Glyph = "", Source = Src, Words = words + " shutdown stop power off", Ask = true, Run = power("shutdown") });
                    into.Add(new PaletteCommand { Title = "Reboot " + what, Detail = detail, Glyph = "", Source = Src, Words = words + " restart", Ask = true, Run = power("reboot") });
                    into.Add(new PaletteCommand { Title = "Stop " + what + " (pull the plug)", Detail = detail, Glyph = "", Source = Src, Words = words + " kill force", Ask = true, Run = power("stop") });
                    if (g.Type == "qemu")
                        into.Add(new PaletteCommand { Title = "Suspend " + what, Detail = detail, Glyph = "", Source = Src, Words = words + " pause", Run = power("suspend") });
                }
                else if (g.Status == "paused" || g.Status == "suspended")
                    into.Add(new PaletteCommand { Title = "Resume " + what, Detail = detail, Glyph = "", Source = Src, Words = words + " unpause continue", Run = power("resume") });
                else
                    into.Add(new PaletteCommand { Title = "Start " + what, Detail = detail, Glyph = "", Source = Src, Words = words + " boot power on turn on", Boost = 1, Run = power("start") });
            }
        }
    }
}
