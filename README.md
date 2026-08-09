# BombsAway v5.0

For those just joining: this mod adds fully physics-simulated ordnance to FRUKT. Press **G**, throw, duck.

v3.5 built the grenade from the ground up — real fragmentation, real wounds, real chaos. v4.0 is the arsenal expansion. Two new weapons. A guided missile system built from actual engineering papers. A targeting HUD. And remote detonations.

And 5.0 finally brings it to GitHub.

---

## What's New

### C4
Press **H** to place. It's sticky — throw it at a wall, a crate, a Bob, it'll stick. Press **F** to detonate.

Place multiple charges and they fire sequentially by default (oldest first, FIFO). Press **F1** to toggle to simultaneous mode if you need everything to go at once.

### Claymore
Press **J** to place. Sticky like C4, but detonates on its own — anything that walks into its directional 40°×40° proximity cone sets it off. Three red sightlines on the face show the cone at a glance.

### Javelin Anti-Tank Missile
Press **E** to fire (requires lock-on first).

This isn't a heat-seeking "fly at the dot" missile. The flight model is built from two aerospace engineering papers on the real FGM-148 Javelin:
- *Zhang (RPI, 2012)* — Design and Analysis of the Two-Stage FGM-148 Javelin
- *Harris & Slegers (UAH, 2009)* — Performance of a Fire-and-Forget Anti-Tank Missile with a Damaged Wing

What that means in practice:
- **Piecewise-linear thrust curve** approximating real motor telemetry. The missile accelerates through its burn, coasts, then decelerates under aerodynamic drag after motor burnout
- **Soft launch** at 18° pitch with an 12 m/s ejection impulse before the flight motor ignites — you can fire from cover
- **4-phase flight**: soft launch → climb → cruise altitude hold → terminal dive
- **Aerodynamic drag**: F = ½ρV²CdA at sea-level density. It slows down after burnout
- **Proportional Navigation guidance** in the terminal phase (a = N·V_closing·ω_LOS, N=3.5 default), blending to pure-pursuit at close range

**Three attack modes** — cycle with **F2**:
- **TOP ATTACK**: climbs to 40m cruise altitude, holds, then dives steeply onto the target from above
- **DIRECT**: shallow climb to 15m, flatter approach. Faster to target, less overhead clearance needed
- **UNGUIDED**: no lock required. Flies straight along your camera forward, ballistic — this is the only mode you can fire without a target lock

Attack mode is baked at launch — what you see on the HUD is what the missile will do.

**Two warheads** — toggle with **F3**:
- **HEAT**: directional shaped charge, narrower blast cone
- **HE**: full-sphere burst, larger blast radius, no cone gate

### Lock-On System
Hold **V** to scan for targets. A CLU-style bracket appears around the nearest valid rigidbody in your cone of view. Press **Q** to confirm the lock. Press **B** to release it.

**F4** toggles persistent lock — by default the lock clears after each launch so you can re-acquire. Persistent mode holds the lock through multiple shots.

### HUD
Top-left panel shows:
- C4 remote detonation mode (FIFO / SIMULTANEOUS)
- Current attack mode (TOP ATTACK / DIRECT / UNGUIDED), warhead (HEAT / HE), lock mode (PERSIST / STD)
- Lock state (TRACKING / LOCKED)
- In-flight telemetry when a missile is airborne (debug level 1+): flight phase, motor state (BRN/CST), current speed

---

## How to Install
1. Download the MelonLoader Installer and install **v0.7.2-ci.2401** or above (enable nightly builds)
2. Run the game once to let MelonLoader initialise
3. Drag **Grenade.dll** into your `Mods/` folder
4. Run the game — `GrenadeConfig.ini` appears next to the DLL on first launch

## How to Update
1. Delete the old DLL **and its config** (`GrenadeConfig.ini`)
2. Drop in the new DLL
3. Done — the config regenerates with all new fields and defaults

---

## Controls (Defaults)

| Key | Action |
|-----|--------|
| G | Throw grenade |
| H | Place C4 |
| J | Place Claymore mine |
| F | Detonate C4 (remote) |
| F1 | Toggle sequential / simultaneous C4 remote mode |
| V (hold) | Scan for missile target |
| Q | Confirm lock |
| B | Release lock |
| E | Launch missile |
| F2 | Cycle missile attack mode: TOP ATTACK / DIRECT / UNGUIDED |
| F3 | Toggle HEAT / HE warhead |
| F4 | Toggle persistent / standard lock mode |

All keys are remappable in `GrenadeConfig.ini`.

---

## Config

Every parameter is in a sectioned, documented `.ini` file. Highlights:

- `FragRayCount` — shrapnel ray count (default 2000)
- `MissileNavGain` — proportional navigation gain (N in the PN law, default 3.5)
- `MissileAscentHeight` — top-attack cruise altitude in metres (default 40); `MissileDirectAscentHeight` is the equivalent for direct attack (default 15)
- `FragLayerMask` / `WorldLayerMask` — physics layer bitmasks. Set these if you want to exclude certain layers from blast or world collision queries
- `AdaptiveQuality` / `MinQualityScale` — automatically scale ray/wound/debris counts down under load instead of hitching (see Performance below)
- Camera shake, VFX durations, blast radii, wound cone shape — all in there

The file is idempotent: new fields appear automatically on version updates without losing your existing values.

---

## Performance

`FragRayCount` is still the main lever for raw detonation cost, but you shouldn't need to touch it: with `AdaptiveQuality` on (default), the mod watches FruitLib's performance monitor (**F11**) and scales ray count, wound points, and debris count down automatically when the frame budget is under pressure, then scales back up once it recovers. `MinQualityScale` sets the floor (default 0.25 — never drops below a quarter of your configured counts). Turn `AdaptiveQuality` off if you'd rather have a fixed, predictable ray count regardless of load.

The missile system is negligible overhead with one physics cast and a handful of vector ops per frame per in-flight missile.

---

## Known Issues

- **No audio.** Unity's IL2CPP audio import pipeline is stripped in this build. Every path to creating AudioClips from scratch hits a compiler wall. This is a known engine limitation, not something fixable from the mod side. Still investigating.
- **Shrapnel can clip through thin geometry.** Different system, same fundamental problem as v3.5.

---

## What's Next

- Smoke and flashbang
- Native item integration
- Audio (if a route around the IL2CPP wall turns up)

---

Have fun. Try top-attack on a rooftop Bob.

— Luca_Nero
