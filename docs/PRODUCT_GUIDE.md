# FrameShift Product Guide

Version active : **1.16.1**.

Numérotation : `1.<version fonctionnelle>.<correctif>`. Une fonctionnalité démarre à `.0`; les petits
correctifs incrémentent le dernier nombre (`1.14.1`, `1.14.2`, etc.).

## Positioning

FrameShift est un utilitaire Windows de traitement multimédia local.

Le produit vise des opérations fréquentes, rapides et offline :
- conversion ;
- compression ;
- extraction ;
- coupe ;
- recadrage ;
- redimensionnement ;
- rotation / flip ;
- inspection technique ;
- petits workflows batch.
- IA locale optionnelle.

Le projet reste l’évolution de FFActions, mais le code actif est désormais une application .NET/WinForms unique.

## Surface active réelle

Actions vidéo :
- `convert-video`
- `compress-video`
- `remove-audio`
- `extract-audio`
- `extract-frames`
- `create-gif`
- `cut-video`
- `crop-video`
- `resize-video`
- `rotate-flip-video`
- `change-video-speed`
- `interpolate-video`
- `interpolate-video-rife`
- `add-subtitles-video`
- `upscale-video`
- `remove-noise-video`
- `create-subtitles-video`
- `media-info`

Actions audio :
- `convert-audio`
- `compress-audio`
- `cut-audio`
- `reverse-audio`
- `change-pitch`
- `change-audio-speed`
- `remove-noise`
- `separate-audio`
- `create-subtitles-audio`
- `media-info`

Actions image :
- `convert-image`
- `compress-image`
- `convert-to-icon`
- `crop-image`
- `resize-image`
- `rotate-flip-image`
- `image-to-pdf`
- `remove-background`
- `remove-object`
- `upscale-image`
- `media-info`

Actions IA locales :
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

Règles communes des actions IA :
- téléchargement du modèle seulement au moment utile ;
- préflight modèle avant traitement si nécessaire ;
- vérification d’intégrité du modèle par SHA256 quand le flux le prévoit ;
- progression WinForms cohérente avec le reste du produit ;
- sorties adjacentes au fichier source ;
- menus contextuels Explorer dédiés.

## Modes d’usage réels

Batch / progression commune :
- `convert-video`
- `convert-audio`
- `convert-image`
- `extract-audio`
- `extract-frames`
- `remove-background`
- `upscale-image`
- `upscale-video`

Mono-fichier interactif :
- `cut-audio`
- `cut-video`
- `create-gif`
- `crop-video`
- `crop-image`
- `convert-to-icon`
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
- `add-subtitles-video`
- `remove-noise`
- `remove-noise-video`
- `image-to-pdf`
- `media-info`

Recadrage visuel actuel :
- `crop-image` propose maintenant un auto-crop, un zoom molette, un retour `Fit` et un déplacement de la feuille au clic-glissé ;
- `crop-video` reprend la même logique visuelle, avec auto-crop calculé sur la frame de travail sélectionnée puis appliqué à toute la vidéo.

UI commune :
- la fenêtre de progression partagée affiche aussi un bandeau donation discret avec bouton `Donate` ;
- ce bandeau n'interrompt jamais le traitement et peut être fermé pour la session.

## Réalité CLI actuelle

Toutes les actions ont un point d’entrée `FrameShift.exe --action <id> ...`, mais elles ne sont pas toutes entièrement headless.

