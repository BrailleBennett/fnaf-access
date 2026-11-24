# fnaf-access

**fnaf-access** is a standalone Windows accessibility companion for **Five Nights at Freddy's (FNaF 1)** designed for blind and visually impaired players.

It translates keyboard input into FNaF 1's mouse-driven controls and outputs live game state to your screen reader (speech + braille) or system TTS via [Prism](https://github.com/ethindp/prism) (`Refractor`), without modifying or redistributing any game files.
[*Join the support Discord*](https://discord.gg/JZ2VSvCrZf)

---

## What the Mod Does

- **Full Keyboard Control:** Navigate menus, configure Custom Night AI levels (0–20), pan smoothly across the office, toggle doors and lights, flip the camera monitor, switch cameras, mute the phone call, and honk Freddy's nose.
- **Live Speech & Braille Narration (Prism):** Automatically detects and outputs to your active screen reader (NVDA, JAWS, Narrator, etc.) using combined speech and braille when supported, falling back to system TTS and switching dynamically if a screen reader starts or stops.
- **Strict Sighted Parity ("Accessible, Not Easier"):**
  - Only announces information a sighted player can legitimately see right now (the active camera feed and animatronic poses, Bonnie/Chica in the doorway/window only while that door's light is on and in view, Golden Freddy in the office, power/usage/time HUD, visible menus, endings, and jumpscares).
  - Enforces physical on-screen reachability: you cannot toggle a door or light panel while panned away from it, or mute the phone call when the Mute Call button is not visible.
- **Animatronic-First Camera Callouts:** Cameras announce who is visible and their pose first (for example, `"Freddy, Camera 1A: Show Stage."`, `"Foxy peeking out, Camera 1C: Pirate Cove."`, `"Golden Freddy poster, Camera 2B: West Hall Corner."`) so you can react immediately.
- **Jumpscare Announcements:** Announces the character who jumpscares you—Bonnie, Chica, Foxy, Freddy, Power-Out Freddy, and Golden Freddy (both in-game and the Custom Night `1-9-8-7` easter egg).
- **Fullscreen & Windowed Support:** Dynamically maps FNaF 1's `1280x720` logical canvas to your live window or fullscreen display.
- **Configurable Keybinds:** All controls are stored in `keybinds.json` and can be remapped freely.

---

## Requirements

- **OS:** Windows 10 / 11 (64-bit)
- **Game:** *Five Nights at Freddy's* (Steam PC version, v1.132)
- **Speech/Braille:** Any screen reader supported by Prism (NVDA, JAWS, etc.) or Windows TTS

---

## Installation

### From GitHub Releases (Recommended)

1. Open the **Releases** page of this repository and download the latest `fnaf-access-win-x64-v*.zip` archive.
2. Extract the zip file to a folder of your choice (`fnaf-access.exe` is a standalone single-file executable with the .NET runtime and Prism bundled inside—no extra installation required).
3. Run `fnaf-access.exe`.
   - You should hear `"FNaF Access ready. Waiting for Five Nights at Freddy's."`
   - A default `keybinds.json` file is created automatically next to `fnaf-access.exe`.
4. Launch **Five Nights at Freddy's** from Steam.
   - `fnaf-access` automatically attaches to the game and begins narrating and accepting keyboard controls whenever the FNaF 1 window is focused.
   - Press `F1` in-game at any time for context-sensitive keybind help, `F8` to temporarily toggle keyboard passthrough, or `Ctrl+C` in the console window to exit `fnaf-access`.

### Optional Command-Line Flags

```powershell
# Start the accessibility companion (default)
.\fnaf-access.exe

# Test your Prism speech and braille backend
.\fnaf-access.exe --test-speech

# Generate or verify keybinds.json without starting the companion
.\fnaf-access.exe --generate-config

# Show CLI usage help
.\fnaf-access.exe --help
```

### Building from Source

Requires the **.NET 11 SDK** on Windows:

```powershell
dotnet build fnaf-access/fnaf-access.csproj
dotnet run --project fnaf-access/fnaf-access.csproj
```

---

## Default Keybinds

Default bindings avoid NVDA/JAWS modifier keys (`Insert`, `CapsLock`). Keys are only intercepted while FNaF 1 is the active foreground window.

### Menus & Custom Night

| Action | Default Key | Notes |
| --- | --- | --- |
| Previous / Next Menu Item | `Up` / `Down` | Title screen navigation |
| Confirm / Start | `Enter` or `Space` | Activates selected menu item, advances intro/ending screens, or clicks Ready in Custom Night |
| Select Animatronic (Custom Night) | `Left` / `Right` | Cycles focus between Freddy, Bonnie, Chica, and Foxy |
| Increase / Decrease AI (Custom Night) | `Up` / `Down` | Adjusts focused animatronic's AI level (`0`–`20`) |

### Office Controls (Monitor Down)

| Action | Default Key | Notes |
| --- | --- | --- |
| Pan Left / Right | Hold `Left` / `Right` | Pans smoothly at native game speed; releasing stops panning |
| Left Door / Left Light | `Q` / `A` | Only works while the left panel is physically in view |
| Right Door / Right Light | `E` / `D` | Only works while the right panel is physically in view |
| Raise / Lower Monitor | `Space` | Toggles the camera monitor |
| Mute Phone Call | `M` | Only works while the Mute Call button is visible (works with monitor up or down) |
| Honk Freddy's Nose | `H` | Only works while the office poster is in view |

### Camera Monitor (Monitor Up)

| Action | Default Key | Notes |
| --- | --- | --- |
| Camera 1A – 1C | `1`, `2`, `3` | Show Stage, Dining Area, Pirate Cove |
| Camera 2A – 2B | `4`, `5` | West Hall, West Hall Corner |
| Camera 3 | `6` | Supply Closet |
| Camera 4A – 4B | `7`, `8` | East Hall, East Hall Corner |
| Camera 5 | `9` | Backstage |
| Camera 6 | `0` | Kitchen (audio only) |
| Camera 7 | `-` | Restrooms |
| Previous / Next Camera | `Left` / `Right` | Cycles through all 11 cameras |
| Lower Monitor | `Space` | Returns to the office |

### Status Queries & Companion Controls

| Action | Default Key | Notes |
| --- | --- | --- |
| Power & Usage | `G` | Speaks remaining power percentage and usage level |
| Time & Night | `T` | Speaks current hour and night number |
| Visible Door & Light Status | `S` | Speaks door/light state (and visible animatronic) for whichever side is currently in view |
| Current View | `V` | Speaks facing side/visible controls, active camera, Title stars/option, or all Custom Night AI levels |
| Repeat / Describe | `R` | Repeats the last announcement or re-describes the active camera |
| Context Help | `F1` | Speaks relevant controls for the current screen |
| Toggle Keyboard Passthrough | `F8` | Temporarily passes all keys directly to the game |

---

## Customizing Keybinds (`keybinds.json`)

On first run, `fnaf-access.exe` creates `keybinds.json` in the same folder as the executable. You can edit it in any text editor and restart `fnaf-access.exe` to apply your changes. Any missing actions are automatically filled back in with their defaults.

### Supported Key Names

- **Letters:** `A`–`Z`
- **Top-row digits:** `0`–`9` (or `D0`–`D9`)
- **Navigation & common keys:** `Left`, `Right`, `Up`, `Down`, `Space`, `Enter`, `Tab`, `Backspace`, `Delete`, `Home`, `End`, `PageUp`, `PageDown`, `Escape`
- **Function keys:** `F1`–`F12`
- **Punctuation:** `-`, `=`, `,`, `.`, `/`, `;`, `'`, `[`, `]`, `\`, `` ` ``
- **Raw Windows Virtual-Key hex codes:** e.g., `"0x20"` for Space

### Example `keybinds.json` (Default Layout)

```json
{
  "Bindings": {
    "PanLeft": "Left",
    "PanRight": "Right",
    "MenuUp": "Up",
    "MenuDown": "Down",
    "MenuConfirm": "Enter",
    "ToggleLeftDoor": "Q",
    "ToggleLeftLight": "A",
    "ToggleRightDoor": "E",
    "ToggleRightLight": "D",
    "ToggleMonitor": "Space",
    "MuteCall": "M",
    "HonkNose": "H",
    "Cam1A": "1",
    "Cam1B": "2",
    "Cam1C": "3",
    "Cam2A": "4",
    "Cam2B": "5",
    "Cam3": "6",
    "Cam4A": "7",
    "Cam4B": "8",
    "Cam5": "9",
    "Cam6": "0",
    "Cam7": "-",
    "CamPrevious": "Left",
    "CamNext": "Right",
    "ReadPowerAndUsage": "G",
    "ReadTimeAndNight": "T",
    "ReadDoorStatus": "S",
    "ReadCurrentView": "V",
    "RepeatLastOrDescribe": "R",
    "TogglePassthrough": "F8",
    "SpeakHelp": "F1"
  }
}
```

### Example: Remapping Doors, Lights, and Panning

If you prefer panning with `A` / `D` and toggling doors/lights with `J` / `U` (left) and `L` / `O` (right), update just those entries in `keybinds.json`:

```json
{
  "Bindings": {
    "PanLeft": "A",
    "PanRight": "D",
    "ToggleLeftDoor": "U",
    "ToggleLeftLight": "J",
    "ToggleRightDoor": "O",
    "ToggleRightLight": "L",
    "ToggleMonitor": "Space"
  }
}
```

*(Any actions you omit will automatically be restored with their default keys on the next launch.)*

---

## Disclaimer

`fnaf-access` is an unofficial, fan-made accessibility companion. It does not include, modify, or redistribute any *Five Nights at Freddy's* assets or executables. *Five Nights at Freddy's* is created and owned by Scott Cawthon.
