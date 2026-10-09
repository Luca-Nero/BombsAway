using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// A texture's pixels on the CPU, drawn from the top-left with PixelFont's glyphs: the
    /// terminals' windows, the rangefinder's and the CLU's HUDs. Upload puts them on the texture.
    /// </summary>
    internal sealed class PixelCanvas
    {
        public readonly int W, H;
        public readonly Color32[] Px;
        private readonly int _textEdge;   // text stops at a glyph starting past this x
        private Il2CppStructArray<Color32> _native;   // Upload's copy in IL2CPP memory, kept (see Upload)

        /// <param name="textMargin">Text is cut where a glyph would start closer than this to the right edge (the terminals' 4); -1 = never.</param>
        public PixelCanvas(int w, int h, int textMargin = -1)
        {
            W = w; H = h;
            Px = new Color32[w * h];
            _textEdge = textMargin < 0 ? int.MaxValue : w - textMargin;
        }

        /// <summary>Width of <paramref name="s"/> in PixelFont's 5x7 glyphs, one column apart.</summary>
        public static int Width(string s) => string.IsNullOrEmpty(s) ? 0 : s.Length * (PixelFont.GW + 1) - 1;

        public void Clear(Color32 c)
        {
            for (int i = 0; i < Px.Length; i++) Px[i] = c;
        }

        /// <summary>x, y from the top-left.</summary>
        public void Put(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            Px[(H - 1 - y) * W + x] = c;
        }

        public void Fill(int x, int y, int w, int h, Color32 c)
        {
            for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) Put(x + i, y + j, c);
        }

        /// <summary>A frame <paramref name="t"/> pixels thick, inside the box.</summary>
        public void Outline(int x, int y, int w, int h, Color32 c, int t = 1)
        {
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                    if (i < t || j < t || i >= w - t || j >= h - t) Put(x + i, y + j, c);
        }

        /// <summary>PixelFont's 5x7 glyphs with the top-left at (x, y). Lower case is drawn as upper.</summary>
        public void Text(string s, int x, int y, Color32 c)
        {
            if (string.IsNullOrEmpty(s)) return;
            foreach (char raw in s)
            {
                if (x > _textEdge) return;
                var g = PixelFont.Bits(char.ToUpperInvariant(raw));
                if (g != null)
                    for (int r = 0; r < PixelFont.GH; r++)
                        for (int col = 0; col < PixelFont.GW; col++)
                            if (g[r * PixelFont.GW + col] == '#') Put(x + col, y + r, c);
                x += PixelFont.GW + 1;
            }
        }

        public void TextCentred(string s, int cx, int y, Color32 c) => Text(s, cx - Width(s) / 2, y, c);

        /// <summary>PixelFont's 3x5 glyphs at <paramref name="scale"/>, one glyph column apart.</summary>
        public void SmallText(string s, int x, int y, int scale, Color32 c)
        {
            foreach (char ch in s)
            {
                var g = PixelFont.SmallBits(ch);
                if (g != null)
                    for (int r = 0; r < PixelFont.SH; r++)
                        for (int col = 0; col < PixelFont.SW; col++)
                            if (g[r * PixelFont.SW + col] == '#') Fill(x + col * scale, y + r * scale, scale, scale, c);
                x += (PixelFont.SW + 1) * scale;
            }
        }

        /// <summary>
        /// Px onto <paramref name="tex"/>. SetPixels32 takes an IL2CPP array: handed a managed
        /// one it allocates and copies a fresh native array every call, so the canvas keeps one
        /// and copies into it (one lookup of its memory, then a block copy).
        /// </summary>
        public void Upload(Texture2D tex)
        {
            if (_native == null) _native = new Il2CppStructArray<Color32>(Px.Length);
            new System.Span<Color32>(Px).CopyTo(NativeSpan.Of(_native, Px.Length));
            tex.SetPixels32(_native);
            tex.Apply(false);
        }

        /// <summary>A point-filtered texture for a canvas (or any pixel-drawn marker), kept over scene unloads.</summary>
        public static Texture2D NewTexture(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };

        /// <summary>Shows <paramref name="t"/> on <paramref name="m"/>: URP's base map, and the main texture for anything else.</summary>
        public static void SetTex(Material m, Texture t)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            m.mainTexture = t;
        }

        /// <summary>
        /// A quad facing -Z with UVs 0..1, from (x0, y0) to (x1, y1) in its own space: the
        /// windows and screens these canvases are shown on.
        /// </summary>
        public static Mesh Quad(float x0, float y0, float x1, float y1)
        {
            var mesh = new Mesh { hideFlags = HideFlags.DontUnloadUnusedAsset };
            mesh.SetVertices(new[] { new Vector3(x0, y0, 0), new Vector3(x1, y0, 0), new Vector3(x1, y1, 0), new Vector3(x0, y1, 0) });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A quad centred on its origin, <paramref name="hw"/> by <paramref name="hh"/> half size.</summary>
        public static Mesh Quad(float hw, float hh) => Quad(-hw, -hh, hw, hh);
    }
}
