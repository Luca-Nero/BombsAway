using UnityEngine;

namespace BombsAway
{
    internal static class Config
    {
        // ── Grenade ───────────────────────────────────────────────────────────────
        // Blast radius / force and overpressure radius are the old model's, used only when a
        // charge's ChargeKgTNT is 0: with a charge the blast wave sets push and injury itself.
        [FruitLib.MenuCategory("Grenade")] public static float Fuse = 2f;
        public static float FlashRate = 0.2f;
        [FruitLib.MenuCategory("Grenade")] public static float ThrowForce = 8f;
        [FruitLib.MenuCategory("Grenade")] public static float ThrowArc = -15f;
        public static float BlastRadius = 5f;
        public static float BlastForce = 1f;
        public static float BlastUpward = 1f;
        public static float OverpressureRadius = 3.5f;
        public static float OverpressureFalloffExp = 1;
        public static int OverpressureWoundPoints = 12;
        [FruitLib.MenuCategory("Grenade")] public static int FragRayCount = 2000;
        public static float FragSpeed = 15f;
        public static float FragMaxTime = 4f;
        public static float FragImpulse = 0.4f;
        [FruitLib.MenuCategory("Grenade")] public static float DamageScale = 1f;
        [FruitLib.MenuCategory("Grenade")] public static int FragPower = 3000;
        [FruitLib.MenuCategory("Grenade")] public static float ChargeKgTNT = 0.2f;

        // ── Smoke grenade ─────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Smoke")] public static float SmokeFuse = 1.5f;
        [FruitLib.MenuCategory("Smoke")] public static float SmokeDuration = 40f;
        /// <summary>Emission scale: below 1 for a thinner (cheaper) cloud.</summary>
        [FruitLib.MenuCategory("Smoke")] public static float SmokeDensity = 1f;
        public static float SmokeCanisterLife = 20f;   // seconds the spent can lies there after the smoke
        // Changing the colour in hand (SmokeTerminal.cs): a DOS window beside the can reloads the
        // dye while the band steps to the new colour.
        /// <summary>A terminal pops up beside the can and reprograms its dye when the colour changes.</summary>
        [FruitLib.MenuCategory("Smoke")] public static bool SmokeTerminal = true;
        public static float SmokeRetintTime = 0.5f;    // seconds the band takes to reach the new colour (0 = at once, no window)
        public static float SmokeTermOffsetX = 0.13f;  // the window's middle from the can's, metres in camera axes (right, up, forward)
        public static float SmokeTermOffsetY = 0.06f;
        public static float SmokeTermOffsetZ = 0f;
        public static float SmokeTermWidth = 0.17f;    // metres wide (the can is about 0.43 m from the eye)
        public static float SmokeTermYaw = 14f;        // degrees turned away from facing the eye square on (the spawn terminal too)
        // The smoke (SmokeCloud.cs, SmokePlume.cs): a plume that rises while warm, bends and drifts
        // with the wind, thins as it spreads, and is drawn as one faceted volume.
        /// <summary>Thin smoke drawn as see-through haze round the solid smoke.</summary>
        [FruitLib.MenuCategory("Smoke")] public static bool SmokeHaze = false;
        /// <summary>The breeze, metres per second (0 = still air). Its heading wanders slowly.</summary>
        [FruitLib.MenuCategory("Smoke")] public static float WindSpeed = 0.4f;
        public static float WindHeading = -1f;         // degrees from +Z the wind blows toward; -1 = random per scene
        // Blasts and rockets push the smoke: a hole that closes again (SmokeCloud.Blast, Wake).
        public static float SmokeBlastClear = 3f;      // metres of hole per cube root of a kilo of TNT (0 = smoke ignores blasts)
        public static float SmokeRefill = 1f;          // how long holes take to close, x (2.5 s + 1 s per metre)
        // Smoke hides things (SmokeCloud.Transmittance): the Javelin's DAY / NIGHT views can't
        // lock through it (WHOT / BHOT can), and it shields eyes from a flashbang.
        [FruitLib.MenuCategory("Smoke")] public static bool SmokeBlocksSight = true;
        public static float SmokeLockClear = 0.35f;    // share of the view a day or night sight needs clear of smoke to track

        // ── Flashbang ─────────────────────────────────────────────────────────────
        // Two senses (Flashbang.cs). Sight: full within FlashFullRange, inverse square beyond,
        // times the eye's cone (full inside FlashFocusAngle of where the eyes point, easing to
        // FlashPeripheralStrength at FlashPeripheralAngle, nothing behind), times FlashOcclusion
        // behind cover. Hearing: the same falloff, cover counting less, facing not at all.
        // Bodies lose balance (they go down) and some muscle; the hands go over the eyes or ears.
        [FruitLib.MenuCategory("Flashbang")] public static float FlashFuse = 1.5f;
        [FruitLib.MenuCategory("Flashbang")] public static float FlashFullRange = 10f;
        [FruitLib.MenuCategory("Flashbang")] public static float FlashStunTime = 4f;
        [FruitLib.MenuCategory("Flashbang")] public static float FlashRecoverTime = 1.5f;
        /// <summary>Balance held by a full stun, percent of normal (0 = falls).</summary>
        [FruitLib.MenuCategory("Flashbang")] public static float FlashBalance = 0f;
        /// <summary>Muscle held by a full stun, percent of normal: enough left for the arms to reach the face.</summary>
        [FruitLib.MenuCategory("Flashbang")] public static float FlashMuscle = 40f;
        [FruitLib.MenuCategory("Flashbang")] public static bool FlashCoverFace = true;
        public static float FlashCoverAt = 0.25f;           // sight (eyes) or hearing (ears) from which the hands go up
        public static float FlashFocusAngle = 25f;          // degrees off where the eyes point: full sight
        public static float FlashPeripheralAngle = 100f;    // degrees: the edge of sight
        public static float FlashPeripheralStrength = 0.35f;
        public static float FlashOcclusion = 0.3f;          // light through cover, share of the open one
        public static float FlashHearingOcclusion = 0.6f;   // sound through cover
        public static float FlashHearingStun = 0.4f;        // a body's stun from the bang alone (not seen), share of hearing
        public static float FlashMinStrength = 0.05f;       // below this a body is left alone
        public static bool FlashCoverOnRig = true;          // hands placed against the IK rig's head (off: the physical head, as in 5.6.3)
        public static float FlashCloseRange = 3f;           // metres: a body this near is blinded whichever way it faces, if the bang can see its head
        // Light off walls (Flashbang.Reflected): a bang in a room blinds whichever way you face.
        [FruitLib.MenuCategory("Flashbang")] public static float FlashReflection = 1f;
        public static int FlashReflectRays = 64;            // rays from the bang that find the lit surfaces
        public static float FlashReflectRange = 12f;        // metres: surfaces further than this aren't lit enough to count
        // The player: one switch for the blind, the ringing and the kick (off: you only hear the
        // bang). Replaces FlashBlindPlayer / FlashDeafenPlayer (5.6.1) and FlashPlayerEffect (5.6.0).
        [FruitLib.MenuCategory("Flashbang")] public static bool FlashPlayer = true;
        [FruitLib.MenuCategory("Flashbang")] public static bool FlashBlackout = false;
        [FruitLib.MenuCategory("Flashbang")] public static float FlashBlindTime = 3.5f;
        public static float FlashBlindFade = 4f;            // seconds the screen takes to clear after the hold, at full sight
        public static float FlashRingTime = 10f;            // seconds your ears ring at full hearing
        public static float FlashRingPitchMin = 0.875f;     // x 4 kHz
        public static float FlashRingPitchMax = 1.6f;
        public static float FlashChargeKgTNT = 0.02f;

