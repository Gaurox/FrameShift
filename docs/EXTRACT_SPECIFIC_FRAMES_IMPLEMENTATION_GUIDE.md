# Extract Frames — First / Last / Keyframes

## Notice complète d’implémentation FrameShift

**Statut :** conception validée, implémentation à réaliser.  
**Projet :** `E:\AI\FrameShift_V1`  
**Périmètre :** évolution de l’action FFmpeg existante `extract-frames`.  
**Objectif :** conserver l’extraction complète en un clic et ajouter l’extraction directe de la première frame, de la dernière frame et des keyframes, sans picker ni saisie de timecode.

---

## 1. Décision produit figée

L’action actuelle visible comme **Extract frames** devient :

```text
Extract all frames
```

Une seconde entrée est ajoutée :

```text
Extract specific frames  ▸
    First frame
    Last frame
    Keyframes
```

Principes validés :

- `Extract all frames` reste directement accessible en un clic ;
- `Extract specific frames` est un sous-menu, sans fenêtre intermédiaire ;
- `First frame`, `Last frame` et `Keyframes` lancent immédiatement le traitement ;
- aucun mode par timecode ;
- aucun réglage mémorisé ou comportement caché ;
- une seule action core `extract-frames`, paramétrée par un mode ;
- le mode historique reste le comportement par défaut afin de préserver la compatibilité CLI et batch.

Le produit reste ainsi fidèle au workflow FrameShift : clic droit, choix explicite, traitement immédiat.

---

## 2. Périmètre fonctionnel

### Inclus

- renommage visible de l’action actuelle en **Extract all frames** ;
- mode `all` : comportement historique inchangé ;
- mode `first` : première frame effectivement décodée ;
- mode `last` : dernière frame effectivement décodable ;
- mode `keyframes` : toutes les frames marquées comme keyframes par le décodeur ;
- support batch multi-vidéos via la file commune existante ;
- sorties adjacentes au fichier source ;
- nommage unique ;
- annulation et nettoyage des sorties partielles ;
- intégration Explorer et installateur ;
- couverture CLI ;
- documentation et tests.

### Hors périmètre

- extraction à un timecode ;
- extraction par numéro de frame ;
- extraction de la frame centrale ;
- extraction toutes les N secondes ;
- picker WinForms ;
- choix du format de sortie ;
- choix du flux vidéo lorsqu’un fichier en contient plusieurs ;
- modification du format PNG historique ;
- nouvelle architecture générique d’extraction vidéo.

---

## 3. Compatibilité à préserver

### Identifiant d’action

Conserver :

```text
extract-frames
```

Ne pas créer :

```text
extract-first-frame
extract-last-frame
extract-keyframes
```

Ces variantes sont des modes d’une même capacité métier.

### Comportement par défaut

L’absence de mode doit continuer à signifier :

```text
all
```

Ainsi, les appels existants restent valides :

```text
FrameShift.exe --action extract-frames "video.mp4"
```

Ils doivent produire exactement le même résultat qu’avant la modification.

### Sortie historique

Le mode `all` doit conserver sans changement :

- le dossier de sortie actuel ;
- son suffixe actuel ;
- le motif actuel des noms de fichiers ;
- le format PNG ;
- la progression actuelle ;
- le comportement batch ;
- les messages d’erreur actuels, sauf renommage visible nécessaire.

Ne pas renommer les dossiers ou les fichiers produits par le mode historique uniquement pour les aligner sur le nouveau libellé du menu.

---

## 4. Contrat CLI et options

Ajouter une option métier unique :

```text
--frame-mode all|first|last|keyframes
```

Exemples :

```text
FrameShift.exe --action extract-frames --frame-mode all "video.mp4"
FrameShift.exe --action extract-frames --frame-mode first "video.mp4"
FrameShift.exe --action extract-frames --frame-mode last "video.mp4"
FrameShift.exe --action extract-frames --frame-mode keyframes "video.mp4"
```

