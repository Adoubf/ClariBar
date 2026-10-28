#define AppVersion "0.6.2"
#ifndef SourceRoot
  #define SourceRoot AddBackslash(SourcePath) + ".."
#endif
#ifndef PayloadDir
  #error "Use scripts/package.ps1 to specify PayloadDir."
#endif
#ifndef ReleaseDir
  #error "Use scripts/package.ps1 to specify ReleaseDir."
#endif

[Setup]
AppId={{93FDC1C2-EB8E-48A4-9CF0-F59AEB645816}
AppName=澄栏 ClariBar
AppVersion={#AppVersion}
AppVerName=澄栏 ClariBar {#AppVersion}
AppPublisher=Haoyue
AppPublisherURL=https://github.com/Adoubf/ClariBar
AppSupportURL=https://github.com/Adoubf/ClariBar/issues
AppUpdatesURL=https://github.com/Adoubf/ClariBar/releases
AppComments=Windows 11 任务栏与实时资源监控
DefaultDirName={localappdata}\Programs\ClariBar
DefaultGroupName=澄栏 ClariBar
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
WizardStyle=modern dynamic
WizardSizePercent=112
WizardImageFile={#SourceRoot}\assets\branding\installer-banner.bmp
WizardSmallImageFile={#SourceRoot}\assets\branding\claribar-64.png
SetupIconFile={#SourceRoot}\assets\branding\ClariBar.ico
UninstallDisplayIcon={app}\ClariBar.exe
UninstallDisplayName=澄栏 ClariBar
OutputDir={#ReleaseDir}
OutputBaseFilename=ClariBar-Setup-{#AppVersion}-x64
Compression=lzma2/max
SolidCompression=yes
LZMAUseSeparateProcess=yes
CloseApplications=no
RestartApplications=no
SetupLogging=yes
ChangesAssociations=no
Uninstallable=yes
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=澄栏 ClariBar 安装程序
VersionInfoProductName=澄栏 ClariBar
VersionInfoOriginalFileName=ClariBar-Setup-{#AppVersion}-x64.exe

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\澄栏 ClariBar"; Filename: "{app}\ClariBar.exe"; WorkingDir: "{app}"
Name: "{group}\卸载澄栏"; Filename: "{uninstallexe}"
Name: "{autodesktop}\澄栏 ClariBar"; Filename: "{app}\ClariBar.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\prerequisites\PawnIO_setup.exe"; Description: "安装硬件传感器组件（读取温度和风扇，需要管理员授权）"; Flags: postinstall shellexec skipifsilent; Check: HardwareDriverMissing
Filename: "{app}\ClariBar.exe"; WorkingDir: "{app}"; Description: "启动澄栏 ClariBar"; Flags: nowait postinstall skipifsilent

[Code]
var
  DataPage: TInputDirWizardPage;

function HardwareDriverMissing: Boolean;
begin
  Result := not RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

function FindClariBarWindow(ClassName: String; WindowName: Integer): HWND;
  external 'FindWindowW@user32.dll stdcall';
function RegisterClariBarMessage(Value: String): Cardinal;
  external 'RegisterWindowMessageW@user32.dll stdcall';

function InstallProcessCount(Directory: String): Integer;
  external 'CB_CountApplicationProcesses@files:ClariBar.Maintenance.dll stdcall setuponly';
function UninstallProcessCount(Directory: String): Integer;
  external 'CB_CountApplicationProcesses@{app}\ClariBar.Maintenance.dll stdcall uninstallonly delayload';

function RequestClariBarExit: Boolean;
var
  Window: HWND;
  Attempt, Count, LastCount: Integer;
begin
  Result := False;LastCount := -1;
  Window := FindClariBarWindow('ClariBar.Host', 0);
  if Window <> 0 then PostMessage(Window, RegisterClariBarMessage('ClariBar.Exit'), 0, 0);
  for Attempt := 1 to 300 do begin
    if IsUninstaller then Count := UninstallProcessCount(ExpandConstant('{app}'))
    else Count := InstallProcessCount(ExpandConstant('{app}'));
    if Count <> LastCount then begin
      Log('Waiting for ClariBar processes: ' + IntToStr(Count));LastCount := Count;
    end;
    if (Count = 0) and not CheckForMutexes('Local\ClariBar.UI') then begin
      Result := True;
      Break;
    end;
    Sleep(200);
  end;
  if IsUninstaller then UnloadDLL(ExpandConstant('{app}\ClariBar.Maintenance.dll'));
end;

procedure InitializeWizard;
var
  PreviousRoot: String;
begin
  DataPage := CreateInputDirPage(wpSelectDir, '运行数据位置',
    '选择配置、日志和临时文件的保存位置',
    '已有颜色、字体和监控指标会继续使用。卸载程序时保留这些个人设置。', False, '');
  DataPage.Add('运行数据目录：');
  PreviousRoot := GetPreviousData('DataRoot', ExpandConstant('{localappdata}\ClariBar'));
  DataPage.Values[0] := ExpandConstant('{param:DATAROOT|' + PreviousRoot + '}');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = DataPage.ID then begin
    if (Length(DataPage.Values[0]) < 4) or not (DataPage.Values[0][2] = ':') then begin
      MsgBox('请选择本地磁盘中的完整目录。', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'DataRoot', DataPage.Values[0]);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not RequestClariBarExit then Result := '请先从系统托盘彻底退出澄栏，再继续安装。';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Root: String;
begin
  if CurStep = ssPostInstall then begin
    Root := DataPage.Values[0];
    ForceDirectories(Root + '\data');ForceDirectories(Root + '\logs');ForceDirectories(Root + '\temp');
    if not SaveStringToFile(ExpandConstant('{app}\runtime-path.txt'), Utf8Encode(Root), False) then
      RaiseException('无法保存运行数据目录。');
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := RequestClariBarExit;
  if not Result then MsgBox('请先退出澄栏，再重新运行卸载程序。', mbError, MB_OK);
end;
