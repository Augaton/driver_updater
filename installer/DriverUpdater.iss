; Inno Setup script. The CI pipeline builds it after `dotnet publish`:
;   ISCC /DAppVersion=1.2.3 installer\DriverUpdater.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; Never change AppId: it is how Windows recognises an update of the same application.
AppId={{16772057-8B78-4AC1-BD9C-C0B08C365D74}
AppName=Driver Updater
AppVersion={#AppVersion}
AppVerName=Driver Updater {#AppVersion}
AppPublisher=Augaton
AppPublisherURL=https://github.com/Augaton/driver_updater
; Program Files only, and no choice of folder: the app runs as administrator, so only
; administrators may be able to replace its files.
DefaultDirName={autopf}\Driver Updater
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\installer-out
OutputBaseFilename=DriverUpdater-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
UninstallDisplayIcon={app}\DriverUpdater.exe
UninstallDisplayName=Driver Updater

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Driver Updater"; Filename: "{app}\DriverUpdater.exe"

[Registry]
; Created empty here so that uninstalling removes what the weekly check stores in it.
Root: HKLM; Subkey: "SOFTWARE\Driver Updater"; Flags: uninsdeletekey

[Run]
Filename: "{app}\DriverUpdater.exe"; Description: "{cm:LaunchProgram,Driver Updater}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Same name as WeeklyCheck.TaskName. Fails harmlessly when the check was never turned on.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""Driver Updater weekly check"" /F"; Flags: runhidden; RunOnceId: "DeleteWeeklyTask"
