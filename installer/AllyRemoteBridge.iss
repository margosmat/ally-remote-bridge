; Compile only after Prepare-Release.ps1 has validated the dependency payload.
#ifnexist "payload\native\manifest.json"
  #error Reviewed native payload is missing. Run Prepare-Release.ps1 first.
#endif
#ifnexist "payload\driver\approved-driver.json"
  #error Reviewed driver payload is missing.
#endif
[Setup]
AppId={{B80764D7-2851-445E-8BE4-12E63031C041}
AppName=Ally Remote Bridge
AppVersion=0.1.0
DefaultDirName={autopf}\Ally Remote Bridge
DefaultGroupName=Ally Remote Bridge
OutputDir=..\artifacts
OutputBaseFilename=AllyRemoteBridge-Setup
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\AllyRemoteBridge.exe
AppMutex=Local\AllyRemoteBridge
CloseApplications=yes
RestartApplications=no
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: checkedonce
Name: installdriver; Description: "Install or update the reviewed USB/IP driver (shared system component)"; Flags: unchecked
[Files]
Source: "payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Ally Remote Bridge"; Filename: "{app}\AllyRemoteBridge.exe"
Name: "{group}\Ally Remote Bridge Settings"; Filename: "{app}\AllyRemoteBridge.exe"; Parameters: "--settings"
Name: "{group}\Ally Remote Bridge Diagnostics"; Filename: "{app}\AllyRemoteBridge.exe"; Parameters: "--diagnostics"
Name: "{group}\Recover bridge-owned device"; Filename: "{app}\AllyRemoteBridge.exe"; Parameters: "--cleanup"
Name: "{autodesktop}\Ally Remote Bridge"; Filename: "{app}\AllyRemoteBridge.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\AllyRemoteBridge.exe"; Description: "Open Ally Remote Bridge settings"; Parameters: "--settings"; Flags: nowait postinstall skipifsilent runasoriginaluser
[UninstallRun]
Filename: "{app}\AllyRemoteBridge.exe"; Parameters: "--installer-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "BridgeDeviceCleanup"
[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\AllyRemoteBridge"
[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('installdriver') then
  begin
    if not Exec(ExpandConstant('{app}\AllyRemoteBridge.exe'), '--install-driver', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('Could not start driver installation.');
    if ResultCode <> 0 then
      RaiseException('Driver installation failed. No certificates or Windows security settings were changed.');
    if FileExists(ExpandConstant('{app}\driver\restart-required')) then
      MsgBox('Restart Windows before starting Ally Remote Bridge.', mbInformation, MB_OK);
  end;
end;
