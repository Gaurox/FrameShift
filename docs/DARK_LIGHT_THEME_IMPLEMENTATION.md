# FrameShift Dark / Light Theme Implementation

## Statut et objectif

Cette notice fige le périmètre de la première implémentation clair/sombre de FrameShift.

Elle complète `PROJECT_RULES.md`, `ARCHITECTURE_FREEZE.md`, `UI_STANDARDIZATION.md` et `UI_DPI_AUDIT.md`. En cas de divergence, ces documents de référence restent prioritaires.

Objectif :
- proposer `Système`, `Clair` et `Sombre` dans les paramètres ;
- utiliser `Système` par défaut ;
- appliquer une apparence cohérente à toutes les fenêtres WinForms appartenant à FrameShift ;
- conserver l'architecture, les layouts, les métriques DPI et l'identité visuelle existants.

Ce chantier reste strictement UI. Il ne modifie ni le core média, ni FFmpeg/FFprobe, ni les actions, ni la file, ni l'installateur.

## Périmètre retenu

Sont inclus :
- `MainForm`, `SettingsForm` et les deux `UserControl` de la fenêtre principale ;
- toutes les fenêtres d'action, pickers et éditeurs ;
- les fenêtres IA et de téléchargement ;
- `ProgressForm` ;
- les headers, sections, cartes, champs, boutons, menus contextuels et grilles appartenant à FrameShift ;
- la barre de titre Windows, en best effort selon la version de Windows.

Le thème change uniquement l'apparence. Il ne doit pas modifier :
- la géométrie des fenêtres ;
- les marges, gaps, rayons et tailles définis dans `FrameShiftUiMetrics` ;
- la stratégie DPI existante ;
- la hiérarchie bandeau / contenu / aide / footer ;
- le comportement fonctionnel des contrôles.

## Décisions techniques figées

### Choix utilisateur

La préférence comporte exactement trois valeurs :
- `System` : suit le thème actuel des applications Windows lors de l'ouverture de FrameShift ou du choix dans les paramètres ;
- `Light` : force la palette claire ;
- `Dark` : force la palette sombre.

Une préférence absente, inconnue ou illisible revient à `System`. Si le thème Windows ne peut pas être déterminé, le repli est `Light`.

Le changement s'applique immédiatement aux fenêtres FrameShift déjà ouvertes. Le suivi continu d'une modification du thème Windows après ce choix n'est pas requis.

### Persistance

La préférence UI est enregistrée séparément dans :

```text
%LOCALAPPDATA%\FrameShift\config\ui-settings.json
```

Format minimal attendu :

```json
{
  "Theme": "System"
}
```

Elle ne doit pas être ajoutée au `settings.json` de `AiModelSettings`, car ce fichier est aussi écrit par l'installateur pour le dossier des modèles IA.

Le chargement et l'enregistrement doivent être tolérants : aucune erreur de préférence UI ne doit empêcher le démarrage de FrameShift.

### Résolution du thème

Le thème effectif est résolu après `ApplicationConfiguration.Initialize()` et avant la création de la première fenêtre. Il est résolu à nouveau lorsque l'utilisateur choisit une préférence dans les paramètres.

La détection Windows reste locale, simple et sans nouvelle infrastructure : lecture de `AppsUseLightTheme` sous `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, avec repli clair en cas d'absence ou d'erreur.

Chaque nouveau processus FrameShift relit la préférence. Aucune synchronisation interprocessus n'est ajoutée.

### Palette

`FrameShiftTheme` reste l'unique source des couleurs UI. Il expose la palette effective claire ou sombre ; les fenêtres ne choisissent jamais elles-mêmes un thème.

Règles :
- `PrimaryBlue` reste `#8EBAF3` ;
- `SecondaryBlue` reste `#4D79B4` ;
- aucune nouvelle couleur d'accent n'est introduite ;
- seuls les neutres reçoivent des équivalents sombres : page, surface, bordure, textes, surface accentuée et hover ;
- les couleurs sombres exactes sont définies uniquement dans `FrameShiftTheme` et validées par contraste avant généralisation ;
- en mode sombre, `PrimaryBlue` peut remplacer `SecondaryBlue` pour un petit texte accentué si le contraste de ce dernier est insuffisant ; aucun remplacement local arbitraire n'est autorisé.

Les helpers existants restent les points d'entrée :
- `FrameShiftUiFactory` pour les contrôles standards ;
- `FrameShiftUiPainter` pour les bordures et dessins UI ;
- `FrameShiftWindowChrome` pour le titre, l'icône et la barre de titre Windows ;
- les shells et layouts existants pour la géométrie.

Il ne faut pas créer de framework de thèmes, de base `Form` générale, de conteneur d'injection ou d'interface appliquée à toutes les fenêtres.