### Règles de parsing

- option absente → `all` ;
- valeur insensible à la casse si le parsing actuel le permet naturellement ;
- valeurs acceptées uniquement : `all`, `first`, `last`, `keyframes` ;
- valeur inconnue → échec clair avant lancement de FFmpeg ;
- ne pas accepter d’alias caché sauf nécessité de compatibilité démontrée ;
- ne pas ajouter de `--timestamp` ou autre option hors périmètre.

### Clé interne

Ajouter dans `ActionOptionKeys` une clé cohérente avec les conventions actuelles, par exemple :

```csharp
public const string FrameMode = "frame-mode";
```

Le nom exact doit suivre le style déjà utilisé dans le fichier actif.

---

## 5. Modèle core recommandé

L’implémentation doit rester simple.

### Option recommandée

Créer un petit type dédié :

```csharp
internal enum ExtractFramesMode
{
    All,
    First,
    Last,
    Keyframes
}
```

Puis un parsing ciblé, soit dans `ExtractFramesAction`, soit dans un petit fichier `ExtractFramesSettings.cs` si cela améliore réellement la testabilité.

Forme possible :

```csharp
internal sealed record ExtractFramesSettings(ExtractFramesMode Mode)
{
    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string> options,
        out ExtractFramesSettings settings,
        out string error);
}
```

### Choix de simplicité

- créer `ExtractFramesSettings.cs` si le parsing, les suffixes et les comportements commencent à alourdir l’action ;
- sinon conserver un parseur privé dans `ExtractFramesAction.cs` ;
- ne pas introduire d’interface, de factory ou de hiérarchie de stratégies pour quatre branches FFmpeg simples.

---

## 6. Contrats de sortie

### 6.1 Mode `all`

Conserver strictement le contrat actuel.

Exemple conceptuel seulement :

```text
video.mp4
video_frames\
    frame_000001.png
    frame_000002.png
    ...
```

Le nom réel existant dans le code fait foi.

### 6.2 Mode `first`

Produire un seul PNG adjacent :

```text
video_first_frame.png
```

En cas de collision :

```text
video_first_frame_001.png
video_first_frame_002.png
```

Utiliser `OutputPathHelper` et les règles existantes de nommage unique.

### 6.3 Mode `last`

Produire un seul PNG adjacent :

```text
video_last_frame.png
```

En cas de collision :

```text
video_last_frame_001.png
video_last_frame_002.png
```

Le fichier final ne doit être publié qu’après réussite complète du traitement. La phase FFmpeg peut écrire dans un fichier temporaire unique, ensuite déplacé vers le chemin final.

### 6.4 Mode `keyframes`

Produire un dossier adjacent unique :

```text
video_keyframes\
    keyframe_000001.png
    keyframe_000002.png
    ...
```

En cas de collision :

```text
video_keyframes_001\
```

Règles :

- numérotation séquentielle indépendante des timestamps ;
- six chiffres recommandés pour rester cohérent avec de longues vidéos ;
- aucun fichier annexe ou manifeste en V1 ;
- dossier supprimé intégralement si le traitement échoue, est annulé ou ne produit aucune image.

---

## 7. Sélection du flux vidéo

Tous les modes doivent cibler explicitement le premier flux vidéo :

```text
-map 0:v:0
```

Ajouter également, selon le style actuel de l’action :

```text
-an -sn -dn
```

Objectifs :

- ne pas traiter l’audio ;
- ne pas traiter les sous-titres ;
- ne pas traiter les flux de données ;
- ne pas dépendre de la sélection automatique de FFmpeg.

Un fichier sans flux vidéo doit être refusé proprement avant ou pendant le lancement, selon le pattern actuel de `ExtractFramesAction`.

---

## 8. Commandes FFmpeg de référence

Toutes les commandes réelles doivent passer par :

```text
src/FrameShift/Core/FFmpeg/FfmpegRunner.cs
```

