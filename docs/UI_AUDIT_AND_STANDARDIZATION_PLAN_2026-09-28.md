# FrameShift — Audit UI et feuille de route officielle UI/DPI

Date : 28 septembre 2026. Référence examinée : commit `cccc644`, version déclarée `1.19.1`.

**Orientation validée : conserver WinForms/.NET 8 et consolider la couche commune existante.** Le problème principal est la coexistence de plusieurs règles de dimensionnement, certaines incompatibles avec le DPI et la taille du texte. Une collection de constantes et une palette partagée ne suffisent pas : les composants communs doivent aussi prendre en charge leur disposition, leur mesure et leurs interactions.

**Statut au 29 septembre 2026 : A/B/C validées ; GO D reçu après le commit `5b62534`. D1 implémentée et vérifiée automatiquement, recette visuelle en attente. D2/D3 et E à G non commencées.** Les sections 2 à 5 conservent les constats de l'audit initial; les sections 6 à 10 fixent les règles et le déroulement du chantier. Les relevés d'exécution figurent sous chaque phase. Ce document ne certifie pas un support DPI global ni le rendu en 4K.

**Parcours obligatoire : A → validation → B → validation → C → GO/NO-GO → D → E → F → G.** Les preuves de validation et les décisions de passage seront consignées au fil de l'exécution; la validation de cette feuille de route ne vaut pas validation technique des phases.

## 1. Synthèse et orientation retenue

Les problèmes prioritaires sont :

1. **Politique DPI incomplète.** Le projet ne déclare pas `ApplicationHighDpiMode`; dix classes de fenêtres directement dérivées de `Form` ne définissent pas `AutoScaleMode`. Aucune référence explicite `AutoScaleDimensions` ni gestion spécifique des changements DPI n'a été trouvée dans la couche Windows.
2. **Helpers partagés qui peuvent annuler une mise à l'échelle.** Les calculs de sections et de boutons utilisent des dimensions courantes en pixels, puis réinjectent des constantes telles que `18`, `34`, `120` et `140` lors de `Resize` ou `Shown`.
3. **Textes et conteneurs mesurés indépendamment.** Titres automatiques, sous-titres à hauteur fixe, rangées de champs et messages trop contraints ne partagent pas un calcul de taille cohérent.
4. **Absence de stratégie pour les petits espaces de travail.** Plusieurs éditeurs imposent une hauteur minimale importante; la majorité des dialogues restent fixes, sans défilement du contenu.
5. **Défauts locaux indépendants du 4K.** Bandeau trop large dans Interpolate Video, contraintes contradictoires dans Cut Audio et Progress, champs temporels non étirés dans Cut Video.
6. **Standardisation des interactions insuffisante.** Des sélecteurs sont des panneaux ou labels cliquables, les raccourcis varient et plusieurs aperçus synchrones bloquent le fil UI.
7. **Validation insuffisamment traçable.** Les tests présents couvrent des comportements, des calculs et le thème; ils ne démontrent pas le rendu de toutes les fenêtres aux différents DPI.

La base actuelle est récupérable : `FrameShiftTheme`, `FrameShiftUiFactory`, `FrameShiftUiMetrics`, les shells d'édition, les runners et plusieurs mécanismes de durée de vie sont déjà de bons points d'appui. Il faut améliorer leur contrat, puis migrer chaque fenêtre, plutôt qu'ajouter une nouvelle série de correctifs locaux.

## 2. Méthode, périmètre et limites

### Travail effectué

- Inspection de la capture fournie : bandeau, titres de sections et textes effectivement coupés.
- Recensement de **36 classes de fenêtres concrètes**, **une base abstraite** (`ResizeMediaFormBase`), **deux `UserControl`** (`ActionsPanel`, `FileQueuePanel`) et des contrôles `JoinVideosTimelineControl` et `SeekTrackBar`.
- Analyse statique des constructions, layouts, tailles, événements de redimensionnement, interactions et chemins d'aperçu pertinents.
- Inspection des helpers de thème, dessin, chrome, disposition et aperçu; recherche transversale des déclarations DPI, accessibilité, clavier et dimensionnement.
- Inspection des points d'ouverture des fenêtres dans `Program*.cs`, des tests UI existants et des pages personnalisées Inno Setup.
- Lecture de `UI_STANDARDIZATION.md` et `UI_DPI_AUDIT.md`, confrontés au code actuel.
- Vérification des principes DPI dans la documentation officielle Microsoft; calcul des contrastes à partir des couleurs du code.

### Ce qui n'a pas été fait

L'application n'a pas été lancée ni compilée pour cet audit documentaire. Aucun réglage d'écran n'a été modifié. Aucun test visuel à 200 %, 300 %, en multi-écran, au Narrateur ou dans l'installateur n'a été exécuté. Les tests existants ont été inspectés, pas exécutés. Les ressources sous `references/` ne sont pas des dépendances du présent audit.

L'inventaire des fenêtres est complet pour les classes recensées dans le dépôt actif. La liste des défauts repose sur l'inspection statique et la capture; elle ne prétend pas épuiser tous les comportements possibles sans recette réelle.

### Lecture des constats

- **Confirmé dans le code** : instruction, omission ou contrainte contradictoire vérifiable; ne signifie pas que le symptôme a été reproduit sur écran.
- **Visible sur la capture** : symptôme observé, sans connaissance de la version exacte du binaire ou du facteur DPI de l'utilisateur.
- **Risque à reproduire** : conséquence probable demandant une vérification en exécution.
- **P1** : utilisabilité, lisibilité ou réactivité fortement affectée; à traiter avant d'annoncer un support DPI fiable.
- **P2** : cohérence, accessibilité, maintenabilité; à traiter après stabilisation des P1, en phase F, dans le périmètre défini en section 7.1.

Les numéros de ligne ci-dessous correspondent à la référence auditée et pourront évoluer. Les chemins abrégés `Forms/`, `AI/`, `Helpers/`, `Controls/` et `ProgressUI/` sont relatifs à `src/FrameShift/Windows/`.

## 3. Lecture du signalement 4K

Sur la capture Cut Video, le titre et les métadonnées se rapprochent jusqu'au chevauchement, les titres Preview et Selection sont coupés, les libellés Start/End perdent leur précision, les champs temporels sont étroits et les deux lignes d'aide sont comprimées. Les boutons restent visibles sur cette capture : elle ne démontre pas leur disparition.

Le code actuel comporte plusieurs explications cohérentes avec ces symptômes :

| Élément | Preuve dans le code | Interprétation et correction |
|---|---|---|
| Fenêtre | `Forms/CutVideoForm.cs:69–80` : tailles fixes de référence, aucun `AutoScaleMode` explicite | Établir le même contrat DPI que les autres fenêtres; reproduire avant de conclure au facteur exact. |
| Bandeau | `Helpers/FrameShiftUiFactory.cs:253–324` : titre `AutoSize` à `(64,8)`, sous-titre à `(64,30)` de hauteur `18` | Le titre peut grandir sans que le sous-titre descende. Utiliser deux lignes mesurées dans une grille. |
| Sections | `Helpers/FrameShiftUiLayout.cs:9–18` : hauteur du titre réassignée à `18` | Après layout, cette valeur brute peut être trop petite pour une police agrandie. Mesurer le titre et préserver l'espacement. |
| Champs début/fin | `Forms/CutVideoForm.cs:326–397` : lignes `28`, colonne de label `80`, boutons `28 × 26`; champs temporels ajoutés sans `Dock`/`Anchor` d'étirement | Un grand espace de grille ne garantit pas la largeur du `TextBox`. Mesurer `HH:MM:SS.mmm`, étirer les champs et garder les boutons accessibles. |
| Aide | `Forms/CutVideoForm.cs:243–244` : deux lignes de `18` | Une ligne de texte ne doit pas être dimensionnée indépendamment de sa police. |
| Aperçu | `Forms/CutVideoForm.cs:458–471` : capture asynchrone attendue synchroniquement avec `CancellationToken.None` | Autre problème confirmé : la navigation peut bloquer le fil UI pendant FFmpeg. |

**La résolution 4K n'est pas le facteur de mise à l'échelle.** Il faut relever séparément résolution physique, pourcentage Windows, taille du texte, écran de lancement et déplacements entre écrans. Un écran 3840 × 2160 à 200 % fournit environ 1920 × 1080 unités logiques; à 300 %, environ 1280 × 720, avant retrait de la barre des tâches et de la chrome. Une fenêtre logique de 800 unités de haut ne tient donc pas nécessairement sur un écran 4K.

Pour reproduire le cas exact, recueillir : version FrameShift, version Windows, résolution et échelle de chaque écran, écran principal, réglage « taille du texte », éventuelle substitution DPI dans les propriétés de compatibilité, mode de lancement et capture entière. Ces données complètent l'audit; elles ne conditionnent pas les corrections structurelles déjà identifiées.

## 4. Registre des problèmes transversaux

### UI-01 — P1 — Initialisation DPI non unifiée

**Confirmé.** `src/FrameShift/Program.cs:24` appelle `ApplicationConfiguration.Initialize()`. Le `.csproj` ne précise pas `ApplicationHighDpiMode`. Dans la configuration standard du SDK, la valeur par défaut est `SystemAware`; l'absence de réglage ne signifie donc pas que le processus est totalement DPI-unaware. Le mode effectivement publié reste à relever sur le binaire. [Référence Microsoft](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop#applicationhighdpimode).

Sur les 37 classes de fenêtres, 24 déclarent `AutoScaleMode.Dpi`; trois classes concrètes héritent de bases qui le déclarent. Les dix déclarations manquantes concernent : `MainForm`, `SettingsForm`, `ProgressForm`, `CutVideoForm`, `CutAudioForm`, `CreateGifForm`, `CropImageForm`, `CropVideoForm`, `ImageToPdfForm`, `RemoveObjectEditorForm`.

La recherche ne trouve ni `AutoScaleDimensions`, ni `DeviceDpi`, ni traitement `DpiChanged` dans le code Windows. L'absence de gestionnaire DPI local n'est pas en soi un défaut pour un contrôle natif; elle devient problématique pour les calculs et dessins maison.

**Action :** déclarer le mode du processus, une référence logique à 96 DPI et une politique commune de formulaire; traiter le dessin manuel séparément. Vérifier le fonctionnement initial et les changements de moniteur. Ne pas considérer l'ajout de `AutoScaleMode.Dpi` comme une correction suffisante.

### UI-02 — P1 — Mélange de dimensions logiques et de pixels courants

**Confirmé.** `FrameShiftUiLayout.LayoutTitledSection`, `LayoutFooterButtons`, `LayoutEvenButtons` emploient `ClientSize` puis écrivent des constantes non converties dans `SetBounds`. `AutoSizeAndPositionFooterButtons` mesure le texte, mais conserve des ajouts bruts de `28` et `14`, des minima et des marges non contextualisés au DPI.

Ces helpers sont rappelés après construction : par exemple `FrameShiftCropEditorUi.cs:181–191` câble `Resize` et `Shown`. Le layout peut donc remettre des boutons à `34` pixels physiques après un agrandissement géré ailleurs.

**Action :** préférer des lignes `AutoSize` et des boutons mesurés. Pour les rares calculs manuels nécessaires, convertir les constantes logiques avec le DPI du contrôle qui dessine ou dispose les éléments. Ne jamais multiplier à nouveau un `ClientSize`, un `Bounds` ou une mesure déjà exprimée en pixels courants.

### UI-03 — P1 — Bandeaux et sections partagés trop rigides

**Confirmé.** `CreateFixedHeader` impose une largeur `536`. `CreateFillHeader` remplit son parent mais son sous-titre conserve une largeur passée par l'appelant. `PopulateHeader` juxtapose des positions fixes et un titre `AutoSize`. `GetTitledSectionHeight` et `GetInfoCardHeight` reposent sur des lignes de `18`.

**Action :** un seul composant de bandeau, largeur héritée du conteneur, icône dans une colonne dédiée, titre et sous-titre mesurés. Section composée d'un titre `AutoSize`, d'un espacement et d'un contenu; carte d'information capable de revenir à la ligne. `58` devient une hauteur minimale indicative, pas un plafond.

### UI-04 — P1 — Dialogues fixes et minima incompatibles avec l'espace disponible

**Confirmé.** Aucune utilisation de `Screen.WorkingArea` n'a été trouvée dans la couche Windows. Les dialogues simples utilisent majoritairement coordonnées et `FixedDialog`. Les minima de Create GIF (`980 × 800`), Burn Subtitles (`1120 × 760`) et Image to PDF (`1080 × 760`, puis recalcul) sont déjà trop contraignants pour certains espaces logiques de 720 unités de haut.

`ImageToPdfForm.cs:534–549` force la hauteur de fenêtre à celle du panneau latéral et augmente `MinimumSize`; son hôte latéral a `AutoScroll = false` (`:187`). Le contenu dicte ainsi une fenêtre potentiellement plus haute que l'écran.

**Action :** dimensionnement initial limité à la zone de travail du moniteur choisi, contenu défilant et boutons de validation toujours visibles. Pour les éditeurs, aperçu flexible et panneau d'options défilant. Une taille minimale ne doit pas rendre impossible l'affichage sur une configuration supportée.

### UI-05 — P1 — Contraintes de layout contradictoires, même à 100 %

**Confirmé dans les valeurs du code; rendu final à contrôler :**

- **Interpolate Video** : fenêtre de largeur `440` (`Forms/InterpolateVideoForm.cs:27`) et bandeau commun de largeur `536`, placé à `x=12`. Le bord droit du bandeau arrive à `548`, soit `108` au-delà de la largeur cliente, sans adaptation locale.
- **Cut Audio** : `ClientSize = 960 × 540`, puis `MinimumSize` et `MaximumSize` à `960 × 540` (`:87–89`). Les deux dernières concernent la fenêtre avec sa chrome, pas la zone cliente. De plus, la grille des champs demande `26 + 26` dans une ligne de `46` (`:126`, `:151–152`), et le contenu supérieur fixe laisse peu de place à la structure externe.
- **Progress** : bloc haut de `188` contenant un padding vertical de `40` et des lignes demandant `22+38+28+28+48 = 164`, soit au moins `204` avant marges. L'en-tête de file demande `28+22+18+8 = 76` dans un panneau de `66` (`ProgressForm.cs:79–85`, `:167–171`, `:189–216`).

**Action :** résoudre le budget de hauteur par la structure et la mesure; ajouter des vérifications ciblées des bornes visibles. Augmenter quelques nombres ne règle pas la famille de défauts.

### UI-06 — P1 — Reconfiguration dynamique qui réintroduit des coordonnées brutes

**Confirmé.** `AI/CreateSubtitlesPickerForm.cs:222–240` réassigne au changement SRT/ASS une largeur cliente `560`, des hauteurs `594/754` et les positions des boutons. `AI/BriaModelNoticeForm.cs:179–195` réapplique `116 × 34` lors du changement d'état. Plusieurs pickers ne recalculent leur footer qu'en `OnShown`.

