# LED Output Manager — Product Requirements Document (PRD)

**Version:** 1.0 — development brief  
**Target:** Windows 10/11 x64 desktop  
**Suggested stack:** C# / .NET 8 or later / WPF  
**Primary development environment:** Antigravity IDE; Visual Studio is optional for debugging and publishing.  
**Status:** Requirements, not an assertion that features have been implemented.

## 1. Product goal

Build a deliberately simple drag-and-drop application that sends independent image, video, and later web content to selected **Windows extended displays**. These displays are physical GPU outputs connected to the inputs of a NovaStar VX2000 Pro. The application does **not** control the NovaStar processor or change Windows display topology; it creates and manages fullscreen windows on already-extended displays.

An operator should be able to select a destination and drop content onto it without learning scenes, layers, canvases, timelines, or projector terminology.

**Core interaction:** Choose output → drop media → show it on that display.

## 2. Actual production topology

```text
NVIDIA Quadro P2000 (4 × DisplayPort)
  DP1 → Operator monitor → application control panel
  DP2 → VX2000 Input 1 → LEFT LED  → managed by app
  DP3 → VX2000 Input 2 → CENTER LED → plain Windows extended desktop
  DP4 → VX2000 Input 3 → RIGHT LED → managed by app
```

- The **CENTER is never claimed by this application**. Operators drag PowerPoint, browsers, or other Windows apps onto it directly.
- Initial configuration contains LEFT and RIGHT; design the internals for N independently configurable outputs later.
- Windows must already be configured to **Extend these displays**. Do not automatically switch to Duplicate, change resolution, or rearrange displays.
- Physical LED pixel dimensions (e.g. source artwork 860 × 1720) are not necessarily the GPU signal resolution. Fit/fill/stretch is performed within the assigned Windows display; VX2000 mapping is configured separately.

## 3. Design principles

1. **Operational simplicity:** One card per managed output. No scene/layer editor.
2. **Independence:** Changing or blacking out RIGHT must not affect LEFT or CENTER.
3. **Persistent signal:** Switching content must not destroy/recreate the output window or change GPU display mode unnecessarily.
4. **Explicit safety:** Never silently take over the operator monitor or CENTER.
5. **Test real displays:** A preview inside the app is not proof of successful physical output.
6. **Incremental delivery:** Implement and validate v0.1 before adding video or web.

## 4. Operator interface

The main window remains on the operator monitor and displays two cards:

```text
LED OUTPUT MANAGER

LEFT LED                         RIGHT LED
Display: [Choose ▼]             Display: [Choose ▼]
[Drop image here]               [Drop image here]
Current: logo-left.png          Current: logo-right.png
Scale: [Fit ▼]                  Scale: [Fit ▼]
[SHOW] [BLACK] [RESTORE]        [SHOW] [BLACK] [RESTORE]
Status: ACTIVE                  Status: ACTIVE

CENTER: Windows Extended Desktop — NOT managed by this app
[Identify Displays]
```

Cards should have obvious active/disconnected/black states, large controls, and clear media names. UI language can initially be Turkish; keep strings centralized for future localization.

## 5. v0.1 — Mandatory MVP

### 5.1 Enumerate displays

- Discover active Windows displays and show friendly label, resolution, primary status, and bounds; refresh when the display configuration changes.
- Show refresh rate if reliable; do not block MVP if unavailable.
- `Identify Displays` temporarily overlays a large number/name on detected displays, then removes overlays without changing output assignments or content.
- Use monitor/device identifiers where available, but treat them as fallible; display numbers alone are not stable across reconnections.
- Handle negative desktop coordinates, non-primary displays left/above the primary, and mixed DPI correctly. Do not assume every monitor starts at X ≥ 0 or shares one DPI.

### 5.2 Assign outputs

