> Historical Find Loop work preserved at `137311e` (formerly labelled 1.22).
> Carried into the unpublished 1.23 integration branch on 8 November 2026.
> Installer, version and qualification results below describe the old candidate,
> not the multilingual 1.22.0 release or acceptance of the new 1.23 build.
> See [the integration plan](RELEASE_PLAN_1.22_1.23.md) for current status.

# Plan des améliorations Find Loop

Date : 24 octobre 2026. Base : candidat local FrameShift 1.22.0.

Avancement au 29 octobre 2026 : les lots 0 à 9 et les contrôles techniques du lot 10 sont commités. La chaîne canonique exécutée depuis `f9c0444` passe **1 302 tests Release, zéro échec, 18 tests ignorés conditionnellement** ; les neuf scénarios de mesure sont également passés séparément. Le nouvel installateur 1.22.0 est construit et la réinstallation est vérifiée par empreinte du binaire installé. Le lot 10 reste **ouvert pour la recette interactive** : une demande de sécurité Windows UnrealGame bloque le bureau ; DPI/texte agrandi, thèmes au clavier et multi-écrans ne sont pas déclarés validés. Aucun média HDR réel n'est disponible, ce que le propriétaire a confirmé. Les preuves, performances et limites précises figurent dans la [validation du lot 10](FIND_LOOP_VALIDATION.md#lot-10-candidate-qualification-on-29-october-2026). Les mentions « sources seulement » ci-dessous décrivent les livraisons historiques des lots ; ce nouveau candidat les intègre, contrairement à l'installateur du 24 octobre.

Find Loop reste intégré à Cut Video. Le parcours cible est **Find Loop → choisir un candidat → Preview junction → Export Loop**. La recherche privilégie le raccord le plus convaincant et présente les variantes utiles trouvées ; l'utilisateur conserve le choix de la durée. Les réglages spécialisés restent accessibles dans une section avancée repliée.

Ce plan couvre la simplification UI, la recherche, les scores, la navigation précise, les aperçus longs, les faux positifs HDR, la conversion HDR vers SDR et l'export des candidats par lot. La conservation HDR complète à l'export constitue une évolution ultérieure : la sortie compatible de ce chantier reste MP4/H.264 SDR.

Les règles de [PROJECT_RULES](PROJECT_RULES.md), [ARCHITECTURE_FREEZE](ARCHITECTURE_FREEZE.md), [UI_FOUNDATION](UI_FOUNDATION.md), [UI_STANDARDIZATION](UI_STANDARDIZATION.md) et du [guide des fenêtres](UI_WINDOW_DEVELOPMENT_GUIDE.md) s'appliquent. Réutiliser les runners, la queue et les composants WinForms existants, sans dépendance ni framework supplémentaire.

## Interface cible

L'interface applicative conserve ses libellés anglais. Le footer reste indépendant du défilement, construit avec les helpers communs.

| Situation | Contenu principal | Commande principale du footer | Commande secondaire |
|---|---|---|---|
| Cut Video à l'ouverture | Sélection et aperçu existants ; bouton Find Loop | Export Selection | Cancel |
| Recherche en cours | Progression, état compréhensible et Stop search | Export désactivé | Cancel ferme après nettoyage |
| Recherche terminée | Liste des candidats, aperçu du raccord, Advanced options replié | Export Loop | Export All Candidates (N), Cancel |
| Sélection modifiée | Bornes éditées et mention Custom selection | Export Loop | Retour aux candidats ou à la coupe |
| Aucun candidat satisfaisant | Résultat explicite, réglages avancés disponibles | Export Loop disponible pour une sélection manuelle valide | Create forward/backward loop |
| Alternative aller-retour | Explication brève, sélection, vitesse utile à ce mode | Export Loop | Back to Cut, Cancel |

La liste présente début, fin exclusive, durée et appréciation du raccord. Une sélection simple suffit ; les flèches parcourent les lignes. Les résultats sont ordonnés par qualité du raccord puis par diversité et durée pour départager les équivalents. Un tri par durée reste une commande secondaire de la liste. Ne pas imposer de cases à cocher pour le parcours normal.

Les contrôles visibles après recherche sont la liste, Preview junction et Stop preview pendant la lecture. La commande Full loop preview reste accessible comme option d'aperçu secondaire. Advanced options regroupe durée minimale facultative, sensibilité, répétitions et conservation audio. La vitesse apparaît uniquement dans l'alternative aller-retour. Conserver les valeurs d'export historiques : un cycle, audio source conservé en naturel si présent, aller-retour silencieux.

Create forward/backward loop remplace la mise en avant de Ping-Pong, avec une explication indiquant que la sélection est lue en avant puis en arrière. Ce choix reste une alternative explicite, accessible sans candidat. Une seule commande de changement de contexte suffit ; ne pas ajouter de fenêtre, de nouvelle famille d'onglets ou de nouveau menu Explorer.

Le passage en contexte Loop intervient à l'utilisation des outils. Back to Cut restaure réellement le contexte Cut et son action principale ; déplacer le scroll ne suffit plus. Changer de contexte, de candidat, de bornes ou de profil couleur annule et attend les tâches devenues obsolètes. Une modification de bornes transforme le résultat en sélection personnalisée sans modifier le candidat sauvegardé dans la liste.

## Ordre et dépendances

Chaque lot se termine par une compilation verte et ses tests ciblés. Les lots restent des unités de modification relisibles. Les nombres ci-dessous sont un ordre de travail, pas des versions de release.

| Lot | Livraison | Prérequis |
|---|---|---|
| 0 | Corpus et mesures de référence | Aucun |
| 1 | Contrats simples et classification couleur | 0 |
| 2 | Parcours UI et footer sans ambiguïté | 1 |
| 3 | Aperçu léger centré sur le raccord | 1, 2 |
| 4 | Horodatages et navigation précise | 1 ; intégration dans 3 |
| 5 | Recherche couvrant davantage de candidats | 0, 1, 4 |
| 6 | Affinage perceptuel et classement | 0, 4, 5 |
| 7 | Export naturel moins coûteux | 3, 4 |
| 8 | Conversion HDR vers SDR cohérente | 1, 3, 6, 7 |
| 9 | Export des candidats par lot | 2, 6, 7 ; 8 pour les sources HDR |
| 10 | Qualification complète et documentation | Tous les lots concernés |

