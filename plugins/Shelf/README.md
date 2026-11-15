# Shelf for vShell

A place to park files, folders, text and pictures while you work, plus Windows' clipboard history - made only with vShell's plugin API.

- **Shelves**: coloured tabs (Pocket and Shelf to start with; + for more). Drop files, folders, drives, text, pictures or web addresses on
  one. Files and folders stay where they are - the shelf only remembers where they are; text, pictures and links become small cards
  (`%APPDATA%\vShell\shelf\cards`).
- **Drag out** to copy (Shift moves, Alt makes a shortcut - or set dragging to move). Something moved away leaves the shelf. A **Pocket**
  lets go of everything you drop somewhere: gather files from different folders, then drag them out together.
- **Explorer**: "Add to shelf" in the right-click menu (Show more options on Windows 11) - `vShell.exe --plugin shelf add "path"`.
- **Clipboard**: Windows' own clipboard history (Win+V) as a tab - click to copy one again, drag it into an app, keep it on a shelf.
- **Module**: on a bar a button (drop on it; hold a drag over it and the shelves open; click for the popup), in a widget the shelves,
  one shelf or the clipboard.

Settings > Plugins > Shelf: the Explorer menu, where Explorer's adds go, what dragging out does, the Clipboard tab.
