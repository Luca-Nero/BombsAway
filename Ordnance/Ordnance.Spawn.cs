using FruitLib;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Color = UnityEngine.Color;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    internal static class OrdnanceFactory
    {
        public static readonly Color ArmyGreen = new Color(0.29f, 0.33f, 0.13f, 1f);

        public static GameObject BuildBody(string meshName, string objName, Vector3 position,
            float meshScale, PrimitiveType fallbackPrimitive, float fallbackScale,
            Color noMaterialColor, out Renderer renderer)
        {
            var mesh = Core.Meshes.GetMesh(meshName);
            GameObject obj;

            if (mesh != null)
            {
                obj = new GameObject(objName);
                obj.transform.position = position;
                obj.transform.localScale = Vector3.one * meshScale;

                var mf = obj.AddComponent<MeshFilter>();
                mf.mesh = mesh;

                var mr = obj.AddComponent<MeshRenderer>();
                FruitMeshUtil.ApplyMaterials(mr, Core.Meshes.GetMaterials(meshName),
                    Config.FindShader(), noMaterialColor);
                mr.shadowCastingMode = ShadowCastingMode.Off;
                renderer = mr;

                var mc = obj.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = true;
            }
            else
            {
                obj = GameObject.CreatePrimitive(fallbackPrimitive);
                obj.name = objName;
                obj.transform.position = position;
                obj.transform.localScale = Vector3.one * fallbackScale;

                renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = new Material(Config.FindShader());
                    renderer.material.color = Color.grey;
                }
            }

            return obj;
        }
    }

    public partial class Core
    {
        private static Color[] SnapshotBaseColors(Renderer rend)
        {
            if (rend == null) return null;
            var mats = rend.materials;
            var baseColors = new Color[mats.Length];
            for (int m = 0; m < mats.Length; m++)
                baseColors[m] = mats[m] != null ? mats[m].color : Color.grey;
            return baseColors;
        }

        /// <summary>Body, rigidbody and state for one charge, at rest at <paramref name="position"/>.
        /// Throwing and placing both start here; only what happens next differs.</summary>
        private static GrenadeState CreateOrdnance(Ordnance o, Vector3 position)
        {
            ExplosionParams ep; string name; float scale, mass; PrimitiveType fallback;
            switch (o)
            {
                case Ordnance.C4:
                    ep = ExplosionParams.FromC4Config(Vector3.zero);
                    name = "C4"; scale = 0.15f; mass = 0.75f; fallback = PrimitiveType.Sphere; break;
                case Ordnance.Claymore:
                    ep = ExplosionParams.FromClaymoreConfig(Vector3.zero);
                    name = "Claymore"; scale = 0.15f; mass = 0.75f; fallback = PrimitiveType.Cube; break;
                default:
                    ep = ExplosionParams.FromGrenadeConfig(Vector3.zero);
                    name = "Grenade"; scale = 0.1f; mass = 0.5f; fallback = PrimitiveType.Sphere; break;
            }

            var obj = OrdnanceFactory.BuildBody(ep.MeshName, name, position, scale,
                fallback, 0.2f, OrdnanceFactory.ArmyGreen, out Renderer rend);

            var rb = obj.AddComponent(Il2CppType.Of<Rigidbody>()).TryCast<Rigidbody>();
            rb.mass = mass;

            var g = new GrenadeState
            {
                Obj = obj,
                GrenadeRenderer = rend,
                Rb = rb,
                Params = ep,
                BaseColors = SnapshotBaseColors(rend),
            };
            if (!ep.Sticky) ConfigurePhysicalBody(g);
            return g;
        }

        private static void ThrowOrdnance(Ordnance o)
        {
            var cam = Camera.main;
            if (cam == null) return;

            var g = CreateOrdnance(o, cam.transform.position + cam.transform.forward * 1f);

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
        }
    }
}
