using FruitLib;

namespace BombsAway
{
    public partial class Core
    {
        // ── HUD ──────────────────────────────────────────────────────────────────

        private static void BuildHud(HudPanel p)
        {
            // C4 remote mode
            string remoteMode = RemoteSequential ? "FIFO" : "SIMULTANEOUS";
            p.Line($"{Config.RemoteToggleKey} | C4 DET:  {remoteMode}");

            // Missile attack mode + lock mode
            string atkMode = MissileAttackMode switch
            {
                AttackMode.Top => "TOP ATTACK",
                AttackMode.Direct => "DIRECT",
                AttackMode.Unguided => "UNGUIDED",
                _ => "?"
            };
            string lockMode = PersistentLock ? "PERSIST" : "STD";
            string warhead = MissileWarheadMode == WarheadMode.HEAT ? "HEAT" : "HE";
            p.Line($"{Config.AttackModeKey}/{Config.WarheadModeKey}/{Config.LockModeKey}  | MISSILE: {atkMode} | {warhead} | {lockMode}");

            if (_lockedTarget != null)
                p.Line("LOCK:    LOCKED", HudPanel.Bad);
            else if (_focusedTarget != null)
                p.Line("LOCK:    TRACKING", HudPanel.Warn);

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
