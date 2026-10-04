using Il2CppInterop.Runtime;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    internal static class OrdnanceFactory
    {
        /// <summary>A grey primitive standing in for a charge the bundle has no model for (it failed to load).</summary>
        public static GameObject BuildFallback(string objName, Vector3 position, PrimitiveType primitive,
            out Renderer renderer, out Material[] materials)
        {
            materials = new Material[0];
            var obj = GameObject.CreatePrimitive(primitive);
            obj.name = objName;
            obj.transform.position = position;
            obj.transform.localScale = Vector3.one * 0.2f;

            renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Config.FindShader());
                mat.color = Color.grey;
                renderer.material = mat;
                materials = new[] { mat };
            }
            return obj;
        }
    }

    public partial class Core
    {
        private static Color[] SnapshotBaseColors(Material[] mats)
        {
            if (mats == null) return null;
            var baseColors = new Color[mats.Length];
            for (int m = 0; m < mats.Length; m++)
                baseColors[m] = mats[m] != null ? mats[m].color : Color.grey;
            return baseColors;
        }

        /// <summary>Materials are not destroyed with their GameObject, so whatever a charge
        /// made for itself goes here when it does.</summary>
        private static void ReleaseMaterials(List<Material> owned)
        {
            if (owned == null) return;
            foreach (var mat in owned)
                if (mat != null) UnityEngine.Object.Destroy(mat);
            owned.Clear();
        }

        /// <summary>Body, rigidbody and state for one charge, at rest at <paramref name="position"/>.
        /// Throwing and placing both start here; only what happens next differs.</summary>
        private static GrenadeState CreateOrdnance(Ordnance o, Vector3 position)
        {
            ExplosionParams ep; string name; float mass; PrimitiveType fallback;
            switch (o)
            {
                case Ordnance.C4:
                    ep = ExplosionParams.FromC4Config(Vector3.zero);
                    name = "C4"; mass = 0.75f; fallback = PrimitiveType.Sphere; break;
                case Ordnance.Claymore:
                    ep = ExplosionParams.FromClaymoreConfig(Vector3.zero);
                    name = "Claymore"; mass = 0.75f; fallback = PrimitiveType.Cube; break;
                case Ordnance.Smoke:
                    ep = ExplosionParams.FromSmokeConfig(Vector3.zero);
                    name = "SmokeGrenade"; mass = 0.55f; fallback = PrimitiveType.Cylinder; break;
                case Ordnance.Flash:
                    ep = ExplosionParams.FromFlashConfig(Vector3.zero);
                    name = "Flashbang"; mass = 0.4f; fallback = PrimitiveType.Cylinder; break;
                default:
                    ep = ExplosionParams.FromGrenadeConfig(Vector3.zero);
                    name = "Grenade"; mass = 0.5f; fallback = PrimitiveType.Sphere; break;
            }

            var obj = OrdnanceModels.Spawn(o, position, out Renderer rend, out Material[] mats);
            if (obj != null)
            {
                obj.name = name;
                // Thrown or placed, a live grenade is the body alone: the pin and spoon stayed
                // with the hand (Ordnance.Held.cs lets them fall).
                OrdnanceModels.StripLooseParts(obj);
            }
            else obj = OrdnanceFactory.BuildFallback(name, position, fallback, out rend, out mats);

            if (o == Ordnance.Smoke) TintBand(obj.transform, SmokeColour);

            // A live C4 left the hand armed: cover open, antenna out, screen on, LED blinking.
            var rig = C4Rig.Bind(obj.transform);
            rig?.Armed();
            // A live claymore left the hand deployed: legs out, head up, lenses lit.
            var clay = ClaymoreRig.Bind(obj.transform);
            clay?.Deployed();

            // Bundled prefabs bring their own Rigidbody.
            var rb = obj.GetComponent<Rigidbody>()
                  ?? obj.AddComponent(Il2CppType.Of<Rigidbody>()).TryCast<Rigidbody>();
            rb.mass = mass;

            var g = new GrenadeState
            {
                SmokeColor = o == Ordnance.Smoke ? SmokeColour : Color.white,
                Obj = obj,
                GrenadeRenderer = rend,
                Rb = rb,
                Params = ep,
                BaseColors = SnapshotBaseColors(mats),
                FlashMats = mats,
                Blink = rig?.Led,
                Clay = clay,
            };
            g.Owned.AddRange(mats);
            if (!ep.Sticky) ConfigurePhysicalBody(g);
            return g;
        }

        private static void ThrowOrdnance(Ordnance o) => ThrowOrdnance(o, null, null);

        /// <summary>
        /// Throws a new charge along the view. <paramref name="from"/>/<paramref name="rotation"/>
        /// start it where the held model was (default: 1 m ahead of the camera, unrotated).
        /// </summary>
        private static GrenadeState ThrowOrdnance(Ordnance o, Vector3? from, Quaternion? rotation)
        {
            var cam = Camera.main;
            if (cam == null) return null;

            var g = CreateOrdnance(o, from ?? cam.transform.position + cam.transform.forward * 1f);
            if (rotation.HasValue) g.Obj.transform.rotation = rotation.Value;

            float force, arc;
            switch (o)
            {
                case Ordnance.C4:       force = Config.C4ThrowForce;   arc = Config.C4ThrowArc;   break;
                case Ordnance.Claymore: force = Config.MineThrowForce; arc = Config.MineThrowArc; break;
                default:                force = Config.ThrowForce;     arc = Config.ThrowArc;     break;
            }

            Vector3 velocity = ThrowVelocity(cam, force, arc);
            try
            {
                if (g.Params.Sticky) LaunchScripted(g, velocity, cam);
                else                 LaunchPhysical(g, velocity, cam);
            }
            catch (System.Exception e)
            {
                // Never leave a charge frozen and untracked: fall back to a plain physics throw.
                MelonLogger.Warning($"[Throw] {o} launch failed, throwing it as a plain physics body: {e.Message}");
                g.Ballistic = false;
                if (g.Rb != null)
                {
                    g.Rb.isKinematic = false;
                    g.Rb.linearVelocity = velocity;
                }
            }

            _grenades.Add(g);
            _lastThrowTime = Time.time;
            return g;
        }
    }
}
