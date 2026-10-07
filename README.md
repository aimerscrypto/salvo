# SALVO — stylized arena shooter

A fast, bright, low-poly-but-soft multiplayer FPS built with **Unity 6 (6000.6) + URP** and
**Netcode for GameObjects**. Inspired by browser arena shooters like venge.io: snappy movement,
readable silhouettes, warm stylized lighting, short drop-in matches.

Every model (Blender Python) and texture (numpy) is generated from source in this repo. Gunfire,
weapon handling, footsteps and impacts are CC0 field recordings from Freesound
(`Tools/SoundSources/CREDITS.md`); UI sounds, music and ambience are synthesized.

## Features

- **Modes:** Free-for-All and Team Deathmatch, 5/8/12-minute matches, automatic match cycling.
- **Maps:** *Outpost* (sun-baked desert outpost: container yard, market, raised plaza, rooftops,
  jump pads) and *Harbor* (whitewashed Aegean fishing town: piers, moored boats, fish-market hall
  with catwalk, domed chapels on terraces — and the sea is lethal). Pick one or let the server
  rotate maps between matches.
- **Multiplayer:** online sessions over **Unity Relay** (quick match, host, join with code),
  **LAN / direct IP**, and **offline practice** with bots. Drop-in/drop-out, bots fill empty slots.
- **Movement:** quake-style ground accel/friction, air strafing, slide, slide-jump, coyote time,
  jump buffering, crouch, jump pads.
- **Weapons:** Viper pistol, Hornet SMG, Ranger rifle, Bulldog pump shotgun, Longbow bolt sniper
  (scoped), Fang knife (backstabs). Spread/bloom, recoil that climbs
  through a burst along a learnable side-to-side pattern and recovers when you stop, a visible
  gun kick and muzzle flip, damage falloff, headshots, magazine and per-shell reloads, procedural
  view-model animation.
- **Hands:** gloved, fully posed hands generated per weapon — fingers wrap the actual grip,
  handguard, pump or foregrip, trigger finger on the trigger, cupped two-hand pistol grip, knife
  fist. The same poses are used in first person, on other players and on the menu showcase.
- **Bots:** NavMesh pathing, line-of-sight perception, reaction time and human-like aim error,
  strafing, seeking health when hurt. Easy / Normal / Hard.
- **Progression & economy:** XP and levels (1–60), coins from kills/headshots/assists/wins,
  level-up bonuses, daily login streak, every gun free from the start, 11 purchasable weapon finishes (rarities, camo patterns),
  squad colors, career stats. Local profile is HMAC-signed against casual tampering.
- **Audio:** recorded gunshots with a punchy close layer plus an outdoor or indoor reverb tail
  (picked by whether the shooter is under a roof / between walls); full recorded reload
  sequences timed to the animations; brass bouncing on the ground; supersonic cracks on near
  misses; your own fire ducks the ambience. Distance falloff with air absorption, muffling through
  walls, gunfire arriving at the speed of sound with a distant rumble, footsteps / landings /
  bullet impacts per surface (sand, stone, wood, metal).
- **UI:** main menu over a cinematic map fly-through, play/loadout (live 3D preview)/shop/profile/
  settings, HUD with dynamic crosshair, hit markers, damage numbers, direction indicators, kill feed,
  streaks, scoreboard, pause, end-of-match rewards.
