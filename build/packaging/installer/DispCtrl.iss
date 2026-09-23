; DispCtrl per-user installer. Built by build/Installer.ps1, which passes:
;   /DAppVersion=0.1.0  /DChannel=beta  /DSourceDir=<published desktop>
;   /DRepoRoot=<repo>   /DOutputDir=<folder for the setup exe>
;
; Per user and unelevated, on purpose: the engine must not run elevated (UIPI
; would cut its tray window off from Explorer, and its command pipe from every
; client), and a per-machine install would need elevation for nothing else.

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z
#endif
#ifndef Channel
  #define Channel "beta"
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<published desktop folder>
#endif
#ifndef RepoRoot
  #define RepoRoot "..\..\.."
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
; Never change the AppId: it is how an upgrade finds the installed copy.
AppId={{6C1B0E4B-6E53-4E0A-9A55-3F0C2D1B7A21}
AppName=DispCtrl
AppVersion={#AppVersion}
AppVerName=DispCtrl {#AppVersion}
AppPublisher=Jesvi Jonathan
AppContact=jesvi22j@gmail.com
AppPublisherURL=https://www.jesvi.net/
AppSupportURL=https://github.com/jesvijonathan/Display-Control/issues
AppUpdatesURL=https://github.com/jesvijonathan/Display-Control/releases
AppCopyright=Copyright (c) 2026 Jesvi Jonathan
VersionInfoVersion={#AppVersion}
VersionInfoProductName=DispCtrl
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\DispCtrl
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
; Restart Manager closes what holds a file by any means it has, and a killed
; engine leaves a hidden taskbar parked off-screen. PrepareToInstall stops it
; properly instead.
CloseApplications=no
RestartApplications=no
ChangesEnvironment=yes
WizardStyle=modern
SetupIconFile={#RepoRoot}\src\DispCtrl.App\Assets\DispCtrl.ico
UninstallDisplayIcon={app}\DispCtrl.App.exe
UninstallDisplayName=DispCtrl
OutputDir={#OutputDir}
OutputBaseFilename=DispCtrl-{#AppVersion}-{#Channel}-win-x64-setup
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "signin"; Description: "Start DispCtrl when I sign in (needed for the tray icon, hotkeys and taskbar hiding)"
Name: "desktopicon"; Description: "Create a desktop shortcut"
Name: "path"; Description: "Add dispctrl to my PATH, for the command line"
; Maintenance, for an update or a reinstall over a copy that misbehaves. Both
; start unticked on every run (see CurPageChanged): remembered from a previous
; install, a reset would repeat itself on every update.
Name: "resetsettings"; Description: "Reset DispCtrl's settings to their defaults (the old file is kept as a backup)"; GroupDescription: "Repair:"; Flags: unchecked
Name: "clearcache"; Description: "Clear DispCtrl's logs and cached monitor data (your named codes and presets are kept)"; GroupDescription: "Repair:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; The same file name the app manages from Settings, so the two agree on
; whether a shortcut exists rather than making a second one.
Name: "{userprograms}\DispCtrl"; Filename: "{app}\DispCtrl.App.exe"; Comment: "Open DispCtrl"
Name: "{userdesktop}\DispCtrl"; Filename: "{app}\DispCtrl.App.exe"; Comment: "Open DispCtrl"; Tasks: desktopicon

[Run]
Filename: "{app}\dispctrl.exe"; Parameters: "startup set --engine on --json"; Flags: runhidden; Tasks: signin; StatusMsg: "Registering DispCtrl to start at sign-in..."
Filename: "{app}\DispCtrl.Engine.exe"; Parameters: "run"; Flags: nowait; StatusMsg: "Starting DispCtrl..."
Filename: "{app}\DispCtrl.App.exe"; Description: "Open DispCtrl now"; Flags: postinstall nowait skipifsilent

[Code]
const
  EnvironmentKey = 'Environment';

{ Stops DispCtrl processes running from Root: the engine gracefully, so it puts
  back any taskbar it hid, and the window outright, which is safe. Returns
  False when the engine is still running afterwards. With RemoveTask, also
  removes the sign-in task, but only when it starts the engine in Root: a
  portable or development copy may own it, and unticking a box here, or
  uninstalling, must not switch that copy off. }
function StopDispCtrl(Root: String; RemoveTask: Boolean): Boolean;
var
  Script, Path, Args: String;
  Code: Integer;
begin
  Result := True;
  // GetTempDir rather than the tmp constant: this also runs in the uninstaller.
  Path := GenerateUniqueName(GetTempDir, '.ps1');
  Script :=
    'param([string]$Root, [switch]$RemoveTask)' + #13#10 +
    '$root = [IO.Path]::GetFullPath($Root).TrimEnd([char]92) + [char]92' + #13#10 +
    'function Mine($name) { @(Get-Process $name -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) }) }' + #13#10 +
    '$engines = Mine ''DispCtrl.Engine''' + #13#10 +
    'foreach ($e in $engines) { Start-Process -FilePath $e.Path -ArgumentList ''stop'' -Wait -WindowStyle Hidden }' + #13#10 +
    'foreach ($e in $engines) { $null = $e.WaitForExit(20000) }' + #13#10 +
    'Mine ''DispCtrl.App'' | Stop-Process -Force -ErrorAction SilentlyContinue' + #13#10 +
    'Mine ''dispctrl'' | Stop-Process -Force -ErrorAction SilentlyContinue' + #13#10 +
    'if ($RemoveTask) {' + #13#10 +
    '  $xml = (schtasks.exe /Query /TN DispCtrl.Engine /XML 2>$null) -join [Environment]::NewLine' + #13#10 +
    '  if ($LASTEXITCODE -eq 0 -and ([xml]$xml).Task.Actions.Exec.Command.Trim([char]34).StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {' + #13#10 +
    '    Start-Process -FilePath (Join-Path $root ''dispctrl.exe'') -ArgumentList ''startup'',''set'',''--engine'',''off'',''--json'' -Wait -WindowStyle Hidden' + #13#10 +
    '  }' + #13#10 +
    '}' + #13#10 +
    'if ((Mine ''DispCtrl.Engine'').Count -gt 0) { exit 1 }' + #13#10;
  if not SaveStringToFile(Path, Script, False) then Exit;
  Args := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + Path + '" -Root "' + Root + '"';
  if RemoveTask then Args := Args + ' -RemoveTask';
  if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
          Args,
          '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := Code = 0;
  DeleteFile(Path);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopDispCtrl(ExpandConstant('{app}'), not WizardIsTaskSelected('signin')) then
    Result := 'DispCtrl''s engine did not stop. Quit it from the tray icon, then run Setup again.';
end;

function PathContains(Paths, Folder: String): Boolean;
begin
  Result := Pos(';' + Uppercase(Folder) + ';', ';' + Uppercase(Paths) + ';') > 0;
end;

procedure AddToPath(Folder: String);
var
  Paths: String;
begin
  if not RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then Paths := '';
  if PathContains(Paths, Folder) then Exit;
  if (Paths <> '') and (Copy(Paths, Length(Paths), 1) <> ';') then Paths := Paths + ';';
  RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', Paths + Folder);
end;

procedure RemoveFromPath(Folder: String);
var
  Paths: String;
  P: Integer;
begin
  if not RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then Exit;
  if not PathContains(Paths, Folder) then Exit;
  Paths := ';' + Paths + ';';
  P := Pos(';' + Uppercase(Folder) + ';', Uppercase(Paths));
  Delete(Paths, P, Length(Folder) + 1);
  Paths := Copy(Paths, 2, Length(Paths) - 2);
  RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', Paths);
end;

// Where DispCtrl keeps its settings, logs and history: the same folder the
// app, the engine and the CLI use (SettingsStore), whatever version installed it.
function DataDir(): String;
begin
  Result := ExpandConstant('{localappdata}\DispCtrl');
end;

// The two repair boxes start unticked on every run, even when the previous
// install had them ticked: Inno remembers task choices for an update.
var
  RepairTasksCleared: Boolean;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectTasks) and not RepairTasksCleared then
  begin
    WizardSelectTasks('!resetsettings,!clearcache');
    RepairTasksCleared := True;
  end;
end;

// Runs after PrepareToInstall has stopped the engine and before the new files
// and the new engine arrive, so nothing holds these files.
procedure ResetSettings();
var
  Settings: String;
begin
  Settings := DataDir() + '\settings.json';
  if FileExists(Settings) then
    RenameFile(Settings, DataDir() + '\settings.backup-' + GetDateTimeString('yyyymmdd-hhnnss', #0, #0) + '.json');
end;

// Logs, the display report, the engine's hotkey status and what the engine
// learned about monitors, which it learns again by itself. Named codes
// (devices\definitions) and presets are the person's own work and stay.
procedure ClearCache();
var
  Dir: String;
begin
  Dir := DataDir();
  DelTree(Dir + '\*.log', False, True, False);
  DelTree(Dir + '\*.log.*', False, True, False);
  DeleteFile(Dir + '\hotkeys-status.json');
  DeleteFile(Dir + '\devices\history.json');
  DelTree(Dir + '\devices\outbox', True, True, True);
  DelTree(Dir + '\devices\*.md', False, True, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    if WizardIsTaskSelected('resetsettings') then ResetSettings();
    if WizardIsTaskSelected('clearcache') then ClearCache();
  end;
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('path') then
      AddToPath(ExpandConstant('{app}'))
    else
      RemoveFromPath(ExpandConstant('{app}'));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { Before any file goes: an engine still running would hold its files, and
      killing it would strand a taskbar. }
    if not StopDispCtrl(ExpandConstant('{app}'), True) then
      SuppressibleMsgBox('DispCtrl''s engine did not stop, so some files may remain. Sign out and back in, then delete ' +
             ExpandConstant('{app}') + '.', mbInformation, MB_OK, IDOK);
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    RemoveFromPath(ExpandConstant('{app}'));
    // Kept by default, so a reinstall picks up where it left off; never
    // deleted by a silent uninstall, which nobody was asked about.
    if DirExists(DataDir()) and not UninstallSilent() then
      if MsgBox('Also delete your DispCtrl settings, presets, named monitor codes and logs?' + #13#10#13#10 +
                'Keep them if you might install DispCtrl again. They are in ' + DataDir() + '.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir(), True, True, True);
  end;
end;
