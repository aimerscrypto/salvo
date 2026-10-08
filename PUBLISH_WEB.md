# Publishing SALVO on the web (itch.io)

The browser version is a Unity WebGL build. Players open a link, click **Play**, and can host or
join online matches with a 6-letter code. Browser and Windows players can share a match.

## 1. Build

In Unity: **SALVO → Build → WebGL (Browser)**. Output: `Builds/WebGL/` (`index.html`, `Build/`,
`TemplateData/`). Then zip the **contents** of that folder (not the folder itself), so
`index.html` sits at the top of the zip:

```
cd Builds/WebGL
powershell Compress-Archive -Path * -DestinationPath ../SALVO-web.zip -Force
```

The build switches Unity's active platform to WebGL. Switch back with
**File → Build Profiles → Windows → Switch Platform** before the next Windows build (the
SALVO Windows build menu items do this automatically).

## 2. Upload to itch.io

1. Sign in at <https://itch.io> (a free account) → **Upload new project**.
2. **Kind of project:** HTML.
3. **Uploads:** add `SALVO-web.zip`, tick **This file will be played in the browser**.
4. **Embed options:**
   - Viewport: **1280 × 720**
   - Tick **Fullscreen button**
   - Tick **SharedArrayBuffer support** (needed by some Unity features in browsers)
   - **Mobile friendly:** leave unticked (keyboard and mouse game).
5. **Visibility:** *Draft* first. Open the page, click Play, and check a practice match and an
   online match. Then set it to **Public** (or **Restricted** with a password to share only with
   friends) and **Save**.
6. Share the page link.

To update the game later, build again, zip it, and replace the file in the same upload slot.

## Playing together

- One player picks **Play → Online → Host**, and the match code appears in the corner / pause menu.
- Others pick **Join with code**. Browser and Windows (`SALVO.exe`) players can mix.
- Everyone must run the same version (older builds use a different network version and won't see
  the match).
- LAN / direct connect isn't available in the browser (browsers can't open network ports).

## Good to know

- Online play uses Unity Relay through the project's Unity Cloud project, whose free tier is
  plenty for friends playing together.
- Browsers run the game slower than the Windows build (WebAssembly + WebGL2). The Performance
  preset is the right choice on laptops with integrated graphics. Chrome or Edge works best.
- The muffled sound through walls / at a distance isn't available in browsers (no per-sound
  filters there); those sounds are just quieter instead.
- Progress (level, coins, finishes) is saved in the browser's storage for that site, so it's
  separate from the Windows version's save and is lost if the player clears site data.
