# FrameShift Architecture Freeze

Décisions techniques stables tant qu’une révision explicite n’est pas validée.

## Stack figée

- langage : C#
- framework : .NET 8 LTS
- UI : WinForms
- installateur : Inno Setup
- backend média : FFmpeg / FFprobe
- packaging : self-contained `win-x64`
- IA future : ONNX Runtime DirectML

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

Mais cela ne signifie pas “headless complet” pour toutes les actions.

État réel :
- certaines actions sont réellement pilotables en CLI si toutes les options sont fournies ;
- certaines actions ouvrent une UI de configuration si les options nécessaires manquent ;
- certaines actions restent volontairement UI-first.

UI-first ou dépendantes d’un formulaire :
- `media-info`
- `image-to-pdf`

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
- `remove-background` s’appuie sur la progression commune, avec file visible, annulation propre et préflight du modèle si nécessaire.

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
