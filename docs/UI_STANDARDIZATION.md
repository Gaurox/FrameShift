# FrameShift UI Standardization

Ce document définit la base visuelle à respecter pour toutes les fenêtres WinForms de FrameShift.

Objectif :
- garder une identité UI cohérente ;
- éviter les variations arbitraires ;
- préserver une mise en page simple, stable et lisible ;
- permettre à une nouvelle fenêtre de s’intégrer au projet sans casser le style existant.

Si une future UI a une contrainte qui n’est pas décrite ici, elle doit s’adapter en conservant l’esprit général :
- mêmes couleurs de référence ;
- mêmes marges de base ;
- mêmes bordures ;
- mêmes hiérarchies typographiques ;
- mêmes comportements de boutons et d’états visuels.

Le style FrameShift doit rester :
- léger ;
- Windows-first ;
- sobre ;
- fonctionnel ;
- cohérent.

---

## 0. Couche UI partagée active

La standardisation UI FrameShift repose désormais sur une petite couche commune WinForms.

Niveaux actifs :
- `FrameShiftTheme.cs` : palette et couleurs de référence ;
- `FrameShiftThemePreference.cs`, `FrameShiftUiSettings.cs` et `WindowsThemeDetector.cs` : préférence `System` / `Light` / `Dark`, persistance, résolution au démarrage et application immédiate aux fenêtres ouvertes ;
- `FrameShiftUiMetrics.cs` : métriques communes (`padding`, hauteurs, largeurs de rail, rayons, gaps) ;
- `FrameShiftUiLayout.cs` : règles de placement réutilisables ;
- `FrameShiftUiFactory.cs` : construction des briques standard (`header`, sections, info cards, boutons, champs) ;
- `FrameShiftEditorShellUi.cs` : shell commun pour les écrans d’édition avec bandeau + spacer + zone de travail principale ;
- `FrameShiftCropEditorUi.cs` : shell partagé de la famille `crop`.
- `FrameShiftWindowChrome.cs` : chrome Windows commune des fenêtres d’action (`Text`, `ShowIcon`, icône globale ou IA selon le contexte).
- `FrameShiftMenuRenderer.cs` : rendu commun des menus `ToolStrip` / `ContextMenuStrip`.
- `IconPaths.cs` : mapping centralisé des icônes actives, y compris les icônes IA du dossier `Assets\Icons\ai`.

Règle de structure :
- d’abord réutiliser la couche commune globale ;
- ensuite seulement ajouter un helper de famille si plusieurs écrans riches partagent vraiment la même charpente ;
- éviter les helpers mono-écran déguisés en couche commune ;
- ne pas reconstruire localement les marges, hauteurs de bandeau, largeurs de rail et cartes standards si un helper partagé existe déjà.

### 0.1 Règles DPI actives

Le chantier DPI/UI a figé des règles obligatoires pour les fenêtres WinForms FrameShift.

Constat à retenir :
- le comportement `AutoScale` WinForms ne suffit pas à lui seul à garantir une géométrie correcte sur tous les écrans Windows ;
- les problèmes corrigés concernaient la géométrie complète, pas uniquement la police ;
- les régressions visées étaient les textes coupés, labels tronqués, groupes trop petits, chevauchements, et fenêtres trop basses à `125 %`, `133 %`, `150 %` et `175 %`.

Règles actives :
- utiliser `AutoScaleMode = AutoScaleMode.Dpi` sur les dialogues fixes et les pickers standard ;
- centraliser les dimensions de référence dans `FrameShiftUiMetrics.cs` au lieu de disperser des nombres magiques liés à `96 DPI` ;
- centraliser la disposition dans `FrameShiftUiLayout.cs`, `FrameShiftEditorShellUi.cs` et `FrameShiftCropEditorUi.cs` pour garder des espacements cohérents ;
- appliquer la chrome commune via `FrameShiftWindowChrome.Apply(...)` avant la construction fine des contrôles ;
- définir un `MinimumSize` explicite sur les fenêtres redimensionnables ou denses quand la lisibilité dépend d’une largeur/hauteur minimales ;
- préférer des layouts à base de `TableLayoutPanel`, `Dock`, `Padding` et panneaux de section plutôt qu’un empilement fragile de coordonnées isolées.

Règle critique :
- ne jamais compter sur une double mise à l’échelle ;
- la géométrie de construction doit rester la référence logique ;
- la géométrie runtime ne doit pas être retraitée une seconde fois par une logique locale concurrente ;
- toute nouvelle fenêtre doit choisir une seule stratégie de scaling et s’y tenir.

---

## 1. Palette de référence

Les deux couleurs de base du projet sont fixes et doivent rester les mêmes partout :

| Rôle | Couleur | Usage |
|---|---:|---|
| Bleu principal | `#8EBAF3` | bordures actives, focus, accents légers, contours de cadres, états sélectionnés |
| Bleu secondaire | `#4D79B4` | bouton principal, titres accentués, état actif fort, texte d’action secondaire |

Couleurs neutres standard :

