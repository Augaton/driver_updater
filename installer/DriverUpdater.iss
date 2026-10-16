; Inno Setup script. The CI pipeline builds it after `dotnet publish`:
;   ISCC /DAppVersion=1.2.3 installer\DriverUpdater.iss
; Saved as UTF-8 with a byte order mark: Inno Setup needs it to read the accents.
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
; French for a French Windows, English for any other: the same rule as the app. No dialog.
ShowLanguageDialog=no

[Languages]
; The first one is used when Windows' language matches none.
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
en.AlreadyInstalled=Driver Updater is already installed
fr.AlreadyInstalled=Driver Updater est déjà installé
en.Versions=Installed version: %1. This setup: {#AppVersion}.
fr.Versions=Version installée : %1. Cette installation : {#AppVersion}.
en.WhatToDo=What do you want to do?
fr.WhatToDo=Que voulez-vous faire ?
en.ChoiceUpdate=Update to version {#AppVersion} (your settings are kept)
fr.ChoiceUpdate=Mettre à jour vers la version {#AppVersion} (vos réglages sont conservés)
en.ChoiceRepair=Repair: reinstall all the app's files
fr.ChoiceRepair=Réparer : réinstaller tous les fichiers de l'application
en.ChoiceOlder=Go back to version {#AppVersion}, an older one (not advised)
fr.ChoiceOlder=Revenir à la version {#AppVersion}, plus ancienne (déconseillé)
en.ChoiceRemove=Uninstall Driver Updater
fr.ChoiceRemove=Désinstaller Driver Updater
en.MemoInstall=Installing Driver Updater {#AppVersion}.
fr.MemoInstall=Installation de Driver Updater {#AppVersion}.
en.MemoRepair=Repairing Driver Updater %1: every file will be reinstalled.
fr.MemoRepair=Réparation de Driver Updater %1 : tous les fichiers seront réinstallés.
en.MemoUpdate=Moving from version %1 to version {#AppVersion}.
fr.MemoUpdate=Passage de la version %1 à la version {#AppVersion}.

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
    CustomMessage('AlreadyInstalled'),
    FmtMessage(CustomMessage('Versions'), [InstalledVersion]),
    CustomMessage('WhatToDo'), True, False);
  if Comparison > 0 then
    MaintenancePage.Add(CustomMessage('ChoiceUpdate'))
  else if Comparison = 0 then
    MaintenancePage.Add(CustomMessage('ChoiceRepair'))
  else
    MaintenancePage.Add(CustomMessage('ChoiceOlder'));
  MaintenancePage.Add(CustomMessage('ChoiceRemove'));
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
    Result := CustomMessage('MemoInstall')
  else if CompareWithInstalled = 0 then
    Result := FmtMessage(CustomMessage('MemoRepair'), [InstalledVersion])
  else
    Result := FmtMessage(CustomMessage('MemoUpdate'), [InstalledVersion]);
  Result := Result + NewLine + NewLine + MemoDirInfo;
end;
