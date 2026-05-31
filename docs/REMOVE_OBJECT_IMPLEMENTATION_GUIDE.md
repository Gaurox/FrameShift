# REMOVE OBJECT (IMAGE) — Implementation Guide

> Document de référence officiel pour l'implémentation de la fonctionnalité **Remove Object (Image)** dans FrameShift.
> **Statut : IMPLÉMENTÉ — v1.0.9.** Ce document est conservé comme référence historique de conception.
> Audience : développeurs et agents IA reprenant le travail, éventuellement plusieurs semaines après la phase d'étude.
> Règle d'or : ce guide est autoportant. Il n'est pas nécessaire de relire la recherche initiale pour développer la fonctionnalité.

> **Note post-implémentation (mai 2026)** : MI-GAN 512 a été retiré du catalogue après validation — le modèle ONNX produit des résultats non fonctionnels (output saturé en couleur uniforme) quel que soit le pré-traitement testé (13 combinaisons normalisation/masque/décodage).
> **Mise à jour (mai 2026)** : un second modèle a été ajouté — **LaMa 2025 (Fast)** (`inpainting_lama_2025jan.onnx`, 93 MB, Apache-2.0, opencv/inpainting_lama). API identique à LaMa FP32 (mêmes tenseurs `image`/`mask`, même plage de sortie [0,255]), aucun changement d'engine. Accessible via le ComboBox (`lama-fast`). SHA256 : `7DF918AC3921D3DAF0AAE1D219776CF0DC4E4935F035AF81841B40ADCF74FDF2`. Hébergé sur `Gaurox/frameshift-models/lama-opencv-onnx/`. La propriété `ForceCpu` a été ajoutée à `ObjectRemovalModelDefinition` (défaut `true`) pour permettre aux futurs modèles d'activer DirectML sans toucher à l'engine.

---

## 0. TL;DR

- Nouvelle action IA locale **`remove-object`** : l'utilisateur peint un masque sur une image, l'IA reconstruit (inpainting) la zone peinte, le résultat est sauvegardé à côté de la source.
- Accès : clic droit Explorer → **FrameShift AI → Remove object** (images uniquement).
- Inférence **in-process** (comme tous les modules IA FrameShift), ONNX Runtime + DirectML→CPU fallback, ImageSharp.
- **Architecture catalogue multi-modèles** : **LaMa** et **MI-GAN** au départ, interchangeables, modèle par défaut paramétrable.
- **Nouveauté structurelle** : 1er module IA avec **éditeur visuel** (canvas + masque) AVANT l'inférence.
- **Licence** : poids entraînés sur Places2 → statut commercial **non garanti**. À documenter honnêtement (UI + notices). Voir §6.

---

## 1. Objectifs produit

- Permettre la suppression d'objets, personnes, défauts ou textes sur une image, en local, hors-ligne, gratuitement.
- Workflow rapide « clic droit → peindre → exécuter → résultat à côté du fichier ».
- Qualité « grand public » : résultat propre sur fonds et textures simples à moyennes.
- Cohérence totale avec l'identité FrameShift : WinForms, offline, léger, sobre, stable.
- Respect strict de : `PROJECT_RULES.md`, `ARCHITECTURE_FREEZE.md`, `UI_STANDARDIZATION.md`.

---

## 2. Périmètre V1

INCLUS :
- Action `remove-object` pour images `.png .jpg .jpeg .webp .bmp`.
- Éditeur visuel : affichage image, pinceau, gomme, taille de pinceau réglable, zoom molette, déplacement (pan), reset masque, exécuter, annuler.
- Inférence locale (LaMa OU MI-GAN selon catalogue), DirectML avec fallback CPU.
- Téléchargement du modèle à la demande, avec vérification SHA256 et affichage de la licence.
- Sortie PNG unique à côté de la source.
- Architecture catalogue permettant d'ajouter/remplacer un modèle sans refonte.
- Gestion d'erreurs et annulation propres.
- Menu Explorer dédié + composant installateur.

---

## 3. Hors périmètre (V1)

