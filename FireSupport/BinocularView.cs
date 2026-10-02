using Il2CppPlayer.Appearances.God;
using Il2CppPlayer.Cam;
using MelonLoader;
using Unity.Cinemachine;
using UnityEngine;

namespace BombsAway
{
    /// <summary>
    /// Looking through the binoculars. Right mouse brings them up (Ads); the zoom is a separate
    /// choice of BinoZoomLevels, stepped with the wheel (as the Javelin's CLU steps WFOV / NFOV),
    /// that the screen shows at the hip too. While they're up, mouse look slows to match the zoom,
    /// so a mouse movement covers the same share of the picture as without them. With the held
    /// model's screen (LrfDisplay) the zoom is its camera's and the main camera stays as it is;
    /// without one (no held models) the main camera zooms under the eyepiece overlay. The game
    /// leaves the camera's field of view alone, so the rest value is kept here and put back on
    /// the way out. Look speed is slowed at the input: the game's GACameraRotation turns each
    /// frame's mouse delta into pan/tilt as delta x (1.5 x m_currentSensMult)^2 (its mouse
    /// setting writes that multiplier), so it is scaled by the square root of the slowdown while
    /// they're up and put back after. (5.8.3 undid part of the pan/tilt's movement afterwards
    /// instead, as GunsGunsGuns does for scopes; whenever the game's input ran after that, a
    /// frame showed the full-speed move before it was pulled back, which stuttered at high zoom.
    /// That remains only as the fallback if the rotation can't be found.)
    /// CameraFX's shake knows about the zoom (Magnification, RestFov) so its punch stays in scale.
    /// </summary>
    internal static class BinocularView
    {
        private static float _ads;            // 0 = lowered, 1 = at the eyes
        private static float _restFov = -1f;  // the camera's own field of view
        private static bool _applied;         // we have set the camera's field of view

        private static PlayerCameraService _svc;
        private static float _nextLookup;
        private static bool _tracking, _warned;
        private static float _lastTilt, _lastPan;

        private static GACameraRotation _rot;
        private static float _nextRotLookup;
        private static bool _sensHeld;          // we have slowed the game's look multiplier
        private static float _sensBase, _sensWritten;

        public static float Ads => _ads;
        public static float Eased => _ads * _ads * (3f - 2f * _ads);

        /// <summary>Fully at the eyes: lasing and the reticle need this.</summary>
        public static bool Up => _ads >= 0.98f;

        private static bool _zoomMain;        // the main camera zooms (no screen to show it on)

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
        public static float LookZoom => 1f + (Zoom - 1f) * Eased;

        /// <summary>The main camera's zoom right now: the binoculars' without a screen, else none.</summary>
        public static float Magnification => _zoomMain ? LookZoom : 1f;

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

        /// <summary>The camera's field of view without the binoculars.</summary>
        public static float RestFov(Camera cam) => _applied && _restFov > 0f ? _restFov : (cam != null ? cam.fieldOfView : 60f);

        /// <summary>Every Update. <paramref name="want"/>: the binoculars in hand and right mouse held.
        /// <paramref name="zoomMain"/>: no screen, so the main camera zooms.</summary>
        public static void Tick(bool want, float dt, bool zoomMain)
        {
            _ads = Mathf.MoveTowards(_ads, want ? 1f : 0f, dt / Mathf.Max(0.01f, Config.BinoAdsTime));
            _zoomMain = zoomMain;
            // Glide in log space, so 3 -> 7 and 7 -> 14 feel alike.
            float target = Level;
            _zoom = _zoom <= 0f ? target
                : Mathf.Exp(Mathf.Lerp(Mathf.Log(_zoom), Mathf.Log(target), 1f - Mathf.Exp(-dt / ZoomEase)));
            if (Mathf.Abs(_zoom - target) < 0.005f) _zoom = target;
            float sens = _ads > 0f ? Mathf.Clamp(Config.BinoSensitivity / LookZoom, 0.02f, 1f) : 1f;
            bool slowed = SlowLook(sens);
            var cam = Camera.main;
            if (cam == null) return;

            if (_ads <= 0f)
            {
                if (_applied) { cam.fieldOfView = _restFov; _applied = false; }
                else if (!CameraFX.Shaking) _restFov = cam.fieldOfView;
                _tracking = false;
                return;
            }

            if (zoomMain)
            {
                if (!_applied)
                {
                    if (_restFov <= 0f || !CameraFX.Shaking) _restFov = cam.fieldOfView;
                    _applied = true;
                }
                cam.fieldOfView = _restFov / LookZoom;
            }
            else if (_applied) { cam.fieldOfView = _restFov; _applied = false; }
            if (!slowed) ScaleLook(sens);
        }

