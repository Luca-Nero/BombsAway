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

            if (ep.Sticky && !g.Stuck && g.Obj != null && g.Timer > 0.05f)
            {
                var rb = g.Rb;
                if (rb != null)
                {
                    float speed = rb.linearVelocity.magnitude;
                    Vector3 velocity = rb.linearVelocity;

                    if (speed > 0.1f)
                    {
                        float castDist = Mathf.Max(speed * Time.deltaTime * 2f, 0.15f);
                        float castRadius = 0.04f;
                        if (Physics.SphereCast(g.Obj.transform.position, castRadius,
                            velocity.normalized, out RaycastHit hit, castDist,
                            Config.WorldLayerMask, QueryTriggerInteraction.Ignore))
                        {
                            if (hit.collider.gameObject != g.Obj)
                            {
                                rb.linearVelocity = Vector3.zero;
                                rb.angularVelocity = Vector3.zero;
                                rb.isKinematic = !ExplosionSystem.IsLimb(hit.collider.gameObject);
                                rb.mass = 0.01f;
                                g.Stuck = true;
                                g.Obj.transform.position = hit.point; // snap to surface

                                Vector3 sNormal = hit.normal;
                                Vector3 throwFwd = g.ThrowDir.sqrMagnitude > 0.01f
                                    ? g.ThrowDir : g.Obj.transform.forward;
                                Vector3 surfaceFwd = (throwFwd
                                    - Vector3.Dot(throwFwd, sNormal) * sNormal);
                                if (surfaceFwd.sqrMagnitude < 0.001f)
                                {
                                    surfaceFwd = Vector3.Cross(sNormal, Vector3.forward);
                                    if (surfaceFwd.sqrMagnitude < 0.001f)
                                        surfaceFwd = Vector3.Cross(sNormal, Vector3.up);
                                }
                                g.Obj.transform.rotation = Quaternion.LookRotation(
                                    surfaceFwd.normalized, sNormal);
                                ep.Forward = g.Obj.transform.forward;

                                Vector3 stickyOffset = ep.Detonation switch
                                {
                                    DetonationMode.Remote => new Vector3(Config.C4LocalOffsetX,
                                                                            Config.C4LocalOffsetY,
                                                                            Config.C4LocalOffsetZ),
                                    DetonationMode.Proximity => new Vector3(Config.MineLocalOffsetX,
                                                                            Config.MineLocalOffsetY,
                                                                            Config.MineLocalOffsetZ),
                                    _ => Vector3.zero,
                                };
                                g.Obj.transform.position += g.Obj.transform.TransformDirection(stickyOffset);

                                if (ep.Detonation == DetonationMode.Proximity)
                                    CreateSightLines(g);

                                var hostRb = hit.collider.attachedRigidbody;
                                if (hostRb != null && !hostRb.isKinematic && ExplosionSystem.IsLimb(hit.collider.gameObject))
                                {
                                    g.HostRb = hostRb;
                                    g.LocalOffset = hostRb.transform.InverseTransformPoint(g.Obj.transform.position);
                                    g.LocalRotation = Quaternion.Inverse(hostRb.transform.rotation) * g.Obj.transform.rotation;

                                    var ownCollider = g.Obj.GetComponent<Collider>();
                                    if (ownCollider != null) ownCollider.enabled = false;
                                }
                            }
                        }
                    }
                }
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

            // ── In-flight tumble & surface pre-alignment ──────────────────────
            if (!g.Stuck && g.Obj != null)
            {
                var rb = g.Rb;
                if (rb != null)
                {
                    Vector3 vel = rb.linearVelocity;
                    float speed = vel.magnitude;

                    if (speed > 0.1f)
                    {
                        float rollRate = 360f + speed * 12f;   // deg/s

                        Vector3 rollAxis = vel.normalized;
                        Quaternion rollDelta = Quaternion.AngleAxis(
                            rollRate * dt, rollAxis);

                        if (g.HasAlignTarget)
                        {
                            float slerpT = Mathf.Clamp01(dt / 1.5f);
                            g.Obj.transform.rotation = Quaternion.Slerp(
                                g.Obj.transform.rotation,
                                g.AlignTargetRot,
                                slerpT);
                        }

                        g.Obj.transform.rotation = rollDelta * g.Obj.transform.rotation;
                    }
                }
            }

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

        private static Vector3 ThrowArcCast(Vector3 origin, Vector3 velocity)
        {
            float gy = Physics.gravity.y;
            float groundY = origin.y - 200f;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit groundProbe, 200f,
                Config.WorldLayerMask, QueryTriggerInteraction.Ignore))
                groundY = groundProbe.point.y;

            float flightTime = ExplosionSystem.BallisticGroundTime(
                gy, velocity.y, origin.y - groundY, Config.Fuse);

            int steps = Mathf.Max(2, Config.ArcDebugSteps);
            Vector3 prev = origin;
            for (int s = 1; s <= steps; s++)
            {
                float ft = flightTime * ((float)s / steps);
                Vector3 pt = origin + velocity * ft
                           + new Vector3(0f, 0.5f * gy * ft * ft, 0f);
                Vector3 seg = pt - prev;
                if (Physics.Raycast(prev, seg.normalized, out RaycastHit hit, seg.magnitude,
                    Config.WorldLayerMask, QueryTriggerInteraction.Ignore))
                    return hit.normal;   // ← surface normal at predicted landing
                prev = pt;
            }
            return Vector3.up;           // no hit — default to world-up
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
