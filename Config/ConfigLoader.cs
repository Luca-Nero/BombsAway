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
            ["AttackModeKey"] = "with the Javelin in hand: toggle TOP / DIRECT attack (or press its toolbar key again)",
            ["LockModeKey"] = "with the Javelin in hand: toggle persistent / standard lock",
            ["WarheadModeKey"] = "with the Javelin or AT-4 in hand: cycle the warhead HEAT / HE / TBX (thermobaric)",
            ["MissileTBXChargeKgTNT"] = "thermobaric warhead: its blast as kg of TNT (wider than the HE warhead's 3, and no fragments)",
            ["MissileTBXDamageScale"] = "thermobaric warhead: scales its blast wounds",
            ["MissileTBXIgniteDelay"] = "thermobaric warhead: seconds from the impact (the fuel cloud spreading) to the cloud going off",
            ["MissileTBXSuction"] = "thermobaric warhead: how hard the air rushing back in after the blast pulls things toward the middle, m/s at the middle (0 = off)",
            ["MissileTBXSuctionDelay"] = "thermobaric warhead: seconds after the blast the air rushes back",
            ["MissileTBXSuctionRadius"] = "thermobaric warhead: metres the rush back in reaches",
            ["MissileTBXDiffraction"] = "thermobaric warhead: share of its blast that still reaches something behind full cover (other charges 0.15)",
            ["MissileTBXCloudLift"] = "thermobaric warhead: metres back from the impact point the fuel cloud's middle (where it goes off) is",
            ["ReleaseLockKey"] = "with the Javelin in hand: clear the current lock",
            ["MineTripwire"] = "claymore: its three lasers are tripwires (off: anything inside its cone sets it off)",
            ["MineTripDelay"] = "claymore: seconds between a laser being broken and the blast (the lenses flicker meanwhile)",
            ["WarheadLabelGlitch"] = "the launcher's HEAT / HE stencil: false = retyped at a DOS cursor, true = glitches into the other word",
            ["RocketSpeed"] ="AT-4 rocket speed out of the tube (m/s)",
            ["RocketReloadTime"] = "seconds before the AT-4 can fire again",
            ["RocketGravity"] = "AT-4 rocket drop (m/s^2), only with RocketConvergence 0",
            ["RocketConvergence"] = "metres out where the AT-4 rocket meets the crosshair line and rides it (0 = along the tube, dropping)",

            ["SmokeFuse"] = "smoke grenade: seconds from the spoon to the smoke",
            ["SmokeDuration"] = "smoke grenade: seconds it keeps smoking",
            ["SmokeDensity"] = "smoke grenade: how thick the cloud is (lower is cheaper)",
            ["SmokeCanisterLife"] = "smoke grenade: seconds the spent can stays after the smoke stops",
            ["WindSpeed"] = "the breeze that moves smoke, metres per second (0 = still air); its heading wanders slowly",
            ["WindHeading"] = "the breeze's prevailing heading, degrees from +Z it blows toward (-1 = random per scene)",
            ["SmokeHaze"] = "smoke grenade: draw thin smoke as see-through haze round the solid smoke",
            ["SmokeBlastClear"] = "smoke grenade: size of the hole an explosion clears in smoke, metres per cube root of a kilo of TNT (3: hand grenade ~1.8 m, HE warhead ~4.3 m; 0 = blasts don't move smoke)",
            ["SmokeBlocksSight"] = "smoke hides what is behind it: the Javelin's DAY and NIGHT views can't lock through it (WHOT and BHOT can), and it shields eyes from a flashbang",
            ["SmokeLockClear"] = "smoke: how much of the view (0-1) a Javelin day or night sight needs clear of smoke to track a target; a metre of solid smoke leaves about 0.08",
            ["SmokeRefill"] ="smoke grenade: how long holes from explosions and rockets take to close (1 = 2.5 s plus 1 s per metre of hole)",
            ["BinoZoomLevels"] = "binoculars: the magnifications the mouse wheel steps through, comma-separated (starts at the middle one)",
            ["LaseTime"] = "binoculars: seconds to hold left mouse on a target before the fix and the call",
            ["ArtyRounds"] = "fire mission: 155 mm rounds per mission (one per gun)",
            ["ArtyDispersion"] = "fire mission: about how far from the mark rounds land, metres (most land within half of this)",
            ["ArtyFlightTime"] = "fire mission: seconds from SHOT to the first round landing",
            ["ArtyChargeKgTNT"] = "fire mission: one 155 mm HE shell's blast, kg of TNT (M795 ~6.6)",
            ["ArtyDamageScale"] = "fire mission: scales the shell's wounds",
            ["ArtyFragRayCount"] = "fire mission: fragments per shell",
            ["ArtyFragPower"] = "fire mission: wound power of one shell fragment at the burst",
            ["BinoAdsTime"] = "binoculars: seconds to bring them up to the eyes",
            ["BinoScreenFraction"] = "binoculars: at the eye, the screen's height as a share of the view's",
            ["RadioTextSize"] = "fire mission: size of the radio log's text (1 = the old size)",
            ["BinoSensitivity"] = "binoculars: mouse look through them, share of the speed that matches the zoom (1 = matched)",
            ["LaseRange"] = "binoculars: the rangefinder's reach, metres",
            ["LaseHoldMils"] = "binoculars: how far the reticle may wander off the lased point (mils; the first tick is 10) before the lase pauses",
            ["LaseGrace"] = "binoculars: seconds the reticle may be off the lased point (sway, a twitch) before the lase starts over; the fill pauses meanwhile",
            ["ArtyShotDelay"] = "fire mission: seconds from the call to SHOT (the readback comes halfway)",
            ["ArtySplashWarning"] = "fire mission: SPLASH comes this many seconds before the first round lands",
            ["ArtyVolleySpread"] = "fire mission: the other rounds land within this many seconds after the first",
            ["ArtyDescentAngle"] = "fire mission: how steeply shells come down, degrees from level",
            ["ArtyTerminalSpeed"] = "fire mission: a shell's speed on its way down, m/s: the streak you see, and its whistle's Doppler and length (near 343, the speed of sound, the whistle bunches up)",
            ["ArtyWhistleFlight"] = "fire mission: seconds of a shell's flight before impact that whistle (heard shorter or longer by Doppler, and late by the distance)",
            ["ArtyBatteryHeading"] = "fire mission: the battery's direction from the target, degrees from +Z (-1 = random per scene)",
            ["ArtyDangerClose"] = "fire mission: a mark nearer you than this (metres) is called DANGER CLOSE",
            ["ArtyStacking"] = "fire mission: allow a new mission while one is still running (false: the battery answers UNABLE)",
            ["ArtyBurstLift"] = "fire mission: metres above the surface a shell bursts",
            ["RadioTypeRate"] = "fire mission: radio text typed at this many characters a second",
            ["MissionPrevKey"] = "binoculars up: the previous fire mission (155 HE, 81 mortar, smoke, illum, precision)",
            ["MissionNextKey"] = "binoculars up: the next fire mission",
            ["MortarRounds"] = "81 mm mortar mission: rounds per mission",
            ["SmokeShellRounds"] = "smoke mission: 155 mm smoke rounds per mission (each leaves a canister smoking on the ground)",
            ["IllumRounds"] = "illumination mission: 155 mm illumination rounds per mission (each hangs a flare under a parachute)",
            ["MortarDispersion"] = "81 mm mortar mission: about how far from the mark rounds land, metres",
            ["MortarFlightTime"] = "81 mm mortar mission: seconds from SHOT to the first round landing",
            ["MortarVolleySpread"] = "81 mm mortar mission: the other rounds land within this many seconds after the first",
            ["MortarDescentAngle"] = "81 mm mortar mission: how steeply bombs come down, degrees from level",
            ["MortarTerminalSpeed"] = "81 mm mortar mission: a bomb's speed on its way down, m/s",
            ["MortarBatteryHeading"] = "81 mm mortar mission: the mortars' direction from the target, degrees from +Z (-1 = random per scene)",
            ["MortarChargeKgTNT"] = "81 mm mortar mission: one bomb's blast, kg of TNT (M821 ~0.95)",
            ["MortarDamageScale"] = "81 mm mortar mission: scales the bomb's wounds",
            ["MortarFragRayCount"] = "81 mm mortar mission: fragments per bomb",
            ["MortarFragPower"] = "81 mm mortar mission: wound power of one fragment at the burst",
            ["SmokeShellDispersion"] = "smoke mission: about how far from the mark canisters land, metres (wider makes a longer screen)",
            ["SmokeShellVolleySpread"] = "smoke mission: the other rounds land within this many seconds after the first",
            ["SmokeShellBurnTime"] = "smoke mission: seconds each canister smokes",
            ["SmokeShellRate"] = "smoke mission: how fast a canister vents (a smoke grenade is 12)",
            ["IllumDispersion"] = "illumination mission: about how far from the mark flares open, metres",
            ["IllumVolleySpread"] = "illumination mission: the other rounds open within this many seconds after the first",
            ["IllumBurstHeight"] = "illumination mission: metres above the ground a flare opens",
            ["IllumBurnTime"] = "illumination mission: seconds a flare burns",
            ["IllumFallSpeed"] = "illumination mission: a flare's fall under its parachute, m/s",
            ["IllumWindScale"] = "illumination mission: the breeze up where the flares hang, times the one on the ground",
            ["IllumLightRange"] = "illumination mission: metres a flare's light reaches",
            ["IllumLightIntensity"] = "illumination mission: a flare's light intensity (it falls off with the square of the distance: 5000 gives about 0.6 on the ground from 90 m up)",
            ["PrecisionRounds"] = "precision mission: guided 155 mm rounds per mission",
            ["PrecisionDispersion"] = "precision mission: about how far from the mark a guided round lands, metres",
            ["PrecisionFlightTime"] = "precision mission: seconds from SHOT to impact (a guided round glides)",
            ["PrecisionDescentAngle"] = "precision mission: how steeply the guided round dives, degrees from level",
            ["PrecisionTerminalSpeed"] = "precision mission: the guided round's speed on its dive, m/s",
            ["FlashFuse"] = "flashbang: seconds from the spoon to the bang",
            ["FlashFullRange"] = "flashbang: metres within which it is at full strength (beyond: inverse square)",
            ["FlashStunTime"] = "flashbang: seconds a body is held at full strength",
            ["FlashRecoverTime"] = "flashbang: seconds a body takes to recover after the hold",
            ["FlashBalance"] = "flashbang: a body's balance at full stun, percent of normal (0 = it falls)",
            ["FlashMuscle"] = "flashbang: a body's muscle at full stun, percent of normal (too low and the hands can't reach the face)",
            ["FlashCoverFace"] = "flashbang: bodies put their hands over their eyes (blinded) or ears (deafened)",
            ["FlashCoverAt"] = "flashbang: sight or hearing (0..1) from which the hands go up",
            ["FlashFocusAngle"] = "flashbang: degrees off where the eyes point that get the full flash",
            ["FlashPeripheralAngle"] = "flashbang: degrees off where the eyes point beyond which the flash isn't seen",
            ["FlashPeripheralStrength"] = "flashbang: the flash at the edge of the peripheral cone, share of full",
            ["FlashHearingOcclusion"] = "flashbang: the bang's loudness through cover, share of the open one",
            ["FlashHearingStun"] = "flashbang: how much the bang alone (not seen) stuns a body, share of its hearing",
            ["FlashRingTime"] = "flashbang: seconds your ears ring at full strength",
            ["FlashRingPitchMin"] = "flashbang: lowest ring pitch, times 4 kHz (picked per bang)",
            ["FlashRingPitchMax"] = "flashbang: highest ring pitch, times 4 kHz",
            ["FlashOcclusion"] = "flashbang: the flash through cover, share of the open one",
            ["FlashMinStrength"] = "flashbang: weaker than this (0..1) and a body is left alone",
            ["FlashCoverOnRig"] = "flashbang: bodies cover their face against the IK rig's head, which the body copies (off: against the physical head; arms go straight once the body lies or hangs)",
            ["FlashReflection"] = "flashbang: how much its light bouncing off walls, floor and ceiling blinds (a bang in a room blinds you whichever way you face; in the open, facing away, it doesn't); 0 = off",
            ["FlashReflectRays"] = "flashbang: rays from the bang that find the surfaces it lights (more is smoother and costs more)",
            ["FlashReflectRange"] = "flashbang: metres within which a wall, floor or ceiling is lit enough to count",
            ["FlashCloseRange"] ="flashbang: metres within which a body is blinded whichever way it faces, as long as the bang can see its head",
            ["FlashPlayer"] = "flashbang: it blinds you when you see it and your ears ring when you hear it (off: you only hear the bang)",
            ["FlashBlackout"] = "flashbang: black-out instead of white-out",
            ["FlashBlindTime"] = "flashbang: seconds you are fully blind at full strength, before the screen starts to clear",
            ["FlashBlindFade"] = "flashbang: seconds the screen takes to clear after that, at full strength",
            ["FlashChargeKgTNT"] = "flashbang: its small blast, kg of TNT",
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
            ["ShootToDetonate"] = "a FruitLib round (GunsGunsGuns etc.) passing through a live charge - thrown, placed, stuck or a missile in flight - sets it off",
            ["ChainReactions"] = "an explosion sets off charges in sight where its blast is strong enough, and by chance ones further out its fragments reach (likelier the closer and the more fragments); those can set off more",
            ["ChainDelayMin"] = "shortest delay before a set-off charge goes (s); a chain goes link by link",
            ["ChainDelayMax"] = "longest delay before a set-off charge goes (s)",
            ["ChainPerFrame"] = "most chained charges that go off in one frame; the rest wait a frame. Each traces thousands of fragments, so 1 keeps a long chain smooth",
            ["BlastPushScale"] = "scales how far blast waves throw bodies and props. 1 = physical, from the charge's TNT figure",
            ["SympatheticKPa"] = "blast overpressure (kPa) that sets off a charge sitting in it without a fragment hitting it",
            ["ChainFragmentSpeed"] = "slowest fragment (m/s) that can set off a charge it hits",
            ["OrdnanceHitRadius"] = "a charge's size for hits (m): how close a round must pass its centre, and the target area fragments have to hit",
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
            ["DebugDrawFlashArms"] = "flashbang: draw, live, how each covering body's head, hand targets, IK hands, physical hands and elbows line up, with labels (legend top left)",
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

            FruitLib.FruitPaths.WriteAllTextAtomic(ConfigPath, sb.ToString());
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
