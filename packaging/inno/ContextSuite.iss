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
UninstallDisplayIcon={app}\app\ContextSuite.Application.exe
SetupMutex=ContextSuiteInstaller
ChangesAssociations=yes

[Files]
Source: "payload\installation\*"; Flags: dontcopy
Source: "payload\runtimes\*"; Flags: dontcopy
Source: "payload\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\installation\*"; DestDir: "{app}\installation"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Context Suite"; Filename: "{app}\app\ContextSuite.Application.exe"

[Code]
var
  RegistrationFailed: Boolean;

function RunAction(Action, ScriptDirectory: String): Boolean;
var
  Code: Integer;
  Detail: AnsiString;
  ResultFile: String;
begin
  ResultFile := ExpandConstant('{tmp}\context-suite-result.txt');
  DeleteFile(ResultFile);
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptDirectory +
    '\Invoke-InstallerAction.ps1" -Action ' + Action + ' -InstallDirectory "' + ExpandConstant('{app}') +
    '" -ResultPath "' + ResultFile + '"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := Result and (Code = 0);
  if LoadStringFromFile(ResultFile, Detail) then Log(String(Detail));
end;

function InitializeSetup: Boolean;
begin
  Result := not IsAdmin;
  if not Result then
    SuppressibleMsgBox('Run setup normally, not as administrator. Context Suite installs for the current user.', mbError, MB_OK, IDOK);
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
