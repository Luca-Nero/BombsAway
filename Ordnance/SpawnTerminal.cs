using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// Equipping and putting away, done from the command line (a prototype, SpawnTerminal).
    ///
    /// Spawn: the DOS window (Effects/DosTerminal.cs) pops up beside the hand and types
    /// SPAWN BA:&lt;ITEM&gt;, and while it runs the item's data scrolls past (what it is, its charge
    /// and fuse as the mod has them, its mesh and atlas as loaded) and the held model is fought
    /// into existence texel by texel (ModelPixels) for SpawnTime: each texel flips between there
    /// and not there until all of it holds. Then OBJECT LIVE (OnLive: the hand settles it), the
    /// model gets its own materials back, and the window folds away. Until then it can't be
    /// thrown or fired.
    ///
    /// Despawn: the model is let go of where it is (frozen in view, parented to the camera) and
    /// fought out of existence the same way over DespawnTime, while the bytes it was made of rise
    /// off it as hex (ByteSpray) and the window runs FREE BA:&lt;ITEM&gt;. A spawn straight after (a
    /// swap) takes the window over; the old model keeps dissolving on its own clock.
    /// </summary>
    internal sealed class SpawnTerminal
    {
        private static readonly Color32 Void = new Color32(34, 34, 36, 255);
        private static readonly Color32 Spark = new Color32(255, 236, 170, 255);
        private const float DespawnLead = 0.1f;    // the model hangs this long before it starts to go
        private const int Bytes = 40;              // hex bytes that rise off a whole model

        private readonly DosTerminal _term = new DosTerminal
        {
            Title = "SPAWN.EXE", FightStep = 1f / 24f,
            CellOld = Void, CellNew = DosTerminal.Bar, CellStatic = Spark,
        };
        private readonly ModelPixels _pixels = new ModelPixels();
        private readonly Action<float> _spawnRound;
        private GameObject _target;

        // The model being put away, and its own clock.
        private readonly ModelPixels _gonePixels = new ModelPixels();
        private readonly ByteSpray _bytes = new ByteSpray();
        private GameObject _gone;
        private Bounds _goneBounds;                // camera space, at the moment it was let go
        private float _goneAt;
        private int _goneRound = -1, _bytesOut;

        /// <summary>Called once the spawned model is whole (the hand settles it).</summary>
        public Action OnLive;

        public SpawnTerminal()
        {
            _spawnRound = p =>
            {
                _pixels.Round(p);
                if (p >= 1f)
                {
                    _pixels.Finish();
                    if (_target != null) OnLive?.Invoke();
                }
            };
        }

        /// <summary>The model in hand is still being spawned: not ready to throw or fire.</summary>
        public bool Busy => _term.Running && _target != null;

        public bool Active => _term.Active || _gone != null || _bytes.Active;

        /// <summary>Spawns <paramref name="model"/> (just put in the hand) into view.</summary>
        public void Begin(GameObject model, string item, string designation, string[] facts)
        {
            _pixels.Abort();
            _target = model;
            _pixels.Begin(model.transform, vanish: false);
            var script = new List<string> { $"RESOLVE {designation}" };
            foreach (var f in facts) script.Add(" " + f);
            script.Add($"MESH {_pixels.Parts} PARTS {_pixels.Verts} VERTS");
            script.Add(_pixels.AtlasLine);
            script.Add("WRITE OBJ[]: CONTESTED");
            _term.Label = item;
            _term.Command = $"SPAWN BA:{item}";
            _term.Script = script.ToArray();
            _term.DoneWord = "OBJECT LIVE";
            _term.TypeRate = 80f;
            _term.OnRound = _spawnRound;
            bool open = _term.Open;
            _term.Begin(Config.SpawnTime, true, false);
            if (!open) Sfx.PlayHeld("CluClick", 0.7f);
        }

        /// <summary>
        /// Puts <paramref name="model"/> away: it is let go of where it is and dissolves into
        /// bytes. False if it can't (no camera): the caller destroys it.
        /// </summary>
        public bool Despawn(GameObject model, string item, string designation)
        {
            var cam = Camera.main;
            if (model == null || cam == null) return false;
            if (model == _target)
            {
                // Still spawning: back to its own materials, then it goes from there.
                _pixels.Finish();
                _target = null;
            }
            EndGone();

            model.transform.SetParent(cam.transform, true);
            _gone = model;
            _goneAt = Time.time + DespawnLead;
            _goneRound = -1;
            _bytesOut = 0;
            _gonePixels.Begin(model.transform, vanish: true);
            _goneBounds = _gonePixels.CameraBounds(cam.transform);

            _term.Label = item;
            _term.Command = $"FREE BA:{item}";
            _term.Script = new[]
            {
                $"RELEASE {designation}",
                " UNBIND HAND.PIVOT",
                $" FREE MESH {_gonePixels.Verts} VERTS",
                $" FREE {_gonePixels.AtlasLine}",
                "WRITE OBJ[]: NULL",
            };
            _term.DoneWord = "OBJECT FREED";
            _term.TypeRate = 300f;   // put away is quick: the command is all but there already
            _term.OnRound = null;
            bool open = _term.Open;
            _term.Begin(Config.DespawnTime, true, false);
            if (!open) Sfx.PlayHeld("CluClick", 0.5f);
            Sfx.PlayHeld("Glitch", 0.35f);
            return true;
        }

        /// <summary>
        /// Every LateUpdate. <paramref name="current"/> is the model in hand now: if it isn't the
        /// one being spawned (gone, swapped), the spawn is called off.
        /// </summary>
        public void Tick(Camera cam, Transform pivot, int layer, GameObject current)
        {
            if (_target != null && current != _target)
            {
                _pixels.Abort();
                _target = null;
                _term.Close();
            }

            TickGone(cam, layer);
            _bytes.Tick(Time.deltaTime, cam, layer);

            if (!_term.Active) return;
            Vector3? anchor = _target != null && pivot != null ? pivot.position
                            : _gone != null ? _gone.transform.position
                            : (Vector3?)null;
            Vector3 offset = _target != null
                ? new Vector3(Config.SpawnTermOffsetX, Config.SpawnTermOffsetY, Config.SpawnTermOffsetZ)
                : new Vector3(Config.SmokeTermOffsetX, Config.SmokeTermOffsetY, Config.SmokeTermOffsetZ);
            // Behind the model it is spawning (or freeing), in that model's camera: most items are
            // drawn by the world camera, the AT-4, the binoculars and a model being freed by the
            // viewmodel one. On the viewmodel camera over a world-drawn item the window always
            // covered it; beside one on the same camera it cut through it.
            var model = _target != null ? _target : _gone;
            _term.Tick(cam, anchor, offset, Config.SmokeTermYaw, model != null ? model.layer : layer,
                       model != null ? model.transform : null);
        }

        /// <summary>The model being put away: its rounds on its own clock, and the bytes leaving it.</summary>
        private void TickGone(Camera cam, int layer)
        {
            if (_gone == null) { if (_gonePixels.Live) EndGone(); return; }
            if (_gone.layer != layer && layer >= 0)
                foreach (var t in _gone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            float d = Mathf.Max(0.05f, Config.DespawnTime);
            float now = Time.time;
            if (now < _goneAt) return;
            float p = Mathf.Clamp01((now - _goneAt) / d);
            int round = Mathf.FloorToInt((now - _goneAt) / (1f / 24f));
            if (round == _goneRound && p < 1f) return;
            _goneRound = round;
            _gonePixels.Round(p);

            // The bytes leave as the texels do, a little ahead of them.
            int due = Mathf.Min(Bytes, Mathf.CeilToInt(Bytes * Mathf.Clamp01(p * 1.15f)));
            for (; _bytesOut < due; _bytesOut++)
            {
                var b = _goneBounds;
                var at = new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.max.y), Random.Range(b.min.z, b.max.z));
                var vel = new Vector3(Random.Range(-0.04f, 0.04f), Random.Range(0.05f, 0.13f), Random.Range(-0.02f, 0.02f));
                _bytes.Emit(cam, at, vel, Random.Range(0.35f, 0.6f));
            }
            if (p >= 1f) EndGone();
        }

        private void EndGone()
        {
            _gonePixels.Abort();
            if (_gone != null) Object.Destroy(_gone);
            _gone = null;
        }

        public void Dispose()
        {
            _pixels.Abort();
            _target = null;
            EndGone();
            _bytes.Dispose();
            _term.Dispose();
        }
    }

    /// <summary>
    /// A held model fought into (or out of) existence texel by texel (TexelFight's rule). Each
    /// part on URP Lit gets a copy of its material, alpha-cut (the variant the launchers' stencils
    /// already bring into the bundle), on a copy of its atlas whose alpha says which texels exist
    /// right now; static flashes hazard yellow, rows tear. Parts on other shaders (the screens) and
    /// on textures made at runtime (the warhead stencil) are hidden meanwhile. Finish puts every
    /// part's own materials back. Each instance keeps its own atlas copies (a model can spawn
    /// while the last one is still going); the atlases are read once (TexelFight.Read).
    /// </summary>
    internal sealed class ModelPixels
    {
        private const string LitShader = "Universal Render Pipeline/Lit";

        private sealed class Sheet
        {
            public Color32[] Atlas;
            public int W, H;
            public Texture2D Tex;
            public Color32[] Px;
            public float[] Claim;
        }

        private sealed class Slot
        {
            public Renderer R;
            public Material[] Original, Swapped;
            public bool Hidden;
        }

        private readonly Dictionary<int, Sheet> _sheets = new Dictionary<int, Sheet>();
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly List<Sheet> _live = new List<Sheet>();
        private bool _vanish;

        public int Parts { get; private set; }
        public int Verts { get; private set; }

        /// <summary>Parts are on the fight's materials.</summary>
        public bool Live => _slots.Count > 0;

        /// <summary>The atlases being fought over, for the terminal: "ATLAS 128X128  2 SHEETS".</summary>
        public string AtlasLine { get; private set; } = "ATLAS NONE";

        /// <param name="vanish">Fight it out of existence instead of into it.</param>
        public void Begin(Transform root, bool vanish)
        {
            Abort();
            _vanish = vanish;
            Parts = Verts = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Parts++;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Verts += mf.sharedMesh.vertexCount;

                var mats = r.sharedMaterials;
                var slot = new Slot { R = r, Original = new Material[mats.Length], Swapped = new Material[mats.Length] };
                bool ok = true;
                for (int i = 0; i < mats.Length; i++)
                {
                    slot.Original[i] = mats[i];
                    slot.Swapped[i] = Cutout(mats[i]);
                    if (slot.Swapped[i] == null) ok = false;
                }
                if (ok) r.sharedMaterials = slot.Swapped;
                else
                {
                    foreach (var m in slot.Swapped) if (m != null) Object.Destroy(m);
                    slot.Swapped = null;
                    slot.Hidden = true;
                    r.enabled = false;
                }
                _slots.Add(slot);
            }

            Sheet big = null;
            foreach (var s in _live)
            {
                for (int i = 0; i < s.Claim.Length; i++) s.Claim[i] = Random.Range(0.02f, 0.98f);
                if (big == null || s.W * s.H > big.W * big.H) big = s;
            }
            AtlasLine = big != null ? $"ATLAS {big.W}X{big.H}  {_live.Count} SHEET{(_live.Count == 1 ? "" : "S")}" : "ATLAS NONE";
            DropUnusedSheets();
            Round(0f);
        }

        /// <summary>One round at progress <paramref name="p"/>: every live sheet's texels.</summary>
        public void Round(float p)
        {
            float g = TexelFight.Contest(p);
            foreach (var s in _live)
            {
                for (int y = 0; y < s.H; y++)
                {
                    int shift = TexelFight.Tear(g);
                    for (int x = 0; x < s.W; x++)
                    {
                        int i = y * s.W + x;
                        int sx = Mathf.Clamp(x - shift, 0, s.W - 1);
                        Color32 a = s.Atlas[i];
                        bool there = TexelFight.Taken(p, g, s.Claim[y * s.W + sx]) != _vanish;
                        if (TexelFight.Flash(g)) s.Px[i] = TexelFight.Static;
                        else if (there) s.Px[i] = a;
                        else s.Px[i] = new Color32(a.r, a.g, a.b, 0);
                    }
                }
                s.Tex.SetPixels32(s.Px);
                s.Tex.Apply(false);
            }
        }

        /// <summary>The visible parts' bounds in <paramref name="frame"/>'s space (where bytes leave from).</summary>
        public Bounds CameraBounds(Transform frame)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var slot in _slots)
            {
                if (slot.R == null || slot.Hidden) continue;
                var wb = slot.R.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = wb.center + Vector3.Scale(wb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var local = frame.InverseTransformPoint(corner);
                    if (!any) { b = new Bounds(local, Vector3.zero); any = true; }
                    else b.Encapsulate(local);
                }
            }
            return b;
        }

        /// <summary>Done: every part's own materials back, hidden parts shown.</summary>
        public void Finish()
        {
            foreach (var slot in _slots)
            {
                if (slot.R == null) continue;
                if (slot.Hidden) slot.R.enabled = true;
                else slot.R.sharedMaterials = slot.Original;
            }
            Abort();
        }

        /// <summary>Lets go of what was made (the model may already be gone).</summary>
        public void Abort()
        {
            foreach (var slot in _slots)
                if (slot.Swapped != null) foreach (var m in slot.Swapped) if (m != null) Object.Destroy(m);
            _slots.Clear();
            _live.Clear();
        }

        private static readonly List<int> _sheetsGone = new List<int>();

        /// <summary>Keeps only the sheets of the model just begun (equipping it again reuses them):
        /// each sheet is a texture and two texel arrays, so one per atlas ever seen would add up.</summary>
        private void DropUnusedSheets()
        {
            _sheetsGone.Clear();
            foreach (var kv in _sheets)
                if (kv.Value == null || !_live.Contains(kv.Value)) _sheetsGone.Add(kv.Key);
            foreach (int key in _sheetsGone)
            {
                var s = _sheets[key];
                if (s != null && s.Tex != null) Object.Destroy(s.Tex);
                _sheets.Remove(key);
            }
        }

        /// <summary>An alpha-cut copy of <paramref name="orig"/> on its atlas's live copy, or null if it can't have one.</summary>
        private Material Cutout(Material orig)
        {
            if (orig == null || orig.shader == null || orig.shader.name != LitShader) return null;
            var tex = orig.HasProperty("_BaseMap") ? orig.GetTexture("_BaseMap") : null;
            // Made at runtime and changing as it goes (the warhead stencil): hidden instead.
            if (tex != null && (string.IsNullOrEmpty(tex.name) || tex.name.StartsWith("BA_"))) return null;
            var sheet = SheetFor(tex);
            if (sheet == null) return null;
            if (!_live.Contains(sheet)) _live.Add(sheet);

            var m = new Material(orig) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.SetTexture("_BaseMap", sheet.Tex);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = 2450;
            return m;
        }

        /// <summary>This instance's live copy of <paramref name="tex"/>'s texels (a small white one for a material without a texture).</summary>
        private Sheet SheetFor(Texture tex)
        {
            int key = tex != null ? tex.GetInstanceID() : 0;
            if (_sheets.TryGetValue(key, out var s)) return s;

            Color32[] atlas; int w, h;
            if (tex == null)
            {
                w = h = 16;
                atlas = new Color32[w * h];
                for (int i = 0; i < atlas.Length; i++) atlas[i] = new Color32(255, 255, 255, 255);
            }
            else atlas = TexelFight.Read(tex, out w, out h);

            if (atlas != null)
                s = new Sheet
                {
                    Atlas = atlas, W = w, H = h,
                    Tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset, name = "BA_Spawn" },
                    Px = new Color32[w * h],
                    Claim = new float[w * h],
                };
            _sheets[key] = s;   // misses too
            return s;
        }
    }

    /// <summary>
    /// The bytes a put-away model leaves as: small two-digit hex glyphs in amber (PixelFont's,
    /// the window's texel size) that rise off it, drift, keep changing value and fade. A pool of
    /// quads parented to the camera, on the viewmodel layer, sharing one sheet of all 256 bytes;
    /// each quad's UVs pick its byte, and a property block fades it.
    /// </summary>
    internal sealed class ByteSpray
    {
        private const int Max = 48;
        private const int CellW = 12, CellH = 8, GlyphW = 11, GlyphH = 7;
        private const float Texel = 0.0009f;          // metres per texel, the window's
        private const float FlipEvery = 0.07f;        // a byte changes value this often

        private Material _mat;
        private Texture2D _sheet;
        private MaterialPropertyBlock _block;
        private bool _tried;

        private readonly GameObject[] _go = new GameObject[Max];
        private readonly Mesh[] _mesh = new Mesh[Max];
        private readonly MeshRenderer[] _mr = new MeshRenderer[Max];
        private readonly Vector3[] _pos = new Vector3[Max], _vel = new Vector3[Max];
        private readonly float[] _age = new float[Max], _life = new float[Max], _flip = new float[Max];
        private int _next, _alive;

        public bool Active => _alive > 0;

        /// <summary>One byte leaving from <paramref name="at"/> (camera space), at <paramref name="vel"/> (camera space).</summary>
        public void Emit(Camera cam, Vector3 at, Vector3 vel, float life)
        {
            if (cam == null || !Ensure(cam)) return;
            int i = _next;
            _next = (_next + 1) % Max;
            if (_go[i] == null) return;
            if (!_go[i].activeSelf) _alive++;
            _pos[i] = at; _vel[i] = vel;
            _age[i] = 0f; _life[i] = life; _flip[i] = 0f;
            SetByte(i, Random.Range(0, 256));
            if (_go[i].transform.parent != cam.transform) _go[i].transform.SetParent(cam.transform, false);
            _go[i].transform.localPosition = at;
            _go[i].transform.localRotation = Quaternion.identity;
            _go[i].SetActive(true);
        }

        public void Tick(float dt, Camera cam, int layer)
        {
            if (_alive <= 0) return;
            for (int i = 0; i < Max; i++)
            {
                var go = _go[i];
                if (go == null || !go.activeSelf) continue;
                _age[i] += dt;
                float u = _age[i] / _life[i];
                if (u >= 1f) { go.SetActive(false); _alive--; continue; }
                if (layer >= 0 && go.layer != layer) go.layer = layer;
                _vel[i] *= Mathf.Exp(-1.5f * dt);
                _pos[i] += _vel[i] * dt;
                go.transform.localPosition = _pos[i];
                _flip[i] += dt;
                if (_flip[i] >= FlipEvery) { _flip[i] = 0f; SetByte(i, Random.Range(0, 256)); }
                // Full for the first half, then fading, flickering out at the end.
                float a = u < 0.5f ? 1f : 1f - (u - 0.5f) / 0.5f;
                if (u > 0.8f && Random.value < 0.4f) a = 0f;
                _block.Clear();
                _block.SetColor("_BaseColor", new Color(1f, 1f, 1f, a));
                _mr[i].SetPropertyBlock(_block);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < Max; i++)
            {
                if (_go[i] != null) Object.Destroy(_go[i]);
                if (_mesh[i] != null) Object.Destroy(_mesh[i]);
                _go[i] = null; _mesh[i] = null; _mr[i] = null;
            }
            if (_mat != null) Object.Destroy(_mat);
            if (_sheet != null) Object.Destroy(_sheet);
            _mat = null; _sheet = null;
            _alive = 0;
            _tried = false;
        }

        private void SetByte(int i, int b)
        {
            float u0 = (b % 16) * CellW / (float)(16 * CellW), v1 = 1f - (b / 16) * CellH / (float)(16 * CellH);
            float du = GlyphW / (float)(16 * CellW), dv = GlyphH / (float)(16 * CellH);
            _mesh[i].SetUVs(0, new[] { new Vector2(u0, v1 - dv), new Vector2(u0 + du, v1 - dv), new Vector2(u0 + du, v1), new Vector2(u0, v1) });
        }

        private bool Ensure(Camera cam)
        {
            if (_mat != null && _go[0] != null) return true;
            if (_tried) return false;
            _tried = true;
            var src = DosTerminal.HudSource();
            if (src == null) return false;

            // Every byte, two hex digits each, amber on clear.
            int w = 16 * CellW, h = 16 * CellH;
            _sheet = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset, name = "BA_Bytes" };
            var px = new Color32[w * h];
            for (int b = 0; b < 256; b++)
            {
                string hex = b.ToString("X2");
                int cx = (b % 16) * CellW, cy = (b / 16) * CellH;   // top-left, from the top
                for (int k = 0; k < 2; k++)
                {
                    var g = PixelFont.Bits(hex[k]);
                    for (int r = 0; r < PixelFont.GH; r++)
                        for (int c = 0; c < PixelFont.GW; c++)
                            if (g[r * PixelFont.GW + c] == '#')
                                px[(h - 1 - (cy + r)) * w + cx + k * (PixelFont.GW + 1) + c] = DosTerminal.Amber;
                }
            }
            _sheet.SetPixels32(px);
            _sheet.Apply(false);
            _mat = new Material(src) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            PixelCanvas.SetTex(_mat, _sheet);
            _block = new MaterialPropertyBlock();

            float hw = GlyphW * Texel * 0.5f, hh = GlyphH * Texel * 0.5f;
            for (int i = 0; i < Max; i++)
            {
                var mesh = PixelCanvas.Quad(hw, hh);
                var go = new GameObject("BA_Byte");
                go.transform.SetParent(cam.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.SetActive(false);
                _go[i] = go; _mesh[i] = mesh; _mr[i] = mr;
            }
            return true;
        }
    }
}
