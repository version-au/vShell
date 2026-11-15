# Fans (vShell plugin)

Your fans' and pump's speeds, found for you: every reading HWiNFO or LibreHardwareMonitor reports in rpm (vShell's
"Sensors from" choice, in a Hardware sensor module's settings).

- **In a widget:** tiles two across or in one column (as the Tile style: the name, the speed in big numbers over a line of the last while),
  or rows (a fan in a ring, name, a graph - Line, Bars, Meter or Ring, with its length and height or the ring's size - and the speed).
- **On a bar:** tiles side by side, every fan side by side, one fan with a graph, or just the fan (the fastest speed, or which fan stopped).
  Click it for every fan in a popup.
- **A little fan** by each speed turns faster as the fan does (slowed right down - a turn in 6 seconds at 1,000 rpm) and stops at 0.
  It steps 10 times a second (a timer, not an animation) and stands still while a full-screen app is in front; it can be switched off.
- **Notifications** (Settings > Plugins > Fans > Notifications): a fan stops (only one that was turning; GPU fans can be left out), or runs
  slower than a speed - for all fans, or each its own (Warn below on the Settings tab). A stopped / slow fan also shows in the warning colour.
- **Settings tab:** rename the fans, leave some out, put them in your order; each fan's Max rpm (its graphs fill at it and Colour by load reaches its high colour there - empty = automatic), 0rpm mode (a fan that switches itself off when not needed: 0 rpm is normal, never "stopped" - set by itself for GPU fans) and its own Warn below.

HWiNFO: in its Sensors window right-click each fan and choose "Report value in Gadget". LibreHardwareMonitor shares every fan.

Made only with vShell's plugin API (`HwInfo`, `MetricView`, `BarManager.HistoryFor`, `Notify`), so it keeps working across vShell updates
with the same plugin API. Own version: 1.0.10.
