using MelonLoader;
using System;
using System.Collections.Generic;
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
        private static float _baseFOV = -1f;   // the field of view before the shake

        private static UnityEngine.Rendering.Universal.ChromaticAberration _chroma;
        private static UnityEngine.Rendering.Universal.Vignette _vignette;
        private static bool _ppResolved = false;
        private static float _baseChroma = 0f;
        private static float _baseVignette = 0f;
        private static float _punch = 0f;                 // chroma/vignette punch, 0..1, fades in Tick
        private const float PunchDuration = 0.45f;        // seconds until the punch has all but faded

        /// <summary>An explosion's shake on its way: it lands when the blast wave gets here.</summary>
        private sealed class Wave { public float At, Reach, Scale; public Vector3 Origin; }
        private static readonly List<Wave> _waves = new List<Wave>();

        /// <summary>
        /// An explosion's shake, for every explosive alike: it arrives with the blast wave, late by
        /// the distance over the speed of sound, and reaches out with the cube root of the charge
        /// (as blast scales): about 16 m for a grenade, 40 m for the HE warhead's 3 kg, 52 m for a
        /// 155 mm shell, 210 m for a Mk 84. <paramref name="scale"/> is how hard it shakes close in
        /// (a gun run's many small hits each only nudge).
        /// </summary>
        public static void Blast(Vector3 origin, float chargeKgTNT, float scale = 1f)
        {
            if (!Config.CamFXActive || scale <= 0f) return;
            float reach = 2f * Mathf.Pow(Mathf.Max(0.001f, chargeKgTNT) / 3f, 1f / 3f);
            _waves.Add(new Wave { At = Time.time + Sfx.Delay(origin), Origin = origin, Reach = reach, Scale = scale });
        }

        /// <summary>RESET BOMBS and scene changes: shakes still on their way don't arrive.</summary>
        public static void Clear() => _waves.Clear();

        /// <param name="reach">Scales how far off a blast still shakes (a big bomb's reaches far).</param>
        public static void AddTrauma(Vector3 blastOrigin, float scale = 1f, float reach = 1f)
        {
            if (!Config.CamFXActive) return;

            var cam = Camera.main;
            if (cam == null) { MelonLogger.Warning("[CAM] Camera.main NULL"); return; }

            float dist = Vector3.Distance(cam.transform.position, blastOrigin);
            float falloff = 1f - Mathf.Clamp01(dist / Mathf.Max(0.01f, Config.CamFX(20f) * Mathf.Max(0.1f, reach)));
            float trauma = Config.CamFX(10f) * falloff * falloff * scale;

            AddKick(trauma);
        }

        /// <summary>A shake of a given size, not from a blast's distance (the AT-4 going off on the shoulder).</summary>
        public static void AddKick(float trauma)
        {
            if (!Config.CamFXActive || trauma < 0.01f) return;
            var cam = Camera.main;
            if (cam == null) return;

            if (_trauma <= 0f) _baseFOV = cam.fieldOfView;

            _trauma = Mathf.Min(1f, _trauma + trauma);
            _shakeTime = 0f;

            ResolvePP();
            _punch = Mathf.Max(_punch, Mathf.Min(1f, trauma));   // a kick over a fading one restarts it, never stacks
        }

        public static void Tick(float dt)
        {
            if (!Config.CamFXActive) { _waves.Clear(); return; }
            float now = Time.time;
            for (int i = _waves.Count - 1; i >= 0; i--)
            {
                var w = _waves[i];
                if (now < w.At) continue;
                _waves.RemoveAt(i);
                AddTrauma(w.Origin, w.Scale, w.Reach);
            }
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

                cam.transform.position += offset;
                cam.transform.Rotate(euler, Space.Self);

                float targetFOV = (_baseFOV > 0f ? _baseFOV : fovBefore) - shake * 15f;
                cam.fieldOfView = Mathf.Lerp(fovBefore, targetFOV, 0.4f);
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

                // Dead refs: the game reset its volumes, resolve again.
                _ppResolved = false;
                _chroma = null;
                _vignette = null;
            }
            _ppResolved = true;

            var profile = GlobalProfile();
            if (profile == null)
            {
                MelonLogger.Warning("[CAM] No usable global volume — PP skipped");
                return;
            }

            if (!profile.TryGet(out _chroma))
                _chroma = profile.Add<UnityEngine.Rendering.Universal.ChromaticAberration>(false);
            if (!profile.TryGet(out _vignette))
                _vignette = profile.Add<UnityEngine.Rendering.Universal.Vignette>(false);

            _baseChroma = _chroma != null ? _chroma.intensity.value : 0f;
            _baseVignette = _vignette != null ? _vignette.intensity.value : 0f;
        }

        /// <summary>The game's global post-processing profile (the last global volume that has one); null if there is none. AdsBlur's too.</summary>
        internal static UnityEngine.Rendering.VolumeProfile GlobalProfile()
        {
            UnityEngine.Rendering.Volume global = null;
            foreach (var v in Resources.FindObjectsOfTypeAll<UnityEngine.Rendering.Volume>())
                if (v != null && v.isGlobal && v.profile != null) global = v;
            return global != null ? global.profile : null;
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