| Rôle | Couleur | Usage |
|---|---:|---|
| Fond de page | `#F5F7FB` | arrière-plan global des fenêtres |
| Surface | `#FFFFFF` | panneaux, cartes, cadres, zones de contenu |
| Bordure standard | `#D8E2F2` | bordures non actives et séparations douces |
| Texte principal | `#1F283A` | titres, libellés importants |
| Texte secondaire | `#525E73` | explications, sous-titres, informations secondaires |
| Texte atténué | `#7A8495` | aides mineures, valeur faible importance |
| Surface accentuée | `#ECF3FF` | cadres d’information bleus, zones de rappel |
| Surface accentuée hover | `#E4EEFE` | survol de surfaces bleues légères |

Ces neutres décrivent la palette `Light`. `FrameShiftTheme` est la seule source des équivalents sombres ; une fenêtre ne doit jamais déduire ou choisir une palette localement.

Règles :
- ne pas introduire de nouvelle couleur d’accent sans validation explicite ;
- éviter le noir pur pour les textes et les bordures ;
- les contours noirs système doivent être remplacés par `#8EBAF3` ou `#4D79B4` selon l’importance visuelle ;
- les états actifs doivent toujours être plus marqués que les états inactifs, mais sans saturation agressive.

---

## 2. Fenêtre standard

Format général recommandé :
- fond global : `PageBackground` ;
- bordure de fenêtre : standard WinForms ;
- pas de fenêtre maximisée par défaut ;
- pas de layout plein écran pour les actions simples ;
- la hauteur doit rester compacte mais respirer.

Marges globales standard :
- marge externe gauche : `12 px`
- marge externe droite : `12 px`
- marge externe haute : `12 px`
- marge externe basse : `12 px`

Toutes les sections principales doivent s’aligner sur ces marges.

Règle de largeur :
- les blocs principaux utilisent la largeur utile de la fenêtre ;
- la longueur horizontale s’adapte à la largeur du formulaire ;
- on garde les mêmes marges extérieures, pas les mêmes largeurs absolues ;
- si la fenêtre grandit, les sections s’étirent de façon uniforme.

Règle d’implémentation :
- utiliser en priorité `FrameShiftUiMetrics` pour les marges et hauteurs standard ;
- utiliser `FrameShiftEditorShellUi` pour les écrans avec un grand workspace et un rail latéral ;
- ne pas redéfinir localement `OuterPadding`, `HeaderHeight` ou une largeur de rail déjà standardisée.

Règles DPI complémentaires :
- pour une fenêtre fixe, partir d’une taille cliente qui laisse de l’air au contenu à `100 %` puis vérifier qu’elle reste lisible aux paliers Windows validés ;
- pour une fenêtre redimensionnable, définir un `MinimumSize` cohérent avec la grille réelle ;
- ne pas “récupérer” un manque de place par des offsets locaux ou par une compression arbitraire du footer ;
- si le contenu long force un compromis, agrandir la fenêtre ou la section avant de réduire les espacements standard.

---

## 3. Bandeau supérieur standard

Chaque fenêtre d’action doit avoir un cadre supérieur standard.

Hauteur standard :
- `58 px` pour les fenêtres compactes ;
- cette hauteur peut monter légèrement si une action exige davantage d’espace, mais il faut rester proche du standard.

Structure du bandeau :
- un cadre à fond blanc ;
- bordure fine bleue ;
- rayon visuel arrondi ;
- icône de la fonction à gauche ;
- nom du programme et nom de la fonction ;
- ligne d’informations secondaire sous le titre ;
- aucune surcharge décorative.

Disposition recommandée :
- marge interne gauche du bandeau : `12 px`
- marge interne haute : `10 px`
- icône : `38 x 38 px`
- l’icône doit être centrée dans son cadre par rendu interne dédié si l’asset source n’est pas naturellement centré
- espacement entre icône et texte : `10 px`
- titre : aligné visuellement sur le haut du bloc texte
- sous-titre / métadonnées : sur une seconde ligne, plus petit

Typographie du bandeau :
- titre :
  - `Segoe UI Semibold`
  - `14 pt`
  - couleur `TextPrimary`
- ligne secondaire :
  - `Segoe UI`
  - `9 pt`
  - couleur `TextSecondary`

Contenu du bandeau :
- titre standard : `FrameShift - <Function Name>`
- fonction visible clairement dans le titre ;
- l’icône doit représenter la fonction affichée quand une icône dédiée existe ;
- la ligne secondaire peut afficher :
  - format source ;
  - taille source ;
  - résolution ;
  - autre information utile mais courte.

Règles :
- le bandeau ne doit pas devenir un header géant ;
- l’icône doit rester visible ;
- éviter `ImageLayout.Center` seul si l’asset produit un décalage visuel ; préférer un canevas intermédiaire centré de la taille du cadre icône ;
- ne pas réutiliser systématiquement l’icône FrameShift générique si une icône de fonction existe déjà ;
- si le texte est long, réduire le contenu secondaire avant de réduire la taille du bandeau ;
- ne pas inventer des décorations différentes selon les fenêtres ;
- si un écran a besoin d’une variante, elle doit rester reconnaissable comme le même bandeau FrameShift.

Règle d’implémentation :
- tous les bandeaux d’action doivent passer par `FrameShiftUiFactory.CreateFillHeader(...)` ou `CreateFixedHeader(...)` ;
- ces deux entrées doivent rester de simples variantes de taille et non deux systèmes visuels divergents ;
- les compensations visuelles d’icône validées doivent être centralisées dans la couche commune, pas dispersées dans chaque formulaire.

