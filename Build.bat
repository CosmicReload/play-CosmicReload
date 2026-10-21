@echo off
rem Double-click: builds the agent, the app and dist\PlayCosmicReloadSetup-<version>.exe.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
pause
