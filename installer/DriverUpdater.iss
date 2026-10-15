; Inno Setup script. The CI pipeline builds it after `dotnet publish`:
;   ISCC /DAppVersion=1.2.3 installer\DriverUpdater.iss
; Saved as UTF-8 with a byte order mark: Inno Setup needs it to read the accents in [Code].
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppId "{16772057-8B78-4AC1-BD9C-C0B08C365D74}"

[Setup]
; Never change AppId: it is how Windows recognises an update of the same application.
AppId={{#AppId}
AppName=Driver Updater
AppVersion={#AppVersion}
AppVerName=Driver Updater {#AppVersion}
AppPublisher=Augaton
AppPublisherURL=https://github.com/Augaton/driver_updater
; Program Files only, and no choice of folder: the app starts itself as administrator for
; some actions, so only administrators may be able to replace its files.
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
CloseApplications=yes
UninstallDisplayIcon={app}\DriverUpdater.exe
UninstallDisplayName=Driver Updater
; Intended: uninstalling clears what the weekly check stored for this user (HKCU).
UsedUserAreasWarning=no
; The look of the app: dark, neon, its own icon and artwork (installer\art\make-art.py).
WizardStyle=modern dark hidebevels
WizardImageFile=art\wizard.bmp,art\wizard-200.bmp
WizardSmallImageFile=art\small.bmp,art\small-200.bmp
SetupIconFile=..\src\DriverUpdater\app.ico
; "Modifier" in Windows' app list opens this setup again, to repair or uninstall.
AppModifyPath="{app}\setup\DriverUpdater-Setup.exe" /modify
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Driver Updater

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion
; A copy of this very setup, for the "Modifier" button. Not when it already runs from there.
Source: "{srcexe}"; DestDir: "{app}\setup"; DestName: "DriverUpdater-Setup.exe"; Flags: external ignoreversion; Check: not RunningFromInstalledCopy

[Icons]
Name: "{autoprograms}\Driver Updater"; Filename: "{app}\DriverUpdater.exe"

[Registry]
; What the weekly check remembers. Not created here, only removed on uninstall.
Root: HKCU; Subkey: "Software\Driver Updater"; Flags: uninsdeletekey dontcreatekey

[Run]
; As the user who started the setup, not with the setup's administrator rights.
Filename: "{app}\DriverUpdater.exe"; Description: "{cm:LaunchProgram,Driver Updater}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Same name as WeeklyCheck.TaskName. Fails harmlessly when the check was never turned on.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""Driver Updater weekly check"" /F"; Flags: runhidden; RunOnceId: "DeleteWeeklyTask"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\setup"

[Code]
// When Driver Updater is already installed, the first page says which version and offers
// what makes sense: update (newer setup), repair (same version), go back (older setup), or
// uninstall. Silent installs (/VERYSILENT) skip the question: they update or repair.
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1';
  ChoiceUninstall = 1;

var
  InstalledVersion: String;
  MaintenancePage: TInputOptionWizardPage;
  LeavingForUninstall: Boolean;

function RunningFromInstalledCopy: Boolean;
begin
  Result := CompareText(ExpandConstant('{srcexe}'), ExpandConstant('{app}\setup\DriverUpdater-Setup.exe')) = 0;
end;

// -1: this setup is older than what is installed, 0: same version, 1: newer.
function CompareWithInstalled: Integer;
var
  Installed, ThisSetup: Int64;
begin
  if StrToVersion(InstalledVersion, Installed) and StrToVersion('{#AppVersion}', ThisSetup) then
    Result := ComparePackedVersion(ThisSetup, Installed)
  else
    Result := 1;
end;

procedure InitializeWizard;
var
  Comparison: Integer;
begin
  if not RegQueryStringValue(HKLM64, UninstallKey, 'DisplayVersion', InstalledVersion) then
    Exit;
  Comparison := CompareWithInstalled;
  MaintenancePage := CreateInputOptionPage(wpWelcome,
    'Driver Updater est déjà installé',
    'Version installée : ' + InstalledVersion + '. Cette installation : {#AppVersion}.',
    'Que voulez-vous faire ?', True, False);
  if Comparison > 0 then
    MaintenancePage.Add('Mettre à jour vers la version {#AppVersion} (vos réglages sont conservés)')
  else if Comparison = 0 then
    MaintenancePage.Add('Réparer : réinstaller tous les fichiers de l''application')
  else
    MaintenancePage.Add('Revenir à la version {#AppVersion}, plus ancienne (déconseillé)');
  MaintenancePage.Add('Désinstaller Driver Updater');
  MaintenancePage.SelectedValueIndex := 0;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Uninstaller: String;
  ResultCode: Integer;
begin
  Result := True;
  if (MaintenancePage <> nil) and (CurPageID = MaintenancePage.ID)
    and (MaintenancePage.SelectedValueIndex = ChoiceUninstall) then
  begin
    if RegQueryStringValue(HKLM64, UninstallKey, 'UninstallString', Uninstaller) then
      Exec(RemoveQuotes(Uninstaller), '', '', SW_SHOW, ewNoWait, ResultCode);
    LeavingForUninstall := True;
    WizardForm.Close;
    Result := False;
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  // Leaving for the uninstaller is not a cancelled install: no "are you sure?".
  if LeavingForUninstall then
    Confirm := False;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  if InstalledVersion = '' then
    Result := 'Installation de Driver Updater {#AppVersion}.'
  else if CompareWithInstalled = 0 then
    Result := 'Réparation de Driver Updater ' + InstalledVersion + ' : tous les fichiers seront réinstallés.'
  else
    Result := 'Passage de la version ' + InstalledVersion + ' à la version {#AppVersion}.';
  Result := Result + NewLine + NewLine + MemoDirInfo;
end;
