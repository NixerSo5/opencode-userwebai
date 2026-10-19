; MiniBrowser v1.0 安装脚本（Inno Setup 6）
; 编译：ISCC.exe MiniBrowser.iss
; 产物：Output\MiniBrowser-Setup-1.0.exe

#define MyAppName "MiniBrowser"
#define MyAppVersion "1.0"
#define MyAppPublisher "opencode-userwebai"
#define MyAppURL "https://github.com/NixerSo5/opencode-userwebai"
#define MyExeName "MiniBrowser.exe"

[Setup]
AppId={{8E0A2F5B-6C1D-4B9A-9C3E-5A2B7D0F1E44}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyExeName}
OutputDir=Output
OutputBaseFilename=MiniBrowser-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项:"

[Files]
; publish 产物（dotnet publish -r win-x64 --self-contained true）
Source: "publish\v1.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
Filename: "https://developer.microsoft.com/microsoft-edge/webview2/"; Description: "未检测到 WebView2 运行时？点此下载（Win11 自带，可不勾选）"; Flags: shellexec unchecked; Check: not WebView2Installed

[Code]
function WebView2Installed(): Boolean;
var
  pv: String;
begin
  Result := RegQueryStringValue(HKLM,
    'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'pv', pv) and (pv <> '') and (pv <> '0.0.0.0');
  if not Result then
    Result := RegQueryStringValue(HKLM,
      'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', pv) and (pv <> '') and (pv <> '0.0.0.0');
  if not Result then
    Result := RegQueryStringValue(HKCU,
      'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', pv) and (pv <> '') and (pv <> '0.0.0.0');
end;
