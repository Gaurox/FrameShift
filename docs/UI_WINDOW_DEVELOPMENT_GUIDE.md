# Développer une fenêtre FrameShift

Guide pratique du 3 octobre 2026, vérifié contre le socle distribué en **1.20.0**. À suivre pour toute nouvelle fenêtre, picker, variante ou modification de composition.

## 1. Références à lire

1. [Règles projet](PROJECT_RULES.md) et [architecture figée](ARCHITECTURE_FREEZE.md) : WinForms/.NET 8, séparation Windows/Core, runners et annulation.
2. [Contrat du socle UI](UI_FOUNDATION.md) : API actives, propriété des composants et **table unique des métriques figées**.
3. [Standard visuel](UI_STANDARDIZATION.md) : présentation, ergonomie et variantes acceptées.
4. Ce guide : construction et recette d'une nouvelle fenêtre.

Les anciens plans décrivent le besoin métier ou l'historique ; ils ne remplacent pas ce contrat. En cas d'écart avec le code, corriger la documentation ou faire décider explicitement d'une évolution du socle. Ne pas créer une variante locale pour contourner le standard.

## 2. Choisir la composition

Réutiliser d'abord un picker existant lorsque les réglages sont les mêmes. Une nouvelle fenêtre reste un `Form` simple composé de contrôles ; aucune nouvelle base générale, injection ou couche de présentation n'est nécessaire.

| Besoin | Composition | Exemple actif |
|---|---|---|
| Réglages sans grand aperçu | `FrameShiftDialogLayout.Create(header, content, actions, status)` | [Interpolate Video](../src/FrameShift/Windows/Forms/InterpolateVideoForm.cs) |
| Grand aperçu et options à côté | `FrameShiftEditorShellUi.Create(header, workspace, actions, options: options, status: status)` | [Crop Image](../src/FrameShift/Windows/Forms/CropImageForm.cs) |
| Sélection temporelle sous l'aperçu | `FrameShiftEditorShellUi.CreateTimeline(header, preview, selection, actions)` | [Cut Video](../src/FrameShift/Windows/Forms/CutVideoForm.cs) |
| Éditeur de recadrage | `FrameShiftCropEditorUi.Create(header, preview, options, cancel, primary, status)` | [Crop Video](../src/FrameShift/Windows/Forms/CropVideoForm.cs) |
| Texte long ou file occupant l'espace restant | `CreateSection(..., fill: true)` dans `FrameShiftDialogLayout.CreateShell` | [Media Info](../src/FrameShift/Windows/Forms/MediaInfoForm.cs), [Progress](../src/FrameShift/Windows/ProgressUI/ProgressForm.Details.cs) |

Le compact fait défiler son corps. L'éditeur fait défiler ses options indépendamment ; son rail passe sous l'aperçu en largeur réduite. La variante temporelle conserve champs et curseur sous l'aperçu. Tous gardent le bandeau et les commandes hors du défilement du corps. Un workspace spécialisé prend en charge son propre manque de place.

## 3. Construire un dialogue compact

Exemple compilable **dans le projet applicatif**, sous `Windows/Forms`. Les dimensions initiales/minimales ci-dessous illustrent ce dialogue ; elles ne prescrivent pas la taille de toutes les fenêtres. `FitInitialHeight` est un helper interne à cet assembly.

