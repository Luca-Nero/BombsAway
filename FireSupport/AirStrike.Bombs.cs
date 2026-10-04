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
    /// The JDAMs: GBU-38 (500 lb, Mk 82), GBU-32 (1000 lb, Mk 83), GBU-31 (2000 lb, Mk 84). A
    /// strike jet (EAGLE, a stand-in F-15E) runs in high and level on a bearing fixed per scene
    /// (JdamHeading), too high to hear more than a faint roar, and releases one bomb
    /// JdamReleaseRange short of the mark at JdamReleaseAltitude. The bomb steers itself onto the
    /// mark (GPS: half of them within JdamCEP), its path bending from the jet's level flight to a
    /// steep dive (JdamImpactAngle), gaining speed as it falls; JdamFallSound seconds of its
    /// rushing fall ride it down, as the shells' whistles do. It goes off where its path first
    /// meets something (a roof, a body, the ground), or JdamBurstHeight short of that (an air
    /// burst). Its shake arrives with its blast wave, from much farther off than a shell's.
    ///   OBS: EAGLE 31, the grid, DANGER CLOSE if the mark is near you for the size, ONE GBU-xx,
    ///   the heading. OVER. The readback; IP INBOUND / CONTINUE; IN FROM THE x / CLEARED HOT;
    ///   ONE AWAY, TIME OF FALL n; and after the impact GOOD EFFECT ON TARGET.
    ///
    /// The MOAB (GBU-43/B) and the CBU-87 (5.16.0) come the same way. The CBU-87 off the same
    /// F-15E; its dispenser opens CbuOpenHeight over the mark and its bomblets come down over a
    /// pattern round it (AirStrike.Cluster.cs). Since 5.22.0 the MOAB is carried as the real one
    /// is, by an MC-130J (HERC 71) running in slow and level: its ramp opens, a drogue streams
    /// out and pulls the bomb off the ramp on its cradle (AirStrike.Carrier.cs), and the bomb
    /// falls steeper than a JDAM and air-bursts MoabBurstHeight up.
    /// </summary>
    internal static partial class AirStrike
    {
        private const float ReleaseAfterIn = 4f;   // seconds from IN / CLEARED HOT to the release
        private const float F15Station = 1.38f;    // the F15 model's centreline pylon foot, under its middle (its Station marker)

        /// <summary>Per scene, as the batteries': degrees from +Z, -1 until the first drop picks it.</summary>
        private static float _bombBearing = -1f;

        private enum BombShape { Jdam, Moab, Dispenser }

        private sealed class BombKind
        {
            public string Code, Name, Gbu, Kind;   // "2K", "2000LB JDAM", "GBU-31", "Jdam2000" (the spec, BombsAway.<Kind>)
            public float Length, Diameter, Charge; // the stand-in bomb at its real size, m; kg TNT
            public BombShape Shape;
            public float ImpactAngle, ImpactSpeed, Cep, DangerClose, BurstHeight;
            public Airframe Craft = Airframe.F15;  // who carries it, and its call signs on the net
            public string Sign = "EAGLE", Callsign = "Eagle 31";
            public bool Cluster => Shape == BombShape.Dispenser;
        }

        /// <summary>A bomb (the BOMB page): the JDAMs, the CBU-87 and the MOAB.</summary>
        internal static bool IsBomb(FireMissionType t) =>
            t == FireMissionType.Jdam500 || t == FireMissionType.Jdam1000 || t == FireMissionType.Jdam2000
            || t == FireMissionType.Moab || t == FireMissionType.Cbu87;

        private static BombKind BombFor(FireMissionType t)
        {
            BombKind k;
            switch (t)
            {
                case FireMissionType.Jdam500:
                    k = new BombKind { Code = "500", Name = "500LB JDAM", Gbu = "GBU-38", Kind = "Jdam500", Length = 2.36f, Diameter = 0.273f, Charge = Config.Jdam500ChargeKgTNT };
                    break;
                case FireMissionType.Jdam1000:
                    k = new BombKind { Code = "1K", Name = "1000LB JDAM", Gbu = "GBU-32", Kind = "Jdam1000", Length = 3.0f, Diameter = 0.356f, Charge = Config.Jdam1000ChargeKgTNT };
                    break;
                case FireMissionType.Moab:
                    // GBU-43/B: 9.19 m long, 1.03 m across, 9.8 t, pulled off an MC-130's ramp by a
                    // drogue; it falls on GPS and grid fins, steeper than a JDAM.
                    return new BombKind
                    {
                        Code = "MOAB", Name = "GBU-43/B MOAB", Gbu = "GBU-43", Kind = "Moab", Shape = BombShape.Moab,
                        Craft = Airframe.C130, Sign = HercSign, Callsign = HercCallsign,
                        Length = 9.19f, Diameter = 1.03f, Charge = Config.MoabChargeKgTNT,
                        ImpactAngle = Config.MoabImpactAngle, ImpactSpeed = Config.MoabImpactSpeed, Cep = Config.JdamCEP,
                        DangerClose = Config.MoabDangerClose, BurstHeight = Mathf.Max(0f, Config.MoabBurstHeight),
                    };
                case FireMissionType.Cbu87:
                    // The SUU-65 dispenser: 2.33 m long, 0.4 m across, 430 kg. Its "burst" is the
                    // opening; each bomblet is its own BLU-97/B (AirStrike.Cluster.cs).
                    return new BombKind
                    {
                        Code = "CBU", Name = "CBU-87 CEM", Gbu = "CBU-87", Kind = "Blu97", Shape = BombShape.Dispenser,
                        Length = 2.33f, Diameter = 0.396f, Charge = Config.Blu97ChargeKgTNT,
                        ImpactAngle = Config.JdamImpactAngle, ImpactSpeed = Config.JdamImpactSpeed, Cep = Config.CbuCEP,
                        DangerClose = Config.CbuDangerClose,
                    };
                default:
                    k = new BombKind { Code = "2K", Name = "2000LB JDAM", Gbu = "GBU-31", Kind = "Jdam2000", Length = 3.88f, Diameter = 0.457f, Charge = Config.Jdam2000ChargeKgTNT };
                    break;
            }
            k.ImpactAngle = Config.JdamImpactAngle;
            k.ImpactSpeed = Config.JdamImpactSpeed;
            k.Cep = Config.JdamCEP;
            k.BurstHeight = Mathf.Max(0f, Config.JdamBurstHeight);
            k.DangerClose = Config.JdamDangerClose * Mathf.Pow(Mathf.Max(1f, k.Charge) / Mathf.Max(1f, Config.Jdam2000ChargeKgTNT), 1f / 3f);
            return k;
        }

        /// <summary>How far under the F-15E's middle the bomb's middle hangs: on the centreline pylon
        /// (the 5f model is 1.2x real, the stand-in real size).</summary>
        private static float StationFor(BombKind k) => F15Station + 0.5f * k.Diameter * (HasBombModel(k) ? OrdnanceScale : 1f);

        /// <summary>The 5f ordnance models (5.24.0) are 1.2x real, as the aircraft that carry them.</summary>
        private const float OrdnanceScale = 1.2f;

        /// <summary>The bundle's model for a bomb: GBU38 / GBU32 / GBU31, GBU43, SUU65.</summary>
        private static string BombPrefab(BombKind k) =>
            k.Shape == BombShape.Moab ? "GBU43" : k.Shape == BombShape.Dispenser ? "SUU65"
            : k.Kind == "Jdam500" ? "GBU38" : k.Kind == "Jdam1000" ? "GBU32" : "GBU31";

        private static bool HasBombModel(BombKind k) => OrdnanceModels.Asset(BombPrefab(k)) != null;

        /// <summary>One bomb's drop: its fall worked out at the call, from the release on past the mark.</summary>
        private sealed class Bomb
        {
            public BombKind K;
            public Path Path;
            public float ReleaseAt, ImpactAt;      // as planned (a roof in the way comes sooner)
            public GameObject Body;
            public Vector3 LastPos;
            public bool Released, Done;
            public PathSound Fall;
            public Cluster C;                      // a CBU's: where it opens and its bomblets
            public bool Delay;                     // a JDAM under cover: through what it meets on the way, off at the mark (JdamDelayFuze)
            public Vector3 Aim;                    // where it was aimed (the mark, spread by the CEP)
            public int Punched;                    // surfaces it went through
            public BombFins Fins;                  // the model's grid fins or pop-out fins, springing out after the release
        }

        /// <summary>
        /// A bomb model's folding fins (the MOAB's GridFin0..3, the SUU-65's Fin0..3): carried folded
        /// aft about their hinges (in the C-130's bay, under the F-15E), out after the release.
        /// The prefab holds them out; folded is each fin's radial turned to point aft.
        /// </summary>
        private sealed class BombFins
        {
            public Transform[] T;
            public Quaternion[] Rest, Fold;
            public float OutAt = float.MaxValue;   // when they start to spring out
            public float Span = 0.35f;

            public static BombFins Of(Transform root)
            {
                var list = new List<Transform>();
                for (int i = 0; i < root.childCount; i++)
                {
                    var c = root.GetChild(i);
                    if (c.name.StartsWith("GridFin") || c.name.StartsWith("Fin")) list.Add(c);
                }
                if (list.Count == 0) return null;
                var f = new BombFins { T = list.ToArray(), Rest = new Quaternion[list.Count], Fold = new Quaternion[list.Count] };
                for (int i = 0; i < f.T.Length; i++)
                {
                    f.Rest[i] = f.T[i].localRotation;
                    Vector3 radial = new Vector3(f.T[i].localPosition.x, f.T[i].localPosition.y, 0f);
                    f.Fold[i] = (radial.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(radial.normalized, Vector3.back) : Quaternion.identity) * f.Rest[i];
                }
                return f;
            }

            public void Folded() { for (int i = 0; i < T.Length; i++) if (T[i] != null) T[i].localRotation = Fold[i]; }

            /// <summary>Folded until OutAt, then out with one small overshoot (a spring, not an ease).</summary>
            public void Tick(float now)
            {
                float u = Mathf.Clamp01((now - OutAt) / Span);
                float x = u - 1f;
                float s = 1f + 2.70158f * x * x * x + 1.70158f * x * x;   // back-out: past open, then back
                for (int i = 0; i < T.Length; i++) if (T[i] != null) T[i].localRotation = Quaternion.SlerpUnclamped(Fold[i], Rest[i], s);
            }
        }

        // ── Calling ─────────────────────────────────────────────────────────────

        private static bool CallBomb(FireMissionType type, Vector3 mark, Vector3 observer)
        {
            var k = BombFor(type);
            string grid = FireMission.Grid(mark);
            int count = k.Cluster ? Mathf.Clamp(Config.CbuBomblets, 1, 400) : 1;   // the terminal's {N}: bomblets for a CBU
            if (!Config.ArtyStacking && Busy)
            {
                if (!RefuseOnTerminal(k.Code, grid, k.Callsign, count, k.Name, k.Gbu, CraftName(k.Craft)))
                {
                    RadioLog.Observer($"{k.Callsign}, grid {grid}. One {k.Gbu}. Over.");
                    RadioLog.Unit(k.Sign, "Unable, engaged. Out.");
                }
                return false;
            }

            // GPS: a circular normal spread whose median miss is the CEP (sigma = CEP / 1.1774).
            float sigma = Mathf.Max(0f, k.Cep) / 1.1774f;
            Vector3 aim = mark + new Vector3(Gauss(), 0f, Gauss()) * sigma;

            float now = Time.time;
            bool herc = k.Craft == Airframe.C130;
            float v0 = herc ? Mathf.Clamp(Config.MoabCarrierSpeed, 50f, 160f) : Mathf.Max(80f, Config.JdamSpeed);
            float range = Mathf.Max(300f, herc ? Config.MoabReleaseRange : Config.JdamReleaseRange);
            float alt = Mathf.Max(200f, Config.JdamReleaseAltitude);
            float v1 = Mathf.Clamp(k.ImpactSpeed, 100f, SpeedOfSound * 0.93f);   // its sound rides it: under the speed of sound

            // The approach (AirStrike.Approach.cs): the axis the bomb arrives along and the jet's
            // run-in, entry and egress; the flight is worked out releasing at t = 0, then moved to the clock.
            var plan = PlanDrop(k, mark, observer, p =>
            {
                Vector3 rel = aim - p.Heading * range + Vector3.up * alt;
                if (herc)
                {
                    // The C-130 flies on over its drop and turns off gently past it.
                    if (p.Shape != null) { p.Shape.OverflyPast = 300f; p.Shape.OverflyClimb = 3f; p.Shape.OverflyG = 1.25f; }
                    return BuildRun(rel, p.Heading, p.Heading, v0, 0f, HercTurnAfter, p.Away, 1.2f, 6f, p.Shape);
                }
                if (p.Shape != null) { p.Shape.OverflyPast = 900f; p.Shape.OverflyClimb = 5f; p.Shape.OverflyG = 3f; }
                return BuildRun(rel, p.Heading, p.Heading, v0, 0f, 2.5f, p.Away, 3f, ClimbAngle, p.Shape);
            }, out Path path);
            Vector3 heading = plan.Heading;
            Vector3 right = Vector3.Cross(Vector3.up, heading).normalized;
            float deg = plan.Az;
            Vector3 terminal = plan.Terminal.sqrMagnitude > 0.5f ? plan.Terminal : Descending(heading, Mathf.Clamp(plan.Angle, 30f, 89f));
            Vector3 release = aim - heading * range + Vector3.up * alt;
            // Where the bomb starts its fall: under the F-15E's pylon at the jet's speed, or off the
            // C-130's ramp lip, slower by the speed the drogue pulled it out at.
            Vector3 drop = herc ? release + Vector3.up * HercStow.y + heading * HercLipZ : release - Vector3.up * StationFor(k);
            float vb = herc ? v0 - HercExitSpeed : v0;
            int hdg = Mathf.RoundToInt(Mathf.Repeat(deg, 360f)) % 360;
            bool danger = Vector3.Distance(mark, observer) < k.DangerClose;

            // On the terminal the jet is spawned where its run begins and the call is made once
            // its program is up: the whole timeline starts from there.
            var term = OpenTerminal(k.Code, danger, mark, path.Pos[0], heading, grid, k.Callsign, count, k.Name, k.Gbu, CraftName(k.Craft));
            SetPlanWords(term, plan, path);
            if (term != null) now += FireTerminal.Play(term, CallSteps(plan));

            Path fall;
            float tf;
            Cluster cluster = null;
            // A dispenser opens over the mark and its bomblets carry on along its flight as their
            // decelerators slow them, so it is aimed short by that much (PlanCluster).
            if (k.Cluster) cluster = PlanCluster(drop, heading, vb, aim, terminal, v1, out fall, out tf);
            else fall = BombPath(drop, heading, vb, aim, terminal, v1, out tf);
            float impactAt = now + Mathf.Max(tf + 14f, Config.JdamTimeOnTarget);
            float releaseAt = impactAt - tf;
            fall.T0 = releaseAt;
            if (cluster != null) { cluster.Shift(releaseAt); impactAt = cluster.FirstLand; }

            // It breaks away from your side once the bomb is off, or flies on over the mark (the C-130
            // always, gently, its ramp closing): the plan's flight.
            path.T0 += releaseAt;
            var s = new Strike
            {
                Number = FireMission.NextNumber(), Type = type, Sign = k.Sign, CraftKind = k.Craft,
                Mark = mark, Heading = heading, Right = right, Term = term,
                FireAt = releaseAt, BurstEnd = releaseAt, InAt = releaseAt - ReleaseAfterIn,
                Path = path,
                B = new Bomb { K = k, Path = fall, ReleaseAt = releaseAt, ImpactAt = impactAt, C = cluster, Delay = plan.Delay, Aim = aim },
            };
            // No turboprop take yet: the C-130 borrows the A-10's turbofans.
            s.Engine = new PathSound { Key = herc ? "JetA10Loop" : "JetF22Loop", From = s.Path.T0, To = s.Path.End, FadeIn = 3f, FadeOut = 3f };
            // A dispenser's rush ends where it opens; the bomblets fall quietly under their decelerators.
            float soundEnd = cluster != null ? cluster.OpenAt : impactAt;
            s.B.Fall = new PathSound { Key = "BombFallLoop", From = Mathf.Max(releaseAt, soundEnd - Mathf.Max(1f, Config.JdamFallSound)), To = soundEnd, FadeIn = 3f, FadeOut = 0.02f };

            // The net.
            int tof = Mathf.RoundToInt(impactAt - releaseAt);
            term?.Set("TOF", tof).Set("OFF", Compass(Vector3.ProjectOnPlane(s.Path.Fwd[s.Path.Fwd.Count - 1], Vector3.up)));
            if (term == null) RadioLog.Observer($"{k.Callsign}, grid {grid}. {(danger ? "Danger close. " : "")}One {k.Gbu}, heading {hdg:000}. Over.");
            Say(s, now + 3f, false, $"Grid {grid}, one {k.Gbu}, heading {hdg:000}.{PlanWords(plan, true)}", "readback");
            Say(s, now + 3f, true, "Readback correct.");
            Say(s, releaseAt - 10f, false, "IP inbound.", "inbound");
            Say(s, releaseAt - 10f, true, "Continue.");
            Say(s, s.InAt, false, $"In from the {Compass(-heading)}.", "in");
            Say(s, s.InAt, true, "Cleared hot.");
            Say(s, releaseAt + 0.4f, false, $"One away. Time of fall {tof}.", "away");
            float doneAt = cluster != null ? cluster.LastLand : impactAt;
            Say(s, impactAt + 1f, false, null, "impact");
            Say(s, doneAt + 4f, true, "Good effect on target. End of mission. Out.", "end", "kill", "last", "free");
            s.CompleteAt = doneAt + 4f;

            _strikes.Add(s);
            s.PlanDraw = DrawPlan(plan, path, mark, fall);
            LogPlan(s, plan, now);
            if (Config.Dbg1) MelonLogger.Msg($"[Air] mission {s.Number} ({k.Code}) at {mark} grid {grid}, bearing {deg:F0}, release in {releaseAt - now:F1}s at {release}, falls {tf:F1}s, aim {Vector3.Distance(aim, mark):F1} m off");
            return true;
        }

        private static string BombStatus(Strike s, string head, float now)
        {
            var b = s.B;
            if (now < s.InAt) return $"{head} CALL";
            if (now < b.ReleaseAt) return $"{head} IN {Mathf.CeilToInt(b.ReleaseAt - now):00}";
            if (b.C != null && now >= b.C.OpenAt) return now < b.ImpactAt ? $"{head} OPEN {Mathf.CeilToInt(b.ImpactAt - now):00}" : $"{head} IMPACT";
            if (!b.Done) return $"{head} FALL {Mathf.Max(0, Mathf.CeilToInt(b.ImpactAt - now)):00}";
            return $"{head} IMPACT";
        }

        /// <summary>
        /// A guided bomb's fall from <paramref name="r"/>, leaving level at the jet's speed, to
        /// <paramref name="t"/>, arriving along <paramref name="tdir"/> at <paramref name="v1"/>:
        /// a cubic Bézier between the two, flown with its speed growing evenly with the distance
        /// fallen, then carried on straight past the mark (lower ground, or the void). Sampled
        /// every SampleDt from T0 = 0; <paramref name="tf"/> is the time to the mark.
        /// </summary>
        private static Path BombPath(Vector3 r, Vector3 rdir, float v0, Vector3 t, Vector3 tdir, float v1, out float tf)
        {
            float chord = Vector3.Distance(r, t);
            Vector3 p1 = r + rdir * (0.45f * chord), p2 = t - tdir * (0.35f * chord);
            const int n = 800;
            var pts = new Vector3[n + 1];
            var time = new float[n + 1];
            float total = 0f;
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n, w = 1f - u;
                pts[i] = w * w * w * r + 3f * w * w * u * p1 + 3f * w * u * u * p2 + u * u * u * t;
                if (i > 0) total += Vector3.Distance(pts[i - 1], pts[i]);
            }
            float flown = 0f;
            for (int i = 1; i <= n; i++)
            {
                float ds = Vector3.Distance(pts[i - 1], pts[i]);
                float va = Mathf.Lerp(v0, v1, flown / total), vb = Mathf.Lerp(v0, v1, (flown + ds) / total);
                time[i] = time[i - 1] + 2f * ds / (va + vb);
                flown += ds;
            }
            tf = time[n];

            var path = new Path { Speed = v1 };
            int steps = Mathf.CeilToInt(tf / SampleDt);
            int j = 0;
            for (int k = 0; k <= steps; k++)
            {
                float tt = Mathf.Min(k * SampleDt, tf);
                while (j < n - 1 && time[j + 1] < tt) j++;
                float f = Mathf.InverseLerp(time[j], time[j + 1], tt);
                path.Pos.Add(Vector3.Lerp(pts[j], pts[j + 1], f));
            }
            // Past the mark, straight on for a few seconds.
            Vector3 end = path.Pos[path.Pos.Count - 1];
            for (int k = 1; k <= Mathf.CeilToInt(4f / SampleDt); k++) path.Pos.Add(end + tdir * (v1 * k * SampleDt));

            int m = path.Pos.Count;
            for (int i = 0; i < m; i++)
            {
                Vector3 d = path.Pos[Mathf.Min(m - 1, i + 1)] - path.Pos[Mathf.Max(0, i - 1)];
                Vector3 fwd = d.sqrMagnitude > 1e-6f ? d.normalized : tdir;
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, fwd);
                path.Fwd.Add(fwd);
                path.Up.Add(up.sqrMagnitude > 1e-6f ? up.normalized : -rdir);
            }
            return path;
        }

        // ── Per frame ───────────────────────────────────────────────────────────

        private static void TickBomb(Strike s, float now)
        {
            var b = s.B;
            if (b == null || b.Done || now < b.ReleaseAt) return;
            if (!b.Released)
            {
                b.Released = true;
                if (s.H != null) DetachHerc(s, now);   // the cradle and the drogue go their own way
                var hung = s.Craft != null ? s.Craft.transform.Find("Bomb") : null;
                if (hung != null) hung.gameObject.SetActive(false);
                b.Body = BuildBomb(b.K, null);
                b.LastPos = b.Path.PosAt(b.ReleaseAt);
                // A model's fins leave folded and spring out once it is clear: the MOAB's grid fins
                // after the drogue's pull, the dispenser's pop-out fins at once.
                b.Fins = b.Body != null ? BombFins.Of(b.Body.transform) : null;
                if (b.Fins != null)
                {
                    b.Fins.Folded();
                    b.Fins.OutAt = now + (b.K.Shape == BombShape.Moab ? 0.6f : 0.12f);
                    b.Fins.Span = b.K.Shape == BombShape.Moab ? 0.5f : 0.25f;
                }
            }
            if (b.Fins != null && b.Body != null) b.Fins.Tick(now);
            // A dispenser opens at its height (AirStrike.Cluster.cs); its bomblets are ticked from then on.
            if (b.C != null && now >= b.C.OpenAt)
            {
                TickDispenser(s, now);
                return;
            }

            b.Path.Sample(now, out Vector3 pos, out Vector3 fwd, out Vector3 up);
            Vector3 seg = pos - b.LastPos;
            float len = seg.magnitude;
            if (len > 1e-3f)
            {
                // What it meets between where it was and where it is now (bodies and roofs move in).
                Vector3 dir = seg / len;
                float hob = b.Delay ? 0f : b.K.BurstHeight;
                float wake = b.K.Diameter + 0.6f;
                Vector3 from = b.LastPos;
                float left = len;
                // On its delay fuze it goes through what it meets well short of its aim (a roof, a
                // ceiling) and goes off at the first thing near the aim.
                while (b.Delay && b.Punched < MaxPunches && FireMission.PathHit(from, dir, left, out Vector3 roof, out Vector3 rn)
                       && Vector3.Dot(b.Aim - roof, dir) > DelayArm)
                {
                    Punch(s, roof, rn, dir);
                    float gone = Vector3.Distance(from, roof) + 0.3f;
                    from += dir * gone;
                    left -= gone;
                    if (left <= 0f) break;
                }
                if (left > 0f && FireMission.PathHit(from, dir, left + hob, out Vector3 hit, out Vector3 normal))
                {
                    float along = Vector3.Distance(b.LastPos, hit);
                    Vector3 at = hob > 0f ? b.LastPos + dir * Mathf.Max(0f, along - hob) : hit + normal * Config.ArtyBurstLift;
                    try { SmokeCloud.Wake(b.LastPos, at, wake, 6f); } catch { }
                    if (b.C != null)
                    {
                        // A dispenser that meets something before its height (a tower under a low opening) is lost.
                        MelonLogger.Msg($"[Air] the CBU-87 hit something at {hit} before it opened");
                        b.Done = true;
                        b.C.Done = true;
                        if (b.Fall != null) b.Fall.To = Mathf.Min(b.Fall.To, now);
                        DestroyBody(b);
                        return;
                    }
                    BombBurst(s, at, hob > 0f ? Vector3.up : normal, dir, now);
                    return;
                }
                try { SmokeCloud.Wake(b.LastPos, pos, wake, 6f); } catch { }
            }
            if (b.Body != null) b.Body.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, up));
            b.LastPos = pos;

            if (now > b.Path.End)
            {
                if (Config.Dbg1) MelonLogger.Msg($"[Air] the bomb missed the arena near {s.Mark}");
                b.Done = true;
                if (b.Fall != null) b.Fall.To = Mathf.Min(b.Fall.To, now);
                DestroyBody(b);
            }
        }

        private const float DelayArm = 2f;     // m short of its aim (along its fall) a delay-fuzed bomb stops going through things
        private const int MaxPunches = 4;

        /// <summary>A delay-fuzed bomb going through a roof: dust and a crack where it went in, a hole through the smoke.</summary>
        private static void Punch(Strike s, Vector3 at, Vector3 normal, Vector3 dir)
        {
            var b = s.B;
            b.Punched++;
            try { ExplosionFx.Play("Gun30", at + normal * 0.1f, -dir, false, default); } catch { }
            try { SmokeCloud.Wake(at - dir * 2f, at + dir * 2f, b.K.Diameter + 0.6f, 6f); } catch { }
            MelonLogger.Msg($"[Air] {b.K.Gbu} through cover at {at} ({b.Punched}), {Vector3.Dot(b.Aim - at, dir):F1} m short of its aim");
        }

        /// <param name="axis">The bomb's flight: its case's side spray leaves square to it.</param>
        private static void BombBurst(Strike s, Vector3 at, Vector3 normal, Vector3 axis, float now)
        {
            var b = s.B;
            b.Done = true;
            // Its fall is heard until the sound of this moment arrives, with the bang.
            if (b.Fall != null) b.Fall.To = Mathf.Min(b.Fall.To, now);
            DestroyBody(b);
            // What its fragments did, counted while FruitLib detonates it (at once, on this call).
            _fragTally = new int[16];
            _limbsCut = 0;
            float t0 = Time.realtimeSinceStartup;
            FruitLib.FruitBallistics.FragmentTraced += TallyFragment;
            FruitLib.FruitBallistics.LimbWounded += TallyWound;
            try
            {
                var x = b.K.Shape == BombShape.Moab ? ExplosionParams.FromMoabConfig(at) : ExplosionParams.FromJdamConfig(b.K.Kind, at);
                x.Forward = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
                x.Axis = axis;
                ExplosionSystem.Detonate(x);
            }
            catch (System.Exception e) { MelonLogger.Warning($"[Air] bomb burst failed: {e.Message}"); }
            finally
            {
                FruitLib.FruitBallistics.FragmentTraced -= TallyFragment;
                FruitLib.FruitBallistics.LimbWounded -= TallyWound;
            }
            var f = _fragTally;
            var st = FruitLib.FruitBallistics.LastExplosion;
            MelonLogger.Msg($"[Air] {b.K.Gbu} burst {Vector3.Distance(at, s.Mark):F1} m from the mark in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms: " +
                            $"fragment wounds {_limbsCut}, over budget {f[(int)FruitLib.FragmentEnd.OverBudget]}, " +
                            $"lodged {f[(int)FruitLib.FragmentEnd.Lodged]}, through limbs {f[(int)FruitLib.FragmentEnd.PassedThrough]}, " +
                            $"stopped {f[(int)FruitLib.FragmentEnd.Stopped]}, ricochets {f[(int)FruitLib.FragmentEnd.Ricocheted]}, " +
                            $"through walls {f[(int)FruitLib.FragmentEnd.Penetrated]}, spent {f[(int)FruitLib.FragmentEnd.Spent]}");
            // Where the time went (FruitLib 5.8.0): the blast and its organ injuries, the fragments
            // and their wound walks, and how the targeted fragments were shared out.
            MelonLogger.Msg($"[Air] {b.K.Gbu} time: FruitLib {st.TotalMs:F0} ms = blast {st.BlastMs:F0} (organs {st.OrganMs:F0}: {st.Organs} hurt, {st.OrgansSkipped} limbs skipped) " +
                            $"+ fragments {st.FragmentMs:F0} (walks {st.WalkMs:F0}: {st.Walks}); targeted: {st.TargetLimbs} limbs in reach, " +
                            $"{st.Expected:F0} hits expected, {st.Aimed} aimed, {st.Dropped} dropped (another limb first), {st.Folded} folded past the cap; {st.Rays} scenery rays");
        }

        private static int[] _fragTally = new int[16];
        private static int _limbsCut;
        private static void TallyFragment(FruitLib.FragmentTrace t) { int i = (int)t.EndedBy; if (i >= 0 && i < _fragTally.Length) _fragTally[i]++; }
        private static void TallyWound(FruitLib.WoundInfo w) => _limbsCut++;

        private static void DestroyBody(Bomb b)
        {
            if (b.Body != null) Object.Destroy(b.Body);
            b.Body = null;
        }

        private static void DropBomb(Bomb b)
        {
            DestroyBody(b);
            DropSound(b.Fall);
            if (b.C != null) DropCluster(b.C);
        }

        // ── The shake, with the blast wave ──────────────────────────────────────

        private sealed class PendingShake { public float At, Reach; public Vector3 Origin; }
        private static readonly List<PendingShake> _shakes = new List<PendingShake>();

        /// <summary>
        /// A bomb went off (ExplosionSystem): the camera shakes when its blast wave arrives, late
        /// by the distance over the speed of sound, out to a reach that grows with the cube root
        /// of the charge (an HE warhead's 3 kg: 20 m; a Mk 84: about 210 m).
        /// </summary>
        public static void Shake(Vector3 origin, string kind)
        {
            if (!Config.CamFXEnabled) return;
            float charge = kind == "Jdam500" ? Config.Jdam500ChargeKgTNT : kind == "Jdam1000" ? Config.Jdam1000ChargeKgTNT
                         : kind == "Moab" ? Config.MoabChargeKgTNT : Config.Jdam2000ChargeKgTNT;
            var cam = Camera.main;
            float delay = cam != null ? Vector3.Distance(cam.transform.position, origin) / SpeedOfSound : 0f;
            _shakes.Add(new PendingShake { At = Time.time + delay, Origin = origin, Reach = 2f * Mathf.Pow(Mathf.Max(1f, charge) / 3f, 1f / 3f) });
        }

        private static void TickShakes(float now)
        {
            for (int i = _shakes.Count - 1; i >= 0; i--)
            {
                if (now < _shakes[i].At) continue;
                CameraFX.AddTrauma(_shakes[i].Origin, 1.5f, _shakes[i].Reach);
                _shakes.RemoveAt(i);
            }
        }

        // ── The stand-ins ───────────────────────────────────────────────────────

        private static Material _yelMat;

        /// <summary>
        /// The bomb: the bundle's model (5.24.0, 1.2x real: GBU38 / GBU32 / GBU31, GBU43, SUU65),
        /// named "Bomb", its folding fins folded when it is hung (<paramref name="parent"/>, under
        /// the jet or in the C-130's bay). Without the model, a stand-in out of boxes at its real
        /// size: the bomb body, a stepped nose and fuze, the yellow band of a live HE filling, the
        /// strakes, the tail kit and its four fins in an X.
        /// </summary>
        private static GameObject BuildBomb(BombKind k, Transform parent)
        {
            var model = SpawnBare(BombPrefab(k));
            if (model != null)
            {
                model.name = "Bomb";
                if (parent != null)
                {
                    model.transform.SetParent(parent, false);
                    BombFins.Of(model.transform)?.Folded();
                }
                return model;
            }
            EnsureMats();
            if (_yelMat == null) _yelMat = new Material(Config.FindShader()) { hideFlags = HideFlags.DontUnloadUnusedAsset, color = new Color(0.82f, 0.59f, 0f) };   // yel
            var root = new GameObject("Bomb");
            root.layer = 2;
            if (parent != null) root.transform.SetParent(parent, false);
            var t = root.transform;
            float L = k.Length, d = k.Diameter;
            if (k.Shape == BombShape.Moab)
            {
                // GBU-43/B: a long, plain body with a blunt ogive nose and four lattice grid fins at the tail.
                Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(d, d, 0.78f * L), _bodyMat);                     // body
                Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.43f * L), new Vector3(0.8f * d, 0.8f * d, 0.08f * L), _bodyMat); // nose
                Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.48f * L), new Vector3(0.45f * d, 0.45f * d, 0.03f * L), _darkMat); // nose cap
                Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.2f * L), new Vector3(1.02f * d, 1.02f * d, 0.02f * L), _yelMat); // the HE band
                Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, -0.43f * L), new Vector3(0.7f * d, 0.7f * d, 0.1f * L), _darkMat); // tail cone
                for (int i = 0; i < 4; i++)
                {
                    var arm = new GameObject("GridFin").transform;
                    arm.SetParent(t, false);
                    arm.localPosition = new Vector3(0f, 0f, -0.43f * L);
                    arm.localRotation = Quaternion.Euler(0f, 0f, 45f + 90f * i);
                    Part(arm, PrimitiveType.Cube, new Vector3(0f, 0.9f * d, 0f), new Vector3(0.95f * d, 0.8f * d, 0.06f), _darkMat); // the lattice, edge on to the flow
                }
                return root;
            }
            if (k.Shape == BombShape.Dispenser)
            {
                // The SUU-65 dispenser: a plain tube with a rounded nose, the yellow bands and four
                // pop-out tail fins (the palette's charcoal, as the other stand-ins).
                Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(d, d, 0.8f * L), _bodyMat);                       // tube
                Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.44f * L), new Vector3(0.7f * d, 0.7f * d, 0.1f * L), _bodyMat);   // nose
                Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.5f * L), new Vector3(0.3f * d, 0.3f * d, 0.03f * L), _darkMat);   // fuze
                Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.3f * L), new Vector3(1.03f * d, 1.03f * d, 0.03f * L), _yelMat);  // the bands
                Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, -0.2f * L), new Vector3(1.03f * d, 1.03f * d, 0.03f * L), _yelMat);
                for (int i = 0; i < 2; i++)
                {
                    var fin = Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, -0.42f * L), new Vector3(0.04f, 2.2f * d, 0.14f * L), _darkMat);
                    fin.localRotation = Quaternion.Euler(0f, 0f, 45f + 90f * i);
                }
                return root;
            }
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.05f * L), new Vector3(d, d, 0.62f * L), _bodyMat);              // body
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.43f * L), new Vector3(0.72f * d, 0.72f * d, 0.14f * L), _bodyMat); // nose
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.49f * L), new Vector3(0.36f * d, 0.36f * d, 0.03f * L), _darkMat); // fuze
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.3f * L), new Vector3(1.03f * d, 1.03f * d, 0.05f * L), _yelMat);   // the HE band
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.08f * L), new Vector3(1.6f * d, 0.04f, 0.28f * L), _darkMat);     // strakes
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, -0.36f * L), new Vector3(0.82f * d, 0.82f * d, 0.22f * L), _darkMat); // tail kit
            for (int i = 0; i < 2; i++)
            {
                var fin = Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, -0.42f * L), new Vector3(0.04f, 2f * d, 0.15f * L), _darkMat);
                fin.localRotation = Quaternion.Euler(0f, 0f, 45f + 90f * i);                                                     // the fins, an X
            }
            return root;
        }

        /// <summary>
        /// The F-15E: the bundle's <c>F15</c> (5f, 1.2x real) with the bomb on its centreline
        /// pylon, or, without it, one out of boxes at its real size (19.4 m long, 13 m span): the
        /// broad fuselage with its intakes beside the cockpit, the swept wing, twin upright fins,
        /// the nozzles side by side, and the bomb on the centreline station.
        /// </summary>
        private static void BuildF15(Strike s)
        {
            var model = SpawnBare("F15");
            if (model != null)
            {
                var hung = BuildBomb(s.B.K, model.transform).transform;
                hung.localPosition = new Vector3(0f, -StationFor(s.B.K), 0f);
                if (s.B.Released) hung.gameObject.SetActive(false);
                s.Craft = model;
                return;
            }
            EnsureMats();
            var root = new GameObject("BA_F15");
            root.layer = 2;
            var t = root.transform;
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, -0.6f), new Vector3(3f, 1.5f, 14.5f), _bodyMat);      // fuselage
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0.1f, 7.7f), new Vector3(1.4f, 1.1f, 3.4f), _bodyMat);    // nose
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0.2f, 9.6f), new Vector3(0.7f, 0.6f, 0.8f), _darkMat);    // radome tip
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0.95f, 5.4f), new Vector3(0.85f, 0.6f, 3.2f), _darkMat);  // canopy (two seats)
            for (int side = -1; side <= 1; side += 2)
            {
                Part(t, PrimitiveType.Cube, new Vector3(side * 1.7f, 0.1f, 4f), new Vector3(1.1f, 1.4f, 2.6f), _darkMat);     // intakes
                var wing = Part(t, PrimitiveType.Cube, new Vector3(side * 3.9f, 0.1f, -1.2f), new Vector3(5.6f, 0.22f, 4.4f), _bodyMat);
                wing.localRotation = Quaternion.Euler(0f, side * -30f, 0f);                                          // swept back
                var tail = Part(t, PrimitiveType.Cube, new Vector3(side * 2.6f, 0f, -7.2f), new Vector3(2.8f, 0.15f, 2.4f), _bodyMat);
                tail.localRotation = Quaternion.Euler(0f, side * -30f, 0f);                                          // tailplanes
                Part(t, PrimitiveType.Cube, new Vector3(side * 1.4f, 2f, -6.3f), new Vector3(0.18f, 3.2f, 2.8f), _bodyMat);   // fins, upright
                Part(t, PrimitiveType.Cube, new Vector3(side * 0.7f, 0f, -8.3f), new Vector3(1.05f, 1.05f, 1.2f), _darkMat);  // nozzles
            }
            var bomb = BuildBomb(s.B.K, t).transform;
            bomb.localPosition = new Vector3(0f, -StationFor(s.B.K), 0f);
            if (s.B.Released) bomb.gameObject.SetActive(false);
            s.Craft = root;
        }
    }
}
