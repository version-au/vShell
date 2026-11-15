using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace vShell.Core
{
    /// <summary>
    /// A plugin's manifest: what it is, written into its DLL so vShell (and build.cmd's plugins.json) can tell without running it.
    /// In the plugin:  [assembly: VShellPlugin("uptimekuma", "Uptime Kuma", Description = "…", Group = "Servers & network", Author = "…")]
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    [PluginApi]
    public sealed class VShellPluginAttribute : Attribute
    {
        public VShellPluginAttribute(string id, string name) { Id = id; Name = name; }
        /// <summary>Kept in the settings ("uptimekuma"): lower case, no spaces, never changed once released.</summary>
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Description { get; set; }
        /// <summary>A Segoe Fluent Icons character ("").</summary>
        public string Glyph { get; set; }
        /// <summary>Its group on the Plugins page ("Smart home", "Servers & network", "Media", "Devices", or one of its own).</summary>
        public string Group { get; set; }
        /// <summary>Who made it.</summary>
        public string Author { get; set; }
        /// <summary>What it brings, short: "a module, notifications, !up in the run box".</summary>
        public string Adds { get; set; }
        /// <summary>The module types it brings, comma separated ("UptimeKuma").</summary>
        public string Types { get; set; }
    }

    /// <summary>
    /// Part of the plugin API: plugins may use it, and it stays the same while the API version does (build.cmd checks). On a type: all of
    /// its public members; on a member of a type that isn't marked: just that member.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Delegate |
                    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Constructor)]
    public sealed class PluginApiAttribute : Attribute { }

    /// <summary>
    /// The plugin API's version. A plugin built against any vShell with the same API version loads in this one - no rebuild for each release.
    /// The number goes up only when something marked [PluginApi] has to change or go (build.cmd stops when that happens by accident).
    /// </summary>
    [PluginApi]
    public static class PluginApi
    {
        /// <summary>This vShell's plugin API.</summary>
        public const int Version = 2;

        /// <summary>
        /// The first vShell of each API version (index = version - 1). A plugin's API is the one of the vShell it was built against.
        /// 2 (0.38.135): vShell moved from .NET 8 to .NET 10 - a plugin built for .NET 10 can't load in a .NET 8 vShell, and the other way
        /// round plugins need a rebuild for .NET 10 too.
        /// </summary>
        static readonly System.Version[] Since = { new System.Version(0, 38, 84), new System.Version(0, 38, 135) };

        /// <summary>The API of a vShell version: 0 = from before plugin API versions (such a plugin needs exactly its own vShell).</summary>
        public static int Of(System.Version vshell)
        {
            if (vshell == null) return 0;
            var v = new System.Version(vshell.Major, vshell.Minor, Math.Max(0, vshell.Build));
            int api = 0;
            for (int i = 0; i < Since.Length; i++) if (v >= Since[i]) api = i + 1;
            return api;
        }
    }
    /// <summary>What's inside a plugin DLL, read without running any of it.</summary>
    public sealed class PluginFileInfo
    {
        /// <summary>The DLL's assembly name ("vShell.Komodo").</summary>
        public string File = "";
        /// <summary>The vShell it was built against (its reference to vShell.dll / vShell.exe).</summary>
        public Version BuiltFor;
        /// <summary>Its own version.</summary>
        public Version Version;
        /// <summary>From its manifest (null = it has none).</summary>
        public PluginInfo Manifest;
        /// <summary>The parts of vShell it uses: type names ("vShell.Ui.Theme", nested with +) and their members it calls (type -> names).</summary>
        public Dictionary<string, HashSet<string>> Uses = new Dictionary<string, HashSet<string>>();
        /// <summary>Its plugin API (from the vShell it was built against; 0 = from before API versions).</summary>
        public int Api { get { return PluginApi.Of(BuiltFor); } }
        static string V(Version v) { return v == null ? "?" : v.Major + "." + v.Minor + "." + Math.Max(0, v.Build); }
        public string BuiltForText { get { return V(BuiltFor); } }

        /// <summary>Look inside a DLL. Throws BadImageFormatException when it isn't a .NET DLL.</summary>
        public static PluginFileInfo Read(string path)
        {
            byte[] data = System.IO.File.ReadAllBytes(path);
            var ctx = new AssemblyLoadContext("vShell plugin check", true);
            // its "vShell" is this one, whichever version it was built against (a newer one too - reading its manifest needs it)
            ctx.Resolving += delegate (AssemblyLoadContext c, AssemblyName n)
            {
                return string.Equals(n.Name, "vShell", StringComparison.OrdinalIgnoreCase) ? typeof(PluginFileInfo).Assembly : null;
            };
            try
            {
                Assembly asm = ctx.LoadFromStream(new MemoryStream(data));
                AssemblyName n = asm.GetName();
                var info = new PluginFileInfo { File = n.Name ?? "", Version = n.Version };
                foreach (AssemblyName r in asm.GetReferencedAssemblies())
                    if (string.Equals(r.Name, "vShell", StringComparison.OrdinalIgnoreCase)) info.BuiltFor = r.Version;
                foreach (CustomAttributeData a in asm.GetCustomAttributesData())
                {
                    if (a.AttributeType.FullName != typeof(VShellPluginAttribute).FullName) continue;
                    var m = new PluginInfo { File = info.File };
                    if (a.ConstructorArguments.Count >= 2)
                    {
                        m.Id = (a.ConstructorArguments[0].Value as string ?? "").Trim().ToLowerInvariant();
                        m.Name = a.ConstructorArguments[1].Value as string ?? "";
                    }
                    foreach (CustomAttributeNamedArgument na in a.NamedArguments)
                    {
                        string v = na.TypedValue.Value as string ?? "";
                        switch (na.MemberName)
                        {
                            case "Description": m.Description = v; break;
                            case "Glyph": m.Glyph = v; break;
                            case "Group": m.Group = v; break;
                            case "Author": m.Author = PluginInfo.AuthorName(v); break;
                            case "Adds": m.Adds = v; break;
                            case "Types": m.Types = Split(v); break;
                        }
                    }
                    if (m.Id.Length > 0 && m.Name.Length > 0) info.Manifest = m;
                }
                ReadUses(data, info);
                return info;
            }
            finally { ctx.Unload(); }
        }

        /// <summary>The vShell types and members it refers to (its metadata: nothing runs).</summary>
        static void ReadUses(byte[] data, PluginFileInfo info)
        {
            using (var pe = new PEReader(new MemoryStream(data)))
            {
                MetadataReader md = pe.GetMetadataReader();
                Func<TypeReferenceHandle, string> name = null;
                name = delegate (TypeReferenceHandle h)
                {
                    TypeReference r = md.GetTypeReference(h);
                    string n = md.GetString(r.Name);
                    if (r.ResolutionScope.Kind == HandleKind.TypeReference) return name((TypeReferenceHandle)r.ResolutionScope) + "+" + n;
                    string ns = md.GetString(r.Namespace);
                    return ns.Length > 0 ? ns + "." + n : n;
                };
                Func<TypeReferenceHandle, bool> fromVShell = null;
                fromVShell = delegate (TypeReferenceHandle h)
                {
                    TypeReference r = md.GetTypeReference(h);
                    if (r.ResolutionScope.Kind == HandleKind.TypeReference) return fromVShell((TypeReferenceHandle)r.ResolutionScope);
                    if (r.ResolutionScope.Kind != HandleKind.AssemblyReference) return false;
                    return string.Equals(md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)r.ResolutionScope).Name), "vShell", StringComparison.OrdinalIgnoreCase);
                };
                foreach (TypeReferenceHandle h in md.TypeReferences)
                    if (fromVShell(h)) { string n = name(h); if (!info.Uses.ContainsKey(n)) info.Uses[n] = new HashSet<string>(); }
                foreach (MemberReferenceHandle h in md.MemberReferences)
                {
                    MemberReference m = md.GetMemberReference(h);
                    EntityHandle parent = m.Parent;
                    // a member of a generic type (ListEditorWindow<T>): its parent is the instance - read the type it's made from
                    if (parent.Kind == HandleKind.TypeSpecification)
                    {
                        try
                        {
                            BlobReader b = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
                            if (b.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance) continue;
                            b.ReadSignatureTypeCode();      // class / value type
                            parent = b.ReadTypeHandle();
                        }
                        catch (BadImageFormatException) { continue; }
                    }
                    if (parent.Kind != HandleKind.TypeReference || !fromVShell((TypeReferenceHandle)parent)) continue;
                    string n = name((TypeReferenceHandle)parent);
                    HashSet<string> set;
                    if (!info.Uses.TryGetValue(n, out set)) info.Uses[n] = set = new HashSet<string>();
                    set.Add(md.GetString(m.Name));
                }
            }
        }

        // protected members too: they're how a plugin's module uses its base (InfoModule, TilesModule)
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        /// <summary>Public, or protected (what a plugin can reach).</summary>
        static bool Reachable(MemberInfo m)
        {
            switch (m.MemberType)
            {
                case MemberTypes.Constructor:
                case MemberTypes.Method: var b = (MethodBase)m; return b.IsPublic || b.IsFamily || b.IsFamilyOrAssembly;
                case MemberTypes.Field: var f = (FieldInfo)m; return f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly;
                case MemberTypes.Property: var p = (PropertyInfo)m; foreach (MethodInfo a in p.GetAccessors(true)) if (Reachable(a)) return true; return false;
                case MemberTypes.Event: var e = (EventInfo)m; return e.AddMethod != null && Reachable(e.AddMethod);
                case MemberTypes.NestedType: var n = (Type)m; return n.IsNestedPublic || n.IsNestedFamily || n.IsNestedFamORAssem;
            }
            return false;
        }

        static Type TypeIn(Assembly vshell, string name) { return vshell.GetType(name, false); }

        /// <summary>What it uses that isn't in this vShell (a type or member that changed or went): "Theme.Fg" … Empty = it can run here.</summary>
        public List<string> MissingIn(Assembly vshell) { return MissingIn(vshell, Uses); }

        /// <summary>Uses as lines for plugins.json ("vShell.Ui.Theme", "vShell.Ui.Theme::Fg"): an update can be checked before it's downloaded.</summary>
        public string[] UsesLines()
        {
            var l = new List<string>();
            foreach (KeyValuePair<string, HashSet<string>> kv in Uses)
            {
                if (kv.Value.Count == 0) l.Add(kv.Key);
                foreach (string m in kv.Value) l.Add(kv.Key + "::" + m);
            }
            l.Sort(StringComparer.Ordinal);
            return l.ToArray();
        }

        /// <summary>Uses back from those lines.</summary>
        public static Dictionary<string, HashSet<string>> UsesFrom(string[] lines)
        {
            var d = new Dictionary<string, HashSet<string>>();
            foreach (string line in lines ?? new string[0])
            {
                int i = line.IndexOf("::", StringComparison.Ordinal);
                string t = i < 0 ? line : line.Substring(0, i);
                HashSet<string> set;
                if (!d.TryGetValue(t, out set)) d[t] = set = new HashSet<string>();
                if (i >= 0) set.Add(line.Substring(i + 2));
            }
            return d;
        }

        /// <summary>What of uses isn't in this vShell (a type or member that changed, went - or comes in a newer vShell).</summary>
        public static List<string> MissingIn(Assembly vshell, Dictionary<string, HashSet<string>> uses)
        {
            var missing = new List<string>();
            foreach (KeyValuePair<string, HashSet<string>> kv in uses)
            {
                Type t = TypeIn(vshell, kv.Key);
                if (t == null) { missing.Add(Short(kv.Key)); continue; }
                foreach (string m in kv.Value)
                {
                    // only what a plugin can reach counts (public / protected: an internal one fails when it's called). The parameters aren't
                    // compared - within one plugin API version build.cmd keeps them as they were (docs\plugin-api.txt)
                    bool have = false;
                    MemberInfo[] found = m == ".ctor" ? (MemberInfo[])t.GetConstructors(All) : t.GetMember(m, All);
                    foreach (MemberInfo x in found) if (Reachable(x)) { have = true; break; }
                    if (!have) missing.Add(Short(kv.Key) + "." + m);
                }
            }
            missing.Sort(StringComparer.Ordinal);
            return missing;
        }

        /// <summary>What it uses that is public in vShell but not part of the plugin API (may change in any release): "BarManager.Rebuild" …</summary>
        public List<string> OutsideApi(Assembly vshell)
        {
            var outside = new List<string>();
            foreach (KeyValuePair<string, HashSet<string>> kv in Uses)
            {
                Type t = TypeIn(vshell, kv.Key);
                if (t == null) continue;      // (MissingIn says so)
                if (IsApi(t)) continue;
                if (kv.Value.Count == 0) continue;      // only named (a parameter's type, a local): what can break is a member
                foreach (string m in kv.Value)
                    if (!MemberIsApi(t, m)) outside.Add(Short(kv.Key) + "." + m);
            }
            outside.Sort(StringComparer.Ordinal);
            return outside;
        }

        static string Short(string full) { int i = full.LastIndexOf('.'); return i >= 0 ? full.Substring(i + 1) : full; }

        /// <summary>The type is marked (or a type it's nested in is).</summary>
        public static bool IsApi(Type t)
        {
            for (Type x = t; x != null; x = x.DeclaringType) if (x.IsDefined(typeof(PluginApiAttribute), false)) return true;
            return false;
        }

        static bool MemberIsApi(Type t, string name)
        {
            // accessors: get_X / set_X -> the property X, add_X / remove_X -> the event X
            string plain = name;
            foreach (string pre in new[] { "get_", "set_", "add_", "remove_" }) if (name.StartsWith(pre)) { plain = name.Substring(pre.Length); break; }
            if (name == ".ctor") { foreach (ConstructorInfo c in t.GetConstructors(All)) if (c.IsDefined(typeof(PluginApiAttribute), false)) return true; return false; }
            foreach (MemberInfo m in t.GetMember(plain, All)) if (m.IsDefined(typeof(PluginApiAttribute), false)) return true;
            foreach (MemberInfo m in t.GetMember(name, All)) if (m.IsDefined(typeof(PluginApiAttribute), false)) return true;
            return false;
        }

        /// <summary>
        /// The plugin API as text, one line per type / member (build.cmd keeps a copy and compares: a line that went or changed would break
        /// plugins built for this API version).
        /// </summary>
        public static List<string> Surface(Assembly vshell)
        {
            var lines = new List<string>();
            foreach (Type t in vshell.GetTypes())
            {
                if (!Visible(t)) continue;
                bool whole = IsApi(t);
                string tn = t.FullName.Replace("vShell.Core.", "").Replace("vShell.Ui.", "");
                if (whole)
                {
                    // its base and the interfaces it has (dropping one breaks a plugin that uses it as one), an enum's number type
                    var bases = new List<string>();
                    if (t.BaseType != null && t.BaseType != typeof(object) && !t.IsEnum && !t.IsValueType) bases.Add(Name(t.BaseType));
                    if (t.IsEnum) bases.Add(Name(Enum.GetUnderlyingType(t)));
                    var ifs = new List<string>();
                    foreach (Type i in t.GetInterfaces()) if ((i.IsPublic || i.IsNestedPublic) && i.Assembly == t.Assembly) ifs.Add(Name(i));     // vShell's own (IMultiRow …), not WPF's
                    ifs.Sort(StringComparer.Ordinal);
                    bases.AddRange(ifs);
                    lines.Add(tn + " (" + Kind(t) + (bases.Count > 0 ? " : " + string.Join(", ", bases) : "") + ")");
                }
                foreach (MemberInfo m in t.GetMembers(All | BindingFlags.DeclaredOnly))
                {
                    if (!Reachable(m) || (!whole && !m.IsDefined(typeof(PluginApiAttribute), false))) continue;
                    string s = Describe(m);
                    if (s != null) lines.Add(tn + " :: " + s);
                }
            }
            lines.Sort(StringComparer.Ordinal);
            return lines;
        }

        /// <summary>
        /// A type a plugin can reach: public, or nested public / protected in one it can reach (TilesModule.Tile - a subclass uses its fields;
        /// the exported types left those out, so they weren't guarded).
        /// </summary>
        static bool Visible(Type t)
        {
            for (; t.IsNested; t = t.DeclaringType)
                if (!(t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamORAssem)) return false;
            return t.IsPublic;
        }

        static string Kind(Type t) { return t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : typeof(Delegate).IsAssignableFrom(t) ? "delegate" : t.IsAbstract && t.IsSealed ? "static class" : "class"; }

        static readonly Dictionary<Type, string> Keywords = new Dictionary<Type, string>
        {
            { typeof(void), "void" }, { typeof(string), "string" }, { typeof(bool), "bool" }, { typeof(int), "int" }, { typeof(long), "long" },
            { typeof(double), "double" }, { typeof(float), "float" }, { typeof(object), "object" }, { typeof(byte), "byte" }, { typeof(char), "char" }
        };

        static string Name(Type t)
        {
            if (t == null) return "void";
            if (t.IsGenericType)
            {
                string n = t.Name.Substring(0, Math.Max(0, t.Name.IndexOf('`')));
                var args = new List<string>();
                foreach (Type a in t.GetGenericArguments()) args.Add(Name(a));
                return n + "<" + string.Join(", ", args) + ">";
            }
            if (t.IsArray) return Name(t.GetElementType()) + "[]";
            string kw;
            if (Keywords.TryGetValue(t, out kw)) return kw;
            if (t.IsByRef) return "ref " + Name(t.GetElementType());
            return t.IsNested && !t.IsGenericParameter ? Name(t.DeclaringType) + "." + t.Name : t.Name;
        }

        static string Params(ParameterInfo[] ps)
        {
            var l = new List<string>();
            foreach (ParameterInfo p in ps) l.Add(Name(p.ParameterType) + (p.IsOptional ? "?" : ""));
            return "(" + string.Join(", ", l) + ")";
        }

        static string Describe(MemberInfo m)
        {
            switch (m.MemberType)
            {
                case MemberTypes.Constructor: var ci = (ConstructorInfo)m; if (ci.IsStatic) return null; return (ci.IsFamily ? "protected " : "") + "new" + Params(ci.GetParameters());
                case MemberTypes.Method:
                    var mi = (MethodInfo)m;
                    if (mi.IsSpecialName) return null;      // accessors: their property / event says it
                    return (mi.IsFamily || mi.IsFamilyOrAssembly ? "protected " : "") + (mi.IsStatic ? "static " : "") + (mi.IsAbstract ? "abstract " : mi.IsVirtual && !mi.IsFinal ? "virtual " : "") + Name(mi.ReturnType) + " " + mi.Name + (mi.IsGenericMethod ? "<" + mi.GetGenericArguments().Length + ">" : "") + Params(mi.GetParameters());
                case MemberTypes.Property:
                    var pi = (PropertyInfo)m;
                    MethodInfo g = pi.GetGetMethod(true), st = pi.GetSetMethod(true);
                    if (g != null && !Reachable(g)) g = null;
                    if (st != null && !Reachable(st)) st = null;
                    if (g == null && st == null) return null;
                    bool stat = (g ?? st).IsStatic;
                    return (stat ? "static " : "") + Name(pi.PropertyType) + " " + pi.Name + (pi.GetIndexParameters().Length > 0 ? Params(pi.GetIndexParameters()) : "") + " {" + (g != null ? " get;" : "") + (st != null ? " set;" : "") + " }";
                case MemberTypes.Field:
                    var fi = (FieldInfo)m;
                    if (fi.IsSpecialName) return null;
                    // a const / enum value is compiled into the plugin: its value is part of the API
                    string value = "";
                    if (fi.IsLiteral)
                    {
                        object raw = fi.GetRawConstantValue();
                        value = " = " + (raw == null ? "null" : raw is string ? "\"" + raw + "\"" : Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture));
                    }
                    return (fi.IsFamily || fi.IsFamilyOrAssembly ? "protected " : "") + (fi.IsLiteral ? "const " : fi.IsStatic ? "static " : "") + (fi.IsInitOnly ? "readonly " : "") + Name(fi.FieldType) + " " + fi.Name + value;
                case MemberTypes.Event:
                    var ei = (EventInfo)m;
                    return "event " + Name(ei.EventHandlerType) + " " + ei.Name;
                case MemberTypes.NestedType: return null;      // listed on its own
            }
            return null;
        }
        static string[] Split(string list)
        {
            var l = new List<string>();
            foreach (string s in (list ?? "").Split(',')) if (s.Trim().Length > 0) l.Add(s.Trim());
            return l.ToArray();
        }
    }
}
