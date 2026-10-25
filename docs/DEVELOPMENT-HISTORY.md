# Development history

The test builds that led to the 1.0.0 release, newest first. They were numbered 0.3 to 2.0.9; the last of them, 2.0.9,
was released as 1.0.0.

## 2.0.9 (released as 1.0.0)

- **NUN VS PLAYERS removed.** The mode where a player was the Nun is gone, with everything that came with it: the lobby's MODE and THE NUN rows, the Nun player's controls and powers, the white shape of a hider through a bed, and the lowered Nun voice. The game is co-op against the game's own Nun, as before 2.0.8. Everything else from 2.0.8 stays: the Nun's hearing choice, the raytraced game sounds, the vent voices, the lobby and the pause menu's player list.
- **The Nun hears through the building, on the raytraced acoustics.** On the host's game she has her own ears in the raytracer: her rays go out to every player's mouth and to wherever a noise is made, and what comes back is how much of it gets to her through the school: all of it down an open corridor, less round a corner or through an open door, little through a shut door, a wall or a floor. A noise (something dropped or thrown, a door, glass, a whistle, a footstep) is held back a moment while her rays go out to it, and goes on to her only if enough of it gets there; a voice is weighed the same way, and so is a shout or a whistle from under a bed (before, that went by a straight line: half as far with a wall in it). Before, the game's own noises reached her through any wall as if in the open (the game's hearing has no walls), and voices went by the mod's own simpler walls. Without the raytracer (if it cannot start) the mod's own acoustics stand in. (ORIGINAL hearing, the game's own, was later removed with the lobby's hearing choice.)
- **Her hearing gets sharper with the difficulty.** In the open a noise reaches her 1.25 times as far as in the game alone on Easy, 1.6 times on Medium (it was 1.5 times on every difficulty), twice on Hard, two and a half times on Extreme, and she needs less of a voice the harder the game. Medium is unforgiving: something thrown (a 10 m noise in the game) sends her to it from 10 m down an open corridor and makes her stop and listen from 16 m; through the floor right under her, ordinary talking brings her. On Easy she needs a louder voice: through a floor she stops to listen at the first words and comes only if the talking goes on.
- **She investigates what she hears.** A faint sound makes her stop, listen and turn to face it (for a moment, at most every few seconds, and not while she can see someone). A clear sound, or faint ones that keep coming from the same place, send her to where it was made, by the way she can walk there (up or down the stairs if it came through a floor), and her search then covers the rooms round that spot instead of patrol points picked by chance. Once she is on a sound, a new one elsewhere only draws her off after a few seconds or if it is clearly louder; the same sound again just keeps her at it. She gives up after a while if she finds nobody.
- **A loud moment is not a shout.** A voice with one sharp peak (a laugh, a plosive close to the microphone) used to count as a full shout to her, so ordinary talking could carry through a floor on Easy. A loud voice now counts at the level it was measured at.
- **Under a bed, one way of hearing.** A shout or a whistle from under a bed is weighed by her raytraced ears like any other sound. It also went through an older, simpler check, and either one could send her to the bed. The older check went with the lobby's hearing choice (below).
- **Always on: raytracing, your own echo, and the Nun hearing you.** The settings no longer have RAYTRACING, OWN ECHO or NUN HEARS YOU (the lobby's VOICE & MIC page neither): the raytraced acoustics, the space's answer to your own voice, and the Nun hearing every player's voice are part of the game for everyone. The host's NUN HEARING choice (ORIGINAL, CO-OP, CUSTOM) is gone too, with its rows in the lobby's GAME box: in multiplayer she always hears as in CO-OP. If the raytracer cannot start, the mod's own acoustics stand in.
- **Every ending, together.** An ending only goes off once everyone still playing is at it, as the school entrance and the scares already wait for the group: walk into it or try it alone and you are told who you are waiting for, and after a minute the team goes ahead without anyone who has not come. The acid vent and the barbed wire are team endings now too: they took only the player who walked in, who then watched the others play on; now they are everyone's end, played on every game, and the team returns to the lobby.
- **One ending for everyone.** Two players setting off different endings at the same moment could each start their own; the host now settles on the first, and a guest's ending waits for the host's word before it plays. A scenario's closing text, which follows its own ending, counts as the same ending. While an ending plays, nobody bleeds out and the team cannot be wiped. A player joining during an ending waits in the lobby instead of loading into it.
- **Revives checked by the host.** A revive counts only after the reviver has really held it (most of the four seconds, or of the second and a half with a first aid kit), and the kit has to be one the reviver holds.
- **The opening does not wait for ever.** If someone never gets out of the bath, or never comes to the school's entrance, the others go ahead after a minute ("Going ahead without ...").
- **Messages from the host in full.** A guest saw only the first 32 characters of a message from the host ("Wait until everyone is out of th"). "Going ahead without ..." and "That item is unavailable or your inventory is full." reached a guest only as the lobby's status line, not on screen. Someone joining during an ending was never told why they were kept in the lobby. All three now show in full on the player's screen.
- **Joining a game that moved on.** A guest still loading a level the host has already left follows the host instead of waiting in the old one.
- **The MULTIPLAYER screen, laid out plainly.** Before a session: your name on its own line, then a HOST A GAME box (the port and a framed HOST GAME button) and a JOIN A GAME box (the invite code, a framed JOIN GAME button, and the games found on your network). Character and voice moved into the lobby, in a YOUR SETTINGS box; the lobby's LOBBY box names its groups (how friends join, the players) and tags each player (HOST, YOU) beside their name. The microphone is shown by its full name wherever it is chosen, the lettering shrinking to fit instead of being cut short.
- **Settings.** The choice rows no longer show a stray arrow when they are switched off.
- **MOD CREDITS.** The multiplayer screen's CREDITS is now MOD CREDITS, with the mod's name and version, the loader, and a separate line for Nun Massacre by Puppet Combo. The game's own credits are untouched.
- **Setup.** An update replaces the mod's folder as a whole: the new files are written beside it and checked first, so a full disk, a locked file or a crash part way leaves the old version working, never a mix (a player's own files in the folder are kept). The files that start BepInEx go in last. The player zip has a THIRD-PARTY-NOTICES.md and a MANIFEST.sha256 of every file; the old install scripts are no longer in the source package.
- **Nobody stuck in a puzzle's animation.** A player pouring the alcohol on the rope while another put the lighter to it was left frozen in the pour for good: the rope's machine went on as the second player's, and the step that lets the first one go went to them instead. Now a puzzle part mid-animation for one player (the rope, a nail, a vent screw, a candle, the clock's gear, the wires, the sink, the fan, the barbed wire plank, the lock, the padlock's close-up) turns anyone else away until it is done ("[name] is using that."), from the moment it is used (so two at once cannot both start it). As a safety net, a player frozen by a puzzle that no longer runs their animation is let go of it a second later (walking and looking again, the camera back on their head); knocked down part way through, they stay down while the puzzle finishes for everyone; and the console's `unstuck` frees a player stuck in a close-up.
- **Teammates' items in their hand.** What a teammate holds sits in their hand, gripped where the item itself is (a hammer by its handle's end, the radar and the candle by their base): a tilted item's grip was taken from the middle of its box, often empty air, so a hammer or the radar floated beside the hand. The map (a single-sided sheet) was invisible in a teammate's hand: thin things are now drawn from both sides. The hammer, a candle and the gas can are held in one hand; only something over 90 cm (a plank) is carried across the body in both hands (they were taken for boards over 55 cm).
- Not compatible with 2.0.8 (protocol 19). Everyone needs 2.0.9.

