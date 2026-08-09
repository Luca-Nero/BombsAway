using UnityEngine;

namespace BombsAway
{
    internal static class Config
    {
        // ── Controls ──────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Controls")] public static KeyCode GrenadeKey = KeyCode.G;
        [FruitLib.MenuCategory("Controls")] public static KeyCode C4Key = KeyCode.H;
        [FruitLib.MenuCategory("Controls")] public static KeyCode MineKey = KeyCode.J;
        [FruitLib.MenuCategory("Controls")] public static KeyCode EnableLockOn = KeyCode.V;
        [FruitLib.MenuCategory("Controls")] public static KeyCode LockOnTarget = KeyCode.Q;
        [FruitLib.MenuCategory("Controls")] public static KeyCode SpawnHomingMissile = KeyCode.E;
        [FruitLib.MenuCategory("Controls")] public static KeyCode DetonateKey = KeyCode.F;
        [FruitLib.MenuCategory("Controls")] public static KeyCode RemoteToggleKey = KeyCode.F1;
        [FruitLib.MenuCategory("Controls")] public static KeyCode AttackModeKey = KeyCode.F2;
        [FruitLib.MenuCategory("Controls")] public static KeyCode WarheadModeKey = KeyCode.F3;
        [FruitLib.MenuCategory("Controls")] public static KeyCode LockModeKey = KeyCode.F4;
        [FruitLib.MenuCategory("Controls")] public static KeyCode ReleaseLockKey = KeyCode.B;

        // ── Grenade ───────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Grenade")] public static float Fuse = 2f;
        [FruitLib.MenuCategory("Grenade")] public static float FlashRate = 0.2f;
        [FruitLib.MenuCategory("Grenade")] public static float ThrowForce = 8f;
        [FruitLib.MenuCategory("Grenade")] public static float ThrowArc = -15f;
        [FruitLib.MenuCategory("Grenade")] public static float BlastRadius = 5f;
        [FruitLib.MenuCategory("Grenade")] public static float BlastForce = 1f;
        [FruitLib.MenuCategory("Grenade")] public static float BlastUpward = 1f;
        [FruitLib.MenuCategory("Grenade")] public static float OverpressureRadius = 3.5f;
        [FruitLib.MenuCategory("Grenade")] public static float OverpressureFalloffExp = 1;
        [FruitLib.MenuCategory("Grenade")] public static int OverpressureWoundPoints = 12;
        [FruitLib.MenuCategory("Grenade")] public static int FragRayCount = 2000;
        [FruitLib.MenuCategory("Grenade")] public static float FragSpeed = 15f;
        [FruitLib.MenuCategory("Grenade")] public static float FragMaxTime = 4f;
        [FruitLib.MenuCategory("Grenade")] public static float FragImpulse = 0.4f;
        [FruitLib.MenuCategory("Grenade")] public static float DamageScale = 1f;

        // ── C4 ────────────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("C4")] public static float C4ThrowForce = 8f;
        [FruitLib.MenuCategory("C4")] public static float C4ThrowArc = -15f;
        [FruitLib.MenuCategory("C4")] public static float C4BlastRadius = 5f;
        [FruitLib.MenuCategory("C4")] public static float C4BlastForce = 1.5f;
        [FruitLib.MenuCategory("C4")] public static float C4BlastUpward = 1f;
        [FruitLib.MenuCategory("C4")] public static float C4OverpressureRadius = 5f;
        [FruitLib.MenuCategory("C4")] public static float C4OverpressureFalloffExp = 1;
        [FruitLib.MenuCategory("C4")] public static int C4OverpressureWoundPoints = 18;
        [FruitLib.MenuCategory("C4")] public static int C4FragRayCount = 2000;
        [FruitLib.MenuCategory("C4")] public static float C4FragSpeed = 20f;
        [FruitLib.MenuCategory("C4")] public static float C4FragMaxTime = 2f;
        [FruitLib.MenuCategory("C4")] public static float C4FragImpulse = 0.2f;
        [FruitLib.MenuCategory("C4")] public static float C4DamageScale = 1.5f;

