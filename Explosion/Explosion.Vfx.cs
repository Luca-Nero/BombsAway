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
        internal enum Kind { Fireball, Smoke, Debris, Fade }

        internal class Item
        {
            public Kind Kind;
            public GameObject Go;
            public Renderer Rend;
            public Color BaseColor;
            public float Elapsed;
            public float Duration;
            public float Delay;
            public float BaseScale;
            public float RiseSpeed;
            public float SpinRate;
            public Vector3 Velocity;
            public float Gravity;
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

                bool alive = it.Kind switch
                {
                    Kind.Fireball => TickFireball(it, dt),
                    Kind.Smoke => TickSmoke(it, dt),
                    Kind.Debris => TickDebris(it, dt),
                    Kind.Fade => TickFade(it, dt),
                    _ => false,
                };

                if (!alive)
                {
                    Kill(it);
                    _items.RemoveAt(i);
                }
            }
        }

        private static bool TickFireball(Item it, float dt)
        {
            it.Elapsed += dt;
            float t = it.Elapsed / it.Duration;
            it.Go.transform.localScale = Vector3.one * it.BaseScale * (1f + t * 0.6f);
            it.Go.transform.position += Vector3.up * dt * 1.2f;
            float a = Mathf.Pow(1f - t, 1.5f);
            if (a < 0.02f) return false;
            ApplyColor(it.Rend, WithAlpha(it.BaseColor, a));
            ExplosionVFX.BillboardToCamera(it.Go);
            return true;
        }

        private static bool TickSmoke(Item it, float dt)
        {
            it.Elapsed += dt;
            float t = it.Elapsed / it.Duration;
            float a = t < 0.2f ? Mathf.Lerp(0f, 0.75f, t / 0.2f) : Mathf.Lerp(0.75f, 0f, (t - 0.2f) / 0.8f);
            if (a < 0.02f) return false;
            it.Go.transform.localScale = Vector3.one * it.BaseScale * (1f + t * 1.8f);
            it.Go.transform.position += Vector3.up * dt * it.RiseSpeed;
            it.Go.transform.Rotate(Vector3.forward, dt * 8f);
            ApplyColor(it.Rend, WithAlpha(it.BaseColor, a));
            ExplosionVFX.BillboardToCamera(it.Go);
            return true;
        }

        private static bool TickDebris(Item it, float dt)
        {
            it.Elapsed += dt;
            float t = it.Elapsed / it.Duration;
            it.Velocity += Vector3.up * it.Gravity * dt;
            it.Go.transform.position += it.Velocity * dt;
            it.Go.transform.Rotate(Vector3.forward, it.SpinRate * dt);
            float a = t < 0.6f ? it.BaseColor.a : Mathf.Lerp(it.BaseColor.a, 0f, (t - 0.6f) / 0.4f);
            if (a < 0.02f) return false;
            ApplyColor(it.Rend, WithAlpha(it.BaseColor, a));
            ExplosionVFX.BillboardToCamera(it.Go);
            return true;
        }

        private static bool TickFade(Item it, float dt)
        {
            it.Elapsed += dt;
            float a = Mathf.Lerp(it.BaseColor.a, 0f, it.Elapsed / it.Duration);
            if (a < 0.02f) return false;
            ApplyColor(it.Rend, WithAlpha(it.BaseColor, a));
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

        // ── Entry points ──────────────────────────────────────────────────────────
        public static void Spawn(Vector3 origin, RaycastHit groundHit)
        {
            if (Config.Dbg2) MelonLogger.Msg($"[VFX] Spawn origin={origin} ground={groundHit.point}");
            SpawnFireball(origin);
            SpawnSmoke(origin);
            SpawnScorch(origin, groundHit);
            SpawnDebris(origin);
        }

        public static void SpawnAerial(Vector3 origin)
        {
            if (Config.Dbg2) MelonLogger.Msg($"[VFX] SpawnAerial origin={origin}");
            SpawnFireball(origin);
            SpawnSmoke(origin);
            SpawnDebris(origin);
        }

        // ── 2. Fireball ───────────────────────────────────────────────────────────
        private static void SpawnFireball(Vector3 origin)
        {
            if (!Config.VFXActive) return;
            float fbScale = Config.VFX(6f);         // fireball scale
            float fbDur = Config.VFX(0.5f);       // fireball duration
            if (Config.Dbg2) MelonLogger.Msg($"[VFX] SpawnFireball scale={fbScale}");
            var matA = GetCachedMat("MuzzleFlash1");
            var matB = GetCachedMat("MuzzleFlash3");
            var colorA = new Color(1f, 0.5f, 0.1f, 0.95f);
            var colorB = new Color(0.8f, 0.25f, 0.05f, 0.8f);

            if (matA != null)
                AddQuadItem(VfxRunner.Kind.Fireball, "VFX_Fireball_A", origin, Quaternion.identity,
                    fbScale * 0.7f, matA, colorA, fbDur, 0f, 0f, 0f, Vector3.zero, 0f);

            if (matB != null)
                AddQuadItem(VfxRunner.Kind.Fireball, "VFX_Fireball_B", origin, Quaternion.identity,
                    fbScale, matB, colorB, fbDur, 0.05f, 0f, 0f, Vector3.zero, 0f);
        }

        // ── 3. Smoke ──────────────────────────────────────────────────────────────
        private static void SpawnSmoke(Vector3 origin)
        {
            if (!Config.VFXActive) return;
            int count = Config.VFXInt(16);
            float smokeScale = Config.VFX(2.5f);
            float smokeDur = Config.VFX(5f);
            float riseSpeed = Config.VFX(1.8f);
            if (Config.Dbg2) MelonLogger.Msg($"[VFX] SpawnSmoke count={count}");
            var mat = GetCachedMat("WFX_T_SmokeLoopAlpha");
            if (mat == null) return;

            for (int i = 0; i < Mathf.Max(1, count); i++)
            {
                float rx = (float)(SharedRng.Instance.NextDouble() - 0.5) * 1.2f;
                float rz = (float)(SharedRng.Instance.NextDouble() - 0.5) * 1.2f;
                float grey = 0.25f + (float)SharedRng.Instance.NextDouble() * 0.2f;
                float scale = smokeScale * (0.8f + (float)SharedRng.Instance.NextDouble() * 0.5f);
                float delay = (float)i / count * 0.3f;

                AddQuadItem(VfxRunner.Kind.Smoke, "VFX_Smoke",
                    origin + new Vector3(rx, 0.3f, rz),
                    Quaternion.AngleAxis((float)SharedRng.Instance.NextDouble() * 360f, Vector3.up),
                    scale, mat, new Color(grey, grey, grey, 0f), smokeDur, delay,
                    riseSpeed, 0f, Vector3.zero, 0f);
            }
        }

        // ── 4. Scorch ─────────────────────────────────────────────────────────────
        private static void SpawnScorch(Vector3 origin, RaycastHit groundHit)
        {
            if (!Config.VFXActive) return;
            if (groundHit.collider == null || ExplosionSystem.IsLimb(groundHit.collider.gameObject)) return;
            float maxHeight = Config.VFX(2f);
            float baseRadius = Config.VFX(1f);
            float fadeTime = Config.VFX(30f);

            float height = origin.y - groundHit.point.y;
            if (Config.Dbg2) MelonLogger.Msg($"[VFX] SpawnScorch height={height:F2} max={maxHeight}");
            if (height > maxHeight) { if (Config.Dbg2) MelonLogger.Msg("[VFX] Scorch: too high, skip"); return; }

            float t = 1f - Mathf.Clamp01(height / maxHeight);
            float radius = baseRadius * t;
            if (radius < 0.1f) { if (Config.Dbg2) MelonLogger.Msg("[VFX] Scorch: radius too small, skip"); return; }

            var layers = new[] {
                ("Soft",             new Color(0.04f, 0.03f, 0.02f, t * 0.95f), 1.0f),
                ("Default-Particle", new Color(0.10f, 0.08f, 0.05f, t * 0.5f),  1.3f),
            };

            foreach (var (texName, color, scaleMult) in layers)
            {
                var mat = GetCachedMat(texName);
                if (mat == null) continue;

                Vector3 pos = groundHit.point + groundHit.normal * (0.01f + scaleMult * 0.01f);
                Quaternion rot = Quaternion.LookRotation(Vector3.forward, groundHit.normal)
                                  * Quaternion.Euler(90f, 0f, 0f);
                rot = Quaternion.AngleAxis((float)SharedRng.Instance.NextDouble() * 360f, groundHit.normal) * rot;

                if (Config.Dbg2) MelonLogger.Msg($"[VFX] Scorch '{texName}' radius={radius:F2} scaleMult={scaleMult} pos={pos}");

                var go = MakeQuad("VFX_Scorch", pos, rot, radius * 2f * scaleMult, mat);
                VfxRunner.Add(new VfxRunner.Item
                {
                    Kind = VfxRunner.Kind.Fade,
                    Go = go,
                    Rend = go.GetComponent<Renderer>(),
                    BaseColor = color,
                    Duration = fadeTime,
                });
            }
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

        private static readonly string[] DebrisTextures =
            { "Medium01","Medium02","Medium03","Medium04","Medium05","Medium06","Thin01","Thin02","Large01" };

        private static void SpawnDebris(Vector3 origin)
        {
            if (!Config.VFXActive) return;
            int count = Config.VFXInt(18);
            float speed = Config.VFX(6f);
            float dur = Config.VFX(1.8f);
            float dScale = Config.VFX(0.35f);
            if (Config.Dbg2) MelonLogger.Msg($"[VFX] SpawnDebris count={count}");
            float ga = Physics.gravity.y;
            float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));

            for (int i = 0; i < Mathf.Max(1, count); i++)
            {
                float ft = (float)i / count;
                float fy = Mathf.Lerp(0.1f, 1f, ft);
                float frXZ = Mathf.Sqrt(Mathf.Max(0f, 1f - fy * fy));
                var dir = new Vector3(frXZ * Mathf.Cos(i * goldenAngle), fy,
                                         frXZ * Mathf.Sin(i * goldenAngle)).normalized;

                float spd = speed * (0.6f + (float)SharedRng.Instance.NextDouble() * 0.8f);
                float grey = 0.4f + (float)SharedRng.Instance.NextDouble() * 0.3f;
                Color col = SharedRng.Instance.NextDouble() > 0.4
                    ? new Color(grey, grey * 0.4f, grey * 0.1f, 0.9f)
                    : new Color(grey * 0.3f, grey * 0.25f, grey * 0.2f, 0.85f);

                var mat = GetCachedMat(DebrisTextures[SharedRng.Instance.Next(DebrisTextures.Length)]);
                if (mat == null) continue;

                float scale = dScale * (0.5f + (float)SharedRng.Instance.NextDouble() * 1f);

                AddQuadItem(VfxRunner.Kind.Debris, "VFX_Debris", origin + dir * 0.3f,
                    Quaternion.identity, scale, mat, col, dur, 0f, 0f, 0f, dir * spd, ga);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void AddQuadItem(VfxRunner.Kind kind, string name, Vector3 pos, Quaternion rot,
            float scale, Material mat, Color baseColor, float duration, float delay,
            float riseSpeed, float spinRate, Vector3 velocity, float gravity)
        {
            var go = MakeQuad(name, pos, rot, scale, mat);
            VfxRunner.Add(new VfxRunner.Item
            {
                Kind = kind,
                Go = go,
                Rend = go.GetComponent<Renderer>(),
                BaseColor = baseColor,
                Duration = duration,
                Delay = delay,
                BaseScale = scale,
                RiseSpeed = riseSpeed,
                SpinRate = spinRate,
                Velocity = velocity,
                Gravity = gravity,
            });
        }

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
