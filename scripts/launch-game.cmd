@echo off
REM Run the Godot project (standalone, no editor).
REM Useful for smoke-testing the preview shader without editor overhead.

setlocal
set "GODOT=%GODOT_EXE%"
if "%GODOT%"=="" set "GODOT=D:\Programs\Godot_v4.7-dev5_mono_win64\Godot_v4.7-dev5_mono_win64.exe"
if not exist "%GODOT%" (
  echo [launch-game] ERROR: Godot not found at "%GODOT%".
  exit /b 1
)
"%GODOT%" --path "%~dp0..\godot-ps1"
endlocal
