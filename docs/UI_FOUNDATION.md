# Socle UI — implémentation B

État du 28 septembre 2026 : **B validée sur le périmètre du socle**. Builds et tests automatisés verts; huit captures manuelles des structures compacte et éditeur reçues à 100/150/200/300 %, puis validation explicite de tous les tests manuels par l'utilisateur, y compris défilement, contenu dynamique, redimensionnement, clavier, transitions entre écrans et texte Windows agrandi. Le socle est prêt pour C; C n'est pas commencée. Les captures Image to PDF concernent la fonction installée historique, dont la migration est prévue en E. La [feuille de route](UI_AUDIT_AND_STANDARDIZATION_PLAN_2026-09-28.md) détaille les preuves et les limites conservées.

## Construction des prochains pilotes

Choix retenu : composition de contrôles WinForms; aucune nouvelle base de formulaire. Réévaluer ce choix au bilan de C seulement si les pilotes démontrent un besoin.

```csharp
SuspendLayout();
FrameShiftWindowPolicy.Initialize(this, new Size(680, 540), new Size(400, 300));
FrameShiftWindowChrome.Apply(this, "FrameShift - Function");

var header = FrameShiftUiFactory.CreateHeader(
    "FrameShift - Function", sourceDescription, iconPath, IconPaths.AppIcon, "▶");
var fields = new TableLayoutPanel
{
    AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
    ColumnCount = 1, Dock = DockStyle.Top, Margin = Padding.Empty
};
fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
fields.Controls.Add(FrameShiftUiFactory.CreateFieldRow("&Value", new TextBox()), 0, 0);
var section = FrameShiftUiFactory.CreateSection("Options", fields);
var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
var primary = FrameShiftUiFactory.CreateMeasuredActionButton("Start", true);
cancel.DialogResult = DialogResult.Cancel;
CancelButton = cancel;
AcceptButton = primary;
// Connect validation and cancellation to the action, as before.
Controls.Add(FrameShiftDialogLayout.Create(header, section,
    FrameShiftDialogLayout.CreateActions(cancel, primary)));
ResumeLayout(true);
```

Pour un éditeur, utiliser `FrameShiftEditorShellUi.Create(header, workspace, actions, options, status)`. Le rail est facultatif et défilant; il passe sous l'aperçu lorsque la largeur disponible ne permet plus les deux colonnes. `FrameShiftCropEditorUi.Create` compose ce même shell. Le dessin du média et ses coordonnées restent à la charge de l'éditeur.

## Contrats des composants

| Composant | Contrat |
|---|---|
| `FrameShiftWindowPolicy` | Appel unique sous `SuspendLayout`, avant contrôles/handles. Référence 96 DPI, `AutoScaleMode.Dpi`, Segoe UI 9 pt. Minima exprimés en taille **cliente** logique, reconstruits et bornés au moniteur courant à l'ouverture, en fin de déplacement/redimensionnement et après changement DPI. Aucun clamp pendant `LocationChanged`, qui empêcherait de changer d'écran. |
| `FrameShiftUiMetrics` | Constantes logiques 96 DPI; `ToPixels` réservé aux calculs manuels. Les tailles clientes, bornes et mesures de texte sont déjà en pixels. Aucun `Scale()` récursif supplémentaire ni multiplication manuelle des polices en production. |
| `CreateHeader` | Titre mesuré avec retour à la ligne; métadonnées sur une ligne avec infobulle et menu « Copy details ». Icône reconstruite depuis le fichier à la taille courante; ancien bitmap et ressources possédées libérés. |
| `CreateSection` | Titre et contenu dans des rangées AutoSize distinctes. Le contenu doit annoncer sa hauteur préférée, par exemple une table de champs AutoSize. |
| `CreateFieldRow` | Label natif avec mnémonique, nom accessible du champ, éditeur extensible, unité facultative. Le label long revient à la ligne et laisse de la place à l'éditeur. |
| `CreateMeasuredActionButton` / `CreateActions` | Taille d'après le texte et les minima logiques. Barre hors scroll; retour à la ligne des boutons et de leurs textes si nécessaire. Relier `AcceptButton`/`CancelButton` et leurs événements dans le formulaire. |
| `CreateStatusMessage` | Texte multiligne sélectionnable/copiable, lecture seule. Hauteur mesurée et plafonnée à 96 unités logiques, défilement vertical des détails longs pour préserver le footer. |
| Ajouts dynamiques | Ajouter dans les mêmes tables de champs/sections; laisser WinForms hériter de la police et du DPI. Les composants recalculent leurs mesures/paddings dans leur contexte courant. Pas de tailles calculées à partir des anciennes bornes. |