        // ── C4 ────────────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("C4")] public static float C4ThrowForce = 8f;
        [FruitLib.MenuCategory("C4")] public static float C4ThrowArc = -15f;
        public static float C4BlastRadius = 5f;
        public static float C4BlastForce = 1.5f;
        public static float C4BlastUpward = 1f;
        public static float C4OverpressureRadius = 5f;
        public static float C4OverpressureFalloffExp = 1;
        public static int C4OverpressureWoundPoints = 18;
        [FruitLib.MenuCategory("C4")] public static int C4FragRayCount = 2000;
        public static float C4FragSpeed = 20f;
        public static float C4FragMaxTime = 2f;
        public static float C4FragImpulse = 0.2f;
        [FruitLib.MenuCategory("C4")] public static float C4DamageScale = 1.5f;
        [FruitLib.MenuCategory("C4")] public static int C4FragPower = 2000;
        [FruitLib.MenuCategory("C4")] public static float C4ChargeKgTNT = 0.75f;

        // ── Claymore ──────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Claymore")] public static float MineThrowForce = 8f;
        [FruitLib.MenuCategory("Claymore")] public static float MineThrowArc = -15f;
        [FruitLib.MenuCategory("Claymore")] public static float MineProximityRange = 10f;
        // Its three lasers are the tripwires (out to MineProximityRange, stopped by walls like the
        // beams you see): a body breaking one sets it off MineTripDelay later. Off: the old cone.
        [FruitLib.MenuCategory("Claymore")] public static bool MineTripwire = true;
        [FruitLib.MenuCategory("Claymore")] public static float MineTripDelay = 0.35f;
        public static float MineBlastRadius = 6f;
        public static float MineBlastForce = 1f;
        public static float MineBlastUpward = 0f;
        public static float MineOverpressureRadius = 3.5f;
        public static float MineOverpressureFalloffExp = 1;
        public static int MineOverpressureWoundPoints = 12;
        [FruitLib.MenuCategory("Claymore")] public static int MineFragRayCount = 1000;
        public static float MineFragSpeed = 45f;
        public static float MineFragMaxTime = 2f;
        public static float MineFragImpulse = 0.2f;
        [FruitLib.MenuCategory("Claymore")] public static float MineDamageScale = 1.2f;
        [FruitLib.MenuCategory("Claymore")] public static int MineFragPower = 3800;
        [FruitLib.MenuCategory("Claymore")] public static float MineChargeKgTNT = 0.9f;

        // ── Missile warhead ───────────────────────────────────────────────────────
        public static float MissileBlastRadius = 3f;
        public static float MissileBlastForce = 1f;
        public static float MissileBlastUpward = 0f;
        public static float MissileOverpressureRadius = 3f;
        public static float MissileOverpressureFalloffExp = 1;
        public static int MissileOverpressureWoundPoints = 12;
        [FruitLib.MenuCategory("Missile")] public static int MissileFragRayCount = 1000;
        public static float MissileFragSpeed = 30f;
        public static float MissileFragMaxTime = 2f;
        public static float MissileFragImpulse = 0.4f;
        [FruitLib.MenuCategory("Missile")] public static float MissileDamageScale = 2f;
        [FruitLib.MenuCategory("Missile")] public static int MissileFragPower = 4000;
        [FruitLib.MenuCategory("Missile")] public static float MissileChargeKgTNT = 1.5f;
        [FruitLib.MenuCategory("Missile")] public static float MissileJetPenetration = 0.8f;
        public static int MissileJetRays = 12;
        public static float MissileJetConeDeg = 3f;
        [FruitLib.MenuCategory("Missile")] public static int MissileJetPower = 30000;
        [FruitLib.MenuCategory("Missile")] public static int MissileJetSpallCount = 60;

        // ── Missile HE warhead ────────────────────────────────────────────────────
        public static float MissileHEBlastRadius = 6f;
        public static float MissileHEBlastForce = 4f;
        public static float MissileHEBlastUpward = 1f;
        public static float MissileHEOverpressureRadius = 12f;
        public static float MissileHEOverpressureFalloffExp = 1f;
        public static int MissileHEOverpressureWoundPoints = 24;
        [FruitLib.MenuCategory("Missile HE")] public static int MissileHEFragRayCount = 2000;
        public static float MissileHEFragSpeed = 30f;
        public static float MissileHEFragMaxTime = 4f;
        public static float MissileHEFragImpulse = 0.2f;
        [FruitLib.MenuCategory("Missile HE")] public static float MissileHEDamageScale = 1.25f;
        [FruitLib.MenuCategory("Missile HE")] public static int MissileHEFragPower = 3000;
        [FruitLib.MenuCategory("Missile HE")] public static float MissileHEChargeKgTNT = 3f;

