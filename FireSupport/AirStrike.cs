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
    /// Air strikes, the AIR page of the binoculars' strip. An aircraft answers the lase on the
    /// net the way close air support runs (JP 3-09.3), compressed: you are the controller.
    ///   OBS: its call sign, the grid, DANGER CLOSE if the mark is near you, the attack. OVER.
    ///   It reads the grid and heading back; READBACK CORRECT. IP INBOUND; CONTINUE.
    ///   IN FROM THE WEST; CLEARED HOT. The attack. OFF NORTH. Then the end of mission.
    /// It attacks AirTimeOnTarget after the call, along an attack heading across your line of
    /// sight (AirAttackHeading -1), so its hits walk past you rather than toward you.
    ///
    /// Its whole flight is worked out at the call (Path): far off and level, a smooth pitch into
    /// the dive, the attack, then a pull-off and climbing turn away from your side. Its sounds
    /// ride that path (PathSound): each frame they sit where the sound now reaching the ear left
    /// the aircraft, pitched by the Doppler shift of that moment, so they arrive late with
    /// distance and rise as it comes at you, like the shells' whistles.
    ///
    /// The 30 mm gun run (A-10, GAU-8): Gun30Burst seconds at Gun30RateOfFire from
    /// Gun30FireRange out, the aim walked Gun30Walk metres through the mark. Every round is a
    /// FruitLib projectile led for drop and drag (Lead); HEI rounds (Gun30HEIShare) burst where
    /// they first hit a surface or a body (BombsAway.Gun30), the others are armour-piercing and
    /// go on through what they can. The rounds outrun their own sound, so the hits walk across
    /// the mark before the "brrrt" arrives.
    ///
    /// The 20 mm gun run (F-22, M61A2) is the same attack from a different profile (Gun, For):
    /// a faster jet in a shallower dive, 6,000 rounds a minute in a shorter burst, a tighter
    /// line of smaller PGU-28/B hits (BombsAway.Gun20), a higher-pitched burp.
    ///
    /// The JDAMs (AirStrike.Bombs.cs): a strike jet high overhead releases one guided bomb that
    /// steers itself down onto the mark.
    ///
    /// The MOAB and the CBU-87 come the JDAMs' way; the CBU's bomblets and its live duds are
    /// AirStrike.Cluster.cs. The MOAB's carrier, the MC-130J with its ramp, drogue and cradle,
    /// is AirStrike.Carrier.cs.
    ///
    /// The rockets (AirStrike.Rockets.cs): an AH-64 in a shallow running dive ripples Hydra 70
    /// rockets in pairs, M151 HE bursts walked through the mark or M255A1 flechettes that throw
    /// their darts short of it.
    /// </summary>
    internal static partial class AirStrike
    {
        private const float SpeedOfSound = 343f;
        private const string Fuzed = "BombsAway.Fuzed";
        private const float DiveTime = 5f;     // seconds of dive before it opens fire
        private const float Approach = 14f;    // seconds of level flight before that, coming in from far off
        private const float PitchOver = 1.6f;  // seconds to pitch from level into the dive
        private const float ClimbAngle = 30f;
        private const float TurnAway = 70f;    // degrees it turns off its heading, away from you
        private const float AfterTime = 16f;   // seconds flown after the pull-off begins, then it's gone
        private const float SampleDt = 0.05f;
        private const float GunCd = 0.3f;      // as the round's spec, for the lead

        /// <summary>What a gun's round is, on its FruitLib projectile (Projectile.Tag).</summary>
        private sealed class RoundTag { public string Kind; public bool Hei; }
        private static readonly RoundTag Hei30 = new RoundTag { Kind = "Gun30", Hei = true }, Ap30 = new RoundTag { Kind = "Gun30" };
        private static readonly RoundTag Hei20 = new RoundTag { Kind = "Gun20", Hei = true }, Ap20 = new RoundTag { Kind = "Gun20" };

        // ── The guns ────────────────────────────────────────────────────────────

        private enum Airframe { A10, F22, F15, AH64, C130 }

        /// <summary>Everything that differs between the gun runs, read from Config at each call.</summary>
        private sealed class Gun
        {
            public FireMissionType Type;
            public Airframe Craft;
            public string Code, Name, Sign, Callsign, Kind;   // Kind: the burst's spec, BombsAway.<Kind>
            public string HeiId, ApId;
            public RoundTag HeiTag, ApTag;
            public float HeiGrams, ApGrams, CaliberMm, ApPenetration;
            public float Burst, Rate, Speed, Dive, Range, Walk, Dispersion, HeiShare, V0, PullG;
            public Vector3 Muzzle;                            // from the aircraft's middle: right, up, forward (m)
            public string EngineKey, GunKey, TailKey;
            public float SmokeStart, SmokeEnd, FlashSize;
        }

        private static Gun For(FireMissionType t)
        {
            if (t == FireMissionType.Gun20)
                return new Gun
                {
                    Type = t, Craft = Airframe.F22, Code = "20", Name = "20MM GUN RUN", Sign = "RAPTOR", Callsign = "Raptor 21", Kind = "Gun20",
                    HeiId = "BombsAway.Gun20HEI", ApId = "BombsAway.Gun20AP", HeiTag = Hei20, ApTag = Ap20,
                    HeiGrams = 102f, ApGrams = 100f, CaliberMm = 20f, ApPenetration = 3f,          // PGU-28/B, PGU-20/U
                    Burst = Mathf.Clamp(Config.Gun20Burst, 0.3f, 4f), Rate = Mathf.Clamp(Config.Gun20RateOfFire, 60f, 7000f) / 60f,
                    Speed = Mathf.Max(80f, Config.Gun20Speed), Dive = Mathf.Clamp(Config.Gun20DiveAngle, 5f, 60f),
                    Range = Mathf.Max(300f, Config.Gun20FireRange), Walk = Mathf.Max(0f, Config.Gun20Walk),
                    Dispersion = Mathf.Max(0f, Config.Gun20Dispersion), HeiShare = Mathf.Clamp01(Config.Gun20HEIShare),
                    V0 = Mathf.Max(200f, Config.Gun20MuzzleVelocity), PullG = 6f,
                    Muzzle = new Vector3(1.56f, 0.62f, 3.96f),                                       // over the right intake (the F22 model's Muzzle)
                    EngineKey = "JetF22Loop", GunKey = "Gun20Loop", TailKey = "Gun20Tail",
                    SmokeStart = 0.5f, SmokeEnd = 6f, FlashSize = 0.6f,
                };
            return new Gun
            {
                Type = FireMissionType.Gun30, Craft = Airframe.A10, Code = "30", Name = "30MM GUN RUN", Sign = "HOG", Callsign = "Hog 11", Kind = "Gun30",
                HeiId = "BombsAway.Gun30HEI", ApId = "BombsAway.Gun30AP", HeiTag = Hei30, ApTag = Ap30,
                HeiGrams = 378f, ApGrams = 425f, CaliberMm = 30f, ApPenetration = 4f,              // PGU-13/B, PGU-14/B
                Burst = Mathf.Clamp(Config.Gun30Burst, 0.3f, 6f), Rate = Mathf.Clamp(Config.Gun30RateOfFire, 60f, 6000f) / 60f,
                Speed = Mathf.Max(60f, Config.Gun30Speed), Dive = Mathf.Clamp(Config.Gun30DiveAngle, 5f, 60f),
                Range = Mathf.Max(300f, Config.Gun30FireRange), Walk = Mathf.Max(0f, Config.Gun30Walk),
                Dispersion = Mathf.Max(0f, Config.Gun30Dispersion), HeiShare = Mathf.Clamp01(Config.Gun30HEIShare),
                V0 = Mathf.Max(200f, Config.Gun30MuzzleVelocity), PullG = 4f,
                Muzzle = new Vector3(0f, -0.5f, 10.7f),                                              // the barrels' mouth under the nose (the A10 model's Muzzle)
                EngineKey = "JetA10Loop", GunKey = "Gun30Loop", TailKey = "Gun30Tail",
                SmokeStart = 0.8f, SmokeEnd = 9f, FlashSize = 1f,
            };
        }

        // ── The flight path ─────────────────────────────────────────────────────

        /// <summary>The aircraft's whole flight, sampled every SampleDt from T0, at one speed.</summary>
        private sealed class Path
        {
            public float T0, Speed;
            public readonly List<Vector3> Pos = new List<Vector3>(), Fwd = new List<Vector3>(), Up = new List<Vector3>();
            public float End => T0 + (Pos.Count - 1) * SampleDt;

            /// <summary>Its speed at <paramref name="t"/>, from the samples (a falling bomb's grows).</summary>
            public float SpeedAt(float t)
            {
                int last = Pos.Count - 1;
                if (last < 1 || t <= T0 || t >= End) return Speed;
                int i = Mathf.Clamp(Mathf.FloorToInt((t - T0) / SampleDt), 0, last - 1);
                return Vector3.Distance(Pos[i], Pos[i + 1]) / SampleDt;
            }

            /// <summary>Where it is at <paramref name="t"/>; before or after the path, carried on in a straight line.</summary>
            public void Sample(float t, out Vector3 pos, out Vector3 fwd, out Vector3 up)
            {
                int last = Pos.Count - 1;
                if (t <= T0) { fwd = Fwd[0]; up = Up[0]; pos = Pos[0] - fwd * Speed * (T0 - t); return; }
                if (t >= End) { fwd = Fwd[last]; up = Up[last]; pos = Pos[last] + fwd * Speed * (t - End); return; }
                float f = (t - T0) / SampleDt;
                int i = Mathf.Min(last - 1, Mathf.FloorToInt(f));
                float u = f - i;
                pos = Vector3.Lerp(Pos[i], Pos[i + 1], u);
                fwd = Vector3.Slerp(Fwd[i], Fwd[i + 1], u).normalized;
                up = Vector3.Slerp(Up[i], Up[i + 1], u).normalized;
            }

            public Vector3 PosAt(float t) { Sample(t, out Vector3 p, out _, out _); return p; }
        }

        /// <summary>
        /// How a run comes in and goes out, past the dive itself (5.25.0, AirStrike.Approach.cs).
        /// Null: straight in along the run-in heading, and a break away before the mark.
        /// </summary>
        private sealed class RunShape
        {
            public float EntryTurn;            // degrees it turns onto its run-in, signed (+ = clockwise from above); 0 = straight in
            public float EntryG = 3f;          // the entry turn's load factor
            public bool Overfly;               // after the attack: recover, fly on over the mark, turn off past it
            public Vector3 Mark;
            public float OverflyPast = 700f;   // metres past the mark (along the run) before it turns off
            public float OverflyClimb = 8f;    // degrees it climbs out at while it overflies
            public float OverflyG = 2.5f;      // the turn-off's load factor
        }

        private const float FinalStraight = 3f;   // seconds level on the run-in heading between the entry turn and the dive

        /// <summary>Seconds the run flies before <paramref name="fireAt"/>: the dive, the run-in, the entry turn and a leg before it.</summary>
        private static float BackTime(float speed, RunShape shape)
        {
            if (shape == null || Mathf.Abs(shape.EntryTurn) < 1f) return DiveTime + Approach;
            return DiveTime + Mathf.Max(Approach, PitchOver + FinalStraight + TurnSeconds(shape.EntryTurn, shape.EntryG, speed) + 3f);
        }

        /// <summary>A level turn through <paramref name="deg"/> at load factor <paramref name="g"/>: ω = g √(n² − 1) / v.</summary>
        private static float TurnRate(float g, float speed) => 9.81f * Mathf.Sqrt(Mathf.Max(0.2f, g * g - 1f)) / Mathf.Max(20f, speed);
        private static float TurnSeconds(float deg, float g, float speed) => Mathf.Abs(deg) * Mathf.Deg2Rad / TurnRate(g, speed);

        /// <summary>
        /// A run that is at <paramref name="m0"/> heading down <paramref name="dive"/> at
        /// <paramref name="fireAt"/>: worked back from there (the dive, pitched over smoothly
        /// from level flight along <paramref name="level"/>, and before that the entry turn onto
        /// it, <paramref name="shape"/>), and forward (the dive until <paramref name="pullAt"/>,
        /// then a pull up into a climbing turn toward <paramref name="away"/>; or, overflying, a
        /// pull up to a shallow climb on over the mark and the turn toward <paramref name="away"/>
        /// once it's past).
        /// </summary>
        private static Path BuildRun(Vector3 m0, Vector3 dive, Vector3 level, float speed, float fireAt, float pullAt, Vector3 away, float pullG,
                                     float climbDeg = ClimbAngle, RunShape shape = null)
        {
            var path = new Path { Speed = speed };
            float divesAt = fireAt - DiveTime;
            float entry = shape != null ? shape.EntryTurn : 0f;
            float turnEnd = divesAt - PitchOver * 0.5f - FinalStraight;          // the entry turn ends here, on the run-in heading
            float omega = shape != null ? TurnRate(shape.EntryG, speed) * Mathf.Rad2Deg : 0f;   // deg/s

            // Backward from the firing point.
            var back = new List<Vector3>();
            var backDir = new List<Vector3>();
            Vector3 p = m0;
            int nBack = Mathf.CeilToInt(BackTime(speed, shape) / SampleDt);
            for (int i = 0; i <= nBack; i++)
            {
                float t = fireAt - i * SampleDt;
                float u = Mathf.Clamp01((divesAt + PitchOver * 0.5f - t) / PitchOver);
                Vector3 lv = level;
                if (Mathf.Abs(entry) >= 1f && t < turnEnd)
                    lv = Quaternion.AngleAxis(-Mathf.Sign(entry) * Mathf.Min(Mathf.Abs(entry), omega * (turnEnd - t)), Vector3.up) * level;   // before the turn it flew this way
                Vector3 d = Vector3.Slerp(dive, lv, u * u * (3f - 2f * u)).normalized;
                back.Add(p); backDir.Add(d);
                p -= d * speed * SampleDt;
            }
            for (int i = back.Count - 1; i >= 1; i--) { path.Pos.Add(back[i]); path.Fwd.Add(backDir[i]); }
            path.T0 = fireAt - (back.Count - 1) * SampleDt;

            // Forward: the dive, then the pull-off.
            Vector3 climb = (away * Mathf.Cos(climbDeg * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(climbDeg * Mathf.Deg2Rad)).normalized;
            float rate = pullG * 9.81f / Mathf.Max(30f, speed);   // rad/s
            p = m0;
            Vector3 dir = dive;
            if (shape != null && shape.Overfly)
            {
                // Recover to a shallow climb along the run, over the mark, then turn off once past it.
                Vector3 run = Vector3.ProjectOnPlane(level, Vector3.up).normalized;
                float oc = shape.OverflyClimb * Mathf.Deg2Rad;
                Vector3 recover = (run * Mathf.Cos(oc) + Vector3.up * Mathf.Sin(oc)).normalized;
                float turnRate = shape.OverflyG * 9.81f / Mathf.Max(30f, speed);
                float turningFor = 0f;
                for (int i = 0; i <= Mathf.CeilToInt(90f / SampleDt); i++)
                {
                    float t = fireAt + i * SampleDt;
                    path.Pos.Add(p); path.Fwd.Add(dir);
                    bool past = Vector3.Dot(p - shape.Mark, run) > shape.OverflyPast;
                    if (past) { dir = Vector3.RotateTowards(dir, climb, turnRate * SampleDt, 0f).normalized; turningFor += SampleDt; }
                    else if (t >= pullAt) dir = Vector3.RotateTowards(dir, recover, rate * SampleDt, 0f).normalized;
                    if (turningFor > AfterTime * 0.75f) break;
                    p += dir * speed * SampleDt;
                }
            }
            else
            {
                int nFwd = Mathf.CeilToInt((pullAt - fireAt + AfterTime) / SampleDt);
                for (int i = 0; i <= nFwd; i++)
                {
                    float t = fireAt + i * SampleDt;
                    path.Pos.Add(p); path.Fwd.Add(dir);
                    if (t >= pullAt) dir = Vector3.RotateTowards(dir, climb, rate * SampleDt, 0f).normalized;
                    p += dir * speed * SampleDt;
                }
            }

            // Banked into its turns: lift along what it accelerates by, plus what holds it up.
            int n = path.Pos.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = (path.Fwd[Mathf.Min(n - 1, i + 1)] - path.Fwd[Mathf.Max(0, i - 1)]) / (2f * SampleDt) * speed;
                Vector3 lift = a + Vector3.up * 9.81f;
                Vector3 f = path.Fwd[i];
                Vector3 up = lift - Vector3.Dot(lift, f) * f;
                path.Up.Add(up.sqrMagnitude > 1e-4f ? up.normalized : Vector3.up);
            }
            return path;
        }

        /// <summary>
        /// When the sound now reaching <paramref name="ear"/> left the aircraft: c (now - te) =
        /// |ear - P(te)|. Slower than sound, the left side falls faster than the right as te
        /// grows, so there is one answer; bisected.
        /// </summary>
        private static float Emitted(Path path, Vector3 ear, float now)
        {
            float lo = now - 40f, hi = now;
            for (int i = 0; i < 28; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (SpeedOfSound * (now - mid) > Vector3.Distance(ear, path.PosAt(mid))) lo = mid; else hi = mid;
            }
            return 0.5f * (lo + hi);
        }

        // ── Sounds that ride the aircraft ───────────────────────────────────────

        /// <summary>A looping sound sent out along the path between From and To, heard where and when it left.</summary>
        private sealed class PathSound
        {
            public string Key, Tail;
            public float From, To, FadeIn, FadeOut, Volume = 1f, Ahead;
            public GameObject Carrier;
            public AudioSource Src;
            public float BasePitch, BaseVolume;
            public bool Done;
        }

        private static void TickSound(PathSound s, Path path, Vector3 ear, float now)
        {
            if (s == null || s.Done) return;
            float te = Emitted(path, ear, now);
            if (te < s.From) return;                                   // not here yet
            if (te > s.To)
            {
                // The last of it has arrived: its tail (the echo after the gun stops), from there.
                if (s.Tail != null && s.Carrier != null) Sfx.Play(s.Tail, s.Carrier.transform.position, null, s.Volume);
                DropSound(s);
                s.Done = true;
                return;
            }
            path.Sample(te, out Vector3 pos, out Vector3 fwd, out _);
            pos += fwd * s.Ahead;
            if (s.Carrier == null)
            {
                s.Carrier = new GameObject("BA_AirSound_" + s.Key);
                s.Carrier.transform.position = pos;
                s.Src = Sfx.Play(s.Key, pos, s.Carrier.transform, s.Volume);
                if (s.Src == null) { DropSound(s); s.Done = true; return; }
                s.BasePitch = s.Src.pitch;
                s.BaseVolume = s.Src.volume;
            }
            s.Carrier.transform.position = pos;
            if (s.Src == null) return;

            Vector3 toEar = ear - pos;
            float closing = toEar.sqrMagnitude > 1e-4f ? path.SpeedAt(te) * Vector3.Dot(fwd, toEar.normalized) : 0f;
            s.Src.pitch = Mathf.Clamp(s.BasePitch * SpeedOfSound / Mathf.Max(30f, SpeedOfSound - closing), 0.1f, 3f);
            float fade = Mathf.Min(1f, s.FadeIn > 0f ? (te - s.From) / s.FadeIn : 1f, s.FadeOut > 0f ? (s.To - te) / s.FadeOut : 1f);
            s.Src.volume = s.BaseVolume * Mathf.Clamp01(fade);
        }

        private static void DropSound(PathSound s)
        {
            if (s == null) return;
            if (s.Carrier != null) Object.Destroy(s.Carrier);
            s.Carrier = null;
            s.Src = null;
        }

        // ── Strikes ─────────────────────────────────────────────────────────────

        /// <summary>A moment on the net: the radio's words (Text, null for none), or on the terminal its script's Steps.</summary>
        private sealed class Line { public float At; public bool Obs; public string Text; public string[] Steps; }

        private sealed class Strike
        {
            public int Number;
            public FireMissionType Type;
            public Gun G;                         // a gun run's; null for a bomb
            public Bomb B;                        // a bomb drop's; null for a gun run
            public RocketRun R;                   // a rocket attack's
            public Hercules H;                    // the C-130's ramp, props, drogue and cradle (the MOAB's carrier)
            public string Sign;                   // on the net: HOG, RAPTOR, EAGLE, GUNFIGHTER, HERC
            public Airframe CraftKind;
            public Vector3 Mark, Heading, Aim0, Aim1;
            public float FireAt, BurstEnd, InAt, CompleteAt;
            public Path Path;
            public GameObject Craft;
            public Transform Muzzle, Rotor, TailRotor;
            public Transform Barrels, GunDoor;    // the model's GAU-8 cluster / M61 door (5f)
            public Quaternion BarrelsRest, DoorRest;
            public Quaternion RotorRest = Quaternion.identity, TailRotorRest = Quaternion.identity;   // the AH64 model's rotors at rest
            public float Spin, SpinRate;
            public TrailRenderer GunSmoke;
            public PathSound Engine, Gun;
            public int Fired, Total;
            public float Rate;
            public readonly List<Line> Radio = new List<Line>();
            public int RadioNext;
            public TermSession Term;              // on the terminal (AirTerminal) instead of the radio net
            public GameObject PlanDraw;           // DebugDrawAirPlan's lines
            public bool Complete;
        }

        private sealed class Fuze { public string Kind; public Vector3 At, Normal; }

        private static readonly List<Strike> _strikes = new List<Strike>();
        private static readonly List<Fuze> _fuzes = new List<Fuze>();
        private static bool _hooked;
        private static int _hitFx;
        private static Material _smokeMat, _flashMat;

        public static string Code(FireMissionType t) => IsBomb(t) ? BombFor(t).Code : IsRocket(t) ? RocketFor(t).Code : For(t).Code;
        public static string Name(FireMissionType t) => IsBomb(t) ? BombFor(t).Name : IsRocket(t) ? RocketFor(t).Name : For(t).Name;
        public static string Describe(FireMissionType t)
        {
            if (IsBomb(t)) { var b = BombFor(t); return $"{b.Name}  {b.Gbu}"; }
            if (IsRocket(t)) { var r = RocketFor(t); return $"{r.Name}  {r.Count} RKT"; }
            var g = For(t); return $"{g.Name}  {g.Burst:0.0}S";
        }

        public static bool Running(FireMissionType t) => _strikes.Exists(s => !s.Complete && s.Type == t);
        private static bool Busy => _strikes.Exists(s => !s.Complete);

        /// <summary>The newest running strike's mark, if it is newer than mission <paramref name="newerThan"/>.</summary>
        public static bool TryMark(int newerThan, out Vector3 mark, out string status)
        {
            mark = default; status = null;
            Strike s = null;
            for (int i = _strikes.Count - 1; i >= 0; i--) if (!_strikes[i].Complete) { s = _strikes[i]; break; }
            if (s == null || s.Number < newerThan) return false;
            mark = s.Mark;
            float now = Time.time;
            string head = $"MSN {s.Number:00} {Code(s.Type)}";
            if (s.B != null) { status = BombStatus(s, head, now); return true; }
            if (now < s.InAt) status = $"{head} CALL";
            else if (now < s.FireAt) status = $"{head} IN {Mathf.CeilToInt(s.FireAt - now):00}";
            else if (now < s.BurstEnd) status = $"{head} HOT";
            else status = $"{head} OFF";
            return true;
        }

        /// <summary>A gun run's hit may draw its burst effect (every Gun30FxEvery-th / Gun20FxEvery-th).</summary>
        public static bool TakeHitFx(string kind) => _hitFx++ % Mathf.Max(1, kind == "Gun20" ? Config.Gun20FxEvery : Config.Gun30FxEvery) == 0;

        // ── Calling ─────────────────────────────────────────────────────────────

        public static bool Call(FireMissionType type, Vector3 mark, Vector3 observer)
        {
            if (IsBomb(type)) return CallBomb(type, mark, observer);
            if (IsRocket(type)) return CallRockets(type, mark, observer);
            Hook();
            var g = For(type);
            RegisterRounds(g);
            string grid = FireMission.Grid(mark);
            int rounds = Mathf.Max(1, Mathf.RoundToInt(g.Burst * g.Rate));
            if (!Config.ArtyStacking && Busy)
            {
                if (!RefuseOnTerminal(g.Code, grid, g.Callsign, rounds, $"{g.CaliberMm:0}MM", g.Name, CraftName(g.Craft)))
                {
                    RadioLog.Observer($"{g.Callsign}, grid {grid}. Guns. Over.");
                    RadioLog.Unit(g.Sign, "Unable, engaged. Out.");
                }
                return false;
            }

            float now = Time.time;
            bool danger = Vector3.Distance(mark, observer) < Config.AirDangerClose;

            // The approach (AirStrike.Approach.cs): the attack axis and dive that reach the mark,
            // the way in and out; its flight is worked out firing at t = 0, then moved to the clock.
            var plan = PlanGun(g, mark, observer, danger, p =>
            {
                Vector3 dn = Descending(p.Heading, p.Angle);
                Vector3 a0 = mark - p.Heading * g.Walk * 0.5f;
                return BuildRun(a0 - dn * g.Range, dn, p.Heading, g.Speed, 0f, g.Burst + 0.4f, p.Away, g.PullG, ClimbAngle, p.Shape);
            }, out Path path);
            Vector3 heading = plan.Heading;
            int hdg = Mathf.RoundToInt(plan.Az) % 360;

            // On the terminal the jet is spawned where its run begins and the call is made once
            // its program is up: the whole timeline starts from there.
            var term = OpenTerminal(g.Code, danger, mark, path.Pos[0], heading, grid, g.Callsign, rounds, $"{g.CaliberMm:0}MM", g.Name, CraftName(g.Craft));
            SetPlanWords(term, plan, path);
            if (term != null) now += FireTerminal.Play(term, CallSteps(plan));

            var s = new Strike
            {
                Number = FireMission.NextNumber(), Type = g.Type, G = g, Sign = g.Sign, CraftKind = g.Craft, Mark = mark, Heading = heading, Term = term,
                Aim0 = mark - heading * g.Walk * 0.5f, Aim1 = mark + heading * g.Walk * 0.5f,
                FireAt = now + Mathf.Max(12f, Config.AirTimeOnTarget),
                Rate = g.Rate,
            };
            s.BurstEnd = s.FireAt + g.Burst;
            s.Total = Mathf.Max(1, Mathf.RoundToInt(g.Burst * s.Rate));
            s.InAt = s.FireAt - 5f;
            path.T0 += s.FireAt;
            s.Path = path;
            s.PlanDraw = DrawPlan(plan, path, mark);

            s.Engine = new PathSound { Key = g.EngineKey, From = s.Path.T0, To = s.Path.End, FadeIn = 3f, FadeOut = 3f };
            s.Gun = new PathSound { Key = g.GunKey, Tail = g.TailKey, From = s.FireAt, To = s.BurstEnd, FadeIn = 0.04f, FadeOut = 0.03f, Ahead = g.Muzzle.z };

            // The net.
            string offWord = Compass(Vector3.ProjectOnPlane(s.Path.Fwd[s.Path.Fwd.Count - 1], Vector3.up));
            term?.Set("OFF", offWord);
            if (term == null) RadioLog.Observer($"{g.Callsign}, grid {grid}. {(danger ? "Danger close. " : "")}Guns, heading {hdg:000}. Over.");
            Say(s, now + 3f, false, $"Grid {grid}, heading {hdg:000}.{PlanWords(plan, false)}", "readback");
            Say(s, now + 3f, true, "Readback correct.");
            Say(s, s.FireAt - 11f, false, "IP inbound.", "inbound");
            Say(s, s.FireAt - 11f, true, "Continue.");
            Say(s, s.InAt, false, $"In from the {Compass(-heading)}.", "in");
            Say(s, s.InAt, true, "Cleared hot.");
            Say(s, s.FireAt + 0.2f, false, null, "away");
            Say(s, s.BurstEnd + 2.5f, false, $"Off {offWord}.", "off");
            Say(s, s.BurstEnd + 5f, true, "Good hits. End of mission. Out.", "end", "kill", "last", "free");
            s.CompleteAt = s.BurstEnd + 5f;

            _strikes.Add(s);
            LogPlan(s, plan, now);
            return true;
        }

        /// <summary>A moment on the net at <paramref name="at"/>: <paramref name="text"/> on the radio (null: nothing), <paramref name="steps"/> on the terminal.</summary>
        private static void Say(Strike s, float at, bool obs, string text, params string[] steps) =>
            s.Radio.Add(new Line { At = at, Obs = obs, Text = text, Steps = steps != null && steps.Length > 0 ? steps : null });

        /// <summary>"A-10C": the airframe as the terminal names it.</summary>
        private static string CraftName(Airframe a) => a switch
        {
            Airframe.F22 => "F-22A",
            Airframe.F15 => "F-15E",
            Airframe.AH64 => "AH-64E",
            Airframe.C130 => "MC-130J",
            _ => "A-10C",
        };

        /// <summary>
        /// A strike's session on the terminal (AirTerminal), or null for the radio net: the
        /// aircraft spawned at <paramref name="spawn"/> (where its run begins), and the words its
        /// script fills in that are known at the call. The caller plays spawn, boot and call.
        /// </summary>
        private static TermSession OpenTerminal(string unit, bool danger, Vector3 mark, Vector3 spawn, Vector3 heading, string grid, string callsign,
                                                int count, string ammo, string weapon, string craft)
        {
            if (!Config.AirTerminal) return null;
            var t = FireTerminal.Begin(unit, danger);
            if (t == null) return null;
            int hdg = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg, 360f)) % 360;
            Vector3 flat = Vector3.ProjectOnPlane(mark - spawn, Vector3.up);
            int az = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 360f) * 6400f / 360f) % 6400;
            return t.Set("GRID", grid).Set("SPAWN", FireMission.Grid(spawn)).Set("RANGE", flat.magnitude / 1000f).Set("AZ", az.ToString("0000"))
                    .Set("HDG", hdg.ToString("000")).Set("FROM", Compass(-heading)).Set("SIGN", callsign)
                    .Set("N", count).Set("NWORD", FireTerminal.Word(count)).Set("AMMO", ammo).Set("WEAPON", weapon).Set("CRAFT", craft);
        }

        /// <summary>
        /// A call while the air is busy, on the terminal: the program on the line (the newest
        /// running strike's) turns it down. False when it has to go on the radio net.
        /// </summary>
        private static bool RefuseOnTerminal(string unit, string grid, string callsign, int count, string ammo, string weapon, string craft)
        {
            if (!Config.AirTerminal) return false;
            TermSession live = null;
            for (int i = _strikes.Count - 1; i >= 0; i--) if (!_strikes[i].Complete) { live = _strikes[i].Term; break; }
            var call = new Dictionary<string, string>
            {
                ["GRID"] = grid, ["SIGN"] = callsign.ToUpperInvariant(), ["N"] = count.ToString(), ["NWORD"] = FireTerminal.Word(count),
                ["AMMO"] = ammo.ToUpperInvariant(), ["WEAPON"] = weapon.ToUpperInvariant(), ["CRAFT"] = craft,
            };
            return FireTerminal.Refuse(live, unit, call);
        }

        /// <summary>The attack heading: AirAttackHeading, or across the line from you to the mark, either way round.</summary>
        private static Vector3 AttackHeading(Vector3 mark, Vector3 observer)
        {
            float set = Config.AirAttackHeading;
            float deg;
            if (set >= 0f) deg = set;
            else
            {
                Vector3 los = Vector3.ProjectOnPlane(mark - observer, Vector3.up);
                float b = los.sqrMagnitude > 1f ? Mathf.Atan2(los.x, los.z) * Mathf.Rad2Deg : Random.Range(0f, 360f);
                deg = b + (Random.value < 0.5f ? 90f : -90f) + Random.Range(-12f, 12f);
            }
            float r = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        /// <summary>"north", "southwest": a flat direction as the net says it (+Z north, +X east).</summary>
        private static string Compass(Vector3 dir)
        {
            float a = Mathf.Repeat(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 22.5f, 360f);
            string[] words = { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };
            return words[Mathf.Clamp(Mathf.FloorToInt(a / 45f), 0, 7)];
        }

        // ── Per frame ───────────────────────────────────────────────────────────

        public static void Tick()
        {
            float now = Time.time;
            FireFuzes();
            FireBomblets();
            FlushDartDust();
            TickShakes(now);
            if (_strikes.Count == 0) return;
            var cam = Camera.main;
            for (int i = _strikes.Count - 1; i >= 0; i--)
            {
                var s = _strikes[i];
                try
                {
                    TickRadio(s, now);
                    TickCraft(s, now);
                    TickGun(s, now);
                    TickBomb(s, now);
                    TickHercDebris(s, now);
                    TickRockets(s, now);
                    if (cam != null)
                    {
                        Vector3 ear = cam.transform.position;
                        TickSound(s.Engine, s.Path, ear, now);
                        TickSound(s.Gun, s.Path, ear, now);
                        if (s.B != null) TickSound(s.B.Fall, s.B.Path, ear, now);
                    }
                    if (!s.Complete && now >= s.CompleteAt && s.RadioNext >= s.Radio.Count) s.Complete = true;
                }
                catch (System.Exception e)
                {
                    MelonLogger.Warning($"[Air] strike failed: {e.Message}");
                    Drop(s);
                    _strikes.RemoveAt(i);
                    continue;
                }
                // Gone once it has flown off and its last sound has arrived.
                if (s.Complete && now > s.Path.End && s.Engine.Done && (s.Gun == null || s.Gun.Done) && (s.B == null || s.B.Done && (s.B.Fall == null || s.B.Fall.Done))
                    && (s.R == null || s.R.Done))
                {
                    Drop(s);
                    _strikes.RemoveAt(i);
                }
                else if (s.Complete && now > s.Path.End + 30f)
                {
                    Drop(s);
                    _strikes.RemoveAt(i);
                }
            }
        }

        private static void TickRadio(Strike s, float now)
        {
            while (s.RadioNext < s.Radio.Count && now >= s.Radio[s.RadioNext].At)
            {
                var l = s.Radio[s.RadioNext++];
                if (s.Term != null) { if (l.Steps != null) FireTerminal.Play(s.Term, l.Steps); }
                else if (l.Text == null) continue;
                else if (l.Obs) RadioLog.Observer(l.Text);
                else RadioLog.Unit(s.Sign, l.Text);
            }
        }

        private static void TickCraft(Strike s, float now)
        {
            if (now < s.Path.T0 || now > s.Path.End)
            {
                if (s.Craft != null) { Object.Destroy(s.Craft); s.Craft = null; }
                return;
            }
            if (s.Craft == null)
            {
                if (s.CraftKind == Airframe.AH64) BuildAH64(s);
                else if (s.CraftKind == Airframe.F15) BuildF15(s);
                else if (s.CraftKind == Airframe.C130) BuildC130(s);
                else SpawnModel(s, s.CraftKind == Airframe.F22 ? "F22" : "A10");
            }
            s.Path.Sample(now, out Vector3 pos, out Vector3 fwd, out Vector3 up);
            s.Craft.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, up));
            if (s.CraftKind == Airframe.AH64) TickHelo(s);

            bool firing = now >= s.FireAt && now <= s.BurstEnd;
            if (s.GunSmoke != null) s.GunSmoke.emitting = firing;
            if (s.Muzzle != null && s.G != null)
            {
                bool on = firing && Random.value < 0.8f;
                if (s.Muzzle.gameObject.activeSelf != on) s.Muzzle.gameObject.SetActive(on);
                if (on) s.Muzzle.localScale = Vector3.one * (s.G.FlashSize * Random.Range(0.7f, 1.5f));
            }
            TickGunParts(s, now);
            if (s.H != null) TickHerc(s, now);
        }

        // The GAU-8 spins up just before the burst and runs down after it; at the real 560 rpm of
        // the cluster a 60 fps frame would land on its own 7-fold symmetry and look still, so it
        // turns at a readable 1,100 deg/s. The M61's door swings up before the burst and shuts after.
        private const float BarrelSpinMax = 1100f, SpinUp = 0.35f, SpinDown = 0.9f, DoorAngle = 70f, DoorTime = 0.25f;

        private static void TickGunParts(Strike s, float now)
        {
            if (s.Barrels != null)
            {
                float target = now >= s.FireAt - SpinUp && now <= s.BurstEnd ? BarrelSpinMax : 0f;
                float rate = BarrelSpinMax / (target > s.SpinRate ? SpinUp : SpinDown);
                s.SpinRate = Mathf.MoveTowards(s.SpinRate, target, rate * Time.deltaTime);
                s.Spin = (s.Spin + s.SpinRate * Time.deltaTime) % 360f;
                s.Barrels.localRotation = s.BarrelsRest * Quaternion.AngleAxis(s.Spin, Vector3.forward);
            }
            if (s.GunDoor != null)
            {
                float open = Mathf.Clamp01(Mathf.Min((now - (s.FireAt - 0.5f)) / DoorTime, (s.BurstEnd + 0.6f - now) / DoorTime));
                open = 1f - (1f - open) * (1f - open);
                s.GunDoor.localRotation = s.DoorRest * Quaternion.AngleAxis(open * DoorAngle, Vector3.forward);
            }
        }

        private static void TickGun(Strike s, float now)
        {
            if (s.G == null || now < s.FireAt || s.Fired >= s.Total) return;
            int due = Mathf.Min(s.Total, Mathf.FloorToInt((now - s.FireAt) * s.Rate) + 1);
            var g = s.G;
            float sigma = g.Dispersion * (Mathf.PI * 2f / 6400f) / 1.794f;   // 80 % inside the angle
            float r = g.CaliberMm * 0.0005f;
            float k = 0.5f * 1.225f * GunCd * (Mathf.PI * r * r) / (g.HeiGrams * 0.001f);
            for (; s.Fired < due; s.Fired++)
            {
                float tk = s.FireAt + s.Fired / s.Rate;
                s.Path.Sample(tk, out Vector3 pos, out Vector3 fwd, out Vector3 up);
                Vector3 muzzle = pos + Vector3.Cross(up, fwd) * g.Muzzle.x + up * g.Muzzle.y + fwd * g.Muzzle.z;
                Vector3 aim = Vector3.Lerp(s.Aim0, s.Aim1, s.Total > 1 ? s.Fired / (float)(s.Total - 1) : 0.5f);
                float dist = Vector3.Distance(muzzle, aim);
                Vector3 across = Vector3.Cross(fwd, up).normalized;
                aim += (across * Gauss() + up * Gauss()) * (sigma * dist);

                bool hei = Random.value < g.HeiShare;
                var p = FruitBallistics.SpawnProjectile(hei ? g.HeiId : g.ApId, muzzle, Lead(muzzle, aim, g.V0, k));
                if (p != null) p.Tag = hei ? g.HeiTag : g.ApTag;
            }
        }

        private static float Gauss() => Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-6f, Random.value))) * Mathf.Cos(2f * Mathf.PI * Random.value);

        /// <summary>
        /// The direction that puts a round from <paramref name="from"/> on <paramref name="target"/>
        /// at <paramref name="v0"/> under gravity and drag (a = -k|v|v + g, as FruitLib flies it),
        /// the way the aircraft's gunsight does: fly a test round, aim off by its miss, three times.
        /// </summary>
        private static Vector3 Lead(Vector3 from, Vector3 target, float v0, float k)
        {
            Vector3 aimAt = target;
            float dist = Vector3.Distance(from, target);
            if (dist < 1f) return (target - from).normalized;
            Vector3 axis = (target - from) / dist;
            Vector3 g = Physics.gravity;
            for (int it = 0; it < 3; it++)
            {
                Vector3 p = from, v = (aimAt - from).normalized * v0;
                const float dt = 0.01f;
                for (int n = 0; n < 800 && Vector3.Dot(p - from, axis) < dist; n++)
                {
                    v += (-k * v.magnitude * v + g) * dt;
                    p += v * dt;
                }
                aimAt += target - p;
            }
            return (aimAt - from).normalized;
        }

        // ── The rounds ──────────────────────────────────────────────────────────

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            FruitBallistics.SurfaceHit += OnSurfaceHit;
            FruitBallistics.LimbWounded += OnLimbWounded;
        }

        /// <summary>A gun's two rounds (HEI and the dense AP penetrator), from the settings at each call.</summary>
        private static void RegisterRounds(Gun g)
        {
            float push = 40f * g.HeiGrams / 378f;   // the 30 mm's 40, by the round's weight
            var hei = ProjectileSpec.Cartridge(g.HeiId, g.HeiGrams, g.CaliberMm, g.V0, GunCd);
            hei.ExternalForces = false;      // flown as the gunsight leads it
            hei.Lifetime = 5f;
            hei.WorldImpulse = push;
            FruitBallistics.Register(hei);

            var ap = ProjectileSpec.Cartridge(g.ApId, g.ApGrams, g.CaliberMm, g.V0, GunCd);
            ap.ExternalForces = false;
            ap.Lifetime = 5f;
            ap.WorldImpulse = push;
            ap.PenetrationScale = g.ApPenetration;
            FruitBallistics.Register(ap);
        }

        // HEI goes off where it first meets something. FruitLib is mid-step here, so the burst
        // waits for this mod's next frame.
        private static void OnSurfaceHit(SurfaceHitInfo h)
        {
            DudHit(h.Collider);   // a live cluster dud goes off when a round or a dart hits it
            if (h.Projectile != null && ReferenceEquals(h.Projectile.Tag, DartTag)) { DartHit(h); return; }
            Arm(h.Projectile, h.Point + h.Normal * 0.05f, h.Normal);
        }
        private static void OnLimbWounded(WoundInfo w) => Arm(w.Projectile, w.Entry, -w.Direction);

        private static void Arm(Projectile p, Vector3 at, Vector3 normal)
        {
            if (p == null || !(p.Tag is RoundTag tag) || !tag.Hei || p.Has(Fuzed)) return;
            p.Set(Fuzed, true);
            p.Kill();
            _fuzes.Add(new Fuze { Kind = tag.Kind, At = at, Normal = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up });
        }

        private static void FireFuzes()
        {
            if (_fuzes.Count == 0) return;
            foreach (var f in _fuzes)
            {
                try
                {
                    var x = f.Kind == "Gun20" ? ExplosionParams.FromGun20Config(f.At) : ExplosionParams.FromGun30Config(f.At);
                    x.Forward = f.Normal;
                    ExplosionSystem.Detonate(x);
                }
                catch (System.Exception e) { MelonLogger.Warning($"[Air] HEI burst failed: {e.Message}"); }
            }
            _fuzes.Clear();
        }

        // ── The aircraft ───────────────────────────────────────────────

        /// <summary>A 5f model from the bundle (1.2x real), on layer 2 with no colliders.</summary>
        private static GameObject SpawnBare(string prefab)
        {
            var src = OrdnanceModels.Asset(prefab);
            if (src == null) return null;
            var root = Object.Instantiate(src);
            root.name = "BA_" + prefab;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = 2;
                var col = t.GetComponent<Collider>();
                if (col != null) { col.enabled = false; Object.Destroy(col); }
            }
            return root;
        }

        /// <summary>
        /// A gun run's 5f model (<c>A10</c>, <c>F22</c>: parts Barrels / GunDoor, a Muzzle marker),
        /// with its gun smoke and flash at the Muzzle.
        /// </summary>
        private static void SpawnModel(Strike s, string prefab)
        {
            var root = SpawnBare(prefab);
            s.Barrels = root.transform.Find("Barrels");
            s.GunDoor = root.transform.Find("GunDoor");
            if (s.Barrels != null) s.BarrelsRest = s.Barrels.localRotation;
            if (s.GunDoor != null) s.DoorRest = s.GunDoor.localRotation;
            s.Spin = s.SpinRate = 0f;
            GunSmokeAndFlash(s, root.transform, root.transform.Find("Muzzle"));
            s.Craft = root;
        }

        /// <summary>The gun's smoke, trailing back from the muzzle while it fires, and its flash.</summary>
        private static void GunSmokeAndFlash(Strike s, Transform t, Transform marker = null)
        {
            EnsureMats();
            var g = s.G;
            var nose = new GameObject("GunFx");
            nose.layer = 2;
            nose.transform.SetParent(t, false);
            nose.transform.localPosition = marker != null ? marker.localPosition + new Vector3(0f, 0f, 0.3f) : g.Muzzle + new Vector3(0f, 0.25f, 0.8f);
            var tr = nose.AddComponent<TrailRenderer>();
            tr.sharedMaterial = _smokeMat;
            tr.time = 3.5f;
            tr.minVertexDistance = 2f;
            tr.startWidth = g.SmokeStart;
            tr.endWidth = g.SmokeEnd;
            tr.startColor = new Color(0.72f, 0.72f, 0.7f, 0.6f);
            tr.endColor = new Color(0.66f, 0.66f, 0.64f, 0f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.emitting = false;
            s.GunSmoke = tr;
            s.Muzzle = Part(nose.transform, PrimitiveType.Cube, new Vector3(0f, 0f, 0.4f), new Vector3(0.9f, 0.9f, 1.4f), _flashMat);
            s.Muzzle.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.Muzzle.gameObject.SetActive(false);
        }

        private static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.layer = 2;
            var col = go.GetComponent<Collider>();
            if (col != null) { col.enabled = false; Object.Destroy(col); }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
            return go.transform;
        }

        private static void EnsureMats()
        {
            if (_smokeMat != null) return;
            var sprite = Config.FindSpriteShader();
            _smokeMat = new Material(sprite) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            _flashMat = new Material(sprite) { hideFlags = HideFlags.DontUnloadUnusedAsset, color = new Color(1f, 0.86f, 0.5f, 1f) };
        }

        // ── Clearing ────────────────────────────────────────────────────────────

        private static void Drop(Strike s)
        {
            if (s.Craft != null) Object.Destroy(s.Craft);
            s.Craft = null;
            if (s.PlanDraw != null) Object.Destroy(s.PlanDraw);
            s.PlanDraw = null;
            DropSound(s.Engine);
            DropSound(s.Gun);
            if (s.B != null) DropBomb(s.B);
            if (s.R != null) DropRockets(s.R);
            if (s.H != null) DropHerc(s.H);
        }

        /// <summary>RESET BOMBS, map resets and scene changes: strikes in the air are called off.</summary>
        public static void Clear()
        {
            foreach (var s in _strikes) Drop(s);
            _strikes.Clear();
            _fuzes.Clear();
            _shakes.Clear();
            _dartDust.Clear();
            ClearCluster();
        }

        public static void OnScene() { Clear(); _bombBearing = -1f; _lastAirAz = -1f; _dartFx = null; _dartDustPs = null; }
    }
}