## Lot 0 Corpus et mesures de référence

Constituer un corpus permettant de comparer l'ancien et le nouveau moteur sur les mêmes sources et bornes attendues. Les vidéos utilisateur restent locales ; les tests portables utilisent des séquences synthétiques déterministes et de petits assets redistribuables si nécessaires.

Travail :

- Reprendre les régressions existantes : cycles, rotation, éclairage évolutif, mouvement opposé, petit premier plan, statique, chaos, VFR, orientation, audio décalé et inversion par morceaux.
- Ajouter une source bouclable exactement de son premier au dernier intervalle, une vidéo contenant plus de trois raccords distincts et une vidéo où un raccord long est meilleur qu'un court.
- Couvrir les durées 5 s, 30 s, 50 s, 1 min, 5 min et 1 h. Les longs médias appartiennent à une qualification de performance reproductible, distincte des petits cas obligatoires de la suite courante.
- Recueillir la vidéo du raccord de 36 s et quelques échecs du retour utilisateur lorsqu'elles sont disponibles ; annoter la visibilité du raccord, pas uniquement son score actuel.
- Mesurer par phase temps, nombre d'images décodées, cadence d'échantillonnage, pistes examinées, candidats distincts, pic mémoire du processus et espace temporaire. Identifier matériel, source et réglages.
- Conserver les résultats locaux sous `scratch/loop-validation/improvements/`, avec mesures avant/après et exemples de raccords rendus. Les ressources de test requises vivent dans `tests/`, jamais dans `references/` ou `scratch/`.

Fichiers : `tests/FrameShift.Tests/VideoLoopTests.cs`, `VideoLoopUiTests.cs`, `BoundedMediaCaptureTests.cs`, `docs/FIND_LOOP_VALIDATION.md`. Prévoir `tests/FrameShift.Tests/VideoLoopPerformanceTests.cs` pour les mesures longues, avec activation explicite et disponibilité annoncée.

Critère de sortie : corpus identifié, mesures reproductibles et régressions existantes conservées. Une absence de clip privé ne compte pas comme une validation de qualité sur ce clip.

## Lot 1 Contrats et classification couleur

Faire évoluer les petits records existants pour séparer les paramètres de recherche, les résultats, les bornes exactes et les choix d'export. Garder `VideoLoopSettings` compatible avec les options CLI actuelles.

Travail :

- Ajouter un résultat de recherche indiquant le nombre de pistes examinées et la couverture atteinte : terminée, interrompue ou limitée par un budget. Un résultat limité ne doit pas annoncer qu'aucune boucle n'existe.
- Conserver début et fin exclusive en temps natif comme source de vérité, avec durée réelle du dernier intervalle lorsque nécessaire. Les numéros de frame approximatifs restent une représentation UI tant que leur correspondance exacte n'est pas résolue.
- Prévoir des mesures de raccord distinctes du texte UI : apparence, mouvement, éclairage et continuité temporelle. L'appréciation visible sera définie et calibrée au lot 6.
- Ajouter un petit record de sélection d'export contenant bornes exactes, réglages et identifiant stable du candidat. Ce record permet une sélection unique ou une liste, sans sérialiser des objets UI dans Core.
- Introduire une classification locale explicite SDR, HDR confirmé et indéterminé. PQ/HLG sont des indices HDR directs ; profondeur 10/12 bits, Main 10 et BT.2020 seuls ne suffisent pas. Distinguer les métadonnées absentes des métadonnées contradictoires.
- Utiliser cette classification dans Find Loop. Garder initialement le contrat historique `IsHdrLikely` pour les autres actions afin de ne pas modifier indirectement Join Videos ou Burn Subtitles. Une consolidation commune ultérieure exige leurs tests spécifiques.
- Afficher la compatibilité couleur au début du parcours. Un SDR 10 bits correctement identifié suit le parcours SDR ; un profil indéterminé ne reçoit pas automatiquement un tone mapping PQ supposé. Un blocage éventuel précise la métadonnée manquante ou incohérente, dès le préflight. Jusqu'au lot 8, annoncer immédiatement la conversion HDR non disponible dans ce candidat plutôt que laisser découvrir ce blocage après une recherche longue.

Fichiers à modifier : `src/FrameShift/Core/Actions/VideoLoopSettings.cs`, `VideoLoopDetector.cs`, `VideoLoopExporter.cs`, `src/FrameShift/Core/FFprobe/MediaProbeResult.cs`, `FfprobeRunner.cs` seulement si une information nécessaire manque réellement au probe. Nouveau fichier ciblé : `src/FrameShift/Core/Actions/VideoLoopColorPolicy.cs`.

Tests : ajouter `VideoLoopColorPolicyTests.cs` ; compléter `VideoLoopTests.cs`. Cas SDR 8 bits, SDR BT.709 10 bits/Main 10, SDR large gamut explicitement identifié, HDR PQ, HDR HLG, absence de transfert et incohérences. Vérifier l'aller-retour des anciennes options CLI, les bornes invalides et l'immutabilité des sélections d'export. Exécuter les tests Join Videos et sous-titres si un contrat FFprobe partagé change.

Critère de sortie : les faux positifs 10 bits sont traités dans Find Loop, les choix couleur sont explicables et les autres actions conservent leur comportement.

## Lot 2 Interface et commandes contextuelles

État au 29 octobre 2026 : implémenté dans les sources, sans nouvel installateur. L'aperçu complet a été conservé sous `Full loop preview`, puis déplacé dans Advanced options au lot 3. À la livraison du lot 2, la liste affichait `To review` avec le classement et les plafonds historiques ; les lots 5 et 6 ci-dessous remplacent ce comportement provisoire. La recette réelle à 96 DPI et les régressions automatisées sont consignées dans [FIND_LOOP_VALIDATION](FIND_LOOP_VALIDATION.md#lot-2-contextual-interface-on-29-october-2026). Les DPI supérieurs et les changements de moniteur restent à recetter.

