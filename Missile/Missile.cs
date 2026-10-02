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

        /// <summary>
        /// A Javelin at <paramref name="target"/> (guided, TOP or DIR), or with
        /// <paramref name="rocket"/> an AT-4 rocket: no seeker, no motor after the tube, straight
        /// out where the launcher points and falling from there.
        /// </summary>
        private static HomingMissileState SpawnMissile(Rigidbody target, TargetBody body = null, bool rocket = false)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            var kind = rocket ? Ordnance.Rocket : Ordnance.Missile;

            var ep = ExplosionParams.ForWarhead(MissileWarheadMode);

            if (rocket)   // armed a few metres out, as it leaves at full speed
                ep.ArmDelay = Config.RocketArmDistance / Mathf.Max(1f, Config.RocketSpeed);

            // Out of the held launcher's tube if there is one, else ahead of the camera.
            bool fromTube = HeldTube(kind, out Vector3 spawnAt, out Vector3 tubeDir);
            if (!fromTube) spawnAt = cam.transform.position + cam.transform.forward * 1.5f;
            HeldFired(rocket);

            var owned = new System.Collections.Generic.List<Material>();
            // The bundled model: its own material copies, and for the Javelin a rig for fins and nozzle glow.
            GameObject obj = OrdnanceModels.Spawn(kind, spawnAt,
                out _, out Material[] bundleMats);
            MissileRig rig = null;
            RocketRig rocketRig = null;
            var mesh = obj == null ? Core.Meshes.GetMesh(ep.MeshName) : null;
            if (obj != null)
            {
                obj.name = rocket ? "Rocket" : "HomingMissile";
                owned.AddRange(bundleMats);
                if (rocket) rocketRig = RocketRig.Bind(obj.transform);
                else        rig = MissileRig.Bind(obj.transform);
                OrdnanceModels.PaintWarhead(kind, obj, MissileWarheadMode);   // its stencil says what it carries
            }
            else if (mesh != null)
            {
                obj = new GameObject("HomingMissile");
                obj.transform.position = spawnAt;
                obj.transform.localScale = Vector3.one * 0.1f;

                var mf = obj.AddComponent<MeshFilter>();
                mf.mesh = mesh;

                var mr = obj.AddComponent<MeshRenderer>();
                owned.AddRange(FruitMeshUtil.ApplyNewMaterials(mr, Core.Meshes.GetMaterials(ep.MeshName),
                    Config.FindShader(), new Color(0.3f, 0.3f, 0.32f, 1f)));
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            else
            {
                obj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                obj.name = "HomingMissile";
                obj.transform.position = spawnAt;
                obj.transform.localScale = new Vector3(0.08f, 0.2f, 0.08f);

                var rend = obj.GetComponent<Renderer>();
                if (rend != null)
                {
                    var bodyMat = new Material(Config.FindShader());
                    bodyMat.color = new Color(0.3f, 0.3f, 0.32f, 1f);
                    rend.material = bodyMat;
                    owned.Add(bodyMat);
                    rend.shadowCastingMode = ShadowCastingMode.Off;
                }

                var col = obj.GetComponent<Collider>();
                if (col != null) GameObject.Destroy(col);
            }

            var trailAnchor = new GameObject("TrailAnchor");
            trailAnchor.transform.SetParent(obj.transform, false);
            Transform nozzle = rig != null ? rig.Nozzle : rocketRig?.Nozzle;
            trailAnchor.transform.localPosition = nozzle != null
                ? nozzle.localPosition
                : new Vector3(0f, 0f, Config.MissileTrailOffsetZ);
            var trail = trailAnchor.AddComponent<TrailRenderer>();
            // The AT-4's motor is spent in the tube: only its tracer streaks after it.
            trail.time = rocket ? 0.35f : 1.5f;
            trail.startWidth = rocket ? 0.05f : 0.12f;
            trail.endWidth = 0.01f;
            var trailShader = Config.FindSpriteShader();
            if (trailShader != null)
            {
                var trailMat = new Material(trailShader);
                owned.Add(trailMat);
                trail.material = trailMat;
                trail.startColor = rocket ? new Color(1f, 0.45f, 0.15f, 0.9f) : new Color(1f, 0.6f, 0.1f, 0.8f);
                trail.endColor = new Color(0.5f, 0.5f, 0.5f, 0f);
            }
            trail.minVertexDistance = 0.1f;
            trail.Clear();

            bool unguided = rocket;
            bool topAttack = !rocket && MissileAttackMode == AttackMode.Top;

            Vector3 initVelocity;
            int initPhase;
            bool beam = false;
            if (unguided)
            {
                // The whole burn happens in the tube: it leaves at full speed. With a convergence
                // range it heads from the muzzle for the centre line that far out and then rides
                // that line, so it goes where the crosshair (or the sight) was, whatever the tube's
                // offset from the eye. Without one it flies along the tube and drops.
                beam = Config.RocketConvergence > 0f;
                Vector3 dir = beam
                    ? (cam.transform.position + cam.transform.forward * Config.RocketConvergence - spawnAt).normalized
                    : fromTube ? tubeDir : cam.transform.forward;
                initVelocity = dir * Config.RocketSpeed;
                initPhase = 1;
            }
            else
            {
                // Out of a held launcher: along its tube, which points up from the sight line.
                // Otherwise the old way: level with the view, MissileLaunchAngle up.
                Vector3 launchDir;
                if (fromTube) launchDir = tubeDir;
                else
                {
                    float launchRad = Config.MissileLaunchAngle * Mathf.Deg2Rad;
                    Vector3 flatFwd = cam.transform.forward;
                    flatFwd.y = 0f;
                    if (flatFwd.sqrMagnitude < 0.001f) flatFwd = Vector3.forward;
                    flatFwd.Normalize();
                    launchDir = (flatFwd * Mathf.Cos(launchRad) + Vector3.up * Mathf.Sin(launchRad)).normalized;
                }
                initVelocity = launchDir * Config.MissileSoftLaunchSpeed;
                initPhase = 0;
            }

            if (rig != null)
            {
                // Out of the tube folded and cold: the motor (glow, exhaust trail) lights at ignition.
                rig.Folded();
                if (rig.Glow != null) rig.Glow.enabled = initPhase != 0;
                trail.emitting = initPhase != 0;
            }
            rocketRig?.Folded();   // the tracer lights with the motor, in the tube: on from the start
            obj.transform.rotation = Quaternion.LookRotation(initVelocity);

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

            var state = new HomingMissileState
            {
                Beam = beam,
                BeamOrigin = cam.transform.position,
                BeamDir = cam.transform.forward,
                Obj = obj,
                TargetRb = beamRb,
                TargetRenderer = targetRend,
                Body = body,
                LastKnownTargetPos = tgtPos,
                PrevLOSDir = initialLOS,
                Velocity = initVelocity,
                Phase = initPhase,
                MotorTime = 0f,
                TopAttack = topAttack,
                Unguided = unguided,
                LaunchY = obj.transform.position.y,
                CruiseAlt = cruiseAlt,
                Owned = owned,
                Params = ep,
                Rig = rig,
                RocketRig = rocketRig,
                Trail = rig != null ? trail : null,
                Thermal = CluThermal,
            };
            _missiles.Add(state);

            if (Config.Dbg1) MelonLogger.Msg(
                $"[Missile] Launched ({(unguided ? "AT-4" : topAttack ? "TOP" : "DIR")}) " +
                $"alt={cruiseAlt:F0}m" +
                $"{(target != null ? $" at '{target.gameObject.name}'" : " (ballistic)")}");
            return state;
        }

        private static void TickMissile(HomingMissileState m, float dt)
        {
            if (m.Dead) return;
            if (m.Obj == null)   // destroyed by the game: nothing left to fly or to go off
            {
                m.Dead = true;
                ReleaseMaterials(m.Owned);
                return;
            }

            m.Timer += dt;
            TickMissileRig(m);

            // Where the target is now, if the seeker can see it: whole body, its middle (top
            // attack: its top, below); else the limb.
            Vector3 now = m.LastKnownTargetPos;
            bool tracked = false;
            if (m.Body != null && m.Body.Alive) { now = m.Body.Centre; tracked = true; }
            else if (m.TargetRb != null)
            {
                try { now = m.TargetRb.transform.position; tracked = true; }
                catch { m.TargetRb = null; }
            }
            // A day or night seeker loses its target in smoke and flies on to where it last saw
            // it, picking it up again once it is in sight. Checked ten times a second.
            if (tracked && !m.Unguided && !m.Thermal && Time.time >= m.NextSightCheck)
            {
                m.NextSightCheck = Time.time + 0.1f;
                bool was = m.Hidden;
                m.Hidden = SmokeHides(m.Obj.transform.position, now);
                if (m.Hidden != was && Config.Dbg1) MelonLogger.Msg($"[Missile] target {(m.Hidden ? "hidden by smoke: flying at its last seen point" : "in sight again")}");
            }
            if (tracked && !m.Hidden) m.LastKnownTargetPos = now;

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
                    Sfx.Play("MissileIgnite", m.Obj.transform.position, m.Obj.transform);
                    m.Motor = Sfx.Play("MissileMotorLoop", m.Obj.transform.position, m.Obj.transform);
                }
                return;
            }

            // The AT-4's rocket burns out in the tube. Converging: straight for the centre line,
            // then along it. Otherwise it only falls.
            if (m.Unguided)
            {
                if (m.Beam)
                {
                    float along = Vector3.Dot(pos - m.BeamOrigin, m.BeamDir);
                    if (!m.OnBeam && along >= Config.RocketConvergence)
                    {
                        m.OnBeam = true;
                        m.Obj.transform.position = pos = m.BeamOrigin + m.BeamDir * along;
                        m.Velocity = m.BeamDir * m.Velocity.magnitude;
                    }
                }
                else m.Velocity += Vector3.down * Config.RocketGravity * dt;
                FinishMissileFrame(m, dt, pos);
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
                    if (m.Body != null && m.Body.Alive) aimPoint.y = m.Body.Top;
                    else if (m.TargetRenderer != null)
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

        /// <summary>Fins spring out during the coast; the nozzle glows and trails while the motor burns.</summary>
        private static void TickMissileRig(HomingMissileState m)
        {
            if (m.RocketRig != null)
            {
                if (m.Timer <= RocketRig.DeployDone + 0.05f) m.RocketRig.DeployAt(m.Timer);
                return;
            }
            if (m.Rig == null) return;
            if (m.Timer <= MissileRig.DeployDone + 0.05f) m.Rig.DeployAt(m.Timer);

            bool burning = m.Phase >= 1 && m.MotorTime < Config.MissileFlightMotorTime;
            if (!burning && m.Motor != null) { GameObject.Destroy(m.Motor.gameObject); m.Motor = null; }   // burnt out
            if (m.Rig.Glow != null)   // sputters for a couple of frames as it lights
                m.Rig.Glow.enabled = burning && (m.MotorTime > 0.06f || ((int)(m.MotorTime / 0.017f) & 1) == 0);
            if (m.Trail != null && m.Trail.emitting != burning) m.Trail.emitting = burning;
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
            ReleaseMaterials(m.Owned);
            m.Obj = null;
            m.Dead = true;
            m.Params.Origin = origin;
            m.Params.Forward = impactDir;
            // A thermobaric warhead only disperses its fuel here; the cloud goes off a moment later.
            if (m.Params.Kind == "MissileTBX") Thermobaric.Disperse(m.Params);
            else ExplosionSystem.Detonate(m.Params);
        }
    }
}
