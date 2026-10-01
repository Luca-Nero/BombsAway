using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BombsAway
{
    /// <summary>
    /// The camera depth texture, which the smoke volume needs (its rays stop at whatever stands in
    /// the smoke). Turned on in the pipeline asset and on the main camera the first time a cloud
    /// starts; left on.
    /// </summary>
    internal static class DepthTexture
    {
        private static bool _logged;

        public static void Require()
        {
            try
            {
                var rp = GraphicsSettings.currentRenderPipeline?.TryCast<UniversalRenderPipelineAsset>();
                bool was = rp != null && rp.supportsCameraDepthTexture;
                if (rp != null && !was) rp.supportsCameraDepthTexture = true;
                var cam = Camera.main;
                var data = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
                if (data != null && data.requiresDepthOption == CameraOverrideOption.Off)
                    data.requiresDepthOption = CameraOverrideOption.UsePipelineSettings;
                if (!_logged)
                {
                    _logged = true;
                    MelonLogger.Msg($"[Smoke] depth texture: pipeline {(rp == null ? "not URP" : was ? "already on" : "turned on")}, camera {(data == null ? "no URP data" : data.requiresDepthOption.ToString())}");
                }
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning($"[Smoke] couldn't turn the depth texture on: {e.Message}");
            }
        }
    }
}
