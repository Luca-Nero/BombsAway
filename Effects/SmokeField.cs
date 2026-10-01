using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Color = UnityEngine.Color;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// A smoke cloud's columns as the texture SmokeVolume.shader reads (same code as the Unity
    /// preview, SmokeVolumeKit.FieldWriter): one texel per column, Size across, centred on the can.
    /// R ground, G depth (blurred so the pile is a dome, not a peak with cliffs), A the highest
    /// smoke top within two texels (the shader skips clear air above it); heights relative to the
    /// can's ground. Empty texels take the nearest column's ground; edge columns (over a drop) sit
    /// EdgeFall lower, so the smoke runs down off the edge.
    /// </summary>
    internal sealed class SmokeField
    {
        public struct Col { public int X, Z; public float Ground, Depth; public bool Edge; }

        public const int Size = 64;
        public const float EdgeFall = 5f;
        public const int BlurPasses = 3;

        public readonly Texture2D Tex;
        public Vector3 BoxMin, BoxMax;

        private readonly Il2CppStructArray<Color> _px = new Il2CppStructArray<Color>(Size * Size);
        private readonly float[] _ground = new float[Size * Size], _depth = new float[Size * Size], _top = new float[Size * Size], _tmp = new float[Size * Size];
        private readonly bool[] _known = new bool[Size * Size];
        private readonly Queue<int> _q = new Queue<int>();
        private int _builtFor = -1;

        public SmokeField()
        {
            Tex = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false);
            Tex.name = "SmokeField";
            Tex.wrapMode = TextureWrapMode.Clamp;
            Tex.filterMode = FilterMode.Bilinear;
        }

        /// <summary>Writes the field; false if nothing shows.</summary>
        public bool Write(List<Col> cols, Vector3 origin, float cell, float rise, float margin)
        {
            const int H = Size / 2;
            if (cols.Count != _builtFor) Grounds(cols);
            System.Array.Clear(_depth, 0, _depth.Length);
            int minI = Size, minJ = Size, maxI = -1, maxJ = -1;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var c in cols)
            {
                int i = c.X + H, j = c.Z + H;
                if (i < 0 || j < 0 || i >= Size || j >= Size) continue;
                int k = j * Size + i;
                if (c.Depth <= _depth[k]) continue;
                _depth[k] = c.Depth;
                if (c.Depth < 0.01f) continue;
                minI = Mathf.Min(minI, i); maxI = Mathf.Max(maxI, i);
                minJ = Mathf.Min(minJ, j); maxJ = Mathf.Max(maxJ, j);
                float g = _ground[k];
                lo = Mathf.Min(lo, g); hi = Mathf.Max(hi, g + c.Depth);
            }
            if (maxI < 0) return false;

            for (int pass = 0; pass < 2 * BlurPasses; pass++)
            {
                bool rows = (pass & 1) == 0;
                var src = rows ? _depth : _tmp;
                var dst = rows ? _tmp : _depth;
                for (int j = 0; j < Size; j++)
                    for (int i = 0; i < Size; i++)
                    {
                        float s = 2f * src[j * Size + i], wsum = 2f;
                        for (int d = -1; d <= 1; d += 2)
                        {
                            int a = rows ? i + d : i, b = rows ? j : j + d;
                            if (a < 0 || b < 0 || a >= Size || b >= Size) continue;
                            s += src[b * Size + a]; wsum += 1f;
                        }
                        dst[j * Size + i] = s / wsum;
                    }
            }

            for (int k = 0; k < _top.Length; k++) _top[k] = _depth[k] > 0.01f ? _ground[k] + _depth[k] : -100f;
            for (int pass = 0; pass < 2; pass++)
            {
                var src = pass == 0 ? _top : _tmp;
                var dst = pass == 0 ? _tmp : _top;
                for (int j = 0; j < Size; j++)
                    for (int i = 0; i < Size; i++)
                    {
                        float m = -100f;
                        for (int d = -2; d <= 2; d++)
                        {
                            int a = pass == 0 ? i + d : i, b = pass == 0 ? j : j + d;
                            if (a < 0 || b < 0 || a >= Size || b >= Size) continue;
                            m = Mathf.Max(m, src[b * Size + a]);
                        }
                        dst[j * Size + i] = m;
                    }
            }
            for (int k = 0; k < Size * Size; k++) _px[k] = new Color(_ground[k], _depth[k], 0f, _top[k]);
            Tex.SetPixels(_px);
            Tex.Apply(false);

            float x0 = origin.x + (minI - H) * cell, x1 = origin.x + (maxI - H) * cell;
            float z0 = origin.z + (minJ - H) * cell, z1 = origin.z + (maxJ - H) * cell;
            BoxMin = new Vector3(x0 - margin, origin.y + lo - 0.5f, z0 - margin);
            BoxMax = new Vector3(x1 + margin, origin.y + hi + rise, z1 + margin);
            return true;
        }

        private void Grounds(List<Col> cols)
        {
            const int H = Size / 2;
            _builtFor = cols.Count;
            System.Array.Clear(_known, 0, _known.Length);
            _q.Clear();
            foreach (var c in cols)
            {
                int i = c.X + H, j = c.Z + H;
                if (i < 0 || j < 0 || i >= Size || j >= Size) continue;
                int k = j * Size + i;
                float g = c.Edge ? c.Ground - EdgeFall : c.Ground;
                if (_known[k] && _ground[k] >= g) continue;
                if (!_known[k]) _q.Enqueue(k);
                _known[k] = true;
                _ground[k] = g;
            }
            while (_q.Count > 0)
            {
                int k = _q.Dequeue(), i = k % Size, j = k / Size;
                for (int d = 0; d < 4; d++)
                {
                    int a = i + (d == 0 ? 1 : d == 1 ? -1 : 0), b = j + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (a < 0 || b < 0 || a >= Size || b >= Size) continue;
                    int n = b * Size + a;
                    if (_known[n]) continue;
                    _known[n] = true;
                    _ground[n] = _ground[k];
                    _q.Enqueue(n);
                }
            }
        }
    }
}