**Action :** masquer/afficher des lignes de contenu, laisser le conteneur recalculer sa taille, conserver le footer dans une ligne indépendante. Rejouer les changements d'options après déplacement DPI et après réduction de la fenêtre.

### UI-07 — P1 — Champs, listes et textes longs insuffisamment contraints

**Confirmé.** `CreateFixedTextInputHost` (`FrameShiftUiFactory.cs:202–209`) place le texte à `y=7` une seule fois; `CreateTextInputHost` (`:186–199`) le recentre sur redimensionnement, sans garantir que le parent est assez haut. Certaines descriptions sont limitées à `16–18` pixels; les titres et noms de modèles ont des largeurs imposées.

`ActionsPanel.cs:225–240` crée chaque bouton à `152 × 34`, quel que soit son libellé ou son compteur. Les grilles de fichiers et de progression utilisent respectivement des lignes de `36` et `40` (`FileQueuePanel.cs:428`, `ProgressForm.cs:676`). Ce sont des points à vérifier avec les métriques réelles de police, pas des défauts visuels automatiquement démontrés par ces constantes.

**Action :** lignes de champs mesurées, colonne de saisie extensible, unités distinctes, largeur minimale calculée sur la valeur représentative. Retour à la ligne pour explications et erreurs; ellipsis réservé aux métadonnées avec accès au texte complet. Mesurer les hauteurs des grilles et conserver nom complet/chemin via copie ou détail accessible.

### UI-08 — P1 — Dessin personnalisé et zones de clic sans contrat DPI

**Confirmé.** `JoinVideosTimelineControl.cs:14–18` fixe hauteur, padding, vignette et largeur interactive minimale (`12`); le seuil de drag est `5` (`:160`). Les sélections vidéo/GIF, waveform audio et poignées de crop possèdent également leurs propres calculs de peinture et souris. `FrameShiftUiPainter` utilise rayons et épaisseur de trait reçus sans adaptation.

`SeekTrackBar.cs:47–62` transforme toute la largeur du contrôle en intervalle de valeurs, sans utiliser le rectangle réel de piste du contrôle natif : vérifier l'écart entre position cliquée et curseur, particulièrement aux extrémités et aux différents DPI.

**Action :** partager la géométrie entre peinture et hit-test, convertir les seules métriques visuelles logiques, utiliser la géométrie native de la piste si nécessaire. Ne jamais appliquer le DPI UI aux coordonnées source d'un crop, d'un masque, d'un temps ou d'une page PDF. Prévoir un mode de sélection alternatif quand une timeline contient beaucoup de clips très courts.

### UI-09 — P1 — Aperçus bloquants et comportement de fermeture disparate

**Confirmé.** Attentes synchrones d'aperçus dans Cut Video (`:471`), chargement initial GIF (`:551`), certains chargements Crop Image (`:235`), Rotate/Flip Image (`:175`) et Image to PDF (`:3596`). Les constructeurs ou gestionnaires concernés s'exécutent sur le fil UI. Cela peut retarder affichage, peinture, déplacement et annulation. Ce n'est pas une conséquence du DPI.

À conserver : plusieurs autres écrans utilisent déjà des tâches annulables; `CutAudioFormLifetime`, `OnnxFormLifetime` et leurs tests donnent des exemples de durée de vie explicite. `DownloadModelForm.cs:199–202` annule à la fermeture, mais ne suit pas la même attente de fin que les éditeurs IA; le besoin exact doit être vérifié selon le téléchargement et son nettoyage.

**Action :** fenêtre affichée avant chargement, état « chargement », `await`, jeton lié à sa durée de vie, résultat obsolète ignoré, remplacement/libération des bitmaps sur le fil UI. Annuler proprement les requêtes antérieures. Conserver l'exécution FFmpeg/FFprobe dans les runners existants; ne pas les déplacer dans un composant visuel.

### UI-10 — P2 — Clavier, focus et accessibilité non standardisés

**Confirmé.** Aucun `AccessibleName`, `AccessibleDescription`, `AccessibleRole` ni `TabIndex` explicite trouvé dans la couche Windows. Cela ne signifie pas que les contrôles natifs sont tous inaccessibles; ils ont des comportements par défaut. En revanche, les éléments composites doivent être vérifiés.

- `ActionsPanel.CreateChip` (`:260–284`) crée des `Label` avec `Click`; ils ne fournissent pas le comportement clavier d'un bouton.
- Les sélecteurs d'unité de compression et ceux de modèles/RIFE utilisent panneaux, labels et menus; leur ouverture au clavier n'est pas définie comme pour une `ComboBox`.
- Settings définit Enter via `AcceptButton`, mais pas Escape via `CancelButton`.
- Join Videos a des raccourcis de suppression et déplacement, mais pas de `AcceptButton`/`CancelButton` ni de traitement Escape visible.
- Download Model et Remove Object n'ont pas le même contrat Enter/Escape que les pickers. L'absence d'une validation par Enter peut être volontaire : l'action longue ou destructive ne doit pas être déclenchée accidentellement.
- Image to PDF possède déjà `ProcessCmdKey`, notamment Escape : ne pas assimiler l'absence de `CancelButton` à une absence de raccourci.

**Action :** contrôles natifs pour choix et filtres, ordre Tab explicite pour les blocs complexes, focus visible, noms accessibles pour glyphes et canevas, accès clavier aux actions essentielles. Définir Escape par état : quitter un outil, arrêter l'aperçu, demander l'annulation ou fermer quand c'est sûr.

### UI-11 — P2 — Couleurs et thèmes : partage utile, contrats incomplets

**Confirmé.** Le thème central gère Light/Dark/System et remappe récursivement des valeurs RGB (`FrameShiftTheme.cs`). Cela reste fragile pour les couleurs locales et les styles de cellules individuels. Le thème système est résolu au démarrage ou au changement de préférence, sans écoute de changement Windows dans le code inspecté. Les fenêtres d'action lancées dans un autre processus ne sont pas dans `Application.OpenForms` du hub.

Les menus d'unités de compression forcent `ToolStripRenderMode.System`, contournant le renderer commun. Les fonds de prévisualisation volontairement sombres doivent être distingués des surfaces de formulaire; ne pas remapper les pixels des médias.

Contrastes calculés sur les couleurs déclarées, sans dépendre de la capture :

| Texte / fond | Ratio approximatif | Cas |
|---|---:|---|
| Blanc / `#8EBAF3` | 2,01:1 | Survol du bouton principal dans `FrameShiftUiFactory.CreateActionButton`. |
| Blanc / `#4D79B4` | 4,46:1 | Bouton principal au repos, juste sous 4,5. |
| `#7A8495` / blanc | 3,78:1 | Texte atténué du thème clair. |
| `#7A8495` / `#F5F7FB` | 3,52:1 | Texte atténué sur fond de page clair. |
| `#C62828` / `#202735` | 2,66:1 | Texte rouge Cancel all sur surface sombre dans Progress. |

Pour le texte utile de petite taille, adopter 4,5:1 comme cible de lisibilité; le W3C distingue le texte normal du grand texte et des contrôles désactivés. C'est ici un repère de conception, pas une déclaration de conformité du logiciel. [Référence W3C](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html).

**Action :** après stabilisation, corriger les associations texte/fond problématiques et les états des composants communs en conservant l'identité bleue. Vérifier le contraste élevé Windows et documenter ses limites; sa prise en charge exhaustive est différée. Documenter la portée du changement de thème entre processus, sans ajouter un mécanisme IPC uniquement pour l'esthétique.

### UI-12 — P2 — Icônes et ressources graphiques

**Confirmé.** `PopulateHeader` crée un bitmap de `38 × 38` et utilise `ImageLayout.None`; aucun renouvellement selon le DPI n'est présent. Une icône peut donc paraître trop petite ou perdre sa netteté après changement d'écran. Les offsets d'icônes sont des coordonnées visuelles à convertir eux aussi.

`ActionsPanel` reconstruit les listes à chaque recherche et sélection par `Controls.Clear()` sans disposer explicitement les anciens contrôles. La propriété et la libération des bitmaps de bandeaux, icônes et polices créés localement ne sont pas un contrat commun. Ce sont des risques de ressources à mesurer, pas la preuve d'une fuite mémoire chiffrée.

**Action :** choisir une taille d'icône adaptée au DPI, régénérer/libérer les bitmaps au bon moment, disposer les contrôles remplacés et les `ToolTip` détenus. Tester ouvertures/fermetures répétées et recherches successives en observant handles GDI/USER et mémoire.

### UI-13 — P2 — Duplication et exceptions accumulées

**Confirmé.** Les trois compressions répètent leurs tuiles et sélecteurs; les deux Remove Noise sont très proches. Main, Settings et Progress ont leurs propres marges/typographies; plusieurs éditeurs reconstruisent le root layout. Les footers cumulent helpers fixes, mesure partielle, anchors et calculs locaux. `ImageToPdfForm` représente près de 5 000 lignes : les changements visuels y sont difficiles à isoler.

**Action :** mutualiser les éléments visuels dont le partage facilite directement la correction; laisser le métier dans les classes actuelles et le Core. Le découpage massif des gros éditeurs est différé. Éviter une base générique à dizaines d'options ou un framework de formulaires déclaratifs.

### UI-14 — P2 — Documents et critères de validation trop affirmatifs

**Confirmé.** `docs/UI_DPI_AUDIT.md` affirme des corrections et une stratégie explicite, puis précise qu'il n'existe pas de helper DPI distinct. `UI_STANDARDIZATION.md` décrit des règles utiles mais des tailles souvent fixes. Les constats actuels montrent que ces textes ne constituent pas une garantie d'application uniforme.

Les tests `FrameShiftThemeTests`, `JoinVideosFormTests`, `AddSubtitlesToVideoBurnEditorFormTests` et les tests de durée de vie sont utiles, mais ne remplacent pas une recette visuelle par configuration.

**Action :** séparer historique, contrat normatif et preuves de recette; chaque validation doit indiquer version, résolution, DPI, écran et scénario. Ne plus annoncer « DPI-safe » sur la seule présence d'un helper ou d'une propriété.

## 5. Inventaire et cible de migration de toutes les fenêtres

Chaque ligne reste à valider en exécution. « Dpi » décrit une déclaration dans le code, pas un résultat de test. Les identifiants UI renvoient aux constats ci-dessus. Les cibles ci-dessous décrivent les corrections possibles; leur ordre et leur périmètre obligatoire sont fixés par la section 7. Elles ne rendent pas les P2 préalables aux migrations P1.

### Hub et utilitaires — 4 fenêtres

| Fenêtre | État et particularité | Cible |
|---|---|---|
| `MainForm` | DPI non explicite; split et proportions recalculés lors du changement de file; styles locaux. | Politique commune, limites de panneaux, conservation du choix de séparation, validation petite largeur; UI-01/04/07/13. |
| `SettingsForm` | DPI non explicite; sections aux positions fixes, chemin tronqué, fermeture clavier partielle. | Shell compact, chemin copiable, champs adaptatifs, Escape; UI-01/07/10. |
| `MediaInfoForm` | Dpi; fenêtre fixe `560 × 500/680`; texte copiable et scrollbars déjà présents. | Fenêtre redimensionnable, texte en Fill, footer commun; UI-04/07. |
| `ProgressForm` | DPI non explicite; budgets de hauteur contradictoires, tableau et donation, annulation différée existante. | Structure spécifique avec composants communs; statut extensible, footer permanent, préserver le contrat d'annulation; UI-01/05/11. |

### Paramètres et transformations — 15 fenêtres

| Fenêtre | État et particularité | Cible |
|---|---|---|
| `ConversionPickerForm` | Dpi; hauteur variable calculée à la construction, positions fixes. Sert plusieurs conversions. | Shell compact à lignes de contenu; tester toutes ses variantes; UI-03/04/07. |
| `CompressMultiFileChoiceForm` | Dpi; tuiles de choix et footer recalculé au premier affichage. | Choix natifs et footer commun; UI-02/10. |
| `CompressAudioForm` | Dpi; trois tuiles, cible/unité, footer OnShown. | Composants partagés compression; UI-02/07/10/13. |
| `CompressVideoForm` | Dpi; structure presque identique à Audio. | Même famille visuelle, sans fusionner les règles métier; UI-02/07/10/13. |
| `CompressImageForm` | Dpi; format et activation de cible, footer déplacé au premier affichage. | Même famille, tester chaque format/état; UI-02/06/07/10. |
| `ResizeImageForm` | Hérite du Dpi de `ResizeMediaFormBase`; formulaire par coordonnées. | Migrer la base une seule fois; UI-03/07. |
| `ResizeVideoForm` | Même base; contraintes propres à la vidéo. | Réutiliser le même visuel, conserver validations vidéo; UI-03/07. |
| `InterpolateVideoForm` | Dpi; bandeau débordant `536` dans fenêtre `440`. | Corriger via bandeau responsive et shell compact; UI-05. |
| `ChangePitchForm` | Dpi; presets, option et aperçu, footer à trois actions de largeurs locales. | Ligne de presets adaptative et zone d'aperçu distincte; UI-07/13. |
| `ChangeSpeedForm` | Dpi; variantes audio/vidéo et conservation du pitch, footer local. | Même shell, scénario audio et vidéo avec/sans audio; UI-07/13. |
| `RotateFlipImageForm` | Dpi; aperçu et sections fixes; chargement synchrone possible. | Shell avec aperçu flexible, rangée de transformations commune; UI-04/09. |
| `RotateFlipVideoForm` | Dpi; fenêtre fixe plus haute; initialisation asynchrone présente. | Même famille, timeline dimensionnée au contenu; UI-04/08. |
| `ConvertToIconForm` | Dpi; nombreuses lignes/colonnes absolues, grille de vignettes et footer manuel. | Contenu mesuré, tailles ICO indépendantes du DPI de leur affichage; UI-02/04/08. |
| `AddSubtitlesToVideoPickerForm` | Dpi; mode et fichier aux positions fixes. | Shell compact, champ fichier extensible, message de validation visible; UI-03/07. |
| `CreateGifForm` | DPI non explicite; hauteur minimale `800`, sélection dessinée, aperçu initial synchrone. | Shell éditeur, contrôles mesurés et aperçu annulable; UI-01/04/08/09. |

### Éditeurs média — 7 fenêtres

| Fenêtre | État et particularité | Cible |
|---|---|---|
| `CutVideoForm` | Cas signalé; DPI non explicite, petits champs temps, dessin et capture synchrones. | Pilote prioritaire du shell éditeur et des champs temporels; UI-01/02/03/07/08/09. |
| `CutAudioForm` | DPI non explicite; FixedDialog + maximum/minimum confondus avec client; waveform et champs. | Taille redimensionnable, waveform flexible, préserver durée de vie/audio; UI-05/08. |
| `CropImageForm` | DPI non explicite; shell crop déjà commun, footer/outils remis en pixels bruts. | Migrer la famille partagée; valider coordonnées source et chargement; UI-01/02/08/09. |
| `CropVideoForm` | Même shell, navigation temporelle supplémentaire. | Même migration avec validation seek/crop exacte; UI-01/02/08. |
| `ImageToPdfForm` | DPI non explicite; hauteur forcée selon rail, nombreuses responsabilités et coordonnées document. | Rail défilant, commandes persistantes, extraction progressive des blocs UI; UI-04/08/09/13. |
| `JoinVideosForm` | Dpi; timeline maison, toolbar/footer spécifiques, raccourcis de déplacement présents. | Shell éditeur et actions communes; dimensions et sélection des clips accessibles; UI-02/08/10. |
| `AddSubtitlesToVideoBurnEditorForm` | Dpi; rail AutoSize/AutoScroll déjà utile, grand minimum, shell crop et footer manuel. | Conserver le rail, corriger shell/minimum et avertissements; UI-02/04/07. |

