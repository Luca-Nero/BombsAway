using System.Collections.Generic;
using FruitLib;
using Il2CppEffectors;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // ExplosionSystem
    // ══════════════════════════════════════════════════════════════════════════════
    //
    // The physics of a detonation - shockwave, overpressure, fragments, wounds - is
    // FruitLib's now (FruitBallistics.SpawnExplosion), where fragments wound through the
    // game's own bullet wound model. What stays here is what makes it a BombsAway
    // explosion: the fireball, smoke and debris, and the camera shake, all hung off
    // FruitLib's events and drawn only for BombsAway's own explosives.
    //
    // Each kind of explosive (grenade, C4, claymore, the two missile warheads) is one
    // registered spec, "BombsAway." + Kind, refreshed from the explosive's own params at
    // the moment it goes off - so the menu's live values still apply, as before.
    internal static class ExplosionSystem
    {
        private const string SpecPrefix = "BombsAway.";

        private static readonly Il2CppSystem.Type LimbReceiverType = Il2CppType.Of<LimbEffectorReceiver>();
        private static readonly Dictionary<string, ExplosionSpec> _specs = new Dictionary<string, ExplosionSpec>();
        private static bool _hooked;

        public static void Init()
        {
            if (_hooked) return;
            _hooked = true;
            FruitBallistics.Exploded  += OnExploded;
            FruitBallistics.DebrisArc += OnDebris;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Detonate — main entry point
        // ══════════════════════════════════════════════════════════════════════
        public static void Detonate(ExplosionParams p)
        {
            var spec = SpecFor(p);
            FruitBallistics.SpawnExplosion(spec.Id, p.Origin, p.Forward);
        }

        /// <summary>The FruitLib spec for this kind of explosive, brought in line with
        /// <paramref name="p"/>. The same object is reused per kind and re-registered.</summary>
        private static ExplosionSpec SpecFor(ExplosionParams p)
        {
            string id = SpecPrefix + (string.IsNullOrEmpty(p.Kind) ? "Explosive" : p.Kind);
            if (!_specs.TryGetValue(id, out var s))
            {
                s = new ExplosionSpec { Id = id };
                _specs[id] = s;
            }

            s.HSpreadDeg = p.HSpreadDeg;
            s.VSpreadDeg = p.VSpreadDeg;

            s.BlastRadius = p.BlastRadius;
            s.BlastForce  = p.BlastForce;
            s.BlastUpward = p.BlastUpward;

            s.OverpressureRadius     = p.OverpressureRadius;
            s.OverpressureFalloffExp = p.OverpressureFalloffExp;
            s.OverpressurePoints     = p.OverpressureWoundPoints;

            s.FragCount   = p.FragRayCount;
            s.FragSpeed   = p.FragSpeed;
            s.FragMaxTime = p.FragMaxTime;
            s.FragImpulse = p.FragImpulse;
            s.FragPower   = p.FragPower;
            s.ArcSteps    = p.ArcSteps;

            // WoundIntensity used to scale the old cone; it now scales every wound.
            s.DamageScale = p.DamageScale * Mathf.Max(0f, Config.WoundIntensity);
            s.MaxWounds   = Mathf.Max(1, Config.MaxWoundsPerExplosion);

            s.AdaptiveQuality = Config.AdaptiveQuality;
            s.MinQuality      = Config.MinQualityScale;
            s.DebrisRatio     = Config.VFXActive ? p.DebrisRaysRatio : 0f;
            s.MaxDebris       = Config.DebrisMaxPerExplosion;

            // Never the ejecta chunks FruitLib throws, whatever the mask says.
            s.LayerMask = Config.FragLayerMask & ~(1 << 2);

            // Fragments get the same bone treatment as bullets: real steel goes through bone.
            s.FragWound.HardTissueScale = 0.1f;

            FruitBallistics.Register(s);
            return s;
        }

        // ── Visuals: BombsAway's own explosives only ─────────────────────────

        private static bool Ours(ExplosionSpec s) => s?.Id != null && s.Id.StartsWith(SpecPrefix);

        private static void OnExploded(ExplosionInfo x)
        {
            if (!Ours(x.Spec)) return;

            if (Config.CamFXEnabled) CameraFX.AddTrauma(x.Origin);

            if (x.HasGround) ExplosionVFX.Spawn(x.Origin, x.Ground);
            else             ExplosionVFX.SpawnAerial(x.Origin);

            if (Config.Dbg1)
                MelonLogger.Msg($"Detonate {x.Spec.Id} at {x.Origin} | cone={x.Spec.HSpreadDeg:F0}x{x.Spec.VSpreadDeg:F0}° " +
                                $"fwd={x.Forward}{(x.Cosmetic ? " | cosmetic" : "")}");
        }

        private static void OnDebris(ExplosionSpec s, Vector3 p0, Vector3 vel, float flightTime)
        {
            if (!Ours(s)) return;
            ExplosionVFX.SpawnDebrisArc(p0, vel, Physics.gravity.y, flightTime);
        }

        // ── Helpers the ordnance code still uses ─────────────────────────────

        internal static Collider[] OverlapSphereShared(Vector3 pos, float radius, int mask,
            QueryTriggerInteraction q, out int count)
        {
            // The allocating overload on purpose: NonAlloc returns nothing in this build.
            var result = Physics.OverlapSphere(pos, radius, mask, q);
            count = result.Length;
            return result;
        }

        internal static float BallisticGroundTime(float gy, float vy, float dy, float maxTime)
        {
            float a = 0.5f * gy, b = vy, c = dy;
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return maxTime;
            float sq = Mathf.Sqrt(disc);
            float t1 = (-b + sq) / (2f * a);
            float t2 = (-b - sq) / (2f * a);
            float tHit = -1f;
            if (t1 > 0.001f && t2 > 0.001f) tHit = Mathf.Min(t1, t2);
            else if (t1 > 0.001f) tHit = t1;
            else if (t2 > 0.001f) tHit = t2;
            return tHit > 0f ? Mathf.Min(tHit, maxTime) : maxTime;
        }

        internal static float EllipticalHalfAngle(float azimuth, float tanH, float tanV)
        {
            float cosAz = Mathf.Cos(azimuth);
            float sinAz = Mathf.Sin(azimuth);
            return Mathf.Atan2(1f,
                Mathf.Sqrt((cosAz * cosAz) / (tanH * tanH) + (sinAz * sinAz) / (tanV * tanV)));
        }

        internal static bool IsLimb(GameObject obj)
        {
            if (obj == null) return false;
            var comp = obj.GetComponentInParent(LimbReceiverType);
            return comp != null && comp.TryCast<LimbEffectorReceiver>() != null;
        }
    }
}
