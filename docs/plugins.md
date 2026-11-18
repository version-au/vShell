# Making a vShell plugin

A plugin is a .NET 10 WPF class library (a DLL) that vShell loads at start. It can bring modules (for bars and widgets), a section on
the Plugins page, notifications, checks that run with the notifications, command palette commands, run box keywords, a Weather source,
a Notes store, and help for Now playing. People add it in **Settings > Plugins** - from the list (plugins in the vShell release) or
with **Add from a file…**.

## The plugin API and its version

- What a plugin may use is marked `[PluginApi]` in vShell. `plugin-api.txt` (in `dist\sdk\` next to `vShell.dll`) lists all of it, one
  line per type or member - the classes inside a base class a module uses (`TilesModule+Tile`, `InfoModule+Reading`) too.
- vShell has a **plugin API version** (`PluginApi.Version`, now **2**, since vShell 0.38.135). A plugin built against any vShell with
  API 2 loads in every vShell with API 2 - no new build for each release. Its API is the one of the vShell it was compiled against;
  there's nothing to write for it.

| Plugin API | vShell | .NET | `TargetFramework` |
|---|---|---|---|
| 1 | 0.38.84 - 0.38.134 | .NET 8 | `net8.0-windows` |
| **2** | **0.38.135 and later** | **.NET 10** | **`net10.0-windows`** |
- The number only goes up when something in the API has to change or go (vShell's build stops when that happens by accident). Then
  plugins need a new build.
- Before loading a plugin, vShell checks that every part of vShell it uses is there; one that isn't says why on the Plugins page
  instead of failing later.
- Public parts of vShell without `[PluginApi]` compile too, but they can change in any release. vShell's build (and Add from a file)
  lists them for a plugin: keep that list empty.

## Moving a plugin from .NET 8 (API 1) to .NET 10 (API 2)

vShell 0.38.135 moved from .NET 8 to .NET 10, and the plugin API went from 1 to 2 with it. **A plugin built for .NET 8 (API 1) no
longer loads**: the Plugins page says "it's for plugin API 1 … this vShell has API 2". (A .NET 10 plugin can't load in an older
.NET 8 vShell either, so older vShells keep their API 1 plugins and never download API 2 builds.) Nothing in the API itself was taken
away, so for most plugins the move is only a rebuild:

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (it can sit next to the .NET 8 one).
2. In your `.csproj`, change `<TargetFramework>net8.0-windows</TargetFramework>` to `<TargetFramework>net10.0-windows</TargetFramework>`.
3. Replace your `sdk\vShell.dll` (and `plugin-api.txt`) with the ones from a vShell 0.38.135 or later build.
4. Raise your plugin's `<Version>`, build it, and check the output for new warnings (.NET 10 marks a few more old APIs obsolete -
   `SYSLIB…` warnings; fix them if you can, they still build).
5. The DLL is now in `bin\Release\net10.0-windows\` (not `net8.0-windows\`). Share it as before: Add from a file…, or send it to V for
   the release. Keep the old .NET 8 build if you want to support people still on vShell 0.38.134 or older.
6. Any NuGet packages your plugin uses: check they support .NET 10 (most .NET 8 ones do) and update them if they offer a newer build.

## What you need

- The .NET 10 SDK.
- `vShell.dll` (and `plugin-api.txt`) - the Plugin SDK: Settings > About > Plugin SDK > Download, or
  https://vsuite.vrsion.com/vshell/sdk.zip (the newest vShell's; also `dist\sdk\` of a vShell build). Any vShell with the API version you want.

## The project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <AssemblyName>vShell.UptimeKuma</AssemblyName>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Version>1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="vShell"><HintPath>sdk\vShell.dll</HintPath><Private>false</Private></Reference>
  </ItemGroup>
</Project>
```

`Private=false`: vShell.dll is never copied next to the plugin - vShell itself is used when it loads.

## The manifest and the plugin class

```csharp
using vShell.Core;
using vShell.Ui;

[assembly: VShellPlugin("uptimekuma", "Uptime Kuma",
    Description = "Your Uptime Kuma monitors: a module with their status, and a notification when one goes down.",
    Glyph = "", Group = "Servers & network", Author = "Your name", Adds = "notifications", Types = "UptimeKuma")]

namespace UptimeKuma
{
    public sealed class Plugin : IPlugin
    {
        Poller _poll;

        public void Load(PluginHost host)
        {
            host.AddModule(new PluginModule
            {
                Type = "UptimeKuma", Display = "Uptime Kuma", ShortName = "Uptime", Glyph = "", Group = "Smart home & more",
                Create = delegate (BarManager mgr, IModuleHost h, ModuleConfig cfg) { return new MyModule(mgr, h, cfg); },
                Settings = delegate (SettingsWindow.SettingsPage p, ModuleConfig cfg) { p.Check("Only show ones that are down", … ); }
            });
            host.AddIntegration("Uptime Kuma", delegate (SettingsWindow.SettingsPage p) { p.Header("Uptime Kuma"); p.Text("Address", …); });
            host.SetUpWhen(delegate (BarSettings s) { return s.IntegrationValue("uptimekuma", "Url", "").Length > 0; });
            host.OnSettings(delegate (BarSettings s) { /* start / change the connection */ });
            host.AddNotifyKind("UptimeKuma.Down", NotifyLevel.Alert);
            host.AddNotifySource("Uptime Kuma");
            _poll = new Poller("Uptime Kuma", Poll, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
            _poll.Start();
        }

        public void Unload() { _poll.Stop(); }      // vShell takes away everything registered through the host itself
    }
}
```

- **The id** (first argument) is kept in people's settings: lower case, never changed once released, not one another plugin uses.
- **Types**: your module type names, comma separated. A module whose plugin isn't added stays in the settings and says "add it in Settings".

## PluginHost - what a plugin can register

Everything registered here is taken away again by vShell when the plugin is removed; `Unload` only has to stop your own timers.

| Method | What it adds |
|---|---|
| `AddModule(PluginModule)` | A module type: names, glyph, gallery group / blurb / pictures, `Create`, its `Settings` rows, `Needs`, `OnBar` / `InWidget`, `HasPopup`, `WidgetSize`. |
| `AddIntegration(title, build)` | Its section on the plugin's own page in Settings > Plugins, Connection tab. |
| `SetUpWhen(check)` | "Not set up yet" and a Set up button on the Plugins page (and a grey dot) until check says it's set up. |
| `OnSettings(configure)` | Called at start and whenever the settings change. |
| `AddNotifications(build)` | Its section on the plugin's page, Notifications tab (the Notifications page has a row that opens it). `NotifySection(title, ok, what)` starts a card; a `Note` after a row shows as an (i) on that row there. |
| `AddNotifyKind(key, level)` / `AddNotifySource(name)` | Its sorts of notification, and its name in the notification history. |
| `AddWatcher(name, check)` | A check run every 5 seconds with the notifications (for things that need asking). |
| `AddCommands(name, source)` | Command palette commands (`PaletteCommand`). |
| `OnPaletteOpening(warm)` | The palette is about to open: get the commands' data fresh. |
| `AddBang(kind, parse, rows, hint)` | A run box `!keyword`: parse the text into a `RunAction`, fill the drop-down (`RunPanel.AddRow`, or `Hold` + `AddElement` for results of its own). |
| `AddRunText(kinds, parse, rows)` | Text in the run box it knows (a magnet link). |
| `AddRunBoxDrop(attach)` | Files dropped on a run box. |
| `AddWeatherSource(source, settings)` | A source for the Weather module (`IWeatherSource`), and its rows in the module's settings. |
| `AddNoteStore(store)` | A store the Notes module can show (`NoteFeedStore` with an `INoteFeed`). |
| `AddCardLinks(handler)` | Its own links on notification cards ("myplugin:…"): return true when the link was one of its. |
| `AddMediaHelper(helper)` | Help for Now playing (`IMediaHelper`: times, art, buttons). |
| `Manager.Bars()` / `Manager.BarsChanged` | vShell's bars (0.38.179+): each one's monitor, edge (Top / Bottom), rectangle in device pixels, corner radius, outline and theme, and whether a full-screen app is in front - for a window of your own that sits against a bar (Shelf's edge tabs). `BarsChanged` fires when one is added, removed, moved or goes behind / comes out from behind a full-screen app; ask `Bars()` again then. |
| `AddCommandLine(verb, run)` | Something vShell is started with for it (vShell 0.38.175+): `vShell.exe --plugin <your id> <verb> args…` - from an Explorer right-click entry or a shortcut you set up. `run` gets the args on the UI thread in the running vShell (a second start hands them over and ends; if vShell wasn't running it starts and then runs them). |
| `SettingsPage.GraphLook(cfg, style)` | The graph look rows that fit one style, as vShell's own modules show them (vShell 0.38.185+): Line / Tile - thickness, fill, curves; Line / Bars - box, rounding, grid; those - time shown; Meter / Ring - none; then the graph colour and Colour by load (0.38.186+). For a module that keeps its own style (`GraphLook(cfg)` shows every row). |
| `HwInfo.All` / `Find(key)` / `Available` / `SourceName` / `IsLhm` | The hardware sensor values (vShell 0.38.182+) - from HWiNFO or LibreHardwareMonitor, whichever is picked in Settings: each `HwReading` has `Sensor`, `Label`, `Value`, `Text` (as shown, "1,250 RPM"), `Unit` and `Key` ("sensor|label", what to save). Read them in your module's update (fresh about once a second); don't change them. |
| `SettingsPage.OpenPlugin(id, heading)` | Go to a plugin's own page in Settings > Plugins, on its settings (vShell 0.38.180+) - a link from your module's settings to the plugin's; `heading` scrolls to the section with that `Header`. |

## Settings

- Keep yours in a `SettingsBag` - `s.Integration("uptimekuma")` for the connection, `cfg.Bag` for a module's - with `Get(key, default)` /
  `Set(key, value)`, `GetSecret` / `SetSecret` (kept encrypted), and `Live(key, make)` for a list you edit in place.
- `SettingsWindow.SettingsPage` has the rows vShell's own settings use: `Header`, `Note`, `Text`, `Secret`, `Check`, `Combo`, `Slider`,
  `Number`, `Color`, `Button`, `Test`, `Row`, `Section`, `Redraw`, `Changed` …

## Modules

- A module is a WPF element. `IModuleHost` (its bar or widget) gives the `Theme`, `IsWidget`, `ContentChanged()`, `OpenSettings()`.
- Ready-made bases, updated by vShell like its own modules: `InfoModule` (a reading or gauge: override `Read(Reading r, bool addSample)` and fill in `r`) and
  `TilesModule` (a list of tiles). `MetricView` draws a graph; `Pills`, `StatusColors`, `ModulePics` (gallery pictures) help with the rest.
- Speed graphs that scale themselves (vShell 0.38.130+): in `Read`, set `r.AutoScale = true` and put the amounts (e.g. MB/s) in `r.Percent` /
  `r.Percent2` instead of percent; `r.ScaleMin` is the smallest full scale and `GraphScale` the one in use (for the tooltip). A `MetricView` of
  your own: give it amounts and set `FullScale = MetricView.ScaleFor(history, history2, min)` each time a sample is added. The module's
  setting is `ModuleConfig.AutoScale`.
- Graph look (vShell 0.38.139+): an `InfoModule` and `SettingsPage.GraphSettings` get it by themselves (line thickness, fill, smooth curves,
  box, grid, time shown, colour by load). A `MetricView` of your own: call `view.ApplyLook(cfg, theme)` after setting it up, and add the rows to
  the module's settings with `p.GraphLook(cfg)` (all of them, whatever the module's Style). With your own automatic scale, pass the time shown
  (vShell 0.38.140+): `MetricView.ScaleFor(history, null, min, cfg.GraphSpan)`, so the scale covers everything the line shows.
- Dots (vShell 0.38.227+) - the dot-matrix look of vShell's own Dots style, three ways:
  - **A graph in dots:** a `MetricView` with `GraphStyle = "Dots"`. With a history (`Data`) it draws columns of dots on a dim dot field
    (`Track`), the newest in `Fill`, the rest in the theme's text colour (after `ApplyLook`); `Data2` + `Value2 >= 0` mirrors a second series
    below the middle (`Fill2`). Without a history it's a meter: a row of dots filling with `Value` (two rows with `Value2`). Warning, colour
    by load and `FullScale` work as in the other styles. On an older vShell it draws a meter, so offering "Dots" is safe.
  - **A whole module face:** `new DotFace(history, history2 = null)` - `Setup(cfg, host, theme, tile, warnPercent = NaN, meter = false)`
    (again when the settings or theme change; `tile` = the widget layout, for an `IMultiRow` module asking for 0 rows), then each reading
    `Show(label, number, unit, detail, warn, percent, percent2)` and, for amounts, `FullScale`. The detail (pass it only when the module's
    settings ask for it) goes after the number in a bar and at the top right of a tile; `Temp` (set before `Show`, vShell 0.38.228+) follows
    it in the Secondary Accent colour. It follows the module's Numbers in dots
    (`cfg.Bag` "dots.numbers"), Show text (the label), Graph length / height, Time shown and colour by load. An `InfoModule` with
    `HasGauges` gets this by itself when its Style is "Dots".
  - **Numbers alone:** `DotText.Draw(dc, text, x, y, cell, brush)` (returns the right edge), `DotText.Width(text, cell)`,
    `DotText.CanDraw(text)` - digits, `% . , ° - + /` and a few unit letters (K M G B C F H V W Z S); a dot is 0.84 of `cell`, a character
    7 cells tall.
- `IMultiRow.RowCount == 0`: it sizes itself in a widget. A text box in a widget calls `WidgetWindow.BeginTyping` / `EndTyping`
  (`ITypingModule`). `WidgetWindow.StyleScrollbars(el)` gives a scroll area of its own the widget's thin scrollbars.
- `PopupWindow` for a popup. A long list goes in a `ScrollViewer` with `MaxHeight = popup.ContentRoom()` (after `UseWidgetStyle`, if
  you call it): the most room there is between the bar and the edge of the screen, so it fits any monitor without a fixed height.
- `Questions.YesNo` / `FormWindow.Ask` / `PromptWindow.Ask` for questions (never owned by a widget).
- An animation can rest while nobody can see it: `ModuleVisibility.Covered(host)` (locked PC, a full-screen window over it).

### Tiles (`TilesModule`)

A row of small readouts - one per server, entity, monitor. Subclass it directly (don't wrap it in an element of your own: vShell only
updates, and sizes in a widget, a `TilesModule` it can see).

```csharp
public sealed class MyServers : TilesModule
{
    int _built;      // how many servers the tiles are for

    public MyServers(BarManager mgr, IModuleHost bar, ModuleConfig cfg) : base(mgr, bar, cfg) { Build(); }

    protected override void BuildTiles()
    {
        _built = MyClient.Servers.Count;
        foreach (Server s in MyClient.Servers)
        {
            Server cap = s;
            Tile t = null;
            t = AddTile(true, false, delegate { OpenCard(cap, t.Root); });     // dot: true, meter: false, click
        }
    }

    public override void Update()      // called by vShell with its other modules (every few seconds)
    {
        if (MyClient.Servers.Count != _built) { Rebuild(); return; }     // servers came or went (Rebuild calls Update)
        // no tiles made: vShell adds an empty one to show a message in
        if (_built == 0) { SetText(Tiles[0], "My servers: none yet", T.FgDim, null, false); return; }
        for (int i = 0; i < Tiles.Count; i++)
        {
            Server s = MyClient.Servers[i];
            SetDot(Tiles[i], s.Up ? StatusColors.Good : StatusColors.Bad);
            SetText(Tiles[i], s.Name, T.Fg, s.Name + "\n" + s.Detail);
        }
    }
}
```

- `AddTile(dot, meter, click)` makes a tile; its parts are `Root` (the whole tile), `Text`, `Dot`, `Meter` (a `MetricView`), `Click`.
- `SetText(tile, text, brush, tip)` keeps a tile from getting narrower as its text changes; `SetDot(tile, brush)` colours its dot
  (`StatusColors.Good / Warn / Bad`, `T.FgDim` for unknown).
- `Rebuild()` when the set of tiles changes: it calls `BuildTiles` and `Update` again, and a widget showing the tiles one per row grows
  or shrinks with it. Only call it when the set changed, not on every update - and compare with a count you keep (as above), not
  `Tiles.Count`: with nothing to show there's still one (empty) tile.
- A card for one tile: `new PopupWindow(Mgr, Bar, tile.Root, T, content)` opens it at that tile (anchor it to `this` for the whole module).
- `RowCount` (one row per tile in a widget's list layout) is read again whenever you call `Bar.ContentChanged()`.

### Command palette results

`PaletteCommand.Run` returns `null` (or `""`) when it worked. Anything else is shown as an **error**, so for a message on success return
`PaletteCommand.Worked("Restarting jellyfin (job 413)")`: the run box shows it with a ✓ and closes a moment later.

## Helpers

- `PluginHttp.Client(name, ignoreCert, timeoutSeconds, followRedirects)` - one cached `HttpClient` per name (making one per request
  uses up sockets); it follows redirects itself (not https -> http). `PluginHttp.GetFollowAsync` / `GetStringAsync` with a client made
  with `followRedirects: false` follow every hop, also https -> http, and send your headers (an API key) only to the same server -
  never to another host, never over plain http after https.
- `Poller(name, poll, fast, slow)` - polls in the background: fast while a module calls `Want()` (for 30 s after), slow otherwise
  (notifications), `PollNow()` after a change. `Start` / `Stop`.
- `JsonValue.Num / Str / Bool(element, name)` - values from JSON without the checks (NaN / "" when missing).
- `Notify.Post(…)` (a notification), `Log.Write(…)` (vShell's log), `TimeText.Clock / Stamp / Ago` (times as the user has them), `Secret`.

## Sharing it, and updates

- **Anyone**: send the DLL; people add it with **Add from a file…** (vShell shows its name, author and description and asks first - a
  plugin runs with vShell's access). It keeps working after updates while the plugin API is the same; a newer build is added the same way.
- **Through vShell's releases**: send it to V. V checks it, puts the DLL in `third-party\`, and it's built into the release's
  `plugins.json` with the others - then it's in everyone's Plugins list. (The build lists anything it uses outside the plugin API.)
- **Updates**: a newer build of a plugin in a release (its own version higher, `plugins.cmd` writes the list) is an update - vShell finds
  it (by itself every 6 hours, or Check for updates on the Plugins page), downloads it and uses it from the next start. No new vShell is
  needed. A build compiled against a newer vShell still runs on an older one with the same plugin API as long as everything it uses is
  there - vShell checks that from `plugins.json` before downloading, and says when it needs a newer vShell.
- Give your plugin a `<Version>` and raise it for each build you share, so vShell can tell which is newer.