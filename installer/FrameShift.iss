#define MyAppName "FrameShift"
#define MyAppId "FrameShift"
#ifndef MyAppVersion
#error MyAppVersion must be passed via /DMyAppVersion=x.y.z — run build_installer.ps1 instead of compiling directly
#endif
#define MyAppPublisher "FrameShift"
#define MyAppExeName "FrameShift.exe"
#ifndef PublishOutputDir
#define PublishOutputDir "..\publish\FrameShift-win-x64"
#endif
#define AppPayloadDir PublishOutputDir
#define AssetsDir "..\src\FrameShift\Assets"
#define ToolsDir "..\src\FrameShift\Tools"

[Setup]
AppName={#MyAppName}
AppId={#MyAppId}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
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
LicenseFile={#AppPayloadDir}\licenses\LICENSE

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
Name: "ai\create_subtitles_audio"; Description: "Create subtitle file (audio)"; Types: complete custom
Name: "ai\create_subtitles_video"; Description: "Create subtitle file (video)"; Types: complete custom
Name: "video"; Description: "Video actions"; Types: complete custom
Name: "video\convert_video"; Description: "Convert video"; Types: complete custom
Name: "video\remove_audio"; Description: "Remove audio"; Types: complete custom
Name: "video\extract_frames"; Description: "Extract all and specific frames"; Types: complete custom
Name: "video\create_gif"; Description: "Create GIF"; Types: complete custom
Name: "video\add_subtitles_video"; Description: "Add subtitles to video"; Types: complete custom
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
Source: "{#AppPayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "Tools\ffmpeg\*,Workers\CreateSubtitlesWorker\*,logs\*,*.pdb,createdump.exe,mscordaccore*.dll,mscordbi.dll,onnxruntime.lib,DirectML.Debug.dll,DirectML.Debug.pdb"; Components: core
Source: "{#AppPayloadDir}\Workers\CreateSubtitlesWorker\*"; DestDir: "{app}\Workers\CreateSubtitlesWorker"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,createdump.exe,mscordaccore*.dll,mscordbi.dll,onnxruntime.lib,DirectML.Debug.dll,DirectML.Debug.pdb"; Components: core
Source: "{#AppPayloadDir}\Tools\ffmpeg\ffmpeg.exe"; DestDir: "{app}\Tools\ffmpeg"; Flags: ignoreversion; Components: core
Source: "{#AppPayloadDir}\Tools\ffmpeg\ffprobe.exe"; DestDir: "{app}\Tools\ffmpeg"; Flags: ignoreversion; Components: core

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
  ModelDirectoryMarkerFileName = '.frameshift-ai-model-directory';
  ModelDirectoryMarkerContent = 'FrameShift AI model directory v1';
  FileAttributeReparsePoint = $00000400;
  FileAttributeDirectory = $00000010;
  InvalidFileAttributes = $FFFFFFFF;

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
  else if (MenuKey = 'create_subtitles_audio') or (MenuKey = 'create_subtitles_video') then
  begin
    IconPath := ExpandConstant('{app}\Assets\Icons\ai\create_subtitles.ico');
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
  if MenuKey = 'add_subtitles_video' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\ai\add_subtitles_video.ico');
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

procedure ConfigureSpecificExtractFrameMenuForHive(
  const Hive: Integer;
  const SpecificKeyPath, MenuKey, LabelText, FrameMode, IconPath: string);
var
  CommandValue: string;
  KeyPath: string;
begin
  KeyPath := SpecificKeyPath + '\shell\' + MenuKey;
  RegWriteStringValue(Hive, KeyPath, 'MUIVerb', LabelText);
  if IconPath <> '' then
  begin
    RegWriteStringValue(Hive, KeyPath, 'Icon', IconPath);
  end;

  CommandValue :=
    '"' + ExpandConstant('{app}\{#MyAppExeName}') + '"' +
    ' --action extract-frames --frame-mode ' + FrameMode + ' "%1"';
  RegWriteStringValue(Hive, KeyPath + '\command', '', CommandValue);
end;

procedure ConfigureExtractFramesMenusForHive(const Hive: Integer; const Ext: string);
var
  BaseKeyPath: string;
  DirectKeyPath: string;
  SpecificKeyPath: string;
  CommandValue: string;
  IconPath: string;
begin
  EnsureFrameShiftRootForHive(Hive, Ext);

  BaseKeyPath := 'Software\Classes\SystemFileAssociations\' + Ext + '\shell\FrameShift\shell\';
  IconPath := GetMenuIconPath('extract_frames');

  DirectKeyPath := BaseKeyPath + 'extract_all_frames';
  RegDeleteKeyIncludingSubkeys(Hive, DirectKeyPath);
  RegWriteStringValue(Hive, DirectKeyPath, 'MUIVerb', 'Extract all frames');
  if IconPath <> '' then
  begin
    RegWriteStringValue(Hive, DirectKeyPath, 'Icon', IconPath);
  end;
  CommandValue :=
    '"' + ExpandConstant('{app}\{#MyAppExeName}') + '"' +
    ' --action extract-frames --frame-mode all "%1"';
  RegWriteStringValue(Hive, DirectKeyPath + '\command', '', CommandValue);

  SpecificKeyPath := BaseKeyPath + 'extract_specific_frames';
  RegDeleteKeyIncludingSubkeys(Hive, SpecificKeyPath);
  RegWriteStringValue(Hive, SpecificKeyPath, 'MUIVerb', 'Extract specific frames');
  RegWriteStringValue(
    Hive,
    SpecificKeyPath,
    'ExtendedSubCommandsKey',
    'SystemFileAssociations\' + Ext + '\shell\FrameShift\shell\extract_specific_frames');
  if IconPath <> '' then
  begin
    RegWriteStringValue(Hive, SpecificKeyPath, 'Icon', IconPath);
  end;

  ConfigureSpecificExtractFrameMenuForHive(Hive, SpecificKeyPath, 'first_frame', 'First frame', 'first', IconPath);
  ConfigureSpecificExtractFrameMenuForHive(Hive, SpecificKeyPath, 'last_frame', 'Last frame', 'last', IconPath);
  ConfigureSpecificExtractFrameMenuForHive(Hive, SpecificKeyPath, 'keyframes', 'Keyframes', 'keyframes', IconPath);
end;

procedure ApplyExtractFramesMenuList(const Extensions: string);
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
      ConfigureExtractFramesMenusForHive(HKLM, Extension);
    end
    else
    begin
      ConfigureExtractFramesMenusForHive(HKCU, Extension);
    end;
  end;
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
    ApplyExtractFramesMenuList(VideoExtensions);
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

  if WizardIsComponentSelected('video\add_subtitles_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'add_subtitles_video',
      'Add subtitles to video',
      'add-subtitles-video',
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

  if WizardIsComponentSelected('ai\create_subtitles_audio') then
  begin
    ApplyAIActionMenuList(
      AudioExtensions,
      'create_subtitles_audio',
      'Create Subtitle File',
      'create-subtitles-audio',
      '');
  end;

  if WizardIsComponentSelected('ai\create_subtitles_video') then
  begin
    ApplyAIActionMenuList(
      VideoExtensions,
      'create_subtitles_video',
      'Create Subtitle File',
      'create-subtitles-video',
      '');
  end;
end;

function GetDefaultModelsDir(): string;
begin
  Result := ExpandConstant('{localappdata}\FrameShift\AI\Models');
end;

function ReadModelsDirectoryFromSettings(): string; forward;

function IsAbsoluteWindowsPath(const Value: string): Boolean;
begin
  Result :=
    ((Length(Value) >= 3) and (Value[2] = ':') and (Value[3] = '\')) or
    (Copy(Value, 1, 2) = '\\');
end;

function IsDriveRoot(const Value: string): Boolean;
begin
  Result := (Length(Value) = 3) and (Value[2] = ':') and (Value[3] = '\');
end;

function IsUncShareRoot(const Value: string): Boolean;
var
  Remainder: string;
  ServerSeparator: Integer;
  ShareSeparator: Integer;
begin
  Result := False;
  if Copy(Value, 1, 2) <> '\\' then
    exit;

  Remainder := Copy(Value, 3, MaxInt);
  ServerSeparator := Pos('\', Remainder);
  if ServerSeparator = 0 then
  begin
    Result := True;
    exit;
  end;

  Remainder := Copy(Remainder, ServerSeparator + 1, MaxInt);
  ShareSeparator := Pos('\', Remainder);
  if ShareSeparator = 0 then
  begin
    Result := True;
    exit;
  end;

  Result := Trim(Copy(Remainder, ShareSeparator + 1, MaxInt)) = '';
end;

function PathIsSameOrChild(const Candidate, Parent: string): Boolean;
var
  ParentWithSlash: string;
begin
  Result := CompareText(Candidate, Parent) = 0;
  if Result then
    exit;

  ParentWithSlash := AddBackslash(Parent);
  Result := CompareText(
    Copy(Candidate, 1, Length(ParentWithSlash)),
    ParentWithSlash) = 0;
end;

function TryNormalizeSafeModelsDir(const Value: string; var Normalized: string): Boolean;
var
  Candidate: string;
  DefaultModelsDir: string;
  UserProfileDir: string;
  WindowsDir: string;
  ProgramFilesDir: string;
  ProgramFilesX86Dir: string;
begin
  Result := False;
  Normalized := '';
  if not IsAbsoluteWindowsPath(Trim(Value)) then
    exit;

  Candidate := RemoveBackslashUnlessRoot(ExpandFileName(Trim(Value)));
  if IsDriveRoot(Candidate) or IsUncShareRoot(Candidate) then
    exit;

  DefaultModelsDir := RemoveBackslashUnlessRoot(ExpandFileName(GetDefaultModelsDir()));
  if CompareText(Candidate, DefaultModelsDir) = 0 then
  begin
    Normalized := Candidate;
    Result := True;
    exit;
  end;

  UserProfileDir := RemoveBackslashUnlessRoot(GetEnv('USERPROFILE'));
  WindowsDir := RemoveBackslashUnlessRoot(ExpandConstant('{win}'));
  ProgramFilesDir := RemoveBackslashUnlessRoot(ExpandConstant('{autopf}'));
  ProgramFilesX86Dir := RemoveBackslashUnlessRoot(ExpandConstant('{autopf32}'));

  if PathIsSameOrChild(DefaultModelsDir, Candidate) or
     ((UserProfileDir <> '') and PathIsSameOrChild(UserProfileDir, Candidate)) or
     PathIsSameOrChild(Candidate, WindowsDir) or
     PathIsSameOrChild(WindowsDir, Candidate) or
     PathIsSameOrChild(Candidate, ProgramFilesDir) or
     PathIsSameOrChild(ProgramFilesDir, Candidate) or
     PathIsSameOrChild(Candidate, ProgramFilesX86Dir) or
     PathIsSameOrChild(ProgramFilesX86Dir, Candidate) then
    exit;

  Normalized := Candidate;
  Result := True;
end;

function IsModelsDirOutsideSelectedAppDir(const Candidate: string): Boolean;
var
  SelectedAppDir: string;
begin
  SelectedAppDir := RemoveBackslashUnlessRoot(Trim(WizardDirValue));
  Result := (SelectedAppDir = '') or
    (not PathIsSameOrChild(Candidate, SelectedAppDir) and
     not PathIsSameOrChild(SelectedAppDir, Candidate));
end;

function GetSuggestedModelsDir(): string;
var
  DefaultModelsDir: string;
  CustomModelsDir: string;
  SafeCustomModelsDir: string;
begin
  DefaultModelsDir := GetDefaultModelsDir();
  CustomModelsDir := ReadModelsDirectoryFromSettings();

  if TryNormalizeSafeModelsDir(CustomModelsDir, SafeCustomModelsDir) and DirExists(SafeCustomModelsDir) then
  begin
    Result := SafeCustomModelsDir;
    exit;
  end;

  if DirExists(DefaultModelsDir) then
  begin
    Result := DefaultModelsDir;
    exit;
  end;

  if TryNormalizeSafeModelsDir(CustomModelsDir, SafeCustomModelsDir) then
  begin
    Result := SafeCustomModelsDir;
    exit;
  end;

  Result := DefaultModelsDir;
end;

procedure AiModelsBrowseClick(Sender: TObject);
var
  Dir: string;
begin
  Dir := AiModelsDirEdit.Text;
  if Dir = '' then
    Dir := GetSuggestedModelsDir();
  if BrowseForFolder('Select AI models folder', Dir, False) then
    AiModelsDirEdit.Text := Dir;
end;

procedure CreateAiModelsPage();
var
  DescLabel: TNewStaticText;
  DirLabel: TNewStaticText;
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
    'AI models are downloaded on first use and are never included in the installer.' + #13#10 +
    'Runtime components such as the subtitle worker are bundled with FrameShift.';

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
  AiModelsDirEdit.Text := GetSuggestedModelsDir();

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
  SafeModelsDir: string;
begin
  ConfigDir := ExpandConstant('{localappdata}\FrameShift\config');
  ConfigFile := ConfigDir + '\settings.json';

  if not ForceDirectories(ConfigDir) then
    exit;

  if not TryNormalizeSafeModelsDir(ModelsDir, SafeModelsDir) or
     (SafeModelsDir = GetDefaultModelsDir()) then
  begin
    SaveStringToFile(ConfigFile, '{}' + #13#10, False);
    exit;
  end;

  // Minimal JSON escaping for the path (backslash → \\)
  EscapedDir := '';
  for I := 1 to Length(SafeModelsDir) do
  begin
    C := SafeModelsDir[I];
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
var
  SafeModelsDir: string;
begin
  if AiModelsPage <> nil then
    Result := Trim(AiModelsDirEdit.Text)
  else
    Result := '';
  if not TryNormalizeSafeModelsDir(Result, SafeModelsDir) then
    Result := GetDefaultModelsDir();
  if SafeModelsDir <> '' then
    Result := SafeModelsDir;
end;

procedure CreateBriaModelAssets(
  const ModelsDir, SubFolder, ModelDisplayName, FileName, ApproxSize: string);
var
  Dir: string;
  Readme: string;
  License: string;
  DirectoryAlreadyExisted: Boolean;
begin
  Dir := ModelsDir + '\RemoveBackground\' + SubFolder;
  DirectoryAlreadyExisted := DirExists(Dir);
  if not ForceDirectories(Dir) then
    exit;

  if not DirectoryAlreadyExisted then
    SaveStringToFile(
      AddBackslash(Dir) + ModelDirectoryMarkerFileName,
      ModelDirectoryMarkerContent + #13#10,
      False);

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
var
  SafeModelsDir: string;
begin
  Result := True;

  if (AiModelsPage <> nil) and (CurPageID = AiModelsPage.ID) then
  begin
    if not TryNormalizeSafeModelsDir(AiModelsDirEdit.Text, SafeModelsDir) or
       not IsModelsDirOutsideSelectedAppDir(SafeModelsDir) then
    begin
      MsgBox(
        'Choose a dedicated AI models folder. Drive roots, user-profile roots, Windows, Program Files, the FrameShift install folder and their parents are not allowed.',
        mbError,
        MB_OK);
      Result := False;
      exit;
    end;

    AiModelsDirEdit.Text := SafeModelsDir;
  end;

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

function GetFileAttributes(const FileName: string): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function IsReparsePoint(const DirectoryName: string): Boolean;
var
  Attributes: Cardinal;
begin
  Attributes := GetFileAttributes(DirectoryName);
  Result := (Attributes <> InvalidFileAttributes) and
            ((Attributes and FileAttributeReparsePoint) <> 0);
end;

function HasReparsePointInPath(const DirectoryName, ModelsDir: string): Boolean;
var
  CurrentDirectory: string;
  ParentDirectory: string;
begin
  // Fail closed if the generated candidate ever falls outside the configured root.
  Result := True;
  CurrentDirectory := RemoveBackslashUnlessRoot(ExpandFileName(DirectoryName));

  while PathIsSameOrChild(CurrentDirectory, ModelsDir) do
  begin
    if IsReparsePoint(CurrentDirectory) then
      exit;

    if CompareText(CurrentDirectory, ModelsDir) = 0 then
    begin
      Result := False;
      exit;
    end;

    ParentDirectory := RemoveBackslashUnlessRoot(ExtractFileDir(CurrentDirectory));
    if CompareText(ParentDirectory, CurrentDirectory) = 0 then
      exit;

    CurrentDirectory := ParentDirectory;
  end;
end;

function IsOwnedModelDirectory(const DirectoryName: string): Boolean;
var
  MarkerContent: AnsiString;
begin
  Result := False;
  if IsReparsePoint(DirectoryName) then
    exit;
  if not LoadStringFromFile(
    AddBackslash(DirectoryName) + ModelDirectoryMarkerFileName,
    MarkerContent) then
    exit;

  Result := CompareText(Trim(String(MarkerContent)), ModelDirectoryMarkerContent) = 0;
end;

function IsFileNameInList(const FileName, FileList: string): Boolean;
var
  Remaining: string;
  ExpectedName: string;
begin
  Result := False;
  Remaining := FileList;
  while Remaining <> '' do
  begin
    ExpectedName := GetListItem(Remaining);
    if (CompareText(FileName, ExpectedName) = 0) or
       (CompareText(FileName, ExpectedName + '.tmp') = 0) then
    begin
      Result := True;
      exit;
    end;
  end;
end;

function GetExpectedModelFileList(const RelativePath: string): string;
begin
  Result := '';
  if CompareText(RelativePath, 'birefnet_lite-onnx') = 0 then
    Result := 'model_fp16.onnx'
  else if CompareText(RelativePath, 'birefnet_hr-matting-onnx') = 0 then
    Result := 'BiRefNet_HR-matting-epoch_135.onnx'
  else if CompareText(RelativePath, 'birefnet_hr-general-onnx') = 0 then
    Result := 'BiRefNet_HR-general-epoch_130.onnx'
  else if CompareText(RelativePath, 'RemoveBackground\BriaBalanced') = 0 then
    Result := 'model_fp16.onnx,README.txt,LICENSE_NOTICE.txt'
  else if CompareText(RelativePath, 'RemoveBackground\BriaHighQuality') = 0 then
    Result := 'model.onnx,README.txt,LICENSE_NOTICE.txt'
  else if CompareText(RelativePath, 'htdemucs') = 0 then
    Result := 'htdemucs.onnx'
  else if CompareText(RelativePath, 'htdemucs-split') = 0 then
    Result := 'htdemucs_split.onnx'
  else if CompareText(RelativePath, 'deepfilternet3_onnx') = 0 then
    Result := 'config.ini,enc.onnx,erb_dec.onnx,df_dec.onnx'
  else if CompareText(RelativePath, 'rife') = 0 then
    Result := 'rife_v425_lite.onnx,rife_v426_x2.onnx'
  else if CompareText(RelativePath, 'lama-onnx') = 0 then
    Result := 'lama_fp32.onnx'
  else if CompareText(RelativePath, 'lama-opencv-onnx') = 0 then
    Result := 'inpainting_lama_2025jan.onnx'
  else if CompareText(RelativePath, 'upscale-image-onnx') = 0 then
    Result := 'realesrgan_x4plus_fp16.onnx,realesrgan_x4plus_anime_6b.onnx,swin2sr_realworld_x4.onnx'
  else if CompareText(RelativePath, 'upscale-video-onnx') = 0 then
    Result := 'realesr_general_x4v3.onnx,realesr_animevideov3.onnx,realesr_animevideov3_x2.onnx,realesr_animevideov3_x3.onnx,realesrgan_x4plus_fp16.onnx'
  else if CompareText(RelativePath, 'whisper-base-onnx') = 0 then
    Result := 'base-encoder.onnx,base-decoder.onnx,base-tokens.txt'
  else if CompareText(RelativePath, 'whisper-small-onnx') = 0 then
    Result := 'small-encoder.onnx,small-decoder.onnx,small-tokens.txt'
  else if CompareText(RelativePath, 'whisper-large-v3-turbo-onnx') = 0 then
    Result := 'turbo-encoder.onnx,turbo-decoder.onnx,turbo-tokens.txt,turbo-encoder.weights';
end;

function IsExpectedModelFile(const RelativePath, FileName: string): Boolean;
begin
  Result := CompareText(FileName, ModelDirectoryMarkerFileName) = 0;
  if Result then
    exit;

  Result := IsFileNameInList(FileName, GetExpectedModelFileList(RelativePath));
end;

function ContainsOnlyExpectedModelFiles(const DirectoryName, RelativePath: string): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not FindFirst(AddBackslash(DirectoryName) + '*', FindRec) then
  begin
    Result := True;
    exit;
  end;

  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        if ((FindRec.Attributes and FileAttributeDirectory) <> 0) or
           not IsExpectedModelFile(RelativePath, FindRec.Name) then
          exit;
      end;
    until not FindNext(FindRec);
    Result := True;
  finally
    FindClose(FindRec);
  end;
end;

function CanDeleteOwnedModelDirectory(
  const ModelsDir, DirectoryName, RelativePath: string): Boolean;
begin
  Result := not HasReparsePointInPath(DirectoryName, ModelsDir) and
            IsOwnedModelDirectory(DirectoryName) and
            ContainsOnlyExpectedModelFiles(DirectoryName, RelativePath);
end;

function DeleteOwnedModelDirectoryFiles(
  const ModelsDir, DirectoryName, RelativePath: string): Boolean;
var
  RemainingFiles: string;
  FileName: string;
  FilePath: string;
begin
  // Do not call DelTree here. A file or subdirectory added after the safety check
  // must remain in place rather than becoming part of a recursive deletion.
  Result := False;
  RemainingFiles := GetExpectedModelFileList(RelativePath);
  if (RemainingFiles = '') or
     not CanDeleteOwnedModelDirectory(ModelsDir, DirectoryName, RelativePath) then
    exit;

  while RemainingFiles <> '' do
  begin
    if not CanDeleteOwnedModelDirectory(ModelsDir, DirectoryName, RelativePath) then
      exit;

    FileName := GetListItem(RemainingFiles);
    FilePath := AddBackslash(DirectoryName) + FileName;
    if FileExists(FilePath) and not DeleteFile(FilePath) then
      exit;
    if FileExists(FilePath + '.tmp') and not DeleteFile(FilePath + '.tmp') then
      exit;
  end;

  if not CanDeleteOwnedModelDirectory(ModelsDir, DirectoryName, RelativePath) then
    exit;

  FilePath := AddBackslash(DirectoryName) + ModelDirectoryMarkerFileName;
  if FileExists(FilePath) and not DeleteFile(FilePath) then
    exit;

  // RemoveDir is non-recursive. If any foreign content appeared while uninstalling,
  // this call fails closed and the remaining directory is preserved.
  Result := (not DirExists(DirectoryName)) or RemoveDir(DirectoryName);
end;

function GetKnownModelDirectories(): string;
begin
  Result :=
    'birefnet_lite-onnx,birefnet_hr-matting-onnx,birefnet_hr-general-onnx,' +
    'RemoveBackground\BriaBalanced,RemoveBackground\BriaHighQuality,' +
    'htdemucs,htdemucs-split,deepfilternet3_onnx,rife,lama-onnx,lama-opencv-onnx,' +
    'upscale-image-onnx,upscale-video-onnx,whisper-base-onnx,whisper-small-onnx,' +
    'whisper-large-v3-turbo-onnx';
end;

function HasOwnedModelDirectories(const ModelsDir: string): Boolean;
var
  Directories: string;
  RelativePath: string;
  Candidate: string;
begin
  Result := False;
  Directories := GetKnownModelDirectories();
  while Directories <> '' do
  begin
    RelativePath := GetListItem(Directories);
    Candidate := AddBackslash(ModelsDir) + RelativePath;
    if CanDeleteOwnedModelDirectory(ModelsDir, Candidate, RelativePath) then
    begin
      Result := True;
      exit;
    end;
  end;
end;

procedure DeleteOwnedModelDirectories(const ModelsDir: string);
var
  Directories: string;
  RelativePath: string;
  Candidate: string;
begin
  Directories := GetKnownModelDirectories();
  while Directories <> '' do
  begin
    RelativePath := GetListItem(Directories);
    Candidate := AddBackslash(ModelsDir) + RelativePath;
    if CanDeleteOwnedModelDirectory(ModelsDir, Candidate, RelativePath) then
      DeleteOwnedModelDirectoryFiles(ModelsDir, Candidate, RelativePath);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ModelsDir: string;
  CustomModelsDir: string;
  SafeModelsDir: string;
  LogsDir: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    CleanupContextMenuKeys;
    CleanupContextMenuAIKeys;
    SHChangeNotify($08000000, $0000, 0, 0);

    // settings.json is user-controlled. An unsafe custom path is ignored and never
    // becomes a deletion target.
    ModelsDir := GetDefaultModelsDir();
    CustomModelsDir := ReadModelsDirectoryFromSettings();
    if TryNormalizeSafeModelsDir(CustomModelsDir, SafeModelsDir) then
      ModelsDir := SafeModelsDir;

    if DirExists(ModelsDir) and HasOwnedModelDirectories(ModelsDir) then
    begin
      if MsgBox(
        'Do you also want to remove downloaded AI models created by FrameShift?' + #13#10#13#10 +
        'FrameShift will delete only marked model folders that it created. The selected models root and any other files will be kept.' + #13#10#13#10 +
        'Location: ' + ModelsDir,
        mbConfirmation,
        MB_YESNO) = IDYES then
      begin
        DeleteOwnedModelDirectories(ModelsDir);
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
