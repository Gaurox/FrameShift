# FrameShift — Contrat du socle UI

État du 2 octobre 2026 : composition WinForms éprouvée en B puis appliquée aux 36 fenêtres C/D/E. Leur rendu est accepté à 100/150/200/300 % ; les preuves et la portée des contrôles complémentaires figurent dans la [feuille de route officielle](UI_AUDIT_AND_STANDARDIZATION_PLAN_2026-09-28.md). F1 réconcilie le contrat et retire les anciennes APIs inutilisées. F2/F3/F4 sont vérifiées automatiquement puis validées manuellement par l'utilisateur : F est terminée sur son périmètre retenu. G a produit la release 1.20.0, acceptée par l'utilisateur et publiée avec l'installateur testé ; sa portée et les limites de preuve sont consignées dans le rapport de release.

## Construction d'une nouvelle fenêtre

Choix retenu et éprouvé par C/D/E : composition de contrôles WinForms. Le formulaire compose ses options et relie la validation au métier ; la géométrie commune reste dans les composants existants.

Le [guide de développement des fenêtres](UI_WINDOW_DEVELOPMENT_GUIDE.md) fournit l'ordre de construction, deux exemples complets (compact et éditeur), les règles de taille initiale, les variantes et la checklist de livraison. Ce document reste le contrat des composants et des métriques ; [UI_STANDARDIZATION](UI_STANDARDIZATION.md) porte les choix visuels. Les bilans de phases ci-dessous sont historiques et ne constituent pas des modèles supplémentaires.

Pour un éditeur, utiliser `FrameShiftEditorShellUi.Create(header, workspace, actions, options: options, status: status)`. Le rail est facultatif et défilant ; il passe sous l'aperçu lorsque la largeur disponible ne permet plus les deux colonnes. `FrameShiftCropEditorUi.Create` compose ce même shell. Le dessin du média et ses coordonnées restent à la charge de l'éditeur.

## Contrat de géométrie figé — 29 septembre 2026

À réutiliser pour toute nouvelle fenêtre et toute modification. Les dimensions sont **logiques à 96 DPI** ; les métriques communes viennent de [FrameShiftUiMetrics.cs](../src/FrameShift/Windows/Helpers/FrameShiftUiMetrics.cs), et les polices/dessins des composants indiqués ci-dessous. Les mesures de texte et les bornes de contrôles sont déjà en pixels physiques.

| Élément | Référence à 96 DPI | Métrique / composant propriétaire |
|---|---|---|
| Validation / annulation / fermeture | 140 × 34 ; mêmes dimensions dans un footer ; écart 10 | `FooterButtonWidth`, `FooterButtonHeight`, `FooterButtonGap` ; `CreateActions` |
| Marges extérieures | 12 sur chaque côté | `OuterPadding` ; shell |
| Bandeau / corps / statut / actions et aperçu / options | Séparation 12 | `OuterPadding` ; shell / workspace |
| Sections ou blocs empilés | Écart 10 | `BlockGap` ; `CreateVerticalStack` |
| Champs, choix sur une ligne et titre / contenu de section | Écart 8 | `LineGap`, `SectionContentGap` ; champs / rangées / sections |
| Intérieur des sections | Gauche 12, haut 10, droite 12, bas 12 | `StandardSectionPadding` ; `CreateSection` |
| Bandeau commun | Minimum 58 de haut, icône 38, marges horizontales 12, verticales 8, écart icône / texte 10 | `HeaderHeight`, `HeaderIconSize`, métriques d'écart ; `FrameShiftHeader` |
| Arrondi des panneaux | Rayon 8 | `PanelCornerRadius` ; peintre commun |
| Arrondi des boutons communs | Rayon 6 | `FrameShiftActionButton` ; dessin commun, rendu natif en contraste élevé |
| Police de contenu | Segoe UI, 9 pt, héritée | `FrameShiftWindowPolicy` |
| Police du titre de bandeau | Segoe UI Semibold, 14 pt | `FrameShiftHeader` ; hauteur du titre mesurée |
| Rail d'éditeur par défaut | Largeur 258 ; repli sous l'aperçu en espace réduit | `EditorRailWidth` ; `FrameShiftEditorWorkspace` |

