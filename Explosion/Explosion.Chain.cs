using System.Collections.Generic;
using FruitLib;
using MelonLoader;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // Shot and sympathetic detonation
    // ══════════════════════════════════════════════════════════════════════════════
    //
    // Any live BombsAway charge - thrown, stuck, placed, a missile in flight, or a cluster
    // bomb's dud lying live (AirStrike.Cluster.cs) - goes off when a FruitLib round passes
    // through it (ShootToDetonate), and when another explosion's blast is strong enough there
    // or one of its fragments hits it (ChainReactions). That explosion can set off the next,
    // and so on.
    //
    // Hits are tested against where each charge is, not against its collider: a charge stuck
    // to a moving body has its collider off, and a missile has none.
    //
    // Fragment hits are a probability, not a test against the traced rays. Those are a sample
    // (a couple of thousand over a whole sphere, ~25 cm apart at 3 m), so whether one happened
    // to pass within a charge's width was luck, and chains died halfway. Instead: the explosion's
    // real fragment count spread over its cone, times the solid angle the charge covers from
    // there, gives the expected number of hits; the chance of at least one is 1 - e^-hits.
    //
    // A detonation found here is queued a few hundredths of a second out rather than run on
    // the spot - it is found from inside a round's flight or another detonation - and no more
    // than ChainPerFrame go off in one frame, since each one traces thousands of fragments.
    public partial class Core
    {
        private const float MissileHitRadius = 0.15f;

        private static readonly List<(object charge, float at)> _pending = new List<(object, float)>();
        private static bool _shootHooked, _chainHooked;

        /// <summary>A scene reload drops every charge, so anything still queued to go off goes too.</summary>
        internal static void ClearPending() => _pending.Clear();

        /// <summary>Hooks and unhooks FruitLib's events as the two settings change, then sets
        /// off whatever is due. Called every frame.</summary>
        private static void TickChain()
        {
            bool shoot = Config.ShootToDetonate, chain = Config.ChainReactions;
            if (shoot != _shootHooked)
            {
                if (shoot) FruitBallistics.ProjectileStep += OnRoundStep;
                else       FruitBallistics.ProjectileStep -= OnRoundStep;
                _shootHooked = shoot;
            }
            if (chain != _chainHooked)
            {
                if (chain) FruitBallistics.Exploded += OnExplodedNearby;
                else       FruitBallistics.Exploded -= OnExplodedNearby;
                _chainHooked = chain;
            }

            if (_pending.Count == 0) return;
            float now = Time.time;
            int budget = Mathf.Max(1, Config.ChainPerFrame);
            while (budget > 0)
            {
                // The one most overdue goes first; the rest wait for a later frame.
                int due = -1;
                for (int i = 0; i < _pending.Count; i++)
                    if (_pending[i].at <= now && (due < 0 || _pending[i].at < _pending[due].at)) due = i;
                if (due < 0) break;

                var charge = _pending[due].charge;
                _pending.RemoveAt(due);
                try
                {
                    // A charge the game destroyed since it was queued is skipped; its own tick retires it.
                    if (charge is GrenadeState g && !g.Dead && g.Obj != null) { Explode(g); budget--; }
                    else if (charge is HomingMissileState m && !m.Dead && m.Obj != null) { ExplodeMissile(m); budget--; }
                    else if (charge is AirStrike.Dud d) AirStrike.SetOff(d);   // with the cluster's bursts, which keep their own pace
                }
                catch (System.Exception e) { MelonLogger.Warning($"[Chain] detonation failed: {e.Message}"); }
            }
        }

        /// <summary>Sets <paramref name="charge"/> off shortly, once.</summary>
        private static void Trigger(object charge, string why)
        {
            foreach (var p in _pending) if (ReferenceEquals(p.charge, charge)) return;
            float delay = Random.Range(Mathf.Max(0f, Config.ChainDelayMin), Mathf.Max(Config.ChainDelayMin, Config.ChainDelayMax));
            _pending.Add((charge, Time.time + delay));
            if (Config.Dbg1) MelonLogger.Msg($"[Chain] {(charge is GrenadeState g ? g.Params.Kind : charge is AirStrike.Dud ? "BLU-97 dud" : "Missile")} set off by {why}");
        }

        // ── Shot ─────────────────────────────────────────────────────────────────

        /// <summary>Each round, each frame, before it moves: does this frame's path pass through
        /// a charge before it meets anything solid?</summary>
        private static void OnRoundStep(Projectile r, float dt)
        {
            if (r.Cosmetic || (_grenades.Count == 0 && _missiles.Count == 0)) return;
            Vector3 a = r.Position;
            Vector3 d = r.Velocity * dt;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 dir = d / len;

            object best = null;
            float bestT = float.MaxValue;
            foreach (var g in _grenades)
                if (!g.Dead && g.Obj != null && Along(a, dir, len, g.Obj.transform.position, Config.OrdnanceHitRadius, out float t) && t < bestT)
                { best = g; bestT = t; }
            foreach (var m in _missiles)
                if (!m.Dead && m.Obj != null && Along(a, dir, len, m.Obj.transform.position, MissileHitRadius, out float t) && t < bestT)
                { best = m; bestT = t; }
            if (best == null) return;

            // A wall in front of it takes the round first; the next frame tries again from beyond
            // it if the round got through.
            if (Blocked(a, dir, bestT)) return;

            Trigger(best, $"a round ({r.Spec?.Id})");
            r.Kill();   // it went into the charge
        }

        // ── Chain ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every live charge in sight of an explosion: set off for certain where the blast is past
        /// SympatheticKPa (for charges without a TNT figure, within a quarter of the blast
        /// radius), else by chance, if its fragments could reach it fast enough.
        /// </summary>
        private static void OnExplodedNearby(ExplosionInfo x)
        {
            if (x.Cosmetic || x.Spec == null || (_grenades.Count == 0 && _missiles.Count == 0 && AirStrike.LiveDuds.Count == 0)) return;
            var s = x.Spec;
            float reach = s.ChargeKgTNT > 0f
                ? FruitBlast.RangeFor(s.ChargeKgTNT * (x.HasGround && x.Ground.distance < 0.4f ? s.SurfaceBurstFactor : 1f),
                                      Mathf.Max(100f, Config.SympatheticKPa))
                : s.BlastRadius * 0.25f;

            foreach (var g in _grenades)
                if (!g.Dead && g.Obj != null) Consider(x, g, g.Obj.transform.position, Config.OrdnanceHitRadius, reach);
            foreach (var m in _missiles)
                if (!m.Dead && m.Obj != null) Consider(x, m, m.Obj.transform.position, MissileHitRadius, reach);
            foreach (var d in AirStrike.LiveDuds)
                if (AirStrike.DudAt(d, out Vector3 at)) Consider(x, d, at, Config.OrdnanceHitRadius, reach);
        }

        private static void Consider(ExplosionInfo x, object charge, Vector3 at, float radius, float reach)
        {
            // The draw first, the line of sight only for one that would go: a cluster's hundreds
            // of bursts each look at every dud.
            float dist = Vector3.Distance(x.Origin, at);
            bool blast = dist <= reach;
            float chance = blast ? 1f : FragmentHitChance(x, at, dist, radius);
            if (chance <= 0f || (!blast && Random.value >= chance)) return;
            if (Blocked(x.Origin, at)) return;
            Trigger(charge, blast ? $"the blast of {x.Spec.Id}" : $"a fragment of {x.Spec.Id} ({chance:P0} chance)");
        }

        /// <summary>
        /// Chance at least one of the explosion's fragments hits a charge of this radius at this
        /// distance: expected hits = fragments per steradian of its cone x the solid angle the
        /// charge covers (πr² / d²), zero outside the cone or where fragments have slowed below
        /// ChainFragmentSpeed.
        /// </summary>
        private static float FragmentHitChance(ExplosionInfo x, Vector3 at, float dist, float radius)
        {
            var s = x.Spec;
            // The real fragment count: a kind with targeted fragments has its rays for the scenery only.
            int frags = s.FragTargeted > 0 ? s.FragTargeted : s.FragCount;
            if (frags <= 0 || s.FragPower <= 0 || dist < 1e-3f) return 0f;

            // Fragments slow as their power falls, exp(-falloff·d), from the speed their power
            // and mass give them at the charge (7.5 power per joule, as FruitLib uses).
            float m = Mathf.Max(0.00005f, s.FragMassGrams * 0.001f);
            float v0 = Mathf.Sqrt(2f * s.FragPower / (7.5f * m));
            if (v0 * Mathf.Exp(-0.5f * s.FragPowerFalloff * dist) < Config.ChainFragmentSpeed) return 0f;

            float steradians = 4f * Mathf.PI;
            if (s.HSpreadDeg < 360f || s.VSpreadDeg < 360f)
            {
                float half = Mathf.Clamp((s.HSpreadDeg + s.VSpreadDeg) * 0.25f, 1f, 180f) * Mathf.Deg2Rad;
                Vector3 fwd = x.Forward.sqrMagnitude > 0f ? x.Forward.normalized : Vector3.up;
                if (Vector3.Angle(fwd, at - x.Origin) * Mathf.Deg2Rad > half) return 0f;
                steradians = 2f * Mathf.PI * (1f - Mathf.Cos(half));
            }

            float hits = frags / steradians * (Mathf.PI * radius * radius) / (dist * dist);
            return 1f - Mathf.Exp(-hits);
        }

        // ── Geometry ─────────────────────────────────────────────────────────────

        /// <summary>Does the segment from <paramref name="a"/> along <paramref name="dir"/> for
        /// <paramref name="len"/> pass within <paramref name="radius"/> of <paramref name="p"/>?
        /// <paramref name="t"/> is how far along.</summary>
        private static bool Along(Vector3 a, Vector3 dir, float len, Vector3 p, float radius, out float t)
        {
            t = Mathf.Clamp(Vector3.Dot(p - a, dir), 0f, len);
            return (a + dir * t - p).sqrMagnitude <= radius * radius;
        }

        /// <summary>Charge to charge. Both ends a little off whatever they lie on, so two charges
        /// on the same floor aren't hidden from each other by the floor's own bumps.</summary>
        private static bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 lift = Vector3.up * 0.1f;
            from += lift; to += lift;
            Vector3 d = to - from;
            float len = d.magnitude;
            return len > 1e-4f && Blocked(from, d / len, len);
        }

        /// <summary>Anything solid on the way: not bodies, not small loose things, and not other
        /// charges - a placed charge is kinematic and keeps its collider, and used to hide every
        /// charge behind it from the blast.</summary>
        private static bool Blocked(Vector3 from, Vector3 dir, float len)
        {
            // Stop short of the charge itself, whose own collider may still be on.
            len -= Config.OrdnanceHitRadius;
            if (len <= 0.01f) return false;
            foreach (var h in Physics.RaycastAll(from, dir, len, ~(1 << 2), QueryTriggerInteraction.Ignore))
            {
                var c = h.collider;
                if (c == null || ExplosionSystem.IsLimb(c.gameObject) || IsCharge(c)) continue;
                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic && c.bounds.size.sqrMagnitude < 0.5f) continue;   // small loose stuff
                return true;
            }
            return false;
        }

        private static bool IsCharge(Collider c)
        {
            var t = c.transform;
            foreach (var g in _grenades)
                if (g.Obj != null && (t == g.Obj.transform || t.IsChildOf(g.Obj.transform))) return true;
            foreach (var m in _missiles)
                if (m.Obj != null && (t == m.Obj.transform || t.IsChildOf(m.Obj.transform))) return true;
            return false;
        }
    }
}