        // ── Missile TBX (thermobaric) warhead ─────────────────────────────────────
        // Two stages (Missile/Thermobaric.cs): at impact a small charge spreads a fuel cloud
        // (no damage); MissileTBXIgniteDelay later it goes off - all blast, no fragments, a
        // wide wave that fills rooms and reaches round cover (MissileTBXDiffraction) - and
        // MissileTBXSuctionDelay after that the air rushes back in, pulling loose things and
        // bodies toward the middle (MissileTBXSuction).
        [FruitLib.MenuCategory("Missile TBX")] public static float MissileTBXChargeKgTNT = 4.5f;
        [FruitLib.MenuCategory("Missile TBX")] public static float MissileTBXDamageScale = 1.2f;
        [FruitLib.MenuCategory("Missile TBX")] public static float MissileTBXIgniteDelay = 0.15f;
        [FruitLib.MenuCategory("Missile TBX")] public static float MissileTBXSuction = 4f;
        public static float MissileTBXSuctionDelay = 0.3f;     // seconds after ignition the air rushes back
        public static float MissileTBXSuctionRadius = 14f;     // metres it reaches
        public static float MissileTBXDiffraction = 0.6f;      // what reaches something behind full cover (other charges 0.15)
        public static float MissileTBXCloudLift = 0.6f;        // metres back from the impact the cloud's middle is
        public static float MissileTBXBlastRadius = 9f;        // the old-model fallbacks (no charge)
        public static float MissileTBXBlastForce = 6f;
        public static float MissileTBXBlastUpward = 1.5f;
        public static float MissileTBXOverpressureRadius = 16f;
        public static int MissileTBXOverpressureWoundPoints = 30;

        // ── Fire support (binoculars) ─────────────────────────────────────────────
        // The binoculars (FireSupport/): right mouse to look, the wheel steps the zoom through
        // BinoZoomLevels, hold left mouse on a target for LaseTime to fix it and call a mission.
        // A 155 mm battery answers on the radio; ArtyRounds shells land round the mark
        // (ArtyDispersion) ArtyShotDelay + ArtyFlightTime later.
        public static string BinoZoomLevels = "3, 7, 14";   // replaces BinoZoom (5.8.3): the wheel steps through these
        [FruitLib.MenuCategory("Fire Support")] public static float LaseTime = 3f;
        [FruitLib.MenuCategory("Fire Support")] public static int ArtyRounds = 6;
        [FruitLib.MenuCategory("Fire Support")] public static float ArtyDispersion = 10f;
        [FruitLib.MenuCategory("Fire Support")] public static float ArtyFlightTime = 10f;
        [FruitLib.MenuCategory("Fire Support")] public static float ArtyChargeKgTNT = 6.6f;
        [FruitLib.MenuCategory("Fire Support")] public static float ArtyDamageScale = 1.5f;
        [FruitLib.MenuCategory("Fire Support")] public static int ArtyFragRayCount = 2500;
        [FruitLib.MenuCategory("Fire Support")] public static int ArtyFragPower = 3500;
        public static float BinoAdsTime = 0.25f;
        public static float BinoSensitivity = 1f;
        public static float LaseRange = 1500f;
        public static float LaseHoldMils = 10f;
        public static float LaseGrace = 0.6f;
        public static float ArtyShotDelay = 4f;
        public static float ArtySplashWarning = 5f;
        public static float ArtyVolleySpread = 3f;
        public static float ArtyDescentAngle = 65f;
        // Renamed in 5.8.4 (were ArtyShellSpeed 300, ArtyWhistleTime 3.4): the whistle now rides
        // the shell, and at 300 m/s a shell nearly keeps up with its own sound, so its whistle
        // piled up into about a second.
        public static float ArtyTerminalSpeed = 160f;
        public static float ArtyWhistleFlight = 7f;
        public static float ArtyBatteryHeading = -1f;
        public static float ArtyDangerClose = 60f;
        public static bool ArtyStacking = false;
        public static float ArtyBurstLift = 0.3f;
        public static float ArtyBlastRadius = 12f;
        public static float ArtyBlastForce = 6f;
        public static float ArtyBlastUpward = 2f;
        public static float ArtyOverpressureRadius = 20f;
        public static int ArtyOverpressureWoundPoints = 24;
        public static float ArtyFragSpeed = 30f;
        public static float ArtyFragMaxTime = 4f;
        public static float ArtyFragImpulse = 0.2f;
        public static float RadioTypeRate = 40f;
        // Fire support on a terminal instead of the radio net (prototype, FireTerminal.cs): the unit
        // is spawned, a program runs the mission, and both are deleted at the end. What they say is
        // UserData/BombsAwayTerminal.txt (TerminalScript.cs); its programs say which missions they run.
        /// <summary>Prototype: the 155 mm HE barrage is called through a terminal that spawns its battery and runs it, instead of the radio net.</summary>
        [FruitLib.MenuCategory("Fire Support")] public static bool ArtyTerminal = true;
        /// <summary>Prototype: air strikes are called through the terminal too, each aircraft run by its own program.</summary>
        [FruitLib.MenuCategory("Fire Support")] public static bool AirTerminal = true;
        public static float ArtyTermDistance = 0.6f;   // metres from the eye the window is drawn at (its size on screen follows RadioTextSize)
        public static float ArtyTermScale = 0.8f;      // its size against the radio log's (1 = the log's glyph size)
        public static float ArtyTermYaw = -18f;        // degrees turned about its upright after facing the eye (negative: its outer edge toward you)
        public static float RadioTextSize = 0.85f;

        // The other missions (5.9.0), picked with Q / E while the binoculars are up. The 81 mm
        // mortars are their own unit (their own bearing, one mission of their own at a time);
        // smoke, illumination and precision are the 155 battery's, and share its shot delay,
        // splash warning and stacking rule.
        [FruitLib.MenuCategory("Fire Support")] public static int MortarRounds = 8;
        [FruitLib.MenuCategory("Fire Support")] public static int SmokeShellRounds = 4;
        [FruitLib.MenuCategory("Fire Support")] public static int IllumRounds = 3;
        public static float MortarDispersion = 16f;
        public static float MortarFlightTime = 7f;
        public static float MortarVolleySpread = 6f;
        public static float MortarDescentAngle = 78f;
        public static float MortarTerminalSpeed = 120f;
        public static float MortarBatteryHeading = -1f;
        public static float MortarChargeKgTNT = 0.95f;
        public static float MortarDamageScale = 1.2f;
        public static int MortarFragRayCount = 1200;
        public static int MortarFragPower = 2500;
        public static float MortarBlastRadius = 7f;
        public static float MortarBlastForce = 4f;
        public static float MortarBlastUpward = 1.5f;
        public static float MortarOverpressureRadius = 10f;
        public static int MortarOverpressureWoundPoints = 14;
        public static float MortarFragSpeed = 30f;
        public static float MortarFragMaxTime = 3f;
        public static float MortarFragImpulse = 0.15f;
        public static float SmokeShellDispersion = 22f;
        public static float SmokeShellVolleySpread = 4f;
        public static float SmokeShellBurnTime = 50f;
        public static float SmokeShellRate = 22f;
        public static float IllumDispersion = 35f;
        public static float IllumVolleySpread = 10f;
        public static float IllumBurstHeight = 90f;
        public static float IllumBurnTime = 50f;
        public static float IllumFallSpeed = 1.6f;
        public static float IllumWindScale = 3f;
        public static float IllumLightRange = 140f;
        public static float IllumLightIntensity = 5000f;   // URP falls off as 1/d^2: ~0.6 on the ground from 90 m
        public static int PrecisionRounds = 1;
        public static float PrecisionDispersion = 1f;
        public static float PrecisionFlightTime = 14f;
        public static float PrecisionDescentAngle = 84f;
        public static float PrecisionTerminalSpeed = 200f;

