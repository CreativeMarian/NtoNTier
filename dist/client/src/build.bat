@echo off
rem ============================================
rem NtoNTier client build script (.NET Framework 4.8 / csc.exe)
rem Embed edge.exe + tap-windows.exe as resources (single-file dist)
rem ============================================
setlocal
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found. Install .NET Framework 4.x
  exit /b 1
)

if not exist "..\edge.exe" (
  echo [ERROR] missing ..\edge.exe
  exit /b 1
)
if not exist "..\tap-windows.exe" (
  echo [ERROR] missing ..\tap-windows.exe
  exit /b 1
)
if not exist "..\hfs.exe" (
  echo [ERROR] missing ..\hfs.exe
  exit /b 1
)

echo Compiling NtoNTier.exe ...
"%CSC%" /nologo /optimize+ /target:winexe /out:NtoNTier.exe ^
  /win32manifest:app.manifest ^
  /resource:..\edge.exe,edge.exe ^
  /resource:..\tap-windows.exe,tap-windows.exe ^
  /resource:..\hfs.exe,hfs.exe ^
  /r:System.dll ^
  /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll ^
  /r:System.Management.dll ^
  /r:System.Runtime.Serialization.dll ^
  /r:System.Xml.dll ^
  /r:libs\Microsoft.Web.WebView2.Core.dll ^
  /r:libs\Microsoft.Web.WebView2.WinForms.dll ^
  Theme.cs Config.cs Net.cs ResourceBootstrap.cs HfsManager.cs DownloadManager.cs FileTransfer.cs BrowserPage.cs Onboarding.cs Controls.cs MainForm.cs ConnectDialog.cs Program.cs

if errorlevel 1 (
  echo.
  echo [ERROR] Build failed. See messages above.
  exit /b 1
)

echo Copying WebView2 runtime DLLs to dist\client ...
copy /y libs\Microsoft.Web.WebView2.Core.dll ..\ >nul
copy /y libs\Microsoft.Web.WebView2.WinForms.dll ..\ >nul
copy /y libs\WebView2Loader.dll ..\ >nul

echo.
echo [OK] Built: %~dp0NtoNTier.exe
echo Engines embedded. WebView2 DLLs copied. Installer-ready distribution.
exit /b 0
