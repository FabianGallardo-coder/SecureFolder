[Setup]
AppName=SecureFolder
AppVersion=1.0.0
AppPublisher=Fabian Gallardo
AppPublisherURL=https://github.com/FabianGallardo-coder/SecureFolder
AppCopyright=Copyright (c) 2026 Fabian Gallardo
DefaultDirName={autopf}\SecureFolder
DefaultGroupName=SecureFolder
OutputDir=..\release
OutputBaseFilename=SecureFolderSetup
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
SetupIconFile={#SourcePath}..\src\SecureFolder.App\Assets\app.ico
UninstallDisplayIcon={app}\SecureFolder.App.exe
LicenseFile={#SourcePath}..\LICENSE
WizardStyle=modern

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourcePath}..\release\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourcePath}vendor\winfsp-2.2.26215.msi"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\SecureFolder"; Filename: "{app}\SecureFolder.App.exe"
Name: "{group}\{cm:UninstallProgram,SecureFolder}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SecureFolder"; Filename: "{app}\SecureFolder.App.exe"; Tasks: desktopicon

[Run]
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\winfsp-2.2.26215.msi"" /passive /norestart"; \
    StatusMsg: "Instalando WinFsp (unidades virtuales)..."; Flags: waituntilterminated skipifsilent
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\winfsp-2.2.26215.msi"" /qn /norestart"; \
    StatusMsg: "Instalando WinFsp (unidades virtuales)..."; Flags: waituntilterminated skipifnotsilent
Filename: "{app}\SecureFolder.App.exe"; Description: "{cm:LaunchProgram,SecureFolder}"; Flags: nowait postinstall skipifsilent

[Code]
function IsWinFspInstalled: Boolean;
begin
    Result := RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\WinFsp') or
              RegKeyExists(HKLM, 'SOFTWARE\WinFsp');
end;

function InitializeSetup: Boolean;
begin
    Result := True;
    if not IsWinFspInstalled then
    begin
        if MsgBox('SecureFolder requiere WinFsp para montar unidades virtuales.' + #13#10 +
            'Se instalará automáticamente durante la instalación.' + #13#10 + #13#10 +
            '¿Continuar?', mbConfirmation, MB_YESNO) = IDNO then
            Result := False;
    end;
end;