## 3.1 Chrome de fenêtre commune

La barre de titre Windows doit rester uniforme sur toutes les fenêtres d’action.

Règles :
- le texte de la fenêtre doit suivre `FrameShift - <nom de la fonction>` ;
- les fenêtres standard utilisent l’icône globale FrameShift ;
- les fenêtres IA et téléchargements de modèles IA utilisent l’icône `FrameShift AI` dans la barre de titre ;
- les icônes de fonction dédiées des fonctionnalités IA restent réservées au bandeau interne ;
- ces icônes dédiées doivent venir uniquement de `Assets\Icons\ai` ;
- la chrome Windows doit être appliquée via une couche commune, pas recodée dans chaque formulaire.

Règle d’implémentation :
- utiliser `FrameShiftWindowChrome.Apply(...)` pour les fenêtres d’action qui partagent cette chrome standard ;
- utiliser la surcharge avec `FrameShift AI` comme icône préférée pour les fenêtres IA ;
- conserver le bandeau interne séparé et inchangé ;
- ne pas mélanger chrome Windows et bandeau interne dans un seul helper local.

---

## 4. Sections principales

Les blocs de contenu doivent suivre un système unique.

Règles de forme :
- fond blanc ou surface très claire ;
- bordure bleue fine ;
- coins visuellement arrondis ;
- pas de bordure système noire ;
- titre de section en haut à gauche ;
- espacement interne constant.

Hauteur et largeur :
- la largeur des sections principales doit être identique au sein d’une même fenêtre ;
- elles doivent partager la même largeur utile que le bandeau ;
- l’écart horizontal entre bords de fenêtre et sections doit rester constant ;
- l’écart vertical entre sections doit rester cohérent.

Espacements recommandés :
- padding interne section : `12 px`
- espacement entre sections : `12 px` à `14 px`
- unité verticale interne standard : `8 px`
- écart entre titre de section et contenu : même valeur que l’unité verticale interne
- écart entre lignes d’un même bloc : même valeur que l’unité verticale interne
- padding bas d’un bloc ajusté au contenu : même valeur que l’unité verticale interne

Titres de section :
- `Segoe UI Semibold`
- `9 pt`
- couleur `SecondaryBlue`
- pas de majuscule forcée si la fenêtre préfère une casse naturelle ;
- rester court et descriptif.

Exemples de sections standards :
- `Compression level`
- `Optional target size`
- `Resize options`
- `Source settings`

Règles :
- les blocs qui portent le même rôle visuel doivent avoir la même longueur horizontale ;
- ne pas mélanger plusieurs styles de cadres dans la même fenêtre ;
- si une section contient des cartes ou des tuiles, la section doit rester le conteneur principal de référence.

Règle d’implémentation :
- utiliser `FrameShiftUiFactory.CreateFillSection(...)` pour les sections standards ;
- pour les cartes latérales d’écrans d’édition, utiliser un helper commun de shell si plusieurs écrans partagent la même structure ;
- éviter de recréer localement un “panel group” quasi identique d’un écran à l’autre.

Règles DPI :
- la hauteur d’une section doit être pensée à partir de son contenu réel ;
- éviter les sections trop basses qui supposent implicitement un affichage à `100 %` ;
- si une section dépend d’un texte potentiellement plus long, réserver une hauteur qui absorbe la croissance verticale normale sans chevauchement.

---

## 5. Cartes, tuiles et choix exclusifs

Les choix de profil ou de preset doivent utiliser un système de cartes/tuiles homogène.

Règles :
- fond blanc ;
- bordure bleue ;
- rayon visuel arrondi ;
- état sélectionné plus marqué ;
- état non sélectionné plus léger ;
- le clic sur la tuile doit activer le choix associé ;
- les choix d’un même groupe doivent être exclusifs.

Taille recommandée des tuiles :
- hauteur compacte ;
- largeur régulière ;
- espacements égaux entre tuiles ;
- pas de tuiles qui débordent visuellement hors du groupe.

Libellés dans les tuiles :
- titre court ;
- description plus courte encore ;
- pas de paragraphe dans une tuile ;
- éviter les descriptions qui cassent la hauteur.

État visuel :
- non sélectionné :
  - bordure `PrimaryBlue`
  - fond blanc
- sélectionné :
  - bordure `SecondaryBlue`
  - accent visuel plus fort
  - indicateur radio visible

Règle de standardisation :
- si une fenêtre a trois choix de profil, ils doivent être rendus comme trois tuiles comparables ;
- si une fenêtre a plus de trois choix, conserver le même langage visuel mais réduire la densité sans changer le style ;
- ne pas utiliser des boutons radio nus si une tuile est plus lisible ;
- ne pas utiliser des tuiles si la densité de contenu devient illisible.

---

## 6. Cadres d’information bleus

Les encadrés avec fond bleu clair servent aux messages d’aide, rappels et notes de contexte.

Forme standard :
- fond `AccentSoft` ;
- bordure `PrimaryBlue` ;
- rayon visuel arrondi ;
- texte de couleur `TextSecondary` ;
- éventuelle icône d’information à gauche si utile.

Usage :
- rappeler que la sortie reste à côté de la source ;
- signaler des limites ou un comportement par défaut ;
- afficher une note utile au bas d’un formulaire ;
- afficher une aide courte et stable.

