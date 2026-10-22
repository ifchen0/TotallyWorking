using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Verse;

namespace TotallyWorking
{
    // Produces chunks of code-looking text at runtime: C# outlines rebuilt from loaded types via
    // reflection, and excerpts of loaded mods' XML. Nothing from the game is shipped with the mod.
    public class CodeSource
    {
        private const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private const long MaxXmlBytes = 512 * 1024;

        private static readonly string[] SkippedAssemblyPrefixes = { "System", "Mono", "Unity", "mscorlib", "netstandard", "Microsoft", "0Harmony", "Newtonsoft", "NAudio" };

        private static readonly Dictionary<Type, string> Aliases = new Dictionary<Type, string>
        {
            { typeof(void), "void" }, { typeof(bool), "bool" }, { typeof(int), "int" }, { typeof(float), "float" },
            { typeof(double), "double" }, { typeof(string), "string" }, { typeof(object), "object" }, { typeof(long), "long" },
            { typeof(byte), "byte" }, { typeof(short), "short" }, { typeof(uint), "uint" }, { typeof(ushort), "ushort" },
            { typeof(ulong), "ulong" }, { typeof(char), "char" },
        };

        private static readonly string[] Comments =
        {
            "// TODO: move this out of the tick path",
            "// FIXME: not thread safe, revisit before release",
            "// keep in sync with the XML defaults",
            "// cached, see ExposeData",
            "// HACK: workaround for the 1.5 save format",
            "// do not reorder, callers rely on this",
            "// null when loading an old save",
        };

        private volatile List<Type> types;
        private volatile List<string> xmlFiles;
        private readonly Random rand = new Random();

        public CodeSource()
        {
            var folders = LoadedModManager.RunningModsListForReading.SelectMany(m => m.foldersToLoadDescendingOrder).ToList();
            Task.Run(() =>
            {
                try { types = CollectTypes(); }
                catch (Exception e) { Log.Warning("[TotallyWorking] Type scan failed: " + e.Message); types = new List<Type>(); }
                try { xmlFiles = CollectXmlFiles(folders); }
                catch (Exception e) { Log.Warning("[TotallyWorking] XML scan failed: " + e.Message); xmlFiles = new List<string>(); }
            });
        }

        public List<string> NextChunk()
        {
            List<string> chunk = null;
            for (int attempt = 0; attempt < 5 && chunk == null; attempt++)
            {
                if (rand.NextDouble() < 0.35)
                    chunk = XmlChunk();
                else
                    chunk = TypeChunk();
            }
            if (chunk == null)
                chunk = new List<string> { "Restoring packages...", "  Determining projects to restore..." };
            chunk.Add("");
            return chunk;
        }

        private static List<Type> CollectTypes()
        {
            return GenTypes.AllTypes.Where(t =>
                t.IsClass && !t.IsNested && t.Namespace != null && !t.Name.Contains('<')
                && !typeof(Delegate).IsAssignableFrom(t)
                && !SkippedAssemblyPrefixes.Any(p => t.Assembly.GetName().Name.StartsWith(p))).ToList();
        }

        private static List<string> CollectXmlFiles(List<string> folders)
        {
            var result = new List<string>();
            foreach (var folder in folders.Distinct())
                foreach (var sub in new[] { "Defs", "Patches" })
                {
                    var dir = Path.Combine(folder, sub);
                    if (Directory.Exists(dir))
                        result.AddRange(Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories));
                }
            return result;
        }

        private List<string> XmlChunk()
        {
            var files = xmlFiles;
            if (files == null || files.Count == 0)
                return null;
            try
            {
                var path = files[rand.Next(files.Count)];
                if (new FileInfo(path).Length > MaxXmlBytes)
                    return null;
                var all = File.ReadAllLines(path);
                if (all.Length < 5)
                    return null;
                int len = Math.Min(all.Length, rand.Next(12, 40));
                int start = rand.Next(0, all.Length - len + 1);
                var chunk = new List<string> { "<!-- " + Path.GetFileName(path) + " -->" };
                for (int i = start; i < start + len; i++)
                    chunk.Add(Clean(all[i]));
                return chunk;
            }
            catch
            {
                return null;
            }
        }

        private List<string> TypeChunk()
        {
            var list = types;
            if (list == null || list.Count == 0)
                return null;
            var t = list[rand.Next(list.Count)];
            try
            {
                var fields = t.GetFields(Declared).Where(f => !f.Name.Contains('<')).ToArray();
                var methods = t.GetMethods(Declared).Where(m => !m.IsSpecialName && !m.Name.Contains('<')).ToArray();
                if (methods.Length == 0)
                    return null;

                var o = new List<string> { "namespace " + t.Namespace, "{" };
                string mods = (t.IsPublic ? "public " : "internal ") + (t.IsAbstract && t.IsSealed ? "static " : t.IsAbstract ? "abstract " : t.IsSealed ? "sealed " : "");
                string baseName = t.BaseType != null && t.BaseType != typeof(object) ? " : " + Name(t.BaseType) : "";
                o.Add("    " + mods + "class " + Name(t) + baseName);
                o.Add("    {");
                foreach (var f in Pick(fields, rand.Next(0, 6)))
                    o.Add("        " + Access(f.IsPublic, f.IsFamily) + (f.IsStatic ? "static " : "") + (f.IsInitOnly ? "readonly " : "") + Name(f.FieldType) + " " + f.Name + ";");
                if (fields.Length > 0)
                    o.Add("");
                var picked = Pick(methods, rand.Next(1, 4));
                for (int i = 0; i < picked.Count; i++)
                {
                    if (i > 0)
                        o.Add("");
                    WriteMethod(o, picked[i], fields, methods);
                }
                o.Add("    }");
                o.Add("}");
                return o;
            }
            catch
            {
                return null;
            }
        }

