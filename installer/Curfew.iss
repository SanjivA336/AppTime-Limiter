; Inno Setup script for Curfew. Build it with tools\build-installer.ps1, which publishes the app first and passes in
; the version and the folder to package (so this file never needs editing for a release).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #error SourceDir must point at the published Curfew folder (use tools\build-installer.ps1).
#endif

[Setup]
; The AppId is how Windows (and later installers) recognise this program. NEVER change it: a different id would make
; an upgrade install a second copy next to the first. (The program used to be called HardCap and kept this id, so installs
; from before the rename upgrade in place.) The install folder below is only the default for a first install; an upgrade
; reuses the folder the person already has, which for the earliest installs is still named HardCap.
AppId={{C55F7251-DC5E-4EFB-B8EA-864F15F4E80B}
AppName=Curfew
AppVersion={#AppVersion}
AppPublisher=Curfew
AppPublisherURL=https://github.com/SanjivA336/AppTime-Limiter
AppSupportURL=https://github.com/SanjivA336/AppTime-Limiter/issues
; Installs for the current user only, so no administrator prompt. The app keeps its data in %LOCALAPPDATA%\Curfew.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Curfew
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\Curfew.App\Assets\app.ico
UninstallDisplayIcon={app}\Curfew.exe
UninstallDisplayName=Curfew
OutputDir=..\artifacts
OutputBaseFilename=Curfew-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Setup stops a running Curfew itself (see PrepareToInstall below) instead of asking it to close. Asking would make it show its
; quit countdown, so a global friction wait would also stall the installer.
CloseApplications=no
RestartApplications=no
LicenseFile=..\LICENSE

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[InstallDelete]
; Upgrading from the time the program was called HardCap: its program files (HardCap.exe, HardCap.dll, HardCap.Core.dll and
; the rest named HardCap.*) and shortcuts are no longer wanted (the shortcuts below are recreated under the new name).
Type: files; Name: "{app}\HardCap.*"
Type: files; Name: "{autoprograms}\HardCap.lnk"
Type: files; Name: "{autodesktop}\HardCap.lnk"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Curfew"; Filename: "{app}\Curfew.exe"
Name: "{autodesktop}\Curfew"; Filename: "{app}\Curfew.exe"; Tasks: desktopicon

[Registry]
; The app's "Start with Windows" setting writes this value. Removing it on uninstall stops Windows from trying to
; launch a program that is no longer there. (ValueType none: nothing is created at install time.)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Curfew"; ValueType: none; Flags: dontcreatekey uninsdeletevalue
; The start-with-Windows entry from the HardCap days points at a program file that is being deleted, so remove it.
; (The app registers its new entry itself when "Start with Windows" is on.)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "HardCap"; ValueType: none; Flags: dontcreatekey deletevalue

[Run]
Filename: "{app}\Curfew.exe"; Description: "Start Curfew"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Make sure the tray app is not still holding its files open.
Filename: "{sys}\taskkill.exe"; Parameters: "/im Curfew.exe /f"; Flags: runhidden skipifdoesntexist; RunOnceId: "StopCurfew"

[Code]
// Stops any running copy before anything is installed, by force (no countdown, no questions), so a wait set in the app can't
// hold the installer up. That covers the current name (Curfew.exe) and the old one (HardCap.exe): an old copy would otherwise
// keep running next to the new program, and the two would each count usage. Usage is saved to disk every 15 seconds, so at most
// that much tracked time can be lost. Nothing happens here until the person has clicked Install.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/im Curfew.exe /f', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/im HardCap.exe /f', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

// Settings and usage history live outside the install folder, so uninstalling keeps them unless the user says otherwise.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataFolder: String;
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
  begin
    DataFolder := ExpandConstant('{localappdata}\Curfew');
    if DirExists(DataFolder) then
    begin
      if MsgBox('Also delete your Curfew settings and usage history?' + #13#10 + #13#10 +
                'Choose No to keep them in case you reinstall.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataFolder, True, True, True);
    end;
  end;
end;
