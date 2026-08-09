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
        private static float _baseFOV = -1f;

        private static UnityEngine.Rendering.Universal.ChromaticAberration _chroma;
        private static UnityEngine.Rendering.Universal.Vignette _vignette;
        private static bool _ppResolved = false;
        private static float _baseChroma = 0f;
        private static float _baseVignette = 0f;

        public static void AddTrauma(Vector3 blastOrigin)
        {
            if (!Config.CamFXActive) return;

            var cam = Camera.main;
            if (cam == null) { MelonLogger.Warning("[CAM] Camera.main NULL"); return; }
            if (Config.Dbg2) MelonLogger.Msg($"[CAM] cam='{cam.name}' pos={cam.transform.position} FOV={cam.fieldOfView:F2}");

            float dist = Vector3.Distance(cam.transform.position, blastOrigin);
            float falloff = 1f - Mathf.Clamp01(dist / Config.CamFX(20f));
            float trauma = Config.CamFX(10f) * falloff * falloff;
            if (Config.Dbg2) MelonLogger.Msg($"[CAM] dist={dist:F2} falloff={falloff:F3} trauma={trauma:F3}");

            if (trauma < 0.01f) { if (Config.Dbg2) MelonLogger.Msg("[CAM] trauma < threshold, skip"); return; }

            if (_trauma <= 0f)
            {
                _baseFOV = cam.fieldOfView;
                if (Config.Dbg2) MelonLogger.Msg($"[CAM] first hit — stored baseFOV={_baseFOV:F2}");
            }

            _trauma = Mathf.Min(1f, _trauma + trauma);
            _shakeTime = 0f;
            if (Config.Dbg2) MelonLogger.Msg($"[CAM] _trauma={_trauma:F3}");

            ResolvePP();
            MelonCoroutines.Start(PunchPP(trauma));
        }

        public static void Tick(float dt)
        {
            if (!Config.CamFXActive) return;
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
                    if (_baseFOV > 0f) cam.fieldOfView = _baseFOV;
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

                float targetFOV = (_baseFOV > 0f ? _baseFOV : fovBefore) - shake * 15f;
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

        private static System.Collections.IEnumerator PunchPP(float traumaScale)
        {
            if (Config.Dbg2) MelonLogger.Msg($"[CAM] PunchPP traumaScale={traumaScale:F3}");
            try
            {
                if (_chroma != null)
                {
                    float peak = Config.CamFX(0.1f) * traumaScale;    // chroma intensity
                    if (Config.Dbg2) MelonLogger.Msg($"[CAM] Chroma: base={_baseChroma:F3} peak={peak:F3}");
                    MelonCoroutines.Start(PunchFloat(
                        v => { _chroma.active = true; _chroma.intensity.overrideState = true; _chroma.intensity.value = v; },
                        _baseChroma, peak, Config.CamFX(0.4f)));      // chroma duration
                }
                else MelonLogger.Warning("[CAM] Chroma null");
            }
            catch (Exception e) { MelonLogger.Warning($"[CAM] Chroma ex: {e.Message}"); }

            try
            {
                if (_vignette != null)
                {
                    float peak = Config.CamFX(0.05f) * traumaScale;   // vignette intensity
                    if (Config.Dbg2) MelonLogger.Msg($"[CAM] Vignette: base={_baseVignette:F3} peak={peak:F3}");
                    MelonCoroutines.Start(PunchFloat(
                        v => { _vignette.active = true; _vignette.intensity.overrideState = true; _vignette.intensity.value = v; },
                        _baseVignette, peak, Config.CamFX(0.5f)));    // vignette duration
                }
                else MelonLogger.Warning("[CAM] Vignette null");
            }
            catch (Exception e) { MelonLogger.Warning($"[CAM] Vignette ex: {e.Message}"); }

            yield break;
        }

        private static System.Collections.IEnumerator PunchFloat(
            Action<float> setter, float baseline, float peak, float duration)
        {
            float elapsed = 0f, punchIn = duration * 0.2f;
            while (elapsed < punchIn) { elapsed += Time.deltaTime; setter(Mathf.Lerp(baseline, peak, elapsed / punchIn)); yield return null; }
            elapsed = 0f;
            float rest = duration * 0.8f;
            while (elapsed < rest) { elapsed += Time.deltaTime; setter(Mathf.Lerp(peak, baseline, elapsed / rest)); yield return null; }
            setter(baseline);
        }
    }
}
