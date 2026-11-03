#ifndef AppVersion
  #error "Use scripts/package.ps1 to supply the version from Version.props."
#endif
#ifndef AppFileVersion
  #error "Use scripts/package.ps1 to supply AppFileVersion."
#endif
#ifndef SourceRoot
  #define SourceRoot AddBackslash(SourcePath) + ".."
#endif
#ifndef PayloadDir
  #error "Use scripts/package.ps1 to specify PayloadDir."
#endif
#ifndef ReleaseDir
  #error "Use scripts/package.ps1 to specify ReleaseDir."
#endif
#ifndef ClariBarAppId
  #define ClariBarAppId "{93FDC1C2-EB8E-48A4-9CF0-F59AEB645816}"
#endif
#ifndef ClariBarGroupName
  #define ClariBarGroupName "{cm:AppDisplayName}"
#endif

[Setup]
AppId={{#ClariBarAppId}
AppName=ClariBar
AppVersion={#AppVersion}
AppVerName=ClariBar {#AppVersion}
AppPublisher=Haoyue
AppPublisherURL=https://github.com/Adoubf/ClariBar
AppSupportURL=https://github.com/Adoubf/ClariBar/issues
AppUpdatesURL=https://github.com/Adoubf/ClariBar/releases
AppComments=Windows 11 taskbar customization and live resource monitoring
DefaultDirName={autopf}\ClariBar
DefaultGroupName={#ClariBarGroupName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
SetupMutex=ClariBar.Setup.{{#ClariBarAppId}
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
UninstallDisplayName=ClariBar
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
VersionInfoVersion={#AppFileVersion}
VersionInfoDescription=ClariBar Setup
VersionInfoProductName=ClariBar
VersionInfoOriginalFileName=ClariBar-Setup-{#AppVersion}-x64.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[CustomMessages]
english.AppDisplayName=ClariBar
chinesesimp.AppDisplayName=澄栏 ClariBar
english.DesktopShortcut=Create a desktop shortcut
chinesesimp.DesktopShortcut=创建桌面快捷方式
english.ShortcutGroup=Shortcuts:
chinesesimp.ShortcutGroup=快捷方式：
english.UninstallShortcut=Uninstall ClariBar
chinesesimp.UninstallShortcut=卸载澄栏
english.HardwareComponent=Install the hardware sensor component (temperatures and fans; administrator authorization required)
chinesesimp.HardwareComponent=安装硬件传感器组件（读取温度和风扇，需要管理员授权）
english.StartApplication=Start ClariBar
chinesesimp.StartApplication=启动澄栏 ClariBar
english.DataTitle=Runtime data location
chinesesimp.DataTitle=运行数据位置
english.DataSubtitle=Choose where to save settings, logs and temporary files
chinesesimp.DataSubtitle=选择配置、日志和临时文件的保存位置
english.DataDescription=Existing colors, fonts and monitoring choices are preserved. Uninstalling keeps your personal runtime data.
chinesesimp.DataDescription=已有颜色、字体和监控指标会继续使用。卸载程序时保留这些个人设置。
english.UpgradeDataDescription=Setup will remove the previous program and install this version, keeping your settings, credentials, history and downloaded models in the existing runtime data directory.
chinesesimp.UpgradeDataDescription=安装程序将自动卸载旧程序并安装此版本，继续使用原运行数据目录，保留设置、密钥、历史记录和已下载模型。
english.UpgradeSummary=Replace installed version %1 and keep its runtime data:
chinesesimp.UpgradeSummary=替换已安装版本 %1，并保留其运行数据：
english.RemovingPrevious=Removing the previous version of ClariBar. Your personal data will be kept...
chinesesimp.RemovingPrevious=正在卸载旧版澄栏，保留个人数据……
english.PreviousInstallInvalid=The previous ClariBar installation is incomplete. Its uninstaller could not be found or validated. Repair or uninstall that installation before trying again. Your personal data has not been removed.
chinesesimp.PreviousInstallInvalid=检测到旧版澄栏安装不完整，无法找到或验证卸载程序。请先修复或卸载旧程序，再重新安装。个人数据未被删除。
english.PreviousInstallConflict=More than one ClariBar installation was found. Remove the extra installation before continuing; personal runtime data is kept when uninstalling.
chinesesimp.PreviousInstallConflict=检测到多个澄栏安装记录。请先卸载多余的程序，再继续安装；卸载时会保留个人运行数据。
english.PreviousInstallAllUsers=ClariBar is installed for all users. Run this installer with /ALLUSERS to upgrade it in the same mode.
chinesesimp.PreviousInstallAllUsers=旧版澄栏是为所有用户安装的。请使用 /ALLUSERS 参数运行此安装包，以相同模式升级。
english.PreviousInstallCurrentUser=ClariBar is installed for the current user. Run this installer with /CURRENTUSER to upgrade that installation.
chinesesimp.PreviousInstallCurrentUser=旧版澄栏是为当前用户安装的。请使用 /CURRENTUSER 参数运行此安装包，升级现有程序。
english.PreviousUninstallFailed=Could not completely uninstall the previous version (error %1). Setup has stopped before installing new files. Your personal runtime data is kept. See the uninstall log: %2
chinesesimp.PreviousUninstallFailed=无法完整卸载旧版（错误 %1），已停止安装新文件。个人运行数据会保留。请查看卸载日志：%2
english.UpgradeDataPathChanged=Upgrades reuse the existing runtime data directory to preserve your configuration. Remove the /DATAROOT override or use the existing directory.
chinesesimp.UpgradeDataPathChanged=升级会沿用原运行数据目录，以保留配置。请移除 /DATAROOT 参数，或将其设为原目录。
english.DataDirectory=Runtime data directory:
chinesesimp.DataDirectory=运行数据目录：
english.ChooseLocalPath=Choose a full path on a local drive.
chinesesimp.ChooseLocalPath=请选择本地磁盘中的完整目录。
english.QuitBeforeInstall=Please quit ClariBar from the system tray before continuing setup.
chinesesimp.QuitBeforeInstall=请先从系统托盘彻底退出澄栏，再继续安装。
english.CannotSaveDataPath=Could not save the runtime data directory.
chinesesimp.CannotSaveDataPath=无法保存运行数据目录。
english.QuitBeforeUninstall=Please quit ClariBar before running the uninstaller again.
chinesesimp.QuitBeforeUninstall=请先退出澄栏，再重新运行卸载程序。

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:ShortcutGroup}"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{cm:AppDisplayName}"; Filename: "{app}\ClariBar.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{cm:AppDisplayName}"; Filename: "{app}\ClariBar.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\prerequisites\PawnIO_setup.exe"; Description: "{cm:HardwareComponent}"; Flags: postinstall shellexec skipifsilent; Check: HardwareDriverMissing
Filename: "{app}\ClariBar.exe"; WorkingDir: "{app}"; Description: "{cm:StartApplication}"; Flags: nowait postinstall skipifsilent

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#ClariBarAppId}_is1';
var
  DataPage: TInputDirWizardPage;
  PreviousAppDir, PreviousUninstaller, PreviousDataRoot, PreviousVersion: String;
  PreviousRemoved: Boolean;
  PreviousRegRoot, PreviousOtherView: Integer;

function SameDirectory(const Left, Right: String): Boolean;
begin
  Result := CompareText(RemoveBackslashUnlessRoot(Left), RemoveBackslashUnlessRoot(Right)) = 0;
end;

function IsLocalDirectory(const Value: String): Boolean;
begin
  Result := False;
  if Length(Value) >= 3 then
    Result := (Value[2] = ':') and (Value[3] = '\') and
      (((Value[1] >= 'A') and (Value[1] <= 'Z')) or ((Value[1] >= 'a') and (Value[1] <= 'z')));
end;

function UninstallerPath(Value: String): String;
var
  ClosingQuote: Integer;
begin
  Value := Trim(Value);
  Result := Value;
  if (Length(Value) > 0) and (Value[1] = '"') then begin
    Delete(Value, 1, 1);
    ClosingQuote := Pos('"', Value);
    if ClosingQuote = 0 then Result := ''
    else Result := Copy(Value, 1, ClosingQuote - 1);
  end;
end;

function InitializeSetup: Boolean;
var
  OtherRoot64, OtherRoot32: Integer;
  OtherAppDir, Command, DataOverride, Error: String;
  RuntimePath: AnsiString;
begin
  Result := False;
  PreviousRemoved := False;
  if IsAdminInstallMode then begin
    PreviousRegRoot := HKLM64;PreviousOtherView := HKLM32;
    OtherRoot64 := HKCU64;OtherRoot32 := HKCU32;
  end else begin
    PreviousRegRoot := HKCU64;PreviousOtherView := HKCU32;
    OtherRoot64 := HKLM64;OtherRoot32 := HKLM32;
  end;
  if not RegKeyExists(PreviousRegRoot, UninstallKey) then begin
    PreviousRegRoot := PreviousOtherView;
    PreviousOtherView := 0;
  end;
  if RegKeyExists(OtherRoot64, UninstallKey) or RegKeyExists(OtherRoot32, UninstallKey) then begin
    if RegKeyExists(PreviousRegRoot, UninstallKey) then Error := CustomMessage('PreviousInstallConflict')
    else if IsAdminInstallMode then Error := CustomMessage('PreviousInstallCurrentUser')
    else Error := CustomMessage('PreviousInstallAllUsers');
  end else if RegKeyExists(PreviousRegRoot, UninstallKey) then begin
    RegQueryStringValue(PreviousRegRoot, UninstallKey, 'InstallLocation', PreviousAppDir);
    RegQueryStringValue(PreviousRegRoot, UninstallKey, 'UninstallString', Command);
    RegQueryStringValue(PreviousRegRoot, UninstallKey, 'DisplayVersion', PreviousVersion);
    PreviousAppDir := RemoveBackslashUnlessRoot(PreviousAppDir);
    PreviousUninstaller := UninstallerPath(Command);
    if (PreviousOtherView <> 0) and RegKeyExists(PreviousOtherView, UninstallKey) then begin
      RegQueryStringValue(PreviousOtherView, UninstallKey, 'InstallLocation', OtherAppDir);
      if not SameDirectory(PreviousAppDir, OtherAppDir) then Error := CustomMessage('PreviousInstallConflict');
    end;
    if Error = '' then begin
      if not IsLocalDirectory(PreviousAppDir) or
         not SameDirectory(ExtractFileDir(PreviousUninstaller), PreviousAppDir) or
         (CompareText(Copy(ExtractFileName(PreviousUninstaller), 1, 5), 'unins') <> 0) or
         (CompareText(ExtractFileExt(PreviousUninstaller), '.exe') <> 0) or
         not FileExists(PreviousUninstaller) or
         not FileExists(ChangeFileExt(PreviousUninstaller, '.dat')) then
        Error := CustomMessage('PreviousInstallInvalid');
    end;
    if Error = '' then begin
      RegQueryStringValue(PreviousRegRoot, UninstallKey, 'Inno Setup CodeFile: DataRoot', PreviousDataRoot);
      if not IsLocalDirectory(PreviousDataRoot) and
         LoadStringFromFile(PreviousAppDir + '\runtime-path.txt', RuntimePath) then begin
        PreviousDataRoot := Trim(Utf8Decode(RuntimePath));
        if (Length(PreviousDataRoot) > 0) and (PreviousDataRoot[1] = #$FEFF) then Delete(PreviousDataRoot, 1, 1);
      end;
      if not IsLocalDirectory(PreviousDataRoot) then PreviousDataRoot := ExpandConstant('{localappdata}\ClariBar');
      PreviousDataRoot := RemoveBackslashUnlessRoot(PreviousDataRoot);
      DataOverride := ExpandConstant('{param:DATAROOT|}');
      if (DataOverride <> '') and not SameDirectory(DataOverride, PreviousDataRoot) then
        Error := CustomMessage('UpgradeDataPathChanged');
      Log('Detected previous ClariBar ' + PreviousVersion + ' at ' + PreviousAppDir);
      Log('Preserving runtime data at ' + PreviousDataRoot);
    end;
  end;
  Result := Error = '';
  if not Result then SuppressibleMsgBox(Error, mbError, MB_OK, IDOK);
end;

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

function ApplicationProcessCount: Integer;
var
  OtherCount: Integer;
begin
  if IsUninstaller then Result := UninstallProcessCount(ExpandConstant('{app}'))
  else begin
    Result := InstallProcessCount(ExpandConstant('{app}'));
    if (PreviousAppDir <> '') and not SameDirectory(PreviousAppDir, ExpandConstant('{app}')) then begin
      OtherCount := InstallProcessCount(PreviousAppDir);
      if (Result < 0) or (OtherCount < 0) then Result := -1
      else Result := Result + OtherCount;
    end;
  end;
end;

function RequestClariBarExit: Boolean;
var
  Window: HWND;
  Attempt, Count, LastCount: Integer;
begin
  Result := False;LastCount := -1;
  try
    Count := ApplicationProcessCount;
    if Count > 0 then begin
      Window := FindClariBarWindow('ClariBar.Host', 0);
      if Window <> 0 then PostMessage(Window, RegisterClariBarMessage('ClariBar.Exit'), 0, 0);
    end;
    for Attempt := 1 to 300 do begin
      Count := ApplicationProcessCount;
      if Count <> LastCount then begin
        Log('Waiting for ClariBar processes: ' + IntToStr(Count));LastCount := Count;
      end;
      if Count = 0 then begin
        Result := True;
        Break;
      end;
      if Count < 0 then Break;
      Sleep(200);
    end;
  finally
    if IsUninstaller then UnloadDLL(ExpandConstant('{app}\ClariBar.Maintenance.dll'));
  end;
end;

procedure InitializeWizard;
var
  PreviousRoot, DataDescription: String;
begin
  if (PreviousAppDir <> '') and (ExpandConstant('{param:DIR|}') = '') then
    WizardForm.DirEdit.Text := PreviousAppDir;
  if PreviousAppDir <> '' then DataDescription := CustomMessage('UpgradeDataDescription')
  else DataDescription := CustomMessage('DataDescription');
  DataPage := CreateInputDirPage(wpSelectDir, CustomMessage('DataTitle'),
    CustomMessage('DataSubtitle'),
    DataDescription, False, '');
  DataPage.Add(CustomMessage('DataDirectory'));
  PreviousRoot := GetPreviousData('DataRoot', ExpandConstant('{localappdata}\ClariBar'));
  if PreviousAppDir <> '' then begin
    DataPage.Values[0] := PreviousDataRoot;
    DataPage.Edits[0].ReadOnly := True;
    DataPage.Buttons[0].Enabled := False;
  end else begin
    DataPage.Values[0] := ExpandConstant('{param:DATAROOT|}');
    if DataPage.Values[0] = '' then DataPage.Values[0] := PreviousRoot;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = DataPage.ID then begin
    if not IsLocalDirectory(DataPage.Values[0]) then begin
      MsgBox(CustomMessage('ChooseLocalPath'), mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + MemoGroupInfo;
  if MemoTasksInfo <> '' then Result := Result + NewLine + NewLine + MemoTasksInfo;
  if PreviousAppDir <> '' then
    Result := Result + NewLine + NewLine + FmtMessage(CustomMessage('UpgradeSummary'), [PreviousVersion]) +
      NewLine + Space + PreviousDataRoot;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'DataRoot', DataPage.Values[0]);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode, Attempt: Integer;
  UninstallLog, PreparingText: String;
begin
  Result := '';
  if not RequestClariBarExit then begin
    Result := CustomMessage('QuitBeforeInstall');
    Exit;
  end;
  if (PreviousAppDir = '') or PreviousRemoved then Exit;
  UninstallLog := AddBackslash(PreviousDataRoot) + 'logs\upgrade-uninstall-' +
    GetDateTimeString('yyyymmdd-hhnnss-zzz', '-', ':') + '.log';
  if not ForceDirectories(ExtractFileDir(UninstallLog)) then begin
    Result := CustomMessage('CannotSaveDataPath');
    Exit;
  end;
  PreparingText := WizardForm.PreparingLabel.Caption;
  WizardForm.PreparingLabel.Caption := CustomMessage('RemovingPrevious');
  try
    Log('Uninstalling previous ClariBar before installing new program files: ' + PreviousUninstaller);
    if not Exec(PreviousUninstaller,
      '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + UninstallLog + '"',
      PreviousAppDir, SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
      Log('Could not start previous uninstaller: ' + SysErrorMessage(ExitCode));
      Result := FmtMessage(CustomMessage('PreviousUninstallFailed'), [IntToStr(ExitCode), UninstallLog]);
      Exit;
    end;
    if ExitCode <> 0 then begin
      Result := FmtMessage(CustomMessage('PreviousUninstallFailed'), [IntToStr(ExitCode), UninstallLog]);
      Exit;
    end;
    { Inno's uninstaller clone may still be removing its original executable. }
    for Attempt := 1 to 100 do begin
      if not RegKeyExists(PreviousRegRoot, UninstallKey) and
         not FileExists(PreviousUninstaller) then Break;
      Sleep(100);
    end;
    if RegKeyExists(PreviousRegRoot, UninstallKey) or
       ((PreviousOtherView <> 0) and RegKeyExists(PreviousOtherView, UninstallKey)) or
       FileExists(PreviousUninstaller) or FileExists(PreviousAppDir + '\ClariBar.exe') then begin
      Result := FmtMessage(CustomMessage('PreviousUninstallFailed'), ['cleanup', UninstallLog]);
      Exit;
    end;
    PreviousRemoved := True;
    Log('Previous ClariBar uninstall completed; runtime data retained at ' + PreviousDataRoot);
  finally
    WizardForm.PreparingLabel.Caption := PreparingText;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Root: String;
begin
  if CurStep = ssPostInstall then begin
    Root := DataPage.Values[0];
    ForceDirectories(Root + '\data');ForceDirectories(Root + '\logs');ForceDirectories(Root + '\temp');
    if not SaveStringToFile(ExpandConstant('{app}\runtime-path.txt'), Utf8Encode(Root), False) then
      RaiseException(CustomMessage('CannotSaveDataPath'));
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := RequestClariBarExit;
  if not Result then MsgBox(CustomMessage('QuitBeforeUninstall'), mbError, MB_OK);
end;