### Contrôles natifs

Le traitement reste ciblé :
- appliquer explicitement fond, texte, sélection et bordures lorsque WinForms les expose ;
- utiliser un renderer clair/sombre commun pour les `ContextMenuStrip` existants ;
- conserver le rendu Windows natif des `ComboBox`, `CheckBox`, `RadioButton`, `TrackBar`, `ProgressBar`, `NumericUpDown` et scrollbars tant qu'il reste lisible ;
- utiliser un petit wrapper ou contrôle custom seulement si un contrôle précis reste réellement illisible après validation.

Il est interdit de remplacer en bloc tous les contrôles natifs.

### Barre de titre

`FrameShiftWindowChrome.Apply(...)` reste le point central. Il applique la préférence sombre à la barre de titre standard via DWM quand Windows le permet, et ignore proprement l'échec sinon.

Aucune barre de titre custom n'est créée. `ProgressForm` doit utiliser le même chemin commun que les autres fenêtres lors de l'implémentation.

### Dessins et aperçus

Les couleurs qui représentent l'interface doivent venir de `FrameShiftTheme` au moment de construire ou dessiner le contrôle.

Les couleurs qui représentent le média ou le document ne changent pas avec le thème, notamment :
- fond noir des previews vidéo et image ;
- page PDF blanche ;
- masque de crop et poignées de sélection ;
- couleurs de sous-titres choisies par l'utilisateur ;
- guides et overlays dont la couleur a une signification fonctionnelle.

## Exclusions explicites

La première version n'inclut pas :
- migration vers .NET 9/10, WPF, WinUI ou une autre UI ;
- `Application.SetColorMode`, indisponible dans la stack .NET 8 figée ;
- suivi en direct d'un changement Windows après l'application de la préférence ;
- synchronisation du thème entre processus déjà ouverts ;
- remplacement des `MessageBox`, `OpenFileDialog`, `FolderBrowserDialog`, `ColorDialog` ou `PrintDialog` ;
- garantie que les dialogues appartenant à Windows suivent un thème forcé différent de celui de Windows ;
- refonte graphique, nouvelles animations, nouvelles icônes ou nouveaux layouts ;
- prise en charge complète supplémentaire du mode contraste élevé.

## Risques principaux

- certains contrôles WinForms natifs peuvent conserver des zones claires en mode sombre ;
- `ContextMenuStrip`, `DataGridView`, contrôles désactivés, focus clavier et sélection demandent une vérification spécifique ;
- les couleurs mémorisées dans des champs statiques, notamment dans `ProgressForm`, peuvent figer la mauvaise palette si elles sont initialisées trop tôt ;
- les handlers `Paint` et couleurs codées en dur peuvent mélanger couleur UI et couleur de contenu ;
- le bleu secondaire fixe peut manquer de contraste comme petit texte sur certaines surfaces sombres ;
- l'attribut DWM de barre de titre ne réagit pas de façon identique sur toutes les versions prises en charge de Windows 10/11 ;
- toute modification involontaire de taille ou de padding peut réintroduire une régression DPI.

La réponse attendue à ces risques est un correctif local et lisible ou une adaptation du helper partagé concerné, jamais une nouvelle couche générique.

## Ordre d'implémentation recommandé

1. Ajouter la préférence UI et la détection Windows.
2. Résoudre le thème avant la première fenêtre.
3. Étendre `FrameShiftTheme` avec les neutres sombres.
4. Adapter `FrameShiftUiFactory`, `FrameShiftUiPainter` et `FrameShiftWindowChrome`.
5. Ajouter le choix d'apparence dans `SettingsForm` avec application immédiate de la palette.
6. Corriger `ProgressForm`, les deux `UserControl`, les menus et les grilles.
7. Auditer les formulaires simples, puis les éditeurs riches et leurs dessins personnalisés.
8. Exécuter la validation complète avant de considérer le chantier terminé.

## Critères de validation

Le chantier est validé uniquement si :
- une installation sans préférence choisit correctement clair ou sombre selon Windows au démarrage ;
- les choix `Light` et `Dark` persistent après redémarrage et s'appliquent immédiatement ;
- un fichier absent, invalide ou contenant une valeur inconnue revient sans crash à `System` ;
- les fenêtres FrameShift ont un fond, des surfaces, textes, bordures, boutons, sélections et états désactivés lisibles dans les deux thèmes ;
- chaque menu contextuel est ouvert et vérifié ;
- les grilles, champs, focus clavier, hover et pressions restent lisibles ;
- les previews, pages PDF, crops, overlays et couleurs utilisateur ne sont pas altérés ;
- la barre de titre reste correcte sur Windows 10 et Windows 11, avec repli sans erreur ;
- aucun contrôle ne se chevauche et aucun texte n'est tronqué à `125 %`, `133 %`, `150 %` et `175 %` ;
- les fenêtres denses de référence (`Progress`, `Image to PDF`, `Crop`, `Cut`, `Create GIF`, `Remove Object`, sous-titres) sont vérifiées dans les deux thèmes ;
- les chemins fonctionnels et le traitement média restent inchangés ;
- les tests existants passent et `dotnet build src/FrameShift/FrameShift.csproj` est vert.

