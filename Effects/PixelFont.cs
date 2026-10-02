using System.Collections.Generic;
using UnityEngine;
using Color = UnityEngine.Color;

namespace BombsAway
{
    /// <summary>
    /// A 5x7 DOS-style pixel font drawn on screen through IMGUI, for the binoculars' readout and
    /// the radio log. One small point-filtered texture per glyph, tinted by GUI.color: the
    /// game's build strips GUI.DrawTextureWithTexCoords, so there is no atlas. Call from OnGUI.
    /// </summary>
    internal static class PixelFont
    {
        public const int GW = 5, GH = 7;   // glyph size in font pixels; one column of gap after each

        // Seven rows top to bottom, space separated, '#' lit.
        private static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
        {
            ['A'] = ".###. #...# #...# ##### #...# #...# #...#",
            ['B'] = "####. #...# #...# ####. #...# #...# ####.",
            ['C'] = ".###. #...# #.... #.... #.... #...# .###.",
            ['D'] = "####. #...# #...# #...# #...# #...# ####.",
            ['E'] = "##### #.... #.... ####. #.... #.... #####",
            ['F'] = "##### #.... #.... ####. #.... #.... #....",
            ['G'] = ".###. #...# #.... #.### #...# #...# .####",
            ['H'] = "#...# #...# #...# ##### #...# #...# #...#",
            ['I'] = ".###. ..#.. ..#.. ..#.. ..#.. ..#.. .###.",
            ['J'] = "..### ...#. ...#. ...#. ...#. #..#. .##..",
            ['K'] = "#...# #..#. #.#.. ##... #.#.. #..#. #...#",
            ['L'] = "#.... #.... #.... #.... #.... #.... #####",
            ['M'] = "#...# ##.## #.#.# #.#.# #...# #...# #...#",
            ['N'] = "#...# #...# ##..# #.#.# #..## #...# #...#",
            ['O'] = ".###. #...# #...# #...# #...# #...# .###.",
            ['P'] = "####. #...# #...# ####. #.... #.... #....",
            ['Q'] = ".###. #...# #...# #...# #.#.# #..#. .##.#",
            ['R'] = "####. #...# #...# ####. #.#.. #..#. #...#",
            ['S'] = ".#### #.... #.... .###. ....# ....# ####.",
            ['T'] = "##### ..#.. ..#.. ..#.. ..#.. ..#.. ..#..",
            ['U'] = "#...# #...# #...# #...# #...# #...# .###.",
            ['V'] = "#...# #...# #...# #...# #...# .#.#. ..#..",
            ['W'] = "#...# #...# #...# #.#.# #.#.# #.#.# .#.#.",
            ['X'] = "#...# #...# .#.#. ..#.. .#.#. #...# #...#",
            ['Y'] = "#...# #...# .#.#. ..#.. ..#.. ..#.. ..#..",
            ['Z'] = "##### ....# ...#. ..#.. .#... #.... #####",
            ['0'] = ".###. #...# #..## #.#.# ##..# #...# .###.",
            ['1'] = "..#.. .##.. ..#.. ..#.. ..#.. ..#.. .###.",
            ['2'] = ".###. #...# ....# ...#. ..#.. .#... #####",
            ['3'] = "##### ...#. ..#.. ...#. ....# #...# .###.",
            ['4'] = "...#. ..##. .#.#. #..#. ##### ...#. ...#.",
            ['5'] = "##### #.... ####. ....# ....# #...# .###.",
            ['6'] = "..##. .#... #.... ####. #...# #...# .###.",
            ['7'] = "##### ....# ...#. ..#.. .#... .#... .#...",
            ['8'] = ".###. #...# #...# .###. #...# #...# .###.",
            ['9'] = ".###. #...# #...# .#### ....# ...#. .##..",
            ['.'] = "..... ..... ..... ..... ..... .##.. .##..",
            [','] = "..... ..... ..... ..... .##.. ..#.. .#...",
            [':'] = "..... .##.. .##.. ..... .##.. .##.. .....",
            ['-'] = "..... ..... ..... .###. ..... ..... .....",
            ['+'] = "..... ..#.. ..#.. ##### ..#.. ..#.. .....",
            ['/'] = "....# ...#. ...#. ..#.. .#... .#... #....",
            ['>'] = ".#... ..#.. ...#. ....# ...#. ..#.. .#...",
            ['<'] = "...#. ..#.. .#... #.... .#... ..#.. ...#.",
            ['%'] = "##... ##..# ...#. ..#.. .#... #..## ...##",
            ['#'] = ".#.#. .#.#. ##### .#.#. ##### .#.#. .#.#.",
            ['('] = "...#. ..#.. .#... .#... .#... ..#.. ...#.",
            [')'] = ".#... ..#.. ...#. ...#. ...#. ..#.. .#...",
            ['?'] = ".###. #...# ....# ...#. ..#.. ..... ..#..",
            ['!'] = "..#.. ..#.. ..#.. ..#.. ..#.. ..... ..#..",
            ['\''] ="..#.. ..#.. .#... ..... ..... ..... .....",
            ['_'] = "..... ..... ..... ..... ..... ..... #####",
            ['='] = "..... ..... ##### ..... ##### ..... .....",
            ['*'] = "..... ..#.. #.#.# .###. #.#.# ..#.. .....",
            ['['] = ".###. .#... .#... .#... .#... .#... .###.",
            [']'] = ".###. ...#. ...#. ...#. ...#. ...#. .###.",
            ['|'] = "..#.. ..#.. ..#.. ..#.. ..#.. ..#.. ..#..",
            ['°'] = ".##.. #..#. .##.. ..... ..... ..... .....",
        };

