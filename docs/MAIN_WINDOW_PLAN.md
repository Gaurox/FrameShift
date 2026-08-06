# FrameShift — Plan de développement : Fenêtre principale (hub glisser-déposer)

> Document de guidage pour l'implémentation. Rien n'est codé à ce stade.
> Rédigé le 2026-07-08. Cible : v1.17.x (à confirmer au moment du versionnage).

---

## 1. Objectif

Transformer la fenêtre principale actuelle — décorative et non fonctionnelle
([`MainForm.cs`](../src/FrameShift/Windows/Forms/MainForm.cs) : 4 tuiles non
cliquables + bloc « AI models folder » + indice « Action routing is not wired
here yet ») — en un **hub de travail glisser-déposer** qui :

- organise toutes les actions FrameShift sans jamais surcharger l'écran ;
- accepte un nombre **illimité** de fichiers (contourne la limite Windows des 15) ;
- reste **simple, rapide, épuré** (esprit [`AGENTS.md`](../AGENTS.md) : outil de
  workflow, pas framework).

### Rappel du problème d'origine (limite « 15 fichiers »)

La limite n'est pas dans le code FrameShift : c'est le plafond de l'Explorateur
Windows sur les verbes statiques `"%1"` (invocation « un fichier à la fois »,
registre `MultipleInvokePromptMinimum`, défaut 15). Toutes les entrées de menu
contextuel de l'installeur utilisent `"%1"`
([`installer/FrameShift.iss`](../installer/FrameShift.iss) — `ConfigureAIActionMenuForHive`
et `ConfigureActionMenuForHive`). La fenêtre-hub évite ce plafond parce que
**c'est l'application** qui lance le traitement, pas l'Explorateur.

---

## 2. Décisions produit validées

| # | Décision | Choix retenu | Conséquence |
|---|----------|--------------|-------------|
| D1 | Stratégie d'exécution | **Process enfant, sélection complète** | La fenêtre lance `FrameShift.exe --action <id> <paths…>` en un seul process. Réutilise 100 % du pipeline existant, zéro refonte back-end. Progression dans la fenêtre de progression habituelle (séparée). |
| D2 | Actions mono-fichier | **Actives si 1 seul fichier compatible sélectionné** | Aucune modif back-end. Le vrai lot reste pour les actions déjà multi-fichiers (dont Remove Background). |
| D3 | Menu contextuel Explorateur | **Ne pas y toucher (fenêtre seulement)** | Périmètre restreint, faible risque. La limite 15 subsiste au clic droit mais la fenêtre la contourne. |

---

## 3. Concept UX (rappel — deux maquettes déjà validées visuellement)

Principe : **« Déposer → Filtrer → Agir »**. La fenêtre part des *fichiers*, pas
des fonctions, donc elle n'affiche jamais les ~37 actions d'un coup.

- **État vide** : une grande zone de dépôt centrée (« Déposer des fichiers ici ·
  ou Parcourir »). Presque rien à l'écran.
- **État chargé** : deux colonnes. À gauche la **file** (liste des fichiers, comptes
  par type, ajout/retrait, dépôt permanent). À droite les **actions filtrées** par
  les types présents, **groupées par catégorie** (union, jamais intersection), avec
  un champ de recherche et des puces de type.
- **Portée** : rien de sélectionné → portée = toute la file ; une puce de type ou
  une sélection manuelle → portée réduite. Chaque action porte un **badge « · N »**
  indiquant sur combien de fichiers de la portée elle s'appliquera.

Détails « produit fini » : glisser-déposer sur toute la fenêtre avec surimpression ;
clavier (`Ctrl+O` ajouter, `Suppr` retirer, `/` recherche, `Entrée` lancer) ;
mémorisation de la taille de fenêtre ; bloc « AI models folder » relégué dans un
petit panneau **Paramètres** (engrenage).

---

## 4. Architecture d'exécution (le cœur — D1)

