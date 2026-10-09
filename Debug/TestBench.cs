using FruitLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Color = UnityEngine.Color;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// A row of walls to shoot at: TestBenchKey (Num 1) puts TestBenchWalls walls of
    /// TestBenchMaterial across your view, TestBenchThickness thick with TestBenchGap between
    /// them, the first TestBenchDistance ahead and standing on whatever is below. Pressing it
    /// again replaces the row; with Shift it clears it. For checking penetration with the
    /// debug draw on.
    ///
    /// FruitLib has no surface types of its own, so the material is picked the way it picks
    /// one for any map object: by keyword in the object's name. The log line says what the
    /// first wall actually resolved to.
    /// </summary>
    internal static class TestBench
    {
        private const string Tag = "[TestBench]";
        private const float Width = 3f, Height = 2.5f;

        private static GameObject _root;
        private static readonly System.Collections.Generic.List<Material> _mats = new System.Collections.Generic.List<Material>();

        public static void Tick(bool inputLive)
        {
            if (!inputLive || Config.TestBenchKey == KeyCode.None || !Input.GetKeyDown(Config.TestBenchKey)) return;

            try
            {
                Clear();
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    MelonLogger.Msg($"{Tag} cleared");
                    return;
                }
                Build();
            }
            catch (System.Exception e) { MelonLogger.Warning($"{Tag} failed: {e.Message}"); }
        }

        /// <summary>A scene reload already took the walls; their materials outlive them.</summary>
        internal static void OnScene() => Clear();

        private static void Clear()
        {
            if (_root != null) GameObject.Destroy(_root);
            _root = null;
            foreach (var mat in _mats)
                if (mat != null) GameObject.Destroy(mat);
            _mats.Clear();
        }

        private static void Build()
        {
            var cam = Camera.main;
            if (cam == null) return;

            // Across the view, upright, whatever the pitch of the camera.
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(cam.transform.up, Vector3.up);
            fwd.Normalize();

            int count = Mathf.Clamp(Config.TestBenchWalls, 1, 50);
            float thick = Mathf.Max(0.005f, Config.TestBenchThickness);
            float gap = Mathf.Max(0f, Config.TestBenchGap);
            string material = string.IsNullOrEmpty(Config.TestBenchMaterial) ? "Concrete" : Config.TestBenchMaterial;

            Vector3 start = cam.transform.position + fwd * Mathf.Max(0.5f, Config.TestBenchDistance);
            float floor = Physics.Raycast(start + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 20f, ~(1 << 2), QueryTriggerInteraction.Ignore)
                ? ground.point.y
                : cam.transform.position.y - 1.6f;

            _root = new GameObject("BA_TestBench");
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            var shader = Config.FindShader();

            Collider first = null;
            for (int i = 0; i < count; i++)
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                // The keyword is what makes it this material (see FruitSurfaces).
                wall.name = $"TestWall_{material}_{i + 1}";
                wall.transform.SetParent(_root.transform, false);
                wall.transform.rotation = rot;
                wall.transform.localScale = new Vector3(Width, Height, thick);
                wall.transform.position = start + fwd * (i * (thick + gap) + thick * 0.5f) + Vector3.up * (floor + Height * 0.5f - start.y);

                var rend = wall.GetComponent<Renderer>();
                if (rend != null && shader != null)
                {
                    // Alternate shades, so each wall reads as its own slab.
                    var c = i % 2 == 0 ? new Color(0.62f, 0.62f, 0.6f, 1f) : new Color(0.45f, 0.45f, 0.44f, 1f);
                    var mat = new Material(shader) { color = c };
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                    _mats.Add(mat);
                    rend.material = mat;
                    rend.shadowCastingMode = ShadowCastingMode.Off;
                }
                if (first == null) first = wall.GetComponent<Collider>();
            }

            string why = null;
            var resolved = first != null ? FruitSurfaces.Explain(first, out why) : null;
            MelonLogger.Msg($"{Tag} {count} walls, {thick * 1000f:0} mm thick, {gap * 1000f:0} mm apart " +
                            $"({count * thick:0.00} m of wall over {count * thick + (count - 1) * gap:0.00} m), first {Config.TestBenchDistance:0.0} m ahead; " +
                            $"resolves to {resolved?.Name ?? "?"} ({why ?? "no collider"})");
        }
    }
}
