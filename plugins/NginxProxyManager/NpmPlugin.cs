using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using vShell.Core;
using vShell.Ui;

[assembly: vShell.Core.VShellPlugin("npm", "Nginx Proxy Manager", Description = "Your Nginx Proxy Manager: its proxy hosts on a bar, notifications for certificates running out, config errors, backends down and new versions, and !npm in the run box.",
    Glyph = "", Group = "Servers & network", Author = "version", Adds = "certificate / host / backend notifications, !npm in the run box", Types = "NginxProxyManager")]

namespace vShell.Npm
{
    /// <summary>
    /// The Nginx Proxy Manager plugin: the module, the Plugins and Notifications sections, the notifications (a click renews a certificate
    /// or asks for the 2FA code), and !npm in the run box (open, enable / disable, renew).
    /// </summary>
    public sealed class NpmPlugin : IPlugin
    {
        public const string Type = "NginxProxyManager";
        public const string Source = "Nginx Proxy Manager";
        public const string Glyph = "";
        const string RenewLink = "npm:renew:", CodeLink = "npm:code";
        static BarSettings _s;
        Action<NpmSnapshot> _changed;
        static RunPanel _box;

        public void Load(PluginHost host)
        {
            BarManager mgr = host.Manager;
            host.AddModule(new PluginModule
            {
                Type = Type, Display = "Nginx Proxy Manager", ShortName = "NPM", Glyph = Glyph, Group = "Smart home & more",
                Blurb = "proxy hosts, certificates", HasPopup = true,
                Needs = delegate (BarSettings s) { return NpmClient.Configured(s) ? null : "Needs the Nginx Proxy Manager connection (Plugins)"; },
                Create = delegate (BarManager m, IModuleHost h, ModuleConfig cfg) { return new NpmModule(m, h, cfg); },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = BarPic
            });
            host.SetUpWhen(NpmClient.Configured);
            host.AddIntegration("Nginx Proxy Manager", IntegrationSection);
            host.AddNotifications(NotifySection);
            NpmClient.Save = mgr.SaveSoon;
            host.OnSettings(delegate (BarSettings s) { _s = s; NpmClient.Configure(s); });

            host.AddNotifyKind("NPM.Cert", NotifyLevel.Alert);
            host.AddNotifyKind("NPM.Error", NotifyLevel.Important);
            host.AddNotifyKind("NPM.Down", NotifyLevel.Important);
            host.AddNotifyKind("NPM.Update", NotifyLevel.Normal);
            host.AddNotifyKind("NPM.SignIn", NotifyLevel.Important);
            host.AddNotifySource(Source);
            host.AddCardLinks(delegate (string url)
            {
                if (url == CodeLink) { AskCode(); return true; }
                long id;
                if (url != null && url.StartsWith(RenewLink) && long.TryParse(url.Substring(RenewLink.Length), out id)) { Renew(id); return true; }
                return false;
            });

            host.AddBang(Type, delegate (string key, string q, BarSettings s)
            {
                if (!NpmClient.Configured(s) || key != NpmSettings.Of(s).Bang) return null;
                return new RunAction { Kind = Type, Query = q, Display = "Nginx Proxy Manager: your proxy hosts" };
            }, Rows, delegate (BarSettings s) { return NpmClient.Configured(s) ? "!" + NpmSettings.Of(s).Bang + " Nginx Proxy Manager" : null; });

            _changed = delegate (NpmSnapshot snap)
            {
                Check(snap);
                RunPanel box = _box;
                if (box != null && _s != null && (box.Text ?? "").StartsWith("!" + NpmSettings.Of(_s).Bang)) box.Redraw();
            };
            NpmClient.Changed += _changed;
        }

        public void Unload()
        {
            if (_changed != null) NpmClient.Changed -= _changed;
            NpmClient.Stop();
            NpmClient.Save = null;
            _box = null;
        }

