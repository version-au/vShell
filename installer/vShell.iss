; vShell setup (Inno Setup 6). Packages dist\vShell.exe - build it first with build.cmd.
;
; Make the setup:  "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\vShell.iss
; It lands in dist\setup\vShell-setup.exe - the same name in every release, so a web page can link to the latest (the version is read from dist\vShell.exe).
;
; Installs for the current user only (no admin): %LOCALAPPDATA%\Programs\vShell. That folder has to be writable - vShell's own
; updater swaps vShell.exe in place (vShell.new.exe / vShell.old.exe next to it), which Program Files wouldn't allow.
; Settings, plugins and the log stay in %APPDATA%\vShell as always, so an existing setup carries straight over.

#define SourceExe "..\dist\vShell.exe"
#if !FileExists(SourceExe)
  #error dist\vShell.exe is missing - run build.cmd first
#endif
; "0.38.144.0" -> "0.38.144"
#define AppVer RemoveFileExt(GetVersionNumbersString(SourceExe))

[Setup]
; signed with the Certum certificate when build.cmd signs (/DSIGN /Svsuite=...sign.cmd): the setup and its uninstaller
#ifdef SIGN
SignTool=vsuite
#endif
AppId={{6E1B6C2A-3F7D-4C1E-9B5A-7D2E8F4A1C30}
AppName=vShell
AppVersion={#AppVer}
AppVerName=vShell {#AppVer}
AppPublisher=Steven Van Teylingen
VersionInfoVersion={#AppVer}
DefaultDirName={autopf}\vShell
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; vShell's single-instance lock: setup and uninstall ask you to close vShell first
AppMutex=vShell.SingleInstance.v1
SetupIconFile=..\vshell.ico
UninstallDisplayIcon={app}\vShell.exe
UninstallDisplayName=vShell
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=..\dist\setup
OutputBaseFilename=vShell-setup

[Messages]
SetupAppRunningError=vShell is running. Close it (right-click its tray icon > Exit), then click OK to carry on.
UninstallAppRunningError=vShell is running. Close it (right-click its tray icon > Exit), then click OK to carry on.

[Tasks]
Name: "startup"; Description: "Start vShell when I sign in (the same as Settings > System > ""vShell - run at login"")"
Name: "desktopicon"; Description: "A shortcut on the desktop"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\vShell"; Filename: "{app}\vShell.exe"
Name: "{autodesktop}\vShell"; Filename: "{app}\vShell.exe"; Tasks: desktopicon

[Registry]
; the same entry vShell's own "run at login" option writes (StartupManager.SetSelf): HKCU Run, named vShell - so that option shows it
; ticked, and turning it off there removes this one. Same name = it replaces one made while vShell ran from another folder.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "vShell"; ValueData: """{app}\vShell.exe"""; Tasks: startup
; switched off in Task Manager before? Windows keeps that separately - remove it, which counts as switched on
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; ValueType: none; ValueName: "vShell"; Flags: deletevalue; Tasks: startup

[Run]
Filename: "{app}\vShell.exe"; Description: "Start vShell now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; left by vShell's updater next to the exe
Type: files; Name: "{app}\vShell.new.exe"
Type: files; Name: "{app}\vShell.old.exe"
Type: files; Name: "{app}\vShell.failed.exe"
Type: files; Name: "{app}\vshell-write-test.tmp"
Type: dirifempty; Name: "{app}"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  ApprovedKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run';

{ vShell was hiding Explorer's taskbar (settings.json "HideWindowsTaskbar": true)? }
function TaskbarWasHidden(): Boolean;
var
  Json: AnsiString;
begin
  Result := False;
  if LoadStringFromFile(ExpandConstant('{userappdata}\vShell\settings.json'), Json) then
    Result := Pos('"HideWindowsTaskbar": true', String(Json)) > 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    { vShell is closed by now (AppMutex). If it was hiding Explorer's taskbar, bring it back - also when vShell was ended
      without closing properly. Only then: it would reset an auto-hide taskbar otherwise. }
    if TaskbarWasHidden() and FileExists(ExpandConstant('{app}\vShell.exe')) then
      Exec(ExpandConstant('{app}\vShell.exe'), '--restore-taskbar', '', SW_HIDE, ewWaitUntilTerminated, Code);
    { start at sign-in: however it was turned on (here, or vShell's own option) }
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'vShell');
    RegDeleteValue(HKEY_CURRENT_USER, ApprovedKey, 'vShell');
  end;

  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() and DirExists(ExpandConstant('{userappdata}\vShell')) then
  begin
    if MsgBox('Also delete your vShell settings, backups, plugins, wallpapers and log?' + #13#10 + #13#10 +
              ExpandConstant('{userappdata}\vShell') + #13#10 + #13#10 +
              'Your note files are kept either way (they live where you put them).' + #13#10 +
              'Keep the settings (No) if you might install vShell again - it picks up where you left off.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\vShell'), True, True, True);
  end;
end;