        private void WriteMethod(List<string> o, MethodInfo m, FieldInfo[] fields, MethodInfo[] methods)
        {
            string mods = Access(m.IsPublic, m.IsFamily);
            if (m.IsStatic)
                mods += "static ";
            else if (m.IsAbstract)
                mods += "abstract ";
            else if (m.IsVirtual && !m.IsFinal)
                mods += m.GetBaseDefinition().DeclaringType != m.DeclaringType ? "override " : "virtual ";
            var ps = m.GetParameters();
            string sig = "        " + mods + Name(m.ReturnType) + " " + m.Name + "(" + string.Join(", ", ps.Select(Param)) + ")";
            if (m.IsAbstract)
            {
                o.Add(sig + ";");
                return;
            }
            o.Add(sig);
            o.Add("        {");
            const string ind = "            ";
            if (mods.Contains("override") && m.ReturnType == typeof(void))
                o.Add(ind + "base." + m.Name + "(" + string.Join(", ", ps.Select(p => p.Name)) + ");");

            int steps = rand.Next(2, 6);
            for (int s = 0; s < steps; s++)
            {
                var f = fields.Length > 0 ? fields[rand.Next(fields.Length)] : null;
                var callee = methods[rand.Next(methods.Length)];
                string call = callee.Name + "(" + string.Join(", ", callee.GetParameters().Select(p => Literal(p.ParameterType))) + ")";
                switch (rand.Next(6))
                {
                    case 0 when f != null:
                        o.Add(ind + "if (" + f.Name + (f.FieldType.IsValueType ? " == default" : " == null") + ")");
                        o.Add(ind + "{");
                        o.Add(ind + "    " + (m.ReturnType == typeof(void) ? "return;" : "return default;"));
                        o.Add(ind + "}");
                        break;
                    case 1 when callee.ReturnType != typeof(void):
                        o.Add(ind + "var " + Camel(callee.Name) + " = " + call + ";");
                        break;
                    case 2:
                        o.Add(ind + "for (int i = 0; i < " + (f != null && f.FieldType == typeof(int) ? f.Name : "count") + "; i++)");
                        o.Add(ind + "{");
                        o.Add(ind + "    " + call + ";");
                        o.Add(ind + "}");
                        break;
                    case 3:
                        o.Add(ind + Comments[rand.Next(Comments.Length)]);
                        break;
                    case 4 when f != null:
                        o.Add(ind + "Log.Message($\"[" + m.DeclaringType.Name + "] " + m.Name + ": {" + f.Name + "}\");");
                        break;
                    default:
                        o.Add(ind + call + ";");
                        break;
                }
            }
            if (m.ReturnType == typeof(bool))
                o.Add(ind + "return " + (fields.Length > 0 && !fields[0].FieldType.IsValueType ? fields[0].Name + " != null;" : "true;"));
            else if (m.ReturnType != typeof(void))
                o.Add(ind + "return default;");
            o.Add("        }");
        }

        private List<T> Pick<T>(T[] source, int count)
        {
            return source.OrderBy(_ => rand.Next()).Take(count).ToList();
        }

        private static string Access(bool isPublic, bool isFamily) => isPublic ? "public " : isFamily ? "protected " : "private ";

        private static string Param(ParameterInfo p)
        {
            string prefix = p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "";
            return prefix + Name(p.ParameterType) + " " + p.Name;
        }

        private static string Literal(Type t)
        {
            if (t.IsByRef)
                t = t.GetElementType();
            if (t == typeof(bool))
                return "false";
            if (t == typeof(string))
                return "\"\"";
            if (t == typeof(float))
                return "0f";
            if (t.IsPrimitive)
                return "0";
            if (t.IsEnum)
            {
                var names = Enum.GetNames(t);
                return names.Length > 0 ? Name(t) + "." + names[0] : "default";
            }
            return t.IsValueType ? "default" : "null";
        }

        private static string Name(Type t)
        {
            if (t.IsByRef)
                return Name(t.GetElementType());
            if (t.IsArray)
                return Name(t.GetElementType()) + "[]";
            if (Aliases.TryGetValue(t, out var alias))
                return alias;
            if (t.IsGenericType)
            {
                var name = t.Name;
                int tick = name.IndexOf('`');
                if (tick >= 0)
                    name = name.Substring(0, tick);
                return name + "<" + string.Join(", ", t.GetGenericArguments().Select(Name)) + ">";
            }
            return t.Name;
        }

        private static string Camel(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        private static string Clean(string line)
        {
            line = line.Replace("\t", "    ").TrimEnd();
            return line.Length > 160 ? line.Substring(0, 160) : line;
        }
    }
}
