; Inno Setup script for Folder Colorizer.
; Build: dotnet publish (self-contained, see build-installer.ps1), then ISCC installer.iss

#define AppName "Folder Colorizer"
#define AppVersion "1.2.2"
#define AppExe "FolderColorizer.exe"

[Setup]
AppId={{6B1E9C2A-4F7D-4C1B-9E3A-7D2F5A8C0B41}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Folder Colorizer
; Per-user, no admin prompt. Must match the path the app registers itself from (Setup.InstalledExe).
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\FolderColorizer
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=installer
OutputBaseFilename=FolderColorizer-Setup-{#AppVersion}
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#AppExe}
; Shown before installing: every system change the app makes.
InfoBeforeFile=docs\install-notes.txt
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
AppPublisherURL=https://github.com/Das7ardly/FolderColorizer
AppSupportURL=https://github.com/Das7ardly/FolderColorizer/issues
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "dist-installer\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
; Adds the right-click menu and starts the Ctrl+Shift+F background app (also at sign-in).
Filename: "{app}\{#AppExe}"; Parameters: "--install"; StatusMsg: "Adding the right-click menu..."; Flags: runhidden
Filename: "{app}\{#AppExe}"; Description: "Open Folder Colorizer settings"; Flags: postinstall nowait skipifsilent

[UninstallRun]
; Removes the menu, the shortcut and the startup entry, and stops the background app.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall"; RunOnceId: "RemoveIntegration"; Flags: runhidden

[Code]
const
  EVENT_MODIFY_STATE = $0002;

function OpenEvent(Access: Cardinal; Inherit: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Handle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

// The background app keeps the exe locked; ask it to quit (the same signal the app
// itself uses) so an update can replace the file.
procedure StopBackgroundApp();
var
  Handle: THandle;
  I: Integer;
begin
  Handle := OpenEvent(EVENT_MODIFY_STATE, False, 'FolderColorizer.Quit.v1');
  if Handle <> 0 then
  begin
    SetEvent(Handle);
    CloseHandle(Handle);
    // Wait (up to 10 s) until it has really exited and released the exe.
    for I := 1 to 50 do
    begin
      if not CheckForMutexes('FolderColorizer.Tray.v1') then Break;
      Sleep(200);
    end;
    Sleep(300);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopBackgroundApp();
  Result := '';
end;
