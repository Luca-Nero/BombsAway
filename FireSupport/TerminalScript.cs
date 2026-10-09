using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;
using Random = UnityEngine.Random;

namespace BombsAway
{
    /// <summary>
    /// What the fire-support terminal says, read from a plain text file the player can edit
    /// (UserData/BombsAwayTerminal.txt, written from the copy in the DLL when it's missing, and
    /// read again whenever it changes, so an edit shows on the next call without a restart).
    ///
    /// A group is one program running one kind of unit, with a personality: its name, the units
    /// it runs, its typing pace, its squelch, its letters flickering. It has steps of its own,
    /// shared by its conversations, and any number of conversations, one picked per mission
    /// (never the same twice running). A step is a list of lines (cmd, sys, ai, obs, status).
    /// [ALL] holds steps every group falls back to.
    ///
    /// A step is looked up most specific first (step.UNIT.danger, step.UNIT, step.danger,
    /// step); between equally specific ones the conversation's beats its group's beats [ALL].
    /// A step that is there but empty says nothing. A step the file has nowhere at all (one
    /// added in a later version, 5.25.0's plan and masked) comes from the built-in [ALL].
    /// </summary>
    internal static class TerminalScript
    {
        public const string FileName = "BombsAwayTerminal.txt";
        private const string Resource = "BombsAway.TerminalScript.txt";

        public enum Kind { Cmd, Sys, Ai, Obs, Status }

        public sealed class Line { public Kind K; public string Text; }

        public class Steps
        {
            public readonly Dictionary<string, List<Line>> ByName = new Dictionary<string, List<Line>>(StringComparer.OrdinalIgnoreCase);
        }

        public sealed class Convo : Steps { public string Name; }

        public sealed class Group : Steps
        {
            public string Name, Program, Prefix;
            public readonly HashSet<string> Units = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public float Rate = 1f, Pitch = 1f;
            public bool Flicker;
            public readonly List<Convo> Convos = new List<Convo>();
            public int Last = -1;
        }

        private static readonly List<Group> _groups = new List<Group>();
        private static Steps _all = new Steps();
        private static Steps _builtinAll;          // the DLL's [ALL], for steps the file has nowhere

        private static DateTime _stamp;
        private static bool _loaded, _wroteWarned;

        public static string FilePath => FruitLib.FruitPaths.Config(FileName, typeof(TerminalScript).Assembly);

        // ── Lookup ──────────────────────────────────────────────────────────────

        /// <summary>A group that runs <paramref name="unit"/> (the strip's code: 155, 30, 2K, MOAB ...), one at random if several do.</summary>
        public static Group For(string unit)
        {
            Refresh();
            Group pick = null;
            int seen = 0;
            foreach (var g in _groups)
                if (g.Units.Contains(unit) && Random.Range(0, ++seen) == 0) pick = g;
            return pick;
        }

        /// <summary>One of the group's conversations, not the last one it had if it has others.</summary>
        public static Convo Pick(Group g)
        {
            if (g == null || g.Convos.Count == 0) return null;
            int i = Random.Range(0, g.Convos.Count);
            if (g.Convos.Count > 1 && i == g.Last) i = (i + 1 + Random.Range(0, g.Convos.Count - 1)) % g.Convos.Count;
            g.Last = i;
            return g.Convos[i];
        }

        /// <summary>The lines of <paramref name="step"/> for this unit; null if no scope has it.</summary>
        public static List<Line> Find(Group g, Convo c, string step, string unit, bool danger)
        {
            foreach (var name in Candidates(step, unit, danger))
            {
                if (c != null && c.ByName.TryGetValue(name, out var l)) return l;
                if (g != null && g.ByName.TryGetValue(name, out l)) return l;
                if (_all.ByName.TryGetValue(name, out l)) return l;
            }
            var builtin = BuiltinAll();
            if (builtin != null)
                foreach (var name in Candidates(step, unit, danger))
                    if (builtin.ByName.TryGetValue(name, out var l)) return l;
            return null;
        }

