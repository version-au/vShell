# Module and plugin API - design (draft for V to agree)

Status: **in progress**. 0.38.84: the plugin API - `[PluginApi]` marks what plugins may use, an API version (1) so plugins load across vShell versions, every plugin (V's too) on it with no InternalsVisibleTo, and build.cmd guarding it (see docs/plugins.md). Done in 0.38.77: the command palette (phase 0), and plugins you add / remove from Settings > Plugins, with
Komodo moved out into `plugins\Komodo` (vShell.Komodo.dll) as the first one - phases 1, 2 and 4 for Komodo, in the shape below but simpler:
`IPlugin` + `PluginHost` + `PluginModule` in `src/Ui/Plugins.cs`, `SettingsWindow.SettingsPage` for settings, `PluginStore` for files and
downloads. First-party plugins use vShell's internals (`InternalsVisibleTo`) and are built for one exact vShell version, so there's no
separate SDK yet - that's what other people's plugins would need (a stable public surface). 0.38.78: plugins keep their own settings
(`SettingsBag`, below); Komodo's `Ko*` fields moved into it. 0.38.79: Seerr is the second plugin (`plugins\Seerr`, vShell.Seerr.dll); 0.38.80: 3D printers (`plugins\Printer`), whose list of printers is a `Live` value in the bag. 0.38.81 - 0.38.82: Downloads, UniFi, Jellyfin, Proxmox and Home Assistant (with vDrop) - every connection is a plugin now; vShell offers hooks for what they add to its own modules (Weather sources, Notes stores, Now playing helpers).
V's answers: plugins by others may come later; cheaper to add is the goal now.

## Why

Adding a module or an integration today touches about 14 places (the checklist in CLAUDE.md):

| Where | What goes there |
|---|---|
| `ModuleTypes` (Settings.cs) | the type const, `All` / `WidgetTypes`, `Display`, `ShortName`, `NotifySources` |
| `ModuleConfig` (Settings.cs) | its fields (prefixed `Ko*`, `Pt*` …) and their normalisation - 570 properties on one class now |
| `BarWindow`, `WidgetWindow` | a `case` each in the module creation switches (32 cases each) |
| `SettingsWindow` | `PopupModules`, gallery `Groups` + `Blurb`, the module inspector `case`, Plugins page section, Notifications section (`NotifySection`, `KindPick`) |
| `SettingsWindow.Gallery.cs` | the "needs the X connection" text |
| `ModulePics.cs` | bar and widget previews |
| `Icons.cs` | the glyph |
| `BarManager.ApplyHa` | `Configure(Settings)` of the Core client |
| `Notify.Kinds`, `NotifyWatch.Hook`, `NotifyCards.Glyph` | notification kinds and the hook |
| `RunParser` / `RunPanel` | `!bang` keyword and its rows |

Each new integration in this style adds to the cost of changing it later. The aim is that a module or an integration is **one folder of
code that registers itself**, and the rest of vShell (bars, widgets, the settings window, notifications, the run box) asks the registry.

## What it is (two levels)

1. **Integration** (`IIntegration`) - a connection to something (Komodo, Proxmox, a printer): its settings, the Plugins page section,
   `Configure`, notification kinds, run-box bangs and palette commands. Most have one module, some several (Home Assistant), some none.
2. **Module type** (`IModuleType`) - something you put on a bar or in a widget: its name, glyph, gallery group, preview, settings inspector
   and how to make one.

Both are registered at start-up into `Registry`. Built-in ones register from their own files; plugins (later) the same way.

## The interfaces (sketch)

```csharp
// Core (no WPF)
public interface IIntegration
{
    string Id { get; }                      // "komodo" - also the settings key
    string Name { get; }                    // "Komodo"
    bool Configured(BarSettings s);
    void Configure(BarSettings s);          // what BarManager.ApplyHa calls today
    IEnumerable<NotifyKind> NotifyKinds { get; }      // key, name, default level (Notify.Kinds today)
    IEnumerable<RunBang> Bangs(BarSettings s);        // "!ko" -> its rows (needs a UI half, see below)
    void Commands(BarSettings s, List<PaletteCommand> into);   // the palette - exists now as Palette.Source
}

// Ui
public interface IModuleType
{
    string Type { get; }                    // "Komodo" (stays the same string as ModuleTypes today - settings files keep working)
    string Display { get; }  string ShortName { get; }  string Glyph { get; }
    string Group { get; }    string Blurb { get; }       // the module gallery
    bool OnBar { get; }      bool InWidget { get; }      bool HasPopup { get; }
    string Needs(BarSettings s);            // "Needs the Komodo connection (Plugins)" or null
    void Defaults(ModuleConfig m);          // new-module defaults (ModuleConfig.Create today)
    void Normalize(ModuleConfig m);
    FrameworkElement Create(BarManager mgr, IModuleHost host, ModuleConfig cfg);
    FrameworkElement Preview(Theme t, bool widget);      // ModulePics
    void Inspector(SettingsPage page, ModuleConfig cfg); // the module settings pane
}
```

`SettingsPage` is a small builder over the helpers the settings window already uses (`ComboRow`, `Note`, check boxes, number rows,
pickers), so a module describes its settings without reaching into `SettingsWindow`'s 4,000 lines.

## Settings storage

The hard part. Today every module's settings are properties on `ModuleConfig`, serialised flat into `settings.json`.

- **Built-in modules keep their properties** for now - no settings file change, nothing to migrate. Moving them out is optional and
  can happen one module at a time.
- **Plugin modules and integrations** keep a `SettingsBag` (`src/Core/SettingsBag.cs`, done in 0.38.78): a module's is `cfg.Bag`
  (saved as `"Extra"`, only when it has one), an integration's `s.Integration("komodo")` (saved under `"Integrations"`).
  `Get(key, default)` gives the default for a missing value or one of another type; `Set(key, value)`. Reads are safe from a poller thread.
  `s.IntegrationValue(id, key, default)` reads without making the bag (for `InUse`).
- A plugin wraps its bag in a small typed class that holds its defaults and clean-up (clamping, lists of choices) in the getters -
  Komodo's `KoSettings` (connection, notifications) and `KoView` (a module), in `plugins\Komodo\Komodo.cs`. Keys are plain names ("Url",
  "Count"), no prefix: a module's bag is its own.
