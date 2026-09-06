; NtoNTier v1.2.3 安装程序脚本
; 内容: NtoNTier.exe + WebView2 托管 DLL + WebView2 Runtime 离线安装包
; 机制: 检测系统未装 WebView2 Runtime 时, 静默安装随包分发的离线独立安装包

[Setup]
AppId={{A7C3E1F2-9B4D-4C5E-8F6A-2D3E4F5A6B7C}
AppName=NtoNTier
AppVersion=1.4.0
AppPublisher=NtoNTier
AppPublisherURL=https://github.com/rejetto/hfs
AppVerName=NtoNTier 1.4.0
DefaultDirName={autopf}\NtoNTier
DefaultGroupName=NtoNTier
DisableDirPage=no
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\NtoNTier.exe
OutputBaseFilename=NtoNTier-v1.4.0-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
OutputDir=..\dist

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "..\dist\client\NtoNTier.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\client\Microsoft.Web.WebView2.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\client\Microsoft.Web.WebView2.WinForms.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\client\WebView2Loader.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\downloads\installer\webview2-runtime-standalone-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\NtoNTier"; Filename: "{app}\NtoNTier.exe"
Name: "{autodesktop}\NtoNTier"; Filename: "{app}\NtoNTier.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\webview2-runtime-standalone-x64.exe"; Parameters: "/silent /install /norestart"; StatusMsg: "正在安装 WebView2 Runtime 运行库（首次安装需要几分钟）..."; Flags: runhidden waituntilterminated; Check: NotWebView2Installed

[Code]
// 检测系统是否已安装 WebView2 Runtime
function NotWebView2Installed: Boolean;
var
  v: string;
begin
  Result := not RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', v);
  if Result then
    Result := not RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', v);
end;