- **Settings:** quality presets (auto-detected), render scale, MSAA, shadows (off / hard / soft),
  FPS cap, V-Sync, display mode (full screen always uses the monitor's native resolution), sensitivity/ADS sensitivity, invert Y, hold/toggle aim & crouch, **key rebinding**,
  FOV, crosshair color/size, audio mixes.

## Controls

| Action | Keyboard / Mouse | Gamepad |
|---|---|---|
| Move / Look | WASD / Mouse | Left / Right stick |
| Fire / Aim | LMB / RMB | RT / LT |
| Jump | Space | A |
| Crouch / Slide (while running) | Ctrl or C | B |
| Reload | R | X |
| Weapons | 1 / 2 / 3, wheel, Q (last) | Y (last) |
| Knife | V | R3 |
| Scoreboard / Pause | Tab / Esc | Select / Start |

## Performance

Built and tuned on an Intel Celeron + UHD integrated GPU:
- One sun light, no additional-light loop; forward renderer, LDR. Hard shadows use a single
  1024 cascade, soft shadows two 2048 cascades.
- **No post-processing pass.** The color grade (exposure, white balance, contrast, lift/gamma/gain,
  saturation, neutral tonemap) is baked on the CPU into a 32³ LUT (`ShaderGrade.cs`, URP's own
  math) and applied with one texture fetch at the end of the Salvo shaders, together with the
  vignette and the damage / low-health effects. URP's post pass cost ~7 ms per frame at 1080p on
  the UHD laptop; this costs ~1.7 ms.
- Custom `Salvo/StylizedLit` shader: albedo from vertex colors, ambient occlusion **baked into
  vertex alpha** in Blender (zero runtime cost), soft light ramp, optional triplanar grain.
  One shared material for the whole world → SRP Batcher + static batching.
- Procedural animation on segmented characters (no skinning, no Animator); posed hand variants snap
  onto the weapon grip markers and a two-bone IK brings each wrist to its hand.
- All combat FX run through a handful of world-space particle systems (one draw call each).
- View model rendered by the main camera (no camera stacking), scaled so it never clips walls.

## Project layout

```
Assets/_Salvo/
  Art/Models       FBX exported by Tools/Blender (do not hand-edit; regenerate)
  Art/Shaders      StylizedLit, Sky, FxUnlit
  Audio            synthesized by Tools/gen_audio.py
  Data             WeaponDefs, SkinDefs, SoundBank, network prefab list (generated)
  Prefabs, Scenes  generated by the SALVO/Build menu
  Resources        SalvoConfig (central content registry)
  Scripts/Runtime  Core (profile, settings, input, audio), Net, Gameplay, UI
  Scripts/Editor   import rules, project setup, content/scene/player builders
  UI               Salvo.uxml + Salvo.uss (UI Toolkit)
Tools/
  Blender/*.py     procedural asset pipeline (salvo_lib.py is the shared kit, salvo_hands.py the
                   posed-hand generator, hands_preview.py renders weapon + hands for look-dev)
  gen_audio.py, gen_textures.py
```

## Regenerating content

```bash
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python Tools/Blender/build_weapons.py
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python Tools/Blender/build_characters.py
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python Tools/Blender/build_props.py
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python Tools/Blender/build_map_outpost.py
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python Tools/Blender/build_map_harbor.py
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python Tools/Blender/render_icons.py
python Tools/gen_textures.py
python Tools/gen_audio.py
python Tools/process_sounds.py   # slices/levels the CC0 recordings, overrides the synthesized placeholders
```

Then in Unity: **SALVO → Setup → Configure Project**, **SALVO → Build → Content**,
**SALVO → Build → Scenes**. Open `Assets/_Salvo/Scenes/Boot.unity` and press Play.

## Online play (Unity Relay)

The project is linked to a Unity Cloud project. For online matches to work for players:
1. In the Unity Cloud dashboard, open the project and make sure **Relay** and **Lobby**
   (Multiplayer Services) are enabled. Anonymous authentication needs no setup.
2. Build with **SALVO → Build → Windows (Release)** → `Builds/Windows/SALVO.exe`.
   It uses IL2CPP when the "Windows Build Support (IL2CPP)" module is installed in Unity Hub,
   otherwise an optimized Mono build.

LAN/direct-IP play uses UDP port **7777** on the host. Offline practice needs no network.

## Testing helpers

The player accepts command-line flags for automated tests:
`-salvo-practice`, `-salvo-host` (LAN), `-salvo-join <ip[:port]>`, `-salvo-online-host`,
`-salvo-code <CODE>`, `-salvo-name <name>` (session only), `-salvo-bots <n>`, `-salvo-map <Outpost|Harbor>`,
`-salvo-preset <Performance|Balanced|Quality>` (session only). Development builds also log a frame
budget every 10 s - CPU main / render thread, GPU, scripts, physics, UI, audio, draw calls,
triangles, shadow casters - and accept `-salvo-gfx "grade=0,msaa=1,shadows=1,scale=0.8,..."` for
cost experiments (`Tools/perfsplit.sh` runs variants and prints the split).

## Verified

- Performance on an Intel Celeron 5205U + UHD Graphics at **1920x1080** (dev build, 6-combatant
  bot match on Outpost): Performance preset ~58 FPS against a 60 cap (10.8 ms GPU, shadows off;
  11.5 ms with hard shadows), Balanced ~37 FPS (19.7 ms GPU: full resolution, MSAA 2x, soft
  shadows), Quality ~33 FPS. The CPU is not the limit: main thread ~4.3 ms, C# scripts ~1.3 ms. Fonts are pre-rasterized and shader
  variants pre-warmed at boot to avoid mid-fight hitches.
- LAN: player-build host + Editor client — movement, hit claims, server damage, kills, kill feed,
  scoreboard and respawns replicate correctly.
- Online: Editor hosts a Relay session, player build joins with the 6-letter code.

## Licenses

Fonts: Lilita One and Rubik — SIL Open Font License (see `Assets/_Salvo/Fonts/OFL-*.txt`).