### IA et modèles — 10 fenêtres

| Fenêtre | État et particularité | Cible |
|---|---|---|
| `RemoveNoiseAudioPickerForm` | Dpi; choix, aperçu et footer, fermeture différée. | Composants visuels communs avec vidéo, conserver l'annulation; UI-02/07/13. |
| `RemoveNoiseVideoPickerForm` | Même structure, presque dupliquée. | Même famille, scénarios spécifiques vidéo; UI-02/07/13. |
| `SeparateAudioPickerForm` | Dpi; choix stems/moteur, descriptions fixes. | Shell compact; textes modèles et états disponibles/indisponibles; UI-03/07. |
| `RifeInterpolateVideoPickerForm` | Dpi; menus construits depuis panneaux, footer OnShown. | Choix natifs, contenu mesuré, footer commun; UI-02/10. |
| `UpscaleImagePickerForm` | Dpi; modèle via menu maison, hauteurs selon options. | Shell compact, choix natifs, grille de taille personnalisée; UI-06/07/10. |
| `UpscaleVideoPickerForm` | Hérite de la précédente; exemple de partage existant à conserver. | Vérifier toutes les options vidéo après migration de la base. |
| `CreateSubtitlesPickerForm` | Dpi; SRT/ASS réécrit tailles/positions à l'exécution, modèle/preset sur lignes fixes. | Lignes visibles/masquées AutoSize, scroll, footer stable; UI-04/06. |
| `RemoveObjectEditorForm` | DPI non explicite; rail défilant déjà présent, footer local, canevas/masque. | Shell éditeur, coordonnées image inchangées, annulation existante préservée; UI-01/08/10. |
| `DownloadModelForm` | Dpi; progression et messages fixes, clavier et fermeture spécifiques. | Shell de progression compact; erreurs repliables/copiables, états et annulation explicites; UI-07/09/10. |
| `BriaModelNoticeForm` | Dpi; jusqu'à cinq boutons de largeur fixe, layout réappliqué après Re-check. | Message extensible, actions secondaires pouvant revenir à la ligne; UI-06/07. |

Total : **4 + 15 + 7 + 10 = 36 fenêtres concrètes**. Les variantes audio/vidéo de Create Subtitles utilisent une seule classe, mais doivent être testées dans les deux contextes. Même règle pour les variantes de Conversion et Change Speed.

### Surfaces complémentaires

| Surface | Audit et action |
|---|---|
| `ResizeMediaFormBase` | Seule base abstraite de formulaires recensée. Intégrer la politique commune sans mélanger modes de scaling dans la hiérarchie. |
| `ActionsPanel` | Boutons de taille fixe créés dynamiquement, filtres labels cliquables, `Controls.Clear`. Boutons mesurés, filtres clavier, disposition et libération des ressources. |
| `FileQueuePanel` | DataGridView, lignes fixes, suppression clavier présente. Mesurer les lignes et actions, vérifier très longs noms, sélection et chemins complets. |
| `JoinVideosTimelineControl` | Géométrie de peinture, thumbnails et hit-test à adapter ensemble. La taille d'une cible ne doit pas devenir négligeable avec beaucoup de clips. |
| `SeekTrackBar` | Garder le contrôle natif; vérifier la conversion position/valeur avec les limites réelles de sa piste. |
| Dialogues Windows | Open/Save/Folder/Color/Print et MessageBox : conserver le comportement natif; vérifier propriétaire, écran et retour du focus. Une fenêtre de premier niveau lancée dans un processus distinct peut légitimement ne pas avoir d'owner. |
| Installateur Inno Setup | Les pages modèles IA et installation existante emploient déjà `ScaleX/ScaleY` (`FrameShift.iss:1327–1366`, `:1513–1540`). Vérifier les hauteurs de texte fixes et les descriptions longues à fort DPI. Partager les règles visuelles et la recette, pas le code WinForms. Harmoniser le libellé « Outils » au milieu des libellés anglais. |

## 6. Cadre d'implémentation retenu

### 6.1 Un petit socle, deux structures, des composants composables

Conserver `Windows/Helpers` et `Windows/Controls`. Il n'est pas nécessaire de déplacer tout le dépôt ni d'ajouter un package UI, MVVM, un conteneur DI, une couche de services ou une nouvelle technologie.

| Élément | Évolution proposée | Responsabilité exacte |
|---|---|---|
| `FrameShiftTheme` | Conserver; corrections ciblées en F | Lisibilité des couleurs et états; aucune disposition. Support exhaustif du contraste élevé différé. |
| `FrameShiftUiMetrics` | Conserver | Espacements et dimensions minimales **logiques à 96 DPI**, vocabulaire unique. |
| Politique commune de fenêtre | Consolider avec le minimum de code | Initialisation DPI/font et adaptation à l'espace de travail. `FrameShiftForm` n'est pas obligatoire : décider en B si une base très fine simplifie réellement les appels et événements communs, puis vérifier en C. |
| Conversion DPI commune | Petit utilitaire si nécessaire | Conversion de métriques logiques pour peinture/hit-test/calcul manuel seulement; jamais `Scale()` récursif sur l'arbre. Un fichier `FrameShiftDpi` n'est utile que si les usages le justifient. |
| `FrameShiftUiFactory` | Simplifier progressivement | Construction des contrôles standards; pas de positions absolues décidées à la place du parent. |
| Structure de dialogue compact | Helper simple, éventuellement `FrameShiftDialogLayout` | Bandeau, corps, footer. Corps défilant si nécessaire; footer indépendant. Pas de moteur générique de formulaires. |
| `FrameShiftEditorShellUi` | Étendre l'existant | Bandeau, workspace flexible, rail facultatif défilant, statut facultatif, footer permanent. |
| `FrameShiftCropEditorUi` | Réduire à une spécialisation | Compose le shell d'édition et ajoute les options crop; ne possède plus une politique générale différente. |
| `FrameShiftUiLayout` | Réduire | Algorithmes manuels vraiment nécessaires; éliminer ceux remplacés par layout natif. |
| `FrameShiftUiPainter` / chrome | Conserver, corriger | Dessin adapté au DPI, images et ressources; titre Windows standard. |

Privilégier la composition et éviter une hiérarchie de bases « Editor/Dialog/AI/Media/Preview ». Toute base éventuelle reste technique et très fine. Main et Progress conservent leur structure fonctionnelle particulière tout en utilisant les mêmes primitives. Les noms de nouveaux helpers ne constituent pas une liste de classes à créer systématiquement.

### 6.2 Composants à standardiser en premier

| Composant | Contrat attendu |
|---|---|
| Bandeau | Icône adaptée au DPI, titre et sous-titre en grille, hauteur calculée, métadonnées longues consultables. |
| Section | Titre mesuré (`AutoSize` ou mesure explicite selon le conteneur), contenu distinct, padding/gap communs, pas de titre redimensionné à `18` pixels. |
| Barre d'actions | Principal à droite, secondaire à côté, commandes auxiliaires séparées; largeur d'après texte + minimum logique; footer hors scroll. |
| Ligne de champ | Label associé, éditeur extensible, unité/aide optionnelle; hauteur d'après `PreferredSize`, ordre Tab et nom accessibles. |
| Message d'état | Texte pouvant revenir à la ligne, rôle info/avertissement/erreur, détail consultable et copiable si long. |
| Choix/présets | `RadioButton`, `CheckBox`, `ComboBox` ou `Button` natifs avec le style commun; comportement clavier hérité et testé. |
| Zone d'aperçu | Fond média, contenu ajusté au ratio, état chargement/erreur/vide, nettoyage explicite. La logique média reste propre à l'action. |

Extraire les tuiles de compression, options de débruitage et champs temps seulement quand deux écrans en ont besoin. Le code partagé doit encapsuler une responsabilité stable, pas seulement renommer une répétition de `new Panel`.

### 6.3 Politique DPI à mettre en œuvre et vérifier

1. Développer et tester la configuration `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` pendant B/C, avec initialisation avant toute création de fenêtre. Son activation dans un build de chantier ne signifie pas qu'elle est publiable. Ne pas ajouter en parallèle des configurations DPI contradictoires dans un manifeste ou dans des appels dispersés. La propriété est prise en charge par le SDK .NET utilisé. [Microsoft](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop#applicationhighdpimode).
2. Fixer un contrat de construction logique 96 DPI, `AutoScaleMode.Dpi` et `AutoScaleDimensions = new SizeF(96F, 96F)`, dans un ordre cohérent avant le premier layout. Utiliser `SuspendLayout/ResumeLayout` lors des constructions complexes.
3. Laisser WinForms gérer la mise à l'échelle des contrôles natifs. La référence de conception et la mesure d'exécution sont deux notions distinctes; ne pas réassigner la référence à chaque resize. Les mélanges de modes dans une hiérarchie de formulaires sont à éviter. [Microsoft — mise à l'échelle automatique](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/forms/autoscale).
4. Calculer seulement les métriques manuelles à partir de `DeviceDpi / 96f`, sans modifier les valeurs source. Au changement DPI, invalider/recalculer dessin, bitmaps et hit-test. Ne pas multiplier une police en points en plus de son adaptation normale.
5. Traiter les contrôles ajoutés dynamiquement après le premier affichage : un bouton créé à la recherche d'action ne doit pas rester à une taille physique `152 × 34` sur un écran à 200 %. Préférer `AutoSize` et des mesures dans son contexte réel.
6. Mesurer aussi les textes : le DPI et le réglage d'accessibilité « taille du texte » demandent des essais séparés. Aucun multiplicateur DPI seul ne garantit cette seconde compatibilité.

**Limite de validation :** `PerMonitorV2` est un réglage de processus. Tester ses effets sur les pilotes et les consommateurs existants à chaque vague. Le GO après C autorise uniquement la généralisation du socle; il ne valide pas la publication de l'activation globale. Pour cette feuille de route, la décision de publication est prise en G sur les preuves de migration D/E et de qualification correspondantes, jamais sur le seul succès des pilotes.

**Nuances obligatoires :** pas d'`AutoSize` généralisé sans vérifier le comportement du conteneur; pas de multiplication DPI systématique des dimensions. Les métriques fixes logiques restent possibles quand elles sont justifiées. Mesure du texte, layout natif et calcul manuel ont chacun un rôle précis.

### 6.4 Règles de dimensionnement et de contenu

- Palette d'espacements limitée : conserver par défaut `12` pour le bord externe, `8` pour la petite séparation, `10/12` entre blocs selon le contrat final. Les noms des tokens définissent leurs usages.
- Police par défaut unique, par exemple Segoe UI 9 pt pour conserver la densité actuelle; titre commun 14 pt. Un choix ultérieur de 10 pt doit passer par le même contrat et la même recette.
- Hauteur de bouton minimale logique `34`, largeur d'après son texte; aucune largeur imposée ne doit couper une commande.
- Lignes contenant du texte : `AutoSize` ou mesure explicite. Lignes `Percent` réservées aux espaces flexibles comme aperçu, liste, tableau. `Absolute` reste valable pour un espacement logique maîtrisé ou une zone graphique justifiée.
- `Dock`, `Anchor`, `Padding` et `Margin` sont des outils, pas une preuve de qualité. Définir leur rôle et éviter les marges implicites dans les grilles serrées.
- Fenêtre compacte : bandeau / corps / footer. Éditeur : bandeau / workspace et options / état / footer. Footer accessible sans défiler le document.
- Définir les tailles clientes séparément des tailles extérieures. Borner au `WorkingArea` du moniteur choisi, dans le même espace de coordonnées, après connaissance de son DPI. Réexaminer les minima si la zone disponible diminue.
- Titres et commandes : aucun texte tronqué. Métadonnées : ellipsis possible avec accès complet. Erreurs/instructions : retour à la ligne, pas d'ellipsis qui masque la décision à prendre.
- Une image 4K reste une image 4K dans le Core. Zoom d'aperçu, DPI Windows, coordonnées image et dimensions PDF sont quatre notions séparées.
- Modal avec propriétaire quand il existe; premier niveau centré sur l'écran choisi quand l'action est lancée séparément. Réouverture/restauration ne doit pas placer une fenêtre hors écran.

## 7. Feuille de route officielle A → G

**Ordre d'exécution : A → validation → B → validation → C → GO/NO-GO → D → E → F → G.** A établit une baseline ciblée; B consolide le socle; C vérifie sa pertinence avant généralisation. D commence par Progress et migre les surfaces communes et dialogues; E termine les éditeurs. F traite les P2 après stabilisation des P1; G qualifie la distribution complète. Ne pas alterner D/E/F au détriment de cet ordre.

### 7.1 Priorités et périmètre obligatoire

| Niveau | Contenu | Traitement |
|---|---|---|
| **P1 obligatoires** | UI-01 à UI-09 : cohérence DPI, helpers runtime, mesure des textes, manque de place, contraintes contradictoires, changements dynamiques, saisies, dessin/hit-test et aperçus bloquants. | A à E, puis qualification en G. Annulation, sorties et séparation Core/Windows restent préservées. |
| **P2 après stabilisation** | UI-10 à UI-14 dans leur périmètre utile : clavier/focus, contrastes et états, icônes et ressources, duplications directement concernées, documentation et garde-fous pour les futures UI. | F. Un obstacle empêchant effectivement l'usage est reclassé P1 dès sa découverte; les règles de test et de traçabilité s'appliquent dès A. |
| **Recommandations volontairement différées** | Refonte graphique étendue ou changement global de police; synchronisation du thème entre processus; prise en charge exhaustive du contraste élevé et de l'accessibilité avancée; découpage massif d'ImageToPdf; extraction généralisée de composants média sans besoin démontré. | Hors critères de réalisation de ce chantier. Documenter les limites; une anomalie bloquante découverte reste à corriger. |

Une base `FrameShiftForm` est une option à décider en B et évaluer en C, pas un livrable imposé. Pas de framework UI maison, d'`AutoSize` aveugle, de multiplication DPI systématique ni de réécriture métier. Les améliorations de réactivité P1 restent dans des changements distincts des corrections de géométrie.

### A — Baseline ciblée et contrat cible — P1

- **Objectif :** établir des reproductions et un état de référence suffisants pour commencer B; fixer les règles logiques/pixels, de mesure du texte et d'espace disponible.
- **Périmètre principal :** configuration/démarrage, inventaire statique; Cut Video pour le signalement, Interpolate Video pour le bandeau, Cut Audio et Progress pour les contraintes de hauteur, Image to PDF pour l'espace réduit. Aucun correctif applicatif à cette phase.
- **Dépendances :** aucune. Relever le commit, la version et le chemin du binaire réellement examiné.
- **Critère de sortie :** reproductions ciblées ou limites de reproduction explicites, défauts préexistants identifiés, contrat commun défini et validation de passage à B consignée. A ne demande ni une recette des 36 fenêtres ni toutes les combinaisons de la matrice finale.
- **Validation nécessaire :** build de référence et tests existants selon 8.1; essais ciblés à 100 % et à une échelle élevée disponible, avec au moins un cas d'espace réduit. Ajouter un scénario seulement s'il éclaire une cause. Les configurations non disponibles restent non validées, sans transformer A en qualification exhaustive.

#### Relevé d'exécution A — 28 septembre 2026

**Référence.** Commit `9ff280a`, version déclarée `1.19.1`. Binaire examiné : `src/FrameShift/bin/Debug/net8.0-windows/FrameShift.exe`, SHA-256 `A8DE45E945303768040579BFDB4D72C9443D4868EBCC714D18CDD95647516A4C`. Aucun fichier applicatif n'a été modifié. `dotnet build src/FrameShift/FrameShift.csproj --no-restore` : succès, 0 avertissement, 0 erreur. `dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj --no-restore` : 463 réussis, 5 ignorés, 0 échec. Il s'agit de la configuration Debug du dépôt, pas de la distribution installée.

**Environnement réellement relevé.** Windows `10.0.26200.0`; deux écrans `2560 × 1440`, zone de travail initiale `2560 × 1392`, `96 DPI / 100 %` sur chacun. L'utilisateur a ensuite réglé temporairement un écran à `200 %` pour les captures ciblées, sans changement de résolution physique. La taille du texte Windows, les substitutions DPI de compatibilité et le mode DPI du binaire publié n'ont pas été vérifiés. Aucun écran 4K physique ni configuration multi-écran à DPI différents n'a été exécuté.

**Méthode et portée des images.** Un outil temporaire a construit les cinq formulaires WinForms avec une vidéo MP4, un WAV et une image PNG locaux dont les chemins contiennent des espaces et des accents, puis a relevé les bornes des contrôles à `DeviceDpi = 96`. Quatre formulaires ont été affichés sur un bureau de test séparé et rendus avec `DrawToBitmap`, sans interaction avec le bureau de l'utilisateur : [Cut Video](ui-baseline-a/cut-video-100.png), [Interpolate Video](ui-baseline-a/interpolate-video-100.png), [Cut Audio](ui-baseline-a/cut-audio-100.png), [Progress](ui-baseline-a/progress-100.png). La méthode de capture a coupé une partie du bas de ces images : cette coupe de l'image n'est **pas** une preuve de footer coupé dans l'application. Les actions CLI et leurs sorties n'ont pas été exécutées. Image to PDF a été construit pour la mesure de sa taille, mais son affichage n'a pas abouti : l'outil de capture a provoqué une erreur Windows, signalée par l'utilisateur. L'outil a été arrêté et retiré. **Aucune capture Image to PDF n'est retenue comme preuve visuelle.** Aucun processus de cet outil ou de FrameShift n'est resté actif après l'arrêt.

**Complément manuel fourni par l'utilisateur.** Sur l'écran `2560 × 1440` à **100 % confirmé par l'utilisateur** (surface maximisée capturée `2560 × 1392`), [Cut Video en taille par défaut](ui-baseline-a/cut-video-manual-default-100.png) et [maximisée](ui-baseline-a/cut-video-manual-maximized-100.png), ainsi qu'[Image to PDF en taille par défaut](ui-baseline-a/image-to-pdf-manual-default-100.png) et [maximisée](ui-baseline-a/image-to-pdf-manual-maximized-100.png), ont été capturées sur les mêmes médias test. Une cinquième image fournie montre **Interpolate Video (RIFE)**, autre fenêtre que **Interpolate Video** du périmètre A : elle n'est pas utilisée pour valider cette dernière. Aucun traitement média n'a été lancé pendant ces observations.

**Essai manuel à échelle élevée et en espace réduit.** L'utilisateur a réglé le même écran physique `2560 × 1440` à **200 % confirmé**, puis fourni [Cut Video en taille d'ouverture](ui-baseline-a/cut-video-manual-default-200.png) et [maximisée](ui-baseline-a/cut-video-manual-maximized-200.png), ainsi qu'[Image to PDF en taille d'ouverture](ui-baseline-a/image-to-pdf-manual-default-200.png) et [maximisée](ui-baseline-a/image-to-pdf-manual-maximized-200.png). La capture maximisée mesure `2560 × 1344` pixels physiques, soit environ `1280 × 672` unités logiques pour la zone de travail à 200 %. Ce palier fournit le cas réel d'espace logique réduit demandé en A. Au moment de l'envoi, l'utilisateur n'avait pas encore remis l'échelle à 100 %; cette remise en état lui a été demandée, sans action de l'agent sur le bureau. La taille du texte Windows séparée de l'échelle d'affichage n'a pas été relevée.

