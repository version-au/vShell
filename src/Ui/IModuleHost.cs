using System;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>A module that needs more than one widget row: RowCount x the widget's module height, or 0 = its own natural height (at least one row).</summary>
    [PluginApi]
    public interface IMultiRow
    {
        int RowCount { get; }
    }

    /// <summary>Sizes that must fit the host (a slim bar style can be much lower than the icon size set on a module).</summary>
    public static class ModuleFit
    {
        /// <summary>The icon size to draw: the one set, but never taller than the host minus the module's own padding / margins (and at least 10).</summary>
        public static double Icon(IModuleHost host, double wanted, double chrome)
        {
            if (host == null) return wanted;
            double room = host.HostHeight - chrome - 2;
            return Math.Max(10, Math.Min(wanted, room));
        }
    }

    /// <summary>What a module needs from the thing it sits in: a bar, or a desktop widget.</summary>
    [PluginApi]
    public interface IModuleHost
    {
        Theme Theme { get; }
        /// <summary>Height available to a module in DIPs: the bar height, or the widget's module row height.</summary>
        double HostHeight { get; }
        /// <summary>true = popups open below the host (it is near the top of the screen), false = above it.</summary>
        bool PopupsBelow { get; }
        bool FullWidth { get; }
        /// <summary>true = a desktop widget (modules may stack several rows), false = a bar (one row).</summary>
        bool IsWidget { get; }
        IntPtr Handle { get; }
        IntPtr MonitorHandle { get; }
        /// <summary>A module's size may have changed (only matters for auto-width bars).</summary>
        void ContentChanged();
        /// <summary>Open Settings on this host.</summary>
        void OpenSettings();
    }
}