Ces règles restent adaptatives : les boutons d'un footer grandissent ensemble pour des textes longs, le bandeau grandit pour un titre sur plusieurs lignes, et les métriques suivent le DPI. Ne pas figer des pixels physiques ni couper les textes. Les boutons internes spécialisés (par exemple ×2/×3/×4) peuvent être plus compacts ; ils ne sont pas des commandes de footer.

Les espacements ne sont pas tous identiques : le shell sépare ses grandes zones par 12, la pile de sections par 10 et les lignes de champs par 8. Leurs composants appliquent ces valeurs ; éviter de rajouter la même marge dans le formulaire. Les seules dimensions locales décrivent un besoin métier (largeur de saisie courte, taille initiale d'aperçu, rail spécialisé), avec mesure et défilement de secours.

## Contrats des composants

| Composant | Contrat |
|---|---|
| `FrameShiftWindowPolicy` | Appel unique sous `SuspendLayout`, avant contrôles/handles. Référence 96 DPI, `AutoScaleMode.Dpi`, Segoe UI 9 pt. Minima exprimés en taille **cliente** logique, reconstruits et bornés au moniteur courant à l'ouverture, en fin de déplacement/redimensionnement et après changement DPI. Aucun clamp pendant `LocationChanged`, qui empêcherait de changer d'écran. |
| `FrameShiftWindowChrome` | Possède uniquement les `Icon` qu'il charge. Réutilise l'instance pour un même chemin, libère l'ancienne au remplacement et la dernière à `Disposed`. Une icône affectée par l'appelant reste empruntée. Chemins absents : conserver l'icône actuelle ; fichier invalide : conserver l'instance utilisable et propager l'erreur. Un seul abonnement au cycle du handle par formulaire ; le titre Windows suit aussi sa recréation. |
| `FrameShiftUiMetrics` | Constantes logiques 96 DPI; `ToPixels` réservé aux calculs manuels. Les tailles clientes, bornes et mesures de texte sont déjà en pixels. Aucun `Scale()` récursif supplémentaire ni multiplication manuelle des polices en production. |
| `CreateHeader` | Titre mesuré avec retour à la ligne ; métadonnées sur une ligne avec infobulle, menu View/Copy details et accès à Tab lorsque non vides. Entrée/Espace ouvrent les détails complets en lecture seule, Ctrl+C les copie directement, Maj+F10 ouvre le menu. Focus dessiné dans le cadre ; noms accessibles du titre et des métadonnées. Icône reconstruite à la taille courante ; ancien bitmap et ressources possédées libérés. |
| `CreateSection` | Titre et contenu dans des rangées AutoSize distinctes. Le contenu doit annoncer sa hauteur préférée, par exemple une table de champs AutoSize. |
| `CreateVerticalStack` / `CreateWrappingLabel` | Composition des pilotes C : table verticale AutoSize, intervalle logique partagé entre rangées, descriptions natives avec retour à la ligne. Les contrôles restent dans un parent commun pour les groupes radio exclusifs. |
| `CreateChoiceRow` / `FrameShiftChoiceCard` | Choix exclusifs radio natifs présentés en cartes, avec description et état sélectionné. Rangée espacée, cartes de hauteur commune et retour à la ligne en largeur réduite. La hauteur préférée est calculée pour la largeur disponible, y compris les demandes non contraintes des tables. |
| Champs compacts et unités | `CreateFieldRow(..., logicalEditorWidth: ...)` borne la largeur d'un champ numérique ; le reste de la ligne absorbe l'espace libre. `CreateFieldWithUnit` garde la valeur et son sélecteur d'unité côte à côte. |
| `FitInitialHeight` | Mesure à `Load`, après scaling natif, du contenu et des rangées réelles ; taille bornée à l'espace de travail. Usage explicite sur les dialogues compacts. Subtitles réutilise cette mesure au changement explicite de format pour ne pas réserver de vide aux styles ASS masqués ; largeur conservée, hauteur et position bornées à l'écran, aucun ajustement si maximisée. Aucun appel depuis Resize/Layout. |
| `CreateTimeline` | Variante éditeur : aperçu flexible au-dessus, commandes temporelles en dessous. Leur viewport défile si leur hauteur mesurée dépasse 60 % de l'espace intérieur disponible ; le footer reste indépendant. |
| `CreateFieldRow` | Label natif avec mnémonique, nom accessible du champ, éditeur extensible, unité facultative. Le label long revient à la ligne et laisse de la place à l'éditeur. |
| `CreateMeasuredActionButton` / `CreateActions` | Même taille pour validation et annulation/fermeture : base 140 × 34 logique, paire agrandie ensemble si un texte le requiert. Barre hors scroll; retour à la ligne des boutons et de leurs textes si nécessaire. Relier `AcceptButton`/`CancelButton` et leurs événements dans le formulaire. |
| Couleurs et états F2 | `FrameShiftTheme` centralise les petits textes, les états erreur/succès et les trois fonds d'action principale lisibles avec texte blanc. La factory définit survol/appui ; le bouton peint l'état désactivé et annule un appui visuel à la perte de focus/désactivation. Le remappage préserve les styles propres aux grilles et l'héritage vide. Voir la notice thème et la recette F2. |
| Clavier et focus F3 | Shell : bandeau, corps, statut, commandes ; annulation avant validation dans l'ordre Tab. Actions reconstruites : focus conservé par identité, repli sur Search si l'action disparaît. Join : flèches/Début/Fin sélectionnent, Ctrl+flèches déplacent, Suppr retire, Entrée/Espace sélectionnent sans confirmer ; ces commandes sont locales à la timeline. Glyphes × nommés via leurs cellules natives, canevas essentiels nommés. [Recette F3](UI_PHASE_F3_MANUAL_TESTS.md) validée manuellement le 2 octobre 2026. |
| `CreateStatusMessage` | Texte multiligne sélectionnable/copiable, lecture seule. Hauteur mesurée et plafonnée à 96 unités logiques, défilement vertical des détails longs pour préserver le footer. |
| Ajouts dynamiques | Ajouter dans les mêmes tables de champs/sections; laisser WinForms hériter de la police et du DPI. Les composants recalculent leurs mesures/paddings dans leur contexte courant. Pas de tailles calculées à partir des anciennes bornes. |

F1 a retiré les anciennes factories fixes, les anciens `CreateFill*`, les roots/spacers historiques et les placements de footers/sections devenus inutilisés. `FrameShiftUiLayout` conserve `MeasureActionButton`. Les panneaux de dessin `CreateFramedPanel(Color, …)` et `CreatePreviewPanel`, les shells actuels et leurs métriques gardent leurs usages validés.

`PerMonitorV2` est configuré dans le `.csproj` applicatif **pour Debug et Release** et initialisé par `ApplicationConfiguration.Initialize()` avant les fenêtres. Cette activation Release est distribuée en 1.20.0 après acceptation de l'installateur ; la portée des preuves reste celle du rapport G. Le projet de démonstration utilise également `PerMonitorV2`; ce projet de test n'est pas distribué par l'installateur.

## Exemples actifs pour une nouvelle fenêtre

| Besoin | Exemple à suivre |
|---|---|
| Composition compacte minimale et contenu dynamique | [SampleForm(editor: false)](../tests/FrameShift.UiSamples/Program.cs), puis [InterpolateVideoForm](../src/FrameShift/Windows/Forms/InterpolateVideoForm.cs) pour l'ajustement de hauteur initiale |
| Aperçu flexible avec options à rail repliable | [SampleForm(editor: true)](../tests/FrameShift.UiSamples/Program.cs), puis [CropImageForm](../src/FrameShift/Windows/Forms/CropImageForm.cs) |
| Options temporelles sous l'aperçu | [CutVideoForm](../src/FrameShift/Windows/Forms/CutVideoForm.cs) et son helper de durée de vie |
| Choix courts en cartes natives | [CompressImageForm](../src/FrameShift/Windows/Forms/CompressImageForm.cs) |
| Liste radio verticale et options conditionnelles | [CreateSubtitlesPickerForm](../src/FrameShift/Windows/AI/CreateSubtitlesPickerForm.cs) |

Le testeur ouvre la démonstration compacte par défaut ; « Open editor sample » ouvre la seconde structure. Son mode `--check <fichier-json>` construit les deux structures et les lanceurs **sans les afficher**. Pour une nouvelle fenêtre, reprendre la composition et les composants ; les faux champs et tailles des cas métier ne sont pas des prescriptions.

[TEST_PHASE_F2.cmd](../TEST_PHASE_F2.cmd) ouvre une recette ciblée à fenêtres non modales, avec thème temporaire dans le processus et états Progress simulés. Elle réutilise les composants de production ; voir [les manipulations F2](UI_PHASE_F2_MANUAL_TESTS.md). Le mode caché `--check` ci-dessous ne demande aucun affichage ; la suite complète comprend aussi des tests avec affichage.

## Validation par commandes

```powershell
dotnet build src/FrameShift/FrameShift.csproj --no-restore
dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj --no-restore --filter FullyQualifiedName~UiFoundationTests
dotnet build tests/FrameShift.UiSamples/FrameShift.UiSamples.csproj --no-restore
dotnet tests/FrameShift.UiSamples/bin/Debug/net8.0-windows/FrameShift.UiSamples.dll --check scratch/phase-b/samples-check.json
```

Le dernier appel construit des handles natifs **sans afficher de fenêtre**; les erreurs sont écrites dans le fichier indiqué et signalées par un code de sortie non nul. Il ne capture aucun écran et ne change aucun réglage Windows. Créer le répertoire de résultats avant l'appel. Un premier build du projet de démonstration nécessite sa restauration NuGet; son lockfile est versionné.

Les tests couvrent les conversions 96/144/192/288 DPI, les textes agrandis ×1/1,5/2/3, les corps longs, les commandes longues, les messages longs, les champs dynamiques, les allers-retours de largeur et les limites du dessin. Les grossissements de police sont des tests de mesure, **pas une simulation validante des changements DPI Windows**.

**Isolation du banc UI :** F1 a observé des échecs variables dans les passages parallèles, y compris avec les anciens helpers ; sa recette isolée passe **144 tests/0 échec**. F4 fixe durablement l'isolation avec `WinFormsTestCollection` et la collection de préférences, sans chevauchement avec les autres collections et avec leurs dossiers de settings temporaires. Le passage normal F4 compte **207 tests/0 échec** ; le flag global `xUnit.ParallelizeTestCollections=false` n'est plus nécessaire. Aucune assertion n'est assouplie.

La suite existante peut afficher des fenêtres. Le passage sans affichage utilise ce filtre, conservant les exclusions de B :

```powershell
dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj --no-restore --filter "FullyQualifiedName!~ExplorerArrivals_BeforeAndAfterHandle&FullyQualifiedName!~AddVideosButton_AddsEveryPickerOccurrence&FullyQualifiedName!~ShownWindow_IsVisibleAndCanBeginClosing&FullyQualifiedName!~Closing_DoesNotBlockTheStaMessagePump&FullyQualifiedName!~RemoveNoisePickers_PreserveRequestedDialogResult"
```

Ce filtre de développement historique exclut 12 cas UI existants, distincts des 5 tests média ignorés lors de la qualification 1.20.0. Il ne constitue pas un gate de release : G a réexécuté la suite Release complète en environnement Windows isolé. Voir le [rapport de qualification](RELEASE_QUALIFICATION_1.20.0.md) et la [chaîne canonique](RELEASE_CHECKLIST.md). Pour une nouvelle fenêtre, ajouter les contrôles pertinents et suivre la [recette de développement](UI_WINDOW_DEVELOPMENT_GUIDE.md#8-vérifier-avant-de-livrer).

## Historique de mise en place du socle

Les bilans B/C/D/E/F ci-dessous conservent les décisions, résultats intermédiaires et recettes rejouables. Une mention de phase suivante, de code non encore migré ou d'application installée antérieure décrit sa date d'exécution. L'état courant est celui de 1.20.0 indiqué en tête ; les règles actives sont les sections précédentes et le guide de développement.

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

## Pilotes C — développement et recette

### Application du contrat aux cinq pilotes

Le [contrat de géométrie](#contrat-de-géométrie-figé--29-septembre-2026), désormais placé parmi les règles actives ci-dessus, a été figé lors de cette phase.

Le bandeau et les espacements des cinq pilotes respectaient déjà ce contrat ; leur géométrie est conservée. Les quelques constantes équivalentes encore littérales du nouveau socle utilisent maintenant les métriques communes. Les menus historiques utilisent encore les chemins de compatibilité avant D/E : ils ne sont pas déclarés uniformisés par cette révision C.

Create Subtitles retrouve des listes verticales radio natives, avec une description secondaire indentée sous chaque choix. Les radios d'un groupe restent dans le même parent pour conserver l'exclusivité et la navigation par flèches. Les cartes restent utilisées pour les trois profils Compress Image, acceptés par l'utilisateur.

Interpolate Video (FFmpeg), Compress Image et Create Subtitles composent le dialogue compact ajusté au contenu à l'ouverture. Cut Video utilise la variante timeline avec commandes sous l'aperçu ; Crop Image conserve le rail défilant et son repli sous l'aperçu. Les appels historiques des autres fenêtres restent disponibles jusqu'à D/E/F.

Les boutons mesurés partagent désormais un dessin légèrement arrondi (6 unités logiques), avec états survol, sélection clavier et focus ; ils restent des boutons natifs pour l'activation et l'accessibilité. Le mode contraste élevé utilise le rendu natif. Les messages courts n'affichent plus de scrollbar ; les détails longs restent consultables par défilement. La recette visuelle de ce nouveau standard commence à 100 %, conformément à la demande utilisateur, avant les essais DPI.

Les aperçus Cut sont désormais asynchrones, annulables et sérialisés dans `CutVideoForm.Preview.cs`. Une demande récente annule la précédente ; son décodage attend la libération du précédent. Les erreurs restent dans l'état de l'aperçu. Crop charge aussi son image hors du fil UI ; son décodeur natif déjà démarré doit terminer avant la libération de ses ressources. Les coordonnées de recadrage restent en pixels source ; seuls dessin et zones de prise des poignées utilisent les métriques DPI.

`tests/FrameShift.UiSamples` propose un lanceur `--pilots`, accessible par double-clic sur `TEST_PHASE_C.cmd`. Il ouvre les cinq formulaires réels et rapporte les réglages sélectionnés sans exporter les médias. Le décodage des aperçus et FFprobe utilisent les runners du projet. L'accès interne accordé à cet assembly de test permet d'ouvrir le picker Subtitles et de retarder un aperçu Cut pour la recette de fermeture. Aucun outil de capture n'est ajouté.

Voir [la procédure manuelle C](UI_PHASE_C_MANUAL_TESTS.md) et [le bilan de l'audit](UI_AUDIT_AND_STANDARDIZATION_PLAN_2026-09-28.md#c--cinq-fenêtres-pilotes--p1). Les paliers Windows 100/150/200/300 % des pilotes sont validés par l'utilisateur ; le déplacement multi-écran et les autres scénarios manuels ont également été confirmés (« ok tout validé »). La validation B concerne les démonstrations du socle.

## Extension D1 — recette validée

Les mêmes métriques s'appliquent désormais à Progress, Main, Settings et Media Info. `CreateActions(params Button[])` couvre aussi une seule commande Close/Cancel ; les commandes d'un même footer restent de taille identique. `CreateSection(..., fill: true)` permet à une file ou à une zone de texte de remplir l'espace disponible sans faire croître tout le formulaire avec son contenu. `FrameShiftGridUi` mesure les lignes des files selon la police et remet leurs colonnes fixes à l'échelle depuis les valeurs logiques.

Main conserve son séparateur file/actions, réorienté verticalement sous 640 unités logiques de largeur. Les filtres sont des boutons natifs mesurés. Progress adapte sa zone centrale à la file, au statut et au soutien ; un défilement de secours apparaît lorsque leur hauteur minimale ne tient plus, avec Cancel all / Close hors de cette zone. Settings ajuste sa hauteur à l'ouverture ; Media Info garde le défilement de son texte monospacé. Les handlers métier sont conservés.

Recette : double-clic sur `TEST_PHASE_D1.cmd`, puis [procédure D1](UI_PHASE_D1_MANUAL_TESTS.md). Les paliers Windows 100/150/200/300 % de D1 sont validés par l'utilisateur ; la suite automatique inclut toujours les 37 cas du socle et des pilotes B/C.

Après les retours utilisateur sur l'erreur longue et le refus du panneau latéral, Progress sépare un résumé d'état mesuré du texte intégral. `ProgressForm.Details.cs` compose toujours une file au-dessus d'un panneau Messages & details de pleine largeur. Le texte natif multiligne remplit la surface disponible, avec au moins six lignes et 110 unités logiques de hauteur réservées ; le panneau occupe 30 % du corps file/détails, contre 60 % avant la réduction demandée, Copy details et retour au suivi courant. La sélection d'une occurrence de file conserve son diagnostic pendant les mises à jour des autres fichiers. Activity utilise le fond bleu doux du thème, une hiérarchie typographique état/pourcentage et masque la ligne ETA quand elle est vide. La file indique son effectif, utilise des libellés d'état lisibles et un fond uniforme. Le bandeau commun, les commandes et les espacements figés restent les mêmes. Cette adaptation reste locale à Progress ; le composant de statut court des autres dialogues n'est pas modifié.

Dans Files, les lignes et l'en-tête partagent le fond Surface, sans alternance. Les noms utilisent TextPrimary ; les en-têtes et états ordinaires restent secondaires. La sélection utilise uniquement PageBackground, une nuance neutre discrète, sans bleu de sélection. La colonne × hérite des fonds de ligne et de sélection ; le retrait en attente utilise TextMuted, le rouge distingue l'annulation d'un traitement actif. Les couleurs des états restent lisibles et cohérentes lorsque la ligne est sélectionnée.

Validation utilisateur D1 : rendu final du commit `c26a723` accepté à 100/150/200/300 %. Les essais multi-écran, texte Windows agrandi et relecture des pilotes C après D1 sont également validés par le retour utilisateur « je valide aussi ». D1 est validée ; D2 est le prochain lot. La phase D complète reste en cours.

## Extension D2 — validée à 100/150/200/300 %

Les douze fenêtres D2 utilisent les mêmes bandeaux, footers et métriques. Les dialogues compacts ajustent leur hauteur initiale au contenu avec défilement de secours. Resize conserve sa base existante et les coordonnées média ; Rotate/Flip utilise le shell avec commandes sous l'aperçu. Après le retour à 100 % du 1er octobre, Resize présente quatre saisies de taille identique en grille 2 × 2 ; les boutons Rotate conservent la palette standard, avec ✓ sur les miroirs actifs. Convert to Icon retrouve trois colonnes tailles/réglages/aperçus, repliables en deux puis une colonne, dans le dialogue mesuré commun. Les sélections métier sont conservées, sans nouvelle base générique de formulaire.

`FrameShiftStatusMessage` réserve explicitement sa hauteur mesurée dans les tables AutoSize après changement de texte/police/largeur/DPI, toujours plafonnée à 96 unités logiques. Cette correction a été contrôlée sur B/C/D1 dans la suite de 522 tests réussis et 5 ignorés. Les 26 cas D2 comprennent les retours tardifs d'aperçus Pitch/Speed après annulation. Les runners du Core ne sont pas modifiés.

La révision du 1er octobre fait également diminuer la hauteur native du message lorsque sa largeur définitive réduit le nombre de lignes ; diminuer uniquement `MinimumSize` gardait du vide. `FitInitialHeight` prend sa hauteur préférée mesurée. Les cases natives Pitch/Speed et cible de compression passent par une rangée mesurée commune ; la restriction de taille cible WAV/FLAC est expliquée et le lanceur privilégie MP3/M4A/OGG. La suite de cette révision compte **539 réussites et 5 ignorés**, dont **43 cas D2**. Le problème de clic Speed signalé par l'utilisateur n'a pas été reproduit par les contrôles cachés ; l'agencement renforcé et le comportement de bascule restent à confirmer visuellement.

Au second retour à 100 %, les quatre saisies Resize sont bornées à 128 unités logiques, environ la moitié de leur largeur du premier rendu 2 × 2. Elles ne grandissent plus avec l'espace libre ; elles peuvent diminuer ensemble si nécessaire. Speed Video calcule sa largeur initiale pour les huit boutons de préréglage, leurs écarts et les marges communes (688 unités logiques avec les libellés actuels) ; le retour à la ligne reste disponible en espace réduit.

Le 1er octobre, l'utilisateur confirme « ok tout validé en 100%/ committe » après les derniers ajustements, puis « tout est validé dans toutes les mises a léchelle. commite et on passe à la suite ». Les fenêtres D2 du commit `c7bd8ed` sont validées à 100/150/200/300 %, y compris les cases d'options et les derniers agencements Speed/Resize. Passage à D3 autorisé. La [procédure D2](UI_PHASE_D2_MANUAL_TESTS.md) et `TEST_PHASE_D2.cmd` restent disponibles pour rejouer les contrôles ; les transitions entre écrans et le texte Windows agrandi ne sont pas confirmés séparément par ce dernier retour. Aucun support global Release n'est annoncé.

## Extension D3 — rendu validé à 100/150/200/300 %

Les huit fenêtres IA emploient la politique commune, le bandeau mesuré, la paire de commandes de taille identique et les écarts figés. Le corps défile en espace réduit ; sa hauteur initiale est mesurée à Load. Les six pickers ne lancent aucune inférence ou acquisition de modèle à l'ouverture.

Remove Noise Audio/Video composent une même présentation légère (`RemoveNoisePickerUi`) avec quatre choix radio natifs en cartes espacées et une case stéréo mesurée ; leur gestion des aperçus et `OnnxFormLifetime` reste dans chaque picker. Les noms de fichiers complets sont transmis au bandeau, qui gère lui-même l'ellipse et la copie. Les erreurs d'aperçu sont sélectionnables dans le statut commun. Separate Audio conserve les stems et le fallback d'engine avec radios natives dans un parent commun. RIFE et Upscale emploient des ComboBox natifs ; leurs calculs et catalogues sont conservés, avec champs numériques compacts. Upscale Video hérite toujours du picker image existant.

Download Model affiche les erreurs complètes dans une zone native multiligne de pleine largeur, au moins sept lignes et 140 unités logiques. La croix de fermeture annule et attend la tâche sans bloquer le fil UI ; Cancel arrête l'opération puis permet Close ou un nouvel essai. Les notifications tardives d'une ancienne tentative sont ignorées. BRIA sépare les liens d'installation manuelle du footer, conserve la vérification et Use anyway, et n'applique plus de coordonnées brutes après Re-check.

Recette : double-clic sur `TEST_PHASE_D3.cmd`, puis [procédure D3](UI_PHASE_D3_MANUAL_TESTS.md). Download et BRIA sont simulés explicitement ; les pickers réels rapportent les réglages sans export. Aperçu Remove Noise facultatif avec modèle déjà installé. Le 1er octobre, l'utilisateur confirme « je valide toutes les fenetres dans toutes les mises a l'échelle » : le rendu D3 est validé à 100/150/200/300 %. Les manipulations complémentaires, le multi-écran et le texte Windows agrandi ne sont pas confirmés séparément par ce retour. Les 24 fenêtres D sont migrées et acceptées visuellement. D3 est sauvegardée sous `50a4626` le 2 octobre avant le GO E. Release et l'installateur restent hors de cette livraison.

## Extension E — sept éditeurs, rendu UI/DPI validé

Cut Audio et Create GIF suivent le shell temporel validé : aperçu au-dessus, contrôles temporels en dessous, défilement de secours et commandes persistantes. Crop Video, Burn Subtitles, Remove Object et Image to PDF utilisent le shell à rail repliable. Join conserve sa timeline et ses commandes spécifiques dans le shell. Tous emploient les mêmes bandeaux, boutons et métriques ; les tailles par défaut réservent l'espace de travail utile, avec saisies bornées lorsque leur valeur est courte.

`EditorPreviewLifetime` sérialise les décodages GIF/Crop/Burn, annule la requête précédente et permet à la fermeture d'attendre sa fin. Cut Audio et Remove Object conservent leurs helpers de durée de vie. Join attend aussi le chargement de métadonnées/vignettes ; PDF importe séquentiellement en arrière-plan et utilise son cache pour Undo/Redo. Les imports ou aperçus ne sont plus décodés dans les constructeurs. Les bitmap tardifs et temporaires sont libérés. FFmpeg/FFprobe restent exécutés par leurs runners du Core.

Après annulation d'une fermeture dans `FormClosing`, poster la fermeture finale avec `BeginInvoke` et restaurer `DialogResult` dans ce callback. Un `await` peut se terminer immédiatement : appeler `Close()` directement à sa suite réentre dans l'événement initial et peut laisser un dialogue modal désactivé, avec son résultat remis à `None`. Le correctif du 2 octobre applique cette règle aux sept éditeurs E ; les tests cachés couvrent aussi le contrôle de fin de dialogue WinForms, OK/Cancel et les fermetures natives répétées pendant un aperçu.

Les coordonnées média restent séparées des mesures UI. Waveform et timeline temporelle utilisent secondes/frames ; crop et masque utilisent pixels source ; PDF conserve coordonnées normalisées et unités de page. Poignées, hit-tests, règles, snapping et marges dessinées utilisent des métriques DPI. `SeekTrackBar` calcule ses valeurs depuis le canal et les dimensions du curseur natifs. Les boutons de bibliothèque PDF utilisent `FrameShiftToolTile`, un bouton natif qui mesure sa légende et reconstruit son icône au DPI courant ; le footer reste celui des autres fenêtres.

Recette : double-clic sur `TEST_PHASE_E.cmd`, puis [procédure E](UI_PHASE_E_MANUAL_TESTS.md). Les aperçus sont réels ; les validations des réglages restent sans export final dans le lanceur. Apply Remove Object et Print PDF gardent leurs opérations réelles sur clic manuel. Le 2 octobre, après le commit `902f2a9`, l'utilisateur valide E « dans toutes les échélles » : le rendu des sept éditeurs est accepté à 100/150/200/300 %. Les contrôles complémentaires de la recette ne sont pas confirmés séparément par ce retour. Les tests cachés ne certifient pas le DPI réel. F1 réalise le nettoyage des anciens chemins et la réconciliation documentaire. Le suivi de F2/F3/F4 et de la qualification G reste dans l'audit.

## Ressources et tests F4

Le chrome possède uniquement ses icônes chargées ; les bitmaps de bandeau/outils et les polices créées par la politique/les composants sont libérés par leur propriétaire. Ne pas disposer les images ou polices empruntées à l'appelant. Les aperçus continuent de transférer explicitement leur bitmap au contrôle qui le détient.

Les classes de tests WinForms rejoignent `WinFormsTestCollection`. Cette collection et celle des préférences ne s'exécutent pas en parallèle avec les autres collections, car thème, settings et GDI sont partagés dans le processus. Chacune possède un dossier de préférences temporaire ; les collections indépendantes gardent leur parallélisme. Conserver ce contrat pour les prochains tests UI plutôt que fournir un flag global à chaque commande de test.

`FrameShift.UiSamples --resources <json>` est une mesure de développement cachée : handles natifs, dessin dans ses propres bitmaps et compteurs GDI/USER après échauffement. Ce mode n'affiche aucune fenêtre et ne capture pas le bureau. Il exerce le noyau chrome/composants/Main/picker/Progress, sans certifier le rendu à plusieurs DPI. Voir [le bilan F4 de l'audit](UI_AUDIT_AND_STANDARDIZATION_PLAN_2026-09-28.md) et [la recette combinée de F](UI_PHASE_F4_MANUAL_TESTS.md). F4 et les recettes manuelles F2/F3/F4 ont été validées par l'utilisateur le 2 octobre 2026, avant la qualification G et la publication 1.20.0 consignées séparément.
