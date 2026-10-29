; Inno Setup 6 script. Built by build.ps1, which publishes the executables first:
;   iscc /DAppVersion=0.1 installer\SourisFacileFenetre.iss
;
; Setup asks whether to install for the current user only (no administrator
; rights needed) or for all users (asks for administrator rights).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "Souris facile fenêtre"
#define AppExe "SourisFacileFenetre.exe"
#define AppUrl "https://github.com/thomfab/souris-facile-fenetre"

[Setup]
AppId={{015A5106-0B3B-4CBA-91C1-A4B4D7C55058}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
VersionInfoVersion={#AppVersion}
; Per-user installs go to %LOCALAPPDATA%\Programs, all-users ones to Program Files.
DefaultDirName={autopf}\Souris facile fenetre
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsedUserAreasWarning=no
; x64compatible includes Windows on Arm, which gets the arm64 build.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\src\SourisFacile\Resources\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir=..\build
OutputBaseFilename=SourisFacileFenetre-{#AppVersion}-setup

[Tasks]
Name: "startup"; Description: "Launch {#AppName} at login"

[Files]
Source: "..\build\win-arm64\{#AppExe}"; DestDir: "{app}"; Check: IsArm64; Flags: ignoreversion
Source: "..\build\win-x64\{#AppExe}"; DestDir: "{app}"; Check: not IsArm64; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
; The same value as "Launch at login" in the app's settings, which is per user.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "SourisFacileFenetre"; ValueData: """{app}\{#AppExe}"""; Tasks: startup
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; \
    ValueName: "SourisFacileFenetre"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  EVENT_MODIFY_STATE = $0002;
  SYNCHRONIZE = $00100000;

function OpenEvent(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: string): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

// Asks a running copy of the app to quit (see Program.cs), so its file can
// be replaced or removed, and waits for it to be gone.
procedure QuitApp();
var
  Handle: THandle;
  Tries: Integer;
begin
  Handle := OpenEvent(EVENT_MODIFY_STATE or SYNCHRONIZE, False, 'SourisFacileFenetre.Quit');
  if Handle = 0 then
    Exit;
  SetEvent(Handle);
  CloseHandle(Handle);
  Tries := 0;
  while CheckForMutexes('SourisFacileFenetre') and (Tries < 50) do
  begin
    Sleep(100);
    Tries := Tries + 1;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  QuitApp();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  QuitApp();
  Result := True;
end;
