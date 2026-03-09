; Inno Setup script for Basse's Mod Manager
; Run from BassesModManager folder: iscc installer.iss
; Or from repo root: iscc BassesModManager\installer.iss
; CI can pass version: iscc /DMyAppVersion=1.1 BassesModManager\installer.iss

#define MyAppName "Basse's Mod Manager"
#ifndef MyAppVersion
  #define MyAppVersion "1.2"
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
OutputBaseFilename=BassesModManager_Setup_{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin

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
; Profiles (from build; SDK DLL for the game)
Source: "{#PayloadPath}\Profiles\*"; DestDir: "{app}\Profiles"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Mods – the 3 approved .fbmod files from repo (required for app to have mods to choose)
Source: "Mods\*"; DestDir: "{app}\Mods"; Flags: ignoreversion
; Images – mod card previews (from build output)
Source: "{#PayloadPath}\Assets\Images\*"; DestDir: "{app}\Assets\Images"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Sounds – hover/click sound effects
Source: "{#PayloadPath}\Assets\Sounds\*"; DestDir: "{app}\Assets\Sounds"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Banners – game banners for cache install window
Source: "{#PayloadPath}\Assets\Banners\*"; DestDir: "{app}\Assets\Banners"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Prereqs – bundled for install when missing (dontcopy = extract only when needed in [Code])
Source: "Prereqs\.NET_Framework_4.8_setup.exe"; DestDir: "{tmp}"; Flags: dontcopy

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; runascurrentuser = start app as the user who ran the installer (non-elevated), avoiding "CreateProcess failed; code 740"
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser

; Prereqs: .NET 4.8 is bundled in Prereqs\.NET_Framework_4.8_setup.exe and installed automatically if missing.
; VC++ Redist (x64): https://aka.ms/vs/17/release/vc_redist.x64.exe – add to Prereqs\ and [Code] if needed later.
[Code]
function IsDotNet48Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
    Result := (Release >= 528040);  // 528040 = .NET 4.8
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
end;
