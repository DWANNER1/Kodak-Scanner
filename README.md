# Kodak Scanner

This is a Windows desktop app that hosts a local web UI for controlling a Kodak Scanmate i1150 scanner. It uses WIA for scanning and supports multi-page PDF/TIFF export.

## Requirements
- Windows 7 or newer
- .NET Framework 4.8
- Kodak WIA driver installed (the scanner must appear in Windows scanners)

## Features
- Configure device, DPI, color mode, duplex, and max pages
- Scan multiple pages from the feeder
- Export formats: PDF (multi-page), TIFF (multi-page), JPG, PNG
- Runs as a standalone desktop app with embedded web UI

## Project Layout
- `KodakScannerApp` - WinForms app + local HTTP server + web UI
- `desktop-app` - Electron + React shell that loads the local web UI

## Build and Run
Open `KodakScannerApp\KodakScannerApp.csproj` in Visual Studio and run.

## Electron Shell (Windows x64)
The Electron app embeds the existing local web UI at `http://localhost:5005/`.

### Dev
From `desktop-app`:
```powershell
npm install
npm run dev
```
Start the C# service separately (Visual Studio or `run.bat`).

### Build Installer (x64)
From `desktop-app`:
```powershell
npm run dist
```
The installer will be under `desktop-app\release`.

## Notes
- If the device only exposes TWAIN and not WIA, WIA-based scanning will not find the scanner. In that case, a TWAIN-based module is required.
- Output defaults to `Documents\Scans` unless overridden in the UI.

## Cloud UI (Render) + Local Agent
The scanner must remain on a local Windows PC. The cloud service provides a remote UI and relays commands to the local agent over WebSockets.

### Cloud service
From `cloud`:
```powershell
npm install
npm start
```

Environment variables:
- `CLOUD_USER` (default: kodak)
- `CLOUD_PASS` (default: kodak)
- `PORT` (Render provides this automatically)

### Local agent
Set these in `KodakScannerApp\App.config`:
- `CloudUrl` (e.g., https://YOUR-RENDER-SERVICE.onrender.com)
- `CloudUser` / `CloudPass`
- `DeviceName`

Build and run the scanner app normally. It will connect to the cloud automatically and listen for scan commands.
