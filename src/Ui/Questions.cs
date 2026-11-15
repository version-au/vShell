using System;
using System.Windows;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// Questions and messages from modules ("Run procedure X?", "Shut down VM 101?", "The terminal couldn't open"), in vShell's theme - the
    /// suite's themed question (vSuite.Core's Ask, as in vXplorer and vView) instead of Windows' plain message box.
    /// From a widget the question must not belong to it: a widget sits on the desktop, behind every window, and never takes the keyboard,
    /// so a question owned by it opened behind everything while blocking the widget - it looked frozen. Those ask on top of everything
    /// instead, on the widget's monitor (or the pointer's when there's no window); popups and bars (in front already) own theirs.
    /// DialogPlacement keeps each one on the monitor it was asked from. If the themed question can't be shown, Windows' message box is.
    /// </summary>
    [vShell.Core.PluginApi]
    public static class Questions
    {
        /// <summary>vShell's settings as they are now (set at start-up), for the questions' colours; null = read settings.json.</summary>
        internal static Func<BarSettings> CurrentSettings;

        public static MessageBoxResult YesNo(Window owner, string question, string title)
        {
            return YesNo(owner, question, title, MessageBoxImage.Question);
        }

        public static MessageBoxResult YesNo(Window owner, string question, string title, MessageBoxImage icon)
        {
            return Ask(owner, question, title, MessageBoxButton.YesNo, icon, MessageBoxResult.No);
        }

        /// <summary>Any buttons (Yes / No / Cancel …), owned the same way: by a bar or popup, on top of everything from a widget or nothing.</summary>
        public static MessageBoxResult Ask(Window owner, string question, string title, MessageBoxButton buttons, MessageBoxImage icon, MessageBoxResult byDefault)
        {
            MessageBoxResult[] results;
            string[] labels;
            switch (buttons)
            {
                case MessageBoxButton.OKCancel: results = new[] { MessageBoxResult.OK, MessageBoxResult.Cancel }; labels = new[] { "OK", "Cancel" }; break;
                case MessageBoxButton.YesNo: results = new[] { MessageBoxResult.Yes, MessageBoxResult.No }; labels = new[] { "Yes", "No" }; break;
                case MessageBoxButton.YesNoCancel: results = new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel }; labels = new[] { "Yes", "No", "Cancel" }; break;
                default: results = new[] { MessageBoxResult.OK }; labels = new[] { "OK" }; break;
            }
            int def = Math.Max(0, Array.IndexOf(results, byDefault));
            // Esc: Cancel when there is one, otherwise No (a question) or OK (a message) - as Windows' message box
            int cancel = Array.IndexOf(results, MessageBoxResult.Cancel);
            if (cancel < 0) cancel = Array.IndexOf(results, MessageBoxResult.No);
            if (cancel < 0) cancel = 0;
            // a warning that asks (shut down, delete …): its Yes in the warning colour
            bool danger = (icon == MessageBoxImage.Warning || icon == MessageBoxImage.Error) && results.Length > 1;
            string glyph = icon == MessageBoxImage.Warning ? "" : icon == MessageBoxImage.Error ? "" : icon == MessageBoxImage.Information ? "" : null;
            try
            {
                int i = vSuite.Ui.Ask.Show(owner, title, question, labels, def, cancel, danger, glyph, delegate (Window dialog) { Own(dialog, owner); });
                return results[Math.Max(0, Math.Min(results.Length - 1, i))];
            }
            catch (Exception ex)
            {
                Log.Write("Questions (themed): " + ex.Message);
                return owner != null && owner.IsVisible && !(owner is DesktopWindow)
                    ? MessageBox.Show(owner, question, title, buttons, icon, byDefault)
                    : MessageBox.Show(question, title, buttons, icon, byDefault);
            }
        }

        /// <summary>A message with OK (something went wrong, or worth knowing), owned like the questions.</summary>
        public static void Tell(Window owner, string text, string title)
        {
            Tell(owner, text, title, MessageBoxImage.Information);
        }

        public static void Tell(Window owner, string text, string title, MessageBoxImage icon)
        {
            Ask(owner, text, title, MessageBoxButton.OK, icon, MessageBoxResult.OK);
        }

        /// <summary>A dialog asked from this window: owned by it - unless it's a widget (then on top of everything, on the widget's monitor, and it takes the keyboard).</summary>
        public static void Own(Window dialog, Window owner)
        {
            if (owner != null && owner.IsVisible && !(owner is DesktopWindow)) { dialog.Owner = owner; return; }
            dialog.Topmost = true;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (owner != null && owner.IsVisible) DialogPlacement.Prefer(dialog, owner);
            dialog.ContentRendered += delegate { try { dialog.Activate(); } catch { } };
        }

        /// <summary>
        /// vShell's theme as the suite's dialogs take it: the bars' colours (Wallpaper Theme included - the same ones the bars show), the
        /// Blur / Frosted setting and the widget style's corners and border.
        /// </summary>
        internal static vSuite.Ui.Theme SuiteTheme()
        {
            BarSettings s = null;
            try { s = CurrentSettings != null ? CurrentSettings() : null; } catch { }
            if (s == null) s = BarSettings.Load();
            return SettingsStyle.SuiteTheme(s);
        }

    }
}