        // Air strikes (5.11.0), the AIR page of the strip: an aircraft checks in on the net, runs
        // in on the lased mark AirTimeOnTarget after the call and attacks it. One at a time unless
        // ArtyStacking. AirAttackHeading -1 = across your line of sight (the hits walk past you,
        // never toward you); 0-360 = that heading, degrees from +Z.
        // The 30 mm gun run (A-10, GAU-8): a dive at
        //
        // DiveAngle, opening fire Gun30FireRange
        // from the mark and walking Gun30Walk metres of hits through it; HEI rounds (Gun30HEIShare,
        // 5 of 6 in the real mix) burst where they hit, the rest are armour-piercing.
        // 5.12.0: the HEI burst made more devastating after the 5.11.0 test (a 4 s strafe ran
        // fine): three times the charge, wounds judged at 1.5x the pressure, the blast wave's push
        // x3, and fragments that hit harder and kick ten times as hard. Renamed (Gun30HEI*) so
        // the old values in the ini don't hold the new defaults back.
        [FruitLib.MenuCategory("Fire Support")] public static float Gun30Burst = 2f;
        // The 20 mm gun run (5.12.0, F-22, M61A2): faster, a shallower dive, a shorter and denser
        // line of smaller hits; PGU-28/B rounds, nearly all of them high-explosive.
        [FruitLib.MenuCategory("Fire Support")] public static float Gun20Burst = 1.2f;
        public static float AirTimeOnTarget = 18f;
        public static float AirAttackHeading = -1f;
        public static float AirDangerClose = 100f;
        // 5.25.0: each strike plans its own approach at the call (AirStrike.Approach.cs). Attack
        // axes all round the mark and several dive (or impact) angles are tried by casting lines
        // from the mark back along them: the ones that reach it, keep their hits from walking at
        // you and differ from the last strike are favoured, with AirApproachVariance of chance on
        // top. A mark under cover gets the axis through the least of it (a JDAM its delay fuze).
        // The aircraft comes in from a varied bearing and turns onto its run-in (up to
        // AirEntryTurnMax), and after the attack either breaks away before the mark or flies on
        // over it (AirOverflyChance, jets only). Its whole flight is checked against the world.
        // Off: the 5.24 approaches (across your line of sight; the bombs' bearing per scene).
        // AirAttackHeading / JdamHeading 0-360 still fix the heading either way.
        [FruitLib.MenuCategory("Fire Support")] public static bool AirDynamicApproach = true;
        public static float AirApproachVariance = 0.35f;   // 0 = always the best-scoring approach
        public static float AirOverflyChance = 0.5f;
        public static float AirEntryTurnMax = 110f;        // degrees
        public static bool JdamDelayFuze = true;           // a JDAM on a mark under cover goes through it and off at the mark
        public static float Gun30RateOfFire = 3900f;
        public static float Gun30Speed = 160f;
        public static float Gun30DiveAngle = 20f;
        public static float Gun30FireRange = 1100f;
        public static float Gun30Walk = 40f;
        public static float Gun30Dispersion = 5f;        // mils: 80 % of rounds land within this angle of the aim
        public static float Gun30HEIShare = 0.83f;
        public static float Gun30MuzzleVelocity = 1010f;
        public static float Gun30HEIChargeKgTNT = 0.15f;   // PGU-13/B's fill isn't published (~0.05 estimated); x3 for effect
        public static float Gun30HEIDamageScale = 1.5f;
        public static float Gun30HEIPushScale = 1f;        // the blast wave's push, times BlastPushScale
        public static int Gun30FragRayCount = 200;
        public static int Gun30HEIFragPower = 2200;
        public static float Gun30HEIFragKick = 0.5f;       // m/s each fragment hit gives what it hits, at full speed
        public static int Gun30HEIOverpressurePoints = 8;
        public static int Gun30FxEvery = 1;              // a hit's burst effect on every Nth HEI round (2 = half of them)
        public static float Gun30BlastRadius = 2.5f;
        public static float Gun30BlastForce = 1f;
        public static float Gun30BlastUpward = 0.5f;
        public static float Gun30OverpressureRadius = 2f;
        public static float Gun30FragSpeed = 30f;
        public static float Gun30FragMaxTime = 1f;
        public static float Gun20RateOfFire = 6000f;
        public static float Gun20Speed = 230f;
        public static float Gun20DiveAngle = 15f;
        public static float Gun20FireRange = 1000f;
        public static float Gun20Walk = 25f;
        public static float Gun20Dispersion = 6f;        // mils, as Gun30Dispersion
        public static float Gun20HEIShare = 0.9f;
        public static float Gun20MuzzleVelocity = 1050f;
        public static float Gun20HEIChargeKgTNT = 0.04f;   // PGU-28/B ~0.01; scaled as the 30 mm's
        public static float Gun20HEIDamageScale = 1.5f;
        public static float Gun20HEIPushScale = 3f;
        public static int Gun20FragRayCount = 100;
        public static int Gun20HEIFragPower = 1600;
        public static float Gun20HEIFragKick = 0.3f;
        public static int Gun20HEIOverpressurePoints = 6;
        public static int Gun20FxEvery = 1;
        public static float Gun20BlastRadius = 1f;
        public static float Gun20BlastForce = 0.7f;
        public static float Gun20BlastUpward = 0.4f;
        public static float Gun20OverpressureRadius = 1.5f;
        public static float Gun20FragSpeed = 30f;
        public static float Gun20FragMaxTime = 1f;

