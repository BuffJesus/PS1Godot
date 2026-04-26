@echo off
REM Build the psxsplash runtime with CD-ROM loader (LOADER=cdrom). This
REM is what the ISO build needs — XA-ADPCM streaming requires the disc
REM bus, which PCdrv can't provide.
REM
REM Output goes to godot-ps1\build\psxsplash-cdrom.{ps-exe,elf} so the
REM PCdrv build at psxsplash.{ps-exe,elf} stays untouched and the
REM Run-on-PSX flow can pick whichever variant matches the scene.
REM
REM Caveat: Makefile shares one obj cache, so this script runs
REM `make clean` first. After running, the PCdrv build is invalidated
REM — re-run scripts\build-psxsplash.cmd to switch back to PCdrv mode.
REM
REM Requires the same toolchain as build-psxsplash.cmd.

setlocal
where mipsel-none-elf-gcc >nul 2>&1
if errorlevel 1 (
  echo [build-psxsplash-cdrom] ERROR: mipsel-none-elf-gcc not on PATH.
  echo                          Install via: ..\pcsx-redux-main\mips.ps1  then  mips install 14.2.0
  exit /b 1
)
where make >nul 2>&1
if errorlevel 1 (
  echo [build-psxsplash-cdrom] ERROR: make not on PATH. Install MSYS2 or Git Bash and add make.
  exit /b 1
)

set "SRC=%~dp0..\psxsplash-main"
set "OUT=%~dp0..\godot-ps1\build"
if not exist "%OUT%" mkdir "%OUT%"

pushd "%SRC%"
make clean >nul 2>&1
make all -j LOADER=cdrom
if errorlevel 1 (
  popd
  echo [build-psxsplash-cdrom] Build failed.
  exit /b 1
)
popd

copy /Y "%SRC%\psxsplash.elf"    "%OUT%\psxsplash-cdrom.elf"    >nul
copy /Y "%SRC%\psxsplash.ps-exe" "%OUT%\psxsplash-cdrom.ps-exe" >nul
echo [build-psxsplash-cdrom] Done: %OUT%\psxsplash-cdrom.ps-exe (+ .elf)
echo [build-psxsplash-cdrom] PCdrv build cache invalidated; re-run scripts\build-psxsplash.cmd to switch back.
endlocal
