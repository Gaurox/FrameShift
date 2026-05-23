# FrameShift Product Guide

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
- `media-info`

Actions audio :
- `convert-audio`
- `compress-audio`
- `cut-audio`
- `reverse-audio`
- `change-pitch`
- `change-audio-speed`
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
- `media-info`

Action IA locale :
- `remove-background` utilise la progression commune, avec préflight du modèle si nécessaire, file visible et continuation du batch sur erreur.

## Modes d’usage réels

Batch avec options partagées :
- `convert-video`
- `convert-audio`
- `convert-image`
- `extract-audio`
- `extract-frames`
- `remove-background`

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
- `image-to-pdf`
- `media-info`

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
- `remove-background` passe par la progression commune WinForms : une seule fenêtre, file visible, erreurs dans la queue et préflight modèle si le modèle manque ;
- plusieurs actions de géométrie ou de vitesse ont un modèle `CLI entry + UI fallback`, pas une couverture CLI complète documentable comme “headless garanti”.

## Règles produit qui restent vraies

- traitement local uniquement ;
- sorties adjacentes au fichier source ;
- nommage unique `_001`, `_002`, etc. ;
- pas d’écrasement de fichier ;
- UI WinForms légère ;
- progression commune partagée pour les actions de lot, y compris `remove-background` ;
- bandeau donation discret dans la fenêtre de progression commune ;
- annulation et nettoyage propres ;
- synchronisation obligatoire entre code, publish, installateur et menus contextuels.

## Ce que FrameShift ne cherche pas à devenir

- un éditeur timeline ;
- une suite vidéo lourde ;
- une plateforme cloud ;
- un framework de plugins ;
- une architecture enterprise.
