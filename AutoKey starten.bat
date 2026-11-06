@echo off
rem Startet AutoKey ohne Konsolenfenster.
start "" powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0AutoKey.ps1"
