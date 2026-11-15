using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using vShell.Core;
using vShell.Ui;

[assembly: vShell.Core.VShellPlugin("binarylane", "BinaryLane", Description = "Your BinaryLane account: credit and how many days it lasts on a bar, a notification when it runs low, failed payments, data transfer and your servers' threshold alerts.",
    Glyph = "", Group = "Servers & network", Author = "version", Adds = "credit alerts, billing / data transfer / threshold notifications", Types = "BinaryLane")]

namespace vShell.BinaryLane
{
    /// <summary>
    /// The BinaryLane plugin: the credit module, the Plugins and Notifications sections, and the notifications (low credit, credit running
    /// out within some days, failed payments, account status, data transfer, threshold alerts raised in mPanel).
    /// </summary>
    public sealed class BinaryLanePlugin : IPlugin
    {
        public const string Type = "BinaryLane";
        const string Source = "BinaryLane";
        BarManager _mgr;
        BarSettings _s;
        Action<BlSnapshot> _changed;

        public void Load(PluginHost host)
        {
            _mgr = host.Manager;
            host.AddModule(new PluginModule
            {
                Type = Type, Display = "BinaryLane credit", ShortName = "BinaryLane", Glyph = "", Group = "Smart home & more",
                Blurb = "credit, days left", HasPopup = true,
                Needs = delegate (BarSettings s) { return BlClient.Configured(s) ? null : "Needs the BinaryLane connection (Plugins)"; },
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new BlModule(mgr, h, cfg); },
                Settings = ModuleSettings, BarPicture = BarPic, WidgetPicture = BarPic
            });
            host.SetUpWhen(BlClient.Configured);
            host.AddIntegration("BinaryLane", IntegrationSection);
            host.AddNotifications(NotifySection);
            host.OnSettings(delegate (BarSettings s) { _s = s; BlClient.Configure(s); });

            host.AddNotifyKind("BinaryLane.Credit", NotifyLevel.Alert);
            host.AddNotifyKind("BinaryLane.Billing", NotifyLevel.Alert);
            host.AddNotifyKind("BinaryLane.Transfer", NotifyLevel.Important);
            host.AddNotifyKind("BinaryLane.Alert", NotifyLevel.Important);
            host.AddNotifySource(Source);
            _changed = Check;
            BlClient.Changed += _changed;
        }

        public void Unload()
        {
            if (_changed != null) BlClient.Changed -= _changed;
            BlClient.Stop();
        }

        // =================================================================
        // Notifications: everything that applies now gets a key; a key that wasn't there last time is said. Keys are kept in the
        // settings, so a restart doesn't say them again - and one that stops applying (a top-up) can be said again later.
        // =================================================================
        void Check(BlSnapshot snap)
        {
            if (_s == null || !snap.Ok || snap.Error.Length > 0) return;
            BlSettings c = BlSettings.Of(_s);
            var said = new HashSet<string>(c.Said.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
            var now = new HashSet<string>();
            Action<string, string, string, string> say = delegate (string key, string kind, string title, string detail)
            {
                now.Add(key);
                if (!said.Contains(key)) Notify.Post(Source, title, detail, BlClient.Panel, Notify.KindOf(kind));
            };
            // a part that couldn't be read this time keeps what it had said
            Action<string> keep = delegate (string prefix) { foreach (string k in said) if (k.StartsWith(prefix)) now.Add(k); };

            BlMoney m = BlMoney.Of(snap, c);
            if (c.LowCreditOn && m.Left < c.LowCredit)
                say("credit", "BinaryLane.Credit", "BinaryLane credit is low: " + BlClient.Money(m.Left),
                    "Below your " + BlClient.Money(c.LowCredit) + " limit" + (m.Monthly > 0 ? " - it lasts about " + m.DaysText + "." : ".") + " Click to top up in mPanel.");
            if (c.DaysOn && m.Days < c.Days)
                say("days", "BinaryLane.Credit", "BinaryLane credit runs out in " + m.DaysText,
                    BlClient.Money(m.Left) + " left at about " + BlClient.Money(m.Monthly) + " a month. Click to top up in mPanel.");

            if (c.NotifyBilling)
            {
                if (snap.Status.Length > 0 && snap.Status != "active")
                    say("status:" + snap.Status, "BinaryLane.Billing", "BinaryLane account: " + snap.Status, StatusText(snap.Status));
                if (snap.FailedOk)
                {
                    foreach (BlInvoice i in snap.Failed)
                        say("invoice:" + i.Id, "BinaryLane.Billing", "BinaryLane: a payment failed",
                            "Invoice " + i.Number + " for " + BlClient.Money(i.Amount) + " isn't paid" +
                            (i.Due != DateTime.MinValue ? " (due " + i.Due.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture) + ")" : "") + ".");
                }
                else keep("invoice:");
                if (snap.ServersOk)
                {
                    foreach (BlServer sv in snap.Servers)
                        if (sv.Status == "archive")
                            say("archive:" + sv.Id, "BinaryLane.Billing", "BinaryLane: " + sv.Name + " is off for non-payment", "Pay the overdue invoice in mPanel to get it back.");
                }
                else keep("archive:");
            }

            if (c.NotifyTransfer)
            {
                if (snap.TransfersOk)
                {
                    foreach (BlTransfer t in snap.Transfers)
                        if (t.IncludedGb > 0 && t.Percent >= c.TransferPercent)
                            say("transfer:" + t.ServerId + ":" + t.PeriodEnd.ToString("yyyyMMdd", CultureInfo.InvariantCulture), "BinaryLane.Transfer",
                                "BinaryLane: " + snap.ServerName(t.ServerId) + " has used " + Math.Floor(t.Percent) + "% of its data transfer",
                                t.UsedGb.ToString("0.#", CultureInfo.CurrentCulture) + " of " + t.IncludedGb.ToString("0", CultureInfo.CurrentCulture) + " GB" +
                                (t.PeriodEnd != DateTime.MinValue ? ", the count starts again " + t.PeriodEnd.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture) : "") + ".");
                }
                else keep("transfer:");
            }