| Fenêtre ciblée | Reproduction ou mesure à 100 % | Statut du symptôme |
|---|---|---|
| **Interpolate Video** | Formulaire client `440 × 384`; bandeau à `x=12`, largeur `536`, bord droit `548` : dépassement de `108` pixels. Le sous-titre est visiblement tronqué sur la capture. | **Reproduit visuellement à 100 %.** UI-03/UI-05. |
| **Cut Audio** | Fenêtre extérieure `960 × 540`, zone cliente `944 × 501`. Le conteneur d'édition mesuré à `183` de haut contient une ligne waveform de `214`, un écart de `8` et une grille de `46` : les champs sortent du conteneur et ne figurent pas dans la capture initiale. La préparation de la waveform était encore en cours. | **Débordement de layout reproduit à 100 %**; l'état final après préparation et les gestes d'édition ne sont pas validés. UI-05. |
| **Progress** | Fenêtre extérieure `1060 × 640`, cliente `1044 × 601`. Le panneau bas du bandeau atteint `y=161` dans un parent haut de `142` (débordement géométrique `19`); le libellé d'aide de file finit `2` pixels sous son parent. La capture de l'état vide ne montre pas de texte essentiel visiblement coupé. | **Contraintes contradictoires confirmées par bornes runtime**; défaut visuel en file active non reproduit. UI-05. |
| **Cut Video** | À 100 %, fenêtre cliente `1040 × 760` et vidéo test de `4,733 s` : titres, champs temporels, aide et footer lisibles, en taille par défaut et maximisée. À 200 %, le titre chevauche/coupe le sous-titre; « Preview », l'état d'aperçu, « Selection » et la première ligne d'aide sont coupés. Les libellés Start/End perdent « frame/time » et les champs de temps ne montrent plus la valeur complète. Les boutons Cancel/OK restent visibles, même maximisé. | **Symptômes de lisibilité du signalement reproduits à 200 % sur 2560 × 1440**, sans présumer de la configuration 4K d'origine. L'attente synchrone de l'aperçu reste confirmée par le code, sans mesure de blocage UI. UI-02/UI-03/UI-07/UI-09. |
| **Image to PDF** | Le constructeur a porté la zone cliente à `1280 × 888` et `MinimumSize` extérieur à `1080 × 927` à 96 DPI; rail d'options `AutoScroll = false`. À 100 %, toutes les sections et commandes apparaissent en taille d'ouverture et maximisée. À 200 %, « Preview » est coupé, l'état « Active image » chevauche le bouton d'ajout, les libellés des cases Ratio/Snap/Rulers/Inches et plusieurs intitulés de commandes sont tronqués dans le rail, y compris maximisé. Les actions Output et View restent présentes sur les captures. | **Défaut visuel reproduit en espace logique réduit et à 200 %**, en plus de la contrainte minimale confirmée à l'exécution. UI-03/UI-04/UI-07. |

**Écarts et limites par rapport à l'audit initial.** Le dépassement d'Interpolate et le budget de Cut Audio sont observables dès 100 %. Les défauts visuels de Cut Video apparaissent à 200 % sur un écran physique `2560 × 1440` : la résolution 4K physique n'est donc pas nécessaire pour reproduire cette famille de symptômes, même si le cas exact signalé en 4K n'est pas qualifié. Les boutons Cut Video restent visibles aux deux tailles testées, conformément à la prudence de l'audit initial. Dans Progress, les dépassements calculés existent bien mais la capture de l'état vide ne démontre pas encore une coupure perceptible; l'affirmation visuelle attend la file active. Image to PDF est lisible à 100 % dans le grand espace, mais son rail tronque plusieurs textes à 200 %; son minimum réel de `927` pixels de haut dépasse la valeur initiale `760` citée dans le code. Aucun résultat à 150 %, 300 %, sur écran 4K physique ou en déplacement entre moniteurs de DPI différent n'est revendiqué.

**Contrat cible confirmé pour B, sans implémentation en A.** Référence logique `96 DPI` définie une seule fois; distinguer les métriques logiques des `ClientSize`/`Bounds` déjà en pixels courants; ne convertir que les dimensions de dessin manuel avec le DPI du contrôle concerné. Mesurer titre, sous-titre, champs et lignes à partir des polices effectives; laisser les zones de travail flexibles; garder le footer indépendant et accessible; borner la fenêtre au `WorkingArea` du moniteur de lancement. Le DPI de l'interface ne modifie ni temps vidéo, ni coordonnées image, ni dimensions PDF. Les règles détaillées restent celles des sections 6.3 et 6.4.

**Critères de sortie de A : atteints le 28 septembre 2026.** Build et tests de référence verts; inventaire statique (36 fenêtres concrètes et une base abstraite) vérifié; défauts préexistants séparés entre reproductions visuelles, débordements de bornes et risques de code; essais ciblés à 100 % et 200 %, dont le cas d'espace logique réduit; contrat de B rappelé ci-dessus. La baseline est suffisante pour engager B dans un travail ultérieur, **sans commencer B ici**. Les limites ne sont pas converties en validations : état de file active Progress, Cut Audio après chargement, Interpolate Video à 200 %, texte Windows agrandi, 4K physique, 150/300 % et multi-écran DPI mixte restent non testés. L'échec de l'outil de capture ne constitue pas un défaut de FrameShift. Décision de passage : **A validée pour B sur ce périmètre**, avec reprise de ces limites dans les validations B/C/D/E concernées; aucune affirmation de support DPI global ni décision de publication.

### B — Consolidation du socle UI/DPI — P1

- **Objectif :** traiter les causes structurelles dans la couche commune : initialisation cohérente, textes mesurés, layouts adaptables, footer indépendant et conversions manuelles limitées. C'est le chantier technique critique.
- **Périmètre principal :** `FrameShiftUiMetrics`, `FrameShiftUiLayout`, `FrameShiftUiFactory`, `FrameShiftEditorShellUi`, `FrameShiftCropEditorUi`, politique de fenêtre, dessin/icônes et configuration DPI. Deux structures simples : dialogue compact et éditeur avec panneau latéral facultatif. Décider de l'utilité d'une base fine sans l'imposer.
- **Dépendances :** A validée. Préserver temporairement les appels existants lorsque nécessaire pour migrer par lots, avec retrait prévu en F; ne pas maintenir deux systèmes concurrents durables.
- **Critère de sortie :** composants communs vérifiés, hauteur/largeur utiles calculées selon le contenu, contenu défilant si nécessaire, stratégie de moniteur et de contrôles dynamiques définie; aucun calcul commun adopté ne réinjecte des pixels fixes incompatibles. Bilan et validation autorisent le passage aux pilotes C.
- **Validation nécessaire :** build et tests ciblés après chaque lot; essais des primitives à 100/150/200/300 %, petit espace de travail, textes longs et changements DPI répétés. Tester `PerMonitorV2` dans les builds de chantier et vérifier les consommateurs existants affectés. Ne pas assimiler ce résultat à une validation de publication globale.

#### Relevé d'exécution B — 28 septembre 2026

**Autorisation et référence.** L'utilisateur a autorisé B (« go phase B ») après validation de A. Travail local sur la base `9ff280a`, version `1.19.1`; pas de publication ni d'installation. Les preuves et modifications documentaires de A sont conservées. Aucun formulaire métier n'est migré dans ce passage; C reste distincte.