        // ── Claymore ──────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Claymore")] public static float MineThrowForce = 8f;
        [FruitLib.MenuCategory("Claymore")] public static float MineThrowArc = -15f;
        [FruitLib.MenuCategory("Claymore")] public static float MineProximityRange = 10f;
        [FruitLib.MenuCategory("Claymore")] public static float MineBlastRadius = 6f;
        [FruitLib.MenuCategory("Claymore")] public static float MineBlastForce = 1f;
        [FruitLib.MenuCategory("Claymore")] public static float MineBlastUpward = 0f;
        [FruitLib.MenuCategory("Claymore")] public static float MineOverpressureRadius = 3.5f;
        [FruitLib.MenuCategory("Claymore")] public static float MineOverpressureFalloffExp = 1;
        [FruitLib.MenuCategory("Claymore")] public static int MineOverpressureWoundPoints = 12;
        [FruitLib.MenuCategory("Claymore")] public static int MineFragRayCount = 1000;
        [FruitLib.MenuCategory("Claymore")] public static float MineFragSpeed = 45f;
        [FruitLib.MenuCategory("Claymore")] public static float MineFragMaxTime = 2f;
        [FruitLib.MenuCategory("Claymore")] public static float MineFragImpulse = 0.2f;
        [FruitLib.MenuCategory("Claymore")] public static float MineDamageScale = 1.2f;

        // ── Missile warhead ───────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Missile")] public static float MissileBlastRadius = 3f;
        [FruitLib.MenuCategory("Missile")] public static float MissileBlastForce = 1f;
        [FruitLib.MenuCategory("Missile")] public static float MissileBlastUpward = 0f;
        [FruitLib.MenuCategory("Missile")] public static float MissileOverpressureRadius = 3f;
        [FruitLib.MenuCategory("Missile")] public static float MissileOverpressureFalloffExp = 1;
        [FruitLib.MenuCategory("Missile")] public static int MissileOverpressureWoundPoints = 12;
        [FruitLib.MenuCategory("Missile")] public static int MissileFragRayCount = 1000;
        [FruitLib.MenuCategory("Missile")] public static float MissileFragSpeed = 30f;
        [FruitLib.MenuCategory("Missile")] public static float MissileFragMaxTime = 2f;
        [FruitLib.MenuCategory("Missile")] public static float MissileFragImpulse = 0.4f;
        [FruitLib.MenuCategory("Missile")] public static float MissileDamageScale = 2f;

        // ── Missile HE warhead ────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Missile HE")] public static float MissileHEBlastRadius = 6f;
        [FruitLib.MenuCategory("Missile HE")] public static float MissileHEBlastForce = 4f;
        [FruitLib.MenuCategory("Missile HE")] public static float MissileHEBlastUpward = 1f;
        [FruitLib.MenuCategory("Missile HE")] public static float MissileHEOverpressureRadius = 12f;
        [FruitLib.MenuCategory("Missile HE")] public static float MissileHEOverpressureFalloffExp = 1f;
        [FruitLib.MenuCategory("Missile HE")] public static int MissileHEOverpressureWoundPoints = 24;
        [FruitLib.MenuCategory("Missile HE")] public static int MissileHEFragRayCount = 2000;
        [FruitLib.MenuCategory("Missile HE")] public static float MissileHEFragSpeed = 30f;
        [FruitLib.MenuCategory("Missile HE")] public static float MissileHEFragMaxTime = 4f;
        [FruitLib.MenuCategory("Missile HE")] public static float MissileHEFragImpulse = 0.2f;
        [FruitLib.MenuCategory("Missile HE")] public static float MissileHEDamageScale = 1.25f;

        // ── Homing guidance ───────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Homing")] public static float MissileMinLaunchDist = 8f;
        [FruitLib.MenuCategory("Homing")] public static float MissileSoftLaunchTime = 0.5f;
        [FruitLib.MenuCategory("Homing")] public static float MissileSoftLaunchSpeed = 12f;
        [FruitLib.MenuCategory("Homing")] public static float MissileSpeed = 60f;
        [FruitLib.MenuCategory("Homing")] public static float MissileAscentHeight = 40f;
        [FruitLib.MenuCategory("Homing")] public static float MissileDirectAscentHeight = 15f;
        [FruitLib.MenuCategory("Homing")] public static float MissileSteerRate = 3f;
        [FruitLib.MenuCategory("Homing")] public static float MissileLockRange = 100f;
        [FruitLib.MenuCategory("Homing")] public static float MissileLockAngle = 10f;
        [FruitLib.MenuCategory("Homing")] public static float MissileDetonationRadius = 1.5f;
        [FruitLib.MenuCategory("Homing")] public static float MissileLaunchAngle = 18f;
        [FruitLib.MenuCategory("Homing")] public static float MissileFlightMotorTime = 5.2f;
        [FruitLib.MenuCategory("Homing")] public static float MissileMass = 11.8f;
        [FruitLib.MenuCategory("Homing")] public static float MissileDragCoeff = 0.3f;
        [FruitLib.MenuCategory("Homing")] public static float MissileDiameter = 0.14f;
        [FruitLib.MenuCategory("Homing")] public static float MissileNavGain = 3.5f;
        [FruitLib.MenuCategory("Homing")] public static float MissileTrailOffsetZ = -0.15f;

