# Release status — 1.0.0

What this build does, what was checked and how, what is still unchecked, and what has to be settled before it is
published. "Scripted" means a run of real game copies on one PC driven by the mod's test driver (`-nmm-` flags), over
the local loopback connection; nobody played. "Human" means people played or listened. Details of every scripted run are
in [VALIDATION.md](VALIDATION.md).

## Build

| | |
|---|---|
| Mod version | 1.0.0, network protocol 19 (connects only to 1.0.0) |
| Game | Nun Massacre Definitive Edition, Windows, 2022.06.08 (`Assembly-CSharp.dll` SHA-256 `0132e2e1556cdd8026eb42a08774cfeb94e313c4e29d40cfe640c8fc5fa0e871`) |
| Loader | BepInEx 5.4.23.3 (`BepInEx_win_x64_5.4.23.3.zip` SHA-256 `41a089e5b1b1f0713b331346baf6677b1184c69eabebf51101097954e854c749`), inside the setup |
| Raytracer | Vercidium Audio 1.10.1, `vaudionative.dll` SHA-256 `d58a5a4dc196eca34941b31b465d395fdfb69af73595d423f89b8f8dfaa66c7e` |
| Plugin | `NunMassacre.Multiplayer.dll` SHA-256 `f102cf487e815a0b078b08f4a9a18d799a15617bc3539328efea658eb9c1e524` |
| Setup | `NunMassacreCoop-Setup-1.0.0.exe` SHA-256 `1a1da37d24ceb49203f28cccf477fb63aadd3da106791f60082c3210adf9ba4c` |
| Archives | `NunMassacreCoop-1.0.0.zip` and `NunMassacreCoop-Source-1.0.0.zip`: their SHA-256 are in `SHA256SUMS-1.0.0.txt` beside them |

The setup carries the same plugin, raytracer and loader as the player zip; each file in the zip is listed with its
SHA-256 in `MANIFEST.sha256`, and the three release files in `SHA256SUMS-1.0.0.txt`. All were built from one source
snapshot by `scripts/package.ps1`.

## Coverage

