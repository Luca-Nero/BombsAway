using System.Collections.Generic;
using FruitLib;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The rockets: Hydra 70 (2.75 in) from an AH-64 (GUNFIGHTER, a stand-in Apache). It comes
    /// in low across your line of sight, noses into a shallow running dive (RocketDiveAngle) and
    /// fires a pair every RocketPairInterval, one from each M261 pod, the first from
    /// RocketFireRange out, then breaks away from your side.
    ///
    /// Each rocket's flight is worked out when it is planned (RocketPath): the Mk 66 motor's
    /// 1.07 s burn to 739 m/s on top of the helicopter's own speed, then a coast under drag and
    /// gravity. The pilot's sight leads it onto its aim point by flying it (RocketLead, as the
    /// guns' Lead); unguided, it then scatters by RocketDispersion. Its smoke trail lasts while
    /// the motor burns. Each frame its move is raycast, so the first thing on its path takes it.
    ///
    /// HE (M151, the "10 pounder"): bursts where it hits (BombsAway.Hydra, ~1.4 kg TNT), its
    /// cast-iron body's fragments aimed at the bodies in reach in a belt square to its flight.
    /// The pairs are walked RocketWalk metres through the mark.
    ///
    /// Flechette (M255A1): the M439 fuze is set by range and throws the darts FlechetteBurstRange
    /// short of the aim, with a puff of red marker pigment. 1,179 hardened steel darts of 3.9 g
    /// (FlechetteCount) carry on at the rocket's speed, each a real FruitLib round
    /// (BombsAway.Flechette) that flies, ricochets, goes through what it can and wounds through
    /// the game's own bullet model. They are thrown to land evenly within FlechettePatternRadius
    /// of where the rocket itself would have come down (so its scatter still counts): a free
    /// cone at the run's shallow arrival strung most of them out hundreds of metres past the
    /// mark. The pair go side by side, so their patterns overlap over the mark.
    ///   OBS: GUNFIGHTER 16, the grid, DANGER CLOSE, ROCKETS, n HE / FLECHETTE, the heading.
    ///   The readback; INBOUND / CONTINUE; IN FROM THE x / CLEARED HOT; ROCKETS AWAY; BREAKING x;
    ///   and once they're down, GOOD EFFECT ON TARGET.
    /// </summary>
    internal static partial class AirStrike
    {
        private const string HeloSign = "GUNFIGHTER", HeloCallsign = "Gunfighter 16";
        private const float MotorBurn = 1.07f;         // Mk 66: seconds
        private const float MotorDeltaV = 739f;        // its burnout speed from rest, m/s
        private const float RocketDragK = 1.3e-4f;     // a = -k|v|v, per metre: ~9 kg, 70 mm, Cd ~0.5 supersonic
        private const float RocketFlight = 9f;         // seconds of flight worked out per rocket
        private const float RocketStep = 0.01f;        // its integration step
        private const float HeloPitch = 6f;            // nose down in forward flight, degrees
        private const float HeloClimb = 12f;           // its break: a gentle climbing turn
        private const float HeloHold = 1.5f;           // seconds it holds the dive after the last pair, before it breaks
        private const float PodRight = 2.76f, PodDown = 0.912f, PodAhead = 0.996f;   // the M261 pods' mouths (the AH64 model's PodL / PodR markers)
        private const float PodStagger = 0.06f;        // seconds between the two rockets of a pair
        private const float RotorHz = 289f / 60f, TailRotorHz = 1403f / 60f;
        private const float DartMuzzle = 650f;         // the darts' spec speed; each is set to its rocket's own
        private const string DartId = "BombsAway.Flechette";
        private static readonly RoundTag DartTag = new RoundTag { Kind = "Dart" };

        private static bool IsRocket(FireMissionType t) => t == FireMissionType.Hydra || t == FireMissionType.Flechette;

        private sealed class RocketKind
        {
            public FireMissionType Type;
            public string Code, Name, Net;   // "RKT" on the strip, "HYDRA M151 HE" under it, "HE" on the net
            public bool Darts;
            public int Count;
        }

        private static RocketKind RocketFor(FireMissionType t) =>
            t == FireMissionType.Flechette
                ? new RocketKind { Type = t, Code = "FLC", Name = "M255A1 FLECHETTE", Net = "flechette", Darts = true, Count = Mathf.Clamp(Config.FlechetteRockets, 1, 8) }
                : new RocketKind { Type = FireMissionType.Hydra, Code = "RKT", Name = "HYDRA M151 HE", Net = "HE", Count = Mathf.Clamp(Config.HydraRockets, 1, 38) };

        /// <summary>One rocket: its flight worked out at the call, from its launch on.</summary>
        private sealed class Rocket
        {
            public float LaunchAt, BurstAt = float.MaxValue;   // BurstAt: the flechette fuze's moment
            public Vector3 Aim;                                // where it was aimed (the darts' pattern lies at its height)
            public Path Path;
            public GameObject Body, TrailGo;
            public Transform Flame;
            public TrailRenderer Trail;
            public Vector3 LastPos;
            public bool Launched, Done;
        }

        private sealed class RocketRun
        {
            public RocketKind K;
            public readonly List<Rocket> Rockets = new List<Rocket>();
            public bool Done
            {
                get { foreach (var r in Rockets) if (!r.Done) return false; return true; }
            }
        }

        // ── Calling ─────────────────────────────────────────────────────────────

        private static bool CallRockets(FireMissionType type, Vector3 mark, Vector3 observer)
        {
            Hook();
            RegisterDart();
            var k = RocketFor(type);
            string grid = FireMission.Grid(mark);
            string rds = $"{k.Count} {k.Net}";
            string ammo = k.Darts ? "FLECHETTE" : "HE", weapon = k.Darts ? "M255A1" : "M151";
            if (!Config.ArtyStacking && Busy)
            {
                if (!RefuseOnTerminal(k.Code, grid, HeloCallsign, k.Count, ammo, weapon, CraftName(Airframe.AH64)))
                {
                    RadioLog.Observer($"{HeloCallsign}, grid {grid}. Rockets, {rds}. Over.");
                    RadioLog.Unit(HeloSign, "Unable, engaged. Out.");
                }
                return false;
            }

            float now = Time.time;
            float speed = Mathf.Max(20f, Config.RocketRunSpeed);
            bool danger = Vector3.Distance(mark, observer) < Config.RocketDangerClose;
            int pairs = (k.Count + 1) / 2;
            float interval = Mathf.Max(0.05f, Config.RocketPairInterval);
            float firing = (pairs - 1) * interval + (k.Count > 1 ? PodStagger : 0f);   // first launch to last

            // The approach (AirStrike.Approach.cs); its flight is worked out firing at t = 0, then moved to the clock.
            float range = Mathf.Max(400f, Config.RocketFireRange);
            var plan = PlanRockets(Mathf.Clamp(Config.RocketDiveAngle, 0f, 30f), mark, observer, danger, p =>
            {
                Vector3 dn = Descending(p.Heading, p.Angle);
                return BuildRun(mark - dn * range, dn, p.Heading, speed, 0f, firing + HeloHold, p.Away, 1.6f, HeloClimb, p.Shape);
            }, out Path path);
            Vector3 heading = plan.Heading;
            Vector3 right = Vector3.Cross(Vector3.up, heading).normalized;
            int hdg = Mathf.RoundToInt(plan.Az) % 360;

            // On the terminal the helicopter is spawned where its run begins and the call is made
            // once its program is up: the whole timeline starts from there.
            var term = OpenTerminal(k.Code, danger, mark, path.Pos[0], heading, grid, HeloCallsign, k.Count, ammo, weapon, CraftName(Airframe.AH64));
            SetPlanWords(term, plan, path);
            if (term != null) now += FireTerminal.Play(term, CallSteps(plan));
            float fireAt = now + Mathf.Max(12f, Config.AirTimeOnTarget);
            float lastLaunch = fireAt + firing;

            var s = new Strike
            {
                Number = FireMission.NextNumber(), Type = k.Type, Sign = HeloSign, CraftKind = Airframe.AH64,
                Mark = mark, Heading = heading, Right = right, Term = term,
                FireAt = fireAt, BurstEnd = lastLaunch, InAt = fireAt - 5f,
                R = new RocketRun { K = k },
            };

            path.T0 += fireAt;
            s.Path = path;
            s.Engine = new PathSound { Key = "HeloAH64Loop", From = s.Path.T0, To = s.Path.End, FadeIn = 3f, FadeOut = 3f };

            // The rockets: a pair every interval, left pod then right. HE walks along the heading
            // through the mark; the flechettes go side by side at it, so the two patterns overlap.
            float sigma = Mathf.Max(0f, Config.RocketDispersion) * (Mathf.PI * 2f / 6400f) / 1.794f;   // 80 % inside the angle
            float walk = k.Darts ? 0f : Mathf.Max(0f, Config.RocketWalk);
            float burstRange = Mathf.Max(10f, Config.FlechetteBurstRange);
            float lastImpact = lastLaunch;
            for (int i = 0; i < k.Count; i++)
            {
                int pair = i / 2;
                float pod = i % 2 == 0 ? -1f : 1f;
                float at = fireAt + pair * interval + (i % 2) * PodStagger;
                Vector3 aim = pairs > 1 ? Vector3.Lerp(mark - heading * walk * 0.5f, mark + heading * walk * 0.5f, pair / (pairs - 1f)) : mark;
                if (k.Darts && k.Count > 1) aim += right * (pod * 6f);

                s.Path.Sample(at, out Vector3 pos, out Vector3 fwd, out Vector3 up);
                Vector3 craftRight = Vector3.Cross(up, fwd);
                Vector3 launch = pos + craftRight * (pod * PodRight) - up * PodDown + fwd * PodAhead;
                Vector3 v0 = fwd * speed;

                Vector3 dir = RocketLead(launch, v0, aim, out float flight);
                Vector3 a = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.right).normalized, b = Vector3.Cross(dir, a);
                dir = (dir + (a * Gauss() + b * Gauss()) * sigma).normalized;

                var r = new Rocket { LaunchAt = at, Aim = aim, Path = RocketPath(launch, v0, dir, at) };
                if (k.Darts)
                {
                    // The M439 fuze, set by range: the moment the rocket is burstRange short of its aim.
                    for (int j = 0; j < r.Path.Pos.Count; j++)
                        if (Vector3.Distance(r.Path.Pos[j], aim) <= burstRange) { r.BurstAt = r.Path.T0 + j * SampleDt; break; }
                }
                s.R.Rockets.Add(r);
                lastImpact = Mathf.Max(lastImpact, at + flight);
            }

            // The net.
            string offWord = Compass(Vector3.ProjectOnPlane(s.Path.Fwd[s.Path.Fwd.Count - 1], Vector3.up));
            string rockets = k.Count == 1 ? "rocket" : "rockets";
            term?.Set("OFF", offWord);
            if (term == null) RadioLog.Observer($"{HeloCallsign}, grid {grid}. {(danger ? "Danger close. " : "")}Rockets, {rds}, heading {hdg:000}. Over.");
            Say(s, now + 3f, false, $"Grid {grid}, {rds} {rockets}, heading {hdg:000}.{PlanWords(plan, false)}", "readback");
            Say(s, now + 3f, true, "Readback correct.");
            Say(s, fireAt - 11f, false, "Inbound.", "inbound");
            Say(s, fireAt - 11f, true, "Continue.");
            Say(s, s.InAt, false, $"In from the {Compass(-heading)}.", "in");
            Say(s, s.InAt, true, "Cleared hot.");
            Say(s, lastLaunch + 0.5f, false, "Rockets away.", "away");
            Say(s, lastLaunch + 3f, false, $"Breaking {offWord}.", "off");
            Say(s, lastImpact + 3f, true, "Good effect on target. End of mission. Out.", "end", "kill", "last", "free");
            s.CompleteAt = lastImpact + 3f;

            _strikes.Add(s);
            s.PlanDraw = DrawPlan(plan, path, mark);
            LogPlan(s, plan, now);
            return true;
        }

        // ── The rocket's flight ─────────────────────────────────────────────────

        /// <summary>
        /// One step of a Hydra's flight from launch (<paramref name="t"/> seconds in): the motor
        /// pushes along its axis (<paramref name="axis"/>, held while it burns), then it coasts;
        /// air drag and gravity throughout.
        /// </summary>
        private static void RocketStepOnce(ref Vector3 p, ref Vector3 v, Vector3 axis, float t, float h)
        {
            Vector3 a = Physics.gravity - RocketDragK * v.magnitude * v;
            if (t < MotorBurn) a += axis * (MotorDeltaV / MotorBurn * 1.06f);   // a little over: the drag it fights while burning
            v += a * h;
            p += v * h;
        }

        /// <summary>
        /// The launch direction that puts a rocket from <paramref name="from"/> (carried along at
        /// <paramref name="v0"/> by the helicopter) on <paramref name="target"/>: fly a test
        /// rocket, aim off by its miss, four times, as the pilot's sight works it out.
        /// <paramref name="flight"/> is its time to the target.
        /// </summary>
        private static Vector3 RocketLead(Vector3 from, Vector3 v0, Vector3 target, out float flight)
        {
            flight = 0f;
            Vector3 aimAt = target;
            float dist = Vector3.Distance(from, target);
            if (dist < 1f) return (target - from).normalized;
            Vector3 axis = (target - from) / dist;
            int max = Mathf.CeilToInt(RocketFlight / RocketStep);
            for (int it = 0; it < 4; it++)
            {
                Vector3 dir = (aimAt - from).normalized;
                Vector3 p = from, v = v0;
                float t = 0f;
                for (int n = 0; n < max && Vector3.Dot(p - from, axis) < dist; n++)
                {
                    RocketStepOnce(ref p, ref v, dir, t, RocketStep);
                    t += RocketStep;
                }
                // Back to where it crossed the target's plane: a step is ~7 m at its speed.
                float speed = Mathf.Max(1f, v.magnitude);
                Vector3 vd = v / speed;
                float over = (Vector3.Dot(p - from, axis) - dist) / Mathf.Max(0.1f, Vector3.Dot(vd, axis));
                p -= vd * over;
                t -= over / speed;
                aimAt += target - p;
                flight = t;
            }
            return (aimAt - from).normalized;
        }

        /// <summary>A rocket's whole flight from <paramref name="t0"/>, sampled every SampleDt.</summary>
        private static Path RocketPath(Vector3 p, Vector3 v0, Vector3 axis, float t0)
        {
            var path = new Path { T0 = t0 };
            Vector3 v = v0;
            int per = Mathf.Max(1, Mathf.RoundToInt(SampleDt / RocketStep));
            int steps = Mathf.CeilToInt(RocketFlight / RocketStep);
            path.Pos.Add(p);
            path.Fwd.Add(axis);
            float t = 0f;
            for (int n = 1; n <= steps; n++)
            {
                RocketStepOnce(ref p, ref v, axis, t, RocketStep);
                t += RocketStep;
                if (n % per == 0)
                {
                    path.Pos.Add(p);
                    path.Fwd.Add(v.sqrMagnitude > 1f ? v.normalized : axis);
                }
            }
            path.Speed = v.magnitude;
            foreach (var f in path.Fwd)
            {
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, f);
                path.Up.Add(up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.forward);
            }
            return path;
        }

        // ── Per frame ───────────────────────────────────────────────────────────

        private static void TickRockets(Strike s, float now)
        {
            var run = s.R;
            if (run == null) return;
            var cam = Camera.main;
            foreach (var r in run.Rockets)
            {
                if (r.Done || now < r.LaunchAt) continue;
                if (!r.Launched) Launch(r, run.K, cam);

                // The flechettes' fuze: the darts are thrown where the rocket is at that moment.
                float until = Mathf.Min(now, r.BurstAt);
                r.Path.Sample(until, out Vector3 pos, out Vector3 fwd, out Vector3 up);

                Vector3 seg = pos - r.LastPos;
                float len = seg.magnitude;
                if (len > 1e-3f)
                {
                    Vector3 dir = seg / len;
                    if (FireMission.PathHit(r.LastPos, dir, len, out Vector3 hit, out Vector3 normal))
                    {
                        try { SmokeCloud.Wake(r.LastPos, hit, 0.6f, 4f); } catch { }
                        if (run.K.Darts) Expel(r, hit - dir * 1f, dir, r.Path.SpeedAt(until), cam, false);   // short: the darts go on into it
                        else HydraBurst(r, hit - dir * 0.15f, normal, dir);
                        continue;
                    }
                    try { SmokeCloud.Wake(r.LastPos, pos, 0.6f, 4f); } catch { }
                }
                r.LastPos = pos;

                if (now >= r.BurstAt)
                {
                    Expel(r, pos, fwd, r.Path.SpeedAt(r.BurstAt), cam, true);
                    continue;
                }

                if (r.Body != null) r.Body.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, up));
                bool burning = now - r.LaunchAt < MotorBurn;
                if (r.TrailGo != null) r.TrailGo.transform.position = pos - fwd * 0.75f;
                if (r.Trail != null) r.Trail.emitting = now - r.LaunchAt < MotorBurn + 0.15f;
                if (r.Flame != null)
                {
                    if (r.Flame.gameObject.activeSelf != burning) r.Flame.gameObject.SetActive(burning);
                    if (burning) r.Flame.localScale = new Vector3(0.1f, 0.1f, Random.Range(0.3f, 0.55f));
                }

                if (now > r.Path.End)
                {
                    if (Config.Dbg1) MelonLogger.Msg($"[Air] a rocket left the arena near {s.Mark}");
                    EndRocket(r);
                }
            }
        }

        private static void Launch(Rocket r, RocketKind k, Camera cam)
        {
            r.Launched = true;
            r.LastPos = r.Path.Pos[0];
            r.Body = BuildRocket(k.Darts, out r.Flame);
            r.Body.transform.SetPositionAndRotation(r.LastPos, Quaternion.LookRotation(r.Path.Fwd[0], r.Path.Up[0]));
            r.TrailGo = RocketTrail(r.LastPos, out r.Trail);
            // The motor's roar leaving the pod, from there, late by the distance.
            float delay = cam != null ? Vector3.Distance(cam.transform.position, r.LastPos) / SpeedOfSound : 0f;
            Sfx.Play("RocketLaunch", r.LastPos, null, 1f, delay);
        }

        /// <summary>An M151 going off where it hit; its body's side spray square to its flight.</summary>
        private static void HydraBurst(Rocket r, Vector3 at, Vector3 normal, Vector3 axis)
        {
            EndRocket(r);
            try
            {
                var x = ExplosionParams.FromHydraConfig(at);
                x.Forward = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
                x.Axis = axis;
                ExplosionSystem.Detonate(x);
            }
            catch (System.Exception e) { MelonLogger.Warning($"[Air] Hydra burst failed: {e.Message}"); }
        }

        /// <summary>
        /// The M255A1's fuze has gone: its darts leave the front of the warhead at the rocket's
        /// speed, each one a real round from here on. Fuzed (<paramref name="fuzed"/>), they are
        /// thrown to land evenly over FlechettePatternRadius round where the rocket's own flight
        /// would have met its aim's height, each at a point up to a body's height over it and
        /// lifted for its drop. A free cone (FlechetteConeDeg), even across, when the rocket met
        /// something first, or with FlechettePatternRadius 0.
        /// </summary>
        private static void Expel(Rocket r, Vector3 at, Vector3 fwd, float speed, Camera cam, bool fuzed)
        {
            EndRocket(r);
            try { ExplosionFx.Play("FlechetteBurst", at, fwd, false, default); } catch { }

            float range = Mathf.Max(10f, Config.FlechetteBurstRange);
            float radius = Mathf.Max(0f, Config.FlechettePatternRadius);
            Vector3 centre = default;
            bool aimed = fuzed && radius > 0f && fwd.y < -0.01f;
            if (aimed)
            {
                float run = (at.y - r.Aim.y) / -fwd.y;
                centre = at + fwd * run;
                aimed = run > 1f && run < 4f * range;
            }
            Vector3 along = Vector3.ProjectOnPlane(fwd, Vector3.up);
            along = along.sqrMagnitude > 1e-4f ? along.normalized : Vector3.forward;
            Vector3 across = Vector3.Cross(Vector3.up, along);

            float spread = Mathf.Tan(Mathf.Clamp(Config.FlechetteConeDeg, 0.5f, 30f) * Mathf.Deg2Rad);
            Vector3 a = Vector3.Cross(fwd, Mathf.Abs(fwd.y) < 0.99f ? Vector3.up : Vector3.right).normalized, b = Vector3.Cross(fwd, a);
            int n = Mathf.Clamp(Config.FlechetteCount, 1, 4000);
            float scale = Mathf.Max(0f, Config.FlechettePowerScale);
            float g = Physics.gravity.magnitude;
            int fired = 0;
            for (int i = 0; i < n; i++)
            {
                float v = speed * Random.Range(0.97f, 1.03f);
                float ang = Random.value * Mathf.PI * 2f;
                Vector3 from, d;
                if (aimed)
                {
                    from = at + fwd * Random.Range(0f, 0.5f);
                    float rr = Mathf.Sqrt(Random.value) * radius;
                    Vector3 to = centre + (across * Mathf.Cos(ang) + along * Mathf.Sin(ang)) * rr + Vector3.up * Random.Range(0.1f, 1.7f);
                    float tof = Vector3.Distance(from, to) / Mathf.Max(100f, v * 0.95f);   // a little slowed by drag
                    to.y += 0.5f * g * tof * tof;
                    d = (to - from).normalized;
                }
                else
                {
                    float rr = Mathf.Sqrt(Random.value) * spread;
                    d = (fwd + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * rr).normalized;
                    from = at + d * Random.Range(0f, 0.5f);
                }
                var p = FruitBallistics.SpawnProjectile(DartId, from, d);
                if (p == null) continue;
                p.Velocity = d * v;   // its power follows this speed
                p.Tag = DartTag;
                p.PowerScale = scale;
                fired++;
            }

            // The darts' rain on the ground, heard once they're there and its sound has come back.
            Vector3 zone = aimed ? centre : at + fwd * range;
            float delay = Vector3.Distance(at, zone) / Mathf.Max(100f, speed) + (cam != null ? Vector3.Distance(cam.transform.position, zone) / SpeedOfSound : 0f);
            Sfx.Play("FlechetteRain", zone, null, 1f, delay);
            if (Config.Dbg1)
                MelonLogger.Msg(aimed
                    ? $"[Air] flechettes: {fired} darts at {speed:F0} m/s from {at}, over {radius:F0} m round {centre} ({Vector3.Distance(centre, r.Aim):F1} m off the aim)"
                    : $"[Air] flechettes: {fired} darts at {speed:F0} m/s from {at}, free cone");
        }

        private static void EndRocket(Rocket r)
        {
            r.Done = true;
            if (r.Body != null) Object.Destroy(r.Body);
            r.Body = null;
            r.Flame = null;
            // The trail fades out on its own.
            if (r.TrailGo != null)
            {
                if (r.Trail != null) r.Trail.emitting = false;
                Object.Destroy(r.TrailGo, r.Trail != null ? r.Trail.time + 0.2f : 0f);
            }
            r.TrailGo = null;
            r.Trail = null;
        }

        private static void DropRockets(RocketRun run)
        {
            foreach (var r in run.Rockets)
            {
                if (r.Body != null) Object.Destroy(r.Body);
                if (r.TrailGo != null) Object.Destroy(r.TrailGo);
                r.Body = null; r.TrailGo = null; r.Trail = null; r.Flame = null;
                r.Done = true;
            }
        }

        // ── The darts ───────────────────────────────────────────────────────────

        /// <summary>
        /// An M255A1 dart: a 3.9 g (60 grain) hardened steel flechette, about 3.3 mm across with
        /// fins, flying nose first, so it keeps its speed and goes through a lot for its weight.
        /// It makes a narrow track that bends as it goes (the yaw on leaving a body).
        /// </summary>
        private static void RegisterDart()
        {
            var d = ProjectileSpec.Cartridge(DartId, 3.9f, 3.3f, DartMuzzle, 0.35f);
            d.ExternalForces = false;
            d.Lifetime = 2f;
            d.WorldImpulse = 4f;
            d.RicochetAngle = 75f;
            d.MaxBounces = 1;
            d.RicochetEnergyLoss = 0.55f;
            d.RicochetScatter = 6f;
            d.PenetrationDeflect = 4f;
            d.KillPowerRatio = 0.04f;
            d.Wound = new WoundProfile
            {
                CrushRadius = 0, CleanEntryDepth = 4, SpreadChance = 0.2f,
                CavitationPeakRadius = 0, TearMinRadius = 0.5f, TearMaxRadius = 1.2f, ExitTearDamage = -220f,
                MaxDepth = 80, HardTissueScale = 0.15f, ImpactImpulse = 6f,
            };
            FruitBallistics.Register(d);
        }

        private struct Dust { public Vector3 At, Normal; }
        private static readonly List<Dust> _dartDust = new List<Dust>();
        private static int _dartHits;
        private static GameObject _dartFx;
        private static ParticleSystem _dartDustPs;
        private static readonly Color32 DustColor = new Color32(104, 102, 98, 255);

        /// <summary>A dart met the ground or a wall (FruitLib is mid-step): its puff of dust waits for this mod's frame.</summary>
        private static void DartHit(SurfaceHitInfo h)
        {
            if (_dartHits++ % Mathf.Max(1, Config.FlechetteHitFxEvery) != 0 || _dartDust.Count >= 1000) return;
            _dartDust.Add(new Dust { At = h.Point, Normal = h.Normal });
        }

        /// <summary>The dust where darts hit, emitted into one long-lived system (FX_DartHits) rather than an effect each.</summary>
        private static void FlushDartDust()
        {
            if (_dartDust.Count == 0) return;
            if (_dartDustPs == null)
            {
                try
                {
                    var prefab = OrdnanceModels.Asset("FX_DartHits");
                    if (prefab != null)
                    {
                        _dartFx = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
                        _dartFx.name = "FX_DartHits";
                        var t = _dartFx.transform.Find("Dust");
                        _dartDustPs = t != null ? t.GetComponent<ParticleSystem>() : null;
                        if (_dartDustPs != null) _dartDustPs.Play(true);
                    }
                }
                catch (System.Exception e) { MelonLogger.Warning($"[Air] dart dust unavailable: {e.Message}"); }
                if (_dartDustPs == null) { _dartDust.Clear(); return; }
            }
            foreach (var d in _dartDust)
            {
                Vector3 n = d.Normal.sqrMagnitude > 1e-4f ? d.Normal.normalized : Vector3.up;
                _dartDustPs.Emit(d.At + n * 0.04f, n * Random.Range(0.5f, 1.6f) + Random.insideUnitSphere * 0.35f,
                                 Random.Range(0.12f, 0.26f), Random.Range(0.5f, 0.95f), DustColor);
            }
            _dartDust.Clear();
        }

        // ── The stand-ins ───────────────────────────────────────────────────────

        private static Material _odMat, _discMat;

        /// <summary>
        /// A Hydra 70: the bundle's HydraHE / HydraFLC (5.24.0, 1.2x real, the motor's flame hung
        /// behind its Nozzle marker), or a stand-in out of boxes at its real size (70 mm, about
        /// 1.4 m with its warhead): the Mk 66 motor, the warhead (HE: olive with the yellow band of
        /// a live filling; flechette: olive with a light band), the fuze, four wrap-around fins,
        /// and the motor's flame while it burns.
        /// </summary>
        private static GameObject BuildRocket(bool darts, out Transform flame)
        {
            EnsureMats();
            var model = SpawnBare(darts ? "HydraFLC" : "HydraHE");
            if (model != null)
            {
                model.name = darts ? "BA_M255" : "BA_M151";
                var nozzle = model.transform.Find("Nozzle");
                Vector3 at = (nozzle != null ? nozzle.localPosition : new Vector3(0f, 0f, -0.87f)) + new Vector3(0f, 0f, -0.2f);
                flame = Part(model.transform, PrimitiveType.Cube, at, new Vector3(0.12f, 0.12f, 0.4f), _flashMat);
                flame.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return model;
            }
            if (_odMat == null) _odMat = new Material(Config.FindShader()) { hideFlags = HideFlags.DontUnloadUnusedAsset, color = new Color(0.185f, 0.195f, 0.165f) };   // od
            if (_yelMat == null) _yelMat = new Material(Config.FindShader()) { hideFlags = HideFlags.DontUnloadUnusedAsset, color = new Color(0.82f, 0.59f, 0f) };     // yel
            var root = new GameObject(darts ? "BA_M255" : "BA_M151");
            root.layer = 2;
            var t = root.transform;
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, -0.2f), new Vector3(0.07f, 0.07f, 1.06f), _bodyMat);    // motor
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.52f), new Vector3(0.072f, 0.072f, 0.4f), _odMat);     // warhead
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.42f), new Vector3(0.075f, 0.075f, 0.05f), darts ? _darkMat : _yelMat);   // its band
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0.77f), new Vector3(0.04f, 0.04f, 0.1f), _darkMat);     // fuze
            for (int i = 0; i < 2; i++)
            {
                var fin = Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, -0.68f), new Vector3(0.012f, 0.24f, 0.1f), _darkMat);
                fin.localRotation = Quaternion.Euler(0f, 0f, 45f + 90f * i);
            }
            flame = Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, -0.9f), new Vector3(0.1f, 0.1f, 0.4f), _flashMat);
            flame.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        /// <summary>The Mk 66's smoke trail, on its own object so it fades out after the rocket is gone.</summary>
        private static GameObject RocketTrail(Vector3 at, out TrailRenderer tr)
        {
            var go = new GameObject("BA_RocketTrail");
            go.layer = 2;
            go.transform.position = at;
            tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = _smokeMat;
            tr.time = 2.2f;
            tr.minVertexDistance = 1.5f;
            tr.startWidth = 0.3f;
            tr.endWidth = 2.6f;
            tr.startColor = new Color(0.82f, 0.81f, 0.78f, 0.6f);
            tr.endColor = new Color(0.72f, 0.71f, 0.69f, 0f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>
        /// The bundle's AH64 (5f, 1.2x real): its Rotor turns about local Y over a faint disc, its
        /// TailRotor about local X. Without it, the box stand-in below.
        /// </summary>
        private static void BuildAH64(Strike s)
        {
            EnsureMats();
            if (_discMat == null) _discMat = new Material(Config.FindSpriteShader()) { hideFlags = HideFlags.DontUnloadUnusedAsset, color = new Color(0.12f, 0.12f, 0.13f, 0.16f) };
            var model = SpawnBare("AH64");
            if (model != null)
            {
                s.Rotor = model.transform.Find("Rotor");
                s.TailRotor = model.transform.Find("TailRotor");
                if (s.Rotor != null)
                {
                    s.RotorRest = s.Rotor.localRotation;
                    var d = Part(model.transform, PrimitiveType.Cylinder, s.Rotor.localPosition, new Vector3(17.56f, 0.005f, 17.56f), _discMat);
                    d.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                if (s.TailRotor != null) s.TailRotorRest = s.TailRotor.localRotation;
                s.Craft = model;
                return;
            }
            BuildAH64Boxes(s);
        }

        /// <summary>
        /// An AH-64 out of boxes at its real size (15.5 m fuselage and boom, 14.6 m rotor), when
        /// the bundle has no model: the narrow fuselage with its tandem canopy, the chin sensor
        /// turret and gun, engine nacelles either side of the mast, stub wings with an M261 pod
        /// outboard and a rail inboard on each, the tail boom, fin, stabilator and the tail rotor
        /// on its left, and the four-bladed main rotor turning over a faint disc.
        /// </summary>
        private static void BuildAH64Boxes(Strike s)
        {
            var root = new GameObject("BA_AH64");
            root.layer = 2;
            var t = root.transform;
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(1.3f, 1.9f, 7f), _bodyMat);           // fuselage
            Part(t, PrimitiveType.Cube, new Vector3(0f, -0.1f, 4.2f), new Vector3(1f, 1.5f, 2.2f), _bodyMat);       // nose
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0.65f, 3.7f), new Vector3(0.9f, 0.7f, 2.8f), _darkMat);     // canopy, two seats in tandem
            Part(t, PrimitiveType.Cube, new Vector3(0f, -0.7f, 5.5f), new Vector3(0.6f, 0.6f, 0.6f), _darkMat);     // the sensor turret
            Part(t, PrimitiveType.Cube, new Vector3(0f, -1.15f, 3.4f), new Vector3(0.15f, 0.15f, 1.6f), _darkMat); // the chin gun
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0.25f, -6.2f), new Vector3(0.55f, 0.75f, 5.6f), _bodyMat); // tail boom
            Part(t, PrimitiveType.Cube, new Vector3(0f, 1.4f, -8.8f), new Vector3(0.18f, 2.4f, 1.3f), _bodyMat);   // fin
            Part(t, PrimitiveType.Cube, new Vector3(0f, 0.1f, -8.9f), new Vector3(3.2f, 0.1f, 0.8f), _bodyMat);    // stabilator
            Part(t, PrimitiveType.Cube, new Vector3(0f, 1.35f, 0.4f), new Vector3(0.3f, 0.7f, 0.3f), _darkMat);    // mast
            for (int side = -1; side <= 1; side += 2)
            {
                Part(t, PrimitiveType.Cube, new Vector3(side * 0.95f, 0.75f, -0.6f), new Vector3(0.65f, 0.75f, 2.6f), _bodyMat);   // engine nacelles
                Part(t, PrimitiveType.Cube, new Vector3(side * 1.9f, -0.1f, 0.3f), new Vector3(2.6f, 0.15f, 1.1f), _bodyMat);      // stub wings
                Part(t, PrimitiveType.Cube, new Vector3(side * PodRight, -PodDown, PodAhead), new Vector3(0.42f, 0.42f, 1.7f), _darkMat);   // M261 pods
                Part(t, PrimitiveType.Cube, new Vector3(side * 1.4f, -0.4f, 0.3f), new Vector3(0.2f, 0.3f, 1.4f), _darkMat);      // rails
            }

            var rotor = new GameObject("Rotor");
            rotor.layer = 2;
            rotor.transform.SetParent(t, false);
            rotor.transform.localPosition = new Vector3(0f, 1.78f, 0.4f);
            for (int i = 0; i < 2; i++)
            {
                var blade = Part(rotor.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(14.6f, 0.05f, 0.53f), _darkMat);
                blade.localRotation = Quaternion.Euler(0f, 90f * i, 0f);
            }
            var disc = Part(t, PrimitiveType.Cylinder, new Vector3(0f, 1.78f, 0.4f), new Vector3(14.6f, 0.005f, 14.6f), _discMat);
            disc.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.Rotor = rotor.transform;

            var tail = new GameObject("TailRotor");
            tail.layer = 2;
            tail.transform.SetParent(t, false);
            tail.transform.localPosition = new Vector3(-0.32f, 1.9f, -9.05f);
            for (int i = 0; i < 2; i++)
            {
                var blade = Part(tail.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(0.03f, 2.8f, 0.2f), _darkMat);
                blade.localRotation = Quaternion.Euler(90f * i, 0f, 0f);
            }
            s.TailRotor = tail.transform;
            s.Craft = root;
        }

        /// <summary>The helicopter flies nose down, its rotors turning at their real speed.</summary>
        private static void TickHelo(Strike s)
        {
            s.Craft.transform.rotation *= Quaternion.Euler(HeloPitch, 0f, 0f);
            float now = Time.time;
            if (s.Rotor != null) s.Rotor.localRotation = s.RotorRest * Quaternion.AngleAxis(Mathf.Repeat(now * RotorHz * 360f, 360f), Vector3.up);
            if (s.TailRotor != null) s.TailRotor.localRotation = s.TailRotorRest * Quaternion.AngleAxis(Mathf.Repeat(now * TailRotorHz * 360f, 360f), Vector3.right);
        }
    }
}