Les exemples ci-dessous décrivent les arguments attendus. Ils ne justifient pas un lancement direct de `Process` depuis l’action.

### 8.1 Toutes les frames

Conserver la commande actuelle sans refonte opportuniste.

### 8.2 Première frame

Commande conceptuelle :

```bash
ffmpeg -i "input.mp4" \
  -map 0:v:0 -an -sn -dn \
  -frames:v 1 \
  "output_first_frame.png"
```

Règle métier :

- la première frame est la première image effectivement produite par le décodeur ;
- ne pas chercher à la déduire depuis les métadonnées ;
- ne pas utiliser de timecode artificiel.

### 8.3 Keyframes

Commande recommandée :

```bash
ffmpeg -skip_frame nokey -i "input.mp4" \
  -map 0:v:0 -an -sn -dn \
  -fps_mode passthrough \
  "keyframe_%06d.png"
```

Points importants :

- `-skip_frame nokey` demande au décodeur d’écarter toutes les frames sauf les keyframes ;
- `nokey` ne doit pas être remplacé par une simple sélection des I-frames ;
- FFmpeg distingue les keyframes des frames intra ;
- `-fps_mode passthrough` évite la duplication ou la suppression destinée à recréer une cadence constante ;
- ne pas utiliser `-r` ;
- ne pas utiliser `select=eq(pict_type\,I)` comme implémentation principale.

Fallback acceptable uniquement si un problème réel est démontré avec un codec pris en charge :

```text
-vf select=eq(key\,1)
```

Ce fallback décode toutes les frames et est donc moins efficace. Il ne doit pas être ajouté préventivement si `-skip_frame nokey` fonctionne avec les binaires FFmpeg distribués par FrameShift.

### 8.4 Dernière frame

La dernière frame ne doit pas être estimée par :

- `duration - 1 frame` ;
- `nb_frames - 1` ;
- un timecode construit depuis le framerate moyen ;
- une frame I finale supposée ;
- `reverse`, qui bufferise la vidéo et peut consommer énormément de mémoire.

La stratégie recommandée est décrite dans la section suivante.

---

## 9. Stratégie fiable pour la dernière frame

### 9.1 Principe

FFmpeg sait chercher relativement à la fin du fichier avec `-sseof`. L’image2 muxer peut continuellement réécrire le même fichier avec `-update 1`.

En décodant une courte portion finale et en réécrivant toujours le même PNG, le fichier restant à la fin est la dernière frame effectivement décodée dans cette portion.

Commande conceptuelle :

```bash
ffmpeg -sseof -10 -i "input.mp4" \
  -map 0:v:0 -an -sn -dn \
  -fps_mode passthrough \
  -f image2 -update 1 \
  "temporary_last_frame.png"
```

### 9.2 Pourquoi une stratégie adaptative est nécessaire

Un simple `-sseof -10` peut ne produire aucune image dans certains fichiers :

- piste audio plus longue que la piste vidéo ;
- forte différence entre durée conteneur et durée vidéo ;
- timestamps atypiques ;
- fin de flux vidéo éloignée de la fin physique du fichier ;
- conteneur ou index imparfait.

Il faut donc élargir progressivement la fenêtre de recherche.

### 9.3 Fenêtres recommandées

Essayer dans cet ordre :

```text
10 secondes
60 secondes
300 secondes
traitement depuis le début en dernier recours
```

Pour chaque tentative :

1. supprimer l’éventuel fichier temporaire de la tentative précédente ;
2. lancer FFmpeg avec `-sseof -N` ;
3. attendre la fin normale du processus ;
4. considérer la tentative réussie seulement si le fichier temporaire existe et a une taille non nulle ;
5. arrêter immédiatement les tentatives en cas de réussite ;
6. respecter l’annulation entre chaque tentative et pendant FFmpeg.

Dernier recours :

