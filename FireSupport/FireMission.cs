using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>What the binoculars call in (append only: <see cref="FireMission"/> sets the order Q / E step through them).</summary>
    internal enum FireMissionType { Arty155, Mortar81, Smoke155, Illum155, Precision155, Gun30, Gun20, Jdam500, Jdam1000, Jdam2000, Hydra, Flechette, Moab, Cbu87 }

    /// <summary>
    /// Fire missions called from the binoculars' laser rangefinder. Two firing units off the map
    /// answer on the radio (RadioLog) the way a real net runs, compressed: the 155 mm battery
    /// (FDC: HE, smoke, illumination and the guided precision round) and the 81 mm mortar
    /// section (MTR). Each unit has its own bearing, fixed per scene, and runs one mission at a
    /// time unless ArtyStacking; the two can fire at once.
    ///   OBS: FIRE MISSION, the grid, DANGER CLOSE if HE is near you, the rounds. OVER.
    ///   The unit reads it back; ArtyShotDelay after the call it fires: SHOT. The rounds fly
    ///   the type's flight time; ArtySplashWarning before the first lands: SPLASH. Then ROUNDS
    ///   COMPLETE.
    /// Each round lands within the type's volley spread of the first, scattered round the mark
    /// (its dispersion), coming down at its descent angle from its unit's side. A round
    /// whistles over the last ArtyWhistleFlight of its flight: a steady tone riding the shell
    /// down its path, which distance and Doppler shape (Whistle, below). Its shell is drawn for
    /// the last moment of its flight and cuts through smoke. HE goes off where its path meets the
    /// first thing in the way (a roof, a body, the ground); smoke leaves a canister smoking
    /// there (SmokeShells); illumination opens a flare IllumBurstHeight over the ground
    /// (IllumFlares). A round whose path misses the arena falls into the void.
    /// </summary>
    internal static class FireMission
    {
        private const float ShellShown = 0.5f;     // seconds of flight drawn before impact
        private const float CompleteAfter = 2f;    // seconds after the last round: ROUNDS COMPLETE
        private const float SpeedOfSound = 343f;   // as the bangs' delay (ExplosionFx)
        private const float WhistleFadeIn = 0.5f;  // seconds of flight over which a whistle swells in

        private enum Unit { Battery, Mortars }
        private enum Payload { HE155, HE81, Smoke, Illum }

        /// <summary>Everything that differs between mission types, read from Config when a mission is called.</summary>
        private sealed class Profile
        {
            public FireMissionType Type;
            public Unit Unit;
            public Payload Payload;
            public string Code, Name;              // "155" on the strip; "155MM HE" under it
            public string Ammo;                    // on the net: "HE", "smoke", "illum", "Excalibur"
            public string Prefix;                  // before the rounds on the net: "Mortars, ", "Precision, "
            public int Rounds;
            public float Dispersion, Flight, Volley, Descent, Speed;
            public float WhistlePitch = 1f, GunPitch = 1f, GunVolume = 0.55f;
            public Vector3 ShellSize = new Vector3(0.155f, 0.155f, 0.8f);
            public float WakeRadius = 0.9f;
        }

        // Three pages on the strip: ARTY, AIR (guns and rockets), BOMB; Q / E run through all of them in one loop.
        private static readonly FireMissionType[] Order =
            { FireMissionType.Arty155, FireMissionType.Mortar81, FireMissionType.Smoke155, FireMissionType.Illum155, FireMissionType.Precision155,
              FireMissionType.Gun30, FireMissionType.Gun20, FireMissionType.Hydra, FireMissionType.Flechette,
              FireMissionType.Jdam500, FireMissionType.Jdam1000, FireMissionType.Jdam2000, FireMissionType.Cbu87, FireMissionType.Moab };
        private static readonly string[] PageNames = { "ARTY", "AIR", "BOMB" };

        /// <summary>Air strikes (AirStrike) rather than the guns and mortars.</summary>
        public static bool IsAir(FireMissionType t) => t >= FireMissionType.Gun30;

        private static Profile For(FireMissionType t)
        {
            switch (t)
            {
                case FireMissionType.Mortar81:
                    return new Profile
                    {
                        Type = t, Unit = Unit.Mortars, Payload = Payload.HE81, Code = "81", Name = "81MM MORTAR HE",
                        Ammo = "HE", Prefix = "Mortars, ", Rounds = Mathf.Clamp(Config.MortarRounds, 1, 24),
                        Dispersion = Config.MortarDispersion, Flight = Config.MortarFlightTime, Volley = Config.MortarVolleySpread,
                        Descent = Config.MortarDescentAngle, Speed = Config.MortarTerminalSpeed,
                        WhistlePitch = 1.3f, GunPitch = 1.7f, GunVolume = 0.4f,
                        ShellSize = new Vector3(0.081f, 0.081f, 0.48f), WakeRadius = 0.6f,
                    };
                case FireMissionType.Smoke155:
                    return new Profile
                    {
                        Type = t, Unit = Unit.Battery, Payload = Payload.Smoke, Code = "SMK", Name = "155MM SMOKE",
                        Ammo = "smoke", Prefix = "", Rounds = Mathf.Clamp(Config.SmokeShellRounds, 1, 12),
                        Dispersion = Config.SmokeShellDispersion, Flight = Config.ArtyFlightTime, Volley = Config.SmokeShellVolleySpread,
                        Descent = Config.ArtyDescentAngle, Speed = Config.ArtyTerminalSpeed,
                    };
                case FireMissionType.Illum155:
                    return new Profile
                    {
                        Type = t, Unit = Unit.Battery, Payload = Payload.Illum, Code = "ILL", Name = "155MM ILLUM",
                        Ammo = "illum", Prefix = "", Rounds = Mathf.Clamp(Config.IllumRounds, 1, 12),
                        Dispersion = Config.IllumDispersion, Flight = Config.ArtyFlightTime, Volley = Config.IllumVolleySpread,
                        Descent = Config.ArtyDescentAngle, Speed = Config.ArtyTerminalSpeed,
                    };
                case FireMissionType.Precision155:
                    return new Profile
                    {
                        Type = t, Unit = Unit.Battery, Payload = Payload.HE155, Code = "PGM", Name = "155MM PRECISION",
                        Ammo = "Excalibur", Prefix = "Precision, ", Rounds = Mathf.Clamp(Config.PrecisionRounds, 1, 6),
                        Dispersion = Config.PrecisionDispersion, Flight = Config.PrecisionFlightTime, Volley = 2f,
                        Descent = Config.PrecisionDescentAngle, Speed = Config.PrecisionTerminalSpeed,
                        WhistlePitch = 0.9f,
                    };
                default:
                    return new Profile
                    {
                        Type = FireMissionType.Arty155, Unit = Unit.Battery, Payload = Payload.HE155, Code = "155", Name = "155MM HE",
                        Ammo = "HE", Prefix = "", Rounds = Mathf.Clamp(Config.ArtyRounds, 1, 24),
                        Dispersion = Config.ArtyDispersion, Flight = Config.ArtyFlightTime, Volley = Config.ArtyVolleySpread,
                        Descent = Config.ArtyDescentAngle, Speed = Config.ArtyTerminalSpeed,
                    };
            }
        }

        private static string Sign(Unit u) => u == Unit.Mortars ? "MTR" : "FDC";

        private sealed class Round
        {
            public float ImpactAt;
            public Vector3 Aim, Dir;               // where it is aimed; its flight direction (down and away from the unit)
            public bool Predicted, HasTarget, Whistled, Done;
            public Vector3 Target, Normal;         // where its path meets something (illum: where it opens)
            public GameObject Shell;
            public Vector3 LastPos;
        }

        private sealed class Mission
        {
            public int Number;
            public Profile P;
            public Vector3 Mark;
            public string Grid;
            public float CalledAt, AckAt, ShotAt, FirstImpact, LastImpact;
            public bool Acked, Shot, Splashed, Complete;
            public TermSession Term;               // run on the terminal (FireTerminal) instead of the radio net
            public Vector3 Guns;                   // on the terminal: where the unit was spawned
            public readonly List<Round> Shells = new List<Round>();
        }

        /// <summary>
        /// A round's whistle: one looping tone on a carrier moved along the shell's path. Each
        /// frame it is put where the sound now reaching the camera left the shell (the emission
        /// time, solved below), and pitched by the Doppler shift of that moment. Unity's own
        /// distance rolloff does the volume. So it swells as the shell comes in, rises in pitch
        /// the more it heads at you, arrives late with distance, and ends exactly as the bang,
        /// which is late by the same distance, arrives.
        /// </summary>
        private sealed class Whistle
        {
            public Vector3 Target, Dir;
            public float ImpactAt, Speed, Pitch = 1f;
            public GameObject Carrier;
            public AudioSource Src;
            public float BasePitch, BaseVolume;
        }

        private static readonly List<Mission> _missions = new List<Mission>();
        private static readonly List<Whistle> _whistles = new List<Whistle>();
        private static int _count;
        private static readonly float[] _bearing = { -1f, -1f };   // per unit, degrees from +Z, per scene
        private static Material _shellMat, _trailMat;

        public static FireMissionType Type = FireMissionType.Arty155;

        /// <summary>When the type was last stepped and which way (-1 Q, +1 E): the screen lights that key.</summary>
        public static float SwitchedAt = -10f;
        public static int SwitchedDir;

        // ── What the binoculars show ────────────────────────────────────────────

        public static int TypeCount => Order.Length;
        public static int Index => System.Array.IndexOf(Order, Type);
        public static string CodeAt(int i) => IsAir(Order[i]) ? AirStrike.Code(Order[i]) : For(Order[i]).Code;

        /// <summary>
        /// The strip's slots for <paramref name="slots"/>, in its pixels: 23 wide with a 2 gap, a
        /// longer code's slot as wide as it needs (MOAB: 27). A page too full for that (more than
        /// 174 between the keys) gets each code's own width plus a pixel of margin and the outline
        /// either side, with a 1 gap.
        /// </summary>
        public static int[] SlotWidths(System.Collections.Generic.List<int> slots, out int gap, out int total)
        {
            const int Wide = 23, WideGap = 2, MaxTotal = 174;
            int n = slots.Count;
            var w = new int[n];
            gap = WideGap;
            total = (n - 1) * WideGap;
            for (int j = 0; j < n; j++) total += w[j] = Mathf.Max(Wide, CodeWidth(slots[j]));
            if (total <= MaxTotal) return w;
            gap = 1; total = (n - 1) * gap;
            for (int j = 0; j < n; j++) total += w[j] = CodeWidth(slots[j]);
            return w;
        }

        private static int CodeWidth(int i) => CodeAt(i).Length * (PixelFont.GW + 1) - 1 + 4;
        public static FireMissionType TypeAt(int i) => Order[i];

        /// <summary>The strip's pages: 0 ARTY, 1 AIR (guns and rockets), 2 BOMB.</summary>
        public static int PageCount => PageNames.Length;
        public static int PageOf(int i) => AirStrike.IsBomb(Order[i]) ? 2 : IsAir(Order[i]) ? 1 : 0;
        public static string PageName(int page) => PageNames[page];

        /// <summary>The chosen type's full name ("155MM HE").</summary>
        public static string TypeName => IsAir(Type) ? AirStrike.Name(Type) : For(Type).Name;

        /// <summary>Rounds the chosen type fires (an air strike: one pass).</summary>
        public static int Rounds => IsAir(Type) ? 1 : For(Type).Rounds;

        /// <summary>"155MM HE  6 RDS", "30MM GUN  2.0S": the line under the strip.</summary>
        public static string Describe => IsAir(Type) ? AirStrike.Describe(Type) : $"{TypeName}  {Rounds} {(Rounds == 1 ? "RD" : "RDS")}";

        /// <summary>Steps the mission type: -1 back (Q), +1 on (E), wrapping round.</summary>
        public static void Step(int dir)
        {
            int n = Order.Length;
            Type = Order[((Index + dir) % n + n) % n];
            SwitchedAt = Time.unscaledTime;
            SwitchedDir = dir;
        }

        /// <summary>A mission of this type is in the air.</summary>
        public static bool Running(FireMissionType t) => IsAir(t) ? AirStrike.Running(t) : _missions.Exists(m => !m.Complete && m.P.Type == t);

        private static bool Busy(Unit u) => _missions.Exists(m => !m.Complete && m.P.Unit == u);

        /// <summary>The newest running mission's mark, a fire mission's or an air strike's.</summary>
        public static bool TryMark(out Vector3 mark, out string status)
        {
            mark = default; status = null;
            Mission m = null;
            for (int i = _missions.Count - 1; i >= 0; i--) if (!_missions[i].Complete) { m = _missions[i]; break; }
            if (AirStrike.TryMark(m != null ? m.Number : 0, out mark, out status)) return true;
            if (m == null) return false;
            mark = m.Mark;
            float now = Time.time;
            string head = $"MSN {m.Number:00} {m.P.Code}";
            if (!m.Shot) status = $"{head} CALL";
            else if (now < m.FirstImpact) status = $"{head} SPLASH {Mathf.CeilToInt(m.FirstImpact - now):00}";
            else status = $"{head} {(m.P.Payload == Payload.Smoke ? "SMOKE" : m.P.Payload == Payload.Illum ? "ILLUM" : "IMPACT")}";
            return true;
        }

        // ── Calling ─────────────────────────────────────────────────────────────

        /// <summary>The lase is complete: call the chosen mission on <paramref name="mark"/>. False if its unit is busy.</summary>
        public static bool Call(Vector3 mark, Vector3 observer)
        {
            if (IsAir(Type)) return AirStrike.Call(Type, mark, observer);
            var p = For(Type);
            string rds = $"{p.Prefix}{p.Rounds} {(p.Rounds == 1 ? "rd" : "rds")} {p.Ammo}";
            // Without a crew: a terminal spawns the unit (prototype, ArtyTerminal), for each mission
            // the terminal script has a program for (the 155 HE barrage in the default script).
            bool term = Config.ArtyTerminal && TerminalScript.For(p.Code) != null && FireTerminal.Available;
            if (!Config.ArtyStacking && Busy(p.Unit))
            {
                var call = new Dictionary<string, string>
                {
                    ["GRID"] = Grid(mark), ["N"] = p.Rounds.ToString(), ["NWORD"] = FireTerminal.Word(p.Rounds),
                    ["AMMO"] = p.Ammo.ToUpperInvariant(), ["WEAPON"] = p.Name,
                };
                if (!(term && FireTerminal.Refuse(LiveTerm(p.Unit), p.Code, call)))
                {
                    RadioLog.Observer($"Fire mission. Grid {Grid(mark)}. {rds}. Over.");
                    RadioLog.Unit(Sign(p.Unit), "Unable, mission in progress. Out.");
                }
                return false;
            }

            int u = (int)p.Unit;
            if (_bearing[u] < 0f)
            {
                float set = p.Unit == Unit.Mortars ? Config.MortarBatteryHeading : Config.ArtyBatteryHeading;
                _bearing[u] = set >= 0f ? set : Random.Range(0f, 360f);
            }
            float now = Time.time;
            var m = new Mission { Number = NextNumber(), P = p, Mark = mark, Grid = Grid(mark), CalledAt = now };
            bool he = p.Payload == Payload.HE155 || p.Payload == Payload.HE81;
            bool danger = he && Vector3.Distance(mark, observer) < Config.ArtyDangerClose;

            // On the terminal the unit is spawned first, out on the side its rounds come from (the
            // guns 9 to 15 km, the mortars 1.5 to 3), and the call is made once its program is up:
            // the timeline starts there.
            float lead = 0f;
            if (term) m.Term = FireTerminal.Begin(p.Code, danger);
            if (m.Term != null)
            {
                bool mortars = p.Unit == Unit.Mortars;
                float b = _bearing[u] * Mathf.Deg2Rad, range = mortars ? Random.Range(1500f, 3000f) : Random.Range(9000f, 15000f);
                m.Guns = mark + new Vector3(Mathf.Sin(b), 0f, Mathf.Cos(b)) * range;
                int az = Mathf.RoundToInt((_bearing[u] + 180f) % 360f * 6400f / 360f) % 6400;   // guns to the mark, mils
                m.Term.Set("GRID", m.Grid).Set("SPAWN", Grid(m.Guns)).Set("RANGE", range / 1000f).Set("AZ", az.ToString("0000"))
                      .Set("N", p.Rounds).Set("NWORD", FireTerminal.Word(p.Rounds)).Set("AMMO", p.Ammo).Set("WEAPON", p.Name)
                      .Set("CRAFT", mortars ? "M252" : "M777A2").Set("SIGN", Sign(p.Unit));
                lead = FireTerminal.Play(m.Term, "spawn", "boot", "wake", "call");
            }
            m.ShotAt = now + lead + Mathf.Max(1f, Config.ArtyShotDelay);
            m.AckAt = now + lead + Mathf.Max(1f, Config.ArtyShotDelay) * 0.5f;
            m.FirstImpact = m.ShotAt + Mathf.Max(Config.ArtySplashWarning + 0.5f, p.Flight);

            for (int i = 0; i < p.Rounds; i++)
            {
                float lands = m.FirstImpact + (i == 0 ? 0f : Random.Range(0f, Mathf.Max(0f, p.Volley)));
                m.Shells.Add(new Round { ImpactAt = lands, Aim = Scatter(mark, p.Dispersion), Dir = Incoming(_bearing[u], p.Descent) });
                m.LastImpact = Mathf.Max(m.LastImpact, lands);
            }
            _missions.Add(m);

            if (m.Term == null) RadioLog.Observer($"Fire mission. Grid {m.Grid}. {(danger ? "Danger close. " : "")}{rds}. Over.");
            if (Config.Dbg1) MelonLogger.Msg($"[Arty] mission {m.Number} ({p.Code}) at {mark} grid {m.Grid}, {p.Rounds} rounds, bearing {_bearing[u]:F0}, first impact in {m.FirstImpact - now:F1}s");
            return true;
        }

        /// <summary>The terminal session of <paramref name="unit"/>'s newest running mission, if it is on the terminal.</summary>
        private static TermSession LiveTerm(Unit unit)
        {
            for (int i = _missions.Count - 1; i >= 0; i--)
                if (!_missions[i].Complete && _missions[i].P.Unit == unit) return _missions[i].Term;
            return null;
        }

        /// <summary>Missions are numbered in one series, fire missions and air strikes alike.</summary>
        internal static int NextNumber() => ++_count;

        /// <summary>An 8-figure grid: metres east and north on a 100 km square, to 10 m.</summary>
        internal static string Grid(Vector3 p)
        {
            int e = Mathf.FloorToInt((p.x + 50000f) / 10f) % 10000;
            int n = Mathf.FloorToInt((p.z + 50000f) / 10f) % 10000;
            return $"{e:0000} {n:0000}";
        }

        /// <summary>The mark, scattered: a normal spread on the ground plane (σ half the dispersion), its tails cut off.</summary>
        private static Vector3 Scatter(Vector3 mark, float dispersion)
        {
            float sigma = Mathf.Max(0f, dispersion) * 0.5f;
            float r = Mathf.Min(sigma * Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-6f, Random.value))), sigma * 3f);
            float a = Random.Range(0f, Mathf.PI * 2f);
            return mark + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        /// <summary>A round's direction of flight: away from its unit's side, down at about <paramref name="descent"/> degrees.</summary>
        private static Vector3 Incoming(float bearing, float descent)
        {
            float b = (bearing + Random.Range(-3f, 3f)) * Mathf.Deg2Rad;
            float d = Mathf.Clamp(descent + Random.Range(-4f, 4f), 20f, 89f) * Mathf.Deg2Rad;
            Vector3 away = -new Vector3(Mathf.Sin(b), 0f, Mathf.Cos(b));
            return (away * Mathf.Cos(d) + Vector3.down * Mathf.Sin(d)).normalized;
        }

        // ── Per frame ───────────────────────────────────────────────────────────

        public static void Tick()
        {
            float now = Time.time;
            TickWhistles(now);
            AirStrike.Tick();
            SmokeShells.Tick();
            IllumFlares.Tick();
            for (int mi = _missions.Count - 1; mi >= 0; mi--)
            {
                var m = _missions[mi];
                string sign = Sign(m.P.Unit);
                if (!m.Acked && now >= m.AckAt)
                {
                    m.Acked = true;
                    if (m.Term != null) FireTerminal.Play(m.Term, "readback");
                    else RadioLog.Unit(sign, $"Grid {m.Grid}, {m.P.Prefix}{m.P.Rounds} {(m.P.Rounds == 1 ? "rd" : "rds")} {m.P.Ammo}. Out.");
                }
                if (!m.Shot && now >= m.ShotAt)
                {
                    m.Shot = true;
                    if (m.Term != null) FireTerminal.Play(m.Term, "shot");
                    else
                    {
                        RadioLog.Unit(sign, "Shot. Over.");
                        RadioLog.Observer("Shot. Out.");
                    }
                    // The guns (or tubes), far off: each fires as its round's offset says.
                    foreach (var r in m.Shells)
                    {
                        var gun = Sfx.PlayHeld("ArtyGuns", m.P.GunVolume, r.ImpactAt - m.FirstImpact);
                        if (gun != null) gun.pitch *= m.P.GunPitch;
                    }
                }
                if (!m.Splashed && now >= m.FirstImpact - Config.ArtySplashWarning)
                {
                    m.Splashed = true;
                    if (m.Term != null) FireTerminal.Play(m.Term, "splash", "kill", "last", "free");   // and the unit is deleted
                    else
                    {
                        RadioLog.Unit(sign, "Splash. Over.");
                        RadioLog.Observer("Splash. Out.");
                    }
                }

                bool allDone = true;
                foreach (var r in m.Shells)
                {
                    if (r.Done) continue;
                    try { TickRound(m.P, r, now); }
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
                    if (m.Term == null)   // on the terminal the unit was deleted at SPLASH
                    {
                        RadioLog.Unit(sign, "Rounds complete. Over.");
                        RadioLog.Observer(m.P.Payload == Payload.Smoke ? "Smoke on target. End of mission. Out."
                                        : m.P.Payload == Payload.Illum ? "Illumination on target. End of mission. Out."
                                        : "End of mission. Out.");
                    }
                    _missions.RemoveAt(mi);
                }
            }
        }

        private static void TickRound(Profile p, Round r, float now)
        {
            float speed = Mathf.Max(50f, p.Speed);
            float lead = Mathf.Max(ShellShown, Config.ArtyWhistleFlight);

            // Where its path meets the world, found once it is near (bodies move). An illumination
            // round opens high over the ground under its aim instead.
            if (!r.Predicted && now >= r.ImpactAt - lead - 0.1f)
            {
                r.Predicted = true;
                if (p.Payload == Payload.Illum)
                {
                    Vector3 ground = PathHit(r.Aim + Vector3.up * 400f, Vector3.down, 800f, out Vector3 g, out _) ? g : r.Aim;
                    r.Target = ground + Vector3.up * Mathf.Max(10f, Config.IllumBurstHeight);
                    r.Normal = Vector3.up;
                    r.HasTarget = true;
                }
                else
                {
                    r.HasTarget = PathHit(r.Aim - r.Dir * 600f, r.Dir, 900f, out r.Target, out r.Normal);
                    if (!r.HasTarget) { r.Target = r.Aim; r.Normal = Vector3.up; }
                }
            }

            // Its whistle rides the path from here on (TickWhistles), and outlives the round
            // while the last of its sound is still on the way.
            if (r.Predicted && !r.Whistled)
            {
                r.Whistled = true;
                _whistles.Add(new Whistle { Target = r.Target, Dir = r.Dir, ImpactAt = r.ImpactAt, Speed = speed, Pitch = p.WhistlePitch });
            }

            // The shell, for the last moment of its flight.
            if (r.Predicted && now >= r.ImpactAt - ShellShown)
            {
                Vector3 pos = r.Target - r.Dir * speed * Mathf.Max(0f, r.ImpactAt - now);
                if (r.Shell == null) { r.Shell = MakeShell(pos, r.Dir, p.ShellSize); r.LastPos = pos; }
                else r.Shell.transform.position = pos;
                if ((pos - r.LastPos).sqrMagnitude > 1e-4f)
                {
                    try { SmokeCloud.Wake(r.LastPos, pos, p.WakeRadius, 5f); } catch { }
                }
                r.LastPos = pos;
            }

            if (now < r.ImpactAt) return;

            if (p.Payload == Payload.Illum)
            {
                DropShell(r);
                r.Done = true;
                IllumFlares.Open(r.Target);
                return;
            }

            // Down: what is on the last stretch of its path now (something may have moved in).
            bool hit = PathHit(r.Target - r.Dir * 40f, r.Dir, r.HasTarget ? 41f : 0f, out Vector3 at, out Vector3 normal);
            if (!hit && r.HasTarget) { at = r.Target; normal = r.Normal; hit = true; }
            if (r.Shell != null) { try { SmokeCloud.Wake(r.LastPos, at, p.WakeRadius, 5f); } catch { } }
            DropShell(r);
            r.Done = true;
            if (!hit)
            {
                if (Config.Dbg1) MelonLogger.Msg($"[Arty] a round missed the arena near {r.Aim}");
                return;
            }

            switch (p.Payload)
            {
                case Payload.Smoke:
                    SmokeShells.Land(at, normal);
                    break;
                case Payload.HE81:
                {
                    var x = ExplosionParams.FromMortarConfig(at + normal * Config.ArtyBurstLift);
                    x.Forward = normal;
                    ExplosionSystem.Detonate(x);
                    break;
                }
                default:
                {
                    var x = ExplosionParams.FromArtilleryConfig(at + normal * Config.ArtyBurstLift);
                    x.Forward = normal;
                    ExplosionSystem.Detonate(x);
                    break;
                }
            }
        }

        private static void TickWhistles(float now)
        {
            var cam = Camera.main;
            float window = Mathf.Max(0.5f, Config.ArtyWhistleFlight);
            for (int i = _whistles.Count - 1; i >= 0; i--)
            {
                var w = _whistles[i];
                if (cam == null) { DropWhistle(w); _whistles.RemoveAt(i); continue; }
                Vector3 ear = cam.transform.position;
                float s = EmittedBeforeImpact(w, ear, now);
                if (s < 0f) { DropWhistle(w); _whistles.RemoveAt(i); continue; }   // the bang is here
                if (s > window) continue;                                             // not yet

                Vector3 at = w.Target - w.Dir * (w.Speed * s);
                if (w.Carrier == null)
                {
                    w.Carrier = new GameObject("BA_ShellWhistle");
                    w.Carrier.transform.position = at;
                    w.Src = Sfx.Play("ArtyWhistleLoop", at, w.Carrier.transform);
                    if (w.Src == null) { DropWhistle(w); _whistles.RemoveAt(i); continue; }
                    w.BasePitch = w.Src.pitch * w.Pitch;
                    w.BaseVolume = w.Src.volume;
                }
                w.Carrier.transform.position = at;
                if (w.Src == null) continue;

                // Doppler at the moment of emission: the shell's speed toward the ear then.
                Vector3 toEar = ear - at;
                float closing = toEar.sqrMagnitude > 1e-4f ? w.Speed * Vector3.Dot(w.Dir, toEar.normalized) : 0f;
                float doppler = SpeedOfSound / Mathf.Max(30f, SpeedOfSound - closing);
                w.Src.pitch = Mathf.Clamp(w.BasePitch * doppler, 0.1f, 3f);
                w.Src.volume = w.BaseVolume * Mathf.Clamp01((window - s) / WhistleFadeIn);
            }
        }

        /// <summary>
        /// How long before impact the shell sent the sound reaching <paramref name="ear"/> now
        /// (negative: it has landed, as heard). The shell is at Target - Dir * Speed * s at s
        /// seconds before impact; sound sent then travels |ear - there| at the speed of sound,
        /// so c (s - tau) = |R + Dir v s| with R = ear - Target and tau the time left to impact.
        /// Squared, a quadratic in s; for a shell slower than sound the larger root is the one.
        /// </summary>
        private static float EmittedBeforeImpact(Whistle w, Vector3 ear, float now)
        {
            float c = SpeedOfSound, v = Mathf.Min(w.Speed, SpeedOfSound * 0.95f);
            float tau = w.ImpactAt - now;
            Vector3 R = ear - w.Target;
            float rd = Vector3.Dot(R, w.Dir);
            float a = c * c - v * v;
            float b = -2f * (c * c * tau + v * rd);
            float k = c * c * tau * tau - R.sqrMagnitude;
            float disc = Mathf.Max(0f, b * b - 4f * a * k);
            return (-b + Mathf.Sqrt(disc)) / (2f * a);
        }

        private static void DropWhistle(Whistle w)
        {
            if (w.Carrier != null) Object.Destroy(w.Carrier);
            w.Carrier = null;
            w.Src = null;
        }

        /// <summary>The first solid thing along a path (never triggers or ignore-raycast parts).</summary>
        internal static bool PathHit(Vector3 from, Vector3 dir, float length, out Vector3 point, out Vector3 normal)
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

        private static GameObject MakeShell(Vector3 pos, Vector3 dir, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "BA_Shell";
            go.layer = 2;
            var col = go.GetComponent<Collider>();
            if (col != null) { col.enabled = false; Object.Destroy(col); }
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
            go.transform.localScale = size;

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
            tr.startWidth = size.x * 0.9f;
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

        /// <summary>RESET BOMBS, map resets and scene changes: missions in the air are called off, canisters and flares go.</summary>
        public static void Clear()
        {
            foreach (var m in _missions) foreach (var r in m.Shells) DropShell(r);
            _missions.Clear();
            foreach (var w in _whistles) DropWhistle(w);
            _whistles.Clear();
            SmokeShells.Clear();
            IllumFlares.Clear();
            AirStrike.Clear();
            FireTerminal.Clear();
        }

        public static void OnScene()
        {
            Clear();
            _bearing[0] = _bearing[1] = -1f;
            AirStrike.OnScene();
        }
    }
}