Règles :
- ces cadres ne doivent pas devenir de longues explications ;
- ils doivent rester lisibles en une ou deux phrases ;
- ne pas mélanger plusieurs niveaux d’information dans le même bloc ;
- s’il faut plus de texte, créer une section dédiée plutôt qu’un pavé d’aide.

Blocs d’information structurée :
- utiliser une ligne simple par information (`Label: value`) ;
- éviter les retours à la ligne internes dans un même label ;
- donner à chaque ligne une hauteur fixe et lisible ;
- calculer la hauteur du bloc à partir du nombre de lignes et du padding haut/bas ;
- conserver un padding haut et bas équilibré, idéalement identique à l’unité verticale interne standard ;
- ne pas laisser un texte multi-ligne entrer dans une ligne de hauteur fixe.

Exemple de texte attendu :
- `The compressed video is created next to the original file. The format stays the same.`

Ces cadres sont un élément identitaire du style FrameShift et doivent être réutilisés dès qu’un message de contexte mérite d’être mis en avant sans alourdir la fenêtre.

---

## 7. Boutons standard

FrameShift doit avoir deux familles de boutons standards.

### 7.1 Bouton principal

Usage :
- action de validation ;
- action principale de la fenêtre ;
- exécution de l’opération.

Style :
- fond `SecondaryBlue` ;
- texte blanc ;
- bordure `SecondaryBlue` ;
- survol `PrimaryBlue` ;
- pression : retour au `SecondaryBlue` ;
- curseur pointeur.

Typographie :
- `Segoe UI Semibold`
- `9 pt` ou équivalent natif de la fenêtre ;
- texte court, verbe d’action simple.

Dimensions :
- largeur standard : `140 px` pour l’action principale ;
- hauteur standard : `34 px`.

### 7.2 Bouton secondaire

Usage :
- annulation ;
- fermeture ;
- retour sans exécution.

Style :
- fond blanc ;
- texte `SecondaryBlue` ;
- bordure `PrimaryBlue` ;
- survol `AccentSoft` ;
- pression `AccentSoftHover` ;
- curseur pointeur.

Dimensions :
- largeur standard : `120 px` pour l’annulation ou l’action secondaire de footer ;
- hauteur standard : `34 px`.

### 7.3 Règles de placement

- le bouton principal se place à droite ;
- le bouton secondaire se place juste à sa gauche ;
- la zone de boutons doit rester en bas de la fenêtre ;
- l’espace vertical entre le dernier bloc visible et la rangée de boutons doit reprendre le même espacement standard que celui entre deux blocs principaux ;
- conserver une marge basse visible sous la rangée de boutons ;
- cette marge basse doit rester cohérente d’une fenêtre à l’autre et ne pas donner l’impression que les boutons touchent le bord ;
- éviter toute ligne de layout, spacer implicite ou hauteur de footer excédentaire qui agrandit artificiellement la fenêtre sans bénéfice visuel ;
- le groupe des boutons ne doit pas être masqué par d’autres blocs ;
- l’espacement horizontal entre les deux boutons doit rester faible et cohérent ;
- les boutons ne doivent pas être trop grands pour la fenêtre.

### 7.4 Règles générales

- pas de boutons gris par défaut si la fenêtre fait partie du style FrameShift ;
- pas de couleurs d’action arbitraires ;
- pas de double style de boutons dans une même fenêtre sans raison claire ;
- les boutons d’outils internes doivent reprendre la hauteur standard `34 px` ;
- dans un bloc d’outils compact, placer les boutons sur une ligne quand la largeur disponible le permet ;
- l’écart horizontal entre deux boutons d’outils doit reprendre l’écart standard entre boutons ;
- si une action a plus de deux boutons, conserver le bouton principal bleu et les autres en variantes secondaires sobres.
- si une rangée de boutons d’outils est indispensable au workflow, elle ne doit pas dépendre d’une hauteur résiduelle ou d’une ligne `Percent` ;
- une rangée d’outils obligatoire doit recevoir une ligne dédiée à hauteur fixe dans le layout ;
- ne pas supposer qu’une rangée d’outils restera visible après application des `Padding`, titres de section, espacements et footer ; la visibilité réelle doit être garantie par la grille elle-même ;
- si une fenêtre combine une grande zone de travail et plusieurs actions d’édition, les boutons d’outils doivent rester identifiables comme un bloc autonome, et non comme un reliquat comprimable du contenu principal.

---

## 8. Champs, sélecteurs et zones de saisie

Les zones de saisie doivent rester discrètes et lisibles.

Style standard :
- fond blanc ;
- bordure simple ;
- rayon visuel léger si la zone est dessinée dans un cadre ;
- pas de contour noir dominant ;
- texte principal noir bleuté ou gris foncé, jamais noir dur si une alternative harmonieuse existe.

Règles :
- les zones de saisie doivent s’aligner proprement avec leurs libellés ;
- les unités ou listes déroulantes doivent rester compactes ;
- les groupes `checkbox + textbox + unit` doivent conserver une ligne claire ;
- les champs composites avec boutons inline ou suffixes visuels doivent réserver explicitement la largeur de ces éléments au lieu de les laisser se dessiner par-dessus la zone de texte ;
- si la saisie est optionnelle, la zone de saisie doit être désactivée visuellement quand l’option n’est pas cochée.
- les menus de sélection simples doivent conserver le rendu natif Windows et ne doivent pas être enfermés dans un cadre bleu supplémentaire ;
- si un champ de sélection doit être renforcé visuellement, préférer la sobriété du cadre Windows standard plutôt qu’un double encadrement custom.

