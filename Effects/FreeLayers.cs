using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BombsAway
{
    /// <summary>
    /// Unnamed layers handed out to whoever needs one of their own (the viewmodel camera, the
    /// smoke), from the top down so they miss GGG's (bottom up). One per user, kept for the
    /// session, so asking again gives the same layer.
    /// </summary>
    internal static class FreeLayers
    {
        private static readonly Dictionary<string, int> _taken = new Dictionary<string, int>();

        /// <summary>The layer for <paramref name="who"/>, or -1 if none is free.</summary>
        public static int Take(string who)
        {
            if (_taken.TryGetValue(who, out int mine)) return mine;
            for (int i = 31; i >= 8; i--)
            {
                if (!string.IsNullOrEmpty(LayerMask.LayerToName(i)) || _taken.ContainsValue(i)) continue;
                _taken[who] = i;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// A layer for <paramref name="who"/>'s transparent things (the smoke, the shock fronts), only
        /// if every URP renderer draws transparents on it: on a layer left out they would vanish
        /// for everyone. -1 otherwise, with a warning that ends in <paramref name="fallback"/>.
        /// </summary>
        public static int TakeTransparent(string who, string fallback)
        {
            try
            {
                int l = Take(who);
                if (l < 0) { MelonLogger.Warning($"[{who}] no free layer: {fallback}."); return -1; }
                var asset = UniversalRenderPipeline.asset;
                var list = asset != null ? asset.m_RendererDataList : null;
                if (list != null)
                    foreach (var d in list)
                    {
                        var u = d != null ? d.TryCast<UniversalRendererData>() : null;
                        if (u != null && (u.transparentLayerMask.value & (1 << l)) == 0)
                        {
                            MelonLogger.Warning($"[{who}] the renderer doesn't draw transparent layer {l}: {fallback}.");
                            return -1;
                        }
                    }
                return l;
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[{who}] picking a layer failed ({e.Message}): {fallback}.");
                return -1;
            }
        }
    }
}
