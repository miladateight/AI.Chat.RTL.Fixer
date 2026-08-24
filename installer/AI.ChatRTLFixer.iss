; Inno Setup script for AI RTL Fixer.
; Style mirrors the Key Fix / Net Doctor installers (Milad AT8).
; Build with: scripts\package-installer.ps1  (or ISCC.exe on this file directly)

#define MyAppName "AI RTL Fixer"
#define MyAppExeName "AI.ChatRTLFixer.Tray.exe"
#define MyOutputBaseFilename "AIChatRTLFixerSetup"
#define MySourceDir "..\dist\portable-self-contained-win-x64"
#define MyAppId "{{35E1F24F-FC8C-4E84-ABD9-48E9A34A0BA4}"
#define MyAppVersion "1.1.2"
#define MyAppPublisher "Milad AT8"
#define MyAppURL "https://github.com/miladateight/AI.RTL.Fixer"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppContact={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename={#MyOutputBaseFilename}-{#MyAppVersion}
SetupIconFile=..\assets\branding\app-logo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardImageFile=..\assets\branding\installer-sidebar.bmp
WizardSmallImageFile=..\assets\branding\installer-small.bmp
InfoBeforeFile=INFO-BEFORE.txt
LicenseFile=..\docs\LICENSE
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
VersionInfoCopyright=Copyright (c) 2026 {#MyAppPublisher}.
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start AI RTL Fixer automatically when Windows starts"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; Opt-in only. The matching value is also cleaned up by the uninstaller below.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AIChatRTLFixer"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} on GitHub"; Filename: "{#MyAppURL}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent unchecked

[UninstallRun]
; Safely close the app if it is running (no forced kill of unrelated processes).
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#MyAppExeName} /F >NUL 2>NUL & exit /B 0"; Flags: runhidden waituntilterminated; RunOnceId: "StopAIChatRTLFixer"
; Remove the optional "Start with Windows" registry entry (HKCU), if the user enabled it.
Filename: "{cmd}"; Parameters: "/C reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v AIChatRTLFixer /f >NUL 2>NUL & exit /B 0"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveAIChatRTLFixerStartup"

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
// After uninstall, offer (optionally) to remove user settings and logs in %AppData%.
// Nothing in AppData is deleted unless the user explicitly confirms.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if MsgBox('Do you also want to delete AI RTL Fixer user settings and logs?' + #13#10 +
              '(%AppData%\AIChatRTLFixer)', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\AIChatRTLFixer'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\AIChatRTLFixer'), True, True, True);
    end;
  end;
end;
