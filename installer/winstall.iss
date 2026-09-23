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
