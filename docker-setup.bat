@echo off
rem Double-click wrapper: runs docker-setup.ps1 without needing an execution
rem policy change on the user's machine.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0docker-setup.ps1"
pause