        // JDAMs (5.13.0): a strike jet (EAGLE) runs in high and level on a bearing fixed per scene
        // (JdamHeading, -1 = random), releases one GBU-38 / -32 / -31 JdamReleaseRange short of
        // the mark at JdamReleaseAltitude, and the bomb steers itself down onto the mark (CEP
        // JdamCEP), arriving steep (JdamImpactAngle) and fast. It goes off where its path first
        // meets something, or JdamBurstHeight metres short of that along its path (an air burst).
        // Charges are the real fills as TNT: Mk 82 89 kg, Mk 83 202 kg, Mk 84 429 kg of tritonal.
        // FruitLib 5.7.0's per-spec reach (JdamXPushRange, JdamInjuryRange) lets their blast
        // waves reach past the usual 40 / 60 m.
        [FruitLib.MenuCategory("Fire Support")] public static float JdamBurstHeight = 0f;
        public static float JdamTimeOnTarget = 26f;
        public static float JdamHeading = -1f;
        public static float JdamSpeed = 230f;
        public static float JdamReleaseAltitude = 1500f;
        public static float JdamReleaseRange = 2400f;
        public static float JdamImpactAngle = 65f;
        public static float JdamImpactSpeed = 300f;
        public static float JdamCEP = 5f;
        public static float JdamDangerClose = 400f;     // for the 2000 lb; the smaller bombs by the cube root of their charge
        public static float JdamFallSound = 8f;
        public static float JdamDamageScale = 1.5f;
        public static float JdamInjuryRange = 60f;
        // 5.14.0: the bombs aim their fragments (FruitLib 5.8.0's targeted fragments). JdamXFragments
        // is the case's real fragment count, shared out over the limbs in reach; JdamXWorldRays
        // (was JdamXFragRayCount, renamed so old ini values drop out) only dress the scenery.
        public static float JdamFragBeltDeg = 30f;     // side spray: most of the case leaves square to the bomb's axis
        public static float JdamFragBeltShare = 0.75f;
        public static int JdamWalksPerLimb = 2;
        public static float Jdam500ChargeKgTNT = 95f;
        public static int Jdam500Fragments = 10000;
        public static int Jdam500WorldRays = 300;
        public static int Jdam500FragPower = 16000;
        public static float Jdam500PushRange = 70f;
        public static int Jdam500MaxWounds = 600;      // 5.13.1: the 240 of MaxWoundsPerExplosion ran out on the first few bodies
        public static float Jdam1000ChargeKgTNT = 215f;
        public static int Jdam1000Fragments = 18000;
        public static int Jdam1000WorldRays = 350;
        public static int Jdam1000FragPower = 20000;
        public static float Jdam1000PushRange = 90f;
        public static int Jdam1000MaxWounds = 900;
        public static float Jdam2000ChargeKgTNT = 460f;
        public static int Jdam2000Fragments = 30000;
        public static int Jdam2000WorldRays = 400;
        public static int Jdam2000FragPower = 24000;
        public static float Jdam2000PushRange = 120f;
        public static int Jdam2000MaxWounds = 1200;

        // MOAB and CBU-87 (5.16.0): dropped as the JDAMs are (the F-15E, its heading, speed,
        // release point and time on target), each with its own fall and burst. Since 5.22.0 the
        // MOAB comes off an MC-130J's ramp instead: slower (MoabCarrierSpeed) and released
        // nearer (MoabReleaseRange), at the JDAMs' height and on their heading.
        // MOAB (GBU-43/B): 8,500 kg of H-6 (~11 t TNT) in a thin aluminium case, air-burst
        // MoabBurstHeight over what it would hit. Its blast reaches far past anything else here:
        // MoabPushRange caps the throw (uncapped it would sweep ~790 m), MoabInjuryRange the
        // injuries (they stop mattering at about 95 m anyway). So low over the ground it counts
        // as a surface burst (MoabSurfaceBurstHeight, scaled: m/kg^(1/3); FruitLib 5.10.0).
        public static float MoabBurstHeight = 2f;
        public static float MoabImpactAngle = 75f;
        public static float MoabImpactSpeed = 260f;
        public static float MoabDangerClose = 1000f;
        public static float MoabCarrierSpeed = 75f;
        public static float MoabReleaseRange = 1100f;
        public static float MoabChargeKgTNT = 11000f;
        public static float MoabSurfaceBurstHeight = 0.15f;
        public static float MoabDamageScale = 1.2f;
        public static float MoabPushRange = 300f;
        public static float MoabInjuryRange = 100f;
        public static int MoabFragments = 100000;      // calibrated to a ~150 m lethal radius, not a literal count
        public static int MoabWorldRays = 400;
        public static int MoabFragPower = 14000;
        public static float MoabFragBeltDeg = 40f;
        public static float MoabFragBeltShare = 0.7f;
        public static int MoabWalksPerLimb = 1;
        public static int MoabMaxWounds = 1000;
        // CBU-87/B: a SUU-65 dispenser falls toward the mark and opens CbuOpenHeight over it;
        // its CbuBomblets BLU-97/B bomblets are thrown out by its spin, slowed by their
        // inflatable decelerators, and come down over a CbuPatternWidth x CbuPatternLength
        // ellipse (across x along the run; the real one is 20 x 20 m to 120 x 240 m by height
        // and spin) within a second or two. A CbuDudRate share don't go off: they lie live and
        // go off when something moves them (CbuDudSensitivity m/s) or a round hits them.
        [FruitLib.MenuCategory("Fire Support")] public static float CbuDudRate = 0.05f;
        public static int CbuBomblets = 202;
        public static float CbuOpenHeight = 250f;
        public static float CbuCEP = 10f;
        public static float CbuPatternWidth = 60f;
        public static float CbuPatternLength = 80f;
        public static float CbuBombletSpeed = 35f;      // m/s, falling under its decelerator
        public static float CbuDangerClose = 300f;
        public static float CbuDudSensitivity = 2f;
        public static int CbuMaxDuds = 60;
        public static int CbuBurstsPerFrame = 8;
        public static int CbuFxEvery = 1;
        public static float Blu97ChargeKgTNT = 0.35f;   // 287 g cyclotol 70/30, less what the cone takes
        public static int Blu97Fragments = 300;
        public static int Blu97WorldRays = 30;
        public static int Blu97FragPower = 7000;        // ~2 g at ~1,000 m/s
        public static float Blu97DamageScale = 1.3f;
        public static int Blu97WalksPerLimb = 2;
        public static int Blu97MaxWounds = 120;
        public static float Blu97FragBeltDeg = 40f;
        public static float Blu97FragBeltShare = 0.8f;
        public static int Blu97JetRays = 6;
        public static float Blu97JetPenetration = 0.25f;   // m of wall for free (the Javelin's 0.8)
        public static int Blu97JetPower = 20000;
        public static int Blu97JetSpallCount = 25;