| Area | Scripted evidence (this build unless noted) | Human evidence | Not yet checked |
|---|---|---|---|
| Nun hears noises through the building, by difficulty | Noise at six spots × four difficulties, her rays' share logged | — | Every kind of game noise in every room; the radius of each is the game's own |
| Nun hears voices | A recorded phrase at four spots × four difficulties, five repeats through the floor | — | Real microphones: levels differ by microphone and its gain; open-mic background noise in real rooms |
| Nun investigates what she hears | Five tracked cases: clear, faint, repeated faint, competing, voice through a floor | — | Long sessions; with three or four players; interplay with a chase already on |
| Under-bed hearing and the bed check | A shout and a whistle from under a bed, three spots each, judged by her raytraced ears alone; flip alone and drag out two in 1.3.0–2.0.8 | — | — |
| No one stuck in a puzzle animation | Rope interruption 0.6 s and 0.05 s apart, safety net with the lock off, knocked down mid-pour; full playthrough with no false "is using that" | — | Every other puzzle part with two people at once (covered by the same lock, tried on the rope only) |
| Revives | Plain (35 health), with a kit (100, kit used), a forged request rejected | — | — |
| Endings together | Every ending waits for the group: the good ending (full playthrough), Carnival, Backrooms and PDM (three players: refused alone with who is missing, then played for all, back to the lobby), the acid vent (both killed, credits, lobby), the barbed wire (both caught, its Nun attacking); one ending committed across games; a late joiner kept in the lobby with a notice | — | The barbed wire's kill (it needs a player struggling) |
| The opening together | Letter and school gates; going ahead without an absent player after 60 s | — | — |
| Joining mid-game | Late-join run (pickup, drop, cabinet within 2 cm, models); a stale load follows the host | — | Joining over a slow or lossy connection |
| Disconnects | Host and guest quitting (earlier builds) | — | Real network drops, latency, packet loss (no network simulation was run) |
| Items in the hand | 15 items × 3 characters photographed | — | — |
| Voice chat and raytraced acoustics | Packets, levels and acoustics measured (2.0.4–2.0.8) | **Blind A/B by the project owner**: raytraced picked 7 of 9 (2.0.7); vent voices (2.0.8) | Real microphones on two PCs; push to talk in play |
| Menus | Screenshots of the MULTIPLAYER connect page, the lobby, VOICE & MIC (the microphone's full name), SETTINGS → MULTIPLAYER and MOD CREDITS | — | Controller navigation; Windows display scaling above 100%; resolutions below 1280×720 |
| Setup | Fresh install, reinstall, locked file (rolled back), interrupted setup, odd folder name, another mod present, wrong build, game running, uninstall; installed files match the manifest | — | A protected folder (Program Files) with the permission prompt; OneDrive folders; the GUI path on this build |
| Online play | Invite codes and UPnP against a fake router (unit tests and in game on loopback) | — | **A real router and a friend on another internet connection** |
| Performance | Host CPU with a teammate talking: 51% of one core, 59 fps (2.0.7: 48%) | — | Three and four players; low-end PCs; a 60–120 minute soak |
| Unit suites and smoke test | NetworkTests 14/14, ProtocolTests 11/11; late-join run (host, guest, late joiner) PASS | — | — |

## Human release gates (not passed: nobody has played this build)

Each needs people; the expected results are what the scripted runs showed.

1. **Two PCs on a LAN or VPN.** Host on one, join on the other by its address. Expect: the lobby on both, START GAME
   loads both together, the opening's group moments wait for both.
2. **Over the internet with an invite code.** The host's lobby shows a code; a friend elsewhere pastes it. Expect: they
   join, or the lobby says the router did not open the port (then try a VPN). Note the router model either way.
3. **Real microphones.** Two players talk at normal level, whisper and shout, in a corridor, through a closed door, on
   different floors, with the Nun near (Medium). Expect: talking down a corridor or through
   an open door draws her; ordinary talking straight under her through the floor draws her on Medium; on Easy she
   stops to listen and comes only if it goes on; a whisper never; a closed door muffles. Check the mic test meter and the hiding meter respond.
4. **A two-person playthrough** from the bathtub to an ending on Medium, with knockdowns, revives, a puzzle tried by both
   at once, and hiding two to a bed. Expect: no one frozen in an animation, the same ending on both screens, both back in
   the lobby.
5. **Four players** for at least 30 minutes, everyone talking. Expect: no growing lag, voices from the right places,
   CPU on the host under one core.

Report with the bug-report template (`.github/ISSUE_TEMPLATE/bug_report.md`) and each player's `BepInEx/LogOutput.log`.

## Known issues

- Blood decals, glass shards and the Nun's body can differ slightly between players (cosmetic).
- No host migration: if the host quits, the session ends for everyone.
- A guest's doors and pickups go through the host, so on a slow connection they react a moment late.
- For about a second after a player is moved suddenly (respawn, the console's `tp`), the Nun may judge their voice by the
  simpler wall model while her rays catch up.

## Publication decisions (the owner's, 2026-10-23)

1. **Vercidium Audio licence.** The raytracer ships under Vercidium's free non-commercial licence; the mod is released
   free, with no donations, which the owner takes to be covered. The licence also mentions projects owned or directed by
   another entity and promotion; if Puppet Combo publishes or promotes it, a short note to Vercidium
   (notices@vercidium.com) would settle that for certain. Its required attribution, "Powered by Vercidium Audio", is in
   MOD CREDITS, the README and the notices.
2. **Character models.** Mike, Maddy and Mark are by Vinrax and credited as such; the mod being free, the owner takes
   their use to be fine.
3. **Wording.** "Unofficial fan mod" is gone from the README, the itch.io draft and the credits. Nothing claims the mod is
   official either: that wording waits for Puppet Combo.
4. **Credits.** The mod is by NetworkPlasma (MOD CREDITS, the README, the itch.io draft).
5. **Version.** Released as 1.0.0 (the test builds were numbered 0.3 to 2.0.9; see DEVELOPMENT-HISTORY.md). The network
   protocol stays 19.
