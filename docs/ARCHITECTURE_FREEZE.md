# FrameShift Architecture Freeze

Décisions techniques stables tant qu’une révision explicite n’est pas validée.

## Versioning

- format : `1.<version fonctionnelle>.<correctif>` ;
- nouvelle fonctionnalité : dernier nombre remis à `0` ;
- petit correctif : incrément du dernier nombre ;
- version active : `1.19.0`.

## Stack figée

- langage : C#
- framework : .NET 8 LTS
- UI : WinForms
- installateur : Inno Setup
- backend média : FFmpeg / FFprobe
- packaging : self-contained `win-x64`
- IA locale : ONNX Runtime DirectML

## Frontière Core / Windows

Core :
- actions ;
- validation ;
- exécution FFmpeg / FFprobe ;
- logs ;
- nommage des sorties ;
- contrats de progression ;
- parsing des settings métiers.

Windows :
- WinForms ;
- dialogues ;
- intégration Explorer ;
- batching Windows ;
- progression visuelle ;
- launcher.

Le core ne manipule pas directement WinForms.

## Exécution média figée

Tous les appels FFmpeg passent par :
- `src/FrameShift/Core/FFmpeg/FfmpegRunner.cs`

Tous les appels FFprobe passent par :
- `src/FrameShift/Core/FFprobe/FfprobeRunner.cs`

Paramètres de process requis :
- `UseShellExecute = false`
- `CreateNoWindow = true`
- `RedirectStandardOutput = true`
- `RedirectStandardError = true`

Progression FFmpeg :
- `-progress pipe:1`
- `-nostats`

## Nommage des sorties

- jamais d’écrasement ;
- suffixes `_001`, `_002`, etc. ;
- sortie adjacente à la source par défaut ;
- nettoyage des sorties partielles sur échec ou annulation.

## Décision UI

WinForms reste la solution retenue.

Ne pas migrer vers :
- WPF
- MAUI
- Avalonia
- Electron
- Blazor

sans validation explicite.

## Compatibilité CLI : état réel figé

Le projet conserve un point d’entrée CLI commun :

```text
FrameShift.exe --action <id> [options] <input-paths...>
```

Implémentation active :
- le launcher reste centré sur `Program`, mais ce code est désormais réparti en plusieurs fichiers `partial` pour séparer bootstrap, parsing CLI, batch/progression, préflight IA, `Image to PDF` et pickers.

Mais cela ne signifie pas “headless complet” pour toutes les actions.

État réel :
- certaines actions sont réellement pilotables en CLI si toutes les options sont fournies ;
- certaines actions ouvrent une UI de configuration si les options nécessaires manquent ;
- certaines actions restent volontairement UI-first.

UI-first ou dépendantes d’un formulaire :
- `media-info`
- `image-to-pdf`
- `remove-object`

Actions avec entrée CLI mais couverture headless partielle selon les options fournies :
- `add-subtitles-video`
- `join-videos` (UI automatique sans option ; `--join-mode auto|copy|normalize` force le pipeline CLI)
- `cut-audio`
- `cut-video`
- `create-gif`
- `convert-to-icon`
- `crop-video`
- `crop-image`
- `rotate-flip-image`
- `rotate-flip-video`
- `resize-video`
- `resize-image`
- `compress-video`
- `compress-audio`
- `compress-image`
- `change-pitch`
- `change-audio-speed`
- `change-video-speed`
- `interpolate-video`
- `interpolate-video-rife`
- `upscale-video`
- `remove-noise`
- `remove-noise-video`
- `remove-background` s’appuie sur la progression commune, avec file visible, annulation propre et préflight du modèle si nécessaire.
- `separate-audio`

Actions les plus proches d’un mode batch/CLI stable :
- `convert-video`
- `convert-audio`
- `convert-image`
- `extract-audio`
- `extract-frames`
- `remove-audio`
- `reverse-audio`
- `remove-background`

`extract-frames` accepte `--frame-mode all|first|last|keyframes`; l’absence de cette option conserve strictement son comportement historique `all`.

La documentation ne doit donc pas promettre “compatibilité CLI complète” sans préciser ce niveau.

## IA locale : état réel figé

Modules IA actifs :
- `remove-background`
- `remove-noise`
- `remove-noise-video`
- `separate-audio`
- `interpolate-video-rife`
- `remove-object`
- `upscale-image`
- `upscale-video`
- `create-subtitles-audio`
- `create-subtitles-video`

