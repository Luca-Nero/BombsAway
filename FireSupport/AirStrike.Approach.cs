using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Random = UnityEngine.Random;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// Each strike's approach, planned at the call (5.25.0, AirDynamicApproach).
    ///
    /// The reach check: from the mark (lifted a little toward the lase's own line, which is open
    /// air by definition), a bundle of lines is cast back along each attack axis the strike
    /// could fly: 24 headings round the mark, each at a few dive angles for the guns and rockets
    /// or impact angles for the bombs. What share of a bundle gets out is how well that axis
    /// reaches the mark. In the open nearly every axis does, and the pick comes down to keeping
    /// the hits from walking at you, a slight liking for across your line of sight, not the same
    /// axis as last time, and AirApproachVariance of chance. A mark in a street or a courtyard
    /// leaves only the steep axes, one under an overhang only the shallow axes from its open
    /// side, one through a window the axes through it. A mark under a roof (nothing gets out)
    /// is masked: the axis through the fewest surfaces is taken, the net says so, and a JDAM
    /// sets its delay fuze to go through the roof and off at the mark (JdamDelayFuze).
    ///
    /// The flight: the aircraft comes in from a varied bearing and turns onto its run-in
    /// (RunShape.EntryTurn, up to AirEntryTurnMax), and after the attack breaks away before the
    /// mark or flies on over it and turns off past it (AirOverflyChance; the helicopter always
    /// breaks, the C-130 always overflies its drop). The whole flight is then checked against
    /// the world, wingtips included; one that would fly into something tries the other egress,
    /// the other break side, a straight-in entry, then the next axis.
    /// </summary>
    internal static partial class AirStrike
    {
        private const int ReachRays = 7;           // the middle line and a ring of six round it
        private const float MaskedBelow = 0.3f;    // a best axis reaching less than this: the mark is under cover
        private const int AxisCount = 24;
        private const float ClearStep = 0.2f;      // seconds between the flight's checks

        /// <summary>Per scene: the last strike's attack heading (degrees), so the next one comes another way.</summary>
        private static float _lastAirAz = -1f;

        /// <summary>A reach line, kept for DebugDrawAirPlan.</summary>
        private struct PlanRay { public Vector3 A, B; public bool Ok; }

        /// <summary>A strike's approach.</summary>
        private sealed class Plan
        {
            public Vector3 Heading;                // flat: the run-in heading, where the aircraft flies
            public float Angle;                    // dive (guns, rockets) or impact angle (bombs), degrees
            public Vector3 Terminal;               // bombs: the way it arrives (its azimuth may differ from Heading)
            public RunShape Shape = new RunShape();
            public float BreakSign = 1f;           // +1 breaks clockwise (seen from above), -1 counter-clockwise
            public float Reach = 1f;               // share of the chosen axis's lines that got out
            public int Cover;                      // surfaces on its middle line
            public bool Masked, Delay;
            public int Axes;                       // axes tried
            public Vector3 Aim;                    // where the reach lines start
            public readonly List<PlanRay> Rays = new List<PlanRay>();
            public Vector3 Away => Quaternion.AngleAxis(BreakSign * TurnAway, Vector3.up) * Heading;
            public float Az => Mathf.Repeat(Mathf.Atan2(Heading.x, Heading.z) * Mathf.Rad2Deg, 360f);
        }

        private sealed class Cand { public float Az, Angle, Reach, Score; public int Cover; }

        private static Vector3 Flat(float azDeg) => new Vector3(Mathf.Sin(azDeg * Mathf.Deg2Rad), 0f, Mathf.Cos(azDeg * Mathf.Deg2Rad));

        /// <summary>The way a line at <paramref name="angleDeg"/> below level along <paramref name="flat"/> travels.</summary>
        private static Vector3 Descending(Vector3 flat, float angleDeg) =>
            (flat * Mathf.Cos(angleDeg * Mathf.Deg2Rad) + Vector3.down * Mathf.Sin(angleDeg * Mathf.Deg2Rad)).normalized;

        /// <summary>The mark lifted 0.3 m toward the observer's eye: the lase's own line is open air.</summary>
        private static Vector3 ReachOrigin(Vector3 mark, Vector3 observer)
        {
            Vector3 open = observer - mark;
            return mark + (open.sqrMagnitude > 1e-4f ? open.normalized : Vector3.up) * 0.3f;
        }

        /// <summary>
        /// How well an attack arriving along <paramref name="dir"/> reaches <paramref name="aim"/>:
        /// the share of ReachRays lines, from the aim and a ring <paramref name="spread"/> round
        /// it, that get <paramref name="length"/> metres back the way it comes without meeting
        /// anything. <paramref name="cover"/>: the surfaces the middle line crosses.
        /// <paramref name="rays"/> collects the middle line (and with <paramref name="ring"/> all of them) to draw.
        /// </summary>
        private static float Reach(Vector3 aim, Vector3 dir, float length, float spread, out int cover, List<PlanRay> rays = null, bool ring = false)
        {
            int mask = Config.WorldLayerMask & ~(1 << 2);
            Vector3 back = -dir;
            Vector3 a = Vector3.Cross(back, Mathf.Abs(back.y) < 0.95f ? Vector3.up : Vector3.right).normalized, b = Vector3.Cross(back, a);
            cover = 0;
            int clear = 0;
            for (int i = 0; i < ReachRays; i++)
            {
                float ang = i * Mathf.PI * 2f / (ReachRays - 1);
                Vector3 from = i == 0 ? aim : aim + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * spread;
                // Only what stays put counts: bodies and loose props are what the strike is for.
                var hits = Physics.RaycastAll(from, back, length, mask, QueryTriggerInteraction.Ignore);
                int solid = 0;
                var seen = i == 0 ? new HashSet<int>() : null;
                foreach (var h in hits)
                {
                    var col = h.collider;
                    if (col == null) continue;
                    var rb = col.attachedRigidbody;
                    if (rb != null && !rb.isKinematic) continue;
                    solid++;
                    seen?.Add(col.GetInstanceID());
                }
                if (i == 0) cover = seen.Count;
                bool ok = solid == 0;
                if (ok) clear++;
                if (rays != null && (ring || i == 0)) rays.Add(new PlanRay { A = from, B = from + back * Mathf.Min(length, ring ? 400f : 60f), Ok = ok });
            }
            return clear / (float)ReachRays;
        }

        /// <summary>
        /// Every axis round the mark at every angle in <paramref name="angles"/>, scored and
        /// best first. <paramref name="walks"/>: the hits walk along the heading (guns, rockets),
        /// so a heading toward the observer is held against it. <paramref name="fixedAz"/> ≥ 0
        /// tries only that heading (AirAttackHeading, JdamHeading).
        /// </summary>
        private static List<Cand> RankAxes(Plan plan, Vector3 mark, Vector3 observer, float[] angles, float baseAngle, float fixedAz,
                                           float length, float spread, bool walks, bool danger)
        {
            var list = new List<Cand>();
            Vector3 toObs = Vector3.ProjectOnPlane(observer - mark, Vector3.up);
            float dObs = toObs.magnitude;
            toObs = dObs > 1f ? toObs / dObs : Vector3.zero;
            float phase = Random.Range(0f, 360f / AxisCount);
            int axes = fixedAz >= 0f ? 1 : AxisCount;
            float variance = Mathf.Max(0f, Config.AirApproachVariance);
            for (int i = 0; i < axes; i++)
            {
                float az = fixedAz >= 0f ? fixedAz : phase + i * 360f / AxisCount;
                Vector3 flat = Flat(az);
                foreach (float ang in angles)
                {
                    float reach = Reach(plan.Aim, Descending(flat, ang), length, spread, out int cover, Config.DebugDrawAirPlan ? plan.Rays : null);
                    var c = new Cand { Az = az, Angle = ang, Reach = reach, Cover = cover };
                    float score = reach - 0.01f * Mathf.Abs(ang - baseAngle);
                    if (walks)
                    {
                        // Hits and overshoots walk along the heading: not at you, least of all close in.
                        float toward = Mathf.Max(0f, Vector3.Dot(flat, toObs));
                        score -= toward * (danger ? 1.5f : Mathf.Lerp(0.6f, 0.1f, Mathf.Clamp01(dObs / 600f)));
                        score += 0.12f * (1f - Mathf.Abs(Vector3.Dot(flat, toObs)));   // across your line of sight reads best
                    }
                    if (_lastAirAz >= 0f && Mathf.Abs(Mathf.DeltaAngle(az, _lastAirAz)) < 35f) score -= 0.15f;
                    c.Score = score + Random.value * variance;
                    list.Add(c);
                }
                plan.Axes++;
            }
            float best = 0f;
            foreach (var c in list) best = Mathf.Max(best, c.Reach);
            plan.Masked = best < MaskedBelow;
            if (plan.Masked)
                foreach (var c in list) c.Score -= 0.12f * c.Cover;   // under cover: through the least of it
            list.Sort((x, y) => y.Score.CompareTo(x.Score));
            return list;
        }

        /// <summary>Takes <paramref name="c"/> as the plan's axis.</summary>
        private static void Adopt(Plan plan, Cand c, float jitterDeg)
        {
            float az = c.Az + (jitterDeg > 0f ? Random.Range(-jitterDeg, jitterDeg) : 0f);
            plan.Heading = Flat(az);
            plan.Angle = c.Angle;
            plan.Reach = c.Reach;
            plan.Cover = c.Cover;
        }

        /// <summary>The break side away from the observer, or either when they're on the run's line.</summary>
        private static float BreakAway(Vector3 heading, Vector3 mark, Vector3 observer)
        {
            Vector3 right = Vector3.Cross(Vector3.up, heading).normalized;
            float side = Vector3.Dot(observer - mark, right);
            if (Mathf.Abs(side) < 30f) return Random.value < 0.5f ? -1f : 1f;
            return side > 0f ? -1f : 1f;
        }

        /// <summary>A varied entry: straight in a third of the time, else a turn of up to AirEntryTurnMax onto the run-in.</summary>
        private static float EntryTurn()
        {
            float max = Mathf.Clamp(Config.AirEntryTurnMax, 0f, 170f);
            if (max < 1f || Random.value < 0.33f) return 0f;
            return (Random.value < 0.5f ? -1f : 1f) * Random.Range(Mathf.Min(25f, max), max);
        }

        // ── The plans ───────────────────────────────────────────────────────────

        /// <summary>A gun run's approach. <paramref name="build"/> makes its flight for a plan (fired at t = 0).</summary>
        private static Plan PlanGun(Gun g, Vector3 mark, Vector3 observer, bool danger, Func<Plan, Path> build, out Path path)
        {
            var plan = new Plan { Aim = ReachOrigin(mark, observer) };
            if (!Config.AirDynamicApproach)
            {
                plan.Heading = AttackHeading(mark, observer);
                plan.Angle = g.Dive;
                plan.BreakSign = BreakAway(plan.Heading, mark, observer);
                plan.Shape = null;
                path = build(plan);
                return plan;
            }
            float[] dives = Angles(g.Dive, 6f, 50f, 0f, 10f, 20f, -8f);
            var ranked = RankAxes(plan, mark, observer, dives, g.Dive, Config.AirAttackHeading, Mathf.Min(g.Range, 900f), 1.5f, true, danger);
            path = Settle(plan, ranked, mark, observer, build, jet: true, overfly: Random.value < Config.AirOverflyChance, entryG: 4f, span: 10f);
            return plan;
        }

        /// <summary>The rockets' approach: the helicopter always breaks away.</summary>
        private static Plan PlanRockets(float baseDive, Vector3 mark, Vector3 observer, bool danger, Func<Plan, Path> build, out Path path)
        {
            var plan = new Plan { Aim = ReachOrigin(mark, observer) };
            if (!Config.AirDynamicApproach)
            {
                plan.Heading = AttackHeading(mark, observer);
                plan.Angle = baseDive;
                plan.BreakSign = BreakAway(plan.Heading, mark, observer);
                plan.Shape = null;
                path = build(plan);
                return plan;
            }
            float[] dives = Angles(baseDive, 2f, 25f, 0f, 6f, 12f, -4f);
            // The rockets arrive about a degree steeper than the dive they're fired from.
            var ranked = RankAxes(plan, mark, observer, Offset(dives, 1f), baseDive + 1f, Config.AirAttackHeading, 900f, 2f, true, danger);
            foreach (var c in ranked) c.Angle -= 1f;
            path = Settle(plan, ranked, mark, observer, build, jet: false, overfly: false, entryG: 1.5f, span: 9f);
            return plan;
        }

        /// <summary>
        /// A bomb's approach: the axis it arrives along (its impact angle, and an azimuth the jet's
        /// run-in may differ from by up to 35°, as a JDAM steers itself), and the jet's flight.
        /// </summary>
        private static Plan PlanDrop(BombKind k, Vector3 mark, Vector3 observer, Func<Plan, Path> build, out Path path)
        {
            var plan = new Plan { Aim = ReachOrigin(mark, observer) };
            bool herc = k.Craft == Airframe.C130;
            if (!Config.AirDynamicApproach)
            {
                if (_bombBearing < 0f) _bombBearing = Config.JdamHeading >= 0f ? Mathf.Repeat(Config.JdamHeading, 360f) : Random.Range(0f, 360f);
                plan.Heading = Flat(_bombBearing + Random.Range(-4f, 4f));
                plan.Angle = k.ImpactAngle;
                plan.Terminal = Descending(plan.Heading, plan.Angle);
                plan.BreakSign = BreakAway(plan.Heading, mark, observer);
                plan.Shape = null;
                path = build(plan);
                return plan;
            }

            float fixedAz = Config.JdamHeading >= 0f ? Mathf.Repeat(Config.JdamHeading, 360f) : -1f;
            float[] angles = k.Cluster ? new[] { k.ImpactAngle }
                           : k.Shape == BombShape.Moab ? Angles(k.ImpactAngle, 45f, 88f, 0f, 10f, -10f, -20f)
                           : Angles(k.ImpactAngle, 30f, 88f, 0f, 10f, 20f, -15f, -25f);
            var ranked = RankAxes(plan, mark, observer, angles, k.ImpactAngle, fixedAz, 700f, 1f, false, false);
            // A JDAM under cover: steep, through the thinnest of it, and off at the mark on its delay.
            if (plan.Masked && Config.JdamDelayFuze && !k.Cluster && k.Shape != BombShape.Moab)
            {
                plan.Delay = true;
                foreach (var c in ranked) if (c.Angle >= 75f) c.Score += 0.5f;
                ranked.Sort((x, y) => y.Score.CompareTo(x.Score));
            }
            float spread = k.Cluster || herc ? 0f : k.Shape == BombShape.Moab ? 20f : 35f;   // the jet's run-in against the bomb's arrival
            path = Settle(plan, ranked, mark, observer, build, jet: true, overfly: herc || Random.value < Config.AirOverflyChance,
                          entryG: herc ? 1.3f : 3f, span: herc ? 25f : 8f, runSpread: fixedAz >= 0f ? 0f : spread);
            return plan;
        }

        /// <summary>
        /// The first of <paramref name="ranked"/> whose flight is clear, trying for each the
        /// chosen egress and entry, then the other egress, the other break side and a straight-in
        /// entry. Nothing clear: the best axis as it is (logged).
        /// </summary>
        private static Path Settle(Plan plan, List<Cand> ranked, Vector3 mark, Vector3 observer, Func<Plan, Path> build,
                                   bool jet, bool overfly, float entryG, float span, float runSpread = -1f)
        {
            float entry = EntryTurn();
            Path first = null;
            Plan firstPlan = null;
            string firstBlock = null;
            int tries = Mathf.Min(ranked.Count, 8);
            for (int i = 0; i < tries; i++)
            {
                var c = ranked[i];
                Adopt(plan, c, runSpread >= 0f ? 0f : 4f);
                Vector3 arrive = Descending(plan.Heading, plan.Angle);
                if (runSpread >= 0f)
                {
                    // A bomb: it arrives along the axis; the jet runs in up to runSpread off it.
                    plan.Terminal = arrive;
                    float off = runSpread > 0f && Random.value < 0.6f ? Random.Range(-runSpread, runSpread) : 0f;
                    plan.Heading = Quaternion.AngleAxis(off, Vector3.up) * plan.Heading;
                }
                float side = BreakAway(plan.Heading, mark, observer);
                // Egress, break side, entry: as chosen first, then the alternatives.
                var tryShapes = new List<(bool over, float side, float entry)>
                {
                    (overfly, side, entry), (!overfly && jet, side, entry), (overfly, -side, entry), (false, side, 0f), (false, -side, 0f),
                };
                foreach (var (over, sd, en) in tryShapes)
                {
                    if (over && !jet) continue;
                    plan.BreakSign = sd;
                    plan.Shape = new RunShape { EntryTurn = en, EntryG = entryG, Overfly = over, Mark = mark };
                    var path = build(plan);
                    if (first == null) { first = path; firstPlan = Copy(plan); }
                    if (FlightClear(path, span, out Vector3 blocked, out string what))
                    {
                        _lastAirAz = plan.Az;
                        return path;
                    }
                    if (firstBlock == null) firstBlock = $"{what} at {blocked}";
                    if (Config.Dbg1) MelonLogger.Msg($"[Air] plan: axis {plan.Az:F0} at {plan.Angle:F0} deg, {(over ? "overfly" : "break")}, entry {en:F0}: flight blocked by {what} at {blocked}");
                }
            }
            MelonLogger.Msg($"[Air] no clear flight among the best axes (the first was blocked by {firstBlock}); flying it as planned");
            CopyInto(firstPlan, plan);
            _lastAirAz = plan.Az;
            return first;
        }

        private static Plan Copy(Plan p) => new Plan
        {
            Heading = p.Heading, Angle = p.Angle, Terminal = p.Terminal, Shape = p.Shape, BreakSign = p.BreakSign,
            Reach = p.Reach, Cover = p.Cover, Masked = p.Masked, Delay = p.Delay, Axes = p.Axes, Aim = p.Aim,
        };

        private static void CopyInto(Plan from, Plan to)
        {
            to.Heading = from.Heading; to.Angle = from.Angle; to.Terminal = from.Terminal; to.Shape = from.Shape;
            to.BreakSign = from.BreakSign; to.Reach = from.Reach; to.Cover = from.Cover;
        }

        /// <summary>The base angle and the base plus each offset, clamped to [lo, hi], each once.</summary>
        private static float[] Angles(float b, float lo, float hi, params float[] offsets)
        {
            var list = new List<float>();
            foreach (float o in offsets)
            {
                float a = Mathf.Clamp(b + o, lo, hi);
                if (!list.Exists(x => Mathf.Abs(x - a) < 2f)) list.Add(a);
            }
            return list.ToArray();
        }

        private static float[] Offset(float[] a, float by)
        {
            var r = new float[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[i] + by;
            return r;
        }

        /// <summary>
        /// Whether the whole flight stays clear of the world: checked every ClearStep along it, on
        /// the aircraft's middle and out at its wingtips (<paramref name="span"/> metres either side).
        /// </summary>
        private static bool FlightClear(Path path, float span, out Vector3 blocked, out string what)
        {
            blocked = default;
            what = null;
            if (path == null || path.Pos.Count < 2) return true;
            int mask = Config.WorldLayerMask & ~(1 << 2);
            int step = Mathf.Max(1, Mathf.RoundToInt(ClearStep / SampleDt));
            for (int i = 0; i + step < path.Pos.Count; i += step)
            {
                Vector3 a = path.Pos[i], b = path.Pos[i + step];
                Vector3 side = Vector3.Cross(path.Up[i], path.Fwd[i]).normalized * span;
                for (int k = -1; k <= 1; k++)
                {
                    if (Physics.Linecast(a + side * k, b + side * k, out RaycastHit hit, mask, QueryTriggerInteraction.Ignore))
                    {
                        blocked = hit.point;
                        what = hit.collider != null ? hit.collider.name : "?";
                        return false;
                    }
                }
            }
            return true;
        }

        // ── On the net ──────────────────────────────────────────────────────────

        /// <summary>The plan's words for the terminal: {DIVE} {LOS} {AXES} {EGRESS} {ENTRY} {FUZE} {COVER}.</summary>
        private static void SetPlanWords(TermSession term, Plan plan, Path path)
        {
            if (term == null || plan == null) return;
            string off = Compass(Vector3.ProjectOnPlane(path.Fwd[path.Fwd.Count - 1], Vector3.up)).ToUpperInvariant();
            bool over = plan.Shape != null && plan.Shape.Overfly;
            term.Set("DIVE", Mathf.RoundToInt(plan.Angle).ToString("00"))
                .Set("LOS", Mathf.RoundToInt(plan.Reach * 100f))
                .Set("AXES", plan.Axes)
                .Set("EGRESS", over ? $"OVERFLY, OFF {off}" : $"BREAK {off}")
                .Set("ENTRY", Compass(-Vector3.ProjectOnPlane(path.Fwd[0], Vector3.up)).ToUpperInvariant())
                .Set("FUZE", plan.Delay ? "DELAY" : "INSTANT")
                .Set("COVER", plan.Cover);
        }

        /// <summary>The steps typed after the call on the terminal: the plan, and the cover if it's masked.</summary>
        private static string[] CallSteps(Plan plan) =>
            plan != null && plan.Masked && Config.AirDynamicApproach
                ? new[] { "spawn", "boot", "wake", "call", "plan", "masked" }
                : Config.AirDynamicApproach ? new[] { "spawn", "boot", "wake", "call", "plan" } : new[] { "spawn", "boot", "wake", "call" };

        /// <summary>What the radio readback adds for the plan: the overfly, and the cover.</summary>
        private static string PlanWords(Plan plan, bool bomb)
        {
            if (plan == null || !Config.AirDynamicApproach) return "";
            string s = plan.Shape != null && plan.Shape.Overfly ? " Overflying." : "";
            if (plan.Masked) s += plan.Delay ? " Target under cover, delay fuze." : bomb ? " Target under cover." : " Target masked, attacking through cover.";
            return s;
        }

        /// <summary>One line per strike (DebugLevel 1): the approach it flies.</summary>
        private static void LogPlan(Strike s, Plan plan, float now)
        {
            if (!Config.Dbg1) return;
            var path = s.Path;
            string how = plan.Shape == null ? "fixed" : $"{(plan.Shape.Overfly ? "overfly" : "break")}, entry {plan.Shape.EntryTurn:+0;-0;0} deg";
            string from = Compass(-Vector3.ProjectOnPlane(path.Fwd[0], Vector3.up));
            string cover = plan.Masked ? $", masked ({plan.Cover} surfaces on the axis{(plan.Delay ? ", delay fuze" : "")})" : "";
            MelonLogger.Msg($"[Air] MSN {s.Number:00} {Code(s.Type)} plan: heading {plan.Az:000} at {plan.Angle:F0} deg of {plan.Axes} axes, reach {plan.Reach * 100f:F0} %{cover}; " +
                            $"{how}, enters from the {from}, attacks in {s.FireAt - now:F1} s, flight {path.End - path.T0:F0} s");
        }

        // ── Debug draw ──────────────────────────────────────────────────────────

        private static Material _planMat;

        /// <summary>DebugDrawAirPlan: the reach lines (green out, red blocked), the chosen axis (yellow) and the flight (cyan), until the strike is dropped.</summary>
        private static GameObject DrawPlan(Plan plan, Path path, Vector3 mark, Path fall = null)
        {
            if (!Config.DebugDrawAirPlan || plan == null) return null;
            try
            {
                if (_planMat == null)
                {
                    _planMat = new Material(Config.FindSpriteShader()) { hideFlags = HideFlags.DontUnloadUnusedAsset };
                    _planMat.SetInt("_ZTest", -1);   // on top of the world, as the explosion debug lines
                }
                var root = new GameObject("BA_AirPlan");
                root.layer = 2;
                foreach (var r in plan.Rays) PlanLine(root, new[] { r.A, r.B }, r.Ok ? new Color(0.3f, 1f, 0.3f, 0.35f) : new Color(1f, 0.25f, 0.2f, 0.35f), 0.04f);
                Vector3 arrive = plan.Terminal != Vector3.zero ? plan.Terminal : Descending(plan.Heading, plan.Angle);
                var ring = new List<PlanRay>();
                Reach(plan.Aim, arrive, 400f, 1.5f, out _, ring, true);
                foreach (var r in ring) PlanLine(root, new[] { r.A, r.B }, r.Ok ? new Color(1f, 0.85f, 0.1f, 0.9f) : new Color(1f, 0.4f, 0.1f, 0.9f), 0.12f);
                PlanLine(root, Points(path), new Color(0.2f, 0.95f, 1f, 0.9f), 1.5f);
                if (fall != null) PlanLine(root, Points(fall), new Color(1f, 0.6f, 0.1f, 0.9f), 0.6f);
                return root;
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[Air] couldn't draw the plan: {e.Message}");
                return null;
            }
        }

        private static Vector3[] Points(Path path)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i < path.Pos.Count; i += 5) pts.Add(path.Pos[i]);
            pts.Add(path.Pos[path.Pos.Count - 1]);
            return pts.ToArray();
        }

        private static void PlanLine(GameObject root, Vector3[] pts, Color c, float width)
        {
            var go = new GameObject("Line");
            go.layer = 2;
            go.transform.SetParent(root.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = _planMat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startWidth = width; lr.endWidth = width;
            lr.startColor = c; lr.endColor = c;
            lr.positionCount = pts.Length;
            for (int i = 0; i < pts.Length; i++) lr.SetPosition(i, pts[i]);
        }
    }
}