Appliquer le parcours cible avant d'augmenter le nombre de résultats. La liste et les commandes doivent supporter les nouveaux résultats sans multiplier les réglages.

Travail :

- Représenter Cut, Natural Loop et Forward/backward avec un état local simple et une méthode de mise à jour des commandes. Conserver séparément l'état occupé : recherche, rendu ou lecture. Aucun framework de présentation n'est nécessaire.
- Remplacer OK par Export Selection en coupe, puis Export Loop en contexte boucle. Utiliser une seule commande principale persistante ; relier `AcceptButton` à son comportement réel.
- Faire revenir Back to Cut au mode coupe. Le parcours CLI sans options boucle et le Cut Video ordinaire restent compatibles.
- Remplacer la ComboBox de trois candidats par une liste native en lecture seule, à sélection unique, adaptée aux colonnes et au DPI avec les helpers de grille existants.
- Déplacer durée minimale et sensibilité dans Advanced options. La durée facultative vide signifie Auto ; conserver une règle interne évitant les micro-boucles et la calibrer au lot 6. Le maximum actuel de la saisie de minimum ne devient pas une limite de durée de source ou de candidat.
- Présenter l'alternative aller-retour discrètement et montrer sa vitesse uniquement lorsqu'elle s'applique. Garder Export All Candidates masqué tant que le lot 9 n'est pas branché.
- Distinguer Stop search de Stop preview. Cancel conserve son rôle de fermeture après annulation et nettoyage.
- Garder chaque candidat intact lors d'une modification manuelle ; montrer Custom selection et permettre de rechoisir l'original. Conserver la sélection utilisateur lors de l'arrivée de nouveaux résultats ; une ancienne tâche ne remplace jamais une recherche plus récente.
- Pendant la transition vers le lot 6, employer une mention neutre de raccord à vérifier plutôt que présenter l'ancien pourcentage comme une nouvelle appréciation calibrée.

Fichiers : `src/FrameShift/Windows/Forms/CutVideoForm.cs`, `CutVideoForm.Loops.cs`, `CutVideoForm.Preview.cs`, `src/FrameShift/ProgramPickersCut.cs`, helpers `FrameShiftEditorShellUi.cs`, `FrameShiftDialogLayout.cs` et `FrameShiftGridUi.cs` en réutilisation. Modifier un helper commun seulement si son contrat manque réellement et vérifier alors les autres consommateurs.

Tests : `VideoLoopUiTests.cs`, tests WinForms communs affectés et tests CLI de `VideoLoopTests.cs`. Vérifier Entrée, Échap, Tab, flèches dans la liste sans déclencher d'export, retour réel à la coupe, zéro/un/plus de trois candidats, édition manuelle, réanalyse, double clic, recherche annulée et fermeture répétée. La commande principale et son footer restent visibles aux petites tailles existantes, dans les deux thèmes.

Critère de sortie : un utilisateur obtient puis choisit un candidat sans modifier de réglage ; aucune commande générique OK ne peut lancer une coupe à la place de l'export boucle affiché.

## Lot 3 Aperçu léger du raccord

État au 29 octobre 2026 : implémenté dans les sources, sans nouvel installateur. `Preview junction` rend un vrai raccord naturel interne ou les deux changements de direction identifiés. L'aperçu complet reste secondaire, en proxy silencieux. La réduction précède les fichiers FFV1 et les budgets ne dépendent plus de la définition/durée entière pour un raccord court. Un seul bundle est conservé par session et supprimé après arrêt du décodeur. La validation des pixels, durées, cas HD/4K, VFR et de la durée de vie du cache figure dans [FIND_LOOP_VALIDATION](FIND_LOOP_VALIDATION.md#lot-3-junction-proxies-on-29-october-2026).

Préparer l'aperçu à la définition de lecture au lieu de rendre d'abord trois cycles pleine définition. Le lecteur actuel limite déjà sa définition à 960 pixels ; appliquer cette réduction avant les intermédiaires et l'encodage temporaire.

Travail :

- Ajouter une demande de rendu d'aperçu explicite ; les paramètres de résolution et d'extraits restent internes et ne modifient pas les paramètres d'export.
- Produire par défaut une séquence continue constituée de la fin puis du début du cycle, avec un vrai raccord interne puis arrêt de lecture. Choisir des fenêtres courtes, adaptées aux boucles très brèves, avec indication qu'il s'agit d'un aperçu du raccord. Ne pas enchaîner artificiellement plusieurs blocs fin/début : le saut du début vers la fin serait un faux raccord ajouté par l'aperçu.
- Garder un aperçu de trois cycles complets accessible secondairement, à définition réduite et avec budget expliqué. Ses deux raccords internes servent à juger la répétition dans la durée. Pour l'aller-retour, montrer les deux changements de direction ; deux extraits courts distincts sont identifiés comme tels si un cycle complet est trop long.
- Préserver l'ordre des images, les horodatages VFR et la durée de maintien de la dernière image. Le raccord est interne au fichier rendu ; un seek ou un redémarrage du lecteur ne sert pas à simuler sa continuité.
- Toujours supprimer l'audio du rendu d'aperçu. Le choix Keep source audio concerne uniquement l'export.
- Partager la construction des bornes, du timing et de la politique couleur avec l'export. Le proxy diffère en définition et en couverture temporelle ; un contrôle export pleine définition restera requis pour juger les détails fins.
- Évaluer le budget sur les images et extraits réellement rendus. Le garde-fou actuel estimé à 8 Gio ne doit plus refuser un aperçu court parce que la sélection source est longue ou en 4K.
- Réutiliser un aperçu seulement si source, bornes, mode, vitesse, politique couleur et version de rendu correspondent. Limiter le cache à un petit nombre de fichiers par session et supprimer les obsolètes.
- Annuler puis attendre rendu et décodeur avant de supprimer leur fichier. Un échec conserve la sélection et permet une nouvelle tentative.

