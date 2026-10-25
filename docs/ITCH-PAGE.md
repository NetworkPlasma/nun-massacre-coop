# itch.io page (draft)

Not published. Keep the page free with no donations or "pay what you want": the mod is released free, and that is
what the Vercidium Audio licence and the character models are used under (see RELEASE-STATUS.md).

---

**Title:** Nun Massacre Co-op

**Short description:** Survive the Nun together. Up to four players, proximity voice chat, and a Nun who hears you.

**Classification:** Tool / mod. **Price:** free, no donations. **Platform:** Windows.

---

## Survive the Nun together

A multiplayer mod for **Nun Massacre Definitive Edition**. Up to four players (one host and three friends) play the whole
game together: the opening, every puzzle, every ending and the alternate scenarios.

- **She hears you.** Talk, run or drop something and the sound travels through the school on raytraced acoustics: down
  corridors, round corners, muffled through walls and floors. She stops to listen, turns towards a sound, and comes to
  where it was made to search. The harder the difficulty, the sharper her ears.
- **Proximity voice chat** with the same acoustics: a tiled bathroom rings, a voice through a wall is a murmur, a
  friend in a vent sounds like they are in a vent.
- **Knocked down, not killed.** Crawl, call for help, and get revived before you bleed out. Shout to pull her off a
  friend, hide two to a bed, and split up at your peril: she goes after whoever is alone.
- **Shared puzzles and endings.** Anyone can work on any puzzle and progress counts for everyone. Endings play on every
  screen at once.
- **The game's own look.** Teammates are drawn with the game's PS1 shader and move with motion-captured animation;
  menus use the game's own art and fonts.
- **No markers, no pings.** You find downed friends by looking and listening.

## Requirements

- **Nun Massacre Definitive Edition for Windows, build 2022.06.08** (your own copy, from itch.io or Patreon).
  Every player needs it.
- Windows 10 or 11, 64-bit.
- A microphone is optional: without one you can still whistle and point.

## Install

1. Download **NunMassacreCoop-1.0.0.zip** and unzip it anywhere.
2. Run **NunMassacreCoop-Setup.exe**. It finds the game by itself; if not, press BROWSE and pick `Nun Massacre.exe`.
3. Press **INSTALL**, then **PLAY**, and choose **MULTIPLAYER** on the main menu.

The setup installs the BepInEx 5 mod loader and the mod. It never changes the game's own files.
**Update:** run the new setup and press UPDATE. **Uninstall:** run the setup and press UNINSTALL; it can also turn the
loader off so the game starts exactly as it came.

## Playing together

- **Host:** MULTIPLAYER → enter a name → HOST GAME. Send the invite code (already on your clipboard) to your friends.
- **Friends:** MULTIPLAYER → click the invite code box and paste the code (Ctrl+V): you join straight away. On the
  host's network, you can instead select their game in the list.
- **Host:** START GAME, then pick difficulty and scenario as usual. Friends can join a game that is already running.

**Playing online: the host needs UPnP turned on in their router's settings.** Hosting then opens the game's port by
itself. Most routers have it on already; if the lobby says the port did not open, turn on UPnP in the router's settings
page and host again. Otherwise let a friend host, forward TCP port 27845 yourself, or all join a free virtual LAN (Radmin
VPN, ZeroTier, Tailscale) and type the host's VPN address into the invite code box, then press Enter. Windows Firewall may ask the first time you host: allow it.

## Controls

| | |
|---|---|
| Revive a teammate | Hold **Interact** next to them |
| Give up while down | Hold **Jump** 2 s |
| Point / whistle | **1** / **2** |
| Team roster | Hold **Tab** |
| Push to talk (if chosen) | Hold **V** |
| Spectating: next view / free camera | **Interact** / **Jump** |
| Console (`unstuck` and more) | **F2** |

## Troubleshooting

- **Friends time out with the invite code:** check that UPnP is on in the host's router, and allow Nun Massacre through
  Windows Firewall on the host. A new code is made each time you host.
- **"Different game build or co-op mod version":** everyone needs build 2022.06.08 and the same mod version.
- **No voice:** VOICE & MIC in the lobby: check the microphone, the mic test and the mic level.
- **Stuck in an animation:** F2, then `unstuck`.
- More in the README inside the download.

## Reporting a bug

Say what happened, how to make it happen again, host or guest, how many players, difficulty and where in the game, and
attach `BepInEx/LogOutput.log` from the game folder. [Link to the issue tracker once it exists.]

## Credits

- Multiplayer mod: NetworkPlasma
- Character models: Mike, Maddy, Mark and Maverick by Vinrax (Maddy, Mark and Maverick under CC BY 4.0; full attributions
  in THIRD-PARTY-NOTICES.md)
- Motion capture: Mixamo
- Raytraced audio: **Powered by Vercidium Audio** (vercidium.com)
- Mod loader: BepInEx and Harmony
- Nun Massacre is a game by Puppet Combo. You need your own copy of the game.
