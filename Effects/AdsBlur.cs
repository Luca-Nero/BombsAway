using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BombsAway
{
    /// <summary>
    /// Blurs the world behind a sight held up to the eye: a Gaussian depth of field on the game's
    /// global volume (the one CameraFX also uses), sharp up to ~half a metre, so the display in
    /// front of the eye stays crisp and everything past it goes soft. Faded in with the lift by
    /// pulling the blur's start distance in from far away. If the profile already had a depth of
    /// field, its settings are put back when the sight comes down.
    /// </summary>
    internal static class AdsBlur
    {
        private static DepthOfField _dof;
        private static bool _resolved, _failed, _added, _applied;
        private static bool _prevActive;
        private static DepthOfFieldMode _prevMode;
        private static float _prevStart, _prevEnd, _prevRadius;

        public static void Set(float amount)
        {
            if (_failed) return;
            if (amount <= 0.001f) { Release(); return; }
            if (!Resolve()) return;
            try
            {
                if (!_applied) Snapshot();
                float e = amount * amount * (3f - 2f * amount);
                _dof.active = true;
                _dof.mode.overrideState = true;               _dof.mode.value = DepthOfFieldMode.Gaussian;
                _dof.gaussianStart.overrideState = true;      _dof.gaussianStart.value = Mathf.Lerp(60f, Config.AdsBlurStart, e);
                _dof.gaussianEnd.overrideState = true;        _dof.gaussianEnd.value = Mathf.Lerp(120f, Config.AdsBlurEnd, e);
                _dof.gaussianMaxRadius.overrideState = true;  _dof.gaussianMaxRadius.value = Mathf.Lerp(0.5f, 1.5f, e);
                _dof.highQualitySampling.overrideState = true; _dof.highQualitySampling.value = true;
                _applied = true;
            }
            catch (Exception ex) { Fail(ex); }
        }

        /// <summary>The volume went away with its scene: look for the new one next time.</summary>
        public static void ResetForScene() { _dof = null; _resolved = false; _applied = false; _added = false; }

        private static void Snapshot()
        {
            _prevActive = _dof.active;
            _prevMode = _dof.mode.value;
            _prevStart = _dof.gaussianStart.value;
            _prevEnd = _dof.gaussianEnd.value;
            _prevRadius = _dof.gaussianMaxRadius.value;
        }

        private static void Release()
        {
            if (!_applied || _dof == null || _dof.Pointer == IntPtr.Zero) { _applied = false; return; }
            try
            {
                if (_added) _dof.active = false;
                else
                {
                    _dof.active = _prevActive;
                    _dof.mode.value = _prevMode;
                    _dof.gaussianStart.value = _prevStart;
                    _dof.gaussianEnd.value = _prevEnd;
                    _dof.gaussianMaxRadius.value = _prevRadius;
                }
            }
            catch (Exception ex) { Fail(ex); }
            _applied = false;
        }

        private static bool Resolve()
        {
            if (_resolved && _dof != null && _dof.Pointer != IntPtr.Zero) return true;
            _resolved = true;
            _dof = null;
            try
            {
                var profile = CameraFX.GlobalProfile();
                if (profile == null) return false;
                if (!profile.TryGet(out _dof))
                {
                    _dof = profile.Add<DepthOfField>(false);
                    _added = true;
                }
                if (Config.Dbg1) MelonLogger.Msg($"[ADS] blur on '{profile.name}' ({(_added ? "added" : "existing")} depth of field)");
                return _dof != null;
            }
            catch (Exception ex) { Fail(ex); return false; }
        }

        private static void Fail(Exception ex)
        {
            _failed = true;
            MelonLogger.Warning($"[ADS] blur unavailable, sighting without it: {ex.Message}");
        }
    }
}
