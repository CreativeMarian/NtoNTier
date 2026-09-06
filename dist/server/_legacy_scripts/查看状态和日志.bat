@echo off
chcp 65001 >nul
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "NtoNServer.ps1" -Status
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "NtoNServer.ps1" -Log
echo.
pause