La fenêtre n'exécute **rien elle-même**. Sur clic d'une action, elle **construit
une ligne de commande** et **lance un process enfant** `FrameShift.exe`, qui
réutilise tout le dispatch existant ([`Program.RunCli`](../src/FrameShift/Program.cs#L119)).

### 4.1 Construction de la commande

```
FrameShift.exe --action <actionId> [--rmbg-model <id>] "<path1>" "<path2>" …
```

- **Chemin de l'exe** : `Environment.ProcessPath` (la fenêtre est elle-même
  `FrameShift.exe`).
- **Arguments** : utiliser `ProcessStartInfo.ArgumentList` (pas de concaténation
  manuelle) → gère automatiquement espaces et accents dans les chemins (exigence
  [`AGENTS.md`](../AGENTS.md) « Runtime Validation »).
- `UseShellExecute = false`, `CreateNoWindow = true` (l'enfant ouvre sa propre UI).
- **Ne jamais passer `--target` / `--profile`** pour les conversions/compressions :
  laisser l'enfant afficher son sélecteur. Raison : `ShouldRunConversionBatch`
  exige `!options.Any()` pour prendre le chemin lot
  ([`ProgramBatch.cs:464`](../src/FrameShift/ProgramBatch.cs#L464)).
- **Seule exception d'option passée par la fenêtre** : `--rmbg-model` pour les
  variantes de Remove Background (voir §7.1).

### 4.2 Pourquoi ça réutilise tout, sans refonte

Le process enfant retombe sur les chemins déjà éprouvés :

- Actions IA lot → `IsAiBatchAction` → `RunConversionBatch`
  ([`Program.cs:139`](../src/FrameShift/Program.cs#L139)).
- Convert/extract → `ShouldRunConversionBatch` → `RunConversionBatch`.
- Compress → `RunCompressBatch` (chemin lot dédié,
  [`Program.cs:326`](../src/FrameShift/Program.cs#L326)).
- Actions « single-run » (options partagées ou aucune) → `Ensure*` puis
  `RunQueuedActionWithProgressForm` ([`ProgramBatch.cs:322`](../src/FrameShift/ProgramBatch.cs#L322)).
- Éditeurs mono-fichier → `Ensure*` (garde `inputPaths.Count != 1`) puis exécution.

La **limite des 15 disparaît** car un seul process reçoit toute la liste. Le
mutex + named pipe existant ([`ConversionBatchSession`](../src/FrameShift/Windows/Batch/ConversionBatchSession.cs))
continue de servir uniquement si l'utilisateur relance la même action (agrégation).

### 4.3 Cycle de vie

- Lancement **fire-and-forget** : la fenêtre lance l'enfant et rend la main ;
  l'enfant gère progression/annulation/erreurs comme aujourd'hui.
- v1 : la file **reste** après lancement (l'utilisateur peut enchaîner une autre
  action). Option future : suivre les enfants pour un badge « en cours ».

---

## 5. Taxonomie complète des actions (LA référence)

Arité observée depuis le code (marqueur `ExactlyOneSourceFileRequired` = mono-fichier).

- **Lot** = accepte N fichiers, options partagées une fois (ou aucune).
- **Mono** = éditeur/dialogue interactif, exige exactement 1 fichier (D2 : actif si 1 sélectionné).
- **Fusion** = combine N → 1 sortie.

Extensions : Vidéo `.mp4 .mkv .avi .mov .webm .m4v` · Audio `.mp3 .wav .wave .flac .m4a .ogg .aac .wma` · Image `.png .jpg .jpeg .webp .bmp`.

### Vidéo
| Action | actionId | Arité | Sélecteur d'options (dans l'enfant) |
|--------|----------|-------|--------------------------------------|
| Convert video | `convert-video` | Lot | `ConversionPickerForm` (container + profil) |
| Compress video | `compress-video` | Lot | `RunCompressBatch` |
| Extract all frames | `extract-frames` | Lot | aucun |
| Extract audio | `extract-audio` | Lot | `ConversionPickerForm` (format) |
| Remove audio | `remove-audio` | Lot | aucun |
| Remove noise (video) | `remove-noise-video` | Lot (IA) | `RemoveNoiseVideoPickerForm` |
| Upscale video | `upscale-video` | Lot (IA) | `UpscaleVideoPickerForm` |
| Create subtitle file | `create-subtitles-video` | Lot (IA) | `CreateSubtitlesPickerForm` |
| Create GIF | `create-gif` | Mono | éditeur |
| Cut video | `cut-video` | Mono | éditeur timeline |
| Crop video | `crop-video` | Mono | `CropVideoForm` |
| Resize video | `resize-video` | Mono | éditeur |
| Rotate / Flip video | `rotate-flip-video` | Mono | `RotateFlipVideoForm` |
| Change video speed | `change-video-speed` | Mono | éditeur |
| Interpolate video (FFmpeg) | `interpolate-video` | Mono | éditeur |
| Interpolate video (RIFE) | `interpolate-video-rife` | Mono (IA) | éditeur + préflight modèle |
| Add subtitles to video | `add-subtitles-video` | Mono | éditeur (+ fichier de sous-titres) |

### Audio
| Action | actionId | Arité | Sélecteur |
|--------|----------|-------|-----------|
| Convert audio | `convert-audio` | Lot | `ConversionPickerForm` |
| Compress audio | `compress-audio` | Lot | `RunCompressBatch` |
| Reverse audio | `reverse-audio` | Lot | aucun |
| Separate audio | `separate-audio` | Lot (IA) | `SeparateAudioPickerForm` — ext : `.wav .mp3 .flac .m4a .ogg .aac .wma` |
| Remove noise | `remove-noise` | Lot (IA) | `RemoveNoiseAudioPickerForm` — ext : `.wav .flac .mp3 .ogg .m4a` |
| Cut audio | `cut-audio` | Mono | éditeur timeline |
| Change pitch | `change-pitch` | Mono | éditeur |
| Change audio speed | `change-audio-speed` | Mono | éditeur |

### Image
| Action | actionId | Arité | Sélecteur |
|--------|----------|-------|-----------|
| Convert image | `convert-image` | Lot | `ConversionPickerForm` |
| Compress image | `compress-image` | Lot | `RunCompressBatch` |
| Remove background | `remove-background` | Lot (IA) | **aucun** — variante via `--rmbg-model` (voir §7.1) |
| Upscale image | `upscale-image` | Lot (IA) | `UpscaleImagePickerForm` |
| Crop image | `crop-image` | Mono | `CropImageForm` |
| Resize image | `resize-image` | Mono | éditeur |
| Rotate / Flip image | `rotate-flip-image` | Mono | `RotateFlipImageForm` |
| Convert to icon | `convert-to-icon` | Mono | `ConvertToIconForm` |
| Remove object | `remove-object` | Mono (IA) | éditeur de masque (`inputPaths[0]`) |
| Image to PDF | `image-to-pdf` | **Fusion** N→1 | `ImageToPdfForm` |

### Transverse
| Action | actionId | Arité | Notes |
|--------|----------|-------|-------|
| Media Info | `media-info` | Mono | Visionneuse. Accepte vidéo/audio/image → groupe « Général ». |

> Récap : ~18 actions **Lot**, ~18 **Mono**, 1 **Fusion**. Les actions Lot sont
> exactement celles qui souffraient de la limite 15 au clic droit.

---

## 6. Découpage des composants WinForms

Tout en WinForms, thème existant ([`FrameShiftTheme`](../src/FrameShift/Windows/Helpers/FrameShiftTheme.cs),
[`FrameShiftUiMetrics`](../src/FrameShift/Windows/Helpers/FrameShiftUiMetrics.cs),
[`FrameShiftUiFactory`](../src/FrameShift/Windows/Helpers/FrameShiftUiFactory.cs),
[`FrameShiftWindowChrome`](../src/FrameShift/Windows/Helpers/FrameShiftWindowChrome.cs)).
Respecter l'architecture : **le Core ne touche jamais aux contrôles WinForms**
(la fenêtre appelle seulement le lanceur de process et le catalogue).

1. **`MainForm` (refonte)** — conteneur, gère les deux états (vide / chargé),
   le glisser-déposer global (`AllowDrop`, `DragEnter`/`DragDrop`), la barre de
   titre (recherche + Paramètres), et l'orchestration file ↔ actions.
2. **`FileQueuePanel`** — modèle de file (liste ordonnée + `HashSet` d'ID),
   ajout/déduplication, retrait, sélection multiple (Ctrl/Maj), rendu des lignes
   (icône de type, nom, méta légère, croix). Expose `SelectionChanged` et
   `QueueChanged`.
3. **`ActionsPanel`** — reçoit la portée courante (fichiers visibles/sélectionnés),
   interroge le **catalogue** (§7), calcule les groupes visibles + les compteurs,
   rend les boutons d'action (icône `.ico` existante + libellé + badge « · N »),
   gère la recherche et les puces de type. Émet `ActionInvoked(actionId, paths)`.
4. **`ActionLauncher`** (nouveau, hors Core UI) — construit la commande et lance
   le process enfant (§4). Point unique, testable.
5. **`SettingsPanel`/dialogue** — récupère la gestion « AI models folder »
   actuellement dans `MainForm` (Browse / Reset / Open), déplacée derrière
   l'engrenage.
6. **Zone de dépôt (état vide)** — panneau centré réutilisant
   `FrameShiftUiFactory.CreateFramedPanel`.

---

## 7. Le catalogue central (seule vraie métadonnée à créer)

Aujourd'hui `ActionDescriptor` n'expose que `Id / DisplayName / Description`
([`ActionDescriptor.cs`](../src/FrameShift/Core/Actions/ActionDescriptor.cs)). Le
mapping type→actions+extensions est éparpillé (installeur + définitions de batch).
**Créer une source de vérité unique**, ex. `ActionCatalog` (Core, sans dépendance
WinForms) : liste de `ActionCatalogEntry` avec :

```
ActionId            // ex. "convert-video"
DisplayName         // "Convert video"
Category            // Video | Audio | Image | General
Arity               // Batch | Single | Combine
AcceptedExtensions  // set d'extensions
IconRelativePath    // pour réutiliser les .ico livrés (voir installer)
ExtraCliArgs        // ex. ["--rmbg-model", "fast"] pour les variantes RemoveBg
```

La fenêtre lit ce catalogue pour : filtrer par type, grouper, compter, activer/
désactiver (règle D2), et construire la commande. **Bonus** : à terme, l'installeur
pourra générer ses menus depuis ce même catalogue (fin de la duplication).

### 7.1 Cas particulier — variantes Remove Background

`ResolveBackgroundRemovalModel` prend le **modèle par défaut (Fast)** si aucun
`--rmbg-model` n'est fourni, et **n'affiche aucun sélecteur**
([`ProgramAiPreflight.cs:564`](../src/FrameShift/ProgramAiPreflight.cs#L564)).
Donc exposer les variantes comme **entrées de catalogue distinctes**, en miroir
des composants installeur ([`installer/FrameShift.iss:894`](../installer/FrameShift.iss#L894)) :

| Entrée | `--rmbg-model` |
|--------|----------------|
| Remove Background (Fast) — défaut | `fast` |
| Remove Background (High Resolution Matting) | `high-resolution` |
| Remove Background (High Resolution General) | `high-resolution-general` |
| Remove Background (BRIA Balanced) | `bria-balanced` |
| Remove Background (BRIA High Quality) | `bria-high-quality` |

Présentation possible : une seule tuile « Remove background » qui déroule les
variantes (sous-menu), pour ne pas encombrer. Le BRIA reste soumis au
`BriaModelNoticeForm` (modèle fourni par l'utilisateur) — géré par l'enfant.

> Les autres actions IA affichent déjà leur sélecteur dans l'enfant (upscale,
> subtitles, separate, denoise) → la fenêtre ne passe que `--action <id>`.

---

## 8. Flux détaillés par arité

### 8.1 Lot (N fichiers)
1. Portée = fichiers de la catégorie (ou sélection). Badge « · N ».
2. Clic → `ActionLauncher` lance `--action <id> <N paths>` (+ `--rmbg-model` si RemoveBg).
3. L'enfant affiche son sélecteur/préflight puis traite tout le lot dans **une**
   fenêtre de progression.

### 8.2 Mono (1 fichier) — règle D2
1. Bouton **actif seulement si exactement 1 fichier compatible** est dans la portée
   (sinon grisé + infobulle « sélectionnez un seul fichier »).
2. Clic → `--action <id> "<path>"`. L'enfant ouvre l'éditeur interactif.

### 8.3 Fusion (image-to-pdf)
1. Actif si ≥ 1 image dans la portée.
2. Clic → `--action image-to-pdf <paths>`. L'enfant combine en un PDF
   ([`RunImageToPdf`](../src/FrameShift/Program.cs#L354)).

---

## 9. Cas limites & validation (exigences `AGENTS.md`)

- **Chemins avec espaces/accents** → `ArgumentList` (jamais de quoting manuel).
- **Sélection mixte** → union groupée ; une action ne concerne que son sous-ensemble
  (l'enfant ignore/rapporte déjà les formats non supportés,
  [`ConversionBatchSession`](../src/FrameShift/Windows/Batch/ConversionBatchSession.cs)).
- **Fichier non supporté déposé** → accepté dans la file mais ne fait apparaître
  aucune action pour lui ; badge/état clair.
- **Action mono avec >1 fichier** → bouton grisé (pas d'erreur après coup).
- **Annulation / process orphelin / cleanup** → inchangés (gérés par l'enfant).
- **Aucune console visible**, logs lisibles → inchangés.
- **Déduplication** des chemins ajoutés plusieurs fois.
- **Dépôt de dossiers** → décision : ignorer, ou aplatir (récupérer les fichiers
  supportés). Proposition v1 : aplatir un niveau. *(à confirmer)*

---

## 10. Carte de réutilisation

| Besoin | Réutiliser |
|--------|-----------|
| Thème / métriques / panneaux | `FrameShiftTheme`, `FrameShiftUiMetrics`, `FrameShiftUiFactory`, `FrameShiftWindowChrome` |
| Icônes par action | `.ico` livrés (voir `GetMenuIconPath` / icônes IA dans l'installeur), `IconPaths` |
| Lancement | `Environment.ProcessPath` + `ProcessStartInfo.ArgumentList` |
| Dispatch enfant | `Program.RunCli` (aucune modif) |
| Paramètres modèles | logique Browse/Reset/Open de `MainForm` actuelle, `AiModelSettings` |
| Point d'entrée fichiers | `TryGetUiStartupPaths` gère déjà l'ouverture de la fenêtre avec des chemins ([`Program.cs:101`](../src/FrameShift/Program.cs#L101)) |

---

## 11. Hors périmètre v1

- Pas de progression embarquée (fenêtre de progression séparée conservée — D1).
- Pas de modification du menu contextuel Explorateur (D3).
- Pas de traitement par lot des actions mono-fichier (D2).
- Pas de presets, historique, chaînage d'actions, onglets, thème sombre.
- Une sélection → une action → on lance.

---

## 12. Étapes d'implémentation proposées (lots)

1. **Catalogue** — `ActionCatalog` + entrées (§5, §7), tests unitaires (mapping
   complet, extensions, arité, variantes RemoveBg).
2. **ActionLauncher** — construction commande + lancement enfant ; tests (quoting
   via ArgumentList, chemins accentués, `--rmbg-model`, pas de `--target`).
3. **FileQueuePanel** — modèle de file + rendu + sélection multiple + dépôt.
4. **ActionsPanel** — filtrage/groupes/compteurs/recherche/puces + activation D2.
5. **MainForm (refonte)** — états vide/chargé, glisser-déposer global, câblage
   file ↔ actions ↔ launcher, barre de titre.
6. **Paramètres** — déplacer « AI models folder » derrière l'engrenage.
7. **Finitions** — clavier, mémorisation taille, surimpression de dépôt, vides.

---

## 13. Checklist de tests (manuels + unitaires)

- [ ] Déposer 50 images → Remove Background (Fast) traite les 50 en un lot (pas de limite 15).
- [ ] Sélection mixte vidéo+image → groupes corrects, badges « · N » exacts.
- [ ] Sélectionner un sous-ensemble → actions et compteurs recalculés.
- [ ] Action mono (crop) grisée à >1 fichier, active à 1.
- [ ] Chemins avec espaces et accents → traitement OK, sortie correcte.
- [ ] Fichier non supporté dans la file → aucune action ne le revendique.
- [ ] Recherche « resize » → filtre instantané multi-types.
- [ ] Convert video sans option passée → sélecteur s'ouvre dans l'enfant.
- [ ] Remove Background variantes → bon `--rmbg-model` transmis.
- [ ] Annulation dans l'enfant → pas de process orphelin.
- [ ] Fenêtre relancée depuis une sélection Explorateur (≤15) → file pré-remplie.

---

## 14. Points à confirmer plus tard

- **Dépôt de dossiers** : ignorer vs aplatir (proposition : aplatir un niveau).
- **Comportement après lancement** : garder la file vs la vider (proposition : garder).
- **Présentation des variantes Remove Background** : sous-menu vs entrées à plat.
- **État « en cours »** dans la fenêtre (suivi des enfants) — reporté après v1.
- **Numéro de version** cible pour la livraison.
