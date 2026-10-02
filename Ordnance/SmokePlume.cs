using System.Collections.Generic;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// A smoke grenade's plume, as real ones behave: the can vents warm smoke that rises while it
    /// is hot, bends over with the wind, grows and thins as it mixes with the air, and is stopped
    /// by the ground, walls and ceilings. In still air that makes a tall leaning column; in a
    /// breeze a low plume stretched far downwind (an M18's reaches about 30 m in still air).
    ///
    /// The smoke is a few hundred parcels: each a ball of smoke with a radius, an amount of smoke
    /// and some heat. Every frame they are splatted into a SmokeGrid (below) that
    /// SmokeVolume.shader draws; plumes whose smoke meets share one grid, so colours mix.
    ///
    /// Shared verbatim by the mod (Ordnance/SmokePlume.cs) and the Unity preview
    /// (FX/_Kit/Editor/SmokePlume.cs): change one, copy it over the other.
    /// </summary>
    internal sealed class SmokePlume
    {
        public struct Settings
        {
            public float Rate;          // parcels per second while the can burns
            public float Rise;          // m/s the smoke leaves the vent upward
            public float Spray;         // m/s it bursts out sideways (the vents are round the lid)
            public float Float;         // m/s^2 of lift that lasts: cooled smoke still lifts slowly
            public float Lift;          // m/s^2 of buoyancy while fully hot
            public float Cooling;       // seconds for the heat to fall to a third
            public float StartRadius;   // m, a parcel at the vent
            public float Growth;        // m/s a parcel widens by itself
            public float Entrain;       // extra widening per m/s it moves through the air
            public float Thin;          // share of the smoke lost per second (more once the can is out)
            public float Turbulence;    // m/s of random wander
            public float Strength;      // density of a fresh parcel's middle (the shader's solid is at 0.5)
            public int Mask;            // what stops smoke

            public static Settings Defaults => new Settings
            {
                Rate = 12f, Rise = 0.4f, Spray = 1.4f, Lift = 0.4f, Float = 0.03f, Cooling = 3f, StartRadius = 0.55f,
                Growth = 0.04f, Entrain = 0.12f, Thin = 0.015f, Turbulence = 0.45f, Strength = 1.1f, Mask = ~0,
            };
        }

        public sealed class Parcel
        {
            // Kick: a shove from a blast or a passing rocket, on top of Vel. The air stops it
            // quickly (KickTime), so smoke bursts aside and stays there.
            public Vector3 Pos, Vel, Kick, Checked;
            public float R, Mass, Age, Ground = float.NegativeInfinity, NextProbe;
        }

        public const int MaxParcels = 600;

        public readonly List<Parcel> Parcels = new List<Parcel>();
        public Color Colour = Color.white;

        private Settings _s;
        private float _emit, _time;
        private readonly System.Random _rng = new System.Random();

        public SmokePlume(Settings s) { _s = s; }

        public void Configure(Settings s) => _s = s;

        public bool Alive => Parcels.Count > 0;

        /// <summary>Moves the smoke on by dt; while <paramref name="feeding"/>, the can at <paramref name="vent"/> adds more.</summary>
        public void Tick(float dt, Vector3 wind, Vector3 vent, bool feeding, float density)
        {
            if (dt <= 0f) return;
            _time += dt;
            if (feeding)
            {
                _emit += dt * _s.Rate * Mathf.Max(0.1f, density);
                while (_emit >= 1f && Parcels.Count < MaxParcels) { _emit -= 1f; Emit(vent); }
                if (_emit > 1f) _emit = 0f;
            }

            float relax = 1f - Mathf.Exp(-dt / 1.2f);       // how fast smoke takes up the wind
            float thin = _s.Thin * (feeding ? 1f : 2.5f);
            for (int i = Parcels.Count - 1; i >= 0; i--)
            {
                var p = Parcels[i];
                p.Age += dt;
                float heat = Mathf.Exp(-p.Age / Mathf.Max(0.1f, _s.Cooling));

                // Rises while hot (against the air's drag), takes up the wind, wanders.
                var v = p.Vel;
                v.y += (_s.Lift * heat + _s.Float - v.y * 0.9f) * dt;
                v.x += (wind.x - v.x) * relax;
                v.z += (wind.z - v.z) * relax;
                v += new Vector3(Rand(), Rand() * 0.5f, Rand()) * (_s.Turbulence * Mathf.Sqrt(dt));
                p.Vel = v;
                var rel = (new Vector3(v.x - wind.x, v.y, v.z - wind.z) + p.Kick).magnitude;
                p.R += (_s.Growth + _s.Entrain * rel) * dt;
                p.Pos += (v + p.Kick) * dt;
                p.Kick *= Mathf.Exp(-dt / KickTime);
                p.Mass -= p.Mass * thin * dt;

                Collide(p, dt);

                // Gone once it is too thin to show.
                float peak = p.Mass * _s.Strength * Dilute(p.R);
                if (peak < 0.03f || p.Age > 120f) Parcels.RemoveAt(i);
            }
        }

        private void Emit(Vector3 vent)
        {
            var p = new Parcel
            {
                Pos = vent + new Vector3(Rand(), 0f, Rand()) * 0.08f,
                Vel = Spray() + Vector3.up * (_s.Rise * (0.6f + 0.8f * (float)_rng.NextDouble())),
                R = _s.StartRadius * (0.8f + 0.4f * (float)_rng.NextDouble()),
                Mass = 1f,
                NextProbe = _time + (float)_rng.NextDouble() * 0.15f,
            };
            p.Checked = p.Pos;
            Parcels.Add(p);
        }

        /// <summary>
        /// Walls and ceilings stop it (a ray along the way it went since the last check), the
        /// ground holds it up and makes it spread. Checked a few times a second per parcel.
        /// </summary>
        private void Collide(Parcel p, float dt)
        {
            var q = QueryTriggerInteraction.Ignore;
            if (_time >= p.NextProbe)
            {
                p.NextProbe = _time + 0.15f;
                var d = p.Pos - p.Checked;
                float len = d.magnitude;
                if (len > 1e-3f && Physics.Raycast(p.Checked, d / len, out var hit, len + 0.2f, _s.Mask, q))
                {
                    p.Pos = hit.point + hit.normal * 0.2f;
                    var n = hit.normal;
                    p.Vel -= n * Mathf.Min(0f, Vector3.Dot(p.Vel, n));
                    p.Kick -= n * Mathf.Min(0f, Vector3.Dot(p.Kick, n));
                }
                p.Checked = p.Pos;
                p.Ground = Physics.Raycast(p.Pos + Vector3.up * 0.3f, Vector3.down, out var g, 40f, _s.Mask, q)
                    ? g.point.y : float.NegativeInfinity;
            }
            // Its middle stays half a radius off the ground: smoke pressed down spreads instead.
            float floor = p.Ground + p.R * 0.5f;
            if (p.Pos.y < floor)
            {
                p.Pos.y = floor;
                if (p.Vel.y < 0f) p.Vel.y = 0f;
                if (p.Kick.y < 0f) p.Kick.y = 0f;
                p.R += 0.15f * dt;
            }
        }

        /// <summary>The box the smoke fills (each parcel's reach), grown by <paramref name="margin"/>; false if there is none.</summary>
        public bool Bounds(float margin, out Vector3 lo, out Vector3 hi)
        {
            lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); hi = -lo;
            if (Parcels.Count == 0) return false;
            foreach (var p in Parcels)
            {
                float k = p.R * Reach + margin;
                lo = Vector3.Min(lo, p.Pos - Vector3.one * k);
                hi = Vector3.Max(hi, p.Pos + Vector3.one * k);
            }
            return true;
        }

        /// <summary>A parcel's splat reaches Reach times its radius.</summary>
        public const float Reach = 1.25f;

        /// <summary>Seconds for the air to take a third of a kick out of a parcel.</summary>
        public const float KickTime = 0.25f;

        /// <summary>A blast throws smoke out to this many times the radius it clears.</summary>
        public const float BlastReach = 2.5f;

        /// <summary>
        /// A blast at <paramref name="at"/> that clears a bubble <paramref name="clear"/> across:
        /// smoke out to BlastReach times that is thrown outward, hardest near the middle, so the
        /// middle empties and the smoke piles up round it. Smoke behind a wall only gets what
        /// spills round it. What is thrown is churned into the air: wider, thinner.
        /// </summary>
        public void Blast(Vector3 at, float clear)
        {
            float reach = clear * BlastReach;
            var q = QueryTriggerInteraction.Ignore;
            foreach (var p in Parcels)
            {
                var d = p.Pos - at;
                float dist = d.magnitude;
                if (dist >= reach) continue;
                float f = 1f - dist / reach;
                f *= f;
                if (dist > 0.3f && Physics.Linecast(at, p.Pos, _s.Mask, q)) f *= 0.15f;
                var dir = (dist > 1e-3f ? d / dist : Vector3.up) + new Vector3(Rand(), Rand(), Rand()) * 0.25f;
                // Moved about 1.4 x clear at the middle (Kick x KickTime), less further out.
                p.Kick += dir.normalized * (clear * 1.4f / KickTime * f);
                p.R += clear * 0.15f * f;
                p.Mass *= 1f - 0.3f * f;
            }
        }

        /// <summary>
        /// Something fast went from <paramref name="a"/> to <paramref name="b"/>: smoke within three
        /// times <paramref name="radius"/> of its path is shoved aside at up to <paramref name="push"/>
        /// m/s, and dragged a little along.
        /// </summary>
        public void Wake(Vector3 a, Vector3 b, float radius, float push)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return;
            var along = ab / Mathf.Sqrt(len2);
            float reach = radius * 3f;
            foreach (var p in Parcels)
            {
                float h = Mathf.Clamp01(Vector3.Dot(p.Pos - a, ab) / len2);
                var off = p.Pos - (a + ab * h);
                float dist = off.magnitude;
                if (dist >= reach) continue;
                float f = 1f - dist / reach;
                f *= f;
                var side = dist > 1e-3f ? off / dist : Vector3.ProjectOnPlane(new Vector3(Rand(), Rand(), Rand()), along).normalized;
                p.Kick += (side + along * 0.4f) * (push * f);
                p.R += radius * 0.1f * f;
            }
        }

        /// <summary>A parcel's density in its middle.</summary>
        public float Amp(Parcel p) => p.Mass * _s.Strength * Dilute(p.R) * Mathf.Clamp01(p.Age * 4f);

        private float Rand() => (float)_rng.NextDouble() * 2f - 1f;

        /// <summary>Out of one of the vents round the lid, sideways.</summary>
        private Vector3 Spray()
        {
            float a = (float)_rng.NextDouble() * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (_s.Spray * (0.5f + 0.5f * (float)_rng.NextDouble()));
        }
        /// <summary>How much a parcel's middle has thinned by growing to radius r (its smoke spread wider).</summary>
        private float Dilute(float r) => Mathf.Pow(_s.StartRadius / r, 2f);
    }

    /// <summary>
    /// The density grid SmokeVolume.shader reads: Nx x Ny x Nz voxels of Cell metres from Min
    /// (voxel centres), world aligned so nothing swims as the smoke moves. Each voxel holds the
    /// smoke's density (D) and its colour weighted by density (R, G, B), so where plumes of two
    /// colours meet the shader shades with their mix. Stored x fastest, then y, then z (the
    /// order Texture3D.SetPixels takes).
    /// </summary>
    internal sealed class SmokeGrid
    {
        public const int MaxDims = 40;              // per axis (the texture is MaxDims^2 x MaxHeight)
        public const int MaxHeight = 28;
        public const float BaseCell = 0.6f;
        public const int Count = MaxDims * MaxHeight * MaxDims;

        public readonly float[] D = new float[Count], R = new float[Count], G = new float[Count], B = new float[Count];
        public Vector3 Min;
        public float Cell = BaseCell;
        public int Nx, Ny, Nz;

        /// <summary>Splats every parcel of <paramref name="plumes"/> into the grid; false if there is nothing.</summary>
        public bool Splat(List<SmokePlume> plumes, float margin)
        {
            System.Array.Clear(D, 0, Count); System.Array.Clear(R, 0, Count);
            System.Array.Clear(G, 0, Count); System.Array.Clear(B, 0, Count);
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
            bool any = false;
            foreach (var pl in plumes)
                if (pl.Bounds(margin, out var a, out var b)) { lo = Vector3.Min(lo, a); hi = Vector3.Max(hi, b); any = true; }
            if (!any) return false;
            var size = hi - lo;
            Cell = Mathf.Max(BaseCell, Mathf.Max(size.x / (MaxDims - 3), Mathf.Max(size.z / (MaxDims - 3), size.y / (MaxHeight - 3))));
            // Snapped to the world grid, so a voxel stays where it is as the smoke moves.
            Min = new Vector3(Mathf.Floor(lo.x / Cell) * Cell, Mathf.Floor(lo.y / Cell) * Cell, Mathf.Floor(lo.z / Cell) * Cell);
            Nx = Mathf.Min(MaxDims, Mathf.CeilToInt((hi.x - Min.x) / Cell) + 1);
            Ny = Mathf.Min(MaxHeight, Mathf.CeilToInt((hi.y - Min.y) / Cell) + 1);
            Nz = Mathf.Min(MaxDims, Mathf.CeilToInt((hi.z - Min.z) / Cell) + 1);

            float inv = 1f / Cell;
            foreach (var pl in plumes)
            {
                var c = pl.Colour;
                foreach (var p in pl.Parcels)
                {
                    float k = p.R * SmokePlume.Reach, k2 = k * k;
                    float amp = pl.Amp(p);
                    if (amp < 0.005f) continue;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((p.Pos.x - k - Min.x) * inv)), x1 = Mathf.Min(Nx - 1, Mathf.CeilToInt((p.Pos.x + k - Min.x) * inv));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt((p.Pos.y - k - Min.y) * inv)), y1 = Mathf.Min(Ny - 1, Mathf.CeilToInt((p.Pos.y + k - Min.y) * inv));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((p.Pos.z - k - Min.z) * inv)), z1 = Mathf.Min(Nz - 1, Mathf.CeilToInt((p.Pos.z + k - Min.z) * inv));
                    for (int z = z0; z <= z1; z++)
                    {
                        float dz = Min.z + z * Cell - p.Pos.z;
                        for (int y = y0; y <= y1; y++)
                        {
                            float dy = Min.y + y * Cell - p.Pos.y;
                            float dyz = dy * dy + dz * dz;
                            if (dyz >= k2) continue;
                            int row = (z * MaxHeight + y) * MaxDims;
                            for (int x = x0; x <= x1; x++)
                            {
                                float dx = Min.x + x * Cell - p.Pos.x;
                                float w = 1f - (dx * dx + dyz) / k2;
                                if (w <= 0f) continue;
                                w *= w * amp;
                                int i = row + x;
                                D[i] += w; R[i] += w * c.r; G[i] += w * c.g; B[i] += w * c.b;
                            }
                        }
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Plumes whose smoke boxes (grown by <paramref name="margin"/>) touch, directly or through
        /// others, put into groups: one grid each.
        /// </summary>
        public static List<List<SmokePlume>> Groups(List<SmokePlume> plumes, float margin)
        {
            int n = plumes.Count;
            var lo = new Vector3[n]; var hi = new Vector3[n]; var has = new bool[n];
            var group = new int[n];
            for (int i = 0; i < n; i++) { has[i] = plumes[i].Bounds(margin, out lo[i], out hi[i]); group[i] = i; }
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (!has[i] || !has[j]) continue;
                    if (lo[i].x > hi[j].x || lo[j].x > hi[i].x || lo[i].y > hi[j].y || lo[j].y > hi[i].y || lo[i].z > hi[j].z || lo[j].z > hi[i].z) continue;
                    int a = Root(group, i), b = Root(group, j);
                    if (a != b) group[b] = a;
                }
            var o = new List<List<SmokePlume>>();
            var byRoot = new Dictionary<int, List<SmokePlume>>();
            for (int i = 0; i < n; i++)
            {
                int r = Root(group, i);
                if (!byRoot.TryGetValue(r, out var list)) { byRoot[r] = list = new List<SmokePlume>(); o.Add(list); }
                list.Add(plumes[i]);
            }
            return o;
        }

        private static int Root(int[] g, int i)
        {
            while (g[i] != i) i = g[i] = g[g[i]];
            return i;
        }
    }

    /// <summary>
    /// Clear air pushed into the smoke: a blast's bubble, a rocket's tunnel. The parcels are
    /// thrown aside too (SmokePlume.Blast, Wake), but they are metres wide, so the hole itself is
    /// cut by SmokeVolume.shader from this list: each void a sphere or a capsule (a segment with
    /// a radius) that takes the smoke out inside it. The void rides the wind, a blast's bubble
    /// rises a little (hot gas), and the smoke round it closes in: the radius shrinks and the
    /// cut fades over its life. At most Max at once (the shader's array).
    /// </summary>
    internal sealed class SmokeVoids
    {
        public const int Max = 16;

        private sealed class Void
        {
            public Vector3 A, B;
            public float R, Age, Life, Open, Rise;
        }

        private readonly List<Void> _v = new List<Void>();

        public int Count => _v.Count;

        /// <summary>The bubble a charge of <paramref name="kgTnt"/> clears: <paramref name="scale"/> metres per cube root of a kilo.</summary>
        public static float ClearRadius(float kgTnt, float scale) => scale * Mathf.Pow(Mathf.Max(0f, kgTnt), 1f / 3f);

        /// <summary>
        /// A blast at <paramref name="at"/> clearing <paramref name="clear"/> metres: throws the
        /// smoke of every plume it reaches and cuts the bubble, which closes over
        /// <paramref name="refill"/> x (2.5 s + 1 s per metre). False if no smoke was near.
        /// </summary>
        public bool Blast(List<SmokePlume> plumes, Vector3 at, float clear, float refill)
        {
            if (clear < 0.05f) return false;
            bool any = false;
            foreach (var pl in plumes)
                if (pl.Bounds(clear * SmokePlume.BlastReach, out var lo, out var hi) && Inside(at, lo, hi))
                {
                    pl.Blast(at, clear);
                    any = true;
                }
            if (any) Add(new Void { A = at, B = at, R = clear, Life = Mathf.Max(0.5f, refill * (2.5f + clear)), Open = 0.12f, Rise = 0.35f });
            return any;
        }

        /// <summary>
        /// Something fast flew <paramref name="a"/> to <paramref name="b"/> leaving a wake
        /// <paramref name="radius"/> wide: shoves the smoke aside and cuts the tunnel, which
        /// closes over <paramref name="refill"/> x 3 s. A flight's frames join into one tunnel.
        /// </summary>
        public bool Wake(List<SmokePlume> plumes, Vector3 a, Vector3 b, float radius, float push, float refill)
        {
            bool any = false;
            foreach (var pl in plumes)
                if (pl.Bounds(radius * 3f, out var lo, out var hi) && Crosses(a, b, lo, hi))
                {
                    pl.Wake(a, b, radius, push);
                    any = true;
                }
            if (!any) return false;
            // The flight's last frame ended where this one starts: make that tunnel longer.
            var last = _v.Count > 0 ? _v[_v.Count - 1] : null;
            if (last != null && last.Open == 0f && last.Age < 0.3f && (last.B - a).sqrMagnitude < 1e-4f && (b - last.A).magnitude < 12f)
                last.B = b;
            else
                Add(new Void { A = a, B = b, R = radius, Life = Mathf.Max(0.5f, refill * 3f) });
            return true;
        }

        private void Add(Void v)
        {
            if (_v.Count >= Max)
            {
                // Make room: the one nearest to closing.
                int k = 0;
                for (int i = 1; i < _v.Count; i++)
                    if (_v[i].Age / _v[i].Life > _v[k].Age / _v[k].Life) k = i;
                _v.RemoveAt(k);
            }
            _v.Add(v);
        }

        public void Tick(float dt, Vector3 wind)
        {
            for (int i = _v.Count - 1; i >= 0; i--)
            {
                var v = _v[i];
                v.Age += dt;
                if (v.Age >= v.Life) { _v.RemoveAt(i); continue; }
                var m = (wind + Vector3.up * (v.Rise * (1f - v.Age / v.Life))) * dt;
                v.A += m; v.B += m;
            }
        }

        public void Clear() => _v.Clear();

        /// <summary>
        /// What is left of the smoke at <paramref name="p"/> once the voids are cut out (1 = all
        /// of it): the shader's Carve, without the noise on the edge.
        /// </summary>
        public float Cut(Vector3 p)
        {
            float k = 1f;
            foreach (var v in _v)
            {
                float u = v.Age / v.Life;
                float r = v.R * Mathf.Lerp(1f, 0.4f, u);
                if (v.Open > 0f) r *= Mathf.Min(1f, 0.35f + 0.65f * v.Age / v.Open);
                float s = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, u));
                if (s <= 0.01f) continue;
                var ab = v.B - v.A;
                float h = Mathf.Clamp01(Vector3.Dot(p - v.A, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                float d = (p - v.A - ab * h).magnitude;
                if (d >= r) continue;
                float e = Mathf.Clamp01((d - 0.7f * r) / (0.3f * r));
                k *= 1f - s * (1f - e * e * (3f - 2f * e));
            }
            return k;
        }

        /// <summary>
        /// The voids that reach into the box <paramref name="lo"/>..<paramref name="hi"/>, for
        /// SmokeVolume.shader's _Voids: two per void, (A, radius) and (B, strength). Returns how many.
        /// </summary>
        public int Pack(IList<Vector4> dst, Vector3 lo, Vector3 hi)
        {
            int n = 0;
            foreach (var v in _v)
            {
                if (n >= Max || 2 * n + 1 >= dst.Count) break;
                float u = v.Age / v.Life;
                float r = v.R * Mathf.Lerp(1f, 0.4f, u);
                if (v.Open > 0f) r *= Mathf.Min(1f, 0.35f + 0.65f * v.Age / v.Open);   // a blast opens in a blink
                float s = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, u));
                if (s <= 0.01f) continue;
                var a = Vector3.Min(v.A, v.B) - Vector3.one * r;
                var b = Vector3.Max(v.A, v.B) + Vector3.one * r;
                if (a.x > hi.x || a.y > hi.y || a.z > hi.z || b.x < lo.x || b.y < lo.y || b.z < lo.z) continue;
                dst[2 * n] = new Vector4(v.A.x, v.A.y, v.A.z, r);
                dst[2 * n + 1] = new Vector4(v.B.x, v.B.y, v.B.z, s);
                n++;
            }
            return n;
        }

        private static bool Inside(Vector3 p, Vector3 lo, Vector3 hi)
            => p.x >= lo.x && p.y >= lo.y && p.z >= lo.z && p.x <= hi.x && p.y <= hi.y && p.z <= hi.z;

        /// <summary>Whether the segment a..b passes through the box (slab test).</summary>
        private static bool Crosses(Vector3 a, Vector3 b, Vector3 lo, Vector3 hi) => Clip(a, b, lo, hi, out _, out _);

        /// <summary>The part of the segment a..b inside the box, as fractions t0..t1 of it; false if none.</summary>
        public static bool Clip(Vector3 a, Vector3 b, Vector3 lo, Vector3 hi, out float t0, out float t1)
        {
            t0 = 0f; t1 = 1f;
            var d = b - a;
            for (int k = 0; k < 3; k++)
            {
                if (Mathf.Abs(d[k]) < 1e-6f)
                {
                    if (a[k] < lo[k] || a[k] > hi[k]) return false;
                    continue;
                }
                float inv = 1f / d[k];
                float u0 = (lo[k] - a[k]) * inv, u1 = (hi[k] - a[k]) * inv;
                if (u0 > u1) { var t = u0; u0 = u1; u1 = t; }
                t0 = Mathf.Max(t0, u0); t1 = Mathf.Min(t1, u1);
                if (t0 > t1) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Seeing through smoke: how much of the light from one point reaches another through every
    /// plume, for what smoke should hide (a day sight's lock, a flashbang's flash). Samples the
    /// same density the grid is splatted from, along the line instead of into voxels, with the
    /// voids cut out. Thin smoke the volume doesn't draw (below ThinAt) hides nothing; smoke as
    /// thick as the drawn solid (the shader's 0.5) takes out Extinction of the light per metre.
    /// </summary>
    internal static class SmokeSight
    {
        public const float Step = 0.25f;        // metres between samples
        public const int MaxSamples = 320;      // a longer stretch samples coarser
        public const float ThinAt = 0.2f;       // density below which smoke hides nothing
        public const float SolidAt = 0.6f;      // density from which it hides fully, per metre
        public const float Extinction = 2.5f;   // per metre of solid smoke: 1 m leaves 8 %

        private static readonly float[] _rho = new float[MaxSamples];

        /// <summary>Share of the light from <paramref name="a"/> that reaches <paramref name="b"/> (1 = clear air).</summary>
        public static float Transmittance(List<SmokePlume> plumes, SmokeVoids voids, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float len = ab.magnitude;
            if (len < 1e-3f || plumes.Count == 0) return 1f;
            var dir = ab / len;

            // Only the stretch of the line inside some plume's smoke is sampled.
            float t0 = float.MaxValue, t1 = float.MinValue;
            foreach (var pl in plumes)
                if (pl.Bounds(0f, out var lo, out var hi) && SmokeVoids.Clip(a, b, lo, hi, out float u0, out float u1))
                {
                    t0 = Mathf.Min(t0, u0 * len);
                    t1 = Mathf.Max(t1, u1 * len);
                }
            if (t1 <= t0) return 1f;
            int n = Mathf.Clamp(Mathf.CeilToInt((t1 - t0) / Step), 1, MaxSamples);
            float ds = (t1 - t0) / n;
            System.Array.Clear(_rho, 0, n);

            // Each parcel adds its splat (the grid's weight, 1 - d^2/k^2 squared) to the samples
            // its ball covers: one pass over the parcels, not one per sample.
            foreach (var pl in plumes)
                foreach (var p in pl.Parcels)
                {
                    float amp = pl.Amp(p);
                    if (amp < 0.005f) continue;
                    float k = p.R * SmokePlume.Reach, k2 = k * k;
                    var rel = p.Pos - a;
                    float tc = Vector3.Dot(rel, dir);
                    float h2 = rel.sqrMagnitude - tc * tc;
                    if (h2 >= k2) continue;
                    float half = Mathf.Sqrt(k2 - h2);
                    int i0 = Mathf.Max(0, Mathf.CeilToInt((tc - half - t0) / ds - 0.5f));
                    int i1 = Mathf.Min(n - 1, Mathf.FloorToInt((tc + half - t0) / ds - 0.5f));
                    for (int i = i0; i <= i1; i++)
                    {
                        float t = t0 + (i + 0.5f) * ds - tc;
                        float w = 1f - (h2 + t * t) / k2;
                        if (w > 0f) _rho[i] += w * w * amp;
                    }
                }

            float tau = 0f;
            for (int i = 0; i < n; i++)
            {
                float rho = _rho[i];
                if (rho <= ThinAt) continue;
                if (voids != null && voids.Count > 0) rho *= voids.Cut(a + dir * (t0 + (i + 0.5f) * ds));
                float o = Mathf.Clamp01((rho - ThinAt) / (SolidAt - ThinAt));
                tau += o * o * (3f - 2f * o) * ds;
            }
            return Mathf.Exp(-Extinction * tau);
        }
    }
}