```bash
ffmpeg -i "input.mp4" \
  -map 0:v:0 -an -sn -dn \
  -fps_mode passthrough \
  -f image2 -update 1 \
  "temporary_last_frame.png"
```

Ce fallback décode la vidéo entière mais reste :

- exact ;
- à mémoire bornée ;
- compatible avec les fichiers dont la durée ou l’index ne permettent pas `-sseof`.

### 9.4 Publication atomique du résultat

Le fichier utilisé par `-update 1` ne doit pas être le chemin final visible par l’utilisateur.

Flux recommandé :

1. réserver le chemin final unique avec `OutputPathHelper` ;
2. créer un dossier temporaire sous la convention FrameShift ;
3. écrire `temporary_last_frame.png` dans ce dossier ;
4. après succès, déplacer le fichier temporaire vers le chemin final ;
5. supprimer le dossier temporaire en `finally` ;
6. supprimer toute sortie finale partielle en cas d’erreur après déplacement commencé.

### 9.5 Résultat attendu

Le résultat doit être la dernière frame délivrée par le décodeur, y compris sur :

- vidéo à framerate variable ;
- vidéo contenant des B-frames ;
- vidéo dont le nombre de frames déclaré est absent ou faux ;
- vidéo dont l’audio se termine après l’image ;
- vidéo à GOP long.

---

## 10. Progression et messages

### Mode `all`

Conserver la progression actuelle.

### Mode `keyframes`

Réutiliser la progression temporelle FFmpeg existante :

- la progression porte sur la lecture de la vidéo ;
- le nombre final de keyframes n’a pas besoin d’être connu à l’avance ;
- ne pas lancer un `ffprobe -show_frames` préalable uniquement pour compter les keyframes.

### Mode `first`

Traitement très court :

- statut initial clair ;
- passage rapide à 100 % après création et validation du fichier ;
- ne pas ajouter une fenêtre dédiée.

### Mode `last`

La progression temporelle brute est peu représentative à cause des recherches `-sseof`.

Utiliser une progression grossière par phase, par exemple :

```text
5 %   Preparing last-frame extraction
20 %  Searching near the end
45 %  Expanding search window
70 %  Final fallback if required
100 % Last frame extracted
```

Ne pas afficher plusieurs phases si la première tentative réussit. Le message peut simplement rester `Extracting last frame…` pendant la tentative active.

### Libellés recommandés

```text
Extracting all frames…
Extracting first frame…
Extracting last frame…
Extracting keyframes…
```

Messages de réussite possibles :

```text
All frames extracted.
First frame extracted.
Last frame extracted.
Keyframes extracted.
```

En mode `keyframes`, le nombre produit peut être ajouté uniquement s’il est disponible simplement en comptant les fichiers après réussite.

---

## 11. Batch et file commune

`extract-frames` est déjà une action batch active. Cette capacité doit être conservée pour les quatre modes.

### Contrat batch

Une invocation Explorer transporte son propre mode :

```text
videoA.mp4 + mode=first
videoB.mp4 + mode=first
```

ou :

```text
videoA.mp4 + mode=keyframes
videoB.mp4 + mode=keyframes
```

Le mode doit rester attaché à la requête correspondante dans la queue.

### Relances distinctes

Si l’utilisateur lance successivement :

```text
First frame
Last frame
Keyframes
```

sur le même fichier, les trois demandes doivent rester distinctes. Ne pas dédupliquer uniquement sur le chemin source.

### Picker

Aucun picker partagé n’est ajouté. Les options sont déjà complètes dans la commande Explorer.

### ProgressForm

Conserver la fenêtre de progression commune. Ne pas créer de `ExtractFramesForm`.

---

## 12. Intégration Explorer et Inno Setup

Fichier principal :

```text
installer/FrameShift.iss
```

### 12.1 Menu final attendu

Dans le menu FrameShift appliqué aux extensions vidéo :

```text
Extract all frames
Extract specific frames  ▸
    First frame
    Last frame
    Keyframes
```

