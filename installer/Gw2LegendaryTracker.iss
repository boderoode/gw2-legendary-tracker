; Inno Setup 6 script. Build with:  iscc /DAppVersion=1.0.0 /O..\artifacts installer\Gw2LegendaryTracker.iss
; It packages the self-contained publish in ..\artifacts\win-x64 (see scripts\build.ps1).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "GW2 Legendary Tracker"
#define AppExe "Gw2LegendaryTracker.exe"

[Setup]
; A fixed id lets a newer setup replace an older installation.
AppId={{8C5E8B8E-4C45-4D0B-9B7E-2D5F3A6B1C77}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
; Per-user install: no administrator rights needed. {autopf} then resolves to %LOCALAPPDATA%\Programs.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
OutputBaseFilename=Gw2LegendaryTracker-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 version 1809 or newer.
MinVersion=10.0.17763
UninstallDisplayIcon={app}\{#AppExe}
CloseApplications=yes

[Languages]
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// The program files are removed by the uninstaller; the user's own data is kept unless the user chooses otherwise.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
  begin
    if MsgBox('Wilt u ook uw gegevens verwijderen?' + #13#10 + #13#10 +
              'Dit zijn uw Legendaries, instellingen, opgeslagen API-keys, back-ups en logbestanden in' + #13#10 +
              ExpandConstant('{localappdata}\Gw2LegendaryTracker'),
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\Gw2LegendaryTracker'), True, True, True);
    end;
  end;
end;
