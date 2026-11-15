using System;
using System.Collections.Generic;
using System.IO;

namespace vShell.Core
{
    /// <summary>One file from Windows' Recent folder.</summary>
    public sealed class RecentFile
    {
        /// <summary>The file's name as Windows shows it ("Budget.xlsx").</summary>
        public string Name = "";
        /// <summary>The shortcut in the Recent folder - opening it opens the file.</summary>
        public string Link = "";
        /// <summary>When it was last opened (local time).</summary>
        public DateTime When;
    }

    /// <summary>
    /// The files you opened lately, newest first: the shortcuts Windows keeps in %APPDATA%\Microsoft\Windows\Recent (the same list as
    /// Explorer's Recent). Folders are left out - their shortcuts have no file extension in the name.
    /// </summary>
    public static class RecentFiles
    {
        public static List<RecentFile> Get(int count)
        {
            var list = new List<RecentFile>();
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;
                var all = new List<FileInfo>();
                foreach (FileInfo f in new DirectoryInfo(dir).EnumerateFiles("*.lnk"))
                {
                    string name = Path.GetFileNameWithoutExtension(f.Name);
                    if (!Path.HasExtension(name)) continue;          // a folder ("Downloads.lnk"), not a file ("Notes.txt.lnk")
                    all.Add(f);
                }
                all.Sort(delegate (FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
                for (int i = 0; i < all.Count && list.Count < count; i++)
                    list.Add(new RecentFile { Name = Path.GetFileNameWithoutExtension(all[i].Name), Link = all[i].FullName, When = all[i].LastWriteTime });
            }
            catch (Exception ex) { Log.Write("Recent files: " + ex.Message); }
            return list;
        }
    }
}
