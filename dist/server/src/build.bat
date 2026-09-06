@echo off
rem ============================================
rem NtoNServerControl build (.NET Framework 4.8 / csc.exe)
rem Embed supernode.exe as resource (single-file dist)
rem ============================================
setlocal
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found. Install .NET Framework 4.x
  exit /b 1
)

if not exist "..\supernode.exe" (
  echo [ERROR] missing ..\supernode.exe
  exit /b 1
)

echo Compiling NtoNServerControl.exe ...
"%CSC%" /nologo /target:winexe /out:..\NtoNServerControl.exe ^
  /win32manifest:app.manifest ^
  /resource:..\supernode.exe,supernode.exe ^
  /r:System.dll ^
  /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll ^
  Theme.cs Config.cs ServerCore.cs Controls.cs ServerMain.cs Program.cs

if errorlevel 1 (
  echo.
  echo [ERROR] Build failed. See messages above.
  exit /b 1
)

echo.
echo [OK] Built: ..\NtoNServerControl.exe
echo supernode embedded. Single-file server console ready.
exit /b 0
