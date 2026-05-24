# FrameShift Project Rules

## Purpose

FrameShift est l'évolution moderne de FFActions.

Le projet doit rester :
- simple ;
- stable ;
- maintenable ;
- offline ;
- réactif ;
- compréhensible ;
- pratique.

Le but n'est pas de créer un framework enterprise.

## Product Position

FrameShift est :
- un utilitaire desktop ;
- un outil de workflow rapide ;
- une application de traitement multimédia.

FrameShift n'est pas :
- une plateforme cloud ;
- une application web ;
- une expérimentation d'architecture enterprise ;
- une grosse couche d'abstraction.

## Priorities

Ordre de priorité :

1. stabilité
2. maintenabilité
3. UX Windows
4. fiabilité FFmpeg
5. réactivité
6. annulation propre
7. lisibilité du code
8. portabilité seulement si elle reste simple et peu coûteuse

À éviter :
- over-engineering
- abstractions inutiles
- optimisation prématurée
- frameworks inutiles

## Mandatory Stack

- langage : C#
- framework : .NET 8 LTS
- UI : WinForms
- packaging : self-contained `win-x64`
- installateur : Inno Setup
- moteur média : FFmpeg / FFprobe
- IA locale : ONNX Runtime DirectML

## Forbidden Decisions

Ne pas introduire sans validation explicite :
- MVVM
- conteneurs d'injection de dépendances
- service locators
- frameworks enterprise
- WPF
- MAUI
- Avalonia
- Electron
- Blazor

## Windows First

Windows est la cible principale.

Linux est seulement une possibilité future.

Ne pas ralentir le développement Windows pour préparer Linux trop tôt.

## Core / Windows Separation

Core :
- exécution FFmpeg
- exécution FFprobe
- logique d'actions
- nommage des sorties
- logs
- validation
- file d'attente
- annulation
- presets
- contrats de progression

Windows :
- UI WinForms
- dialogues
- fenêtre de progression
- intégration Explorer
- comportement du launcher

Les actions de lot doivent privilégier la fenêtre de progression commune `ProgressForm` plutôt que créer des fenêtres dédiées, sauf besoin UX explicite et validé.

Le core ne doit jamais manipuler directement des contrôles WinForms.

## References Folder Rule

Les dossiers de `references/` sont strictement en lecture de référence.

Usages autorisés :
- lire l'ancien code ;
- inspecter l'ancienne architecture ;
- récupérer scripts, assets, icônes ou binaires ;
- recopier ensuite ce qui est utile dans la vraie structure FrameShift.

Usages interdits :
- dépendances runtime ;
- source installateur ;
- localisation active des outils.

## FFmpeg / FFprobe Rules

Tous les appels FFmpeg passent par :
- `src/FrameShift/Core/FFmpeg/FfmpegRunner.cs`

Tous les appels FFprobe passent par :
- `src/FrameShift/Core/FFprobe/FfprobeRunner.cs`

Paramètres de process requis :
- `UseShellExecute = false`
- `CreateNoWindow = true`
- `RedirectStandardOutput = true`
- `RedirectStandardError = true`

Stratégie de progression :
- `-progress pipe:1`
- `-nostats`

## Output Rules

Ne jamais écraser un fichier existant.

Toujours générer un nom unique :
- `_001`
- `_002`
- etc.

Par défaut, les sorties restent à côté du fichier source.

## UI Rules

L'UI doit rester :
- simple ;
- stable ;
- réactive ;
- lisible.

À éviter :
- interfaces géantes ;
- animations excessives ;
- dialogues trop imbriqués.

La chrome Windows des fenêtres d'action doit rester centralisée :
- titre standard `FrameShift - <fonction>` ;
- icône de barre de titre et de barre des tâches = icône globale FrameShift ;
- bandeau interne de la fonction = séparé et géré par les helpers UI partagés.

Un bandeau donation discret peut être partagé par la fenêtre de progression commune sans bloquer le traitement ni changer la logique de queue.

## Code Style

Préférer :
- petites classes ciblées ;
- méthodes lisibles ;
- noms pratiques ;
- logique explicite.

Éviter :
- abstract factories ;
- architecture trop générique ;
- interfaces partout.

Si une classe simple suffit, utiliser une classe simple.

## Runtime Validation

Chaque action migrée doit valider :
- chemins avec espaces ;
- chemins avec accents ;
- annulation ;
- nettoyage sur échec ;
- aucun process FFmpeg orphelin ;
- nommage unique ;
- pas de console visible ;
- logs lisibles.

## Completion Rule

Après chaque modification de code, rebuild avant de considérer la tâche terminée.

## Build Discipline

Règle obligatoire pour toute modification faite dans le projet :

- après chaque modification de code : exécuter au minimum `dotnet build src/FrameShift/FrameShift.csproj` ;
- ne jamais considérer un correctif terminé sans build vert ;
- si le test réel passe par l'installateur Inno Setup, le menu contextuel Explorer ou une installation existante :
- exécuter aussi `dotnet publish src/FrameShift/FrameShift.csproj -c Release -r win-x64 --self-contained true` ;
- puis seulement recompiler `installer/FrameShift.iss`.

Important :

- `bin\Debug\...` ne prouve pas qu'une installation Explorer utilise le nouveau binaire ;
- l'ISS package la sortie `Release\...\publish` ;
- en cas de doute runtime, toujours vérifier quel binaire a réellement été publié puis installé.
