using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// What the 155 battery's smoke rounds leave where they land: a canister (the smoke
    /// grenade's model at 1.6x, lying on its side) venting a plume through the smoke grenade's
    /// own effect and SmokeCloud, harder than a grenade (SmokeShellRate), for SmokeShellBurnTime.
    /// A real M116 throws its canisters out above the target; here the round brings its
    /// canister down where it lands. The spent canister stays SmokeCanisterLife, as a grenade's.
    /// </summary>
    internal static class SmokeShells
    {
        private const float Scale = 1.6f;

        private sealed class Canister
        {
            public GameObject Root;
            public SmokeCloud Cloud;
            public AudioSource Hiss;
            public float BurnUntil, GoneAt = -1f;
        }

        private static readonly List<Canister> _live = new List<Canister>();

        /// <summary>A smoke round down at <paramref name="at"/> on a surface facing <paramref name="normal"/>.</summary>
        public static void Land(Vector3 at, Vector3 normal)
        {
            var prefab = OrdnanceModels.Prefab(Ordnance.Smoke);
            var fxPrefab = OrdnanceModels.Asset("FX_SmokeGrenade");
            if (prefab == null || fxPrefab == null) { if (Config.Dbg1) MelonLogger.Msg("[Arty] no smoke can or FX_SmokeGrenade in the bundle"); return; }

            // The can lies on its side: its long axis (local Y) along the ground, a random way round.
            var b = OrdnanceModels.LocalBounds(prefab, skipLooseParts: true);
            float radius = Mathf.Max(b.extents.x, b.extents.z) * Scale;
            var root = new GameObject("BA_SmokeCanister");
            root.transform.SetPositionAndRotation(
                at + normal * radius,
                Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 90f));
            root.transform.localScale = Vector3.one * Scale;
            OrdnanceModels.AddParts(prefab, root.transform, 0, true);
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var t = root.transform.GetChild(i);
                if (OrdnanceModels.IsLoosePart(t.name)) Object.Destroy(t.gameObject);
            }

            // Out of its top end, as the grenade vents out of its lid.
            var fx = Object.Instantiate(fxPrefab);
            fx.transform.SetParent(root.transform, false);
            fx.transform.localPosition = new Vector3(0f, b.max.y * 0.8f, 0f);
            fx.transform.localRotation = Quaternion.identity;
            fx.transform.localScale = Vector3.one / Scale;   // the jet and the volume at their own size

            var s = SmokePlume.Settings.Defaults;
            s.Rate = Mathf.Max(1f, Config.SmokeShellRate);
            s.Spray = 2.2f;
            s.StartRadius = 0.75f;
            var volume = fx.transform.Find("Volume");
            var c = new Canister
            {
                Root = root,
                Cloud = SmokeCloud.Start(volume != null ? volume.gameObject : null, fx.transform, Config.SmokeShellBurnTime, Color.white, s),
                BurnUntil = Time.time + Mathf.Max(1f, Config.SmokeShellBurnTime),
            };

            float delay = Heard(at);
            Sfx.Play("SmokeIgnite", at, null, 1f, delay);
            c.Hiss = Sfx.Play("SmokeHissLoop", root.transform.position, root.transform);
            _live.Add(c);
        }

        public static void Tick()
        {
            float now = Time.time;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var c = _live[i];
                if (c.Root == null) { _live.RemoveAt(i); continue; }
                if (c.GoneAt < 0f)
                {
                    if (now < c.BurnUntil) continue;
                    c.Cloud?.StopFeeding();
                    if (c.Hiss != null) { Object.Destroy(c.Hiss.gameObject); c.Hiss = null; }
                    c.GoneAt = now + Config.SmokeCanisterLife;
                    continue;
                }
                if (now < c.GoneAt) continue;
                Object.Destroy(c.Root);
                _live.RemoveAt(i);
            }
        }

        public static void Clear()
        {
            foreach (var c in _live) if (c.Root != null) Object.Destroy(c.Root);
            _live.Clear();
        }

        /// <summary>Seconds a sound from <paramref name="at"/> takes to reach the camera.</summary>
        internal static float Heard(Vector3 at)
        {
            var cam = Camera.main;
            return cam != null ? Vector3.Distance(cam.transform.position, at) / 343f : 0f;
        }
    }

    /// <summary>
    /// The illumination rounds' flares: each opens IllumBurstHeight over the ground, then burns
    /// for IllumBurnTime under a small parachute, sinking at IllumFallSpeed, carried by the
    /// breeze (stronger up there: IllumWindScale), swinging gently, trailing smoke. Its light
    /// flickers, comes up as the candle catches and dies away in its last seconds. One that
    /// comes down before it burns out lies burning where it lands.
    ///
    /// The light's range and type aren't in the game's own code (stripped); MelonLoader's
    /// wrappers reach them through their icalls. If that fails, the light keeps Unity's
    /// defaults (a 10 m point light) and the flare still burns, smokes and hisses.
    /// </summary>
    internal static class IllumFlares
    {
        private const float Hang = 1.9f;          // metres from the canopy down to the candle
        private const float Catch = 0.6f;         // seconds for the candle to catch
        private const float DieAway = 4f;         // seconds the light dies over at the end
        private static readonly Color Warm = new Color(1f, 0.9f, 0.74f);

        private sealed class Flare
        {
            public GameObject Root;               // the canopy; Swing hangs under it
            public Transform Swing, Candle, Glow;
            public Light Lamp;
            public TrailRenderer Trail;
            public AudioSource Hiss;
            public float Born, BurnUntil, Phase, NextProbe;
            public Vector3 SwingAxis;
            public bool Landed;
        }

        private static readonly List<Flare> _live = new List<Flare>();
        private static Material _candleMat, _glowMat, _canopyMat, _trailMat;
        private static Texture2D _glowTex;
        private static Mesh _canopy;

        /// <summary>A flare opens at <paramref name="at"/>.</summary>
        public static void Open(Vector3 at)
        {
            EnsureAssets();
            var f = new Flare { Born = Time.time, BurnUntil = Time.time + Mathf.Max(5f, Config.IllumBurnTime), Phase = UnityEngine.Random.Range(0f, 10f) };
            float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            f.SwingAxis = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

            f.Root = new GameObject("BA_IllumFlare");
            f.Root.layer = 2;
            f.Root.transform.position = at + Vector3.up * Hang;

            var canopy = new GameObject("Canopy");
            canopy.layer = 2;
            canopy.transform.SetParent(f.Root.transform, false);
            canopy.AddComponent<MeshFilter>().sharedMesh = _canopy;
            canopy.AddComponent<MeshRenderer>().sharedMaterial = _canopyMat;

            f.Swing = new GameObject("Swing").transform;
            f.Swing.gameObject.layer = 2;
            f.Swing.SetParent(f.Root.transform, false);

            f.Candle = Primitive(PrimitiveType.Cube, "Candle", f.Swing, _candleMat).transform;
            f.Candle.localPosition = new Vector3(0f, -Hang, 0f);
            f.Candle.localScale = new Vector3(0.13f, 0.32f, 0.13f);

            f.Glow = Primitive(PrimitiveType.Quad, "Glow", f.Swing, _glowMat).transform;
            f.Glow.localPosition = new Vector3(0f, -Hang - 0.1f, 0f);
            f.Glow.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var lampGo = new GameObject("Lamp");
            lampGo.transform.SetParent(f.Swing, false);
            lampGo.transform.localPosition = new Vector3(0f, -Hang - 0.4f, 0f);
            f.Lamp = lampGo.AddComponent<Light>();
            LightNative.MakePoint(f.Lamp, Mathf.Max(5f, Config.IllumLightRange));
            f.Lamp.color = Warm;
            f.Lamp.intensity = 0f;

            f.Trail = f.Candle.gameObject.AddComponent<TrailRenderer>();
            if (_trailMat != null) f.Trail.sharedMaterial = _trailMat;
            f.Trail.time = 7f;
            f.Trail.startWidth = 0.3f;
            f.Trail.endWidth = 1.8f;
            f.Trail.startColor = new Color(0.86f, 0.86f, 0.84f, 0.5f);
            f.Trail.endColor = new Color(0.8f, 0.8f, 0.78f, 0f);
            f.Trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // The pop of the round opening, late by the distance; then the candle's hiss.
            var pop = Sfx.Play("ArtyGuns", at, null, 0.6f, SmokeShells.Heard(at));
            if (pop != null) pop.pitch *= 2.2f;
            f.Hiss = Sfx.Play("SmokeHissLoop", f.Candle.position, f.Candle);
            if (f.Hiss != null) f.Hiss.pitch *= 1.35f;

            _live.Add(f);
        }

        public static void Tick()
        {
            if (_live.Count == 0) return;
            float now = Time.time, dt = Time.deltaTime;
            var cam = Camera.main;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var f = _live[i];
                if (f.Root == null) { _live.RemoveAt(i); continue; }
                try
                {
                    if (!TickFlare(f, now, dt, cam)) { Drop(f); _live.RemoveAt(i); }
                }
                catch (Exception e)
                {
                    MelonLogger.Warning($"[Illum] flare failed: {e.Message}");
                    Drop(f);
                    _live.RemoveAt(i);
                }
            }
        }

        private static bool TickFlare(Flare f, float now, float dt, Camera cam)
        {
            float age = now - f.Born, left = f.BurnUntil - now;
            if (left < -2f) return false;   // burnt out, the last of the trail drawn

            // Down under the canopy, with the wind; until it lands.
            if (!f.Landed)
            {
                Vector3 v = Vector3.down * Mathf.Max(0.1f, Config.IllumFallSpeed) + Breeze.Velocity * Mathf.Max(0f, Config.IllumWindScale);
                f.Root.transform.position += v * dt;
                if (now >= f.NextProbe)
                {
                    f.NextProbe = now + 0.1f;
                    if (Ground(f.Candle.position, Mathf.Max(0.5f, v.magnitude * 0.15f)))
                    {
                        f.Landed = true;
                        f.Swing.localRotation = Quaternion.identity;
                    }
                }
                if (!f.Landed)
                {
                    float swing = 9f * Mathf.Sin(age * 1.15f + f.Phase) + 3f * Mathf.Sin(age * 2.7f + f.Phase * 1.7f);
                    f.Swing.localRotation = Quaternion.AngleAxis(swing, f.SwingAxis);
                }
            }

            // The light: catching, flickering, dying away.
            float burn = left <= 0f ? 0f : Mathf.Clamp01(age / Catch) * Mathf.Clamp01(left / DieAway);
            float flicker = 0.82f + 0.18f * Mathf.PerlinNoise(age * 9f, f.Phase);
            // Down on the ground the candle is half buried and the light stays near it.
            if (f.Lamp != null) f.Lamp.intensity = Mathf.Max(0f, Config.IllumLightIntensity) * burn * flicker * (f.Landed ? 0.02f : 1f);
            if (f.Glow != null)
            {
                float size = 2.4f * burn * (0.9f + 0.1f * flicker);
                f.Glow.localScale = new Vector3(size, size, size);
                if (cam != null) f.Glow.rotation = Quaternion.LookRotation(f.Glow.position - cam.transform.position, Vector3.up);
            }
            if (left <= 0f)
            {
                if (f.Trail != null) f.Trail.emitting = false;
                if (f.Hiss != null) { Object.Destroy(f.Hiss.gameObject); f.Hiss = null; }
                if (f.Candle != null) f.Candle.gameObject.GetComponent<Renderer>().enabled = false;
            }
            return true;
        }

        private static bool Ground(Vector3 from, float reach)
        {
            int mask = Config.WorldLayerMask & ~(1 << 2);
            var hits = Physics.RaycastAll(from + Vector3.up * 0.1f, Vector3.down, reach + 0.1f, mask, QueryTriggerInteraction.Ignore);
            foreach (var h in hits) if (h.collider != null) return true;
            return false;
        }

        private static void Drop(Flare f)
        {
            if (f.Hiss != null) Object.Destroy(f.Hiss.gameObject);
            if (f.Root != null) Object.Destroy(f.Root);
        }

        public static void Clear()
        {
            foreach (var f in _live) Drop(f);
            _live.Clear();
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = 2;
            var col = go.GetComponent<Collider>();
            if (col != null) { col.enabled = false; Object.Destroy(col); }
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
            return go;
        }

        // ── Materials and meshes, made once ─────────────────────────────────────

        private static void EnsureAssets()
        {
            if (_candleMat != null) return;
            var sprite = Config.FindSpriteShader();
            var lit = Config.FindShader();

            _candleMat = new Material(sprite) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            _candleMat.color = new Color(1f, 0.97f, 0.9f, 1f);
            _trailMat = new Material(sprite) { hideFlags = HideFlags.DontUnloadUnusedAsset };

            // The glow: chunky rings, point-filtered like the mod's screens.
            const int N = 16;
            _glowTex = new Texture2D(N, N, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - N * 0.5f, dy = y + 0.5f - N * 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * N + x] = r < 2.2f ? new Color32(255, 255, 250, 255)
                                  : r < 4.4f ? new Color32(255, 240, 200, 200)
                                  : r < 6.6f ? new Color32(255, 215, 150, 90)
                                  : r < 8f && ((x + y) & 1) == 0 ? new Color32(255, 200, 130, 40)
                                  : new Color32(0, 0, 0, 0);
                }
            _glowTex.SetPixels32(px);
            _glowTex.Apply(false);
            _glowMat = new Material(sprite) { hideFlags = HideFlags.DontUnloadUnusedAsset, mainTexture = _glowTex };

            _canopyMat = new Material(lit) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            _canopyMat.color = new Color(0.32f, 0.33f, 0.29f);   // the palette's olive cast, dark

            _canopy = Canopy(0.95f, 0.5f, 8);
        }

        /// <summary>A flat-shaded octagonal dome (a cone), both faces, opening downward.</summary>
        private static Mesh Canopy(float radius, float height, int sides)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var top = new Vector3(0f, height, 0f);
                var p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                var p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                int k = v.Count;
                v.Add(top); v.Add(p1); v.Add(p0);       // outside
                v.Add(top); v.Add(p0); v.Add(p1);       // inside
                t.Add(k); t.Add(k + 1); t.Add(k + 2);
                t.Add(k + 3); t.Add(k + 4); t.Add(k + 5);
            }
            var m = new Mesh { hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.SetVertices(v.ToArray());
            m.SetTriangles(t.ToArray(), 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>Light setters the game's code stripped; the interop wrappers call their icalls.</summary>
    internal static class LightNative
    {
        private static bool _failed;

        /// <summary>A point light reaching <paramref name="range"/> metres, if the engine allows it.</summary>
        public static void MakePoint(Light light, float range)
        {
            if (_failed || light == null) return;
            try
            {
                light.type = LightType.Point;
                light.range = range;
            }
            catch (Exception e)
            {
                _failed = true;
                MelonLogger.Warning($"[Illum] can't set the flares' light range ({e.Message}); they light 10 m round them");
            }
        }
    }
}