Règles DPI :
- ne pas fixer une hauteur de champ qui devient illisible dès que le rendu Windows agrandit la police ou les bordures ;
- si plusieurs contrôles sont alignés sur une ligne, vérifier qu’ils gardent tous la même base visuelle après scaling ;
- éviter de serrer une ligne jusqu’au point où la moindre hausse de DPI tronque l’unité, la flèche ou le texte.

### 8.4 Gestion des labels longs

Règles :
- utiliser `AutoEllipsis = true` pour les labels de résumé, d’état ou de source quand la ligne doit rester mono-ligne ;
- ne pas tronquer silencieusement un libellé critique sans stratégie visuelle claire ;
- si un texte doit rester entièrement lisible, lui réserver une largeur ou une hauteur adaptée au lieu de le compresser ;
- réduire en priorité le texte secondaire avant de sacrifier le titre principal ou le bouton d’action ;
- éviter les labels trop proches du bord droit d’une section.

Cas typiques :
- résumé source dans un bandeau ;
- ligne d’information dans une carte ;
- label d’état dans une colonne latérale.

### 8.1 Pattern recommandé pour une saisie optionnelle

Quand une option active ou désactive une valeur de saisie, utiliser un champ composite plutôt qu’un `TextBox` seul.

Pattern standard :
- un libellé ou une `CheckBox` d’activation ;
- un petit cadre bleu autour du champ de valeur ;
- un `TextBox` borderless à l’intérieur ;
- un sélecteur d’unité ou d’option compact à droite ;
- un texte d’aide léger dans la même ligne.

Règles importantes :
- le cadre de valeur doit rester visible même quand le champ est inactif ;
- éviter les rectangles gris Windows par défaut ;
- éviter les champs “flottants” sans indication visuelle ;
- si le champ n’est pas éditable, le rendre `ReadOnly` plutôt que de le masquer complètement ;
- si l’état désactivé doit rester lisible, utiliser une couleur de texte atténuée plutôt qu’un gris système agressif.

Pour les mini-sélecteurs d’unités :
- éviter d’ajouter un cadre bleu custom autour d’un `ComboBox` natif ;
- garder le cadre standard Windows du contrôle quand le sélecteur est simple ;
- si une sélection d’unité a besoin d’un traitement plus poussé, elle doit rester sobre et ne pas créer de double bordure ;
- le contrôle doit rester compact, aligné et cohérent avec le reste du formulaire.

Règle d’intégration :
- si l’UI demande un champ de saisie unique avec unité, il faut privilégier une ligne compacte harmonisée au lieu d’un contrôle standard isolé ;
- la ligne doit conserver sa présence visuelle même quand elle est inactive ;
- si le champ est au cœur de l’action, il doit être immédiatement identifiable comme une valeur à compléter.

### 8.2 Champs composites avec boutons inline

Certaines fenêtres utilisent un champ de valeur accompagné de petits boutons d’ajustement, par exemple `frame + < >`.

Règles :
- le champ texte et ses boutons doivent être pensés comme un seul sous-layout ;
- la largeur du champ doit être calculée en retirant explicitement la place des boutons et de leurs espacements ;
- ne pas laisser un `TextBox` docké remplir tout le conteneur si des boutons sont ensuite positionnés manuellement dans le même espace ;
- les boutons inline doivent rester compacts, lisibles et alignés verticalement avec le champ ;
- aucun bouton, séparateur ou bordure ne doit se superposer au texte ou à la bordure du champ ;
- si ce pattern apparaît plusieurs fois dans une même fenêtre, tous les groupes doivent utiliser la même géométrie.

### 8.3 Harmonisation `TextBox` / sélecteurs déroulants

Références actuelles à suivre :
- `CompressVideoForm.cs`
- `RifeInterpolateVideoPickerForm.cs`
- `FrameShiftUiFactory.cs`

Objectif :
- éviter qu’un `TextBox` paraisse “dessiné” différemment d’un sélecteur juste à côté ;
- éviter les hauteurs mixtes ;
- éviter les blocs tassés où un menu dépasse en bas ;
- éviter les menus trop étroits ou trop courts.

Pattern standard recommandé :
- les champs de valeur utilisent `CreateValueTextBox(...)` dans `CreateFixedTextInputHost(...)` ;
- hauteur visuelle standard : `30 px` ;
- padding interne standard du host : `8, 5, 8, 5` ;
- les sélecteurs doivent reprendre la même hauteur visuelle que les champs texte voisins ;
- dans une même ligne, `TextBox`, sélecteurs et suffixes doivent partager la même base verticale.

Règles de cohérence :
- ne pas mélanger dans une même ligne un `TextBox` encadré et un `ComboBox` qui a l’air plus haut ou plus bas ;
- si un sélecteur natif ne donne pas un rendu cohérent, le masquer derrière un petit conteneur stylé FrameShift plutôt que d’accepter un compromis visuel ;
- utiliser le même rayon, le même bleu de bordure et le même fond que les autres champs ;
- garder les libellés sur une ligne dédiée au-dessus quand cela améliore l’alignement ;
- si une ligne commence à être dense, agrandir la section ou la fenêtre au lieu de compresser les contrôles.

