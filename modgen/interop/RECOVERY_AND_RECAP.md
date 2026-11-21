# Recovery diagnostics, dialogue and Recap

## Recovery playtest tools

Enable **Prisoner cases (test)**, **Outpost history (test)** and **Outlaw recovery parties (test)**. Add **Covert prisoner recovery (test)** for Sayo or **Shek gate recovery (test)** for Maruka. Outlaw camps provide the named seconds. The routes use the same optional Rivalry Core claim system as the other recovery previews.

**F10 → Wasteland Remembers: prisoner recovery** shows the current or most recent stage, recovery method, captive record, outpost key and exact fixture when known. It distinguishes the walk to a lock being refused, a missing work position, and a scout at the fixture whose lockpick attempt Meitou rejects. The lookup only reads the snapshot built by the simulation. It does not spawn, teleport, free or retarget anyone.

A nearby awake squadmate can comment on the still-present signs of the attempt. A worked lock, smashed gate, or empty cage each has a short ambient line. The witness must be nearby, on the same floor, with clear sight. Pending clues expire after one game day and a spoken clue survives a save without replaying. These are Meitou speech bubbles using text. The marks do not create a History entry, a rumour, or a Recap event. Covert rescue and return also stay out of global summaries. Visible assaults and ransoms still have their ordinary custody summaries.

If custody moves during a dispatch, the party keeps the original cell as its search objective. Once it reaches that spot, is blocked nearby, or is discovered there, it can give up the search. A verified new cell at a different qualifying outpost can get one follow-up in the same custody episode. A standing member of the captive's faction must have clear sight nearby, and the report takes six game hours to travel. Moving again invalidates the lead. An optional follow-up must still pass the usual authority, bounty, hostility and Rivalry Core checks. A reload keeps the original cell and reported cell by stable keys. Legacy attempts without a saved first location need a fresh custody episode for this follow-up.

## Using video Recap

TWR uses BeatTheCount's documented `Recap.RecapApi.AddMoment` call through reflection. It leaves capture, playback, fallback screenshots, and recording settings with Recap. The new calls for **the Western Hive ransom handover** and a **public recovery release** offer the existing story once, immediately after the operation succeeds, with the cage's actual coordinates. The next custody sweep writes its usual record without another media request. A covert release offers no custom moment. Existing positive candidates include the first witnessed outpost defence and first completed wall. Prefer moments TWR alone understands, since Recap also tracks ordinary raids, named kills, dismemberments and new buildings. [Recap Workshop description](https://steamcommunity.com/sharedfiles/filedetails/?id=3837629983)

Recap 1.3.1 introduced short silent event clips. The supplied Workshop package is **1.3.3**, with the November 19 playback and performance fixes. Its normal renderer needs Vulkan in SDR and FFmpeg with `libx264`. The 1.3.3 entry DLL selects `Recap.Modern` on TWR's supported clients. A positioned external moment with `picture: true` enters the same capture queue as Recap's own events and is eligible for a clip. The capture starts after Recap receives the request and the scene stays in view, so a TWR handover tends to record the aftermath.

I confirmed the actual TWR DLL calls the actual Recap 1.3.3 DLL and preserves the exact scene in a media request on all five client builds from the release matrix. Recap's own FFmpeg path also encoded and decoded a three-second clip made from synthetic frames. No live GPU capture or camera interaction was exercised. For the first live check, enable **Pictures** and **Event clips**, keep the camera within about 150 m of the cage, close open windows and leave it in view for about five seconds. **F10 → Recap: status** shows accepted moments and encoded clips. Recap can fall back to a still if footage is short or another clip is busy. [Recap update notes](https://steamcommunity.com/sharedfiles/filedetails/changelog/3837629983)

## PZA compatibility

| Mod | Evidence and expected behavior |
| --- | --- |
| Director's Kit for Meitou 0.4.1 | Inspected public source at `9938780daf0df70de156b178a0e0891626d38330`. It owns camera framing, with Shift+K and Shift+J, and steps aside for other camera modes. The TWR recovery and Recap changes leave camera and controls entirely to the host and installed camera mods. A wide or distant shot can still put Recap outside its capture range. [Source](https://github.com/PZA-Mods/PZAMeitouCamera/tree/9938780daf0df70de156b178a0e0891626d38330) |
| SlowMo for Meitou 0.4.2 | Inspected public source at `c73191646e2204cf7b04dc4bed73cab3eafaf127`. Shift+L and kill cam use the simulation clock. Recovery and witness delays use game time, so lower speed stretches them in real time. Recap may produce a still during a heavily slowed event. [Source](https://github.com/PZA-Mods/PZAMeitouSlowMo/tree/c73191646e2204cf7b04dc4bed73cab3eafaf127) |
| Recap 1.3.3 | The uploaded Workshop binaries passed TWR’s positioned-request and duplicate-rejection checks. Their shared capture path accepts these requests. Joint live recording and playback with Director’s Kit or SlowMo remain to be checked. [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3837629983) |

The PZA source review used Windows/Linux Meitou code and the upstream manifests. Both manifests require Meitou 0.20.1 or newer. These TWR recovery routes use APIs available starting in 0.21.0. None of these integrations adds a new hard dependency.

## Steam Workshop update check

SteamCMD can download and validate this single item. Use the account that owns Kenshi and let Steam prompt for credentials.

```text
steamcmd +login YOUR_STEAM_USERNAME +workshop_download_item 233860 3837629983 validate +quit
```

If SteamCMD signs into a separate Steam library, check the path printed by SteamCMD. For the copy Meitou actually loads, inspect `steamapps/workshop/appworkshop_233860.acf` in the Steam library that holds Kenshi. Find item `3837629983` under `WorkshopItemsInstalled` and compare its `timeupdated` with Valve's public `time_updated` for the item. At review time the server returned `1795129446` (Nov 19, 2026 at 6:04:06 p.m. EST). Future uploads will move that number.

```powershell
$library = 'C:\Program Files (x86)\Steam'
Select-String -Path (Join-Path $library 'steamapps\workshop\appworkshop_233860.acf') -Pattern '"3837629983"' -Context 0,8
$meta = Invoke-RestMethod -Method Post -Uri 'https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/' -Body @{'itemcount'='1'; 'publishedfileids[0]'='3837629983'}
$meta.response.publishedfiledetails[0] | Select-Object title, result, time_updated
```

Set `$library` to your actual Steam library if Kenshi is installed elsewhere. Restart Meitou's launcher after the download so it picks up the current subscribed folder. [Valve Workshop metadata API](https://partner.steamgames.com/doc/webapi/ISteamRemoteStorage#GetPublishedFileDetails) · [SteamCMD command syntax](https://developer.valvesoftware.com/wiki/SteamCMD)
