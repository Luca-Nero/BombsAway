using Il2CppPlayer.Appearances.God;
using Il2CppPlayer.Cam;
using MelonLoader;
using UnityEngine;

namespace BombsAway
{
    /// <summary>
    /// Slows mouse look while a magnifying sight is up (the binoculars' screen, the Javelin's
    /// CLU), so a mouse movement covers the same share of the picture as without it. Slowed at
    /// the input: the game's GACameraRotation turns each frame's mouse delta into pan/tilt as
    /// delta x (1.5 x m_currentSensMult)^2 (its mouse setting writes that multiplier), so it is
    /// scaled by the square root of the slowdown while a sight asks for it and put back after.
    /// Sights <see cref="Request"/> each frame; <see cref="Apply"/> once after them writes the
    /// slowest asked for, or puts it back when nobody asked.
    /// </summary>
    internal static class LookSpeed
    {
        private static GACameraRotation _rot;
        private static float _nextRotLookup;
        private static bool _held;              // we have slowed the game's look multiplier
        private static float _base, _written;
        private static float _want = 1f;        // this frame's slowest request

        /// <summary>This frame, look at most <paramref name="factor"/> of its speed (1 = untouched).</summary>
        public static void Request(float factor) => _want = Mathf.Min(_want, factor);

        /// <summary>Once a frame, after every sight has asked.</summary>
        public static void Apply()
        {
            float factor = _want;
            _want = 1f;
            if (factor >= 0.999f) { Restore(); return; }
            var rot = Rotation();
            if (rot == null) return;
            float cur = rot.m_currentSensMult;
            // First frame, or the game wrote it since (the mouse setting changed): that's the base.
            if (!_held || Mathf.Abs(cur - _written) > 1e-5f) { _base = cur; _held = true; }
            _written = _base * Mathf.Sqrt(Mathf.Max(0.0004f, factor));   // the game squares it
            rot.m_currentSensMult = _written;
        }

        private static void Restore()
        {
            if (!_held) return;
            _held = false;
            // Unless the game has written its own value since.
            if (_rot != null && Mathf.Abs(_rot.m_currentSensMult - _written) <= 1e-5f) _rot.m_currentSensMult = _base;
        }

        private static GACameraRotation Rotation()
        {
            if (_rot != null) return _rot;
            if (Time.time < _nextRotLookup) return null;
            _nextRotLookup = Time.time + 2f;
            var refs = FruitLib.FruitScene.First<GAReferences>();
            _rot = refs != null ? refs.CameraRotation : null;
            if (Config.Dbg1) MelonLogger.Msg($"[LookSpeed] camera rotation {(_rot != null ? $"found, sens x{_rot.m_currentSensMult:F2}" : "not found: look speed isn't slowed")}");
            return _rot;
        }

        public static void OnScene()
        {
            _held = false;   // the scene's player, and its multiplier, are new
            _rot = null;
            _nextRotLookup = 0f;
            _want = 1f;
        }
    }
}