        // Rockets (5.15.0): an AH-64 (GUNFIGHTER) runs in low across your line of sight in a
        // shallow dive and fires Hydra 70 rockets in pairs, one from each M261 pod, from
        // RocketFireRange out, a pair every RocketPairInterval. Each flies a Mk 66 motor's burn
        // (1.07 s to 739 m/s), then coasts under drag and gravity; unguided, so they scatter by
        // RocketDispersion. HE (M151): 1 kg of Comp B-4 (~1.4 kg TNT) in a cast-iron body that
        // bursts where it hits, its fragments aimed at the bodies in reach in a side-spray belt
        // (as the bombs'); the pairs walk RocketWalk metres through the mark. Flechette
        // (M255A1): its fuze, set by range, throws FlechetteCount steel darts of 3.9 g forward
        // FlechetteBurstRange short of the mark, with a red marker pigment; every dart is a real
        // FruitLib round (FruitLib 5.9.0 lifted its cap on rounds in flight for them).
        [FruitLib.MenuCategory("Fire Support")] public static int HydraRockets = 8;
        [FruitLib.MenuCategory("Fire Support")] public static int FlechetteRockets = 2;
        public static float RocketRunSpeed = 55f;
        public static float RocketDiveAngle = 8f;
        public static float RocketFireRange = 1800f;
        public static float RocketPairInterval = 0.3f;
        public static float RocketDispersion = 3f;      // mils: 80 % of rockets land within this angle of their aim
        public static float RocketWalk = 30f;
        public static float RocketDangerClose = 150f;
        public static float HydraChargeKgTNT = 1.4f;    // 1.04 kg Comp B-4 x ~1.33
        public static int HydraFragments = 2500;
        public static int HydraWorldRays = 150;
        public static int HydraFragPower = 3000;
        public static float HydraDamageScale = 1.3f;
        public static int HydraWalksPerLimb = 2;
        public static int HydraMaxWounds = 300;
        public static float HydraFragBeltDeg = 40f;
        public static float HydraFragBeltShare = 0.7f;
        public static float FlechetteBurstRange = 150f;
        public static int FlechetteCount = 1179;
        public static float FlechettePatternRadius = 15f;   // m: the darts land evenly over this round where the rocket would have (5.15.1)
        public static float FlechetteConeDeg = 6f;      // half-angle of the free cone: only with FlechettePatternRadius 0, or a rocket that hits early
        public static float FlechettePowerScale = 1f;
        public static int FlechetteHitFxEvery = 3;

        // ── Detonation ────────────────────────────────────────────────────────────
        // Shooting a live charge sets it off; an explosion sets off charges its fragments reach
        // or its blast is strong enough at (Explosion/Explosion.Chain.cs).
        [FruitLib.MenuCategory("Detonation")] public static bool ShootToDetonate = true;
        [FruitLib.MenuCategory("Detonation")] public static bool ChainReactions = true;
        // Scales how far blast waves throw things (1 = physical, from the charge's TNT figure).
        [FruitLib.MenuCategory("Detonation")] public static float BlastPushScale = 1f;
        public static float SympatheticKPa = 2000f;
        public static float ChainFragmentSpeed = 150f;
        public static float ChainDelayMin = 0.03f;
        public static float ChainDelayMax = 0.12f;
        public static int ChainPerFrame = 1;
        public static float OrdnanceHitRadius = 0.12f;

        // ── Homing guidance ───────────────────────────────────────────────────────
        public static float MissileMinLaunchDist = 8f;
        public static float MissileSoftLaunchTime = 0.5f;
        public static float MissileSoftLaunchSpeed = 12f;
        [FruitLib.MenuCategory("Homing")] public static float MissileSpeed = 60f;
        [FruitLib.MenuCategory("Homing")] public static float MissileAscentHeight = 40f;
        [FruitLib.MenuCategory("Homing")] public static float MissileDirectAscentHeight = 15f;
        [FruitLib.MenuCategory("Homing")] public static float MissileSteerRate = 3f;
        [FruitLib.MenuCategory("Homing")] public static float MissileLockRange = 100f;
        [FruitLib.MenuCategory("Homing")] public static float MissileLockAngle = 10f;
        // Seeker: hold on a target this long to lock; the whole ragdoll or just the limb under the
        // reticle; lose the lock when it strays past LockBreakAngle from the view (or leaves range).
        [FruitLib.MenuCategory("Homing")] public static float LockTime = 3f;
        [FruitLib.MenuCategory("Homing")] public static bool LockWholeBody = true;
        [FruitLib.MenuCategory("Homing")] public static bool LockBreaks = true;
        public static float LockBreakAngle = 15f;
        public static float MissileDetonationRadius = 1.5f;
        public static float MissileLaunchAngle = 18f;
        public static float MissileFlightMotorTime = 5.2f;
        public static float MissileMass = 11.8f;
        public static float MissileDragCoeff = 0.3f;
        public static float MissileDiameter = 0.14f;
        public static float MissileNavGain = 3.5f;
        public static float MissileTrailOffsetZ = -0.15f;

        // ── AT-4 ──────────────────────────────────────────────────────────────────
        // Unguided: leaves the tube at RocketSpeed and only falls from there. It shares the
        // missile's warheads (Missile / Missile HE) and the HEAT / HE toggle.
        [FruitLib.MenuCategory("AT-4")] public static float RocketSpeed = 150f;
        [FruitLib.MenuCategory("AT-4")] public static float RocketReloadTime = 2f;
        public static float RocketGravity = 9.81f;
        public static float RocketArmDistance = 3f;
        // Metres out where the rocket meets the centre line and rides it from there (0: along the tube, dropping).
        public static float RocketConvergence = 100f;

        // ── Wounds ────────────────────────────────────────────────────────────────
        public static float StickyExplosionLift = 0.25f;
        public static float MissileExplosionLift = 0.3f;
        [FruitLib.MenuCategory("Wounds")] public static float WoundIntensity = 1f;
        public static int MaxWoundsPerExplosion = 240;

        // ── Throwing ──────────────────────────────────────────────────────────────
        // C4 / claymore tumble end over end in flight and roll into their landing pose.
        public static float ThrowTumbleRate = 540f;
        public static float ThrowTumbleVariance = 0.25f;
        // The grenade is physical: launch spin (deg/s), bounce, friction, how fast it stops rolling.
        public static float GrenadeTumbleRate = 600f;
        [FruitLib.MenuCategory("Grenade")] public static float GrenadeBounciness = 0.3f;
        public static float GrenadeFriction = 0.6f;
        public static float GrenadeRollDamping = 0.4f;

