using FruitLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Color = UnityEngine.Color;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    public partial class Core
    {
        private static float MissileThrustCurve(float motorTime)
        {
            float burn = Config.MissileFlightMotorTime;
            if (motorTime <= 0f || motorTime >= burn) return 0f;

            float t = motorTime / burn;
            if (t < 0.058f) return Mathf.Lerp(0f, 0.74f, t / 0.058f);
            if (t < 0.115f) return Mathf.Lerp(0.74f, 0.84f, (t - 0.058f) / 0.057f);
            if (t < 0.231f) return Mathf.Lerp(0.84f, 0.97f, (t - 0.115f) / 0.116f);
            if (t < 0.346f) return Mathf.Lerp(0.97f, 1.00f, (t - 0.231f) / 0.115f);
            if (t < 0.462f) return Mathf.Lerp(1.00f, 0.84f, (t - 0.346f) / 0.116f);
            if (t < 0.808f) return Mathf.Lerp(0.84f, 0.065f, (t - 0.462f) / 0.346f);
            return Mathf.Lerp(0.065f, 0f, (t - 0.808f) / 0.192f);
        }

        private static float MissileDragDecel(float speed)
        {
            const float rho = 1.225f; // sea-level air density kg/m³
            float r = Config.MissileDiameter * 0.5f;
            float area = Mathf.PI * r * r;
            float dragForce = 0.5f * rho * speed * speed * Config.MissileDragCoeff * area;
            return dragForce / Mathf.Max(Config.MissileMass, 0.1f);
        }

        private static void SpawnMissile(Rigidbody target)
        {
            var cam = Camera.main;
            if (cam == null) return;

            var ep = MissileWarheadMode == WarheadMode.HE
                ? ExplosionParams.FromMissileHEConfig(Vector3.zero)
                : ExplosionParams.FromMissileConfig(Vector3.zero);

            if (MissileAttackMode == AttackMode.Unguided)
                ep.ArmDelay = 0f;

            GameObject obj;
            var mesh = Core.Meshes.GetMesh(ep.MeshName);
            if (mesh != null)
            {
                obj = new GameObject("HomingMissile");
                obj.transform.position = cam.transform.position + cam.transform.forward * 1.5f;
                obj.transform.localScale = Vector3.one * 0.1f;

                var mf = obj.AddComponent<MeshFilter>();
                mf.mesh = mesh;

                var mr = obj.AddComponent<MeshRenderer>();
                FruitMeshUtil.ApplyMaterials(mr, Core.Meshes.GetMaterials(ep.MeshName),
                    Config.FindShader(), new Color(0.3f, 0.3f, 0.32f, 1f));
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            else
            {
                obj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                obj.name = "HomingMissile";
                obj.transform.position = cam.transform.position + cam.transform.forward * 1.5f;
                obj.transform.localScale = new Vector3(0.08f, 0.2f, 0.08f);

                var rend = obj.GetComponent<Renderer>();
                if (rend != null)
                {
                    rend.material = new Material(Config.FindShader());
                    rend.material.color = new Color(0.3f, 0.3f, 0.32f, 1f);
                    rend.shadowCastingMode = ShadowCastingMode.Off;
                }

                var col = obj.GetComponent<Collider>();
                if (col != null) GameObject.Destroy(col);
            }

            var trailAnchor = new GameObject("TrailAnchor");
            trailAnchor.transform.SetParent(obj.transform, false);
            trailAnchor.transform.localPosition = new Vector3(0f, 0f, Config.MissileTrailOffsetZ);
            var trail = trailAnchor.AddComponent<TrailRenderer>();
            trail.time = 1.5f;
            trail.startWidth = 0.12f;
            trail.endWidth = 0.01f;
            var trailShader = Config.FindSpriteShader();
            if (trailShader != null)
            {
                trail.material = new Material(trailShader);
                trail.startColor = new Color(1f, 0.6f, 0.1f, 0.8f);
                trail.endColor = new Color(0.5f, 0.5f, 0.5f, 0f);
            }
            trail.minVertexDistance = 0.1f;
            trail.Clear();

            bool unguided = MissileAttackMode == AttackMode.Unguided;
            bool topAttack = MissileAttackMode == AttackMode.Top;

            Vector3 initVelocity;
            int initPhase;
            if (unguided)
            {
                initVelocity = cam.transform.forward * Config.MissileSpeed;
                initPhase = 1;
            }
            else
            {
                float launchRad = Config.MissileLaunchAngle * Mathf.Deg2Rad;
                Vector3 flatFwd = cam.transform.forward;
                flatFwd.y = 0f;
                if (flatFwd.sqrMagnitude < 0.001f) flatFwd = Vector3.forward;
                flatFwd.Normalize();
                Vector3 launchDir = (flatFwd * Mathf.Cos(launchRad)
                                   + Vector3.up * Mathf.Sin(launchRad)).normalized;
                initVelocity = launchDir * Config.MissileSoftLaunchSpeed;
                initPhase = 0;
            }

            Renderer targetRend = null;
            if (target != null)
                try { targetRend = target.GetComponentInChildren<Renderer>(); } catch { }

            Vector3 tgtPos;
            Rigidbody beamRb = null;
            if (target != null)
            {
                tgtPos = target.transform.position;
                beamRb = target;
            }
            else
            {
                tgtPos = cam.transform.position + cam.transform.forward * 800f;
            }

            float cruiseAlt = topAttack
                ? tgtPos.y + Config.MissileAscentHeight
                : tgtPos.y + Config.MissileDirectAscentHeight;

            Vector3 initialLOS = (tgtPos - obj.transform.position).normalized;

            _missiles.Add(new HomingMissileState
            {
                Obj = obj,
                TargetRb = beamRb,
                TargetRenderer = targetRend,
                LastKnownTargetPos = tgtPos,
                PrevLOSDir = initialLOS,
                Velocity = initVelocity,
                Phase = initPhase,
                MotorTime = 0f,
                TopAttack = topAttack,
                Unguided = unguided,
                LaunchY = obj.transform.position.y,
                CruiseAlt = cruiseAlt,
                Params = ep,
            });

            if (Config.Dbg1) MelonLogger.Msg(
                $"[Missile] Launched ({(unguided ? "UNGUIDED" : topAttack ? "TOP" : "DIR")}) " +
                $"alt={cruiseAlt:F0}m" +
                $"{(target != null ? $" at '{target.gameObject.name}'" : " (ballistic)")}");
        }

        private static void TickMissile(HomingMissileState m, float dt)
        {
            if (m.Dead || m.Obj == null) return;

            m.Timer += dt;

            if (m.TargetRb != null)
            {
                try { m.LastKnownTargetPos = m.TargetRb.transform.position; }
                catch { m.TargetRb = null; }
            }

            Vector3 pos = m.Obj.transform.position;
            Vector3 targetPos = m.LastKnownTargetPos;

            if (m.Phase == 0)
            {
                m.Velocity += Vector3.down * 6f * dt;   // gravity sag
                m.Obj.transform.position += m.Velocity * dt;
                if (m.Velocity.sqrMagnitude > 0.01f)
                    m.Obj.transform.rotation = Quaternion.LookRotation(m.Velocity);

                if (m.Timer >= Config.MissileSoftLaunchTime)
                {
                    m.Phase = 1;
                    m.MotorTime = 0f;
                    if (Config.Dbg1) MelonLogger.Msg("[Missile] Flight motor ignition");
                }
                return;
            }

            // ── Common: flight motor thrust + drag ──────────────────────
            m.MotorTime += dt;

            float speed = m.Velocity.magnitude;
            Vector3 curDir = speed > 0.01f ? m.Velocity / speed : Vector3.forward;

            float thrustNorm = MissileThrustCurve(m.MotorTime);
            float peakAccel = Config.MissileSpeed / (Config.MissileFlightMotorTime * 0.45f);
            float thrustAccel = thrustNorm * peakAccel;
            float dragDecel = MissileDragDecel(speed);
            float netAccel = thrustAccel - dragDecel;
            speed = Mathf.Max(speed + netAccel * dt, 1f);   // floor at 1 m/s

            if (m.Unguided)
            {
                m.Velocity = m.Velocity.normalized * speed;
                FinishMissileFrame(m, dt, pos);
                return;
            }

            if (m.Phase == 1)
            {
                Vector3 desiredDir;

                if (m.TopAttack)
                {
                    Vector3 waypoint = new Vector3(targetPos.x, m.CruiseAlt, targetPos.z);
                    desiredDir = (waypoint - pos).normalized;

                    float altProgress = Mathf.Clamp01((pos.y - m.LaunchY)
                        / (m.CruiseAlt - m.LaunchY + 0.1f));
                    desiredDir = (desiredDir + Vector3.up * (1f - altProgress) * 2f).normalized;

                    if (pos.y >= m.CruiseAlt * 0.85f
                        || m.MotorTime > Config.MissileFlightMotorTime * 0.7f)
                    {
                        m.Phase = 2;
                        if (Config.Dbg1) MelonLogger.Msg(
                            $"[Missile] Altitude hold at {pos.y:F0}m");
                    }
                }
                else
                {
                    desiredDir = (targetPos - pos).normalized;

                    if (speed >= Config.MissileSpeed * 0.85f
                        || m.MotorTime > Config.MissileFlightMotorTime * 0.4f)
                    {
                        m.Phase = 3;
                        Vector3 los = targetPos - pos;
                        if (los.sqrMagnitude > 0.01f)
                            m.PrevLOSDir = los.normalized;
                        if (Config.Dbg1) MelonLogger.Msg(
                            $"[Missile] Direct terminal at {pos.y:F0}m, range=" +
                            $"{Vector3.Distance(pos, targetPos):F0}m");
                    }
                }

                Vector3 newDir = Vector3.RotateTowards(
                    curDir, desiredDir, Config.MissileSteerRate * dt, 0f);
                m.Velocity = newDir.normalized * speed;
            }
            if (m.Phase == 2)
            {
                Vector3 toTarget = targetPos - pos;
                Vector3 horizToTarget = new Vector3(toTarget.x, 0f, toTarget.z);
                float horizDist = horizToTarget.magnitude;

                float altErr = m.CruiseAlt - pos.y;
                Vector3 desiredDir = horizToTarget.normalized
                    + Vector3.up * Mathf.Clamp(altErr * 0.3f, -1f, 1f);
                desiredDir.Normalize();

                Vector3 newDir = Vector3.RotateTowards(
                    curDir, desiredDir, Config.MissileSteerRate * dt, 0f);
                m.Velocity = newDir.normalized * speed;
                float losAngle = Mathf.Atan2(pos.y - targetPos.y, horizDist) * Mathf.Rad2Deg;
                float terminalThreshold = m.TopAttack ? 20f : 8f;

                if (losAngle > terminalThreshold || horizDist < 5f)
                {
                    m.Phase = 3;
                    // Snapshot LOS for proportional navigation
                    Vector3 los = (targetPos - pos);
                    if (los.sqrMagnitude > 0.01f)
                        m.PrevLOSDir = los.normalized;
                    if (Config.Dbg1) MelonLogger.Msg(
                        $"[Missile] Terminal guidance (LOS={losAngle:F1}°, " +
                        $"range={horizDist:F0}m)");
                }
            }

            if (m.Phase == 3)
            {
                Vector3 aimPoint = targetPos;

                if (m.TopAttack)
                {
                    if (m.TargetRenderer != null)
                    {
                        try { aimPoint.y = m.TargetRenderer.bounds.max.y; }
                        catch { m.TargetRenderer = null; }
                    }
                }

                Vector3 los = aimPoint - pos;
                float range = los.magnitude;
                if (range < 0.1f) range = 0.1f;
                Vector3 losDir = los / range;
                Vector3 losRate = Vector3.Cross(m.PrevLOSDir, losDir) / Mathf.Max(dt, 0.001f);
                m.PrevLOSDir = losDir;

                float closingSpeed = -Vector3.Dot(m.Velocity, losDir);
                Vector3 pnAccel = Vector3.zero;
                if (closingSpeed > 2f)
                    pnAccel = Config.MissileNavGain * closingSpeed * losRate;

                Vector3 desiredVel = m.Velocity + pnAccel * dt;
                Vector3 pnDir = desiredVel.normalized;
                float pursuitWeight = Mathf.Max(
                    0.6f,                                    // always at least 60% pursuit
                    Mathf.Clamp01(1f - range / 10f)
                );
                Vector3 desiredDir = Vector3.Lerp(pnDir, losDir, pursuitWeight).normalized;

                float terminalSteer = m.TopAttack ? 2.5f : 1.8f;
                Vector3 newDir = Vector3.RotateTowards(
                    curDir, desiredDir,
                    Config.MissileSteerRate * terminalSteer * dt, 0f);
                m.Velocity = newDir.normalized * speed;

                if (range < Config.MissileDetonationRadius)
                {
                    ExplodeMissile(m);
                    return;
                }
            }

            FinishMissileFrame(m, dt, pos);
        }

        private static void FinishMissileFrame(HomingMissileState m, float dt, Vector3 preMovePos)
        {
            if (m.Unguided)
            {
                m.Obj.transform.position += m.Velocity * dt;
                if (m.Velocity.sqrMagnitude > 0.01f)
                    m.Obj.transform.rotation = Quaternion.LookRotation(m.Velocity);
            }

            Vector3 frameMove = m.Velocity * dt;
            float castDist = frameMove.magnitude + m.Params.ImpactCastRange;
            Vector3 incomingDir = m.Velocity.normalized;
            Vector3 castOrigin = (m.Unguided ? m.Obj.transform.position : preMovePos)
                                 - incomingDir * 0.15f;

            if (m.Timer > m.Params.ArmDelay
                && Physics.SphereCast(castOrigin, m.Params.ImpactCastRadius,
                    incomingDir, out RaycastHit hit, castDist + 0.15f,
                    Config.WorldLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.gameObject != m.Obj)
                {
                    m.Obj.transform.position = hit.point;
                    ExplodeMissile(m);
                    return;
                }
            }

            if (m.Unguided)
            {
                if (m.Timer > 20f) ExplodeMissile(m);
                return;
            }

            m.Obj.transform.position += frameMove;
            if (m.Velocity.sqrMagnitude > 0.01f)
                m.Obj.transform.rotation = Quaternion.LookRotation(m.Velocity);

            if (m.Timer > 20f) ExplodeMissile(m);
        }

        private static void ExplodeMissile(HomingMissileState m)
        {
            Vector3 origin = m.Obj != null
                ? m.Obj.transform.position : m.LastKnownTargetPos;

            Vector3 impactDir = m.Velocity.sqrMagnitude > 0.001f
                ? m.Velocity.normalized
                : Vector3.down;

            origin -= impactDir * Config.MissileExplosionLift;

            if (m.Obj != null)
            {
                var col = m.Obj.GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }
            Physics.SyncTransforms();
            if (m.Obj != null) GameObject.Destroy(m.Obj);
            m.Obj = null;
            m.Dead = true;
            m.Params.Origin = origin;
            m.Params.Forward = impactDir;
            ExplosionSystem.Detonate(m.Params);
        }
    }
}
