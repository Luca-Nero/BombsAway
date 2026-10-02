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

            // Register every kind up front, so FruitLib knows charges are coming and can look
            // up ragdolls' organs ahead of the first blast rather than during it.
            try
            {
                SpecFor(ExplosionParams.FromGrenadeConfig(Vector3.zero));
                SpecFor(ExplosionParams.FromC4Config(Vector3.zero));
                SpecFor(ExplosionParams.FromClaymoreConfig(Vector3.zero));
                SpecFor(ExplosionParams.FromMissileConfig(Vector3.zero));
                SpecFor(ExplosionParams.FromMissileHEConfig(Vector3.zero));
                SpecFor(ExplosionParams.FromMissileTBXConfig(Vector3.zero));
                SpecFor(ExplosionParams.FromArtilleryConfig(Vector3.zero));
                SpecFor(ExplosionParams.FromMortarConfig(Vector3.zero));
            }
            catch (System.Exception e) { MelonLogger.Warning($"[Explosion] pre-registering specs failed: {e.Message}"); }
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

            s.Features = p.Features;
            s.BlastDiffraction = p.BlastDiffraction;
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
            s.ChargeKgTNT = p.ChargeKgTNT;
            s.BlastPushScale = Config.BlastPushScale;
            s.ArcSteps    = p.ArcSteps;
            // What the fragments are: with FragPower this sets their real speed, and how well
            // they go through walls (FruitLib 5.4).
            s.FragMassGrams = FragmentGrams(p.Kind);
            s.JetRays        = p.JetRays;
            s.JetConeDeg     = p.JetConeDeg;
            s.JetPenetration = p.JetPenetration;
            s.JetPower       = p.JetPower;
            s.JetSpallCount  = p.JetSpallCount;

            // WoundIntensity used to scale the old cone; it now scales every wound.
            s.DamageScale = p.DamageScale * Mathf.Max(0f, Config.WoundIntensity);
            // 0 = unlimited, as the setting says.
            s.MaxWounds   = Config.MaxWoundsPerExplosion > 0 ? Config.MaxWoundsPerExplosion : 1000000;
            s.SecondaryMaxWounds =s.MaxWounds >= 1000000 ? 1000000 : Mathf.Max(1, s.MaxWounds / 2);

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

        /// <summary>
        /// One fragment's mass per explosive. A grenade's notched liner breaks into small, very
        /// fast pieces (~1250 m/s at its power); a claymore throws 0.7 g steel balls (~1200 m/s,
        /// as the M18A1's); C4 has no casing, so its "fragments" are heavier, slower debris; the
        /// warheads' casings break up in between.
        /// </summary>
        private static float FragmentGrams(string kind)
        {
            switch (kind)
            {
                case "Grenade":   return 0.5f;
                case "Claymore":  return 0.7f;
                case "C4":        return 4f;
                case "MissileHE": return 1.5f;
                case "Arty155":   return 3f;     // a thick forged body: heavier, slower pieces
                case "Mortar81":  return 1.2f;   // a thin cast-iron body: small, fast pieces
                default:          return 2f;
            }
        }

        // ── Visuals: BombsAway's own explosives only ─────────────────────────

        private static bool Ours(ExplosionSpec s) => s?.Id != null && s.Id.StartsWith(SpecPrefix);

        private static void OnExploded(ExplosionInfo x)
        {
            // Any explosion pushes smoke, whoever's it is.
            if (x.Spec != null)
            {
                try { SmokeCloud.Blast(x.Origin, x.Forward, x.Spec.ChargeKgTNT, x.Spec.BlastRadius, x.Spec.HSpreadDeg < 150f); }
                catch (System.Exception e) { MelonLogger.Warning($"[Smoke] blast failed: {e.Message}"); }
            }

            if (!Ours(x.Spec)) return;

            if (Config.CamFXEnabled) CameraFX.AddTrauma(x.Origin);

            // The bundle's effect for this kind (ExplosionFx), or the old code-built one.
            string kind = x.Spec.Id.Substring(SpecPrefix.Length);
            // A kind without its own effect yet borrows the nearest one (the shells: the HE warhead's).
            if (!ExplosionFx.Play(kind, x.Origin, x.Forward, x.HasGround, x.Ground)
                && !(BorrowsFx(kind, out string stand) && ExplosionFx.Play(stand, x.Origin, x.Forward, x.HasGround, x.Ground)))
            {
                if (x.HasGround) ExplosionVFX.Spawn(x.Origin, x.Ground);
                else             ExplosionVFX.SpawnAerial(x.Origin);
            }

            if (Config.Dbg1)
                MelonLogger.Msg($"Detonate {x.Spec.Id} at {x.Origin} | cone={x.Spec.HSpreadDeg:F0}x{x.Spec.VSpreadDeg:F0}° " +
                                $"fwd={x.Forward}{(x.Cosmetic ? " | cosmetic" : "")}");
        }

        /// <summary>The effect a kind borrows while it has none of its own in the bundle.</summary>
        private static bool BorrowsFx(string kind, out string stand)
        {
            stand = kind == "Arty155" || kind == "Mortar81" || kind == "MissileTBX" ? "MissileHE" : null;
            return stand != null;
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