### 12.2 Commandes

Entrée directe :

```text
"{app}\FrameShift.exe" --action extract-frames --frame-mode all "%1"
```

Sous-commandes :

```text
"{app}\FrameShift.exe" --action extract-frames --frame-mode first "%1"
"{app}\FrameShift.exe" --action extract-frames --frame-mode last "%1"
"{app}\FrameShift.exe" --action extract-frames --frame-mode keyframes "%1"
```

### 12.3 Menu imbriqué

Le menu FrameShift est déjà un menu en cascade. Pour créer un sous-menu dans ce menu, utiliser la méthode statique Windows appropriée, préférentiellement `ExtendedSubCommandsKey` si la structure actuelle ne permet pas directement les sous-verbes imbriqués.

Règles :

- rester dans le registre statique ;
- ne pas créer de shell extension COM ;
- étendre minimalement les helpers Inno Setup existants ;
- conserver les comportements HKLM/HKCU déjà validés ;
- conserver le nettoyage générique à la désinstallation ;
- réutiliser l’icône actuelle d’extraction de frames si elle existe ;
- éviter de dupliquer une grande portion du code d’installation pour trois commandes.

### 12.4 Compatibilité de mise à niveau

L’installation d’une nouvelle version doit supprimer ou remplacer proprement l’ancienne entrée :

```text
Extract frames
```

Elle ne doit pas laisser simultanément :

```text
Extract frames
Extract all frames
```

Vérifier également qu’une désinstallation supprime le sous-menu et ses trois sous-commandes.

---

## 13. Validation des sorties et erreurs

### Validation commune

Après réussite FFmpeg :

- mode fichier unique → vérifier existence et taille non nulle ;
- mode dossier → vérifier qu’au moins un PNG existe ;
- une sortie vide est un échec, même si FFmpeg retourne un code ambigu ;
- journaliser stderr via le mécanisme existant ;
- conserver un message utilisateur court et lisible.

### Cas sans keyframe extraite

Si `keyframes` ne produit aucun fichier :

- supprimer le dossier créé ;
- retourner une erreur claire ;
- ne pas créer un dossier vide présenté comme succès.

Message possible :

```text
No keyframe could be extracted from this video.
```

### Cas sans frame vidéo

Message possible :

```text
No decodable video frame was found.
```

### Nettoyage

Toujours supprimer :

- fichiers temporaires du mode `last` ;
- dossier temporaire ;
- fichier final partiel en cas d’échec ;
- dossier `keyframes` partiel en cas d’échec ou d’annulation ;
- dossier `all` partiel selon le comportement historique déjà prévu.

Aucun processus FFmpeg ne doit rester orphelin.

---

## 14. Fichiers probablement concernés

Vérifier l’état réel du dépôt avant modification. Les points de contact attendus sont :

### Core

```text
src/FrameShift/Core/Actions/ExtractFramesAction.cs
src/FrameShift/Core/Actions/ActionOptionKeys.cs
src/FrameShift/Core/Actions/ActionRegistry.cs
src/FrameShift/Core/FFmpeg/FfmpegRunner.cs
src/FrameShift/Core/Helpers/OutputPathHelper.cs
```

Création possible :

```text
src/FrameShift/Core/Actions/ExtractFramesSettings.cs
```

Ne modifier `FfmpegRunner.cs` que si une capacité réellement partagée manque. Les quatre commandes peuvent normalement être construites dans l’action avec le runner existant.

### Launcher / batch

```text
src/FrameShift/ProgramCli.cs
src/FrameShift/ProgramBatch.cs
src/FrameShift/Windows/Batch/ConversionBatchSession.cs
```

Le besoin exact dépend du parsing actuel et de la manière dont les options sont sérialisées dans la queue.

### Installateur

```text
installer/FrameShift.iss
```

### Tests

Création recommandée :

