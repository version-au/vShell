using System;
using System.Windows.Media;
using vShell.Core;

namespace vShell.Ui
{
    /// <summary>
    /// One of vShell's bars as a plugin sees it (BarManager.Bars): where it is and how it looks, so a plugin's own window can sit against
    /// it - flush under a top bar or over a bottom one, the way the drop-down terminal does. A snapshot: ask again on BarsChanged.
    /// </summary>
    [PluginApi]
    public sealed class BarInfo
    {
        /// <summary>The bar's window.</summary>
        public IntPtr Handle { get; internal set; }
        /// <summary>The monitor it's on (an HMONITOR).</summary>
        public IntPtr Monitor { get; internal set; }
        /// <summary>"Top" or "Bottom".</summary>
        public string Edge { get; internal set; } = "Bottom";
        /// <summary>The bar itself in device pixels (not its shadow or the room round it).</summary>
        public int Left { get; internal set; }
        public int Top { get; internal set; }
        public int Right { get; internal set; }
        public int Bottom { get; internal set; }
        /// <summary>Its corner radius and outline thickness in DIPs (0 = no outline), and the outline's brush.</summary>
        public double CornerRadius { get; internal set; }
        public double BorderThickness { get; internal set; }
        public Brush Outline { get; internal set; }
        /// <summary>The bar's theme (its colours, font and size).</summary>
        public Theme Theme { get; internal set; }
        /// <summary>A full-screen app is in front on its monitor (the bar is behind it).</summary>
        public bool BehindFullScreen { get; internal set; }
    }
}
