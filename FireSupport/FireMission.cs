using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>What the binoculars call in. Cycled with the warhead key; air and precision strikes go here later.</summary>
    internal enum FireMissionType { Arty155 }

    /// <summary>
    /// Fire missions called from the binoculars' laser rangefinder. A 155 mm battery somewhere
    /// off the map answers on the radio (RadioLog) the way a real net runs, compressed:
    ///   OBS: FIRE MISSION, the grid, DANGER CLOSE if it is near you, the rounds. OVER.
    ///   FDC reads it back; ArtyShotDelay after the call the guns fire: SHOT. The shells fly
    ///   ArtyFlightTime; ArtySplashWarning before the first lands: SPLASH. Then ROUNDS COMPLETE.
    /// Each gun's round lands within ArtyVolleySpread of the first, scattered round the mark
    /// (ArtyDispersion), coming down steeply (ArtyDescentAngle) from the battery's side, which
    /// is fixed per scene. A round's whistle starts ArtyWhistleLead before it lands, late by the
    /// distance like its bang; its shell is drawn for the last moment of its flight, cuts through
    /// smoke, and goes off where its path meets the first thing in the way: a roof, a body, the
    /// ground. A round whose path misses the arena falls into the void. Only one mission at a
    /// time unless ArtyStacking.
    /// </summary>
    internal static class FireMission
    {
        private const float ShellShown = 0.5f;     // seconds of flight drawn before impact
        private const float CompleteAfter = 2f;    // seconds after the last round: ROUNDS COMPLETE

        private sealed class Round
        {
            public float ImpactAt;
            public Vector3 Aim, Dir;               // where it is aimed; its flight direction (down and away from the battery)
            public bool Predicted, HasTarget, Whistled, Done;
            public Vector3 Target, Normal;         // where its path meets something
            public GameObject Shell;
            public Vector3 LastPos;
        }

        private sealed class Mission
        {
            public int Number;
            public Vector3 Mark;
            public string Grid;
            public int Rounds;
            public float CalledAt, AckAt, ShotAt, FirstImpact, LastImpact;
            public bool Acked, Shot, Splashed, Complete;
            public readonly List<Round> Shells = new List<Round>();
        }

        private static readonly List<Mission> _missions = new List<Mission>();
        private static int _count;
        private static float _bearing = -1f;      // the battery, degrees from +Z, per scene
        private static Material _shellMat, _trailMat;

        public static FireMissionType Type = FireMissionType.Arty155;

        public static string TypeName => Type switch { _ => "ARTY 155 HE" };

        /// <summary>A mission is running (calls are refused unless ArtyStacking).</summary>
        public static bool Busy => _missions.Exists(m => !m.Complete);

        // ── What the binoculars show ────────────────────────────────────────────

        /// <summary>The newest running mission's mark.</summary>
        public static bool TryMark(out Vector3 mark, out string status)
        {
            mark = default; status = null;
            Mission m = null;
            for (int i = _missions.Count - 1; i >= 0; i--) if (!_missions[i].Complete) { m = _missions[i]; break; }
            if (m == null) return false;
            mark = m.Mark;
            float now = Time.time;
            if (!m.Shot) status = $"MSN {m.Number:00} CALL";
            else if (now < m.FirstImpact) status = $"MSN {m.Number:00} SPLASH {Mathf.CeilToInt(m.FirstImpact - now):00}";
            else status = $"MSN {m.Number:00} IMPACT";
            return true;
        }

        // ── Calling ─────────────────────────────────────────────────────────────

        /// <summary>The lase is complete: call a mission on <paramref name="mark"/>. False if the battery is busy.</summary>
        public static bool Call(Vector3 mark, Vector3 observer)
        {
            if (!Config.ArtyStacking && Busy)
            {
                RadioLog.Observer($"Fire mission. Grid {Grid(mark)}. Over.");
                RadioLog.Battery("Unable, mission in progress. Out.");
                return false;
            }

            if (_bearing < 0f) _bearing = Config.ArtyBatteryHeading >= 0f ? Config.ArtyBatteryHeading : Random.Range(0f, 360f);
            float now = Time.time;
            var m = new Mission
            {
                Number = ++_count,
                Mark = mark,
                Grid = Grid(mark),
                Rounds = Mathf.Clamp(Config.ArtyRounds, 1, 24),
                CalledAt = now,
            };
            m.ShotAt = now + Mathf.Max(1f, Config.ArtyShotDelay);
            m.AckAt = now + Mathf.Max(1f, Config.ArtyShotDelay) * 0.5f;
            m.FirstImpact = m.ShotAt + Mathf.Max(Config.ArtySplashWarning + 0.5f, Config.ArtyFlightTime);

            for (int i = 0; i < m.Rounds; i++)
            {
                float lands = m.FirstImpact + (i == 0 ? 0f : Random.Range(0f, Mathf.Max(0f, Config.ArtyVolleySpread)));
                m.Shells.Add(new Round { ImpactAt = lands, Aim = Scatter(mark), Dir = Incoming() });
                m.LastImpact = Mathf.Max(m.LastImpact, lands);
            }
            _missions.Add(m);

            bool danger = Vector3.Distance(mark, observer) < Config.ArtyDangerClose;
            RadioLog.Observer($"Fire mission. Grid {m.Grid}. {(danger ? "Danger close. " : "")}{m.Rounds} rds HE. Over.");
            if (Config.Dbg1) MelonLogger.Msg($"[Arty] mission {m.Number} at {mark} grid {m.Grid}, {m.Rounds} rounds, battery bearing {_bearing:F0}, first impact in {m.FirstImpact - now:F1}s");
            return true;
        }

        /// <summary>An 8-figure grid: metres east and north on a 100 km square, to 10 m.</summary>
        private static string Grid(Vector3 p)
        {
            int e = Mathf.FloorToInt((p.x + 50000f) / 10f) % 10000;
            int n = Mathf.FloorToInt((p.z + 50000f) / 10f) % 10000;
            return $"{e:0000} {n:0000}";
        }

        /// <summary>The mark, scattered by the guns: a normal spread on the ground plane, its tails cut off.</summary>
        private static Vector3 Scatter(Vector3 mark)
        {
            float sigma = Mathf.Max(0f, Config.ArtyDispersion) * 0.5f;
            float r = Mathf.Min(sigma * Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-6f, Random.value))), sigma * 3f);
            float a = Random.Range(0f, Mathf.PI * 2f);
            return mark + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        /// <summary>A shell's direction of flight: away from the battery's side, steeply down.</summary>
        private static Vector3 Incoming()
        {
            float b = (_bearing + Random.Range(-3f, 3f)) * Mathf.Deg2Rad;
            float d = Mathf.Clamp(Config.ArtyDescentAngle + Random.Range(-4f, 4f), 20f, 89f) * Mathf.Deg2Rad;
            Vector3 away = -new Vector3(Mathf.Sin(b), 0f, Mathf.Cos(b));
            return (away * Mathf.Cos(d) + Vector3.down * Mathf.Sin(d)).normalized;
        }

        // ── Per frame ───────────────────────────────────────────────────────────

        public static void Tick()
        {
            float now = Time.time;
            for (int mi = _missions.Count - 1; mi >= 0; mi--)
            {
                var m = _missions[mi];
                if (!m.Acked && now >= m.AckAt)
                {
                    m.Acked = true;
                    RadioLog.Battery($"Grid {m.Grid}, {m.Rounds} rds HE. Out.");
                }
                if (!m.Shot && now >= m.ShotAt)
                {
                    m.Shot = true;
                    RadioLog.Battery("Shot. Over.");
                    RadioLog.Observer("Shot. Out.");
                    // The guns, far off: each fires as its round's offset says.
                    foreach (var r in m.Shells)
                        Sfx.PlayHeld("ArtyGuns", 0.55f, r.ImpactAt - m.FirstImpact);
                }
                if (!m.Splashed && now >= m.FirstImpact - Config.ArtySplashWarning)
                {
                    m.Splashed = true;
                    RadioLog.Battery("Splash. Over.");
                    RadioLog.Observer("Splash. Out.");
                }

                bool allDone = true;
                foreach (var r in m.Shells)
                {
                    if (r.Done) continue;
                    try { TickRound(r, now); }
                    catch (System.Exception e)
                    {
                        MelonLogger.Warning($"[Arty] round failed: {e.Message}");
                        DropShell(r);
                        r.Done = true;
                    }
                    if (!r.Done) allDone = false;
                }

                if (allDone && !m.Complete && now >= m.LastImpact + CompleteAfter)
                {
                    m.Complete = true;
                    RadioLog.Battery("Rounds complete. Over.");
                    RadioLog.Observer("End of mission. Out.");
                    _missions.RemoveAt(mi);
                }
            }
        }

        private static void TickRound(Round r, float now)
        {
            float speed = Mathf.Max(50f, Config.ArtyShellSpeed);
            var cam = Camera.main;
            float lead = Mathf.Max(ShellShown, Config.ArtyWhistleLead);

            // Where its path meets the world, found once it is near (bodies move).
            if (!r.Predicted && now >= r.ImpactAt - lead - 0.1f)
            {
                r.Predicted = true;
                r.HasTarget = PathHit(r.Aim - r.Dir * 600f, r.Dir, 900f, out r.Target, out r.Normal);
                if (!r.HasTarget) { r.Target = r.Aim; r.Normal = Vector3.up; }
            }

            // The whistle, late by the distance as the bang will be, so it ends as the bang arrives.
            if (r.Predicted && !r.Whistled)
            {
                float delay = cam != null ? Vector3.Distance(cam.transform.position, r.Target) / 343f : 0f;
                if (now >= r.ImpactAt - Config.ArtyWhistleLead)
                {
                    r.Whistled = true;
                    Sfx.Play("ArtyIncoming", r.Target - r.Dir * 30f, null, 1f, delay);
                }
            }

            // The shell, for the last moment of its flight.
            if (r.Predicted && now >= r.ImpactAt - ShellShown)
            {
                Vector3 pos = r.Target - r.Dir * speed * Mathf.Max(0f, r.ImpactAt - now);
                if (r.Shell == null) { r.Shell = MakeShell(pos, r.Dir); r.LastPos = pos; }
                else r.Shell.transform.position = pos;
                if ((pos - r.LastPos).sqrMagnitude > 1e-4f)
                {
                    try { SmokeCloud.Wake(r.LastPos, pos, 0.9f, 5f); } catch { }
                }
                r.LastPos = pos;
            }

            if (now < r.ImpactAt) return;

            // Down: what is on the last stretch of its path now (something may have moved in).
            bool hit = PathHit(r.Target - r.Dir * 40f, r.Dir, r.HasTarget ? 41f : 0f, out Vector3 at, out Vector3 normal);
            if (!hit && r.HasTarget) { at = r.Target; normal = r.Normal; hit = true; }
            if (r.Shell != null) { try { SmokeCloud.Wake(r.LastPos, at, 0.9f, 5f); } catch { } }
            DropShell(r);
            r.Done = true;
            if (!hit)
            {
                if (Config.Dbg1) MelonLogger.Msg($"[Arty] a round missed the arena near {r.Aim}");
                return;
            }

            var p = ExplosionParams.FromArtilleryConfig(at + normal * Config.ArtyBurstLift);
            p.Forward = normal;
            ExplosionSystem.Detonate(p);
        }

        /// <summary>The first solid thing along a path (never triggers or ignore-raycast parts).</summary>
        private static bool PathHit(Vector3 from, Vector3 dir, float length, out Vector3 point, out Vector3 normal)
        {
            point = default; normal = Vector3.up;
            if (length <= 0f) return false;
            int mask = Config.WorldLayerMask & ~(1 << 2);
            var hits = Physics.RaycastAll(from, dir, length, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.collider == null || h.distance >= best) continue;
                best = h.distance;
                point = h.point;
                normal = h.normal;
            }
            return best < float.MaxValue;
        }

        private static GameObject MakeShell(Vector3 pos, Vector3 dir)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "BA_Shell155";
            go.layer = 2;
            var col = go.GetComponent<Collider>();
            if (col != null) { col.enabled = false; Object.Destroy(col); }
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
            go.transform.localScale = new Vector3(0.155f, 0.155f, 0.8f);

            if (_shellMat == null)
            {
                _shellMat = new Material(Config.FindShader()) { hideFlags = HideFlags.DontUnloadUnusedAsset };
                _shellMat.color = new Color(0.17f, 0.18f, 0.16f);
            }
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = _shellMat;

            var tr = go.AddComponent<TrailRenderer>();
            if (_trailMat == null)
            {
                var sh = Config.FindSpriteShader();
                if (sh != null) _trailMat = new Material(sh) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            }
            if (_trailMat != null) tr.sharedMaterial = _trailMat;
            tr.time = 0.12f;
            tr.startWidth = 0.14f;
            tr.endWidth = 0f;
            tr.startColor = new Color(0.75f, 0.75f, 0.72f, 0.45f);
            tr.endColor = new Color(0.75f, 0.75f, 0.72f, 0f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        private static void DropShell(Round r)
        {
            if (r.Shell != null) Object.Destroy(r.Shell);
            r.Shell = null;
        }

        /// <summary>RESET BOMBS, map resets and scene changes: missions in the air are called off.</summary>
        public static void Clear()
        {
            foreach (var m in _missions) foreach (var r in m.Shells) DropShell(r);
            _missions.Clear();
        }

        public static void OnScene()
        {
            Clear();
            _bearing = -1f;
        }
    }
}
