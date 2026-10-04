using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    public partial class Core
    {
        private static void TickGrenade(GrenadeState g, float dt)
        {
            // Destroyed by the game (a delete tool, say): nothing left to tick or to go off.
            if (g.Obj == null)
            {
                g.Dead = true;
                ReleaseMaterials(g.Owned);
                return;
            }

            var ep = g.Params;
            g.Timer += dt;

            // Sticky ordnance flies a scripted arc and sticks on its first contact
            // (Ordnance.Flight.cs); a grenade is left entirely to physics.
            if (g.Ballistic && !g.Stuck)
            {
                TickScriptedFlight(g, dt);
                if (g.Dead) return;
            }

            // ── Arming ────────────────────────────────────────────────
            if (!g.Armed && g.Timer >= ep.ArmDelay)
            {
                g.Armed = true;
                if (Config.Dbg1 && ep.Detonation != DetonationMode.Timer)
                    MelonLogger.Msg($"[Ordnance] Armed ({ep.Detonation}) at {g.Timer:F2}s");
            }

            if (g.Stuck && g.HasHost)
            {
                if (g.HostRb == null) Unstick(g);   // the body it was stuck to is gone
                else
                {
                    g.Obj.transform.position = g.HostRb.transform.TransformPoint(g.LocalOffset);
                    g.Obj.transform.rotation = g.HostRb.transform.rotation * g.LocalRotation;
                }
            }

            if (g.SightLines != null)
                UpdateSightLines(g);

            // ── Detonation check (mode-specific) ─────────────────────
            bool detonate = false;

            switch (ep.Detonation)
            {
                case DetonationMode.Timer:
                    // A smoke grenade lights instead of going off, and burns on (Ordnance.Smoke.cs).
                    if (ep.Kind == "Smoke")
                    {
                        if (g.SmokeUntil < 0f && g.Timer >= ep.FuseTime) LightSmoke(g);
                        if (g.SmokeUntil >= 0f) TickSmoke(g);
                        break;
                    }
                    if (ep.FlashTime > 0f && g.Timer >= ep.FuseTime - ep.FlashTime && g.Obj != null)
                    {
                        g.FlashAccum += dt;
                        if (g.FlashAccum >= Config.FlashRate)
                        {
                            g.FlashAccum = 0f;
                            g.FlashToggle = !g.FlashToggle;
                            if (g.FlashMats != null && g.BaseColors != null)
                            {
                                float brightness = g.FlashToggle
                                    ? 1.6f : 0.3f;
                                var mats = g.FlashMats;
                                for (int m = 0; m < mats.Length && m < g.BaseColors.Length; m++)
                                {
                                    if (mats[m] == null) continue;
                                    var bc = g.BaseColors[m];
                                    mats[m].color = new Color(
                                        Mathf.Clamp01(bc.r * brightness),
                                        Mathf.Clamp01(bc.g * brightness),
                                        Mathf.Clamp01(bc.b * brightness),
                                        bc.a);
                                }
                            }
                        }
                    }
                    if (g.Timer >= ep.FuseTime)
                        detonate = true;
                    break;

                case DetonationMode.Remote:
                    if (g.Blink != null) g.Blink.enabled = (g.Timer % 1f) < 0.12f;
                    detonate = g.RemoteTriggered;
                    break;

                case DetonationMode.Proximity:
                    if (g.Armed && g.Obj != null)
                    {
                        // Tripped: the lenses flicker for MineTripDelay, then it goes.
                        if (g.TripAt >= 0f)
                        {
                            g.Clay?.Lit(((int)((Time.time - g.TripAt) / 0.04f) & 1) == 0);
                            detonate = Time.time - g.TripAt >= Config.MineTripDelay;
                            break;
                        }
                        g.ProxScanAccum += dt;
                        if (g.ProxScanAccum >= ep.ProximityInterval)
                        {
                            g.ProxScanAccum = 0f;
                            bool tripped = g.Clay != null && Config.MineTripwire ? LaserTripped(g) : ProximityScan(g);
                            if (tripped)
                            {
                                Sfx.Play("ClayTrip", g.Obj.transform.position);
                                if (Config.MineTripDelay > 0f) g.TripAt = Time.time;
                                else detonate = true;
                            }
                        }
                    }
                    break;
            }

            if (detonate)
                Explode(g);
        }

        // ── Sticking: shared by a thrown charge landing and a charge placed by hand ──

        /// <summary>Up along the surface normal, forward along <paramref name="fwdHint"/>
        /// flattened onto the surface - so a claymore faces the way it was aimed.</summary>
        internal static Quaternion SurfaceRotation(Vector3 normal, Vector3 fwdHint)
        {
            Vector3 surfaceFwd = fwdHint - Vector3.Dot(fwdHint, normal) * normal;
            if (surfaceFwd.sqrMagnitude < 0.001f)
            {
                surfaceFwd = Vector3.Cross(normal, Vector3.forward);
                if (surfaceFwd.sqrMagnitude < 0.001f)
                    surfaceFwd = Vector3.Cross(normal, Vector3.up);
            }
            return Quaternion.LookRotation(surfaceFwd.normalized, normal);
        }

        /// <summary>Local offset of a stuck charge from the hit point (the Placement config).</summary>
        internal static Vector3 StickOffset(DetonationMode mode) => mode switch
        {
            DetonationMode.Remote    => new Vector3(0f, OrdnanceModels.RestHeight(Ordnance.C4, 0.05f), 0f)
                                      + new Vector3(Config.C4StickNudgeX, Config.C4StickNudgeY, Config.C4StickNudgeZ),
            DetonationMode.Proximity => new Vector3(0f, OrdnanceModels.RestHeight(Ordnance.Claymore, 0.08f), 0f)
                                      + new Vector3(Config.MineStickNudgeX, Config.MineStickNudgeY, Config.MineStickNudgeZ),
            _ => Vector3.zero,
        };

        /// <summary>Pins a charge at a final pose on <paramref name="host"/>. Anything with a
        /// rigidbody carries it along - a limb, a crate, a limb the puppeteer drives
        /// kinematically; the static world just holds it.</summary>
        private static void StickAt(GrenadeState g, Vector3 pos, Quaternion rot, Collider host)
        {
            var rb = g.Rb;
            bool onLimb = ExplosionSystem.IsLimb(host.gameObject);
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = !onLimb;
                rb.mass = 0.01f;
            }
            g.Stuck = true;
            g.Obj.transform.SetPositionAndRotation(pos, rot);
            g.Params.Forward = g.Obj.transform.forward;

            if (g.Params.Detonation == DetonationMode.Proximity)
                CreateSightLines(g);

            var hostRb = host.attachedRigidbody;
            if (hostRb != null)
            {
                g.HostRb = hostRb;
                g.HasHost = true;
                g.LocalOffset = hostRb.transform.InverseTransformPoint(pos);
                g.LocalRotation = Quaternion.Inverse(hostRb.transform.rotation) * rot;

                var ownCollider = g.Obj.GetComponent<Collider>();
                if (ownCollider != null) ownCollider.enabled = false;
            }
        }

        /// <summary>The body a stuck charge rode on was destroyed: undo StickAt and let it drop.
        /// A sticky charge falls the scripted way from where it hung, so it sticks again to
        /// whatever it lands on; anything else drops as a plain physical body.</summary>
        private static void Unstick(GrenadeState g)
        {
            g.Stuck = false;
            g.HasHost = false;
            g.HostRb = null;

            var col = g.Obj.GetComponent<Collider>();
            if (col != null) col.enabled = true;

            if (g.Params.Sticky && g.Rb != null)
            {
                g.Rb.isKinematic = true;
                g.Ballistic = true;
                g.Velocity = Vector3.zero;
                g.FlightTime = 0f;
                g.ThrowDir = g.Obj.transform.forward;   // lands facing the way it hung
                g.CastRadius = FlightRadius(g.Obj);
                g.SpinRate = 0f;
                g.HasLanding = false;
                return;
            }

            if (g.Rb != null)
            {
                g.Rb.isKinematic = false;
                g.Rb.linearVelocity = Vector3.zero;
                g.Rb.angularVelocity = Vector3.zero;
            }
        }

        private static void Explode(GrenadeState g)
        {
            // Smoke never goes off as a blast: shot or chained, it only lights.
            if (g.Params.Kind == "Smoke") { LightSmoke(g); return; }

            // Nothing to go off if the game already destroyed it: there is no origin to blow up at.
            if (g.Obj == null)
            {
                g.Dead = true;
                ReleaseMaterials(g.Owned);
                return;
            }

            Vector3 origin = g.Obj.transform.position;
            g.Params.Forward = g.Obj.transform.forward;
            if (g.Stuck)
                origin += g.Obj.transform.up * Config.StickyExplosionLift;
            // C4 throws everything off the surface it sits on, and its blast is round anyway: its
            // forward is that surface's normal, which the effect aims its burst along (ExplosionFx).
            if (g.Params.Kind == "C4")
                g.Params.Forward = g.Obj.transform.up;

            var col = g.Obj.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            Physics.SyncTransforms();
            GameObject.Destroy(g.Obj);
            ReleaseMaterials(g.Owned);
            g.Obj = null;
            g.Dead = true;
            g.Params.Origin = origin;
            ExplosionSystem.Detonate(g.Params);
            if (g.Params.Kind == "Flash") Flashbang.Bang(origin);
        }

        /// <summary>
        /// A claymore's lasers as tripwires: true if a body is the first thing along any of the
        /// three beams, out to MineProximityRange (a wall before it blocks that beam, as it
        /// stops the beam you see).
        /// </summary>
        private static bool LaserTripped(GrenadeState g)
        {
            Transform t = g.Obj.transform;
            float range = Config.MineProximityRange;
            for (int i = 0; i < 3; i++)
            {
                g.Clay.Laser(i, t, out Vector3 o, out Vector3 d);
                // The allocating overload: NonAlloc returns nothing in this build.
                var hits = Physics.RaycastAll(o, d, range, Config.FragLayerMask | Config.WorldLayerMask, QueryTriggerInteraction.Ignore);
                Collider first = null;
                float best = float.MaxValue;
                foreach (var h in hits)
                {
                    if (h.collider == null || h.collider.transform.IsChildOf(t)) continue;
                    if (h.distance < best) { best = h.distance; first = h.collider; }
                }
                if (first == null || !ExplosionSystem.IsLimb(first.gameObject)) continue;
                if (Config.Dbg1) MelonLogger.Msg($"[Claymore] Laser {i} broken by '{first.gameObject.name}' at {best:F1} m");
                return true;
            }
            return false;
        }

        private static bool ProximityScan(GrenadeState g)
        {
            if (g.Obj == null) return false;
            var ep = g.Params;
            Vector3 pos = g.Obj.transform.position;
            Vector3 fwd = ep.Forward.normalized;
            bool fullSphere = ep.ProximityHSpreadDeg >= 360f && ep.ProximityVSpreadDeg >= 360f;

            Quaternion lookInv = Quaternion.identity;
            float tanH = 0f, tanV = 0f;
            if (!fullSphere)
            {
                fwd = g.Obj.transform.forward;   // live mesh facing
                lookInv = Quaternion.Inverse(Quaternion.LookRotation(fwd));
                float hHalfRad = Mathf.Min(ep.ProximityHSpreadDeg * 0.5f, 180f) * Mathf.Deg2Rad;
                float vHalfRad = Mathf.Min(ep.ProximityVSpreadDeg * 0.5f, 180f) * Mathf.Deg2Rad;
                tanH = Mathf.Tan(Mathf.Min(hHalfRad, 1.5f));
                tanV = Mathf.Tan(Mathf.Min(vHalfRad, 1.5f));
            }

            var overlaps = ExplosionSystem.OverlapSphereShared(pos, ep.ProximityRadius,
                Config.FragLayerMask, QueryTriggerInteraction.Ignore, out int count);
            for (int i = 0; i < count; i++)
            {
                var col = overlaps[i];
                if (col == null) continue;
                if (!ExplosionSystem.IsLimb(col.gameObject)) continue;

                if (col.transform.IsChildOf(g.Obj.transform)) continue;

                if (!fullSphere)
                {
                    Vector3 toTarget = (col.transform.position - pos).normalized;
                    Vector3 local = lookInv * toTarget;
                    float azimuth = Mathf.Atan2(local.y, local.x);
                    float halfAngle = ExplosionSystem.EllipticalHalfAngle(azimuth, tanH, tanV);
                    if (Vector3.Dot(toTarget, fwd) < Mathf.Cos(halfAngle)) continue;
                }

                if (Config.Dbg2)
                    MelonLogger.Msg($"[Prox] Target detected: '{col.gameObject.name}' " +
                        $"dist={Vector3.Distance(pos, col.transform.position):F2}");
                return true;
            }
            return false;
        }

        // ── Claymore sight lines ────────────────────────────────────────────────

        /// <summary>The three lasers, from the bundled model's lenses; none without its rig.</summary>
        private static void CreateSightLines(GrenadeState g)
        {
            if (g.Obj == null || g.Clay == null) return;

            var mat = new Material(Config.FindSpriteShader());
            mat.color = Color.red;
            mat.SetInt("_ZTest", 0);
            g.Owned.Add(mat);

            g.SightLines = new LineRenderer[3];

            for (int i = 0; i < 3; i++)
            {
                var lineObj = new GameObject($"SightLine_{i}");
                lineObj.transform.SetParent(g.Obj.transform, false);

                var lr = lineObj.AddComponent<LineRenderer>();
                lr.material = mat;
                lr.startWidth = 0.0075f;
                lr.endWidth = 0.0075f;
                lr.startColor = Color.red;
                lr.endColor = new Color(1f, 0f, 0f, 0.3f);
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                g.SightLines[i] = lr;
            }

            UpdateSightLines(g);
        }

        private static void UpdateSightLines(GrenadeState g)
        {
            if (g.SightLines == null || g.Obj == null || g.Clay == null) return;

            // From the lenses, and only as far as the first thing in the way.
            Transform t = g.Obj.transform;
            float range = Config.MineProximityRange;
            for (int i = 0; i < 3; i++)
            {
                g.Clay.Laser(i, t, out Vector3 o, out Vector3 d);
                float reach = range;
                if (Physics.Raycast(o, d, out RaycastHit hit, range, Config.WorldLayerMask, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(t))
                    reach = hit.distance;
                g.SightLines[i].SetPosition(0, o);
                g.SightLines[i].SetPosition(1, o + d * reach);
            }
        }
    }
}
