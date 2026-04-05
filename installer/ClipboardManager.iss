#define MyAppName "Clipboard Manager"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "OpenAI Codex"
#define MyAppExeName "ClipboardManager.App.exe"

[Setup]
AppId={{6B4418E1-8B2E-432F-8CC1-2ACB8C1E36F6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Clipboard Manager
DefaultGroupName=Clipboard Manager
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=ClipboardManager-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\icons\app-icon.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\src\ClipboardManager.App\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Clipboard Manager"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Clipboard Manager"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Clipboard Manager"; Flags: nowait postinstall skipifsilent
