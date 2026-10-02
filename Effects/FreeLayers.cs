using System.Collections.Generic;
using UnityEngine;

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
    }
}
