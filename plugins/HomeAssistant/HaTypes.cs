using System;
using System.Collections.Generic;

namespace vShell.Core
{
    /// <summary>A Climate module zone: a Home Assistant fan entity (an AirTouch zone damper) and the name shown for it.</summary>
    public sealed class ClimateZone
    {
        public string Entity { get; set; } = "";
        public string Name { get; set; } = "";
    }

    /// <summary>One Home Assistant entity shown by the Home Assistant module.</summary>
    public sealed class HaEntityRef
    {
        /// <summary>With Bubble Card buttons: "Auto", "Switch" (click toggles), "Slider" (drag sets brightness / speed / position / value) or "State" (shows it).</summary>
        public string BubbleType { get; set; } = "Auto";
        public string EntityId { get; set; } = "";
        /// <summary>Empty = Home Assistant's friendly name.</summary>
        public string Label { get; set; } = "";
        /// <summary>Empty = the entity's own unit.</summary>
        public string Unit { get; set; } = "";
        /// <summary>-1 = automatic.</summary>
        public int Decimals { get; set; } = -1;
        /// <summary>Clicking the tile toggles / triggers the entity (lights, switches, scenes, scripts, buttons ...).</summary>
        public bool Clickable { get; set; } = true;
        /// <summary>"None", "Meter", "Bars", "Line" or "Dots" (vShell 0.38.227's dot graph): a small graph drawn next to this entity's value (numeric entities; on/off count as 100/0).</summary>
        public string GraphStyle { get; set; } = "None";
        /// <summary>The value that shows as an empty / a full graph.</summary>
        public double GraphMin { get; set; } = 0;
        public double GraphMax { get; set; } = 100;
        public int GraphWidth { get; set; } = 60;
        /// <summary>Graph height as a percentage of the bar / module row height.</summary>
        public int GraphHeightPercent { get; set; } = 60;
        /// <summary>Hex colour of the graph; empty = the module's graph colour (Main Accent by default).</summary>
        public string GraphColor { get; set; } = "";
        /// <summary>"Right" = the graph after the text (at the right edge of the row in a widget list), "Left" = before the text.</summary>
        public string GraphAlign { get; set; } = "Right";
        /// <summary>Show the value (and graph) in the Secondary Accent colour when it crosses WarnAt (in the entity's own unit).</summary>
        public bool WarnOn { get; set; } = false;
        public double WarnAt { get; set; } = 0;
        public bool WarnBelow { get; set; } = false;
        /// <summary>Old fade setting (the fade is now always on); kept so old settings load.</summary>
        public double WarnBlend { get; set; } = 0;
        /// <summary>Warning colour, same format as GraphColor; empty = the Secondary Accent.</summary>
        public string WarnColor { get; set; } = "";
        /// <summary>The threshold follows the graph: 70 % of "Graph: empty at" - "full at". Entities added from now on start with it on;
        /// typing your own threshold turns it off.</summary>
        public bool WarnAuto { get; set; } = false;

        /// <summary>A newly added entity: the warning on, at 70 % of its graph range.</summary>
        public static HaEntityRef Fresh(string id)
        {
            var r = new HaEntityRef { EntityId = id, WarnOn = true, WarnAuto = true };
            r.SyncWarn();
            return r;
        }

        public void SyncWarn()
        {
            if (!WarnAuto) return;
            double v = GraphMin + 0.7 * (GraphMax - GraphMin);
            WarnAt = Math.Round(v, Math.Abs(v) >= 100 ? 0 : 1);
        }
    }

    /// <summary>A Home Assistant notification rule: when the entity does something, show the message.</summary>
    public sealed class HaNotifyRule
    {
        public static readonly string[] Whens = { "Becomes", "Above", "Below", "Changes" };
        public static readonly string[] Clicks = { "Nothing", "Open Home Assistant", "Toggle" };

        public bool Enabled { get; set; } = true;
        public string Entity { get; set; } = "";
        /// <summary>Becomes (a state), Above / Below (a number), Changes (any change).</summary>
        public string When { get; set; } = "Becomes";
        public string Value { get; set; } = "";
        /// <summary>It has to stay that way this many minutes first (0 = straight away).</summary>
        public int ForMinutes { get; set; } = 0;
        /// <summary>The text; {name} {state} {unit} {entity} are filled in. Empty = "{name} is {state}".</summary>
        public string Message { get; set; } = "";
        /// <summary>Nothing, Open Home Assistant, Toggle.</summary>
        public string Click { get; set; } = "Open Home Assistant";
        /// <summary>Normal, Important or Alert (NotifyLevel). Empty = from Important (rules from 0.32.0).</summary>
        public string Level { get; set; } = "";
        /// <summary>0.32.0 rules: shown as important (now Level).</summary>
        public bool Important { get; set; } = false;

        [System.Text.Json.Serialization.JsonIgnore]
        public NotifyLevel LevelKind { get { NotifyLevel l; return Enum.TryParse(Level, out l) ? l : NotifyLevel.Normal; } }

        public void Fix()
        {
            if (Entity == null) Entity = "";
            Entity = Entity.Trim();
            if (Array.IndexOf(HaNotifyRule.Whens, When) < 0) When = "Becomes";
            if (Value == null) Value = "";
            if (Message == null) Message = "";
            if (Array.IndexOf(HaNotifyRule.Clicks, Click) < 0) Click = "Open Home Assistant";
            if (Array.IndexOf(ModuleTypes.NotifyLevels, Level) < 0) Level = Important ? "Important" : "Normal";
            Important = Level != "Normal";
            ForMinutes = Math.Max(0, Math.Min(1440, ForMinutes));
        }

        public string Describe()
        {
            string w = When == "Changes" ? "changes" : When == "Above" ? "goes above " + Value : When == "Below" ? "goes below " + Value : "becomes " + Value;
            return Entity + " " + w + (ForMinutes > 0 ? " for " + ForMinutes + " min" : "");
        }
    }
}
