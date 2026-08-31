; Inno Setup Script for DhirDhar Solution
; Produces a SINGLE self-contained production installer EXE: DhirDhar-2.1.1-x64-Setup.exe

#define MyAppName "DhirDhar Solution"
#define MyAppVersion "2.1.1"
#define MyAppPublisher "DhirDhar Solution"
#define MyAppExeName "DhirDhar.Desktop.exe"
#define MyAppIcon "d:\DhirDhar\DhirDhar Solution\src\DhirDhar.Desktop\Assets\AppIcon.ico"
#define MyPublishDir "d:\DhirDhar\DhirDhar Solution\Release"
#define MyOutputDir "d:\DhirDhar\DhirDhar Solution\Installer"

[Setup]
AppId={{B8C3A417-6D92-4F3A-8B1E-9C8F0E2D1A5B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf64}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir={#MyOutputDir}
OutputBaseFilename=DhirDhar-2.1.1-x64-Setup
SetupIconFile={#MyAppIcon}
UninstallDisplayIcon={app}\Assets\AppIcon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml,*.log,*.tmp,Packages,Packages\*,Installer,Installer\*,Publish,Publish\*,obj,obj\*,bin,bin\*"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
function InitializeSetup(): Boolean;
var
  PrevPath: String;
begin
  Result := True;
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1', 'Inno Setup: App Path', PrevPath) then
  begin
    if (Pos('TEMP', UpperCase(PrevPath)) > 0) or (Pos('APPDATA\LOCAL\TEMP', UpperCase(PrevPath)) > 0) then
    begin
      RegDeleteKeyIncludingSubkeys(HKEY_LOCAL_MACHINE, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1');
    end;
  end;
end;
