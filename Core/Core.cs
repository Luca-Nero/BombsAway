using FruitLib;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;
using static BombsAway.ExplosionSystem;
using Color = UnityEngine.Color;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

[assembly: MelonInfo(typeof(BombsAway.Core), "BombsAway!", BombsAway.Core.Version, "Luca_Nero")]
[assembly: MelonGame()]

namespace BombsAway
{
    public partial class Core : MelonMod
    {
        public const string Version = "5.0.1";

        private static readonly List<GrenadeState> _grenades = new List<GrenadeState>();
        private static readonly List<HomingMissileState> _missiles = new List<HomingMissileState>();
        public static bool RemoteSequential = true;

        // ── Lock-on state ────────────────────────────────────────────
        private static Rigidbody _focusedTarget;
        private static Rigidbody _lockedTarget;
        public static AttackMode MissileAttackMode = AttackMode.Top;
        public static WarheadMode MissileWarheadMode = WarheadMode.HEAT;
        public static bool PersistentLock = false;

        private static readonly HashSet<KeyCode> _pressedRemoteKeys = new HashSet<KeyCode>();

        internal static FruitMeshLibrary Meshes;

        public override void OnInitializeMelon()
        {
            HarmonyInstance.PatchAll();
            ConfigLoader.Load();
            Meshes = new FruitMeshLibrary(System.Reflection.Assembly.GetExecutingAssembly());
            ExplosionSystem.Init();
            FruitMenu.Register("BombsAway", ConfigLoader.IniPath, typeof(Config));
            FruitHud.Register("BombsAway", BuildHud, order: 10);

            FruitPerfMon.RegisterCounter("BA Ordnance", () => _grenades.Count);
            FruitPerfMon.RegisterCounter("BA Missiles", () => _missiles.Count);
            FruitPerfMon.RegisterCounter("BA VFX", () => VfxRunner.ActiveCount);

            FruitUpdateCheck.Register("BombsAway", Version, "Luca-Nero", "BombsAway");

            LoggerInstance.Msg($"BombsAway v{Version} loaded.");
        }

        public override void OnUpdate()
        {

            if (!FruitMenu.IsInputSuppressed)
            {
                // ── Attack mode toggle (TOP -> DIRECT -> UNGUIDED -> TOP …) ────────
                if (Input.GetKeyDown(Config.AttackModeKey))
                {
                    MissileAttackMode = (AttackMode)(((int)MissileAttackMode + 1) % 3);
                    if (Config.Dbg1) MelonLogger.Msg($"[Missile] Attack mode: {MissileAttackMode}");
                }

                // ── Warhead mode toggle (HEAT <-> HE) ─────────────────────────────
                if (Input.GetKeyDown(Config.WarheadModeKey))
                {
                    MissileWarheadMode = MissileWarheadMode == WarheadMode.HEAT
                        ? WarheadMode.HE : WarheadMode.HEAT;
                    if (Config.Dbg1) MelonLogger.Msg($"[Missile] Warhead: {MissileWarheadMode}");
                }

                // ── Lock mode toggle ─────────────────────────────────────────────
                if (Input.GetKeyDown(Config.LockModeKey))
                {
                    PersistentLock = !PersistentLock;
                    if (Config.Dbg1) MelonLogger.Msg(
                        $"[Missile] Lock mode: {(PersistentLock ? "PERSISTENT" : "STANDARD")}");
                }

                // ── Release lock ─────────────────────────────────────────────────
                if (Input.GetKeyDown(Config.ReleaseLockKey) && _lockedTarget != null)
                {
                    if (Config.Dbg1) MelonLogger.Msg("[Missile] Lock released");
                    _lockedTarget = null;
                    HideLockIndicator();
                }

                if (Input.GetKeyDown(Config.GrenadeKey)) SpawnGrenade();
                if (Input.GetKeyDown(Config.C4Key))      SpawnC4();
                if (Input.GetKeyDown(Config.MineKey))    SpawnMine();

                // ── Lock-on system ───────────────────────────────────────────────
                if (Input.GetKey(Config.EnableLockOn))
                {
                    _focusedTarget = ScanForTarget();
                    if (Input.GetKeyDown(Config.LockOnTarget) && _focusedTarget != null)
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

                if (Input.GetKeyDown(Config.SpawnHomingMissile))
                    TryLaunchMissile();

                // ── Remote mode toggle ───────────────────────────────────────────
                if (Input.GetKeyDown(Config.RemoteToggleKey))
                {
                    RemoteSequential = !RemoteSequential;
                    MelonLogger.Msg($"[Remote] Mode: {(RemoteSequential ? "SEQUENTIAL (oldest first)" : "SIMULTANEOUS (all at once)")}");
                }

                ProcessRemoteDetonation();
            }

            // ── Main ordnance tick —─────—─────—─────—─────—─────—─────—─────—─────
            float dt = Time.deltaTime;
            for (int i = _grenades.Count - 1; i >= 0; i--)
            {
                var g = _grenades[i];
                if (g.Dead) { _grenades.RemoveAt(i); continue; }

                TickGrenade(g, dt);

                if (g.Dead) _grenades.RemoveAt(i);
            }

            // ── Missile tick ─────────────────────────────────────────────
            for (int i = _missiles.Count - 1; i >= 0; i--)
            {
                var m = _missiles[i];
                if (m.Dead) { _missiles.RemoveAt(i); continue; }
                TickMissile(m, dt);
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

        private static void ProcessRemoteDetonation()
        {
            _pressedRemoteKeys.Clear();
            bool anyRemote = false;
            foreach (var g in _grenades)
            {
                if (g.Dead || g.Params.Detonation != DetonationMode.Remote) continue;
                g.RemoteTriggered = false;
                anyRemote = true;
                if (g.Armed && Input.GetKeyDown(g.Params.RemoteKey))
                    _pressedRemoteKeys.Add(g.Params.RemoteKey);
            }
            if (!anyRemote || _pressedRemoteKeys.Count == 0) return;

            foreach (var key in _pressedRemoteKeys)
            {
                if (RemoteSequential)
                {
                    GrenadeState oldest = null;
                    for (int i = 0; i < _grenades.Count; i++)
                    {
                        var g = _grenades[i];
                        if (g.Dead || !g.Armed) continue;
                        if (g.Params.Detonation != DetonationMode.Remote) continue;
                        if (g.Params.RemoteKey != key) continue;
                        if (oldest == null || g.Timer > oldest.Timer) oldest = g;
                    }
                    if (oldest != null)
                    {
                        oldest.RemoteTriggered = true;
                        if (Config.Dbg1) MelonLogger.Msg($"[Remote] Sequential: oldest on key={key}");
                    }
                }
                else
                {
                    int count = 0;
                    foreach (var g in _grenades)
                    {
                        if (g.Dead || !g.Armed) continue;
                        if (g.Params.Detonation != DetonationMode.Remote) continue;
                        if (g.Params.RemoteKey != key) continue;
                        g.RemoteTriggered = true; count++;
                    }
                    if (Config.Dbg1) MelonLogger.Msg($"[Remote] Simultaneous: {count} on key={key}");
                }
            }
        }

        public override void OnLateUpdate()
        {
            CameraFX.Tick(Time.deltaTime);
            VfxRunner.Tick(Time.deltaTime);
        }
    }
}
