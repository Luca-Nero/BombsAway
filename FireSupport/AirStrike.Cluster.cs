using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The CBU-87 (5.16.0). The SUU-65 dispenser falls toward the mark as a JDAM does and opens
    /// CbuOpenHeight over it. Its spin throws the CbuBomblets BLU-97/B bomblets out; each one's
    /// inflatable decelerator brakes it hard (a time constant of BombletTau), and it comes down
    /// at about CbuBombletSpeed, nose first. Every bomblet's whole fall is worked out at the
    /// call: where it lands is spread evenly over a CbuPatternWidth x CbuPatternLength ellipse
    /// round the mark (across x along the run), and its throw is what takes it there. The
    /// dispenser is aimed short by the distance its bomblets carry on along its flight, so the
    /// throws come out even round it. Their fall speeds differ a little, so they land over a
    /// second or two, and go off where they first meet something: a roof (the jet goes on down
    /// into the room), a body, the ground (BombsAway.Blu97). A CbuDudRate share lie where they
    /// land, live: one goes off when something moves it or a round hits it.
    /// </summary>
    internal static partial class AirStrike
    {
        private const float BombletTau = 0.6f;    // s: the decelerator's braking, as exp(-t / tau)

        private sealed class Bomblet
        {
            public Vector3 O, V0, Vend, Last;
            public float T;                       // seconds from the opening to its landing, as planned
            public GameObject Body;
            public bool Dud, Done;

            /// <summary>Where it is <paramref name="t"/> s after the opening: braked from its throw toward its fall.</summary>
            public Vector3 PosAt(float t) => O + Vend * t + (V0 - Vend) * (BombletTau * (1f - Mathf.Exp(-t / BombletTau)));
            public Vector3 VelAt(float t) => Vend + (V0 - Vend) * Mathf.Exp(-t / BombletTau);
        }

        private sealed class Cluster
        {
            public float OpenAt, FirstLand, LastLand;   // planned from the release, then shifted to game time (Shift)
            public Vector3 Open, OpenVel;
            public readonly List<Bomblet> Bomblets = new List<Bomblet>();
            public bool Opened, Done;
            public int Bursts, Duds, Left;
            public float FruitMs, WorstMs;
            public int Walks;

            public void Shift(float t0) { OpenAt += t0; FirstLand += t0; LastLand += t0; }
        }

        private sealed class Dud
        {
            public GameObject Go;
            public Rigidbody Rb;
            public Collider Col;
            public float Born;
            public bool Set;                      // it goes off on the next frame
        }

        private struct BombletBurst { public Vector3 At, Dir; public Cluster From; }

        private static readonly List<BombletBurst> _bomblets = new List<BombletBurst>();
        private static readonly List<Dud> _duds = new List<Dud>();
        private static int _bombletFx;

        /// <summary>A cluster bomb may draw every CbuFxEvery-th bomblet's burst effect.</summary>
        public static bool TakeBombletFx() => _bombletFx++ % Mathf.Max(1, Config.CbuFxEvery) == 0;

        // ── Planning ────────────────────────────────────────────────────────────

        /// <summary>
        /// The dispenser's fall (<paramref name="fall"/>, from the release at T0 = 0) and its
        /// bomblets. The fall is aimed past or short of <paramref name="aim"/> until the bomblets'
        /// carry from where it opens is centred on it (three passes); <paramref name="tf"/> is
        /// the time from the release to the pattern's middle coming down.
        /// </summary>
        private static Cluster PlanCluster(Vector3 r, Vector3 rdir, float v0, Vector3 aim, Vector3 tdir, float v1, out Path fall, out float tf)
        {
            float h = Mathf.Max(30f, Config.CbuOpenHeight);
            Vector3 target = aim, open = aim, vel = Vector3.down;
            int iOpen = 1;
            fall = null;
            for (int it = 0; it < 3; it++)
            {
                fall = BombPath(r, rdir, v0, target, tdir, v1, out _);
                int n = fall.Pos.Count;
                iOpen = n - 2;
                for (int i = 1; i < n - 1; i++) if (fall.Pos[i].y <= aim.y + h) { iOpen = i; break; }
                open = fall.Pos[iOpen];
                vel = (fall.Pos[iOpen + 1] - fall.Pos[iOpen - 1]) / (2f * SampleDt);
                // Thrown out with nothing, a bomblet carries on its dispenser's way for about tau.
                Vector3 miss = open + vel * BombletTau - aim;
                miss.y = 0f;
                target -= miss;
            }

            var c = new Cluster { OpenAt = iOpen * SampleDt, Open = open, OpenVel = vel };
            Vector3 heading = Vector3.ProjectOnPlane(rdir, Vector3.up).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, heading).normalized;
            int count = Mathf.Clamp(Config.CbuBomblets, 1, 400);
            float halfW = Mathf.Max(1f, Config.CbuPatternWidth) * 0.5f, halfL = Mathf.Max(1f, Config.CbuPatternLength) * 0.5f;
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            float spin = Random.Range(0f, Mathf.PI * 2f);
            float first = float.MaxValue, last = 0f, sum = 0f;
            for (int i = 0; i < count; i++)
            {
                // Even over the ellipse (a sunflower), each nudged so it never looks drawn.
                float rr = Mathf.Sqrt((i + Random.Range(0.1f, 0.9f)) / count);
                float a = i * golden + spin + Random.Range(-0.25f, 0.25f);
                Vector3 land = aim + across * (Mathf.Cos(a) * rr * halfW) + heading * (Mathf.Sin(a) * rr * halfL);
                land.y = aim.y;

                float vt = Mathf.Max(5f, Config.CbuBombletSpeed * (1f + 0.08f * Mathf.Clamp(Gauss(), -2.5f, 2.5f)));
                float t = FallTime(open.y - land.y, vel.y, vt);
                float carry = BombletTau * (1f - Mathf.Exp(-t / BombletTau));
                // Straight down at the end; the throw across makes up the rest.
                Vector3 vend = Vector3.down * vt;
                Vector3 flat = (land - open) / carry;
                var b = new Bomblet
                {
                    O = open, Vend = vend, T = t, Last = open,
                    V0 = new Vector3(flat.x, vel.y, flat.z),
                    Dud = Random.value < Mathf.Clamp01(Config.CbuDudRate),
                };
                c.Bomblets.Add(b);
                first = Mathf.Min(first, t); last = Mathf.Max(last, t); sum += t;
            }
            c.Left = count;
            c.FirstLand = c.OpenAt + first;
            c.LastLand = c.OpenAt + last;
            tf = c.OpenAt + sum / count;
            return c;
        }

        /// <summary>
        /// How long a bomblet thrown out at vertical speed <paramref name="vy"/> (falling: below 0)
        /// takes to come down <paramref name="h"/> metres as its decelerator brakes it toward
        /// <paramref name="vt"/>: vt t + (-vy - vt) tau (1 - e^(-t/tau)) = h. Rises with t; bisected.
        /// </summary>
        private static float FallTime(float h, float vy, float vt)
        {
            float lo = 0f, hi = 400f;
            for (int i = 0; i < 40; i++)
            {
                float t = 0.5f * (lo + hi);
                float fallen = vt * t + (-vy - vt) * BombletTau * (1f - Mathf.Exp(-t / BombletTau));
                if (fallen < h) lo = t; else hi = t;
            }
            return 0.5f * (lo + hi);
        }

        // ── Per frame ───────────────────────────────────────────────────────────

        /// <summary>The dispenser at its opening height: it opens (once), then its bomblets fall.</summary>
        private static void TickDispenser(Strike s, float now)
        {
            var b = s.B;
            var c = b.C;
            if (!c.Opened) OpenDispenser(s, now);
            if (c.Done) return;

            float t = now - c.OpenAt;
            foreach (var m in c.Bomblets)
            {
                if (m.Done) continue;
                Vector3 pos = m.PosAt(t);
                Vector3 seg = pos - m.Last;
                float len = seg.magnitude;
                if (len > 1e-4f)
                {
                    Vector3 dir = seg / len;
                    if (FireMission.PathHit(m.Last, dir, len + 0.1f, out Vector3 hit, out Vector3 normal))
                    {
                        Land(c, m, hit, normal, dir);
                        continue;
                    }
                }
                if (m.Body != null)
                {
                    Vector3 v = m.VelAt(t);
                    m.Body.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.down, Vector3.up));
                }
                m.Last = pos;
                // Past the arena's edge: into the void.
                if (t > m.T + 8f) { DropBomblet(m); c.Left--; }
            }
            if (c.Left <= 0) FinishCluster(s);
        }

        private static void OpenDispenser(Strike s, float now)
        {
            var b = s.B;
            var c = b.C;
            c.Opened = true;
            Hook();   // a round that hits a dud sets it off (OnSurfaceHit)
            if (b.Fall != null) b.Fall.To = Mathf.Min(b.Fall.To, now);
            DestroyBody(b);
            // The case splits along its length with a sharp crack, and the bomblets fly out.
            try { ExplosionFx.Play("CbuOpen", c.Open, c.OpenVel.normalized, false, default); } catch { }
            var cam = Camera.main;
            float delay = cam != null ? Vector3.Distance(cam.transform.position, c.Open) / SpeedOfSound : 0f;
            Sfx.Play("CbuOpen", c.Open, null, 1f, delay);
            foreach (var m in c.Bomblets) m.Body = BuildBomblet();
            if (Config.Dbg1) MelonLogger.Msg($"[Air] CBU-87 open at {c.Open} ({c.Open.y - s.Mark.y:F0} m over the mark), {c.Bomblets.Count} bomblets, landing in {c.FirstLand - now:F1}-{c.LastLand - now:F1}s");
        }

        /// <summary>A bomblet comes down: it goes off where it hits (next frame, in turn), or lies there live.</summary>
        private static void Land(Cluster c, Bomblet m, Vector3 hit, Vector3 normal, Vector3 dir)
        {
            m.Done = true;
            c.Left--;
            if (m.Body != null) Object.Destroy(m.Body);
            m.Body = null;
            if (m.Dud) { LayDud(hit, normal, dir); c.Duds++; return; }
            _bomblets.Add(new BombletBurst { At = hit + normal * 0.12f, Dir = dir, From = c });
        }

        private static void FinishCluster(Strike s)
        {
            var c = s.B.C;
            if (c.Done) return;
            c.Done = true;
            s.B.Done = true;
            // Logged once its last burst has gone off (FireBomblets), so the log holds them all.
            if (!_bomblets.Exists(q => q.From == c)) LogCluster(c);
        }

        private static void LogCluster(Cluster c) =>
            MelonLogger.Msg($"[Air] CBU-87: {c.Bursts} bomblets went off, {c.Duds} duds lie live; FruitLib {c.FruitMs:F0} ms in all, " +
                            $"{c.WorstMs:F0} ms in the worst frame ({Mathf.Max(1, Config.CbuBurstsPerFrame)} a frame at most), {c.Walks} wound walks");

        /// <summary>
        /// Every frame: up to CbuBurstsPerFrame bomblets go off, oldest first, and the live duds
        /// are watched. Each is FruitLib's BombsAway.Blu97 with its jet and belt along its fall.
        /// </summary>
        private static void FireBomblets()
        {
            TickDuds();
            if (_bomblets.Count == 0) return;
            int n = Mathf.Min(_bomblets.Count, Mathf.Max(1, Config.CbuBurstsPerFrame));
            float frame = 0f;
            for (int i = 0; i < n; i++)
            {
                var q = _bomblets[i];
                try
                {
                    var x = ExplosionParams.FromBlu97Config(q.At);
                    x.Forward = q.Dir.sqrMagnitude > 1e-4f ? q.Dir.normalized : Vector3.down;
                    x.Axis = x.Forward;
                    ExplosionSystem.Detonate(x);
                    var st = FruitLib.FruitBallistics.LastExplosion;
                    if (q.From != null)
                    {
                        q.From.Bursts++;
                        q.From.FruitMs += st.TotalMs;
                        q.From.Walks += st.Walks;
                        frame += st.TotalMs;
                    }
                }
                catch (System.Exception e) { MelonLogger.Warning($"[Air] bomblet burst failed: {e.Message}"); }
            }
            Cluster from = _bomblets[n - 1].From;
            _bomblets.RemoveRange(0, n);
            if (from == null) return;
            from.WorstMs = Mathf.Max(from.WorstMs, frame);
            // The last of a cluster's bursts: what it cost, for the frame time.
            if (from.Done && !_bomblets.Exists(q => q.From == from)) LogCluster(from);
        }

        // ── Live duds ───────────────────────────────────────────────────────────

        private static void LayDud(Vector3 hit, Vector3 normal, Vector3 dir)
        {
            if (Config.CbuMaxDuds <= 0) return;
            while (_duds.Count >= Config.CbuMaxDuds) { if (_duds[0].Go != null) Object.Destroy(_duds[0].Go); _duds.RemoveAt(0); }
            // Nose in, slanted as it struck, or tipped over.
            Vector3 axis = Random.value < 0.5f ? dir : Vector3.Cross(normal, Random.onUnitSphere).normalized;
            if (axis.sqrMagnitude < 1e-4f) axis = -normal;
            // The bundle's BLU97 (5.24.0) with its decelerator gone, its own box round the can.
            // Its own body on the world's layer: the blast, bodies and props move it.
            var go = Object.Instantiate(OrdnanceModels.Asset("BLU97"));
            go.name = "BA_Blu97Dud";
            var chute = go.transform.Find("Chute");
            if (chute != null) Object.Destroy(chute.gameObject);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            var body = go.transform.Find("Body");
            var mf = body != null ? body.GetComponent<MeshFilter>() : null;
            var box = go.AddComponent<BoxCollider>();
            if (mf != null && mf.sharedMesh != null) { box.center = mf.sharedMesh.bounds.center; box.size = mf.sharedMesh.bounds.size; }
            else { box.center = Vector3.zero; box.size = new Vector3(0.096f, 0.096f, 0.26f); }
            // Nose in: its probe's tip on the hit, so the box doesn't start inside the ground; on its side, resting on it.
            float tip = box.center.z + 0.5f * box.size.z;
            Vector3 at = Mathf.Abs(Vector3.Dot(axis, normal)) > 0.3f ? hit - axis * tip + normal * 0.01f : hit + normal * (0.5f * box.size.x + 0.01f);
            go.transform.SetPositionAndRotation(at, Quaternion.FromToRotation(Vector3.forward, axis));
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1.5f;
            _duds.Add(new Dud { Go = go, Rb = rb, Col = box, Born = Time.time });
        }

        /// <summary>A round hit something: if it was a dud, it goes off on the next frame (FruitLib is mid-step).</summary>
        private static bool DudHit(Collider col)
        {
            if (col == null || _duds.Count == 0) return false;
            foreach (var d in _duds)
                if (d.Col != null && d.Col.Pointer == col.Pointer) { d.Set = true; return true; }
            return false;
        }

        private static void TickDuds()
        {
            if (_duds.Count == 0) return;
            float now = Time.time, sense = Mathf.Max(0.2f, Config.CbuDudSensitivity);
            for (int i = _duds.Count - 1; i >= 0; i--)
            {
                var d = _duds[i];
                if (d.Go == null) { _duds.RemoveAt(i); continue; }
                // Settled first: the drop that laid it doesn't count.
                bool moved = now - d.Born > 1.5f && d.Rb != null && d.Rb.linearVelocity.magnitude > sense;
                if (!d.Set && !moved) continue;
                Vector3 at = d.Go.transform.position, axis = d.Go.transform.forward;
                Object.Destroy(d.Go);
                _duds.RemoveAt(i);
                _bomblets.Add(new BombletBurst { At = at, Dir = Random.value < 0.5f ? axis : -axis });
                if (Config.Dbg1) MelonLogger.Msg($"[Air] a BLU-97 dud went off at {at} ({(d.Set ? "hit" : "moved")})");
            }
        }

        /// <summary>A BLU-97/B: the bundle's BLU97 (5.24.0, 1.5x real: the yellow can, its standoff probe out, the decelerator behind it).</summary>
        private static GameObject BuildBomblet()
        {
            var model = SpawnBare("BLU97");
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return model;
        }

        // ── Clearing ────────────────────────────────────────────────────────────

        private static void DropBomblet(Bomblet m)
        {
            m.Done = true;
            if (m.Body != null) Object.Destroy(m.Body);
            m.Body = null;
        }

        private static void DropCluster(Cluster c)
        {
            foreach (var m in c.Bomblets) if (m.Body != null) { Object.Destroy(m.Body); m.Body = null; }
        }

        private static void ClearCluster()
        {
            _bomblets.Clear();
            foreach (var d in _duds) if (d.Go != null) Object.Destroy(d.Go);
            _duds.Clear();
        }
    }
}
