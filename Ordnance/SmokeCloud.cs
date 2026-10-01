using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// A smoke grenade's cloud: smoke that spreads over the ground like a heavy gas.
    ///
    /// The ground round the can is cut into columns SmokeCellSize across, each holding a depth
    /// of smoke (metres). Every step (5 a second) a column deeper than SmokeFrontDepth shares
    /// smoke with its four neighbours in proportion to the difference (SmokeSpread), heavier
    /// downhill; and every column, however thin, gives some to the neighbours downwind (the
    /// Breeze). So the cloud keeps a front about SmokeFrontDepth deep, piles up round the can,
    /// leans and drifts with the wind. The can feeds SmokeVolume m^3/s into the column under it
    /// while it burns; the cloud thins slowly while it does and clears (SmokeClearRate) after.
    ///
    /// Neighbours are found the first time smoke reaches for them, with rays: a wall at knee and
    /// head height stops it; a low obstacle is a column on top of it (smoke spills over once it
    /// is deeper than the obstacle is tall); a drop up to MaxDrop is a lower column it pours down
    /// into; past that (the arena's edge) is an edge column, where smoke falls away in a slow
    /// stream of puffs and is lost.
    ///
    /// Drawn as one faceted volume (SmokeVolume.shader on the prefab's Volume box): the columns
    /// are written into a small texture each frame (SmokeField) and the shader marches through
    /// them, its noise and facets riding the wind as one, solid in the core and hazy at the front
    /// and top. It needs the camera depth texture, which Start turns on.
    /// </summary>
    internal sealed class SmokeCloud
    {
        private const float Step = 0.2f;
        private const float MaxShown = 8f;
        private const float MaxDrop = 6f;
        private const float Downhill = 0.5f;       // weight of a ground difference against a depth difference
        private const float EdgeDrain = 1.2f;      // per second: how fast smoke falls away over an edge

        private sealed class Column
        {
            public Vector3 Base;           // centre, on the ground
            public int X, Z;
            public float Depth, Inflow, Shown;
            public bool Edge;              // over a drop into nothing: smoke falls away here
            public Vector3 EdgeDir;
            public readonly Column[] Next = new Column[4];
            public byte Probed, Blocked;   // per direction
        }

        private static readonly List<SmokeCloud> _all = new List<SmokeCloud>();
        private static readonly Vector3[] Dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        private static readonly int[] Dx = { 1, -1, 0, 0 }, Dz = { 0, 0, 1, -1 };

        private readonly Dictionary<long, List<Column>> _byCell = new Dictionary<long, List<Column>>();
        private readonly List<Column> _columns = new List<Column>();
        private readonly GameObject _box;
        private readonly Material _mat;
        private readonly SmokeField _field = new SmokeField();
        private readonly List<SmokeField.Col> _cols = new List<SmokeField.Col>();
        private Vector3 _drift;
        private float _age;
        private readonly Transform _source;
        private readonly Vector3 _origin;
        private readonly Color _colour;
        private readonly float _cell;
        private float _burnUntil, _acc;
        private ParticleSystem _jet;

        private Il2CppStructArray<ParticleSystem.Particle> _jetArr;

        /// <summary>Clouds alive now (the HUD's debug readout).</summary>
        public static int Count => _all.Count;

        private SmokeCloud(GameObject box, ParticleSystem jet, Transform source, float burnFor, Color colour)
        {
            _box = box; _jet = jet; _source = source; _colour = colour;
            var r = box.GetComponent<Renderer>();
            _mat = new Material(r.sharedMaterial);
            _mat.SetColor("_Color", colour);
            _mat.SetTexture("_Field", _field.Tex);
            r.sharedMaterial = _mat;
            box.SetActive(false);
            _origin = source.position;
            _cell = Mathf.Clamp(Config.SmokeCellSize, 0.5f, 4f);
            _burnUntil = Time.time + burnFor;
        }

        /// <summary>
        /// Starts a cloud fed from <paramref name="source"/> for <paramref name="burnFor"/> seconds.
        /// <paramref name="volume"/> is the prefab's Volume box: it is taken off the can so the
        /// cloud outlives it. <paramref name="jet"/> (may be null) is pushed by the wind.
        /// </summary>
        public static SmokeCloud Start(GameObject volume, ParticleSystem jet, Transform source, float burnFor, Color colour)
        {
            if (volume == null || source == null || volume.GetComponent<Renderer>() == null) return null;
            DepthTexture.Require();
            var t = volume.transform;
            t.SetParent(null, false);
            t.rotation = Quaternion.identity;
            var c = new SmokeCloud(volume, jet, source, burnFor, colour);
            _all.Add(c);
            return c;
        }

        /// <summary>The can stopped (burnt out, or gone): no more smoke in.</summary>
        public void StopFeeding() => _burnUntil = Mathf.Min(_burnUntil, Time.time);

        /// <summary>The can's jet is gone with the can.</summary>
        public void DropJet() => _jet = null;

        public static void TickAll()
        {
            if (_all.Count == 0) return;
            float dt = Time.deltaTime;
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                var c = _all[i];
                bool alive;
                try { alive = c.Tick(dt); }
                catch (System.Exception e)
                {
                    MelonLogger.Warning($"[Smoke] cloud failed, removing it: {e.Message}");
                    alive = false;
                }
                if (!alive) { c.Destroy(); _all.RemoveAt(i); }
            }
        }

        public static void ClearAll()
        {
            foreach (var c in _all) c.Destroy();
            _all.Clear();
        }

        private void Destroy()
        {
            if (_box != null) Object.Destroy(_box);
            if (_mat != null) Object.Destroy(_mat);
            if (_field.Tex != null) Object.Destroy(_field.Tex);
        }

        private bool Tick(float dt)
        {
            if (_box == null) return false;
            _acc += dt;
            int steps = 0;
            while (_acc >= Step && steps < 3) { _acc -= Step; Simulate(Step); steps++; }
            if (_acc > Step) _acc = 0f;   // a long hitch: don't try to catch up
            bool any = Draw(dt);
            PushJet(dt);
            return any || Time.time < _burnUntil;
        }

        // ── Spreading ───────────────────────────────────────────────────────────

        private void Simulate(float dt)
        {
            bool burning = Time.time < _burnUntil;
            float front = Mathf.Max(0.1f, Config.SmokeFrontDepth);
            float spread = Mathf.Max(0f, Config.SmokeSpread);
            Vector3 wind = Breeze.Velocity;
            var flows = new float[4];

            foreach (var c in _columns) c.Inflow = 0f;
            for (int ci = 0; ci < _columns.Count; ci++)
            {
                var c = _columns[ci];
                if (c.Edge || c.Depth <= 0.001f) continue;
                float total = 0f;
                for (int d = 0; d < 4; d++)
                {
                    flows[d] = 0f;
                    // Only reach for new ground at the front, where there is smoke to send.
                    if ((c.Probed & (1 << d)) == 0 && c.Depth >= front * 0.8f) Probe(c, d);
                    var n = c.Next[d];
                    if (n == null) continue;
                    float f = 0f;
                    if (c.Depth > front)
                    {
                        float grad = n.Edge ? c.Depth : (c.Depth - n.Depth) + Downhill * (c.Base.y - n.Base.y);
                        // Smoke can't climb a step taller than itself.
                        if (!n.Edge && n.Base.y > c.Base.y + c.Depth) grad = 0f;
                        if (grad > 0f) f += spread * grad * dt;
                    }
                    float w = Vector3.Dot(Dirs[d], wind);
                    if (w > 0f) f += c.Depth * w / _cell * dt;
                    flows[d] = f;
                    total += f;
                }
                if (total <= 0f) continue;
                float scale = Mathf.Min(1f, 0.45f * c.Depth / total);
                for (int d = 0; d < 4; d++)
                {
                    if (flows[d] <= 0f) continue;
                    float f = flows[d] * scale;
                    c.Next[d].Inflow += f;
                    c.Depth -= f;
                }
            }

            float k = burning ? Config.SmokeFadeBurning : Config.SmokeClearRate;
            foreach (var c in _columns)
            {
                c.Depth += c.Inflow;
                if (c.Edge) c.Depth -= c.Depth * EdgeDrain * dt;
                c.Depth -= (c.Depth * k + 0.01f) * dt * (c.Depth < 0.35f ? 2f : 1f);
                if (c.Depth < 0f) c.Depth = 0f;
            }

            if (burning && _source != null)
            {
                var src = ColumnUnder(_source.position);
                if (src != null) src.Depth += Mathf.Max(0f, Config.SmokeVolume * Config.SmokeDensity) * dt / (_cell * _cell);
            }
        }

        /// <summary>The column the can sits in (made if new); null if there is no ground under it.</summary>
        private Column ColumnUnder(Vector3 p)
        {
            if (!Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out var hit, 3f, Config.WorldLayerMask, QueryTriggerInteraction.Ignore))
                return null;
            int x = Mathf.RoundToInt((p.x - _origin.x) / _cell), z = Mathf.RoundToInt((p.z - _origin.z) / _cell);
            return Get(x, z, hit.point.y, false) ?? Make(x, z, hit.point.y, false);
        }

        /// <summary>What lies one cell that way: a wall, more ground (higher, lower, on top of something) or an edge.</summary>
        private void Probe(Column c, int d)
        {
            if (_columns.Count >= Config.SmokeMaxCells) return;   // full: try again later
            c.Probed |= (byte)(1 << d);
            Vector3 dir = Dirs[d];
            int mask = Config.WorldLayerMask;
            var q = QueryTriggerInteraction.Ignore;
            bool low = Physics.Raycast(c.Base + Vector3.up * 0.5f, dir, _cell, mask, q);
            bool high = Physics.Raycast(c.Base + Vector3.up * 1.6f, dir, _cell, mask, q);
            if (low && high) { c.Blocked |= (byte)(1 << d); return; }

            int x = c.X + Dx[d], z = c.Z + Dz[d];
            Vector3 at = new Vector3(_origin.x + x * _cell, c.Base.y, _origin.z + z * _cell);
            Column n;
            if (Physics.Raycast(at + Vector3.up * 1.8f, Vector3.down, out var hit, 1.8f + MaxDrop, mask, q))
                n = Get(x, z, hit.point.y, false) ?? Make(x, z, hit.point.y, false);
            else
            {
                n = Get(x, z, c.Base.y, true) ?? Make(x, z, c.Base.y, true);
                n.EdgeDir = dir;
            }
            c.Next[d] = n;
            // Linked both ways: the new column needn't probe back.
            int back = d ^ 1;
            if (n.Next[back] == null && !n.Edge) { n.Next[back] = c; n.Probed |= (byte)(1 << back); }
        }

        private static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        private Column Get(int x, int z, float ground, bool edge)
        {
            if (!_byCell.TryGetValue(CellKey(x, z), out var list)) return null;
            foreach (var c in list) if (c.Edge == edge && Mathf.Abs(c.Base.y - ground) < 1f) return c;
            return null;
        }

        private Column Make(int x, int z, float ground, bool edge)
        {
            var c = new Column
            {
                X = x, Z = z, Edge = edge,
                Base = new Vector3(_origin.x + x * _cell, ground, _origin.z + z * _cell),
            };
            long key = CellKey(x, z);
            if (!_byCell.TryGetValue(key, out var list)) _byCell[key] = list = new List<Column>(1);
            list.Add(c);
            _columns.Add(c);
            return c;
        }

        // ── Drawing ─────────────────────────────────────────────────────────────

        /// <summary>Writes the columns into the volume; false once nothing is left to show.</summary>
        private bool Draw(float dt)
        {
            float follow = 1f - Mathf.Exp(-dt * 3f);
            Vector3 wind = Breeze.Velocity;
            _drift += wind * dt;
            _age += dt;
            _cols.Clear();
            foreach (var c in _columns)
            {
                c.Shown += (Mathf.Min(c.Depth, MaxShown) - c.Shown) * follow;
                if (c.Shown < 0.01f && c.Depth <= 0f) c.Shown = 0f;
                _cols.Add(new SmokeField.Col { X = c.X, Z = c.Z, Ground = c.Base.y - _origin.y, Depth = c.Shown, Edge = c.Edge });
            }
            // Room for what the shader adds: billows above the tops, warp and the blur sideways.
            float rise = _mat.GetFloat("_Soft") + _mat.GetFloat("_Lumps") + _mat.GetFloat("_Detail") + 0.5f;
            float margin = (1 + SmokeField.BlurPasses) * _cell + _mat.GetFloat("_Warp") + 0.5f;
            if (!_field.Write(_cols, _origin, _cell, rise, margin)) { _box.SetActive(false); return false; }
            _box.SetActive(true);
            const int H = SmokeField.Size / 2;
            _mat.SetVector("_FieldMin", new Vector4(_origin.x - H * _cell, _origin.y, _origin.z - H * _cell, 0f));
            _mat.SetFloat("_FieldCell", _cell);
            _mat.SetFloat("_FieldSize", SmokeField.Size);
            _mat.SetVector("_BoxMin", _field.BoxMin);
            _mat.SetVector("_BoxMax", _field.BoxMax);
            _mat.SetVector("_Drift", _drift);
            _mat.SetVector("_Wind", wind);
            _mat.SetFloat("_Age", _age);
            _box.transform.position = (_field.BoxMin + _field.BoxMax) * 0.5f;
            _box.transform.localScale = _field.BoxMax - _field.BoxMin;
            return true;
        }

        /// <summary>The jet's puffs drift with the wind, more the older they are.</summary>
        private void PushJet(float dt)
        {
            if (_jet == null || !ParticleIO.Ok) return;
            Vector3 wind = Breeze.Velocity;
            if (wind.sqrMagnitude < 1e-4f) return;
            int n = _jet.particleCount;
            if (n == 0) return;
            if (_jetArr == null || _jetArr.Length < n) _jetArr = new Il2CppStructArray<ParticleSystem.Particle>(Mathf.Max(n, 128));
            n = ParticleIO.Get(_jet, _jetArr, n);
            if (n == 0) return;
            for (int i = 0; i < n; i++)
            {
                var p = _jetArr[i];
                float age = 1f - p.m_Lifetime / Mathf.Max(0.01f, p.m_StartLifetime);
                p.m_Position += wind * (dt * Mathf.Clamp01(age * 3f));
                _jetArr[i] = p;
            }
            ParticleIO.Set(_jet, _jetArr, n);
        }
    }
}
