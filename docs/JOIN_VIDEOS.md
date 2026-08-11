# Join Videos

Action : `join-videos`.

Join Videos raccorde plusieurs clips dans une seule piste, sans transition, trim, piste additionnelle ou outil de montage avancé.

## UI

La fenêtre affiche une timeline horizontale : aperçu, nom et durée de chaque clip ; sa largeur reste liée à la durée. Le glisser-déposer sur la timeline définit l'ordre final ; glisser des fichiers depuis l'Explorateur directement dans la fenêtre ouverte les ajoute aussi à la liste, avec un indicateur d'insertion qui montre la position exacte. En tri Custom, les fichiers déposés s'insèrent à cet endroit précis ; avec un tri déterministe actif, l'endroit du dépôt n'a pas d'effet puisque le tri recalcule la position. Les tris disponibles sont : reçu, nom naturel, date de création, date de modification et personnalisé ; le tri par défaut est le nom naturel, et le dernier tri déterministe choisi est mémorisé pour les prochaines ouvertures.

Un même chemin peut être présent plusieurs fois : chaque occurrence est conservée. L'ordre reçu depuis Explorer est affiché comme un ordre reçu, jamais comme un ordre de sélection garanti.

`Suppr` retire le clip sélectionné, `Ctrl+←`/`Ctrl+→` le déplace d'une position, et « Clear all » vide la timeline. Le survol d'un clip dont la résolution ou la présence audio diffère du premier clip affiche un indice de compatibilité dans l'infobulle.

## Traitement

- Si les signatures de flux FFprobe sont strictement compatibles, FrameShift utilise le concat demuxer et copie les flux sans réencodage.
- Sinon, les sources SDR sont normalisées automatiquement vers MP4 / H.264 / AAC. La résolution et l'orientation de référence viennent du premier clip ; les autres images gardent leur ratio et reçoivent des bandes noires si nécessaire. Un clip sans audio reçoit un silence stéréo 48 kHz sur sa durée.
- HDR : un groupe HDR homogène peut seulement passer en copie directe. Un mélange HDR/SDR ou une normalisation HDR est refusé proprement en V1.

La sortie est créée à côté du premier clip de la timeline sous la forme `<nom>_joined.<ext>` ou `<nom>_joined.mp4`, avec suffixe unique `_001`, `_002`, etc. Aucun fichier existant n'est écrasé.

## CLI

Sans option, la commande ouvre l'éditeur :

```text
FrameShift.exe --action join-videos "clip 1.mp4" "clip 2.mp4"
```

Le CLI peut forcer le pipeline :

```text
FrameShift.exe --action join-videos --join-mode auto "clip 1.mp4" "clip 2.mp4"
FrameShift.exe --action join-videos --join-mode copy "clip 1.mp4" "clip 2.mp4"
FrameShift.exe --action join-videos --join-mode normalize "clip 1.mp4" "clip 2.mp4"
```

`copy` échoue si la compatibilité stricte n'est pas établie. `normalize` refuse les clips HDR.
