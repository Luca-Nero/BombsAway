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
[assembly: HarmonyDontPatchAll]

namespace BombsAway
{
    public partial class Core : MelonMod
    {
        public const string Version = "5.4.0";

        private static readonly List<GrenadeState> _grenades = new List<GrenadeState>();
        private static readonly List<HomingMissileState> _missiles = new List<HomingMissileState>();
        public static bool RemoteSequential = true;

        // ── Lock-on state ────────────────────────────────────────────
        private static Rigidbody _focusedTarget;
        private static Rigidbody _lockedTarget;
        public static AttackMode MissileAttackMode = AttackMode.Top;
        public static WarheadMode MissileWarheadMode = WarheadMode.HEAT;
        public static bool PersistentLock = false;

        internal static FruitMeshLibrary Meshes;

        // ── FruitLib dependency ──────────────────────────────────────────────
        // 3.1.0: the first FruitLib with FruitBallistics, which every detonation now goes through.
        private const int LibMajor = 5, LibMinor = 5, LibPatch = 0;
        private bool _active;

        public override void OnInitializeMelon()
        {
            _active = FruitGate.Check("BombsAway", LibMajor, LibMinor, LibPatch);
            if (!_active) return;

            HarmonyInstance.PatchAll();
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
            PlacementProbe.Tick(!FruitMenu.IsInputSuppressed);
            TestBench.Tick(!FruitMenu.IsInputSuppressed);
            TickChain();
            TickPlacement();

            if (!FruitMenu.IsInputSuppressed)
            {
                TickLoadout();

                // ── Missile settings: only with the launcher in hand ─────────────
                if (Holding(Ordnance.Missile))
                {
                    if (Input.GetKeyDown(Config.AttackModeKey))   // TOP -> DIRECT -> UNGUIDED -> TOP
                    {
                        MissileAttackMode = (AttackMode)(((int)MissileAttackMode + 1) % 3);
                        if (Config.Dbg1) MelonLogger.Msg($"[Missile] Attack mode: {MissileAttackMode}");
                    }

                    if (Input.GetKeyDown(Config.WarheadModeKey))  // HEAT <-> HE
                    {
                        MissileWarheadMode = MissileWarheadMode == WarheadMode.HEAT
                            ? WarheadMode.HE : WarheadMode.HEAT;
                        if (Config.Dbg1) MelonLogger.Msg($"[Missile] Warhead: {MissileWarheadMode}");
                    }

                    if (Input.GetKeyDown(Config.LockModeKey))
                    {
                        PersistentLock = !PersistentLock;
                        if (Config.Dbg1) MelonLogger.Msg(
                            $"[Missile] Lock mode: {(PersistentLock ? "PERSISTENT" : "STANDARD")}");
                    }

                    if (Input.GetKeyDown(Config.ReleaseLockKey) && _lockedTarget != null)
                    {
                        if (Config.Dbg1) MelonLogger.Msg("[Missile] Lock released");
                        _lockedTarget = null;
                        HideLockIndicator();
                    }
                }

                // ── C4 remote mode: only with C4 in hand ─────────────────────────
                if (Holding(Ordnance.C4) && Input.GetKeyDown(Config.RemoteToggleKey))
                {
                    RemoteSequential = !RemoteSequential;
                    MelonLogger.Msg($"[Remote] Mode: {(RemoteSequential ? "SEQUENTIAL (oldest first)" : "SIMULTANEOUS (all at once)")}");
                }

                // ── Lock-on: RMB held scans, LMB while scanning locks ────────────
                if (SlotScanning)
                {
                    _focusedTarget = ScanForTarget();
                    if (Input.GetMouseButtonDown(0) && _focusedTarget != null)
                    {
                        _lockedTarget = _focusedTarget;
                        if (Config.Dbg1) MelonLogger.Msg($"[Missile] Locked: '{_lockedTarget.gameObject.name}'");
                    }
                }
                else
                {
                    _focusedTarget = null;
                }

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

                ProcessRemoteDetonation();
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
                try { TickMissile(m, dt); }
                catch (System.Exception e)
                {
                    MelonLogger.Warning($"[Missile] tick failed, discarding it: {e.Message}");
                    try { if (m.Obj != null) GameObject.Destroy(m.Obj); } catch { }
                    ReleaseMaterials(m.Owned);
                    m.Obj = null;
                    m.Dead = true;
                }
                if (m.Dead) _missiles.RemoveAt(i);
            }
        }

        private static void TryLaunchMissile()
        {
            bool canLaunch = _lockedTarget != null || MissileAttackMode == AttackMode.Unguided;
            if (!canLaunch) return;

            Rigidbody launchTarget = MissileAttackMode == AttackMode.Unguided ? null : _lockedTarget;
            if (launchTarget != null)
            {
                float dist = Vector3.Distance(
                    Camera.main.transform.position, launchTarget.transform.position);
                if (dist < Config.MissileMinLaunchDist)
                {
                    if (Config.Dbg1) MelonLogger.Msg(
                        $"[Missile] Too close ({dist:F1}m < {Config.MissileMinLaunchDist}m)");
                    return;
                }
            }

            SpawnMissile(launchTarget);
            if (!PersistentLock) { _lockedTarget = null; _focusedTarget = null; }
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
            if (count > 0 && Config.Dbg1)
                MelonLogger.Msg($"[Remote] {(RemoteSequential ? "Sequential" : "Simultaneous")}: {count} fired");
        }

        public override void OnLateUpdate()
        {
            if (!_active) return;
            LateUpdateBody();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void LateUpdateBody()
        {
            CameraFX.Tick(Time.deltaTime);
            VfxRunner.Tick(Time.deltaTime);
        }
    }
}
