; Compiled only by tools/release/Build-InnoInstaller.ps1.
#include "BuildConfig.iss"

[Setup]
AppId=ContextSuite.InternalInstaller
AppName=Context Suite (internal installer)
AppVersion={#SuiteVersion}
AppPublisher=Keenan Selbee
DefaultDirName={localappdata}\Programs\Context Suite
DisableDirPage=yes
UsePreviousAppDir=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.26100
SetupArchitecture=x64
WizardStyle=modern dynamic
DisableProgramGroupPage=yes
OutputDir={#OutputDirectory}
OutputBaseFilename=ContextSuite-{#SuiteVersion}-win-x64-internal
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
UninstallDisplayIcon={app}\installation\Analyze.ico
SetupMutex=ContextSuiteInstaller
ChangesAssociations=yes

[Files]
Source: "payload\installation\*"; Flags: dontcopy
Source: "payload\runtimes\*"; Flags: dontcopy
Source: "payload\release\*"; DestDir: "{app}\releases\{code:GetReleaseId}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\installation\*"; DestDir: "{app}\installation"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Context Suite"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File ""{app}\installation\Launch-Active.ps1"""; IconFilename: "{app}\installation\Analyze.ico"; Flags: runminimized

[UninstallDelete]
; Exact installer-owned metadata only. Payloads use Inno's accumulated file ledger.
Type: files; Name: "{app}\active.json"
Type: files; Name: "{app}\bootstrap.json"
Type: files; Name: "{app}\recovery.json"
Type: files; Name: "{app}\recovery.lock"
Type: files; Name: "{app}\uninstall.json"

[Code]
var
  RegistrationFailed: Boolean;
  StageId: String;
  LifecycleMutex: THandle;
  MenuPage: TInputOptionWizardPage;

function SelectedMenuMode: String;
begin
  Result := 'modern';
  if Assigned(MenuPage) then
    if MenuPage.SelectedValueIndex = 1 then Result := 'classic';
end;

procedure InitializeWizard;
var
  PreviousMode, OverrideValue: String;
begin
  MenuPage := CreateInputOptionPage(wpWelcome, 'Explorer menu',
    'Where should Context Suite commands appear?',
    'This only controls Context Suite. Your Windows right-click menu settings will not be changed.', True, False);
  MenuPage.Add('Windows 11 menu');
  MenuPage.Add('Classic menu (Show more options)');
  MenuPage.SelectedValueIndex := 0;
  PreviousMode := GetPreviousData('MenuMode', '');
  if PreviousMode = 'classic' then MenuPage.SelectedValueIndex := 1
  else if PreviousMode <> 'modern' then begin
    { Best-effort detection of the common per-user classic override. Missing,
      unreadable or nonempty values keep the Windows 11 default. }
    if RegQueryStringValue(HKCU64,
      'Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32', '', OverrideValue) then
      if OverrideValue = '' then MenuPage.SelectedValueIndex := 1;
  end;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  if not RegistrationFailed then SetPreviousData(PreviousDataKey, 'MenuMode', SelectedMenuMode);
end;

function CreateMutexW(Attributes: THandle; InitialOwner: Boolean; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function ReleaseMutex(Handle: THandle): Boolean;
  external 'ReleaseMutex@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function AcquireLifecycle: Boolean;
var
  WaitResult: LongWord;
begin
  { Serialize setup AND uninstall across the full Inno file-ledger interval.
    The per-user name also avoids unrelated Windows users blocking each other. }
  LifecycleMutex := CreateMutexW(0, False, 'Global\ContextSuiteLifecycle.' +
    GetMD5OfString(Lowercase(ExpandConstant('{localappdata}'))));
  Result := False;
  if LifecycleMutex = 0 then exit;
  WaitResult := WaitForSingleObject(LifecycleMutex, 0);
  Result := (WaitResult = 0) or (WaitResult = 128);
  if not Result then begin
    CloseHandle(LifecycleMutex);
    LifecycleMutex := 0;
    SuppressibleMsgBox('Another Context Suite setup or uninstall is running. Finish it, then retry.', mbError, MB_OK, IDOK);
  end;
end;

procedure ReleaseLifecycle;
begin
  if LifecycleMutex <> 0 then begin
    ReleaseMutex(LifecycleMutex);
    CloseHandle(LifecycleMutex);
    LifecycleMutex := 0;
  end;
end;

function GetReleaseId(Param: String): String;
begin
  { File destination constants may be evaluated before PrepareToInstall. This
    per-process temporary directory is unique; the stage is still create-only. }
  if StageId = '' then StageId := Lowercase(GetMD5OfString(ExpandConstant('{tmp}')));
  Result := StageId;
end;

function RunAction(Action, ScriptDirectory: String): Boolean;
var
  Code: Integer;
  Detail: AnsiString;
  ResultFile: String;
  Arguments: String;
begin
  ResultFile := ExpandConstant('{tmp}\context-suite-result.txt');
  DeleteFile(ResultFile);
  Arguments := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptDirectory +
    '\Invoke-InstallerAction.ps1" -Action ' + Action + ' -InstallDirectory "' + ExpandConstant('{app}') +
    '" -ResultPath "' + ResultFile + '"';
  if StageId <> '' then Arguments := Arguments + ' -ReleaseId "' + StageId + '"';
  Arguments := Arguments + ' -MenuMode ' + SelectedMenuMode;
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Arguments, '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := Result and (Code = 0);
  if LoadStringFromFile(ResultFile, Detail) then Log(String(Detail));
end;

function InitializeSetup: Boolean;
begin
  Result := not IsAdmin;
  if not Result then
    SuppressibleMsgBox('Run setup normally, not as administrator. Context Suite installs for the current user.', mbError, MB_OK, IDOK);
  if Result then Result := AcquireLifecycle;
end;

procedure DeinitializeSetup;
begin
  ReleaseLifecycle;
end;

function InitializeUninstall: Boolean;
begin
  Result := not IsAdmin;
  if Result then Result := AcquireLifecycle
  else SuppressibleMsgBox('Run uninstall as the original non-elevated user.', mbError, MB_OK, IDOK);
end;

procedure DeinitializeUninstall;
begin
  ReleaseLifecycle;
end;

function InstallRuntime(FileName: String; var NeedsRestart: Boolean): Boolean;
var
  Code: Integer;
begin
  ExtractTemporaryFile(FileName);
  Result := ShellExec('runas', ExpandConstant('{tmp}\') + FileName,
    '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, Code);
  if Result then begin
    NeedsRestart := (Code = 3010) or (Code = 1641);
    Result := (Code = 0) or NeedsRestart;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Detail: AnsiString;
begin
  Result := '';
  ExtractTemporaryFiles('{tmp}\*.ps1');
  ExtractTemporaryFiles('{tmp}\*.json');
  ExtractTemporaryFiles('{tmp}\*.msix');
  if not RunAction('Check', ExpandConstant('{tmp}')) then begin
    if LoadStringFromFile(ExpandConstant('{tmp}\context-suite-result.txt'), Detail) then
      Result := String(Detail)
    else Result := 'Installer preflight failed. See setup log.';
    exit;
  end;
  StageId := GetReleaseId('');
  if not RunAction('Runtime', ExpandConstant('{tmp}')) then begin
    if SuppressibleMsgBox('Missing Microsoft runtimes will be installed from bundled offline installers. Windows may request administrator approval. Continue?',
      mbConfirmation, MB_YESNO, IDYES) <> IDYES then begin
      Result := 'Required runtime installation was declined.';
      exit;
    end;
    if not RunAction('Desktop', ExpandConstant('{tmp}')) then begin
      if not InstallRuntime('{#DesktopRuntimeFile}', NeedsRestart) then
        Result := 'Microsoft .NET runtime installation failed or was cancelled.'
      else if NeedsRestart then Result := 'Restart Windows, then run setup again.';
      if Result <> '' then exit;
    end;
    if not RunAction('VisualCpp', ExpandConstant('{tmp}')) then begin
      if not InstallRuntime('vc_redist.x64.exe', NeedsRestart) then
        Result := 'Microsoft Visual C++ runtime installation failed or was cancelled.'
      else if NeedsRestart then Result := 'Restart Windows, then run setup again.';
      if Result <> '' then exit;
    end;
    if not RunAction('Runtime', ExpandConstant('{tmp}')) then
      Result := 'Required runtimes are still unavailable. See setup log.';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then begin
    if not RunAction('Stage', ExpandConstant('{tmp}')) then
      RaiseException('Unable to stage installation. Existing files were not overwritten; see setup log.');
  end;
  if CurStep = ssPostInstall then begin
    RegistrationFailed := not RunAction('Install', ExpandConstant('{app}\installation'));
    if RegistrationFailed then
      SuppressibleMsgBox('Explorer registration failed. Setup is incomplete. Files and the uninstaller were retained for recovery; see setup log. Do not use this installation.', mbError, MB_OK, IDOK);
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and RegistrationFailed then begin
    WizardForm.FinishedHeadingLabel.Caption := 'Installation incomplete';
    WizardForm.FinishedLabel.Caption := 'Explorer registration failed. Files and the uninstaller remain for recovery. See setup log before retrying.';
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  Result := 0;
  if RegistrationFailed then Result := 20;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then begin
    if IsAdmin then RaiseException('Run uninstall as the original non-elevated user.');
    if not RunAction('Uninstall', ExpandConstant('{app}\installation')) then
      RaiseException('Explorer unregistration failed. Application files are retained. Retry uninstall; see uninstall log.');
  end;
end;
