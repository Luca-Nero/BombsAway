using FruitLib;

namespace BombsAway
{
    public partial class Core
    {
        // ── HUD ──────────────────────────────────────────────────────────────────

        private static void BuildHud(HudPanel p)
        {
            // Only what the slot in hand needs: C4 its remote mode, the missile its settings.
            if (Equipped && Selected == Ordnance.C4)
            {
                string remoteMode = RemoteSequential ? "FIFO" : "SIMULTANEOUS";
                p.Line($"{Config.RemoteToggleKey} | C4 DET:  {remoteMode}");
            }
            else if (Equipped && Selected == Ordnance.Missile)
            {
                string atkMode = MissileAttackMode == AttackMode.Top ? "TOP ATTACK" : "DIRECT";
                string lockMode = PersistentLock ? "PERSIST" : "STD";
                string warhead = MissileWarheadMode == WarheadMode.HEAT ? "HEAT" : "HE";
                p.Line($"{Config.AttackModeKey}/{Config.WarheadModeKey}/{Config.LockModeKey}  | MISSILE: {atkMode} | {warhead} | {lockMode}");

                string view = _cluView switch { CluView.Night => "NIGHT", CluView.WHot => "WHOT", CluView.BHot => "BHOT", _ => "DAY" };
                p.Line($"{Config.CluViewKey}/wheel | CLU: {view} | {(_cluNfov ? "NFOV" : "WFOV")} | {(Config.LockWholeBody ? "BODY" : "LIMB")}");

                if (_lockedTarget != null)
                    p.Line(LockPinned ? "LOCK:    LOCKED  (MISSILE AWAY)" : "LOCK:    LOCKED", HudPanel.Bad);
                else if (_focusedTarget != null)
                    p.Line($"LOCK:    TRACKING {UnityEngine.Mathf.RoundToInt(_lockProgress * 100f)}%", HudPanel.Warn);
            }
            else if (Equipped && Selected == Ordnance.Smoke)
                p.Line($"{Config.WarheadModeKey} | SMOKE: {SmokeColourName}");
            else if (Equipped && Selected == Ordnance.Rocket)
            {
                string warhead = MissileWarheadMode == WarheadMode.HEAT ? "HEAT" : "HE";
                p.Line($"{Config.WarheadModeKey} | AT-4: {warhead}");
                if (!RocketReady) p.Line("RELOADING", HudPanel.Warn);
            }

            if (Config.Dbg1 && Flashbang.Count > 0) p.Line($"FLASH:   {Flashbang.Count} STUNNED", HudPanel.Warn);

            if (Config.Dbg1)
            {
                for (int mi = 0; mi < _missiles.Count; mi++)
                {
                    var ms = _missiles[mi];
                    if (ms.Dead || ms.Obj == null) continue;
                    string phase = ms.Phase switch
                    {
                        0 => "LAUNCH",
                        1 => "CLIMBOUT",
                        2 => "ALT HOLD",
                        3 => "TERMINAL",
                        _ => "???"
                    };
                    float spd = ms.Velocity.magnitude;
                    string motor = ms.MotorTime < Config.MissileFlightMotorTime ? "BRN" : "CST";
                    p.Line($"MSL{mi + 1}:  {phase} {motor} {spd:F0}m/s", HudPanel.Dim);
                }
            }
        }
    }
}