        // ── Held charges ──────────────────────────────────────────────────────────
        // First-person model while a grenade or C4 is selected. Pose in the camera's axes
        // (metres right/up/forward, degrees) relative to the game's item pivot.
        [FruitLib.MenuCategory("Effects")] public static bool ShowHeldModels = true;
        // Equipping as a spawn (prototype, SpawnTerminal.cs): a terminal beside the hand runs
        // SPAWN BA:<ITEM> while the item is fought into existence texel by texel.
        /// <summary>Prototype: equipping spawns the item into view from a terminal instead of raising it.</summary>
        [FruitLib.MenuCategory("Effects")] public static bool SpawnTerminal = true;
        public static float SpawnTime = 0.6f;          // seconds the item takes to fight its way into existence
        public static float DespawnTime = 0.35f;       // seconds a put-away item takes to dissolve into bytes
        public static float SpawnSettleLift = 0.012f;  // metres the item hovers over the hand while it spawns, before it drops in
        public static float SpawnSettleSway = 120f;    // degrees a second of sway the hand gives it as it settles
        public static float SpawnTermOffsetX = 0.13f;  // the spawn window's middle from the item pivot, metres in camera axes
        public static float SpawnTermOffsetY = 0.06f;
        public static float SpawnTermOffsetZ = 0f;
        // A launcher's HEAT / HE stencil: retyped at a DOS cursor, or (on) glitched into the other word.
        [FruitLib.MenuCategory("Effects")] public static bool WarheadLabelGlitch = false;
        // Every BombsAway sound (Effects/Sfx.cs); 0 is silent.
        [FruitLib.MenuCategory("Effects")] public static float SfxVolume = 1f;
        public static float HoldOffsetX = 0f;
        public static float HoldOffsetY = 0f;
        public static float HoldOffsetZ = 0f;
        public static float HoldPitch = -10f;
        public static float HoldYaw = 65f;
        public static float HoldRoll = 0f;
        // Throw: pull the pin, the spoon springs, a beat, then the body leaves the hand (seconds).
        public static float PinYankTime = 0.13f;
        public static float SpoonSnapTime = 0.05f;
        public static float LaunchDelay = 0.05f;
        public static float RearmDelay = 0.35f;
        public static float RaiseTime = 0.15f;
        public static float PartDebrisLifetime = 8f;
        public static float C4HoldOffsetX = 0f;
        public static float C4HoldOffsetY = -0.03f;
        public static float C4HoldOffsetZ = 0.08f;
        public static float C4HoldPitch = -40f;
        public static float C4HoldYaw = 15f;
        public static float C4HoldRoll = 0f;
        public static float MineHoldOffsetX = 0f;
        public static float MineHoldOffsetY = -0.04f;
        public static float MineHoldOffsetZ = 0.1f;
        public static float MineHoldPitch = 10f;
        public static float MineHoldYaw = -15f;
        public static float MineHoldRoll = 0f;
        // Claymore deploying: legs flip out, the sensor head pops up, the lenses light (seconds).
        public static float MineLegsTime = 0.14f;
        public static float MineHeadTime = 0.08f;
        public static float MineLensTime = 0.15f;
        // Javelin launcher: hip pose of the display centre, and the lift to the eye (right mouse).
        // Posed in Blender through the game camera (Assets/FRUKT/_Kit/fpview.py, pose "F").
        public static float JavelinHipOffsetX = 0.044f;
        public static float JavelinHipOffsetY = -0.052f;
        public static float JavelinHipOffsetZ = -0.029f;
        public static float JavelinHipPitch = 10f;
        public static float JavelinHipYaw = -18f;
        public static float JavelinHipRoll = 3f;
        // AT-4 on the shoulder (posed with Assets/FRUKT/_Kit/fpview.py): canted, but its tube's
        // axis aimed at the centre line RocketConvergence out. At the eye the rear aperture sits
        // AT4EyeRelief in front of it (the model is drawn by the viewmodel camera, whose near
        // plane is 1 cm, so the tube running past the cheek isn't cut). The spent tube is tossed AT4SpentDelay
        // after firing. (Keys renamed from AT4Hip*: the first pose was yawed across the view.)
        public static float AT4CarryOffsetX = 0.1024f;
        public static float AT4CarryOffsetY = 0.0923f;
        public static float AT4CarryOffsetZ = -0.2584f;
        public static float AT4CarryPitch = -0.095f;
        public static float AT4CarryYaw = -0.141f;
        public static float AT4CarryRoll = -10f;
        public static float AT4EyeRelief = 0.13f;
        public static float AT4SpentDelay = 0.4f;
        // Binoculars in the hand (posed with Assets/FRUKT/_Kit/fpview.py): low right, the screen
        // toward you. Brought up (on the viewmodel camera), the screen comes to the middle of the
        // view at BinoScreenFraction of its height, like the Javelin's CLU. (Keys renamed from
        // BinoHold*: the root moved from the eyecups to the screen's centre.)
        public static float BinoHipOffsetX = 0.0653f;
        public static float BinoHipOffsetY = 0.0198f;
        public static float BinoHipOffsetZ = -0.0779f;
        public static float BinoHipPitch = 12f;
        public static float BinoHipYaw = -14f;
        public static float BinoHipRoll = 8f;
        public static float BinoScreenFraction = 0.75f;
        // Making a fresh tube ready (Ordnance.AT4.cs): pin, cocking lever, safety; the backblast's shake.
        public static float AT4PinTime = 0.2f;
        public static float AT4CockTime = 0.3f;
        public static float AT4SafetyTime = 0.1f;
        public static float AT4ShakeTrauma = 0.3f;
        public static float AdsTime = 0.18f;
        public static float AdsScreenFraction = 0.7f;   // the display's share of the screen height at the eye
        public static float AdsBlurStart = 0.45f;       // metres: sharp up to here, fully soft by AdsBlurEnd
        public static float AdsBlurEnd = 1.2f;
        public static float CluZoomWide = 4f;           // CLU magnification, WFOV / NFOV
        public static float CluZoomNarrow = 12f;
        // C4 arming: flick the cover open, snap the switch, the antenna shoots out (seconds).
        public static float C4CoverTime = 0.09f;
        public static float C4SwitchTime = 0.05f;
        public static float C4AntennaTime = 0.12f;

