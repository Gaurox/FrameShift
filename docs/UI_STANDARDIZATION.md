# FrameShift — Standard visuel et conception des fenêtres

Réconciliation F1 du 2 octobre 2026. Ce document décrit les choix visuels et ergonomiques appliqués aux fenêtres WinForms actuelles.

## Références et état du chantier

- [UI_FOUNDATION](UI_FOUNDATION.md) porte le **contrat technique et les métriques figées** : politique DPI, construction, composants, tailles et exemples.
- [L'audit UI/DPI du 28 septembre](UI_AUDIT_AND_STANDARDIZATION_PLAN_2026-09-28.md) porte la feuille de route, les décisions et les preuves de recette.
- [CODE_FILE_INDEX](CODE_FILE_INDEX.md) indique où se trouve le code actif.
- [UI_DPI_AUDIT](UI_DPI_AUDIT.md) conserve l'ancien bilan à titre historique ; ses anciennes recettes de placement ne s'appliquent plus aux nouvelles fenêtres.
- [RELEASE_CHECKLIST](RELEASE_CHECKLIST.md) porte la qualification de distribution et la chaîne canonique de publication.

Les 36 fenêtres C/D/E ont rejoint le socle et leur rendu a été accepté par l'utilisateur à 100/150/200/300 %. La portée des contrôles complémentaires reste celle consignée dans l'audit. F1 retire les chemins de compatibilité inutilisés et réconcilie ces documents. F2/F3/F4 sont vérifiées et acceptées sur leur périmètre ; G a produit l'installateur 1.20.0, accepté puis publié sur GitHub. Le rapport de release conserve les résultats et les limites de preuve.

## 1. Composition commune

Une fenêtre est construite par composition de contrôles WinForms. Le formulaire décrit ses options et relie les événements au métier.

| Besoin | Point d'entrée actif |
|---|---|
| Politique de fenêtre, police, DPI et contraintes de taille | `FrameShiftWindowPolicy.Initialize` avant contrôles/handles, sous `SuspendLayout` |
| Titre Windows et icône globale/IA | `FrameShiftWindowChrome.Apply` |
| Bandeau interne de fonction | `FrameShiftUiFactory.CreateHeader` |
| Dialogue compact avec contenu défilant | `FrameShiftDialogLayout.Create` |
| Corps spécialisé, statut et actions persistantes | `FrameShiftDialogLayout.CreateShell` |
| Éditeur avec options à côté de l'aperçu, repliées dessous en espace réduit | `FrameShiftEditorShellUi.Create` |
| Aperçu au-dessus des commandes temporelles | `FrameShiftEditorShellUi.CreateTimeline` |
| Composition crop et panneau de dessin | `FrameShiftCropEditorUi.Create` / `CreatePreviewPanel` |
| Sections, champs, choix et textes mesurés | `FrameShiftUiFactory` et ses contrôles partagés |
| Actions de validation/annulation/fermeture | `CreateMeasuredActionButton` puis `FrameShiftDialogLayout.CreateActions` |
| Informations courtes ou messages copiables | `CreateWrappingLabel` / `CreateStatusMessage` |
| Grilles de fichiers | `FrameShiftGridUi` pour les mesures DPI/texte |

`FrameShiftUiLayout` conserve le calcul de mesure des boutons ; les anciens placements de sections/footers/rangées ont été retirés en F1. Les factories `CreateFixed*`, les anciens `CreateFill*` et les anciens roots/spacers des shells ont également été retirés. Utiliser les entrées actives ci-dessus.

## 2. Géométrie et adaptation à l'espace

La [table de géométrie figée](UI_FOUNDATION.md#contrat-de-géométrie-figé--29-septembre-2026) est la référence unique. Les valeurs sont logiques à 96 DPI et proviennent de `FrameShiftUiMetrics`.

- Marges extérieures et séparation des grandes zones : 12.
- Écart entre blocs empilés : 10 ; entre champs/choix et titre/contenu de section : 8.
- Bandeau : hauteur minimale 58, icône 38 ; titre et métadonnées mesurés.
- Validation, annulation et fermeture : même taille de référence 140 × 34 et écart 10. La paire grandit ensemble si les textes le demandent ; retour à la ligne quand nécessaire.
- Les paddings de section viennent du socle ; éviter les marges implicites ajoutées par les contrôles locaux.

Ces références constituent des minima et espacements de conception, pas des tailles de texte plafonnées. Utiliser `TableLayoutPanel`, `Dock`, `AutoSize` et la hauteur préférée des contenus. Les sections principales partagent les mêmes alignements extérieurs.

### Taille initiale et défilement

Pour un dialogue compact, `FitInitialHeight` mesure le contenu à `Load`, après la mise à l'échelle native. Montrer toutes les options à l'ouverture lorsque l'espace de travail le permet. Les sections masquées ne doivent pas réserver un grand espace vide.

En espace réduit, le corps ou le rail d'options défile ; les actions de footer restent atteignables. La politique commune reconstruit les minima depuis les références logiques et les borne au moniteur courant. Un minimum qui dépasse l'écran ne constitue pas une solution au manque de place.

Une adaptation de hauteur lors d'un changement explicite d'options est acceptable si elle est bornée à l'écran. Ne pas relancer ce calcul depuis chaque `Resize`/`Layout`, ni ajouter une ligne vide pour positionner le footer.

### DPI, texte et dessin

WinForms applique `AutoScaleMode.Dpi` depuis la référence 96 DPI. Le socle convertit les métriques manuelles par `ToPixels` dans le contexte du contrôle courant. `Bounds`, `ClientSize` et les mesures de texte sont déjà en pixels : ne pas les multiplier de nouveau.

Les polices sont héritées ; aucune multiplication manuelle de leur taille ou `Scale()` récursif en production. Peinture et hit-tests utilisent la même conversion ; les coordonnées média restent en secondes/frames, pixels source ou unités de page.

`PerMonitorV2` est activé **en Debug et Release** et distribué en 1.20.0 après acceptation utilisateur. Le testeur l'utilise également. Les résultats de qualification et la décision de publication sont consignés en G.

## 3. Bandeau et hiérarchie du texte

Chaque fenêtre reprend le bandeau commun : icône de fonction à gauche, titre `FrameShift - <Function>`, puis informations source. La chrome Windows des actions classiques utilise l'icône globale ; les fenêtres IA utilisent l'icône FrameShift AI.

- Titre du bandeau : Segoe UI Semibold, 14 pt ; contenu hérité : Segoe UI, 9 pt.
- Le titre peut revenir à la ligne. Le bandeau augmente sa hauteur mesurée.
- Les métadonnées restent compactes avec ellipse, tooltip et menu « Copy details » donnant le texte complet. Le complément clavier est suivi en F3.
- Une icône dédiée de fonction est choisie via `IconPaths`, avec fallback ; ses offsets sont convertis par le composant.
- Les textes longs utiles restent consultables ; conserver accents, chemins et noms complets dans les données du contrôle.

Les descriptions utilisent des labels avec retour à la ligne. Les détails longs ou erreurs utilisent un texte natif sélectionnable/copiable ; une ellipse seule ne suffit pas à rendre une erreur consultable.

## 4. Sections, choix et champs

### Sections

`CreateSection` sépare le titre et le contenu dans deux rangées mesurées. Le contenu doit annoncer sa hauteur préférée, par exemple une table de champs AutoSize. `CreateVerticalStack` applique les écarts entre blocs. Une section destinée à occuper l'espace restant peut utiliser `fill: true`.

### Choix

Utiliser des `RadioButton`, `CheckBox` et `ComboBox` natifs. Les radios exclusives partagent un parent. Une carte `FrameShiftChoiceCard` reste un radio natif avec titre et description ; `CreateChoiceRow` espace les choix et les replie si nécessaire.

Les cartes conviennent aux choix courts comparables, notamment la qualité de compression. Create Subtitles conserve la liste verticale radio avec descriptions validée par l'utilisateur. Ne pas remplacer cette présentation par des cartes.

Les presets sont des boutons standards espacés, aux coins légèrement arrondis. Leur sélection doit être compréhensible par le texte ou un indicateur ; éviter les changements de couleur arbitraires.

### Champs

`CreateFieldRow` gère label, éditeur, unité facultative, mesure et nom accessible. Un champ numérique court peut recevoir `logicalEditorWidth` : l'espace libre reste dans la ligne plutôt que d'étirer inutilement la saisie.

`CreateFieldWithUnit` maintient valeur et sélecteur d'unité côte à côte. Les unités et formats utilisent une `ComboBox` native en `DropDownList`. Les saisies optionnelles gardent une case native dans une rangée mesurée ; leur état activé/désactivé exprime les capacités réelles du format.

Les champs d'une même grille partagent leurs alignements et leur largeur. Redimensionner une fenêtre ne doit pas créer des hauteurs ou marges différentes pour les champs comparables.

## 5. Actions, informations et progression

Les boutons de footer proviennent du composant mesuré commun ; `CreateActions` égalise leur taille. Le formulaire relie `AcceptButton`, `CancelButton` et les événements selon son état. L'action longue doit conserver ses garde-fous de validation et d'annulation.

`CreateStatusMessage` est un texte multiligne en lecture seule, sélectionnable et copiable. Sa hauteur est mesurée et plafonnée à 96 unités logiques ; le texte long défile. Pour une lecture détaillée centrale, une zone native plus haute est une exception utile, comme Download Model ou Progress.

Progress conserve la file sobre et les détails en dessous avec une hauteur de lecture utile. Éviter les bandes alternées et les en-têtes bleus saturés dans Files. Les messages complets restent consultables pour chaque occurrence de fichier.

## 6. Thème et états visuels

Utiliser les rôles de `FrameShiftTheme` : `PageBackground`, `Surface`, `SurfaceBorder`, `TextPrimary`, `TextSecondary`, `TextMuted`, `AccentSoft`, `AccentSoftHover`, `AccentText`, `ErrorText`, `SuccessText` et les trois fonds `PrimaryButtonBackground` / `PrimaryButtonHover` / `PrimaryButtonPressed`.

L'identité conserve les bleus `#8EBAF3` et `#4D79B4` pour le dessin. F2 utilise des dérivés lisibles pour les petits textes et les actions ; les rôles exacts sont dans la [notice thème](DARK_LIGHT_THEME_IMPLEMENTATION.md). Les aplats texte/fond utiles des deux palettes sont testés à **4,5:1 minimum**, y compris sur les surfaces de survol. Les boutons principaux ont un survol et un appui distincts, conservent leur texte blanc et changent d'aspect lorsqu'ils sont désactivés. Le focus/choix actif emploie `AccentText` ; les messages d'erreur et succès suivent le thème. Cette vérification ciblée ne certifie pas le contraste élevé exhaustif ni les pixels des médias.

La préférence Light/Dark/System est persistée séparément des réglages IA. System est résolu à l'initialisation ou au changement de préférence ; le rafraîchissement concerne les fenêtres ouvertes **du même processus**. Les autres processus ne reçoivent pas de changement en direct. Les styles locaux de grille nécessitent une vérification spécifique.

Les fonds d'aperçu sombres ou les pixels des médias ne sont pas des surfaces de formulaire à remapper. Les contrôles natifs restent privilégiés pour leur comportement Windows ; leur apparence standard n'impose pas de créer un faux sélecteur.

## 7. Variantes validées à préserver

| Famille | Choix de conception |
|---|---|
| Interpolate Video | Champ FPS compact ; presets espacés ; hauteur initiale mesurée |
| Compression | Choix de qualité côte à côte lorsque l'espace le permet ; cible optionnelle et unité natives |
| Cut Video / Cut Audio / Create GIF | Sélection et champs temporels sous l'aperçu ; footer indépendant |
| Create Subtitles | Radios verticales et descriptions ; hauteur recalculée au changement explicite de format |
| Resize Image / Video | Quatre saisies égales en grille 2 × 2 ; largeur de référence 128, réduite ensemble si nécessaire |
| Speed Video | Presets sur une ligne à l'ouverture si l'écran permet la largeur mesurée ; repli en espace réduit |
| Rotate/Flip | Palette standard ; indicateur ✓ sur les miroirs actifs |
| Convert to Icon | Tailles/réglages/aperçus en trois colonnes, puis deux ou une selon l'espace |
| Crop / Burn Subtitles / Remove Object / Image to PDF | Aperçu flexible et options à rail repliable ; opérations média spécifiques conservées |
| Image to PDF | Tuiles d'outils aux dimensions communes mesurées ; champs Page alignés à gauche et à droite |
| Download Model | Erreur multiligne dédiée ; annulation et fermeture attendent la tâche |
| Main / Progress | Files natives ; progression commune et détails copiables ; commandes persistantes |

## 8. Construction, ressources et recette

Les bitmaps, icônes, polices et tooltips créés par l'application doivent avoir un propriétaire et être libérés au remplacement/à la fermeture. Disposer les contrôles retirés d'une reconstruction. Le contrat est déjà explicite dans les bandeaux et la politique de police ; les points restants sont suivis en F4.

Les aperçus passent par les helpers de durée de vie existants : travail asynchrone, annulation, attente à la fermeture et libération des retours tardifs. Après une fermeture annulée dans `FormClosing`, poster la fermeture finale avec `BeginInvoke` et restaurer le résultat modal. Ne pas réentrer immédiatement dans `Close()` après un `await` qui peut déjà être terminé.

Avant de terminer une nouvelle fenêtre ou une modification :

- choisir le shell adapté et réutiliser les métriques/composants ;
- vérifier options dynamiques, textes longs et espace réduit, avec footer atteignable ;
- vérifier noms accessibles, Tab/Shift+Tab, focus, choix et Enter/Échap selon l'état ;
- vérifier clair/sombre, survol, sélection et désactivation ;
- vérifier peinture/hit-tests et conservation des coordonnées média ;
- contrôler chargement, fermeture, annulation et propriété des ressources ;
- exécuter le build obligatoire et les tests pertinents ;
- consigner les essais réels avec binaire/commit, résolution, DPI, texte, thème et scénario.

Les [exemples et formulaires de référence](UI_FOUNDATION.md#exemples-actifs-pour-une-nouvelle-fenêtre) donnent les points de départ. Les tests cachés de mesure ne remplacent pas la recette Windows réelle. La matrice finale et la distribution installée sont qualifiées en G par la chaîne canonique.
