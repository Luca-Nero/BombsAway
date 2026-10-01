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
        // The cloud (SmokeCloud.cs): smoke spreads over the ground like a heavy gas, in columns
        // SmokeCellSize across, keeping a front about SmokeFrontDepth deep, and drifts with the wind.
        /// <summary>The breeze, metres per second (0 = still air). Its heading wanders slowly.</summary>
        [FruitLib.MenuCategory("Smoke")] public static float WindSpeed = 0.4f;
        public static float WindHeading = -1f;         // degrees from +Z the wind blows toward; -1 = random per scene
        public static float SmokeVolume = 4.5f;        // m^3 of smoke a can puts out per second (x SmokeDensity)
        public static float SmokeSpread = 0.35f;       // how readily deep smoke flows into shallower ground, per second
        public static float SmokeFrontDepth = 1f;      // metres: thinner than this, smoke stops spreading (wind still moves it)
        public static float SmokeFadeBurning = 0.01f;  // share of the smoke lost per second while the can burns
        public static float SmokeClearRate = 0.06f;    // the same once it is out: the cloud clears over ~25 s
        public static float SmokeCellSize = 1.25f;
        public static int SmokeMaxCells = 350;         // ground columns per cloud

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
        public static float MineSightOriginX = 0f;
        public static float MineSightOriginY = 0.85f;
        public static float MineSightOriginZ = 0f;
        public static float MineSightSpacing = 0.185f;

        // ── Effects ───────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Effects")] public static bool CamFXEnabled = true;
        [FruitLib.MenuCategory("Effects")] public static float CamFXIntensity = 1f;
        [FruitLib.MenuCategory("Effects")] public static float VFXIntensity = 1f;
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
        public static int FragLayerMask  = ~0;
        public static int WorldLayerMask = ~0;
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