Pattern retenu pour les sélecteurs stylés :
- un `Panel` encadré via `CreateFramedPanel(...)` ;
- padding `8, 5, 8, 5` ;
- un `Label` docké en remplissage pour la valeur ;
- un petit `Label` docké à droite pour la flèche `▾` ;
- un `ContextMenuStrip` pour la sélection.

Quand utiliser ce pattern :
- si l’on veut que le sélecteur ait exactement la même présence visuelle qu’un champ texte ;
- si plusieurs sélecteurs doivent être strictement harmonisés avec des champs de valeur en lecture seule ;
- si l’on veut contrôler précisément la largeur d’ouverture du menu.

Règles pour les menus déroulants :
- par défaut, le menu doit au minimum reprendre la largeur du contrôle d’ancrage ;
- pour un sélecteur large comme `Model`, le menu doit s’ouvrir sur toute la largeur du bloc ;
- pour un petit sélecteur comme `Target`, on peut garder une largeur minimale raisonnable, mais jamais inférieure à celle du contrôle ;
- si la largeur du menu est forcée, recalculer aussi sa hauteur préférée ;
- ne jamais fixer seulement la largeur d’un `ContextMenuStrip` sans recalculer sa hauteur, sinon les petits menus risquent d’apparaître tronqués ;
- chaque `ToolStripMenuItem` doit recevoir la largeur utile du menu quand `AutoSize` est désactivé.

Règles de layout :
- laisser au moins une marge visible sous le dernier contrôle d’un bloc ;
- si un sélecteur du bas semble “collé” à la bordure, agrandir le bloc avant d’ajouter des offsets locaux arbitraires ;
- après toute modification de hauteur interne, redescendre aussi le bloc d’info, le footer et la taille globale de la fenêtre ;
- vérifier qu’aucune ligne de contrôle, aucun menu et aucun bloc d’info ne se superposent visuellement.

Méthode de validation :
- ouvrir la fenêtre avec un vrai fichier ;
- ouvrir chaque menu déroulant, pas seulement le plus grand ;
- vérifier séparément un petit menu (`Target`), un menu moyen (`Playback`) et un menu large (`Model`) ;
- vérifier que tous les choix restent lisibles, non tronqués et alignés ;
- vérifier que la fenêtre reste propre à `100 %` et en DPI Windows standard.

Principe à retenir :
- si un contrôle de saisie ou de sélection paraît différent des autres, ce n’est pas un détail cosmétique : il faut l’aligner sur le pattern standard avant de considérer la fenêtre terminée.

---

## 9. Typographie

Typographie de base recommandée :
- `Segoe UI`

Hiérarchie :
- titre de fenêtre ou titre de bandeau :
  - `Segoe UI Semibold`
  - `14 pt`
- titre de section :
  - `Segoe UI Semibold`
  - `9 pt`
- contenu standard :
  - `Segoe UI`
  - `9 pt`
- notes secondaires :
  - `Segoe UI`
  - `9 pt`
  - couleur plus douce

Règles :
- pas de police exotique ;
- pas de graisse aléatoire ;
- pas de taille différente sans fonction visuelle claire ;
- utiliser la graisse semibold pour guider l’œil, pas pour tout surcharger.

---

## 10. Espacement et densité

FrameShift doit privilégier une densité confortable plutôt qu’un écran trop compact ou trop vide.

Règles de base :
- espacement vertical cohérent entre les blocs ;
- texte jamais collé aux bordures ;
- boutons et champs alignés sur une grille simple ;
- éviter les zones trop larges sans fonction ;
- éviter les sections trop serrées qui donnent une impression de bricolage.

Règle pratique :
- si une fenêtre semble trop dense, augmenter d’abord les marges internes et les hauteurs de section avant d’augmenter fortement les tailles globales ;
- si une fenêtre semble trop vide, resserrer les groupes sans supprimer l’air autour des éléments principaux.

---

## 11. Bordures et arrondis

Les bordures doivent être cohérentes et systématiques.

Règles :
- les cadres importants utilisent `PrimaryBlue` ;
- les cadres sélectionnés ou boutons principaux utilisent `SecondaryBlue` ;
- aucun contour noir ne doit rester comme choix visuel principal ;
- les arrondis doivent rester modestes et réguliers ;
- les gros arrondis “fantaisie” sont interdits sauf validation explicite.

Règles de rayon recommandées :
- bandeau : rayon visuel léger ;
- section principale : rayon moyen ;
- tuiles : rayon moyen ;
- champs et petits conteneurs : rayon faible.

Si une UI ne supporte pas nativement les bords arrondis, elle doit au minimum simuler l’arrondi avec une bordure propre et un fond cohérent.

---

## 12. Règles de repli

Quand une fenêtre rencontre une nécessité qui n’est pas décrite ici :

1. garder les deux couleurs de référence ;
2. garder la hiérarchie bandeau / sections / aide / boutons ;
3. conserver les marges standard ;
4. réutiliser les styles existants plutôt que d’en créer un nouveau ;
5. préférer une adaptation légère à une refonte complète ;
6. si un nouveau motif visuel est indispensable, le formaliser dans ce document avant de l’appliquer ailleurs.

