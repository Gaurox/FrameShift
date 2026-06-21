# FrameShift Architecture Freeze

Décisions techniques stables tant qu’une révision explicite n’est pas validée.

## Versioning

- format : `1.<version fonctionnelle>.<correctif>` ;
- nouvelle fonctionnalité : dernier nombre remis à `0` ;
- petit correctif : incrément du dernier nombre ;
- version active : `1.14.0`.

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

Règles stables :
- pas de modèle embarqué dans Git ni dans l’installateur ;
- téléchargement du modèle au moment utile, pas à l’ouverture simple d’une UI ;
- vérification d’intégrité du modèle dans le flux de préflight ou de téléchargement quand le module le prévoit ;
- `remove-background` conserve `fast` comme comportement par défaut et expose trois modèles via option CLI / menu Explorer sans dupliquer l’action ;
- dans l’état figé de `1.0.11`, les deux modèles `high-resolution` tournent volontairement en **CPU only** ; seul `fast` reste en `DirectML` avec fallback CPU ;
- la file batch WinForms doit accepter les relances tardives d’une action déjà ouverte comme des requêtes indépendantes, même quand plusieurs invocations ciblent exactement le même chemin source ;
- `upscale-image` (Upscale Image 4x) : mini-catalogue de 3 modèles (Real-ESRGAN x4plus défaut / Real-ESRGAN Anime 6B / Swin2SR Quality) choisis via un picker UI-first (`UpscaleImagePickerForm`), `--upscale-model <id>` en headless ; `DirectML` avec fallback CPU, tiling automatique (tuile 512, overlap, réduction adaptative 512→256→128 en OOM) ; le moteur gère par-modèle les noms de tenseurs et la contrainte multiple-de-fenêtre (Swin2SR = pad multiple de 8 + crop) ; échelle paramétrable x2/x3/x4 + taille cible (aspect verrouillé) via passe x4 native puis rééchantillonnage Lanczos clampé à ≤ x4 ; sortie PNG `_upscaled_<facteur ou WxH>` ; modèles hébergés sur Gaurox/frameshift-models avec SHA256 pinné/vérifié + README/licences ; l'auto-download est bloqué net si un checksum est laissé en placeholder ;
- les artefacts upscale sont séparés par action : `upscale-image-onnx/` (x4plus, Anime 6B, Swin2SR) et `upscale-video-onnx/` (General v3, AnimeVideo v3, copie x4plus Quality), chacun avec README et licences autonomes sur Hugging Face et dans le stockage local. `ModelLocator` copie les anciens fichiers valides depuis `upscale-onnx/` pour compatibilité ;
- `upscale-video` partage `UpscaleModelCatalog`, `ModelDownloader`, `UpscaleFrameProcessor` et le tuilage avec `upscale-image`, mais pas son dossier d'artefacts. Le pipeline FFmpeg suit RIFE : extraction BMP, traitement frame par frame avec session ONNX réutilisée, réencodage au FPS source, audio copié puis transcodé si nécessaire, fallback NVENC → CPU, progression, annulation et nettoyage ;
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
- `dotnet publish src/FrameShift/FrameShift.csproj -c Release -r win-x64 --self-contained true`
- recompilation Inno Setup ensuite

Raison :
- éviter de tester un ancien binaire ;
- garder une chaîne claire entre code, publish et setup.