```text
tests/FrameShift.Tests/ExtractFramesSettingsTests.cs
```

Autres tests à ajuster si présents :

```text
ProgramCliTests
ActionRegistry tests
Installer validation scripts
```

### Documentation

```text
docs/PRODUCT_GUIDE.md
docs/ARCHITECTURE_FREEZE.md
docs/CODE_FILE_INDEX.md
docs/CHANGELOG.md
README.md
```

Mettre à jour le site uniquement dans le chantier de publication prévu, pas automatiquement pendant le développement core si la version n’est pas encore destinée à être publiée.

---

## 15. Phases d’implémentation

## Phase 0 — Analyse ciblée de l’existant

Objectif : confirmer le contrat actuel avant de modifier le code.

À vérifier :

- commande FFmpeg actuelle de `ExtractFramesAction` ;
- nom exact du dossier historique ;
- motif de fichiers historique ;
- gestion actuelle des sorties partielles ;
- progression actuelle ;
- propagation des options batch ;
- identité de queue ;
- helpers Inno Setup utilisés pour le menu vidéo ;
- mécanisme actuel de suppression des anciennes clés lors d’une mise à niveau.

Livrable : courte synthèse des points réellement modifiés, sans coder si une incohérence structurante est découverte.

## Phase 1 — Modes core et compatibilité CLI

Objectif : rendre l’action capable d’exécuter les quatre modes sans modifier encore le menu Explorer.

Travaux :

- ajouter `FrameMode` dans `ActionOptionKeys` ;
- ajouter le parsing `all|first|last|keyframes` ;
- conserver `all` par défaut ;
- isoler les builders d’arguments FFmpeg par mode ;
- implémenter les sorties `first`, `last`, `keyframes` ;
- implémenter la stratégie adaptative `last` ;
- conserver le mode `all` inchangé ;
- ajouter validation et nettoyage ;
- ajouter les tests unitaires du parsing et du nommage.

Validation de phase :

```text
dotnet build src/FrameShift/FrameShift.csproj
```

Puis essais CLI manuels sur de courtes vidéos.

## Phase 2 — Batch et progression commune

Objectif : confirmer que chaque mode circule correctement dans la queue existante.

Travaux :

- vérifier la sérialisation des options dans les requêtes batch ;
- conserver le mode par invocation ;
- vérifier les relances distinctes du même fichier ;
- adapter les noms affichés dans `ProgressForm` si nécessaire ;
- utiliser une progression grossière pour `last` ;
- vérifier l’annulation sur les quatre modes.

Validation de phase :

- sélection de plusieurs vidéos ;
- même mode appliqué au lot ;
- relances successives de modes différents ;
- aucune confusion ou déduplication entre les requêtes.

## Phase 3 — Menu Explorer et installateur

Objectif : livrer l’UX validée.

Travaux :

- renommer l’entrée historique en `Extract all frames` ;
- ajouter `Extract specific frames` ;
- ajouter les trois sous-commandes ;
- transmettre `--frame-mode` ;
- supprimer l’ancienne entrée à l’upgrade ;
- vérifier la désinstallation ;
- conserver les icônes et la structure FrameShift existantes.

Validation de phase :

```text
dotnet publish src/FrameShift/FrameShift.csproj -c Release -r win-x64 --self-contained true
```

Puis recompilation de :

```text
installer/FrameShift.iss
```

Tester le binaire publié et installé, jamais uniquement le binaire Debug.

## Phase 4 — Validation médias et robustesse

Objectif : couvrir les cas qui peuvent tromper une extraction naïve.

Jeu de tests recommandé :

- MP4 H.264 CFR classique ;
- MKV ;
- MOV ;
- WebM ;
- vidéo VFR ;
- vidéo avec B-frames ;
- vidéo à GOP long ;
- vidéo d’une seule frame ;
- vidéo sans audio ;
- audio plus long que la piste vidéo ;
- fichier avec espaces et accents ;
- fichier contenant plusieurs flux vidéo ;
- fichier corrompu ou tronqué ;
- collision avec sorties existantes ;
- annulation pendant `all` ;
- annulation pendant `keyframes` ;
- annulation pendant une tentative `last` ;
- lot de plusieurs fichiers.

