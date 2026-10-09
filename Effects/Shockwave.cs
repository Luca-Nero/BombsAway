using System;
using System.Collections.Generic;
using FruitLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace BombsAway
{
    /// <summary>
    /// A blast's shock front seen as a ring of bent light racing out from it, then gone. Every
    /// FruitLib explosion of at least <see cref="Config.ShockwaveMinCharge"/> starts one (any
    /// mod's, as with the holes in smoke). ShockFront runs the front out at the speed its own
    /// overpressure gives it; FX_Shockwave (a sphere with the Shockwave shader) is sized to it
    /// every frame and bends the picture behind it, read from URP's opaque texture.
    ///
    /// The opaque texture costs a copy of the screen each frame, so cameras only ask for it while
    /// a front is out: the world camera, and the binoculars' and the CLU's screens, which call
    /// <see cref="Admit"/>. The sphere sits on a free layer of its own, which only those cameras
    /// draw while it's in use. What each camera asked for before is put back afterwards.
    /// </summary>
    internal static class Shockwave
    {
        private const string OpaqueTex = "_CameraOpaqueTexture";
        private const float BasePx = 14f;                 // bend at full strength, pixels of a 1080 line screen

        private sealed class Front
        {
            public ShockFront F;
            public GameObject Go;
            public Material Mat;
        }

        private static readonly List<Front> _live = new List<Front>();
        private static readonly Stack<Front> _pool = new Stack<Front>();
        private static readonly List<UniversalAdditionalCameraData> _admitted = new List<UniversalAdditionalCameraData>();
        private static readonly List<CameraOverrideOption> _prevOption = new List<CameraOverrideOption>();
        private static int _layer = -2;
        private static bool _active, _failed, _reported, _downsampleSet;
        private static Downsampling _prevDownsample;
        private static int _activeFrames;
        private static float _nextScan;
        private static int _ids = -1;

        /// <summary>The free layer's bit, or 0.</summary>
        public static int LayerBit => _layer >= 0 ? 1 << _layer : 0;

        /// <summary>FruitLib reports an explosion: start its front if it's big enough.</summary>
        public static void Blast(in ExplosionInfo x)
        {
            if (_failed || !Config.Shockwave || Config.ShockwaveStrength <= 0f || x.Spec == null) return;
            float kg = x.Spec.ChargeKgTNT;
            if (kg < Mathf.Max(0.001f, Config.ShockwaveMinCharge)) return;
            // On the ground the wave is reflected up off it: the blast acts about twice as big
            // (FruitLib's surface burst, ExplosionSpec.SurfaceBurstFactor).
            float w3 = Mathf.Pow(kg, 1f / 3f);
            if (x.HasGround && x.Ground.distance <= Mathf.Max(0.5f, 0.15f * w3)) kg *= Mathf.Max(1f, x.Spec.SurfaceBurstFactor);
            try { Spawn(x.Origin, kg); }
            catch (Exception e) { Fail(e); }
        }

        private static void Spawn(Vector3 at, float kg)
        {
            Layer();
            if (_live.Count >= Mathf.Max(1, Config.ShockwaveMax)) Recycle(0);
            Front f = _pool.Count > 0 ? _pool.Pop() : Make();
            if (f == null) return;
            f.F = new ShockFront(at, kg, Config.ShockwaveFadeKPa);
            _live.Add(f);
            Place(f, CameraCache.Main);
            if (Config.Dbg1)
                MelonLogger.Msg($"[Shockwave] {kg:0.##} kg at {at}: gone at {ShockFrontReach(kg):0} m");
        }

        /// <summary>How far a front of <paramref name="kg"/> shows (where it falls to the fade pressure), for the log.</summary>
        private static float ShockFrontReach(float kg)
        {
            var f = new ShockFront(Vector3.zero, kg, Config.ShockwaveFadeKPa);
            for (int i = 0; i < 4000 && !f.Done; i++) f.Step(0.005f);
            return f.Radius;
        }

        private static Front Make()
        {
            var prefab = OrdnanceModels.Asset("FX_Shockwave");
            if (prefab == null)
            {
                _failed = true;
                MelonLogger.Warning("[Shockwave] FX_Shockwave isn't in the bundle: no shock fronts.");
                return null;
            }
            var go = Object.Instantiate(prefab);
            go.name = $"BA_Shockwave{++_ids}";
            Object.DontDestroyOnLoad(go);
            if (_layer >= 0) go.layer = _layer;
            var r = go.GetComponent<Renderer>();
            var mat = new Material(r.sharedMaterial) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            r.sharedMaterial = mat;
            return new Front { Go = go, Mat = mat };
        }

        /// <summary>Moves every front on and sizes its sphere; after the explosions' own effects (LateUpdate).</summary>
        public static void Tick()
        {
            if (_live.Count == 0) { if (_active) Deactivate(); return; }
            if (!Config.Shockwave) { Clear(); return; }
            try
            {
                var cam = CameraCache.Main;
                float dt = Time.deltaTime;
                for (int i = _live.Count - 1; i >= 0; i--)
                {
                    var f = _live[i];
                    if (f.Go == null) { _live.RemoveAt(i); continue; }
                    f.F.Step(dt);
                    if (f.F.Done || f.F.Age > 12f) { Recycle(i); continue; }
                    Place(f, cam);
                }
                if (_live.Count > 0) Activate(cam);
                else Deactivate();
            }
            catch (Exception e) { Fail(e); }
        }

        private static void Place(Front f, Camera cam)
        {
            var s = f.F;
            float r = s.Radius, l = s.Thickness;
            var t = f.Go.transform;
            t.position = s.Center;
            t.localScale = Vector3.one * (2f * r * 1.06f);   // the primitive sphere is 1 m across; its flat faces must clear the front
            var m = f.Mat;
            m.SetVector("_Center", s.Center);
            m.SetFloat("_Radius", r);
            m.SetFloat("_Thick", l);
            m.SetFloat("_Norm", 1f / ShockFront.PeakBend(r, l));
            m.SetFloat("_Amp", Config.ShockwaveStrength * BasePx / 1080f);
            m.SetFloat("_Glint", Config.ShockwaveGlint);
            m.SetFloat("_Refract", Config.ShockwaveRefract ? 1f : 0f);
            // From inside it the front has passed you (the blast's shake says so): fade out as it does.
            float inside = cam != null ? Mathf.Clamp01(((cam.transform.position - s.Center).magnitude / r - 1.06f) / 0.08f) : 1f;
            m.SetFloat("_Fade", s.Strength * inside);
            if (!f.Go.activeSelf) f.Go.SetActive(true);
        }

        private static void Recycle(int i)
        {
            var f = _live[i];
            _live.RemoveAt(i);
            if (f.Go == null) return;
            f.Go.SetActive(false);
            _pool.Push(f);
        }

        /// <summary>RESET BOMBS and scene changes: every front goes.</summary>
        public static void Clear()
        {
            for (int i = _live.Count - 1; i >= 0; i--) Recycle(i);
            Deactivate();
        }

        // ── Cameras ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A screen of ours that draws the world (the binoculars', the CLU's), after it has set its
        /// culling mask this frame: while a front is out it draws the fronts' layer and asks for
        /// the opaque texture; otherwise the layer is left out.
        /// </summary>
        public static void Admit(Camera cam)
        {
            int bit = LayerBit;
            if (cam == null || bit == 0) return;
            if (!_active) { if ((cam.cullingMask & bit) != 0) cam.cullingMask &= ~bit; return; }
            if ((cam.cullingMask & bit) == 0) cam.cullingMask |= bit;
            AskForOpaque(cam);
        }

        private static void Activate(Camera main)
        {
            _active = true;
            _activeFrames++;
            int bit = LayerBit;
            if (main != null)
            {
                if (bit != 0 && (main.cullingMask & bit) == 0) main.cullingMask |= bit;
                AskForOpaque(main);
            }
            if (!_downsampleSet && Config.ShockwaveRefract) FullSizeCopy();
            // Any other camera that draws everything would draw the fronts without the texture: leave them out.
            if (bit != 0 && Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                foreach (var c in Camera.allCameras)
                {
                    if (c == null || c == main || (c.cullingMask & bit) == 0 || c.name.StartsWith("BA_")) continue;
                    c.cullingMask &= ~bit;
                }
            }
            if (!_reported && _activeFrames == 3) Report();
        }

        private static void AskForOpaque(Camera cam)
        {
            if (!Config.ShockwaveRefract) return;
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            for (int i = 0; i < _admitted.Count; i++) if (_admitted[i] != null && _admitted[i].Pointer == data.Pointer) return;
            _admitted.Add(data);
            _prevOption.Add(data.requiresColorOption);
            data.requiresColorOption = CameraOverrideOption.On;
        }

        private static void Deactivate()
        {
            if (!_active) return;
            _active = false;
            _activeFrames = 0;
            for (int i = 0; i < _admitted.Count; i++)
            {
                var d = _admitted[i];
                try { if (d != null && d.Pointer != IntPtr.Zero) d.requiresColorOption = _prevOption[i]; }
                catch (Exception) { }
            }
            _admitted.Clear();
            _prevOption.Clear();
            int bit = LayerBit;
            var main = CameraCache.Main;
            if (bit != 0 && main != null && (main.cullingMask & bit) != 0) main.cullingMask &= ~bit;
            if (_downsampleSet)
            {
                try
                {
                    var asset = UniversalRenderPipeline.asset;
                    if (asset != null) asset.m_OpaqueDownsampling = _prevDownsample;
                }
                catch (Exception) { }
                _downsampleSet = false;
            }
        }

        /// <summary>URP copies the picture at half size by default; the bend wants it sharp. Put back afterwards.</summary>
        private static void FullSizeCopy()
        {
            _downsampleSet = true;
            try
            {
                var asset = UniversalRenderPipeline.asset;
                if (asset == null) return;
                _prevDownsample = asset.m_OpaqueDownsampling;
                if (_prevDownsample != Downsampling.None) asset.m_OpaqueDownsampling = Downsampling.None;
            }
            catch (Exception e) { MelonLogger.Warning($"[Shockwave] couldn't set the copy's size ({e.Message}); it may look soft."); }
        }

        /// <summary>Once a session, a few frames into the first front: what the pipeline gives the shader.</summary>
        private static void Report()
        {
            _reported = true;
            try
            {
                var tex = Shader.GetGlobalTexture(OpaqueTex);
                var asset = UniversalRenderPipeline.asset;
                string pipe = asset != null ? $"pipeline opaque texture {asset.supportsCameraOpaqueTexture}, copy {_prevDownsample}" : "no URP asset";
                if (tex != null)
                    MelonLogger.Msg($"[Shockwave] opaque texture {tex.width}x{tex.height} ({pipe}); layer {_layer}.");
                else
                    MelonLogger.Warning($"[Shockwave] no opaque texture bound yet ({pipe}; layer {_layer}). If the ring looks black or smeared, set ShockwaveRefract = false.");
            }
            catch (Exception e) { MelonLogger.Warning($"[Shockwave] report failed: {e.Message}"); }
        }

        /// <summary>A free layer for the spheres; -1 = none (they then stay on the prefab's layer).</summary>
        private static int Layer()
        {
            if (_layer == -2) _layer = FreeLayers.TakeTransparent("Shockwave", "the fronts stay on the default layer, so every camera draws them");
            return _layer;
        }

        private static void Fail(Exception e)
        {
            _failed = true;
            MelonLogger.Warning($"[Shockwave] turned off after an error: {e.Message}");
            try { Clear(); } catch (Exception) { }
        }
    }
}
