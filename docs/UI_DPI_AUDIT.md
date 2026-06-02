# FrameShift UI DPI Audit

Référence officielle du chantier DPI/UI.

Périmètre :
- documentation uniquement ;
- aucune modification de code dans ce document ;
- objectif : figer les constats, la stratégie retenue et les règles futures pour les fenêtres WinForms FrameShift.

## 1. Problème traité

Le chantier a corrigé les défauts de rendu observés sur des écrans Windows configurés en :
- `125 %`
- `133 %`
- `150 %`
- `175 %`

Symptômes relevés avant correction :
- textes coupés ;
- labels tronqués ;
- groupes trop petits ;
- contrôles qui se chevauchent ;
- fenêtres trop petites ;
- disposition incorrecte selon le facteur de scaling Windows.

Le problème n’était pas limité à la police.

La géométrie complète était concernée :
- tailles de fenêtres ;
- boutons ;
- panels ;
- marges ;
- espacements ;
- groupes ;
- layouts.

## 2. Diagnostic retenu

Le diagnostic final validé est le suivant :
- le comportement standard `AutoScale` de WinForms ne garantissait pas à lui seul un résultat fiable sur tous les écrans ciblés ;
- dans certains cas, le DPI de référence et le DPI courant pouvaient aboutir à un facteur effectif de `1` ;
- le rendu pouvait donc rester sous-dimensionné alors que le scaling Windows était supérieur à `100 %`.

Conséquence pratique :
- une fenêtre pouvait garder une géométrie pensée pour un affichage logique trop compact ;
- les textes, groupes et boutons perdaient alors l’espace réellement nécessaire au runtime.

## 3. Stratégie retenue

La stratégie validée par le chantier est une stratégie DPI explicite et centralisée côté UI.

Principes :
- le projet reste WinForms ;
- Windows reste la cible principale ;
- la géométrie ne doit pas dépendre uniquement d’un comportement implicite du framework ;
- les métriques communes doivent être centralisées ;
- les fenêtres denses doivent protéger leur lisibilité par leur grille et, si nécessaire, par `MinimumSize`.

Règle conceptuelle de référence :

```text
scale = DeviceDpi / 96f
```

Exemples de paliers Windows :
- `96 DPI` -> `1.00`
- `120 DPI` -> `1.25`
- `128 DPI` -> `1.33`
- `144 DPI` -> `1.50`
- `168 DPI` -> `1.75`

Point critique :
- ne jamais appliquer deux fois la même logique de scaling.

La séparation validée est la suivante :
- géométrie de construction : tailles logiques, métriques de référence, structure du layout ;
- géométrie runtime : adaptation DPI appliquée une seule fois par la stratégie retenue.

## 4. Infrastructure UI partagée concernée

Le dépôt actif centralise aujourd’hui les règles de géométrie et de layout principalement via :
- `src/FrameShift/Windows/Helpers/FrameShiftUiMetrics.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftUiLayout.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftUiFactory.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftEditorShellUi.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftCropEditorUi.cs`
- `src/FrameShift/Windows/Helpers/FrameShiftWindowChrome.cs`

Rôles documentés :
- `FrameShiftUiMetrics.cs` : constantes communes de padding, gaps, hauteurs, largeurs de rail et dimensions de boutons ;
- `FrameShiftUiLayout.cs` : positionnement réutilisable des footers, sections titrées et rangées de boutons ;
- `FrameShiftUiFactory.cs` : construction cohérente des headers, sections, cartes d’info, champs et boutons ;
- `FrameShiftEditorShellUi.cs` : shell standard des écrans à workspace + rail latéral ;
- `FrameShiftCropEditorUi.cs` : shell partagé de la famille `crop`, avec grille explicite et footer stable ;
- `FrameShiftWindowChrome.cs` : application centralisée de la chrome de fenêtre et des icônes.

Règles observées dans l’implémentation actuelle :
- les dialogues et pickers standard utilisent largement `AutoScaleMode = AutoScaleMode.Dpi` ;
- les écrans riches s’appuient sur des `TableLayoutPanel` avec `RowStyle`/`ColumnStyle` explicites ;
- les fenêtres redimensionnables importantes définissent un `MinimumSize` ;
- les labels sensibles à la largeur utilisent `AutoEllipsis` quand une ligne doit rester compacte.

Note d’état :
- le dépôt actif ne contient pas de fichier `FrameShiftDpi.cs` distinct à la date de cette mise à jour ;
- la centralisation DPI visible dans le code actif passe aujourd’hui par la combinaison métriques partagées + layouts partagés + contraintes de fenêtre.

## 5. Fenêtres validées dans le chantier

Fenêtres explicitement mentionnées dans la validation :
- `Resize Image`
- `Compress Image`
- `Crop Image`
- `Crop Video`
- `Remove Object`
- `Separate Audio`
- `RIFE Picker`

Objectifs de validation :
- absence de texte coupé ;
- absence de chevauchement ;
- visibilité des boutons ;
- cohérence des espacements ;
- stabilité des layouts.

## 6. Règles futures obligatoires

Pour toute nouvelle fenêtre DPI-safe :
- partir des helpers UI partagés avant d’introduire une géométrie locale ;
- réutiliser `FrameShiftUiMetrics` pour les marges, hauteurs et gaps standard ;
- définir une grille explicite pour les zones critiques ;
- éviter les tailles fixes non justifiées ;
- utiliser `MinimumSize` quand la fenêtre est redimensionnable ou dense ;
- traiter les labels longs volontairement ;
- éviter les doubles stratégies de scaling ;
- vérifier les layouts sur les paliers Windows validés.

Pour les `TableLayoutPanel` :
- réserver une ligne fixe aux éléments qui doivent rester visibles ;
- éviter les lignes `Percent` concurrentes qui écrasent un footer ou une toolbar ;
- mesurer explicitement les blocs dynamiques quand `AutoSize` n’est pas suffisant ;
- neutraliser les `Margin` implicites qui perturbent l’alignement.

Pour les marges et espacements :
- garder `OuterPadding`, `BlockGap`, `LineGap` et les paddings standards comme références ;
- corriger la structure avant d’ajouter des offsets locaux arbitraires ;
- conserver une géométrie lisible à la taille minimale.

## 7. Résultat attendu à conserver

L’objectif n’est pas de créer une infrastructure complexe.

L’objectif est :
- une UI WinForms simple ;
- cohérente ;
- stable ;
- lisible sous les facteurs de scaling Windows réellement utilisés ;
- maintenable sans multiplication de correctifs locaux.

Règle finale :
- la meilleure solution DPI reste la plus simple qui protège durablement la lisibilité et la stabilité des fenêtres.
