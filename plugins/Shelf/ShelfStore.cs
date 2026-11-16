using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using vShell.Core;

namespace VShellShelf
{
    /// <summary>One thing on a shelf: a file / folder where it is (only its path is kept), or a card vShell made (text, a picture, a link) in the cards folder.</summary>
    public sealed class ShelfItem
    {
        public string Id { get; set; } = "";
        /// <summary>file, folder, text, image, link (the last three are card files in ShelfStore.CardsFolder).</summary>
        public string Kind { get; set; } = "file";
        public string Path { get; set; } = "";
        public DateTime Added { get; set; }

        public bool IsCard { get { return Kind == "text" || Kind == "image" || Kind == "link"; } }

        /// <summary>The name shown: the file / folder name, a drive as "C:\".</summary>
        public string Name
        {
            get
            {
                string p = (Path ?? "").TrimEnd('\\');
                string n = System.IO.Path.GetFileName(p);
                return n.Length > 0 ? n : Path;
            }
        }

        /// <summary>The folder it's in ("" for a drive).</summary>
        public string Folder
        {
            get
            {
                try { return System.IO.Path.GetDirectoryName((Path ?? "").TrimEnd('\\')) ?? ""; }
                catch { return ""; }
            }
        }
    }

    /// <summary>A shelf: a name, a colour and its items. A Pocket lets go of what you drag out of it.</summary>
    public sealed class Shelf
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "Shelf";
        public string Color { get; set; } = "#4C9AFF";
        public bool Pocket { get; set; }
        public List<ShelfItem> Items { get; set; } = new List<ShelfItem>();
    }

    public sealed class ShelfData
    {
        public List<Shelf> Shelves { get; set; } = new List<Shelf>();
    }

    /// <summary>
    /// The shelves, kept in %APPDATA%\vShell\shelf\shelves.json (saved half a second after a change). Everything here runs on the UI
    /// thread; Changed tells the views to draw again.
    /// </summary>
    public static class ShelfStore
    {
        public static readonly string[] Colors = { "#4C9AFF", "#E8A33D", "#4CC38A", "#E5534B", "#A371F7", "#3DC9C9", "#E870B5", "#9AA4AF" };
        public static readonly string[] ColorNames = { "Blue", "Amber", "Green", "Red", "Purple", "Teal", "Pink", "Grey" };

        public static string Folder { get { return System.IO.Path.Combine(BarSettings.ConfigDir, "shelf"); } }
        public static string CardsFolder { get { return System.IO.Path.Combine(Folder, "cards"); } }
        static string FileName { get { return System.IO.Path.Combine(Folder, "shelves.json"); } }

        static ShelfData _data;
        static DispatcherTimer _save;
        static bool _noSave;

        /// <summary>Something on a shelf changed (on the UI thread).</summary>
        public static event Action Changed;

        public static List<Shelf> Shelves { get { Load(); return _data.Shelves; } }

        static void Load()
        {
            if (_data != null) return;
            bool there = false;
            for (int tries = 0; tries < 4 && _data == null; tries++)
            {
                try
                {
                    there = File.Exists(FileName);
                    if (there) _data = JsonSerializer.Deserialize<ShelfData>(File.ReadAllText(FileName));
                    break;
                }
                catch (IOException ex) when (tries < 3) { Log.Write("Shelf: shelves.json - " + ex.Message + " - trying again"); System.Threading.Thread.Sleep(150); }
                catch (Exception ex) { Log.Write("Shelf: couldn't read shelves.json - " + ex.Message); break; }
            }
            if (_data == null && there)
            {
                // there but unreadable: the new, empty shelves must never be saved over it - keep it aside, or save nothing this run
                try
                {
                    string kept = System.IO.Path.Combine(Folder, "shelves.broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                    File.Copy(FileName, kept, true);
                    Log.Write("Shelf: the shelves.json that couldn't be read is kept as " + kept);
                }
                catch (Exception ex)
                {
                    _noSave = true;
                    Log.Write("Shelf: couldn't keep a copy of shelves.json (" + ex.Message + ") - nothing is saved over it until vShell starts again");
                }
            }
            if (_data == null) _data = new ShelfData();
            if (_data.Shelves == null) _data.Shelves = new List<Shelf>();
            _data.Shelves.RemoveAll(delegate (Shelf s) { return s == null; });
            foreach (Shelf s in _data.Shelves)
            {
                if (string.IsNullOrEmpty(s.Id)) s.Id = NewId();
                if (s.Items == null) s.Items = new List<ShelfItem>();
                s.Items.RemoveAll(delegate (ShelfItem i) { return i == null || string.IsNullOrEmpty(i.Path); });
                foreach (ShelfItem i in s.Items) if (string.IsNullOrEmpty(i.Id)) i.Id = NewId();
                if (string.IsNullOrWhiteSpace(s.Name)) s.Name = "Shelf";
            }
            if (_data.Shelves.Count == 0)
            {
                _data.Shelves.Add(new Shelf { Id = NewId(), Name = "Pocket", Color = "#E8A33D", Pocket = true });
                _data.Shelves.Add(new Shelf { Id = NewId(), Name = "Shelf", Color = "#4C9AFF" });
            }
        }

        static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 12); }

        /// <summary>Tell the views and save soon.</summary>
        static void Touch()
        {
            Action h = Changed;
            if (h != null) h();
            if (_save == null)
            {
                _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _save.Tick += delegate { _save.Stop(); SaveNow(); };
            }
            _save.Stop();
            _save.Start();
        }

        public static void SaveNow()
        {
            if (_data == null || _noSave) return;
            try
            {
                Directory.CreateDirectory(Folder);
                string tmp = FileName + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, FileName, true);
            }
            catch (Exception ex) { Log.Write("Shelf: couldn't save shelves.json - " + ex.Message); }
        }

        public static Shelf Find(string id)
        {
            return Shelves.Find(delegate (Shelf s) { return s.Id == id; });
        }

        /// <summary>The shelf with that id, else the first that isn't a Pocket, else the first.</summary>
        public static Shelf Default(string id)
        {
            Shelf s = string.IsNullOrEmpty(id) ? null : Find(id);
            return s ?? Shelves.Find(delegate (Shelf x) { return !x.Pocket; }) ?? Shelves[0];
        }

        public static int TotalCount
        {
            get { int n = 0; foreach (Shelf s in Shelves) n += s.Items.Count; return n; }
        }

        /// <summary>A file / folder / drive is there (false for an empty path; quick for local paths, a network path can take a moment).</summary>
        public static bool Exists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return File.Exists(path) || Directory.Exists(path); }
            catch { return false; }
        }

        // =================================================================
        // Adding
        // =================================================================

        /// <summary>Files / folders by path (ones already on that shelf are skipped). Returns how many were added.</summary>
        public static int Add(Shelf shelf, IEnumerable<string> paths)
        {
            int n = 0;
            foreach (string p0 in paths)
            {
                string p = (p0 ?? "").Trim();
                if (p.Length == 0) continue;
                if (shelf.Items.Exists(delegate (ShelfItem i) { return string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase); })) continue;
                bool dir = false;
                try { dir = Directory.Exists(p); } catch { }
                shelf.Items.Insert(0, new ShelfItem { Id = NewId(), Kind = dir ? "folder" : "file", Path = p, Added = DateTime.UtcNow });
                n++;
            }
            if (n > 0) Touch();
            return n;
        }

        /// <summary>Text as a card: a web address becomes a link (.url, an internet shortcut), anything else a .txt.</summary>
        public static ShelfItem AddText(Shelf shelf, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = text.Trim();
            Uri uri;
            bool link = t.IndexOfAny(new[] { ' ', '\n', '\r', '\t' }) < 0 && Uri.TryCreate(t, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https");
            string file;
            if (link)
            {
                Uri u = new Uri(t);
                file = CardFile(u.Host + (u.AbsolutePath.Length > 1 ? " " + u.AbsolutePath.Trim('/').Replace('/', ' ') : ""), ".url");
                if (!WriteCard(file, "[InternetShortcut]\r\nURL=" + t + "\r\n")) return null;
            }
            else
            {
                string first = t.Split('\n')[0].Trim();
                file = CardFile(first.Length > 0 ? first : "Text", ".txt");
                if (!WriteCard(file, text)) return null;
            }
            var item = new ShelfItem { Id = NewId(), Kind = link ? "link" : "text", Path = file, Added = DateTime.UtcNow };
            shelf.Items.Insert(0, item);
            Touch();
            return item;
        }

        /// <summary>A picture as a .png card.</summary>
        public static ShelfItem AddImage(Shelf shelf, BitmapSource image)
        {
            if (image == null) return null;
            string file = CardFile("Picture " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss"), ".png");
            try
            {
                Directory.CreateDirectory(CardsFolder);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(image));
                using (var fs = File.Create(file)) enc.Save(fs);
            }
            catch (Exception ex) { Log.Write("Shelf: couldn't save a picture - " + ex.Message); return null; }
            var item = new ShelfItem { Id = NewId(), Kind = "image", Path = file, Added = DateTime.UtcNow };
            shelf.Items.Insert(0, item);
            Touch();
            return item;
        }

        static bool WriteCard(string file, string text)
        {
            try
            {
                Directory.CreateDirectory(CardsFolder);
                File.WriteAllText(file, text, new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex) { Log.Write("Shelf: couldn't save a card - " + ex.Message); return false; }
        }

        /// <summary>A free file name in the cards folder from some text ("Shopping list.txt", "Shopping list (2).txt").</summary>
        static string CardFile(string name, string ext)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                if (sb.Length >= 48) break;
                if (Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), c) >= 0 || char.IsControl(c)) sb.Append(' ');
                else sb.Append(c);
            }
            string b = sb.ToString().Trim().TrimEnd('.');
            if (b.Length == 0) b = "Card";
            string f = System.IO.Path.Combine(CardsFolder, b + ext);
            for (int i = 2; File.Exists(f); i++) f = System.IO.Path.Combine(CardsFolder, b + " (" + i + ")" + ext);
            return f;
        }

        /// <summary>The format a drag from one shelf to another carries ("shelf id|item id|item id…").</summary>
        public const string ItemsFormat = "vShell.Shelf.Items";

        /// <summary>Something a shelf can take: files, text, a picture.</summary>
        public static bool CanTake(IDataObject d)
        {
            if (d == null) return false;
            try
            {
                return d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent(DataFormats.UnicodeText) || d.GetDataPresent(DataFormats.Text) ||
                       d.GetDataPresent(DataFormats.Bitmap);
            }
            catch { return false; }
        }

        /// <summary>
        /// Whatever was dropped or pasted: items from another shelf move here; files are kept by path; else a picture or text becomes a
        /// card. Returns how many things were added.
        /// </summary>
        public static int AddFrom(Shelf shelf, IDataObject d)
        {
            if (d == null) return 0;
            try
            {
                if (d.GetDataPresent(ItemsFormat))
                {
                    string[] parts = ((d.GetData(ItemsFormat) as string) ?? "").Split('|');
                    Shelf from = parts.Length > 1 ? Find(parts[0]) : null;
                    if (from != null && from != shelf)
                    {
                        var ids = new HashSet<string>(parts);
                        return MoveTo(from, shelf, from.Items.FindAll(delegate (ShelfItem i) { return ids.Contains(i.Id); }));
                    }
                    if (from == shelf) return 0;
                }
                if (d.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = d.GetData(DataFormats.FileDrop) as string[];
                    if (files != null && files.Length > 0) return Add(shelf, files);
                }
                if (d.GetDataPresent(DataFormats.Bitmap))
                {
                    BitmapSource img = d.GetData(DataFormats.Bitmap) as BitmapSource;
                    if (img != null) return AddImage(shelf, img) != null ? 1 : 0;
                }
                string text = d.GetDataPresent(DataFormats.UnicodeText) ? d.GetData(DataFormats.UnicodeText) as string :
                              d.GetDataPresent(DataFormats.Text) ? d.GetData(DataFormats.Text) as string : null;
                if (!string.IsNullOrWhiteSpace(text)) return AddText(shelf, text) != null ? 1 : 0;
            }
            catch (Exception ex) { Log.Write("Shelf: couldn't take what was dropped - " + ex.Message); }
            return 0;
        }

        /// <summary>What's on the clipboard, as AddFrom.</summary>
        public static int Paste(Shelf shelf)
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    StringCollection files = Clipboard.GetFileDropList();
                    var l = new List<string>();
                    foreach (string f in files) l.Add(f);
                    return Add(shelf, l);
                }
                if (Clipboard.ContainsImage()) return AddImage(shelf, Clipboard.GetImage()) != null ? 1 : 0;
                if (Clipboard.ContainsText()) return AddText(shelf, Clipboard.GetText()) != null ? 1 : 0;
            }
            catch (Exception ex) { Log.Write("Shelf: couldn't paste - " + ex.Message); }
            return 0;
        }

        // =================================================================
        // Changing
        // =================================================================

        /// <summary>Take items off a shelf (a card's file goes too - it only lived on the shelf).</summary>
        public static void Remove(Shelf shelf, IEnumerable<ShelfItem> items)
        {
            int n = 0;
            foreach (ShelfItem i in new List<ShelfItem>(items))
            {
                if (!shelf.Items.Remove(i)) continue;
                n++;
                DeleteCard(i);
            }
            if (n > 0) Touch();
        }

        static void DeleteCard(ShelfItem i)
        {
            if (!i.IsCard) return;
            try
            {
                string full = System.IO.Path.GetFullPath(i.Path);
                if (full.StartsWith(System.IO.Path.GetFullPath(CardsFolder) + "\\", StringComparison.OrdinalIgnoreCase) && File.Exists(full)) File.Delete(full);
            }
            catch (Exception ex) { Log.Write("Shelf: couldn't delete a card - " + ex.Message); }
        }

        /// <summary>Items to another shelf (ones it already has stay where they were). Returns how many moved.</summary>
        public static int MoveTo(Shelf from, Shelf to, IEnumerable<ShelfItem> items)
        {
            if (from == to) return 0;
            int n = 0;
            foreach (ShelfItem i in new List<ShelfItem>(items))
            {
                if (to.Items.Exists(delegate (ShelfItem x) { return string.Equals(x.Path, i.Path, StringComparison.OrdinalIgnoreCase); })) continue;
                if (!from.Items.Remove(i)) continue;
                to.Items.Insert(0, i);
                n++;
            }
            if (n > 0) Touch();
            return n;
        }

        /// <summary>A missing item found again somewhere else.</summary>
        public static void Relocate(ShelfItem item, string path)
        {
            item.Path = path;
            bool dir = false;
            try { dir = Directory.Exists(path); } catch { }
            if (!item.IsCard) item.Kind = dir ? "folder" : "file";
            Touch();
        }

        public static Shelf NewShelf(string name)
        {
            var used = new HashSet<string>();
            foreach (Shelf s0 in Shelves) used.Add(s0.Color);
            string color = Array.Find(Colors, delegate (string c) { return !used.Contains(c); }) ?? Colors[Shelves.Count % Colors.Length];
            var s = new Shelf { Id = NewId(), Name = name, Color = color };
            Shelves.Add(s);
            Touch();
            return s;
        }

        public static void Rename(Shelf s, string name) { s.Name = name; Touch(); }
        public static void SetColor(Shelf s, string color) { s.Color = color; Touch(); }
        public static void SetPocket(Shelf s, bool pocket) { s.Pocket = pocket; Touch(); }

        public static void Clear(Shelf s) { Remove(s, s.Items); }

        public static void Delete(Shelf s)
        {
            if (Shelves.Count <= 1) return;
            foreach (ShelfItem i in s.Items) DeleteCard(i);
            Shelves.Remove(s);
            Touch();
        }

        /// <summary>Move a shelf one place left (-1) or right (+1) in the tabs.</summary>
        public static void Shift(Shelf s, int by)
        {
            int i = Shelves.IndexOf(s), j = i + by;
            if (i < 0 || j < 0 || j >= Shelves.Count) return;
            Shelves.RemoveAt(i);
            Shelves.Insert(j, s);
            Touch();
        }
    }
}