En pratique :
- certaines actions acceptent des options CLI complètes et peuvent s’exécuter sans formulaire ;
- certaines actions ouvrent un picker si les options attendues ne sont pas déjà fournies ;
- `media-info` reste dépendant d’une fenêtre WinForms ;
- `image-to-pdf` reste un éditeur interactif, même si l’action finale est exécutée côté core ;
- `remove-background` passe par la progression commune WinForms : une seule fenêtre, file visible, erreurs dans la queue, progression régulière pendant les longues inférences CPU et préflight modèle si le modèle manque ;
- `remove-background` supporte maintenant trois variantes de modèle via CLI et menus Explorer : `fast` par défaut, `high-resolution` pour le rendu HR matting, et `high-resolution-general` pour la variante HR segmentation ;
- dans `1.0.11`, les deux variantes haute résolution sont volontairement exécutées en CPU only ; `fast` reste la variante `DirectML` quand le GPU est compatible ;
- `remove-background` expose en plus deux variantes optionnelles **BRIA RMBG-2.0** (`bria-balanced` → `model_fp16.onnx` ~500 MB, `bria-high-quality` → `model.onnx` ~1 GB) ; ces modèles sont **fournis par l'utilisateur** : FrameShift ne les télécharge jamais, ne les héberge pas et ne les redistribue pas. L'utilisateur doit les récupérer manuellement depuis la page officielle BRIA (`https://huggingface.co/briaai/RMBG-2.0/tree/main`) et les déposer dans le dossier modèle correspondant. Composants installeur optionnels, décochés par défaut. Usage non commercial uniquement (CC BY-NC 4.0) ; si le fichier est absent ou ne correspond pas au checksum officiel, FrameShift affiche un popup dédié (Open BRIA page / Open folder / Re-check / Cancel, + Use anyway sur mismatch) au lieu de télécharger quoi que ce soit ; le bouton **Re-check** revérifie le modèle en place et enchaîne directement sur l'action si le bon fichier est désormais présent ;
- les relances `remove-background` pendant qu'une fenêtre de progression est déjà ouverte sont maintenant traitées comme des demandes distinctes dans la file visible, y compris si l'utilisateur relance exactement le même fichier source ;
- `remove-noise` et `remove-noise-video` utilisent un picker de force et des options audio adaptées au média source ;
- `separate-audio` suit le même modèle avec fallback picker si `--stems` ou `--separate-engine` ne sont pas fournis ;
- `interpolate-video-rife` suit un flux UI-first avec picker de modèle/multiplicateur/vitesse puis préflight du modèle avant traitement ;
- `remove-object` est un éditeur visuel UI-first (canvas + masque) : préflight et téléchargement du modèle gérés dans l'éditeur, sortie `_cleaned.png` adjacente à la source ; catalogue extensible avec deux modèles disponibles : **LaMa FP32 (Quality)** (~208 MB) et **LaMa 2025 (Fast)** (~93 MB, opencv/inpainting_lama Jan 2025) ;
- `upscale-image` agrandit une image **x4** ; une seule entrée Explorer ouvre un **picker de modèle** (choix exclusif, style FrameShift), `--upscale-model <id>` court-circuite le picker en headless. Trois modèles hébergés dans le dossier dédié `Gaurox/frameshift-models/upscale-image-onnx/` (SHA256 vérifié, README et licences BSD-3/Apache-2.0 propres au dossier) : **Real-ESRGAN x4plus** (général, défaut), **Real-ESRGAN Anime 6B** et **Swin2SR (Quality)**. Le picker propose x2/x3/x4 et une taille cible ; passe x4 native puis Lanczos, nommage unique, DirectML → CPU et tuilage adaptatif restent inchangés ;
- `upscale-video` partage le moteur et le downloader, mais utilise exclusivement `Gaurox/frameshift-models/upscale-video-onnx/` et son dossier local homonyme. Le picker propose **Real-ESRGAN General v3** (défaut), **AnimeVideo v3** et l'entrée distincte **x4plus Quality** (`realesrgan-x4plus-video`), en x2/x3/x4 ou taille cible. Le chemin principal passe maintenant par un pipeline FFmpeg `rawvideo` en mémoire ; si nécessaire, FrameShift retombe automatiquement sur l'ancien pipeline BMP. Pour **AnimeVideo v3**, FrameShift garde une seule entrée visible mais route automatiquement les demandes x2/x3 vers des variantes ONNX dédiées afin d’éviter le downscale CPU précédent. FPS et audio sont conservés ; DirectML retombe sur CPU, NVENC sur libx264, et un audio incompatible est transcodé. Les modèles valides de l'ancien dossier local `upscale-onnx` sont copiés automatiquement vers le nouveau dossier pour éviter un nouveau téléchargement. Le traitement image par image peut laisser un léger scintillement temporel sur certaines sources bruitées ;
- `create-subtitles-audio` et `create-subtitles-video` partagent le même pipeline Whisper via un worker isolé (`FrameShift.SubtitlesWorker`) ; DirectML avec fallback CPU automatique (init et inférence) ; trois modèles : `whisper-base`, `whisper-small` (défaut), `whisper-turbo` (~3,1 GB). Le picker expose trois formats de sortie exclusifs : `Standard SRT` (défaut), `Advanced ASS Subtitle`, `FrameShift Customization Project`. Quand `Advanced ASS Subtitle` est choisi, un preset exclusif apparaît : `Classic` (défaut), `Word Highlight`, `Progressive Reveal`. Headless : `--subtitles-model <id>`, `--subtitles-format <srt|ass|project>` et `--subtitles-ass-preset <classic|word-highlight|progressive-reveal>`. Les presets dynamiques utilisent les timings mot à mot fiables du `SubtitleProject`, appliquent au besoin un début d’affichage raffiné conservateur, et retombent automatiquement sur `Classic` si l’alignement du segment n’est pas fiable. En `Word Highlight`, la phrase complète apparaît dès le premier mot utile ; `Progressive Reveal` reste progressif. Limitation Turbo connue : détection langue renvoie vide (mel 80 vs 128), transcription correcte ;
- `add-subtitles-video` supporte maintenant deux modes. `Selectable Subtitle Track` ajoute un `.srt` externe comme piste activable : `MKV` garde une piste native `subrip`, `MP4/MOV/M4V` utilisent `mov_text` quand les flux existants restent compatibles, sinon le flux retombe sur `MKV`. `Burn Subtitles Into Video` accepte `.srt`, `.ass` et `.frameshift-subtitles.json`, génère au besoin un `.ass` temporaire adapté à la résolution vidéo, réencode la vidéo et copie l’audio quand le conteneur le permet. Ce mode ouvre un éditeur visuel DPI-safe avec aperçu d’une frame réelle, navigation temporelle simple, réglages de style principaux, aperçu animé court et rendu d’aperçu debouncé via `FFmpeg/libass`. Les presets ASS partagés gardent le même comportement que `Create Subtitle File` : aucun affichage pendant le silence précédent, `Word Highlight` montre immédiatement la phrase complète au début utile, et `Progressive Reveal` reste progressif. Les `.ass` externes restent en passthrough avec leurs réglages désactivés. L’action est câblée comme action vidéo produit complète : registre, launcher, CLI, menu Explorer vidéo et composant installateur dédié.
- plusieurs actions de géométrie ou de vitesse ont un modèle `CLI entry + UI fallback`, pas une couverture CLI complète documentable comme “headless garanti”.

## Règles produit qui restent vraies

- traitement local uniquement ;
- sorties adjacentes au fichier source ;
- nommage unique `_001`, `_002`, etc. ;
- pas d’écrasement de fichier ;
- UI WinForms légère ;
- progression commune partagée pour les actions de lot, y compris `remove-background` ;
- progression commune partagée aussi pour les actions IA qui passent par la queue visible ou un traitement long ;
- bandeau donation discret dans la fenêtre de progression commune ;
- annulation et nettoyage propres ;
- synchronisation obligatoire entre code, publish, installateur et menus contextuels.

## Ce que FrameShift ne cherche pas à devenir

- un éditeur timeline ;
- une suite vidéo lourde ;
- une plateforme cloud ;
- un framework de plugins ;
- une architecture enterprise.