- A list of objects the settings page edits in place (the printers): `bag.Live("Printers", make)` gives the same object every time and
  saves whatever it holds - no `Set`. `Get` on that key still works (a copy).
- Secrets: stored as `Secret.Protect` text; the catalog entry's `Secrets` lists them so `BarSettings.Normalize` encrypts any plain one.
- Moving an integration's old fields in: list them in its catalog entry (`OldSettings` / `OldModuleSettings`, old field -> key).
  Fields vShell no longer has land in `BarSettings.Old` / `ModuleConfig.Old` (`[JsonExtensionData]`) when loading; `Normalize` moves the
  listed ones into the bags and drops the rest (as unknown fields always were). So remove the fields from `Settings.cs` in the same change.

## Plugins from outside vShell (later - needs a decision)

Two ways, not exclusive:

| | .NET plugin DLLs | Scripted widgets |
|---|---|---|
| What | A DLL in `%APPDATA%\vShell\plugins` implementing the interfaces | A folder with `widget.json` + HTML / JS, shown in WebView2 |
| Can do | Everything a built-in can | Draw a widget from data it fetches; settings from the manifest; maybe notifications and palette commands through a small JS API |
| Needs | A separate `vShell.Sdk.dll` (the interfaces + `Theme`, `PaletteCommand` …) that both vShell and plugins reference - vShell is a single-file exe, so plugins can't compile against it directly; `AssemblyLoadContext` per plugin; an SDK version check | WebView2 (already shipped for the terminal); a layered-window workaround like `TermSurface`, since widgets are layered windows |
| Risk | Full trust: a plugin can do anything vShell can. Only load ones you put there; show them in Settings with an off switch | Sandboxed by WebView2; much safer to share |
| Effort | Medium once the registry exists | Large (hosting + API), independent of the registry |

My suggestion: the registry first (it pays off for built-ins alone), then DLL plugins, and scripted widgets only if there's a reason to share
widgets with other people.

## Phases

0. **Done (0.38.77)**: the command palette - `Palette.Register(name, source)`, sources per integration (`PaletteSources`, `PaletteUi`).
   This is the shape the rest follows: an integration contributes, the run box asks.
1. **Registry + `IModuleType`** for the creation switches, gallery, glyphs, previews and "needs" text. Move 2-3 modules over (Komodo,
   Printer, Downloads) to prove it; the old switches stay as the fallback for the rest. No settings change. One version.
2. **`IIntegration`**: `Configure`, notification kinds / hook, bangs and palette commands; the Plugins and Notifications pages built
   from the registry. Move the integrations over one by one.
3. **`SettingsPage` builder** and `ModuleConfig.Extra`; move module inspectors over as they're touched.
4. **Plugin loading** (DLLs, `vShell.Sdk`), a Plugins page in Settings, an example plugin (e.g. Uptime Kuma or Pi-hole).

Each phase leaves vShell working and shippable; modules move over when they're next changed anyway, not all at once.

## Questions for V

1. Is outside plugins (phase 4) a goal, or is this mainly to make adding your own integrations cheaper? If the latter, phases 1-3 are enough.
2. OK to leave the built-in modules' settings where they are (no settings migration), and only put new modules' settings in `Extra`?
3. Which module first for phase 1? I'd take Komodo (popup, inspector, notifications, bang and palette - it touches every part).
