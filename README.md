# BombsAway!

![Version](https://img.shields.io/github/v/release/Luca-Nero/BombsAway?style=flat-square)
![Game Version](https://img.shields.io/badge/Game-v0.1%2B-blue?style=flat-square)
[![Ko-fi](https://img.shields.io/badge/Ko--fi-Donate-ff5e5b?style=flat-square&logo=ko-fi&logoColor=white)](https://ko-fi.com/Luca_Nero)

Fully physics-simulated ordnance for FRUKT. Throw fragmentation grenades, stick C4 to anything and blow it remotely, cover a corridor with a directional Claymore, or put a Javelin anti-tank missile through a rooftop. Every blast does real ray-traced fragmentation, overpressure wounding, and camera shake - nothing is faked with a damage sphere.

---

## Features

- **Fragmentation Grenade:** Press **G** to throw. Fuse-timed, with a ray-traced frag pattern (2000 rays by default) that wounds every limb it actually hits.
    - **Overpressure:** A separate falloff-driven blast wave wounds bodies inside the overpressure radius, independent of shrapnel.
- **C4 Charge:** Press **H** to place - it sticks to walls, crates, and Bobs. Press **F** to detonate.
    - **Remote Modes:** Multiple charges fire sequentially (oldest first) by default. Press **F1** to switch to simultaneous.
- **Claymore Mine:** Press **J** to place. Sticky, but self-triggering - anything entering its directional proximity cone sets it off. Three red sightlines on the face show the cone at a glance.
    - **Directional Blast:** High-velocity frag (45 m/s) thrown forward through the cone rather than spherically.
- **Javelin Anti-Tank Missile:** Press **E** to launch. The flight model is built from two aerospace papers on the real FGM-148 - piecewise-linear thrust curve, soft launch at 18° with an ejection impulse, four-phase flight, aerodynamic drag (F = ½ρV²CdA), and Proportional Navigation guidance in the terminal phase.
    - **Three Attack Modes (F2):** TOP ATTACK climbs to 40 m and dives; DIRECT takes a flatter 15 m approach; UNGUIDED flies ballistic along your camera forward and needs no lock.
    - **Two Warheads (F3):** HEAT is a directional shaped charge with a narrow cone; HE is a full-sphere burst with a much larger radius.
- **Lock-On System:** Hold **V** to scan, **Q** to confirm the lock, **B** to release. A CLU-style bracket tracks the nearest valid rigidbody in your cone of view.
    - **Persistent Lock (F4):** Hold the lock through multiple shots instead of clearing it after each launch.
- **HUD Panel:** Remote mode, attack mode, warhead, lock mode, lock state, and live in-flight telemetry (flight phase, motor burn/coast, current speed) while a missile is airborne.
- **Adaptive Quality:** Ray, wound, and debris counts scale down automatically under frame pressure and recover once the budget frees up, instead of hitching.
- **QoL Tweaks:** Per-ordnance blast tuning, configurable physics layer masks for blast and world queries, camera shake intensity and VFX intensity sliders, and placement offsets for C4 and Claymore.

## Requirements & Compatibility

- **Prerequisites:** MelonLoader 0.7.2+ Installation. [Check out their Tutorial!](https://melonwiki.xyz/#/)
- **Prerequisites:** [FruitLib](https://github.com/Luca-Nero/FruitLib) in your `Mods/` folder - BombsAway uses it for the config menu, HUD, performance monitor, and mesh loading.
- **Compatibility:** No known Incompatabilities.

## Installation

1. Download the latest release from the [Releases page](../../releases/latest).
2. Extract the archive.
3. Drop the contents into your game's `Mods/` directory.

## Controls (Defaults)

| Key | Action |
|-----|--------|
| G | Throw grenade |
| H | Place C4 |
| J | Place Claymore mine |
| F | Detonate C4 (remote) |
| F1 | Toggle sequential / simultaneous remote mode |
| V (hold) | Scan for missile target |
| Q | Confirm lock |
| B | Release lock |
| E | Launch missile |
| F2 | Cycle attack mode (TOP ATTACK / DIRECT / UNGUIDED) |
| F3 | Toggle HEAT / HE warhead |
| F4 | Toggle persistent / standard lock |

## Configuration

`GrenadeConfig.ini` is created next to the DLL on first launch. It is sectioned and documented - Controls, Grenade, C4, Claymore, Missile, Missile HE, Homing, Wounds, Effects, Placement, and Debug. The file is rewritten on load, so new fields appear on update without losing your existing values. Everything is also editable live through FruitLib's in-game menu.

Notable knobs: `FragRayCount` (shrapnel density), `MissileNavGain` (N in the PN guidance law), `MissileAscentHeight` / `MissileDirectAscentHeight` (cruise altitudes), `AdaptiveQuality` and `MinQualityScale` (performance floor), and `FragLayerMask` / `WorldLayerMask` if you need to exclude physics layers.

## Known Issues

- **No audio.** Unity's IL2CPP audio import pipeline is stripped in this build, so there is currently no route to creating AudioClips from scratch. Engine limitation, still under investigation.
- **Shrapnel can clip through thin geometry.**

---

## Support & Feedback

Found a bug or have a suggestion? Feel free to open an issue on the [Issues page](../../issues) or catch me on Discord.

If you enjoy my work and want to support future updates, feel free to [buy me a coffee on Ko-fi](https://ko-fi.com/Luca_Nero)!

## License

[MIT](LICENSE) © Luca Nero / Game Community
