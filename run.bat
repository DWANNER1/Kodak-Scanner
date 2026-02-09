@echo off
setlocal

set ROOT=%~dp0
set EXE=%ROOT%KodakScannerApp\bin\Debug\KodakScannerApp.exe

if not exist "%EXE%" (
  echo App not built yet. Run build.bat first.
  exit /b 1
)

start "Kodak Scanner" "%EXE%"
endlocal
