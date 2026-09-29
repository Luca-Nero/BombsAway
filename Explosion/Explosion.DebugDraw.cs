using MelonLoader;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Color = UnityEngine.Color;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// Draws what FruitLib's ballistics did with a BombsAway detonation: every fragment leg
    /// (coloured by how it ended) and a line to each body / limb the blast reached (coloured by
    /// how much of it got through cover). Off by default. FruitLib only builds the traces while
    /// someone is subscribed, so the events are hooked and unhooked from <see cref="Tick"/> as
    /// Config.DebugDrawExplosions flips, and cost nothing while it is off.
    ///
    /// Lines are pooled LineRenderers under a DontDestroyOnLoad root. A detonation can raise
    /// thousands of traces, so the pool is capped and the oldest line is reused once it is full.
    /// </summary>
    internal static class ExplosionDebugDraw
    {
        private const string SpecPrefix = "BombsAway.";
        private const string Tag = "[ExplosionDebug]";

        // ── Line pool ────────────────────────────────────────────────────────────

        private sealed class Line
        {
            public GameObject Go;
            public LineRenderer Lr;
            public float Expires;
        }

        private static GameObject _root;
        private static Material _mat;
        private static readonly Stack<Line> _free = new Stack<Line>();
        // Oldest first: every line lives the same time, so this is also expiry order.
        private static readonly Queue<Line> _live = new Queue<Line>();
        private static int _total;                 // lines that exist, live or free

        // ── State ────────────────────────────────────────────────────────────────

        private static bool _fragHooked, _blastHooked;
        private static int _spentCounter;
        private static bool _failedOnce;

        // Tally of the detonation(s) since the last summary. Blast traces arrive before
        // Exploded and fragments after it, so a summary is printed from the next Tick.
        private static readonly int[] _ends = new int[8];
        private static readonly List<string> _specs = new List<string>();
        private static int _explosions, _fragments, _laterLegs, _blastBodies, _blastLimbs, _blastOccluded, _linesDrawn;
        private static int _jetLegs, _spallLegs, _spallOverBudget, _recycled, _boneLegs;
        private static bool _dirty;

        // ── Tick ─────────────────────────────────────────────────────────────────

        public static void Tick()
        {
            try
            {
                bool on = Config.DebugDrawExplosions;
                bool wantBlast = on && Config.DebugDrawBlast;

                if (on && !_fragHooked)
                {
                    FruitLib.FruitBallistics.FragmentTraced += OnFragment;
                    FruitLib.FruitBallistics.Exploded += OnExploded;
                    _fragHooked = true;
                }
                else if (!on && _fragHooked)
                {
                    FruitLib.FruitBallistics.FragmentTraced -= OnFragment;
                    FruitLib.FruitBallistics.Exploded -= OnExploded;
                    _fragHooked = false;
                }

                if (wantBlast && !_blastHooked)
                {
                    FruitLib.FruitBallistics.BlastTraced += OnBlast;
                    _blastHooked = true;
                }
                else if (!wantBlast && _blastHooked)
                {
                    FruitLib.FruitBallistics.BlastTraced -= OnBlast;
                    _blastHooked = false;
                }

                if (_dirty) Flush();

                // Switched off: take the lines down now rather than leaving them for ten seconds.
                if (!on) { if (_live.Count > 0) ClearLines(); }
                else ExpireLines();
            }
            catch (Exception e)
            {
                if (_failedOnce) return;
                _failedOnce = true;
                MelonLogger.Warning($"{Tag} tick failed (reported once): {e.Message}");
            }
        }

        // ── Event handlers ───────────────────────────────────────────────────────

        private static bool Mine(FruitLib.ExplosionSpec spec)
            => spec != null && spec.Id != null && spec.Id.StartsWith(SpecPrefix, StringComparison.Ordinal);

        private static void OnExploded(FruitLib.ExplosionInfo x)
        {
            try
            {
                if (!Mine(x.Spec)) return;
                _explosions++;
                if (!_specs.Contains(x.Spec.Id)) _specs.Add(x.Spec.Id);
                _dirty = true;
            }
            catch { }
        }

        private static void OnFragment(FruitLib.FragmentTrace t)
        {
            try
            {
                if (!Mine(t.Spec)) return;

                int end = (int)t.EndedBy;
                if (end >= 0 && end < _ends.Length) _ends[end]++;
                _fragments++;
                if (t.Leg > 0) _laterLegs++;
                if (t.Jet) _jetLegs++;
                if (t.Spall) _spallLegs++;
                if (t.Bone) _boneLegs++;
                if (t.EndedBy == FruitLib.FragmentEnd.OverBudget && t.Spall) _spallOverBudget++;
                _dirty = true;

                // Plain fragments that hit nothing, or only stopped in a wall, are most of them:
                // draw a sample. Anything that met a limb, ricocheted, went through a wall, or is
                // jet or spall always goes up - otherwise a big charge fills the pool and the
                // first-traced half of its sphere is recycled away.
                bool boring = t.EndedBy == FruitLib.FragmentEnd.Spent || t.EndedBy == FruitLib.FragmentEnd.TooWeak
                              || t.EndedBy == FruitLib.FragmentEnd.Stopped;
                if (boring && t.Leg == 0 && !t.Jet && !t.Spall)
                {
                    int every = Config.DebugDrawSpentEvery;
                    if (every > 1 && (_spentCounter++ % every) != 0) return;
                }

                DrawArc(t);

                if (t.EndedBy == FruitLib.FragmentEnd.Penetrated)
                    DrawSegment(t.End, t.Exit, new Color(1f, 1f, 1f, 0.9f), 0.03f);
            }
            catch (Exception e) { ReportOnce(e); }
        }

        private static void OnBlast(FruitLib.BlastTrace b)
        {
            try
            {
                if (!Mine(b.Spec)) return;

                if (b.Overpressure) _blastLimbs++; else _blastBodies++;
                if (b.Occluder != null) _blastOccluded++;
                _dirty = true;

                float tr = Mathf.Clamp01(b.Transmission);
                Color c = tr >= 0.5f
                    ? Color.Lerp(new Color(1f, 0.9f, 0.1f, 0.8f), new Color(0.1f, 1f, 0.1f, 0.8f), (tr - 0.5f) * 2f)
                    : Color.Lerp(new Color(1f, 0.1f, 0.1f, 0.8f), new Color(1f, 0.9f, 0.1f, 0.8f), tr * 2f);

                DrawSegment(b.Origin, b.Target, c, b.Overpressure ? 0.005f : 0.012f);

                // A small cross where the line meets whatever is in the way.
                if (b.Occluder != null)
                {
                    Vector3 d = b.Target - b.Origin;
                    float len = d.magnitude;
                    if (len < 0.0001f) return;
                    var ray = new Ray(b.Origin, d / len);
                    if (!b.Occluder.Raycast(ray, out RaycastHit hit, len)) return;

                    Color cross = new Color(1f, 1f, 1f, 0.9f);
                    const float r = 0.08f;
                    Vector3 p = hit.point;
                    DrawSegment(p - new Vector3(r, 0f, 0f), p + new Vector3(r, 0f, 0f), cross, 0.01f);
                    DrawSegment(p - new Vector3(0f, r, 0f), p + new Vector3(0f, r, 0f), cross, 0.01f);
                    DrawSegment(p - new Vector3(0f, 0f, r), p + new Vector3(0f, 0f, r), cross, 0.01f);
                }
            }
            catch (Exception e) { ReportOnce(e); }
        }

        private static void ReportOnce(Exception e)
        {
            if (_failedOnce) return;
            _failedOnce = true;
            MelonLogger.Warning($"{Tag} draw failed (reported once): {e}");
        }

        // ── Drawing ──────────────────────────────────────────────────────────────

        private static Color ColorFor(FruitLib.FragmentEnd end)
        {
            switch (end)
            {
                case FruitLib.FragmentEnd.Spent:         return new Color(0.7f, 0.7f, 0.7f, 0.25f);
                case FruitLib.FragmentEnd.Lodged:        return new Color(1f, 0.1f, 0.1f, 0.9f);
                case FruitLib.FragmentEnd.PassedThrough: return new Color(1f, 0.1f, 1f, 0.9f);
                case FruitLib.FragmentEnd.Ricocheted:    return new Color(1f, 0.95f, 0.1f, 0.9f);
                case FruitLib.FragmentEnd.Penetrated:    return new Color(0.1f, 1f, 1f, 0.9f);
                case FruitLib.FragmentEnd.Stopped:       return new Color(1f, 0.55f, 0.05f, 0.9f);
                case FruitLib.FragmentEnd.OverBudget:    return new Color(0.45f, 0.1f, 0.05f, 0.9f);
                case FruitLib.FragmentEnd.TooWeak:       return new Color(0.5f, 0.5f, 0.5f, 0.3f);
                default:                                 return new Color(1f, 1f, 1f, 0.5f);
            }
        }

        /// <summary>The leg's arc, p(t) = Start + v t + g t² / 2, sampled into a short polyline.</summary>
        private static void DrawArc(in FruitLib.FragmentTrace t)
        {
            Vector3 g = Physics.gravity;
            float T = Mathf.Max(0f, t.Time);

            // The arc leaves its chord by at most g t² / 8: below a couple of centimetres a
            // straight line is the same picture.
            int n = g.magnitude * T * T * 0.125f < 0.02f ? 2 : 10;

            var line = Begin(n, ColorFor(t.EndedBy), t.Jet ? 0.03f : t.Leg > 0 ? 0.015f : 0.01f);
            if (line == null) return;

            var lr = line.Lr;
            lr.SetPosition(0, t.Start);
            for (int i = 1; i < n - 1; i++)
            {
                float s = T * i / (n - 1);
                lr.SetPosition(i, t.Start + t.ArcVelocity * s + 0.5f * s * s * g);
            }
            lr.SetPosition(n - 1, t.End);
        }

        private static void DrawSegment(Vector3 a, Vector3 b, Color c, float width)
        {
            var line = Begin(2, c, width);
            if (line == null) return;
            line.Lr.SetPosition(0, a);
            line.Lr.SetPosition(1, b);
        }

        private static Line Begin(int points, Color c, float width)
        {
            var line = Acquire();
            if (line == null) return null;

            var lr = line.Lr;
            lr.material = _mat;
            lr.positionCount = points;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = c;
            lr.endColor = c;

            line.Go.SetActive(true);
            line.Expires = Time.unscaledTime + Mathf.Max(0.1f, Config.DebugDrawSeconds);
            _live.Enqueue(line);
            _linesDrawn++;
            return line;
        }

        // ── Pool ─────────────────────────────────────────────────────────────────

        private static bool EnsureRoot()
        {
            if (_root != null && _mat != null) return true;

            // Lost the root (or the material was unloaded): whatever the pool holds is gone with it.
            if (_root == null)
            {
                _free.Clear();
                _live.Clear();
                _total = 0;
                _root = new GameObject("BombsAwayDebugDraw");
                GameObject.DontDestroyOnLoad(_root);
            }

            if (_mat == null)
            {
                var shader = Config.FindSpriteShader();
                if (shader == null) return false;
                _mat = new Material(shader);
                _mat.SetInt("_ZTest", -1);   // on top of the world, as the lock-on brackets are
            }
            return _root != null && _mat != null;
        }

        private static Line Acquire()
        {
            if (!EnsureRoot()) return null;

            while (_free.Count > 0)
            {
                var l = _free.Pop();
                if (l.Lr != null && l.Go != null) return l;
                _total--;
            }

            if (_total < Mathf.Max(16, Config.DebugDrawMaxLines))
            {
                var go = new GameObject("DbgLine");
                go.transform.SetParent(_root.transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCornerVertices = 0;
                _total++;
                return new Line { Go = go, Lr = lr };
            }

            // Full: reuse the oldest line still on screen.
            while (_live.Count > 0)
            {
                var l = _live.Dequeue();
                if (l.Lr != null && l.Go != null) { _recycled++; return l; }
                _total--;
            }
            return null;
        }

        private static void ExpireLines()
        {
            float now = Time.unscaledTime;
            while (_live.Count > 0)
            {
                var l = _live.Peek();
                if (l.Lr != null && l.Go != null && l.Expires > now) break;
                _live.Dequeue();
                if (l.Lr == null || l.Go == null) { _total--; continue; }
                l.Go.SetActive(false);
                _free.Push(l);
            }
        }

        private static void ClearLines()
        {
            while (_live.Count > 0)
            {
                var l = _live.Dequeue();
                if (l.Lr == null || l.Go == null) { _total--; continue; }
                l.Go.SetActive(false);
                _free.Push(l);
            }
        }

        // ── Summary ──────────────────────────────────────────────────────────────

        private static void Flush()
        {
            _dirty = false;
            if (_explosions == 0 && _fragments == 0 && _blastBodies == 0 && _blastLimbs == 0) return;

            var sb = new StringBuilder();
            sb.Append(Tag).Append(' ').Append(string.Join(", ", _specs)).Append(" x").Append(_explosions).Append(": ");
            sb.Append(_fragments).Append(" fragment legs (");
            bool first = true;
            for (int i = 0; i < _ends.Length; i++)
            {
                if (_ends[i] == 0) continue;
                if (!first) sb.Append(", ");
                sb.Append(((FruitLib.FragmentEnd)i).ToString()).Append(' ').Append(_ends[i]);
                first = false;
            }
            sb.Append("), ").Append(_laterLegs).Append(" after a ricochet/wall/limb; blast reached ")
              .Append(_blastBodies).Append(" bodies + ").Append(_blastLimbs).Append(" limbs, ")
              .Append(_blastOccluded).Append(" behind cover; ").Append(_linesDrawn).Append(" lines drawn");
            if (_jetLegs > 0 || _spallLegs > 0)
                sb.Append("; jet ").Append(_jetLegs).Append(" legs, spall ").Append(_spallLegs)
                  .Append(" legs (").Append(_spallOverBudget).Append(" over budget)");
            if (_boneLegs > 0)
                sb.Append("; bone fragments ").Append(_boneLegs).Append(" legs");
            if (_recycled > 0)
                sb.Append("; POOL FULL: ").Append(_recycled).Append(" older lines reused (raise DebugDrawMaxLines)");
            MelonLogger.Msg(sb.ToString());

            Array.Clear(_ends, 0, _ends.Length);
            _specs.Clear();
            _explosions = _fragments = _laterLegs = _blastBodies = _blastLimbs = _blastOccluded = _linesDrawn = 0;
            _jetLegs = _spallLegs = _spallOverBudget = _recycled = _boneLegs = 0;
        }
    }
}
