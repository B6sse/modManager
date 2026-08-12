; Inno Setup script for Basse's Mod Manager
; Run from BassesModManager folder: iscc installer.iss
; Or from repo root: iscc BassesModManager\installer.iss
; CI can pass version: iscc /DMyAppVersion=1.1 BassesModManager\installer.iss

#define MyAppName "Axon"
#ifndef MyAppVersion
  #define MyAppVersion "1.6"
#endif
#define MyAppPublisher "Basse"
#define MyAppExeName "BassesModManager.exe"

; PayloadPath: folder that contains exe, dlls, .config, ThirdParty, Profiles (from build).
; Relative to script dir. CI can pass /DPayloadPath=...
#ifndef PayloadPath
  #define PayloadPath "bin\Release"
#endif

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\BassesModManager
DefaultGroupName={#MyAppName}
OutputDir=..\Output
OutputBaseFilename=Axon_Setup_{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
; Auto-update support (Plans/AUTO_UPDATE_PLAN.md, Spor A): the app holds a mutex with
; this exact name so Setup can detect and cleanly close/replace a running instance
; during silent updates (must match the mutex name created in App.xaml.cs)
AppMutex=BassesModManagerAppMutex
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Main app (exe, config, all dlls – exclude .pdb and .xml)
Source: "{#PayloadPath}\BassesModManager.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadPath}\BassesModManager.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadPath}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
; ThirdParty (from build; required by FrostyModExecutor)
Source: "{#PayloadPath}\ThirdParty\*"; DestDir: "{app}\ThirdParty"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Profiles – only the SDK for the one game this app supports. The full Frosty set is
; ~150 MB of SDKs for Anthem, FIFA, Madden and the rest, none of which can ever be loaded
; here: the app hardcodes one profile and looks the SDK up by name.
Source: "{#PayloadPath}\Profiles\StarWarsSDK.dll"; DestDir: "{app}\Profiles"; Flags: ignoreversion
; Plugins (from build; required to apply mods whose data is in a plugin's own format)
Source: "{#PayloadPath}\Plugins\*"; DestDir: "{app}\Plugins"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Mods – only the small approved ones. The Auric set is several hundred MB and is NOT
; bundled: it would be re-downloaded by every auto-update even for a code-only change, so
; the app fetches it on demand instead. Mods live under ProgramData rather than {app} so
; the app can add and remove them without needing administrator rights.
Source: "Mods\White Dot.fbmod"; DestDir: "{commonappdata}\BassesModManager\Mods"; Flags: ignoreversion
Source: "Mods\Red Dot.fbmod"; DestDir: "{commonappdata}\BassesModManager\Mods"; Flags: ignoreversion
Source: "Mods\Green Dot.fbmod"; DestDir: "{commonappdata}\BassesModManager\Mods"; Flags: ignoreversion
Source: "Mods\Improved_Scoreboard.fbmod"; DestDir: "{commonappdata}\BassesModManager\Mods"; Flags: ignoreversion
Source: "Mods\Improved-Game-Startup.fbmod"; DestDir: "{commonappdata}\BassesModManager\Mods"; Flags: ignoreversion
Source: "Mods\Improved-Low-Health-Visibility.fbmod"; DestDir: "{commonappdata}\BassesModManager\Mods"; Flags: ignoreversion
Source: "Mods\Improved-Pause-Screen-Effects.fbmod"; DestDir: "{commonappdata}\BassesModManager\Mods"; Flags: ignoreversion
; Kyber gets injected into the game, so it stays in the admin-only program folder rather
; than the user-writable mods folder. Kept out of Mods\ in the repo too - that folder is
; now exactly "the mods that ship", and this is not a mod.
Source: "Kyber.dll"; DestDir: "{app}"; Flags: ignoreversion
; Images – mod card previews (from build output)
Source: "{#PayloadPath}\Assets\Images\*"; DestDir: "{app}\Assets\Images"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Sounds – hover/click sound effects
Source: "{#PayloadPath}\Assets\Sounds\*"; DestDir: "{app}\Assets\Sounds"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Banners – game banners for cache install window
Source: "{#PayloadPath}\Assets\Banners\*"; DestDir: "{app}\Assets\Banners"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Prereqs – bundled for install when missing (dontcopy = extract only when needed in [Code])
Source: "Prereqs\.NET_Framework_4.8_setup.exe"; DestDir: "{tmp}"; Flags: dontcopy
#ifexist "Prereqs\vc_redist.x64.exe"
Source: "Prereqs\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: dontcopy
#endif

[Dirs]
; The app manages mods here at runtime - verifying, deleting rejected files and
; downloading the Auric set - and it runs non-elevated, so Users need write access.
; Without this the folder inherits ProgramData's default read-only-for-Users ACL.
Name: "{commonappdata}\BassesModManager"; Permissions: users-modify
Name: "{commonappdata}\BassesModManager\Mods"; Permissions: users-modify

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[InstallDelete]
; The MyAppName rebrand from "Basse's Mod Manager" to "Axon" made Inno Setup create a
; new Axon.lnk shortcut instead of renaming the old one (it tracks icons by their Name:
; string). AppId is unchanged, so this is still recognized as an update of the same
; install: {group} keeps resolving to whatever Start Menu folder the user originally
; picked (Inno's UsePreviousGroup, on by default) rather than the new DefaultGroupName -
; Axon.lnk lands right next to the old shortcut in that same folder, no new folder
; involved. So the old Start Menu shortcut is removed via {group}, not a hardcoded path.
; The desktop shortcut has no such folder-memory concept, so that one is a literal path.
Type: files; Name: "{autodesktop}\Basse's Mod Manager.lnk"
Type: files; Name: "{group}\Basse's Mod Manager.lnk"
; Mods moved out of the program folder to ProgramData, where the app can manage them
; without elevation. Removing the old folder also clears out the crosshair files that
; older releases installed under different names: the app rejects those now (name and
; hash must both match) and, running non-elevated, could not delete them itself - which
; would otherwise mean a "could not remove" warning on every single start.
Type: filesandordirs; Name: "{app}\Mods"

[Run]
; runascurrentuser = start app as the user who ran the installer (non-elevated), avoiding "CreateProcess failed; code 740".
; No skipifsilent: silent auto-updates (/VERYSILENT from UpdateService) must relaunch
; the app afterwards so the update feels like a quick restart.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall runascurrentuser

; Prereqs: .NET 4.8 is bundled in Prereqs\.NET_Framework_4.8_setup.exe and installed automatically if missing.
; VC++ 2015-2022 Redist (x64) is bundled in Prereqs\vc_redist.x64.exe and installed automatically if missing
; (required by the native Frosty DLLs: FrostyHash, zlibwapi, libzstd, CryptBase).
[Code]
function IsDotNet48Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
    Result := (Release >= 528040);  // 528040 = .NET 4.8
end;

function IsVCRedist64Installed: Boolean;
var
  Installed: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
    Result := (Installed = 1);
end;

function InitializeSetup: Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not IsDotNet48Installed then
  begin
    ExtractTemporaryFile('.NET_Framework_4.8_setup.exe');
    if Exec(ExpandConstant('{tmp}\.NET_Framework_4.8_setup.exe'), '/passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      Result := True
    else
      MsgBox('.NET Framework 4.8 setup failed or was cancelled. The app may not run until .NET 4.8 is installed.', mbError, MB_OK);
    Result := True;  // allow our installer to continue either way
  end;
#ifexist "Prereqs\vc_redist.x64.exe"
  if not IsVCRedist64Installed then
  begin
    ExtractTemporaryFile('vc_redist.x64.exe');
    if not Exec(ExpandConstant('{tmp}\vc_redist.x64.exe'), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      MsgBox('Visual C++ Redistributable setup failed or was cancelled. The app may not run until it is installed.', mbError, MB_OK);
    Result := True;  // allow our installer to continue either way
  end;
#endif
end;
