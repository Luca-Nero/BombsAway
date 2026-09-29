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

            if (g.Stuck && g.HostRb != null)
            {
                g.Obj.transform.position = g.HostRb.transform.TransformPoint(g.LocalOffset);
                g.Obj.transform.rotation = g.HostRb.transform.rotation * g.LocalRotation;
            }

            if (g.Stuck && g.SightLines != null)
                UpdateSightLines(g);

            // ── Detonation check (mode-specific) ─────────────────────
            bool detonate = false;

            switch (ep.Detonation)
            {
                case DetonationMode.Timer:
                    if (g.Timer >= ep.FuseTime - ep.FlashTime && g.Obj != null)
                    {
                        g.FlashAccum += dt;
                        if (g.FlashAccum >= Config.FlashRate)
                        {
                            g.FlashAccum = 0f;
                            g.FlashToggle = !g.FlashToggle;
                            if (g.GrenadeRenderer != null && g.BaseColors != null)
                            {
                                float brightness = g.FlashToggle
                                    ? 1.6f : 0.3f;
                                var mats = g.GrenadeRenderer.materials;
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
                    detonate = g.RemoteTriggered;
                    break;

                case DetonationMode.Proximity:
                    if (g.Armed && g.Obj != null)
                    {
                        g.ProxScanAccum += dt;
                        if (g.ProxScanAccum >= ep.ProximityInterval)
                        {
                            g.ProxScanAccum = 0f;
                            if (ProximityScan(g)) {
                                detonate = true;
                            }
                                
                        }
                    }
                    break;

                case DetonationMode.Impact:
                    if (g.Armed && g.Obj != null)
                    {
                        Vector3 fwd = g.Obj.transform.forward;
                        float castDist = ep.ImpactCastRange;
                        if (Physics.SphereCast(g.Obj.transform.position, ep.ImpactCastRadius,
                            fwd, out RaycastHit impactHit, castDist,
                            Config.WorldLayerMask, QueryTriggerInteraction.Ignore))
                        {
                            if (impactHit.collider.gameObject != g.Obj)
                            {
                                g.Obj.transform.position = impactHit.point;
                                detonate = true;
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
            DetonationMode.Remote    => new Vector3(Config.C4LocalOffsetX, Config.C4LocalOffsetY, Config.C4LocalOffsetZ),
            DetonationMode.Proximity => new Vector3(Config.MineLocalOffsetX, Config.MineLocalOffsetY, Config.MineLocalOffsetZ),
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
                g.LocalOffset = hostRb.transform.InverseTransformPoint(pos);
                g.LocalRotation = Quaternion.Inverse(hostRb.transform.rotation) * rot;

                var ownCollider = g.Obj.GetComponent<Collider>();
                if (ownCollider != null) ownCollider.enabled = false;
            }
        }

        private static void Explode(GrenadeState g)
        {
            Vector3 origin = g.Obj != null ? g.Obj.transform.position : Vector3.zero;
            if (g.Obj != null)
                g.Params.Forward = g.Obj.transform.forward;
            if (g.Stuck && g.Obj != null)
                origin += g.Obj.transform.up * Config.StickyExplosionLift;

            if (g.Obj != null)
            {
                var col = g.Obj.GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }
            Physics.SyncTransforms();
            if (g.Obj != null) GameObject.Destroy(g.Obj);
            g.Obj = null;
            g.Dead = true;
            g.Params.Origin = origin;
            ExplosionSystem.Detonate(g.Params);
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

        private static void CreateSightLines(GrenadeState g)
        {
            if (g.Obj == null) return;

            var mat = new Material(Config.FindSpriteShader());
            mat.color = Color.red;
            mat.SetInt("_ZTest", 0);

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
            if (g.SightLines == null || g.Obj == null) return;

            Transform t = g.Obj.transform;
            Vector3 centre = new Vector3(
                Config.MineSightOriginX,
                Config.MineSightOriginY,
                Config.MineSightOriginZ);
            float sp = Config.MineSightSpacing;
            float len = Config.MineProximityRange;
            float halfSpread = 30f;

            Vector3[] origins = {
                centre + new Vector3(-sp, 0f, 0f),
                centre,
                centre + new Vector3( sp, 0f, 0f),
            };
            float[] angles = { -halfSpread, 0f, halfSpread };

            for (int i = 0; i < 3; i++)
            {
                Vector3 worldOrigin = t.TransformPoint(origins[i]);
                Vector3 localDir = Quaternion.AngleAxis(angles[i], Vector3.up)
                                   * Vector3.forward;
                Vector3 worldDir = t.TransformDirection(localDir);

                g.SightLines[i].SetPosition(0, worldOrigin);
                g.SightLines[i].SetPosition(1, worldOrigin + worldDir * len);
            }
        }
    }
}
