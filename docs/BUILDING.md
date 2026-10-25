# Building from source

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

## Settings file

`BepInEx/config/local.nunmassacre.multiplayer.cfg` holds the player's name, last invite code or address and port, whether hosting opens the router port (`OpenRouterPort`), character, and voice options (including the push-to-talk key and each teammate's volume as `PlayerVolumes`). For a bug report about the Nun, `NunDiagnostics = true` under `[Debug]` writes what she hears and why to the log.

## Credits in full

- Loader: [BepInEx 5.4.23.3](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.3) and Harmony, carried inside the setup program.
- Raytraced audio: **Powered by Vercidium Audio** ([vercidium.com](https://vercidium.com)), its native library `vaudionative.dll` shipped beside the plugin under Vercidium's free non-commercial licence. This mod is free and earns nothing; Vercidium Audio is Vercidium's, and its licence terms apply to it.
- Character models: Mike, Maddy, Mark and Maverick by Vinrax ([sketchfab.com/vinrax](https://sketchfab.com/vinrax), [x.com/Vinrax](https://x.com/Vinrax)); the attributions are in `THIRD-PARTY-NOTICES.md`. They keep their author's ownership; no redistribution licence is asserted here. Maverick is converted from `redneck-character-psx-style.zip` by `python scripts/convert-player-model.py redneck`, which leaves out the cigarette.
- Motion capture: Mixamo (walks, runs, idle, jumps, crawl, shooting, lying on the stomach, a hit reaction, throwing, picking up, opening, and the body's sprawl from Falling Back Death). The crouch is made from the walks and the idle by `scripts/make-crouch.py`.
