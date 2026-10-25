# Validation — 1.0.0

All runs used real copies of the game (Windows 2022.06.08) on one PC, started by the opt-in test driver: two or three processes, hard-linked game files, loopback networking. Most were headless (`-batchmode`); the HUD screenshots used windowed processes. Each item below was observed in the logs or screenshots of at least one such run.

## Automated suites

| Suite | Result |
|---|---|
| `tests/NetworkTests` (transport: multiple clients, framing, limits, reconnects, queue bounds; invite codes; UPnP router client against a fake router) | 14/14 pass |
| `tests/ProtocolTests` (codec, compression limits, type-injection guard, life-state fields, voice codec SNR 40.6 dB and corrupt-packet rejection) | 11/11 pass |
| `scripts/test-runtime.ps1 -LateJoin` + `verify-runtime.ps1` (host, guest, late joiner; native pickup/drop, cabinet state within 2 cm, model loaded, no sync errors) | PASS |

## 1.0.0 (built as 2.0.9)

The sections after this one are the test builds before the release, numbered 0.3 to 2.0.8.

- **The MULTIPLAYER screen, final build:** each box says what it asks ("BE THE HOST AND INVITE FRIENDS:", "PASTE YOUR FRIEND'S INVITE CODE:", "ONLINE NEEDS UPNP ON IN YOUR ROUTER."); JOIN A GAME is one framed invite code box ("CLICK HERE, THEN CTRL+V") with no join button. Two windowed copies, the host requiring the invite key: a mistyped code in the box said "THAT INVITE CODE IS NOT RIGHT. CHECK IT WITH THE HOST."; the real code joined as soon as it was in the box. The host's lobby, with the router refusing, read "NO CODE: TURN ON UPNP IN YOUR ROUTER." and "FRIENDS ON YOUR NETWORK CAN JOIN."; the guest's, "YOU'RE IN. THE HOST STARTS THE GAME WHEN EVERYONE IS HERE."
- **Lobby and joining, final build:** two windowed copies. The host's lobby shows the players and the invite code, no port or network address. The guest's MULTIPLAYER screen listed "HOST'S GAME 1/4 LOBBY" under ON YOUR NETWORK; selecting it showed "CONNECTING TO HOST'S GAME..." (no address anywhere) and joined. With the router refusing (a fake router), the lobby's foot read "Your router did not open the port. Turn on UPnP in its settings and host again (or use a VPN)." on two lines.
- **Held items, final build:** every item in each character's hand, photographed by the host from beside the hand, from in front of the body and from the side. A hammer is held a fist's width up its handle with the end showing below the fist (it had been gripped by its very end, seeming to stand on the fist); the radar, the bottles and the other broad things lie in the palm with the back of the hand outside them (the radar had been centred on the hand, half of it out past the knuckles). NetworkTests 14/14 and ProtocolTests 11/11 again.
- **The release build, last:** NetworkTests 14/14, ProtocolTests 11/11; the late-join run PASS (pickup, drop, cabinet within 2 cm, models; a first run failed its pickup check once, the re-run and every earlier run passed); the full two-player playthrough from the bathtub to the good ending (the ending waited for the host, then played for both, both back in the lobby, worlds identical); the three scenarios with three players, each ending refused alone and then played for all; noises at the four spots on Easy and Medium as below; a whistle from under a bed; the rope refused to a second player; screenshots of every MULTIPLAYER page. The setup from the release package: fresh install, reinstall keeping a player's file, a locked file rolled back, uninstall with the loader off.
- **Screens, 1.0.0:** the connect page (the name on its own line, HOST A GAME and JOIN A GAME with framed buttons and the network search), the lobby (how friends join and the players, tagged HOST and YOU; YOUR SETTINGS with character and microphone), VOICE & MIC and SETTINGS → MULTIPLAYER (the microphone's full name, no RAYTRACING, OWN ECHO or NUN HEARS YOU rows) and MOD CREDITS (with "BY NETWORKPLASMA").


- **The Nun's hearing, noises (final build):** the Nun held in the third-floor corridor, a 10 m noise (the game's radius for a thrown thing) made round her at each difficulty, her memory cleared before each. In brackets, the share of it her rays found (after her 0.4 exponent), then the strength (1 is the edge of her hearing; 1.6 sends her to it). Down the open corridor 8 m (1.00): Easy stops to listen (1.55), Medium to Extreme investigate (1.98 to 3.09). Round the corridor's far end, 14 m (0.41): missed on every difficulty (0.47 to 0.74). The bedroom beside her through its doorway, 4.2 m (1.00): investigated on every difficulty (2.98 to 5.97). The floor below, 6.2 m (0.47): missed on Easy (0.94), listened to on Medium and Hard (1.21, 1.51), investigated on Extreme (1.89). Two floors down, 11.2 m (0.49): missed up to Hard, listened to on Extreme (1.10).
- **Voices (final build):** the guest spoke a recorded phrase at a normal speaking level at four spots; before each the Nun was put back in place and her memory cleared. Down the corridor 8 m and 11.5 m (1.00) and in the bedroom through its doorway (0.43 to 0.49): heard and investigated on every difficulty. Right under her on the floor below (0.33 to 0.35; five repeats on Medium, 0.32 to 0.40, after letting her rays settle 3 s at each spot): heard and investigated at the first words on Medium, Hard and Extreme (0.033 to 0.036 at her ears, against 0.012, 0.008 and 0.005). On Easy, five repeats: the louder words reached 0.022 to 0.031 against the 0.020 Easy needed, and she investigated every time; that was harsher than meant, so Easy now needs 0.030. Re-run five times at 0.030: she stopped to listen at the first word that reached her and investigated two or three words later, every time. At 0.030, talking down the corridor (8 m, 12 m; loudest 0.071, 0.073) and through the bedroom's doorway (0.053) still brings her on Easy.
- **A loud moment is not a shout:** in the first of those runs the phrase's loudest packets (a peak over 0.85) reached her as a shout at a fixed level of 0.30, about twice their measured 0.15, and through the floor that was enough on Easy. A shout now carries its measured level (at most 0.5).
- **An earlier measurement corrected:** the first 2.0.9 notes had voices through a floor heard "only from Hard up". That run let the Nun wander after her first investigation and spoke a second after each move, before her rays had settled at the new spot (0.09 then, 0.32 to 0.40 settled). The numbers above replace it.
- **Investigating (Medium, her position and facing logged twice a second):** a clear noise 7 m down the corridor (strength 2.24): she investigated, walked to it and searched round it, closest 0.2 m. A faint one (1.1) from the bedroom: she stopped, turned from 76° to 13° off it within a second, and went on with her search without going there. Three faint ones 1.2 s apart: suspicion 0.50, 0.89, then she investigated and walked to the bedroom's search points (closest 2.7 m; the spot itself is behind the bed). Competing: on a sound in the bedroom, a second down the corridor a second later was ignored ("still on the last sound"); the same 7 s later, she switched to it. A voice on the floor below (Hard): she investigated, took the stairs down (height 10 to 5) and came within 0.9 m of where the guest had talked; the guest had already left without a sound.
- **Stuck mid-animation:** the guest poured the alcohol on the rope and the host put the lighter to it 0.6 s later. Before: the host's step took over the rope, and the guest stayed frozen in the pour (ItemCutscene) for the 22 s watched. After: the host was turned away ("client is using that."), the guest's pour finished (back to Normal), and the host lit the rope once it had. The same 50 ms apart: turned away. With the lock switched off (a test switch), the safety net let the guest go a second after the rope's machine moved on without them. Knocked down 0.8 s into the pour: they stayed down while the pour finished, and once revived walked off normally.
- **Items in the hand:** each of 15 items (the lighter, map, bandages, radar, revolver, hammer, wire clippers, rubbing alcohol, drain cleaner, gasoline, candle, tape, rock, body part, plank) photographed in each character's hand from the front and side: the middle of the hand inside every item (the plank 2 mm from it), the hammer and radar gripped at their base, the map visible.
- **Endings, together (the acid vent):** the guest walked into the vent's trigger with the host at the start of the level: told "Wait for everyone: you finish together (host).", and nothing happened to them. Ten seconds later the host came over: on the guest's next ask it went off ("everyone is here"), both games ran the ending (the Nun sent away, her watching, the steam), the steam killed both players (health 0 on each), and each game's end credits started. The guest skipped the credits (as Escape does): "client reached the end", and both games went back to the lobby.
- **Endings, together (the barbed wire):** the same with the pit: told to wait alone; with the host there, both games ran it, both players were pulled into the wire and its Nun came to attack on both. Its Nun only strikes while the caught player struggles, which the frozen test players cannot do, so the kill itself was not reached in a scripted run.
- **A scenario's ending is one ending:** PDM's lid closing was committed as the team's ending, then its closing text was refused as "another ending" and the team stayed in the level. The closing text now counts as the same ending.
- **Under a bed, one way of hearing:** a shout and a whistle from under the third-floor bed, the Nun held on the floor below, 12 m down the corridor, then in the doorway: she went for the bed from the floor below and from the doorway, and did not hear it from the corridor (the whistle's strength 0.95 there), both by her raytraced ears alone. With ORIGINAL hearing the old check decided the whistle as before (the bed from the floor below and the doorway, not the corridor); ORIGINAL hearing was removed afterwards.
- **Messages from the host:** a guest taking the letter while the host was still in the bath saw "Wait until everyone is out of the bath (host)." in full (it was cut at 32 characters). A player who joined during an ending saw "The team is finishing an ending. You will join them in the lobby." and stayed in the lobby (before, that notice was dropped).
- **Revives checked by the host:** a plain revive gave 35 health; one with a first aid kit gave 100 and used the kit; a forged request (no hold, a kit that was not the reviver's) was turned down ("not held long enough").
- **Endings:** two players setting off different endings at the same moment: every game played the one the host settled on. While it played, nobody bled out and the team could not be wiped. A player joining during it stayed in the lobby with a notice instead of loading into it.
- **The opening:** a guest who never got out of the bath: after 60 s, "Going ahead without client", and the letter took the host outside. A guest still loading a level the host had already left followed the host.
- **Screens:** MOD CREDITS (title, version line, Vinrax, Mixamo, Vercidium, BepInEx and Harmony, the separate Puppet Combo line) and SETTINGS → MULTIPLAYER with RAYTRACING and OWN ECHO, screenshots checked; the switched-off rows show no stray arrow.
- **Setup, run quietly against hard-linked copies of the game without the loader:** a fresh install; installing again with a player's own file in the mod folder (kept); installing while a mod file was held open (refused with "Nothing was changed", the old version intact, no leftover folders); uninstalling with the loader turned off (the player's file kept). A folder named with spaces, ü, an en dash and Japanese characters: installed; uninstalled with another BepInEx mod present, which kept the loader. Leftover `.new` and `.old` folders from an interrupted setup: cleared, install completed. A different game build (one byte added to `Assembly-CSharp.dll`): refused. The game running from that folder (a stand-in process under its name): refused. The installed files' SHA-256 match the release manifest.
- **NUN VS PLAYERS removed:** the build has no trace of the mode left in the plugin's code (its file, lobby rows, packets, voice effect and hooks), and what it had switched off for the Nun player (the Nun's reaction to a knocked-down player, her weapon's own hit, a whistle under a bed drawing her) runs as before. Suites and the late-join run below are from this build.

## 2.0.8

- **NUN VS PLAYERS, the host as the Nun:** both games windowed. The mode started on both with her mind and navigation agent off; walking her along the third-floor corridor, the guest's game showed her pose and walk (Forward 0.34 to 0.38 mid-stride); a swing in reach took the guest from 100 to 60 (her weapons' damage, 40); a door step chose a door 21 m from the nearest player. Screenshots: the survivor sees her lit by her lamp down the corridor; her player sees through her eyes, by her lamp.
- **A guest as the Nun:** walking her 4 s toward the host, the host's game had her where the guest's did (233.36 against 233.35) and she stopped 0.64 m short of the host (the bump; before it she walked through). Her player's game first ignored the host's copy of her (it was pulling her back, so she walked at two thirds of her pace). One swing, one hit: 100 to 60 (before, the game's own weapon hit the host as well, 100 to 20, and a second hit put them down). Trap: one bear trap 0.7 m in front of her, made on every game. **E** at a gas switch: the switch's own "Pressed". **Q**: her scream (Characters/Killer Stuff new/Audio Follow Killer/Chase/Scream). **E** at a door: pushed.
- **Under a bed:** the host hid under a bed with the guest's Nun 2 m away. Her player's screenshot showed only the bed; after the host spoke a phrase (level 0.09) or whistled, it showed the host's white shape through the bed. **E** turned the bed over and dragged the host out (ending 1.1 m from her), where the next swing hit (100 to 60). Screenshots from both sides.
- **From the lobby:** the host set NUN VS PLAYERS with THE NUN on RANDOM and pressed SKIP AHEAD with a guest joined: both games started the school with player 2 as the Nun, her player looking through her eyes.
- **Her voice:** the guest spoke a recorded phrase 4 m from the host as a survivor and then as the Nun: the same level (RMS 0.030 and 0.028), the Nun's lower (median pitch 101 Hz against 89 Hz).
- **Vent voices, blind:** three pairs (inside the duct with them, right outside its opening, the room under it): the duct echo picked outside the opening and inside, the old ring in the room below; inside now has neither.
- **Lobby and pause menu:** screenshots of the LOBBY and GAME boxes (CO-OP, custom hearing rows, NUN VS PLAYERS), the character page (preview inside its frame), and the PLAYERS box with three players.

## 2.0.7

- **Raytraced acoustics, blind tests:** the guest spoke a recorded phrase (Windows speech) in nine places while the host recorded exactly what it heard (the whole mix at its audio listener, every other sound muted), once with the mod's own acoustics and once raytraced, labelled 1 and 2 at random. The player chose by ear without knowing which was which. Round 1 (the raytracer seeing only box colliders, as it turned out): the mod's own won five of six. Round 2, after the fixes: raytraced won seven of nine (big room, corridor, church, kitchen through the wall, bathroom through the walls, inside a vent, right outside one); the vent clip that lost lacked the vent's ring, since added.
- **The raytracer's view of the school** (a test harness against the library, outside the game): a closed room of meshes wound Unity's way returned 5% of its sound with a 0.1 s decay, and a wall wound that way let 99% through; wound the raytracer's way, 89% and 2.2 s, and 3% through. In the level: 504 colliders as 727 pieces (482 meshes, 278 single-sided panels given thickness), 90 left out (unreadable meshes: bodies and some desks), built in about 0.7 s. A teammate in plain sight 6 to 9 m away is heard clear (it was muffled to 0.6 before the winding fix); through the kitchen's wall and closed door the voice is at 0.2 and muffled to about 500 Hz.
- **Cost:** host, headless, a teammate talking: 40% of a core without the raytracer, 48% with it; 60 frames a second either way.
- **Your own voice:** recorded in seven places at its final level (the space's answer against the phrase spoken): 26 dB under the voice in the vent, 27 in the church, 29 in the corridor, 32 to 33 in the big room, the shower room and the bathroom, 36 in the courtyard.
- **Pause menu list:** with three players, the list showed the other two with their levels (one muted, one at 150%, set through the test driver) and the menu's title and buttons moved left with the CRT button left in its corner; closed and reopened, the same. A muted teammate's voice reached the host at gain 0; at 100%, at 1.

## 2.0.6

- **Tools by the number of players:** a host alone found one lighter and one map; two players at the school's entrance, two of each, one radar, one revolver. A third player joining mid-game got a lighter, map and bandages of their own (only the bandages in play in that scenario), created on all three games; they picked up their lighter ("claimed by peer 3" on every game) and equipped it.
- **Scenarios, three players with one joining mid-game:** Carnival, Backrooms and PDM each matched on all three games at the same moment (0 differences, about 7,000 objects each). Each ending played for everyone: the Carnival's rocking chair (clown doll from the candle puzzle) and its closing text, everyone held for it; the Backrooms way out and its text; PDM's statue, coffin and lid, and its text; then all three back in the lobby together. Before the fix, the host walked about under the Carnival's closing text.
- **The PDM cross:** the crucified body and the statue head on it sat 0.6 m and 17 degrees apart between host and guest; after, 0 differences (PDM 7,065 objects, the main game 7,148).
- **Legs:** each character filmed walking and crouch-walking from the side, behind and in front, before and after, and the knees close up at three strengths of the halfway joint (none: a point behind the knee; all: a square block; half: a soft crease, chosen). The thigh no longer pulls into a slab at the hip in a crouch, the feet stand flat, Maddy's heels on the floor.

## 2.0.5

- **Unlocks, the coffin ending:** the guest closed the lid. The host's game gave the end-of-game unlocks as the ending played ("Unlocks: the game gave its end-of-game unlocks here"); the guest's game, which does not run that step, gave them too ("end-of-game unlocks given here too (peer 1 beat it): Factory True, Circus True"). Run straight from the host's game: the same on the guest's.
- **Anomalies, the PDM scenario:** the guest found PDM_Anomaly_5: counted in both saves (1 and 1). The host found PDM_Anomaly_1, which the guest's save already had: the host's count 2, the guest's still 1, the anomaly hidden on both. In a new pair of games (the guest found PDM_Anomaly_3, the host PDM_Anomaly_4): 2 and 2, and still 2 and 2 in the next session.
- **Test saves:** every test game wrote to its own `nmm_test_save.dat`; the player's `save_data.dat` was not written by any test run after the change.
- **Voice out of a vent:** the guest crawling in the duct behind the stairwell wall, the host on the stairs 4.4 m away: straight through the wall -12 dB at 800 Hz; heard instead out of the vent's opening at (179.0, 8.5, 35.4), 6.0 m round. The host in the lounge a floor below the duct, no opening in view: silent. The search along the duct covers the whole network (1164 cells) and stays in it; before, it left the duct at a joint and filled its 1600 cells under other ducts across the building.
- **PS1 shader:** a teammate's character draws with `psx/vertexlit` (point-filtered texture), as the Nun and the rooms do.
- **Setup program:** on this PC it found both copies of the game (one with 2.0.5, one with 1.0.1 shown as UPDATE), skipped the test copies, and read the game's key art and its fonts (VCR OSD Mono, Glass Houses) from the copy, checked by SHA-256. Into a fresh copy of the game with none of the mod: it installed BepInEx 5.4.23.3 and the mod, removed the old zip's install scripts beside the game, and the game then loaded "Nun Massacre Co-op 2.0.5" (the build check passed). Uninstalling with the loader turned off left the game as it came. Screenshots of the window with the game's art and with the title card before a copy is found.
- **HUD under the screen effects:** windowed screenshots of the team list, a downed player, a revive in progress and the hiding meter show them under the scanlines and colour bleed.

## 2.0.4

- **The Nun hears voices:** the guest's test voice (a raised voice) with the Nun 4 m away in the same corridor: "Nun heard peer 2 talking 4.0m away (level 0.051)"; she walked to the guest.
- **Footsteps:** on the host's game, the host running past the Nun made a 7 m step noise three times a second, walking a 2 m one twice a second, crouching none; walking up to her she came at the host (state "Reset Can't See Timer", then the chase). The guest walking past her at the other end of the school: 2 m steps on the host's game, and she went after the guest (target 2). The player has no footstep noise of its own in this game (the noise kit on the player: Detectable.MakeNoise and RaycastAlert only).
- **Hearing, a test tone from the guest:** 3 m along the corridor -2 dB (audibility 0.81); next room with the door shut -10 dB at 1.1 kHz; the floor below, right under, -18 dB at 260 Hz; round the corridor's corner heard from the corner; 12 m down the corridor behind the listener -10 dB, low-passed to 7.5 kHz (behind), with the corridor's echo.

## 2.0.3

- A test tone from the guest on the floor below, heard on the host's game: right under the host -18 dB, low-passed to 260 Hz (no words); 2.5 m to the side -23 dB; 6 m to the side nothing; two floors below nothing. No way round by the stairs is looked for. On the same floor, round the corner of the corridor, the voice still comes from the corner (11.4 m round).

## 2.0.2

- **Male walk:** Mike and Mark filmed walking from the front, the back and the side before and after. Mixamo's Standard Walk (before): the ankles 1.1 to 8 cm out from the middle (the left nearly on the line), hip sway 4.2 cm, the forward hand 73 degrees ahead. Mixamo's Walking (now): the ankles 5.3 to 12.3 cm out, the shoulders turning 24 degrees against the hips (Standard Walk 12), the arms swinging 25 degrees back to 30 forward.
- **Voice, a test tone from the guest heard on the host's game:**
  - Round the corner of the corridor into the passage beyond (straight line: three walls and a door, -39 dB): heard from the corner (237.2, 38.9), 11.8 m round.
  - In the next room behind its closed door: straight through the door -13 dB, muffled to 1.5 kHz (the way round goes through the same door: not louder).
  - One floor down, straight under: -17 dB at 560 Hz, a murmur (the way round by the stairs is 70 m).
  - 3 m along the corridor -2 dB (was -6); 11.7 m along it -10 dB (was about -18), with the echo off the corridor's end.
- **Rooms:** the corridor (2.5 m across) echoes; the stairwell (1.5 m wide, 5.6 m long, the next flight 1.5 m over) boxy (reflections -2.1 dB); the big ground-floor room a 1.8 s tail.
- A first regression run failed its "drop" check once (the dropped lighter not yet free in time); the re-run and every later run passed.

## 2.0.1

- Screenshots (1280x720) of the MULTIPLAYER screen before and after: before, the join instructions wrapped onto a third line cut off at the bottom edge; after, two lines well clear of it. The hosting lobby (port, network address, players, roster, START GAME, STOP HOSTING, CHARACTER, VOICE & MIC, CREDITS, BACK) ends above the description line.
- The new VOICE & MIC page: every row shown with its value (VOICE CHAT ON, OPEN MIC, the microphone, < 100% >, the live meter, MEDIUM, < 100% >, ON); switching TALK to push to talk shows TALK KEY; BACK returns to the lobby.
- SETTINGS > MULTIPLAYER: the microphone row read "Microphone (1." over two lines before, "1080P PRO STREAM" on one line after. Names checked: "Microphone (Realtek(R) Audio)" -> REALTEK(R) AUDIO, "Headset Microphone (HyperX Cloud II Wireless)" -> HYPERX CLOUD II WIRELESS, "Mic in at front panel (black) (Realtek High Definition Audio)" -> REALTEK HIGH DEFINITION..., "Yeti Stereo Microphone" -> YETI STEREO MICROPHONE.

## 2.0.0

- **End to end:** one continuous two-player playthrough on the final build, from the bathtub (both wake, the letter, the school entrance together) through every main-game puzzle (candles and the stained glass, the statue, all four VHS tapes and the crosses they reveal, the scale, the clock and its gear, the padlock, the drain, lockpicking the red door, the rope, the electric box and wire clipper, the plank over the barbed wire, the exhaust fan, the window) to the good ending: the Nun set alight, the doll turned ("You are forgiven"), the guest walking into the ending, and both games returning to the lobby together. World fingerprints taken just before the ending: 7,085 objects on each, nothing missing, no differing field. The only rejections were the test driver using the exhaust fan's far side from 19 m (refused by the host's range check, as in earlier runs). Earlier attempts had the two scripted players grabbing the same tape (the host refused the second grab, so one tape short and no crosses); the test's "pick" step can now name which item each player takes.
- **Regression:** NetworkTests 14/14, ProtocolTests 11/11, `test-runtime.ps1 -LateJoin` + `verify-runtime.ps1` PASS (host, guest and late joiner) on the final build.

- **Peeking:** the guest leant fully left and right (the game's head offset, 0.50 m peek distance) at a corridor corner, as Mike, Maddy and Mark, filmed from the host's game from in front, at an angle and close up. Each leant out past the edge with the near hand flat on the far side of the corner at face height ("PEEK lean=1.00 hand=1.00", the grip on the side face, its normal pointing the way they looked). Before the fix the right hand never found the corner (the leant shoulder was already level with the edge), and Mark's and Maddy's wrists were wrung round (their measured palms put the hand 122 to 142 degrees round the forearm): now a hand that would turn that far goes flat the other way round.
- **Crouch:** all three characters filmed crouched still and sneaking (side and front) and crouch-walking up and down the stairs. Posture as shown (test readout): hips 0.55-0.73 m over the floor (0.43-0.49 m before: nearly kneeling), back 40-48 degrees forward on flat ground and 25-32 degrees climbing stairs (the slope taken off), crown 1.30-1.39 m. Clipping while crouched on the stairs: a knee touching a step edge (3 cm) in 4 of 165 frames; nothing else.
- **Feet:** the guest idled, walked, crouched, crouch-walked, ran, climbed and came down the stairs and crawled a duct both ways, the host measuring every frame (toe and heel against the floor under them, and every joint against the scenery). Before: a sole 7-9 cm under the floor in every frame on flat ground, 15 cm on the stairs. After: no frame with a sole in the floor anywhere; on the stairs only a knee brushing a step edge (2.5 cm, 13 of 131 frames).
- **Vents:** in the duct off the kitchen, 248 frames crawling in and 229 back: the body inside the duct except for 8 frames turning round where it narrows (the chest 11 cm into the side for a moment).
- **Idle:** teammates who stop stand in their idle instead of walking on the spot.
- **Protocol 16:** a 1.9.0 guest joining a 2.0 host was refused ("The host has a different game build or co-op mod version.") and left in the main menu.
- **Sync audit, doors included:** the "use everything" step had never opened a door (it looked for the door component on the interactable instead of its parent); fixed, the audit re-run: host and guest each used doors, cabinets and light switches on their floor (within 14 m, then within 30 m), the guest fired the revolver, the host set a bear trap, and a third player joined 50 s in. Fingerprints: 7,098 and then 7,117 objects on all three games, nothing missing, no differing field (host vs guest, host vs late joiner).

## Animation pass (1.9.0)

- Filmed the guest's avatar from the host's game playing each new move in turn: the throw (arm overarm), the hit (arms flung, with the blood burst), the floor pick-up (bent right down, hand to the floor), the reach to take and the pull to open. Each clip's start is logged ("playing=NMM.Anim.Throw", "NMM.Anim.Hit").
- Pose sheet of all four characters: the new reach, pick-up and hit poses, the prone pose, and the body (on the back, one knee up).
- The whole pose now steps at twelve poses a second (StepPose after the pose, wall shove and head look); the 3-player regression and the unit tests pass with it.

## Sync and late-join audit (1.8.0)

- Main scenario, three games: the host used 10 doors, cabinets and light switches on the 3rd floor and the guest 10 on the 2nd, the guest fired the revolver, the host set a bear trap; a third player joined 50 s in. World fingerprints 45 s later: 7,098 objects on each game, no object missing on either side, no differing state field (host vs guest, host vs late joiner). With positions compared too, the only differences were single 20 cm grid steps from quantised positions, and a statue head that is still moving at level start.
- Late joiner and players: with the host knocked down before the late joiner arrived and the guest under a bed, the late joiner showed the host downed in the same pose and spot as the guest's game, the guest prone under the bed, and the Nun at the host's position for her.
- Carnival, Backrooms and PDM, three games each (late joiner 45 s in): fingerprints matched (7,021, 7,085 and 7,034 objects) except the opening-text machine, where guests were still in the scroll after the host had finished; fixed (both guests skipped the rest once the host was through and could move).
- Backrooms: the guest walked into the way out; the host's game started the same ending ("watching the team ending"), both showed the closing text, and both returned to the lobby.

## The Nun, part two (1.7.0)

- The Nun's body collider on the guest's game measured 0 m tall at her feet before the fix; afterwards 1.40 m (centre 0.85 m, radius 0.25 m), the same as on the host's. The guest shot her from 6 m: she went into her retreat and ran to her retreat point (before the fix the same shot never hit her).
- The guest fired on the 3rd floor with the Nun 24 m away on another level: she walked to where the shot was fired ("Peer 2 fired at ... the Nun hears the gunshot"). The host heard the guest's gunshots: volume 0.68 from far below, 0.60 and muffled to 4.7 kHz through a bedroom wall.
- The guest knocked down beside her: "Nun stands over peer 2 (3.5s)", the guest's jolt ended, "Nun leaves the body"; about 30 s later "Nun goes back to check on peer 2", and again later.
- Her vent taunt and the poison gas switch are not reachable in this game build: the only transition into them is switched off in the game's own state machine and nothing else sends the event. Left as the game has it.

## The Nun hunting every player (1.6.0)

- The guest on the 3rd floor, the host far below; the Nun put 6 m from the guest and alerted: she went for the guest, her jump scare fired on the guest's own view ("Nun chase effects: on peer 2"), the guest's screen showed the VHS jolt and motion blur (screenshots), the host's stayed calm with motion blur off. Her chase music: gain 1.00 on the guest's game, 0.78 and 0.45 on the host's (15 to 24 m away); 0.10 on the guest's while she was after the host.
- Her nearest search spots, measured from her quarry: 2.0, 3.0 and 9.3 m from the guest once she was after them (all out of range while she measured from the host far below).
- Two players apart, nobody in sight: she chose the guest (alone and nearer) and, sent to lie in wait, walked to a hiding spot 3.6 m from the guest. Logged "tuned for 2 players".
- Two players under one bed: the grab, drag-out and attack as in 1.4.0; the downed guest's jolt ended, and moved to the host when she turned on them.

## The MULTIPLAYER screens restyled (1.5.0)

- Captured the MULTIPLAYER screen offline, hosting, as a connected guest, and as a third copy seeing the host's game in its network list ("HOST'S GAME 2/4 LOBBY"), plus the CHARACTER and CREDITS pages: every page built from the PLAY screen's rows and buttons over the dimmed menu room; HOST GAME, CHARACTER, CREDITS, BACK and the CHARACTER page's BACK all worked when clicked.
- Windowed host and guest in the level: the guest knocked down showed YOU'RE DOWN with the bleed-out bar as a VCR slider; the host's hold-Tab TEAM list showed the guest DOWN 4:56.

## Endings together, the bed check, lying under a bed (1.4.0)

- Good ending and coffin ending, host and guest: the ending started on one game and ran on both at once; both showed the end screen, teammates' bodies were hidden during it, and both returned to the lobby.
- Two players under one bed (3rd floor), the guest whistled with the Nun 7.8 m away: she walked to the bed and stooped at it without flipping it ("Nun reaches under bed interactable mesh and drags 2 out: peer 2, peer 1"); the guest was hauled 0.84 m and the host 0.56 m out to their stand-up spots, both ending 0.75 m from her, stood up, and she knocked the guest down. Filmed from beside the bed: the bed stayed flat throughout.
- The guest alone under the bed: she flipped it (on both screens) and the guest was turned out from under it with the game's own state change (StandFromHiding), not dragged; she then knocked the guest down. The host, standing nearby, was not touched.
- The host alone under the bed: the game's own step turned the host out; the guest was not touched.
- Lying under a bed (Mike): hands and forearms on the floor (5 cm up), head 20 cm up; 0 of 175 frames had any part of the body in the bed. All three humans rendered in the new pose; it holds still while looking around (the head stays down when looking up).

## The Nun searching a bed (1.3.0)

- Host and guest hid under the same bed (3rd floor); the Nun was put in the corridor outside, 7.8 m away through the door. The guest whistled: she walked to the bed, lifted it (the bed lifted on both screens), and pulled out the guest and then the host ("Nun pulls 2 out from under bed: peer 2, peer 1"); both were dragged out and stood up, she waited 2.8 s, then attacked the guest (40, 40, 40: knocked down) and left the host alone. Filmed from beside the bed.

## Gestures and idles (1.2.0)

- The guest pointed (looking 35 degrees right and 15 up) and whistled; the host saw the right arm go out towards that direction and the left hand go to the mouth, and logged the whistle as a noise the Nun hears.
- Filmed standing still (breathing idle), standing still holding the radar (item arm kept), crouched still (Mike and Maddy) and crouch-walking into a stop: 0 of 187 and 0 of 134 frames clipping.

## Settings page, realistic voice, credits (1.1.0)

- SETTINGS → MULTIPLAYER captured in the main menu: the entry sits above BACK, the page shows voice chat, talk, talk key, microphone, level, mic test, voice gate, voices and Nun hears you in the game's own rows; TALK switched push to talk to open mic. The page is added in the pause menu's settings too (logged for the level scene).
- Voice heard by the host from a guest playing a test tone (logged): same corridor 3 m facing them -6 dB full range; facing away -10 dB, 4.5 kHz; 12 m down the corridor -18 dB; behind a wall 4 m -18 dB, 900 Hz; behind a closed door 4 m -13 dB, 1.4 kHz; one floor below 5 m -28 dB, 380 Hz (strengthened after this to about -31 dB).

## Holding items, head look, crouch (1.0.26)

- Mike holding each item in turn (lighter, radar, hammer, drain cleaner, VHS tape, map, bandages), filmed from the front and side: each in a closed fist, palm in and thumb up, upright as the game holds it. The wooden board went diagonally across the body in both hands, standing and walking; putting it down raised no errors.
- Head direction against where the guest looked (logged): standing looking up 30 and down 30 degrees, head 30 and -30; crouch-walking looking up 25, head 25; crawling in a vent looking 70 degrees to the side and 20 up, head 70 and 20; looking ahead and 20 down, head 0 and -20.
- Crouched walk filmed for Mike and Maddy: short steps, back straighter, arms close in; 0 of 189 frames clipping.

## Runs, crouched walk, jumps, revolver (1.0.25)

Filmed in the game (host watching the guest):
- Maddy running a corridor at 5.5 m/s, twice: run clip, 0 of 283 frames clipping.
- Maddy crouch-walking at 1.5 m/s: the sneak faces ahead, at crouch height, 0 of 181 frames clipping.
- Jumps at the game's own jump speed and gravity (1.2 m up, 0.5 s in the air): Mike standing and running, Maddy standing. Knees tucked and arms up in the air, landing dip after, 0 of 134 frames clipping.
- Mike picking up the revolver, holding it out two-handed, firing three shots: each shot reached the host (shot count 1, 2, 3) and kicked the arms.

## Clipping on slopes, stairs and in vents; PS1 walks (1.0.24)

Measured in the game with a probe (test step `clipwatch`): every frame, each joint of the teammate's body is a ball about as thick as the body there, and any scenery cutting into it more than 1.5 cm counts as clipping. Before these changes the vent crawl clipped in 191 of 329 frames (chest, head and thigh fully into the duct wall) and a teammate's legs went through a duct's end wall.

| Filmed (host watching the guest) | Frames | Clipped | Worst |
|---|---|---|---|
| Maddy walking a corridor at 3 m/s | 235 | 0 | - |
| Mike walking a corridor at 3 m/s | 234 | 1 | foot 1.7 cm (touching) |
| Mike walking down the school stairway | 167 | 0 | - |
| Mike walking up it | 183 | 0 | - |
| Maddy crawling 5.6 m up a sloping, bending duct | 303 | 0 | - |
| Maddy at the duct's dead end | 314 | 0 | - |
| Mike downed, crawling up the stairway (1.9 m up, 3.8 m along) | 333 | 0 | - |

- Walks render stepped (12 poses a second) for Maddy and Mike, upright, arms swinging; Maddy's no longer crosses her feet or flicks her wrists out.
- The test scripts can now wait for the level and for a teammate (`waitscene`, `waitpeer`), and the host can drive a guest's steps (`peer`), so the guest's actions and the host's filming line up whatever the load times.

## Crawling in vents, PS1 stepping (1.0.23)

- A crouching guest in a 1.2 m vent duct, filmed moving through it: on hands and knees with the crawl, head under the duct's top. In the open corridor the same crouch stays a crouch.
- The downed crawl filmed at 20 frames a second: the pose changes about twelve times a second, the body moving smoothly between.

## Motion-captured crawl (1.0.22)

- The Mixamo crawl (80 frames, 0.63 m per cycle) fitted onto Mike, Maddy and Mark, rendered at four points of the cycle: hands and knees on the floor, lower legs flat behind, planted hands flat.
- Walks rendered for every character at four points of the cycle (Standard Walk on Mike and Mark, Female Walk on Maddy, shorter steps on Mandy), and filmed in the game at the game's walking speed (3 m/s) for Maddy and Mike: steps and arm swing, feet on the floor.
- A downed guest crawling in the game, seen from the side: on all fours on the floor, nothing under it. Crawling into a wall: the head and hands stop at the wall, the body further back (before this, the head went into the wall).

## Safety net, rejoining, host console (1.0.21)

- A third player joined after a guest had unscrewed the vent cover: before, the joiner's vent cover machine and its four screws were back at their first step (five differences from the host); now the joiner catches up and the worlds match exactly.
- A guest dropped 40 m below the map was put back where they last stood ("You fell out of the world and were put back").
- A guest walking into the standing host stopped 0.55 m from them, with the game's own movement running; the host was not moved.
- Coffin ending with the host standing right beside the coffin: the guest lay down and closed the lid, nobody was pushed or fell, and both returned to the lobby.
- Console: players, bring, tp (to a spot), spawn, unstuck, goto, revive (a downed guest got back up) and an unknown name all behaved; commands written to nmm-commands.txt while hosting ran within a second.
- Crawling hands rendered flat on the floor for every character, fingers ahead.

## Vent crouch and downed crawl (1.0.20)

- Inside a 1.2 m vent duct a crouching guest, seen from a metre away, crouches with the back hunched and the head tucked: head bone 0.95 m above the duct floor (1.19 m in an open room), the head under the duct's top.
- A downed guest crawling forward is on hands and knees (head about 0.5 m up), knees on the floor and lower legs flat behind, hands alternating with the opposite knee (four phases rendered for every character); crawling into a wall, the head stops at the wall instead of inside it.

## Controls after the opening, held items (1.0.19)

- A real four-player session's log (1.0.17) showed a Rewired error on every frame from the first knockdown on: the mod's input had gone stale when the level changed, so reviving, giving up and spectator switching could never register. A test that starts in the opening, walks into the school and is knocked down now logs no Rewired errors at all, and the downed player's controls read normally.
- From the opening: the host opened the letter while the guest was still in the tub: "Wait until everyone is out of the bath (client)". Once the guest was up, the letter took both outside; both entered the school, were knocked down together, bled out as a team, and both respawned with two lives left.
- Inside a vent duct (1.2 m high), a crouching guest is seen from a metre away on hands and knees, the head about 0.6 m above the duct floor, clear of its top. The school has no vent enter/exit triggers, so before this only the test flag ever showed the crawl.
- A teammate holding the radar, the hammer and the lighter, seen from behind them: each held upright in the raised hand, the radar's screen towards its holder.

## Downed, revived and spectating (1.0.18)

Player input was frozen for these runs: the hidden test games otherwise also receive this PC's keyboard and mouse.

- Upstairs by the candles, a guest crouched and was knocked down: the view settled 0.38 m above the floor (before, the fixed drop put it under the floor when crouching). The guest crawled about a metre, the view staying above the floor.
- The host stood beside them and held Interact: the revive ran to completion (17%, 68%, done) and the guest was back up with 35 health.
- Out of lives, the guest's Interact cycled behind the host, through the host's eyes (from the host model's head), the free camera, and back, every press a different view.

## A tool for every player (1.0.17)

- Host, guest and a third player who joined before the school loaded: the host switched to tools for three players and told the others; all three games built the school with three of each tool (three lighters side by side in the cabinet drawer, none overlapping), and the host and guest worlds matched (7281 objects, 0 differences).

## Online play without a VPN (1.0.16)

- Unit tests against a fake UPnP router: the port is forwarded to this PC with a one-hour lease, renewed and removed; when another computer has the port the next one is used (and the invite carries it); routers that allow only permanent forwarding get one; a router whose internet address is shared (100.64/10) is reported and the useless forwarding removed; a router that refuses says why. Invite codes survive lower case, missing dashes and O/0, I/L/1 mix-ups; a mistyped code fails its check; addresses and host names are not mistaken for codes.
- In the game, against a fake router on loopback with every guest treated as coming from the internet: the host's router forwarded the port and the lobby showed the invite code (screenshot); a guest and a late third player without the key were turned away with "This game is online: join with the host's invite code"; with the key both joined and played; the forwarding was removed when the host quit. Automated test runs never contact the real router.
- Not yet tried against a real router and a friend on another internet connection.

## A full playthrough in one session (1.0.15)

One continuous host + guest run (Maddy), started in the opening and played to the good ending without restarting, the Nun active throughout (hits switched off by the test driver so a knockdown could not stall the script). Each player carried at most three items, as the game allows, and put tools down once done with them.

- Opening: the host got out of the tub and opened the letter while the guest was still in it: "Wait until everyone is out of the bath (client)". The guest got out, the letter took both outside, and the school entrance waited for both, then loaded them together.
- Candles: one placed by each player, both lit by the guest; the cross fell, the glass broke, and the guest put the statue head on the statue (coffin ready).
- VHS tapes: two each; the crosses appeared and both players climbed the floating cross.
- Scale: three body parts from the host, four from the guest; the scale broke, the guest fitted the gear, and the host set the clock.
- Padlock (host, 7935) open; drain cleared and the red door lockpicked (guest), its wrench handed back and put down.
- Rope soaked with alcohol (guest) and lit (host; the lighter came back to the host); electric box opened and the wire cut (guest); the fan stopped, and the host and the guest climbed through it from either side. The plank went over the barbed wire (host).
- Window broken by the guest's thrown rock, door unbolted.
- Good ending: the guest poured the gasoline, the host lit the Nun, the doll turned to "You are forgiven", and the guest walked into the ending. Both games returned to the lobby.
- World fingerprints just before the ending: 7085 objects on each, none missing, 0 field differences.
- Separately, the coffin ending (guest lies down and closes the lid) returned both to the lobby.

Earlier attempts at this run found the letter bug fixed in 1.0.15; their other failures were the script's own (two players reaching for the same tape at the same moment, where the host correctly gives it to one; an inventory left full; dropping an item while facing a wall, which the game refuses).

## The dead stay where they fell (1.0.14)

- A guest (Maddy, then Mark) with no lives left was knocked down beside the host and bled out: on the host a body was left at the spot where she lay (on the floor, 2 cm from her last position) while her spectating avatar stayed hidden, and her own game left the same body for her spectator camera. A picture from beside the host shows the body on its back with its arms out. All four characters were rendered in the pose.

## Teammates react (1.0.12, groans removed in 1.0.13)

- A guest playing Maddy picked up the lighter: the host's copy of her reached for it (one reach). She took 20 damage and was then knocked down: the host played two hurt reactions (the game's blood burst copied onto her avatar, a flinch, and a pain cry from her position), and nothing more while she was down.
- Every character was rendered reaching for a handle, reaching to the floor, and flinching; the Ghost was rendered dragging itself in the crawl pose.

## Spectator light, footsteps, crawling (1.0.11)

- A guest ran out of lives and spectated the host: the player's "Blue light - Nun Massacre" stayed 0.4 m above the spectator camera (it used to go 2500 m down with the parked body), and the spectator view kept the blue tint (rendered from the player's own camera before and after).
- The guest walked 3.6 m: the host played teammate footsteps at the game's own pace (stepInterval 5, walk speed 3: a step every 2.5 m of walking).
- The guest reported being in a vent: on the host its avatar went to hands and knees within a second (hands and knees on the floor, pelvis 0.46 m and head 0.57 m up), and stood again on leaving. All four characters rendered in the crawl pose, still and moving.

## Getting out of the bathtub (1.0.10)

- The host got out of the tub (from inside it, as a player does) while the guest watched: sampled every 0.5 s, the guest's copy of the host stayed lying at the tub for the whole 4.5 s climb, then stood at the spot beside the tub in one step, with no positions in between.

## Looking around while lying down (1.0.9)

- A guest in the bathtub turned their view 60° and then 120° the other way: their avatar's body stayed in place in the tub (root position and facing unchanged) and only the head turned (screenshots from above the tub).
- Two players under one bed; the guest turned their view: from the host's eyes, the guest lies propped on the elbows beside them and the head turns from facing the host, to away, to the other side. From the room, only the head and shoulders show at the bed's edge.

## The opening together (1.0.8)

- The host got out of the bath and opened the letter while the guest was still in it: "Wait until everyone is out of the bath (client)", nothing happened. The guest got out and read it: both games played the letter's sequence and both players arrived on the hill, 0.9 m apart.
- The host walked into the school entrance alone: "Wait for everyone: you go into the school together." When the guest walked in beside the host, both games loaded the school together.

## Shared scares (1.0.7)

- The host walked into a ghost-child scare with the guest about 47 m away: held back ("not everyone is close"), the trigger stayed armed. When the guest walked in beside the host, it fired once for both ("Scare: ... chance High25"), and the ghost child ran and vanished on both games at the same moment (before the fix it stood frozen on the guest).

## Holding things together (1.0.7)

- Host and guest both grabbed the same door: both held it (no refusal). The guest let go: the host still held it and it could still move.

## Two-player edge cases (1.0.6)

- Paperclip grabbed by host and guest in the same instant: one count (2 players, each showing 1).
- Same door grabbed by both: the second got "Someone else is holding that." (Changed in 1.0.7: both may hold it.)
- Guest knocked down; host revived with a first aid kit: the kit stayed in the host's inventory until the guest's game confirmed the revive, then was used up.
- Guest knocked down while zoomed into the sink close-up (field of view 40): after the revive, field of view 70, camera on the head, level, normal controls.
- Guest disconnected inside the padlock close-up: the host then opened the padlock normally. Guest disconnected while picking the red door's lock: the host's keyhole went back to ready (not busy), and the host took the wrench from the keyhole and picked the lock.
- A late joiner (third player) got the trap thrown before they joined (it was missing before: the snapshot referred to it, but the trap itself was dropped).
- Coffin: the guest lay down; the host's attempt to close the lid was refused; the guest closed it and the ending played.
- Acid vent: the guest died in it and became a spectator ("You died - spectating your team"); the host kept playing in the school.
- A guest disconnecting while trapped in the barbed wire pit with the spare lighter: it dropped by the cabinet where they last stood on walkable floor, not in the pit.
- Not exercised directly: the Nun keeping her victim during a bed drag, and switching tools a moment before using a puzzle (both covered by the normal puzzle runs passing).

## PLAY choices and spares (1.0.5)

- Item randomisation on (host's choice; the guest's own setting was off): both games scattered the items identically from the host's seed, so both had the same 6738 synced objects and every randomised item in the same place. Each of the five tools had its spare beside it on both games. A first version put a spare off a table edge, where it fell 2.6 m away; spares now need a surface under them.
- Scenario: the host chose PDM; the guest built the PDM layout too (7034 objects on each, fingerprints identical).

## Teammates take up room (1.0.4)

- The game ignores collisions between Player-layer objects, so a guest walking straight at the host passed through to 0.01 m. Teammates now carry a body blocker on the DontCollideEnvironment layer (which collides with players and is not looked up by the game's scripts): the guest stopped 0.55 m from the host's centre.
- Both start on the same spot: the blocker stays off until they are apart, so neither is trapped (the guest walked into the host from the shared spawn without sticking).
- Two players under the same bed and crawling out to their own places still work with the blockers on.

## Hiding together (1.0.3, host + two guests)

- All 17 hiding beds in the Nun scene are the same single bed (1.1 × 2.4 m, entered from the long side); each has room for a second place 75 cm along the bed, with space beside it to crawl in and stand up again.
- The host went under a bed and got the game's own place; the guest went under the same bed 3 s later and lay 75 cm beside them (both in the hiding state). The third player was told "No room under this bed: two are already hiding there." and stayed out. After the guest crawled out (to their own exit point), the third player got the free place.
- Screenshots from each hider's eyes, turned to the side, show the other player lying beside them under the bed (Mike and Mark); from the room, a hiding teammate's head and hands show at the bed's edge; nothing sticks out at the far side.

## Shared puzzles and a full playthrough (1.0.2)

Host and guest in the Nun scene, scripted steps, with a fingerprint of every synced object and puzzle machine compared between the two games after each run (rotations within 12°; ragdolls, loose props and animated bodies left out).

- Candles and stained glass: the host placed one candle and the guest the other; the guest lit both with the lighter. Candles reached 2 on both, the cross fell, the glass broke, and the guest carried the statue head to the statue (statue state 2 and the coffin active on both). Fingerprints identical.
- VHS tapes: host and guest each inserted two tapes into the four VCRs. Tapes reached 4 on both and all four crosses appeared; each player took a cross and climbed the floating cross. Fingerprints identical.
- Good ending: the guest poured the gasoline on the caged Nun, the host lit her, the doll became "You are forgiven" on both, and the guest walked into the ending. The host kept playing untouched until the lobby return.
- Coffin ending: the guest lay down and closed the lid; the host stayed free (not moved into the coffin), then both returned to the lobby.
- Acid vent ending: the guest triggered it; the Nun watching, the steam and the acid ray ran on the guest's game only (no longer switched off by the host's copy), the guest died, the credits ran, and both returned to the lobby.
- Barbed wire: the guest trapped in the pit gets the ending's Nun on their own game; behaves as in single player (the host alone behaves the same).
- Rope and cabinet, wire cutters and exhaust fan, plank over the barbed wire, window: done by the guest (the rock thrown by the guest broke the glass for both, then landed where the host picked it up); host and guest each climbed through the fan. Fingerprints identical.
- Lockpicking (guest): solved on the guest's screen; the host's keyhole machine followed (solved, not busy, state 2), the red door unlocked for both, and the wrench went back to the guest. Before the fix the host's door stayed locked.
- Padlock (host): the host's combination (7935) opened the locker for the guest too, and the host got control back.
- Spare tools: both games create the same spares (7518 synced objects on each, fingerprints identical); host and guest each took a lighter and a revolver.
- Opening scene (Outside Final): the guest got out of the tub while the host stayed in it with their own "Get out?" prompt, and got out later by themselves. The host walked into the school while the guest stayed in the tub, untouched; the guest then got out, walked in and joined the host in the school. The teammate in the tub lies back in it (head at the player's eye position, checked from screenshots for Maddy and Mandy).

## Release smoke test (1.0.2)

- Unit suites (network 9/9, protocol 11/11) and the three-player regression (host, guest, late joiner; pickup, drop, cabinet state within 2 cm, characters loaded, no sync errors) pass on the release build.

## Release smoke test (1.0.1)

- Unit suites (network 9/9, protocol 11/11) and the three-player regression (host, guest, late joiner; pickup, drop, cabinet state within 2 cm, characters loaded, no sync errors) pass on the release build (the regression now picks the nearest item that is not pinned to a puzzle mount, and clears old reports before a run).
- Knockdown and revive: the guest took lethal damage and went down (5-minute bleed-out, counting down). The host walked over and held Interact (progress 0.25, 0.75, done), and the guest got back up with 35 health. Both saw the notices.

## Spectator free camera (1.0.1)

- The guest was put on their last life and bled out, which put them into spectating (following the host). Jump toggled the free camera, which flew 9 m in 2 s at walking speed (4.5 m/s; 12 m/s with Run). Toggling back glided the camera back behind the host. With nobody left to follow, the free camera starts on its own.

## Starting together (0.9.2, host + guest at the main menu, headless)

- SKIP AHEAD: the guest started loading 2 ms after the host, on the same loading-screen variant. With the guest's report delayed 8 s for the test, the host held its loaded level until the guest reported; both entered the Nun scene 43 ms apart. Without the delay both entered within 33 ms, synced (both ready, both in each other's roster).
- PLAY INTRO: same flow into Outside Final; entered 74 ms apart, both ready.
- A guest not in the main menu answers at once so the host is not held up; the host enters after 60 s without a slow guest, and a guest never waits more than 120 s or after losing the host.

## Menu and lobby

- MULTIPLAYER entry on the main menu, built from the menu's own widgets (screenshot checked).
- Host from the menu; second process found it under GAMES ON YOUR NETWORK (`HOST'S GAME 1/4 IN LOBBY`) and joined with one click, despite a different port typed locally.
- Lobby roster on both sides; host START GAME → SKIP AHEAD took both into the Nun scene; PLAY INTRO took both into `Outside Final`.
- Guest pressing PLAY is redirected to the lobby; host leaving mid-game returns guests to the MULTIPLAYER screen with the reason.

## Performance (host + guest, Nun scene, headless)

| | 0.1.0 | 0.3.0 |
|---|---|---|
| Host frame, average / worst | 286 / 710 ms | 33.5 / 43 ms (game's 30 FPS cap) |
| Guest frame, average / worst | 130 / 790 ms | 34 / ~85 ms |
| Host world capture per tick | 254 ms | 3–6 ms |
| Guest snapshot apply | 66 ms | 3 ms |
| Periodic rescans | 350–550 ms every 15 s | none after load |

## Frame pacing (0.9.2, windowed, Nun scene, ~82 fps display cap, `-nmm-perf`)

| | 0.7.0 | 0.9.2 |
|---|---|---|
| Guest: mod main-thread cost per frame, average / worst | 1.8–2.0 / 50 ms | 0.23 / 1–4 ms |
| Guest: 99th-percentile frame | 36.4 ms | 18.2 ms (same as playing offline) |
| Guest: garbage collections per 10 s | 11–12 | 5–6 (offline: 0–1) |
| Host: mod main-thread cost per frame, average / worst | 1.0 / 9–14 ms | 0.64 / 4–5 ms |
| Host: world upload | 39 KB/s | 30 KB/s |

- The guest stall: every 0.5 s, keeping guest-side physics and AI switched off re-classified every synced component (about 50 ms). The components are now classified once per object rebuild.
- JSON work moved off the game thread: packets, including world snapshots, are parsed on the network threads, and the host encodes world deltas on one ordered background thread. Broadcasts are encoded once for all peers.
- Props spun by the game's cosmetic Rotate scripts are no longer pose-synced, because every peer animates them itself. Physics that never settles (hanging chains) is sent at 2.5 Hz after 2 s of small continuous movement. Large moves are still sent immediately.

## Quit / disconnect

- Before the fix, a host quitting with a connected guest froze in ~5 of 7 runs. After it, every host quit and mid-game STOP HOSTING completed, including the three-player regression run.

## Nun versus teammates

- Host forced a chase; the Nun retargeted to the guest's avatar, closed in, and three weapon hits (40 each) were routed to the guest, who was knocked down.
- After the knockdown she stopped the chase, lost sight briefly and returned to searching.
- Fixes found by these runs: avatars needed a solid body (she walked into the trigger), a blade-to-body distance test (swing started inside the capsule), and `AlwaysAnimate` on her animators (off-screen culling froze the weapon).

## Knockdown, revive, lives

- Lethal damage → downed (camera low, controls locked, bleed timer); host got "client is down".
- Host held interact next to the downed guest: progress 0.25 → 0.74 → revived at 35 health on the guest; "is back up" notice on the host.
- Bleed-out → one life lost through the game's own LoseLife sequence → respawn at the respawn point.
- Both players down → "Everyone is down" → both bled out, lost a life and respawned.
- Last life lost → spectating; host then out of lives → team game over on both → both regrouped in the lobby, guest still connected.

## Characters

- Scientist and Ghost (0.9.2): their rigs map onto the standard bones; the pose sheet shows all four characters in every pose. The Ghost wears a robe wider than its shoulders: its arms keep the model's own outward angle (plus 2°) so they stay outside it, its legs walk at 60% stride so the robe stays closed, and it leans instead of folding its legs to crouch or kneel.
- Carried light (0.9.2): every player body carries an always-on scenario fill light, which made every avatar show a phantom flashlight beam (and told the Nun teammates had a light on). Only world-lighting lights in the camera rig count now; all three regression peers report no light when not carrying one. Not yet exercised with a real flashlight in hand.
- Both supplied characters render on all eight procedural poses (idle, walk, run, crouch, holding an item, kneeling to revive, downed, prone), checked on a pose sheet. Maddy's (the secretary's) A-pose arms are corrected to hang naturally like Mike's.
- CUSTOMIZE page (screenshot checked): only the character selector, with a still preview that slowly turns while facing the camera.
- Guest switched character in the lobby and again mid-session: the host's roster showed `client - SECRETARY`, then `client - CASUAL GUY` (the names before they became Mandy and Mike), and the guest's avatar was rebuilt as the new character in the level (rendered from the host).
- Protocol version 5: 0.4.x peers are rejected with the version message.

## Calling the Nun (host + guest, headless, scripted positions)

- Shout in view mid-chase: she was chasing the guest when the host called from behind her, 9.4 m away. She stopped in place (agent speed 0), turned about 200° in 1.1 s (yaw 59, 110, 155, 200, 245, 257), screamed, entered her own chase start and ran to the host. In a second run the guest called her off the host, and she ran the guest down.
- No spinning: a call from the other player 2 s after a switch was ignored ("she only just switched"). A call while she was within reach of her target was ignored, and a call from out of her sight was ignored.
- Hiding spot: the host hid under a bed and she ran to drag them out. The guest shouted 14-15 m away behind a wall, and she stopped and turned toward the voice. With "follow" she walked to where the voice came from (arriving within 1.4 m), searched and found the guest; the host stayed hidden. With "go back" she resumed her run to the bed after the pause, and a second shout during that run was ignored.
- Shouts reach the host as a dedicated "shout" request (protocol 6). A shout she answers herself is not also handled by her own hearing.

## Runtime objects

- A bear trap created on the host was recreated on the guest at the same place (`dyn/1/Bear%20Trap`), with its landing pose synced.

## Proximity voice

- Host and guest each transmitted a synthetic speech-like signal (`-nmm-voice-test`, no microphone). Over a minute each sent about 1,000 packets and the other received them all; both played the voice through the game's audio output (about 1.7 million output samples at 48 kHz), and the talking indicators followed the bursts.
- The VOICE SETTINGS page listed the PC's real microphone; headless test processes never open a real microphone.
- Hiding meter (windowed host, synthetic voice): the test crawled under a bed through its own interaction (player state `Hiding`). The meter appeared in the supplied outline. Its fill rose and fell smoothly with a soft top edge, and turned from cream through salmon to red as the level rose (checked frame by frame at 0.1 s). The voice drew the Nun at most every 2 s while fully red. She was 48 m away, beyond the noise's 12 m reach, and correctly did not react.
- Name tags and talking indicators are removed; the three-player regression ran clean afterwards.
- Room acoustics (0.9.2, host + guest with synthetic voice): the host measured the space around the guest twice a second (13 rays) and set that voice's reverb. A 2.4 m room gave a 0.5 s tail, a 3.1 m room 0.8 s, and a 4.7 m room 1.2 s (halls go up to 3.2 s; open air is dry). Flagged as in a vent, the voice switched to no room reverb, a 650 Hz high-pass and a metallic slap-back echo.
- Not yet verified by ear: the acoustics, 3D placement, wall muffling and push-to-talk with real microphones.

## Not yet exercised (as of 0.9.2)

Full human playthroughs (every puzzle and ending was run by the test driver, not by people), the alternate scenarios, the under-bed kill against a teammate, the revolver against the Nun from a guest, and play over a real LAN/VPN between two PCs. Since then the scenarios (2.0.6), the bed check against teammates (1.3.0 to 1.4.0) and a guest firing the revolver (1.0.25, 1.8.0) have been run by the test driver; human play and two PCs still have not. The current list is in RELEASE-STATUS.md.
