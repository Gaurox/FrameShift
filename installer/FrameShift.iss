#define MyAppName "FrameShift"
#define MyAppId "FrameShift"
#ifndef MyAppVersion
#define MyAppVersion "1.14.0"
#endif
#define MyAppPublisher "FrameShift"
#define MyAppExeName "FrameShift.exe"
#define PublishOutputDir "..\src\FrameShift\bin\Release\net8.0-windows\win-x64\publish"
#define AppPayloadDir PublishOutputDir
#define AssetsDir "..\src\FrameShift\Assets"
#define ToolsDir "..\src\FrameShift\Tools"

[Setup]
AppName={#MyAppName}
AppId={#MyAppId}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\FrameShift
DefaultGroupName={#MyAppName}
OutputDir=.
OutputBaseFilename=FrameShift_{#MyAppVersion}_Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
DisableProgramGroupPage=yes
AlwaysShowComponentsList=yes
SetupIconFile={#AssetsDir}\Icons\app\app.ico
UninstallDisplayIcon={app}\Assets\Icons\app\app.ico
ShowComponentSizes=no
DisableDirPage=no
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "complete"; Description: "First test installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "core"; Description: "FrameShift application"; Types: complete custom; Flags: fixed
Name: "ai"; Description: "FrameShift AI"; Types: complete custom
Name: "ai\remove_background"; Description: "Remove background"; Types: complete custom
Name: "ai\remove_background\fast"; Description: "Fast"; Types: complete custom
Name: "ai\remove_background\high_resolution"; Description: "High Resolution (Matting) - CPU only"; Types: custom
Name: "ai\remove_background\high_resolution_general"; Description: "High Resolution (General) - CPU only"; Types: custom
Name: "ai\remove_background\bria_balanced"; Description: "BRIA Remove Background (Balanced) - manual model, not included"; Types: custom
Name: "ai\remove_background\bria_high_quality"; Description: "BRIA Remove Background (High Quality) - manual model, not included"; Types: custom
Name: "ai\remove_noise"; Description: "Remove noise"; Types: complete custom
Name: "ai\remove_noise_video"; Description: "Remove noise (video)"; Types: complete custom
Name: "ai\separate_audio"; Description: "Audio separation"; Types: complete custom
Name: "ai\interpolate_video_rife"; Description: "Interpolate video (RIFE)"; Types: complete custom
Name: "ai\remove_object"; Description: "Remove object"; Types: complete custom
Name: "ai\upscale_image"; Description: "Upscale image (4x)"; Types: complete custom
Name: "ai\upscale_video"; Description: "Upscale video (2x / 3x / 4x)"; Types: complete custom
Name: "video"; Description: "Video actions"; Types: complete custom
Name: "video\convert_video"; Description: "Convert video"; Types: complete custom
Name: "video\remove_audio"; Description: "Remove audio"; Types: complete custom
Name: "video\extract_frames"; Description: "Extract frames"; Types: complete custom
Name: "video\create_gif"; Description: "Create GIF"; Types: complete custom
Name: "video\extract_audio"; Description: "Extract audio"; Types: complete custom
Name: "video\cut_video"; Description: "Cut video"; Types: complete custom
Name: "video\crop_video"; Description: "Crop video"; Types: complete custom
Name: "video\rotate_flip_video"; Description: "Rotate / Flip video"; Types: complete custom
Name: "video\resize_video"; Description: "Resize video"; Types: complete custom
Name: "video\compress_video"; Description: "Compress video"; Types: complete custom
Name: "video\interpolate_video"; Description: "Interpolate video"; Types: complete custom
Name: "audio"; Description: "Audio actions"; Types: complete custom
Name: "audio\cut_audio"; Description: "Cut audio"; Types: complete custom
Name: "audio\convert_audio"; Description: "Convert audio"; Types: complete custom
Name: "audio\reverse_audio"; Description: "Reverse audio"; Types: complete custom
Name: "audio\compress_audio"; Description: "Compress audio"; Types: complete custom
Name: "audio\change_pitch"; Description: "Change pitch"; Types: complete custom
Name: "audio\change_audio_speed"; Description: "Change audio speed"; Types: complete custom
Name: "video\change_video_speed"; Description: "Change video speed"; Types: complete custom
Name: "image"; Description: "Image actions"; Types: complete custom
Name: "image\image_to_pdf"; Description: "Image to PDF"; Types: complete custom
Name: "image\convert_image"; Description: "Convert image"; Types: complete custom
Name: "image\compress_image"; Description: "Compress image"; Types: complete custom
Name: "image\convert_icon"; Description: "Convert to icon"; Types: complete custom
Name: "image\crop_image"; Description: "Crop image"; Types: complete custom
Name: "image\resize_image"; Description: "Resize image"; Types: complete custom
Name: "image\rotate_flip_image"; Description: "Rotate / Flip image"; Types: complete custom
Name: "tools"; Description: "Outils"; Types: complete custom
Name: "tools\media_info"; Description: "Media Info"; Types: complete custom

[Files]
Source: "{#AppPayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "Tools\ffmpeg\*,logs\*,*.pdb,createdump.exe,mscordaccore*.dll,mscordbi.dll,onnxruntime.lib,DirectML.Debug.dll,DirectML.Debug.pdb"; Components: core
Source: "{#AppPayloadDir}\Tools\ffmpeg\ffmpeg.exe"; DestDir: "{app}\Tools\ffmpeg"; Flags: ignoreversion; Components: video\convert_video video\remove_audio video\extract_frames video\create_gif video\extract_audio video\cut_video video\crop_video video\rotate_flip_video video\resize_video video\compress_video video\change_video_speed video\interpolate_video audio\cut_audio audio\convert_audio audio\reverse_audio audio\compress_audio audio\change_pitch audio\change_audio_speed image\image_to_pdf image\convert_image image\compress_image image\convert_icon image\crop_image image\resize_image image\rotate_flip_image ai\interpolate_video_rife ai\upscale_video
Source: "{#AppPayloadDir}\Tools\ffmpeg\ffprobe.exe"; DestDir: "{app}\Tools\ffmpeg"; Flags: ignoreversion; Components: video\convert_video video\remove_audio video\extract_frames video\create_gif video\extract_audio video\cut_video video\crop_video video\rotate_flip_video video\resize_video video\compress_video video\change_video_speed video\interpolate_video image\resize_image audio\cut_audio audio\convert_audio audio\reverse_audio audio\compress_audio audio\change_pitch audio\change_audio_speed ai\separate_audio ai\interpolate_video_rife ai\upscale_video tools\media_info

[Icons]
Name: "{autoprograms}\FrameShift"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Icons\app\app.ico"; Components: core

[Code]
procedure ExitProcess(uExitCode: Integer); external 'ExitProcess@kernel32.dll stdcall';
procedure SHChangeNotify(wEventId: LongInt; uFlags: Cardinal; dwItem1: Integer; dwItem2: Integer); external 'SHChangeNotify@shell32.dll stdcall';

const
  VideoExtensions = '.mp4,.mkv,.avi,.mov,.webm,.m4v';
  AudioExtensions = '.mp3,.wav,.wave,.flac,.m4a,.ogg,.aac,.wma';
  ImageExtensions = '.png,.jpg,.jpeg,.webp,.bmp';
  PdfImageExtensions = '.png,.jpg,.jpeg,.webp,.bmp';
  UninstallSubkey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1';
  ExistingInstallActionInstall = 1;
  ExistingInstallActionUninstall = 2;
  ExistingInstallStateOlder = -1;
  ExistingInstallStateSame = 0;
  ExistingInstallStateNewer = 1;

var
  InstalledVersion: string;
  InstalledUninstallString: string;
  InstalledVersionState: Integer;
  HasInstalledVersion: Boolean;
  ExistingInstallPage: TWizardPage;
  ExistingInstallStatusLabel: TNewStaticText;
  ExistingInstallInstallRadio: TNewRadioButton;
  ExistingInstallUninstallRadio: TNewRadioButton;
  ExistingInstallSelectedAction: Integer;
  AiModelsPage: TWizardPage;
  AiModelsDirEdit: TEdit;
  AiModelsDirBrowseButton: TButton;
  AiModelsSelectedDir: string;

function GetListItem(var List: string): string;
var
  CommaPos: Integer;
begin
  CommaPos := Pos(',', List);
  if CommaPos > 0 then
  begin
    Result := Trim(Copy(List, 1, CommaPos - 1));
    Delete(List, 1, CommaPos);
  end
  else
  begin
    Result := Trim(List);
    List := '';
  end;
end;

function TryGetInstalledValue(const ValueName: string; var Value: string): Boolean;
begin
  Result :=
    RegQueryStringValue(HKLM64, UninstallSubkey, ValueName, Value) or
    RegQueryStringValue(HKLM, UninstallSubkey, ValueName, Value) or
    RegQueryStringValue(HKCU64, UninstallSubkey, ValueName, Value) or
    RegQueryStringValue(HKCU, UninstallSubkey, ValueName, Value);
end;

function FindInstalledVersion(): Boolean;
begin
  InstalledVersion := '';
  InstalledUninstallString := '';
  Result := TryGetInstalledValue('DisplayVersion', InstalledVersion);
  if Result then
  begin
    TryGetInstalledValue('UninstallString', InstalledUninstallString);
  end;
end;

function ReadNextVersionPart(const Version: string; var Index: Integer): Integer;
var
  StartIndex: Integer;
  Token: string;
begin
  while (Index <= Length(Version)) and (Version[Index] = '.') do
  begin
    Index := Index + 1;
  end;

  StartIndex := Index;
  while (Index <= Length(Version)) and (Version[Index] <> '.') do
  begin
    Index := Index + 1;
  end;

  Token := Copy(Version, StartIndex, Index - StartIndex);
  if Token = '' then
  begin
    Result := 0;
  end
  else
  begin
    Result := StrToIntDef(Token, 0);
  end;
end;

function CompareVersionStrings(const LeftVersion, RightVersion: string): Integer;
var
  LeftIndex: Integer;
  RightIndex: Integer;
  LeftPart: Integer;
  RightPart: Integer;
  Iteration: Integer;
begin
  LeftIndex := 1;
  RightIndex := 1;

  for Iteration := 0 to 3 do
  begin
    LeftPart := ReadNextVersionPart(LeftVersion, LeftIndex);
    RightPart := ReadNextVersionPart(RightVersion, RightIndex);

    if LeftPart < RightPart then
    begin
      Result := -1;
      exit;
    end;

    if LeftPart > RightPart then
    begin
      Result := 1;
      exit;
    end;
  end;

  Result := 0;
end;

function TryRunInstalledUninstaller(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if InstalledUninstallString = '' then
  begin
    exit;
  end;

  Result := Exec(
    RemoveQuotes(InstalledUninstallString),
    '',
    '',
    SW_SHOWNORMAL,
    ewWaitUntilTerminated,
    ResultCode);
end;

procedure UpdateExistingInstallPageContent();
begin
  if not HasInstalledVersion then
  begin
    exit;
  end;

  if InstalledVersionState = ExistingInstallStateOlder then
  begin
    ExistingInstallPage.Caption := 'Existing installation detected';
    ExistingInstallPage.Description :=
      'Choose whether to update the installed copy or uninstall it first.';
    ExistingInstallStatusLabel.Caption :=
      'FrameShift version ' + InstalledVersion + ' is already installed.' + #13#10#13#10 +
      'This setup can update it to version {#MyAppVersion}.';
    ExistingInstallInstallRadio.Caption := 'Update to version {#MyAppVersion}';
  end
  else if InstalledVersionState = ExistingInstallStateSame then
  begin
    ExistingInstallPage.Caption := 'Existing installation detected';
    ExistingInstallPage.Description :=
      'Choose whether to reinstall the current version or uninstall it.';
    ExistingInstallStatusLabel.Caption :=
      'FrameShift version {#MyAppVersion} is already installed.' + #13#10#13#10 +
      'You can reinstall this version over the current installation.';
    ExistingInstallInstallRadio.Caption := 'Reinstall version {#MyAppVersion}';
  end
  else
  begin
    ExistingInstallPage.Caption := 'Existing installation detected';
    ExistingInstallPage.Description :=
      'Choose whether to install this setup over the current copy or uninstall it.';
    ExistingInstallStatusLabel.Caption :=
      'A newer FrameShift version (' + InstalledVersion + ') is already installed.' + #13#10#13#10 +
      'You can still install version {#MyAppVersion} over the current installation.';
    ExistingInstallInstallRadio.Caption := 'Install version {#MyAppVersion} over the current installation';
  end;

  ExistingInstallUninstallRadio.Caption := 'Uninstall the current FrameShift installation';
  ExistingInstallInstallRadio.Checked := True;
  ExistingInstallSelectedAction := ExistingInstallActionInstall;
end;

procedure CleanupContextMenuKeysForHive(const Hive: Integer; const Extensions: string);
var
  RemainingExtensions: string;
  Extension: string;
begin
  RemainingExtensions := Extensions;
  while RemainingExtensions <> '' do
  begin
    Extension := GetListItem(RemainingExtensions);
    RegDeleteKeyIncludingSubkeys(
      Hive,
      'Software\Classes\SystemFileAssociations\' + Extension + '\shell\FrameShift');
  end;
end;

procedure CleanupContextMenuKeys;
begin
  CleanupContextMenuKeysForHive(HKCU, VideoExtensions);
  CleanupContextMenuKeysForHive(HKLM, VideoExtensions);
  CleanupContextMenuKeysForHive(HKCU, AudioExtensions);
  CleanupContextMenuKeysForHive(HKLM, AudioExtensions);
  CleanupContextMenuKeysForHive(HKCU, ImageExtensions);
  CleanupContextMenuKeysForHive(HKLM, ImageExtensions);
end;

procedure CleanupContextMenuAIKeysForHive(const Hive: Integer; const Extensions: string);
var
  RemainingExtensions: string;
  Extension: string;
begin
  RemainingExtensions := Extensions;
  while RemainingExtensions <> '' do
  begin
    Extension := GetListItem(RemainingExtensions);
    RegDeleteKeyIncludingSubkeys(
      Hive,
      'Software\Classes\SystemFileAssociations\' + Extension + '\shell\FrameShiftAI');
  end;
end;

procedure CleanupContextMenuAIKeys;
begin
  CleanupContextMenuAIKeysForHive(HKCU, ImageExtensions);
  CleanupContextMenuAIKeysForHive(HKLM, ImageExtensions);
  CleanupContextMenuAIKeysForHive(HKCU, VideoExtensions);
  CleanupContextMenuAIKeysForHive(HKLM, VideoExtensions);
  CleanupContextMenuAIKeysForHive(HKCU, AudioExtensions);
  CleanupContextMenuAIKeysForHive(HKLM, AudioExtensions);
end;

procedure EnsureFrameShiftAIRootForHive(const Hive: Integer; const Ext: string);
var
  KeyPath: string;
begin
  KeyPath := 'Software\Classes\SystemFileAssociations\' + Ext + '\shell\FrameShiftAI';
  RegWriteStringValue(Hive, KeyPath, 'MUIVerb', 'FrameShift AI');
  RegWriteStringValue(Hive, KeyPath, 'SubCommands', '');
  RegWriteStringValue(Hive, KeyPath, 'Icon', ExpandConstant('{app}\Assets\Icons\ai\frameshift_ai.ico'));
end;

procedure ConfigureAIActionMenuForHive(
  const Hive: Integer;
  const Ext, MenuKey, LabelText, ActionId, CommandSuffix: string);
var
  KeyPath: string;
  CommandValue: string;
  IconPath: string;
begin
  EnsureFrameShiftAIRootForHive(Hive, Ext);
  KeyPath := 'Software\Classes\SystemFileAssociations\' + Ext + '\shell\FrameShiftAI\shell\' + MenuKey;
  RegDeleteKeyIncludingSubkeys(Hive, KeyPath);
  RegWriteStringValue(Hive, KeyPath, 'MUIVerb', LabelText);
  if Pos('remove_background', MenuKey) = 1 then
  begin
    IconPath := ExpandConstant('{app}\Assets\Icons\ai\remove_background.ico');
  end
  else if MenuKey = 'remove_noise' then
  begin
    IconPath := ExpandConstant('{app}\Assets\Icons\ai\remove_noise_icon.ico');
  end
  else if MenuKey = 'remove_noise_video' then
  begin
    IconPath := ExpandConstant('{app}\Assets\Icons\ai\remove_noise_icon.ico');
  end
  else if MenuKey = 'separate_audio' then
  begin
    IconPath := ExpandConstant('{app}\Assets\Icons\ai\separate_audio_icon.ico');
  end
  else if MenuKey = 'interpolate_video_rife' then
  begin
    IconPath := ExpandConstant('{app}\Assets\Icons\ai\Interpolate_icon.ico');
  end
  else if MenuKey = 'remove_object' then
  begin
    IconPath := ExpandConstant('{app}\Assets\Icons\ai\remove_object.ico');
  end
  else if MenuKey = 'upscale_image' then
  begin
    IconPath := ExpandConstant('{app}\Assets\Icons\ai\upscale_image.ico');
  end
  else if MenuKey = 'upscale_video' then
  begin
    IconPath := ExpandConstant('{app}\Assets\Icons\ai\upscale_video.ico');
  end
  else
  begin
    IconPath := ExpandConstant('{app}\Assets\Icons\ai\frameshift_ai.ico');
  end;
  RegWriteStringValue(Hive, KeyPath, 'Icon', IconPath);
  CommandValue :=
    '"' + ExpandConstant('{app}\{#MyAppExeName}') + '"' +
    ' --action ' + ActionId + CommandSuffix + ' "%1"';
  RegWriteStringValue(Hive, KeyPath + '\command', '', CommandValue);
end;

procedure ApplyAIActionMenuList(
  const Extensions, MenuKey, LabelText, ActionId, CommandSuffix: string);
var
  RemainingExtensions: string;
  Extension: string;
begin
  RemainingExtensions := Extensions;
  while RemainingExtensions <> '' do
  begin
    Extension := GetListItem(RemainingExtensions);
    if IsAdminInstallMode then
    begin
      ConfigureAIActionMenuForHive(HKLM, Extension, MenuKey, LabelText, ActionId, CommandSuffix);
    end
    else
    begin
      ConfigureAIActionMenuForHive(HKCU, Extension, MenuKey, LabelText, ActionId, CommandSuffix);
    end;
  end;
end;

procedure EnsureFrameShiftRootForHive(const Hive: Integer; const Ext: string);
var
  KeyPath: string;
begin
  KeyPath := 'Software\Classes\SystemFileAssociations\' + Ext + '\shell\FrameShift';
  RegWriteStringValue(Hive, KeyPath, 'MUIVerb', 'FrameShift');
  RegWriteStringValue(Hive, KeyPath, 'SubCommands', '');
  RegWriteStringValue(Hive, KeyPath, 'Icon', ExpandConstant('{app}\Assets\Icons\app\app.ico'));
end;

function GetMenuIconPath(const MenuKey: string): string;
begin
  Result := '';
  if MenuKey = 'convert_video' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\convert-audio-video-image-icon.ico');
  end;
  if MenuKey = 'convert_audio' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\convert-audio-video-image-icon.ico');
  end;
  if MenuKey = 'cut_audio' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\cut-video-audio-icon.ico');
  end;
  if MenuKey = 'convert_image' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\convert-audio-video-image-icon.ico');
  end;
  if MenuKey = 'media_info' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\media-info-video-image-audio-icon.ico');
  end;
  if MenuKey = 'convert_icon' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\convert-icon-image-icon.ico');
  end;
  if MenuKey = 'remove_audio' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\remove-audio-video-icon.ico');
  end;
  if MenuKey = 'reverse_audio' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\reverse-audio-audio-icon.ico');
  end;
  if MenuKey = 'change_pitch' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\change-pitch-audio-icon.ico');
  end;
  if MenuKey = 'change_audio_speed' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\change-audio-speed-audio-icon.ico');
  end;
  if MenuKey = 'change_video_speed' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\change-video-speed-video-icon.ico');
  end;
  if MenuKey = 'extract_frames' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\extract-frames-video-icon.ico');
  end;
  if MenuKey = 'create_gif' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\create-gif-video-icon.ico');
  end;
  if MenuKey = 'image_to_pdf' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\image-to-pdf-image-icon.ico');
  end;
  if MenuKey = 'extract_audio' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\extract-audio-video-icon.ico');
  end;
  if MenuKey = 'cut_video' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\cut-video-audio-icon.ico');
  end;
  if MenuKey = 'crop_video' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\crop-video-image-icon.ico');
  end;
  if MenuKey = 'resize_video' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\resize-image-video-icon.ico');
  end;
  if MenuKey = 'compress_video' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\compress-video-image-audio-icon.ico');
  end;
  if MenuKey = 'compress_audio' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\compress-video-image-audio-icon.ico');
  end;
  if MenuKey = 'compress_image' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\compress-video-image-audio-icon.ico');
  end;
  if MenuKey = 'crop_image' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\crop-video-image-icon.ico');
  end;
  if MenuKey = 'resize_image' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\resize-image-video-icon.ico');
  end;
  if MenuKey = 'rotate_flip_image' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\rotate-video-image-icon.ico');
  end;
  if MenuKey = 'rotate_flip_video' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\rotate-video-image-icon.ico');
  end;
  if MenuKey = 'interpolate_video' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\interpolate-video-icon.ico');
  end;
end;

procedure ConfigureActionMenuForHive(
  const Hive: Integer;
  const Ext, MenuKey, LabelText, ActionId, PositionValue: string);
var
  KeyPath: string;
  CommandValue: string;
  IconPath: string;
begin
  EnsureFrameShiftRootForHive(Hive, Ext);

  KeyPath := 'Software\Classes\SystemFileAssociations\' + Ext + '\shell\FrameShift\shell\' + MenuKey;
  RegDeleteKeyIncludingSubkeys(Hive, KeyPath);
  RegWriteStringValue(Hive, KeyPath, 'MUIVerb', LabelText);

  IconPath := GetMenuIconPath(MenuKey);
  if IconPath <> '' then
  begin
    RegWriteStringValue(Hive, KeyPath, 'Icon', IconPath);
  end;

  if PositionValue <> '' then
  begin
    RegWriteStringValue(Hive, KeyPath, 'Position', PositionValue);
  end;

  CommandValue :=
    '"' + ExpandConstant('{app}\{#MyAppExeName}') + '"' +
    ' --action ' + ActionId + ' "%1"';
  RegWriteStringValue(Hive, KeyPath + '\command', '', CommandValue);
end;

procedure ApplyActionMenuList(
  const Extensions, MenuKey, LabelText, ActionId, PositionValue: string);
var
  RemainingExtensions: string;
  Extension: string;
begin
  RemainingExtensions := Extensions;
  while RemainingExtensions <> '' do
  begin
    Extension := GetListItem(RemainingExtensions);
    if IsAdminInstallMode then
    begin
      ConfigureActionMenuForHive(HKLM, Extension, MenuKey, LabelText, ActionId, PositionValue);
    end
    else
    begin
      ConfigureActionMenuForHive(HKCU, Extension, MenuKey, LabelText, ActionId, PositionValue);
    end;
  end;
end;

procedure InstallSelectedMenus;
begin
  CleanupContextMenuKeys;
  CleanupContextMenuAIKeys;

  if WizardIsComponentSelected('video\convert_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'convert_video',
      'Convert video',
      'convert-video',
      '');
  end;

  if WizardIsComponentSelected('audio\convert_audio') then
  begin
    ApplyActionMenuList(
      AudioExtensions,
      'convert_audio',
      'Convert audio',
      'convert-audio',
      '');
  end;

  if WizardIsComponentSelected('audio\cut_audio') then
  begin
    ApplyActionMenuList(
      AudioExtensions,
      'cut_audio',
      'Cut audio',
      'cut-audio',
      '');
  end;

  if WizardIsComponentSelected('video\remove_audio') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'remove_audio',
      'Remove audio',
      'remove-audio',
      '');
  end;

  if WizardIsComponentSelected('audio\reverse_audio') then
  begin
    ApplyActionMenuList(
      AudioExtensions,
      'reverse_audio',
      'Reverse audio',
      'reverse-audio',
      '');
  end;

  if WizardIsComponentSelected('audio\compress_audio') then
  begin
    ApplyActionMenuList(
      AudioExtensions,
      'compress_audio',
      'Compress audio',
      'compress-audio',
      '');
  end;

  if WizardIsComponentSelected('audio\change_pitch') then
  begin
    ApplyActionMenuList(
      AudioExtensions,
      'change_pitch',
      'Change pitch',
      'change-pitch',
      '');
  end;

  if WizardIsComponentSelected('audio\change_audio_speed') then
  begin
    ApplyActionMenuList(
      AudioExtensions,
      'change_audio_speed',
      'Change audio speed',
      'change-audio-speed',
      '');
  end;

  if WizardIsComponentSelected('video\change_video_speed') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'change_video_speed',
      'Change video speed',
      'change-video-speed',
      '');
  end;

  if WizardIsComponentSelected('video\extract_frames') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'extract_frames',
      'Extract frames',
      'extract-frames',
      '');
  end;

  if WizardIsComponentSelected('video\create_gif') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'create_gif',
      'Create GIF',
      'create-gif',
      '');
  end;

  if WizardIsComponentSelected('video\extract_audio') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'extract_audio',
      'Extract audio',
      'extract-audio',
      '');
  end;

  if WizardIsComponentSelected('video\cut_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'cut_video',
      'Cut video',
      'cut-video',
      '');
  end;

  if WizardIsComponentSelected('video\crop_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'crop_video',
      'Crop video',
      'crop-video',
      '');
  end;

  if WizardIsComponentSelected('video\resize_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'resize_video',
      'Resize video',
      'resize-video',
      '');
  end;

  if WizardIsComponentSelected('video\compress_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'compress_video',
      'Compress video',
      'compress-video',
      '');
  end;

  if WizardIsComponentSelected('video\interpolate_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'interpolate_video',
      'Interpolate video',
      'interpolate-video',
      '');
  end;

  if WizardIsComponentSelected('image\compress_image') then
  begin
    ApplyActionMenuList(
      ImageExtensions,
      'compress_image',
      'Compress image',
      'compress-image',
      '');
  end;

  if WizardIsComponentSelected('image\crop_image') then
  begin
    ApplyActionMenuList(
      ImageExtensions,
      'crop_image',
      'Crop image',
      'crop-image',
      '');
  end;

  if WizardIsComponentSelected('image\resize_image') then
  begin
    ApplyActionMenuList(
      ImageExtensions,
      'resize_image',
      'Resize image',
      'resize-image',
      '');
  end;

  if WizardIsComponentSelected('image\convert_image') then
  begin
    ApplyActionMenuList(
      ImageExtensions,
      'convert_image',
      'Convert image',
      'convert-image',
      '');
  end;

  if WizardIsComponentSelected('image\convert_icon') then
  begin
    ApplyActionMenuList(
      ImageExtensions,
      'convert_icon',
      'Convert to icon',
      'convert-to-icon',
      '');
  end;

  if WizardIsComponentSelected('image\image_to_pdf') then
  begin
    ApplyActionMenuList(
      PdfImageExtensions,
      'image_to_pdf',
      'Image to PDF',
      'image-to-pdf',
      '');
  end;

  if WizardIsComponentSelected('image\rotate_flip_image') then
  begin
    ApplyActionMenuList(
      ImageExtensions,
      'rotate_flip_image',
      'Rotate / Flip image',
      'rotate-flip-image',
      '');
  end;

  if WizardIsComponentSelected('video\rotate_flip_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'rotate_flip_video',
      'Rotate / Flip video',
      'rotate-flip-video',
      '');
  end;

  if WizardIsComponentSelected('tools\media_info') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'media_info',
      'Media Info',
      'media-info',
      '');
    ApplyActionMenuList(
      AudioExtensions,
      'media_info',
      'Media Info',
      'media-info',
      '');
    ApplyActionMenuList(
      ImageExtensions,
      'media_info',
      'Media Info',
      'media-info',
      '');
  end;

  if WizardIsComponentSelected('ai\remove_background\fast') or
     (WizardIsComponentSelected('ai\remove_background') and
      not WizardIsComponentSelected('ai\remove_background\high_resolution') and
      not WizardIsComponentSelected('ai\remove_background\high_resolution_general')) then
  begin
    ApplyAIActionMenuList(
      ImageExtensions,
      'remove_background_fast',
      'Remove Background (Fast)',
      'remove-background',
      ' --rmbg-model fast');
  end;

  if WizardIsComponentSelected('ai\remove_background\high_resolution') then
  begin
    ApplyAIActionMenuList(
      ImageExtensions,
      'remove_background_high_resolution',
      'Remove Background (High Resolution Matting)',
      'remove-background',
      ' --rmbg-model high-resolution');
  end;

  if WizardIsComponentSelected('ai\remove_background\high_resolution_general') then
  begin
    ApplyAIActionMenuList(
      ImageExtensions,
      'remove_background_high_resolution_general',
      'Remove Background (High Resolution General)',
      'remove-background',
      ' --rmbg-model high-resolution-general');
  end;

  if WizardIsComponentSelected('ai\remove_background\bria_balanced') then
  begin
    ApplyAIActionMenuList(
      ImageExtensions,
      'remove_background_bria_balanced',
      'Remove Background (BRIA Balanced)',
      'remove-background',
      ' --rmbg-model bria-balanced');
  end;

  if WizardIsComponentSelected('ai\remove_background\bria_high_quality') then
  begin
    ApplyAIActionMenuList(
      ImageExtensions,
      'remove_background_bria_high_quality',
      'Remove Background (BRIA High Quality)',
      'remove-background',
      ' --rmbg-model bria-high-quality');
  end;

  if WizardIsComponentSelected('ai\remove_noise') then
  begin
    ApplyAIActionMenuList(
      AudioExtensions,
      'remove_noise',
      'Remove noise',
      'remove-noise',
      '');
  end;

  if WizardIsComponentSelected('ai\remove_noise_video') then
  begin
    ApplyAIActionMenuList(
      VideoExtensions,
      'remove_noise_video',
      'Remove noise (video)',
      'remove-noise-video',
      '');
  end;

  if WizardIsComponentSelected('ai\separate_audio') then
  begin
    ApplyAIActionMenuList(
      AudioExtensions,
      'separate_audio',
      'Audio separation',
      'separate-audio',
      '');
  end;

  if WizardIsComponentSelected('ai\interpolate_video_rife') then
  begin
    ApplyAIActionMenuList(
      VideoExtensions,
      'interpolate_video_rife',
      'Interpolate video (RIFE)',
      'interpolate-video-rife',
      '');
  end;

  if WizardIsComponentSelected('ai\remove_object') then
  begin
    ApplyAIActionMenuList(
      ImageExtensions,
      'remove_object',
      'Remove object',
      'remove-object',
      '');
  end;

  if WizardIsComponentSelected('ai\upscale_image') then
  begin
    ApplyAIActionMenuList(
      ImageExtensions,
      'upscale_image',
      'Upscale Image 4x',
      'upscale-image',
      '');
  end;

  if WizardIsComponentSelected('ai\upscale_video') then
  begin
    ApplyAIActionMenuList(
      VideoExtensions,
      'upscale_video',
      'Upscale Video 4x',
      'upscale-video',
      '');
  end;
end;

function GetDefaultModelsDir(): string;
begin
  Result := ExpandConstant('{localappdata}\FrameShift\AI\Models');
end;

procedure AiModelsBrowseClick(Sender: TObject);
var
  Dir: string;
begin
  Dir := AiModelsDirEdit.Text;
  if Dir = '' then
    Dir := GetDefaultModelsDir();
  if BrowseForFolder('Select AI models folder', Dir, False) then
    AiModelsDirEdit.Text := Dir;
end;

procedure CreateAiModelsPage();
var
  DescLabel: TNewStaticText;
  DirLabel: TNewStaticText;
  EditPanel: TPanel;
begin
  AiModelsPage := CreateCustomPage(
    wpSelectComponents,
    'AI models folder',
    'Choose where FrameShift AI will store downloaded AI models.');

  DescLabel := TNewStaticText.Create(AiModelsPage);
  DescLabel.Parent := AiModelsPage.Surface;
  DescLabel.Left := ScaleX(0);
  DescLabel.Top := ScaleY(4);
  DescLabel.Width := AiModelsPage.SurfaceWidth;
  DescLabel.Height := ScaleY(32);
  DescLabel.AutoSize := False;
  DescLabel.WordWrap := True;
  DescLabel.Caption :=
    'Models are downloaded on first use and never included in the installer.' + #13#10 +
    'Leave the default to use ' + GetDefaultModelsDir() + '.';

  DirLabel := TNewStaticText.Create(AiModelsPage);
  DirLabel.Parent := AiModelsPage.Surface;
  DirLabel.Left := ScaleX(0);
  DirLabel.Top := DescLabel.Top + DescLabel.Height + ScaleY(10);
  DirLabel.Width := AiModelsPage.SurfaceWidth;
  DirLabel.Height := ScaleY(16);
  DirLabel.AutoSize := False;
  DirLabel.Caption := 'AI models folder:';

  AiModelsDirEdit := TEdit.Create(AiModelsPage);
  AiModelsDirEdit.Parent := AiModelsPage.Surface;
  AiModelsDirEdit.Left := ScaleX(0);
  AiModelsDirEdit.Top := DirLabel.Top + DirLabel.Height + ScaleY(4);
  AiModelsDirEdit.Width := AiModelsPage.SurfaceWidth - ScaleX(90);
  AiModelsDirEdit.Height := ScaleY(22);
  AiModelsDirEdit.Text := GetDefaultModelsDir();

  AiModelsDirBrowseButton := TButton.Create(AiModelsPage);
  AiModelsDirBrowseButton.Parent := AiModelsPage.Surface;
  AiModelsDirBrowseButton.Left := AiModelsDirEdit.Left + AiModelsDirEdit.Width + ScaleX(8);
  AiModelsDirBrowseButton.Top := AiModelsDirEdit.Top - ScaleY(1);
  AiModelsDirBrowseButton.Width := ScaleX(78);
  AiModelsDirBrowseButton.Height := ScaleY(24);
  AiModelsDirBrowseButton.Caption := 'Browse...';
  AiModelsDirBrowseButton.OnClick := @AiModelsBrowseClick;
end;

procedure WriteAiModelSettings(const ModelsDir: string);
var
  ConfigDir: string;
  ConfigFile: string;
  JsonContent: string;
  EscapedDir: string;
  I: Integer;
  C: Char;
begin
  if (ModelsDir = '') or (ModelsDir = GetDefaultModelsDir()) then
    exit;

  ConfigDir := ExpandConstant('{localappdata}\FrameShift\config');
  ConfigFile := ConfigDir + '\settings.json';

  if not ForceDirectories(ConfigDir) then
    exit;

  // Minimal JSON escaping for the path (backslash → \\)
  EscapedDir := '';
  for I := 1 to Length(ModelsDir) do
  begin
    C := ModelsDir[I];
    if C = '\' then
      EscapedDir := EscapedDir + '\\'
    else
      EscapedDir := EscapedDir + C;
  end;

  JsonContent :=
    '{' + #13#10 +
    '  "ModelsDirectory": "' + EscapedDir + '"' + #13#10 +
    '}';

  SaveStringToFile(ConfigFile, JsonContent, False);
end;

function GetEffectiveModelsDir(): string;
begin
  if AiModelsPage <> nil then
    Result := Trim(AiModelsDirEdit.Text)
  else
    Result := '';
  if Result = '' then
    Result := GetDefaultModelsDir();
end;

procedure CreateBriaModelAssets(
  const ModelsDir, SubFolder, ModelDisplayName, FileName, ApproxSize: string);
var
  Dir: string;
  Readme: string;
  License: string;
begin
  Dir := ModelsDir + '\RemoveBackground\' + SubFolder;
  if not ForceDirectories(Dir) then
    exit;

  Readme :=
    ModelDisplayName + #13#10 +
    '====================================' + #13#10#13#10 +
    'FrameShift does NOT distribute, bundle, mirror or download the BRIA RMBG-2.0 model.' + #13#10 +
    'This folder is a placeholder for a model file you must obtain yourself.' + #13#10#13#10 +
    'How to install:' + #13#10 +
    '  1. Open the official BRIA page:' + #13#10 +
    '       https://huggingface.co/briaai/RMBG-2.0/tree/main' + #13#10 +
    '  2. Review BRIA''s documentation and licensing.' + #13#10 +
    '  3. Download the ONNX model and place it in THIS folder.' + #13#10#13#10 +
    'Expected file:   ' + FileName + #13#10 +
    'Approximate size: ' + ApproxSize + #13#10 +
    'Target folder:    ' + Dir + #13#10#13#10 +
    'FrameShift verifies the file against the official BRIA checksum. If it does not' + #13#10 +
    'match, FrameShift will point you back to the official BRIA page.' + #13#10;
  SaveStringToFile(Dir + '\README.txt', Readme, False);

  License :=
    'BRIA RMBG-2.0 - License notice' + #13#10 +
    '====================================' + #13#10#13#10 +
    'The BRIA RMBG-2.0 model is distributed separately by BRIA AI.' + #13#10 +
    'FrameShift does not redistribute, bundle or host this model.' + #13#10#13#10 +
    'Usage of the model is governed solely by BRIA''s licensing terms' + #13#10 +
    '(non-commercial; CC BY-NC 4.0 at the time of writing).' + #13#10#13#10 +
    'You must review the official BRIA page and accept BRIA''s terms before use:' + #13#10 +
    '  https://huggingface.co/briaai/RMBG-2.0/tree/main' + #13#10;
  SaveStringToFile(Dir + '\LICENSE_NOTICE.txt', License, False);
end;

procedure CreateSelectedBriaAssets();
var
  ModelsDir: string;
begin
  ModelsDir := GetEffectiveModelsDir();

  if WizardIsComponentSelected('ai\remove_background\bria_balanced') then
    CreateBriaModelAssets(
      ModelsDir,
      'BriaBalanced',
      'Remove Background (BRIA Balanced)',
      'model_fp16.onnx',
      '~500 MB');

  if WizardIsComponentSelected('ai\remove_background\bria_high_quality') then
    CreateBriaModelAssets(
      ModelsDir,
      'BriaHighQuality',
      'Remove Background (BRIA High Quality)',
      'model.onnx',
      '~1 GB');
end;

procedure InitializeWizard();
begin
  HasInstalledVersion := FindInstalledVersion();
  if HasInstalledVersion then
  begin
    InstalledVersionState := CompareVersionStrings(InstalledVersion, '{#MyAppVersion}');
  end
  else
  begin
    InstalledVersionState := ExistingInstallStateSame;
  end;

  CreateAiModelsPage();

  ExistingInstallPage :=
    CreateCustomPage(
      wpWelcome,
      'Existing installation detected',
      'Choose how to continue with the detected FrameShift installation.');

  ExistingInstallStatusLabel := TNewStaticText.Create(ExistingInstallPage);
  ExistingInstallStatusLabel.Parent := ExistingInstallPage.Surface;
  ExistingInstallStatusLabel.Left := ScaleX(0);
  ExistingInstallStatusLabel.Top := ScaleY(8);
  ExistingInstallStatusLabel.Width := ExistingInstallPage.SurfaceWidth;
  ExistingInstallStatusLabel.Height := ScaleY(52);
  ExistingInstallStatusLabel.AutoSize := False;
  ExistingInstallStatusLabel.WordWrap := True;

  ExistingInstallInstallRadio := TNewRadioButton.Create(ExistingInstallPage);
  ExistingInstallInstallRadio.Parent := ExistingInstallPage.Surface;
  ExistingInstallInstallRadio.Left := ScaleX(0);
  ExistingInstallInstallRadio.Top := ExistingInstallStatusLabel.Top + ExistingInstallStatusLabel.Height + ScaleY(12);
  ExistingInstallInstallRadio.Width := ExistingInstallPage.SurfaceWidth;
  ExistingInstallInstallRadio.Height := ScaleY(24);
  ExistingInstallInstallRadio.Checked := True;

  ExistingInstallUninstallRadio := TNewRadioButton.Create(ExistingInstallPage);
  ExistingInstallUninstallRadio.Parent := ExistingInstallPage.Surface;
  ExistingInstallUninstallRadio.Left := ScaleX(0);
  ExistingInstallUninstallRadio.Top := ExistingInstallInstallRadio.Top + ExistingInstallInstallRadio.Height + ScaleY(8);
  ExistingInstallUninstallRadio.Width := ExistingInstallPage.SurfaceWidth;
  ExistingInstallUninstallRadio.Height := ScaleY(24);

  UpdateExistingInstallPageContent();
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (ExistingInstallPage <> nil) and (PageID = ExistingInstallPage.ID) then
  begin
    Result := not HasInstalledVersion;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if (ExistingInstallPage <> nil) and (CurPageID = ExistingInstallPage.ID) then
  begin
    if ExistingInstallUninstallRadio.Checked then
    begin
      ExistingInstallSelectedAction := ExistingInstallActionUninstall;
    end
    else
    begin
      ExistingInstallSelectedAction := ExistingInstallActionInstall;
    end;

    if ExistingInstallSelectedAction = ExistingInstallActionUninstall then
    begin
      if not TryRunInstalledUninstaller() then
      begin
        MsgBox('Unable to start the installed FrameShift uninstaller.', mbError, MB_OK);
      end;

      Result := False;
      ExitProcess(0);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    InstallSelectedMenus;
    SHChangeNotify($08000000, $0000, 0, 0);
    if AiModelsPage <> nil then
      WriteAiModelSettings(Trim(AiModelsDirEdit.Text));
    CreateSelectedBriaAssets();
  end;
end;

function ReadModelsDirectoryFromSettings(): string;
var
  ConfigFile: string;
  JsonText: AnsiString;
  Tail: string;
  KeyPos: Integer;
  QuotePos: Integer;
  EndPos: Integer;
begin
  Result := '';
  ConfigFile := ExpandConstant('{localappdata}\FrameShift\config\settings.json');
  if not FileExists(ConfigFile) then
    exit;
  if not LoadStringFromFile(ConfigFile, JsonText) then
    exit;
  // Locate the key
  KeyPos := Pos('"ModelsDirectory"', String(JsonText));
  if KeyPos = 0 then
    exit;
  // Slice everything after the key
  Tail := Copy(String(JsonText), KeyPos + Length('"ModelsDirectory"'), MaxInt);
  // Find the opening quote of the value (skip past colon/spaces)
  QuotePos := Pos('"', Tail);
  if QuotePos = 0 then
    exit;
  Tail := Copy(Tail, QuotePos + 1, MaxInt);
  // Find the closing quote
  EndPos := Pos('"', Tail);
  if EndPos = 0 then
    exit;
  Result := Copy(Tail, 1, EndPos - 1);
  // Unescape \\ → \
  Result := StringChangeEx(Result, '\\', '\', True);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ModelsDir: string;
  CustomModelsDir: string;
  AIDir: string;
  LogsDir: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    CleanupContextMenuKeys;
    CleanupContextMenuAIKeys;
    SHChangeNotify($08000000, $0000, 0, 0);

    // Use custom models dir from settings if present, otherwise the default
    ModelsDir := ExpandConstant('{localappdata}\FrameShift\AI\Models');
    CustomModelsDir := ReadModelsDirectoryFromSettings();
    if (CustomModelsDir <> '') and (CustomModelsDir <> ModelsDir) then
      ModelsDir := CustomModelsDir;

    if DirExists(ModelsDir) then
    begin
      if MsgBox(
        'Do you also want to delete downloaded AI models?' + #13#10#13#10 +
        'Location: ' + ModelsDir,
        mbConfirmation,
        MB_YESNO) = IDYES then
      begin
        DelTree(ModelsDir, True, True, True);
        AIDir := ExpandConstant('{localappdata}\FrameShift\AI');
        RemoveDir(AIDir);
      end;
    end;

    LogsDir := ExpandConstant('{localappdata}\FrameShift\logs');
    if DirExists(LogsDir) then
    begin
      if MsgBox(
        'Do you also want to delete diagnostic logs?' + #13#10#13#10 +
        'Location: ' + LogsDir,
        mbConfirmation,
        MB_YESNO) = IDYES then
      begin
        DelTree(LogsDir, True, True, True);
      end;
    end;
  end;
end;