        /// <summary>The DLL's own [ALL] steps, parsed once.</summary>
        private static Steps BuiltinAll()
        {
            if (_builtinAll != null) return _builtinAll;
            _builtinAll = new Steps();
            try
            {
                string def = Default();
                if (def != null) ParseInto(def, "(built in)", new List<Group>(), _builtinAll, false);
            }
            catch (Exception e) { MelonLogger.Warning($"[Terminal] couldn't read the built-in script: {e.Message}"); }
            return _builtinAll;
        }

        private static IEnumerable<string> Candidates(string step, string unit, bool danger)
        {
            if (danger && !string.IsNullOrEmpty(unit)) yield return $"{step}.{unit}.danger";
            if (!string.IsNullOrEmpty(unit)) yield return $"{step}.{unit}";
            if (danger) yield return $"{step}.danger";
            yield return step;
        }

        /// <summary>{NAME} replaced from <paramref name="vars"/> (names in upper case); unknown ones are left as typed.</summary>
        public static string Fill(string text, Dictionary<string, string> vars)
        {
            if (text.IndexOf('{') < 0) return text;
            var sb = new StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                int close = open >= 0 ? text.IndexOf('}', open + 1) : -1;
                if (open < 0 || close < 0) { sb.Append(text, i, text.Length - i); break; }
                sb.Append(text, i, open - i);
                string key = text.Substring(open + 1, close - open - 1).Trim().ToUpperInvariant();
                if (vars != null && vars.TryGetValue(key, out var v)) sb.Append(v);
                else sb.Append(text, open, close - open + 1);
                i = close + 1;
            }
            return sb.ToString();
        }

        // ── Loading ─────────────────────────────────────────────────────────────

        /// <summary>Reads the file again if it changed (or was never read); writes the default when it's missing.</summary>
        public static void Refresh()
        {
            string path = FilePath;
            try
            {
                if (!File.Exists(path))
                {
                    string def = Default();
                    if (def == null) { if (!_loaded) Parse("", "(none)"); return; }
                    try { FruitLib.FruitPaths.WriteAllTextAtomic(path, def); MelonLogger.Msg($"[Terminal] wrote the default script to {path}"); }
                    catch (Exception e)
                    {
                        if (!_wroteWarned) MelonLogger.Warning($"[Terminal] couldn't write {FileName}: {e.Message}; using the built-in script");
                        _wroteWarned = true;
                        if (!_loaded) Parse(def, "(built in)");
                        return;
                    }
                }
                var stamp = File.GetLastWriteTimeUtc(path);
                if (_loaded && stamp == _stamp) return;
                string text = File.ReadAllText(path);
                _stamp = stamp;
                Parse(text, path);
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[Terminal] couldn't read {FileName}: {e.Message}");
                if (!_loaded) Parse(Default() ?? "", "(built in)");
            }
        }

