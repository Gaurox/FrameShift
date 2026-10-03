# FrameShift — Plan correctif des quatre P1

**Date et relecture finale :** 3 octobre 2026.

**Base vérifiée :** FrameShift 1.20.0, révision `977d380ebfbf48baaed3036ffa704f8c7edc9319`.

**Référence :** [audit de sécurité](E:/AI/FrameShift_V1/docs/SECURITY_AUDIT_2026-10-03.md).

**Statut :** application commencée sur `codex/security-p1`. L'installateur, les lectures restreintes, les sorties et les contrôles du runtime sont implémentés et testés localement. La migration ImageSharp 4.1.2 attend une licence de compilation valide ; la qualification installée et la clôture des quatre P1 restent à réaliser. Voir le [suivi d'application](E:/AI/FrameShift_V1/docs/SECURITY_P1_IMPLEMENTATION_2026-10-03.md).

## Organisation : trois phases

| Phase | Travail | Résultat attendu |
|---|---|---|
| **1. Installateur et dépendances** | Corriger FS-SEC-01, mettre à niveau ImageSharp et restreindre ses décodeurs (02), préparer le runtime corrigé et son contrôle de distribution (04). | Code compilable, tests ciblés réussis, versions et prérequis de compilation maîtrisés. |
| **2. Conservation des fichiers** | Corriger FS-SEC-03 avec un mécanisme commun de publication sans remplacement ; migrer les producteurs par famille. | Aucune écriture ou suppression finale dangereuse dans le périmètre ; tests de collisions et d'annulation réussis. |
| **3. Distribution vérifiée** | Construire par la chaîne officielle et qualifier cette candidate installée. | Preuves de clôture des quatre P1 sur les artefacts destinés à être distribués. |

C'est l'ordre recommandé. Un prérequis externe de phase 1, notamment la licence de compilation ImageSharp, ne doit pas empêcher le travail indépendant sur les sorties en phase 2. **La phase 3 exige que les deux premières soient terminées.**

Chaque lot comprend sa correction et ses tests, dans des commits courts. Conserver C#/.NET 8/WinForms/Inno Setup et les runners existants. Les autres P2/P3 restent dans l'audit ; les fichiers auxiliaires susceptibles d'être écrasés font toutefois partie intégrante de FS-SEC-03.

## Phase 1 — Installateur et dépendances

### 1.1 — Installateur : retirer l'exécution non fiable (FS-SEC-01)

