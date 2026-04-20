@echo off
REM Launch PCSX-Redux with PCdrv pointed at the splashpack build directory.
REM Phase 2+ only — needs godot-ps1\build\psxsplash.elf to exist.
REM
REM PCSX-Redux CLI flags used:
REM   -pcdrv             enable PCdrv (host filesystem passthrough)
REM   -pcdrvbase PATH    base directory PCdrv exposes to the PS1
REM   -loadexe PATH      load a PS1 executable on start
REM   -fastboot          skip the BIOS intro animation
REM   -run               auto-resume emulation after load

setlocal
set "REDUX=%PCSX_REDUX_EXE%"
if "%REDUX%"=="" set "REDUX=C:\tools\pcsx-redux\pcsx-redux.exe"
if not exist "%REDUX%" (
  echo [launch-emulator] ERROR: PCSX-Redux not found at "%REDUX%".
  echo                   Set the PCSX_REDUX_EXE environment variable or edit this script.
  exit /b 1
)

set "OUT=%~dp0..\godot-ps1\build"
REM Prefer the raw PSX-EXE format (.ps-exe) over the ELF. PCSX-Redux's
REM -loadexe handles both, but ELF loading has been unreliable on this build —
REM PC gets set correctly but the code segment sometimes isn't copied to RAM,
REM leaving the CPU to execute garbage (manifests as thousands of 8-bit reads
REM from random addresses right after "Successful: new PC = ...").
set "EXE=%OUT%\psxsplash.ps-exe"
if not exist "%EXE%" set "EXE=%OUT%\psxsplash.elf"

if not exist "%EXE%" (
  echo [launch-emulator] ERROR: %EXE% does not exist.
  echo                   Build psxsplash first: scripts\build-psxsplash.cmd
  exit /b 1
)

REM -stdout routes psxsplash's ramsyscall_printf output to stdout; we redirect
REM to build/pcsx.log so the calling tool (or us, after the fact) can read it.
REM Appended with >> in case a previous run is already tailing — but we nuke
REM it first so each launch starts clean.
set "LOG=%OUT%\pcsx.log"
if exist "%LOG%" del "%LOG%"
"%REDUX%" -stdout -pcdrv -pcdrvbase "%OUT%" -loadexe "%EXE%" -fastboot -run > "%LOG%" 2>&1
endlocal