Fichiers : `VideoLoopExporter.cs`, `VideoLoopSettings.cs`, `CutVideoForm.Loops.cs`, `CutVideoForm.Preview.cs`, `src/FrameShift/Windows/Controls/LoopVideoPlayer.cs`. Nouveau fichier : `src/FrameShift/Core/Actions/VideoLoopPreviewBuilder.cs`, simple constructeur des extraits d'aperçu réutilisant le rendu commun.

Tests : nouveau `VideoLoopPreviewTests.cs`, plus `VideoLoopUiTests.cs`. Couvrir une sélection de 36 s en 1080p, une longue sélection 4K dont seul le raccord est rendu, absence de piste audio, durée et définition du proxy, un vrai raccord pour l'extrait court et deux pour trois cycles complets, boucles plus courtes que la fenêtre, VFR, trois vitesses aller-retour et les deux changements de direction. Comparer les images autour du raccord avec un export de référence et exclure un saut artificiel entre extraits courts. Tester Stop, changement de candidat en lecture, exception de rendu, fermeture et libération immédiate du fichier.

Critère de sortie : l'aperçu court d'une longue boucle SDR est possible sans encoder la sélection entière à sa définition source, et conserve le raccord réel.

## Lot 4 Horodatages et navigation précise

État au 29 octobre 2026 : implémenté dans les sources, sans nouvel installateur. La capture sélectionne les PTS entiers de la première et de la dernière image réellement incluses. Les pas utilisent les voisins natifs et les bornes temporelles prioritaires ; les numéros restent explicitement approximatifs tant qu'ils ne sont pas indexés. Un index ordinal paresseux conserve un préfixe sur disque (256 Mio maximum, 24 octets/image) ; huit fenêtres de 4 096 horodatages au plus bornent le cache temporel en mémoire. Le premier accès ordinal lointain reste un décodage nécessaire et annulable, signalé dans l'interface ; les accès répétés et les extensions proches réutilisent les données déjà résolues. Un changement de source annule les tâches et invalide index, candidats, images et proxies, puis demande de rouvrir Cut Video pour rafraîchir les métadonnées. Voir [FIND_LOOP_VALIDATION](FIND_LOOP_VALIDATION.md#lot-4-native-navigation-on-29-october-2026).

Un candidat exact, l'image affichée et les bornes exportées doivent désigner le même passage. Corriger aussi le coût de résolution des bornes modifiées loin dans une source longue.

Travail :

- Réutiliser les ticks entiers et la base de temps collectés par `VideoLoopTimingCollector` quand ils sont disponibles ; éviter toute régression vers un texte `pts_time` arrondi.
- Conserver une référence temporelle commune à l'analyse, à la capture et au rendu, incluant les décalages vidéo/conteneur.
- Afficher les images de début et de fin à partir des horodatages du candidat. La dernière image incluse précède la fin exclusive ; ne pas montrer l'image du cycle suivant comme fin de sélection.
- Pour une édition VFR en images, résoudre les véritables images décodées. Les pas précédent/suivant utilisent les voisins natifs connus ; les champs ne prétendent pas être exacts lorsqu'ils reposent encore sur le FPS moyen.
- Mettre en cache les correspondances déjà résolues. Si un index temporel est nécessaire, le construire paresseusement, avec annulation et stockage borné ; signaler une première indexation longue. Un index complet ou un `ffprobe -show_frames` capturé intégralement en mémoire n'est pas un prérequis à chaque clic.
- Invalider index, candidats et aperçus si le fichier change. Conserver le debounce existant et la sérialisation des captures ; une requête obsolète ne doit pas repeindre la sélection courante.
- Étendre le helper partagé de capture avec une surcharge ciblée seulement si nécessaire ; préserver les autres éditeurs qui l'utilisent.

Fichiers : `VideoLoopFrameReader.cs`, `VideoLoopTimingCollector.cs`, `VideoLoopExporter.cs`, `CutVideoForm.cs`, `CutVideoForm.Preview.cs`, `CutVideoForm.Loops.cs`, `src/FrameShift/Windows/Helpers/PreviewFrameHelper.cs`. Prévoir `VideoLoopTimingIndex.cs` dans `Core/Actions` uniquement si le cache ne tient plus clairement dans le lecteur existant.

Tests : `VideoLoopTests.cs`, `VideoLoopUiTests.cs`, `BoundedMediaCaptureTests.cs` ; ajouter des cas dans `VideoLoopTimingTests.cs` si la taille le justifie. Couvrir VFR irrégulier, début vidéo décalé, longues positions à 23,976/59,94/240 FPS, première et dernière image, sélection d'une seule image en naturel, changement de fichier, navigation rapide et annulation pendant indexation. Mesurer le premier accès puis les accès répétés loin dans la vidéo.

Critère de sortie : les pixels de l'aperçu de borne correspondent aux images réellement retenues dans l'export ; une édition répétée ne redécode pas systématiquement toute la source depuis son début.

## Lot 5 Couverture de recherche et candidats nombreux

État au 29 octobre 2026 : implémenté dans les sources, sans nouvel installateur. Le moteur conserve des signatures compactes après décodage de blocs de 256 images au plus. Il maintient 8 FPS sur une heure ; au-delà du budget de 57 600 échantillons, des fenêtres réparties couvrent les extrémités et les trous sont annoncés. Les 192 pistes sont réparties entre 24 régions au plus et familles de durées ; les élagages de buckets/pistes et la garde de 512 résultats sont signalés. Les snapshots de résultats sont immuables, publiés par lots et la sélection/le tri utilisateur restent stables. Les tests couvrent notamment vingt régions cycliques distinctes et le raccord sur la source entière. La qualification dédiée d'une heure est consignée dans [FIND_LOOP_VALIDATION](FIND_LOOP_VALIDATION.md#lot-5-search-coverage-on-29-october-2026). Le classement provisoire shortest-first et `To review` de ce lot est remplacé par le lot 6 ci-dessous.

Remplacer les limites de sélection silencieuses par une recherche progressive couvrant mieux toute la vidéo, tout en bornant mémoire et comparaisons.

Travail :

- Retirer la limite de présentation à trois candidats. La liste reçoit tous les candidats validés et dédoublonnés conservés par la recherche ; sa sélection ne change pas automatiquement pendant une mise à jour.
- Remplacer le plafond global de 16 pistes par un budget réparti entre régions temporelles et familles de durées. Une zone très répétitive ne doit pas absorber tout l'effort disponible.
- Traiter la première passe par blocs : libérer les pixels décodés après calcul des signatures et conserver un index compact couvrant la source. Éviter la réduction automatique à une image/seconde comme seule couverture d'une source d'une heure.
- Maintenir une cadence de recherche raisonnable sur les longues sources et adapter la densité locale aux passages ambigus. Toute couverture partielle ou fin sur budget est indiquée dans le résultat. La durée de décodage réelle reste mesurée et annulable.
- Conserver une recherche par index spatial et voisinages bornés ; répartir les voisins aussi par temps lorsque de nombreuses signatures sont identiques. Ne pas comparer toutes les paires de frames natives.
- Vérifier explicitement le raccord fin→début de la source et les bornes proches des extrémités, sans exiger des images inexistantes hors du fichier.
- Conserver plusieurs solutions locales distinctes autour d'une piste lorsque leurs raccords ou durées diffèrent réellement. Dédupliquer avec des tolérances temporelles liées à la cadence et aux variantes ; regrouper les décalages insignifiants d'un même cycle.
- Appliquer la sensibilité de façon cohérente à la présélection et à l'affinage, avec des garde-fous indépendants contre les ruptures de scène et mouvements incompatibles.
- Publier les résultats par lots, sans mise à jour UI pour chaque image. Documenter les plafonds de travail et les raisons d'arrêt. Une limite interne ne doit pas être dissimulée derrière une annonce exhaustive.

Fichiers : `VideoLoopDetector.cs`, `VideoLoopFrameReader.cs`, `VideoLoopSettings.cs`, `CutVideoForm.Loops.cs`. Garder les signatures et le décodage dans Core ; ne pas faire dépendre l'algorithme du contrôle de liste.

Tests : `VideoLoopTests.cs`, nouveau `VideoLoopSearchCoverageTests.cs`, qualification `VideoLoopPerformanceTests.cs`. Vérifier plus de trois candidats, plus de 16 raccords répartis dans la source, première/dernière borne, séquence statique avec minimum supérieur au voisinage temporel de l'ancien index, cycles à moyenne couleur constante, raccords séparés de plusieurs minutes, source d'une heure et annulation à chaque phase. Contrôler déterminisme, diversité, couverture annoncée, absence d'explosion sur le statique et allocations bornées par bloc.

Critère de sortie : les bons raccords répartis dans la vidéo ne sont plus exclus par les seuls plafonds 3/16, et le coût observé reste compatible avec les budgets explicités.

## Lot 6 Affinage perceptuel et classement

État au 29 octobre 2026 : implémenté dans les sources, sans nouvel installateur. La présélection reste à 64×48 ; l'affinage conserve le ratio affiché, jusqu'à 192 pixels sur le grand côté, avec au plus 384 images par fenêtre et 24 paires vérifiées. La présélection des paires tient compte du mouvement signé et du raccord réel. Le contexte varie entre 120 et 350 ms, avec au plus cinq comparaisons temporelles ; les vitesses utilisent les intervalles natifs, y compris en VFR. Le score distingue apparence, éclairage, mouvement, continuité et rupture de scène. Les candidats sont classés par qualité, puis durée et début pour les ex aequo ; les variantes statiques sont regroupées par famille de durée. Les appréciations `Promising`, `Approximate` et `Needs review` utilisent des seuils fixes indépendants de la sensibilité, sans pourcentage. Auto conserve une seconde sur les sources ordinaires, avec adaptation aux sources courtes après résolution de la fin vidéo native. Voir [FIND_LOOP_VALIDATION](FIND_LOOP_VALIDATION.md#lot-6-perceptual-refinement-on-29-october-2026) pour les exports comparés et les limites de cette calibration.

Retenir d'abord le meilleur raccord ; conserver des durées différentes lorsque leur qualité est proche. L'allongement d'un cycle ne justifie pas à lui seul une moins bonne position.

Travail :

- Garder une petite définition pour la présélection, puis décoder les meilleurs voisinages à une définition supérieure conservant le ratio. Adapter les dimensions et calculs de tuiles ; ne pas réutiliser des constantes 64×48 sur un autre buffer.
- Comparer une durée de contexte adaptée au mouvement, avec un nombre d'images plafonné, plutôt que cinq images fixes quelle que soit la cadence. Tenir compte des intervalles natifs pour comparer des mouvements en VFR.
- Faire entrer apparence et continuité temporelle dans la présélection des paires affinées ; le top 24 actuel fondé uniquement sur l'apparence ne doit pas éliminer toutes les paires au mouvement cohérent.
- Conserver l'attention portée au petit premier plan et au raccord réel dernière→première image. Mesurer séparément saut spatial, éclairage, sens et amplitude du mouvement, pauses ou phases sautées et rupture de scène.
- Définir le cas statique sans division par une quantité de mouvement nulle. Regrouper ses variantes pour garder une liste utile.
- Dans chaque voisinage, classer les solutions par erreur de raccord avant la durée. Au classement global, préserver plusieurs durées et points de départ sans surreprésenter leurs quasi-doublons.
- Remplacer le pourcentage `100 × (1 − erreur)` par des appréciations courtes telles que Promising, Approximate et Needs review, calibrées sur le corpus. Les métriques numériques détaillées restent dans les logs ; aucune appréciation ne certifie un raccord parfait.
- Calibrer le minimum Auto sur le corpus pour éviter les micro-boucles sans éliminer inutilement les sources courtes. Les valeurs utilisateur avancées gardent leur sens explicite.

Fichiers : `VideoLoopDetector.cs`, `VideoLoopFrameReader.cs`, `VideoLoopSettings.cs`, `CutVideoForm.Loops.cs`. Extraire un petit `VideoLoopSeamEvaluator.cs` dans Core seulement si les mesures de raccord deviennent une responsabilité trop importante du détecteur.

Tests : `VideoLoopTests.cs`, nouveau `VideoLoopQualityTests.cs` et cas réels annotés du lot 0. Ajouter un petit objet qui change sur fond fixe, détail perdu à 64×48, éclairage local, bonne pose avec mauvais mouvement, saut de phase, pause artificielle, raccord long excellent contre court acceptable, cadences différentes et VFR équivalents. Vérifier dimensions portrait et large, mapping des appréciations, stabilité des ex aequo et dédoublonnage. Comparer les raccords exportés, pas seulement les scores internes.

Critère de sortie : le classement suit les défauts réellement introduits dans les tests et améliore les cas annotés ; aucune note visuellement flatteuse n'est produite uniquement parce que le candidat franchit le seuil Normal.

## Lot 7 Export naturel et budgets de ressources

État au 29 octobre 2026 : implémenté dans les sources, sans nouvel installateur. La voie naturelle de production écrit un cycle H.264/PCM dans un MOV temporaire, puis répète la vidéo par copie et encode l'audio assemblé une seule fois en AAC. Les horodatages VFR, le maintien final, la géométrie et la publication unique sont conservés. Le minimum de décodage/anticipation reste borné par les paramètres de l'encodeur, indépendamment de la durée ; les aperçus et l'inversion gardent leurs intermédiaires FFV1 adaptés. Les estimations distinguent les trois usages ; l'espace libre et les octets temporaires sont revérifiés pendant les processus et avant l'assemblage. Un arrêt non confirmé conserve les espaces possédés et signale un échec, au lieu de supprimer des fichiers encore ouverts. La voie par graphe de répétition décodée a été mesurée puis écartée pour son raccord VFR incorrect et son coût mémoire. La référence FFV1 de test et les mesures avant/après sont consignées dans [FIND_LOOP_VALIDATION](FIND_LOOP_VALIDATION.md#lot-7-natural-export-resources-on-29-october-2026).

Réduire le coût du mode naturel sans perdre les garanties déjà acquises sur le timing, l'audio et la publication des sorties. L'inversion par morceaux reste réservée à l'aller-retour.

Travail :

- Séparer l'estimation des ressources du naturel, de l'aller-retour et de l'aperçu. Calculer sur la définition de traitement et les artefacts réellement nécessaires ; réévaluer l'espace libre entre phases si le travail est long.
- Prototyper une voie naturelle avec intermédiaire compact encodé une fois puis répétitions compatibles, ou un graphe de répétition borné. Mesurer les deux sur le corpus et retenir une seule voie de production simple ; conserver la voie FFV1 comme référence tant que l'équivalence n'est pas démontrée.
- Vérifier la durée de maintien de la dernière image en un cycle et à chaque répétition. Garder la fin exclusive VFR et la correction de durée finale ; éviter tout doublon d'image.
- Préserver une seule continuité audio avant l'encodage AAC final : offset, silence initial, padding utile et absence de délai d'amorçage AAC ajouté à chaque jonction. Vérifier aussi les répétitions assemblées sans réencodage vidéo.
- Conserver géométrie affichée, SAR, rotation appliquée une seule fois, dimensions impaires, MP4/H.264 et noms uniques adjacents. Limiter la mémoire par la même discipline que les pipelines actuels.
- Pour l'aller-retour, garder les morceaux d'inversion bornés et compter la durée de sortie réelle aux trois vitesses. Les erreurs de ressources expliquent le besoin estimé et la phase concernée.
- Garder `OutputOperation` pour publier exclusivement après succès ; nettoyer les temporaires et sorties partielles après annulation ou échec. Vérifier l'arrêt des processus avant suppression.

Fichiers : `VideoLoopExporter.cs`, `VideoLoopSettings.cs`, `CutVideoAction.cs`. `OutputOperation`, `OutputPathHelper`, `FfmpegRunner` et `FfprobeRunner` restent les points de passage existants ; les modifier seulement pour un besoin établi par les tests.

Tests : cas d'export dans `VideoLoopTests.cs`, nouveaux `VideoLoopResourceTests.cs` et `VideoLoopPreviewTests.cs`. Vérifier 36 s en 1080p, source 4K, 1/3/10 répétitions, VFR et EOF, AAC avec audio décalé ou plus long que la vidéo, SAR non carré, rotation et dimensions impaires. Simuler espace insuffisant, collision à la publication, annulation dans chaque phase et arrêt non confirmé. Comparer les images et durées avec la voie de référence ; mesurer le coût disque avant/après.

Critère de sortie : les limites reposent sur la voie réellement utilisée ; les longues sélections naturelles qualifiées ne sont plus refusées par l'ancienne estimation uniforme, et les garanties temporelles/audio restent vérifiées.

## Lot 8 Conversion HDR vers SDR

État au 29 octobre 2026 : implémenté dans les sources, sans nouvel installateur. Rampes PQ/HLG et SDR BT.2020 qualifiées avec le FFmpeg embarqué ; contrôle de la courbe linéaire, du timing VFR, des tags et de la cohérence analyse/preview/export. Les sources HDR exigent une fonction de transfert explicite, des primaires BT.2020 et une matrice BT.2020 non constante. PQ utilise le maximum valide des pics mastering/MaxCLL disponibles ; absence de pic : 1000 nits. HLG emploie un écran de référence à 1000 nits. L'acceptation subjective sur vidéos HDR réelles reste à faire ; voir [validation](FIND_LOOP_VALIDATION.md#lot-8-hdr-to-sdr-conversion-on-29-october-2026).

Accepter les vraies sources HDR via une sortie SDR compatible, annoncée clairement. Une réduction de profondeur à 8 bits seule ne remplace pas cette conversion.

Travail :

- Vérifier les capacités du FFmpeg livré via `FfmpegRunner`, avec résultat mis en cache, puis choisir une chaîne logicielle disponible pour linéarisation, tone mapping, conversion de primaires/matrice/plage et sortie SDR BT.709. Ne pas ajouter de dépendance externe pour ce besoin.
- Centraliser la construction du filtre et des métadonnées de sortie dans `VideoLoopColorPolicy.cs`. Réutiliser la politique dans analyse, aperçu et export, tout en autorisant leur définition propre.
- Définir les traitements PQ et HLG, les métadonnées HDR absentes ou partielles et la valeur de repli éventuelle à partir de médias qualifiés. Garder un diagnostic explicite pour les profils incohérents ou non pris en charge.
- Indiquer HDR source → SDR output avant la recherche et utiliser Export SDR Loop comme commande principale lorsque la conversion s'applique. Ne pas ajouter de popup de confirmation répétée.
- Convertir correctement le SDR large gamut et le SDR 10 bits sans leur appliquer un tone mapping HDR. Supprimer les métadonnées HDR devenues inexactes et écrire les tags SDR nécessaires.
- Garantir que l'analyse et l'aperçu représentent le même rendu couleur que l'export. Le traitement couleur ne doit pas modifier les bornes ni la cadence.

Fichiers : `VideoLoopColorPolicy.cs`, `VideoLoopFrameReader.cs`, `VideoLoopExporter.cs`, `VideoLoopPreviewBuilder.cs`, `CutVideoForm.Loops.cs`, `MediaProbeResult.cs` et `FfprobeRunner.cs` si nécessaire.

Tests : `VideoLoopColorPolicyTests.cs`, nouveau `VideoLoopHdrTests.cs`, régressions de preview/export. Utiliser des rampes et plages de luminance/couleur déterministes pour PQ et HLG, du SDR 10 bits BT.709, du SDR BT.2020 et des métadonnées absentes ou contradictoires. Vérifier courbe, tags, format de pixels, absence de clipping massif/couleurs délavées sur les références, timing identique et cohérence aperçu/export. Tester l'absence du filtre requis et l'annulation pendant conversion. Compléter par une recette visuelle réelle.

Référence technique : [FFmpeg tonemap](https://ffmpeg.org/ffmpeg-filters.html#tonemap) et [zscale](https://ffmpeg.org/ffmpeg-filters.html#zscale). Le filtre tonemap exige notamment des données linéarisées ; qualifier la chaîne exacte avec le binaire embarqué.

Critère de sortie : un HDR confirmé qualifié peut produire une boucle SDR annoncée, les SDR 10 bits ne sont plus refusés comme HDR et aucun filtre requis ne manque silencieusement à l'exécution.

## Lot 9 Export des candidats par lot

État au 29 octobre 2026 : implémenté dans les sources, sans nouvel installateur. Le bouton secondaire du footer capture les candidats dédoublonnés dans l'ordre affiché, avec bornes natives, réglages et identité du fichier source. Chaque candidat devient une requête indépendante. Un reporter ciblé route progression, états, suppression et annulation vers l'identifiant de son occurrence ; le chemin réel reste séparé des libellés. Le parcours modal jusqu'à la fenêtre commune et les sorties décodées sont vérifiés dans [validation](FIND_LOOP_VALIDATION.md#lot-9-batch-candidate-export-on-29-october-2026).

Ajouter Export All Candidates (N) comme commande secondaire du footer, uniquement lorsque des candidats validés sont disponibles. Le bouton exporte la liste affichée et dédoublonnée ; la commande principale continue d'exporter le candidat choisi ou la sélection personnalisée.

Travail :

- Capturer une liste immuable de sélections d'export et de leurs bornes natives au clic. Une édition personnalisée ne remplace pas implicitement tous les candidats.
- Adapter le retour de `EnsureCutVideoOptions` pour distinguer le parcours CLI/sélection unique de la liste de requêtes UI. Construire un `ActionRequest` par candidat, chacun avec ses propres options, puis utiliser `RunQueuedRequestsWithProgressForm`.
- Garder une occurrence distincte pour chaque candidat malgré le chemin source identique. Afficher son numéro et sa durée dans la progression ou les détails, sans transformer le libellé en faux chemin de fichier.
- Vérifier les identifiants de ligne pour les transitions d'état, l'annulation et la suppression. `ActionQueueRunner` sait déjà retirer une occurrence par ID, mais plusieurs appels de progression/annulation restent fondés sur le chemin : ajouter un routage par occurrence ciblé si les tests révèlent une ambiguïté.
- Exécuter séquentiellement par la queue commune. L'annulation de l'élément courant laisse les autres disponibles ; l'annulation globale arrête le reste du lot. Une erreur de candidat reste visible avec sa cause.
- Conserver les sorties réussies lors d'un échec ultérieur, supprimer uniquement les sorties partielles possédées et ne jamais écraser. Associer numéro de candidat et bornes aux logs/détails et conserver les suffixes de collision uniques adjacents.
- Afficher le nombre de sorties prévues dans le libellé de commande. Une confirmation supplémentaire n'est pas nécessaire pour ce parcours déjà explicite.

Fichiers : `VideoLoopSettings.cs`, `CutVideoForm.Loops.cs`, `src/FrameShift/ProgramPickersCut.cs`, `Program.cs`, `ProgramBatch.cs`, `Core/Actions/CutVideoAction.cs`, `ActionQueueRunner.cs`. Vérifier `Core/Progress/IProgressReporter.cs`, `Windows/ProgressUI/ProgressForm.cs` et ses partials ; n'étendre ces contrats que pour les besoins d'identité réellement manquants, avec compatibilité des reporters existants.

Tests : nouveau `VideoLoopBatchExportTests.cs`, `ActionQueueRemovalTests.cs`, `VideoLoopUiTests.cs`, tests des reporters communs affectés. Couvrir deux candidats d'une même source avec bornes différentes, export de toute la liste, état indépendant de chaque ligne, suppression du deuxième doublon, annulation courante puis globale, échec intermédiaire, changements de source, collisions préexistantes et audio propre à chaque requête. Inspecter les images/durées des fichiers obtenus pour prouver que le dernier dictionnaire d'options n'a pas été réutilisé pour tout le lot.

Critère de sortie : N candidats produisent N exports indépendants dans la fenêtre commune, avec bornes correctes et commandes de queue sans ambiguïté.

## Lot 10 Qualification et documentation

État au 29 octobre 2026 : suite Release complète, campagne de neuf mesures recherche/aperçu/export, six nouveaux cas chemins/échecs, médias locaux, export UNC, payload, construction canonique et réinstallation de même version exécutés. Documentation et checksum synchronisés. **Recette manuelle complète non terminée** : demande de sécurité Windows à traiter par le propriétaire, un seul écran actif, aucun clip HDR réel disponible. Installation vierge, mise à jour depuis la version publiée, désinstallation et clic réel Explorer restent à recetter ; la réinstallation existante et la commande enregistrée ne les remplacent pas. Voir le rapport du lot 10 pour poursuivre sans refaire les contrôles déjà exécutés.

Finaliser les choix à partir des mesures et de la recette avant de construire un installateur de test.

- Exécuter les régressions existantes et nouvelles, puis une comparaison avant/après du corpus. Conserver les exemples où une vidéo ne présente aucun raccord naturel convaincant ; le logiciel décrit le résultat de sa recherche sans promettre que toute vidéo possède une boucle.
- Mesurer recherche, aperçu et export sur les durées du lot 0, sur machine identifiée. Définir les plafonds mémoire et disque à partir de ces mesures. Vérifier qu'une annulation interrompt aussi le décodage et libère les fichiers ; les délais de processus restent ceux des runners.
- Vérifier réellement Light/Dark, DPI 100/150/200/300 %, texte agrandi, petite fenêtre et changements de moniteur. Contrôler listes longues, descriptions couleur, footer et navigation clavier ; les tests de layout cachés ne remplacent pas ces contrôles.
- Tester chemins avec espaces, accents, apostrophes, noms longs et UNC, disque plein, source disparue/modifiée et destination devenue inaccessible. Vérifier absence de console et de processus FFmpeg orphelin.
- Mettre à jour `FIND_LOOP.md`, `FIND_LOOP_VALIDATION.md`, `CODE_FILE_INDEX.md`, `UI_STANDARDIZATION.md` pour la variante Cut Video/Find Loop et `CHANGELOG.md` avec le comportement effectivement livré. Mettre à jour `ARCHITECTURE_FREEZE.md` si ses contrats évoluent.
- À la préparation de release, synchroniser version `.csproj`/ISS/changelog selon les règles du projet, puis exécuter exclusivement `build_installer.ps1`. Les modifications de contrats et les fonctionnalités ne sont pas présentées comme une simple correction de texte.
- Vérifier le payload et les hashes du nouvel installateur. Recetter installation, mise à jour et entrée Explorer Cut Video sur le binaire construit ; consigner la version réellement utilisée. Installation et publication restent des actions distinctes de l'élaboration du plan.

Fichiers : documentation citée, `tests/FrameShift.Tests/*VideoLoop*Tests.cs`, `src/FrameShift/FrameShift.csproj`, `installer/FrameShift.iss`, `build_installer.ps1` en utilisation du workflow canonique. Réutiliser les contrôles de release et de payload existants.

Critère de sortie : parcours simple validé sur le binaire candidat, régressions vertes, raccords et couleurs évalués sur corpus, budgets mesurés et limites restantes consignées avec leur portée.

## Matrice minimale de validation

| Domaine | Cas obligatoires | Preuve attendue |
|---|---|---|
| Parcours UI | Coupe, boucle, retour, liste vide, sélection personnalisée, aller-retour | Commande affichée = action exécutée ; footer et clavier corrects |
| Couverture | Plus de 3 résultats, plus de 16 pistes réparties, début/fin, longues vidéos | Candidats attendus trouvés ; couverture/arrêt explicités |
| Qualité | Court médiocre/long meilleur, mouvement opposé, petit objet, lumière, statique | Ordre cohérent et défauts réellement détectés dans le rendu |
| Temps | CFR/VFR, EOF, offsets, 23,976/59,94/240 FPS | Frames exactes, fin exclusive et maintien final conservés |
| Aperçu | Raccord court, cycle complet, 36 s/1080p, sélection 4K, aller-retour | Proxy borné, silence, vrai raccord interne ; deux raccords pour trois cycles complets |
| Couleur | SDR 8/10 bits, large gamut, PQ, HLG, métadonnées incertaines | Classification correcte, tags et pixels SDR cohérents |
| Audio export | Présent/absent, mute, offset, AAC répété | Durée et offset conservés sans amorçage ajouté à chaque cycle |
| Queue | Plusieurs occurrences d'une même source | Bornes, états et commandes indépendants par candidat |
| Ressources | Long, haute définition, espace insuffisant, annulation/échec | Budgets expliqués, nettoyage et terminaison confirmés |
| Windows | Thèmes, DPI, texte agrandi, clavier, espaces/accents/UNC | Recette réelle et intégration Explorer documentées |

## Discipline de validation pendant implémentation

Après chaque modification de code, exécuter au minimum `dotnet build src/FrameShift/FrameShift.csproj` avec le SDK configuré du dépôt, puis les tests ciblés du lot. Les commandes de sélection des tests utilisent les classes effectivement ajoutées, sans transformer une sélection vide en preuve de succès.

Les tests unitaires ciblent les décisions et les cas limites ; les tests médias inspectent les sorties décodées ; les tests UI couvrent les commandes et la durée de vie asynchrone. Les mesures longues utilisent une qualification dédiée, sans rendre la suite courante dépendante d'une heure de décodage ou d'un clip privé. La chaîne canonique de release exécute sa suite Release obligatoire sans exclusion ajoutée pour contourner une régression.

Les seuils d'acceptation chiffrés de qualité et de performance seront fixés après le lot 0, puis conservés pour les comparaisons. Un build vert prouve la compilation ; les tests de timing, les mesures de ressources et l'examen perceptuel constituent des preuves complémentaires avec des objectifs différents.