## Phase 5 — Documentation et préparation release

Objectif : aligner toute la surface produit.

Travaux :

- documenter les quatre modes ;
- remplacer les mentions visibles `Extract frames` par `Extract all frames` quand elles désignent l’entrée de menu ;
- expliquer que `Keyframes` extrait les keyframes réelles, pas toutes les I-frames ;
- mettre à jour `CODE_FILE_INDEX.md` si un fichier est ajouté ;
- ajouter une entrée de changelog dans la version cible ;
- vérifier cohérence version csproj / ISS / changelog lors de la préparation de release ;
- exécuter la chaîne build, tests, publish et installateur prévue par le projet.

---

## 16. Plan de tests détaillé

### 16.1 Tests unitaires de settings

Cas minimum :

- options vides → `All` ;
- `all` → `All` ;
- `first` → `First` ;
- `last` → `Last` ;
- `keyframes` → `Keyframes` ;
- valeur vide explicite → erreur ou défaut selon convention existante, à figer ;
- valeur inconnue → erreur claire ;
- casse différente si supportée.

### 16.2 Tests des builders d’arguments

Vérifier sans lancer FFmpeg :

- `first` contient `-frames:v 1` ;
- `keyframes` contient `-skip_frame nokey` avant `-i` ;
- `keyframes` contient `-fps_mode passthrough` ;
- `last` contient `-sseof` sur les tentatives rapides ;
- `last` contient `-update 1` ;
- tous les modes contiennent `-map 0:v:0` ;
- aucun mode n’ajoute `-r` ;
- les chemins restent transmis via le mécanisme sûr existant.

### 16.3 Oracle manuel pour les keyframes

Pour valider un échantillon, utiliser `ffprobe -show_frames` comme oracle de test et compter les frames dont `key_frame=1`.

Exemple de diagnostic manuel :

```bash
ffprobe -v error -select_streams v:0 \
  -show_frames -show_entries frame=key_frame \
  -of csv=p=0 "input.mp4"
```

Ne pas lancer ce scan coûteux dans le chemin de production.

### 16.4 Oracle manuel pour la dernière frame

Créer une référence lente mais exacte en décodant depuis le début avec `-update 1`, puis comparer l’image obtenue avec le résultat de la stratégie adaptative.

Comparer :

- dimensions ;
- hash du PNG si les paramètres d’encodage sont identiques ;
- sinon pixels décodés ou comparaison visuelle stricte.

Le résultat adaptatif et la référence complète doivent représenter la même dernière frame.

---

## 17. Critères d’acceptation

### Produit

- [ ] `Extract all frames` remplace visuellement l’ancien `Extract frames`.
- [ ] `Extract specific frames` contient exactement `First frame`, `Last frame`, `Keyframes`.
- [ ] Aucun picker ne s’ouvre.
- [ ] Aucun mode timecode n’est ajouté.
- [ ] Chaque entrée lance directement l’action attendue.

### Core

- [ ] L’action id reste `extract-frames`.
- [ ] Le mode absent reste `all`.
- [ ] Le mode historique produit exactement les mêmes sorties qu’avant.
- [ ] `first` produit une seule image.
- [ ] `last` produit la dernière frame décodable.
- [ ] `keyframes` produit seulement les keyframes, sans duplication de cadence.
- [ ] Le premier flux vidéo est explicitement sélectionné.

### Sorties

- [ ] Sorties adjacentes au fichier source.
- [ ] Aucun écrasement.
- [ ] Suffixes uniques `_001`, `_002`, etc.
- [ ] Aucun dossier vide présenté comme succès.
- [ ] Aucun fichier temporaire restant.

