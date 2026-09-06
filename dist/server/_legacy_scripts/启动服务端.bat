@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ============================================
echo   NtoNTier 服务端 · 启动中
echo ============================================
powershell -NoProfile -ExecutionPolicy Bypass -File "NtoNServer.ps1" -Start
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "NtoNServer.ps1" -Status
echo.
echo 提示: 服务已在后台运行。窗口可关闭。
pause