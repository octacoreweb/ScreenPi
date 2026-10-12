; Inno Setup Script for ScreenPi
; Native Windows Screen Time Tracker by EchoPhase

[Setup]
AppName=ScreenPi
AppVersion=1.0.0
AppPublisher=EchoPhase
DefaultDirName={autopf}\EchoPhase\ScreenPi
OutputBaseFilename=ScreenPi_Setup_x64
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes
OutputDir=.
PrivilegesRequired=lowest

[Files]
Source: ".\publish\ScreenPi\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\ScreenPi"; Filename: "{app}\DashboardUI.exe"
Name: "{autoprograms}\ScreenPi"; Filename: "{app}\DashboardUI.exe"
; CRITICAL: Do not create shortcuts for TrackerService.exe

[Registry]
; Force background tracker to auto-start silently on user logon
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ScreenPiTracker"; ValueData: """{app}\TrackerService.exe"""

[Run]
; Launch UI on finish
Filename: "{app}\DashboardUI.exe"; Description: "Launch ScreenPi"; Flags: nowait postinstall skipifsilent
; Launch tracker immediately in background
Filename: "{app}\TrackerService.exe"; Flags: nowait runhidden skipifsilent