### Robustesse

- [ ] Chemins avec espaces et accents.
- [ ] Annulation propre.
- [ ] Aucun FFmpeg orphelin.
- [ ] Nettoyage sur erreur.
- [ ] Vidéo VFR.
- [ ] Vidéo avec B-frames.
- [ ] Audio plus long que la vidéo.
- [ ] Lot multi-fichiers.

### Packaging

- [ ] Ancienne entrée Explorer supprimée à l’upgrade.
- [ ] Sous-menu correctement désinstallé.
- [ ] Publish Release self-contained validé.
- [ ] Installateur recompilé et testé.

### Documentation

- [ ] `PRODUCT_GUIDE.md` aligné.
- [ ] `ARCHITECTURE_FREEZE.md` aligné.
- [ ] `CODE_FILE_INDEX.md` aligné si nouveau fichier.
- [ ] `CHANGELOG.md` aligné avec la version cible.
- [ ] README mis à jour si la liste publique des actions est touchée.

---

## 18. Risques et réponses

### Risque : confondre keyframes et I-frames

Réponse :

- utiliser `-skip_frame nokey` ;
- ne pas utiliser `pict_type=I` comme comportement produit ;
- tester avec un fichier où toutes les I-frames ne sont pas nécessairement des points d’accès équivalents.

### Risque : dernière frame incorrecte à cause de la durée déclarée

Réponse :

- ne pas calculer un timecode final ;
- décoder réellement une portion finale ;
- élargir la fenêtre ;
- décoder depuis le début en fallback.

### Risque : dernière frame lente sur fichiers atypiques

Réponse :

- chemin rapide `-sseof -10` ;
- élargissement seulement en cas d’absence de sortie ;
- fallback complet uniquement en dernier recours.

### Risque : progression étrange avec `-sseof`

Réponse :

- progression par phase ;
- ne pas présenter un pourcentage temporel trompeur.

### Risque : sous-menu Explorer trop complexe dans l’ISS

Réponse :

- utiliser le registre statique Windows ;
- `ExtendedSubCommandsKey` pour une cascade imbriquée si nécessaire ;
- extension minimale des helpers existants ;
- aucune extension COM.

### Risque : régression du mode historique

Réponse :

- mode `all` par défaut ;
- commande et sorties historiques inchangées ;
- test de non-régression avant intégration du menu.

---

## 19. Sources techniques officielles

- Documentation FFmpeg — options de décodage `skip_frame`, dont `nokey` et `nointra` :  
  https://www.ffmpeg.org/ffmpeg.html
- Documentation FFmpeg — `-sseof` et modes `-fps_mode` :  
  https://www.ffmpeg.org/ffmpeg.html
- Documentation FFmpeg image2 — option `update` :  
  https://ffmpeg.org/ffmpeg-formats.html
- Documentation FFprobe — `-show_frames` :  
  https://www.ffmpeg.org/ffprobe-all.html
- Microsoft Learn — menus imbriqués avec `ExtendedSubCommandsKey` :  
  https://learn.microsoft.com/windows/win32/shell/how-to-create-cascading-menus-with-the-extendedsubcommandskey-registry-entry

---

## 20. Résumé d’implémentation

Architecture finale :

```text
Explorer
├── Extract all frames
└── Extract specific frames
    ├── First frame
    ├── Last frame
    └── Keyframes

           ↓

FrameShift.exe --action extract-frames --frame-mode <mode>

           ↓

ExtractFramesAction
├── all        → comportement historique
├── first      → -frames:v 1
├── last       → -sseof adaptatif + -update 1
└── keyframes  → -skip_frame nokey + -fps_mode passthrough

           ↓

FfmpegRunner → sortie adjacente unique → ProgressForm commune
```

La solution reste volontairement limitée : une action existante étendue, aucun nouveau formulaire, aucun timecode, aucune surcouche architecturale et une intégration complète jusqu’au build, publish et installateur.
