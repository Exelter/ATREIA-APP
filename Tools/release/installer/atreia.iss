; The installer the players download (Atreia-App-Setup.exe): a setup wizard (Inno Setup 6) around Velopack's own Setup.exe (11 November 2026, the owner:
; players trust a wizard more than Velopack's one-click install, which only flashes a splash). The wizard shows what the app is and does, then runs
; Velopack's Setup.exe silently, which installs the app exactly as before: in %LocalAppData%\AtreiaApp, with its shortcuts, its entry in Windows'
; installed apps (the uninstall) and its updates. So the wizard installs nothing of its own and has no uninstaller.
;
; Built by Tools/release/build-release.ps1 (step 5):
;   ISCC atreia.iss /DAppVersion=2.0.4 /DNumericVersion=2.0.4.0 /DVelopackSetup=<Velopack's Setup.exe> /DOutputDir=<folder> /DOutputName=Atreia-App-Setup

#ifndef VelopackSetup
  #error VelopackSetup (the path of Velopack's Setup.exe) is required
#endif
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0.0"
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#ifndef OutputName
  #define OutputName "Atreia-App-Setup"
#endif

[Setup]
AppId={{6F01BC79-5484-4AF4-9FA8-A4C51860A8A6}
AppName=Atreia App
AppVersion={#AppVersion}
AppVerName=Atreia App {#AppVersion}
AppPublisher=Atreia
AppPublisherURL=https://atreia.app
AppSupportURL=https://atreia.app/go/discord
AppUpdatesURL=https://atreia.app
AppCopyright=Copyright (c) 2026 Atreia
VersionInfoVersion={#NumericVersion}
VersionInfoProductVersion={#NumericVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany=Atreia
VersionInfoDescription=Atreia App Setup
VersionInfoProductName=Atreia App
VersionInfoCopyright=Copyright (c) 2026 Atreia
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
SetupIconFile=..\..\..\Client\assets\app\aiondps.ico
WizardStyle=modern
WizardImageFile=wizard-100.bmp,wizard-150.bmp,wizard-200.bmp
WizardSmallImageFile=small-100.bmp,small-150.bmp,small-200.bmp
InfoBeforeFile=before-install.txt
; the pages: welcome, what the app does, ready, installing, done (no folder or Start menu pages: Velopack puts the app and its shortcuts in place)
DisableWelcomePage=no
DisableDirPage=yes
DisableProgramGroupPage=yes
CreateAppDir=no
Uninstallable=no
; for this Windows account only, like Velopack: no administrator rights, no UAC prompt
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Velopack's Setup.exe is already compressed
Compression=lzma2/fast
SolidCompression=no
ShowLanguageDialog=no
CloseApplications=no
SetupLogging=yes

[Messages]
WelcomeLabel2=This will install Atreia App [ver] on your computer.%n%nAtreia shows your damage meter, boss timers, pets, loot and more right over AION 2.%n%nIt is free, installs for your Windows account only and updates itself.
FinishedHeadingLabel=Atreia App is installed
FinishedLabelNoIcons=Atreia App is on your computer, with a shortcut on your desktop and in the Start menu.
FinishedLabel=Atreia App is on your computer, with a shortcut on your desktop and in the Start menu.

[Files]
Source: "{#VelopackSetup}"; DestName: "AtreiaApp-Install.exe"; Flags: dontcopy

[Run]
Filename: "https://npcap.com/#download"; Description: "Open the Npcap download page (the app needs Npcap to read the game's traffic)"; Flags: postinstall shellexec nowait skipifsilent; Check: NeedsNpcap
Filename: "{localappdata}\AtreiaApp\current\AtreiaApp.exe"; Description: "Start Atreia App"; Flags: postinstall nowait skipifsilent; Check: AppInstalled

[Code]
var
  InstallFailed: Boolean;
  LogPath: String;

function NpcapInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\Npcap\wpcap.dll')) or FileExists(ExpandConstant('{sys}\wpcap.dll'))
    or FileExists(ExpandConstant('{syswow64}\Npcap\wpcap.dll'));
end;

function AppInstalled: Boolean;
begin
  Result := not InstallFailed;
end;

function NeedsNpcap: Boolean;
begin
  Result := (not InstallFailed) and (not NpcapInstalled);
end;

// Velopack's Setup.exe, silently: it installs (or repairs, or replaces) the app and its shortcuts, and starts nothing.
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    WizardForm.StatusLabel.Caption := 'Installing Atreia App...';
    WizardForm.ProgressGauge.Style := npbstMarquee;
    LogPath := ExpandConstant('{%TEMP}\Atreia-App-install.log');
    ExtractTemporaryFile('AtreiaApp-Install.exe');
    if not Exec(ExpandConstant('{tmp}\AtreiaApp-Install.exe'), '--silent --log "' + LogPath + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
      or (ResultCode <> 0) or not FileExists(ExpandConstant('{localappdata}\AtreiaApp\current\AtreiaApp.exe')) then
    begin
      InstallFailed := True;
      Log(Format('Velopack Setup failed (exit code %d), its log: %s', [ResultCode, LogPath]));
    end;
    WizardForm.ProgressGauge.Style := npbstNormal;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and InstallFailed then
  begin
    WizardForm.FinishedHeadingLabel.Caption := 'Atreia App could not be installed';
    WizardForm.FinishedLabel.Caption := 'Something stopped the install. Close Atreia App if it is open and run this setup again.' + #13#10#13#10 +
      'If it fails again, send this file in the Atreia Discord (atreia.app/go/discord):' + #13#10 + LogPath;
  end;
end;
