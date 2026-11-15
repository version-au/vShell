using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace vShell.Core
{
    public enum StartupSource { UserRun, MachineRun, MachineRun32, UserFolder, CommonFolder }

    public sealed class StartupItem
    {
        public string Name = "";          // registry value name, or file name in a startup folder
        public string Command = "";       // command line, or full path of the file in a startup folder
        public StartupSource Source;
        public bool Enabled = true;

        public bool IsFolder { get { return Source == StartupSource.UserFolder || Source == StartupSource.CommonFolder; } }
        /// <summary>true for entries every user shares (removing them needs administrator rights; disabling does not).</summary>
        public bool IsMachineWide { get { return Source == StartupSource.MachineRun || Source == StartupSource.MachineRun32 || Source == StartupSource.CommonFolder; } }

        public string SourceText
        {
            get
            {
                switch (Source)
                {
                    case StartupSource.UserRun: return "Registry (this user)";
                    case StartupSource.MachineRun: return "Registry (all users)";
                    case StartupSource.MachineRun32: return "Registry (all users, 32-bit)";
                    case StartupSource.UserFolder: return "Startup folder (this user)";
                    default: return "Startup folder (all users)";
                }
            }
        }

        /// <summary>Sub-key of ...\Explorer\StartupApproved that records enabled/disabled for this kind of entry.</summary>
        public string ApprovedSub
        {
            get
            {
                if (IsFolder) return "StartupFolder";
                return Source == StartupSource.MachineRun32 ? "Run32" : "Run";
            }
        }
    }

    /// <summary>
    /// The programs Windows starts at sign-in (Run keys + Startup folders) with the same enabled/disabled switch Task Manager uses
    /// (HKCU\...\Explorer\StartupApproved). vShell runs them itself when it is the shell.
    /// </summary>
    public static class StartupManager
    {
        const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Run32Path = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static List<StartupItem> List()
        {
            var list = new List<StartupItem>();
            AddRegistry(list, Registry.CurrentUser, RunPath, StartupSource.UserRun);
            AddRegistry(list, Registry.LocalMachine, RunPath, StartupSource.MachineRun);
            AddRegistry(list, Registry.LocalMachine, Run32Path, StartupSource.MachineRun32);
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.UserFolder);
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.CommonFolder);
            foreach (var it in list) it.Enabled = !IsDisabled(it);
            return list;
        }

        static void AddRegistry(List<StartupItem> list, RegistryKey root, string path, StartupSource src)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(path))
                {
                    if (k == null) return;
                    foreach (string name in k.GetValueNames())
                    {
                        string cmd = k.GetValue(name) as string;
                        if (string.IsNullOrWhiteSpace(cmd)) continue;
                        list.Add(new StartupItem { Name = name, Command = cmd, Source = src });
                    }
                }
            }
            catch (Exception ex) { Log.Write("Startup list " + path + ": " + ex.Message); }
        }

        static void AddFolder(List<StartupItem> list, string dir, StartupSource src)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                foreach (string f in Directory.GetFiles(dir))
                {
                    string fn = Path.GetFileName(f);
                    if (string.Equals(fn, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new StartupItem { Name = fn, Command = f, Source = src });
                }
            }
            catch (Exception ex) { Log.Write("Startup folder " + dir + ": " + ex.Message); }
        }

        // ---- enabled / disabled ---------------------------------------------------------
        static bool? ReadApproved(RegistryKey root, string sub, string name)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(ApprovedPath + sub))
                {
                    if (k == null) return null;
                    var data = k.GetValue(name) as byte[];
                    if (data == null || data.Length == 0) return null;
                    return (data[0] & 1) == 0;       // 02/06 = enabled, 03 = disabled
                }
            }
            catch { return null; }
        }

        static bool IsDisabled(StartupItem it)
        {
            bool? v = ReadApproved(Registry.CurrentUser, it.ApprovedSub, it.Name);       // the user's own choice wins
            if (v == null && it.IsMachineWide) v = ReadApproved(Registry.LocalMachine, it.ApprovedSub, it.Name);
            return v == false;
        }

        /// <summary>true if Windows (and so vShell) should start this entry.</summary>
        public static bool IsEnabled(StartupItem it) { return !IsDisabled(it); }

        /// <summary>Enable or disable an entry for the current user. Returns an error message or null.</summary>
        public static string SetEnabled(StartupItem it, bool enabled)
        {
            try
            {
                var data = new byte[12];
                if (enabled) data[0] = 2;
                else
                {
                    data[0] = 3;
                    BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
                }
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ApprovedPath + it.ApprovedSub))
                    k.SetValue(it.Name, data, RegistryValueKind.Binary);
                it.Enabled = enabled;
                return null;
            }
            catch (Exception ex) { Log.Write("Startup SetEnabled: " + ex.Message); return ex.Message; }
        }

        // ---- remove / add -----------------------------------------------------------------
        /// <summary>Delete the entry. Returns an error message or null.</summary>
        public static string Remove(StartupItem it)
        {
            try
            {
                switch (it.Source)
                {
                    case StartupSource.UserRun: DeleteRegValue(Registry.CurrentUser, RunPath, it.Name); break;
                    case StartupSource.MachineRun: DeleteRegValue(Registry.LocalMachine, RunPath, it.Name); break;
                    case StartupSource.MachineRun32: DeleteRegValue(Registry.LocalMachine, Run32Path, it.Name); break;
                    default: File.Delete(it.Command); break;
                }
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ApprovedPath + it.ApprovedSub, true))
                        if (k != null) k.DeleteValue(it.Name, false);
                }
                catch { }
                return null;
            }
            catch (UnauthorizedAccessException) { return "Administrator rights are needed to remove \"" + it.Name + "\". You can disable it instead."; }
            catch (System.Security.SecurityException) { return "Administrator rights are needed to remove \"" + it.Name + "\". You can disable it instead."; }
            catch (Exception ex) { Log.Write("Startup Remove: " + ex.Message); return ex.Message; }
        }

        static void DeleteRegValue(RegistryKey root, string path, string name)
        {
            using (RegistryKey k = root.OpenSubKey(path, true))
                if (k != null) k.DeleteValue(name, false);
        }

        // ---- vShell itself ---------------------------------------------------------------------
        const string SelfName = "vShell";

        static bool IsSelfCommand(string command)
        {
            string me = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(command) || me.Length == 0) return false;
            return command.IndexOf(me, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>true when vShell (this exe) is in the user's startup apps and enabled.</summary>
        public static bool IsSelfEnabled()
        {
            try
            {
                foreach (var it in List())
                    if (it.Enabled && (IsSelfCommand(it.Command) || (it.Source == StartupSource.UserRun && it.Name == SelfName))) return true;
            }
            catch (Exception ex) { Log.Write("Startup IsSelfEnabled: " + ex.Message); }
            return false;
        }

        /// <summary>Put vShell in the user's startup apps (on) or take every entry that starts it out again (off). Returns an error or null.</summary>
        public static string SetSelf(bool on)
        {
            string me = Environment.ProcessPath;
            if (string.IsNullOrEmpty(me)) return "Could not find vShell's own program file.";
            if (on) return Add(SelfName, "\"" + me + "\"");
            string err = null;
            foreach (var it in List())
                if (IsSelfCommand(it.Command) || (it.Source == StartupSource.UserRun && it.Name == SelfName))
                {
                    string e = it.IsMachineWide ? SetEnabled(it, false) : Remove(it);     // a machine-wide entry can only be switched off
                    if (e != null && err == null) err = e;
                }
            return err;
        }

        /// <summary>Add (or replace) a per-user startup entry. Returns an error message or null.</summary>
        public static string Add(string name, string command)
        {
            name = (name ?? "").Trim();
            command = (command ?? "").Trim();
            if (name.Length == 0) return "Enter a name.";
            if (command.Length == 0) return "Enter a program or command.";
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunPath))
                    k.SetValue(name, command, RegistryValueKind.String);
                SetEnabled(new StartupItem { Name = name, Command = command, Source = StartupSource.UserRun }, true);
                return null;
            }
            catch (Exception ex) { Log.Write("Startup Add: " + ex.Message); return ex.Message; }
        }
    }
}
