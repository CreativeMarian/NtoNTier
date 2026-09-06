@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo 守护模式: 进程退出会自动重启，请保持本窗口打开。
echo 关掉本窗口 = 停止守护（但不会立即杀已运行的进程）。
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "NtoNServer.ps1" -Watch
pause