- Remove Object **Vidéo** (tracking, propagation de masque, cohérence temporelle) → étude future, voir §19.
- Sélection automatique d'objet (SAM / « clic-objet ») → V2/V3.
- Pipeline crop-autour-du-masque haute résolution → V2 (V1 = downscale global vers la taille modèle).
- Modèles de diffusion (Stable Diffusion, BRIA, etc.) → écartés (lourds / VRAM / payants / prompts), contraires à la philosophie.
- Inpainting guidé par texte (prompt).
- Mode batch multi-fichiers (l'édition est mono-image par nature).
- Undo/redo multi-niveaux du masque (V1 = reset global ; undo optionnel V1.1 si simple).

---

## 4. Choix d'architecture

Principes hérités (vérifiés sur Remove Background / Separate Audio / Remove Noise) :
- L'IA tourne **in-process** dans `FrameShift.exe`. Il N'EXISTE PAS de projet moteur séparé.
- Chaque module IA vit sous `src/FrameShift/Core/AI/<Feature>/`.
- Pattern par module : `Action` (IFrameShiftAction) + `Engine` (inférence ONNX pure) + `ModelLocator` + `ModelDownloader` (+ contrats/records).
- Infrastructure partagée : `AiModelStorage` (racine modèles), `AiModelFileDownloader` (download + SHA256), `OutputPathHelper`, `AppLogger`, `IProgressReporter`, `DownloadModelForm`.
- DirectML d'abord, fallback CPU sur exception. Provider exposé pour l'affichage.
- Sortie toujours adjacente à la source, nommage unique, jamais d'écrasement, nettoyage des sorties partielles.

Spécificité Remove Object :
- **Action UI-first** (comme `image-to-pdf` / `media-info`) : interceptée dans `Program.RunCli` AVANT la queue commune, ouvre un éditeur.
- Le **masque** est une donnée d'entrée produite par l'UI ; le moteur reste sans dépendance WinForms et **stateless par image** (clé pour la future vidéo).
- **Moteur abstrait derrière un catalogue** (inspiré de `RifeModelCatalog`) pour basculer LaMa ↔ MI-GAN.

---

## 5. Architecture catalogue LaMa + MI-GAN

Objectif : ne pas coder en dur un modèle, permettre l'ajout/remplacement, et exposer la licence de chaque modèle.

Concept (à implémenter en C# simple, pas de sur-abstraction) :
- `ObjectRemovalModelDefinition` : `Id`, `DisplayName`, `Folder`, `FileName(s)`, `Url(s)`, `Sha256(s)`, `SizeBytes`, `License`, `InputSize` (512), `Notes` (origine Places2…).
- `ObjectRemovalModelCatalog` : liste statique des définitions + helper `Default` (paramétrable).
- `IObjectRemovalEngine` : contrat d'inférence commun.
- Implémentations : `LamaEngine`, `MiganEngine` (ou un seul `OnnxInpaintEngine` paramétré si les pipelines sont assez proches ; à décider au Lot 2 selon les I/O réels).

Candidats initiaux :

| Id | DisplayName | Source | Code | Poids | Taille approx. | Input |
|---|---|---|---|---|---|---|
| `lama` | LaMa (FP32) | Carve/LaMa-ONNX (`lama_fp32.onnx`) | Apache-2.0 © Samsung Research | Places2 (non validé commercial) | ~200 Mo | image 512² + mask 512² |
| `migan` | MI-GAN 512 | Picsart MI-GAN (ONNX) | MIT | Places2 (non validé commercial) | ~25–30 Mo | image+mask 512² (pipeline pré/post intégré dans certaines exports) |

Notes d'intégration à confirmer au Lot 0 :
- LaMa : 2 entrées (`image`, `mask`), 512×512 fixe, opset 17, FFC/FFT (risque DirectML → fallback CPU probable).
- MI-GAN : vérifier les noms d'entrées/sorties et l'échelle attendue ; certaines exports embarquent crop/normalisation, d'autres non.
- Convention masque LaMa : **1 = zone à supprimer/reconstruire, 0 = à conserver** (à valider empiriquement).

Le modèle par défaut V1 reste **paramétrable** et n'est PAS figé ici (décision produit/juridique ouverte).

---

## 6. Décision actuelle concernant Places2 (FIGÉE)

Décision validée par le mainteneur :
- On part sur **LaMa + MI-GAN** malgré l'ambiguïté Places2.
- Risque jugé faible dans le contexte FrameShift (application **gratuite**, financée par **dons**).
- **Obligations de documentation (impératives)** :
  1. NE JAMAIS présenter les poids comme « commercial guaranteed » / « libre pour usage commercial ».
  2. Afficher la licence réelle de chaque modèle dans `DownloadModelForm` (champ `modelLicense` déjà prévu).
  3. Ajouter les notices dans `THIRD_PARTY_NOTICES.md` (LaMa Apache-2.0 © 2021 Samsung Research + lien Carve ; MI-GAN MIT © Picsart).
  4. Mentionner clairement l'**origine Places2** des poids (libellé licence ou note du catalogue).
  5. Conserver l'**architecture catalogue** pour pouvoir remplacer le modèle plus tard sans refonte.

Faits juridiques de référence (résumé) :
- Code LaMa : Apache-2.0 (© 2021 Samsung Research). Port ONNX Carve : tag `apache-2.0`. MI-GAN : code MIT.
- Données Places2 : « non-commercial research and educational purposes » (restriction sur la DONNÉE).
- Aucun README (Carve, advimman, smartywu/big-lama, MI-GAN) ne traite l'usage commercial des POIDS.
- Le statut des poids tiers entraînés sur Places2 est juridiquement non tranché. → Statut interne retenu : **« origine Places2 — usage commercial non garanti »**.

Libellé licence recommandé dans le catalogue/UI (exemple) :
- LaMa : `Apache-2.0 (code) — weights trained on Places2; commercial use not guaranteed`
- MI-GAN : `MIT (code) — weights trained on Places2; commercial use not guaranteed`

---

## 7. Structure des dossiers
src/FrameShift/Core/AI/RemoveObject/ (NOUVEAU)
src/FrameShift/Windows/AI/ (ajout d'1 fichier éditeur)
src/FrameShift/Assets/Icons/ai/ (ajout remove_object.ico)
installer/FrameShift.iss (modif)
THIRD_PARTY_NOTICES.md, docs/* (modif)

Stockage runtime des modèles (créé à la volée, hors Git/installeur) :
%LOCALAPPDATA%\FrameShift\AI\Models<model_folder><model_file>.onnx
ex: ...\AI\Models\lama-onnx\lama_fp32.onnx
ex: ...\AI\Models\migan-512\migan_512.onnx

(`AiModelStorage.RootDirectory` = `%LOCALAPPDATA%\FrameShift\AI\Models`, confirmé par la routine de désinstallation.)
---
## 8. Structure des fichiers
### Core (nouveaux)
src/FrameShift/Core/AI/RemoveObject/
├── RemoveObjectAction.cs # IFrameShiftAction, id "remove-object"
├── IObjectRemovalEngine.cs # contrat + record InpaintProgress(int Percent, string Status)
├── ObjectRemovalEngine.cs # (ou LamaEngine.cs / MiganEngine.cs) inférence ONNX
├── ObjectRemovalModelCatalog.cs # définitions LaMa + MI-GAN, défaut paramétrable
├── ObjectRemovalModelDefinition.cs # record définition modèle (id, urls, sha, licence...)
├── ModelLocator.cs # chemins sous AiModelStorage, migration legacy si besoin
└── ModelDownloader.cs # wrapper sur AiModelFileDownloader par modèle

### Windows (nouveau)
src/FrameShift/Windows/AI/
└── RemoveObjectEditorForm.cs # éditeur visuel (canvas + masque + outils)

Optionnel si nécessaire seulement :
src/FrameShift/Windows/Controls/
└── MaskCanvasControl.cs # contrôle canvas double-buffer (n'ajouter que si la base crop ne suffit pas)

### Réutilisés tels quels (NE PAS dupliquer)
Core/AI/AiModelStorage.cs
Core/AI/AiModelFileDownloader.cs
Core/AI/AiModelDownloadProgress.cs
Core/Helpers/OutputPathHelper.cs
Core/Logging/AppLogger.cs
Core/Progress/IProgressReporter.cs
Windows/AI/DownloadModelForm.cs
Windows/Helpers/FrameShiftEditorShellUi.cs
Windows/Helpers/FrameShiftCropEditorUi.cs (RÉFÉRENCE pour zoom/pan/Fit)
Windows/Helpers/FrameShiftUiFactory.cs / FrameShiftTheme.cs / FrameShiftUiMetrics.cs
Windows/Helpers/FrameShiftWindowChrome.cs / IconPaths.cs

### Modifiés (intégration)
Core/Actions/ActionRegistry.cs # enregistrer l'action
Program.cs / ProgramCli.cs # router remove-object (UI-first)
installer/FrameShift.iss # composant + menu + icône
THIRD_PARTY_NOTICES.md # notices LaMa + MI-GAN
docs/PRODUCT_GUIDE.md / ARCHITECTURE_FREEZE.md / CODE_FILE_INDEX.md

---
## 9. Intégration ActionRegistry
Dans `Core/Actions/ActionRegistry.cs`, méthode `CreateDefault()`, ajouter à la liste des actions :
new RemoveObjectAction(),

`RemoveObjectAction` implémente `IFrameShiftAction` :
- `Descriptor = new("remove-object", "Remove Object", "Supprime un objet d'une image via inpainting IA local.")`
- `ExecuteAsync(ActionRequest request, CancellationToken ct)` :
  - valide input (existence, extension supportée) ;
  - préflight modèle (présence ; sinon message ou déclenchement download selon flux) ;
  - reçoit le masque (cf. §10 pour le flux UI vs headless) ;
  - délègue au moteur du catalogue ;
  - retourne `ActionExecutionResult(success, message, outputPath)` ;
  - gère `OperationCanceledException` et nettoyage.
> Suivre exactement le squelette de `RemoveBackgroundAction` (préflight `ModelLocator.ModelExists()`, monitor d'annulation par item, `IProgressReporter.ReportProgress`).
---
## 10. Intégration Program.cs (routage UI-first)
Remove Object nécessite l'éditeur AVANT l'inférence → action **UI-first**.
Dans `Program.RunCli(...)` (ou `ProgramCli`/un `ProgramRemoveObject.cs` partial dédié pour rester propre), intercepter `remove-object` AVANT la queue commune, comme `image-to-pdf` :
if (actionId == "remove-object")
return RunRemoveObject(inputPaths[0], options, logger);

`RunRemoveObject` :
1. Vérifie le fichier et l'extension.
2. Ouvre `RemoveObjectEditorForm` (single-instance, mono-fichier).
3. L'éditeur gère : préflight/téléchargement modèle, édition du masque, exécution, progression, sauvegarde.
4. Retourne 0 (succès/annulation utilisateur normale).
Deux options d'exécution de l'inférence (choisir au Lot 4/5) :
- (A) **Tout dans l'éditeur** : l'éditeur appelle directement le moteur via `RemoveObjectAction`/engine et affiche sa propre progression. (Plus simple, recommandé V1.)
- (B) **Déléguer à la progression commune** : l'éditeur produit le masque puis passe par `RunQueuedActionWithProgressForm`. (Plus cohérent visuellement, mais nécessite de transmettre le masque à l'action via `ActionRequest.Options`/fichier temporaire.)
Mode headless optionnel (utile pour tests) : accepter `--mask <path.png>` pour exécuter sans UI. Non requis produit, mais précieux pour valider le moteur au Lot 3.
---
## 11. Intégration installer (FrameShift.iss)
`[Components]` : ajouter
Name: "ai\remove_object"; Description: "Remove object"; Types: complete custom

`[Code]` :
- Dans `ConfigureAIActionMenuForHive`, ajouter une branche icône :
else if MenuKey = 'remove_object' then
IconPath := ExpandConstant('{app}\Assets\Icons\ai\remove_object.ico');

- Dans `InstallSelectedMenus`, ajouter :
if WizardIsComponentSelected('ai\remove_object') then
ApplyAIActionMenuList(ImageExtensions, 'remove_object', 'Remove object', 'remove-object');

- Le menu apparaît sous la racine `FrameShift AI` (déjà gérée par `EnsureFrameShiftAIRootForHive`).
- Commande générée automatiquement : `"{app}\FrameShift.exe" --action remove-object "%1"`.
Assets : ajouter `src/FrameShift/Assets/Icons/ai/remove_object.ico` (utilisé en bandeau interne ET en icône menu Explorer).
Rappel build (PROJECT_RULES — Build Discipline) :
- `dotnet build src/FrameShift/FrameShift.csproj`
- puis `dotnet publish ... -c Release -r win-x64 --self-contained true`
- puis recompiler l'ISS. Ne jamais tester l'ancien binaire.
---
## 12. Gestion des modèles
- `ModelLocator` : chemins sous `AiModelStorage.RootDirectory\<folder>\`, `ModelExists()` / `EnsureDirectoryExists()`, migration de layout legacy si nécessaire (suivre `RemoveBackground.ModelLocator`).
- `ModelDownloader` : par modèle, s'appuyer sur `AiModelFileDownloader.DownloadAsync(url, dest, sha256, progress, ct, logPrefix)` (download streaming `.tmp` → vérif SHA256 → `File.Move` atomique → nettoyage en cas d'échec/annulation).
- Hébergement des `.onnx` : dépôt HuggingFace du projet
`https://huggingface.co/Gaurox/frameshift-models/resolve/main/<folder>/<file>.onnx`
(calculer et figer le SHA256 de chaque fichier dans le catalogue).
- Téléchargement **au moment utile** (pas à la simple ouverture d'une UI vide). Préflight quand l'utilisateur lance l'édition/exécution.
- MI-GAN ~25 Mo (download quasi instantané) ; LaMa ~200 Mo.
---
## 13. Workflow utilisateur
Nominal :
1. Clic droit sur une image → **FrameShift AI → Remove object**.
2. `FrameShift.exe --action remove-object "<image>"` → ouverture `RemoveObjectEditorForm`.
3. Préflight modèle : si absent → `DownloadModelForm` (affiche nom, taille, **licence**), annulable. Sinon, édition directe.
4. L'utilisateur peint le masque sur l'objet (pinceau/gomme/taille/zoom/pan), peut **Reset**.
5. **Apply** → inférence (resize 512 → Run → recollage zone masquée) avec progression.
6. Sauvegarde `<nom>_cleaned.png` à côté de la source (nommage unique). Option : ouvrir le dossier.
7. Fermeture / nouvelle passe possible.
---
## 14. UI détaillée (UI_STANDARDIZATION strict)
Type : **éditeur léger redimensionnable** (§12.2 / §12.3 du standard). Hiérarchie : **bandeau / workspace / rangée d'outils / aide / footer**, rail de contrôle fixe à droite.
- Chrome : `FrameShiftWindowChrome.Apply(this, "FrameShift - Remove Object", IconPaths.FrameShiftAiIcon, IconPaths.AppIcon)` → barre de titre icône **FrameShift AI**.
- Bandeau (≈58 px) via `FrameShiftUiFactory.CreateFixedHeader/CreateFillHeader` : icône `remove_object`, titre, ligne secondaire (`nom · LARGEURxHAUTEUR · format`).
- Workspace (bloc dominant) : image affichée, **overlay masque** semi-transparent (rouge ~40 %). Base mécanique reprise de `FrameShiftCropEditorUi` (zoom molette, pan clic-glissé, Fit).
- Couleurs : `#8EBAF3` (PrimaryBlue) / `#4D79B4` (SecondaryBlue) ; fond `#F5F7FB` ; surfaces blanches ; bordures bleues, jamais noires.
- Rail outils (ligne dédiée à hauteur fixe, jamais comprimable) :
- `Brush` / `Eraser` (toggle exclusif, tuiles ou boutons cohérents) ;
- **Brush size** (champ composite + slider, hauteur 30/34 px standard) ;
- `Reset mask` (secondaire) ;
- `Fit` (secondaire) ;
- indicateur Zoom %.
- Footer : `Cancel` (120×34, secondaire) à gauche du principal ; `Apply` (140×34, principal `SecondaryBlue`) à droite. Marge basse cohérente.
- Cadre d'aide bleu (`AccentSoft` + bordure `PrimaryBlue`) : texte court.
- Souris : clic-gauche glissé = peindre ; gomme = mode actif (ou clic-droit glissé) ; molette = zoom ; espace+drag (ou molette-drag) = pan ; `[` / `]` = taille pinceau.
- Le masque est stocké en **coordonnées image** (indépendant du zoom), prêt pour le resize 512.
- Respecter la taille minimale de fenêtre garantissant les espacements standards (sinon relever la taille min avant de toucher au style).
---
## 15. Mockup ASCII
┌───────────────────────────────────────────────────────────────────────────┐
│ [icon] FrameShift — Remove Object │
│ photo.jpg · 4032×3024 · JPG │
├───────────────────────────────────────────────────────────────┬───────────┤
│ │ TOOLS │
│ │ ┌───────┐ │
│ ██████ ← overlay masque (rouge ~40%) │ │ Brush │ │
│ [ image zoomable + déplaçable ] │ │ Eraser│ │
│ │ └───────┘ │
│ │ Brush size│
│ │ [──●────] │
│ │ 40 px │
│ │ │
│ │ [ Reset ] │
│ │ [ Fit ] │
│ │ Zoom 100% │
├───────────────────────────────────────────────────────────────┴───────────┤
│ ⓘ Paint over the object to remove. Output saved next to the source (PNG). │
├─────────────────────────────────────────────────────────────────────────────┤
│ [ Cancel ] [ Apply ▶ ] │
└─────────────────────────────────────────────────────────────────────────────┘

---
## 16. Stratégie DirectML / fallback CPU
Reproduire exactement le pattern `BackgroundRemovalEngine.CreateSession` :
try {
var opts = new SessionOptions { GraphOptimizationLevel = ORT_ENABLE_ALL };
opts.AppendExecutionProvider_DML();
return (new InferenceSession(modelPath, opts), "DirectML");
} catch (Exception ex) {
log("DirectML failed, fallback CPU: " + ex.Message);
var opts = new SessionOptions { GraphOptimizationLevel = ORT_ENABLE_ALL };
return (new InferenceSession(modelPath, opts), "CPU");
}

- Exposer `Provider` (DirectML / CPU) pour l'affichage de progression (« Remove Object (CPU) »).
- ATTENTION LaMa : opérateurs FFT/DFT (FFC) possiblement **non supportés en DirectML** → fallback CPU probable. Acceptable car CPU reste rapide (~1–4 s en 512²). À mesurer au Lot 0.
- Détecter la présence du provider DML via `OrtEnv.Instance().GetAvailableProviders()` si on veut éviter une exception coûteuse (cf. `SeparateAudio.ModelLocator.IsDmlAvailable`).
- Détecter le type d'entrée (`float` vs `Float16`) via `InputMetadata` et construire le tensor en conséquence (LaMa fp32 = float).
---
## 17. Stratégie de téléchargement
- Modèle téléchargé à la demande via `DownloadModelForm` :
new DownloadModelForm(
featureTitle: "Remove Object",
featureSubtitle: "<model display name>",
preferredIconPath: IconPaths.<remove_object ai icon>,
modelDisplayName: def.DisplayName,
modelLicense: def.License, // NE PAS dire "commercial guaranteed"
modelSizeBytes: def.SizeBytes,
downloadAction: (progress, ct) => ModelDownloader.DownloadAsync(def, dest, progress, ct));

- Vérification SHA256 obligatoire (via `AiModelFileDownloader`). Mismatch → `InvalidDataException` → message « fichier corrompu, réessayer », nettoyage `.tmp`.
- Erreurs réseau (`HttpRequestException`) → message « vérifier la connexion », nettoyage.
- Annulation → CTS, suppression `.tmp`, retour propre à l'éditeur.
- Ne pas écrire le modèle final tant que la vérification n'est pas passée (`.tmp` → `File.Move`).
---
## 18. Gestion des erreurs (à couvrir explicitement)
| Cas | Comportement attendu |
|---|---|
| Format non supporté | Refuser (`.png/.jpg/.jpeg/.webp/.bmp`), message clair |
| Image trop grande | Garde `MaxPixels` (≈ 80 MP comme RemoveBackground) ; au-delà message clair |
| Masque vide | Bloquer `Apply` + message (« rien à supprimer ») |
| Modèle absent | Flux `DownloadModelForm` ; refus/échec → retour éditeur sans crash |
| SHA256 mismatch | `InvalidDataException`, nettoyage, message « réessayer » |
| Réseau | `HttpRequestException`, message « vérifier connexion » |
| DirectML/FFT non supporté | Fallback CPU automatique, logguer le provider |
| Mémoire insuffisante | try/catch autour de `Run`, message lisible, pas de sortie partielle |
| Annulation (download/inférence) | CTS lié, nettoyage sorties partielles, retour propre |
| Échec écriture sortie | Supprimer la sortie partielle (`DeletePartialOutput`), remonter l'erreur |
| Chemins avec espaces/accents | Doivent fonctionner (validation runtime obligatoire) |
Toujours : pas de console visible, logs lisibles via `AppLogger`.
---
## 19. Nommage des fichiers & conventions de sortie
- Sortie générée via :
OutputPathHelper.CreateUniqueOutputPath(inputPath, "_cleaned", ".png")

- Format de sortie : **PNG** (qualité, pas de recompression destructrice).
- Suffixe : `_cleaned` (cohérent avec `_nobg` de RemoveBackground). Collisions → `_cleaned_001`, `_002`, …
- Sortie **toujours adjacente** au fichier source. Jamais d'écrasement.
- Nettoyage de toute sortie partielle en cas d'échec/annulation.
---
## 20. Pré/post-traitement (cœur qualité)
LaMa/MI-GAN sont **fixés à 512×512**. Pipeline V1 :
1. Charger l'image (`ImageSharp`, `Rgba32`), garde `MaxPixels`.
2. Construire le masque plein format (depuis l'éditeur, coordonnées image).
3. Redimensionner **image ET masque** à 512×512.
4. Construire tensors (image NCHW float ; masque 1×1×512×512 ; convention `1 = à retirer` à confirmer).
5. `session.Run`.
6. Sortie 512² → redimensionner à la taille d'origine.
7. **Recoller uniquement la zone masquée** sur l'image d'origine (composer via le masque plein format), pour ne PAS dégrader le reste de l'image par le resize. ← point qualité n°1.
8. Sauvegarder en PNG unique.
V2 (hors V1) : crop autour du masque → 512 → inpaint → blend (qualité haute résolution).
---
## 21. Risques connus
- **Juridique (Places2)** : statut commercial des poids non garanti. Mitigation : documentation honnête (UI + notices), application gratuite/dons, catalogue remplaçable. NE PAS sur-affirmer.
- **DirectML/FFT (LaMa)** : fallback CPU probable. Mitigation : CPU rapide en 512², provider affiché.
- **Qualité haute résolution** : downscale global en V1 → perte locale possible. Mitigation : V2 crop-around-mask.
- **Format masque non documenté** : convention LaMa/MI-GAN à valider empiriquement (Lot 0).
- **Perf rendu overlay/zoom** : double-buffering, invalidation maîtrisée, recalcul après taille finale (cf. notes UI §12.2).
- **MI-GAN pipeline d'export variable** : certaines exports embarquent pré/post, d'autres non → vérifier I/O exacts.
- **Cohérence build/publish/installer** : suivre Build Discipline pour ne pas tester un ancien binaire.
---
## 22. Compatibilité future (Remove Object Vidéo)
L'architecture V1 est un socle compatible, à condition de respecter dès maintenant :
- Moteur `IObjectRemovalEngine` **sans dépendance WinForms** et **stateless par image**.
- Masque = **donnée** (coordonnées image normalisées), découplé de l'UI.
- Catalogue de modèles réutilisable.
Ce qui restera à ajouter pour la vidéo (chantier distinct, hors scope) :
- tracking d'objet (ex. SAM2 / point-tracking),
- propagation de masque entre frames,
- cohérence temporelle (LaMa seul scintille ; modèle vidéo plus lourd potentiellement nécessaire).
Ne pas sur-concevoir la V1 image pour la vidéo ; juste ne pas se fermer la porte.
---
## 23. Plan de développement détaillé par lots
> Chaque lot : build vert obligatoire. Lots touchant runtime/Explorer : publish + recompilation ISS.
**Lot 0 — Spike technique (bloquant)**
- But : valider I/O réels (noms entrées image/mask, convention masque, échelle sortie), DML vs CPU, qualité du recollage, comportement MI-GAN vs LaMa.
- Fichiers : aucun en prod (script/test jetable).
- Risques : format masque non documenté, FFT non-DML.
- Dépendances : §6 (décisions licence déjà prises).
**Lot 1 — Catalogue + Locator + Downloader**
- But : `ObjectRemovalModelDefinition`, `ObjectRemovalModelCatalog` (LaMa + MI-GAN, licences, SHA256), `ModelLocator`, `ModelDownloader`. Héberger les `.onnx` sur HF Gaurox.
- Réutilise : `AiModelStorage`, `AiModelFileDownloader`.
- Risques : URLs/SHA. Dépend du Lot 0.
**Lot 2 — Moteur d'inférence (sans UI)**
- But : `IObjectRemovalEngine` + implémentation(s), pré/post (resize 512, recollage zone), DML→CPU, save `_cleaned.png`.
- Réutilise : `OutputPathHelper`, ImageSharp.
- Risques : qualité recollage, mémoire. Dépend Lots 0–1.
**Lot 3 — Action + registre + routage (testable headless)**
- But : `RemoveObjectAction`, enregistrement `ActionRegistry`, routage `Program`. Option `--mask` pour test headless.
- Risques : intégration préflight. Dépend Lot 2.
**Lot 4 — Éditeur visuel**
- But : `RemoveObjectEditorForm` (canvas, pinceau/gomme/taille/zoom/pan/reset, Apply/Cancel) sur base `FrameShiftEditorShellUi` + mécanique `FrameShiftCropEditorUi`.
- Risques : perf overlay, mapping écran↔image, conformité UI. Dépend Lot 3.
**Lot 5 — Préflight modèle dans l'UI + progression + erreurs**
- But : `DownloadModelForm` branché, progression inférence, gestion complète erreurs/annulation.
- Risques : UX annulation. Dépend Lot 4.
**Lot 6 — Installateur + assets + menus Explorer**
- But : composant `ai\remove_object`, menu `Remove object`, icône `remove_object.ico`.
- Risques : registre HKLM/HKCU, sync publish/ISS. Dépend Lot 5.
**Lot 7 — Docs, notices, validation finale**
- But : `THIRD_PARTY_NOTICES.md` (LaMa + MI-GAN), `PRODUCT_GUIDE.md`, `ARCHITECTURE_FREEZE.md`, `CODE_FILE_INDEX.md` ; checklist §24.
- Dépend Lot 6.
---
## 24. Checklist de validation finale
Fonctionnel :
- [ ] Clic droit image → FrameShift AI → Remove object ouvre l'éditeur.
- [ ] Pinceau, gomme, taille, zoom molette, pan, Reset fonctionnent.
- [ ] Préflight : modèle téléchargé à la demande, SHA256 vérifié, licence affichée.
- [ ] Apply produit `<nom>_cleaned.png` à côté de la source, nommage unique, pas d'écrasement.
- [ ] Masque vide bloqué avec message.
- [ ] DirectML utilisé si possible, sinon fallback CPU, provider loggé.
Robustesse (PROJECT_RULES — Runtime Validation) :
- [ ] Chemins avec espaces et accents OK.
- [ ] Annulation propre (download et inférence), nettoyage des sorties/temp partiels.
- [ ] Aucune fenêtre console visible.
- [ ] Logs lisibles.
- [ ] Image trop grande / format non supporté → messages clairs, pas de crash.
- [ ] Erreur réseau / SHA mismatch → messages clairs, nettoyage.
UI (UI_STANDARDIZATION) :
- [ ] Bandeau standard, titre `FrameShift - Remove Object`, icône fonction visible.
- [ ] Barre de titre = icône FrameShift AI.
- [ ] Deux bleus de référence, aucune bordure noire système.
- [ ] Boutons principal/secondaire conformes (140×34 / 120×34, 34 px outils).
- [ ] Rangée d'outils toujours visible (ligne dédiée à hauteur fixe).
- [ ] Espacements standards respectés à la taille minimale.
Licence / conformité (§6) :
- [ ] `DownloadModelForm` affiche la licence réelle, sans « commercial guaranteed ».
- [ ] Origine Places2 mentionnée (libellé licence ou note catalogue).
- [ ] `THIRD_PARTY_NOTICES.md` : LaMa (Apache-2.0 © 2021 Samsung Research + Carve) et MI-GAN (MIT © Picsart).
- [ ] Architecture catalogue en place (modèle remplaçable).
Build / packaging (Build Discipline) :
- [ ] `dotnet build` vert.
- [ ] `dotnet publish -c Release -r win-x64 --self-contained true`.
- [ ] ISS recompilé ; menu Explorer et action testés sur binaire publié/installé (pas Debug).
- [ ] Désinstallation propose la suppression des modèles (`%LOCALAPPDATA%\FrameShift\AI\Models`).
---
## 25. Références internes (pour reprise rapide)
- Pattern action IA : `Core/AI/RemoveBackground/RemoveBackgroundAction.cs`
- Pattern moteur ONNX (DML/CPU, tensors, ImageSharp) : `Core/AI/RemoveBackground/BackgroundRemovalEngine.cs`
- Locator/migration : `Core/AI/RemoveBackground/ModelLocator.cs`
- Download partagé : `Core/AI/AiModelFileDownloader.cs`
- Catalogue multi-modèles (modèle à suivre) : `Core/AI/VideoInterpolation/RifeModelCatalog.cs`
- Téléchargement UI : `Windows/AI/DownloadModelForm.cs`
- Canvas zoom/pan/Fit (référence éditeur) : `Windows/Helpers/FrameShiftCropEditorUi.cs`, `Windows/Forms/CropImageForm.cs`
- Shell éditeur : `Windows/Helpers/FrameShiftEditorShellUi.cs`
- Chemins de sortie : `Core/Helpers/OutputPathHelper.cs`
- Registre/menu installateur : `installer/FrameShift.iss` (`ApplyAIActionMenuList`, `ConfigureAIActionMenuForHive`)
---
FIN DU GUIDE — version pré-implémentation. À mettre à jour à chaque lot validé.