# Nun Massacre Co-op — 1.0.0

A co-op multiplayer mod for **Nun Massacre Definitive Edition (Windows, 2022.06.08)**. Up to four players (one host, three guests) survive the Nun together, with proximity voice chat, revives, and a Nun that hunts every player. It runs on BepInEx 5 and does not modify the game's own files.

## Install

1. Run **NunMassacreCoop-Setup.exe**. Put it in the game folder, next to `Nun Massacre.exe`, or run it from anywhere: it finds Nun Massacre on the PC by itself (Steam, GOG, Epic, itch, or a folder you unzipped it to). If it doesn't, press **BROWSE** and pick `Nun Massacre.exe`, or drop the game's folder on the window.
2. Press **INSTALL**. It installs the BepInEx 5.4.23.3 loader (carried inside the setup and checked against its SHA-256) and the mod. It never changes the game's own files and won't overwrite another mod loader. If the game is somewhere protected, such as Program Files, Windows asks for permission.
3. Press **PLAY** and choose **MULTIPLAYER** on the main menu.

The setup shows which copies already have the mod and in which version, and offers **UPDATE** when one is older. **UNINSTALL** removes the mod, and if no other mods use BepInEx it can also turn the loader off, so the game starts exactly as it came (your multiplayer settings stay). The setup's picture and lettering are the game's own (its key art and its fonts), read from your copy of the game.

Every player needs the same game build and the same mod version (1.0.0 connects only to 1.0.0); the game refuses to connect mismatched copies. The setup checks the build: the mod is for the Windows Definitive Edition, 2022.06.08. If BepInEx 5 is already installed, copying `BepInEx/plugins/NunMassacreCoop` from the zip into the game folder also works.

## Playing together

1. **Host:** choose **MULTIPLAYER**, enter **YOUR NAME** at the top, and select **HOST GAME** in the **HOST A GAME** box (TCP port **27845** by default). The lobby opens. Its **LOBBY** box shows the **invite code** for friends over the internet (already copied to your clipboard; select it to copy it again) and the players who have joined, each name lit red while they talk. The **YOUR SETTINGS** box holds your character and your voice and microphone.
2. **Friends:** choose **MULTIPLAYER**, enter your name, then in the **JOIN A GAME** box paste the host's invite code into **INVITE CODE** and select **JOIN GAME**. Games on your own network are listed under it; select one to join. A LAN or VPN address typed into INVITE CODE works too.
3. **Host:** select **START GAME**, then the game's usual PLAY screen (difficulty, scenario, item randomisation, PLAY INTRO or SKIP AHEAD). The host's choices apply to everyone. Everyone starts loading at the same moment on the same loading screen, waits there ("WAITING FOR THE OTHER PLAYERS...") until all have the level loaded, and then everyone enters together. Anyone still loading after a minute catches up on their own.

Friends can also join a game that is already running. After an ending or a team game over, everyone returns to the lobby together. A guest who chooses *exit to main menu* leaves the session; if the host leaves, guests return to the MULTIPLAYER screen with the reason.

**Over the internet (no VPN needed):** when you host, the mod asks your router to open the game's port (UPnP, which most home routers have switched on) and makes the invite code from your internet address, the port and a one-time key. Only friends with the code can join from the internet. The port is closed again when you stop hosting or quit. Windows Firewall may ask to allow the game the first time you host: allow it, or friends cannot reach you.

If the lobby says the router did not open the port, either turn on UPnP in your router's settings, forward TCP port 27845 to your PC yourself (friends then type your public address), let a friend host instead, or use a virtual LAN such as Tailscale, ZeroTier, Radmin VPN or Hamachi and join the host's VPN address. Some internet providers (often mobile, satellite and some fibre) share one address between many customers; then no router setting helps and a friend hosts or you use a VPN. `127.0.0.1` only reaches another copy on the same PC.

