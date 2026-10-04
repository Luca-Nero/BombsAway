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
                SpecFor(ExplosionParams.FromGun30Config(Vector3.zero));
                SpecFor(ExplosionParams.FromGun20Config(Vector3.zero));
                SpecFor(ExplosionParams.FromJdamConfig("Jdam500", Vector3.zero));
                SpecFor(ExplosionParams.FromJdamConfig("Jdam1000", Vector3.zero));
                SpecFor(ExplosionParams.FromJdamConfig("Jdam2000", Vector3.zero));
                SpecFor(ExplosionParams.FromHydraConfig(Vector3.zero));
                SpecFor(ExplosionParams.FromMoabConfig(Vector3.zero));
                SpecFor(ExplosionParams.FromBlu97Config(Vector3.zero));
            }
            catch (System.Exception e) { MelonLogger.Warning($"[Explosion] pre-registering specs failed: {e.Message}"); }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Detonate — main entry point
        // ══════════════════════════════════════════════════════════════════════
        public static void Detonate(ExplosionParams p)
        {
            var spec = SpecFor(p);
            FruitBallistics.SpawnExplosion(spec.Id, p.Origin, p.Forward, p.Axis, 0, ExplosionFeatures.All);
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
            s.BlastPushScale = Config.BlastPushScale * Mathf.Max(0f, p.PushScale);
            s.MaxPushRange   = p.MaxPushRange;
            s.MaxInjuryRange = p.MaxInjuryRange;
            s.FragPowerFalloff = p.FragPowerFalloff;
            s.FragTargeted     = p.FragTargeted;
            s.MaxWalksPerLimb  = p.MaxWalksPerLimb;
            s.FragBeltDeg      = p.FragBeltDeg;
            s.FragBeltShare    = p.FragBeltShare;
            s.FragPenetrationScale = p.FragPenetrationScale;
            s.SurfaceBurstScaledHeight = p.SurfaceBurstScaledHeight;
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
            // 0 = unlimited, as the setting says. A kind may bring its own (the bombs).
            int wounds = p.MaxWounds > 0 ? p.MaxWounds : Config.MaxWoundsPerExplosion;
            s.MaxWounds   = wounds > 0 ? wounds : 1000000;
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
                case "Gun30":     return 2f;     // a 30 mm HEI body breaking up
                case "Gun20":     return 1f;     // a 20 mm one
                case "Jdam500":   return 6f;     // a bomb's thick cast case: heavy pieces that carry far
                case "Jdam1000":  return 8f;
                case "Jdam2000":  return 10f;
                case "Hydra":     return 1.5f;   // the M151's cast-iron body
                case "Moab":      return 12f;    // chunks of its aluminium case (penetrating as aluminium: FragPenetrationScale)
                case "Blu97":     return 2f;     // the scored steel case's ~30 grain pieces
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
                // And shows its shock front, whoever's it is.
                Shockwave.Blast(x);
            }

            if (!Ours(x.Spec)) return;

            // The bundle's effect for this kind (ExplosionFx).
            string kind = x.Spec.Id.Substring(SpecPrefix.Length);
            // A gun run's hits come at 65 to 100 a second: each only nudges the camera.
            bool gunHit = kind == "Gun30" || kind == "Gun20";
            // A bomb's shake arrives with its blast wave, from much farther off (AirStrike).
            if (kind.StartsWith("Jdam") || kind == "Moab") AirStrike.Shake(x.Origin, kind);
            else if (Config.CamFXEnabled) CameraFX.AddTrauma(x.Origin, kind == "Gun30" ? 0.08f : kind == "Gun20" ? 0.05f : kind == "Blu97" ? 0.12f : 1f);
            // A gun run may draw only every Nth hit's effect, a cluster bomb every Nth bomblet's (AirStrike decides which).
            if (gunHit && !AirStrike.TakeHitFx(kind)) return;
            if (kind == "Blu97" && !AirStrike.TakeBombletFx()) return;
            // The 81 mm mortar has no effect of its own: it borrows the HE warhead's.
            ExplosionFx.Play(kind == "Mortar81" ? "MissileHE" : kind, x.Origin, x.Forward, x.HasGround, x.Ground);

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