- Configure LEFT and RIGHT independently from active displays.
- Prevent duplicate assignment of one display to both cards.
- Do not allow the designated CENTER display to be assigned without an explicit configuration change and confirmation; default behavior is to exclude it.
- Warn before assigning the primary/operator display; require explicit confirmation.
- Do not open a fullscreen output window until the operator explicitly starts it.
- If only two physical displays exist, allow a **single-output test**: operator display + one simulated LED display.

### 5.3 Drag-and-drop image playback

- Accept PNG, JPG/JPEG, and optionally WEBP if implemented/tested correctly.
- A drop on LEFT changes only LEFT; a drop on RIGHT changes only RIGHT.
- Validate file existence and decoding before replacing the current visible content; report errors in the control panel.
- Display image in a dedicated, borderless, correctly positioned fullscreen window on the selected **real secondary Windows display**.
- Keep the control panel on the operator monitor.
- `Fit`: preserve aspect ratio and show entire image, with black letterboxing if necessary.
- `Fill`: preserve aspect ratio and crop as necessary.
- `Stretch`: fill output without preserving aspect ratio.
- Default to `Fit`; allow per-output selection.

### 5.4 Black / restore

- `BLACK` displays solid black on only the selected output without closing its window.
- `RESTORE` returns that output to its previously displayed media and scale mode.
- Do not alter the other output or the CENTER desktop.

### 5.5 Save configuration

Store per-output display association, scale mode, and last media path in a local JSON configuration under an appropriate per-user app-data directory. Use safe writes. On startup, load the settings, but **do not automatically take over a screen** in v0.1. If the previously selected display is missing or ambiguous, show `Needs assignment` rather than guessing.

### 5.6 MVP acceptance test

With Windows set to Extended Desktop and two physical monitors connected:

1. Control UI remains on Monitor 1.
2. Identify Displays correctly distinguishes Monitor 1 and Monitor 2.
3. Assign LEFT to Monitor 2; explicitly start output.
4. Drag a real 860 × 1720 PNG/JPG onto LEFT.
5. Monitor 2 shows the image fullscreen, not a preview inside the control panel.
6. Fit, Fill, and Stretch behave as specified.
7. Black and Restore work without closing the output window.
8. Settings survive restart; no screen is taken over without confirmation.
9. Invalid media or missing monitor causes a visible error rather than a crash.

With **three** physical monitors (operator + LEFT + RIGHT), verify that changing/blackening RIGHT never changes LEFT. The CENTER Windows desktop is outside app scope. Do not claim simultaneous-output independence has been physically tested with only two monitors.

**Gate:** Do not start v0.5 until the actual two-monitor MVP test passes.

## 6. v0.5 — Usable media controller

- Add MP4 playback first; expand to MOV/MKV/WEBM only after codec and deployment tests.
- Evaluate LibVLCSharp for playback; document native runtime packaging and license obligations. WPF MediaElement may be used for an initial experiment but should not be assumed to handle every codec reliably.
- Per-output Play/Pause/Restart, Loop toggle, Mute/Volume.
- Keep the fullscreen window and display signal alive during media changes.
- Prepare new media before switching when practical; avoid leaving a blank screen on a failed load.
- Show a clear status indicator and current media name.
- Build a portable/self-contained Windows x64 release; document any native dependencies.

## 7. v0.7 — Web / simultaneous interpretation

- Add a per-output URL source using WebView2, plus simple named URL presets.
- Preserve independent browser state per output as appropriate.
- Test login, cookies, autoplay, pop-ups, audio routing, and fullscreen behavior on the actual simultaneous-interpretation service.
- Do **not** promise all DRM, WebRTC, permission-sensitive, or browser-extension-dependent services will work embedded. Provide a documented fallback using ordinary Edge/Chrome on the designated extended display.

## 8. v1.0 — Event readiness