**Your character:** in the lobby, **YOUR SETTINGS → CHARACTER** shows a turning 3D preview. Choose **Mike**, **Maddy** (the secretary) or **Mark** (the scientist). Teammates see your choice in game; it applies right away, even mid-session.

## How co-op plays

- **Knocked down, not killed.** When the Nun brings you to 0 health you go down: you can look around and crawl slowly on hands and knees (towards where you look), and a five-minute bleed-out timer runs, on every difficulty: time enough to lure the Nun away and come back. Hold **Jump** for two seconds to give up.
- **Revive.** Walk up to a downed teammate and hold **Interact**: 4 seconds brings them back with 35 health, or 1.5 seconds with a **first aid kit** equipped (full health, uses the kit only if the revive works). Not through walls or closed doors. The timer pauses while you revive. There are no markers or name tags: a notice says who is down, and you find them by looking and listening.
- **The Nun stands over the one she knocked down** for a few seconds, then backs off (about six seconds in all) and searches. While someone is down she keeps coming back to search round them. Reviving under pressure is the point.
- **Stay together.** Out of her sight the Nun goes after whoever is off on their own: she searches round them and lies in wait near them. When she spots you, you get her jump scare and the jolt on your screen; her chase music is loudest for the one she is after and fades with distance for everyone else.
- **Shout to save a friend.** If she is chasing your teammate, step into her line of sight (within about 25 m) and shout. She stops, turns to you, screams and comes after you instead. She will not break off when she is already on top of them, and after switching she ignores calls for about 8 seconds.
- **Hiding together.** Two players fit under a bed, side by side, and can see each other while they wait. A third has to find another bed ("No room under this bed"). Beds are the game's only hide action; anywhere else (a bathroom stall, a room behind a closed door, a vent) you hide by staying out of the Nun's sight.
- **Teammates take up room.** You cannot walk through each other: whoever walks into the other stops (nobody is shoved), and a gap only wide enough for one, like a vent, takes one at a time. Crouching in a vent (or anywhere too low for a crouch) they crawl on hands and knees.
- **You hear teammates walk:** the game's own footsteps for the floor under them, from where they are. Crouching is silent.
- **Teammates move like people:** motion-captured walks (Mixamo's Walking for the men, Female Walk for Maddy), runs, jumps and idles, a hunched crouched sneak (upright enough on stairs), a crawl when downed or in a vent, lying flat under a bed, and a two-handed revolver stance that kicks when they fire, all stepped in the game's PS1 style (twelve poses a second) and drawn with the game's own PS1 shader. Feet stay flat on the floor and out of it, toes and heels included, on slopes and stairs; hips and knees bend as one surface (a hidden halfway joint at each knee keeps a soft crease at the back of a bend); hands and knees too when crawling; bodies stay out of walls and duct ceilings. What a teammate holds is in their right hand at its real size, the right way up as you hold it yourself: a hammer by the end of its handle, a bottle by its base, a map or a note open in front of them (a plank across the body in both hands).
- **Peeking round corners.** When a teammate leans (the game's **Q** and **E**), their character leans out the same way and their hand goes flat on the far side of the corner beside their face, so you see a head and a hand come round the edge.
- **Teammates react.** A hit from the Nun draws blood and a recoil (Maddy cries out with the game's own voice); they reach out to take things, bend down to pick things off the floor, pull doors and drawers open and throw overarm. A teammate who runs out of lives leaves a body sprawled where they fell.
- **Distract her from a hiding spot.** If she is running to drag a teammate out from under a bed, shout from another room (within about 20 m). She stops, turns toward your voice, and usually goes to where it came from and searches there.
- **Bleeding out** costs one of your own lives through the game's normal death sequence. If everyone is down at once, nobody can revive and the team bleeds out together.
- **Out of lives:** your body stays where you fell until the level ends; you spectate and rejoin in the next level. **Interact** changes the view each press: behind a teammate, through their eyes, the next teammate, then a free camera. **Jump** also toggles the free camera: move and look to fly anywhere, through walls, with **Run** to go faster and **Crouch** to drop. The game's blue moonlight stays with you while spectating. When every player is out of lives, it is game over for the team.
- **Puzzles are shared.** Anyone can work on any puzzle, and progress counts for everyone: one of you places a candle, another lights it. A close-up or minigame (the clock hands, lockpicking, the padlock) belongs to whoever started it; the others see the result. A tool used in a puzzle returns to whoever used it.
- **One at a time mid-animation.** While someone is part way through a puzzle's animation (pouring the alcohol on the rope, hammering a nail, a vent screw, the sink, crawling through the fan), that puzzle is theirs until it finishes: anyone else trying it is told who is using it. Nobody is left frozen in an animation: if one is ever cut short, you are let go of it, and the host's `unstuck` also frees a player stuck in a close-up. Knocked down part way through one, you stay down; the puzzle finishes for everyone.
- **A tool for every player.** The lighter, the map and the bandages come one per player (one to four), side by side where the original lies (wherever item randomisation puts it): alone, you find one of each, as in the single-player game. The radar and the revolver stay one each for the team: one player carries the radar and calls out where the Nun is, one has the gun. The count is set by who is in the game when the school is entered; a player who joins after that gets their own lighter, map and bandages beside where the originals lay.
- **The opening, together.** Everyone wakes in the bathtub and gets up when they are ready (teammates see you lying there until you do). The letter takes the whole group outside together once everyone is out of the bath; the scares and the title card on the way happen to all of you; and you go into the school together, once everyone is at the entrance.
- **Endings, together.** Every ending happens to the whole team: the good ending, the coffin ending, the scenarios' endings (the Carnival's rocking chair, the Backrooms way out, PDM's coffin, each with its closing text), and the two that kill you (staying in the acid vent, falling into the barbed wire). An ending only goes off once everyone still playing is there: walk into it or try it alone and a notice says who you are waiting for; after a minute the team goes ahead without anyone who has not come. Then it plays on everyone's screen at once, with teammates hidden so nobody is in the shot, and everyone returns to the lobby together. Only the player in the coffin can close its lid.
- **Scares happen together.** A scare only fires when everyone still in the game is close by (about 12 m, same floor), and then it happens to all of you at once. Split up and it waits until you come through together.
- **Holding things together.** Several players can drag the same door or object; one letting go does not stop it for the others. If a player leaves mid-puzzle, the puzzle is freed for everyone else.
- **Tab** shows the team: health, downed or out, and lives. Notices (joins, knockdowns, revives) appear in the top-left corner. These, the revive bars and the hiding meter sit under the game's VHS and CRT screen effects, like its own prompts.
- **Unlocks count.** Beating the game together unlocks what it does alone (the Factory; the Circus for a player who lost no life, or on Extreme) on every player's game. An anomaly found by anyone counts in every player's save (all ten unlock the Backrooms), once each.

## How the Nun hears

In multiplayer she hears far more than in the game alone, the same in every game:

- **Through the building.** She listens with the same raytraced acoustics you hear with. A noise (something dropped or thrown, a door, glass, a whistle, a gunshot, footsteps) reaches her down an open corridor as far as it carries, less far round a corner or through an open door, and through a shut door, a wall or a floor only from close by.
- **Sharper the harder the game.** In the open a noise reaches her 1.25 times as far as in the game alone on Easy, 1.6 times on Medium, twice on Hard and two and a half times on Extreme (Explore is as the game).
- **Voices.** Talking at an ordinary level reaches her down a corridor and through an open doorway on every difficulty. Through a floor, right under her, ordinary talking brings her from Medium up; on Easy she stops to listen and comes only if the talking goes on. Shut doors and walls take most of a voice. A whisper never reaches her. Steady background noise (a fan, mains hum) and short knocks on the microphone do not count. Footsteps: running carries, walking only close to her, crouching never.
- **What she does about it.** A faint sound makes her stop, listen and turn towards it. A clear one, or faint ones that keep coming from the same place, send her to where the sound was made (not to where you are now), the way she can walk there, stairs included, and she searches the rooms round it. A new sound elsewhere only draws her off once she has been at the first for a few seconds, or if it is clearly louder. If she finds nobody she gives up and goes back to her rounds; if she sees someone on the way, the chase is on.
- **Under a bed.** A meter on the right turns from cream to red the louder you are; fully red means she hears you if the sound gets to her. If she hears you under a bed (or you whistle there) she comes for that bed. Alone under it, she flips it over. Two of you under it, she leaves the bed where it is, reaches under and drags you both out, then goes for the one she heard.

## Proximity voice chat

- **Raytraced acoustics** (powered by [Vercidium Audio](https://vercidium.com)), for voices and the game's own sounds (doors, cabinets, lockers, footsteps): the school's walls, floors, doors and furniture, each with what it is made of (tile, painted plaster, brick, wood, glass, sheet metal, dirt), are built into a raytracer as the level loads. Rays bounce round the real building from you and from every teammate who talks, on background threads, and give each voice the reverb of the spaces it is in and how muffled it reaches you, round corners, through doorways, through walls and along the ducts: a tiled bathroom rings, the church and the long corridors carry a long tail, a closed room next door is a muffled murmur, a voice in a vent rings off its metal. It costs about a twelfth of one processor core. It is always on; if its library cannot start, the mod's simpler acoustics (distance, walls and floors, without the echoes) stand in.
- **Your own voice comes back** off the space you are in, quietly, as a real place answers it: a little ring in a bathroom or the church, an echo off the far end of a corridor, the tin of a duct. Only the space's answer is played, never your voice itself. Use headphones if your microphone would pick it up from speakers.
- **Each teammate's volume** in the pause menu: Escape lists everyone else playing beside the menu, with the game's own slider from muted to twice as loud (kept by name for later games), whether they are down or out, and their name lighting up red while they talk.
- Teammates' voices come from their characters and carry as they would in the building: clear close by, fading across a big room, faint down a long corridor, gone by about 45 m, and duller far away. A voice also comes round the way a person would walk: from a room, you hear it from its open door; round a corner, from the corner; from a vent, out of the vent's opening (inside the duct with them, from along it); through a window or any gap you can both see, from the gap. Through a wall you hear someone muffled; a closed door takes most of a voice. A teammate on another floor is only a dull, wordless murmur, and only when they are about right under (or over) you; two floors apart, nothing. Down a long corridor a voice echoes. A voice behind you is a little duller than one ahead; a very quiet one (a whisper through a wall) is lost. Someone facing away is quieter. Big bare rooms echo, small hard rooms (a bathroom, a stairwell) sound boxy, a voice in a vent rings hollow off the metal, and one from under a bed is muffled. A downed teammate sounds weaker; under a bed you hear everyone through the mattress; knocked down, your ears ring. In the lobby and loading screens everyone hears everyone.
- **Open mic** (voice activation, the default) or **push to talk** (hold **V**).
- Downed players can still call for help. Players who are out of lives hear the living but can only talk to each other.
- **SETTINGS → MULTIPLAYER** (main menu or pause menu), or **YOUR SETTINGS → VOICE & MIC** in the lobby: voice on/off, open mic or push to talk, the talk key, the microphone, **mic level** (raise it if teammates hear you too quietly), a live mic test, the voice gate and teammate volume.
- Two copies on one PC with speakers will echo each other: use headphones.

- **Fell out of the world?** Falling through the floor or off the map puts you back where you last stood on solid ground, with no life lost.
- **Joining or rejoining mid-level** brings you up to date: doors, items and every puzzle's progress.

## Host console

**F2** (or **`**) opens a console at the top of the screen; **F2** or **Esc** closes it. Your controls are paused while it is open.

| Command | Does |
|---|---|
| `players` | everyone's number, name, state and position |
| `bring <player>` | move a player to you |
| `goto <player>` | move yourself to a player |
| `tp <player> <player>` or `tp <player> <x> <y> <z>` | move a player to another, or to a spot |
| `unstuck [player\|all]` | back to their last solid footing, and out of any puzzle close-up they are stuck in (on its own: you) |
| `spawn <player\|all>` | to the level's respawn point |
| `revive <player>` | get a downed player back up |
| `kick <player>` | remove a player from the game |

A player can be named by the start of their name, their number from `players`, or `me`. Guests can use `unstuck` (on themselves) and `players`. While you host, the game also runs commands written to `BepInEx/nmm-commands.txt` (one per line; the file is emptied once read), so someone can fix things from outside the game.

## Gestures

- **1** — point where you look. Your character's arm goes out; nothing appears on screen.
- **2** — whistle. Teammates hear it from where you are, through walls and floors like a voice. The Nun hears it too.

## Controls

| Action | Key |
|---|---|
| Revive a downed teammate | Hold **Interact** next to them |
| Give up while downed | Hold **Jump** for 2 s |
| Crawl while downed | Move (slowly, towards where you look) |
| Spectating: next view (behind, eyes, next teammate, free camera) | **Interact** |
| Spectator free camera on/off | **Jump** (then move/look to fly, **Run** faster, **Crouch** down) |
| Peek round a corner (teammates see you lean and grip the edge) | **Q** / **E** (the game's own lean) |
| Point / whistle | **1** / **2** |
| Team roster | Hold **Tab** |
| Push to talk (if selected) | Hold **V** (**TALK KEY** in the voice settings changes it) |

## Settings file

`BepInEx/config/local.nunmassacre.multiplayer.cfg` holds your name, last invite code or address and port, whether hosting opens the router port (`OpenRouterPort`), character, and voice options (including the push-to-talk key, each teammate's volume as `PlayerVolumes`). The game must be closed while you edit it. For a bug report about the Nun, `NunDiagnostics = true` under `[Debug]` writes what she hears and why to the log.

## Troubleshooting

- **Friends cannot join with the invite code:** check what the host's lobby says under the port. If it shows an invite code but friends time out, Windows Firewall is probably blocking the game on the host: allow *Nun Massacre* in Windows Security → Firewall → Allow an app. A new code is made every time you host, so send the current one.
- **Friends cannot find or join the game on a LAN or VPN:** check that you are on the same network or VPN, that the port matches, and that Windows Firewall allows the game. LAN discovery does not work across most VPNs: type the address instead.
- **"Different game build or co-op mod version":** both players need the 2022.06.08 Definitive Edition and the same mod release.
- **No voice:** open **VOICE & MIC** in the lobby (or SETTINGS → MULTIPLAYER), check the microphone and the mic test meter, the voice gate (open mic) or the push-to-talk key, and the mic level.
- **Teammates hear an echo of themselves:** someone's microphone is picking up their speakers. Use headphones.
- **The Nun never reacts to talking:** check that VOICE CHAT is on and the mic test meter moves when you speak. She only hears what gets through the building to her, and a whisper never does.
- **Stuck in an animation or a close-up:** press **F2** and type `unstuck`. The host can free anyone with `unstuck <player>`.
- **The setup says "a different build of Nun Massacre":** the mod only runs on the Windows Definitive Edition, 2022.06.08. Update the game through the store you bought it from.
- **The setup says another mod loader is installed:** something other than BepInEx already loads into the game (`winhttp.dll`, `version.dll` or `doorstop_config.ini` beside `Nun Massacre.exe`). Remove that mod first, or install BepInEx 5 yourself and copy in the plugin folder.
- **The setup says to close Nun Massacre:** the game is running from that folder. Quit it and press INSTALL again.
- **Anything else:** the mod's log is `BepInEx/LogOutput.log`. Please include it with a bug report.

**Removing the mod by hand.** Delete the folder `BepInEx/plugins/NunMassacreCoop` in the game folder. To make the game start without BepInEx at all, also delete `winhttp.dll` and `doorstop_config.ini` beside `Nun Massacre.exe`. The game's own files are never changed, and your saves are the game's own and stay where they are. If an update was interrupted, run the setup again: it replaces the mod's folder as a whole, so a half-copied update never runs.

## Known limitations

- Tested in scripted runs on one PC, two to four copies of the game over the local connection: a continuous two-player playthrough from the bathtub to the good ending, every ending separately, joining mid-game, and the Nun's hearing measured spot by spot (see `docs/VALIDATION.md` and `docs/RELEASE-STATUS.md` in the source package). Play between two PCs over the internet, with real microphones, and with four people at once still has to be confirmed by human testers.
- The alternate scenarios (Carnival, Backrooms, PDM) have been played to their endings in co-op, with a player joining mid-game, but not every one of their puzzles has been played in co-op: most are the main game's puzzles (played through in full), in their own places.
- Some runtime effects (blood decals, glass shards, the Nun's corpse) are cosmetic and differ between players.
- No host migration: if the host quits, the session ends.
- Doors and pickups for guests go through the host, so over a slow connection they react a moment late.

## Credits

Multiplayer mod by **NetworkPlasma**. In game: **MULTIPLAYER → MOD CREDITS**. The game's own credits are left as they are.

- Loader: [BepInEx 5.4.23.3](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.3) and Harmony, carried inside the setup program.
- Raytraced audio: **Powered by Vercidium Audio** ([vercidium.com](https://vercidium.com)), its native library `vaudionative.dll` shipped beside the plugin under Vercidium's free non-commercial licence. This mod is free and earns nothing; Vercidium Audio is Vercidium's, and its licence terms apply to it.
- Character models: **Mike, Maddy and Mark by Vinrax** ([sketchfab.com/vinrax](https://sketchfab.com/vinrax), [x.com/Vinrax](https://x.com/Vinrax)). They keep their author's ownership and licences; no redistribution licence is asserted here.
- Motion capture: Mixamo (walks, runs, idle, jumps, crawl, shooting, lying on the stomach, a hit reaction, throwing, picking up, opening, and the body's sprawl from Falling Back Death). The crouch is made from the walks and the idle by `scripts/make-crouch.py`.
- Nun Massacre is a game by Puppet Combo. You need your own copy of it to play.

## Build from source

Requires the .NET SDK 8, a legally obtained copy of the matching game in this workspace, the pinned BepInEx loader under `.tools/BepInEx`, and Vercidium Audio's SDK (free for non-commercial use, from [vercidium.com/download](https://www.vercidium.com/download)) unpacked under `vendor/vaudio`, so that `vendor/vaudio/3d/native/production/windows/vaudionative.dll` exists. The source package does not include it.

```powershell
.\scripts\bootstrap.ps1
.\scripts\build.ps1 -Install
dotnet run --project tests\NetworkTests\NetworkTests.csproj -c Release
dotnet run --project tests\ProtocolTests\ProtocolTests.csproj -c Release
.\scripts\test-runtime.ps1 -LateJoin -Label regress; .\scripts\verify-runtime.ps1 -LateJoin -Label regress
.\scripts\package.ps1
```

The characters are already converted and embedded. Re-converting them is optional and needs Python with `ufbx==0.0.5`: `python scripts/convert-player-model.py`. The test driver (command-line flags starting with `-nmm-`) does nothing in normal play. The supported game build is identified by the SHA-256 of `Assembly-CSharp.dll`: `0132E2E1556CDD8026EB42A08774CFEB94E313C4E29D40CFE640C8FC5FA0E871`. Game files and decompiled research are never included in the release archives.
