using System.Collections.Generic;
using System.Reflection;
using FruitLib;
using MelonLoader;
using UnityEngine;

namespace BombsAway
{
    /// <summary>
    /// Ordnance models from the mod's asset bundle (Bundles/bombsaway.bundle, embedded). Each
    /// prefab is a root with a convex MeshCollider and a Rigidbody, and one child per moving
    /// part (grenade: Body, Spoon, Pin) so later animations can drive them. Anything the
    /// bundle lacks, or a bundle that fails to load, falls back to the old *_mesh.json path.
    /// </summary>
    internal static class OrdnanceModels
    {
        private const string BundleResource = "bombsaway.bundle";

        private static FruitBundle _bundle;
        private static bool _loadTried;
        private static readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();

        /// <summary>What goes out into the world.</summary>
        private static string PrefabName(Ordnance o) => o switch
        {
            Ordnance.Grenade => "Grenade",
            Ordnance.C4       => "C4",
            Ordnance.Claymore => "Claymore",
            Ordnance.Missile  => "JavelinMissile",
            Ordnance.Rocket   => "AT4Rocket",
            Ordnance.Smoke    => "SmokeGrenade",
            Ordnance.Flash    => "Flashbang",
            _                 => null,
        };

        /// <summary>What the hand holds: the charge itself, or for a missile or rocket its launcher.</summary>
        private static string HeldPrefabName(Ordnance o) => o switch
        {
            Ordnance.Missile => "JavelinLauncher",
            Ordnance.Rocket  => "AT4Launcher",
            _                => PrefabName(o),
        };

        internal static bool Has(Ordnance o) => Prefab(o) != null;

        /// <summary>The bundled prefab, or null to use the JSON mesh.</summary>
        internal static GameObject Prefab(Ordnance o) => Load(PrefabName(o));

        /// <summary>Any prefab in the bundle by name (an explosion effect), or null.</summary>
        internal static GameObject Asset(string name) => Load(name);

        /// <summary>The bundled model for the hand, or null if there is none.</summary>
        internal static GameObject HeldPrefab(Ordnance o) => Load(HeldPrefabName(o));

        private static void EnsureBundle()
        {
            if (_loadTried) return;
            _loadTried = true;
            try { _bundle = FruitBundle.FromResource(Assembly.GetExecutingAssembly(), BundleResource); }
            catch (System.Exception e) { MelonLogger.Warning($"[Models] {BundleResource} failed to load, using JSON meshes: {e.Message}"); }
        }

        private static GameObject Load(string name)
        {
            if (name == null) return null;
            if (_prefabs.TryGetValue(name, out var cached)) return cached;
            EnsureBundle();

            GameObject prefab = null;
            if (_bundle != null)
                try { prefab = _bundle.Load<GameObject>(name); }
                catch (System.Exception e) { MelonLogger.Warning($"[Models] '{name}' failed to load: {e.Message}"); }
            _prefabs[name] = prefab;
            return prefab;
        }

        private static readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();

        /// <summary>A texture in the bundle by name (an HE atlas), or null. Cached, misses too.</summary>
        internal static Texture2D Texture(string name)
        {
            if (_textures.TryGetValue(name, out var cached)) return cached;
            EnsureBundle();
            Texture2D tex = null;
            if (_bundle != null)
                try { tex = _bundle.Load<Texture2D>(name); }
                catch (System.Exception e) { MelonLogger.Warning($"[Models] texture '{name}' failed to load: {e.Message}"); }
            _textures[name] = tex;
            return tex;
        }

        /// <summary>
        /// The HE copy of a projectile's atlas on its (already copied) body material, so the
        /// stencil on it says what it carries. Painted by the model scripts next to the HEAT one.
        /// </summary>
        internal static void PaintWarhead(Ordnance o, GameObject obj, bool he)
        {
            if (!he || obj == null) return;
            var tex = Texture(PrefabName(o) + "_Albedo_HE");
            var body = obj.transform.Find("Body");
            var r = body != null ? body.GetComponent<Renderer>() : null;
            if (tex != null && r != null && r.sharedMaterial != null) r.sharedMaterial.SetTexture("_BaseMap", tex);
        }

        /// <summary>
        /// A live copy of the prefab with its own copies of its materials, so the
        /// fuse flash tints this charge only. Null if the bundle has no model for it.
        /// </summary>
        internal static GameObject Spawn(Ordnance o, Vector3 position, out Renderer mainRenderer, out Material[] materials)
        {
            mainRenderer = null;
            materials = new Material[0];
            if (Prefab(o) == null) return null;

            var obj = _bundle.Spawn(PrefabName(o), position, Quaternion.identity);
            if (obj == null) return null;

            // One copy per distinct material (the C4 has three: atlas, LED, screen).
            var renderers = obj.GetComponentsInChildren<Renderer>();
            var copies = new Dictionary<System.IntPtr, Material>();
            foreach (var r in renderers)
            {
                var src = r.sharedMaterial;
                if (src == null) continue;
                if (!copies.TryGetValue(src.Pointer, out var own)) copies[src.Pointer] = own = new Material(src);
                r.sharedMaterial = own;
            }
            materials = new Material[copies.Count];
            copies.Values.CopyTo(materials, 0);

            var body = obj.transform.Find("Body");
            mainRenderer = body != null ? body.GetComponent<Renderer>() : (renderers.Length > 0 ? renderers[0] : null);
            return obj;
        }

        /// <summary>
        /// A plain visual copy of a prefab's parts under <paramref name="root"/>: one child per
        /// part, same names, offsets and materials, no Rigidbody or collider.
        /// </summary>
        internal static void AddParts(GameObject prefab, Transform root, int layer, bool shadows)
        {
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var go = new GameObject(mf.name);
                go.layer = layer;
                go.transform.SetParent(root, false);
                go.transform.localPosition = prefab.transform.InverseTransformPoint(mf.transform.position);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                var src = mf.GetComponent<MeshRenderer>();
                if (src != null) mr.sharedMaterial = src.sharedMaterial;
                mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On
                                               : UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>Parts the hand lets go of before the charge leaves it (grenade: pin and spoon).</summary>
        internal static bool IsLoosePart(string name) => name == "Pin" || name == "Spoon";

        /// <summary>
        /// How high the pivot sits above a surface the model rests on flat: its lowest point
        /// (loose parts aside). <paramref name="fallback"/> without a bundled model.
        /// </summary>
        internal static float RestHeight(Ordnance o, float fallback)
        {
            var prefab = Prefab(o);
            return prefab != null ? -LocalBounds(prefab, skipLooseParts: true).min.y : fallback;
        }

        /// <summary>A live grenade is the body alone: its pin and spoon stayed with the hand.</summary>
        internal static void StripLooseParts(GameObject obj)
        {
            for (int i = obj.transform.childCount - 1; i >= 0; i--)
            {
                var t = obj.transform.GetChild(i);
                if (IsLoosePart(t.name)) Object.Destroy(t.gameObject);
            }
        }

        /// <summary>
        /// Every part's mesh bounds in root space. The parts are only offset, never rotated or
        /// scaled, so this is a union of translated boxes.
        /// </summary>
        internal static Bounds LocalBounds(GameObject root, bool skipLooseParts = false)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || (skipLooseParts && IsLoosePart(mf.name))) continue;
                var mb = mf.sharedMesh.bounds;
                mb.center += root.transform.InverseTransformPoint(mf.transform.position);
                if (!any) { b = mb; any = true; } else b.Encapsulate(mb);
            }
            return b;
        }
    }
}