```csharp
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

internal sealed class ExampleOptionsForm : Form
{
    public int SelectedValue { get; private set; }

    public ExampleOptionsForm(string inputPath, string functionIconPath)
    {
        SuspendLayout();
        // Must precede adding controls or creating a window handle.
        FrameShiftWindowPolicy.Initialize(this, new Size(560, 410), new Size(360, 280));
        const string title = "FrameShift - Example Options";
        FrameShiftWindowChrome.Apply(this, title);
        var header = FrameShiftUiFactory.CreateHeader(title,
            $"Source: {Path.GetFileName(inputPath)}", functionIconPath, IconPaths.AppIcon, "▶");

        var value = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 50 };
        var content = FrameShiftUiFactory.CreateSection("Options",
            FrameShiftUiFactory.CreateVerticalStack(
                FrameShiftUiFactory.CreateWrappingLabel("Choose a value for this operation."),
                FrameShiftUiFactory.CreateFieldRow("&Value", value, "%", logicalEditorWidth: 140)));

        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.DialogResult = DialogResult.Cancel;
        var primary = FrameShiftUiFactory.CreateMeasuredActionButton("Apply", true);
        primary.Click += (_, _) =>
        {
            // Capture validated settings; the caller runs the Core action.
            SelectedValue = decimal.ToInt32(value.Value);
            DialogResult = DialogResult.OK;
            Close();
        };
        var root = FrameShiftDialogLayout.Create(header, content,
            FrameShiftDialogLayout.CreateActions(cancel, primary),
            FrameShiftUiFactory.CreateStatusMessage("Creates a new file next to the source."));
        Controls.Add(root);
        Load += (_, _) =>
        {
            if (WindowState == FormWindowState.Normal)
                FrameShiftDialogLayout.FitInitialHeight(this, root);
        };
        AcceptButton = primary;
        CancelButton = cancel;
        ResumeLayout(true);
    }
}
```

Ouvrir le picker avec un propriétaire et libérer le formulaire après `ShowDialog`, par exemple avec `using var dialog`. Le lancement du traitement et ses paramètres restent dans le flux existant de l'action ; aucune commande FFmpeg n'est exécutée dans ce constructeur.

### Taille à l'ouverture

- Choisir une largeur logique adaptée aux libellés, champs et presets ; ne pas imposer un format de fenêtre unique à tous les métiers.
- Pour un compact, appeler `FitInitialHeight` à `Load`, après la mise à l'échelle native : toutes les options visibles tiennent sans vide inutile si l'espace de travail le permet. Le corps défile sinon.
- Ne pas réserver de hauteur aux sections masquées ni ajouter de spacer pour pousser les commandes vers le bas.
- Lors d'un changement explicite de format/options, un compact peut refaire cette mesure après mise à jour de `Visible`, uniquement après chargement et si la fenêtre est normale. Borner alors sa position à l'espace de travail comme [Create Subtitles](../src/FrameShift/Windows/AI/CreateSubtitlesPickerForm.cs).
- Ne pas rappeler ce calcul depuis `Resize`/`Layout`. Le redimensionnement choisi par l'utilisateur doit rester stable.
- Un éditeur conserve une hauteur initiale utile à l'aperçu ; ne pas lui appliquer automatiquement le calcul de hauteur du compact. La politique commune borne tailles et minima au moniteur courant.

## 4. Construire un éditeur avec rail

Ce second exemple montre la composition uniquement. Son panneau vide sera remplacé par le canevas métier et son chargement asynchrone. Il ne décode aucun média et ne démarre aucun traitement.

```csharp
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

internal sealed class ExampleEditorForm : Form
{
    public ExampleEditorForm(string inputPath, string functionIconPath)
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(1000, 660), new Size(400, 300));
        const string title = "FrameShift - Example Editor";
        FrameShiftWindowChrome.Apply(this, title);
        var header = FrameShiftUiFactory.CreateHeader(title,
            $"Source: {Path.GetFileName(inputPath)}", functionIconPath, IconPaths.AppIcon, "▶");
        var preview = FrameShiftCropEditorUi.CreatePreviewPanel();
        preview.AccessibleName = "Media preview";
        var options = FrameShiftUiFactory.CreateSection("Options",
            FrameShiftUiFactory.CreateVerticalStack(
                FrameShiftUiFactory.CreateFieldRow("&Amount",
                    new NumericUpDown { Minimum = 0, Maximum = 100, Value = 50 },
                    logicalEditorWidth: 128)));
        var close = FrameShiftUiFactory.CreateMeasuredActionButton("Close", false);
        close.DialogResult = DialogResult.Cancel;
        Controls.Add(FrameShiftEditorShellUi.Create(header, preview,
            FrameShiftDialogLayout.CreateActions(close), options: options,
            status: FrameShiftUiFactory.CreateStatusMessage("Preview layout example.")));
        AcceptButton = close;
        CancelButton = close;
        ResumeLayout(true);
    }
}
```