        private static readonly Dictionary<char, Texture2D> _tex = new Dictionary<char, Texture2D>();
        private static Texture2D _white;

        /// <summary>A plain white texture: rectangles and bars drawn with GUI.color.</summary>
        public static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply(false);
                }
                return _white;
            }
        }

        private static Texture2D Glyph(char c)
        {
            if (_tex.TryGetValue(c, out var t) && t != null) return t;
            if (!Glyphs.TryGetValue(c, out var def)) return null;
            string rows = def.Replace(" ", "");
            t = new Texture2D(GW, GH, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[GW * GH];
            for (int y = 0; y < GH; y++)
                for (int x = 0; x < GW; x++)
                    px[(GH - 1 - y) * GW + x] = rows[y * GW + x] == '#' ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            t.SetPixels32(px);
            t.Apply(false);
            _tex[c] = t;
            return t;
        }

        /// <summary>Screen width of <paramref name="s"/> at <paramref name="px"/> screen pixels per font pixel.</summary>
        public static float Width(string s, float px) => s.Length == 0 ? 0f : (s.Length * (GW + 1) - 1) * px;

        public static float Height(float px) => GH * px;

        /// <summary>Draws <paramref name="s"/> with its top-left at (x, y) in GUI coordinates. Lower case is drawn as upper.</summary>
        public static void Draw(string s, float x, float y, float px, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            float step = (GW + 1) * px;
            foreach (char raw in s)
            {
                char ch = char.ToUpperInvariant(raw);
                if (ch != ' ')
                {
                    var g = Glyph(ch);
                    if (g != null) GUI.DrawTexture(new Rect(Mathf.Round(x), Mathf.Round(y), GW * px, GH * px), g);
                }
                x += step;
            }
            GUI.color = prev;
        }

        /// <summary>Centred on <paramref name="cx"/>.</summary>
        public static void DrawCentred(string s, float cx, float y, float px, Color c) => Draw(s, cx - Width(s, px) * 0.5f, y, px, c);

        public static void Box(float x, float y, float w, float h, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(Mathf.Round(x), Mathf.Round(y), Mathf.Max(1f, Mathf.Round(w)), Mathf.Max(1f, Mathf.Round(h))), White);
            GUI.color = prev;
        }

        public static void Clear()
        {
            foreach (var t in _tex.Values) if (t != null) Object.Destroy(t);
            _tex.Clear();
        }
    }
}
