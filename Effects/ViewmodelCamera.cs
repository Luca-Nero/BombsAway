using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BombsAway
{
    /// <summary>
    /// A viewmodel camera, as GunsGunsGuns does it (View/AkViewmodel.cs): an overlay camera in
    /// the world camera's URP stack that draws one free layer with a 1 cm near plane, the layer
    /// taken out of the world camera. A held model on that layer is never cut open by the world
    /// camera's 10 cm near plane (the AT-4's tube runs back past the cheek at the eye), and never
    /// sinks into a wall. The game resets the world camera's own near plane, so pulling that in
    /// does not hold. The layer is picked from the top down so it misses GGG's (bottom up).
    /// </summary>
    internal static class ViewmodelCamera
    {
        private const float Near = 0.01f;
        private const float Far = 30f;

        private static Camera _cam, _baseCam;
        private static int _layer = -1;
        private static bool _attempted;

        /// <summary>The layer to put a held model on, or -1 to leave it where it is.</summary>
        public static int Layer(Camera baseCam)
        {
            Ensure(baseCam);
            return _cam != null ? _layer : -1;
        }

        public static void Reset()
        {
            _cam = null; _baseCam = null;
            _layer = -1;
            _attempted = false;
        }

        /// <summary>Every frame while it is in use: the world camera's field of view may change.</summary>
        public static void Sync()
        {
            if (_cam != null && _baseCam != null) _cam.fieldOfView = _baseCam.fieldOfView;
        }

        private static void Ensure(Camera baseCam)
        {
            if (baseCam == null) return;
            if (_baseCam == baseCam && (_cam != null || _attempted)) return;

            _attempted = true;
            _baseCam = baseCam;
            _cam = null;
            try
            {
                int layer = FreeLayer();
                if (layer < 0) { MelonLogger.Warning("[Viewmodel] no free layer; held models stay on the world camera."); return; }
                var baseData = baseCam.GetComponent<UniversalAdditionalCameraData>();
                if (baseData == null) { MelonLogger.Warning("[Viewmodel] the world camera has no URP data; held models stay on it."); return; }

                var go = new GameObject("BA_ViewmodelCam");
                go.transform.SetParent(baseCam.transform, false);
                var cam = go.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.Depth;
                cam.cullingMask = 1 << layer;
                cam.nearClipPlane = Near;
                cam.farClipPlane = Far;
                cam.fieldOfView = baseCam.fieldOfView;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderType = CameraRenderType.Overlay;

                baseCam.cullingMask &= ~(1 << layer);
                baseData.cameraStack.Add(cam);
                _cam = cam;
                _layer = layer;
                if (Config.Dbg1) MelonLogger.Msg($"[Viewmodel] overlay camera on layer {layer}.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[Viewmodel] could not set up the overlay camera: {e.Message}");
                _cam = null;
            }
        }

        private static int FreeLayer()
        {
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return -1;
        }
    }
}
