@echo off
REM Launch PCSX-Redux mounting the PS1Godot-built ISO in CD-ROM mode.
REM Used by the Run-on-PSX flow when any scene has Route=XA clips and
REM their .xa sidecars are present — XA needs the disc bus, PCdrv
REM doesn't carry it.
REM
REM PCSX-Redux CLI flags used here:
REM   -iso PATH        mount the .cue/.bin pair as a virtual disc
REM   -loadexe PATH    bootstrap with the runtime's PSX-EXE (mkpsxiso
REM                    doesn't write a SYSTEM.CNF for our discs, and
REM                    the -loadexe path is independent of the ISO mount
REM                    so the runtime CD reads still target the disc)
REM   -fastboot        skip BIOS intro
REM   -run             auto-resume emulation after load
REM   -stdout          route ramsyscall_printf to stdout, captured below

setlocal
set "REDUX=%PCSX_REDUX_EXE%"
if "%REDUX%"=="" set "REDUX=C:\tools\pcsx-redux\pcsx-redux.exe"
if not exist "%REDUX%" (
  echo [launch-emulator-iso] ERROR: PCSX-Redux not found at "%REDUX%".
  echo                       Set the PCSX_REDUX_EXE environment variable.
  exit /b 1
)

set "OUT=%~dp0..\godot-ps1\build"
set "CUE=%OUT%\game.cue"
set "EXE=%OUT%\psxsplash-cdrom.ps-exe"
if not exist "%EXE%" set "EXE=%OUT%\psxsplash-cdrom.elf"

if not exist "%CUE%" (
  echo [launch-emulator-iso] ERROR: %CUE% does not exist.
  echo                       Build the ISO first: python tools\build_iso\build_iso.py
  exit /b 1
)
if not exist "%EXE%" (
  echo [launch-emulator-iso] ERROR: %EXE% does not exist.
  echo                       Build the CDROM-loader runtime first: scripts\build-psxsplash-cdrom.cmd
  exit /b 1
)

set "LOG=%OUT%\pcsx.log"
if exist "%LOG%" del "%LOG%"
"%REDUX%" -stdout -iso "%CUE%" -loadexe "%EXE%" -fastboot -run > "%LOG%" 2>&1
endlocal