Règles stables :
- pas de modèle embarqué dans Git ni dans l’installateur ;
- téléchargement du modèle au moment utile, pas à l’ouverture simple d’une UI ;
- vérification d’intégrité du modèle dans le flux de préflight ou de téléchargement quand le module le prévoit ;
- l’installateur place toujours FFmpeg, FFprobe et tout le dossier `Workers\CreateSubtitlesWorker` (y compris ses DLL natives) avec le composant fixe `core` ; les composants optionnels pilotent uniquement les menus Explorer et ne conditionnent jamais la disponibilité d’une action dans la fenêtre principale ;
- le dossier de modèles personnalisé reste pris en charge, mais une racine de volume, un profil, Windows, Program Files, `{app}` ou l’un de leurs parents est refusé ; à la désinstallation, FrameShift ne supprime jamais cette racine ni récursivement un dossier de modèles : il retire seulement les fichiers explicitement connus dans un dossier marqué comme créé par FrameShift, puis ne retire ce dossier que s’il est vide ;
- `remove-background` conserve `fast` comme comportement par défaut et expose trois modèles via option CLI / menu Explorer sans dupliquer l’action ;
- dans l’état figé de `1.0.11`, les deux modèles `high-resolution` tournent volontairement en **CPU only** ; seul `fast` reste en `DirectML` avec fallback CPU ;
- les boucles CPU pré/post de `BackgroundRemovalEngine` utilisent `ProcessPixelRows` + accès direct aux buffers de `DenseTensor` (construction tenseur d’entrée, construction masque, composite) ; gain mesuré ×5–×22 sur ces phases pour les grandes images via le chemin `fast`/Bria ; les chemins `high-resolution` restent bornés par l’inférence CPU, le gain pré/post y est négligeable ; sortie vérifiée bit-à-bit identique ;
- la file batch WinForms doit accepter les relances tardives d’une action déjà ouverte comme des requêtes indépendantes, même quand plusieurs invocations ciblent exactement le même chemin source ;
- `upscale-image` (Upscale Image 4x) : mini-catalogue de 3 modèles (Real-ESRGAN x4plus défaut / Real-ESRGAN Anime 6B / Swin2SR Quality) choisis via un picker UI-first (`UpscaleImagePickerForm`), `--upscale-model <id>` en headless ; `DirectML` avec fallback CPU, tiling automatique (tuile 512, overlap, réduction adaptative 512→256→128 en OOM) ; le moteur gère par-modèle les noms de tenseurs et la contrainte multiple-de-fenêtre (Swin2SR = pad multiple de 8 + crop) ; échelle paramétrable x2/x3/x4 + taille cible (aspect verrouillé) via passe x4 native puis rééchantillonnage Lanczos clampé à ≤ x4 ; sortie PNG `_upscaled_<facteur ou WxH>` ; modèles hébergés sur Gaurox/frameshift-models avec SHA256 pinné/vérifié + README/licences ; l'auto-download est bloqué net si un checksum est laissé en placeholder ;
- les artefacts upscale sont séparés par action : `upscale-image-onnx/` (x4plus, Anime 6B, Swin2SR) et `upscale-video-onnx/` (General v3, AnimeVideo v3, copie x4plus Quality), chacun avec README et licences autonomes sur Hugging Face et dans le stockage local. `ModelLocator` copie les anciens fichiers valides depuis `upscale-onnx/` pour compatibilité ;
- `upscale-video` partage `UpscaleModelCatalog`, `ModelDownloader`, `UpscaleFrameProcessor` et le tuilage avec `upscale-image`, mais pas son dossier d'artefacts. Par défaut, le pipeline FFmpeg suit désormais une voie mémoire `rawvideo` proche de RIFE : décodage direct en mémoire, traitement frame par frame avec session ONNX + buffers réutilisés, puis réencodage au FPS source. L'ancien pipeline extraction BMP → traitement → réencodage reste disponible comme fallback automatique si le mode mémoire échoue ; audio copié puis transcodé si nécessaire, fallback NVENC → CPU, progression, annulation et nettoyage. Pour `realesr-animevideov3`, FrameShift conserve une seule entrée utilisateur mais choisit en interne la variante d'exécution x2 / x3 / x4 selon la demande ; le profilage de ce pipeline (juin 2026) le montre très majoritairement limité par l'inférence ONNX DirectML — les copies, conversions et I/O FFmpeg représentent une part négligeable du temps total —, donc aucune optimisation CPU ne déplace le temps mur ; seul un `.Clone()` plein-frame redondant a été retiré de `UpscaleFrameProcessor` (pic mémoire ~−23 MB sur un clip représentatif), sans changement de modèle, de qualité ni de comportement et avec sortie vérifiée bit-à-bit identique ;
- `create-subtitles-audio` / `create-subtitles-video` (libellé utilisateur : **Create Subtitle File**) : trois modèles sélectionnables via `CreateSubtitlesPickerForm` (radio buttons) ou `--subtitles-model <id>` : `whisper-base` (~280 MB, 3 artefacts), `whisper-small` (~925 MB, 3 artefacts, **défaut**), `whisper-turbo` (~3,1 GB, 4 artefacts dont `turbo-encoder.weights` en ONNX external data). Le picker expose aussi 3 sorties exclusives : **Standard SRT** (défaut), **Advanced ASS Subtitle** et **FrameShift Customization Project** ; en headless, `--subtitles-format <srt|ass|project>` / `--subtitles-output-format <...>` sélectionne la sortie. Quand **Advanced ASS Subtitle** est sélectionné, un choix exclusif de preset apparaît : **Classic** (défaut), **Word Highlight**, **Progressive Reveal** ; en headless, `--subtitles-ass-preset <classic|word-highlight|progressive-reveal>` / `--ass-preset <...>` sélectionne le preset. Les presets dynamiques s’appuient sur les timings mot à mot fiables du modèle interne `SubtitleProject`, peuvent retarder conservativement le début d’affichage via `RefinedDisplayStart`, et retombent automatiquement sur `Classic` si le segment n’a pas d’alignement fiable. En `Word Highlight`, la phrase complète apparaît dès le premier mot utile ; `Progressive Reveal` conserve sa révélation progressive. Catalog dans `CreateSubtitlesModelCatalog`, `Artifacts[0..2]` = chemins sherpa, `Artifacts[3+]` = fichiers extra copiés par le workaround ASCII. Les deux actions restent séparées dans l’installateur et l’Explorer mais partagent strictement le même fenêtrage `< 30 s`, la même fusion, le même modèle interne `SubtitleProject` et le même downloader. Inférence Whisper isolée dans `FrameShift.SubtitlesWorker` (worker process dédié) pour éviter la collision native `onnxruntime.dll`. SRT reste la sortie par défaut ; aucun nouvel éditeur avancé ni famille d’effets ASS supplémentaire n’est activé dans cet état figé.
- `add-subtitles-video` reste une action vidéo produit unique, non IA, enregistrée dans `ActionRegistry` et intégrée aux surfaces Windows habituelles : launcher `--action add-subtitles-video`, fallback picker/UI, composant installateur vidéo dédié et entrée Explorer pour les extensions vidéo. Le mode `Selectable Subtitle Track` conserve la vidéo/audio sans réencodage dans le cas nominal ; le mode `Burn Subtitles Into Video` reste mono-fichier, UI-first quand les options manquent, génère un ASS de travail temporaire, réencode la vidéo via `FfmpegRunner`, sonde via `FfprobeRunner`, sort à côté de la source avec nommage unique, et nettoie les sorties partielles ainsi que les fichiers temporaires de preview/export sur annulation ou échec.
- `join-videos` est une action vidéo mono-sortie/mono-piste : une timeline WinForms simple porte l'ordre, les répétitions de même chemin restent des occurrences distinctes, et l'action est exécutée une seule fois dans la progression commune. Le concat direct passe seulement avec signatures FFprobe strictement compatibles ; sinon le mode automatique normalise exclusivement du SDR vers MP4/H.264/AAC, en prenant géométrie/orientation du premier clip et en complétant les clips sans audio par silence. HDR mélangé ou à normaliser est refusé en V1. L'entrée Explorer utilise `MultiSelectModel=Player`, puis le mutex/pipe local déjà utilisé par les actions combinées pour agréger les invocations ; aucune promesse d'ordre de sélection Explorer n'est faite.
- intégration Explorer dédiée sous `FrameShift AI` ;
- barre de titre des fenêtres IA fixée sur l’icône `FrameShift AI` ;
- icônes de fonction IA dédiées centralisées dans `Assets\Icons\ai` pour les bandeaux internes et les menus Explorer.

## Linux

Linux n’est pas une cible active.

Règle :
- ne pas ralentir ou complexifier Windows pour préparer Linux ;
- garder le core portable seulement quand cela reste gratuit et lisible.

## Build workflow figé

Après modification de code :
- `dotnet build src/FrameShift/FrameShift.csproj`

Avant test via installateur ou menu contextuel :
- `.\build_installer.ps1`

`build_installer.ps1` est l'unique chaîne de release : validation des entrées, restore verrouillé, tests Release, nettoyage limité à `publish\FrameShift-win-x64`, publish `win-x64` self-contained, contrôle du payload, puis compilation Inno Setup. Les anciens scripts sont uniquement des wrappers de compatibilité ; aucune autre commande n'est une méthode de release officielle.