Les dialogues appartenant à Windows peuvent rester dans le thème Windows courant : cela ne constitue pas un échec de validation.

## Phase 4 — validation visuelle et corrections ciblées

Validation effectuée en lançant l'exécutable WinForms avec la préférence `Dark` forcée dans un profil local temporaire, avec des fichiers de test dont les chemins contiennent des espaces et un caractère accentué. La préférence temporaire a été retirée après chaque parcours.

### Écrans validés visuellement

- `MainForm` : surfaces, textes, boutons, zones vides et barre de titre DWM sombre ;
- `SettingsForm` : choix `Dark`, application immédiate et bouton de fermeture ;
- `ImageToPdfForm` : page PDF blanche, règles, canvas, poignées, actions et champs désactivés ;
- `CropImageForm` : canvas, masque et poignées de crop ;
- `CutVideoForm`, `CutAudioForm` et `CreateGifForm` : previews, curseurs et champs ;
- `RemoveObjectEditorForm` : preview/canvas, masque, slider et actions IA ;
- `AddSubtitlesToVideoPickerForm` puis `AddSubtitlesToVideoBurnEditorForm` : navigation picker, preview et couleurs de sous-titres.

Les previews vidéo noires, la page PDF blanche, les couleurs de média, les masques et les couleurs choisies pour les sous-titres ont été conservés tels quels.

### Défaut corrigé

Dans `ImageToPdfForm`, les tuiles d'ordre désactivées (`To back`, `Back 1`, `Front 1`, `To front`) recevaient la couleur système `GrayText` de WinForms, presque noire lorsque l'application est en sombre. Le texte de leur seule zone textuelle est désormais redessiné avec `FrameShiftTheme.TextSecondary`. Les boutons conservent leur état natif désactivé, leurs icônes et leur géométrie.

### Limites WinForms / Windows constatées

- Certains `ComboBox` natifs gardent une zone d'édition blanche malgré `BackColor` en thème sombre. Le texte noir sur blanc reste lisible ; aucun owner-draw ni remplacement global n'est justifié.
- Les boîtes de dialogue système, les menus Windows et le rendu DWM exact restent dépendants de la version de Windows et de son propre thème. `FrameShiftWindowChrome` conserve son repli sans échec lorsque l'attribut DWM n'est pas disponible.
- La session de validation disponible était à 96 DPI. Modifier l'échelle d'affichage de Windows pour simuler les autres paliers aurait modifié la session utilisateur ; les validations de `125 %`, `133 %`, `150 %` et `175 %` restent donc manuelles.

### Vérifications manuelles restantes

- ouvrir chaque `ContextMenuStrip` et chaque sous-menu en clair et sombre, notamment depuis la file de fichiers ;
- vérifier `ProgressForm` pendant une opération assez longue : états en attente, actif, erreur, annulation et boutons désactivés ;
- revoir `SettingsForm` après sélection de `System` et `Light`, puis bascule réelle sous les deux thèmes Windows ;
- exécuter la matrice DPI `125 %`, `133 %`, `150 %`, `175 %` sur Windows 10 et Windows 11, avec focus clavier, hover, scrollbars, `NumericUpDown`, `ComboBox` et texte désactivé ;
- contrôler les dialogues système natifs sur les versions de Windows effectivement prises en charge.

## Phase 5 — clôture release 1.17.0

Le chantier clair/sombre est clôturé pour la préparation locale de `1.17.0` : le scan final des couleurs explicites de l'UI ne révèle pas de neutre clair oublié. Les couleurs volontairement conservées correspondent aux previews média, pages PDF, transparence, masks/crops, poignées, états d'erreur ou couleurs choisies par l'utilisateur.

La préférence utilisateur est documentée dans le README et le guide produit : `System` (défaut, thème actuel des applications Windows), `Light` et `Dark`, persistés dans `ui-settings.json` et appliqués immédiatement aux fenêtres ouvertes. Aucun contrôle natif lisible n'a été remplacé.

La release locale doit toutefois rester soumise aux vérifications manuelles de la phase 4, notamment la matrice DPI, les menus contextuels, les états longs de `ProgressForm` et les différences Windows 10 / Windows 11 avant toute publication externe.
