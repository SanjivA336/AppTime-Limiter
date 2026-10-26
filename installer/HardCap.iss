; Inno Setup script for HardCap. Build it with tools\build-installer.ps1, which publishes the app first and passes in
; the version and the folder to package (so this file never needs editing for a release).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #error SourceDir must point at the published HardCap folder (use tools\build-installer.ps1).
#endif

[Setup]
; The AppId is how Windows (and later installers) recognise this program. NEVER change it: a different id would make
; an upgrade install a second copy next to the first. The same goes for the install folder below.
AppId={{C55F7251-DC5E-4EFB-B8EA-864F15F4E80B}
AppName=HardCap
AppVersion={#AppVersion}
AppPublisher=HardCap
AppPublisherURL=https://github.com/SanjivA336/AppTime-Limiter
AppSupportURL=https://github.com/SanjivA336/AppTime-Limiter/issues
; Installs for the current user only, so no administrator prompt. The app keeps its data in %LOCALAPPDATA%\HardCap.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\HardCap
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\HardCap.App\Assets\app.ico
UninstallDisplayIcon={app}\HardCap.exe
UninstallDisplayName=HardCap
OutputDir=..\artifacts
OutputBaseFilename=HardCap-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Closes a running HardCap (it lives in the tray) before replacing its files, then offers to start it again.
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\HardCap"; Filename: "{app}\HardCap.exe"
Name: "{autodesktop}\HardCap"; Filename: "{app}\HardCap.exe"; Tasks: desktopicon

[Registry]
; The app's "Start with Windows" setting writes this value. Removing it on uninstall stops Windows from trying to
; launch a program that is no longer there. (ValueType none: nothing is created at install time.)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "HardCap"; ValueType: none; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\HardCap.exe"; Description: "Start HardCap"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Make sure the tray app is not still holding its files open.
Filename: "{sys}\taskkill.exe"; Parameters: "/im HardCap.exe /f"; Flags: runhidden skipifdoesntexist; RunOnceId: "StopHardCap"

[Code]
// Settings and usage history live outside the install folder, so uninstalling keeps them unless the user says otherwise.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataFolder: String;
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
  begin
    DataFolder := ExpandConstant('{localappdata}\HardCap');
    if DirExists(DataFolder) then
    begin
      if MsgBox('Also delete your HardCap settings and usage history?' + #13#10 + #13#10 +
                'Choose No to keep them in case you reinstall.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataFolder, True, True, True);
    end;
  end;
end;
