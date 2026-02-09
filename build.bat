@echo off
setlocal

set ROOT=%~dp0
set SLN=%ROOT%KodakScannerApp.sln

if not exist "%SLN%" (
  echo Solution not found: %SLN%
  exit /b 1
)

set VSWHERE="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist %VSWHERE% (
  for /f "usebackq delims=" %%i in (`%VSWHERE% -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
    set MSBUILD=%%i
  )
)

if not defined MSBUILD (
  if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" set MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe
)
if not defined MSBUILD (
  if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe" set MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe
)

if not defined MSBUILD (
  echo MSBuild not found. Open %SLN% in Visual Studio and build.
  exit /b 1
)

"%MSBUILD%" "%SLN%" /p:Configuration=Debug /p:Platform="Any CPU"
endlocal
