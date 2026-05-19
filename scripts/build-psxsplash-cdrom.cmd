@echo off
REM Shim into scripts/run.py — see that file for the actual logic.
python "%~dp0run.py" build-psxsplash --loader=cdrom %*
