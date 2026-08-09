using MelonLoader;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // Config ini creator
    // ══════════════════════════════════════════════════════════════════════════════
    internal static class ConfigLoader
    {
        public static string IniPath => Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
            "GrenadeConfig.ini");
        private static string ConfigPath => IniPath;

        public static void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) { Write(); MelonLogger.Msg("Wrote default GrenadeConfig.ini"); return; }
                foreach (var line in File.ReadAllLines(ConfigPath))
                {
                    string t = line.Trim();
                    if (string.IsNullOrEmpty(t) || t.StartsWith("#")) continue;
                    int eq = t.IndexOf('=');
                    if (eq < 0) continue;
                    SetField(t.Substring(0, eq).Trim(), t.Substring(eq + 1).Trim());
                }
                Write();
                MelonLogger.Msg("GrenadeConfig.ini loaded.");
            }
            catch (Exception e) { MelonLogger.Warning($"Config load failed: {e.Message}"); }
        }

        private static void SetField(string key, string value)
        {
            var f = typeof(Config).GetField(key,
                BindingFlags.Public | BindingFlags.Static);
            if (f == null) return;
            try
            {
                if (f.FieldType == typeof(float)) f.SetValue(null, float.Parse(value, CultureInfo.InvariantCulture));
                else if (f.FieldType == typeof(int)) f.SetValue(null, int.Parse(value));
                else if (f.FieldType == typeof(bool)) f.SetValue(null, value.ToLower() == "true");
                else if (f.FieldType == typeof(string)) f.SetValue(null, value);
                else if (f.FieldType == typeof(KeyCode)) f.SetValue(null, (KeyCode)Enum.Parse(typeof(KeyCode), value, true));
            }
            catch { }
        }

        private static readonly Dictionary<string, string> FieldHelp = new Dictionary<string, string>
        {
            ["GrenadeKey"] = "key to throw grenade",
            ["C4Key"] = "key to throw / place C4",
            ["MineKey"] = "key to throw / place claymore",
            ["EnableLockOn"] = "hold to enable missile lock-on",
            ["LockOnTarget"] = "key to lock focused target",
            ["SpawnHomingMissile"] = "key to fire homing missile",
            ["DetonateKey"] = "key to detonate placed C4",
            ["RemoteToggleKey"] = "key to toggle sequential/simultaneous remote mode",
            ["AttackModeKey"] = "key to toggle missile TOP / DIRECT / UNGUIDED attack",
            ["LockModeKey"] = "key to toggle missile lock mode",
            ["WarheadModeKey"] = "key to toggle HEAT / HE warhead",
            ["ReleaseLockKey"] = "key to clear current missile lock",

            ["Fuse"] = "seconds before detonation",
            ["FlashRate"] = "LED blink interval in final seconds",
            ["ThrowForce"] = "initial throw velocity (m/s)",
            ["ThrowArc"] = "vertical throw offset in degrees",
            ["BlastRadius"] = "rigidbody push radius (m)",
            ["BlastForce"] = "outward force at centre",
            ["BlastUpward"] = "upward force at centre",
            ["OverpressureRadius"] = "wound radius (m)",
            ["OverpressureFalloffExp"] = "distance falloff exponent",
            ["OverpressureWoundPoints"] = "wound sampling points",
            ["FragRayCount"] = "total fragment rays",
            ["FragSpeed"] = "initial fragment speed (m/s)",
            ["FragMaxTime"] = "max fragment lifetime (s)",
            ["FragImpulse"] = "velocity added on shrapnel hit",

            ["MineProximityRange"] = "linear range to detonate mine",

            ["MissileLockRange"] = "max distance to scan for a lock target (m)",
            ["MissileLockAngle"] = "half-angle of the lock-on scan cone (deg)",
            ["MissileNavGain"] = "proportional navigation gain (N in the PN law)",
            ["MissileAscentHeight"] = "top-attack cruise altitude above target (m)",
            ["MissileDirectAscentHeight"] = "direct-attack cruise altitude above target (m)",

            ["WoundIntensity"] = "scales wound depth / severity",
            ["MaxWoundsPerExplosion"] = "hard cap on ApplyWound calls per detonation — keeps the game's wound queue from stalling (lower = faster, 0 = unlimited)",

            ["CamFXEnabled"] = "master toggle for shake / post-process",
            ["CamFXIntensity"] = "0 = off, 1 = default, 5 = max",

            ["VFXIntensity"] = "scales visual effect intensity",
            ["DebrisRaysRatio"] = "fraction of frag rays that spawn debris visuals",
            ["DebrisMaxPerExplosion"] = "hard cap on debris chunks per explosion, regardless of ray count",
            ["AdaptiveQuality"] = "scale ray/wound/debris counts down automatically under load (see FruitLib's perf monitor, F11)",
            ["MinQualityScale"] = "AdaptiveQuality floor — 0.25 = never drop below a quarter of configured counts",

            ["DebugLevel"] = "0 = silent, 1 = key events, 2 = verbose",
            ["DebugDrawRays"] = "draw debug rays / arcs",
            ["DebugRayDuration"] = "debug ray lifetime (s)",
            ["FragLayerMask"] = "physics layer bitmask for blast / wound queries",
            ["WorldLayerMask"] = "physics layer bitmask for world-collision queries (sticky, impact, arc prediction)",
        };

        private static bool IsRenderable(Type t) =>
            t == typeof(bool) || t == typeof(float) || t == typeof(int) ||
            t == typeof(string) || t == typeof(KeyCode);

        private static void Write()
        {
            var sb = new StringBuilder();

            sb.AppendLine("# ╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine($"# ║        GrenadeFramework v{Core.Version}  —  Configuration             ║");
            sb.AppendLine("# ╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine("# Reload requires game restart. All floats use . as decimal separator.");
            sb.AppendLine();

            var categories = new List<string>();
            var byCategory = new Dictionary<string, List<FieldInfo>>();

            foreach (var f in typeof(Config).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.IsSpecialName || !IsRenderable(f.FieldType)) continue;
                var attr = (FruitLib.MenuCategoryAttribute)Attribute.GetCustomAttribute(
                    f, typeof(FruitLib.MenuCategoryAttribute));
                if (attr == null) continue; // not a user-facing setting

                if (!byCategory.TryGetValue(attr.Name, out var list))
                {
                    list = new List<FieldInfo>();
                    byCategory[attr.Name] = list;
                    categories.Add(attr.Name);
                }
                list.Add(f);
            }

            foreach (var cat in categories)
            {
                sb.AppendLine($"# ── {cat} ──");
                foreach (var f in byCategory[cat])
                {
                    if (FieldHelp.TryGetValue(f.Name, out var help))
                        sb.AppendLine($"# {f.Name} : {help}");
                    sb.AppendLine($"{f.Name} = {FormatValue(f)}");
                }
                sb.AppendLine();
            }

            File.WriteAllText(ConfigPath, sb.ToString());
        }

        private static string FormatValue(FieldInfo f)
        {
            object val = f.GetValue(null);
            if (f.FieldType == typeof(float))
                return ((float)val).ToString("0.##############", CultureInfo.InvariantCulture);
            return val?.ToString() ?? "";
        }
    }
}
