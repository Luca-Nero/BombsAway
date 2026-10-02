using FruitLib;
using MelonLoader;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using static BombsAway.ExplosionSystem;
using Color = UnityEngine.Color;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

[assembly: MelonInfo(typeof(BombsAway.Core), "BombsAway!", BombsAway.Core.Version, "Luca_Nero")]
[assembly: MelonGame()]
[assembly: MelonOptionalDependencies("FruitLib")]

namespace BombsAway
{
    public partial class Core : MelonMod
    {
        public const string Version = "5.8.0";

        private static readonly List<GrenadeState> _grenades = new List<GrenadeState>();
        private static readonly List<HomingMissileState> _missiles = new List<HomingMissileState>();
        public static bool RemoteSequential = true;

        // ── Lock-on state ────────────────────────────────────────────
        private static Rigidbody _focusedTarget;
        private static Rigidbody _lockedTarget;
        private static CluView _cluView = CluView.Day;
        private static bool _cluNfov;
        public static AttackMode MissileAttackMode = AttackMode.Top;
        public static WarheadMode MissileWarheadMode = WarheadMode.HEAT;
        public static bool PersistentLock = false;

        internal static FruitMeshLibrary Meshes;

        // ── FruitLib dependency ──────────────────────────────────────────────
        // 3.1.0: the first FruitLib with FruitBallistics, which every detonation now goes through.
        private const int LibMajor = 5, LibMinor = 6, LibPatch = 0;
        private bool _active;

        public override void OnInitializeMelon()
        {
            _active = FruitGate.Check("BombsAway", LibMajor, LibMinor, LibPatch);
            if (!_active) return;

            Init();
        }

