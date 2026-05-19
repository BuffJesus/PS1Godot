@echo off
REM Launch the Godot editor on the PS1Godot project.
REM Auto-rebuilds the C# plugin DLL first so source edits are picked up — Godot
REM 4.x .NET does not refresh the plugin assembly on startup, and a stale DLL
REM silently runs old code.
REM
REM Set GODOT_EXE to override Godot's path. Set GODOT_NO_BUILD=1 to skip the
REM pre-launch rebuild (e.g. to inspect the editor with a known-bad build).
REM Requires Godot 4.4+ with .NET (Mono) support and dotnet SDK on PATH.

setlocal
set "GODOT=%GODOT_EXE%"
if "%GODOT%"=="" set "GODOT=D:\Programs\Godot_v4.7-dev5_mono_win64\Godot_v4.7-dev5_mono_win64.exe"
if not exist "%GODOT%" (
  echo [launch-editor] ERROR: Godot not found at "%GODOT%".
  echo                Set the GODOT_EXE environment variable or edit this script.
  exit /b 1
)

if "%GODOT_NO_BUILD%"=="1" goto :skip_build
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [launch-editor] WARN: dotnet not on PATH; skipping pre-build. Plugin may be stale.
  goto :skip_build
)
echo [launch-editor] Building C# plugin...
pushd "%~dp0..\godot-ps1"
dotnet build --nologo -v q
if errorlevel 1 (
  echo [launch-editor] ERROR: dotnet build failed. Fix compile errors before launching.
  echo                Set GODOT_NO_BUILD=1 to skip this gate and launch anyway.
  popd
  exit /b 1
)
popd
:skip_build

REM Tee Godot's stdout+stderr to a log file so a crash that closes the
REM cmd window before we can copy still leaves a trail on disk. PowerShell
REM Tee-Object preserves the live console output too. Path is alongside
REM the project so it's easy to find.
set "LOGFILE=%~dp0..\godot-ps1\.editor.log"
echo [launch-editor] Logging to "%LOGFILE%"
powershell -NoProfile -Command "& '%GODOT%' --editor --path '%~dp0..\godot-ps1' 2>&1 | Tee-Object -FilePath '%LOGFILE%'"

REM Keep the cmd window open if Godot exited abnormally so the author
REM can scroll back through the log. Press any key to dismiss.
echo.
echo [launch-editor] Godot exited (code %ERRORLEVEL%). Log saved to "%LOGFILE%".
pause
endlocal