- One-click named presets (e.g. LEFT Logo, RIGHT Logo, RIGHT Simultaneous, RIGHT Video, Black).
- Clear output health: connected/disconnected, assigned display, current source, black state, and any media error.
- Detect display topology changes; attempt recovery only when a matching display is unambiguous. Otherwise ask the operator to reassign. Never redirect output to CENTER/primary as an automatic fallback.
- Operator mode to lock display assignments during an event.
- Basic rotating local logs with no credentials or sensitive web session contents.
- Test on the actual Quadro P2000 → adapters/cables → VX2000 inputs with the fixed display arrangement; verify resolution, EDID/handshake, reconnect, fullscreen positioning, video stability, and behavior after Windows sleep/restart.
- Include a quick operator runbook and recovery instructions.

## 9. Technical architecture

Suggested modules:

```text
MainWindow (operator UI)
  ├─ DisplayService (enumeration, identity, topology notifications)
  ├─ OutputManager
  │    ├─ OutputController LEFT → OutputWindow LEFT
  │    └─ OutputController RIGHT → OutputWindow RIGHT
  ├─ MediaService (image first; video/web later)
  └─ SettingsService (JSON persistence)
```

- Each `OutputController` owns its own assignment, window, current source, scaling, and black state.
- WPF `Window` placement must respect Windows per-monitor DPI and virtual desktop bounds; do not assume WPF device-independent units equal physical pixels.
- Do not depend on hardcoded `DISPLAY2` / `DISPLAY4` indexes.
- Keep window ownership and shutdown behavior explicit. Closing the control panel should not accidentally leave inaccessible fullscreen windows; provide an operator-accessible `Stop all outputs` / exit confirmation.
- Separate media decoding failures from display-routing failures.
- Prefer simple, maintainable code and tests for configuration/assignment logic over speculative abstractions.

## 10. Out of scope

No NovaStar API control, NDI/SDI, streaming, recording, OBS/FreeShow integration, PowerPoint import, scene editor, timeline, video compositing, transitions, cloud, accounts, database, or automatic Windows display topology changes. CENTER content remains managed by ordinary Windows apps.

## 11. Build, test, and handoff

- Source code and solution must be buildable on Windows from Antigravity's terminal with the installed .NET SDK (`dotnet restore`, `dotnet build`). Visual Studio may also open the same `.sln`.
- Keep a short `README.md` with prerequisites, run/build commands, test steps, and current limitations.
- Commit after each validated milestone; never claim physical display tests passed based only on compilation or mock/preview tests.
- Publish a Windows x64 release without requiring Visual Studio on the reji PC; verify deployment on a clean machine before calling it portable.

---

# Antigravity agent instructions — READ FIRST

You are implementing this PRD in a **Windows workspace**. The user is building a simple, real-world event output controller, not a general-purpose media production suite.

**Work on v0.1 ONLY. Do not implement the entire roadmap in one pass.**

1. Inspect the repository and installed Windows/.NET environment first. If no project exists, create a minimal WPF solution targeting a supported installed .NET SDK (prefer .NET 8 or later). Do not assume Visual Studio is installed.
2. Summarize the proposed file structure and short implementation plan, then implement the smallest vertical slice: detect displays → assign a non-primary display → drag-and-drop an image → show it in a real borderless fullscreen window.
3. Add Fit/Fill/Stretch, Black/Restore, settings persistence, and a simple Identify Displays action after the slice builds.
4. Keep CENTER excluded from app-controlled output and do not change Windows Extend/Duplicate settings.
5. Run `dotnet restore` and `dotnet build`; fix errors. State explicitly which tests were run and which require the user's physical monitor interaction. Do not claim a successful physical test you could not observe.
6. Ask the user to perform the two-monitor acceptance test before proceeding to video, web, presets, or reconnection automation.
7. Do not install unnecessary packages, add a backend, introduce a database, or rewrite unrelated files. Explain any package and license implications before introducing native media dependencies.
8. Preserve working code; make small, reviewable changes and update README with exact run instructions.

**First deliverable:** a buildable v0.1 WPF application that displays a dropped image on a user-selected *physical* extended monitor, with a control window remaining on the operator monitor.
