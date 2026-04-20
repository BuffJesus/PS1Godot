@echo off
REM Build the psxsplash runtime with PCdrv backend for emulator iteration.
REM Output goes to godot-ps1\build\psxsplash.elf so launch-emulator.cmd finds it.
REM
REM Requires:
REM   - mipsel-none-elf toolchain on PATH (install via pcsx-redux-main\mips.ps1)
REM   - GNU make (MSYS2, Git Bash, or WSL)

setlocal
where mipsel-none-elf-gcc >nul 2>&1
if errorlevel 1 (
  echo [build-psxsplash] ERROR: mipsel-none-elf-gcc not on PATH.
  echo                    Install it via: ..\pcsx-redux-main\mips.ps1  then  mips install 14.2.0
  exit /b 1
)
where make >nul 2>&1
if errorlevel 1 (
  echo [build-psxsplash] ERROR: make not on PATH. Install MSYS2 or Git Bash and add make.
  exit /b 1
)

set "SRC=%~dp0..\psxsplash-main"
set "OUT=%~dp0..\godot-ps1\build"
if not exist "%OUT%" mkdir "%OUT%"

pushd "%SRC%"
make all -j PCDRV_SUPPORT=1
if errorlevel 1 (
  popd
  echo [build-psxsplash] Build failed.
  exit /b 1
)
popd

REM Copy both the ELF (for symbols / debugger) and the PSX-EXE (what PCSX-Redux
REM actually loads — see launch-emulator.cmd for the ELF caveat).
copy /Y "%SRC%\psxsplash.elf"    "%OUT%\psxsplash.elf"    >nul
copy /Y "%SRC%\psxsplash.ps-exe" "%OUT%\psxsplash.ps-exe" >nul
echo [build-psxsplash] Done: %OUT%\psxsplash.ps-exe (+ .elf)
endlocal