        // ── Wounds ────────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Wounds")] public static float StickyExplosionLift = 0.25f;
        [FruitLib.MenuCategory("Wounds")] public static float MissileExplosionLift = 0.3f;
        [FruitLib.MenuCategory("Wounds")] public static float WoundIntensity = 1f;
        [FruitLib.MenuCategory("Wounds")] public static int MaxWoundsPerExplosion = 240;
        [FruitLib.MenuCategory("Wounds")] public static float FragWoundDelay = 0.15f;

        // ── Effects ───────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Effects")] public static bool CamFXEnabled = true;
        [FruitLib.MenuCategory("Effects")] public static float CamFXIntensity = 1f;
        [FruitLib.MenuCategory("Effects")] public static float VFXIntensity = 1f;
        [FruitLib.MenuCategory("Effects")] public static float DebrisRaysRatio = 0.04f;
        [FruitLib.MenuCategory("Effects")] public static int DebrisMaxPerExplosion = 24;
        [FruitLib.MenuCategory("Effects")] public static float DebrisMeshScale = 0.04f;
        [FruitLib.MenuCategory("Effects")] public static float DebrisTrailTime = 0.6f;
        [FruitLib.MenuCategory("Effects")] public static float DebrisLifetime = 4f;
        [FruitLib.MenuCategory("Effects")] public static int ArcDebugSteps = 12;
        [FruitLib.MenuCategory("Effects")] public static bool AdaptiveQuality = true;
        [FruitLib.MenuCategory("Effects")] public static float MinQualityScale = 0.25f;

        // ── Placement ─────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Placement")] public static float C4LocalOffsetX = 0f;
        [FruitLib.MenuCategory("Placement")] public static float C4LocalOffsetY = 0.05f;
        [FruitLib.MenuCategory("Placement")] public static float C4LocalOffsetZ = 0f;
        [FruitLib.MenuCategory("Placement")] public static float MineLocalOffsetX = 0f;
        [FruitLib.MenuCategory("Placement")] public static float MineLocalOffsetY = 0.08f;
        [FruitLib.MenuCategory("Placement")] public static float MineLocalOffsetZ = 0f;
        [FruitLib.MenuCategory("Placement")] public static float MineSightOriginX = 0f;
        [FruitLib.MenuCategory("Placement")] public static float MineSightOriginY = 0.85f;
        [FruitLib.MenuCategory("Placement")] public static float MineSightOriginZ = 0f;
        [FruitLib.MenuCategory("Placement")] public static float MineSightSpacing = 0.185f;

        // ── Debug ─────────────────────────────────────────────────────────────────
        [FruitLib.MenuCategory("Debug")] public static int DebugLevel = 0;
        [FruitLib.MenuCategory("Debug")] public static bool DebugDrawRays = false;
        [FruitLib.MenuCategory("Debug")] public static float DebugRayDuration = 2f;
        [FruitLib.MenuCategory("Debug")] public static int FragLayerMask  = ~0;
        [FruitLib.MenuCategory("Debug")] public static int WorldLayerMask = ~0;

        // ── Helpers (not shown in menu) ───────────────────────────────────────────
        public static float CamFX(float baseVal) =>
            baseVal * Mathf.Clamp(CamFXIntensity, 0f, 5f);
        public static float VFX(float baseVal) =>
            baseVal * Mathf.Clamp(VFXIntensity, 0f, 5f);
        public static int VFXInt(int baseVal) =>
            Mathf.RoundToInt(baseVal * Mathf.Clamp(VFXIntensity, 0f, 5f));

        public static bool CamFXActive => CamFXEnabled && CamFXIntensity > 0f;
        public static bool VFXActive => VFXIntensity > 0f;

        public static float WChunkStep => 0.5f * WoundIntensity;
        public static int WConeRadius(int step) => Mathf.RoundToInt(
            (step == 0 ? 2f : step == 1 ? 1f : 0f) * WoundIntensity);
        public static float WConeSignal(int step) =>
            (step == 0 ? 1.0f : step == 1 ? 0.6f : 0.3f) * WoundIntensity;
        public static float WPenetrationScale => 0.25f * WoundIntensity;

        public static bool Dbg1 => DebugLevel >= 1;
        public static bool Dbg2 => DebugLevel >= 2;
        public static float QualityScale => AdaptiveQuality
            ? Mathf.Lerp(1f, Mathf.Clamp01(MinQualityScale), FruitLib.FruitPerfMon.PressureLevel)
            : 1f;

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
