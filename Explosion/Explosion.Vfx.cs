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
        private static void EnsureTextures()
        {
            if (_texScanDone) return;
            _texScanDone = true;
            var all = Resources.FindObjectsOfTypeAll<Texture2D>();
            foreach (var t in all)
                if (t != null && !string.IsNullOrEmpty(t.name) && !_texCache.ContainsKey(t.name))
                    _texCache[t.name] = t;
            if (Config.Dbg2) MelonLogger.Msg($"[VFX] Texture scan: {_texCache.Count} cached");
        }

        private static Texture2D Tex(string name)
        {
            EnsureTextures();
            _texCache.TryGetValue(name, out var t);
            if (t == null) MelonLogger.Warning($"[VFX] Tex '{name}' NOT FOUND in cache");
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
        public static void SpawnDebrisArc(Vector3 p0, Vector3 vel, float gy, float flightTime)
        {
            if (!Config.VFXActive) return;
            MelonCoroutines.Start(AnimateDebrisChunk(p0, vel, gy, flightTime));
        }

        private static System.Collections.IEnumerator AnimateDebrisChunk(
            Vector3 p0, Vector3 vel, float gy, float flightTime)
        {
            var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chunk.name = "VFX_FragChunk";
            var col = chunk.GetComponent<Collider>();
            if (col != null) GameObject.Destroy(col); // no collider during flight

            float baseScale = Config.DebrisMeshScale
                * (0.6f + UnityEngine.Random.value * 0.8f);
            chunk.transform.position = p0;
            chunk.transform.localScale = new Vector3(
                baseScale * UnityEngine.Random.Range(0.5f, 1.5f),
                baseScale * UnityEngine.Random.Range(0.5f, 1.5f),
                baseScale * UnityEngine.Random.Range(0.7f, 2f));

            // Dark material
            var shader = Config.FindSpriteShader();
            float grey = UnityEngine.Random.Range(0.08f, 0.2f);
            var mat = new Material(shader);
            mat.color = new Color(grey, grey * 0.9f, grey * 0.7f, 1f);
            chunk.GetComponent<Renderer>().material = mat;

            // Dark smoke trail
            var trailObj = new GameObject("DebrisTrail");
            trailObj.transform.SetParent(chunk.transform, false);
            var trail = trailObj.AddComponent<TrailRenderer>();
            trail.time = Config.DebrisTrailTime;
            trail.startWidth = baseScale * 2f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.05f;
            var trailMat = new Material(shader);
            trailMat.color = new Color(0.15f, 0.12f, 0.1f, 0.5f);
            trail.material = trailMat;
            trail.startColor = new Color(0.2f, 0.15f, 0.1f, 0.6f);
            trail.endColor = new Color(0.3f, 0.25f, 0.2f, 0f);
            trail.Clear();

            Vector3 spinAxis = UnityEngine.Random.onUnitSphere;
            float spinRate = UnityEngine.Random.Range(200f, 600f);

            float elapsed = 0f;
            while (elapsed < flightTime && chunk != null)
            {
                float dt = Time.deltaTime;
                elapsed += dt;

                Vector3 pos = p0 + vel * elapsed
                    + new Vector3(0f, 0.5f * gy * elapsed * elapsed, 0f);
                chunk.transform.position = pos;
                chunk.transform.Rotate(spinAxis, spinRate * dt);
                yield return null;
            }

            if (chunk == null) yield break;

            Vector3 impactVel = vel + new Vector3(0f, gy * flightTime, 0f);
            var rb = chunk.AddComponent<Rigidbody>();
            rb.mass = 0.05f;
            rb.linearVelocity = impactVel * 0.3f; // damped bounce
            rb.angularVelocity = spinAxis * spinRate * Mathf.Deg2Rad * 0.2f;
            var boxCol = chunk.AddComponent<BoxCollider>();
            boxCol.size = Vector3.one;

            float settleTime = Config.DebrisLifetime;
            float settleElapsed = 0f;
            float fadeStart = settleTime * 0.7f;
            while (settleElapsed < settleTime && chunk != null)
            {
                settleElapsed += Time.deltaTime;
                if (settleElapsed > fadeStart)
                {
                    float fadeT = (settleElapsed - fadeStart) / (settleTime - fadeStart);
                    float a = Mathf.Lerp(1f, 0f, fadeT);
                    var c = mat.color;
                    c.a = a;
                    mat.color = c;
                }
                yield return null;
            }

            if (chunk != null) GameObject.Destroy(chunk);
            if (mat != null) GameObject.Destroy(mat);
            if (trailMat != null) GameObject.Destroy(trailMat);
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