Les anciennes entrées `CreateFixed*`, `CreateFillHeader`, `CreateFillSection`, `CreateRootLayout`, `LayoutFooterButtons`, etc. restent temporairement compatibles avec leurs consommateurs. Elles ne constituent pas le contrat des prochains pilotes. Migration par C/D/E, suppression des chemins inutilisés en F. Ne pas remplacer aveuglément une section à coordonnées fixes par une section AutoSize.

`PerMonitorV2` est configuré dans le `.csproj` applicatif **uniquement pour Debug** et initialisé par `ApplicationConfiguration.Initialize()` avant les fenêtres. Release garde sa configuration antérieure. Le projet de démonstration utilise toujours `PerMonitorV2`; ce projet de test n'est pas distribué par l'installateur.

## Validation par commandes

```powershell
dotnet build src/FrameShift/FrameShift.csproj --no-restore
dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj --no-restore --filter FullyQualifiedName~UiFoundationTests
dotnet build tests/FrameShift.UiSamples/FrameShift.UiSamples.csproj --no-restore
dotnet tests/FrameShift.UiSamples/bin/Debug/net8.0-windows/FrameShift.UiSamples.dll --check scratch/phase-b/samples-check.json
```

Le dernier appel construit des handles natifs **sans afficher de fenêtre**; les erreurs sont écrites dans le fichier indiqué et signalées par un code de sortie non nul. Il ne capture aucun écran et ne change aucun réglage Windows. Créer le répertoire de résultats avant l'appel. Un premier build du projet de démonstration nécessite sa restauration NuGet; son lockfile est versionné.

Les tests couvrent les conversions 96/144/192/288 DPI, les textes agrandis ×1/1,5/2/3, les corps longs, les commandes longues, les messages longs, les champs dynamiques, les allers-retours de largeur et les limites du dessin. Les grossissements de police sont des tests de mesure, **pas une simulation validante des changements DPI Windows**.

La suite existante peut afficher des fenêtres. Le passage sans affichage de B utilise ce filtre :

```powershell
dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj --no-restore --filter "FullyQualifiedName!~ExplorerArrivals_BeforeAndAfterHandle&FullyQualifiedName!~AddVideosButton_AddsEveryPickerOccurrence&FullyQualifiedName!~ShownWindow_IsVisibleAndCanBeginClosing&FullyQualifiedName!~Closing_DoesNotBlockTheStaMessagePump&FullyQualifiedName!~RemoveNoisePickers_PreserveRequestedDialogResult"
```

Il exclut 12 cas UI existants, distincts des 5 tests média déjà ignorés. Les réexécuter ultérieurement en environnement de recette adapté.

## Recette manuelle de B — validée par l'utilisateur

Les manipulations ci-dessous restent disponibles pour rejouer la recette. La procédure complète transmise dans la conversation ajoutait trois créations de champs avec saisie, trois répétitions du titre long et du redimensionnement, Tab/Maj+Tab/flèches/Entrée/Échap, les allers-retours entre écrans et le texte Windows agrandi. L'utilisateur a répondu : « c'est ok. je valide tous les tests ». Cette déclaration complète les captures; elle ne qualifie pas les fonctions métier non migrées.

Sans commande à saisir, ouvrir dans l'Explorateur :

`E:\AI\FrameShift_V1\tests\FrameShift.UiSamples\bin\Debug\net8.0-windows\FrameShift.UiSamples.exe`

1. La fenêtre **UI test compact** s'ouvre. Vérifier titre, champs, défilement des options et boutons inférieurs. Le statut indique le DPI réellement reçu.
2. Cliquer **Test long caption**, puis le recliquer pour revenir. Défiler jusqu'à **Add a field**, ajouter un champ et vérifier qu'il reste accessible.
3. Cliquer **Open editor sample** au bas des options. Réduire sa largeur : les options doivent passer sous l'aperçu. Agrandir à nouveau : elles doivent revenir à droite, sans déplacer les commandes du bas hors fenêtre.
4. Effectuer ces essais à 100, 150, 200 et 300 % réglés manuellement dans Windows; conserver la résolution physique. Fermer/réouvrir les démonstrations à chaque palier pour vérifier aussi l'ouverture. Fournir une capture entière de chaque structure, avec le statut DPI, et signaler toute coupure.
5. Si deux écrans à échelles différentes sont disponibles, déplacer chaque démonstration aller-retour plusieurs fois, maximiser/restaurer et vérifier le statut et les commandes. Tester également le clavier (Tab, Shift+Tab, Enter, Escape) et la taille de texte Windows augmentée; noter les cas non exécutés.
6. Remettre ensuite les réglages Windows souhaités. Aucune étape n'est effectuée automatiquement par l'agent.

Ces fenêtres utilisent des valeurs fictives. Aucun média, traitement FFmpeg, fichier utilisateur ou paramètre applicatif n'est modifié. Elles servent à qualifier les composants avant la migration des pilotes C. Elles ne démontrent pas que les fenêtres métier historiques sont déjà corrigées.
