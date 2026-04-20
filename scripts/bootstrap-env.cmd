@echo off
REM One-shot environment bootstrap for PS1Godot.
REM Sets the three env vars the launch scripts and NuGet.Config rely on.
REM Re-run whenever you move the Godot or PCSX-Redux install.
REM
REM Uses `setx` which persists user-scope. You must close and re-open
REM terminals / Rider for the new values to appear.

setlocal

set "GODOT_EXE_PATH=D:\Programs\Godot_v4.7-dev5_mono_win64\Godot_v4.7-dev5_mono_win64.exe"
set "GODOT_NUPKGS_PATH=D:\Programs\Godot_v4.7-dev5_mono_win64\GodotSharp\Tools\nupkgs"
set "PCSX_REDUX_PATH=C:\tools\pcsx-redux\pcsx-redux.exe"

echo Setting GODOT_EXE       = %GODOT_EXE_PATH%
setx GODOT_EXE "%GODOT_EXE_PATH%" >nul

echo Setting GODOT_NUPKGS    = %GODOT_NUPKGS_PATH%
setx GODOT_NUPKGS "%GODOT_NUPKGS_PATH%" >nul

if exist "%PCSX_REDUX_PATH%" (
  echo Setting PCSX_REDUX_EXE = %PCSX_REDUX_PATH%
  setx PCSX_REDUX_EXE "%PCSX_REDUX_PATH%" >nul
) else (
  echo Skipping PCSX_REDUX_EXE — %PCSX_REDUX_PATH% does not exist yet.
  echo Set it yourself when you install PCSX-Redux.
)

echo.
echo Done. Close and re-open any open terminals / Rider for changes to take effect.
endlocal
