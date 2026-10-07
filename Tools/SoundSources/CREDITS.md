# Sound sources

All recordings below are from [Freesound](https://freesound.org) and released under
**CC0 1.0 (public domain)** - no attribution required; credited here anyway. The files in this
folder are Freesound's high-quality MP3 previews. `python Tools/process_sounds.py` slices,
trims and levels them into `Assets/_Salvo/Audio` (run it after `Tools/gen_audio.py`, which
provides the synthesized UI sounds, music, ambience and remaining placeholders).

| File | Sound | Author | Used for |
|---|---|---|---|
| viper_fire.mp3 | [9mm pistol shot](https://freesound.org/people/michorvath/sounds/427592/) | michorvath | Viper fire |
| hornet_fire.mp3 | [AR15 pistol shot](https://freesound.org/people/michorvath/sounds/427598/) | michorvath | Hornet fire |
| ranger_fire.mp3 | [AR15 rifle shot](https://freesound.org/people/michorvath/sounds/427596/) | michorvath | Ranger fire |
| bulldog_fire.mp3 | [20 gauge shotgun gunshot](https://freesound.org/people/michorvath/sounds/427595/) | michorvath | Bulldog fire |
| longbow_fire.mp3 | [50 cal shot](https://freesound.org/people/ajhartman42295/sounds/668071/) | ajhartman42295 | Longbow fire |
| distant.mp3 | [R12-08 Classic Western Rifle](https://freesound.org/people/craigsmith/sounds/486062/) | craigsmith | distant gunfire / echo layer |
| dry_fire.mp3 | [Rifle clip empty](https://freesound.org/people/michorvath/sounds/427603/) | michorvath | dry fire click |
| pistol_reload.mp3 | [1911 Reload](https://freesound.org/people/nioczkus/sounds/396331/) | nioczkus | pistol slide / reload |
| ar15_mag.mp3 | [AR15 Various Sounds](https://freesound.org/people/wadaltmon/sounds/263513/) | wadaltmon | magazine out / in |
| pump.mp3 | [Pump Action Shotgun](https://freesound.org/people/awrmacd/sounds/387190/) | awrmacd | shotgun pump |
| bolt.mp3 | [Mosin Nagant Bolt (fast)](https://freesound.org/people/rammbostein/sounds/267895/) | rammbostein | sniper bolt |
| steps_sand.mp3 | [Footsteps Dirt Gravel](https://freesound.org/people/PotatokingXII/sounds/352870/) | PotatokingXII | sand footsteps |
| steps_stone.mp3 | [Walking on pavement](https://freesound.org/people/JohnsonBrandEditing/sounds/244310/) | JohnsonBrandEditing | stone footsteps |
| steps_wood.mp3 | [footstep on wood foley](https://freesound.org/people/martian/sounds/19292/) | martian | wood footsteps |
| steps_metal.mp3 | [Metal Steps](https://freesound.org/people/Phil25/sounds/208101/) | Phil25 | metal footsteps |
| impact_sand.mp3 | [Sticks in dirt 2](https://freesound.org/people/worthahep88/sounds/319214/) | worthahep88 | bullet hits on sand |
| impact_stone.mp3 | [ricochet](https://freesound.org/people/cedarstudios/sounds/148840/) | cedarstudios | bullet hits on stone |
| impact_metal.mp3 | [bullet impacts](https://freesound.org/people/pushkin/sounds/144444/) | pushkin | bullet hits on metal |
| rifle_reload_game.mp3 | [Assault Rifle Reload](https://freesound.org/people/qubodup/sounds/815879/) | qubodup | Ranger reload |
| sniper_reload.mp3 | [Assault Rifle Reload](https://freesound.org/people/Kodack/sounds/256912/) | Kodack | Longbow reload |
| rifle_reload.mp3 | [An M16 Rifle being reloaded while empty; full sequence](https://freesound.org/people/ser%C3%B8ut%C5%8Dnin--depriv%C9%99d/sounds/725397/) | serøutōnin--deprivəd | (spare, not used) |
| smg_reload.mp3 | [Uzi submachine gun being reloaded while empty](https://freesound.org/people/ser%C3%B8ut%C5%8Dnin--depriv%C9%99d/sounds/677160/) | serøutōnin--deprivəd | Hornet reload (pauses shortened) |
| pistol_reload_full.mp3 | [PistolReloadSound.wav](https://freesound.org/people/MaximBomba/sounds/432139/) | MaximBomba | Viper reload (whole take) |
| shell_load.mp3 | [Shotgun Shell Load](https://freesound.org/people/Bratish/sounds/500293/) | Bratish | Bulldog shell insert |
| casings_concrete.mp3 | [Brass bullet shell casing drop onto concrete](https://freesound.org/people/GrayJoy/sounds/210102/) | GrayJoy | spent casings |
| casings_tile.mp3 | [Bouncing Shell Casings (Various Sizes)](https://freesound.org/people/GryffDavid/sounds/318964/) | GryffDavid | spent casings |
| handling.mp3 | [Gun Handling.wav](https://freesound.org/people/danhelbling/sounds/272394/) | danhelbling | weapon draw |

Processed / synthesized on top (see `process_sounds.py`): every gunshot gets a sub-bass body and soft
saturation for its close-up layer, plus outdoor and indoor reverb tails rendered by convolving the
recording with synthetic impulse responses; the near-miss supersonic cracks are synthesized N-waves.