        // ── Placement ─────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Placement")] public static bool PlacementEnabled = true;
        [FruitLib.MenuCategory("Placement")] public static float PlaceDistance = 2.5f;
        [FruitLib.MenuCategory("Placement")] public static bool ShowPlacementHologram = true;
        // No placing for this long after a throw, so the preview never lands on the round in flight.
        public static float PlaceCooldownAfterThrow = 0.4f;
        // Yaw about the surface normal: the game's own rotate mode (hold its rotate key, move the
        // mouse), and hold PlaceRotateKey + wheel for fixed steps. A small random yaw per placement.
        [FruitLib.MenuCategory("Placement")] public static bool UseGameRotationMode = true;
        public static float PlaceRotateSensitivity = 3f;
        public static float PlaceRotateStep = 15f;
        public static float PlaceYawJitter = 6f;
        // Added to where a stuck C4 sits by its own model (bottom face on the surface).
        public static float C4StickNudgeX = 0f;
        public static float C4StickNudgeY = 0f;
        public static float C4StickNudgeZ = 0f;
        // Added to where a stuck claymore stands by its own model (feet on the surface).
        public static float MineStickNudgeX = 0f;
        public static float MineStickNudgeY = 0f;
        public static float MineStickNudgeZ = 0f;

        // ── Effects ───────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Effects")] public static bool CamFXEnabled = true;
        [FruitLib.MenuCategory("Effects")] public static float CamFXIntensity = 1f;
        [FruitLib.MenuCategory("Effects")] public static float VFXIntensity = 1f;
        // A blast's shock front as a ring of bent light racing out from it (Shockwave).
        [FruitLib.MenuCategory("Effects")] public static bool Shockwave = true;
        [FruitLib.MenuCategory("Effects")] public static float ShockwaveStrength = 1f;
        public static float ShockwaveMinCharge = 0.5f;  // kg TNT: smaller blasts show none (a hand grenade's front is gone in two frames)
        public static float ShockwaveFadeKPa = 4f;      // the front fades out as its overpressure falls to this
        public static float ShockwaveGlint = 0.12f;     // the pale line along the front, so it shows against plain sky
        public static int ShockwaveMax = 16;            // fronts drawn at once (the oldest goes first)
        public static bool ShockwaveRefract = true;     // false: only the pale ring, never the opaque texture
        public static float DebrisRaysRatio = 0.04f;
        public static int DebrisMaxPerExplosion = 24;
        public static float DebrisMeshScale = 0.04f;
        public static float DebrisTrailTime = 0.6f;
        public static float DebrisLifetime = 4f;
        public static int ArcDebugSteps = 12;
        [FruitLib.MenuCategory("Effects")] public static bool AdaptiveQuality = true;
        public static float MinQualityScale = 0.25f;

        // ── Controls ──────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Controls")] public static KeyCode RemoteToggleKey = KeyCode.F1;
        [FruitLib.MenuCategory("Controls")] public static KeyCode AttackModeKey = KeyCode.F2;
        [FruitLib.MenuCategory("Controls")] public static KeyCode CluViewKey = KeyCode.N;
        [FruitLib.MenuCategory("Controls")] public static KeyCode WarheadModeKey = KeyCode.F3;
        [FruitLib.MenuCategory("Controls")] public static KeyCode LockModeKey = KeyCode.F4;
        [FruitLib.MenuCategory("Controls")] public static KeyCode ReleaseLockKey = KeyCode.B;
        [FruitLib.MenuCategory("Controls")] public static KeyCode PlaceRotateKey = KeyCode.R;
        [FruitLib.MenuCategory("Controls")] public static KeyCode MissionPrevKey = KeyCode.Q;
        [FruitLib.MenuCategory("Controls")] public static KeyCode MissionNextKey = KeyCode.E;

        // ── Debug ─────────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Debug")] public static int DebugLevel = 0;
        // Draws every fragment leg and blast line of BombsAway detonations (see Explosion.DebugDraw.cs).
        [FruitLib.MenuCategory("Debug")] public static bool DebugDrawExplosions = false;
        public static float DebugDrawSeconds = 10f;
        public static int DebugDrawMaxLines = 3000;
        public static int DebugDrawSpentEvery = 8;
        public static bool DebugDrawBlast = true;
        // Live lines and labels on every body covering its face (Flashbang.Diagnostics.cs).
        [FruitLib.MenuCategory("Debug")] public static bool DebugDrawFlashArms = false;
        // An air strike's plan, drawn until it's over: the reach lines tried from the mark (green
        // reach it, red don't), the chosen axis (yellow) and the aircraft's whole flight (cyan).
        // DebugLevel 1 also logs each strike's plan ([Air] MSN nn ... plan:).
        [FruitLib.MenuCategory("Debug")] public static bool DebugDrawAirPlan = false;
        public static int FragLayerMask  = ~0;
        public static int WorldLayerMask = ~0;
        // The debug keys below do nothing unless this is on.
        [FruitLib.MenuCategory("Debug")] public static bool DebugHotkeys = false;
        // Test bench: a row of walls to shoot at (Debug/TestBench.cs). Shift+key clears it.
        [FruitLib.MenuCategory("Debug")] public static KeyCode TestBenchKey = KeyCode.Keypad1;
        // Logs the game's item, ragdoll and camera sizes (Debug/ScaleProbe.cs); hold a native gun for its hip pose.
        [FruitLib.MenuCategory("Debug")] public static KeyCode ScaleProbeKey = KeyCode.F9;
        public static int TestBenchWalls = 10;
        public static float TestBenchThickness = 0.1f;
        public static float TestBenchGap = 0.1f;
        public static float TestBenchDistance = 3f;
        public static string TestBenchMaterial = "Concrete";

        // ── Helpers (not shown in menu) ───────────────────────────────────────────
        public static float CamFX(float baseVal) =>
            baseVal * Mathf.Clamp(CamFXIntensity, 0f, 5f);
        public static float VFX(float baseVal) =>
            baseVal * Mathf.Clamp(VFXIntensity, 0f, 5f);
        public static int VFXInt(int baseVal) =>
            Mathf.RoundToInt(baseVal * Mathf.Clamp(VFXIntensity, 0f, 5f));

        public static bool CamFXActive => CamFXEnabled && CamFXIntensity > 0f;
        public static bool VFXActive => VFXIntensity > 0f;


        public static bool Dbg1 => DebugLevel >= 1;
        public static bool Dbg2 => DebugLevel >= 2;

        private static Shader _cachedLitShader;
        private static bool _litShaderResolved;
        public static Shader FindShader()
        {
            if (!_litShaderResolved)
            {
                _cachedLitShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _litShaderResolved = true;
            }
            return _cachedLitShader;
        }

        private static Shader _cachedSpriteShader;
        private static bool _spriteShaderResolved;
        public static Shader FindSpriteShader()
        {
            if (!_spriteShaderResolved)
            {
                _cachedSpriteShader = Shader.Find("Sprites/Default");
                _spriteShaderResolved = true;
            }
            return _cachedSpriteShader;
        }
    }
}
