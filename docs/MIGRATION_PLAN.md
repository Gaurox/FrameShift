# FrameShift Migration Plan

## Statut

La migration initiale FFActions -> FrameShift est considérée comme terminée.

Le projet actif n’est plus un squelette de migration :
- la structure `src/FrameShift/` est la base réelle ;
- les actions principales sont branchées ;
- l’installateur Inno Setup est aligné sur la surface active ;
- les anciens projets sous `references/` restent de la lecture seule.

Ce document sert désormais de rappel d’architecture et de garde-fou post-migration, pas de checklist d’action à migrer une par une.

## Ce qui est stabilisé

Base active validée :
- `Program.cs` pour le launcher, le parsing CLI et l’ouverture des pickers ;
- `ActionRegistry.cs` pour la surface des actions core ;
- `FfmpegRunner.cs` pour tous les appels FFmpeg ;
- `FfprobeRunner.cs` pour tous les appels FFprobe, y compris Media Info ;
- `ProgressForm.cs` pour la progression partagée des actions classiques et de `remove-background` ;
- `DownloadModelForm.cs` comme downloader IA partagé ;
- `installer/FrameShift.iss` pour le packaging et l’intégration Explorer.

UI partagée active :
- `FrameShiftTheme.cs`
- `FrameShiftUiMetrics.cs`
- `FrameShiftUiLayout.cs`
- `FrameShiftUiFactory.cs`
- `FrameShiftEditorShellUi.cs`
- `FrameShiftCropEditorUi.cs`
- `FrameShiftWindowChrome.cs`

## Schéma réel des actions

Le modèle dominant est :

1. entrée `Program.cs`
2. ouverture éventuelle d’un formulaire WinForms
3. sérialisation des options dans `ActionOptionKeys`
4. validation et exécution par une action core

Ce schéma couvre aussi bien :
- les actions batch avec options partagées ;
- les actions mono-fichier avec picker ;
- les modules interactifs comme `Image to PDF`.

## État batch réel

Batch avec file Windows active :
- `convert-video`
- `convert-audio`
- `convert-image`
- `extract-audio`
- `extract-frames`

Batch différé avec picker avant progression :
- `convert-video`
- `convert-audio`
- `convert-image`
- `extract-audio`

`extract-frames` peut rejoindre la file batch mais n’a pas de picker partagé.

Les autres actions restent principalement mono-fichier, soit par dépendance UI, soit parce que le contrat batch n’a pas encore été durci.

`remove-background` suit maintenant le même principe de progression partagée que les autres lots :
- une seule fenêtre de progression ;
- file visible ;
- erreurs reportées dans la queue ;
- préflight du modèle avant lancement si nécessaire ;
- continuation du batch sur fichier corrompu.

`separate-audio` suit aussi ce schéma :
- picker si les stems ou le moteur ne sont pas déjà fournis ;
- préflight du modèle CPU ou GPU selon le routage demandé ;
- progression commune ;
- continuation du batch sur erreur fichier.

## Exceptions assumées

`Image to PDF` :
- module interactif ;
- routé par `FrameShift.exe` ;
- UI dédiée ;
- export final core ;
- support WebP actif via FFmpeg côté launcher/UI et validation core alignée.

`Media Info` :
- action visible côté produit ;
- probe via `FfprobeRunner.TryProbeMediaInfoAsync(...)` ;
- rendu via `MediaInfoFormatter` ;
- affichage WinForms requis.

## Dette restante

La migration initiale est finie, mais il reste de la consolidation :
- documentation à maintenir au rythme du code ;
- couverture de tests encore partielle sur certains branchements ;
- certaines actions ont une entrée CLI mais pas une couverture headless complète ;
- duplication résiduelle légère dans les scripts, helpers ou formulaires.

## Règle de travail post-migration

À partir de maintenant :
- le code actif fait foi ;
- `references/` sert uniquement de contexte ;
- toute nouvelle capacité doit être branchée jusqu’au publish et à l’installateur ;
- toute promesse documentaire doit être vérifiée contre `Program.cs`, `ActionRegistry.cs` et `installer/FrameShift.iss`.