Règle de sécurité :
- l’IA ou le développeur qui implémente une fenêtre doit chercher à maintenir le style FrameShift plutôt qu’à inventer un style local ;
- si un compromis est nécessaire, il doit préserver :
  - la lisibilité ;
  - la cohérence des couleurs ;
  - la simplicité ;
  - la stabilité du layout.

### 12.1 Garde-fous pour les nouveaux cas

Si une future fenêtre a besoin d’un motif non couvert par ce document :
- commencer par chercher un motif déjà existant dans FrameShift ;
- réutiliser la même logique de largeur, de bordure et de hiérarchie ;
- garder les deux couleurs de référence ;
- conserver le principe bandeau / sections / aide / boutons ;
- ne pas introduire un contrôle système dont l’apparence casse le style général ;
- si un contrôle natif est acceptable techniquement mais pas visuellement, le masquer derrière un petit conteneur de style FrameShift ;
- si une action optionnelle active une saisie, la saisie doit rester identifiable même dans l’état non actif.

Le principe directeur est le suivant :
- si le comportement doit rester simple, le style doit aussi rester simple ;
- si le contrôle natif ne respecte pas le style, le composant doit être stylisé autour de lui ;
- si le style ne peut pas être obtenu proprement avec le contrôle natif, il faut préférer un petit composant custom plutôt qu’un faux compromis visuel.

### 12.2 Fenêtres redimensionnables et éditeurs légers

Certaines fenêtres interactives peuvent être redimensionnables sans suivre exactement la géométrie des dialogues fixes.

Règles spécifiques :
- conserver la hiérarchie FrameShift bandeau / workspace / aide / boutons ;
- utiliser un rail de contrôle fixe quand la fenêtre contient une grande zone de travail flexible ;
- laisser la zone de travail principale prendre l’espace restant ;
- garder un espacement vertical standard entre le bandeau et le workspace principal, matérialisé par un spacer dédié plutôt que par un margin implicite ;
- garder les espaces entre blocs bleus identiques aux autres fenêtres de référence ;
- garder le même espacement standard entre le dernier bloc de contenu et la rangée de boutons, sans créer de zone vide supplémentaire dans la grille ;
- aligner la colonne de contrôle droite sur le même repère visuel que le bandeau, sans marge implicite de conteneur intermédiaire ;
- éviter tout effet de trace ou de superposition sur les cadres bleus pendant le redimensionnement ;
- garder l’icône de fonction alignée sur le standard du bandeau FrameShift ;
- conserver les dimensions et l’espacement standards des boutons de validation et d’annulation ;
- donner aux boutons d’outils la même hauteur que les boutons d’action standards ;
- garder une distance stable entre la zone de travail et l’élément immédiatement en dessous ;
- garder une distance stable entre le titre d’un bloc et le premier élément de son contenu ;
- faire en sorte qu’un bloc d’information adapte sa hauteur à son contenu plutôt que l’inverse ;
- préserver des retours à la ligne et un espacement vertical lisibles dans les blocs d’information ;
- conserver un espacement régulier entre le titre d’un bloc de choix et ses options internes ;
- définir une taille minimale de fenêtre suffisante pour respecter les espacements standards sans écrasement du layout.

Règle pratique :
- si une fenêtre redimensionnable ne peut pas respecter les espacements standards à sa taille minimale, sa taille minimale doit être relevée avant de modifier le style.

Règles `MinimumSize` :
- `MinimumSize` n’est pas optionnel pour une fenêtre riche qui combine preview, rail latéral, cartes d’info et footer ;
- la taille minimale doit protéger la lisibilité des boutons, des labels et des contrôles interactifs ;
- relever `MinimumSize` est préférable à l’introduction de micro-ajustements locaux qui cassent la cohérence globale ;
- après modification d’une grille ou d’une section, revérifier que `MinimumSize` reste aligné avec la géométrie réellement nécessaire.

Blocs titrés à hauteur adaptée :
- le titre du bloc doit avoir une position fixe en haut du bloc ;
- le premier élément du contenu doit commencer sous le titre avec le même espacement vertical que celui utilisé entre deux lignes de contenu ;
- la hauteur du bloc doit être calculée à partir du dernier élément visible, en ajoutant le padding bas standard ;
- le padding bas du bloc doit reprendre le même espacement que celui entre le titre et le premier élément ;
- le dernier élément ne doit pas porter de marge basse parasite si le bloc calcule déjà son padding inférieur ;
- le bloc suivant doit conserver son espacement standard avec le bloc précédent ;
- éviter de mélanger `AutoSize`, `DockStyle.Fill` et panneaux internes auto-ajustés pour calculer la hauteur d’un bloc titré ;
- si le bloc est dynamique, mesurer explicitement le contenu utile puis appliquer cette hauteur au bloc ou à la ligne qui le contient.

