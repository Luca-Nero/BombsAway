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
        public static string IniPath => FruitLib.FruitPaths.Config("GrenadeConfig.ini", typeof(ConfigLoader).Assembly);
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
            ["RemoteToggleKey"] = "with C4 in hand: toggle FIFO / simultaneous remote detonation",
            ["AttackModeKey"] = "with the missile in hand: cycle TOP / DIRECT / UNGUIDED attack",
            ["LockModeKey"] = "with the missile in hand: toggle persistent / standard lock",
            ["WarheadModeKey"] = "with the missile in hand: toggle HEAT / HE warhead",
            ["ReleaseLockKey"] = "with the missile in hand: clear the current lock",

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

            ["WoundIntensity"] = "scales every explosive wound: fragment power and overpressure damage",
            ["FragPower"] = "wound power of one grenade fragment at the charge (FruitLib ballistics; a 7.62 rifle round is ~15000)",
            ["C4FragPower"] = "wound power of one C4 fragment at the charge",
            ["ChargeKgTNT"] = "grenade filler as kg of TNT (an M67's 180 g of Comp B is ~0.2). Drives the blast wave: lungs, then gut, then skin, then limbs give way as the pressure rises. 0 = old fixed-radius overpressure",
            ["C4ChargeKgTNT"] = "C4 charge as kg of TNT (one M112 block ~0.75)",
            ["MineChargeKgTNT"] = "claymore charge as kg of TNT (M18A1 ~0.9); its blast follows the claymore's cone",
            ["MissileChargeKgTNT"] = "HEAT warhead's blast as kg of TNT; most of a HEAT charge goes into the jet",
            ["MissileHEChargeKgTNT"] = "HE warhead as kg of TNT",
            ["MineFragPower"] = "wound power of one claymore ball at the charge",
            ["MissileFragPower"] = "wound power of one HEAT warhead fragment at the charge",
            ["MissileJetPenetration"] = "metres of wall the HEAT jet goes through without losing anything, spread over every wall it meets; past that it slows like any fragment",
            ["MissileJetRays"] = "fragments in the HEAT jet, fired down the missile's axis (0 = no jet)",
            ["MissileJetConeDeg"] = "full angle of the HEAT jet, degrees",
            ["MissileJetPower"] = "wound power of one HEAT jet ray (a rifle round is ~15000); the missile's damage scale applies on top",
            ["MissileJetSpallCount"] = "fragments of wall the HEAT jet blows out of the back of each wall it goes through - what kills behind cover (0 = none)",
            ["MissileHEFragPower"] = "wound power of one HE warhead fragment at the charge",
            ["MaxWoundsPerExplosion"] = "hard cap on ApplyWound calls per detonation — keeps the game's wound queue from stalling (lower = faster, 0 = unlimited)",

            ["CamFXEnabled"] = "master toggle for shake / post-process",
            ["CamFXIntensity"] = "0 = off, 1 = default, 5 = max",

            ["VFXIntensity"] = "scales visual effect intensity",
            ["DebrisRaysRatio"] = "fraction of frag rays that spawn debris visuals",
            ["DebrisMaxPerExplosion"] = "hard cap on debris chunks per explosion, regardless of ray count",
            ["AdaptiveQuality"] = "scale ray/wound/debris counts down automatically under load (see FruitLib's perf monitor, F11)",
            ["MinQualityScale"] = "AdaptiveQuality floor — 0.25 = never drop below a quarter of configured counts",

            ["DebugLevel"] = "0 = silent, 1 = key events, 2 = verbose",
            ["DebugDrawExplosions"] = "draw each fragment path and blast line of a detonation (red = lodged, magenta = through a limb, yellow = ricochet, cyan = through a wall, orange = stopped) and log a summary per explosion",
            ["DebugDrawSeconds"] = "how long each debug line stays on screen (s)",
            ["DebugDrawMaxLines"] = "debug line pool size; when full the oldest line is reused",
            ["DebugDrawSpentEvery"] = "draw only every Nth fragment that hit nothing (they are most of them); 1 = all",
            ["TestBenchKey"] = "puts a row of test walls across your view (replacing the last); with Shift it removes them (None to disable)",
            ["TestBenchWalls"] = "how many test walls",
            ["TestBenchThickness"] = "thickness of each test wall, metres",
            ["TestBenchGap"] = "air gap between test walls, metres",
            ["TestBenchDistance"] = "how far ahead the first test wall stands, metres",
            ["TestBenchMaterial"] = "what the test walls are made of, by FruitLib surface name: Concrete, Brick, Steel, Wood, Drywall, Glass, Soil, Water",
            ["DebugDrawBlast"] = "also draw shockwave / overpressure lines to each body and limb (green = open, red = covered) with a cross where cover blocks them",
            ["FragLayerMask"] = "physics layer bitmask for blast / wound queries",
            ["WorldLayerMask"] = "physics layer bitmask for world-collision queries (sticky, impact, arc prediction)",
        };

        private static bool IsRenderable(Type t) =>
            t == typeof(bool) || t == typeof(float) || t == typeof(int) ||
            t == typeof(string) || t == typeof(KeyCode);

        internal static void Write()
        {
            var sb = new StringBuilder();

            sb.AppendLine("# ╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine($"# ║        GrenadeFramework v{Core.Version}  —  Configuration             ║");
            sb.AppendLine("# ╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine("# Reload requires game restart. All floats use . as decimal separator.");
            sb.AppendLine();

            var categories = new List<string>();
            var advanced   = new List<FieldInfo>();   // no [MenuCategory]: ini-only, not in the menu
            var byCategory = new Dictionary<string, List<FieldInfo>>();

            foreach (var f in typeof(Config).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.IsSpecialName || !IsRenderable(f.FieldType)) continue;
                var attr = (FruitLib.MenuCategoryAttribute)Attribute.GetCustomAttribute(
                    f, typeof(FruitLib.MenuCategoryAttribute));
                if (attr == null) { advanced.Add(f); continue; }

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

            if (advanced.Count > 0)
            {
                sb.AppendLine("# ── Advanced (ini only, not in the menu) ──");
                foreach (var f in advanced)
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
