using MelonLoader;
using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // How thrown ordnance flies.
    //
    // Two models, because the two kinds want opposite things from a surface.
    //
    // STICKY (C4, claymore): scripted, not simulated. The body is kinematic in flight and
    // follows the exact ballistic arc; every frame's step is swept *before* it is taken,
    // so the first surface the charge would touch is where it sticks. There is no contact
    // for physics to resolve, so nothing can bounce. The old version let physics fly it
    // and looked for the wall from Update with a 4 cm sphere at the centre - but the
    // convex hull reaches 13 cm, physics steps on its own clock, so the hull hit first,
    // physics bounced it, and the next cast pointed away from the surface it had just
    // left. Rotation was written onto the simulated body every frame on top, which added
    // contact impulses of its own.
    //
    // The landing is predicted at throw time with the same sweep, and the tumble is timed
    // backwards from it: rotation(t) = spin(rate * (t - T)) * landing pose, which is
    // exactly the landing pose at the moment of contact. The charge tumbles end over end
    // and arrives flat, with no snap. If the world moved meanwhile (a Bob walked into the
    // arc), the real hit wins and the pose snaps by what changed.
    //
    // A scripted flight is a function of its launch values alone, which is also what
    // FruitNet will need: a throw replicates as one message.
    //
    // GRENADE: physical. It is supposed to bounce and roll, so physics owns it outright -
    // launched with a real angular velocity, its own physics material, and nothing
    // writing its transform afterwards.
    // ══════════════════════════════════════════════════════════════════════════════

    public partial class Core
    {
        private const float PredictStep = 1f / 60f;
        private const float PredictHorizon = 8f;
        /// <summary>A sticky charge thrown off the map falls this long, then quietly goes away.</summary>
        private const float MaxScriptedFlight = 30f;

        private static PhysicsMaterial _grenadeMaterial;
        private static bool _grenadeMaterialFailed;

        private static Vector3 ThrowVelocity(Camera cam, float force, float arc) =>
            (Quaternion.AngleAxis(arc, cam.transform.right) * cam.transform.forward) * force;

        private static float Vary(float value) =>
            value * (1f + UnityEngine.Random.Range(-Config.ThrowTumbleVariance, Config.ThrowTumbleVariance));

        // ── Sticky: scripted flight ─────────────────────────────────────────────

        private static void LaunchScripted(GrenadeState g, Vector3 velocity, Camera cam)
        {
            if (g.Rb != null)
            {
                g.Rb.isKinematic = true;
                g.Rb.interpolation = RigidbodyInterpolation.None;
            }

            g.Ballistic = true;
            g.Velocity = velocity;
            g.FlightTime = 0f;
            // Every sticky charge now faces the way it was thrown, not wherever the tumble
            // happened to leave its forward - that is what makes the landing predictable.
            g.ThrowDir = cam.transform.forward;
            g.CastRadius = FlightRadius(g.Obj);

            // End over end about the thrower's right, wobbled so no two throws match.
            g.SpinAxis = Quaternion.AngleAxis(UnityEngine.Random.Range(-15f, 15f), velocity.normalized)
                       * cam.transform.right;
            g.SpinRate = Vary(Config.ThrowTumbleRate);

            g.HasLanding = PredictLanding(g, g.Obj.transform.position, velocity, out g.LandTime, out RaycastHit hit);
            if (g.HasLanding) StickPose(g, hit, out _, out g.LandRot);

            if (Config.Dbg1)
                MelonLogger.Msg(g.HasLanding
                    ? $"[Throw] {g.Params.Kind}: lands on '{hit.collider.name}' in {g.LandTime:F2}s, tumble {g.SpinRate:F0} deg/s"
                    : $"[Throw] {g.Params.Kind}: no landing within {PredictHorizon}s, free tumble");

            ApplyTumble(g, 0f);
        }

        private static void TickScriptedFlight(GrenadeState g, float dt)
        {
            if (g.Obj == null) return;

            g.FlightTime += dt;
            if (g.FlightTime > MaxScriptedFlight)
            {
                Object.Destroy(g.Obj);
                ReleaseMaterials(g.Owned);
                g.Obj = null;
                g.Dead = true;
                return;
            }

            // Exact under constant gravity: the chord of this step's arc.
            Vector3 p = g.Obj.transform.position;
            Vector3 v = g.Velocity;
            Vector3 vNext = v + Physics.gravity * dt;
            Vector3 step = (v + vNext) * (0.5f * dt);

            if (FlightCast(g, p, step, out RaycastHit hit))
            {
                StickPose(g, hit, out Vector3 pos, out Quaternion rot);
                if (Config.Dbg1)
                    MelonLogger.Msg($"[Throw] {g.Params.Kind} stuck on '{hit.collider.name}' at {g.FlightTime:F2}s"
                        + (g.HasLanding
                            ? $" (predicted {g.LandTime:F2}s, pose off by {Quaternion.Angle(rot, g.LandRot):F1} deg)"
                            : ""));
                g.Ballistic = false;
                StickAt(g, pos, rot, hit.collider);
                return;
            }

            g.Obj.transform.position = p + step;
            g.Velocity = vNext;
            ApplyTumble(g, dt);
        }

        /// <summary>Timed so the spin reaches zero at the predicted contact; free spin otherwise.</summary>
        private static void ApplyTumble(GrenadeState g, float dt)
        {
            var t = g.Obj.transform;
            if (g.HasLanding)
                t.rotation = Quaternion.AngleAxis(g.SpinRate * (g.FlightTime - g.LandTime), g.SpinAxis) * g.LandRot;
            else
                t.rotation = Quaternion.AngleAxis(g.SpinRate * dt, g.SpinAxis) * t.rotation;
        }

        /// <summary>Walks the same arc TickScriptedFlight will, with the same sweep.</summary>
        private static bool PredictLanding(GrenadeState g, Vector3 p, Vector3 v, out float time, out RaycastHit hit)
        {
            Vector3 gravity = Physics.gravity;
            for (float t = 0f; t < PredictHorizon; t += PredictStep)
            {
                Vector3 vNext = v + gravity * PredictStep;
                Vector3 step = (v + vNext) * (0.5f * PredictStep);
                if (FlightCast(g, p, step, out hit))
                {
                    time = t + PredictStep * (hit.distance / step.magnitude);
                    return true;
                }
                p += step;
                v = vNext;
            }
            time = 0f;
            hit = default;
            return false;
        }

        /// <summary>
        /// The nearest surface along one step. A sphere as thick as the charge's thinnest
        /// side: that is the side it lands on, and it keeps the prediction and the flight
        /// agreeing. Loose ordnance (our own included) is not a surface.
        ///
        /// <b>Single casts on purpose.</b> Physics.SphereCastAll is stripped from this build:
        /// it compiles against the reference assemblies and throws at runtime, which is what
        /// left every thrown charge frozen in mid-air. So a loose round in the way is stepped
        /// past and the cast repeated. A cast never reports the collider it starts inside,
        /// which keeps the charge's own collider out of it.
        /// </summary>
        private static bool FlightCast(GrenadeState g, Vector3 from, Vector3 step, out RaycastHit hit)
        {
            hit = default;
            float remaining = step.magnitude;
            if (remaining < 1e-5f) return false;

            Vector3 dir = step / remaining;
            int mask = Config.WorldLayerMask & ~(1 << IgnoreRaycastLayer);
            float travelled = 0f;

            for (int tries = 0; tries < 4 && remaining > 1e-5f; tries++)
            {
                if (!Physics.SphereCast(from, g.CastRadius, dir, out hit, remaining, mask, QueryTriggerInteraction.Ignore))
                    return false;

                bool own = g.Obj != null && hit.collider.transform.IsChildOf(g.Obj.transform);
                if (!own && !IsLooseOrdnance(hit.collider))
                {
                    hit.distance += travelled;
                    return true;
                }

                const float skip = 0.02f;
                float advance = hit.distance + skip;
                from += dir * advance;
                travelled += advance;
                remaining -= advance;
            }
            hit = default;
            return false;
        }

        private static float FlightRadius(GameObject obj)
        {
            var mf = obj.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) return 0.05f;
            Vector3 e = Vector3.Scale(mesh.bounds.extents, obj.transform.lossyScale);
            return Mathf.Clamp(Mathf.Min(e.x, Mathf.Min(e.y, e.z)), 0.01f, 0.1f);
        }

        /// <summary>Where a charge touching <paramref name="hit"/> sits: the same pose placing uses.</summary>
        private static void StickPose(GrenadeState g, RaycastHit hit, out Vector3 pos, out Quaternion rot)
        {
            Vector3 fwd = g.ThrowDir.sqrMagnitude > 0.01f ? g.ThrowDir : g.Obj.transform.forward;
            rot = SurfaceRotation(hit.normal, fwd);
            pos = hit.point + rot * StickOffset(g.Params.Detonation);
        }

        // ── Grenade: physical flight ────────────────────────────────────────────

        private static void LaunchPhysical(GrenadeState g, Vector3 velocity, Camera cam)
        {
            var rb = g.Rb;
            if (rb == null) return;
            rb.linearVelocity = velocity;

            Vector3 axis = Quaternion.AngleAxis(UnityEngine.Random.Range(-25f, 25f), velocity.normalized)
                         * cam.transform.right;
            rb.angularVelocity = axis * (Vary(Config.GrenadeTumbleRate) * Mathf.Deg2Rad);
        }

        /// <summary>Thrown or set down, a grenade is a physics object from the start.</summary>
        private static void ConfigurePhysicalBody(GrenadeState g)
        {
            var rb = g.Rb;
            if (rb == null) return;
            // No continuous collision: Rigidbody.collisionDetectionMode's setter is stripped
            // from this build. A thrown grenade covers ~16 cm a physics step against a 6.5 cm
            // radius, which holds for floors and walls; paper-thin props can still be missed.
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.maxAngularVelocity = 60f;
            rb.angularDamping = Config.GrenadeRollDamping;

            var mat = GrenadeMaterial();
            var col = g.Obj.GetComponent<Collider>();
            // Collider.sharedMaterial's setter is stripped too; material assigns the same asset.
            if (mat != null && col != null) col.material = mat;
        }

        /// <summary>
        /// Bounce combined as Maximum, so the grenade's own bounciness decides and not
        /// whatever the floor was authored with. Values re-read every throw, so the menu
        /// sliders apply without a restart.
        /// </summary>
        private static PhysicsMaterial GrenadeMaterial()
        {
            if (_grenadeMaterialFailed) return null;
            try
            {
                if (_grenadeMaterial == null)
                {
                    _grenadeMaterial = new PhysicsMaterial { name = "BA_Grenade" };
                    _grenadeMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    _grenadeMaterial.bounceCombine = PhysicsMaterialCombine.Maximum;
                    _grenadeMaterial.frictionCombine = PhysicsMaterialCombine.Average;
                }
                _grenadeMaterial.bounciness = Mathf.Clamp01(Config.GrenadeBounciness);
                _grenadeMaterial.dynamicFriction = Mathf.Max(0f, Config.GrenadeFriction);
                _grenadeMaterial.staticFriction = Mathf.Max(0f, Config.GrenadeFriction * 1.2f);
                return _grenadeMaterial;
            }
            catch (System.Exception e)
            {
                _grenadeMaterialFailed = true;
                MelonLogger.Warning("[Throw] could not create the grenade physics material, using the default: " + e.Message);
                return null;
            }
        }
    }
}
