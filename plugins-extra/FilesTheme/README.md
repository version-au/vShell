# Files theme (plugin you add yourself)

Keeps the [Files](https://files.community) app in your vShell colours. Not part of vShell's release: `build.cmd` doesn't build
`plugins-extra\`, and it isn't in plugins.json.

## Build and add

```
dotnet build plugins-extra\FilesTheme\vShell.FilesTheme.csproj -c Release
```

Then in vShell: **Settings > Plugins > Add from a file…** and pick
`plugins-extra\FilesTheme\bin\Release\net10.0-windows\vShell.FilesTheme.dll`. It only uses the plugin API, so it keeps loading in newer
vShells with the same plugin API.

Versions: **2.0.0** is built for .NET 10 (plugin API 2, vShell 0.38.135 and later). 1.0.0 was the .NET 8 build (plugin API 1, vShell
0.38.134 and older) - it no longer loads in vShell 0.38.135+, so remove it and add the 2.0.0 DLL instead.

## What it does

- Finds Files' settings (`%LOCALAPPDATA%\Packages\Files_*\LocalState\settings\user_settings.json`) and writes the colours worked out from
  the vShell theme: window = Background, address bar / selected tab / toolbar = Background 2 (or a shade of Background without a
  gradient), file list = halfway between, sidebar see-through, dark / light mode from the text colour, Segoe UI. The backdrop (Solid /
  Mica / Acrylic) is its own setting.
- Files reads these only when it starts and writes its whole settings file back when it closes, so while it's running nothing is
  written: a notification offers a restart (click it). If you don't, the colours go in once Files isn't running (checked every 10 s).
- Files keeps the Windows accent colour and its own rounded corners (it has no settings for them).

## Testing

1. Add the plugin with Files installed: Settings > Plugins > Files theme says Files is installed.
2. Close Files fully (tray icon > Exit). Change the vShell theme: open Files - it has the new colours.
3. With Files open, change the theme: one notification "Files has new colours waiting". Click it: Files closes and reopens in the new colours.
4. Ignore the notification and quit Files from its tray icon: within ~10 s the colours are written; next start shows them.
5. Pick colours quickly in the theme editor: only one write / notification once you stop for a moment.
6. Backdrop Acrylic with a see-through vShell background: the wallpaper tints through Files.
7. Untick "Keep Files in my vShell colours": theme changes leave Files alone. "Apply to Files now" writes them straight away (restarting Files if open).
