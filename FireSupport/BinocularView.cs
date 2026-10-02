using Il2CppPlayer.Cam;
using MelonLoader;
using Unity.Cinemachine;
using UnityEngine;

namespace BombsAway
{
    /// <summary>
    /// Looking through the binoculars: the main camera zooms to BinoZoom and mouse look slows to
    /// match, so a mouse movement covers the same share of the picture as without them. The
    /// game leaves the camera's field of view alone, so the rest value is kept here and put
    /// back on the way out. Look speed is scaled the way GunsGunsGuns does for its scopes: the
    /// player camera's Cinemachine pan/tilt is read each frame and the part of its movement since
    /// our last write that the zoom takes away is undone.
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

        public static float Ads => _ads;
        public static float Eased => _ads * _ads * (3f - 2f * _ads);

        /// <summary>Fully at the eyes: lasing and the reticle need this.</summary>
        public static bool Up => _ads >= 0.98f;

        /// <summary>The zoom applied right now (1 = none).</summary>
        public static float Magnification => 1f + (Mathf.Max(1f, Config.BinoZoom) - 1f) * Eased;

        /// <summary>The camera's field of view without the binoculars.</summary>
        public static float RestFov(Camera cam) => _applied && _restFov > 0f ? _restFov : (cam != null ? cam.fieldOfView : 60f);

        /// <summary>Every Update. <paramref name="want"/>: the binoculars in hand and right mouse held.</summary>
        public static void Tick(bool want, float dt)
        {
            _ads = Mathf.MoveTowards(_ads, want ? 1f : 0f, dt / Mathf.Max(0.01f, Config.BinoAdsTime));
            var cam = Camera.main;
            if (cam == null) return;

            if (_ads <= 0f)
            {
                if (_applied) { cam.fieldOfView = _restFov; _applied = false; }
                else if (!CameraFX.Shaking) _restFov = cam.fieldOfView;
                _tracking = false;
                return;
            }

            if (!_applied)
            {
                if (_restFov <= 0f || !CameraFX.Shaking) _restFov = cam.fieldOfView;
                _applied = true;
            }
            cam.fieldOfView = _restFov / Magnification;
            ScaleLook(Mathf.Clamp(Config.BinoSensitivity / Magnification, 0.02f, 1f));
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
        }

        public static void OnScene()
        {
            _applied = false;
            _ads = 0f;
            _restFov = -1f;
            _svc = null;
            _tracking = false;
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
