# RejiDisplay — LED Output Manager (v0.1 MVP)

RejiDisplay is a lightweight, deliberately simple C# / .NET 8 WPF application designed to send independent image content to Windows extended displays (connected to NovaStar VX2000 Pro inputs or secondary monitors).

---

## v0.1 MVP Features Implemented

1. **Win32 Physical Display Enumeration**:
   - Enumerates physical monitors via Win32 P/Invoke (`EnumDisplayMonitors`, `GetMonitorInfo`, `GetDpiForMonitor`).
   - Retrieves resolution, physical bounds, DPI context, primary status, and monitor IDs.
   - Listens to Windows display topology changes (`DisplaySettingsChanged`).

2. **Strict Display Safety & Exclusions**:
   - **Primary Display Protection**: The operator's primary screen is strictly excluded from being assigned as an LED output.
   - **Reserved CENTER Display**: Explicit CENTER display designation for Windows Extended Desktop (e.g., PowerPoint / browsers). Reserved displays are excluded from assignment choices.
   - **Card Uniqueness**: LEFT LED and RIGHT LED cards cannot claim the same physical display simultaneously.

3. **Persistent & Robust Identity**:
   - Display assignments are tracked via `DeviceName`, `DeviceId`, and resolution verification.
   - If a saved display is disconnected or ambiguous on launch, it stays inactive (`Needs assignment`).
   - Content is NEVER silently redirected to the primary screen.

4. **Borderless Fullscreen Windowing**:
   - Creates borderless, un-bordered WPF fullscreen windows positioned precisely using physical screen coordinates (`SetWindowPos`) for negative monitor bounds and mixed-DPI setups.
   - Supports `Fit` (Uniform letterbox), `Fill` (UniformToFill crop), and `Stretch` (Fill).

5. **Safe Media Handling**:
   - Image drag-and-drop supporting PNG, JPG, JPEG, WEBP, and BMP.
   - Safe validation (`BitmapDecoder`) before replacing visible media. Corrupt/invalid files trigger an error banner without blanking live output.

6. **Blackout & Independent Lifecycle**:
   - `BLACK` / `RESTORE` toggle keeps the display signal active while showing solid black.
   - Individual `SHOW` and `STOP` controls for each card, plus a global `STOP ALL` button.
   - Independent `Identify Displays` overlays (3.5s auto-fade) running on separate transparent windows without interrupting live output.

---

## Build & Test Instructions

### Prerequisites
- Windows 10/11 x64
- .NET 8 SDK installed (`dotnet --info`)

### Building the Project
From the repository root:
```powershell
dotnet restore
dotnet build
```

### Running Automated Unit Tests
```powershell
dotnet test
```

### Launching the Application
```powershell
dotnet run --project src/RejiDisplay/RejiDisplay.csproj
```

---

## Physical Acceptance Test Procedure (2-Monitor Setup)

To perform the physical acceptance test with **Monitor 1 (Operator)** and **Monitor 2 (LED Output)**:

1. **Setup Windows Desktop**:
   - Ensure Windows display settings are set to **Extend these displays** (Win + P → Extend).

2. **Launch Application**:
   - Run `dotnet run --project src/RejiDisplay/RejiDisplay.csproj`.
   - Verify that the Operator UI opens centered on **Monitor 1**.

3. **Identify Displays**:
   - Click `🔍 EKRANLARI NUMARALANDIR (IDENTIFY)`.
   - Confirm that a semi-transparent overlay appears on **Monitor 1** (`OPERATÖR / BİRİNCİL EKRAN`) and **Monitor 2**, fading out automatically after 3.5 seconds.

4. **Assign Output**:
   - On the **LEFT LED** card, select **Ekran 2** from the *Hedef Ekran* dropdown.
   - Verify that **Ekran 1** (Operator) does NOT appear in the dropdown.

5. **Drag-and-Drop Image**:
   - Drag an image file (PNG/JPG/WEBP) onto the **LEFT LED** drop zone.
   - Confirm the thumbnail and filename update.

6. **Start Live Output**:
   - Click `▶ YAYINLA (SHOW)`.
   - Confirm that **Monitor 2** shows the image in borderless fullscreen.
   - Confirm that **Monitor 1** remains completely interactive.

7. **Test Scaling Modes**:
   - Switch between `Fit`, `Fill`, and `Stretch` radio buttons; verify the image scaling updates live on **Monitor 2**.

8. **Test Blackout & Restore**:
   - Click `⚫ SİYAH EKRAN (BLACK)`. Verify **Monitor 2** turns solid black without closing the window.
   - Click `🟡 GERİ YÜKLE (RESTORE)`. Verify the image reappears.

9. **Test Output Lifecycle**:
   - Click `⏹ DURDUR (STOP)`. Verify **Monitor 2** fullscreen window closes cleanly without affecting operator controls.
