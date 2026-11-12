#define AppName "AutoClicker"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppExeName "AutoClicker.exe"

[Setup]
AppId={{A17F8536-EB09-4C4B-A943-02B577B1DC46}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\AutoClicker
DefaultGroupName=AutoClicker
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\..\artifacts\release\windows
OutputBaseFilename=AutoClicker-{#AppVersion}-win-x64-setup
SetupIconFile=..\..\assets\branding\autoclicker.ico
UninstallDisplayIcon={app}\{#AppExeName}
ArchitecturesInstallIn64BitMode=x64
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=no
ChangesAssociations=no
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\AutoClicker"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\AutoClicker"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch AutoClicker"; Flags: postinstall nowait skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AutoClicker');
end;
