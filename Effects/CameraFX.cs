using MelonLoader;
using System;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // Camera FX
    // ══════════════════════════════════════════════════════════════════════════════
    internal static class CameraFX
    {
        private static float _trauma = 0f;
        private static float _shakeTime = 0f;
        private static float _baseFOV = -1f;   // the unzoomed field of view (BinocularView divides it by its zoom)

        /// <summary>A shake is running (it moves the camera's field of view).</summary>
        public static bool Shaking => _trauma > 0f;

        private static UnityEngine.Rendering.Universal.ChromaticAberration _chroma;
        private static UnityEngine.Rendering.Universal.Vignette _vignette;
        private static bool _ppResolved = false;
        private static float _baseChroma = 0f;
        private static float _baseVignette = 0f;
        private static float _punch = 0f;                 // chroma/vignette punch, 0..1, fades in Tick
        private const float PunchDuration = 0.45f;        // seconds until the punch has all but faded

        /// <param name="reach">Scales how far off a blast still shakes (a big bomb's reaches far).</param>
        public static void AddTrauma(Vector3 blastOrigin, float scale = 1f, float reach = 1f)
        {
            if (!Config.CamFXActive) return;

            var cam = Camera.main;
            if (cam == null) { MelonLogger.Warning("[CAM] Camera.main NULL"); return; }
            if (Config.Dbg2) MelonLogger.Msg($"[CAM] cam='{cam.name}' pos={cam.transform.position} FOV={cam.fieldOfView:F2}");

            float dist = Vector3.Distance(cam.transform.position, blastOrigin);
            float falloff = 1f - Mathf.Clamp01(dist / Mathf.Max(0.01f, Config.CamFX(20f) * Mathf.Max(0.1f, reach)));
            float trauma = Config.CamFX(10f) * falloff * falloff * scale;
            if (Config.Dbg2) MelonLogger.Msg($"[CAM] dist={dist:F2} falloff={falloff:F3} trauma={trauma:F3}");

            if (trauma < 0.01f) { if (Config.Dbg2) MelonLogger.Msg("[CAM] trauma < threshold, skip"); return; }
            AddKick(trauma);
        }

        /// <summary>A shake of a given size, not from a blast's distance (the AT-4 going off on the shoulder).</summary>
        public static void AddKick(float trauma)
        {
            if (!Config.CamFXActive || trauma < 0.01f) return;
            var cam = Camera.main;
            if (cam == null) return;

            if (_trauma <= 0f)
            {
                _baseFOV = BinocularView.RestFov(cam);
                if (Config.Dbg2) MelonLogger.Msg($"[CAM] first hit — stored baseFOV={_baseFOV:F2}");
            }

            _trauma = Mathf.Min(1f, _trauma + trauma);
            _shakeTime = 0f;
            if (Config.Dbg2) MelonLogger.Msg($"[CAM] _trauma={_trauma:F3}");

            ResolvePP();
            _punch = Mathf.Max(_punch, Mathf.Min(1f, trauma));   // a kick over a fading one restarts it, never stacks
        }

        public static void Tick(float dt)
        {
            if (!Config.CamFXActive) return;
            TickPunch(dt);
            try
            {
                if (_trauma <= 0f) return;

                var cam = Camera.main;
                if (cam == null) { MelonLogger.Warning("[CAM] Tick: cam null"); _trauma = 0f; return; }

                _trauma = Mathf.Max(0f, _trauma - 2.5f * dt);  // decay
                _shakeTime += dt;

                if (_trauma < 0.001f)
                {
                    if (Config.Dbg2) MelonLogger.Msg($"[CAM] shake done — restore FOV={_baseFOV:F2}");
                    if (_baseFOV > 0f) cam.fieldOfView = _baseFOV / BinocularView.Magnification;
                    _trauma = 0f;
                    return;
                }

                float shake = _trauma;
                float tc = _shakeTime * Config.CamFX(18f);  // frequency
                float seed = 43.7f;

                float ox = (Mathf.PerlinNoise(tc, 0f) - 0.5f) * 2f;
                float oy = (Mathf.PerlinNoise(tc + seed, 0.5f) - 0.5f) * 2f;
                float oz = (Mathf.PerlinNoise(0f, tc) - 0.5f) * 2f;
                float rx = (Mathf.PerlinNoise(tc + 10f, 0f) - 0.5f) * 2f;
                float ry = (Mathf.PerlinNoise(tc + 20f, 0.5f) - 0.5f) * 2f;
                float rz = (Mathf.PerlinNoise(tc + 30f, 1f) - 0.5f) * 2f;

                float maxOffset = Config.CamFX(0.6f);   // position offset
                float maxAngle = Config.CamFX(12f);    // rotation offset

                var offset = new Vector3(ox, oy, oz) * (maxOffset * shake);
                var euler = new Vector3(
                    rx * maxAngle * shake,
                    ry * maxAngle * shake * 0.5f,
                    rz * maxAngle * shake * 0.3f);

                float fovBefore = cam.fieldOfView;
                var posBefore = cam.transform.position;

                cam.transform.position += offset;
                cam.transform.Rotate(euler, Space.Self);

                // Through the binoculars the punch is in their scale, not the naked eye's.
                float zoom = BinocularView.Magnification;
                float targetFOV = ((_baseFOV > 0f ? _baseFOV : fovBefore * zoom) - shake * 15f) / zoom;
                cam.fieldOfView = Mathf.Lerp(fovBefore, targetFOV, 0.4f);

                if (_shakeTime < 0.2f)
                    if (Config.Dbg2) MelonLogger.Msg($"[CAM] Tick shake={shake:F3} offset={offset} euler={euler} " +
                                    $"FOV {fovBefore:F2}->{cam.fieldOfView:F2} " +
                                    $"pos_delta={cam.transform.position - posBefore}");
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[CAM] Tick exception: {e.Message}");
                _trauma = 0f;
            }
        }

        private static void ResolvePP()
        {
            if (_ppResolved)
            {
                bool chromaDead = _chroma == null || _chroma.Pointer == IntPtr.Zero;
                bool vignetteDead = _vignette == null || _vignette.Pointer == IntPtr.Zero;
                if (!chromaDead && !vignetteDead) return;

                if (Config.Dbg2) MelonLogger.Msg("[CAM] PP refs dead (game reset?) — re-resolving");
                _ppResolved = false;
                _chroma = null;
                _vignette = null;
            }
            _ppResolved = true;

            if (Config.Dbg2) MelonLogger.Msg("[CAM] ResolvePP start");
            var vols = Resources.FindObjectsOfTypeAll<UnityEngine.Rendering.Volume>();
            if (Config.Dbg2) MelonLogger.Msg($"[CAM] Found {vols.Length} Volume(s)");

            UnityEngine.Rendering.Volume globalVol = null;
            foreach (var v in vols)
            {
                if (v == null) continue;
                if (Config.Dbg2) MelonLogger.Msg($"[CAM]   vol='{v.gameObject.name}' global={v.isGlobal} priority={v.priority} profile={(v.profile != null ? v.profile.name : "NULL")}");
                if (v.isGlobal && v.profile != null) globalVol = v;
            }

            if (globalVol == null)
            {
                MelonLogger.Warning("[CAM] No usable global volume — PP skipped");
                return;
            }

            var profile = globalVol.profile;
            if (Config.Dbg2) MelonLogger.Msg($"[CAM] Profile='{profile.name}' has {profile.components.Count} components:");
            foreach (var comp in profile.components)
                if (comp != null) if (Config.Dbg2) MelonLogger.Msg($"[CAM]   {comp.GetIl2CppType().Name} active={comp.active}");

            if (!profile.TryGet(out _chroma))
            {
                _chroma = profile.Add<UnityEngine.Rendering.Universal.ChromaticAberration>(false);
                if (Config.Dbg2) MelonLogger.Msg("[CAM] Added ChromaticAberration");
            }
            else if (Config.Dbg2) MelonLogger.Msg("[CAM] ChromaticAberration already in profile");

            if (!profile.TryGet(out _vignette))
            {
                _vignette = profile.Add<UnityEngine.Rendering.Universal.Vignette>(false);
                if (Config.Dbg2) MelonLogger.Msg("[CAM] Added Vignette");
            }
            else if (Config.Dbg2) MelonLogger.Msg("[CAM] Vignette already in profile");

            _baseChroma = _chroma != null ? _chroma.intensity.value : 0f;
            _baseVignette = _vignette != null ? _vignette.intensity.value : 0f;
            if (Config.Dbg2) MelonLogger.Msg($"[CAM] PP ready chroma={_chroma != null}(base={_baseChroma:F3}) vignette={_vignette != null}(base={_baseVignette:F3})");
        }

        /// <summary>Fades the chromatic aberration and vignette punch: one value for every kick,
        /// so a gun run's hundred hits a second write it once a frame.</summary>
        private static void TickPunch(float dt)
        {
            if (_punch <= 0f) return;
            _punch *= Mathf.Exp(-dt * 4f / Mathf.Max(0.05f, Config.CamFX(PunchDuration)));
            if (_punch < 0.002f) _punch = 0f;
            try
            {
                if (_chroma != null)
                {
                    _chroma.active = true; _chroma.intensity.overrideState = true;
                    _chroma.intensity.value = _baseChroma + Config.CamFX(0.1f) * _punch;      // chroma intensity
                }
                if (_vignette != null)
                {
                    _vignette.active = true; _vignette.intensity.overrideState = true;
                    _vignette.intensity.value = _baseVignette + Config.CamFX(0.05f) * _punch; // vignette intensity
                }
            }
            catch (Exception e) { MelonLogger.Warning($"[CAM] punch: {e.Message}"); _punch = 0f; }
        }
    }
}