## 2.0.8

- **NUN VS PLAYERS, a game mode of its own.** The host picks it in the lobby (GAME → MODE) and picks who is the Nun (THE NUN: a player, or RANDOM, drawn as the game starts). That player is the Nun herself: her body, her lamp, her walk and her sounds, seen through her eyes, with the game's own mind for her switched off. Everyone else plays as in co-op and wins by escaping; she wins when they are all out of lives. CO-OP stays as it was.
  - **Her powers, as the game's Nun has them:** **left mouse** swings her weapon (its own damage to whoever is in front of her and in reach, every 1.4 s); **E** turns over the bed in front of her and drags out whoever is under it, presses a vent gas switch, or pushes a door open or shut; **R** at a door steps into it and out of another one near the players (10 to 30 m from the nearest) that none of them is looking at, every 25 s; **F** sets a bear trap (every 20 s, six at most); **Q** screams (every 8 s); **C** crouches, **Run** runs.
  - She walks the school as the game's Nun does: along its floors and stairs, never through walls, furniture or a shut door, and not through players either (she bumps into them).
  - **Under the beds:** the Nun player cannot see anyone hiding under a bed. Someone who talks out loud there, or whistles, shows through the bed as a white shape for a couple of seconds.
  - **Her voice:** the Nun player is heard from her, a little lower and rougher than their own voice.
  - Her weapon's own hit is off in this mode, so a swing hits each player once, the same way on every game (with the game's hit on top, a host could be hit twice).