La largeur de rail par défaut provient de `FrameShiftUiMetrics.EditorRailWidth` (258 unités logiques). Un outil riche peut fournir `logicalRailWidth` avec une justification fonctionnelle. Le shell effectue le repli ; ne pas déplacer soi-même le rail à chaque `Resize`.

Pour un outil temporel, remplacer la création du shell par `CreateTimeline(header, preview, selection, actions)`. `selection` est une composition AutoSize des champs/curseurs et des informations utiles. Le shell plafonne son viewport à 60 % du corps ; les contrôles défilent si nécessaire. Ce ratio est géré par le composant.

## 5. Respecter la géométrie et le DPI

La [table du contrat](UI_FOUNDATION.md#contrat-de-géométrie-figé--29-septembre-2026) est l'unique référence des dimensions communes ; [FrameShiftUiMetrics.cs](../src/FrameShift/Windows/Helpers/FrameShiftUiMetrics.cs) en est la source de code.

- Initialiser la politique une seule fois, sous `SuspendLayout`, avant contrôles/handles. Ne pas réassigner ensuite `AutoScaleMode`, `AutoScaleDimensions`, `Font` ou les minima pour refaire sa politique.
- L'application configure `PerMonitorV2` au démarrage, en Debug **et** Release. Ne pas changer le mode global depuis un formulaire.
- Utiliser `CreateHeader`, `CreateSection`, `CreateVerticalStack`, `CreateFieldRow` et `CreateActions`. Ces composants possèdent leurs marges/paddings ; ne pas les doubler dans leurs parents.
- Laisser les rangées de contenu en AutoSize et les zones de workspace en Fill/Percent. Une section `fill: true` est réservée à un contenu extensible, pas à une liste d'options qui doit annoncer sa hauteur préférée.
- **Footer :** `CreateMeasuredActionButton` sans largeur locale, puis `CreateActions`. Validation, Cancel et Close ont une base commune 140 × 34 ; les boutons d'un même footer grandissent ensemble si les textes l'exigent, et se replient si nécessaire. Aucun `Width`/`Height`/`MaximumSize` local ne doit annuler cette mesure.
- Les boutons d'outils/presets peuvent recevoir une largeur compacte justifiée ; leur espacement vient de `CreateChoiceRow`. Ne pas les confondre avec les commandes du footer.
- Une saisie courte utilise `logicalEditorWidth`. Pour une grille de valeurs comparables, partager colonnes et largeur, comme [Resize](../src/FrameShift/Windows/Forms/ResizeMediaFormBase.cs). Plusieurs `CreateFieldRow` indépendants ne garantissent pas l'alignement de labels de longueurs différentes.
- Si un calcul de dessin/colonne est nécessaire, convertir **la constante logique** avec `FrameShiftUiMetrics.ToPixels(control, value)` au DPI du contrôle. Recalculer depuis cette référence lors d'un changement DPI, jamais depuis la précédente largeur déjà convertie.
- `Bounds`, `ClientSize` et les mesures natives de texte sont déjà en pixels physiques. Aucun second facteur DPI, `Scale()` récursif ou multiplication manuelle de taille de police.
- Dessin et hit-tests emploient le même mapping. Frames/secondes, pixels source, masque et unités de page restent indépendants du DPI ; déplacer la fenêtre ne change aucun réglage média.

## 6. Bandeau, choix, textes et couleurs

- Titre Windows et bandeau : `FrameShift - <Function>`. `FrameShiftWindowChrome` possède l'icône Windows chargée ; `CreateHeader` possède ses ressources de bandeau. Ne pas recréer leurs polices/icônes localement.
- Pour une fenêtre IA, appeler `FrameShiftWindowChrome.Apply(this, title, IconPaths.FrameShiftAiIcon, IconPaths.AppIcon)` ; le bandeau reçoit l'icône dédiée à la fonction, avec fallback. Les ressources actives vont dans `src/FrameShift/Assets`, jamais dans `references/`.
- Donner le texte complet au bandeau : le composant gère ellipse, infobulle, consultation/copie et clavier. Ne pas tronquer les données avant de les lui transmettre.
- Préférer `RadioButton`, `CheckBox`, `ComboBox` en `DropDownList` et `NumericUpDown` natifs. Les radios exclusives partagent **le même parent direct**, sans panneau distinct pour chaque option.
- `CreateChoiceRow` replie les presets/cartes en espace réduit. Les cartes de compression et la liste radio verticale de Create Subtitles sont des variantes acceptées ; ne pas imposer des cartes à tous les pickers.
- Descriptions : `CreateWrappingLabel`. Information/erreur courte : `CreateStatusMessage`, sélectionnable/copiable et plafonné à 96 unités logiques. Si la lecture de détails est centrale, prévoir un texte natif multiligne plus haut, comme Progress/Download Model ; pas une erreur réduite à une ligne ou à un tooltip.
- Employer les **rôles** de `FrameShiftTheme`, pas des couleurs hexadécimales locales. Boutons principaux et leurs états viennent de la factory. Les petits textes utilisent les rôles lisibles, pas automatiquement les bleus d'identité.
- Les factories prennent la palette courante. Le changement de préférence remappe les fenêtres ouvertes du même processus. Pour du dessin personnalisé, lire la palette au moment de peindre ; ne pas conserver un pinceau de l'ancien thème.
- Les pixels des médias, pages PDF et couleurs choisies par l'utilisateur gardent leur sens. Consulter la [notice thème](DARK_LIGHT_THEME_IMPLEMENTATION.md) pour les limites System, grilles et contrôles natifs.

## 7. Clavier, état et travail asynchrone

- Ordonner Tab/Shift+Tab selon la lecture : bandeau, corps, statut, commandes. Nommer les saisies, canevas et commandes sans texte via `AccessibleName` ; choisir des mnémoniques distinctes pour les labels.
- Relier `AcceptButton` et `CancelButton`. Enter confirme seulement un état valide ; Échap/Cancel/croix respectent la même politique de fermeture et d'annulation. Désactiver la validation pendant un chargement indispensable.
- Garder flèches, Enter/Espace et raccourcis locaux au contrôle concerné ; une timeline ne doit pas déclencher l'export en sélectionnant un élément. Préserver le focus si des options/actions sont reconstruites.
- Aucun `.Wait()`, `.Result` ou `.GetAwaiter().GetResult()` sur un chemin UI d'aperçu. Chargement/décodage après construction, asynchrone, annulable ; FFmpeg/FFprobe passent par leurs runners du Core.
- Reprendre la durée de vie de l'éditeur le plus proche : [EditorPreviewLifetime](../src/FrameShift/Windows/Helpers/EditorPreviewLifetime.cs), [Cut Video Preview](../src/FrameShift/Windows/Forms/CutVideoForm.Preview.cs) ou le helper IA déjà employé. `Dispose()` seul du helper ne remplace pas l'attente explicite de sa tâche à la fermeture.
- Après un `FormClosing` annulé pour attendre une tâche, poster la fermeture finale par `BeginInvoke`, avec garde contre les appels répétés et restauration de `DialogResult` ; voir [Crop Video](../src/FrameShift/Windows/Forms/CropVideoForm.cs). Un `await` déjà terminé peut reprendre immédiatement et rendre un `Close()` direct réentrant.
- Un résultat tardif ne doit pas toucher un formulaire fermé ; libérer les bitmaps non transférés. Chaque image, flux, timer, police, tooltip ou abonnement créé a un propriétaire. Libérer l'ancien au remplacement et les contrôles retirés lors d'une reconstruction ; ne pas disposer une ressource empruntée.

## 8. Vérifier avant de livrer

Commencer par le design à 100 %, puis tester la **nouvelle fenêtre réelle**. La validation des fenêtres de 1.20.0 ne valide pas automatiquement les suivantes.

### Commandes de développement

Depuis la racine du dépôt :

```powershell
dotnet build src/FrameShift/FrameShift.csproj
dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj --filter FullyQualifiedName~UiFoundationTests
dotnet build tests/FrameShift.UiSamples/FrameShift.UiSamples.csproj
New-Item -ItemType Directory -Force scratch/ui-validation | Out-Null
dotnet tests/FrameShift.UiSamples/bin/Debug/net8.0-windows/FrameShift.UiSamples.dll --check scratch/ui-validation/samples-check.json
```

Le filtre et `--check` contrôlent le socle existant ; ils ne découvrent pas automatiquement un nouveau formulaire. Ajouter des vérifications ciblées utiles pour ses variantes et ses risques, en reprenant `StaTest` et `[Collection(WinFormsTestCollection.Name)]` des [tests UI](../tests/FrameShift.Tests/UiFoundationTests.cs). Les tests WinForms et de préférences partagent l'état du processus et leurs dossiers temporaires ; conserver leur isolation.

`--check` crée des handles **sans affichage**, sans capture du bureau et sans modifier les réglages Windows. Les tests de texte agrandi sont des tests de mesure ; ils ne certifient pas un changement DPI réel. La suite complète comprend des tests avec affichage : prévoir un environnement Windows de test approprié. Pour une distribution, la [chaîne canonique](RELEASE_CHECKLIST.md) exécute la suite Release complète, sans exclusions de confort.

### Checklist de recette de la nouvelle fenêtre

- [ ] Ouverture à 100 % : contenu complet si l'écran le permet, aucun vide réservé aux options masquées, bandeau et commandes standards.
- [ ] 100/150/200/300 % : taille normale, maximisée et espace réduit ; rien de coupé ou superposé ; seuls les contenus prévus défilent ; footer atteignable.
- [ ] Déplacement entre écrans de DPI différents, aller/retour sans dérive ; taille du texte Windows indépendante ; aucun réglage média modifié.
- [ ] Noms/chemins longs avec espaces et accents, titres longs, messages/erreurs longs lisibles et copiables ; options ajoutées/retirées après affichage et changement DPI.
- [ ] Champs comparables alignés ; contrôles optionnels cochables ; presets et radios cohérents ; Tab/Shift+Tab, focus, raccourcis, Enter/Échap/croix.
- [ ] Clair/sombre/System, focus, survol, appui, sélection, état désactivé et erreur ; valeurs utiles lisibles, médias conservés.
- [ ] Aperçu lent, changements rapides puis fermeture/annulation ; pas de gel, retour tardif, fuite de ressources ou processus orphelin ; réouvertures répétées.
- [ ] Si traitement : export réel, sortie adjacente unique, source préservée, nettoyage sur erreur/annulation, logs lisibles et absence de console.
- [ ] Relevé daté : fonction/variante, binaire ou commit, résolution, échelle, taille du texte, thème, écran(s), scénario et résultat. Marquer explicitement les cas non testés ; confirmation texte suffisante, captures facultatives.

Ajouter le formulaire aux exemples/recettes pertinents et à [CODE_FILE_INDEX](CODE_FILE_INDEX.md). Si une règle commune évolue, ajuster ses composants, `UI_FOUNDATION` et `UI_STANDARDIZATION` ensemble, puis rejouer les fenêtres représentatives affectées. La [recette G](UI_PHASE_G_MANUAL_TESTS.md) concerne ensuite le binaire installé.