**Socle réalisé.** [Contrat d'utilisation et recette](UI_FOUNDATION.md), [tests](../tests/FrameShift.Tests/UiFoundationTests.cs), [démonstrations](../tests/FrameShift.UiSamples/Program.cs).

| Élément | Implémentation et portée |
|---|---|
| Configuration / politique | `ApplicationHighDpiMode=PerMonitorV2` en Debug seulement. `FrameShiftWindowPolicy.Initialize` prépare 96 DPI, mode Dpi, police commune et minima clients bornés au moniteur. WinForms garde la responsabilité du scaling natif. Pas de nouvelle base `FrameShiftForm` : composition retenue, à réévaluer au bilan C. |
| Mesure / unités | `FrameShiftUiMetrics.ToPixels` convertit les constantes logiques; mesure de texte déjà en pixels conservée. Les composants reconstruisent leurs métriques depuis la référence, sans accumuler les changements de taille. |
| Bandeau / sections / champs | Nouveau bandeau mesuré avec titre multilignes, métadonnées consultables/copiables et icône adaptée; sections AutoSize; champs extensibles dont les labels longs reviennent à la ligne. |
| Compact / éditeur | Une structure commune bandeau/corps/statut/actions. Corps compact défilant; aperçu flexible et rail défilant facultatif, placé sous l'aperçu en largeur réduite. Crop compose la même structure. |
| Commandes / messages | Boutons dimensionnés selon leur texte, retour à la ligne si nécessaire et footer hors du corps défilant. Messages longs sélectionnables/copiables avec défilement propre et hauteur plafonnée. |
| Dessin / ressources | Rayons et épaisseur de bordure adaptés au DPI; géométrie arrondie bornée pour petites surfaces. Icône du nouveau bandeau reconstruite à sa taille courante et ancien bitmap libéré. Les coordonnées média ne sont pas modifiées. |
| Compatibilité temporaire | Les anciennes factories/layouts servent encore les fenêtres non migrées; leurs signatures sont conservées et les entrées de transition sont commentées. Retrait des chemins inutilisés en F. Aucun remplacement global des coordonnées locales des fenêtres. |

**Vérifications exécutées, sans prise de contrôle.** `dotnet build src/FrameShift/FrameShift.csproj --no-restore` et build du projet `tests/FrameShift.UiSamples` : succès, 0 avertissement, 0 erreur. La restauration initiale du nouveau projet a utilisé les packages locaux, sources réseau désactivées; aucun package ajouté à l'application. Suite existante avec filtre sans affichage : **465 réussis, 5 ignorés, 0 échec, 470 cas sélectionnés**. **12 cas affichant des fenêtres ont été exclus** (JoinVideos : deux cas; CutAudio affiché : un; boucle de fermeture ONNX : un; fermeture des pickers RemoveNoise : huit); ils ne sont pas déclarés validés dans ce passage. Journal local : `scratch/phase-b/phase-b.trx`. Après le dernier ajustement du message long, les **14 tests ciblés** ont été rejoués avec succès, puis les deux builds et le contrôle des démonstrations ont été rejoués.

Les tests couvrent conversions **96/144/192/288 DPI**, absence de double conversion du texte, retour aux dimensions de référence, polices agrandies **×1/1,5/2/3**, contenu et messages longs, footer accessible, ajout dynamique, libellés et commandes longs, changements répétés de largeur, coordonnées de moniteur négatives et dessin sur surfaces minuscules. Ils ont notamment détecté puis permis de corriger le débordement d'un très long bouton et le manque de largeur de saisie face à un long libellé.

**Runtime réellement exécuté.** Test STA avec handles natifs cachés, contexte de thread `PerMonitorV2` : **DeviceDpi=96**, formulaire client `740 × 620`; ajout de champ après création des handles vérifié. Le projet de démonstration a été exécuté par commande `--check` sans `Show()` : son bootstrap retourne réellement **HighDpiMode=PerMonitorV2**, les deux fenêtres restent `Visible=false`, clients `680 × 540` et `1000 × 660`, DPI **96**. Résultats locaux : `scratch/phase-b/samples-check.json`. Aucun outil de capture, bureau alternatif, injection clavier/souris ou changement des réglages Windows n'a été utilisé.

**Recette manuelle reçue après implémentation.** L'utilisateur indique que la démonstration paraît correcte à 100/150/200/300 %, mais que ses fonctions présentent des défauts dès 150 %, croissants jusqu'à 300 %. Onze captures fournies et conservées en deux envois : quatre du **dialogue compact**, quatre de **UI test editor**, trois de la fonction installée **Image to PDF**.

| Preuve | Relevé / résultat visuel |
|---|---|
| [Compact 100 %](ui-baseline-b/compact-100.png) | Statut 96 DPI, client 680 × 540, WorkingArea 2560 × 1392. Titre, champs visibles et footer lisibles. |
| [Compact 150 %](ui-baseline-b/compact-150.png) | Statut 144 DPI, client 1020 × 810, WorkingArea 2560 × 1368. Même constat de lisibilité. |
| [Compact 200 %](ui-baseline-b/compact-200.png) | Statut 192 DPI, client 1360 × 1080, WorkingArea 2560 × 1344. Temps complet et deux commandes inférieures visibles. |
| [Compact 300 %](ui-baseline-b/compact-300.png) | Statut 288 DPI, client 2040 × 1193, WorkingArea 2560 × 1296. Hauteur cliente réduite par rapport à 540 × 3; corps avec barre de défilement et footer entièrement visible. Le sous-titre utilise l'ellipsis prévu. |
| [Éditeur 100 %](ui-baseline-b/editor-100.png) | Statut 96 DPI, client 1000 × 660, WorkingArea 2560 × 1392. Bandeau, aperçu, champs visibles et footer lisibles. Le haut du rail (titre et Start time) est hors champ dans cette capture défilée; son accès en remontant a ensuite été confirmé par la validation utilisateur de la recette complète. |
| [Éditeur 150 %](ui-baseline-b/editor-150.png) | Statut 144 DPI, client 1500 × 990, WorkingArea 2560 × 1368. Bandeau, titre de section, temps complet, champs et footer lisibles; rail muni d'une barre de défilement. |
| [Éditeur 200 %](ui-baseline-b/editor-200.png) | Statut 192 DPI, client 2000 × 1273, WorkingArea 2560 × 1344. Hauteur bornée à l'espace disponible; aperçu et rail distincts, commandes de fermeture et de test du titre entièrement visibles. |
| [Éditeur 300 %](ui-baseline-b/editor-300.png) | Statut 288 DPI, client 2524 × 1193, WorkingArea 2560 × 1296. Largeur et hauteur bornées; bandeau, temps complet et footer lisibles. Moins d'options simultanément visibles, dans un rail défilant. Le rail reste à droite : cette capture ne teste pas son passage sous l'aperçu. |
| [Image to PDF 150 %](ui-baseline-b/image-to-pdf-150.png) | Sous-titre et titre Preview coupés; état Active image partiellement masqué par Add image; libellés Ratio/Snap/Rulers/Inches tronqués. Défauts visuels confirmés dès 150 % sur cette fonction. |
| [Image to PDF 200 %](ui-baseline-b/image-to-pdf-200.png) | Coupures accentuées du bandeau, des cases et des labels; chevauchements texte/icône dans les commandes. Symptômes cohérents avec les captures A à 200 %. |
| [Image to PDF 300 %](ui-baseline-b/image-to-pdf-300.png) | Titre principal fortement coupé, libellés de page abrégés par manque d'espace, commandes et cases sévèrement tronquées/chevauchées. |

Les quatre captures compactes confirment **la lisibilité de l'état montré aux quatre DPI réels**, y compris le maintien du footer quand la fenêtre est bornée en hauteur. Elles ne prouvent pas les clics, l'accès à toute la zone défilante, l'ajout dynamique, le changement de titre, le clavier ni les transitions multi-écran. **L'utilisateur confirme que les trois captures Image to PDF proviennent de son application installée habituelle.** B n'a produit aucune installation : ces images montrent donc les défauts de la version installée, et ne constituent pas une régression du build B. La version et le hash installés n'ont pas été relevés. Image to PDF conserve son ancien layout et sa migration est prévue en E. L'affirmation « toutes les fonctions » est un signalement utilisateur; les preuves visuelles reçues ici concernent Image to PDF seulement. Les symptômes à 200 % étaient déjà reproduits en A.

**Validation manuelle finale par l'utilisateur.** Après réception d'une procédure complète sans captures supplémentaires, l'utilisateur confirme : **« c'est ok. je valide tous les tests »**. Cette confirmation couvre les deux démonstrations à 100/150/200/300 %, défilement haut/bas et accès à Start time, ajout de trois champs et saisie, titre et commande longs avec retours répétés à l'état initial, rail sous l'aperçu puis à droite après redimensionnements répétés, maximisation/restauration, navigation Tab/Maj+Tab/flèches/Entrée/Échap, allers-retours entre écrans à échelles différentes et taille du texte Windows augmentée. Preuve : déclaration utilisateur sur la procédure fournie, complémentaire aux huit captures; aucune observation automatisée de ces gestes ni mesure supplémentaire de l'environnement n'est revendiquée. Les valeurs exactes des réglages texte et des écrans lors de cette dernière recette ne sont pas relevées séparément.

**Limites conservées pour C et les lots suivants.** La recette B valide les primitives et les deux structures de démonstration. La configuration Debug affecte le processus entier : la validation utilisateur de ces démonstrations ne vaut pas exécution des 12 tests UI exclus ni qualification des fenêtres métier historiques. Vérifier leurs consommateurs lors des pilotes et migrations, avec reprise des défauts ouverts de A. Release garde sa configuration DPI antérieure; aucune qualification de l'installateur ou d'Explorer n'a été effectuée. Aucun support DPI global de la version installée n'est annoncé.

**Critères de sortie de B : atteints sur le périmètre du socle; B validée le 28 septembre 2026.** Builds et tests automatisés verts, huit captures des deux démonstrations aux quatre échelles, puis validation explicite de toute la procédure manuelle par l'utilisateur. Le socle est prêt pour les pilotes C avec les limites tracées ci-dessus. Cette clôture ne lance pas C : aucune migration C commencée, prochain lancement à la demande de l'utilisateur. Le jalon GO/NO-GO après C reste obligatoire avant D/E. Les défauts métier visuels de A restent ouverts jusqu'aux migrations prévues. Cette clôture est uniquement documentaire; aucun nouveau build n'est nécessaire.

### C — Cinq fenêtres pilotes — P1

- **Objectif :** valider les fondations sur les formes d'UI réellement utilisées et corriger leurs limites avant toute généralisation.
- **Périmètre principal :** **Interpolate Video**, **Compress Image**, **Cut Video**, **Create Subtitles**, **Crop Image**. Ils couvrent dialogue simple, champs conditionnels, sélection temporelle, reconfiguration SRT/ASS et éditeur à panneau latéral. Corriger l'aperçu bloquant de Cut Video dans un changement distinct du layout.
- **Dépendances :** B validée. Toute évolution du socle pendant C entraîne la revalidation des pilotes et consommateurs concernés.
- **Critère de sortie :** cinq pilotes utilisables sans offsets correctifs arbitraires, textes/champs lisibles, commandes accessibles en espace réduit, changements d'options stables, dessin et valeurs média cohérents. Produire le bilan nécessaire au GO/NO-GO ci-dessous; ne pas commencer D/E automatiquement.
- **Validation nécessaire :** build, tests de layout/comportement et essais réels à 100/150/200/300 %; scénarios SRT → ASS → SRT, formats/cible de compression, déplacement multi-écran, clavier, chargement lent et fermeture pendant aperçu. Tracer toute configuration non exécutée comme non validée.

#### Exécution C — 28 septembre 2026

**Autorisation et référence.** L'utilisateur a demandé « ok go pour phase C et les 5 pilotes ». Base : commit A/B `c91a91f`, version `1.19.1`. Les changements C sont dans le dépôt de développement, non publiés. Aucun contrôle du bureau, capture automatique ou changement des réglages Windows n'a été utilisé.

**Implémentation des cinq pilotes :**

| Pilote | Changements réalisés | Preuves automatisées / recette restante |
|---|---|---|
| Interpolate Video **FFmpeg** | Policy DPI et dialogue compact commun ; FPS et presets natifs, footer hors scroll. Le picker RIFE reste hors C. | Saisie décimale et settings préservés ; layout/redimensionnement/texte agrandi testés. DPI Windows réels à confirmer. |
| Compress Image | Profils radio natifs avec descriptions séparées, combo de format, cible et unité natives ; suppression des positions/footers recalculés localement. | PNG désactive la cible ; JPG/WEBP conservent saisie/unité ; `1,5 MB` devient 1572864 octets. Settings et layout testés ; recette réelle à confirmer. |
| Cut Video | Shell éditeur, bornes et temps dans le rail défilant, repli sous l'aperçu en largeur réduite ; barre et conversion pointeur/frame utilisent les mêmes métriques DPI. Aperçu asynchrone isolé dans `CutVideoForm.Preview.cs`. | Bornes 16/60 à 15 FPS pour 1–4 s, aller-retour frame/position et reflow testés. Demandes sérialisées, annulation, résultat périmé libéré, fermeture non bloquante et reprise après erreur testées. Décodage FFmpeg réel et nettoyage des PNG temporaires vérifiés. Gestes/DPI réels à confirmer. |
| Create Subtitles | Trois groupes radio dans des sections mesurées ; les presets ASS apparaissent dans le corps défilant, sans changer la taille de fenêtre. | Cycles SRT/ASS/SRT, preset conservé, fenêtre stable et settings inchangés testés. Clavier/DPI réels à confirmer. |
| Crop Image | Shell éditeur commun ; options mesurées et défilantes ; dessin et zones de prise des poignées adaptés au DPI. Chargement image asynchrone, validation désactivée jusqu'au chargement réussi, erreur inline. | PNG au chemin accentué chargé ; crop en pixels source conservé après resize/reflow/Fit ; échec et Dispose pendant chargement testés. Gestes, zoom et DPI réels à confirmer. |

**Composants réellement partagés et exceptions.** Les cinq utilisent `FrameShiftWindowPolicy`, le header mesuré, les sections et les actions mesurées. Trois composent `FrameShiftDialogLayout`, deux le shell éditeur (dont Crop via `FrameShiftCropEditorUi`). Deux helpers simples ont été ajoutés à la factory : pile verticale AutoSize et description native avec retour à la ligne. Pas de classe `FrameShiftForm` : la composition suffit aux cinq pilotes, sans couche supplémentaire. Les exceptions locales restent le dessin de la plage Cut, la géométrie/interaction Crop et leurs durées de vie d'aperçu ; les unités média ne sont jamais multipliées par le DPI. Le rail Cut utilise la largeur logique partagée des rails larges. Les anciens chemins de layout des autres consommateurs sont conservés jusqu'à D/E/F.

**Changement distinct de l'aperçu Cut.** Le constructeur ne décode plus. Le premier aperçu part après `Shown`, les suivantes après debounce. Une nouvelle demande annule l'ancienne et attend sa libération ; les bitmaps périmés sont détruits. Une fermeture annule et attend de façon asynchrone avant de fermer. Les erreurs restent lisibles dans l'état de l'aperçu. Le runner et les commandes d'export métier restent inchangés.

**Vérifications par commandes.** Builds applicatif Debug et `FrameShift.UiSamples` sans restauration ; tests de `UiPilotTests` et revalidation du socle B ; contrôle `--check` des deux démonstrations et du lanceur avec handles natifs invisibles. Journaux : `scratch/phase-c/phase-c.trx`, `phase-c-ui.trx`, `phase-c-decoder.trx`, `samples-check.json`. Le contrôle natif relève `PerMonitorV2`, **96 DPI**, `Visible=false`. Les grossissements de police ×1/1,5/2/3 sont des tests de mesure, pas des essais Windows à ces échelles.

Un passage de la suite a échoué sur `DynamicField_HasAccessibleName_AndGrowsForText` (test B), sans échec des pilotes. Rejeu isolé puis des 30 cas UI : succès. Le test a été stabilisé en comparant deux polices explicites (9 → 20 pt), au lieu de prendre la police Windows par défaut comme référence ; des valeurs diagnostiques accompagnent désormais l'assertion. Aucune correction du composant de champs n'a été nécessaire. Le résultat du passage final est consigné ci-dessous.

**Résultat final automatisé :** **481 réussis, 5 ignorés, 0 échec, 486 sélectionnés**, dont **16 cas pilotes C** et les 14 cas du socle B. Après le dernier ajustement désactivant OK pendant le chargement Crop, les **2 tests Crop concernés** ont été rejoués avec succès (`phase-c-crop-final.trx`), puis le build du lanceur et son contrôle natif : succès. Builds : **0 avertissement, 0 erreur**. SHA-256 de `src/FrameShift/bin/Debug/net8.0-windows/FrameShift.dll` pour la recette : `35885CCA81D711B33DC75C5ACFE73C3D496B32CF387C436DC1E8F155B4EF8CDC`. `git diff --check` ne rapporte pas d'erreur d'espacement.

**Périmètre non qualifié.** Les 12 tests existants qui affichent des fenêtres restent exclus avec le filtre documenté dans `UI_FOUNDATION.md`, distincts des 5 tests média ignorés. Pas de nouvelle qualification complète des exports, modèles IA ou installateur. Release garde sa configuration DPI antérieure. Les défauts de l'application installée et d'Image to PDF restent hors de cette migration.

**Recette restante et décision.** Un double-clic sur `TEST_PHASE_C.cmd` ouvre le lanceur des cinq vrais formulaires du build de développement. Il affiche les réglages choisis sans produire de sorties média et propose un délai annulable de 3 s pour Cut. La [procédure complète sans captures](UI_PHASE_C_MANUAL_TESTS.md) couvre les cinq pilotes à 100/150/200/300 %, options, clavier, redimensionnement, changement d'écran, texte Windows agrandi et fermeture pendant chargement. La validation utilisateur des démonstrations B n'est pas réutilisée comme preuve des pilotes C.

**Critères de sortie de C : pas encore atteints**, recette réelle des pilotes et validation utilisateur manquantes. **GO D/E non proposé à ce stade** pour cette raison. Aucune phase D/E commencée. Les tests automatiques étayent la conservation des valeurs et la structure, mais ne démontrent pas à eux seuls la disparition des P1 à toutes les échelles. Sur Crop, un décodage natif d'image déjà démarré se termine hors du fil UI avant libération ; Auto crop garde son analyse existante, qui n'a pas fait l'objet d'une qualification de performance sur très grandes images.

#### Révision de design C après retour utilisateur à 100 %

L'utilisateur a jugé la première disposition de C insuffisante à 100 % et a autorisé une amélioration esthétique et ergonomique large des pilotes, en conservant le socle. **Le design à 100 % doit être validé avant les prochains essais DPI.** Les résultats de la première implémentation ci-dessus sont historiques ; ils ne valident pas cette révision.

- **Standard proposé :** boutons mesurés légèrement arrondis, espacements explicites, focus et activation clavier natifs ; cartes radio descriptives de hauteur commune ; champs compacts pour les valeurs numériques ; sélecteur d'unité près de la valeur. Rendu natif en contraste élevé. Les composants de C et les démonstrations B sont revalidés après ces changements partagés ; les API historiques des fenêtres hors pilotes ne sont pas remplacées.
- **Interpolate Video :** source FPS rappelée dans une seule section de choix ; presets ×2/×3/×4 compacts et espacés ; champ FPS de largeur logique 140, sans étirement ; taille initiale mesurée.
- **Compress Image :** trois qualités en cartes sur une ligne à la largeur initiale ; retour à la ligne en largeur réduite. Format, cible et unité regroupés dans Output. Taille initiale calculée pour le contenu complet.
- **Cut Video :** champs de frames/temps et barre de sélection remis sous l'aperçu. Début et fin se placent côte à côte, puis sur deux rangées en largeur réduite. Le viewport temporel ne défile que si la hauteur disponible est insuffisante ; preview et footer restent indépendants. Les fonctions d'aperçu et les calculs média ne sont pas modifiés par cette révision.
- **Create Subtitles :** modèles, formats et styles ASS en cartes ; hauteur initiale mesurée avec la variante ASS complète, bornée à l'écran. L'espace est réservé même en SRT afin de garder la fenêtre stable lors des changements de format.
- **Crop Image :** disposition conservée ; reçoit les boutons communs harmonisés.

**Défauts du socle reproduits et corrigés pendant cette révision :** les mesures non contraintes d'une rangée de choix dans les tables pouvaient annoncer une hauteur de colonne malgré un affichage horizontal, créant une zone vide défilante. `FrameShiftFlowRow` mesure maintenant selon la largeur disponible. Le calcul de hauteur initiale tient compte des dimensions réelles minimales des contrôles natifs, et non du seul texte. Les messages courts n'affichent plus systématiquement une scrollbar. Lors de ce dernier changement, les tests ont révélé une recréation de handle pendant `WM_WINDOWPOSCHANGED` : la mise à jour de scrollbar est désormais différée et protégée pendant la destruction/recréation. Un test dédié couvre répétitions, textes courts/longs et destruction avec mise à jour en attente. Le harness STA échoue désormais sur les exceptions WinForms plutôt que d'ouvrir un dialogue d'erreur.

**Preuves et limites :** nouveaux tests de taille initiale sans scroll des trois dialogues, largeur FPS stable à l'agrandissement, trois qualités alignées et espacées, contenu complet ASS, taille bornée en espace réduit, contrôles Cut sous l'aperçu. Les contrôles sont construits avec handles invisibles ; aucun pilotage du bureau ni réglage Windows automatique. Les journaux de cette révision sont `scratch/phase-c/phase-c-design-ui.trx`, `phase-c-design.trx` et `samples-check-design.json`. La procédure manuelle a été réordonnée pour commencer uniquement à 100 %. Le retour utilisateur sur ce nouveau design puis les essais réels DPI restent requis pour clôturer C. D/E ne sont pas commencées.

**Résultat automatisé final de cette révision — 29 septembre 2026 :** **486 réussis, 5 ignorés, 0 échec, 491 sélectionnés**, dont 20 cas pilotes et 15 cas du socle. Les 12 tests affichant des fenêtres restent exclus, distincts des 5 tests média ignorés. Builds application et lanceur : **0 avertissement, 0 erreur**. Contrôle natif du lanceur et des deux démonstrations : succès, `PerMonitorV2`, `96 DPI`, `Visible=false`. SHA-256 de `src/FrameShift/bin/Debug/net8.0-windows/FrameShift.dll` : `BB5D5E5BF5565EDE550405C56CFBB0E0BF170337E0C797A14F00E2D1BAEBA84A`. Ces résultats ne constituent pas une validation visuelle ni une qualification DPI des pilotes.

#### Ajustement Subtitles et contrat commun — 29 septembre 2026

L'utilisateur accepte les rendus globaux à 100 %, mais demande le retour de Subtitles à une présentation classique et des dimensions identiques pour les boutons de validation/fermeture. **Nouvelle validation de ces ajustements attendue avant les essais DPI.**

- Subtitles : listes verticales radio natives, descriptions indentées et couleurs hiérarchisées, sans cartes. Même logique de modèles/formats/styles ; exclusivité et cycles SRT/ASS conservés. Taille initiale mesurée pour la variante ASS complète, défilement si l'espace manque.
- Footer : référence commune `FooterButtonWidth = 140`, hauteur logique 34 ; rôles principal et secondaire de même taille. La paire grandit ensemble si le texte le nécessite, avec retour à la ligne en espace réduit. Les anciennes largeurs 140/120 restent uniquement dans les API de compatibilité des menus non migrés.
- La barre d'actions mesure la taille commune avant que le parent alloue la hauteur : cela corrige le cas d'un libellé modifié après construction qui faisait passer la paire sur deux rangées sans toujours réserver la hauteur correspondante.
- Bandeau, marges et écarts : déjà communs aux cinq pilotes, conservés. Les valeurs équivalentes encore littérales du nouveau socle ont été reliées aux métriques. Le tableau figé de `UI_FOUNDATION.md` est la référence des futures migrations, sans variantes locales arbitraires.
- Vérification renforcée : dimensions égales après redimensionnement/agrandissement du texte, radios exclusifs, taille initiale sans défilement ni hauteur vide excessive. Le scénario de départ avec une hauteur provisoire courte a révélé une boucle géométrique : la scrollbar rétrécissait les choix, leur retour à la ligne maintenait la scrollbar. Le calcul initial restaure la pleine largeur sans scrollbar, puis laisse les mesures se stabiliser sur au plus trois passes avant de réactiver le défilement. Cette convergence n'est exécutée qu'à l'ouverture.

Le périmètre reste C : les fenêtres historiques et l'application installée ne sont pas déclarées uniformisées ; leur migration reste en D/E. Aucun test DPI réel ni prise de contrôle du bureau dans cette révision.

**Binaire de recette de cet ajustement :** SHA-256 `EE914D6859050B804BF4868A2E7EB29894EDFB51EACCAB8ED069EBA08DCA9C0E` pour `FrameShift.dll`, identique dans la sortie applicative Debug et le lanceur. Build du lanceur incluant l'application : 0 avertissement, 0 erreur. Contrôle natif `samples-check-standard.json` : succès, `PerMonitorV2`, 96 DPI, fenêtres invisibles. Les résultats des passages intermédiaires ne valent pas validation du build final ; le résultat final est consigné ci-dessous.

**Validation automatisée finale : 35 tests UI réussis, 0 échec, 0 ignoré** (`scratch/phase-c/phase-c-standard-final.trx`), incluant les cinq pilotes et le socle partagé. Les 8 cas ciblant les mesures et les choix Subtitles ont également passé leur rejeu ciblé. `git diff --check` : succès. La suite métier complète n'a pas été rejouée pour cet ajustement exclusivement UI ; son dernier résultat demeure celui de la révision précédente (486 réussis, 5 ignorés, 12 tests affichant des fenêtres exclus). C reste ouverte en attente de l'accord utilisateur sur ce rendu, puis des essais réels de mise à l'échelle.

#### Suppression du vide réservé dans Subtitles — 29 septembre 2026

**Retour utilisateur : Interpolate Video, Compress Image, Cut Video et Crop Image validés à 100 %.** La capture Subtitles confirme un espace vide important sous Output format en SRT, correspondant à la place réservée aux styles ASS masqués. Ce choix de conception est abandonné ; les autres pilotes ne sont pas modifiés.

Subtitles mesure désormais uniquement le contenu visible à l'ouverture. Au changement explicite de format, sa hauteur s'ajuste : ASS ajoute ses styles ; SRT et Project récupèrent la hauteur compacte. Largeur conservée, taille et position bornées à la zone de travail ; défilement si nécessaire. Aucun ajustement depuis Resize/Layout ni changement d'état d'une fenêtre maximisée. Les valeurs choisies sont conservées. Cela remplace la règle précédente de hauteur constante avec réserve ASS.

**Vérification ciblée : 6 tests réussis, 0 échec** (`scratch/phase-c/phase-c-subtitles-fit.trx`) : trois ouvertures SRT/ASS/Project avec cycles répétés et conservation des styles, contrôle du vide résiduel et des limites écran ; trois cas de taille initiale des dialogues compacts et espace réduit. Handles invisibles, sans prise de contrôle. Builds application et lanceur : **0 avertissement, 0 erreur**. SHA-256 du `FrameShift.dll` livré avec le lanceur : `AF83BE01BA6E47F505C908E829E40E93FAF79A1DC5F5011CBC1B6CCE77ED7523`. Pas de rejeu exhaustif pour cet ajustement local ; les 35 tests UI et 486 tests réussis précédemment restent datés de leurs révisions respectives.

**Validation utilisateur du 29 septembre 2026 :** correction Subtitles acceptée (« validé. committe. »). Les cinq rendus sont maintenant validés à 100 %, y compris les boutons communs et la hauteur compacte de Subtitles. Cette validation autorise leur enregistrement dans Git ; elle ne vaut pas validation des essais DPI encore à effectuer. C reste ouverte pour cette recette ; aucun GO D/E.

#### Validation des mises à l'échelle C — 29 septembre 2026

Après le commit `a3128f0`, l'utilisateur confirme : **« toutes les mises a l'échelles testées et validées »**. Dans le périmètre de la recette transmise, cela valide les cinq pilotes aux paliers **100/150/200/300 %**. Validation manuelle déclarative, sans nouvelle capture demandée ; aucun défaut signalé. Le design à 100 % avait déjà été validé séparément.

Cette confirmation ne certifie pas les échelles intermédiaires de la matrice finale, la résolution 4K, l'application installée ni les fenêtres hors C. Elle ne confirme pas explicitement les scénarios distincts de déplacement multi-écran, taille du texte Windows, clavier, changements d'options, gestes Cut/Crop et fermeture pendant chargement. Leur couverture est en cours de clarification avant de conclure le jalon C ; les preuves automatisées déjà consignées restent acquises, sans être présentées comme essais manuels.

**Bilan provisoire :** socle commun utilisé par les cinq pilotes, composants et exceptions documentés ; composition WinForms suffisante, aucune base `FrameShiftForm` nécessaire à ce stade. Design et rendu aux quatre paliers validés par l'utilisateur ; aucun nouveau P1 rapporté. Le GO D/E reste distinct et n'est pas donné automatiquement. Mise à jour documentaire seulement : aucun rebuild requis ni nouvelle exécution de tests revendiquée.

#### Bilan final C et validation de la recette — 29 septembre 2026

Après réception des manipulations restantes, l'utilisateur répond **« ok tout validé »**. Cette confirmation couvre la recette transmise : clavier (Tab/Maj+Tab, flèches, Espace, Entrée/Échap), changements d'options, gestes et valeurs Cut/Crop, fermeture pendant chargement, déplacement entre écrans et augmentation de la taille du texte Windows. Elle complète la validation explicite antérieure des cinq pilotes à 100/150/200/300 %. Preuve déclarative utilisateur ; aucun relevé automatique ni capture supplémentaire. Les valeurs exactes du réglage de texte et la chronologie de chaque essai ne sont pas mesurées par l'agent.

| Pilote | Résultat final dans le périmètre C |
|---|---|
| Interpolate Video FFmpeg | Design, échelles, clavier, presets et valeur personnalisée validés. |
| Compress Image | Design, échelles, profils et cycles formats/cible/unité validés. |
| Cut Video | Design, échelles, bornes/champs/poignées, aperçu et fermeture pendant chargement validés. |
| Create Subtitles | Design compact, échelles, groupes radio, cycles SRT/ASS/Project et conservation du style validés. |
| Crop Image | Design, échelles, poignées, ratios, zoom/Fit, conservation des dimensions source et fermeture pendant chargement validés. |

**Socle retenu :** politique DPI, bandeau mesuré, sections/marges, boutons de footer identiques, mesure du contenu et stratégie de défilement communs. Cut garde les commandes sous l'aperçu ; Crop utilise le rail adaptable ; Subtitles ajuste explicitement la hauteur au format. Ces exceptions répondent aux workflows et utilisent les composants communs. Aucune base `FrameShiftForm` ni framework supplémentaire nécessaire. Aperçus et coordonnées média restent séparés du layout.

**Preuves techniques :** commit de référence `a3128f0`, version de développement 1.19.1, dernier binaire de recette `AF83BE01BA6E47F505C908E829E40E93FAF79A1DC5F5011CBC1B6CCE77ED7523`. Builds sans avertissement/erreur ; suites et rejeux tracés dans les révisions précédentes (35 tests UI, puis 6 cas ciblés après la dernière correction locale ; 486 tests réussis et 5 ignorés lors du dernier passage large). Aucun nouveau test exécuté pour cette clôture documentaire. Aucun P1 restant signalé sur les pilotes ; les changements partagés ont été vérifiés sur leurs consommateurs B/C.

**Limites conservées :** pas de qualification de toutes les fenêtres historiques, de la distribution installée, de l'installateur, des exports complets ou modèles IA ; 12 tests affichant des fenêtres exclus des passages automatisés. Échelles intermédiaires et 4K non certifiées par cette recette. Release conserve sa configuration DPI antérieure. Ces limites relèvent des migrations et de la qualification ultérieures, et ne sont pas transformées en résultats positifs.

**Critères techniques de sortie de C : atteints sur le périmètre testé ; recette C validée. GO proposé pour D1**, en commençant par Progress, puis Main (ActionsPanel/FileQueuePanel), Settings et MediaInfo. Le lancement de D attend l'accord explicite du responsable conformément au jalon ; **D/E non commencées**, aucune publication autorisée par cette validation.

### Jalon après C — GO / NO-GO obligatoire

Ce jalon fait partie de la sortie de C; il n'ajoute pas de huitième phase.

Le bilan présente : composants réellement partagés, exceptions locales restantes, résultat de chaque pilote, régressions éventuelles chez les autres consommateurs, simplicité du socle, configurations testées/non testées et défauts ouverts. Il conclut aussi sur l'utilité éventuelle d'une base `FrameShiftForm` et sur la capacité à poursuivre sans multiplier les abstractions.

- **GO proposé :** les P1 des pilotes sont résolus sur les configurations testées, les primitives supportent le contenu dynamique et l'espace réduit, les vérifications requises disposent de preuves et aucune limite connue ne remet en cause la généralisation. Les exceptions sont petites et motivées.
- **NO-GO :** défaut structurel restant, double scaling, commandes inaccessibles, régression bloquante, validation essentielle manquante ou architecture qui nécessite des corrections locales répétées. Revenir sur B/C et revalider; ne pas contourner le problème par une migration massive.
- **Décision :** présenter ce bilan au responsable du projet et obtenir sa validation explicite avant D/E. Consigner date, commit, preuves et décision. La validation actuelle de la feuille de route ne constitue pas ce GO.

Un GO autorise la migration, **pas la publication de `PerMonitorV2` ni une annonce de support DPI généralisé**. Si une nouvelle faille du socle est découverte ensuite, suspendre la généralisation concernée, corriger et revalider les consommateurs affectés.

### D — Surfaces communes et dialogues — P1

- **Objectif :** généraliser le socle validé par lots courts, en traitant d'abord les surfaces les plus utilisées. **ProgressForm est la première priorité de D.**
- **Périmètre principal :** 24 fenêtres, en trois lots successifs : D1 = Progress, Main avec ActionsPanel/FileQueuePanel, Settings, MediaInfo; D2 = Conversion, CompressMultiFileChoice, Compress Audio/Video, Resize Image/Video via leur base, Change Pitch/Speed, Rotate/Flip Image/Video, Convert to Icon, picker Add Subtitles; D3 = Remove Noise Audio/Video, Separate Audio, RIFE, Upscale Image/Video, Download Model, BRIA. Conserver la structure fonctionnelle spécifique de Main et Progress.
- **Dépendances :** GO explicite après C. Valider chaque lot avant le suivant; toute modification commune déclenche une vérification de ses consommateurs affectés.
- **Critère de sortie :** les 24 fenêtres et leurs variantes utilisent la politique commune, restent lisibles et accessibles en espace réduit, et leurs changements d'état ne réinjectent plus des dimensions brutes. File/annulation de Progress et comportements métier préservés; aucun P1 ouvert sur le périmètre migré.
- **Validation nécessaire :** build et tests ciblés par lot; recette à 100/150/200/300 %, petits/grands menus, modèles et formats, erreurs longues, contrôles dynamiques, annulation et états de progression. Appliquer les invariants runtime de 8.1; conserver des vérifications des cinq pilotes pour les modifications communes.

#### Exécution D1 — 29 septembre 2026

**Autorisation :** « Go phase D en suivant les standards validés », après validation complète de C et commit `5b62534`. Développement local, sans publication ni changement de mode DPI Release.

**Surfaces migrées :**

| Surface | Réalisation D1 |
|---|---|
| Progress, priorité du lot | Bandeau mesuré commun ; statut long copiable/défilant ; barre indéterminée à l'attente puis numérique au premier progrès ; file extensible et métriques de lignes/colonnes sensibles au DPI ; soutien masquable ; Cancel all / Close dans le footer partagé. |
| Main | Bandeau et footer communs ; état vide simple ; file/actions côte à côte puis empilées en largeur réduite ; sélection et périmètre des actions conservés. |
| FileQueuePanel / ActionsPanel | En-tête de file mesuré, noms complets en infobulle, boutons d'actions mesurés, filtres natifs accessibles au clavier, recherche et arité conservées ; anciens contrôles reconstruits désormais libérés. |
| Settings | Sections modèles/apparence adaptatives, hauteur ajustée au contenu à l'ouverture, défilement de secours, Close standard. Les handlers de préférences restent les mêmes. |
| Media Info | Bandeau commun, surface texte extensible et défilement natif dans les deux axes, Copy / Close de taille identique, fonte monospacée libérée avec la fenêtre. |

**Socle :** mêmes métriques figées en C ; extension de `CreateActions` au footer à une commande et de `CreateSection` aux surfaces remplissant l'espace. Les usages B/C existants gardent leur disposition. `FrameShiftGridUi` centralise seulement les métriques des deux files, sans nouvelle hiérarchie de formulaires. Main conserve un séparateur local réorientable et Progress une taille centrale minimale avec défilement de secours : ces adaptations servent leur contenu spécifique.

**Vérifications exécutées :**

- Build application et build UiSamples : réussis, zéro erreur/avertissement.
- 7 nouveaux cas `UiD1Tests` : handles cachés, redimensionnement/texte agrandi, footer accessible, états Progress, annulation idempotente, périmètre de sélection de Main, filtres répétés et ajustement de Settings.
- 7 tests existants file/retrait : réussis ; contrats des occurrences, retraits et annulation conservés.
- Suite finale séquentielle avec filtre sans affichage : **495 réussis, 5 skips média/IA, 0 échec**. Les 12 cas affichant des fenêtres sont exclus séparément. Cette suite inclut les **37 cas du socle et des cinq pilotes C**, ainsi que les 7 nouveaux cas D1. Commande dans [la recette D1](UI_PHASE_D1_MANUAL_TESTS.md).
- Contrôle `UiSamples --check` : sortie 0, PMv2, DPI natif observé 96, quatre fenêtres de démonstration/lanceurs demeurées invisibles. Aucun palier Windows n'a été modifié ou simulé par cette commande.
- Traces locales : `scratch/phase-d1/d1-final.trx`, `d1-queues.trx`, `d1-layout.trx`, `hidden-pmv2.json`.

**Incidents de validation consignés :** le premier passage parallèle a échoué sur deux assertions de layout B/C ; les sept cas concernés repassent isolément sans changement de code produit, puis les 37 cas B/C passent dans la suite séquentielle. L'interférence exacte du passage parallèle reste à isoler ; les résultats ne prouvent pas une validation DPI réelle. Le premier passage séquentiel a révélé une course préexistante du test RawVideo : lecture du marqueur PID pendant son écriture exclusive. Le helper de test attend désormais un PID lisible/valide ; le processus décodeur du test concerné est libéré même si la préparation échoue. Les deux processus factices laissés par l'échec ont été identifiés puis arrêtés. Aucun changement dans les runners ou le Core.

**Validation manuelle actualisée :** interfaces D1 validées à 100 % par l'utilisateur après les derniers ajustements de Progress. Restent à vérifier : 150/200/300 %, focus/clavier, clair/sombre, transitions et défilements, déplacement multi-écran et texte Windows agrandi. Le lanceur `TEST_PHASE_D1.cmd` ouvre les vrais formulaires avec états simulés pour Progress et intercepte les actions de Main. Settings reste réel ; sa recette le précise. Les états simulés ne certifient pas de nouveaux exports ou téléchargements.

**Décision : interfaces D1 validées à 100 %, qualification DPI encore en attente. Critères de sortie de D non atteints.** Conformément au découpage validé, D2 puis D3 restent à réaliser après validation du lot précédent. Aucun lancement de E, aucune installation, capture ou prise de contrôle du bureau.

#### Retour D1 et refonte de Progress — 29 septembre 2026

- L'utilisateur confirme « tout ok sauf erreur longue » et fournit une capture de Progress : la zone Current task ne montre qu'une ligne et une partie de la suivante, avec un défilement peu utilisable. Ce défaut est **reproduit visuellement**, malgré les premiers tests qui vérifiaient surtout le footer et la conservation du texte. Les autres scénarios sont acceptés par l'utilisateur ; ce retour ne précise pas de nouveaux paliers DPI.
- Refonte locale de Progress demandée et réalisée : résumé d'état court en haut ; file simplifiée File/Status et panneau Details extensible côte à côte ; empilement en largeur réduite. Le message intégral est dans un TextBox natif multiligne Dock Fill, pas dans le statut court empilé de la première version. Zone de lecture minimale calculée pour huit lignes ; Copy details ; retour Current task ; lecture par occurrence de file sans remplacement par les mises à jour d'un autre fichier ; ouverture des nouveaux messages au début du texte.
- Le chemin complet accompagne le message dans Details et sa copie. Les valeurs internes de la file, retraits et contrats d'annulation restent conservés. Le lanceur simule aussi la fin de l'annulation individuelle lorsque son timer observe la demande.
- Vérification complémentaire : **15 tests ciblés réussis** (8 D1 et 7 file/retraits), dont un test régressif qui exige au moins 200 pixels de hauteur de lecture à l'ouverture, au moins huit lignes après réduction/agrandissement de police, le message complet, le retour au début et la conservation de la sélection pendant les mises à jour. Builds application/UiSamples réussis, zéro erreur/avertissement. Trace : `scratch/phase-d1/d1-progress-redesign.trx`.
- Aucun helper partagé ni pilote C modifié pour cette correction. Le passage de 495 tests ci-dessus décrit la première version D1 ; la nouvelle vérification est ciblée sur la refonte. **Nouvelle présentation Progress à revoir manuellement**, puis paliers DPI à confirmer explicitement. D2/D3 restent en attente.

#### Second retour Progress — disposition verticale et présentation

L'utilisateur refuse les détails à droite et demande un panneau en haut ou en bas, plus haut, ainsi qu'une refonte esthétique dans le thème. La présentation précédente reste un essai rejeté ; elle n'est pas validée.

- Disposition retenue : Activity, Files, puis Messages & details **sous la file et sur toute sa largeur**, à toutes les largeurs de fenêtre.
- Lecture : minimum de douze lignes selon la police et 220 unités logiques ; contexte et commandes regroupés au-dessus du texte ; hauteur initiale portée à 920 unités logiques, bornée par la zone de travail commune. Défilement de secours conservé sur petit écran. Le test contrôle aussi l'absence de défilement extérieur à la taille initiale dans son environnement.
- Présentation locale : bandeau Activity bleu doux, état en gras, pourcentage plus visible, ETA masquée lorsqu'elle est vide ; compteur de fichiers, libellés Waiting/Processing/Failed/etc. présentés sans changer les états stockés ; lignes alternées discrètes et séparateurs utilisant les métriques communes. Les fontes locales sont libérées et recalculées lors des changements de police.
- Bandeau supérieur, boutons, arrondis, palette et écarts restent ceux du socle validé. Aucun changement des autres fenêtres acceptées.
- Vérification : **15 tests ciblés réussis**, dont position permanente sous la file, même largeur des deux panneaux, hauteur minimale de lecture, texte intégral et sélection conservée. Trace : `scratch/phase-d1/d1-progress-vertical.trx`. Builds application et lanceur réussis, zéro erreur/avertissement.
- **Rendu manuel de cette nouvelle version en attente**, sans annoncer de validation DPI réelle. D2/D3 ne démarrent pas dans cette révision.

#### Harmonisation des couleurs Files — retour suivant

La capture utilisateur montre l'en-tête File en bleu Windows saturé. Le style des en-têtes définissait le fond normal mais laissait les couleurs de sélection natives : la sélection d'une cellule colorait aussi son en-tête de colonne.

Correction locale : fond neutre et texte secondaire explicités pour l'en-tête normal/sélectionné ; lignes sélectionnées en bleu doux ; fonds alternés hérités par la colonne × au lieu d'une case de fond différente ; retrait en attente en accent du thème, rouge réservé à l'annulation active ; couleur des états conservée en sélection. Les valeurs utilisées appartiennent à la palette clair/sombre existante.

Builds application/lanceur réussis sans avertissement ; **15 tests ciblés réussis**, trace `scratch/phase-d1/d1-files-colors.trx`. Aucun test visuel automatisé ni nouvelle validation manuelle annoncée. Rendu harmonisé à revoir par l'utilisateur.

#### Files sobre — suppression des bandes alternées

La capture suivante confirme que l'alternance Surface/PageBackground ajoutée à la sélection AccentSoft produit trois fonds distincts, refusés par l'utilisateur. Cette révision remplace le choix visuel précédent : lignes et en-tête sur Surface uniforme, sélection seule sur PageBackground, noms en TextPrimary, croix de retrait en TextMuted. Les états sémantiques et l'annulation active conservent leurs repères. Aucun changement de disposition ou de comportement.

Build application et lanceur réussi, zéro avertissement/erreur. Pas de nouveau test ni de nouvelle exécution de la suite pour ce changement limité aux couleurs ; les 15 tests du passage précédent restent le dernier contrôle comportemental. Rendu à valider manuellement en relançant le lanceur D1.

#### Détails réduits après validation des couleurs

L'utilisateur valide les couleurs, puis demande de réduire de moitié la zone erreur. La cible du panneau passe de 60 % à 30 % du corps file/détails ; l'espace libéré revient à Files. Un minimum de six lignes / 110 unités logiques de lecture préserve les messages et commandes aux fortes échelles, avec défilement si nécessaire. Le minimum du corps additionne les besoins des deux panneaux, sans imposer leur proportion en espace réduit : aucun défilement extérieur ajouté à l'ouverture par défaut.

Build application/lanceur réussi sans avertissement ni erreur ; **8 tests D1 réussis**, dont lecture multiligne, absence de défilement extérieur par défaut, redimensionnement/texte agrandi et conservation du diagnostic sélectionné. Trace : `scratch/phase-d1/d1-details-height.trx`. Nouvelle hauteur à revoir manuellement.

#### Validation visuelle à 100 % et sauvegarde Git

L'utilisateur confirme : « ok les ui sont validées en 100% commite. demain je testerai avec les mise à l'échelle ». La validation couvre le rendu final des interfaces D1, y compris Files sobre et la zone erreur réduite. Les essais 150/200/300 % sont encore à réaliser par l'utilisateur ; aucune validation DPI supplémentaire ni clôture de D1 n'est déduite de ce retour. D2/D3 et E restent en attente. Commit du lot D1 demandé, sans publication.

### E — Éditeurs restants — P1

- **Objectif :** achever les migrations en garantissant espace de travail adaptable, interaction précise et absence de confusion entre DPI et coordonnées média.
- **Périmètre principal :** sept fenêtres : Cut Audio, Create GIF, Crop Video, Join Videos et sa timeline, Burn Subtitles, Remove Object, Image to PDF; `SeekTrackBar` et helpers d'aperçu concernés. Commencer par Cut Audio/GIF, terminer par Image to PDF sans refonte massive.
- **Dépendances :** D validée, socle et pilotes toujours stables. Conserver les contrats d'annulation et les runners existants.
- **Critère de sortie :** options accessibles par défilement si nécessaire, commandes persistantes, dessin/hit-test alignés après changement DPI, coordonnées source inchangées et aperçus concernés réactifs/annulables. Aucun P1 ouvert sur les fenêtres migrées; **C (5) + D (24) + E (7) couvrent les 36 fenêtres**.
- **Validation nécessaire :** build et tests métier/durée de vie par éditeur; essais réels aux quatre échelles, espace réduit, poignées, waveform, timeline, pinceau, zoom, médias portrait/paysage et rotation. Comparer les valeurs média et résultats produits; vérifier fermeture pendant chargement et invariants runtime de 8.1.

### F — Standardisation durable — P2 après stabilisation

- **Objectif :** résoudre les P2 retenus et rendre la prochaine UI simple à construire avec le socle éprouvé.
- **Périmètre principal :** clavier/focus, contrastes et états problématiques, netteté et propriété des ressources graphiques, duplications utiles à supprimer, anciens chemins de layout sans consommateurs. Réconcilier `UI_STANDARDIZATION.md`, `CODE_FILE_INDEX.md`, les références de version, l'historique `UI_DPI_AUDIT.md` et `RELEASE_CHECKLIST.md`; identifier les exemples compact/éditeur à suivre.
- **Dépendances :** D et E validées, aucun P1 restant. Une régression P1 découverte redevient prioritaire.
- **Critère de sortie :** P2 du périmètre convenu traités, chemins obsolètes retirés sans casser leurs usages, règles documentaires cohérentes avec le code et exceptions/différés explicites. Pas de second framework UI ni d'extraction générique sans besoin démontré.
- **Validation nécessaire :** build et tests ciblés pour chaque lot de code; vérification clair/sombre, focus et navigation, ouvertures/fermetures et reconstructions répétées. Contrôler ressources et consommateurs des helpers modifiés; vérifier la cohérence des documents et liens.

### G — Qualification complète et décision de publication

- **Objectif :** démontrer le fonctionnement de l'ensemble sur la distribution réellement destinée aux utilisateurs et statuer sur la publication de l'activation DPI globale.
- **Périmètre principal :** les 36 fenêtres et leurs variantes, dialogues Windows, lancement hub/Explorer, progression, self-contained `win-x64` et pages personnalisées Inno Setup; matrice finale de la section 8.
- **Dépendances :** D, E et F validées, preuves par phase disponibles, aucune validation critique remplacée par une simple affirmation documentaire.
- **Critère de sortie :** aucun P1 connu sur les configurations supportées, résultats de recette datés pour tout l'inventaire, aucune dérive cumulative DPI ni régression métier/annulation. Consigner les limites restantes et la décision de publication; ne déclarer `PerMonitorV2` validé globalement qu'au vu de cette qualification.
- **Validation nécessaire :** recette finale complète de la section 8, tests Release et chaîne canonique `./build_installer.ps1`, puis vérification du binaire réellement installé, de ses dépendances et des entrées Explorer. Aucun publish/Inno manuel parallèle.

**Règle de reprise :** pour chaque passage, consigner phase, commit/binaire, résultat de build/tests, scénarios exécutés, défauts et limites, puis décision de poursuite. Une validation manquante reste identifiée comme telle. Les corrections de layout, aperçus et comportement restent dans des changements séparés autant que possible. Les règles Core, runners et nommage unique ne sont pas réécrites pour les besoins du chantier UI.

## 8. Plan de validation

Cette section décrit les moyens et la qualification finale, pas une liste à exécuter intégralement dès A. A reste ciblée; B valide les primitives; C les cinq pilotes; D/E chaque lot migré; F ses changements P2; G l'ensemble. Un correctif d'un helper partagé impose de vérifier ses consommateurs affectés.

### 8.1 Build et invariants communs

- Baseline A : relever le résultat du build et des tests existants, sans corriger dans cette phase les défauts applicatifs identifiés.
- Après chaque lot de modification de code, le minimum obligatoire est `dotnet build src/FrameShift/FrameShift.csproj`; un build vert est nécessaire avant de déclarer le lot terminé.
- Projet de tests : `dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj`; cibler les tests pertinents pendant les migrations, puis exécuter la suite complète lors de la qualification. Les assertions de layout ne remplacent pas les essais DPI réels.
- Avant toute release ou test via installateur, Explorer ou installation existante, utiliser uniquement `./build_installer.ps1`. Ce script assure restore verrouillé, tests Release, publication self-contained, contrôle du payload et compilation Inno. Ne pas substituer un publish et une compilation ISS manuels.
- Pour chaque action migrée : chemins avec espaces/accents, annulation, nettoyage après échec, sorties uniques adjacentes au média par défaut, aucun processus FFmpeg orphelin, aucune console visible et logs lisibles. Vérifier les interactions avec les runners sans changer la séparation Core/Windows.
- Une mise à jour purement documentaire, comme celle qui officialise cette feuille de route, ne constitue pas une exécution de A ni une modification de code nécessitant un rebuild.

### 8.2 Matrice d'environnement pour la qualification finale

| Axe | Configurations à couvrir |
|---|---|
| DPI | 100, 125, 150, 175, 200, 250, 300 %. Ajouter le 133 % historique comme scénario de scaling personnalisé si utilisé. |
| Résolution / espace | 1920 × 1080; 2560 × 1440; 3840 × 2160 à 150/200/250/300 %; espace logique réduit proche de 1280 × 720, moins la barre des tâches. |
| Multi-écran | 100 ↔ 200 %, 150 ↔ 300 %; ouvrir sur chaque écran, déplacer aller-retour plusieurs fois, maximiser/restaurer. |
| Texte | Réglage Windows de taille du texte par défaut puis augmenté; texte français avec accents, noms et chemins longs. |
| Thème | Clair, sombre, System; changement de préférence avec fenêtres ouvertes. Contrôle ciblé en contraste élevé pour relever les limites, sans promettre une prise en charge exhaustive différée. |
| Distribution | Build de test puis publication self-contained win-x64 et installateur; même version/réglages dans les résultats. |

En G, exécuter toutes les fenêtres à 100, 150, 200 et 300 % au minimum. Vérifier les composants partagés à chaque palier intermédiaire, puis rejouer toutes les fenêtres affectées si un défaut y apparaît. Ne pas déduire le multi-écran d'un simple `Scale()` de test. Les résultats intermédiaires restent traçables, mais ne dispensent pas de vérifier la distribution finale.

### 8.3 Scénarios fonctionnels à appliquer au périmètre de chaque phase

1. Ouvrir avec données normales, nom très long, chemin contenant espaces/accents; vérifier titre et accès au chemin complet.
2. Réduire à la taille disponible, agrandir, maximiser/restaurer, changer d'écran : footer visible, contenu accessible, aucune dérive cumulative de taille.
3. Changer les options qui modifient l'UI : format/cible de compression, SRT/ASS/preset, modèle/échelle/taille personnalisée, mode sous-titres, état BRIA, fichier unique/multiple, variantes audio/vidéo.
4. Naviguer sans souris : Tab/Shift+Tab, flèches, Space, Enter/Escape selon l'état; vérifier le focus au retour d'un dialogue et d'une erreur.
5. Charger un média lent ou invalide, déclencher plusieurs aperçus, changer rapidement la sélection, fermer pendant chargement/inférence/téléchargement : état clair, réponse UI, nettoyage et absence de callback sur contrôle détruit.
6. Déplacer poignées, pinceau et timeline aux extrémités à chaque DPI; comparer valeurs source avant/après migration. Tester médias portrait/paysage et vidéos avec métadonnées de rotation.
7. Tester progressions indéterminées/déterminées, files longues, erreurs longues, annulation en cours, achèvement, donation affichée/masquée et suppression d'une entrée en attente.
8. Réouvrir/fermer les dialogues et reconstruire la liste d'actions à répétition; surveiller stabilité des handles et ressources.
9. En G, parcourir les pages de l'installateur, descriptions de composants et choix du dossier modèle dans un environnement de recette identifié, puis vérifier le lancement de la version installée.

### 8.4 Automatisation utile, sans faux sentiment de couverture

- Tests STA de construction et layout sur un processus configuré avant la création des handles; réutiliser l'approche `StaTest` existante. Créer de petites données locales pour éviter de rendre les tests dépendants d'un modèle IA.
- Vérifications géométriques ciblées : footer dans la zone cliente, texte utile tenant dans sa ligne, absence de chevauchement entre voisins censés être séparés. Respecter les exceptions légitimes des conteneurs scrollables et dessins superposés.
- Tests de changement d'état après affichage : notamment SRT → ASS → SRT et recalcul du footer.
- Tests de conversion logique/pixels et de géométrie peinture/hit-test; comparaison de coordonnées média indépendante du DPI.
- Captures avec métadonnées et revue visuelle pour confirmer le rendu réel. Les captures seules ne vérifient ni clavier, ni processus orphelins, ni justesse des sorties.
- Contrôle léger en CI/revue : nouveau formulaire utilisant la politique commune; toute nouvelle affectation de coordonnées dans `Resize/Shown` justifiée; pas de `.GetAwaiter().GetResult()` dans un chemin UI d'aperçu. Une recherche textuelle signale un point de revue, pas automatiquement une erreur.

### 8.5 Critères d'acceptation du chantier

- Aucun titre, label essentiel, valeur temporelle ou bouton coupé sur la matrice supportée.
- Aucun chevauchement involontaire; textes longs intégralement consultables.
- Validation/annulation visibles et atteignables même quand le corps défile.
- Aucune réduction/augmentation cumulative après changements DPI répétés.
- Peinture et zones de clic alignées; coordonnées source inchangées.
- Ouverture et aperçu ne bloquant pas la boucle UI; annulation propre et aucune nouvelle régression de fermeture.
- Tests métier existants conservés; pour chaque action migrée : chemins espaces/accents, sorties uniques, nettoyage après échec, absence de console et de processus FFmpeg orphelin, logs lisibles.
- Toutes les fenêtres de l'inventaire disposent d'un résultat de recette daté. Les exceptions connues sont explicites; aucun statut « validé » sans preuve de la configuration testée.

Format conseillé pour les résultats :

```text
Version/commit | Fenêtre/variante | Résolution | DPI | Taille texte | Thème
Écran de lancement/destination | Scénario | Résultat | Capture/log | Défaut restant
```

## 9. Règles pour toute future interface

Avant de créer une nouvelle fenêtre, choisir le shell compact ou le shell éditeur et vérifier si l'action peut réutiliser un picker existant.

Checklist à intégrer au développement et à la revue :

- [ ] La fenêtre reprend le contrat DPI commun; aucun mode divergent dans une base héritée.
- [ ] Bandeau, sections, champs et actions proviennent des composants partagés.
- [ ] Les constantes sont des métriques logiques documentées; aucun calcul ne mélange espaces logiques, pixels écran et coordonnées média.
- [ ] Les textes pilotent la taille des rangées; les champs s'étirent; les instructions et erreurs peuvent revenir à la ligne.
- [ ] Le corps possède une stratégie de manque de place; le footer reste accessible.
- [ ] Les contrôles dynamiques ont été testés après affichage et changement d'options/DPI.
- [ ] Tab, focus, Enter/Escape, noms accessibles et contrôles natifs sont vérifiés.
- [ ] L'aperçu est asynchrone, annulable, avec état explicite et propriété des ressources claire.
- [ ] Clair, sombre, état désactivé, focus et survol restent lisibles.
- [ ] Une exception au shell est courte, motivée par un besoin réel et accompagnée de ses tests.
- [ ] La nouvelle fenêtre et ses variantes figurent dans l'inventaire de recette.

**Exemple de répartition attendue :** une future action décrit ses options, ajoute les champs et relie la validation au Core. Elle ne décide pas à nouveau de la hauteur du bandeau, de la position de Cancel, de l'espacement entre sections ou de la formule DPI. Les personnalisations concernent le média et l'interaction propre à l'action.

## 10. Points volontairement ouverts et décisions attendues

| Point ouvert | Quand le trancher | Limite à respecter |
|---|---|---|
| Utilité d'une base `FrameShiftForm`, forme exacte des helpers supplémentaires | B, puis bilan C | Choisir la solution la plus simple éprouvée par les pilotes; aucune classe nouvelle imposée par son nom. |
| Environnements de recette disponibles, configuration exacte du signalement et espace minimal supporté | Relever en A; consolider au GO/NO-GO et qualifier en G | Ne pas bloquer la baseline sur la recette exhaustive; ne jamais déclarer un scénario non exécuté comme validé. |
| Exceptions locales justifiées et dimensions de certaines zones graphiques | Pilotes C, puis lot concerné D/E | Aucune exception ne doit masquer un défaut structurel de scaling ou rendre les commandes inaccessibles. |
| Charges et découpage précis des lots | Recalibrer au bilan C | Les anciennes fourchettes de l'audit ne sont pas des engagements; conserver les sept phases et leur ordre. |
| Publication de l'activation globale `PerMonitorV2` | Décision en G | Le développement/test en B/C et le GO de migration ne valent pas autorisation ni preuve de publication. |

Les sujets différés de 7.1 restent hors réalisation obligatoire, sauf les améliorations esthétiques des pilotes explicitement autorisées et consignées sous C. Le suivi des validations est ajouté sous chaque phase, sans empiler une nouvelle version concurrente de la feuille de route. A et B sont validées sur leurs périmètres documentés; C est validée sur les cinq pilotes et sa recette manuelle ; GO D reçu, D1 implémentée en attente de recette, D2/D3 et E à G non commencées.

## 11. Points d'entrée dans le dépôt

- [Configuration du projet](../src/FrameShift/FrameShift.csproj) et [démarrage](../src/FrameShift/Program.cs).
- [Factory UI](../src/FrameShift/Windows/Helpers/FrameShiftUiFactory.cs), [layout commun](../src/FrameShift/Windows/Helpers/FrameShiftUiLayout.cs) et [métriques](../src/FrameShift/Windows/Helpers/FrameShiftUiMetrics.cs).
- [Shell éditeur](../src/FrameShift/Windows/Helpers/FrameShiftEditorShellUi.cs) et [shell crop](../src/FrameShift/Windows/Helpers/FrameShiftCropEditorUi.cs).
- [Cut Video](../src/FrameShift/Windows/Forms/CutVideoForm.cs), [Cut Audio](../src/FrameShift/Windows/Forms/CutAudioForm.cs), [Interpolate Video](../src/FrameShift/Windows/Forms/InterpolateVideoForm.cs) et [Progress](../src/FrameShift/Windows/ProgressUI/ProgressForm.cs).
- [Create Subtitles](../src/FrameShift/Windows/AI/CreateSubtitlesPickerForm.cs), [Image to PDF](../src/FrameShift/Windows/Forms/ImageToPdfForm.cs), [timeline Join Videos](../src/FrameShift/Windows/Controls/JoinVideosTimelineControl.cs).
- [Thème](../src/FrameShift/Windows/Helpers/FrameShiftTheme.cs), [dessin](../src/FrameShift/Windows/Helpers/FrameShiftUiPainter.cs) et [installateur](../installer/FrameShift.iss).
- Documents existants à réconcilier pendant l'implémentation : [standardisation](UI_STANDARDIZATION.md), [historique DPI](UI_DPI_AUDIT.md), [recette de release](RELEASE_CHECKLIST.md).