**Constat vérifié :** [la détection](E:/AI/FrameShift_V1/installer/FrameShift.iss:159) peut assembler des informations HKLM/HKCU ; [l'exécution](E:/AI/FrameShift_V1/installer/FrameShift.iss:238) lance ensuite le désinstalleur indiqué par le registre avec les droits du setup.

**Correction retenue :**

- Retirer de l'assistant le lancement direct fondé sur `UninstallString`, son option de désinstallation et le code devenu inutilisé.
- Conserver la mise à jour/réinstallation dans le setup et la désinstallation enregistrée par Inno Setup, accessible depuis Windows.
- Pour détecter l'installation machine, lire les informations utiles dans une même clé/vue HKLM. Une entrée HKCU ne doit ni compléter une valeur machine manquante ni fournir une commande.
- Si une ancienne installation utilisateur est détectée, l'indiquer sans lancer sa commande ni effacer son enregistrement.

**Conséquence utilisateur explicite :** la désinstallation reste possible depuis Windows ; son raccourci dans l'assistant de mise à jour disparaît. Mentionner ce changement dans les notes de correction.

Cette solution évite d'ajouter un système de validation d'exécutables, de signatures et de droits NTFS à l'installateur. Un simple filtre HKLM ou un préfixe `Program Files` ne suffirait pas à garantir qu'un exécutable est protégé. Remplacer uniquement `Exec` par `ExecAsOriginalUser` n'est pas non plus une garantie de contexte non élevé. [Documentation Inno](https://jrsoftware.org/ishelp/topic_isxfunc_execasoriginaluser.htm).

**Validation :** revue de l'absence de ce chemin d'exécution ; recette de l'installation/mise à jour/désinstallation Windows et d'entrées HKCU trompeuses en phase 3. Un test textuel du script ne remplace pas cette recette.

### 1.2 — ImageSharp : mise à niveau et lectures restreintes (FS-SEC-02)

**Cible vérifiée : ImageSharp 4.1.2.** Elle prend en charge .NET 8 et inclut les corrections TIFF ainsi que les correctifs supplémentaires de métadonnées qui justifient de dépasser 4.1.1. [Paquet officiel](https://www.nuget.org/packages/SixLabors.ImageSharp/4.1.2), [avis TIFF](https://github.com/SixLabors/ImageSharp/security/advisories/GHSA-jj3q-cwqj-842r), [avis ICC](https://github.com/SixLabors/ImageSharp/security/advisories/GHSA-gwg2-r3hj-4w44).

**Correction retenue :**

1. Créer un petit helper Core de lecture ImageSharp avec une configuration explicite : PNG, JPEG, WebP et BMP. Ne pas enregistrer TIFF ni les autres décodeurs inutiles.
2. Passer cette configuration à toutes les lectures `Identify` et `Load` concernées. Si une détection préalable est utilisée, elle doit précéder l'identification détaillée et ne pas déclencher un décodeur non autorisé. Aucun repli vers les options par défaut après un refus.
3. Couvrir le [précontrôle de l'upscale](E:/AI/FrameShift_V1/src/FrameShift/ProgramAiPreflight.cs:173), les trois moteurs images IA, le [chargement WebP pour PDF](E:/AI/FrameShift_V1/src/FrameShift/Core/Helpers/ImageToPdfPdfExporter.cs:54), et les lectures BMP de RIFE/upscale vidéo. La protection du chargement doit s'appliquer à chaque image du lot et aux parcours CLI.
4. Migrer le paquet, adapter seulement les API réellement affectées, puis actualiser les verrouillages application/tests/UiSamples et les notices.

La restriction porte sur les **lectures ImageSharp**. Elle ne doit pas retirer des formats actuellement traités par FFmpeg ni transformer tous les aperçus GDI/PDFsharp en une nouvelle chaîne de décodage. `LoadPixelData` du rawvideo ne sélectionne pas un décodeur de fichier ; conserver ce chemin et tester sa compatibilité avec la nouvelle bibliothèque.

Éviter les contrôles répétés dans toutes les couches UI/Core : le helper doit garantir la restriction au moment de la lecture réelle. Il n'est pas nécessaire de réorganiser la recherche des modèles IA pour fermer cette faille. Ne pas modifier `Configuration.Default` globalement pendant les traitements.

**Prérequis de compilation :** ImageSharp 4 demande un `sixlabors.lic` valide pour une dépendance directe. Aucun fichier de ce type n'a été trouvé dans l'inventaire du dépôt. Vérifier les modalités communautaires applicables au projet GPL v3 et prévoir son chemin de compilation. Le fichier d'exemple de l'éditeur, expiré le 4 septembre 2026, ne convient pas. [Instructions Six Labors](https://sixlabors.com/posts/licence-enforcement-changes/).

La restriction des décodeurs peut être préparée avec la version actuelle pendant la résolution de ce prérequis. Elle ne permet pas à elle seule de déclarer la migration terminée. La compatibilité du framework ne prouve pas la compatibilité des API ou du rendu.

**Tests ciblés :**

- Helper : TIFF inoffensifs renommés `.png`, `.jpg` et `.webp` refusés ; tests exécutables sans modèles IA ni GPU.
- Intégration : précontrôle `Identify`, chargements IA et PDF WebP utilisent la restriction ; aucun retour aux décodeurs par défaut.
- Non-régression : PNG/JPEG/WebP/BMP, transparence, dimensions et couleurs sur un petit corpus représentatif ; export PDF et pipelines BMP/rawvideo. Fixer explicitement les options de profil de couleur si la migration modifie le rendu attendu.
- Compilation Debug/Release et tests de traitement affectés ; courte recette d'inférence réelle en phase 3.

### 1.3 — .NET : corriger la chaîne puis contrôler le payload (FS-SEC-04)

**État reconfirmé :** SDK installé 8.0.420 ; runtime publié 8.0.26 dans l'application et le worker. **Cible : SDK 8.0.425 / runtime 8.0.31**, publiés par Microsoft le 8 septembre 2026. [Notes Microsoft](https://github.com/dotnet/core/blob/main/release-notes/8.0/8.0.31/8.0.31.md).

**Correction retenue :**

1. Préparer le SDK choisi et un `global.json` qui le fixe exactement, avec `rollForward: disable` et préversions désactivées. La version sera révisée explicitement lors des prochains correctifs de maintenance.
2. Vérifier que restore et publish utilisent des paramètres cohérents pour l'application et le worker : Release, `win-x64`, publication autonome et runtime choisi. Examiner aussi la restauration des références depuis le projet de tests afin qu'elle ne remplace pas ces assets par une configuration incompatible.
3. Conserver le mode verrouillé après régénération intentionnelle des fichiers concernés. Utiliser le runtime fourni par le SDK choisi si cela suffit ; n'ajouter un `RuntimeFrameworkVersion` partagé que si la vérification révèle un besoin.
4. Étendre [Assert-PublishPayload](E:/AI/FrameShift_V1/build_installer.ps1:233) avant Inno : runtime .NET et Windows Desktop de l'application, runtime .NET du worker, version du paquet ImageSharp, manifests et fichiers requis cohérents.

Le script publie avec `--no-restore` : le choix du runtime a donc déjà eu lieu lors de la restauration. Ajouter une version uniquement à la dernière commande ne corrige pas nécessairement les assets restaurés. [Documentation Microsoft](https://learn.microsoft.com/en-us/dotnet/core/deploying/runtime-patch-selection).

Comparer les versions de paquets dans `deps.json` et `includedFrameworks` dans `runtimeconfig.json`. Une version d'assembly seule n'identifie pas nécessairement la version NuGet. Vérifier également les binaires runtime réellement copiés, avec une comparaison adaptée à leur version produit ; ne pas modifier les manifests pour masquer un ancien binaire.

**Tests ciblés :** adapter [ReleaseScriptTests](E:/AI/FrameShift_V1/tests/FrameShift.Tests/ReleaseScriptTests.cs:11) pour produire de vrais exemples de manifests et tester : application ancienne, worker ancien, ImageSharp ancien, fichier requis absent et manifests incohérents. Chaque échec doit arrêter la chaîne avant Inno ; le cas conforme doit passer.

**Sortie de phase 1 :** code compilable et tests ciblés verts. La preuve du runtime distribué et la recette de l'installateur attendent la phase 3. Si l'implémentation intervient plus tard, vérifier de nouveau les avis éditeurs avant de figer les versions.

## Phase 2 — Garantir la conservation des fichiers (FS-SEC-03)

### Mécanisme commun, limité au besoin

[OutputPathHelper](E:/AI/FrameShift_V1/src/FrameShift/Core/Helpers/OutputPathHelper.cs:9) reste responsable des noms et suffixes. Il propose un nom ; la publication sans remplacement arbitre les collisions. Ajouter une petite classe de sortie en cours, avec le minimum nécessaire pour fichiers et dossiers, sans framework de transactions.

**Cycle à appliquer :**

1. Créer un dossier de travail unique **dans le dossier parent des sorties finales**. Il est propre à l'opération et créé exclusivement : ne jamais réutiliser un dossier préexistant. Sous Windows, un petit wrapper `CreateDirectoryW` permet de distinguer création réussie et `ERROR_ALREADY_EXISTS`. [Contrat Windows](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createdirectoryw).
2. Écrire à l'intérieur, en gardant la véritable extension du résultat. Pour une extraction, utiliser un sous-dossier contenant uniquement les images à publier.
3. Laisser les bibliothèques existantes écrire dans ce dossier détenu. `-y` peut y rester pour FFmpeg et ses reprises. `FileMode.CreateNew` sert quand une création exclusive de fichier est nécessaire ; ne pas imposer une nouvelle API de flux à tous les encodeurs.
4. Après succès, terminer les processus, fermer les writers et vérifier le résultat attendu. Vérifier l'annulation avant de publier.
5. Déplacer le fichier avec `File.Move(..., overwrite: false)`, ou le sous-dossier avec `Directory.Move`. En cas de collision, essayer le suffixe suivant sur le résultat déjà calculé, sans relancer le traitement. Les noms occupés par un fichier **ou un dossier** doivent être sautés. [Contrat File.Move](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-8.0).
6. Marquer immédiatement le résultat comme publié, puis annoncer le chemin réellement obtenu. Nettoyer uniquement le dossier de travail détenu. Une annulation ou une erreur de notification après publication ne doit pas supprimer le résultat.

Distinguer collision et autre panne : pas de boucle aveugle sur toute `IOException`. Borner les tentatives ; signaler les accès refusés, verrous et erreurs d'E/S. Si la publication échoue, l'action signale l'échec et applique son nettoyage au seul travail détenu.

Le dossier de travail et les sorties ayant le même parent, le déplacement local n'impose pas une copie supplémentaire de la vidéo. Ne pas ajouter de stratégie copie/suppression entre volumes ni de remplacement en secours. **Pas de nouvelle interdiction générale des jonctions ou des dossiers réseau** : conserver les chemins usuels ; si l'opération sans remplacement échoue, retourner une erreur propre. Tester les supports effectivement utilisés. Ce mécanisme traite les collisions et nettoyages ; il ne prétend pas isoler l'application d'un logiciel malveillant possédant les mêmes droits utilisateur.

### Migration par familles

L'inventaire confirme **37 fichiers consommateurs** : 29 fichiers d'actions et 8 fichiers IA, hors helper. Ce nombre désigne les points à examiner, pas 37 vulnérabilités distinctes. Inclure aussi les écrivains auxiliaires qui n'appellent pas directement le helper.

| Ordre interne | Périmètre | Attention particulière |
|---|---|---|
| 1 | Classe de sortie, puis `ConvertImageAction` pilote | Rejouer la sentinelle de l'audit avant de généraliser. |
| 2 | Conversions et actions audio/vidéo/image, GIF, icônes, assemblage, sous-titrage vidéo | Écrire au chemin de travail ; retourner le nom final réel. |
| 3 | RIFE, upscale vidéo et autres reprises d'encodage | Chaque tentative nettoie uniquement son résultat de travail ; conserver les replis CPU/GPU. |
| 4 | Images IA, PDF, WAV IA et séparation audio | Fermer les writers avant publication ; couvrir l'échec d'ouverture d'un writer supplémentaire. |
| 5 | Extraction de dossiers, première/dernière image, sous-titres et fichiers auxiliaires | Adapter publication et nettoyage aux cas ci-dessous. |

**Cas particuliers à préserver :**

- **Première image :** elle écrit actuellement directement au nom final ; elle doit être migrée complètement. **Dernière image et sous-titres :** ils utilisent déjà un déplacement sans remplacement ; conserver cette garantie et ajouter la reprise sur collision.
- **Dossiers d'extraction :** déplacer le sous-dossier de résultats entier ; ne pas créer, fusionner ou nettoyer récursivement un dossier final sélectionné par son seul nom.
- **Plusieurs WAV :** produire et finaliser tous les fichiers avant publication. Les déplacements restent individuels. En cas d'échec intermédiaire, conserver les fichiers déjà publiés et signaler le résultat partiel, avec leurs chemins dans le message/journal existant. Ne pas ajouter de transaction globale ni supprimer les résultats par leurs noms pour annuler le lot.
- **Sous-titres :** retirer le nettoyage de `outputPath + ".tmp"` si ce fichier n'a pas été créé par l'opération. Traiter aussi le `.diagnostic.json` adjacent sans remplacement, avec un nom lié au résultat final. Une erreur de diagnostic facultatif est signalée sans transformer un sous-titre valide en sortie perdue.
- **Annulation tardive :** après publication d'un résultat unique, conserver le résultat et annoncer qu'il a été créé. Pour un lot partiellement publié, indiquer précisément les fichiers déjà disponibles. Ne pas afficher « rien produit » alors qu'un fichier existe.

Pour chaque consommateur, relever destination de travail, publication, reprises et nettoyage. Examiner tous les `-y` restants ; ne pas modifier globalement le runner, qui gère également flux et intermédiaires.

### Tests nécessaires

| Scénario | Résultat attendu |
|---|---|
| Deux opérations, puis deux processus, visant le même nom | Deux résultats distincts et complets ; aucun écrasement. Synchroniser les tests par barrières, pas uniquement par temporisations. |
| Sentinelle fichier/dossier créée entre choix et déplacement | Contenu inchangé ; choix du suffixe suivant. |
| Échec ou annulation avant publication, pendant une reprise, juste après publication | Nettoyage du travail propre uniquement ; fichier étranger et résultat publié conservés. |
| Dossier de travail préexistant ou dossier d'extraction final concurrent | Aucun dossier adopté, fusionné ou supprimé. |
| Destination verrouillée, droits insuffisants, panne d'E/S simulée, suffixes épuisés | Erreur bornée et lisible ; aucune suppression étrangère. |
| Deuxième writer ou deuxième publication d'un lot en erreur | Handles fermés ; résultats déjà publiés conservés et signalés. |
| Diagnostic ou ancien `.tmp` adjacent préexistant | Fichier préexistant inchangé ; sous-titre valide conservé. |

Valider le mécanisme sur disque, puis les intégrations par famille. Ajouter des traitements réels représentatifs avec les runners et FFmpeg/FFprobe du projet : conversion, extraction, reprise d'encodage et PDF. Vérifier espaces/accents, annulation, absence de console et absence de processus restant. Les tests de la classe commune ne suffisent pas à prouver que toutes les actions l'utilisent correctement.

**Sortie de phase 2 :** inventaire de migration terminé, tests ciblés réussis, aucun producteur ou nettoyage final dangereux restant dans ce périmètre.

## Phase 3 — Construire et vérifier la distribution

1. Préparer une version de correction — **1.20.1 proposée** — avec notes et notices actualisées. Conserver l'identité de la release 1.20.0 pour la recette de mise à jour.
2. Exécuter [build_installer.ps1](E:/AI/FrameShift_V1/build_installer.ps1:301) depuis un état Git propre : restore verrouillé, **suite Release complète sans filtre**, nettoyage contrôlé, publication autonome, contrôle du payload, compilation Inno. Garder l'isolation des tests WinForms prévue par le projet.
3. Relever commit, versions et empreintes de la candidate. Vérifier l'absence d'anciens composants, de tests et de fichiers de travail dans le payload. Les tests ignorés sont identifiés avec leur raison.
4. En VM Windows avec snapshot, vérifier installation neuve, mise à jour depuis 1.20.0, réinstallation, installation personnalisée/core seul, menus Explorer et désinstallation Windows. Contrôler la conservation des modèles et fichiers étrangers selon la [checklist existante](E:/AI/FrameShift_V1/docs/RELEASE_CHECKLIST.md).
5. Dans cette VM, utiliser des entrées HKCU trompeuses et un témoin inoffensif, avec données HKLM absentes ou incomplètes : aucune exécution du témoin. Tester l'élévation par le même compte puis par un autre compte. La machine de développement n'est pas utilisée pour ce scénario de privilèges.
6. Sur le **payload installé**, rejouer les refus de TIFF sur les chemins ImageSharp exposés, les collisions à deux instances et les annulations ; exécuter un export PDF et une courte recette IA/vidéo représentative, avec CPU et DirectML lorsque ces chemins sont affectés. Réutiliser les modèles disponibles. Consigner les limites matérielles ; un contrôle indispensable non exécuté reste ouvert.
7. Enregistrer les résultats dans une qualification de cette candidate. Toute reconstruction produit un nouvel artefact à identifier et à vérifier avant diffusion.

### Clôture des quatre P1

| P1 | Preuve indispensable |
|---|---|
| **FS-SEC-01** | Exécution élevée issue de `UninstallString` retirée ; témoin HKCU non exécuté ; installation, mise à jour et désinstallation Windows légitimes validées. |
| **FS-SEC-02** | Paquet corrigé embarqué ; compilation/licence résolues ; lectures ImageSharp restreintes ; formats et pipelines concernés fonctionnels. |
| **FS-SEC-03** | Tous les producteurs concernés examinés/migrés ; concurrence fichiers/dossiers/lots et annulation testées ; aucune sortie étrangère ou publiée supprimée. |
| **FS-SEC-04** | Runtime approuvé effectivement installé pour application et worker ; contrôle de release rejetant les anciens composants ; démarrage autonome et traitements réussis. |

Les 98 tests réussis lors de l'audit et la qualification précédente de 1.20.0 ne valident pas les futurs changements. Un échec bloque la diffusion et renvoie au lot concerné. Les commits séparés permettent de corriger ou revenir sur ce lot dans la branche de travail ; une ancienne version ne redevient pas sécurisée par ce retour arrière.

## Résultat de la relecture finale

**Plan retenu après corrections.** Les trois phases sont suffisantes ; aucune phase supplémentaire ni nouveau framework n'est nécessaire.

Les simplifications apportées sont les suivantes : une seule stratégie de publication des sorties ; pas de réécriture générale des encodeurs ou des aperçus ; pas de filtrage global des chemins Windows ; pas de réorganisation des modèles IA ; pas de blocage des travaux indépendants par la licence ; distinction corrigée entre première et dernière image extraite ; annulation tardive et résultats partiels définis explicitement.

Vérifications réalisées pour cette relecture : code des points sensibles, inventaire des 37 consommateurs, appels ImageSharp, manifestes de runtime, SDK disponible, chaîne de release et documentation des éditeurs. Les liens locaux du document sont contrôlés après écriture. Cette étape modifie uniquement le plan : les nouveaux tests de correction, la compilation avec les dépendances cibles et la recette en VM restent à exécuter pendant l'implémentation.
