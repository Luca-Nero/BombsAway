using Il2CppInterop.Runtime;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;
using Color = UnityEngine.Color;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    internal static class VfxRunner
    {
        /// <summary>
        /// A puff thrown out fast and stopped by the air (the AT-4's backblast): velocity
        /// decays, it swells quickly and then slowly, rises a little, and thins out.
        /// </summary>
        internal class Item
        {
            public GameObject Go;
            public Renderer Rend;
            public Color BaseColor;
            public float Elapsed;
            public float Duration;
            public float Delay;
            public float BaseScale;
            public float RiseSpeed;
            public Vector3 Velocity;
            public float Drag;         // velocity lost per second, exponential
            public float Grow;         // how many times its size it swells by
        }

        private static readonly List<Item> _items = new List<Item>();
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");
        private static readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        public static int ActiveCount => _items.Count;

        public static void Add(Item item)
        {
            ApplyColor(item.Rend, item.BaseColor);
            _items.Add(item);
        }

        public static void Tick(float dt)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var it = _items[i];
                if (it.Go == null) { _items.RemoveAt(i); continue; }

                if (it.Delay > 0f)
                {
                    it.Delay -= dt;
                    if (it.Delay > 0f) continue;
                }

                if (!TickPlume(it, dt))
                {
                    Kill(it);
                    _items.RemoveAt(i);
                }
            }
        }

        private static bool TickPlume(Item it, float dt)
        {
            it.Elapsed += dt;
            float t = it.Elapsed / it.Duration;
            if (t >= 1f) return false;
            it.Velocity *= Mathf.Exp(-it.Drag * dt);
            it.Go.transform.position += (it.Velocity + Vector3.up * it.RiseSpeed) * dt;
            it.Go.transform.localScale = Vector3.one * it.BaseScale * (1f + it.Grow * (1f - Mathf.Exp(-4f * t)));
            float a = it.BaseColor.a * (t < 0.06f ? t / 0.06f : Mathf.Pow(1f - (t - 0.06f) / 0.94f, 1.4f));
            ApplyColor(it.Rend, WithAlpha(it.BaseColor, a));
            ExplosionVFX.BillboardToCamera(it.Go);
            return true;
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static void ApplyColor(Renderer rend, Color c)
        {
            _mpb.SetColor(ColorPropId, c);
            rend.SetPropertyBlock(_mpb);
            rend.enabled = c.a > 0.01f;
        }

        private static void Kill(Item it)
        {
            if (it.Go == null) return;
            if (it.Rend != null) it.Rend.enabled = false;
            GameObject.Destroy(it.Go);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Explosion VFX
    // ══════════════════════════════════════════════════════════════════════════════
    internal static class ExplosionVFX
    {
        private static readonly Dictionary<string, Texture2D> _texCache
            = new Dictionary<string, Texture2D>();
        private static bool _texScanDone = false;
        private static float _texRescanTime;   // earliest moment a missing/destroyed texture may trigger a rescan
        private static readonly HashSet<string> _texWarned = new HashSet<string>();
        private static void EnsureTextures()
        {
            if (_texScanDone) return;
            _texScanDone = true;
            _texRescanTime = Time.unscaledTime + 5f;
            var all = Resources.FindObjectsOfTypeAll<Texture2D>();
            foreach (var t in all)
            {
                if (t == null || string.IsNullOrEmpty(t.name)) continue;
                // Keep a live entry; replace one that has been destroyed since it was cached.
                if (!_texCache.TryGetValue(t.name, out var old) || old == null)
                    _texCache[t.name] = t;
            }
            if (Config.Dbg2) MelonLogger.Msg($"[VFX] Texture scan: {_texCache.Count} cached");
        }

        private static Texture2D Tex(string name)
        {
            EnsureTextures();
            _texCache.TryGetValue(name, out var t);
            if (t == null && Time.unscaledTime >= _texRescanTime)
            {
                // Missing, or destroyed with a scene: rescan, at most once per 5 s.
                _texScanDone = false;
                EnsureTextures();
                _texCache.TryGetValue(name, out t);
            }
            if (t == null && _texWarned.Add(name)) MelonLogger.Warning($"[VFX] Tex '{name}' NOT FOUND in cache");
            return t;
        }

        private static Material _spriteMat;
        private static Material GetSpriteMat()
        {
            if (_spriteMat != null) return _spriteMat;
            var shader = Config.FindSpriteShader();
            if (shader == null) { MelonLogger.Warning("[VFX] Sprites/Default MISSING"); return null; }
            _spriteMat = new Material(shader);
            if (Config.Dbg2) MelonLogger.Msg($"[VFX] SpriteMat ready shader='{shader.name}'");
            return _spriteMat;
        }

        private static readonly Dictionary<string, Material> _matCache = new Dictionary<string, Material>();
        private static Material GetCachedMat(string texName)
        {
            if (_matCache.TryGetValue(texName, out var cached) && cached != null)
            {
                if (cached.mainTexture == null)
                {
                    var retryTex = Tex(texName);
                    if (retryTex != null) cached.mainTexture = retryTex;
                }
                return cached;
            }

            var base_ = GetSpriteMat();
            if (base_ == null) { MelonLogger.Warning($"[VFX] GetCachedMat '{texName}': base null"); return null; }
            var m = new Material(base_);
            var tex = Tex(texName);
            if (tex != null) { m.mainTexture = tex; if (Config.Dbg2) MelonLogger.Msg($"[VFX] GetCachedMat '{texName}' OK {tex.width}x{tex.height}"); }
            else { MelonLogger.Warning($"[VFX] GetCachedMat '{texName}': NO TEXTURE — flat colour only"); }
            _matCache[texName] = m;
            return m;
        }

        // ── Ballistic debris — 3D mesh chunks with dark smoke trails ────────────────
        // Chunks are pooled and share two materials; per-chunk grey and fade go through a
        // property block. Pooled objects die with the scene (see ResetForScene).
        private sealed class DebrisChunk
        {
            public GameObject Go;
            public Renderer Rend;
            public Rigidbody Body;
            public Collider Col;
            public TrailRenderer Trail;
        }

        private const int DebrisPoolMax = 64;
        private static readonly Stack<DebrisChunk> _debrisPool = new Stack<DebrisChunk>();
        private static readonly MaterialPropertyBlock _debrisMpb = new MaterialPropertyBlock();
        private static readonly int DebrisColorId = Shader.PropertyToID("_Color");
        private static Material _debrisMat;
        private static Material _debrisTrailMat;
        private static bool _debrisShaderWarned;

        private static bool EnsureDebrisMats()
        {
            if (_debrisMat != null && _debrisTrailMat != null) return true;
            var shader = Config.FindSpriteShader();
            if (shader == null)
            {
                if (!_debrisShaderWarned)
                {
                    _debrisShaderWarned = true;
                    MelonLogger.Warning("[VFX] Sprites/Default MISSING — debris chunks skipped");
                }
                return false;
            }
            if (_debrisMat == null) _debrisMat = new Material(shader);
            if (_debrisTrailMat == null)
            {
                _debrisTrailMat = new Material(shader);
                _debrisTrailMat.color = new Color(0.15f, 0.12f, 0.1f, 0.5f);
            }
            return true;
        }

        private static DebrisChunk RentDebris()
        {
            while (_debrisPool.Count > 0)
            {
                var pooled = _debrisPool.Pop();
                if (pooled.Go != null) return pooled;   // else it died with a scene: discard
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "VFX_FragChunk";
            var chunk = new DebrisChunk { Go = go };
            chunk.Rend = go.GetComponent<Renderer>();
            chunk.Rend.sharedMaterial = _debrisMat;

            // The primitive's own unit BoxCollider stays off (and the body kinematic) during flight.
            chunk.Col = go.GetComponent<Collider>();
            chunk.Col.enabled = false;
            chunk.Body = go.AddComponent<Rigidbody>();
            chunk.Body.mass = 0.05f;
            chunk.Body.isKinematic = true;

            var trailObj = new GameObject("DebrisTrail");
            trailObj.transform.SetParent(go.transform, false);
            var trail = trailObj.AddComponent<TrailRenderer>();
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.05f;
            trail.sharedMaterial = _debrisTrailMat;
            trail.startColor = new Color(0.2f, 0.15f, 0.1f, 0.6f);
            trail.endColor = new Color(0.3f, 0.25f, 0.2f, 0f);
            chunk.Trail = trail;
            return chunk;
        }

        private static void ReleaseDebris(DebrisChunk chunk)
        {
            if (chunk.Go == null) return;
            if (_debrisPool.Count >= DebrisPoolMax) { GameObject.Destroy(chunk.Go); return; }

            // Velocities can only be cleared while the body is still dynamic.
            chunk.Body.linearVelocity = Vector3.zero;
            chunk.Body.angularVelocity = Vector3.zero;
            chunk.Body.isKinematic = true;
            chunk.Col.enabled = false;
            chunk.Go.layer = 0;
            chunk.Go.SetActive(false);
            _debrisPool.Push(chunk);
        }

        // Call on scene load: pooled chunks and cached textures may have died with the old scene.
        internal static void ResetForScene()
        {
            foreach (var c in _debrisPool)
                if (c.Go != null) GameObject.Destroy(c.Go);
            _debrisPool.Clear();
            // Rebuilt on the next blast, on whatever textures the new scene finds.
            foreach (var m in _matCache.Values) if (m != null) GameObject.Destroy(m);
            _matCache.Clear();
            _texCache.Clear();
            _texWarned.Clear();
            _texScanDone = false;
            _texRescanTime = 0f;
        }

        public static void SpawnDebrisArc(Vector3 p0, Vector3 vel, float gy, float flightTime)
        {
            if (!Config.VFXActive) return;
            if (!EnsureDebrisMats()) return;
            MelonCoroutines.Start(AnimateDebrisChunk(p0, vel, gy, flightTime));
        }

        private static System.Collections.IEnumerator AnimateDebrisChunk(
            Vector3 p0, Vector3 vel, float gy, float flightTime)
        {
            var chunk = RentDebris();
            var go = chunk.Go;
            var tf = go.transform;

            float baseScale = Config.DebrisMeshScale
                * (0.6f + UnityEngine.Random.value * 0.8f);
            go.layer = 0;
            tf.position = p0;
            tf.rotation = Quaternion.identity;
            tf.localScale = new Vector3(
                baseScale * UnityEngine.Random.Range(0.5f, 1.5f),
                baseScale * UnityEngine.Random.Range(0.5f, 1.5f),
                baseScale * UnityEngine.Random.Range(0.7f, 2f));

            // Dark tint
            float grey = UnityEngine.Random.Range(0.08f, 0.2f);
            var tint = new Color(grey, grey * 0.9f, grey * 0.7f, 1f);
            _debrisMpb.SetColor(DebrisColorId, tint);
            chunk.Rend.SetPropertyBlock(_debrisMpb);

            // Dark smoke trail
            chunk.Trail.time = Config.DebrisTrailTime;
            chunk.Trail.startWidth = baseScale * 2f;

            go.SetActive(true);
            chunk.Trail.Clear();

            Vector3 spinAxis = UnityEngine.Random.onUnitSphere;
            float spinRate = UnityEngine.Random.Range(200f, 600f);

            float elapsed = 0f;
            while (elapsed < flightTime && go != null)
            {
                float dt = Time.deltaTime;
                elapsed += dt;

                Vector3 pos = p0 + vel * elapsed
                    + new Vector3(0f, 0.5f * gy * elapsed * elapsed, 0f);
                tf.position = pos;
                tf.Rotate(spinAxis, spinRate * dt);
                yield return null;
            }

            if (go == null) yield break;

            Vector3 impactVel = vel + new Vector3(0f, gy * flightTime, 0f);
            chunk.Body.isKinematic = false;
            chunk.Body.linearVelocity = impactVel * 0.3f; // damped bounce
            chunk.Body.angularVelocity = spinAxis * spinRate * Mathf.Deg2Rad * 0.2f;
            chunk.Col.enabled = true;
            // Ignore Raycast, as FruitLib's ejecta: a landed chunk must not stop the next
            // explosion's fragments (they skip layer 2), or catch rounds.
            go.layer = 2;

            float settleTime = Config.DebrisLifetime;
            float settleElapsed = 0f;
            float fadeStart = settleTime * 0.7f;
            while (settleElapsed < settleTime && go != null)
            {
                settleElapsed += Time.deltaTime;
                if (settleElapsed > fadeStart)
                {
                    float fadeT = (settleElapsed - fadeStart) / (settleTime - fadeStart);
                    float a = Mathf.Lerp(1f, 0f, fadeT);
                    _debrisMpb.SetColor(DebrisColorId, new Color(tint.r, tint.g, tint.b, a));
                    chunk.Rend.SetPropertyBlock(_debrisMpb);
                }
                yield return null;
            }

            ReleaseDebris(chunk);
        }

        // ── AT-4 backblast ────────────────────────────────────────────────────────
        // Out of the venturi, away from the muzzle: a jet of flame tongues, a cloud of smoke
        // and dust thrown back in a cone, and some of it spilling round the shooter into view
        // (the rest is behind the eye). At the front a small flash and the puff the rocket
        // leaves. The venturi lights the surroundings for a moment.

        public static void SpawnBackblast(Vector3 breech, Vector3 back, Vector3 muzzle)
        {
            if (!Config.VFXActive) return;
            var flameA = GetCachedMat("MuzzleFlash1");
            var flameB = GetCachedMat("MuzzleFlash3");
            var smoke = GetCachedMat("WFX_T_SmokeLoopAlpha");
            Vector3 side = Vector3.Cross(back, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();

            if (flameA != null && flameB != null)
                for (int i = 0; i < 6; i++)
                    AddPlume("VFX_Backblast_Jet", breech + back * 0.1f, Cone(back, 8f) * Rand(16f, 32f),
                             i % 2 == 0 ? flameA : flameB, Config.VFX(Rand(0.35f, 0.6f)), new Color(1f, 0.62f, 0.22f, 0.95f),
                             Rand(0.1f, 0.18f), drag: 14f, grow: 2.5f, rise: 0f, delay: i * 0.01f);

            if (smoke != null)
            {
                int n = Mathf.Max(1, Config.VFXInt(14));
                for (int i = 0; i < n; i++)
                {
                    float g = Rand(0.55f, 0.72f);
                    AddPlume("VFX_Backblast_Cloud", breech + back * 0.2f, Cone(back, 35f) * Rand(6f, 22f),
                             smoke, Config.VFX(Rand(0.5f, 0.9f)), new Color(g, g * 0.98f, g * 0.94f, 0.8f),
                             Config.VFX(Rand(1.4f, 2.4f)), drag: 3.5f, grow: 3.5f, rise: 0.35f, delay: Rand(0f, 0.05f));
                }
                int spill = Mathf.Max(1, Config.VFXInt(6));
                for (int i = 0; i < spill; i++)
                {
                    float s = i % 2 == 0 ? 1f : -1f;
                    Vector3 dir = (side * s + back * Rand(-0.1f, 0.4f) - back * Rand(0f, 0.5f) + Vector3.up * Rand(0f, 0.3f)).normalized;
                    float g = Rand(0.6f, 0.75f);
                    AddPlume("VFX_Backblast_Spill", breech + side * s * 0.3f, dir * Rand(3f, 6.5f),
                             smoke, Config.VFX(Rand(0.4f, 0.7f)), new Color(g, g, g, 0.55f),
                             Config.VFX(Rand(1.2f, 2f)), drag: 2.5f, grow: 3f, rise: 0.25f, delay: Rand(0.02f, 0.08f));
                }
                Vector3 fwd = -back;
                for (int i = 0; i < 3; i++)
                    AddPlume("VFX_Muzzle_Puff", muzzle + fwd * 0.1f, Cone(fwd, 20f) * Rand(1.5f, 4f),
                             smoke, Config.VFX(Rand(0.22f, 0.38f)), new Color(0.72f, 0.72f, 0.72f, 0.6f),
                             Config.VFX(Rand(0.8f, 1.4f)), drag: 3f, grow: 2.5f, rise: 0.2f, delay: 0.02f);
            }
            if (flameA != null)
                AddPlume("VFX_Muzzle_Flash", muzzle + (-back) * 0.08f, -back * 3f, flameA, Config.VFX(0.3f),
                         new Color(1f, 0.7f, 0.3f, 0.9f), 0.06f, drag: 10f, grow: 1.5f, rise: 0f, delay: 0f);

            MelonCoroutines.Start(Flash(breech + back * 0.5f, 0.09f));
        }

        private static float Rand(float a, float b) => UnityEngine.Random.Range(a, b);

        /// <summary>A direction within <paramref name="deg"/> of <paramref name="axis"/>.</summary>
        private static Vector3 Cone(Vector3 axis, float deg) =>
            Quaternion.AngleAxis(Rand(-deg, deg), Vector3.Cross(axis, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(axis, Vector3.up) : Vector3.right)
            * Quaternion.AngleAxis(Rand(-deg, deg), Vector3.up) * axis;

        private static void AddPlume(string name, Vector3 pos, Vector3 velocity, Material mat, float scale, Color color,
                                     float duration, float drag, float grow, float rise, float delay)
        {
            var go = MakeQuad(name, pos, Quaternion.identity, scale, mat);
            VfxRunner.Add(new VfxRunner.Item
            {
                Go = go, Rend = go.GetComponent<Renderer>(), BaseColor = color,
                Duration = duration, Delay = delay, BaseScale = scale, RiseSpeed = rise,
                Velocity = velocity, Drag = drag, Grow = grow,
            });
        }

        /// <summary>The venturi's light on everything round it, gone in a blink.</summary>
        private static System.Collections.IEnumerator Flash(Vector3 at, float time)
        {
            var go = new GameObject("VFX_Backblast_Light");
            go.transform.position = at;
            var light = go.AddComponent(Il2CppType.Of<Light>()).TryCast<Light>();
            light.type = LightType.Point;
            light.range = 9f;
            light.color = new Color(1f, 0.62f, 0.28f);
            float peak = Config.VFX(7f);
            for (float t = 0f; t < time && light != null; t += Time.deltaTime)
            {
                float k = 1f - t / time;
                light.intensity = peak * k * k;
                yield return null;
            }
            if (go != null) GameObject.Destroy(go);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static GameObject MakeQuad(string name, Vector3 pos,
                                            Quaternion rot, float scale, Material mat)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            var col = quad.GetComponent<Collider>();
            if (col != null) GameObject.Destroy(col);
            quad.transform.position = pos;
            quad.transform.rotation = rot;
            quad.transform.localScale = Vector3.one * scale;
            quad.GetComponent<Renderer>().material = mat;
            return quad;
        }

        internal static void BillboardToCamera(GameObject quad)
        {
            if (quad == null) return;
            var cam = CameraCache.Main;
            if (cam != null) quad.transform.LookAt(cam.transform.position);
        }
    }
}
