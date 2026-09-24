; winstall — Inno Setup script.
; Build: iscc /DMyArch=x64 /DMyVersion=1.1.0 /DSrcDir=..\dist\win-x64 /DOutDir=..\dist-installer installer\winstall.iss
; MyArch: x64 | x86 | arm64. Per-user install (PrivilegesRequired=lowest), no UAC.

#define MyAppName "winstall"
#define MyAppPublisher "Yanletov168"
#define MyAppURL "https://github.com/Yanletov168/winstall-app"
#define MyAppExe "winstall.exe"

#if MyArch == "x64"
  #define ArchAllowed "x64compatible"
  #define ArchInstallMode "x64compatible"
#elif MyArch == "arm64"
  #define ArchAllowed "arm64"
  #define ArchInstallMode "arm64"
#else
  #define ArchAllowed ""
  #define ArchInstallMode ""
#endif

[Setup]
AppId={{BF65CF67-319D-4AD8-806A-8B6C0080C908}
AppName={#MyAppName}
AppVersion={#MyVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
; Windows 10 floor: documents the supported range instead of allowing older releases.
MinVersion=10.0
#if ArchAllowed != ""
ArchitecturesAllowed={#ArchAllowed}
ArchitecturesInstallIn64BitMode={#ArchInstallMode}
#endif
PrivilegesRequired=lowest
OutputDir={#OutDir}
OutputBaseFilename=Setup-{#MyAppName}-{#MyArch}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExe}
VersionInfoVersion={#MyVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "{#SrcDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
english.WingetMissingAsk=WinGet was not found on this system.%n%nwinstall needs the system-wide Windows Package Manager. Download and install the official App Installer now?%n(Requires an internet connection and a single UAC prompt.)
russian.WingetMissingAsk=В системе не найден WinGet.%n%nДля работы winstall нужен системный Windows Package Manager. Скачать и установить официальный App Installer сейчас?%n(Нужны интернет и один UAC-запрос.)
english.WingetSkipped=Skipped. Install App Installer manually later:%nhttps://github.com/microsoft/winget-cli/releases/latest
russian.WingetSkipped=Пропущено. Позже установите App Installer вручную:%nhttps://github.com/microsoft/winget-cli/releases/latest
english.WingetDownloadFailed=Could not download App Installer. Install it manually:%nhttps://github.com/microsoft/winget-cli/releases/latest
russian.WingetDownloadFailed=Не удалось скачать App Installer. Установите его вручную:%nhttps://github.com/microsoft/winget-cli/releases/latest
english.WingetInstallFailed=App Installer setup did not complete. winstall cannot work until WinGet is installed:%nhttps://github.com/microsoft/winget-cli/releases/latest
russian.WingetInstallFailed=Установка App Installer не завершена. Без WinGet winstall работать не будет:%nhttps://github.com/microsoft/winget-cli/releases/latest
english.WingetInstalled=App Installer is installed, WinGet is available.
russian.WingetInstalled=App Installer установлен, WinGet доступен.

[Code]
const
  WingetBundleUrl = 'https://github.com/microsoft/winget-cli/releases/latest/download/Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle';

function WingetPresent(): Boolean;
var
  Res: Integer;
begin
  Result := Exec('winget.exe', '--version', '', SW_HIDE, ewWaitUntilTerminated, Res) and (Res = 0);
end;

{ Runs after files are in place. Never installs anything silently:
  the App Installer download needs explicit consent and its own UAC prompt. }
procedure InstallWingetIfMissing();
var
  BundlePath, DownloadCmd, ElevateCmd: String;
  Res: Integer;
begin
  if WingetPresent() then Exit;
  if MsgBox(CustomMessage('WingetMissingAsk'), mbConfirmation, MB_YESNO) <> IDYES then
  begin
    MsgBox(CustomMessage('WingetSkipped'), mbInformation, MB_OK);
    Exit;
  end;
  BundlePath := ExpandConstant('{tmp}\DesktopAppInstaller.msixbundle');
  DownloadCmd := '-NoProfile -NonInteractive -Command "Invoke-WebRequest -Uri ''' +
    WingetBundleUrl + ''' -OutFile ''' + BundlePath + ''' -UseBasicParsing"';
  if (not Exec('powershell.exe', DownloadCmd, '', SW_SHOW, ewWaitUntilTerminated, Res)) or (Res <> 0) then
  begin
    MsgBox(CustomMessage('WingetDownloadFailed'), mbError, MB_OK);
    Exit;
  end;
  ElevateCmd := '-NoProfile -NonInteractive -Command "Start-Process powershell -Verb RunAs -Wait ' +
    '-ArgumentList ''-NoProfile -NonInteractive -Command Add-AppxPackage ''''' + BundlePath + '''''"' ;
  if (not Exec('powershell.exe', ElevateCmd, '', SW_SHOW, ewWaitUntilTerminated, Res)) or (Res <> 0) then
  begin
    MsgBox(CustomMessage('WingetInstallFailed'), mbError, MB_OK);
    Exit;
  end;
  if WingetPresent() then
    MsgBox(CustomMessage('WingetInstalled'), mbInformation, MB_OK)
  else
    MsgBox(CustomMessage('WingetInstallFailed'), mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallWingetIfMissing();
end;