        // =================================================================
        // Notifications: everything that applies now gets a key; a key that wasn't there last time is said. Keys are kept in the
        // settings, so a restart doesn't say them again.
        // =================================================================
        static void Check(NpmSnapshot snap)
        {
            if (_s == null) return;
            NpmSettings c = NpmSettings.Of(_s);
            var said = new HashSet<string>(c.Said.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
            var now = new HashSet<string>();
            Action<string, string, string, string, string> say = delegate (string key, string kind, string title, string detail, string url)
            {
                now.Add(key);
                if (!said.Contains(key)) Notify.Post(Source, title, detail, url, Notify.KindOf(kind));
            };

            if (snap.NeedCode || !snap.Ok || snap.Error.Length > 0)
            {
                // nothing new to go on: keep what was said
                foreach (string k in said) if (k != "code") now.Add(k);
                if (snap.NeedCode)
                    say("code", "NPM.SignIn", "Nginx Proxy Manager needs your 2FA code", "Click to enter it - until then vShell can't check your hosts and certificates.", CodeLink);
                Save(c, now);
                return;
            }

            if (c.NotifyCerts)
                foreach (NpmCert cert in snap.Certs)
                {
                    double days = cert.DaysLeft;
                    if (days >= c.CertDays || (c.OnlyUsedCerts && !snap.Used(cert))) continue;
                    string stamp = cert.Id + ":" + cert.Expires.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    string what = cert.LetsEncrypt ? "Click to renew it now." : "It's a certificate you uploaded - upload a new one in Nginx Proxy Manager.";
                    string url = cert.LetsEncrypt ? RenewLink + cert.Id : NpmClient.AdminUrl(_s, "/nginx/certificates");
                    // said once when it's getting close, and once more when it has run out
                    if (days < 0) say("certgone:" + stamp, "NPM.Cert", "Certificate expired: " + cert.Name, "Its sites show a certificate error now. " + what, url);
                    else say("cert:" + stamp, "NPM.Cert", "Certificate for " + cert.Name + ": " + cert.DaysText,
                             (cert.LetsEncrypt ? "Nginx Proxy Manager should have renewed it by now - it may be failing. " : "") + what, url);
                }

            if (c.NotifyErrors)
                foreach (NpmHost h in snap.Hosts)
                    if (h.Enabled && !h.Online)
                        say("nginx:" + h.Id + ":" + h.Modified, "NPM.Error", h.Domain + " has a config error",
                            h.NginxErr.Length > 0 ? h.NginxErr : "nginx couldn't load it, so the site isn't served.", NpmClient.AdminUrl(_s, "/nginx/proxy"));

            if (c.CheckBackends && c.NotifyDown)
            {
                foreach (NpmHost h in snap.Hosts)
                    if (h.Enabled && h.Backend == Backend.Down)
                        say("down:" + h.Id, "NPM.Down", h.Domain + ": its backend is down",
                            "Nothing answers on " + h.ForwardHost + ":" + h.ForwardPort + " (checked from this PC).", h.SiteUrl);
                foreach (string k in said)
                {
                    long id;
                    if (!k.StartsWith("down:") || now.Contains(k) || !long.TryParse(k.Substring(5), out id)) continue;
                    foreach (NpmHost h in snap.Hosts)
                        if (h.Id == id && h.Backend == Backend.Up)
                            Notify.Post(Source, h.Domain + " is back up", h.ForwardHost + ":" + h.ForwardPort + " answers again.", h.SiteUrl, Notify.KindOf("NPM.Down"));
                }
            }

            if (c.NotifyUpdate && snap.Update && snap.Latest.Length > 0)
                say("update:" + snap.Latest, "NPM.Update", "Nginx Proxy Manager " + snap.Latest + " is out",
                    "You have " + (snap.Current.Length > 0 ? snap.Current : "an older one") + ".", "https://github.com/NginxProxyManager/nginx-proxy-manager/releases");

            Save(c, now);
        }

        static void Save(NpmSettings c, HashSet<string> now)
        {
            var list = new List<string>(now);
            list.Sort(StringComparer.Ordinal);
            string joined = string.Join("\n", list);
            if (joined == c.Said) return;
            c.Said = joined;
            if (NpmClient.Save != null) NpmClient.Save();
        }

        // =================================================================
        // Actions (from notifications, the popup and the run box)
        // =================================================================
        /// <summary>Ask for the 2FA code and sign in with it.</summary>
        public static async void AskCode()
        {
            if (_s == null) return;
            string code = PromptWindow.Ask(null, "Nginx Proxy Manager", "Your 2FA code (from your authenticator app)", "");
            if (string.IsNullOrWhiteSpace(code)) return;
            string err = await NpmClient.EnterCodeAsync(_s, code);
            if (err != null) Notify.Post(Source, "Nginx Proxy Manager: signing in didn't work", err + " - click to try again.", CodeLink, Notify.KindOf("NPM.SignIn"));
        }

        /// <summary>Renew a certificate (asks first).</summary>
        public static async void Renew(long certId)
        {
            if (_s == null) return;
            NpmCert cert = NpmClient.Latest.Cert(certId);
            string name = cert != null ? cert.Name : "certificate " + certId;
            if (Questions.YesNo(null, "Renew the certificate for " + name + " now? Let's Encrypt can take a minute.", "Renew certificate") != MessageBoxResult.Yes) return;
            string err = await NpmClient.RenewAsync(_s, certId);
            if (err == null) Notify.Post(Source, "Certificate renewed: " + name, "Let's Encrypt issued a new one.", NpmClient.AdminUrl(_s, "/nginx/certificates"), NotifyLevel.Normal);
            else Notify.Post(Source, "Renewing " + name + " didn't work", err, NpmClient.AdminUrl(_s, "/nginx/certificates"), Notify.KindOf("NPM.Cert"));
        }

        // ---- Add / edit a host ----
        const string NoCert = "None (http only)", NewCert = "New Let's Encrypt certificate", CertPrefix = "Existing: ", Public = "Publicly accessible";

        /// <summary>
        /// The host form: empty to add one (domain = something to start it with), or filled in from a host to change it. Returns the
        /// values, or null when cancelled (or Nginx Proxy Manager can't be reached).
        /// </summary>
        static NewHost HostForm(NpmSnapshot snap, NpmHost edit, string domain)
        {
            string title = edit == null ? "Add a proxy host" : "Edit " + edit.Domain;
            if (snap.NeedCode) { AskCode(); return null; }
            if (!snap.Ok)
            {
                Questions.Tell(null, "vShell hasn't reached Nginx Proxy Manager yet" + (snap.Error.Length > 0 ? ": " + snap.Error : "") + ". Try again in a moment.", title);
                return null;
            }

            var certs = new List<string> { NewCert, NoCert };
            var certIds = new List<long> { -1, 0 };
            foreach (NpmCert c in snap.Certs)
            {
                if (c.DaysLeft < 0 && (edit == null || c.Id != edit.CertId)) continue;     // an expired one only if it's this host's
                certs.Add(CertPrefix + c.Name + " (" + c.DaysText + ")");
                certIds.Add(c.Id);
            }
            var lists = new List<string> { Public };
            string list = Public;
            foreach (NpmAccessList l in snap.AccessLists)
            {
                lists.Add(l.Name);
                if (edit != null && l.Id == edit.AccessListId) list = l.Name;
            }
            string cert = NewCert;
            if (edit != null) cert = edit.CertId > 0 && certIds.Contains(edit.CertId) ? certs[certIds.IndexOf(edit.CertId)] : NoCert;

            var fDomains = new FormField("Domain names", edit != null ? string.Join(", ", edit.Domains) : (domain ?? "").Trim())
            {
                Hint = "For example photos.example.com - more than one: separate them with commas. They must already point at Nginx Proxy Manager for a Let's Encrypt certificate."
            };
            var fScheme = FormField.Combo("Scheme", edit != null ? edit.ForwardScheme : "http", new[] { "http", "https" });
            var fHost = new FormField("Forward host name / IP", edit != null ? edit.ForwardHost : "") { Hint = "Where the app runs, as Nginx Proxy Manager sees it: 192.168.2.50, or a Docker container name on its network." };
            var fPort = new FormField("Forward port", edit != null ? edit.ForwardPort.ToString(CultureInfo.InvariantCulture) : "");
            var fCert = FormField.Combo("SSL certificate", cert, certs.ToArray());
            if (edit != null && edit.CertId > 0) fCert.Hint = "A new Let's Encrypt certificate replaces this one once it's issued; until then the host keeps it.";
            var fForce = FormField.Check("Force SSL (send http to https)", edit == null || edit.SslForced);
            var fHttp2 = FormField.Check("HTTP/2 support", edit == null || edit.Http2);
            var fBlock = FormField.Check("Block common exploits", edit == null || edit.BlockExploits);
            var fWs = FormField.Check("Websockets support", edit == null || edit.Websockets);
            var fList = FormField.Combo("Access list", list, lists.ToArray());
            var fields = new List<FormField> { fDomains, fScheme, fHost, fPort, fCert, fForce, fHttp2, fBlock, fWs, fList };

            for (bool first = true; ; first = false)
            {
                // adding, with a domain given that an existing certificate covers (a wildcard): offer that one
                if (first && edit == null && fDomains.Value.Length > 0)
                    foreach (NpmCert c in snap.Certs)
                        if (c.DaysLeft >= 0 && Covers(c, Domains(fDomains.Value))) { fCert.Value = certs[certIds.IndexOf(c.Id)]; break; }
                if (!FormWindow.Ask(null, title, fields)) return null;
                string err;
                NewHost n = Read(snap, edit, fields, certs, certIds, out err);
                if (err == null) return n;
                if (Questions.YesNo(null, err + "\n\nChange it?", title) != MessageBoxResult.Yes) return null;
            }
        }

        /// <summary>Edit a host: the form filled in from it, then saving it (and a new certificate when asked for).</summary>
        public static async void EditHost(long hostId)
        {
            if (_s == null) return;
            BarSettings S = _s;
            NpmSnapshot snap = NpmClient.Latest;
            NpmHost h = null;
            foreach (NpmHost x in snap.Hosts) if (x.Id == hostId) h = x;
            if (h == null) return;
            NewHost n = HostForm(snap, h, null);
            if (n == null) return;
            string name = n.Domains[0];
            if (n.CertId == -1)
                Notify.Post(Source, "Saving " + name + "…", "Then asking Let's Encrypt for its certificate - that can take a minute.", null, NotifyLevel.Normal, false);
            Tuple<string, string> r = await NpmClient.UpdateHostAsync(S, h, n);
            if (r.Item1 != null)
                Notify.Post(Source, "Saving " + name + " didn't work", r.Item1, NpmClient.AdminUrl(S, "/nginx/proxy"), NotifyLevel.Important);
            else if (r.Item2 != null)
                Notify.Post(Source, "Saved " + name + " - but no new certificate",
                            "Let's Encrypt said: " + r.Item2 + ". " + (h.CertId > 0 ? "It keeps its old certificate." : "It's served over http for now.") +
                            " Check the domain points at Nginx Proxy Manager (ports 80 and 443 forwarded).", NpmClient.AdminUrl(S, "/nginx/proxy"), NotifyLevel.Important);
            else if (n.CertId == -1)
                Notify.Post(Source, "Saved " + name, "With a new certificate.", "https://" + name, NotifyLevel.Normal);
        }

        /// <summary>The Add a host form, then adding it (and its certificate). domain = something to start the form with.</summary>
        public static async void AddHost(string domain)
        {
            if (_s == null) return;
            BarSettings S = _s;
            NewHost n = HostForm(NpmClient.Latest, null, domain);
            if (n == null) return;

            string name = n.Domains[0];
            if (n.CertId == -1)
                Notify.Post(Source, "Adding " + name + "…", "Then asking Let's Encrypt for its certificate - that can take a minute.", null, NotifyLevel.Normal, false);
            Tuple<long, string, string> r = await NpmClient.AddHostAsync(S, n);
            string site = (n.CertId != 0 && r.Item3 == null ? "https://" : "http://") + name;
            if (r.Item2 != null)
                Notify.Post(Source, "Adding " + name + " didn't work", r.Item2, NpmClient.AdminUrl(S, "/nginx/proxy"), NotifyLevel.Important);
            else if (r.Item3 != null)
                Notify.Post(Source, "Added " + name + " - but without a certificate",
                            "Let's Encrypt said: " + r.Item3 + ". It's served over http for now. Check the domain points at Nginx Proxy Manager (ports 80 and 443 " +
                            "forwarded), then right-click it in the list > Get a Let's Encrypt certificate.", site, NotifyLevel.Important);
            else
                Notify.Post(Source, "Added " + name, "→ " + n.Scheme + "://" + n.Host + ":" + n.Port + (n.CertId != 0 ? " with SSL" : "") + ". Click to open it.", site, NotifyLevel.Normal);
        }

        /// <summary>The form's values as a host, or why they won't do (edit = the host being changed, whose own domains are fine).</summary>
        static NewHost Read(NpmSnapshot snap, NpmHost edit, List<FormField> f, List<string> certs, List<long> certIds, out string err)
        {
            err = null;
            var n = new NewHost { Domains = Domains(f[0].Value), Scheme = f[1].Value == "https" ? "https" : "http", Host = (f[2].Value ?? "").Trim() };
            if (n.Domains.Count == 0) { err = "Enter a domain name."; return n; }
            foreach (string d in n.Domains)
            {
                if (d.IndexOf('.') < 0 || d.IndexOfAny("&| @!#%^();:/\\}{=+?<>,~`'\"".ToCharArray()) >= 0) { err = "\"" + d + "\" isn't a domain name."; return n; }
                foreach (NpmHost h in snap.Hosts)
                    if (edit == null || h.Id != edit.Id)
                        foreach (string hd in h.Domains)
                        if (string.Equals(hd, d, StringComparison.OrdinalIgnoreCase)) { err = d + " is already on a proxy host."; return n; }
            }
            if (n.Host.Length == 0 || n.Host.IndexOf("://") >= 0 || n.Host.IndexOf(' ') >= 0) { err = "Enter the forward host as a name or IP only (no http://)."; return n; }
            int port;
            if (!int.TryParse((f[3].Value ?? "").Trim(), out port) || port < 1 || port > 65535) { err = "The forward port is a number from 1 to 65535."; return n; }
            n.Port = port;
            int ci = certs.IndexOf(f[4].Value);
            n.CertId = ci >= 0 ? certIds[ci] : 0;
            n.ForceSsl = f[5].Checked; n.Http2 = f[6].Checked; n.BlockExploits = f[7].Checked; n.Websockets = f[8].Checked;
            n.AccessListId = 0;
            foreach (NpmAccessList l in snap.AccessLists) if (l.Name == f[9].Value) n.AccessListId = l.Id;
            return n;
        }

        static List<string> Domains(string text)
        {
            var l = new List<string>();
            foreach (string part in (text ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string d = part.Trim().ToLowerInvariant();
                if (d.StartsWith("https://")) d = d.Substring(8);
                if (d.StartsWith("http://")) d = d.Substring(7);
                d = d.TrimEnd('/');
                if (d.Length > 0 && !l.Contains(d)) l.Add(d);
            }
            return l;
        }

        /// <summary>Does the certificate cover every domain (exactly, or a *.example.com one level down)?</summary>
        static bool Covers(NpmCert c, List<string> domains)
        {
            if (domains.Count == 0) return false;
            foreach (string d in domains)
            {
                bool ok = false;
                foreach (string cd in c.Domains)
                {
                    string x = cd.ToLowerInvariant();
                    if (x == d) ok = true;
                    else if (x.StartsWith("*.") && d.EndsWith(x.Substring(1)) && d.Substring(0, d.Length - x.Length + 1).IndexOf('.') < 0) ok = true;
                }
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Get a Let's Encrypt certificate for a host that has none.</summary>
        public static async void RequestCert(long hostId)
        {
            if (_s == null) return;
            NpmHost h = null;
            foreach (NpmHost x in NpmClient.Latest.Hosts) if (x.Id == hostId) h = x;
            if (h == null) return;
            if (Questions.YesNo(null, "Ask Let's Encrypt for a certificate for " + string.Join(", ", h.Domains) +
                                "? The domain must point at Nginx Proxy Manager. It can take a minute.", "Get a certificate") != MessageBoxResult.Yes) return;
            string err = await NpmClient.RequestCertAsync(_s, h.Id, h.Domains, true, true);
            if (err == null) Notify.Post(Source, h.Domain + " has a certificate now", "Served over https (http is sent to https).", "https://" + h.Domain, NotifyLevel.Normal);
            else Notify.Post(Source, "No certificate for " + h.Domain, "Let's Encrypt said: " + err, NpmClient.AdminUrl(_s, "/nginx/certificates"), NotifyLevel.Important);
        }

        /// <summary>
        /// Delete a host (asks first). When its certificate isn't used by any other host, offers to delete that too. Returns null when it
        /// worked, "" when you said no, otherwise why it didn't work.
        /// </summary>
        internal static async Task<string> Delete(NpmHost h)
        {
            if (_s == null) return "Not set up";
            BarSettings S = _s;
            if (Questions.YesNo(null, "Delete " + string.Join(", ", h.Domains) + " (→ " + h.Target + ")?\n\n" +
                                "It stops being served, and its settings are gone - this can't be undone.", "Delete proxy host", MessageBoxImage.Warning) != MessageBoxResult.Yes) return "";
            NpmSnapshot snap = NpmClient.Latest;
            NpmCert cert = snap.Cert(h.CertId);
            bool shared = false;
            if (cert != null)
                foreach (NpmHost o in snap.Hosts) if (o.Id != h.Id && o.CertId == cert.Id) shared = true;
            bool alsoCert = cert != null && !shared &&
                            Questions.YesNo(null, "No other host uses its certificate (" + cert.Name + "). Delete the certificate too?", "Delete proxy host") == MessageBoxResult.Yes;

            string err = await NpmClient.DeleteHostAsync(S, h);
            if (err != null) return err;
            if (alsoCert)
            {
                string certErr = await NpmClient.DeleteCertAsync(S, cert.Id);
                if (certErr != null)
                    Notify.Post(Source, "Deleted " + h.Domain + " - but not its certificate", certErr, NpmClient.AdminUrl(S, "/nginx/certificates"), NotifyLevel.Important);
            }
            return null;
        }

        /// <summary>Turn a host on or off (asks first when turning it off).</summary>
        internal static async Task<string> Enable(NpmHost h, bool on)
        {
            if (_s == null) return "Not set up";
            if (!on && Questions.YesNo(null, "Turn off " + h.Domain + "? It stops being served until you turn it on again.", "Disable proxy host") != MessageBoxResult.Yes) return "";
            return await NpmClient.EnableAsync(_s, h, on);
        }

        // =================================================================
        // Settings
        // =================================================================
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            NpmSettings c = NpmSettings.Of(S);
            p.Header("Nginx Proxy Manager");
            p.Text("Address of the admin page (for example http://192.168.2.90:81)", delegate { return c.Url; }, delegate (string v) { c.Url = v; });
            p.Text("Email you sign in with", delegate { return c.Email; }, delegate (string v) { c.Email = v; });
            PasswordBox pw = p.Secret("Password", "Password", delegate { return c.Password; }, delegate (string v) { c.Password = v; },
                                      "Nginx Proxy Manager has no API keys, so vShell signs in as you. A user of its own (Users > Add User) is safer. Stored encrypted for your Windows account.");
            p.Check("Accept a self-signed HTTPS certificate", delegate { return c.IgnoreCert; }, delegate (bool v) { c.IgnoreCert = v; });
            p.Text("Run box keyword (without the !)", delegate { return c.Bang; }, delegate (string v) { c.Bang = v; },
                   "!" + c.Bang + " in a run box lists your hosts (Enter opens one). Type edit, disable, enable, renew or delete before a name for those, " +
                   "or add (and a domain) to add a host.");
            p.Test(delegate
            {
                if (pw.Password.Length > 0) { c.Password = pw.Password; pw.Password = ""; p.Changed(); }
            }, delegate { return NpmClient.TestAsync(S); });
            p.Add(p.Btn("Enter 2FA code…", delegate { _s = S; AskCode(); }));
            p.Note("Only if your account uses 2FA. vShell keeps the session and renews it, so it asks again only after it couldn't for longer " +
                   "than a session lasts (about a day) - when vShell wasn't running, for example.");
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            NpmSettings c = NpmSettings.Of(S);
            p.NotifySection("Nginx Proxy Manager notifications", NpmClient.Configured(S), "Nginx Proxy Manager");
            p.Check("A certificate is running out", delegate { return c.NotifyCerts; }, delegate (bool v) { c.NotifyCerts = v; p.Redraw(); });
            if (c.NotifyCerts)
            {
                p.Number("Days before it runs out", delegate { return c.CertDays; }, delegate (double v) { c.CertDays = v; });
                p.Check("Only certificates a host uses", delegate { return c.OnlyUsedCerts; }, delegate (bool v) { c.OnlyUsedCerts = v; });
            }
            p.Kind("Certificates show as", "NPM.Cert");
            p.Note("Let's Encrypt certificates renew themselves 30 days before they run out, so one inside 14 days usually means renewing fails. " +
                   "Click the notification to renew it now. Said once, and again when it has run out.");
            p.Check("A host has a config error (nginx couldn't load it)", delegate { return c.NotifyErrors; }, delegate (bool v) { c.NotifyErrors = v; });
            p.Kind("Config errors show as", "NPM.Error");
            p.Check("Check each host's backend from this PC", delegate { return c.CheckBackends; }, delegate (bool v) { c.CheckBackends = v; p.Redraw(); });
            if (c.CheckBackends)
                p.Check("A backend goes down or comes back", delegate { return c.NotifyDown; }, delegate (bool v) { c.NotifyDown = v; });
            p.Kind("Backends show as", "NPM.Down");
            p.Note("vShell connects to each enabled host's forward host and port. Down after two failed checks in a row. Hosts forwarded to " +
                   "a Docker container name (or localhost) can't be checked from this PC and are skipped.");
            p.Check("A new version of Nginx Proxy Manager is out", delegate { return c.NotifyUpdate; }, delegate (bool v) { c.NotifyUpdate = v; });
            p.Kind("New versions show as", "NPM.Update");
            p.Kind("2FA code needed shows as", "NPM.SignIn");
            p.Note("vShell checks every 2 minutes (every 30 seconds while a module or the run box shows it).");
        }

        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            var v = new NpmView(cfg);
            if (!NpmClient.Configured(p.Settings)) p.Note("Set up the Nginx Proxy Manager connection first (Plugins page in this window).");
            p.Check("Globe icon", delegate { return v.Icon; }, delegate (bool on) { v.Icon = on; });
            p.Check("Hide while nothing's wrong", delegate { return v.HideFine; }, delegate (bool on) { v.HideFine = on; });
            if (!p.IsWidget) p.Title(cfg, "");
            p.Note("Shows how many hosts you have, and how many have a problem (red). Click for the list: click a host to open it, right-click for more, " +
                   "Add a host… at the bottom.");
        }

        static FrameworkElement BarPic(Theme t)
        {
            return ModulePics.Row(6, ModulePics.G(0xE774, t.MainAccent, 16), ModulePics.T(t, "18 hosts · 1 problem", t.Fg, 12.5));
        }

        // =================================================================
        // The run box: !npm [disable | enable | renew] text
        // =================================================================
        static void Rows(RunPanel box, RunAction a)
        {
            _box = box;
            BarSettings S = box.Settings;
            NpmSnapshot snap = NpmClient.Latest;
            if (snap.NeedCode)
            {
                box.AddRow("", "Enter your 2FA code", "Nginx Proxy Manager needs it", delegate { box.Close(); AskCode(); }, null);
                box.Status("Nginx Proxy Manager needs your 2FA code.");
                box.Select(0);
                return;
            }
            if (!snap.Ok) { box.Status(snap.Error.Length > 0 ? "Nginx Proxy Manager: " + snap.Error : "Asking Nginx Proxy Manager…"); return; }

            string q = (a.Query ?? "").Trim(), verb = "";
            foreach (string v in new[] { "disable", "enable", "renew", "delete", "edit", "add" })
                if (q.Equals(v, StringComparison.OrdinalIgnoreCase) || q.StartsWith(v + " ", StringComparison.OrdinalIgnoreCase)) { verb = v; q = q.Substring(v.Length).Trim(); break; }
            if (verb == "add")
            {
                string domain = q;
                box.AddRow("", domain.Length > 0 ? "Add a proxy host for " + domain : "Add a proxy host", "opens the form",
                           delegate { box.Remember(box.Text); box.Close(); AddHost(domain); }, null);
                box.Status("Enter opens the Add a host form" + (domain.Length > 0 ? " with " + domain + " filled in" : " - you can type the domain after add"));
                box.Select(0);
                return;
            }
            double certDays = NpmSettings.Of(S).CertDays;
            int shown = 0;
            foreach (NpmHost h in snap.Hosts)
            {
                if (shown >= 12) break;
                if (q.Length > 0 && !Matches(h, q)) continue;
                NpmHost host = h;
                NpmCert cert = snap.Cert(h.CertId);
                string problem = snap.Problem(h, certDays);
                if (verb == "disable" || verb == "enable")
                {
                    bool on = verb == "enable";
                    if (h.Enabled == on) continue;
                    box.AddRow("", (on ? "Enable " : "Disable ") + h.Domain, h.Target, async delegate
                    {
                        box.Status((on ? "Enabling " : "Disabling ") + host.Domain + "…");
                        string err = await Enable(host, on);
                        if (err == "") { box.Status(""); return; }
                        box.Status(err != null ? "Nginx Proxy Manager: " + err : (on ? "Enabled " : "Disabled ") + host.Domain + " ✓");
                        if (err == null) box.Remember(box.Text);
                    }, null);
                }
                else if (verb == "edit")
                    box.AddRow("", "Edit " + h.Domain, h.Target, delegate { box.Remember(box.Text); box.Close(); EditHost(host.Id); }, null);
                else if (verb == "delete")
                {
                    box.AddRow("", "Delete " + h.Domain, h.Target, async delegate
                    {
                        string err = await Delete(host);
                        if (err == "") return;
                        box.Status(err != null ? "Nginx Proxy Manager: " + err : "Deleted " + host.Domain + " ✓");
                    }, null);
                }
                else if (verb == "renew")
                {
                    if (cert == null || !cert.LetsEncrypt) continue;
                    long id = cert.Id;
                    box.AddRow("", "Renew the certificate for " + h.Domain, cert.DaysText, delegate { box.Close(); Renew(id); }, null);
                }
                else
                    box.AddRow(problem.Length > 0 ? "" : Glyph, h.Domain, !h.Enabled ? "off" : problem.Length > 0 ? problem : h.Target,
                               delegate { box.Remember(box.Text); box.Close(); RunPanel.Open(host.SiteUrl); }, null);
                shown++;
            }
            int problems = snap.Problems(certDays);
            box.Status(shown == 0 ? (q.Length > 0 ? "No host matches \"" + q + "\"" : "Nothing to show")
                       : NpmClient.Count(snap.Hosts.Count, "host") + (problems > 0 ? " · " + NpmClient.Count(problems, "problem") : "") +
                         (verb.Length == 0 ? "  ·  Enter opens it  ·  type add, edit, disable, enable, renew or delete first for those" : ""));
            box.Select(0);
        }

        static bool Matches(NpmHost h, string q)
        {
            foreach (string d in h.Domains) if (d.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return h.ForwardHost.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>On a bar (or a widget): "18 hosts · 1 problem", red with a problem; click for the list of hosts.</summary>
    public sealed class NpmModule : InfoModule
    {
        PopupWindow _popup;
        ScrollViewer _scroll;
        string _popupSig;
        bool _menuOpen;
        int _level;     // 0 fine, 2 something's wrong

        public NpmModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg) { }

        protected override bool Clickable { get { return true; } }

        protected override void Read(Reading r, bool addSample)
        {
            var v = new NpmView(Cfg);
            r.Icon = v.Icon ? NpmPlugin.Glyph : "";
            BarSettings s = Mgr.Settings;
            _level = 0;
            if (!NpmClient.Configured(s)) { r.Text = Prefix("") + "NPM: set up in Settings > Plugins"; r.Tip = "Add the Nginx Proxy Manager connection on the Plugins page of Settings."; return; }
            NpmSnapshot snap = NpmClient.Latest;
            if (snap.NeedCode) { _level = 2; r.Text = Prefix("") + "NPM: 2FA code needed"; r.Tip = "Click to enter it."; return; }
            if (!snap.Ok) { r.Text = Prefix("") + (snap.Error.Length > 0 ? "NPM n/a" : "NPM…"); r.Tip = snap.Error; return; }
            double certDays = NpmSettings.Of(s).CertDays;
            int problems = snap.Problems(certDays);
            if (problems > 0) _level = 2;
            r.Text = Prefix("") + NpmClient.Count(snap.Hosts.Count, "host") + (problems > 0 ? " · " + NpmClient.Count(problems, "problem") : "");
            r.Hide = v.HideFine && problems == 0 && snap.Error.Length == 0;

            var tip = new List<string>();
            foreach (NpmHost h in snap.Hosts)
            {
                string p = snap.Problem(h, certDays);
                if (p.Length > 0) tip.Add(h.Domain + ": " + p);
            }
            if (tip.Count == 0) tip.Add("All fine");
            NpmCert next = null;
            foreach (NpmCert c in snap.Certs) if (snap.Used(c) && (next == null || c.Expires < next.Expires)) next = c;
            if (next != null) tip.Add("Next certificate to renew: " + next.Name + ", " + next.DaysText);
            if (snap.Update) tip.Add("Nginx Proxy Manager " + snap.Latest + " is out");
            tip.Add("Checked " + TimeText.Clock(snap.Fetched.ToLocalTime()) + (snap.Error.Length > 0 ? " - last try failed: " + snap.Error : ""));
            r.Tip = string.Join("\n", tip);

            // the popup is redrawn when what it shows changed (not on every check, nor while its menu is open), keeping its scroll place
            if (_popup != null && _popup.IsOpen && !_menuOpen && Sig(snap, certDays, NpmSettings.Of(s).CheckBackends) != _popupSig)
            {
                double at = _scroll != null ? _scroll.VerticalOffset : 0;
                _popup.SetContent(Card(ModTheme ?? Bar.Theme));
                if (at > 0 && _scroll != null) _scroll.ScrollToVerticalOffset(at);
            }
        }

        /// <summary>What the popup shows, as text: it's redrawn only when this changes.</summary>
        static string Sig(NpmSnapshot snap, double certDays, bool checks)
        {
            var b = new StringBuilder();
            b.Append(snap.Ok).Append('|').Append(snap.NeedCode).Append('|').Append(snap.Error).Append('|').Append(snap.Current)
             .Append('|').Append(snap.Update ? snap.Latest : "").Append('|').Append(checks);
            foreach (NpmHost h in snap.Hosts)
            {
                NpmCert c = snap.Cert(h.CertId);
                b.Append('\n').Append(h.Id).Append(' ').Append(string.Join(",", h.Domains)).Append(' ').Append(h.Enabled).Append(' ').Append(h.Target)
                 .Append(' ').Append(h.Backend).Append(' ').Append(snap.Problem(h, certDays)).Append(' ').Append(c != null ? c.Name + ": " + c.DaysText : "");
            }
            return b.ToString();
        }

        protected override void Decorate(TextBlock txt) { Colour(); }
        protected override void OnThemed(Theme t) { Colour(); }

        void Colour()
        {
            Theme t = ModTheme ?? Bar.Theme;
            Txt.Foreground = _level == 2 ? StatusColors.Bad : (Brush)t.Fg;
        }

        protected override void OnClick()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            if (NpmClient.Latest.NeedCode) { NpmPlugin.AskCode(); return; }
            Theme t = ModTheme ?? Bar.Theme;
            _popup = new PopupWindow(Mgr, Bar, this, t, Card(t));
            if (Mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(t, Mgr.Settings.WidgetStyle);
            if (_scroll != null) _scroll.MaxHeight = _popup.ContentRoom();
            Mgr.ShowPopup(_popup);
        }

        void Close() { if (_popup != null) { _popup.CloseSafe(); _popup = null; } }

        TextBlock Line(Theme t, string text, Brush fg, double size, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, FontFamily = t.Font, FontSize = size, Foreground = fg, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        TextBlock Link(Theme t, string text, Action click)
        {
            TextBlock l = Line(t, text, t.MainAccent, t.FontSize);
            l.Cursor = Cursors.Hand;
            l.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            l.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return l;
        }

        /// <summary>The popup: every host with its state, where it forwards to and its certificate; a click opens it, right-click for more.</summary>
        FrameworkElement Card(Theme t)
        {
            BarSettings s = Mgr.Settings;
            NpmSnapshot snap = NpmClient.Latest;
            double certDays = NpmSettings.Of(s).CertDays;
            _popupSig = Sig(snap, certDays, NpmSettings.Of(s).CheckBackends);
            var sp = new StackPanel { Margin = new Thickness(12, 10, 12, 12), MinWidth = 300, MaxWidth = 460 };
            TextBlock head = Line(t, "Nginx Proxy Manager" + (snap.Current.Length > 0 ? "  " + snap.Current : ""), t.Fg, t.FontSize + 2, true);
            head.Margin = new Thickness(0, 0, 0, 6);
            sp.Children.Add(head);
            if (snap.Update) sp.Children.Add(Line(t, snap.Latest + " is out", StatusColors.Warn, t.FontSize));
            if (snap.Error.Length > 0)
            {
                TextBlock e = Line(t, snap.Error, StatusColors.Bad, t.FontSize);
                e.TextWrapping = TextWrapping.Wrap;
                sp.Children.Add(e);
            }

            var list = new StackPanel();
            foreach (NpmHost h in snap.Hosts) list.Children.Add(HostRow(t, snap, h, certDays));
            if (snap.Hosts.Count == 0) list.Children.Add(Line(t, snap.Ok ? "No proxy hosts" : "Asking Nginx Proxy Manager…", t.FgDim, t.FontSize));
            _scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 0) };
            if (_popup != null) _scroll.MaxHeight = _popup.ContentRoom();
            sp.Children.Add(_scroll);

            var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            TextBlock add = Link(t, "Add a host…", delegate { Close(); NpmPlugin.AddHost(""); });
            add.Margin = new Thickness(0, 0, 18, 0);
            if (snap.Ok && !snap.NeedCode) links.Children.Add(add);
            links.Children.Add(Link(t, "Open Nginx Proxy Manager", delegate { Close(); RunPanel.Open(NpmClient.AdminUrl(s, "/")); }));
            sp.Children.Add(links);
            return sp;
        }

        FrameworkElement HostRow(Theme t, NpmSnapshot snap, NpmHost h, double certDays)
        {
            string problem = snap.Problem(h, certDays);
            NpmCert cert = snap.Cert(h.CertId);
            // the dot: red = a problem, green = its backend answers, a green ring = served but its backend isn't checked from this PC, dim = off
            bool ring = h.Enabled && problem.Length == 0 && h.Backend != Backend.Up;
            Brush dot = !h.Enabled ? t.FgDim : problem.Length > 0 ? StatusColors.Bad : (Brush)StatusColors.Good;
            string backend = !h.Enabled ? "" : h.Backend == Backend.Up ? "\nBackend answers (checked from this PC)"
                           : h.Backend == Backend.Down ? "\nBackend doesn't answer (checked from this PC)"
                           : h.Backend == Backend.Unknown ? "\nBackend not checked - this PC can't reach " + h.ForwardHost + " (a Docker name or localhost)"
                           : NpmSettings.Of(Mgr.Settings).CheckBackends ? "\nBackend not checked yet" : "\nBackend checks are off (Settings > Notifications)";

            var g = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var e = new Ellipse
            {
                Width = 8, Height = 8, Fill = ring ? Brushes.Transparent : dot, Stroke = ring ? dot : null, StrokeThickness = 1.5,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 7, 0, 0)
            };
            g.Children.Add(e);
            var text = new StackPanel { Margin = new Thickness(0, 2, 4, 2) };
            text.Children.Add(Line(t, h.Domain + (h.Domains.Count > 1 ? "  +" + (h.Domains.Count - 1) : ""), h.Enabled ? t.Fg : t.FgDim, t.FontSize));
            string detail = !h.Enabled ? "off" : "→ " + h.Target + (cert != null ? "  ·  \U0001F512 " + cert.DaysText : "");
            text.Children.Add(Line(t, problem.Length > 0 ? problem : detail, problem.Length > 0 ? StatusColors.Bad : t.FgDim, t.FontSize - 1.5));
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            g.ToolTip = string.Join("\n", h.Domains) + "\n→ " + h.Target + (cert != null ? "\nCertificate: " + cert.Name + ", " + cert.DaysText : "\nNo certificate (http only)") + backend;

            g.MouseEnter += delegate { g.Background = t.Hover; };
            g.MouseLeave += delegate { g.Background = Brushes.Transparent; };
            g.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs ev) { ev.Handled = true; };
            g.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs ev) { ev.Handled = true; Close(); RunPanel.Open(h.SiteUrl); };

            var menu = new ContextMenu();
            Action<string, Action> item = delegate (string header, Action run)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += delegate { run(); };
                menu.Items.Add(mi);
            };
            item("Open " + h.SiteUrl, delegate { Close(); RunPanel.Open(h.SiteUrl); });
            item("Open in Nginx Proxy Manager", delegate { Close(); RunPanel.Open(NpmClient.AdminUrl(Mgr.Settings, "/nginx/proxy")); });
            item("Copy address", delegate { try { Clipboard.SetText(h.SiteUrl); } catch { } });
            menu.Items.Add(new Separator());
            item("Edit…", delegate { Close(); NpmPlugin.EditHost(h.Id); });
            item(h.Enabled ? "Disable" : "Enable", async delegate
            {
                string err = await NpmPlugin.Enable(h, !h.Enabled);
                if (!string.IsNullOrEmpty(err)) Notify.Post(NpmPlugin.Source, (h.Enabled ? "Disabling " : "Enabling ") + h.Domain + " didn't work", err, null, NotifyLevel.Important);
            });
            if (cert != null && cert.LetsEncrypt) item("Renew certificate (" + cert.DaysText + ")", delegate { NpmPlugin.Renew(cert.Id); });
            if (h.CertId <= 0) item("Get a Let's Encrypt certificate", delegate { Close(); NpmPlugin.RequestCert(h.Id); });
            menu.Items.Add(new Separator());
            item("Delete…", async delegate
            {
                string err = await NpmPlugin.Delete(h);
                if (!string.IsNullOrEmpty(err)) Notify.Post(NpmPlugin.Source, "Deleting " + h.Domain + " didn't work", err, null, NotifyLevel.Important);
            });
            menu.Opened += delegate { _menuOpen = true; };
            menu.Closed += delegate { _menuOpen = false; };
            g.ContextMenu = menu;
            return g;
        }
    }
}