        public override void OnLateInitializeMelon()
        {
            if (_active) return;
            try { Unregister(FruitGate.FailureReason, silent: true); } catch { }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Init()
        {
            // Before the ini is read, so Reset to Defaults goes back to the code's values.
            FruitMenu.CaptureDefaults(typeof(Config));
            ConfigLoader.Load();
            Meshes = new FruitMeshLibrary(System.Reflection.Assembly.GetExecutingAssembly());
            ExplosionSystem.Init();
            FruitMenu.Register("BombsAway", ConfigLoader.IniPath, typeof(Config), ConfigLoader.Write);
            FruitHud.Register("BombsAway", BuildHud, order: 10);
            RegisterLoadout();

            // World > SCENE: a row of our own, and the map's resets clear our charges too.
            FruitWorldMenu.AddButton("BombsAway.ResetBombs", "Reset Bombs", ClearAllCharges);
            FruitWorldMenu.MapReset += OnMapReset;

            var perf = FruitPerfMon.For("BombsAway");
            perf.Counter("BA Ordnance", () => _grenades.Count);
            perf.Counter("BA Missiles", () => _missiles.Count);
            perf.Counter("BA VFX", () => VfxRunner.ActiveCount);

            FruitUpdateCheck.Register("BombsAway", Version, "Luca-Nero", "BombsAway");

            LoggerInstance.Msg($"BombsAway v{Version} loaded.");
        }

        public override void OnUpdate()
        {
            if (!_active) return;
            UpdateBody();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void UpdateBody()
        {
            ExplosionDebugDraw.Tick();
            TestBench.Tick(!FruitMenu.IsInputSuppressed);
            ScaleProbe.Tick(!FruitMenu.IsInputSuppressed);
            TickChain();
            TickPlacement();
            TickHeld();
            Flashbang.Tick();
            Breeze.Tick();
            TickBinoculars(Time.deltaTime);
            FireMission.Tick();
            RadioLog.Tick();

            if (!FruitMenu.IsInputSuppressed)
            {
                TickLoadout();

                // ── Missile settings: only with the launcher in hand ─────────────
                if (Holding(Ordnance.Missile))
                {
                    // TOP <-> DIR: its key, or pressing the launcher's toolbar key again (Arma style).
                    if (Input.GetKeyDown(Config.AttackModeKey) || MissileSlotRepressed()) { ToggleAttackMode(); Sfx.PlayHeld("CluClick"); }

                    // DAY -> NIGHT -> WHOT -> BHOT on the CLU.
                    if (Input.GetKeyDown(Config.CluViewKey))
                    {
                        _cluView = (CluView)(((int)_cluView + 1) % 4);
                        Sfx.PlayHeld("CluClick");
                        if (Config.Dbg1) MelonLogger.Msg($"[CLU] View: {_cluView}");
                    }

                    if (Input.GetKeyDown(Config.LockModeKey))
                    {
                        PersistentLock = !PersistentLock;
                        if (Config.Dbg1) MelonLogger.Msg(
                            $"[Missile] Lock mode: {(PersistentLock ? "PERSISTENT" : "STANDARD")}");
                    }

                    if (Input.GetKeyDown(Config.ReleaseLockKey) && _lockedTarget != null) BreakLock("released");
                }

                // HEAT <-> HE: the Javelin and the AT-4 share the warhead choice.
                if ((Holding(Ordnance.Missile) || Holding(Ordnance.Rocket)) && Input.GetKeyDown(Config.WarheadModeKey))
                {
                    MissileWarheadMode = MissileWarheadMode == WarheadMode.HEAT
                        ? WarheadMode.HE : WarheadMode.HEAT;
                    if (Config.Dbg1) MelonLogger.Msg($"[Missile] Warhead: {MissileWarheadMode}");
                }
                // The smoke grenade's warhead key is its colour.
                else if (Holding(Ordnance.Smoke) && Input.GetKeyDown(Config.WarheadModeKey))
                    CycleSmokeColour();

                // ── C4 remote mode: only with C4 in hand ─────────────────────────
                if (Holding(Ordnance.C4) && Input.GetKeyDown(Config.RemoteToggleKey))
                {
                    RemoteSequential = !RemoteSequential;
                    MelonLogger.Msg($"[Remote] Mode: {(RemoteSequential ? "SEQUENTIAL (oldest first)" : "SIMULTANEOUS (all at once)")}");
                }

                // ── Seeker: RMB (the launcher up) tracks, locking after LockTime (Missile/Seeker.cs) ──
                TickLockPin();
                if (Holding(Ordnance.Missile)) TickSeeker(Time.deltaTime, SlotScanning);
                else ClearCandidate();

                // The brackets belong to the launcher: with anything else in hand they are hidden,
                // but the lock itself is kept (PersistentLock), so taking it out again shows it.
                // With the CLU in hand its track gates do this job, so they stay hidden too.
                if (!Holding(Ordnance.Missile) || _clu != null)
                {
                    HideLockIndicator();
                    HideFocusIndicator();
                }
                else
                {
                    if (_lockedTarget != null)
                    {
                        try
                        {
                            var go = _lockedTarget.gameObject;
                            if (go == null) { _lockedTarget = null; HideLockIndicator(); }
                            else UpdateLockIndicator(_lockedTarget, true);
                        }
                        catch { _lockedTarget = null; HideLockIndicator(); }
                    }
                    else if (_focusedTarget != null) UpdateLockIndicator(_focusedTarget, false);
                    else                             HideLockIndicator();

                    // Secondary (focus) bracket — shown when re-locking with an existing lock
                    if (_lockedTarget != null && _focusedTarget != null && _focusedTarget != _lockedTarget)
                        UpdateFocusIndicator(_focusedTarget);
                    else
                        HideFocusIndicator();
                }

                ProcessRemoteDetonation();
            }
            else
            {
                // A menu is open, so the whole input block is skipped: hide the brackets here.
                HideLockIndicator();
                HideFocusIndicator();
            }

            // ── Main ordnance tick —─────—─────—─────—─────—─────—─────—─────—─────
            float dt = Time.deltaTime;
            for (int i = _grenades.Count - 1; i >= 0; i--)
            {
                var g = _grenades[i];
                if (g.Dead) { _grenades.RemoveAt(i); continue; }

                // One bad charge must not stop the rest ticking: retire it and carry on.
                try { TickGrenade(g, dt); }
                catch (System.Exception e)
                {
                    MelonLogger.Warning($"[Ordnance] {g.Params.Kind} tick failed, discarding it: {e.Message}");
                    try { if (g.Obj != null) GameObject.Destroy(g.Obj); } catch { }
                    ReleaseMaterials(g.Owned);
                    g.Obj = null;
                    g.Dead = true;
                }

                if (g.Dead) _grenades.RemoveAt(i);
            }

            // ── Missile tick ─────────────────────────────────────────────
            for (int i = _missiles.Count - 1; i >= 0; i--)
            {
                var m = _missiles[i];
                if (m.Dead) { _missiles.RemoveAt(i); continue; }
                Vector3 wakeFrom = m.Obj != null ? m.Obj.transform.position : Vector3.zero;
                bool wasFlying = m.Obj != null;
                try { TickMissile(m, dt); }
                catch (System.Exception e)
                {
                    MelonLogger.Warning($"[Missile] tick failed, discarding it: {e.Message}");
                    try { if (m.Obj != null) GameObject.Destroy(m.Obj); } catch { }
                    ReleaseMaterials(m.Owned);
                    m.Obj = null;
                    m.Dead = true;
                }
                // Its wake through smoke: a tunnel, wider behind a burning motor.
                if (wasFlying)
                {
                    Vector3 wakeTo = m.Obj != null ? m.Obj.transform.position : m.Params != null ? m.Params.Origin : wakeFrom;
                    bool motor = !m.Unguided && m.Phase >= 1 && m.MotorTime < Config.MissileFlightMotorTime;
                    if ((wakeTo - wakeFrom).sqrMagnitude < 60f * 60f)
                    {
                        try { SmokeCloud.Wake(wakeFrom, wakeTo, motor ? 1.3f : 1f, motor ? 6f : 4f); }
                        catch (System.Exception e) { MelonLogger.Warning($"[Smoke] wake failed: {e.Message}"); }
                    }
                }
                if (m.Dead) _missiles.RemoveAt(i);
            }
        }

        private static void TryLaunchMissile()
        {
            Rigidbody launchTarget = _lockedTarget;
            if (launchTarget == null) return;
            // One missile per lock, unless the lock is persistent: this one's is still flying.
            if (LockPinned && !PersistentLock) return;

            float dist = Vector3.Distance(
                Camera.main.transform.position, launchTarget.transform.position);
            if (dist < Config.MissileMinLaunchDist)
            {
                if (Config.Dbg1) MelonLogger.Msg(
                    $"[Missile] Too close ({dist:F1}m < {Config.MissileMinLaunchDist}m)");
                return;
            }

            // The lock stays until this missile is down (Seeker.cs TickLockPin).
            var m = SpawnMissile(launchTarget, _lockedBody);
            if (m != null) _lockMissile = m;
            ClearCandidate();
            HideLockIndicator();
            HideFocusIndicator();
        }

        /// <summary>RMB with C4 in hand: oldest armed charge (FIFO) or all of them (SIMULTANEOUS).</summary>
        private static void ProcessRemoteDetonation()
        {
            bool fire = Holding(Ordnance.C4) && Input.GetMouseButtonDown(1);

            GrenadeState oldest = null;
            int count = 0;
            foreach (var g in _grenades)
            {
                if (g.Dead || g.Params.Detonation != DetonationMode.Remote) continue;
                g.RemoteTriggered = false;
                if (!fire || !g.Armed) continue;

                if (!RemoteSequential) { g.RemoteTriggered = true; count++; }
                else if (oldest == null || g.Timer > oldest.Timer) oldest = g;
            }

            if (oldest != null) { oldest.RemoteTriggered = true; count = 1; }
            if (fire) Sfx.PlayHeld("C4Remote");
            if (count > 0 && Config.Dbg1)
                MelonLogger.Msg($"[Remote] {(RemoteSequential ? "Sequential" : "Simultaneous")}: {count} fired");
        }

        /// <summary>The flashbang holds stunned bodies down every physics tick too: the puppeteer writes there.</summary>
        public override void OnFixedUpdate()
        {
            if (!_active) return;
            Flashbang.Tick();
        }

        public override void OnGUI()
        {
            if (!_active) return;
            DrawFireSupport();
            Flashbang.DrawOverlay();
            Flashbang.DrawDiagnostics();
        }

        public override void OnLateUpdate()
        {
            if (!_active) return;
            LateUpdateBody();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void LateUpdateBody()
        {
            LateTickHeld();
            Flashbang.Tick();
            CameraFX.Tick(Time.deltaTime);
            VfxRunner.Tick(Time.deltaTime);
            ExplosionFx.Tick();
            SmokeCloud.TickAll();   // after the particle systems have run: the cloud writes its puffs over them
            Sfx.Tick();
        }
    }
}
