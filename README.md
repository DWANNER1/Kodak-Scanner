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

## Build and Run
Open `KodakScannerApp\KodakScannerApp.csproj` in Visual Studio and run.

## Notes
- If the device only exposes TWAIN and not WIA, WIA-based scanning will not find the scanner. In that case, a TWAIN-based module is required.
- Output defaults to `Documents\Scans` unless overridden in the UI.
