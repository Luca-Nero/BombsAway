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

        public static void Load()
        {
            try
            {
                if (!File.Exists(IniPath)) { Write(); MelonLogger.Msg("Wrote default GrenadeConfig.ini"); return; }
                foreach (var line in File.ReadAllLines(IniPath))
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
            ["SmokeTerminal"] = "smoke grenade: changing the colour in hand pops up a terminal beside the can that reloads its dye (off: the colour just fades over)",
            ["SmokeRetintTime"] = "smoke grenade: seconds the can's band takes to step to a new colour (0 = at once, no terminal)",
            ["SmokeTermOffsetX"] = "smoke grenade: the colour terminal's place from the can, metres to the right (camera axes)",
            ["SmokeTermOffsetY"] = "smoke grenade: the colour terminal's place from the can, metres up",
            ["SmokeTermOffsetZ"] = "smoke grenade: the colour terminal's place from the can, metres forward",
            ["SmokeTermWidth"] = "smoke grenade: the colour terminal's width (and the spawn terminal's), metres (the can is about 0.43 m from the eye)",
            ["SmokeTermYaw"] = "smoke grenade: degrees the colour terminal (and the spawn terminal) is turned away from facing you square on",
            ["SpawnTerminal"] = "prototype: equipping an item spawns it into view from a terminal beside the hand, texel by texel (off: it rises into view as before)",
            ["SpawnTime"] = "spawn terminal: seconds the item takes to fight its way into existence",
            ["DespawnTime"] = "spawn terminal: seconds a put-away item takes to dissolve into bytes",
            ["SpawnSettleLift"] = "spawn terminal: metres the item hovers over the hand while it spawns, before it drops in and settles",
            ["SpawnSettleSway"] = "spawn terminal: how much the item sways as it settles into the hand (degrees a second of kick; 0 = none)",
            ["SpawnTermOffsetX"] = "spawn terminal: its place from the item in hand, metres to the right (camera axes)",
            ["SpawnTermOffsetY"] = "spawn terminal: its place from the item in hand, metres up",
            ["SpawnTermOffsetZ"] = "spawn terminal: its place from the item in hand, metres forward",
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
            ["ArtyFragments"] = "fire mission: one 155 mm shell's fragments, shared out over the bodies in reach (cost follows the hits, not this number)",
            ["ArtyWorldRays"] = "fire mission: untargeted fragment rays per shell for walls, props and the debug draw",
            ["ArtyWalksPerLimb"] = "fire mission: most fragment wounds one limb takes from one shell",
            ["ArtyFragBeltDeg"] = "fire mission: the side-spray belt's thickness, degrees, square to the shell's flight (0 = an even sphere)",
            ["ArtyFragBeltShare"] = "fire mission: share of the shell's fragments in the belt, 0..1",
            ["ArtyFragPower"] = "fire mission: wound power of one shell fragment at the burst",
            ["BinoAdsTime"] = "binoculars: seconds to bring them up to the eyes",
            ["BinoScreenFraction"] = "binoculars: at the eye, the screen's height as a share of the view's",
            ["RadioTextSize"] = "fire mission: size of the radio log's text (1 = the old size); the artillery terminal follows it",
            ["ArtyTerminal"] = "prototype: artillery missions (155 HE, smoke, illum, precision, 81 mm mortars) are called through a terminal where the radio log would be, which spawns the unit, boots a program to run it, and deletes both at SPLASH (off: the radio net). What it says is in BombsAwayTerminal.txt next to this file",
            ["AirTerminal"] = "prototype: air strikes are called through the same terminal, each aircraft run by a program with its own personality, deleted at the end of the mission (off: the radio net). What they say is in BombsAwayTerminal.txt next to this file; edits show on the next call",
            ["ArtyTermDistance"] = "artillery terminal: metres from the eye it is drawn at (its size on screen stays the radio log's)",
            ["ArtyTermScale"] = "artillery terminal: its size against the radio log's (1 = the same glyph size); its left edge stays on screen either way",
            ["ArtyTermYaw"] = "artillery terminal: degrees it is turned about its upright after facing you (negative brings its outer edge toward you)",
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
            ["ArtyStacking"] = "fire mission: allow a new mission while one is still running (false: the battery, or the mortars, answer UNABLE while their own last one is)",
            ["AirStacking"] = "air strike: allow a new strike from an aircraft still on its last one (false: it answers UNABLE; a different aircraft can always be called)",
            ["ArtyBurstLift"] = "fire mission: metres above the surface a shell bursts",
            ["RadioTypeRate"] = "fire mission: radio text typed at this many characters a second",
            ["MissionPrevKey"] = "binoculars up: the previous fire mission (ARTY: 155 HE, 81 mortar, smoke, illum, precision; AIR: 30 and 20 mm gun runs)",
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
            ["MortarFragments"] = "81 mm mortar mission: one bomb's fragments, shared out over the bodies in reach",
            ["MortarWorldRays"] = "81 mm mortar mission: untargeted fragment rays per bomb for walls, props and the debug draw",
            ["MortarWalksPerLimb"] = "81 mm mortar mission: most fragment wounds one limb takes from one bomb",
            ["MortarFragBeltDeg"] = "81 mm mortar mission: the side-spray belt's thickness, degrees, square to the bomb's flight (0 = an even sphere)",
            ["MortarFragBeltShare"] = "81 mm mortar mission: share of the bomb's fragments in the belt, 0..1",
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
            ["Gun30Burst"] = "30 mm gun run: seconds the A-10 fires (3,900 rounds a minute)",
            ["Gun20Burst"] = "20 mm gun run: seconds the F-22 fires (6,000 rounds a minute)",
            ["AirTimeOnTarget"] = "air strikes: seconds from the call to the attack",
            ["AirAttackHeading"] = "air strikes: attack heading in degrees from +Z; -1 = planned per strike",
            ["AirApproachVariance"] = "air strikes: how much chance goes into picking among the approaches that reach the mark (0 = always the best)",
            ["AirOverflyChance"] = "air strikes: share of jet attacks that fly on over the mark afterwards instead of breaking away before it",
            ["AirEntryTurnMax"] = "air strikes: how far round (degrees) an aircraft may come in onto its attack heading",
            ["JdamDelayFuze"] = "JDAM: on a mark under cover, the bomb goes through the roof and off at the mark",
            ["AirDangerClose"] = "air strikes: a mark this close to you (m) is called danger close",
            ["Gun30RateOfFire"] = "30 mm gun run: rounds a minute",
            ["Gun30Speed"] = "30 mm gun run: the aircraft's speed, m/s",
            ["Gun30DiveAngle"] = "30 mm gun run: dive angle, degrees",
            ["Gun30FireRange"] = "30 mm gun run: slant range to the mark where it opens fire, m",
            ["Gun30Walk"] = "30 mm gun run: length of the line of hits walked through the mark, m",
            ["Gun30Dispersion"] = "30 mm gun run: mils from the aim that 80 % of rounds land within",
            ["Gun30HEIShare"] = "30 mm gun run: share of high-explosive rounds (the rest armour-piercing)",
            ["Gun30MuzzleVelocity"] = "30 mm gun run: muzzle velocity, m/s",
            ["Gun30HEIChargeKgTNT"] = "30 mm gun run: each HEI round's burst, kg TNT (the real fill, scaled up for effect)",
            ["Gun30HEIDamageScale"] = "30 mm gun run: scales the pressure each HEI burst's wounds are judged at",
            ["Gun30HEIPushScale"] = "30 mm gun run: how hard each HEI burst's blast throws bodies and props, times BlastPushScale",
            ["Gun30FragRayCount"] = "30 mm gun run: fragments per HEI burst",
            ["Gun30HEIFragPower"] = "30 mm gun run: wound power of each fragment",
            ["Gun30HEIFragKick"] = "30 mm gun run: m/s a fragment hit gives what it hits, at full speed",
            ["Gun30HEIOverpressurePoints"] = "30 mm gun run: blast wounds drawn per limb the burst tears open",
            ["Gun30FxEvery"] = "30 mm gun run: draw the burst effect on every Nth HEI hit (raise if frame time suffers)",
            ["Gun20RateOfFire"] = "20 mm gun run: rounds a minute",
            ["Gun20Speed"] = "20 mm gun run: the aircraft's speed, m/s",
            ["Gun20DiveAngle"] = "20 mm gun run: dive angle, degrees",
            ["Gun20FireRange"] = "20 mm gun run: slant range to the mark where it opens fire, m",
            ["Gun20Walk"] = "20 mm gun run: length of the line of hits walked through the mark, m",
            ["Gun20Dispersion"] = "20 mm gun run: mils from the aim that 80 % of rounds land within",
            ["Gun20HEIShare"] = "20 mm gun run: share of high-explosive rounds (the rest armour-piercing)",
            ["Gun20MuzzleVelocity"] = "20 mm gun run: muzzle velocity, m/s",
            ["Gun20HEIChargeKgTNT"] = "20 mm gun run: each HEI round's burst, kg TNT (the real fill, scaled up for effect)",
            ["Gun20HEIDamageScale"] = "20 mm gun run: scales the pressure each HEI burst's wounds are judged at",
            ["Gun20HEIPushScale"] = "20 mm gun run: how hard each HEI burst's blast throws bodies and props, times BlastPushScale",
            ["Gun20FragRayCount"] = "20 mm gun run: fragments per HEI burst",
            ["Gun20HEIFragPower"] = "20 mm gun run: wound power of each fragment",
            ["Gun20HEIFragKick"] = "20 mm gun run: m/s a fragment hit gives what it hits, at full speed",
            ["Gun20HEIOverpressurePoints"] = "20 mm gun run: blast wounds drawn per limb the burst tears open",
            ["Gun20FxEvery"] = "20 mm gun run: draw the burst effect on every Nth HEI hit (raise if frame time suffers)",
            ["JdamBurstHeight"] = "JDAM: 0 = goes off on impact; above 0 an air burst, this many metres short of what it hits along its path",
            ["JdamTimeOnTarget"] = "JDAM: seconds from the call to impact",
            ["JdamHeading"] = "JDAM: the bearing the bombers fly in on, degrees from +Z; -1 = planned per drop",
            ["JdamSpeed"] = "JDAM: the strike jet's speed, m/s",
            ["JdamReleaseAltitude"] = "JDAM: height over the mark the bomb is released at, m",
            ["JdamReleaseRange"] = "JDAM: ground distance short of the mark the bomb is released at, m",
            ["JdamImpactAngle"] = "JDAM: how steeply the bomb comes down at the end, degrees from level",
            ["JdamImpactSpeed"] = "JDAM: the bomb's speed at impact, m/s (kept under the speed of sound)",
            ["JdamCEP"] = "JDAM: half the bombs land within this many metres of the mark (5 with GPS)",
            ["JdamDangerClose"] = "JDAM: a mark this close to you (m) is danger close for the 2000 lb; the smaller bombs by the cube root of their charge",
            ["JdamFallSound"] = "JDAM: seconds of the bomb's fall you hear before it lands",
            ["JdamDamageScale"] = "JDAM: scales the pressure the bombs' wounds are judged at",
            ["JdamInjuryRange"] = "JDAM: the farthest out the blast wave is checked for injuries, m (FruitLib's own default is 60)",
            ["JdamFragBeltDeg"] = "JDAM: the side-spray belt's thickness, degrees: most of the case flies out in this band square to the bomb's axis (0 = an even sphere)",
            ["JdamFragBeltShare"] = "JDAM: share of the fragments in the belt, 0..1; the rest go every way (nose and tail spray)",
            ["JdamWalksPerLimb"] = "JDAM: most fragment wounds one limb takes from one bomb; extra hits push and add their power to these (lower = shorter hitch near the bomb)",
            ["Jdam500ChargeKgTNT"] = "500 lb JDAM (GBU-38, Mk 82): the charge, kg TNT",
            ["Jdam500Fragments"] = "500 lb JDAM: the case's fragments; each body in reach gets its expected share of them, aimed (cost follows the hits, not this number)",
            ["Jdam500WorldRays"] = "500 lb JDAM: untargeted fragment rays for walls, props and the debug draw; they pass through bodies",
            ["Jdam500FragPower"] = "500 lb JDAM: wound power of each fragment at the bomb",
            ["Jdam500PushRange"] = "500 lb JDAM: the farthest the blast wave throws anything, m",
            ["Jdam500MaxWounds"] = "500 lb JDAM: wounds it may cut, blast and fragments together (its own, instead of MaxWoundsPerExplosion; higher = more torn up, longer hitch)",
            ["Jdam1000ChargeKgTNT"] = "1000 lb JDAM (GBU-32, Mk 83): the charge, kg TNT",
            ["Jdam1000Fragments"] = "1000 lb JDAM: the case's fragments, shared out over the bodies in reach",
            ["Jdam1000WorldRays"] = "1000 lb JDAM: untargeted fragment rays for the scenery",
            ["Jdam1000FragPower"] = "1000 lb JDAM: wound power of each fragment at the bomb",
            ["Jdam1000PushRange"] = "1000 lb JDAM: the farthest the blast wave throws anything, m",
            ["Jdam1000MaxWounds"] = "1000 lb JDAM: wounds it may cut, blast and fragments together (instead of MaxWoundsPerExplosion)",
            ["Jdam2000ChargeKgTNT"] = "2000 lb JDAM (GBU-31, Mk 84): the charge, kg TNT",
            ["Jdam2000Fragments"] = "2000 lb JDAM: the case's fragments, shared out over the bodies in reach",
            ["Jdam2000WorldRays"] = "2000 lb JDAM: untargeted fragment rays for the scenery",
            ["Jdam2000FragPower"] = "2000 lb JDAM: wound power of each fragment at the bomb",
            ["Jdam2000PushRange"] = "2000 lb JDAM: the farthest the blast wave throws anything, m",
            ["Jdam2000MaxWounds"] = "2000 lb JDAM: wounds it may cut, blast and fragments together (instead of MaxWoundsPerExplosion)",
            ["MoabBurstHeight"] = "MOAB: it goes off this many metres short of what it would hit along its fall (an air burst)",
            ["MoabImpactAngle"] = "MOAB: how steeply it comes down at the end, degrees from level",
            ["MoabImpactSpeed"] = "MOAB: its speed at the end of its fall, m/s",
            ["MoabDangerClose"] = "MOAB: a mark this close to you (m) is danger close",
            ["MoabCarrierSpeed"] = "MOAB: the MC-130J's speed on its run, m/s (a heavy drop is flown slow)",
            ["MoabReleaseRange"] = "MOAB: ground distance short of the mark the bomb leaves the ramp, m",
            ["MoabChargeKgTNT"] = "MOAB (GBU-43/B): the charge, kg TNT (8,500 kg of H-6 is about 11,000)",
            ["MoabSurfaceBurstHeight"] = "MOAB: a burst this low counts as a surface burst (its wave off the ground adds 1.8x), as a scaled height, m/kg^(1/3): 0.15 is about 3 m for 11 t; 0 = only on contact",
            ["MoabDamageScale"] = "MOAB: scales the pressure its wounds are judged at",
            ["MoabPushRange"] = "MOAB: the farthest its blast wave throws anything, m (uncapped it reaches ~790 m; everything inside is swept)",
            ["MoabInjuryRange"] = "MOAB: the farthest out the blast wave is checked for injuries, m (they stop mattering at about 95 m)",
            ["MoabFragments"] = "MOAB: the aluminium case's fragments, shared out over the bodies in reach (calibrated to a ~150 m lethal radius; cost follows the hits)",
            ["MoabWorldRays"] = "MOAB: untargeted fragment rays for walls, props and the debug draw",
            ["MoabFragPower"] = "MOAB: wound power of each fragment at the bomb",
            ["MoabFragBeltDeg"] = "MOAB: the side-spray belt's thickness, degrees, square to its fall (0 = an even sphere)",
            ["MoabFragBeltShare"] = "MOAB: share of the fragments in the belt, 0..1",
            ["MoabWalksPerLimb"] = "MOAB: most fragment wounds one limb takes; extra hits push and add their power (it reaches many limbs: keep it low)",
            ["MoabMaxWounds"] = "MOAB: wounds it may cut, blast and fragments together (higher = more torn up, longer hitch)",
            ["CbuDudRate"] = "CBU-87: share of bomblets that don't go off and lie live, going off when moved or shot (real: about 5 %)",
            ["CbuBomblets"] = "CBU-87: BLU-97/B bomblets in the dispenser (202)",
            ["CbuOpenHeight"] = "CBU-87: height over the mark the dispenser opens at, m",
            ["CbuCEP"] = "CBU-87: half the dispensers open over a point within this many metres of the mark",
            ["CbuPatternWidth"] = "CBU-87: the bomblets' pattern across the run, m",
            ["CbuPatternLength"] = "CBU-87: the bomblets' pattern along the run, m",
            ["CbuBombletSpeed"] = "CBU-87: a bomblet's speed falling under its decelerator, m/s (each varies, so they land over a second or two)",
            ["CbuDangerClose"] = "CBU-87: a mark this close to you (m) is danger close",
            ["CbuDudSensitivity"] = "CBU-87: a live dud goes off when something moves it faster than this, m/s",
            ["CbuMaxDuds"] = "CBU-87: most live duds lying about at once; the oldest is cleared past it",
            ["CbuBurstsPerFrame"] = "CBU-87: most bomblets going off in one frame; the rest go off on the next (lower = smoother, a little later)",
            ["CbuFxEvery"] = "CBU-87: draw every Nth bomblet's burst effect (raise if frame time suffers)",
            ["Blu97ChargeKgTNT"] = "BLU-97/B bomblet: its blast, kg TNT",
            ["Blu97Fragments"] = "BLU-97/B: the scored case's fragments (about 300), shared out over the bodies in reach",
            ["Blu97WorldRays"] = "BLU-97/B: untargeted fragment rays for walls, props and the debug draw",
            ["Blu97FragPower"] = "BLU-97/B: wound power of each fragment at the bomblet",
            ["Blu97DamageScale"] = "BLU-97/B: scales the pressure its wounds are judged at",
            ["Blu97WalksPerLimb"] = "BLU-97/B: most fragment wounds one limb takes from one bomblet",
            ["Blu97MaxWounds"] = "BLU-97/B: wounds one bomblet may cut (fragments and blast)",
            ["Blu97FragBeltDeg"] = "BLU-97/B: the side-spray belt's thickness, degrees, square to its fall (0 = an even sphere)",
            ["Blu97FragBeltShare"] = "BLU-97/B: share of the fragments in the belt, 0..1",
            ["Blu97JetRays"] = "BLU-97/B: rays of its shaped charge's jet, fired down its fall (0 = no jet)",
            ["Blu97JetPenetration"] = "BLU-97/B: metres of wall the jet goes through for free (a roof, into the room under it)",
            ["Blu97JetPower"] = "BLU-97/B: wound power of each jet ray",
            ["Blu97JetSpallCount"] = "BLU-97/B: spall blown off the back of each wall the jet goes through",
            ["HydraRockets"] = "rockets, HE: Hydra 70 M151 rockets per mission, fired in pairs",
            ["FlechetteRockets"] = "rockets, flechette: M255A1 rockets per mission (1,179 darts each, every one a real round)",
            ["RocketRunSpeed"] = "rockets: the AH-64's speed on its run, m/s",
            ["RocketDiveAngle"] = "rockets: the AH-64's dive while it fires, degrees",
            ["RocketFireRange"] = "rockets: slant range to the mark where the first pair leaves, m",
            ["RocketPairInterval"] = "rockets: seconds between pairs",
            ["RocketDispersion"] = "rockets: mils from the aim that 80 % of the rockets land within (unguided)",
            ["RocketWalk"] = "rockets, HE: length of the line the pairs are walked along through the mark, m",
            ["RocketDangerClose"] = "rockets: a mark this close to you (m) is danger close",
            ["HydraChargeKgTNT"] = "rockets, HE: each M151's burst, kg TNT",
            ["HydraFragments"] = "rockets, HE: each M151 body's fragments, shared out over the bodies in reach (cost follows the hits, not this number)",
            ["HydraWorldRays"] = "rockets, HE: untargeted fragment rays for walls, props and the debug draw",
            ["HydraFragPower"] = "rockets, HE: wound power of each fragment at the burst",
            ["HydraDamageScale"] = "rockets, HE: scales the pressure each burst's wounds are judged at",
            ["HydraWalksPerLimb"] = "rockets, HE: most fragment wounds one limb takes from one burst",
            ["HydraMaxWounds"] = "rockets, HE: wounds one burst may cut, blast and fragments together",
            ["HydraFragBeltDeg"] = "rockets, HE: the side-spray belt's thickness, degrees, square to the rocket's flight (0 = an even sphere)",
            ["HydraFragBeltShare"] = "rockets, HE: share of the fragments in the belt, 0..1",
            ["FlechetteBurstRange"] = "rockets, flechette: metres short of the mark the fuze throws the darts",
            ["FlechetteCount"] = "rockets, flechette: darts per rocket (M255A1: 1,179 of 3.9 g)",
            ["FlechettePatternRadius"] = "rockets, flechette: the darts are thrown to land evenly within this radius (m) of where the rocket would have come down; 0 = a free cone of FlechetteConeDeg (at a shallow dive most darts then land far past the mark)",
            ["FlechetteConeDeg"] = "rockets, flechette: half-angle of the free cone, degrees (FlechettePatternRadius 0, or a rocket that meets something before its fuze)",
            ["FlechettePowerScale"] = "rockets, flechette: scales each dart's wound power (1 = from its speed and mass)",
            ["FlechetteHitFxEvery"] = "rockets, flechette: a puff of dust where every Nth dart hits the ground (raise if frame time suffers)",
            ["FlashFuse"] ="flashbang: seconds from the spoon to the bang",
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
            ["ChargeKgTNT"] = "grenade filler as kg of TNT (an M67's 180 g of Comp B is ~0.2). Drives the blast wave: lungs, then gut, then skin, then limbs give way as the pressure rises",
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
            ["MissileHEFragments"] = "HE warhead: its body's fragments, shared out over the bodies in reach",
            ["MissileHEWorldRays"] = "HE warhead: untargeted fragment rays for walls, props and the debug draw",
            ["MissileHEWalksPerLimb"] = "HE warhead: most fragment wounds one limb takes from one warhead",
            ["MissileHEFragBeltDeg"] = "HE warhead: the side-spray belt's thickness, degrees, square to the missile's flight (0 = an even sphere)",
            ["MissileHEFragBeltShare"] = "HE warhead: share of the fragments in the belt, 0..1",
            ["MaxWoundsPerExplosion"] = "hard cap on ApplyWound calls per detonation — keeps the game's wound queue from stalling (lower = faster, 0 = unlimited)",

            ["CamFXEnabled"] = "master toggle for shake / post-process",
            ["CamFXIntensity"] = "0 = off, 1 = default, 5 = max",

            ["VFXIntensity"] = "scales visual effect intensity",
            ["Shockwave"] = "a blast's shock front seen as a ring of bent light racing out from it, then gone",
            ["ShockwaveStrength"] = "how far the shock front bends the picture behind it (1 = about 14 pixels at 1080p for a strong front; 0 = off)",
            ["ShockwaveMinCharge"] = "smallest charge that shows a shock front, kg TNT (0.5: C4 and up; a hand grenade's front is gone within two frames)",
            ["ShockwaveFadeKPa"] = "the shock front fades out as its overpressure falls to this, kPa (lower = it shows farther out and longer)",
            ["ShockwaveGlint"] = "the faint pale line along the shock front, so it shows against plain sky (0 = none)",
            ["ShockwaveMax"] = "shock fronts drawn at once; the oldest goes first",
            ["ShockwaveRefract"] = "read the picture behind the shock front (URP's opaque texture); false = only a pale ring",
            ["DebrisRaysRatio"] = "fraction of frag rays that spawn debris visuals",
            ["DebrisMaxPerExplosion"] = "hard cap on debris chunks per explosion, regardless of ray count",
            ["AdaptiveQuality"] = "scale ray/wound/debris counts down automatically under load (see FruitLib's perf monitor, F11)",
            ["MinQualityScale"] = "AdaptiveQuality floor — 0.25 = never drop below a quarter of configured counts",

            ["DebugLevel"] = "0 = silent, 1 = key events, 2 = verbose",
            ["DebugDrawExplosions"] = "draw each fragment path and blast line of a detonation (red = lodged, magenta = through a limb, yellow = ricochet, cyan = through a wall, orange = stopped) and log a summary per explosion",
            ["DebugDrawSeconds"] = "how long each debug line stays on screen (s)",
            ["DebugDrawMaxLines"] = "debug line pool size; when full the oldest line is reused",
            ["DebugDrawSpentEvery"] = "draw only every Nth fragment that hit nothing (they are most of them); 1 = all",
            ["DebugHotkeys"] = "turns the test bench key on (TestBenchKey); off, it does nothing",
            ["TestBenchKey"] = "with DebugHotkeys on: puts a row of test walls across your view (replacing the last); with Shift it removes them (None to disable)",
            ["TestBenchWalls"] = "how many test walls",
            ["TestBenchThickness"] = "thickness of each test wall, metres",
            ["TestBenchGap"] = "air gap between test walls, metres",
            ["TestBenchDistance"] = "how far ahead the first test wall stands, metres",
            ["TestBenchMaterial"] = "what the test walls are made of, by FruitLib surface name: Concrete, Brick, Steel, Wood, Drywall, Glass, Soil, Water",
            ["DebugDrawAirPlan"] = "air strikes: draw each strike's plan until it's over: the reach lines tried from the mark (green reach it, red don't), the chosen axis (yellow) and the aircraft's flight (cyan)",
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
            sb.AppendLine($"# ║        BombsAway v{Core.Version}  —  Configuration                    ║");
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

            FruitLib.FruitPaths.WriteAllTextAtomic(IniPath, sb.ToString());
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
