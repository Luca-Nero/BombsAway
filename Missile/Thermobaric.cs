using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The launchers' thermobaric (TBX) warhead, in its three stages:
    ///
    /// Dispersal, at impact: a small burster spreads the fuel as a fast tan-grey cloud
    /// (FX_TBXCloud, if the bundle has it). Nothing is hurt.
    ///
    /// Ignition, MissileTBXIgniteDelay later: the cloud goes off round its middle, a little back
    /// from the impact (MissileTBXCloudLift). A FruitLib explosion with no fragments
    /// (ExplosionParams.FromMissileTBXConfig): a bigger charge than the HE warhead's, and a wave
    /// that fills rooms and spills round cover (MissileTBXDiffraction).
    ///
    /// The rush back, MissileTBXSuctionDelay after that: the fireball burnt the air's oxygen and
    /// pushed the air out, and it comes back in, pulling loose things and bodies toward the
    /// middle at up to MissileTBXSuction m/s, falling off to nothing at MissileTBXSuctionRadius.
    /// A wall in between leaves a little of it.
    /// </summary>
    internal static class Thermobaric
    {
        private sealed class Cloud
        {
            public ExplosionParams P;
            public Vector3 At;
            public float IgniteAt, RushAt;
            public bool Ignited;
        }

        private static readonly List<Cloud> _clouds = new List<Cloud>();
        private static readonly HashSet<System.IntPtr> _pulled = new HashSet<System.IntPtr>();

        /// <summary>The warhead hit: its fuel spreads, to go off in a moment.</summary>
        public static void Disperse(ExplosionParams p)
        {
            Vector3 fwd = p.Forward.sqrMagnitude > 1e-4f ? p.Forward.normalized : Vector3.down;
            Vector3 at = p.Origin - fwd * Mathf.Max(0f, Config.MissileTBXCloudLift);
            // Not out through the far side of whatever it came off.
            if (Physics.Linecast(p.Origin, at, out RaycastHit back, Config.WorldLayerMask, QueryTriggerInteraction.Ignore))
                at = back.point + back.normal * 0.2f;

            try
            {
                bool hasGround = Physics.Raycast(p.Origin, Vector3.down, out RaycastHit ground, 200f, Config.WorldLayerMask, QueryTriggerInteraction.Ignore);
                ExplosionFx.Play("TBXCloud", p.Origin, fwd, hasGround, ground);
            }
            catch (System.Exception e) { MelonLogger.Warning($"[TBX] dispersal effect failed: {e.Message}"); }
            // The burster only pops: a small shake, coming with its sound and gone with distance.
            CameraFX.Blast(p.Origin, p.ChargeKgTNT, 0.015f);

            float now = Time.time;
            _clouds.Add(new Cloud { P = p, At = at, IgniteAt = now + Mathf.Max(0f, Config.MissileTBXIgniteDelay), RushAt = -1f });
            if (Config.Dbg1) MelonLogger.Msg($"[TBX] fuel dispersed at {p.Origin}, igniting at {at} in {Config.MissileTBXIgniteDelay:F2} s");
        }

        public static void Tick()
        {
            if (_clouds.Count == 0) return;
            float now = Time.time;
            for (int i = _clouds.Count - 1; i >= 0; i--)
            {
                var c = _clouds[i];
                try
                {
                    if (!c.Ignited && now >= c.IgniteAt)
                    {
                        c.Ignited = true;
                        c.P.Origin = c.At;
                        ExplosionSystem.Detonate(c.P);
                        c.RushAt = now + Mathf.Max(0f, Config.MissileTBXSuctionDelay);
                    }
                    if (c.Ignited && now >= c.RushAt)
                    {
                        Rush(c.At);
                        _clouds.RemoveAt(i);
                    }
                }
                catch (System.Exception e)
                {
                    MelonLogger.Warning($"[TBX] cloud failed: {e.Message}");
                    _clouds.RemoveAt(i);
                }
            }
        }

        /// <summary>The air comes back in: everything loose within reach pulled toward <paramref name="at"/>.</summary>
        private static void Rush(Vector3 at)
        {
            float pull = Config.MissileTBXSuction;
            float reach = Mathf.Max(1f, Config.MissileTBXSuctionRadius);
            if (pull <= 0f) return;
            _pulled.Clear();
            int n = 0;
            var cols = ExplosionSystem.OverlapSphereShared(at, reach, Config.FragLayerMask, QueryTriggerInteraction.Ignore, out int count);
            for (int i = 0; i < count; i++)
            {
                var col = cols[i];
                var rb = col != null ? col.attachedRigidbody : null;
                if (rb == null || rb.isKinematic || !_pulled.Add(rb.Pointer)) continue;
                Vector3 to = at - rb.worldCenterOfMass;
                float d = to.magnitude;
                if (d < 0.4f) continue;   // in the middle: nowhere to be pulled
                float f = 1f - d / reach;
                if (f <= 0f) continue;
                f *= f;
                if (Physics.Linecast(at, rb.worldCenterOfMass, Config.WorldLayerMask, QueryTriggerInteraction.Ignore)) f *= 0.25f;
                // Toward the middle, a little up with it: the hot core rises as the air comes in.
                Vector3 dir = (to / d + Vector3.up * 0.15f).normalized;
                rb.linearVelocity += dir * (pull * f);
                n++;
            }
            if (Config.Dbg1) MelonLogger.Msg($"[TBX] the air rushed back: {n} bodies and props pulled");
        }

        public static void Clear() => _clouds.Clear();
    }
}
