using Il2CppPlayer.Appearances.God;
using Il2CppPlayer.Cam;
using MelonLoader;
using UnityEngine;

namespace BombsAway
{
    /// <summary>
    /// Looking through the binoculars. Right mouse brings them up (Ads); the zoom is a separate
    /// choice of BinoZoomLevels, stepped with the wheel (as the Javelin's CLU steps WFOV / NFOV),
    /// that the screen shows at the hip too. While they're up, mouse look slows to match the zoom,
    /// so a mouse movement covers the same share of the picture as without them. The zoom is the
    /// held model's screen camera's (LrfDisplay); the main camera stays as it is. Look speed is
    /// slowed at the input: the game's GACameraRotation turns each frame's mouse delta into
    /// pan/tilt as delta x (1.5 x m_currentSensMult)^2 (its mouse setting writes that
    /// multiplier), so it is scaled by the square root of the slowdown while they're up and put
    /// back after.
    /// </summary>
    internal static class BinocularView
    {
        private static float _ads;            // 0 = lowered, 1 = at the eyes

        private static GACameraRotation _rot;
        private static float _nextRotLookup;
        private static bool _sensHeld;          // we have slowed the game's look multiplier
        private static float _sensBase, _sensWritten;

        public static float Ads => _ads;
        private static float Eased => _ads * _ads * (3f - 2f * _ads);

        /// <summary>Fully at the eyes: lasing and the reticle need this.</summary>
        public static bool Up => _ads >= 0.98f;

        private const float ZoomEase = 0.06f; // seconds: a step glides rather than snaps
        private static string _levelsFrom;
        private static float[] _levels = { 7f };
        private static int _level = -1;       // index into _levels; -1 = not chosen yet (the middle one)
        private static float _zoom = -1f;     // the screen's zoom, gliding toward the chosen level

        /// <summary>The zoom levels from BinoZoomLevels ("3, 7, 14"), ascending, each at least 1.</summary>
        private static float[] Levels
        {
            get
            {
                if (_levelsFrom == Config.BinoZoomLevels) return _levels;
                _levelsFrom = Config.BinoZoomLevels;
                var list = new System.Collections.Generic.List<float>();
                foreach (var part in (_levelsFrom ?? "").Split(','))
                    if (float.TryParse(part.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z) && z >= 1f)
                        list.Add(z);
                if (list.Count == 0) list.Add(7f);
                list.Sort();
                _levels = list.ToArray();
                _level = Mathf.Clamp(_level, -1, _levels.Length - 1);
                return _levels;
            }
        }

        /// <summary>The chosen zoom level (where the zoom glides to).</summary>
        public static float Level
        {
            get
            {
                var lv = Levels;
                if (_level < 0) _level = lv.Length / 2;
                return lv[_level];
            }
        }

        /// <summary>The screen's optical zoom right now (1 = none), whether up or not.</summary>
        public static float Zoom => _zoom > 0f ? _zoom : Level;

        /// <summary>The zoom the eye gets: none at the hip, the screen's at the eye.</summary>
        private static float LookZoom => 1f + (Zoom - 1f) * Eased;

        /// <summary>The wheel: +1 a level narrower, -1 a level wider; false at the end of the range.</summary>
        public static bool Step(int dir)
        {
            float before = Level;
            _level = Mathf.Clamp(_level + dir, 0, Levels.Length - 1);
            return Level != before;
        }

        /// <summary>Middle click: the next level, round to the widest after the narrowest.</summary>
        public static bool Cycle()
        {
            float before = Level;
            _level = (_level + 1) % Levels.Length;
            return Level != before;
        }

        /// <summary>Every Update. <paramref name="want"/>: the binoculars in hand and right mouse held.</summary>
        public static void Tick(bool want, float dt)
        {
            _ads = Mathf.MoveTowards(_ads, want ? 1f : 0f, dt / Mathf.Max(0.01f, Config.BinoAdsTime));
            // Glide in log space, so 3 -> 7 and 7 -> 14 feel alike.
            float target = Level;
            _zoom = _zoom <= 0f ? target
                : Mathf.Exp(Mathf.Lerp(Mathf.Log(_zoom), Mathf.Log(target), 1f - Mathf.Exp(-dt / ZoomEase)));
            if (Mathf.Abs(_zoom - target) < 0.005f) _zoom = target;
            SlowLook(_ads > 0f ? Mathf.Clamp(Config.BinoSensitivity / LookZoom, 0.02f, 1f) : 1f);
        }

        /// <summary>
        /// Slows mouse look to <paramref name="factor"/> of its speed at the game's own input
        /// (1 = put it back).
        /// </summary>
        private static void SlowLook(float factor)
        {
            var rot = Rotation();
            if (rot == null) return;
            if (factor >= 0.999f) { RestoreLook(); return; }
            float cur = rot.m_currentSensMult;
            // First frame, or the game wrote it since (the mouse setting changed): that's the base.
            if (!_sensHeld || Mathf.Abs(cur - _sensWritten) > 1e-5f) { _sensBase = cur; _sensHeld = true; }
            _sensWritten = _sensBase * Mathf.Sqrt(factor);   // the game squares it
            rot.m_currentSensMult = _sensWritten;
        }

        private static void RestoreLook()
        {
            if (!_sensHeld) return;
            _sensHeld = false;
            // Unless the game has written its own value since.
            if (_rot != null && Mathf.Abs(_rot.m_currentSensMult - _sensWritten) <= 1e-5f) _rot.m_currentSensMult = _sensBase;
        }

        private static GACameraRotation Rotation()
        {
            if (_rot != null) return _rot;
            if (Time.time < _nextRotLookup) return null;
            _nextRotLookup = Time.time + 2f;
            var refs = FruitLib.FruitScene.First<GAReferences>();
            _rot = refs != null ? refs.CameraRotation : null;
            if (Config.Dbg1) MelonLogger.Msg($"[Binoculars] camera rotation {(_rot != null ? $"found, sens x{_rot.m_currentSensMult:F2}" : "not found: look speed isn't slowed")}");
            return _rot;
        }

        /// <summary>Back to the naked eye at once (holstered, scene change).</summary>
        public static void Reset()
        {
            _ads = 0f;
            RestoreLook();
        }

        public static void OnScene()
        {
            _ads = 0f;
            _zoom = -1f;   // the chosen level is kept; only the glide restarts
            _sensHeld = false;   // the scene's player, and its multiplier, are new
            _rot = null;
            _nextRotLookup = 0f;
        }
    }
}
