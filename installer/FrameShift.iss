#define InstallerSpace " "
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
ShowLanguageDialog=yes
LicenseFile={#AppPayloadDir}\licenses\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "spanish"; MessagesFile: "Languages\Spanish.isl"

[Types]
Name: "complete"; Description: "{cm:Text_1fe0d37d15}"
Name: "custom"; Description: "{cm:Text_00500efca7}"; Flags: iscustom

[Components]
Name: "core"; Description: "{cm:Text_aeac198900}"; Types: complete custom; Flags: fixed
Name: "ai"; Description: "FrameShift AI"; Types: complete custom
Name: "ai\remove_background"; Description: "{cm:Text_0e8f9035c9}"; Types: complete custom
Name: "ai\remove_background\fast"; Description: "{cm:Text_6217537829}"; Types: complete custom
Name: "ai\remove_background\high_resolution"; Description: "{cm:Text_5470dd0c9a}"; Types: custom
Name: "ai\remove_background\high_resolution_general"; Description: "{cm:Text_bc2498e36c}"; Types: custom
Name: "ai\remove_background\bria_balanced"; Description: "{cm:Text_4608c5ee1e}"; Types: custom
Name: "ai\remove_background\bria_high_quality"; Description: "{cm:Text_69e1fb5aac}"; Types: custom
Name: "ai\remove_noise"; Description: "{cm:Text_9bc1f7c1e3}"; Types: complete custom
Name: "ai\remove_noise_video"; Description: "{cm:Text_125fce9bb3}"; Types: complete custom
Name: "ai\separate_audio"; Description: "{cm:Text_3d51c42051}"; Types: complete custom
Name: "ai\interpolate_video_rife"; Description: "{cm:Text_683021e507}"; Types: complete custom
Name: "ai\remove_object"; Description: "{cm:Text_30fe992bda}"; Types: complete custom
Name: "ai\upscale_image"; Description: "{cm:Text_f05887f634}"; Types: complete custom
Name: "ai\upscale_video"; Description: "{cm:Text_4a13677024}"; Types: complete custom
Name: "ai\create_subtitles_audio"; Description: "{cm:Text_35af0bd25c}"; Types: complete custom
Name: "ai\create_subtitles_video"; Description: "{cm:Text_13f68875ed}"; Types: complete custom
Name: "ai\extract_text"; Description: "{cm:Text_e34e5a91aa}"; Types: complete custom
Name: "video"; Description: "{cm:Text_1446a2327c}"; Types: complete custom
Name: "video\convert_video"; Description: "{cm:Text_0b156b9372}"; Types: complete custom
Name: "video\remove_audio"; Description: "{cm:Text_9376f309e8}"; Types: complete custom
Name: "video\extract_frames"; Description: "{cm:Text_d337414dc5}"; Types: complete custom
Name: "video\create_gif"; Description: "{cm:Text_0cc6a31c0b}"; Types: complete custom
Name: "video\add_subtitles_video"; Description: "{cm:Text_6bfb10b252}"; Types: complete custom
Name: "video\join_videos"; Description: "{cm:Text_3ec0956fdc}"; Types: complete custom
Name: "video\extract_audio"; Description: "{cm:Text_4beb0703b6}"; Types: complete custom
Name: "video\cut_video"; Description: "{cm:Text_dd001b9053}"; Types: complete custom
Name: "video\crop_video"; Description: "{cm:Text_c11a926c82}"; Types: complete custom
Name: "video\rotate_flip_video"; Description: "{cm:Text_ca6a2bc920}"; Types: complete custom
Name: "video\resize_video"; Description: "{cm:Text_1fd793ab6c}"; Types: complete custom
Name: "video\compress_video"; Description: "{cm:Text_5f2aa68583}"; Types: complete custom
Name: "video\interpolate_video"; Description: "{cm:Text_0db17fe2ac}"; Types: complete custom
Name: "audio"; Description: "{cm:Text_5edf60f0a6}"; Types: complete custom
Name: "audio\cut_audio"; Description: "{cm:Text_5f5dd9b194}"; Types: complete custom
Name: "audio\convert_audio"; Description: "{cm:Text_700c7653c2}"; Types: complete custom
Name: "audio\reverse_audio"; Description: "{cm:Text_5671164ebd}"; Types: complete custom
Name: "audio\compress_audio"; Description: "{cm:Text_2aeb7305fb}"; Types: complete custom
Name: "audio\change_pitch"; Description: "{cm:Text_40b1b7f1f2}"; Types: complete custom
Name: "audio\change_audio_speed"; Description: "{cm:Text_fa5e8be522}"; Types: complete custom
Name: "video\change_video_speed"; Description: "{cm:Text_ccb39ab460}"; Types: complete custom
Name: "image"; Description: "{cm:Text_d2028a7d74}"; Types: complete custom
Name: "image\image_to_pdf"; Description: "{cm:Text_5b772970d6}"; Types: complete custom
Name: "image\convert_image"; Description: "{cm:Text_0c5e85a9ef}"; Types: complete custom
Name: "image\compress_image"; Description: "{cm:Text_aefcefd57e}"; Types: complete custom
Name: "image\convert_icon"; Description: "{cm:Text_5122ef45cd}"; Types: complete custom
Name: "image\crop_image"; Description: "{cm:Text_dbf83a261e}"; Types: complete custom
Name: "image\resize_image"; Description: "{cm:Text_491e690264}"; Types: complete custom
Name: "image\rotate_flip_image"; Description: "{cm:Text_d32c9fc51b}"; Types: complete custom
Name: "tools"; Description: "{cm:Text_dcb23c7eb5}"; Types: complete custom
Name: "tools\media_info"; Description: "{cm:Text_0fc6e5a501}"; Types: complete custom

[Files]
Source: "{#AppPayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "Tools\ffmpeg\*,Workers\CreateSubtitlesWorker\*,logs\*,*.pdb,createdump.exe,mscordaccore*.dll,mscordbi.dll,onnxruntime.lib,DirectML.Debug.dll,DirectML.Debug.pdb"; Components: core
Source: "{#AppPayloadDir}\Workers\CreateSubtitlesWorker\*"; DestDir: "{app}\Workers\CreateSubtitlesWorker"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,createdump.exe,mscordaccore*.dll,mscordbi.dll,onnxruntime.lib,DirectML.Debug.dll,DirectML.Debug.pdb"; Components: core
Source: "{#AppPayloadDir}\Tools\ffmpeg\ffmpeg.exe"; DestDir: "{app}\Tools\ffmpeg"; Flags: ignoreversion; Components: core
Source: "{#AppPayloadDir}\Tools\ffmpeg\ffprobe.exe"; DestDir: "{app}\Tools\ffmpeg"; Flags: ignoreversion; Components: core

[Icons]
Name: "{autoprograms}\FrameShift"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Icons\app\app.ico"; Components: core

[UninstallDelete]
Type: files; Name: "{app}\language.txt"

[CustomMessages]
english.AppLanguage=en
chinesesimp.AppLanguage=zh-CN
english.Text_1fe0d37d15=Complete installation
chinesesimp.Text_1fe0d37d15=完整安装
english.Text_00500efca7=Custom installation
chinesesimp.Text_00500efca7=自定义安装
english.Text_aeac198900=FrameShift application
chinesesimp.Text_aeac198900=FrameShift 应用程序
english.Text_0e8f9035c9=Remove background
chinesesimp.Text_0e8f9035c9=移除背景
english.Text_6217537829=Fast
chinesesimp.Text_6217537829=快速
english.Text_5470dd0c9a=High Resolution (Matting) - CPU only
chinesesimp.Text_5470dd0c9a=高分辨率抠图（仅 CPU）
english.Text_bc2498e36c=High Resolution (General) - CPU only
chinesesimp.Text_bc2498e36c=高分辨率通用（仅 CPU）
english.Text_4608c5ee1e=BRIA Remove Background (Balanced) - manual model, not included
chinesesimp.Text_4608c5ee1e=BRIA 移除背景（均衡）— 需手动准备模型，安装包不含模型
english.Text_69e1fb5aac=BRIA Remove Background (High Quality) - manual model, not included
chinesesimp.Text_69e1fb5aac=BRIA 移除背景（高质量）— 需手动准备模型，安装包不含模型
english.Text_9bc1f7c1e3=Remove noise
chinesesimp.Text_9bc1f7c1e3=降噪
english.Text_125fce9bb3=Remove noise (video)
chinesesimp.Text_125fce9bb3=视频降噪
english.Text_3d51c42051=Audio separation
chinesesimp.Text_3d51c42051=音轨分离
english.Text_683021e507=Interpolate video (RIFE)
chinesesimp.Text_683021e507=视频插帧（RIFE）
english.Text_30fe992bda=Remove object
chinesesimp.Text_30fe992bda=移除物体
english.Text_f05887f634=Upscale image (4x)
chinesesimp.Text_f05887f634=图像超分辨率（4 倍）
english.Text_4a13677024=Upscale video (2x / 3x / 4x)
chinesesimp.Text_4a13677024=视频超分辨率（2／3／4 倍）
english.Text_35af0bd25c=Create subtitle file (audio)
chinesesimp.Text_35af0bd25c=生成字幕文件（音频）
english.Text_13f68875ed=Create subtitle file (video)
chinesesimp.Text_13f68875ed=生成字幕文件（视频）
english.Text_e34e5a91aa=Extract text (images and PDF)
chinesesimp.Text_e34e5a91aa=提取文字（图像和 PDF）
english.Text_1446a2327c=Video actions
chinesesimp.Text_1446a2327c=视频工具
english.Text_0b156b9372=Convert video
chinesesimp.Text_0b156b9372=转换视频
english.Text_9376f309e8=Remove audio
chinesesimp.Text_9376f309e8=移除音频
english.Text_d337414dc5=Extract all and specific frames
chinesesimp.Text_d337414dc5=提取全部帧或指定帧
english.Text_0cc6a31c0b=Create GIF
chinesesimp.Text_0cc6a31c0b=制作 GIF
english.Text_6bfb10b252=Add subtitles to video
chinesesimp.Text_6bfb10b252=为视频添加字幕
english.Text_3ec0956fdc=Join videos
chinesesimp.Text_3ec0956fdc=合并视频
english.Text_4beb0703b6=Extract audio
chinesesimp.Text_4beb0703b6=提取音频
english.Text_dd001b9053=Cut video
chinesesimp.Text_dd001b9053=截取视频
english.Text_c11a926c82=Crop video
chinesesimp.Text_c11a926c82=裁剪视频
english.Text_ca6a2bc920=Rotate / Flip video
chinesesimp.Text_ca6a2bc920=旋转／翻转视频
english.Text_1fd793ab6c=Resize video
chinesesimp.Text_1fd793ab6c=调整视频尺寸
english.Text_5f2aa68583=Compress video
chinesesimp.Text_5f2aa68583=压缩视频
english.Text_0db17fe2ac=Interpolate video
chinesesimp.Text_0db17fe2ac=视频插帧
english.Text_5edf60f0a6=Audio actions
chinesesimp.Text_5edf60f0a6=音频工具
english.Text_5f5dd9b194=Cut audio
chinesesimp.Text_5f5dd9b194=截取音频
english.Text_700c7653c2=Convert audio
chinesesimp.Text_700c7653c2=转换音频
english.Text_5671164ebd=Reverse audio
chinesesimp.Text_5671164ebd=倒放音频
english.Text_2aeb7305fb=Compress audio
chinesesimp.Text_2aeb7305fb=压缩音频
english.Text_40b1b7f1f2=Change pitch
chinesesimp.Text_40b1b7f1f2=调整音高
english.Text_fa5e8be522=Change audio speed
chinesesimp.Text_fa5e8be522=调整音频速度
english.Text_ccb39ab460=Change video speed
chinesesimp.Text_ccb39ab460=调整视频速度
english.Text_d2028a7d74=Image actions
chinesesimp.Text_d2028a7d74=图像工具
english.Text_5b772970d6=Image to PDF
chinesesimp.Text_5b772970d6=图像转 PDF
english.Text_0c5e85a9ef=Convert image
chinesesimp.Text_0c5e85a9ef=转换图像
english.Text_aefcefd57e=Compress image
chinesesimp.Text_aefcefd57e=压缩图像
english.Text_5122ef45cd=Convert to icon
chinesesimp.Text_5122ef45cd=转换为图标
english.Text_dbf83a261e=Crop image
chinesesimp.Text_dbf83a261e=裁剪图像
english.Text_491e690264=Resize image
chinesesimp.Text_491e690264=调整图像尺寸
english.Text_d32c9fc51b=Rotate / Flip image
chinesesimp.Text_d32c9fc51b=旋转／翻转图像
english.Text_dcb23c7eb5=Tools
chinesesimp.Text_dcb23c7eb5=工具
english.Text_0fc6e5a501=Media Info
chinesesimp.Text_0fc6e5a501=媒体信息
english.Text_11921b10b5=A previous per-user FrameShift installation was detected.
chinesesimp.Text_11921b10b5=检测到之前为当前用户安装的 FrameShift。
english.Text_d0f481737f=You can remove that copy from Windows Settings. This setup will install FrameShift for this machine.
chinesesimp.Text_d0f481737f=你可以在 Windows 设置中卸载该副本。本安装程序将为此计算机安装 FrameShift。
english.Text_71d35a9d28=Existing installation detected
chinesesimp.Text_71d35a9d28=检测到已有安装
english.Text_95c37994f6=Continue to update the installed copy.
chinesesimp.Text_95c37994f6=继续以更新已安装的版本。
english.Text_a297e247fc=FrameShift version{#InstallerSpace}
chinesesimp.Text_a297e247fc=已安装 FrameShift 版本：
english.Text_3be487c2e1= is already installed.
chinesesimp.Text_3be487c2e1=。
english.Text_8184978e09=This setup can update it to version {#MyAppVersion}.
chinesesimp.Text_8184978e09=本安装程序可将其更新到 {#MyAppVersion}。
english.Text_ff9523a8db=Continue to reinstall the current version.
chinesesimp.Text_ff9523a8db=继续以重新安装当前版本。
english.Text_4c20602531=FrameShift version {#MyAppVersion} is already installed.
chinesesimp.Text_4c20602531=已安装 FrameShift {#MyAppVersion}。
english.Text_22d892abf9=You can reinstall this version over the current installation.
chinesesimp.Text_22d892abf9=你可以覆盖当前安装以重新安装此版本。
english.Text_f89e950309=Continue to install this version over the current copy.
chinesesimp.Text_f89e950309=继续以覆盖安装此版本。
english.Text_af410f27ab=A newer FrameShift version (
chinesesimp.Text_af410f27ab=已安装较新版本的 FrameShift（
english.Text_a258558d77=) is already installed.
chinesesimp.Text_a258558d77=）。
english.Text_33579ebc2a=You can still install version {#MyAppVersion} over the current installation.
chinesesimp.Text_33579ebc2a=你仍可用 {#MyAppVersion} 覆盖当前安装。
english.Text_5cec5c225a=To uninstall FrameShift, use Windows Settings.
chinesesimp.Text_5cec5c225a=如需卸载 FrameShift，请使用 Windows 设置。
english.Text_8d55fd53b9=A previous per-user copy was also detected; you can remove it from Windows Settings.
chinesesimp.Text_8d55fd53b9=还检测到之前为当前用户安装的副本，可在 Windows 设置中卸载。
english.Text_c74676dfc9=Extract all frames
chinesesimp.Text_c74676dfc9=提取全部帧
english.Text_722f989b80=Extract specific frames
chinesesimp.Text_722f989b80=提取指定帧
english.Text_20fa2acd9d=First frame
chinesesimp.Text_20fa2acd9d=第一帧
english.Text_4f0ad82e44=Last frame
chinesesimp.Text_4f0ad82e44=最后一帧
english.Text_60651e8404=Keyframes
chinesesimp.Text_60651e8404=关键帧
english.Text_a4500def3d=Join videos
chinesesimp.Text_a4500def3d=合并视频
english.Text_fdc1b44321=Convert video
chinesesimp.Text_fdc1b44321=转换视频
english.Text_74a16ad6ec=Convert audio
chinesesimp.Text_74a16ad6ec=转换音频
english.Text_addd98ba08=Cut audio
chinesesimp.Text_addd98ba08=截取音频
english.Text_efbba1bee0=Remove audio
chinesesimp.Text_efbba1bee0=移除音频
english.Text_19cfb4c9f5=Reverse audio
chinesesimp.Text_19cfb4c9f5=倒放音频
english.Text_953a2b6b84=Compress audio
chinesesimp.Text_953a2b6b84=压缩音频
english.Text_af1200ab6f=Change pitch
chinesesimp.Text_af1200ab6f=调整音高
english.Text_4a1ea75a7e=Change audio speed
chinesesimp.Text_4a1ea75a7e=调整音频速度
english.Text_2658d5539f=Change video speed
chinesesimp.Text_2658d5539f=调整视频速度
english.Text_2979daede3=Create GIF
chinesesimp.Text_2979daede3=制作 GIF
english.Text_9fa0d868a4=Add subtitles to video
chinesesimp.Text_9fa0d868a4=为视频添加字幕
english.Text_7bc1090835=Extract audio
chinesesimp.Text_7bc1090835=提取音频
english.Text_3665a1b4dc=Cut video
chinesesimp.Text_3665a1b4dc=截取视频
english.Text_0e7b0b8ea7=Crop video
chinesesimp.Text_0e7b0b8ea7=裁剪视频
english.Text_b2fb3f9dfd=Resize video
chinesesimp.Text_b2fb3f9dfd=调整视频尺寸
english.Text_224d74689f=Compress video
chinesesimp.Text_224d74689f=压缩视频
english.Text_db0eacf22e=Interpolate video
chinesesimp.Text_db0eacf22e=视频插帧
english.Text_bc9e6c1ecf=Compress image
chinesesimp.Text_bc9e6c1ecf=压缩图像
english.Text_a190952588=Crop image
chinesesimp.Text_a190952588=裁剪图像
english.Text_f75fa94e8d=Resize image
chinesesimp.Text_f75fa94e8d=调整图像尺寸
english.Text_bb417e6969=Convert image
chinesesimp.Text_bb417e6969=转换图像
english.Text_f3956e810e=Convert to icon
chinesesimp.Text_f3956e810e=转换为图标
english.Text_d24530fc4c=Image to PDF
chinesesimp.Text_d24530fc4c=图像转 PDF
english.Text_357a79509d=Rotate / Flip image
chinesesimp.Text_357a79509d=旋转／翻转图像
english.Text_b5349fc7f1=Rotate / Flip video
chinesesimp.Text_b5349fc7f1=旋转／翻转视频
english.Text_ad9f476d26=Media Info
chinesesimp.Text_ad9f476d26=媒体信息
english.Text_875ae019f4=Remove Background (Fast)
chinesesimp.Text_875ae019f4=移除背景（快速）
english.Text_61ad2d8a39=Remove Background (High Resolution Matting)
chinesesimp.Text_61ad2d8a39=移除背景（高分辨率抠图）
english.Text_0676d08465=Remove Background (High Resolution General)
chinesesimp.Text_0676d08465=移除背景（高分辨率通用）
english.Text_1b8476bae4=Remove Background (BRIA Balanced)
chinesesimp.Text_1b8476bae4=移除背景（BRIA 均衡）
english.Text_d7650ed9b4=Remove Background (BRIA High Quality)
chinesesimp.Text_d7650ed9b4=移除背景（BRIA 高质量）
english.Text_8a97d93d9b=Remove noise
chinesesimp.Text_8a97d93d9b=降噪
english.Text_a7dc9f181f=Remove noise (video)
chinesesimp.Text_a7dc9f181f=视频降噪
english.Text_a057287dbb=Audio separation
chinesesimp.Text_a057287dbb=音轨分离
english.Text_a11d766b34=Interpolate video (RIFE)
chinesesimp.Text_a11d766b34=视频插帧（RIFE）
english.Text_fd8c11b3f1=Remove object
chinesesimp.Text_fd8c11b3f1=移除物体
english.Text_578e57daed=Upscale Image 4x
chinesesimp.Text_578e57daed=图像超分辨率（4 倍）
english.Text_7092368127=Upscale Video 4x
chinesesimp.Text_7092368127=视频超分辨率（4 倍）
english.Text_c8478663aa=Create Subtitle File
chinesesimp.Text_c8478663aa=生成字幕文件
english.Text_456f83e647=Extract text...
chinesesimp.Text_456f83e647=提取文字…
english.Text_77bc786105=Select AI models folder
chinesesimp.Text_77bc786105=选择 AI 模型文件夹
english.Text_93123aa558=AI models folder
chinesesimp.Text_93123aa558=AI 模型文件夹
english.Text_22dda8f08d=Choose where FrameShift AI will store downloaded AI models.
chinesesimp.Text_22dda8f08d=选择 FrameShift AI 保存已下载模型的位置。
english.Text_8567135b14=AI models are downloaded on first use and are never included in the installer.
chinesesimp.Text_8567135b14=AI 模型会在首次使用时下载，安装包中不包含模型。
english.Text_deb424754d=Runtime components such as the subtitle worker are bundled with FrameShift.
chinesesimp.Text_deb424754d=字幕工作进程等运行组件随 FrameShift 一起提供。
english.Text_845f910b66=AI models folder:
chinesesimp.Text_845f910b66=AI 模型文件夹：
english.Text_0b90c6ef40=Browse...
chinesesimp.Text_0b90c6ef40=浏览…
english.Text_ce198b381c=FrameShift does NOT distribute, bundle, mirror or download the BRIA RMBG-2.0 model.
chinesesimp.Text_ce198b381c=FrameShift 不分发、捆绑、镜像或下载 BRIA RMBG-2.0 模型。
english.Text_49b0fad14c=This folder is a placeholder for a model file you must obtain yourself.
chinesesimp.Text_49b0fad14c=此文件夹用于存放需要你自行获取的模型文件。
english.Text_269476c0e6=How to install:
chinesesimp.Text_269476c0e6=安装方法：
english.Text_4208d0511e=  1. Open the official BRIA page:
chinesesimp.Text_4208d0511e=  1. 打开 BRIA 官方页面：
english.Text_e482e00f44=  2. Review BRIA's documentation and licensing.
chinesesimp.Text_e482e00f44=  2. 阅读 BRIA 的文档和许可条款。
english.Text_c6e5d8f605=  3. Download the ONNX model and place it in THIS folder.
chinesesimp.Text_c6e5d8f605=  3. 下载 ONNX 模型并放入此文件夹。
english.Text_0911e80cb8=Expected file:{#InstallerSpace}
chinesesimp.Text_0911e80cb8=所需文件：
english.Text_c6a26d8074=Approximate size:{#InstallerSpace}
chinesesimp.Text_c6a26d8074=大约大小：
english.Text_652e21e125=Target folder:{#InstallerSpace}
chinesesimp.Text_652e21e125=目标文件夹：
english.Text_391e4e86ea=FrameShift verifies the file against the official BRIA checksum. If it does not
chinesesimp.Text_391e4e86ea=FrameShift 会使用 BRIA 官方校验和验证文件。如果校验不匹配，
english.Text_028133a1bf=match, FrameShift will point you back to the official BRIA page.
chinesesimp.Text_028133a1bf=FrameShift 会提示你返回 BRIA 官方页面。
english.Text_834ff95b7e=Choose how to continue with the detected FrameShift installation.
chinesesimp.Text_834ff95b7e=选择如何处理检测到的 FrameShift 安装。
english.Text_74b47686d0=Choose a dedicated AI models folder. Drive roots, user-profile roots, Windows, Program Files, the FrameShift install folder and their parents are not allowed.
chinesesimp.Text_74b47686d0=请选择专用 AI 模型文件夹。不能使用磁盘根目录、用户主目录、Windows、Program Files、FrameShift 安装目录及其父目录。
english.Text_219946869b=Do you also want to remove downloaded AI models created by FrameShift?
chinesesimp.Text_219946869b=是否同时移除 FrameShift 下载的 AI 模型？
english.Text_6628d02a16=FrameShift will delete only marked model folders that it created. The selected models root and any other files will be kept.
chinesesimp.Text_6628d02a16=FrameShift 仅删除自身创建并标记的模型文件夹。模型根目录与其他文件会保留。
english.Text_adcadd0004=Location:{#InstallerSpace}
chinesesimp.Text_adcadd0004=位置：
english.Text_1aa09e20ca=Do you also want to delete diagnostic logs?
chinesesimp.Text_1aa09e20ca=是否同时删除诊断日志？

english.LanguageWriteError=Could not save the FrameShift installation language.
chinesesimp.LanguageWriteError=无法保存 FrameShift 安装语言。
spanish.LanguageWriteError=No se pudo guardar el idioma de instalación de FrameShift.
spanish.AppLanguage=es
spanish.Text_1fe0d37d15=Instalación completa
spanish.Text_00500efca7=Instalación personalizada
spanish.Text_aeac198900=Aplicación FrameShift
spanish.Text_0e8f9035c9=Quitar fondo
spanish.Text_6217537829=Rápido
spanish.Text_5470dd0c9a=Alta resolución (máscara alfa) - solo CPU
spanish.Text_bc2498e36c=Alta resolución (general) - solo CPU
spanish.Text_4608c5ee1e=BRIA Quitar fondo (equilibrado) - modelo manual, no incluido
spanish.Text_69e1fb5aac=BRIA Quitar fondo (alta calidad) - modelo manual, no incluido
spanish.Text_9bc1f7c1e3=Reducir ruido
spanish.Text_125fce9bb3=Reducir ruido del video
spanish.Text_3d51c42051=Separación de audio
spanish.Text_683021e507=Interpolar video (RIFE)
spanish.Text_30fe992bda=Quitar objetos
spanish.Text_f05887f634=Ampliar imagen (4x)
spanish.Text_4a13677024=Ampliar video (2x / 3x / 4x)
spanish.Text_35af0bd25c=Crear archivo de subtítulos (audio)
spanish.Text_13f68875ed=Crear archivo de subtítulos (video)
spanish.Text_e34e5a91aa=Extraer texto (imágenes y PDF)
spanish.Text_1446a2327c=Acciones de video
spanish.Text_0b156b9372=Convertir video
spanish.Text_9376f309e8=Quitar audio
spanish.Text_d337414dc5=Extraer todos los fotogramas o fotogramas específicos
spanish.Text_0cc6a31c0b=Crear GIF
spanish.Text_6bfb10b252=Añadir subtítulos al video
spanish.Text_3ec0956fdc=Unir videos
spanish.Text_4beb0703b6=Extraer audio
spanish.Text_dd001b9053=Cortar video
spanish.Text_c11a926c82=Recortar video
spanish.Text_ca6a2bc920=Girar / voltear video
spanish.Text_1fd793ab6c=Cambiar tamaño de video
spanish.Text_5f2aa68583=Comprimir video
spanish.Text_0db17fe2ac=Interpolar video
spanish.Text_5edf60f0a6=Acciones de audio
spanish.Text_5f5dd9b194=Cortar audio
spanish.Text_700c7653c2=Convertir audio
spanish.Text_5671164ebd=Invertir audio
spanish.Text_2aeb7305fb=Comprimir audio
spanish.Text_40b1b7f1f2=Cambiar tono
spanish.Text_fa5e8be522=Cambiar velocidad del audio
spanish.Text_ccb39ab460=Cambiar velocidad del video
spanish.Text_d2028a7d74=Acciones de imagen
spanish.Text_5b772970d6=Imágenes a PDF
spanish.Text_0c5e85a9ef=Convertir imagen
spanish.Text_aefcefd57e=Comprimir imagen
spanish.Text_5122ef45cd=Convertir a icono
spanish.Text_dbf83a261e=Recortar imagen
spanish.Text_491e690264=Cambiar tamaño de imagen
spanish.Text_d32c9fc51b=Girar / voltear imagen
spanish.Text_dcb23c7eb5=Herramientas
spanish.Text_0fc6e5a501=Información del archivo
spanish.Text_11921b10b5=Se detectó una instalación anterior de FrameShift para este usuario.
spanish.Text_d0f481737f=Puede quitar esa copia desde la Configuración de Windows. Este instalador instalará FrameShift para este equipo.
spanish.Text_71d35a9d28=Se detectó una instalación existente
spanish.Text_95c37994f6=Continúe para actualizar la copia instalada.
spanish.Text_a297e247fc=La versión de FrameShift{#InstallerSpace}
spanish.Text_3be487c2e1= ya está instalada.
spanish.Text_8184978e09=Este instalador puede actualizarla a la versión {#MyAppVersion}.
spanish.Text_ff9523a8db=Continúe para reinstalar la versión actual.
spanish.Text_4c20602531=La versión {#MyAppVersion} de FrameShift ya está instalada.
spanish.Text_22d892abf9=Puede reinstalar esta versión sobre la instalación actual.
spanish.Text_f89e950309=Continúe para instalar esta versión sobre la copia actual.
spanish.Text_af410f27ab=Ya está instalada una versión más reciente de FrameShift (
spanish.Text_a258558d77=).
spanish.Text_33579ebc2a=Aun así, puede instalar la versión {#MyAppVersion} sobre la instalación actual.
spanish.Text_5cec5c225a=Para desinstalar FrameShift, utilice la Configuración de Windows.
spanish.Text_8d55fd53b9=También se detectó una copia anterior para este usuario; puede quitarla desde la Configuración de Windows.
spanish.Text_c74676dfc9=Extraer todos los fotogramas
spanish.Text_722f989b80=Extraer fotogramas específicos
spanish.Text_20fa2acd9d=Primer fotograma
spanish.Text_4f0ad82e44=Último fotograma
spanish.Text_60651e8404=Fotogramas clave
spanish.Text_a4500def3d=Unir videos
spanish.Text_fdc1b44321=Convertir video
spanish.Text_74a16ad6ec=Convertir audio
spanish.Text_addd98ba08=Cortar audio
spanish.Text_efbba1bee0=Quitar audio
spanish.Text_19cfb4c9f5=Invertir audio
spanish.Text_953a2b6b84=Comprimir audio
spanish.Text_af1200ab6f=Cambiar tono
spanish.Text_4a1ea75a7e=Cambiar velocidad del audio
spanish.Text_2658d5539f=Cambiar velocidad del video
spanish.Text_2979daede3=Crear GIF
spanish.Text_9fa0d868a4=Añadir subtítulos al video
spanish.Text_7bc1090835=Extraer audio
spanish.Text_3665a1b4dc=Cortar video
spanish.Text_0e7b0b8ea7=Recortar video
spanish.Text_b2fb3f9dfd=Cambiar tamaño de video
spanish.Text_224d74689f=Comprimir video
spanish.Text_db0eacf22e=Interpolar video
spanish.Text_bc9e6c1ecf=Comprimir imagen
spanish.Text_a190952588=Recortar imagen
spanish.Text_f75fa94e8d=Cambiar tamaño de imagen
spanish.Text_bb417e6969=Convertir imagen
spanish.Text_f3956e810e=Convertir a icono
spanish.Text_d24530fc4c=Imágenes a PDF
spanish.Text_357a79509d=Girar / voltear imagen
spanish.Text_b5349fc7f1=Girar / voltear video
spanish.Text_ad9f476d26=Información del archivo
spanish.Text_875ae019f4=Quitar fondo (rápido)
spanish.Text_61ad2d8a39=Quitar fondo (alta resolución con máscara alfa)
spanish.Text_0676d08465=Quitar fondo (alta resolución general)
spanish.Text_1b8476bae4=Quitar fondo (BRIA equilibrado)
spanish.Text_d7650ed9b4=Quitar fondo (BRIA alta calidad)
spanish.Text_8a97d93d9b=Reducir ruido
spanish.Text_a7dc9f181f=Reducir ruido del video
spanish.Text_a057287dbb=Separación de audio
spanish.Text_a11d766b34=Interpolar video (RIFE)
spanish.Text_fd8c11b3f1=Quitar objetos
spanish.Text_578e57daed=Ampliar imagen 4x
spanish.Text_7092368127=Ampliar video 4x
spanish.Text_c8478663aa=Crear archivo de subtítulos
spanish.Text_456f83e647=Extraer texto...
spanish.Text_77bc786105=Seleccionar carpeta de modelos de IA
spanish.Text_93123aa558=Carpeta de modelos de IA
spanish.Text_22dda8f08d=Elija dónde guardará FrameShift AI los modelos de IA descargados.
spanish.Text_8567135b14=Los modelos de IA se descargan al usarlos por primera vez y nunca se incluyen en el instalador.
spanish.Text_deb424754d=Los componentes de ejecución, como el proceso de subtítulos, se incluyen con FrameShift.
spanish.Text_845f910b66=Carpeta de modelos de IA:
spanish.Text_0b90c6ef40=Examinar...
spanish.Text_ce198b381c=FrameShift NO distribuye, incluye, replica ni descarga el modelo BRIA RMBG-2.0.
spanish.Text_49b0fad14c=Esta carpeta está reservada para un archivo de modelo que debe obtener por su cuenta.
spanish.Text_269476c0e6=Cómo instalar:
spanish.Text_4208d0511e=  1. Abra la página oficial de BRIA:
spanish.Text_e482e00f44=  2. Revise la documentación y la licencia de BRIA.
spanish.Text_c6e5d8f605=  3. Descargue el modelo ONNX y colóquelo en ESTA carpeta.
spanish.Text_0911e80cb8=Archivo esperado:{#InstallerSpace}
spanish.Text_c6a26d8074=Tamaño aproximado:{#InstallerSpace}
spanish.Text_652e21e125=Carpeta de destino:{#InstallerSpace}
spanish.Text_391e4e86ea=FrameShift verifica el archivo con la suma de verificación oficial de BRIA. Si no
spanish.Text_028133a1bf=coincide, FrameShift le indicará que vuelva a la página oficial de BRIA.
spanish.Text_834ff95b7e=Elija cómo continuar con la instalación de FrameShift detectada.
spanish.Text_74b47686d0=Elija una carpeta exclusiva para modelos de IA. No se permiten las raíces de unidades, las raíces de perfiles de usuario, Windows, Program Files, la carpeta de instalación de FrameShift ni sus carpetas superiores.
spanish.Text_219946869b=¿También desea quitar los modelos de IA descargados creados por FrameShift?
spanish.Text_6628d02a16=FrameShift solo eliminará las carpetas de modelos marcadas que haya creado. Se conservarán la carpeta raíz de modelos y los demás archivos.
spanish.Text_adcadd0004=Ubicación:{#InstallerSpace}
spanish.Text_1aa09e20ca=¿También desea eliminar los registros de diagnóstico?

[Code]
procedure SHChangeNotify(wEventId: LongInt; uFlags: Cardinal; dwItem1: Integer; dwItem2: Integer); external 'SHChangeNotify@shell32.dll stdcall';

const
  VideoExtensions = '.mp4,.mkv,.avi,.mov,.webm,.m4v';
  AudioExtensions = '.mp3,.wav,.wave,.flac,.m4a,.ogg,.aac,.wma';
  ImageExtensions = '.png,.jpg,.jpeg,.webp,.bmp';
  PdfImageExtensions = '.png,.jpg,.jpeg,.webp,.bmp';
  UninstallSubkey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1';
  ExistingInstallStateOlder = -1;
  ExistingInstallStateSame = 0;
  ExistingInstallStateNewer = 1;
  ModelDirectoryMarkerFileName = '.frameshift-ai-model-directory';
  ModelDirectoryMarkerContent = 'FrameShift AI model directory v1';
  FileAttributeReparsePoint = $00000400;
  FileAttributeDirectory = $00000010;
  InvalidFileAttributes = $FFFFFFFF;

type
  TModelDirectoryHandles = array of THandle;
  TModelAttributeTag = record
    Attributes: Cardinal;
    Tag: Cardinal;
  end;

var
  InstalledVersion: string;
  HasLegacyUserInstall: Boolean;
  InstalledVersionState: Integer;
  HasInstalledVersion: Boolean;
  ExistingInstallPage: TWizardPage;
  ExistingInstallStatusLabel: TNewStaticText;
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

function FindInstalledVersion(): Boolean;
var
  LegacyVersion: string;
begin
  InstalledVersion := '';
  Result := RegQueryStringValue(HKLM64, UninstallSubkey, 'DisplayVersion', InstalledVersion) or
    RegQueryStringValue(HKLM32, UninstallSubkey, 'DisplayVersion', InstalledVersion);
  HasLegacyUserInstall := RegQueryStringValue(HKCU64, UninstallSubkey, 'DisplayVersion', LegacyVersion) or
    RegQueryStringValue(HKCU32, UninstallSubkey, 'DisplayVersion', LegacyVersion);
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

procedure UpdateExistingInstallPageContent();
begin
  if not HasInstalledVersion then
  begin
    if HasLegacyUserInstall then
    begin
      ExistingInstallStatusLabel.Caption :=
        CustomMessage('Text_11921b10b5') + #13#10#13#10 +
        CustomMessage('Text_d0f481737f');
    end;
    exit;
  end;

  if InstalledVersionState = ExistingInstallStateOlder then
  begin
    ExistingInstallPage.Caption := CustomMessage('Text_71d35a9d28');
    ExistingInstallPage.Description :=
      CustomMessage('Text_95c37994f6');
    ExistingInstallStatusLabel.Caption :=
      CustomMessage('Text_a297e247fc') + InstalledVersion + CustomMessage('Text_3be487c2e1') + #13#10#13#10 +
      CustomMessage('Text_8184978e09');
  end
  else if InstalledVersionState = ExistingInstallStateSame then
  begin
    ExistingInstallPage.Caption := CustomMessage('Text_71d35a9d28');
    ExistingInstallPage.Description :=
      CustomMessage('Text_ff9523a8db');
    ExistingInstallStatusLabel.Caption :=
      CustomMessage('Text_4c20602531') + #13#10#13#10 +
      CustomMessage('Text_22d892abf9');
  end
  else
  begin
    ExistingInstallPage.Caption := CustomMessage('Text_71d35a9d28');
    ExistingInstallPage.Description :=
      CustomMessage('Text_f89e950309');
    ExistingInstallStatusLabel.Caption :=
      CustomMessage('Text_af410f27ab') + InstalledVersion + CustomMessage('Text_a258558d77') + #13#10#13#10 +
      CustomMessage('Text_33579ebc2a');
  end;

  ExistingInstallStatusLabel.Caption := ExistingInstallStatusLabel.Caption + #13#10#13#10 +
    CustomMessage('Text_5cec5c225a');
  if HasLegacyUserInstall then
    ExistingInstallStatusLabel.Caption := ExistingInstallStatusLabel.Caption + #13#10 +
      CustomMessage('Text_8d55fd53b9');
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
  CleanupContextMenuKeysForHive(HKCU, '.pdf');
  CleanupContextMenuKeysForHive(HKLM, '.pdf');
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
  CleanupContextMenuAIKeysForHive(HKCU, '.pdf');
  CleanupContextMenuAIKeysForHive(HKLM, '.pdf');
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
  if ActionId = 'extract-text' then
    RegWriteStringValue(Hive, KeyPath, 'MultiSelectModel', 'Player');
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
  if MenuKey = 'join_videos' then
  begin
    Result := ExpandConstant('{app}\Assets\Icons\menus\context\ico\join-videos-video-icon.ico');
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
  RegWriteStringValue(Hive, DirectKeyPath, 'MUIVerb', CustomMessage('Text_c74676dfc9'));
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
  RegWriteStringValue(Hive, SpecificKeyPath, 'MUIVerb', CustomMessage('Text_722f989b80'));
  RegWriteStringValue(
    Hive,
    SpecificKeyPath,
    'ExtendedSubCommandsKey',
    'SystemFileAssociations\' + Ext + '\shell\FrameShift\shell\extract_specific_frames');
  if IconPath <> '' then
  begin
    RegWriteStringValue(Hive, SpecificKeyPath, 'Icon', IconPath);
  end;

  ConfigureSpecificExtractFrameMenuForHive(Hive, SpecificKeyPath, 'first_frame', CustomMessage('Text_20fa2acd9d'), 'first', IconPath);
  ConfigureSpecificExtractFrameMenuForHive(Hive, SpecificKeyPath, 'last_frame', CustomMessage('Text_4f0ad82e44'), 'last', IconPath);
  ConfigureSpecificExtractFrameMenuForHive(Hive, SpecificKeyPath, 'keyframes', CustomMessage('Text_60651e8404'), 'keyframes', IconPath);
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

procedure ConfigureJoinVideosMenuForHive(const Hive: Integer; const Ext: string);
var
  KeyPath: string;
begin
  ConfigureActionMenuForHive(Hive, Ext, 'join_videos', CustomMessage('Text_a4500def3d'), 'join-videos', '');
  KeyPath := 'Software\Classes\SystemFileAssociations\' + Ext + '\shell\FrameShift\shell\join_videos';
  // Player lets Explorer pass larger multi-selections. FrameShift still treats the
  // received order as provisional and exposes an explicit timeline order UI.
  RegWriteStringValue(Hive, KeyPath, 'MultiSelectModel', 'Player');
end;

procedure ApplyJoinVideosMenuList(const Extensions: string);
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
      ConfigureJoinVideosMenuForHive(HKLM, Extension);
    end
    else
    begin
      ConfigureJoinVideosMenuForHive(HKCU, Extension);
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
      CustomMessage('Text_fdc1b44321'),
      'convert-video',
      '');
  end;

  if WizardIsComponentSelected('audio\convert_audio') then
  begin
    ApplyActionMenuList(
      AudioExtensions,
      'convert_audio',
      CustomMessage('Text_74a16ad6ec'),
      'convert-audio',
      '');
  end;

  if WizardIsComponentSelected('audio\cut_audio') then
  begin
    ApplyActionMenuList(
      AudioExtensions,
      'cut_audio',
      CustomMessage('Text_addd98ba08'),
      'cut-audio',
      '');
  end;

  if WizardIsComponentSelected('video\remove_audio') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'remove_audio',
      CustomMessage('Text_efbba1bee0'),
      'remove-audio',
      '');
  end;

  if WizardIsComponentSelected('audio\reverse_audio') then
  begin
    ApplyActionMenuList(
      AudioExtensions,
      'reverse_audio',
      CustomMessage('Text_19cfb4c9f5'),
      'reverse-audio',
      '');
  end;

  if WizardIsComponentSelected('audio\compress_audio') then
  begin
    ApplyActionMenuList(
      AudioExtensions,
      'compress_audio',
      CustomMessage('Text_953a2b6b84'),
      'compress-audio',
      '');
  end;

  if WizardIsComponentSelected('audio\change_pitch') then
  begin
    ApplyActionMenuList(
      AudioExtensions,
      'change_pitch',
      CustomMessage('Text_af1200ab6f'),
      'change-pitch',
      '');
  end;

  if WizardIsComponentSelected('audio\change_audio_speed') then
  begin
    ApplyActionMenuList(
      AudioExtensions,
      'change_audio_speed',
      CustomMessage('Text_4a1ea75a7e'),
      'change-audio-speed',
      '');
  end;

  if WizardIsComponentSelected('video\change_video_speed') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'change_video_speed',
      CustomMessage('Text_2658d5539f'),
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
      CustomMessage('Text_2979daede3'),
      'create-gif',
      '');
  end;

  if WizardIsComponentSelected('video\add_subtitles_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'add_subtitles_video',
      CustomMessage('Text_9fa0d868a4'),
      'add-subtitles-video',
      '');
  end;

  if WizardIsComponentSelected('video\join_videos') then
  begin
    ApplyJoinVideosMenuList(VideoExtensions);
  end;

  if WizardIsComponentSelected('video\extract_audio') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'extract_audio',
      CustomMessage('Text_7bc1090835'),
      'extract-audio',
      '');
  end;

  if WizardIsComponentSelected('video\cut_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'cut_video',
      CustomMessage('Text_3665a1b4dc'),
      'cut-video',
      '');
  end;

  if WizardIsComponentSelected('video\crop_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'crop_video',
      CustomMessage('Text_0e7b0b8ea7'),
      'crop-video',
      '');
  end;

  if WizardIsComponentSelected('video\resize_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'resize_video',
      CustomMessage('Text_b2fb3f9dfd'),
      'resize-video',
      '');
  end;

  if WizardIsComponentSelected('video\compress_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'compress_video',
      CustomMessage('Text_224d74689f'),
      'compress-video',
      '');
  end;

  if WizardIsComponentSelected('video\interpolate_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'interpolate_video',
      CustomMessage('Text_db0eacf22e'),
      'interpolate-video',
      '');
  end;

  if WizardIsComponentSelected('image\compress_image') then
  begin
    ApplyActionMenuList(
      ImageExtensions,
      'compress_image',
      CustomMessage('Text_bc9e6c1ecf'),
      'compress-image',
      '');
  end;

  if WizardIsComponentSelected('image\crop_image') then
  begin
    ApplyActionMenuList(
      ImageExtensions,
      'crop_image',
      CustomMessage('Text_a190952588'),
      'crop-image',
      '');
  end;

  if WizardIsComponentSelected('image\resize_image') then
  begin
    ApplyActionMenuList(
      ImageExtensions,
      'resize_image',
      CustomMessage('Text_f75fa94e8d'),
      'resize-image',
      '');
  end;

  if WizardIsComponentSelected('image\convert_image') then
  begin
    ApplyActionMenuList(
      ImageExtensions,
      'convert_image',
      CustomMessage('Text_bb417e6969'),
      'convert-image',
      '');
  end;

  if WizardIsComponentSelected('image\convert_icon') then
  begin
    ApplyActionMenuList(
      ImageExtensions,
      'convert_icon',
      CustomMessage('Text_f3956e810e'),
      'convert-to-icon',
      '');
  end;

  if WizardIsComponentSelected('image\image_to_pdf') then
  begin
    ApplyActionMenuList(
      PdfImageExtensions,
      'image_to_pdf',
      CustomMessage('Text_d24530fc4c'),
      'image-to-pdf',
      '');
  end;

  if WizardIsComponentSelected('image\rotate_flip_image') then
  begin
    ApplyActionMenuList(
      ImageExtensions,
      'rotate_flip_image',
      CustomMessage('Text_357a79509d'),
      'rotate-flip-image',
      '');
  end;

  if WizardIsComponentSelected('video\rotate_flip_video') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'rotate_flip_video',
      CustomMessage('Text_b5349fc7f1'),
      'rotate-flip-video',
      '');
  end;

  if WizardIsComponentSelected('tools\media_info') then
  begin
    ApplyActionMenuList(
      VideoExtensions,
      'media_info',
      CustomMessage('Text_ad9f476d26'),
      'media-info',
      '');
    ApplyActionMenuList(
      AudioExtensions,
      'media_info',
      CustomMessage('Text_ad9f476d26'),
      'media-info',
      '');
    ApplyActionMenuList(
      ImageExtensions,
      'media_info',
      CustomMessage('Text_ad9f476d26'),
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
      CustomMessage('Text_875ae019f4'),
      'remove-background',
      ' --rmbg-model fast');
  end;

  if WizardIsComponentSelected('ai\remove_background\high_resolution') then
  begin
    ApplyAIActionMenuList(
      ImageExtensions,
      'remove_background_high_resolution',
      CustomMessage('Text_61ad2d8a39'),
      'remove-background',
      ' --rmbg-model high-resolution');
  end;

  if WizardIsComponentSelected('ai\remove_background\high_resolution_general') then
  begin
    ApplyAIActionMenuList(
      ImageExtensions,
      'remove_background_high_resolution_general',
      CustomMessage('Text_0676d08465'),
      'remove-background',
      ' --rmbg-model high-resolution-general');
  end;

  if WizardIsComponentSelected('ai\remove_background\bria_balanced') then
  begin
    ApplyAIActionMenuList(
      ImageExtensions,
      'remove_background_bria_balanced',
      CustomMessage('Text_1b8476bae4'),
      'remove-background',
      ' --rmbg-model bria-balanced');
  end;

  if WizardIsComponentSelected('ai\remove_background\bria_high_quality') then
  begin
    ApplyAIActionMenuList(
      ImageExtensions,
      'remove_background_bria_high_quality',
      CustomMessage('Text_d7650ed9b4'),
      'remove-background',
      ' --rmbg-model bria-high-quality');
  end;

  if WizardIsComponentSelected('ai\remove_noise') then
  begin
    ApplyAIActionMenuList(
      AudioExtensions,
      'remove_noise',
      CustomMessage('Text_8a97d93d9b'),
      'remove-noise',
      '');
  end;

  if WizardIsComponentSelected('ai\remove_noise_video') then
  begin
    ApplyAIActionMenuList(
      VideoExtensions,
      'remove_noise_video',
      CustomMessage('Text_a7dc9f181f'),
      'remove-noise-video',
      '');
  end;

  if WizardIsComponentSelected('ai\separate_audio') then
  begin
    ApplyAIActionMenuList(
      AudioExtensions,
      'separate_audio',
      CustomMessage('Text_a057287dbb'),
      'separate-audio',
      '');
  end;

  if WizardIsComponentSelected('ai\interpolate_video_rife') then
  begin
    ApplyAIActionMenuList(
      VideoExtensions,
      'interpolate_video_rife',
      CustomMessage('Text_a11d766b34'),
      'interpolate-video-rife',
      '');
  end;

  if WizardIsComponentSelected('ai\remove_object') then
  begin
    ApplyAIActionMenuList(
      ImageExtensions,
      'remove_object',
      CustomMessage('Text_fd8c11b3f1'),
      'remove-object',
      '');
  end;

  if WizardIsComponentSelected('ai\upscale_image') then
  begin
    ApplyAIActionMenuList(
      ImageExtensions,
      'upscale_image',
      CustomMessage('Text_578e57daed'),
      'upscale-image',
      '');
  end;

  if WizardIsComponentSelected('ai\upscale_video') then
  begin
    ApplyAIActionMenuList(
      VideoExtensions,
      'upscale_video',
      CustomMessage('Text_7092368127'),
      'upscale-video',
      '');
  end;

  if WizardIsComponentSelected('ai\create_subtitles_audio') then
  begin
    ApplyAIActionMenuList(
      AudioExtensions,
      'create_subtitles_audio',
      CustomMessage('Text_c8478663aa'),
      'create-subtitles-audio',
      '');
  end;

  if WizardIsComponentSelected('ai\create_subtitles_video') then
  begin
    ApplyAIActionMenuList(
      VideoExtensions,
      'create_subtitles_video',
      CustomMessage('Text_c8478663aa'),
      'create-subtitles-video',
      '');
  end;
  if WizardIsComponentSelected('ai\extract_text') then
    ApplyAIActionMenuList(ImageExtensions + ',.pdf', 'extract_text', CustomMessage('Text_456f83e647'), 'extract-text', '');
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
  if BrowseForFolder(CustomMessage('Text_77bc786105'), Dir, False) then
    AiModelsDirEdit.Text := Dir;
end;

procedure CreateAiModelsPage();
var
  DescLabel: TNewStaticText;
  DirLabel: TNewStaticText;
begin
  AiModelsPage := CreateCustomPage(
    wpSelectComponents,
    CustomMessage('Text_93123aa558'),
    CustomMessage('Text_22dda8f08d'));

  DescLabel := TNewStaticText.Create(AiModelsPage);
  DescLabel.Parent := AiModelsPage.Surface;
  DescLabel.Left := ScaleX(0);
  DescLabel.Top := ScaleY(4);
  DescLabel.Width := AiModelsPage.SurfaceWidth;
  DescLabel.Height := ScaleY(32);
  DescLabel.AutoSize := False;
  DescLabel.WordWrap := True;
  DescLabel.Caption :=
    CustomMessage('Text_8567135b14') + #13#10 +
    CustomMessage('Text_deb424754d');

  DirLabel := TNewStaticText.Create(AiModelsPage);
  DirLabel.Parent := AiModelsPage.Surface;
  DirLabel.Left := ScaleX(0);
  DirLabel.Top := DescLabel.Top + DescLabel.Height + ScaleY(10);
  DirLabel.Width := AiModelsPage.SurfaceWidth;
  DirLabel.Height := ScaleY(16);
  DirLabel.AutoSize := False;
  DirLabel.Caption := CustomMessage('Text_845f910b66');

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
  AiModelsDirBrowseButton.Caption := CustomMessage('Text_0b90c6ef40');
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
    CustomMessage('Text_ce198b381c') + #13#10 +
    CustomMessage('Text_49b0fad14c') + #13#10#13#10 +
    CustomMessage('Text_269476c0e6') + #13#10 +
    CustomMessage('Text_4208d0511e') + #13#10 +
    '       https://huggingface.co/briaai/RMBG-2.0/tree/main' + #13#10 +
    CustomMessage('Text_e482e00f44') + #13#10 +
    CustomMessage('Text_c6e5d8f605') + #13#10#13#10 +
    CustomMessage('Text_0911e80cb8') + FileName + #13#10 +
    CustomMessage('Text_c6a26d8074') + ApproxSize + #13#10 +
    CustomMessage('Text_652e21e125') + Dir + #13#10#13#10 +
    CustomMessage('Text_391e4e86ea') + #13#10 +
    CustomMessage('Text_028133a1bf') + #13#10;
  SaveStringToFile(Dir + '\README.txt', Utf8Encode(Readme), False);

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
      CustomMessage('Text_1b8476bae4'),
      'model_fp16.onnx',
      '~500 MB');

  if WizardIsComponentSelected('ai\remove_background\bria_high_quality') then
    CreateBriaModelAssets(
      ModelsDir,
      'BriaHighQuality',
      CustomMessage('Text_d7650ed9b4'),
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
      CustomMessage('Text_71d35a9d28'),
      CustomMessage('Text_834ff95b7e'));

  ExistingInstallStatusLabel := TNewStaticText.Create(ExistingInstallPage);
  ExistingInstallStatusLabel.Parent := ExistingInstallPage.Surface;
  ExistingInstallStatusLabel.Left := ScaleX(0);
  ExistingInstallStatusLabel.Top := ScaleY(8);
  ExistingInstallStatusLabel.Width := ExistingInstallPage.SurfaceWidth;
  ExistingInstallStatusLabel.Height := ScaleY(160);
  ExistingInstallStatusLabel.AutoSize := False;
  ExistingInstallStatusLabel.WordWrap := True;

  UpdateExistingInstallPageContent();
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (ExistingInstallPage <> nil) and (PageID = ExistingInstallPage.ID) then
  begin
    Result := not HasInstalledVersion and not HasLegacyUserInstall;
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
        CustomMessage('Text_74b47686d0'),
        mbError,
        MB_OK);
      Result := False;
      exit;
    end;

    AiModelsDirEdit.Text := SafeModelsDir;
  end;

end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not SaveStringToFile(ExpandConstant('{app}\language.txt'), CustomMessage('AppLanguage'), False) then
      RaiseException(CustomMessage('LanguageWriteError'));
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

function OpenModelDirectory(const FileName: string; Access, Sharing: Cardinal;
  Security: Integer; Disposition, Flags: Cardinal; Template: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';
function InspectModelDirectory(Handle: THandle; InfoClass: Integer;
  var Info: TModelAttributeTag; Size: Cardinal): Boolean;
  external 'GetFileInformationByHandleEx@kernel32.dll stdcall';
function CloseModelDirectory(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

procedure ReleaseModelDirectoryPath(var Handles: TModelDirectoryHandles);
var
  I: Integer;
begin
  for I := GetArrayLength(Handles) - 1 downto 0 do
    if Handles[I] <> -1 then CloseModelDirectory(Handles[I]);
  SetArrayLength(Handles, 0);
end;

function AcquireModelDirectoryPath(const DirectoryName: string;
  var Handles: TModelDirectoryHandles): Boolean;
var
  Paths: TArrayOfString;
  CurrentPath, ParentPath: string;
  I, Count: Integer;
  Handle: THandle;
  Info: TModelAttributeTag;
begin
  Result := False;
  SetArrayLength(Handles, 0);
  CurrentPath := RemoveBackslashUnlessRoot(ExpandFileName(DirectoryName));
  while CurrentPath <> '' do
  begin
    Count := GetArrayLength(Paths);
    SetArrayLength(Paths, Count + 1);
    Paths[Count] := CurrentPath;
    ParentPath := RemoveBackslashUnlessRoot(ExtractFileDir(CurrentPath));
    if CompareText(CurrentPath, ParentPath) = 0 then break;
    CurrentPath := ParentPath;
  end;
  // Lock from the volume/share root down, and inspect the opened object rather than
  // checking a name that another process could replace before the next operation.
  for I := GetArrayLength(Paths) - 1 downto 0 do
  begin
    Handle := OpenModelDirectory(Paths[I], $80000000, 1, 0, 3, $02200000, 0);
    if Handle = -1 then
    begin
      ReleaseModelDirectoryPath(Handles);
      exit;
    end;
    Count := GetArrayLength(Handles);
    SetArrayLength(Handles, Count + 1);
    Handles[Count] := Handle;
    if not InspectModelDirectory(Handle, 9, Info, 8) or
       ((Info.Attributes and FileAttributeReparsePoint) <> 0) or
       ((Info.Attributes and FileAttributeDirectory) = 0) then
    begin
      ReleaseModelDirectoryPath(Handles);
      exit;
    end;
  end;
  Result := GetArrayLength(Handles) > 0;
end;

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
  if not PathIsSameOrChild(CurrentDirectory, ModelsDir) then
    exit;

  // Inspect ancestors above the configured root as well (a parent may be a junction).
  while CurrentDirectory <> '' do
  begin
    if IsReparsePoint(CurrentDirectory) then
      exit;

    ParentDirectory := RemoveBackslashUnlessRoot(ExtractFileDir(CurrentDirectory));
    if (ParentDirectory = '') or (CompareText(ParentDirectory, CurrentDirectory) = 0) then
    begin
      Result := False;
      exit;
    end;

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
  if IsReparsePoint(AddBackslash(DirectoryName) + ModelDirectoryMarkerFileName) then
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
       (CompareText(FileName, ExpectedName + '.tmp') = 0) or
       (CompareText(FileName, ExpectedName + '.download-lock') = 0) then
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
    Result := 'turbo-encoder.onnx,turbo-decoder.onnx,turbo-tokens.txt,turbo-encoder.weights'
  else if (CompareText(RelativePath, 'pp-ocrv6-tiny-onnx') = 0) or
          (CompareText(RelativePath, 'pp-ocrv6-small-onnx') = 0) then
    Result := 'LICENSE,NOTICE-export-provenance.md,README.md,SHA256SUMS,source_manifest.json,.download-lock'
  else if (CompareText(RelativePath, 'pp-ocrv6-tiny-onnx\det') = 0) or
          (CompareText(RelativePath, 'pp-ocrv6-tiny-onnx\rec') = 0) or
          (CompareText(RelativePath, 'pp-ocrv6-small-onnx\det') = 0) or
          (CompareText(RelativePath, 'pp-ocrv6-small-onnx\rec') = 0) then
    Result := 'inference.onnx,inference.yml,inference.json,README.md';
end;

function IsOcrModelDirectory(const RelativePath: string): Boolean;
begin
  Result := (CompareText(RelativePath, 'pp-ocrv6-tiny-onnx') = 0) or
            (CompareText(RelativePath, 'pp-ocrv6-small-onnx') = 0);
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
        if (FindRec.Attributes and FileAttributeReparsePoint) <> 0 then
          exit;
        if (FindRec.Attributes and FileAttributeDirectory) <> 0 then
        begin
          if not IsOcrModelDirectory(RelativePath) or
             ((FindRec.Name <> 'det') and (FindRec.Name <> 'rec')) or
             not ContainsOnlyExpectedModelFiles(AddBackslash(DirectoryName) + FindRec.Name,
               RelativePath + '\' + FindRec.Name) then
            exit;
        end
        else if not IsExpectedModelFile(RelativePath, FindRec.Name) then
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
  ChildName: string;
  ChildDirectory: string;
  ChildFiles: string;
  Handles, ChildHandles: TModelDirectoryHandles;
begin
  // Do not call DelTree here. A file or subdirectory added after the safety check
  // must remain in place rather than becoming part of a recursive deletion.
  Result := False;
  RemainingFiles := GetExpectedModelFileList(RelativePath);
  if (RemainingFiles = '') or
     not CanDeleteOwnedModelDirectory(ModelsDir, DirectoryName, RelativePath) then
    exit;
  if not AcquireModelDirectoryPath(DirectoryName, Handles) then exit;
  try
    if IsOcrModelDirectory(RelativePath) then
    begin
      ChildFiles := 'det,rec';
      while ChildFiles <> '' do
      begin
        ChildName := GetListItem(ChildFiles);
        ChildDirectory := AddBackslash(DirectoryName) + ChildName;
        if DirExists(ChildDirectory) then
        begin
          if not AcquireModelDirectoryPath(ChildDirectory, ChildHandles) then exit;
          try
            RemainingFiles := GetExpectedModelFileList(RelativePath + '\' + ChildName);
            while RemainingFiles <> '' do
            begin
              if not CanDeleteOwnedModelDirectory(ModelsDir, DirectoryName, RelativePath) then
                exit;
              FileName := GetListItem(RemainingFiles);
              FilePath := AddBackslash(ChildDirectory) + FileName;
              if FileExists(FilePath) and not DeleteFile(FilePath) then exit;
              if FileExists(FilePath + '.tmp') and not DeleteFile(FilePath + '.tmp') then exit;
              if FileExists(FilePath + '.download-lock') and not DeleteFile(FilePath + '.download-lock') then exit;
            end;
          finally
            ReleaseModelDirectoryPath(ChildHandles);
          end;
          if not CanDeleteOwnedModelDirectory(ModelsDir, DirectoryName, RelativePath) or
             not RemoveDir(ChildDirectory) then exit;
        end;
      end;
    end;
    RemainingFiles := GetExpectedModelFileList(RelativePath);

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
      if FileExists(FilePath + '.download-lock') and not DeleteFile(FilePath + '.download-lock') then
        exit;
    end;

    if not CanDeleteOwnedModelDirectory(ModelsDir, DirectoryName, RelativePath) then
      exit;

    FilePath := AddBackslash(DirectoryName) + ModelDirectoryMarkerFileName;
    if FileExists(FilePath) and not DeleteFile(FilePath) then
      exit;

    // RemoveDir is non-recursive. If any foreign content appeared while uninstalling,
    // this call fails closed and the remaining directory is preserved.
    // Only the leaf must be released to remove it; keep every ancestor pinned.
    CloseModelDirectory(Handles[GetArrayLength(Handles) - 1]);
    Handles[GetArrayLength(Handles) - 1] := -1;
    Result := (not DirExists(DirectoryName)) or RemoveDir(DirectoryName);
  finally
    ReleaseModelDirectoryPath(Handles);
  end;
end;

function GetKnownModelDirectories(): string;
begin
  Result :=
    'birefnet_lite-onnx,birefnet_hr-matting-onnx,birefnet_hr-general-onnx,' +
    'RemoveBackground\BriaBalanced,RemoveBackground\BriaHighQuality,' +
    'htdemucs,htdemucs-split,deepfilternet3_onnx,rife,lama-onnx,lama-opencv-onnx,' +
    'upscale-image-onnx,upscale-video-onnx,whisper-base-onnx,whisper-small-onnx,' +
    'whisper-large-v3-turbo-onnx,pp-ocrv6-tiny-onnx,pp-ocrv6-small-onnx';
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
        CustomMessage('Text_219946869b') + #13#10#13#10 +
        CustomMessage('Text_6628d02a16') + #13#10#13#10 +
        CustomMessage('Text_adcadd0004') + ModelsDir,
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
        CustomMessage('Text_1aa09e20ca') + #13#10#13#10 +
        CustomMessage('Text_adcadd0004') + LogsDir,
        mbConfirmation,
        MB_YESNO) = IDYES then
      begin
        DelTree(LogsDir, True, True, True);
      end;
    end;
  end;
end;
