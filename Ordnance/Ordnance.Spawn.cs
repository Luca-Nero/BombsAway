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
        private static Quaternion ThrowAndPredictAlign(GameObject obj, Rigidbody rb, Camera cam,
            float throwForce, float throwArc)
        {
            Vector3 throwVel = (Quaternion.AngleAxis(throwArc, cam.transform.right)
                                * cam.transform.forward) * throwForce;
            rb.linearVelocity = throwVel;

            Vector3 landNorm = ThrowArcCast(obj.transform.position, throwVel);
            float tiltX = (float)(SharedRng.Instance.NextDouble() * 20.0 - 10.0);
            float tiltZ = (float)(SharedRng.Instance.NextDouble() * 20.0 - 10.0);
            return Quaternion.LookRotation(Vector3.Cross(landNorm, cam.transform.right), landNorm)
                 * Quaternion.Euler(tiltX, 0f, tiltZ);
        }

        private static Color[] SnapshotBaseColors(Renderer rend)
        {
            if (rend == null) return null;
            var mats = rend.materials;
            var baseColors = new Color[mats.Length];
            for (int m = 0; m < mats.Length; m++)
                baseColors[m] = mats[m] != null ? mats[m].color : Color.grey;
            return baseColors;
        }

        private static void SpawnGrenade()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var ep = ExplosionParams.FromGrenadeConfig(Vector3.zero);

            Vector3 spawnPos = cam.transform.position + cam.transform.forward * 1f;
            var obj = OrdnanceFactory.BuildBody(ep.MeshName, "Grenade", spawnPos, 0.1f,
                PrimitiveType.Sphere, 0.2f, OrdnanceFactory.ArmyGreen, out Renderer rend);

            var rb = obj.AddComponent(Il2CppType.Of<Rigidbody>()).TryCast<Rigidbody>();
            rb.mass = 0.5f;

            var alignTarget = ThrowAndPredictAlign(obj, rb, cam, Config.ThrowForce, Config.ThrowArc);

            _grenades.Add(new GrenadeState
            {
                Obj = obj,
                GrenadeRenderer = rend,
                Rb = rb,
                Params = ep,
                BaseColors = SnapshotBaseColors(rend),
                HasAlignTarget = true,
                AlignTargetRot = alignTarget,
            });
        }

        private static void SpawnC4()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var ep = ExplosionParams.FromC4Config(Vector3.zero);

            Vector3 spawnPos = cam.transform.position + cam.transform.forward * 1f;
            var obj = OrdnanceFactory.BuildBody(ep.MeshName, "C4", spawnPos, 0.15f,
                PrimitiveType.Sphere, 0.2f, OrdnanceFactory.ArmyGreen, out Renderer rend);

            var rb = obj.AddComponent(Il2CppType.Of<Rigidbody>()).TryCast<Rigidbody>();
            rb.mass = 0.75f;

            var alignTarget = ThrowAndPredictAlign(obj, rb, cam, Config.C4ThrowForce, Config.C4ThrowArc);

            _grenades.Add(new GrenadeState
            {
                Obj = obj,
                GrenadeRenderer = rend,
                Rb = rb,
                Params = ep,
                BaseColors = SnapshotBaseColors(rend),
                HasAlignTarget = true,
                AlignTargetRot = alignTarget,
            });
        }

        private static void SpawnMine()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var ep = ExplosionParams.FromClaymoreConfig(Vector3.zero);

            Vector3 spawnPos = cam.transform.position + cam.transform.forward * 1f;
            var obj = OrdnanceFactory.BuildBody(ep.MeshName, "Claymore", spawnPos, 0.15f,
                PrimitiveType.Cube, 0.2f, OrdnanceFactory.ArmyGreen, out Renderer rend);

            var rb = obj.AddComponent(Il2CppType.Of<Rigidbody>()).TryCast<Rigidbody>();
            rb.mass = 0.75f;

            var alignTarget = ThrowAndPredictAlign(obj, rb, cam, Config.MineThrowForce, Config.MineThrowArc);

            _grenades.Add(new GrenadeState
            {
                Obj = obj,
                GrenadeRenderer = rend,
                Rb = rb,
                Params = ep,
                BaseColors = SnapshotBaseColors(rend),
                HasAlignTarget = true,
                AlignTargetRot = alignTarget,
                ThrowDir = cam.transform.forward,
            });
        }
    }
}
