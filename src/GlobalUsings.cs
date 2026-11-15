// the suite's shared library (vSuite.Core submodule): vShell's updates come from it. vShell keeps its own Log (plugins call it by name),
// so the library's namespace isn't brought in whole - just these.
global using Updater = vSuite.Updater;
global using UpdateInfo = vSuite.UpdateInfo;
// the Wallpaper Theme's colours (vShell's WallpaperColor picks the picture, the library works out the colours)
global using WallpaperPalette = vSuite.WallpaperPalette;
