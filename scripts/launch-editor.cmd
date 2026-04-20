@echo off
REM Launch the Godot editor on the PS1Godot project.
REM
REM Set GODOT_EXE to override the default path, or edit the line below.
REM Requires Godot 4.4+ with .NET (Mono) support.

setlocal
set "GODOT=%GODOT_EXE%"
if "%GODOT%"=="" set "GODOT=D:\Programs\Godot_v4.7-dev5_mono_win64\Godot_v4.7-dev5_mono_win64.exe"
if not exist "%GODOT%" (
  echo [launch-editor] ERROR: Godot not found at "%GODOT%".
  echo                Set the GODOT_EXE environment variable or edit this script.
  exit /b 1
)
"%GODOT%" --editor --path "%~dp0..\godot-ps1"
endlocal