Implémentation WinForms recommandée :
- neutraliser les `Margin` implicites des `TableLayoutPanel` quand ils faussent l’alignement ;
- éviter de compter sur `AutoSize` pour mesurer un bloc contenant des contrôles dockés ;
- mesurer le contenu utile avec `PreferredSize` ou une hauteur connue, puis appliquer cette hauteur à la ligne qui contient le bloc ;
- ne pas mélanger positionnement manuel et `DockStyle.Fill` dans un même sous-layout de saisie si cela peut provoquer une superposition visuelle ;
- dessiner les cadres arrondis sans changer la `Region` pendant le redimensionnement ;
- invalider les cadres au `SizeChanged` pour éviter les traces de bordures.
- pour les fenêtres à zone de travail calculée à partir de la largeur visible, comme waveform, timeline ou preview editor, recalculer le rendu une fois la taille finale connue ;
- ne pas figer un rendu de waveform, timeline ou canvas à partir d’une largeur mesurée trop tôt dans le cycle d’affichage ;
- quand un écran utilise un header avec icône de fonction, prévoir si nécessaire un léger offset visuel validé pour compenser un asset non centré naturellement ;
- le centrage d’une icône dans son canevas doit être évalué visuellement, pas seulement mathématiquement ;
- pour les fenêtres fixes WinForms, vérifier explicitement la hauteur utile restante après addition de tous les espacements standards, au lieu de supposer que la somme théorique du layout suffit.

Règles `TableLayoutPanel` :
- utiliser `RowStyle` et `ColumnStyle` explicites pour les lignes critiques ;
- réserver une ligne fixe aux toolbars ou footers qui doivent toujours rester visibles ;
- éviter les combinaisons ambiguës de lignes `Percent` avec des blocs obligatoires placés tout en bas ;
- ne pas compter sur le layout implicite pour absorber un contenu qui varie avec le DPI ;
- si un bloc titré dépend d’une hauteur calculée, ajuster explicitement sa ligne plutôt que de laisser WinForms improviser.

Règles de marges :
- conserver `OuterPadding`, `BlockGap`, `LineGap` et `StandardSectionPadding` comme références de base ;
- éviter les marges locales arbitraires destinées seulement à “faire rentrer” un écran ;
- quand un bloc semble trop serré, corriger la grille ou la hauteur utile avant d’ajouter un `+2` ou `-3` dispersé ;
- les marges doivent servir la lisibilité, pas masquer un problème structurel de layout.

### 12.3 Fenêtres d’édition audio et vidéo compactes

Certaines fenêtres de type éditeur léger, comme une coupe audio ou un sélecteur de segment, ont besoin d’une hiérarchie plus précise que les dialogues simples.

Hiérarchie recommandée :
- bandeau standard ;
- section principale de travail ;
- rangée d’outils d’édition ;
- bloc d’aide ou d’état ;
- footer avec validation et annulation.

Règles :
- la zone de travail principale reste le bloc visuel dominant ;
- la rangée d’outils d’édition ne doit pas être absorbée par la section principale si cela compromet sa visibilité ;
- si la rangée d’outils doit toujours être visible, elle doit être placée sur sa propre ligne dédiée entre la zone de travail et le bloc d’aide, ou dans une sous-ligne fixe explicitement réservée ;
- le bloc d’aide doit rester en dessous des outils si son rôle est informatif, afin de ne pas masquer visuellement les actions ;
- l’espacement entre la zone de travail, la rangée d’outils, le bloc d’aide et le footer doit reprendre les espacements standards du projet ;
- la hiérarchie visuelle doit faire comprendre immédiatement ce qui est zone interactive, ce qui est action d’édition, ce qui est contexte, et ce qui valide l’opération.

---

## 13. Checklist rapide pour une nouvelle fenêtre

Avant de valider une nouvelle UI, vérifier :

- `AutoScaleMode = AutoScaleMode.Dpi` est bien défini si la fenêtre suit le modèle des dialogues/pickers standard ;
- le bandeau supérieur suit le standard ;
- le titre est clair ;
- l’icône de la fonction est visible ;
- les sections ont les mêmes largeurs et les mêmes bordures ;
- les deux bleus de référence sont bien utilisés ;
- les bordures noires système ont disparu ;
- les boutons suivent le style principal/secondaire ;
- les choix exclusifs sont réellement exclusifs ;
- les blocs d’aide bleus sont utilisés quand il faut contextualiser ;
- les fenêtres redimensionnables gardent leurs espacements standards à la taille minimale ;
- les boutons d’action et d’outil ont des hauteurs cohérentes avec les fenêtres de référence ;
- les blocs titrés calculés au contenu affichent toutes leurs lignes sans chevauchement ni découpe ;
- le premier élément, les lignes internes et le bas du bloc utilisent la même unité verticale ;
- les saisies optionnelles utilisent bien un champ composite lisible quand nécessaire ;
- les menus de sélection simples ne créent pas de double cadre bleu ;
- les labels longs sont soit pleinement lisibles, soit gérés volontairement via `AutoEllipsis` ;
- les `TableLayoutPanel` critiques ont des lignes/colonnes explicites et ne cachent pas une rangée importante ;
- la fenêtre tient correctement à `125 %`, `133 %`, `150 %` et `175 %` sans texte coupé ni chevauchement ;
- si la fenêtre est redimensionnable, `MinimumSize` protège réellement la géométrie minimale validée ;
- aucune partie de la fenêtre ne paraît improvisée.

Si l’un de ces points n’est pas respecté, il faut corriger la fenêtre avant de la considérer comme standardisée.