        private static string Default()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
            {
                if (s == null) { MelonLogger.Warning($"[Terminal] {Resource} isn't in the DLL"); return null; }
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        private static void Parse(string text, string from)
        {
            bool reload = _loaded;
            _groups.Clear();
            _all = new Steps();
            _loaded = true;
            ParseInto(text, from, _groups, _all, true, reload);
        }

        /// <summary>Reads a script into <paramref name="groups"/> and <paramref name="all"/>; <paramref name="report"/>: log what was read and any problems.</summary>
        private static void ParseInto(string text, string from, List<Group> groups, Steps all, bool report, bool reload = false)
        {

            Group g = null;
            Steps scope = null;          // where the next @step goes: the group, a conversation, or [ALL]
            List<Line> step = null;
            int convos = 0, warnings = 0;
            var noGlyph = new HashSet<char>();
            string[] rows = text.Replace("\r\n", "\n").Split('\n');

            void Warn(int n, string msg)
            {
                if (++warnings <= 12 && report) MelonLogger.Warning($"[Terminal] {FileName} line {n}: {msg}");
            }

            for (int n = 1; n <= rows.Length; n++)
            {
                string t = rows[n - 1].Trim();
                if (t.Length == 0 || t[0] == '#') continue;

                if (t[0] == '[' && t.EndsWith("]"))
                {
                    string name = t.Substring(1, t.Length - 2).Trim();
                    step = null;
                    if (name.Equals("ALL", StringComparison.OrdinalIgnoreCase)) { g = null; scope = all; continue; }
                    g = new Group { Name = name, Program = name + ".AI", Prefix = name };
                    groups.Add(g);
                    scope = g;
                    continue;
                }
                if (t.StartsWith("=="))
                {
                    step = null;
                    if (g == null) { Warn(n, "a conversation outside a group"); scope = null; continue; }
                    var c = new Convo { Name = t.Substring(2).Trim() };
                    g.Convos.Add(c);
                    scope = c;
                    convos++;
                    continue;
                }
                if (t[0] == '@')
                {
                    if (scope == null) { Warn(n, "a step outside a group"); step = null; continue; }
                    step = new List<Line>();
                    scope.ByName[t.Substring(1).Trim()] = step;
                    continue;
                }

                int colon = t.IndexOf(':'), eq = t.IndexOf('=');
                if (step == null && g != null && scope == g && eq > 0 && (colon < 0 || eq < colon))
                {
                    SetKey(g, t.Substring(0, eq).Trim().ToLowerInvariant(), t.Substring(eq + 1).Trim(), n, Warn);
                    continue;
                }
                if (colon <= 0) { Warn(n, $"not a line, a step or a setting: \"{t}\""); continue; }
                if (step == null) { Warn(n, "a line before any @step"); continue; }

                string tag = t.Substring(0, colon).Trim().ToLowerInvariant();
                Kind k;
                switch (tag)
                {
                    case "cmd": k = Kind.Cmd; break;
                    case "sys": k = Kind.Sys; break;
                    case "ai": k = Kind.Ai; break;
                    case "obs": k = Kind.Obs; break;
                    case "status": k = Kind.Status; break;
                    default: Warn(n, $"unknown line type \"{tag}\" (cmd, sys, ai, obs, status)"); continue;
                }
                string body = t.Substring(colon + 1).Trim().ToUpperInvariant();
                step.Add(new Line { K = k, Text = body });

                // Placeholders and the cut mark aren't drawn; anything else without a glyph is.
                bool inVar = false;
                foreach (char ch in body)
                {
                    if (ch == '{') inVar = true;
                    else if (ch == '}') inVar = false;
                    else if (!inVar && ch != ' ' && ch != '^' && PixelFont.Bits(ch) == null) noGlyph.Add(ch);
                }
            }

            if (!report) return;
            if (noGlyph.Count > 0)
                MelonLogger.Warning($"[Terminal] {FileName}: no glyph for {string.Join(" ", noGlyph)} (drawn as a space)");
            if (warnings > 12) MelonLogger.Warning($"[Terminal] {FileName}: {warnings - 12} more problems");
            foreach (var gr in groups)
                if (gr.Units.Count == 0) MelonLogger.Warning($"[Terminal] [{gr.Name}] runs no units (units = ...)");
            MelonLogger.Msg($"[Terminal] script {(reload ? "reloaded" : "loaded")}: {groups.Count} programs, {convos} conversations ({from})");
        }

        private static void SetKey(Group g, string key, string value, int n, Action<int, string> warn)
        {
            switch (key)
            {
                case "program": g.Program = value.ToUpperInvariant(); break;
                case "prefix": g.Prefix = value.ToUpperInvariant(); break;
                case "units":
                    foreach (var u in value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)) g.Units.Add(u.Trim());
                    break;
                case "rate": g.Rate = Num(value, 1f, 0.2f, 5f); break;
                case "pitch": g.Pitch = Num(value, 1f, 0.3f, 2f); break;
                case "flicker":
                    g.Flicker = value.Equals("on", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                                || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                    break;
                default: warn(n, $"unknown setting \"{key}\" (program, prefix, units, rate, pitch, flicker)"); break;
            }
        }

        private static float Num(string s, float def, float lo, float hi) =>
            float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)
                ? Math.Max(lo, Math.Min(hi, v)) : def;
    }
}
