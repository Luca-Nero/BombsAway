# BombsAway!

![Version](https://img.shields.io/github/v/release/Luca-Nero/BombsAway?style=flat-square)
![Game Version](https://img.shields.io/badge/Game-Release-blue?style=flat-square)
[![Ko-fi](https://img.shields.io/badge/Ko--fi-Donate-ff5e5b?style=flat-square&logo=ko-fi&logoColor=white)](https://ko-fi.com/Luca_Nero)

Fully physics-simulated ordnance for FRUKT, each its own item on the inventory's Weapons shelf - put them on your toolbar, left click to use. Throw fragmentation grenades, stick C4 to anything and blow it remotely, cover a corridor with a directional Claymore, put a Javelin anti-tank missile through a rooftop, or point an AT-4 and let it fly. Every blast does real ray-traced fragmentation, overpressure wounding, and camera shake - nothing is faked with a damage sphere.

---

## Features

- **Fragmentation Grenade:** Left click to throw. Fuse-timed, with a ray-traced frag pattern (2000 rays by default) that wounds every limb it actually hits.
    - **Overpressure:** A separate falloff-driven blast wave wounds bodies inside the overpressure radius, independent of shrapnel.
- **C4 Charge:** Left click to place - it sticks to walls, crates, and Bobs. Right click to detonate.
    - **Remote Modes:** Multiple charges fire sequentially (oldest first) by default. Press **F1** (C4 in hand) to switch to simultaneous.
- **Claymore Mine:** Left click to place. Sticky, but self-triggering - anything entering its directional proximity cone sets it off. Three red sightlines on the face show the cone at a glance.
    - **Directional Blast:** High-velocity frag (45 m/s) thrown forward through the cone rather than spherically.
- **Javelin Anti-Tank Missile:** Right click raises the launcher's command launch unit to your eye; hold it on a target to lock, left click to launch. The flight model is built from two aerospace papers on the real FGM-148 - piecewise-linear thrust curve, soft launch at 18° with an ejection impulse, four-phase flight, aerodynamic drag (F = ½ρV²CdA), and Proportional Navigation guidance in the terminal phase.
    - **Two Attack Modes:** TOP ATTACK climbs to 40 m and dives; DIRECT takes a flatter 15 m approach. Press the Javelin's toolbar key again (or **F2**) to switch.
    - **Two Warheads (F3):** HEAT is a directional shaped charge with a narrow cone; HE is a full-sphere burst with a much larger radius.
    - **Command Launch Unit:** A live sight on the launcher's display with a sim-style HUD. **N** cycles DAY / NIGHT / WHOT / BHOT, the mouse wheel (or middle click) switches the narrow and wide fields of view.
    - **Lock-On:** Keep a target in the sight for 3 s to lock - the whole body or a single limb (setting). The lock drops when you lower the launcher, and optionally when the target strays from your view. **B** releases it; **F4** keeps it through several shots.
- **AT-4 Anti-Tank Rocket:** Carried on the shoulder; right click brings the flip-up sights to your eye, left click fires straight where it points - no lock, no guidance, just a tracer and a drop. Every fresh tube comes up in its transport state and is made ready on its own - pin yanked, cocking lever slammed forward, safety flicked off - and its sights spring up the first time you raise it. It fires with a proper backblast out of the rear, then the spent tube gets tossed aside and a fresh one comes up. Shares the Javelin's warheads (F3).
- **HUD Panel:** Shows only what the item in hand needs - remote mode with C4; attack mode, warhead, sight and lock state with the Javelin; warhead and reload with the AT-4. Debug mode adds live in-flight telemetry (flight phase, motor burn/coast, current speed).
- **Adaptive Quality:** Ray, wound, and debris counts scale down automatically under frame pressure and recover once the budget frees up, instead of hitching.
- **QoL Tweaks:** Per-ordnance blast tuning, configurable physics layer masks for blast and world queries, camera shake intensity and VFX intensity sliders, and placement offsets for C4 and Claymore.

## Requirements & Compatibility

- **Prerequisites:** MelonLoader 0.7.2+ Installation. [Check out their Tutorial!](https://melonwiki.xyz/#/)
- **Prerequisites:** [FruitLib](https://github.com/Luca-Nero/FruitLib) 5.2.0+ in your `Mods/` folder - BombsAway uses it for the config menu, HUD, performance monitor, and mesh loading.
- **Compatibility:** No known Incompatabilities.

## Installation

1. Download the latest release from the [Releases page](../../releases/latest).
2. Extract the archive.
3. Drop the contents into your game's `Mods/` directory.

## Controls (Defaults)

Grenade, C4, Claymore, Javelin and AT-4 are separate items on the inventory's Weapons shelf. Put the ones you want on your toolbar, select one, then:

| Input | Action |
|-----|--------|
| Left click | Throw grenade, place C4 or Claymore, launch Javelin, fire AT-4 |
| Right click (C4) | Detonate placed charges |
| F1 (C4) | Toggle sequential / simultaneous remote mode |
| Right click hold (Javelin) | Sight through the CLU; holding on a target locks it |
| Right click hold (AT-4) | Aim through the iron sights |
| Mouse wheel / middle click (Javelin) | Narrow / wide field of view |
| N (Javelin) | Cycle DAY / NIGHT / WHOT / BHOT |
| B (Javelin) | Release lock |
| Its toolbar key again, or F2 (Javelin) | Toggle TOP ATTACK / DIRECT |
| F3 (Javelin, AT-4) | Toggle HEAT / HE warhead |
| F4 (Javelin) | Toggle persistent / standard lock |

## Configuration

`GrenadeConfig.ini` is created next to the DLL on first launch. It is sectioned and documented - Controls, Grenade, C4, Claymore, Missile, Missile HE, Homing, AT-4, Wounds, Effects, Placement, and Debug. The file is rewritten on load, so new fields appear on update without losing your existing values. Everything is also editable live through FruitLib's in-game menu.

Notable knobs: `FragRayCount` (shrapnel density), `MissileNavGain` (N in the PN guidance law), `MissileAscentHeight` / `MissileDirectAscentHeight` (cruise altitudes), `AdaptiveQuality` and `MinQualityScale` (performance floor), and `FragLayerMask` / `WorldLayerMask` if you need to exclude physics layers.

## Known Issues

- **No audio.** Unity's IL2CPP audio import pipeline is stripped in this build, so there is currently no route to creating AudioClips from scratch. Engine limitation, still under investigation.
- **Shrapnel can clip through thin geometry.**

---

## Support & Feedback

Found a bug or have a suggestion? Feel free to open an issue on the [Issues page](../../issues) or catch me on Discord.

If you enjoy my work and want to support future updates, feel free to [buy me a coffee on Ko-fi](https://ko-fi.com/Luca_Nero)!

## License

[AGPL-3.0](LICENSE) © Luca Nero / Game Community