- **The Nun's hearing, chosen by the host** (CO-OP mode, GAME → NUN HEARING): **ORIGINAL** hears as in the game alone; **CO-OP** is the tuning so far (voices, footsteps, every noise carrying half as far again); **CUSTOM** turns each of voices, footsteps, sharper hearing and thrown things on or off. Thrown and dropped things landing is new as a choice: off, she no longer hears them.
- **The game's sounds raytraced too.** Doors, cabinets, lockers, drawers, drops, footsteps, the Nun's breathing and the radio get the same treatment as voices: the 32 nearest are muffled by what is between you (a door slammed behind a wall is muffled from its first instant), and the space you are in rings with all of them. Music, the rain and the menus stay dry. Sounds the game plays flat, as if at your ears (a door unlocked, a lock picked, a vent's screws, a locker or cabinet opened), now come from where they happen, so a teammate's locker across the building is no longer in your ear.
- **Voices out of vents without the robot.** Heard through a vent's opening, a teammate in the duct now has the duct's hollow echo, timed by its length, in place of a short ring that made them sound robotic. Heard through the duct's metal (in the room under it) they keep the ring; inside the duct with them, there is no effect at all. Chosen by blind listening.
- **The lobby, rebuilt** in two framed boxes: LOBBY (your name, the invite code, the four player slots, each name lit red while that player talks, the Nun and the host tagged) and GAME (the mode and the Nun's hearing; only the host can change them, and the others see them as they change). The join field is **INVITE CODE**, empty to paste into; a LAN or VPN address still works there. Hosting opens the router port by default, as before. The character page has its turning preview in its own frame (it ran over the choices before).
- **The pause menu's player list** sits in a framed PLAYERS box.
- Not compatible with 2.0.7 (protocol 18). Everyone needs 2.0.8.

## 2.0.7

- **Raytraced voice acoustics, powered by [Vercidium Audio](https://vercidium.com).** The level's walls, floors, ceilings, doors and furniture go into Vercidium Audio's raytracer as it loads, each piece with what it is made of (read from its look: tile, painted plaster, brick, wood, glass, sheet metal, dirt, cloth); doors are moved in it as they swing, and scenario rooms are added and taken away as they appear. Rays from you and from each teammate who talks bounce round the building on background threads:
  - **Reverb** from what comes back: the space you are in and the one the speaker is in, more of theirs the further away they are. Early reflections start off the nearest wall; where the rays run out before a small hard room has died away, its decay is taken from the other end or worked out from its size and surfaces.
  - **Muffling** from what gets from you to them: round corners, through doorways and gaps, through walls (by their thickness and material) and along the ducts. A voice behind a closed door and a wall is a muffled murmur; one you can see is clear.
  - **Where it comes from**, as before: the doorway, the corner or the duct's opening it reaches you by (pathing, as in Steam Audio), as far away as that way is long; a speaker facing away is still quieter, and a far voice loses its top in the air.
  - **Vents** keep their metal ring on top of what the rays find.
  - Chosen by blind listening tests against the mod's own acoustics in nine places (a big room, a corridor, the church, the courtyard, through the kitchen wall, through the bathroom walls, inside a vent, right outside one, the hall under one): the raytraced version won seven of nine, after the fixes below.
  - Making the rays see the school as built: Unity winds a solid's triangles the other way round from the raytracer, so every mesh wall was inside out to it and transparent (a closed room had no reverb and no wall muffled anything); single-sided panels (a wall that is one plane, a duct's sheet metal, a stall partition) are given the thickness of what they are (15 cm of plaster or tile, 4 cm of wood, 2 cm of metal); merged meshes are split into their separate pieces, each with its own material.
  - About a twelfth of one processor core while a level is loaded; building the level into it takes under a second as it loads. `RaytracedAudio = false` in the settings file uses the mod's own acoustics instead. Without the library the mod falls back to them by itself.
- **Your own voice comes back off the space you are in.** A tiled bathroom rings, the church and a long corridor answer from their far end, a duct rings tinny, the open air hardly at all: quietly (26 to 29 dB under your voice in a duct, the church or a corridor, 32 or more in an ordinary room or the open), with no early reflections so it never sounds like a second voice, and only the space's answer is played, never your voice itself. `HearOwnEcho = false` turns it off (if your microphone picks it up from speakers).
- **New echoes, also without the raytracer:** a big hall's far wall answers a shout a beat later, and out in the open a building's wall nearby gives a single slap back; a voice's echo delay no longer slides (it warbled).
- **Each teammate's voice volume in the pause menu.** Escape lists everyone else playing beside the menu, with the game's own slider from muted to twice as loud (saved by name for later games), the level, whether they are down or out, and their name in red while they talk. With mouse, keyboard or controller: right from the menu's buttons into the list. The menu's title and buttons move left so the two sit together in the middle of the screen.
- Compatible with 2.0.6 (protocol 17): the acoustics and the volumes are worked out on each player's own game.

## 2.0.6

- **Tools for the number of players.** The lighter, the map and the bandages come one per player, one to four: a player alone finds one of each, as in the single-player game (before, there were always at least two). The radar and the revolver are one each for the team: one player carries the radar and calls out the Nun, one has the gun (before, everyone got their own).
- **A late player's own tools.** Someone who joins after the school is entered gets their own lighter, map and bandages, beside where the originals lay (before, they had to share). Each one is made on every player's game, and on the games of anyone who joins later still; only the tools in play in that scenario are made.
- **The scenarios, played through in co-op.** The Carnival, the Backrooms and PDM were each played to their ending with three players, one of them joining mid-game: every game's world matched, and each ending played on everyone's screen together before the team went back to the lobby.
- **The Carnival's ending, together.** Setting the clown doll in the rocking chair ends the Carnival with its closing text; it now plays on everyone's screen at once, with everyone held for it and teammates hidden, and the team returns to the lobby together. Before, only the player who set the doll was held: the others could walk about under the text, and the Nun could reach them. The closing text after the Backrooms way out and PDM's coffin is held for everyone the same way.
- **Bodies hang the same for everyone.** The body crucified on PDM's stained-glass cross (the statue head rests on it) sags under physics on the host's game but stood stiff in its default pose on everyone else's. Bones that only physics moves are now sent like any other moving object, for every slumped or hanging body in the school.
- **Legs that bend like legs.** Teammates' thighs no longer slide out of their hips when a leg swings forward (in every stride, and most in a crouch), and their knees no longer fold to a point or poke out behind:
  - The supplied models are weighted coarsely at the hips and knees (on Mike most of the hip followed only the pelvis or only a thigh, and his knee changed from thigh to shin within one ring of vertices). Each model's hip and knee weights are now spread over the joint as it loads, the knee from thigh to shin over a quarter of the thigh's length either side, as Maddy's and Mark's already were.
  - Each knee has a hidden helper joint, turned halfway between the thigh and the shin, that half the skin at the knee follows: a soft crease at the back of a deep bend instead of a pinched point (all of it squared the knee off).
  - The thigh and shin turn about one knee axis, so the knee only folds and never twists.
  - Crouching, the pelvis no longer tips forward with the crouch animation (about 28 degrees): the lower back takes that lean instead, the chest and head where they were, and the hips bend about 20 degrees less.
- **Flat feet.** Everyone stood and walked on tiptoe: the animations give a foot's direction from the ankle to the ball of the foot, which slopes down even standing flat, and it was laid straight onto the models' feet. Each model's foot is now matched to the idle animation's flat foot (Maddy's heels too).
- Not compatible with 2.0.5 and earlier (protocol 17): the school is built with a different number of tools. Everyone needs 2.0.6.

## 2.0.5

- **A setup program.** `NunMassacreCoop-Setup.exe` replaces the install scripts. It finds Nun Massacre on the PC by itself: beside the setup if it was put in the game folder, in the installed-programs list, and by looking through the usual folders and drives. It lists every copy with its state (ready, mod installed and which version, an older version to update, the wrong game build, another mod loader), takes a copy picked with BROWSE or dropped on the window, and installs, updates, uninstalls and starts the game. It carries the BepInEx loader inside it (nothing is downloaded) and asks Windows for permission only when the game is somewhere protected. It shows the game's key art with MULTIPLAYER MOD lettered under the title in the title's own font and warp, played like a worn VHS tape, and uses the game's own fonts for its text; both are read from the player's copy of the game. Its icon is the Nun over the barbed wire pit, from the barbed wire ending. It also tidies away the old install scripts beside the game.
- **Unlocks count in co-op.** Beating the game together unlocks what it unlocks alone, on every player's game: the Factory, and the Circus for a player who lost no life (or on Extreme), each by that player's own lives. Before, only the game that ran the ending gave them (in the coffin ending, the host's and not the guest's).
- **Anomalies count once, and stay counted.** An anomaly (five in the PDM scenario, five in the Carnival; all ten unlock the Backrooms) found by anyone counts in every player's save. One a player had already found is no longer counted for them again, which reached ten (and the Backrooms) early. Each find is saved at once: the game saves the count only at the tenth, so finds made before quitting were lost.
- **Voices out of vents.** A teammate crawling in a duct is heard out of the duct's opening when you can see it (the vent in the stairwell wall, an open vent cover), as loud as the way along the duct and out is long. Inside the duct with them, you hear them from as far down it as you can see. A teammate in a duct above a room with no opening into it is heard only through the ceiling. The search along the duct stays in the duct (it no longer slipped out at a joint into the space under other ducts) and takes the shortest way out, not the first.
- **Voices out of openings.** A voice behind a wall also comes out of a gap you can both see (a window, a hatch, a doorway) when that is louder than through the wall.
- **Vents everywhere count.** A teammate in the main school's vents now counts as in a vent (the game's own vent triggers are off there): their voice is the duct's, hollow and ringing, and a vent's metal sides count as walls.
- **Teammates in the PS1 look.** Teammates are drawn with the game's own PS1 shader, as the Nun and the rooms are, with unfiltered textures, instead of Unity's standard shader.
- **The HUD under the screen effects.** The team list, the revive bars, the hiding meter, the down and spectating prompts and the notices are drawn under the game's VHS and CRT effects (scanlines, colour bleed, the tape's wobble), as the game's own prompts are.
- Test copies of the game (started with the test driver's options) keep their own save beside the game, copied once from the player's, and never write to the player's save.
- Compatible with 2.0.0 to 2.0.4 (protocol 16). Unlocks are passed on between 2.0.5 games.

## 2.0.4

Voice chat, tuned for realism and for horror (after Phasmophobia, Lethal Company and how real buildings carry speech).

- **The Nun hears your voice.** As in Phasmophobia, talking near her draws her: she hears how loud you actually speak, how far she is, and the walls, closed doors and floors between you (the same rules you hear teammates by). Talking out loud carries about 10 m in the same room, a raised voice about 20 m, a shout about 35 m; through a wall only talking close by; a whisper never. Under a bed near her, talking gives the bed away. Before, only a shout counted, and it carried 20 m through anything. It follows each player's NUN HEARS YOU setting (2.0.0 to 2.0.3 guests: only their shouts, as before).
- **She hears footsteps.** Every player's: running is loud (about 10 m), walking soft (she hears it only close by), crouching silent. Before, nobody's steps reached her, and guests' steps could not.
- **Sharper hearing.** Every noise she reacts to (a knocked or dropped thing, a door, glass, a voice, a step) carries half as far again. A guest's noise now reaches her as far as it really carries (before, every guest noise counted as 20 m).
- **A threshold of hearing.** A voice that arrives very quiet (a whisper through a wall, a murmur far down a corridor) now sinks away below it, as it would under a building's own creaks, while a shout still carries. Whisper to stay together; shout and everyone (and she) hears.
- **Walls as real walls.** Through an ordinary interior wall you hear that someone is talking, not what they say (muffled to 800 Hz). A closed door is the wall's weak part (half the voice, muffled to 1.1 kHz); open, the voice comes through the doorway almost clear.
- **Front and back.** A voice behind you is a little duller and softer, as a real head hears it, so you can tell whether a teammate (or a scream) is ahead of you or behind.
- **Your room rings too.** A voice coming into a big hall from elsewhere picks up the hall's echo, not only the echo of the room it came from.
- **No crackle.** A shout at a raised volume eases off instead of clipping.
- Lighter: teammates who are not talking cost nothing, and the voice's rays and routes no longer allocate memory every update.
- Compatible with 2.0.0 to 2.0.3 (protocol 16).

## 2.0.3

- **Another floor is a muffled murmur, from right under you.** A teammate on the floor below (or above) is now heard only when they are about under (or over) where you stand, fading out within a few steps to the side, and only as a dull, wordless murmur: nothing of their words gets through (everything above 260 Hz is gone). Two floors apart, nothing. A voice on another floor no longer comes round by the stairs either.
- Compatible with 2.0.0 to 2.0.2 (protocol 16).

## 2.0.2

- **Mike and Mark walk like men.** Their walk was Mixamo's Standard Walk, which sets each foot down on the middle line, sways the hips as much as Maddy's walk and lifts the forward hand to the waist with the wrist cocked: on these characters it read as a catwalk. They now walk with Mixamo's Walking: the feet on two tracks, the shoulders turning against the hips, the arms swinging loosely from the shoulder. Their crouch is remade from it.
- **Voices come round the building, not through it.** When a wall is in the way, a teammate's voice now also travels the way a person would walk (along the Nun's navigation mesh: through doorways, round corners, along corridors) and is heard from where that way comes into view: the open door they are behind, the corner of the corridor, as loud as the whole way round is long. Shout in a room and the others hear it from its door, not through the wall from where you stand; round a bend in a corridor, it comes from the bend. Whichever is louder wins: round about, or straight through the wall or floor, muffled.
- **More forgiving.** A voice fades as loud as it sounds rather than as the sound pressure falls: full within a couple of metres, about two thirds at 4 m, 40% at 8 m, a fifth across a big room, still there down a long corridor, gone by about 45 m (was 30 m). Through a wall you hear someone muffled (about half of it, was under a third); a floor apart, a low murmur instead of nothing unless shouted; two floors apart, a shout faintly.
- **Doors matter.** A closed door takes most of a voice and muffles it, on the straight line and on the way round: shut it and they are much quieter. Open, the voice comes through the doorway.
- **More situations heard:**
  - Down a long corridor a voice echoes back off the far end, more the further away the speaker is.
  - Far away, the air takes the top off a voice, and you hear more of the room around them than of them.
  - A small hard room (a bathroom, a stall, a stairwell) sounds boxy: strong, bright, early reflections.
  - A teammate who is down sounds weaker and breathier.
  - Hiding under a bed, you hear everyone through the mattress.
  - Knocked down, your ears ring and the top of every voice is gone.
- Compatible with 2.0.0 and 2.0.1 (protocol 16).

## 2.0.1

- **Voice and microphone in the lobby.** MULTIPLAYER has a new **VOICE & MIC** row (showing the microphone in use) that opens the same settings as SETTINGS > MULTIPLAYER: voice chat on or off, open mic or push to talk and its key, the microphone, the mic level with a live test meter, the voice gate, teammates' voices and whether the Nun hears you. Select a row to change it; left and right step the levels.
- **The join instructions are back on screen.** The description line at the foot of the MULTIPLAYER screens wrapped onto a third line that ran off the bottom. It now starts higher and runs down, and the column above packs its rows closer when it is long (games on your network, a full lobby), so the two never overlap.
- **Microphone names are readable.** The microphone was cut to 13 letters ("Microphone (R." on two lines). It now shows the device's own name, as Windows gives it in brackets ("Microphone (2- USB Audio Device)" shows USB AUDIO DEVICE), on one line, in SETTINGS and in the lobby.
- Compatible with 2.0.0 (protocol 16).

## 2.0.0

The 2.0 release. Everything since 1.0.26 (the 1.1 to 1.9 updates, summarised below) plus a last round of polish.

**Every player needs 2.0.** The network protocol is now 16: a 2.0 game only connects to other 2.0 games (older versions are told the host has a different co-op mod version), so everyone in a session has every feature below.

### New in 2.0.0

- **Peeking round corners.** When a teammate leans with the game's **Q** or **E**, their character leans out the same way, head and shoulders past the edge, and the hand on that side reaches round the corner and goes flat against its far side, beside their face. With no corner in reach the hand rests on the wall beside them; with nothing in reach they just lean. A hand holding an item keeps hold of it, and a revolver stays aimed.
- **A new crouch.** Crouched teammates sneak: hips down, back hunched forward over the knees, head up, shorter steps; still, they hold the same crouch. It is made from each character's own walk (men's and women's) and the idle (`scripts/make-crouch.py`), so it moves in the same style as everything else. Getting down to the game's crouch height is shared between the hips and leaning further over, and the hips never sink further than a crouch (before, they dropped until the character was nearly kneeling, the feet tucked far behind the knees).
- **Crouching on stairs.** Crouched on stairs or a ramp, the back comes up by part of the slope, so a crouched climb is a hunched climb, not on all fours with the hands at the steps; going down, they sit back a little.
- **Feet stay out of the floor.** A teammate's toes or heels were about 7 cm into the floor most of the time (the clips put the ankles at flat-floor height and a pointed toe went under it), and deeper on stairs, where the body moves on between the twelve poses a second. Now, last of all every frame, any toe or heel under the floor beneath it comes up onto it (the knee bends more); on a stair edge the foot tips up off the step, and only if the leg is bent as far as it goes do the hips rise a little. Measured on flat floors, walking, crouching and running, and up and down stairs: no frame with a sole in the floor (it was every frame on flat ground).
- **Vents checked.** Crawling the length of a duct and back, filmed and measured: the body stays inside the duct except for an elbow or knee touching a wall for a moment while turning round where it narrows.
- **Standing still looks like standing still.** Teammates who had stopped sometimes walked on the spot (the game's character controller kept reporting its last speed). Their speed is now measured from how far they actually move.
- **Mandy (the Ghost) is gone.** The characters are Mike, Maddy and Mark, all by Vinrax. Anyone whose settings still name the Ghost plays Mike.
- Tidied: no build warnings; the sync audit's "use everything" step now opens doors too (it only reached cabinets and light switches before).

### 2.0 at a glance (1.1.0 to 1.9.0)

- **Voice (1.1.0):** the voice settings are in the game's own **SETTINGS → MULTIPLAYER** page, with microphone choice and a **mic level** (25% to 400%). Voices carry realistically: they fade with distance (gone by 30 m), are muffled through walls and doors, only a murmur through a floor, bend round open doorways, ring in vents and are muffled under beds.
- **Menus (1.5.0):** the MULTIPLAYER screens are laid out and drawn like the game's own PLAY screen; progress bars in game are the game's VCR sliders.
- **Point and whistle (1.2.0):** **1** points (the character's arm goes out; nothing on screen), **2** whistles, heard through the building by teammates and the Nun.
- **Under the bed (1.3.0, 1.4.0):** talking or whistling under a bed can give it away. Alone under it, she flips it over; two under it, she reaches under and drags you both out, stands you up and goes for the one she heard.
- **Endings together (1.4.0, 1.8.0):** the good, coffin, Backrooms and PDM endings play on everyone's screen at once, teammates hidden from the shot, then everyone returns to the lobby together.
- **The Nun (1.6.0, 1.7.0):** her jump scare, screen jolt and chase music are for whoever she is hunting; she searches round the player she is after and picks off whoever strays from the group; guests can shoot her and she hears every gunshot; teammates hear each other's shots; she stands over the one she knocked down and keeps coming back to the body.
- **Sync (1.8.0):** audited with three players and a late joiner across the main game and the Carnival, Backrooms and PDM scenarios (every synced object matched); a guest who loads late no longer stays frozen in a scenario's opening text.
- **Animation (1.2.0, 1.4.0, 1.9.0):** idles, lying flat under a bed, a hit reaction, an overarm throw, bending to pick things up, reaching to take, pulling doors and drawers open, and a body that lies sprawled where a teammate fell; every pose steps at twelve poses a second, in the game's PS1 style.

## 1.9.0

Animation pass.

- **New motion-captured moves (Mixamo, all unarmed, checked for props):** a teammate hit by the Nun recoils with Mixamo's large hit reaction (upper body: their legs keep going); throwing something is an overarm throw; picking something off the floor bends right down to it; taking something within reach reaches out for it; opening a door, cabinet or drawer reaches out and pulls. Down or crawling, or with an item in the hand already, the hand-made versions as before.
- **Bodies** lie as Mixamo's Falling Back Death ends: sprawled on the back, one knee up.
- **Every pose in the PS1 style.** Before, only the motion-captured clips stepped from pose to pose twelve times a second; the hand-made poses, the head turning to look, gestures and the arm on an item moved smoothly. Now everything a teammate's body does steps at twelve poses a second, the same as the clips (they still move across the floor smoothly).
- Compatible with 1.0.21 to 1.8.0 (protocol 15).

## 1.8.0

Sync and late-join audit.

- **Audited:** the host and a guest opened and closed 20 doors, cabinets and light switches on two floors, fired the revolver and set a bear trap; a third player then joined late. All 7,098 synced objects matched on all three games (state, colliders, renderers, lights, puzzle variables). A late joiner also sees teammates as they are (downed, hiding under a bed) and the Nun where she is. Repeated in the Carnival, Backrooms and PDM scenarios: they match too.
- **Fixed: a guest frozen by a scenario's opening text.** The Carnival, Backrooms and PDM scenarios open with their story scrolling up the screen while you are held in place (46 s). A guest whose level loaded a little later, or a friend joining a game in progress, stayed frozen in it after the host was free and the Nun was about. Now a guest still in it when the host is through skips the rest (as the game's own menu button does).
- **The Backrooms ending is watched together**, like the good and coffin endings: when anyone reaches the way out, everyone sees the closing text and returns to the lobby together. (The PDM ending already was.)
- Compatible with 1.0.21 to 1.7.0 (protocol 15).

## 1.7.0

The Nun, part two.

- **Guests can shoot her.** On a guest's game her body was a tiny ball at her feet (only the host's game sizes it), so a guest's bullet went straight through her: she never bled or retreated from a guest's shot. Her body is now the size it is on the host's game, and a guest's hit sends her running as the host's does.
- **She hears every gunshot.** A revolver shot draws her to where it was fired, now whoever fires it (before, only the host's shots).
- **Everyone hears a teammate's gunshot.** Teammates' shots were silent on everyone else's game (only the recoil showed). Now you hear the revolver's report from where they fired it: deafening close by, muffled through walls and floors, never just a murmur.
- **She stands over the one she put down.** After a knockdown she looms over the downed player for a few seconds, looking down at them, then walks off (still blind and deaf for the rest of the six-second window) before she searches again.
- **She comes back to the body.** While someone lies downed she returns every 35 to 55 seconds to search round them: a downed teammate is bait, and a revive is never safe for long.
- Compatible with 1.0.21 to 1.6.0 (protocol 15). Guests older than 1.7.0 still cannot hit her and do not hear teammates' shots.

## 1.6.0

The Nun, part one: she hunts every player properly, and hunts a group the way a group should be hunted.

- **Her jump scare works on guests.** When she spots a guest, they now get her full jump scare: the stinger and scream, the VHS jolt and motion blur on their screen, the controller rumble. Before, her check for "am I on screen" looked through the host's camera, so she never jump-scared a guest at all, and her motion blur came on for the host instead.
- **The jolt is the hunted player's.** The VHS damage effect and motion blur are on the screen of whoever she is after, move with her if she turns on someone else mid-chase, and end when she puts that player down.
- **Chase music is for the one she is after.** Her chase music and stingers are full volume for the player she is hunting and anyone near her, and fade to a faint sting the further you are from her (she is after someone, somewhere). Before, everyone heard them at full volume wherever they were.
- **She searches round the player she is after.** Her search spots and hiding spots were always ranked from the host (the game finds "the player" once, by tag); a guest could never be searched for. Now they are measured from the player she is hunting.
- **She picks off whoever is alone.** When she cannot see anyone she goes after the player furthest from their teammates (closer players still first when it is close). In sight, mid-chase, mid-taunt at a vent or heading to where she last saw someone, she stays on them.
- **She lies in wait near you.** With two or more players her ambushes are at one of the three hiding spots nearest the player she is hunting (the game picked one anywhere on the map), and she takes fewer breaks off the map chopping meat (30% of her idle choices instead of 50%). Alone, the game's own.
- Compatible with 1.0.21 to 1.5.0 (protocol 15). Guests older than 1.6.0 hear her music as before and get no jolt.

## 1.5.0

- **The MULTIPLAYER screens look like the game's own.** They are laid out as the PLAY screen: the menu room shows through behind them (dimmed, no longer blacked out), one column of the game's rows with the name on the left and the value on the right (YOUR NAME ...... MIKE), the VCR arrow on the selected row, buttons listed down the column, and one line of description at the foot. Text fields are game rows too (no more purple boxes). In a session the column shows the invite code (select it to copy it again), the port, your network address, the players, then START GAME and STOP HOSTING. Games on your network are rows (HOST'S GAME ...... 2/4 LOBBY).
- **CUSTOMIZE is now CHARACTER**, a row showing who you play; its page has the turning preview without the purple frame. The credits page is spaced as a game page.
- **In game, progress bars are the game's sliders**: reviving, being revived and bleeding out show as `<||||||------>` in the VCR font, like NUN VOLUME in the settings.
- Compatible with 1.0.21 to 1.4.0 (protocol 15).

## 1.4.0

- **Endings, together.** When the good ending (walking out once the Nun has burned) or the coffin ending starts on anyone's game, it starts on everyone's at the same moment and plays in full on each screen: the fade, the end screen and the credits. Teammates' bodies are hidden while it plays so nobody stands in the shot. The fatal endings (the acid vent, the barbed wire) are still only the player who walks into them.
- **One under the bed: she flips it.** With one player under a bed, the Nun's bed check is the game's own again: she flips the bed over and turns them out from under it, no dragging. A guest under it now gets exactly what the host would (before, a lone guest was dragged instead).
- **Two under the bed: she grabs you.** The bed stays where it is. She stoops, reaches under it and hauls both of you out across the floor, one just behind the other, to either side of her; you are stood up in front of her and she goes for the one she came for. No bed flip and no bed-lift sound.
- **Lying under a bed.** Teammates hiding under a bed now lie flat and still on their stomach, arms folded under the chin and head low, from Mixamo's unarmed stomach pose with the legs laid flat (the old pose kicked its legs whenever the body moved). It fits under the beds' slats: no part of the body goes through the bed. The Ghost keeps its own pose, without the kicking.
- Compatible with 1.0.21 to 1.3.0 (protocol 15). Guests older than 1.4.0 do not watch the endings with you and are not turned out of a flipped bed.

## 1.3.0

- **Too loud under a bed and she comes for it.** Talking out loud or whistling while hiding under a bed now gives that bed away: if the Nun is close enough to hear (about 20 m for a voice, 30 m for a whistle, half that through walls) she walks to it and lifts it, her own bed check.
- **She drags you out.** Whoever is under that bed is pulled out flat on the floor towards her feet, looking up at her, and stood up in front of her; with two of you under it, the one she came for first and the other a moment later. She waits until you are both up, then goes for the one she came for. The other has a head start.
- Fixed: when the Nun lifted a bed with a guest under it, the guest was never pulled out (the game's step moved only the host), and guests did not see the bed lift. Now everyone sees it, and everyone under it comes out.
- Compatible with 1.0.21 to 1.2.0 (protocol 15); older guests are not dragged out.

## 1.2.0

- **Point and whistle.** **1** points: your character's arm goes straight out to where you look, for teammates to follow (nothing appears on anyone's screen). **2** whistles a two-note call: teammates hear it from where you are, through the building like a voice (muffled through walls, a murmur through a floor), and so does the Nun, within about 30 m. Your character puts two fingers to their lips.
- **Idles.** Standing still, teammates breathe and shift their weight (Mixamo's Breathing Idle); crouched and still, they hold a low crouch with their arms in (Mixamo's Crouching Idle, given the idle's arms). The crouched walk now comes down to the same height, so stopping does not bob.
- Compatible with 1.0.21 to 1.1.0 (protocol 15); older versions do not show the gestures.

## 1.1.0

First step towards 2.0.

- **Voice settings live in the game's own SETTINGS menu**, on a new **MULTIPLAYER** page (main menu and pause menu alike), built from the game's own rows: voice chat on/off, talk mode (open mic or push to talk), the push-to-talk key, the microphone, a new **mic level** (25% to 400%, for a mic that is too quiet), a live mic test meter, the voice gate, teammate voices volume, and whether the Nun hears you. The lobby's VOICE page is gone.
- **Realistic voice chat.** Voices now carry as they would in the building, and it is far less forgiving:
  - Half as loud each time the distance doubles (3 m: -6 dB, 12 m: -18 dB), gone by 30 m (was full volume out to 22 m).
  - Each wall in between takes most of a voice and dulls it to a muffle; a closed door a little less; a floor or ceiling nearly all of it, so one floor apart only a shout comes through as a low murmur and two floors nothing.
  - Sound bending round an open doorway's edge comes through, dulled.
  - Someone talking away from you is quieter and duller.
  - In a vent (theirs or yours) a voice rings hollow and metallic off the duct.
  - A voice from under a bed is muffled.
- **Credits** for the character models: Mike, Maddy and Mark by Vinrax ([sketchfab.com/vinrax](https://sketchfab.com/vinrax), [x.com/Vinrax](https://x.com/Vinrax)); Mandy (the Ghost) by Elbolilloduro ([sketchfab.com/Elbolilloduro](https://sketchfab.com/Elbolilloduro), [x.com/elbolillodur0](https://x.com/elbolillodur0)). On a new CREDITS page in the co-op lobby (the links open in your browser), on the character page, and in the README.
- Compatible with 1.0.21 to 1.0.26 (protocol 15).

## 1.0.26

- **Held items sit in the hand.** Every item is now gripped in a closed fist (palm in, thumb up) and turns with the hand; its grip (low on it, as the game holds it) sits in the palm. Before, items floated at the wrist turned to match the view, and the hand was palm-up. The holding arm is lower too, so a radar or hammer no longer covers the face.
- **The board takes both hands.** A long item (the wooden board) is carried diagonally across the body, low on the right and high on the left, one hand either side of its middle.
- **Heads look where players look in every pose.** Crouched, crawling in a vent or downed, the head now turns and tilts to where the player looks (before, a crouch or crawl tipped it down to the floor).
- **A better crouched walk.** Short, quick steps with the feet under the hips instead of lunges, a straighter back, and the arms close to the body (the clip's arms stuck out wide).
- Fixed: a teammate putting down something carried in both hands made the host's game error every frame for a moment.
- Compatible with 1.0.21 to 1.0.25 (protocol 15).

## 1.0.25

- **Running.** Teammates run with motion-captured runs (Mixamo's run for Mike and Mark, the women's run for Maddy, shorter steps for Mandy) instead of the hand-made one, blending in from a jog.
- **Crouch-walking.** A crouched teammate on the move sneaks with Mixamo's Crouched Walking, turned to face where they go (the original crabs along sideways) and brought down to the game's crouch height. Under something low they get lower still. Standing still crouched keeps the old pose.
- **Jumping.** A teammate's jump shows: knees tucked and arms up in the air, timed to their actual rise and fall, then the landing dip (a men's and a women's jump).
- **The revolver.** A teammate holding the revolver holds it out in both hands where they look, and each shot they fire kicks their arms up (Mixamo's Shooting). Empty clicks do not.
- All in the same PS1 stepped style and with the same floor, slope and wall fitting as the walks.
- Compatible with 1.0.21 to 1.0.24 (protocol 15); older versions just do not show the new jumps and shots.

## 1.0.24

- **Walks in the PS1 style too.** Teammates' walks step from pose to pose twelve times a second, like the crawl; the body still moves smoothly. Walks also keep pace better at the game's walking speed (less foot sliding).
- **Maddy's walk looks natural.** The Female Walk was a catwalk (feet crossing the middle, swinging hips, flicked wrists, leaning back). It is now taken about halfway to the standard walk (scripts/blend-animation.py): a lighter step that still walks like a person. Mandy's shorter-step walk gets the same.
- **No clipping on slopes and stairs.** Each foot stands on the floor under it, not on an imagined flat floor: on stairs and slopes the hips lower and the knees bend so neither foot sinks in or floats. A crawling teammate tilts with the slope and every hand, knee and foot rests on its own bit of floor.
- **No clipping in vents.** Players are much thinner to the game (10 cm) than their bodies, so they can hug a duct's side; the body now keeps off walls (shoulders, elbows and knees stay out) and to the middle of a narrow duct, with the elbows coming in where it is tight. A low duct brings the crawl lower (elbows bend, hips fold) so the back and head stay under its ceiling. In a vent the body lies along the way they crawl, so looking around turns only the head and not the body through the walls. At a duct's dead end or a corner the body slides forward instead of putting its legs through the wall behind.
- The crawl no longer carries the body 10 cm to one side (it was off-centre in the converted clip, which put a knee into duct walls).
- Downed players can crawl up stairs (checked on the school stairway).
- Fixed: a guest who joined while their game was still starting up was dropped ("You left the co-op game") if the host was already playing, because reaching the main menu for the first time looked like quitting to it.
- Compatible with 1.0.21 to 1.0.23 (protocol 15).

## 1.0.23

- **Crawling in vents.** A teammate crouching somewhere too low for a crouch (every vent, under a table) goes onto hands and knees with the motion-captured crawl, and back to a crouch once there is room.
- **The crawl in the game's PS1 style.** Like the PS1 games Nun Massacre copies, the crawl now steps from pose to pose about twelve times a second with no in-betweens; the body still moves across the floor smoothly.
- Compatible with 1.0.21 and 1.0.22 (protocol 15).

## 1.0.22

- **A real crawl for downed players.** A downed teammate crawls with a motion-captured crawl (Mixamo "Crawling", converted by scripts/convert-animation.py and fitted onto each character's own skeleton) instead of the hand-made one, paced to how fast they actually move so hands and knees do not slide. Mike, Maddy and Mark use it; the Ghost keeps dragging herself along (a robe on all fours looks like a box).
- **Real walks.** Walking teammates use motion-captured walks: Mixamo "Standard Walk" for Mike and Mark, "Female Walk" for Maddy, and Mandy the same with shorter steps so her robe stays closed. The walk fades in as they set off and out as they stop or break into a run (standing and running keep the hand-made poses); the head still looks where they look, an arm holding an item stays raised, and reaching for a door still shows. Feet are kept on the floor each frame.
- Nothing clips: each frame the body settles so the lowest knee or hand rests on the floor (per character), planted hands lie flat, and the head and hands stop short of a wall ahead (the body slides back by just as much).
- Compatible with 1.0.21 (protocol 15).

## 1.0.21

- Fixed: players were sometimes pushed through walls or under the map, and in the coffin ending a player could be shoved through the cave wall. Teammates kept each other out with invisible colliders, and a teammate's collider moving into you made the physics push you out the shortest way, which could be through a wall or the floor. Teammates now keep their distance by ordinary movement (which never passes through walls): whoever walks into the other stops, and someone standing still is not shoved.
- **Falling out of the world puts you back.** Anywhere in the game, falling through the floor or off the map returns you to where you last stood on solid ground, with no life lost ("You fell out of the world and were put back").
- **Rejoining catches up with the puzzles.** A player who joins or rejoins mid-level now also gets the progress of every puzzle machine (vent covers unscrewed, gears fitted, tapes in...). Before, objects looked right but the puzzles themselves had started over for them.
- **Host console.** F2 (or `) opens a console: players, bring, goto, tp, unstuck, spawn, revive, kick. Guests can use unstuck on themselves. The host's game also runs commands written to BepInEx/nmm-commands.txt, so a stuck player can be helped from outside the game.
- Crawling (downed): planted hands lie flat on the floor, palm down and fingers ahead.
- A teammate who is out for good leaves their body where they lay bleeding, not where the game's death sequence moved them.
- Protocol 15: 1.0.20 and older are refused.

## 1.0.20

- **Crouching in vents again, head ducked.** Teammates crouch in vents (no longer crawl), and under a low ceiling the crouch deepens, the back hunches and the head tucks by just as much as the space needs, so it stays under the top of the vent.
- **Downed players crawl on hands and knees.** A knocked-down teammate is on all fours and crawls: each knee slides forward in turn with the lower leg folding under it and lying flat again once planted, each hand lifting and reaching with the opposite knee, the weight shifting over the planted knee. Down and still, they stay on hands and knees, head low and breathing hard. Your own view while down sits at that height.
- Crawling no longer pushes the head into walls ahead.
- Compatible with 1.0.17 to 1.0.19 (protocol 14).

## 1.0.19

- Fixed: after the opening, none of the co-op buttons worked: holding Interact to revive, holding Jump to give up, crawling, and changing the spectator view. The game rebuilds its input system with each level and the mod kept using the old one (the log filled with Rewired errors every frame). It now asks for the current one each time.
- Fixed: the opening's letter could still take everyone outside while a player was in the intro, before waking in the tub. They now count as in the bath until they have woken up (at most 30 seconds).
- Falling out of the world in the opening (which has no lives) puts you back where you last stood, instead of taking you out of the game until the school.
- **Held items look right on teammates.** The radar, hammer, lighter and the rest are held the way the game holds them in front of the player's own eyes (a radar's screen towards them, a hammer upright), instead of twisting with the hand.
- **Teammates crawl in every vent.** A crouching teammate goes onto hands and knees wherever the ceiling is low (every vent, and under tables), instead of crouching with the head through the top. Before, only vents that told the game a player was inside counted, and the school has none of those.
- **No more marker on downed teammates.** The red "DOWN" label through walls is gone: find a downed teammate by looking and listening. The short notice that someone is down stays.
- Compatible with 1.0.17 and 1.0.18 (protocol 14).

## 1.0.18

- Fixed: reviving could fail to start. The check that no wall stands between you and a downed teammate aimed at their chest lying on the floor, where the floor itself (in rooms whose floor is also the ceiling of the room below) could count as a wall. It now aims just above the body, on your game and the host's. If a revive is still refused, the log names what is in the way.
- **Crawl while downed.** A downed player can drag themselves along the floor, slowly, towards where they look (to get out of sight, or closer to a teammate). You stay still while someone revives you. Teammates see you on your stomach, pulling yourself along by the arms.
- Fixed: knocked down while crouching, your view sank through the floor. It now always rests just above the floor under you, also as you crawl over stairs.
- No more groaning every few seconds while you are down.
- **Spectating: Interact changes the view every time.** Behind a teammate, then through their eyes, then the next teammate, then the free camera, and round again. With a single teammate it used to switch to the same person, so it looked as if nothing happened.
- The log names the reason whenever a downed player bleeds out (timer, gave up, fall, or the whole team down).
- Compatible with 1.0.17 (protocol 14).

## 1.0.17

- **A tool for every player.** The lighter, the radar, the map, the revolver and the bandages now come in one copy per player: two with two players, three with three, four with four, side by side where the original lies. The count is set by who is in the game when the school is entered; someone who joins mid-level shares the existing ones until the next game.
- Protocol 14: 1.0.16 and older are refused (they would build the school with a different number of tools).

## 1.0.16

- **Play online without a VPN.** When you host, the mod asks your router (UPnP) to open the game's port and shows an **invite code** in the lobby, already copied to the clipboard (**COPY INVITE CODE** copies it again). Friends anywhere paste it into **ADDRESS OR INVITE CODE** and select **JOIN GAME**. The port is closed again when you stop hosting or quit (and expires on its own after an hour if the game is killed).
- Only friends with the code can join from the internet: it carries a one-time key, and an address that tries ten wrong keys is turned away. Players on your network or VPN still join by address, as before.
- If the router cannot open the port (UPnP switched off, or an internet connection that shares its address, as some mobile and fibre providers do), the lobby says why and what to do instead.
- New setting `OpenRouterPort` (on by default) in the settings file.
- Compatible with 1.0.10 to 1.0.15 (protocol 13); joining by invite code needs 1.0.16 on both sides.

## 1.0.15

A full two-player playthrough in one session, from the bathtub to the good ending, plus the coffin ending: every puzzle completes, both games end with identical worlds, and everyone returns to the lobby together.

- Fixed: in the opening, the letter could be read (taking everyone outside) while a player was still loading in or waking up in the tub, pulling them out of the bath. It now waits for them ("Wait until everyone is out of the bath").
- The log now names what took a player's held item whenever one is used up or placed, to make any lost-item report traceable.
- Compatible with 1.0.10 to 1.0.14 (protocol 13).

## 1.0.14

- **The dead stay where they fell.** A teammate who is out for good (bled out with no lives left, or killed in the acid vent or the barbed wire) no longer vanishes: their body lies where they fell, on its back, until the level ends. Spectating, you can find your own body too.
- Compatible with 1.0.10 to 1.0.13 (protocol 13).

## 1.0.13

- Downed teammates no longer groan: they can call out for help over voice chat.
- Compatible with 1.0.10 to 1.0.12 (protocol 13).

## 1.0.12

- **Seeing a teammate get hurt.** When the Nun hits a teammate, you see it: the game's blood burst from their body and a flinch. Maddy and Mandy also cry out with the game's own pain voice, from where they are; it is a woman's voice, so Mike and Mark take hits silently (their player's microphone is their voice).
- **Groans from a downed teammate.** A downed Maddy or Mandy groans now and then, at the same rhythm the player hears from themselves, so you can find them by ear.
- **Hands on things.** Teammates reach out when they open a door, pick something up or use something: an arm forward for a handle, a crouch and a reach for something on the floor.
- **The Ghost crawls differently.** In a vent, Mandy drags herself along on her arms with the robe trailing, instead of a boxy hands-and-knees.
- Compatible with 1.0.10 and 1.0.11 (protocol 13).

## 1.0.11

- **Spectating keeps the game's look.** The game's blue moonlight hangs off the player; spectating used to leave it behind, so the view turned flat and untinted. It now stays with the spectator camera, following a teammate or flying free.
- **Teammates' footsteps.** You hear teammates walk: the game's own footstep sounds for the floor under them (wood, carpet, tile, or outdoors), at the same pace as your own, coming from where they are and fading out by about 16 m. Crouching is silent, as it is for you.
- **Crawling in vents.** A teammate in a vent crawls on hands and knees, low enough to stay inside it (they used to crouch and poke out through the top). They still take up room: one at a time through a vent.
- Compatible with 1.0.10 (protocol 13).

## 1.0.10

- **Getting out of the bathtub, as others see it.** Teammates no longer watch your character slide awkwardly out of the tub: they see you lying in it until you are out, then you appear standing beside it.

## 1.0.9

- **Lying still, looking around.** A teammate lying in the bathtub no longer spins in it when they look around: the body stays put and only the head and neck turn to where they are looking.
- **Hiding under a bed looks natural.** Teammates lie on their stomach propped on their elbows with the head up (instead of both arms stretched out ahead), and their head turns to where they are looking, so two players under one bed can see each other looking around.

## 1.0.8

- **The opening is played together.** Everyone still gets out of the bath on their own (and sees the others lying in it). Reading the letter takes the whole group outside at once, standing side by side, but only when everyone is out of the bath ("Wait until everyone is out of the bath"). The opening's scares and the title card happen together, with the same "everyone close by" rule as in the school. And you go into the school together: the entrance waits until the whole group is there ("Wait for everyone: you go into the school together"), then everyone loads in at the same time.
- Protocol 13: 1.0.7 and older are refused.

## 1.0.7

- **Hold things together.** Two players can drag the same door or object at once again (1.0.6 allowed only one). When one lets go, it keeps moving for whoever is still holding it; it only stops once nobody holds it.
- **Scares are shared.** A scare now happens to the whole group at once, with its camera effects for everyone, and only when everyone still in the game is close by (within about 12 m, on the same floor). Split up, and it waits until you come through together. Its chance of happening is rolled once for everyone. The opening scene's scares stay each player's own.
- Fixed: a scare's ghost child stood frozen on a guest's screen instead of running.
- Protocol 12: 1.0.6 and older are refused.

## 1.0.6

Two-player edge cases: things that only go wrong when people overlap, interrupt each other or leave mid-way.

- **Fatal endings are personal.** Dying in the acid vent or the barbed wire takes only that player out: they spectate while the others play on. If nobody else is left, it ends the run as before. Completing an ending (the coffin, the good ending) still brings everyone back to the lobby.
- **The coffin is one player's.** Once someone lies down, only they can close the lid ("Only the player in the coffin can close it").
- **Leaving mid-puzzle no longer blocks it.** If a player disconnects during lockpicking, the padlock, the clock or a close-up, the puzzle is freed for the others; the lockpicking wrench stays in the keyhole for someone else.
- **Items left by a disconnecting player** drop where they last stood on walkable floor, never inside a vent, the barbed wire pit or the acid vent.
- **Doors and props have one holder.** A second player gets "Someone else is holding that" instead of fighting over it, and only the holder letting go releases it (also released when the holder goes down or leaves).
- **Reviving:** not through walls or closed doors. A first aid kit is used up only once the revive has actually worked, so a revive that loses the race to bleeding out keeps the kit.
- **Knocked down during a close-up:** after the revive, the camera is back on your head, level, at normal zoom.
- **The Nun keeps her victim** while she drags someone from under a bed or swings: a teammate running closer does not redirect the grab.
- **Joining a game in progress:** traps the Nun had already thrown now appear for the newcomer.
- **Paperclips** count once even if two players grab the same one together.
- **Puzzles** use the tool you are holding at the moment you use them (not a moment earlier), and the host no longer refuses a guest's puzzle step because the host is switching items.
- An ending in progress pauses bleed-out and the team-wipe check, and the team returns to the lobby once.
- Protocol 11: 1.0.5 and older are refused.

## 1.0.5

- **Two of every tool, in every layout.** The lighter, radar, map, revolver and now the bandages each have a spare next to the original. Spares are placed beside the original on the same surface (not off the edge of a table), including when items are randomised.
- **The host's PLAY choices apply to everyone.** The scenario (normal, PDM, Carnival, Backrooms) and item randomisation now come from the host, and randomised items land in the same places for every player. Before, each player's own settings were used, so with randomisation (or a different scenario) players could end up with different worlds.
- Protocol 10: 1.0.4 and older are refused.

## 1.0.4

- **Teammates take up room.** You can no longer walk through each other (the game itself turns off collisions between players). Two players cannot stand in the same spot, so in a bathroom stall, a corner or a room behind a door you hide next to each other, and a gap only wide enough for one, such as a vent, lets one through at a time. Downed, hiding and lying teammates do not block, and nobody gets stuck inside a teammate (everyone starts on the same spot).

## 1.0.3

- **Hiding together.** Two players fit under a bed, lying side by side instead of inside each other, and can look around and see each other. A bed takes two at most (one where a wall or furniture leaves no room beside the first place); anyone else is told there is no room and has to find another bed. The host hands out the places, so two players diving under the same bed at once never end up in the same spot.
- A teammate under a bed lies facing out, towards the room, whichever way they are looking.
- Protocol 9: 1.0.2 and older are refused.

## 1.0.2

The whole game can be played through together, with puzzles shared between players.

- **Puzzles are shared.** Anyone can work on any puzzle and the progress counts for everyone: one player places a candle and another lights it, two players each insert tapes, someone else pours the gasoline. Close-ups, the clock hands, lockpicking and the padlock are played by whoever started them; everyone else's game follows the result. Tools used in a puzzle (screwdriver, hammer, lighter...) go back to the player who used them.
- **Two of each tool.** In co-op the lighter, radar, map and revolver each have a spare next to the original.
- **The opening is your own.** Everyone wakes in the bathtub and gets up when they choose; teammates see you lying in the tub until you do. Reading the letter, the walk and the scares are per player. If the host walks into the school first, guests finish the opening and walk in themselves.
- **Endings:** the coffin, the good ending, the acid vent and the barbed wire play for the player who reaches them, without moving or freezing anyone else, then the team returns to the lobby.
- Thrown rocks fly for everyone and can be picked up again (they used to vanish for the others). Breaking a window breaks it for everyone.
- Fixed: a guest picking the red door's lock left it locked for everyone else (and the keyhole stuck "busy").
- Fixed: getting out of the bathtub took the prompt away from everyone still in it.
- Fixed: an item dropped far away could jump to the feet of a player who had held it before.
- Fixed: a statue head, hanging body part or tape held by one player stayed pinned or wrong on the others' screens.
- Fixed: scares re-armed themselves for guests; the acid vent ending never finished for a guest.
- Protocol 8: 1.0.1 and older are refused.

## 1.0.0

First release. Everything below, rounded out: release packaging, installer wording, and documentation for players.

- Bleeding out now takes five minutes on every difficulty (was 25-90 s), shown as m:ss.
- Spectator free camera: when out of lives, press Jump to fly anywhere instead of following a teammate.

## 0.9.x

- Everyone starts a game together: the host's PLAY INTRO / SKIP AHEAD starts every guest's loading screen at once (the same screen), and all enter the level together once everyone has loaded.
- Voice takes on the room around the speaker: halls echo, small rooms barely, outdoors stays dry, vents sound metallic.
- Characters named Mike, Maddy (secretary), Mark (scientist) and Mandy (ghost). The ghost's arms hang outside its robe and its legs step under it.

## 0.8.0

- New characters: the scientist and the ghost.
- Fixed a phantom flashlight beam on teammates (an always-on scenario light counted as a carried light; the Nun also treated teammates as lit).
- The character screen no longer shows your player name.

## 0.7.x

- The Nun answers calls: shout in her sight to pull her off a chased teammate; shout from another room to distract her from a hiding teammate. She visibly turns before committing and will not be spun between players.
- Much smoother frames, especially for guests: removed a 50 ms stall twice a second, moved network encoding off the game thread, and stopped re-sending props that never stop moving.

## 0.6.x

- Removed name tags and talking indicators. Only downed teammates are marked.
- Hiding under a bed shows a voice meter: the louder you are, the redder it gets; fully red means the Nun can hear you.

## 0.5.0

- Character selection with a 3D preview (MULTIPLAYER → CUSTOMIZE).

## 0.4.0

- Proximity voice chat (open mic or push to talk), with the Nun hearing shouts.
- Arm swing and fuller walk/run animation for teammates.

## Earlier

- MULTIPLAYER on the main menu, lobby with LAN game list, joining games in progress.
- Knockdown, revive, bleed-out, spectating and shared game over; the Nun hunts every player.
- Synced items, doors, cabinets, puzzles, traps and physics.