            if (c.NotifyAlerts)
            {
                if (snap.AlertsOk)
                {
                    foreach (BlAlert a in snap.Alerts)
                        say("alert:" + a.ServerId + ":" + a.Type + ":" + a.Raised, "BinaryLane.Alert",
                            "BinaryLane: " + a.Name + " on " + snap.ServerName(a.ServerId),
                            (double.IsNaN(a.Current) ? "" : "Now " + Value(a.Current, a.Unit) + ", ") + "your alert is set at " + Value(a.Value, a.Unit) + ".");
                }
                else keep("alert:");
            }

            var list = new List<string>(now);
            list.Sort(StringComparer.Ordinal);
            string joined = string.Join("\n", list);
            if (joined != c.Said)
            {
                c.Said = joined;
                _mgr.SaveSoon();
            }
        }

        static string StatusText(string status)
        {
            switch (status)
            {
                case "warning": return "Your account is under review. If you're not sure why, contact BinaryLane support soon.";
                case "locked": return "Your account is locked and can't be used. Contact BinaryLane support.";
                case "incomplete": return "Your account isn't ready yet - usually it needs payment details.";
                default: return "Check your account in mPanel.";
            }
        }

        static string Value(double v, string unit)
        {
            string n = v.ToString("0.##", CultureInfo.CurrentCulture);
            return unit.Length == 0 ? n : unit == "%" ? n + "%" : n + " " + unit;
        }

        // =================================================================
        // Settings
        // =================================================================
        static void IntegrationSection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            BlSettings c = BlSettings.Of(S);
            p.Header("BinaryLane");
            PasswordBox pw = p.Secret("API token", "API token", delegate { return c.Token; }, delegate (string v) { c.Token = v; },
                                      "Make one in mPanel (Account > API Info). vShell only reads with it. It is stored encrypted for your Windows account.");
            p.Test(delegate
            {
                if (pw.Password.Trim().Length > 0) { c.Token = pw.Password.Trim(); pw.Password = ""; p.Changed(); }
            }, delegate { return BlClient.TestAsync(S); });
            p.Number("What you spend a month, AU$ (0 = work it out)", delegate { return c.MonthlySpend; }, delegate (double v) { c.MonthlySpend = v; });
            p.Note("Used for how many days your credit lasts. With 0, vShell adds up your servers' monthly prices (with extra memory, disk, IPv4 " +
                   "and transfer) - backups and other extras aren't counted, so put your real monthly spend here if you know it.");
            p.Check("Count charges not invoiced yet as already spent", delegate { return c.CountUnbilled; }, delegate (bool v) { c.CountUnbilled = v; });
            p.Note("BinaryLane adds up your usage and invoices it later. With this on, the credit shown is what's left once that's paid.");
        }

        static void NotifySection(SettingsWindow.SettingsPage p)
        {
            BarSettings S = p.Settings;
            BlSettings c = BlSettings.Of(S);
            p.NotifySection("BinaryLane notifications", BlClient.Configured(S), "BinaryLane");
            p.Check("Credit drops below an amount", delegate { return c.LowCreditOn; }, delegate (bool v) { c.LowCreditOn = v; p.Redraw(); });
            if (c.LowCreditOn) p.Number("Amount, AU$", delegate { return c.LowCredit; }, delegate (double v) { c.LowCredit = v; });
            p.Check("Credit runs out within some days", delegate { return c.DaysOn; }, delegate (bool v) { c.DaysOn = v; p.Redraw(); });
            if (c.DaysOn) p.Number("Days", delegate { return c.Days; }, delegate (double v) { c.Days = v; });
            p.Kind("Credit shows as", "BinaryLane.Credit");
            p.Note("Each says once, then again only after the credit has gone back up (a top-up) and dropped again.");
            p.Check("A payment failed, a server is off for non-payment, or the account needs attention", delegate { return c.NotifyBilling; }, delegate (bool v) { c.NotifyBilling = v; });
            p.Kind("Billing shows as", "BinaryLane.Billing");
            p.Check("A server has used most of its data transfer", delegate { return c.NotifyTransfer; }, delegate (bool v) { c.NotifyTransfer = v; p.Redraw(); });
            if (c.NotifyTransfer) p.Slider("Percent used", 50, 100, delegate { return c.TransferPercent; }, delegate (double v) { c.TransferPercent = Math.Round(v); });
            p.Kind("Data transfer shows as", "BinaryLane.Transfer");
            p.Check("A threshold alert set in mPanel is raised (CPU, memory, disk …)", delegate { return c.NotifyAlerts; }, delegate (bool v) { c.NotifyAlerts = v; });
            p.Kind("Threshold alerts show as", "BinaryLane.Alert");
            p.Note("vShell checks every 15 minutes (every 2 while a BinaryLane module is on screen). Click a notification to open mPanel.");
        }

        static void ModuleSettings(SettingsWindow.SettingsPage p, ModuleConfig cfg)
        {
            var v = new BlView(cfg);
            if (!BlClient.Configured(p.Settings)) p.Note("Set up the BinaryLane connection first (Plugins page in this window).");
            p.Combo("Show", new[] { "Both", "Credit", "Days left" }, delegate { return v.Show; }, delegate (string s) { v.Show = s; });
            p.Check("Card icon", delegate { return v.Icon; }, delegate (bool on) { v.Icon = on; });
            if (!p.IsWidget) p.Title(cfg, "");
            p.Note("The text turns amber when you're getting close to a credit alert, red once you're past one. Hover for the details, click for more.");
        }

        static FrameworkElement BarPic(Theme t)
        {
            return ModulePics.Row(6, ModulePics.G(0xE8C7, t.MainAccent, 16), ModulePics.T(t, "$42.10 · 23 days", t.Fg, 12.5));
        }
    }

    /// <summary>The credit on a bar (or a widget): "$42.10 · 23 days", coloured near / past the alerts; the details in a popup.</summary>
    public sealed class BlModule : InfoModule
    {
        PopupWindow _popup;
        BlSnapshot _popupFor;
        int _level;     // 0 fine, 1 getting close, 2 past an alert

        public BlModule(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg) { }

        protected override bool Clickable { get { return true; } }

        protected override void Read(Reading r, bool addSample)
        {
            var v = new BlView(Cfg);
            r.Icon = v.Icon ? "" : "";
            BarSettings s = Mgr.Settings;
            _level = 0;
            if (!BlClient.Configured(s)) { r.Text = Prefix("") + "BinaryLane: set up in Settings > Plugins"; r.Tip = "Add your API token on the Plugins page of Settings."; return; }
            BlSnapshot snap = BlClient.Latest;
            if (!snap.Ok) { r.Text = Prefix("") + (snap.Error.Length > 0 ? "BinaryLane n/a" : "BinaryLane…"); r.Tip = snap.Error; return; }
            BlSettings c = BlSettings.Of(s);
            BlMoney m = BlMoney.Of(snap, c);
            if ((c.LowCreditOn && m.Left < c.LowCredit) || (c.DaysOn && m.Days < c.Days)) _level = 2;
            else if ((c.LowCreditOn && m.Left < c.LowCredit * 1.5) || (c.DaysOn && m.Days < c.Days * 1.5)) _level = 1;
            string credit = BlClient.Money(m.Left), days = m.DaysText;
            r.Text = Prefix("") + (v.Show == "Credit" ? credit : v.Show == "Days left" ? days : credit + " · " + days);
            r.Tip = string.Join("\n", Lines(snap, c, m));
            // the popup follows a new snapshot
            if (_popup != null && _popup.IsOpen && _popupFor != snap) _popup.SetContent(Card(ModTheme ?? Bar.Theme));
        }

        static List<string> Lines(BlSnapshot snap, BlSettings c, BlMoney m)
        {
            var l = new List<string>();
            l.Add("Credit: " + BlClient.Money(snap.Credit));
            l.Add("Not invoiced yet: " + BlClient.Money(snap.Unbilled));
            if (c.CountUnbilled) l.Add("Left once that's paid: " + BlClient.Money(m.Left));
            if (m.Monthly > 0)
                l.Add("About " + BlClient.Money(m.Monthly) + " a month" +
                      (m.MonthlyGuessed ? " (your " + (snap.Servers.Count == 1 ? "server" : snap.Servers.Count + " servers") + ", not counting backups)" : "") +
                      " - lasts " + m.DaysText);
            foreach (BlInvoice i in snap.Failed) l.Add("Payment failed: invoice " + i.Number + ", " + BlClient.Money(i.Amount));
            if (snap.Status.Length > 0 && snap.Status != "active") l.Add("Account: " + snap.Status);
            l.Add("Checked " + TimeText.Clock(snap.Fetched.ToLocalTime()) + (snap.Error.Length > 0 ? " - last try failed: " + snap.Error : ""));
            return l;
        }

        protected override void Decorate(TextBlock txt) { Colour(); }
        protected override void OnThemed(Theme t) { Colour(); }

        void Colour()
        {
            Theme t = ModTheme ?? Bar.Theme;
            Txt.Foreground = _level == 2 ? StatusColors.Bad : _level == 1 ? StatusColors.Warn : (Brush)t.Fg;
        }

        protected override void OnClick()
        {
            if (_popup != null && _popup.IsOpen) { _popup.CloseSafe(); _popup = null; return; }
            Theme t = ModTheme ?? Bar.Theme;
            _popup = new PopupWindow(Mgr, Bar, this, t, Card(t));
            if (Mgr.Settings.WidgetStyle != null) _popup.UseWidgetStyle(t, Mgr.Settings.WidgetStyle);
            Mgr.ShowPopup(_popup);
        }

        /// <summary>The popup: the numbers, any failed payments, and Open mPanel.</summary>
        FrameworkElement Card(Theme t)
        {
            var sp = new StackPanel { Margin = new Thickness(14, 10, 14, 12), MinWidth = 240 };
            sp.Children.Add(new TextBlock { Text = "BinaryLane", FontFamily = t.Font, FontSize = t.FontSize + 2, FontWeight = FontWeights.SemiBold, Foreground = t.Fg, Margin = new Thickness(0, 0, 0, 6) });
            BarSettings s = Mgr.Settings;
            BlSnapshot snap = BlClient.Latest;
            _popupFor = snap;
            if (snap.Ok)
            {
                BlSettings c = BlSettings.Of(s);
                BlMoney m = BlMoney.Of(snap, c);
                sp.Children.Add(new TextBlock
                {
                    Text = BlClient.Money(m.Left) + "  ·  " + m.DaysText, FontFamily = t.Font, FontSize = t.FontSize + 6, FontWeight = FontWeights.SemiBold,
                    Foreground = _level == 2 ? StatusColors.Bad : _level == 1 ? StatusColors.Warn : (Brush)t.Fg, Margin = new Thickness(0, 0, 0, 6)
                });
                foreach (string line in Lines(snap, c, m))
                    sp.Children.Add(new TextBlock { Text = line, FontFamily = t.Font, FontSize = t.FontSize, Foreground = t.FgDim, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, Margin = new Thickness(0, 1, 0, 1) });
            }
            else sp.Children.Add(new TextBlock { Text = snap.Error.Length > 0 ? snap.Error : "Asking BinaryLane…", FontFamily = t.Font, FontSize = t.FontSize, Foreground = t.FgDim, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 });

            var open = new TextBlock { Text = "Open mPanel", FontFamily = t.Font, FontSize = t.FontSize, Foreground = t.MainAccent, Cursor = Cursors.Hand, Margin = new Thickness(0, 10, 0, 0) };
            open.MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { e.Handled = true; };
            open.MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (_popup != null) { _popup.CloseSafe(); _popup = null; }
                RunPanel.Open(BlClient.Panel);
            };
            sp.Children.Add(open);
            return sp;
        }
    }
}