        /// <summary>
        /// Slows mouse look to <paramref name="factor"/> of its speed at the game's own input
        /// (1 = put it back). False if the camera rotation can't be found.
        /// </summary>
        private static bool SlowLook(float factor)
        {
            var rot = Rotation();
            if (rot == null) return false;
            if (factor >= 0.999f) { RestoreLook(); return true; }
            float cur = rot.m_currentSensMult;
            // First frame, or the game wrote it since (the mouse setting changed): that's the base.
            if (!_sensHeld || Mathf.Abs(cur - _sensWritten) > 1e-5f) { _sensBase = cur; _sensHeld = true; }
            _sensWritten = _sensBase * Mathf.Sqrt(factor);   // the game squares it
            rot.m_currentSensMult = _sensWritten;
            return true;
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
            if (Config.Dbg1) MelonLogger.Msg($"[Binoculars] camera rotation {(_rot != null ? $"found, sens x{_rot.m_currentSensMult:F2}" : "not found: falling back to undoing pan/tilt")}");
            return _rot;
        }

        /// <summary>Back to the naked eye at once (holstered, scene change).</summary>
        public static void Reset()
        {
            if (_applied)
            {
                var cam = Camera.main;
                if (cam != null && _restFov > 0f) cam.fieldOfView = _restFov;
            }
            _applied = false;
            _ads = 0f;
            _tracking = false;
            RestoreLook();
        }

        public static void OnScene()
        {
            _applied = false;
            _ads = 0f;
            _restFov = -1f;
            _zoom = -1f;   // the chosen level is kept; only the glide restarts
            _svc = null;
            _tracking = false;
            _sensHeld = false;   // the scene's player, and its multiplier, are new
            _rot = null;
            _nextRotLookup = 0f;
        }

        private static void ScaleLook(float sens)
        {
            var pt = PanTilt();
            if (pt == null) return;
            var tilt = pt.TiltAxis;
            var pan = pt.PanAxis;
            float t = tilt.Value, n = pan.Value;
            if (_tracking)
            {
                float dTilt = t - _lastTilt, dPan = n - _lastPan;
                if (Mathf.Abs(dPan) > 90f) dPan = 0f;   // wrapped round
                float take = 1f - sens;
                t -= dTilt * take;
                n -= dPan * take;
                if (tilt.Range.y > tilt.Range.x) t = Mathf.Clamp(t, tilt.Range.x, tilt.Range.y);
                tilt.Value = t; pt.TiltAxis = tilt;
                pan.Value = n; pt.PanAxis = pan;
            }
            _lastTilt = t; _lastPan = n;
            _tracking = true;
        }

        private static CinemachinePanTilt PanTilt()
        {
            if (_svc == null)
            {
                if (Time.time < _nextLookup) return null;
                _nextLookup = Time.time + 2f;
                _svc = FruitLib.FruitScene.First<PlayerCameraService>();
                if (_svc == null) return null;
            }
            var pt = _svc.m_panTilt;
            if (pt == null && !_warned)
            {
                _warned = true;
                MelonLogger.Warning("[Binoculars] the camera has no pan/tilt: look speed isn't slowed while zoomed.");
            }
            return pt;
        }
    }
}
