# Audit de sécurité de FrameShift — 3 octobre 2026

**Version examinée :** 1.20.0.

**Dépôt :** `E:\AI\FrameShift_V1`.

**Révision :** `977d380ebfbf48baaed3036ffa704f8c7edc9319`.

**Livrable :** problèmes à corriger, ordre de priorité, corrections recommandées et critères de validation.

**État :** audit réalisé ; aucune correction du code applicatif effectuée.

## 1. Résultat et ordre de priorité

Les premières corrections doivent porter sur l'exécution privilégiée d'une commande issue du registre utilisateur, les décodeurs ImageSharp vulnérables, les collisions de fichiers de sortie et le runtime .NET embarqué. Ensuite viennent la sécurité du stockage des modèles, les communications entre instances et les limites de ressources.

Le rapport contient **18 constats : 4 P1, 9 P2 et 5 P3**. Ils comprennent des défauts reproduits, des défauts établis par lecture du code, des dépendances concernées par des avis éditeurs et des mesures de durcissement. Ces catégories sont indiquées explicitement : tous les constats ne constituent pas une exploitation démontrée.

| Priorité | Signification |
|---|---|
| P0 | Urgence immédiate, exploitation critique démontrée. Aucun constat classé P0 dans cet audit. |
| P1 | À traiter avant la prochaine distribution : exécution privilégiée, corruption de mémoire potentielle, perte de données ou correctifs de sécurité manquants. |
| P2 | À traiter dans le cycle de sécurisation suivant : protection des fichiers, disponibilité, isolation locale et confidentialité. |
| P3 | Durcissement et maintenance à planifier après P1/P2. |

Cet ordre exprime une **priorité de correction adaptée à un utilitaire Windows local**, pas un score CVSS.

| Ordre | Identifiant | Priorité | Problème | État de la preuve |
|---:|---|---|---|---|
| 1 | FS-SEC-01 | P1 | L'installateur peut exécuter en administrateur un désinstalleur désigné par HKCU | Chemin établi dans le code ; exploitation UAC non exécutée |
| 2 | FS-SEC-02 | P1 | ImageSharp 3.1.12 concerné par des défauts TIFF ; l'extension ne bloque pas le décodeur | Avis éditeurs + accès au décodeur reproduit avec un fichier inoffensif |
| 3 | FS-SEC-03 | P1 | Une collision de sortie peut écraser ou supprimer un fichier appartenant à une autre opération | Écrasement reproduit |
| 4 | FS-SEC-04 | P1 | Application et worker publiés avec .NET 8.0.26, en retard sur les correctifs de sécurité | Artefacts locaux + notes Microsoft |
| 5 | FS-SEC-05 | P2 | Les jonctions contournent les protections du répertoire de modèles | Contournement reproduit dans un dossier de test |
| 6 | FS-SEC-06 | P2 | Le test d'écriture des modèles détruit un fichier `.writetest` préexistant | Suppression reproduite |
| 7 | FS-SEC-07 | P2 | Identité et espace de noms des pipes interinstances insuffisamment protégés | Configuration constatée ; attaque entre comptes non exécutée |
| 8 | FS-SEC-08 | P2 | Un client de pipe silencieux peut bloquer l'écoute ; limites inégales selon les actions | Lecture du code |
| 9 | FS-SEC-09 | P2 | Contrôles de ressources trop tardifs ou absents pour certaines entrées | Lecture du code |
| 10 | FS-SEC-10 | P2 | Sorties des processus non bornées et sondes sans délai interne | Lecture du code |
| 11 | FS-SEC-11 | P2 | Téléchargements de modèles sans plafond d'octets ni délai d'inactivité | Lecture du code |
| 12 | FS-SEC-12 | P2 | Fichier temporaire de téléchargement partagé et remplacement non atomique | Lecture du code ; collision non provoquée |
| 13 | FS-SEC-13 | P2 | La télémétrie ONNX Runtime n'est pas explicitement désactivée | Code + documentation de la version ; transmission non observée |
| 14 | FS-SEC-14 | P3 | Les DLL natives du worker ne sont pas contrôlées par le garde-fou de release | Absence de contrôle constatée ; empreintes actuelles correctes |
| 15 | FS-SEC-15 | P3 | Application et installateur locaux non signés Authenticode | Vérification Windows |
| 16 | FS-SEC-16 | P3 | Diagnostics contenant chemins et transcriptions produits sans option dédiée | Lecture du code |
| 17 | FS-SEC-17 | P3 | Nettoyage des temporaires incomplet et propriété des sessions insuffisamment vérifiée | Lecture du code |
| 18 | FS-SEC-18 | P3 | Politique de sécurité et contrôle des dépendances à mettre à jour | Documentation et scripts examinés |

## 2. Périmètre et méthode

### Surfaces examinées

- Inventaire et recherche de motifs de sécurité sur les **271 fichiers C# applicatifs** et les **73 fichiers C# de tests**, hors `bin` et `obj` ; lecture approfondie des chemins critiques identifiés.
- Programme principal, arguments CLI, lancement des actions et intégration Explorer.
- FFmpeg/FFprobe : arguments, résolution des exécutables, captures de sortie, annulation et cycle de vie des processus.
- Actions audio/vidéo/image, sous-titres, PDF, noms de sortie, temporaires et nettoyage.
- Modules IA : modèles, téléchargement, vérification SHA-256, configuration des dossiers, inférence et worker de transcription.
- Pipes et mutex utilisés par les files de traitement, Join Videos et Image to PDF.
- Scripts de publication, Inno Setup, désinstallation, paquets verrouillés, DLL natives et artefacts locaux publiés.
- Journaux, diagnostics, politique de sécurité, recherche ciblée de secrets et fichiers du site statique présents dans le dépôt.

### Vérifications effectuées

1. Revue statique des frontières de confiance et des opérations sensibles.
2. Exécution de **98 tests existants ciblés**, tous réussis, aucun ignoré : stockage des modèles, noms de sortie, runners, annulation, rawvideo, scripts de release, CLI, messages de file et planification des sous-titres.
3. Programme de vérification isolé dans `scratch`, utilisant l'assemblage Release recompilé pour les dernières reproductions et les exécutables FFmpeg du projet. Les échantillons comportaient des espaces et des accents.
4. Contrôle des signatures Windows, empreintes FFmpeg et empreintes des quatre DLL natives du worker.
5. Consultation des avis NuGet directs/transitifs pour l'application et le worker, complétée par les sources primaires des éditeurs.

### Composants inventoriés

| Composant | Version observée | Origine du contrôle |
|---|---|---|
| Application FrameShift | 1.20.0 | Projet et payload local |
| .NET embarqué, application et worker | 8.0.26 | Fichiers runtimeconfig/deps publiés |
| SDK de compilation | 8.0.420 | `dotnet --info` |
| FFmpeg / FFprobe | 9.0 essentials, version déclarée par les exécutables | `-version` et empreintes épinglées |
| Microsoft.ML.OnnxRuntime.DirectML | 1.24.4 | Projet, verrouillage et notices natives |
| NAudio / NAudio.Core | 2.2.1 | Projets et verrouillages |
| PDFsharp | 6.2.4 | Projet et verrouillage |
| SixLabors.ImageSharp | 3.1.12 | Projet et verrouillage ; version d'assembly `3.0.0.0`, distincte de la version du paquet |
| org.k2fsa.sherpa.onnx | 1.13.3 | Projet worker et remplacement natif documenté |
| DirectML natif du worker | 1.15.4 | Notice et empreinte de la DLL |

### Limites

Il s'agit d'une revue de sécurité du dépôt et des artefacts locaux, accompagnée de vérifications ciblées. Elle ne constitue pas une certification ni une campagne exhaustive de fuzzing. Aucune élévation de privilèges, corruption de mémoire, saturation disque/RAM ou attaque contre un autre compte Windows n'a été exécutée. L'installateur n'a pas été installé/désinstallé pendant l'audit ; son code et sa signature ont été examinés.

Les binaires FFmpeg et les DLL natives n'ont pas fait l'objet d'une revue de leur code machine. Les tests de modèles IA lourds et de GPU n'ont pas tous été rejoués. Les dossiers `references` sont exclus des ressources actives. Le site distant vers lequel redirige la page statique et l'historique Git complet des secrets sont hors du périmètre vérifié.

## 3. Constats détaillés

### FS-SEC-01 — Commande utilisateur exécutée depuis un installateur privilégié

**Priorité : P1. Nature : défaut de frontière de privilèges. Confiance : élevée sur le chemin de code.**

**Localisation :** [FrameShift.iss](E:/AI/FrameShift_V1/installer/FrameShift.iss:159), lignes 159–175 et 238–254 ; `PrivilegesRequired=admin`, ligne 27.

`TryGetInstalledValue` consulte HKLM puis HKCU. Une valeur `UninstallString` provenant de HKCU peut ensuite être transmise directement à `Exec`, après seulement `RemoveQuotes`. L'origine des valeurs n'est pas conservée ; `DisplayVersion` et `UninstallString` peuvent aussi provenir de ruches différentes. Aucune validation de l'exécutable, de son emplacement protégé ou de sa signature n'est effectuée à cet endroit.

**Scénario :** un processus non élevé du même utilisateur prépare la clé HKCU `Software\Microsoft\Windows\CurrentVersion\Uninstall\FrameShift_is1` et y désigne un exécutable qu'il contrôle. En l'absence de valeurs HKLM prioritaires, l'utilisateur lance le vrai installateur, accepte l'UAC et choisit la désinstallation de l'installation détectée. Le script dispose alors d'un chemin vers l'exécution de la commande préparée avec ses droits administrateur. Le scénario nécessite cette interaction ; ce n'est pas une élévation automatique sans consentement UAC.

**Correction :** ne jamais exécuter avec élévation une commande de désinstallation issue de HKCU. Pour une installation machine, lire les valeurs ensemble dans la même clé HKLM, vérifier le désinstalleur dans un dossier protégé et refuser les redirections. Une installation utilisateur éventuelle doit être traitée dans le contexte non élevé correspondant. Conserver le résultat de sortie réel de la désinstallation.

**Validation attendue :** une fausse entrée HKCU pointant vers un exécutable témoin inoffensif ne doit jamais déclencher son exécution élevée ; une mise à jour et une désinstallation HKLM légitimes doivent fonctionner. À tester en VM avec le même compte et avec élévation par un autre compte. Le mode administrateur est confirmé par la [documentation Inno Setup](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm).

### FS-SEC-02 — Décodeurs TIFF vulnérables accessibles malgré le contrôle d'extension

**Priorité : P1. Nature : dépendance vulnérable avec chemin d'accès confirmé.**

**Localisations :** [FrameShift.csproj](E:/AI/FrameShift_V1/src/FrameShift/FrameShift.csproj:32) ; [UpscaleEngine.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/Upscale/UpscaleEngine.cs:49) ; [BackgroundRemovalEngine.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/RemoveBackground/BackgroundRemovalEngine.cs:67) ; [ObjectRemovalEngine.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/RemoveObject/ObjectRemovalEngine.cs:66).

La dépendance verrouillée est `SixLabors.ImageSharp` **3.1.12**. Deux avis publiés le 20 août 2026 décrivent des écritures hors limites dans les décodeurs TIFF fax : [GHSA-jj3q-cwqj-842r](https://github.com/SixLabors/ImageSharp/security/advisories/GHSA-jj3q-cwqj-842r) et [GHSA-v76p-62qx-wwq2](https://github.com/SixLabors/ImageSharp/security/advisories/GHSA-v76p-62qx-wwq2). La version intégrée entre dans les plages affectées ; les avis indiquent des correctifs en 4.1.1.

Les actions filtrent l'extension, puis `Image.Load<Rgba32>` détecte le format réel. **Reproduction inoffensive :** un TIFF 16 × 16 pixels renommé en `.png` passe le contrôle d'extension et est décodé comme `TIFF` par la bibliothèque intégrée. Cela confirme l'accès au décodeur, sans exécuter les fichiers malveillants des avis.

**Impact :** un fichier image fourni par un tiers peut atteindre du code affecté par une corruption de mémoire. Un crash est plausible ; aucune exécution de code n'a été démontrée dans FrameShift pendant cet audit.

**Correction :** remplacer la dépendance par une version corrigée compatible et vérifiée ; privilégier **4.1.2 ou une version ultérieure corrigée** plutôt que s'arrêter à 4.1.1, également concernée par d'autres avis. Vérifier compatibilité .NET 8, API et licence. En protection immédiate, désactiver les décodeurs non nécessaires et refuser les formats réels non autorisés avant le décodage complet, y compris pour l'export PDF.

**Validation attendue :** un TIFF renommé en PNG/JPEG doit être refusé avant décodage ; les formats officiellement supportés doivent rester fonctionnels. Exécuter les cas de régression de l'éditeur dans un environnement isolé après correction.

**Nuance :** les commandes NuGet utilisées n'ont signalé aucun paquet vulnérable, mais les avis primaires contredisent une conclusion générale d'absence de vulnérabilités. L'avis [GHSA-gwg2-r3hj-4w44](https://github.com/SixLabors/ImageSharp/security/advisories/GHSA-gwg2-r3hj-4w44) inclut aussi 3.1.12 ; son chemin ICC paresseux n'a pas été identifié comme utilisé ici. Ne pas présenter ce dernier comme une exploitation de FrameShift établie.

### FS-SEC-03 — L'unicité des sorties n'est pas garantie entre sélection et écriture

**Priorité : P1. Nature : perte de données. Reproduction : réussie.**

**Localisations :** [OutputPathHelper.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/Helpers/OutputPathHelper.cs:22), lignes 22–32 et 64–74 ; [ConvertImageAction.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/Actions/ConvertImageAction.cs:55), lignes 55, 73–100 et 116 ; [ExtractFramesAction.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/Actions/ExtractFramesAction.cs:98), lignes 98–110 et 507–519.

Le helper retourne un chemin absent sans le réserver. De nombreuses actions passent ensuite `-y` à FFmpeg. Deux instances peuvent donc sélectionner le même nom avant que l'une écrive. Les nettoyages suppriment également la sortie par son chemin, sans preuve qu'elle appartient exclusivement à l'opération courante. Pour les dossiers d'images, `Directory.CreateDirectory` accepte un dossier créé entre-temps et le nettoyage peut le supprimer récursivement.

**Reproduction :** deux appels au helper avant création retournent le même chemin. Un fichier sentinelle a ensuite été créé à ce chemin. Les arguments réels de `ConvertImageAction`, exécutés par le runner avec FFmpeg, l'ont remplacé par un PNG. Aucun fichier utilisateur réel n'a été utilisé.

**Correction :** produire dans un emplacement temporaire propre à l'opération, puis publier avec un déplacement sans remplacement ; en cas de collision, sélectionner le nom suivant. Pour les fichiers gérés directement, utiliser `FileMode.CreateNew`. Ne supprimer que les artefacts effectivement créés par l'opération. Utiliser `-n` pour les destinations finales exposées, avec une gestion explicite des collisions. **Remplacer uniquement `-y` par `-n` ne suffit pas** si le traitement d'erreur supprime ensuite le fichier préexistant.

**Validation attendue :** deux conversions simultanées doivent produire deux sorties distinctes ; une sentinelle créée après sélection doit rester intacte, y compris après échec/annulation. Ajouter le cas équivalent pour les dossiers d'extraction et les sorties PDF/IA.

### FS-SEC-04 — Runtime .NET embarqué en retard sur les correctifs

**Priorité : P1. Nature : maintenance de sécurité. Preuve : artefacts et source Microsoft.**

**Localisations :** [FrameShift.runtimeconfig.json](E:/AI/FrameShift_V1/publish/FrameShift-win-x64/FrameShift.runtimeconfig.json:7) ; [runtimeconfig du worker](E:/AI/FrameShift_V1/publish/FrameShift-win-x64/Workers/CreateSubtitlesWorker/FrameShift.SubtitlesWorker.runtimeconfig.json:7) ; [build_installer.ps1](E:/AI/FrameShift_V1/build_installer.ps1:312).

Le payload publié contient .NET **8.0.26** pour l'application et le worker. Le SDK utilisé est 8.0.420. Les notes Microsoft consultées indiquent .NET **8.0.31 / SDK 8.0.425**, publié le 8 septembre 2026 avec des correctifs de sécurité. Des correctifs ont aussi été publiés entre ces versions. [Notes Microsoft 8.0.31](https://github.com/dotnet/core/blob/main/release-notes/8.0/8.0.31/8.0.31.md), [suivi des CVE .NET 8](https://github.com/dotnet/core/blob/main/release-notes/8.0/cve.md).

**Impact :** une distribution autonome conserve son propre runtime ancien, même si Windows possède un runtime plus récent. L'applicabilité de chaque CVE à une fonctionnalité FrameShift n'a pas été démontrée ; les avis ASP.NET ne doivent pas être attribués automatiquement à cette application WinForms.

**Correction :** republier application et worker avec le dernier correctif .NET 8 approuvé et un SDK correspondant ; fixer la version de SDK de release et vérifier les versions réellement embarquées avant compilation de l'installateur. Conserver .NET 8 conformément aux règles du projet.

**Validation attendue :** contrôler les deux `runtimeconfig.json`, les versions des DLL runtime et le payload effectivement installé ; les tests de release doivent passer avec le nouveau SDK.

### FS-SEC-05 — Validation lexicale des modèles contournable par une jonction

**Priorité : P2. Nature : redirection d'opérations sur fichiers. Reproduction : réussie.**

**Localisations :** [AiModelDirectorySafety.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/AiModelDirectorySafety.cs:24), lignes 24–67 et 75–87 ; [AiModelStorage.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/AiModelStorage.cs:81), lignes 81–100 ; [FrameShift.iss](E:/AI/FrameShift_V1/installer/FrameShift.iss:1659), lignes 1659–1684.

Le runtime vérifie des chemins normalisés et leurs préfixes, sans résoudre leur destination physique ni refuser les reparse points. Un dossier autorisé sur le plan lexical peut être une jonction vers un emplacement que la validation directe refuserait. Les sous-dossiers de modèles peuvent aussi être redirigés.

**Reproduction :** dans `scratch`, le chemin direct d'un dossier situé sous le répertoire de l'exécutable de test a été refusé. Une jonction vers ce même dossier, située ailleurs, a été acceptée. Le test d'écriture a alors supprimé la sentinelle `.writetest` dans la destination protégée.

**Impact :** écritures, remplacements et suppressions au-delà du dossier prévu, dans la limite des droits du processus. L'application non élevée ne gagne pas de droits administrateur par ce mécanisme. La désinstallation possède déjà des contrôles utiles, mais son contrôle des ancêtres s'arrête à la racine de modèles ; une jonction située au-dessus mérite également un test.

**Correction :** contrôler la racine, ses ancêtres et les sous-dossiers touchés ; refuser les reparse points ou vérifier la destination finale par handle. Appliquer les mêmes règles avant écriture et nettoyage, en limitant les opérations aux fichiers réellement possédés.

**Validation attendue :** jonction à la racine, dans un ancêtre et dans un sous-dossier ; aucune sentinelle de la destination ne doit être modifiée. Prévoir aussi un remplacement de jonction entre validation et accès.

### FS-SEC-06 — Le test d'écriture efface un fichier existant

**Priorité : P2. Nature : perte de données déterministe. Reproduction : réussie.**

**Localisation :** [AiModelSettings.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/AiModelSettings.cs:85), lignes 85–94.

`IsDirectoryUsable` écrit `ok` dans un fichier au nom fixe `.writetest`, puis le supprime. La sélection d'un répertoire partagé contenant déjà ce fichier détruit son contenu puis le fichier lui-même, sans confirmation ni contrôle de propriété.

**Correction :** créer une sonde au nom aléatoire avec `FileMode.CreateNew`, conserver son handle et supprimer uniquement cette sonde. Nettoyer dans un `finally` et gérer la collision sans toucher au fichier rencontré.

**Validation attendue :** un `.writetest` préexistant reste strictement identique ; deux vérifications simultanées ne se gênent pas ; aucune sonde ne reste après succès ou échec.

### FS-SEC-07 — Canaux interinstances sans identité utilisateur/session explicite

**Priorité : P2. Nature : durcissement d'une frontière IPC. Exploitation entre comptes non démontrée.**

**Localisations :** [ConversionBatchSession.cs](E:/AI/FrameShift_V1/src/FrameShift/Windows/Batch/ConversionBatchSession.cs:187), lignes 187–193, 314–599 et 992–997 ; [ProgramImageToPdf.cs](E:/AI/FrameShift_V1/src/FrameShift/ProgramImageToPdf.cs:113) ; [ProgramJoinVideos.cs](E:/AI/FrameShift_V1/src/FrameShift/ProgramJoinVideos.cs:160).

Les noms de pipes sont fixes et ne comprennent ni SID ni session. Les serveurs utilisent uniquement `PipeOptions.Asynchronous` ; les clients ne vérifient pas explicitement l'identité du serveur. Les mutex `Local\...` sont liés à une session alors que le nom du pipe n'est pas construit avec cette séparation.

**Impact :** confusion entre sessions et possibilité d'un serveur préparé à l'avance recevant les chemins/options destinés à une autre instance, si les conditions d'accès Windows le permettent. Un processus du même utilisateur peut déjà envoyer des commandes à la file. Le descripteur Windows par défaut ne doit pas être confondu avec un droit d'écriture universel : cet audit n'affirme pas que tout autre compte peut injecter des tâches. [Droits des pipes Windows](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights).

**Correction :** aligner noms de pipe et mutex sur l'utilisateur et la session ; utiliser `CurrentUserOnly` des deux côtés et une politique explicite d'accès. Vérifier aussi le processus serveur et son exécutable attendu avant de transmettre les chemins si la protection contre un serveur imposteur du même utilisateur est recherchée ; `CurrentUserOnly` seul ne distingue pas deux applications du même compte. Si des clients de niveaux d'intégrité différents sont nécessaires, définir et tester précisément les droits accordés. Garder les traitements FrameShift non élevés.

**Validation attendue :** deux comptes et deux sessions du même compte restent isolés ; un serveur concurrent ne reçoit pas les chemins du client légitime ; une instance élevée n'accepte pas arbitrairement des commandes d'une instance moins privilégiée.

### FS-SEC-08 — Lectures de pipes bloquantes et plafonds incomplets

**Priorité : P2. Nature : déni de service local. Preuve : code.**

**Localisations :** [ConversionBatchSession.cs](E:/AI/FrameShift_V1/src/FrameShift/Windows/Batch/ConversionBatchSession.cs:228), lignes 228–256, 1004–1007 et 1097–1127 ; [ProgramImageToPdf.cs](E:/AI/FrameShift_V1/src/FrameShift/ProgramImageToPdf.cs:130) ; [ProgramJoinVideos.cs](E:/AI/FrameShift_V1/src/FrameShift/ProgramJoinVideos.cs:175).

La file batch impose déjà un plafond de **1 MiB**, mais lit de façon synchrone sans délai de lecture. Un client peut se connecter puis ne pas envoyer de fin de ligne. Join Videos et Image to PDF lisent jusqu'à fermeture du client, sans plafond de message ou de nombre de chemins. L'annulation utilisée pour attendre une connexion ne borne pas ces lectures après connexion. Certaines réponses batch sont également écrites sous le verrou `_sync`.

**Impact :** blocage de l'écoute des nouvelles demandes, attente du client ou accumulation mémoire ; une écriture bloquée sous verrou peut gêner la gestion de la session.

**Correction :** lectures/écritures asynchrones avec délai et annulation ; un message délimité par taille, plafond d'octets et plafond de chemins pour tous les canaux ; déconnexion des clients silencieux. Effectuer les écritures réseau hors du verrou de la file.

**Validation attendue :** client silencieux, message sans fin, message trop grand, nombre excessif de chemins et fermeture pendant réception. L'écoute doit redevenir disponible et l'application doit pouvoir se fermer normalement.

### FS-SEC-09 — Certaines limites interviennent après allocation, ou manquent

**Priorité : P2. Nature : disponibilité face à des entrées hostiles ou très volumineuses.**

**Localisations :** [UpscaleEngine.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/Upscale/UpscaleEngine.cs:49), lignes 49–50 ; [BackgroundRemovalEngine.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/RemoveBackground/BackgroundRemovalEngine.cs:67), lignes 67–72 ; [ObjectRemovalEngine.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/RemoveObject/ObjectRemovalEngine.cs:66), lignes 66–70 ; [loader de sous-titres](E:/AI/FrameShift_V1/src/FrameShift/Core/Actions/AddSubtitlesToVideoSubtitleSourceLoader.cs:100) ; [worker](E:/AI/FrameShift_V1/src/FrameShift.SubtitlesWorker/Program.cs:377) ; [ExtractFramesAction.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/Actions/ExtractFramesAction.cs:98).

- Les limites de pixels IA sont vérifiées **après** `Image.Load`, donc après l'allocation principale. Les contrôles existent mais protègent trop tard contre l'expansion d'une image compressée.
- Le fichier SRT/JSON est lu intégralement en mémoire, puis transformé en texte/objets, sans plafond de taille, de segments ou de mots.
- Le worker charge tout le WAV 16 kHz dans une `List<float>`, puis le copie avec `ToArray`. Le traitement par fenêtres ne rend donc pas cette lecture progressive. Dix heures impliquent environ 576 millions d'échantillons, soit environ 2,3 Go pour un seul tableau float, hors liste et modèle.
- L'extraction de toutes les images n'a pas le précontrôle disque présent dans certaines pipelines BMP IA.

**Correction :** inspecter dimensions et format autorisé avant décodage complet ; limiter les allocations du décodeur ; plafonner les entrées texte et leurs collections. Lire les WAV par fenêtres ou réaliser un précontrôle mémoire. Estimer et surveiller l'espace disque lors de l'extraction d'images. Réutiliser les estimateurs existants plutôt qu'ajouter un framework.

**Validation attendue :** entrées dépassant les budgets refusées avec un message lisible avant les grandes allocations ; extraction interrompue proprement avant saturation disque. Utiliser des fixtures limitées, sans provoquer d'épuisement de la machine de développement.

### FS-SEC-10 — Captures de processus non bornées et sondes sans budget interne

**Priorité : P2. Nature : consommation mémoire et blocage.**

**Localisations :** [FfmpegRunner.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/FFmpeg/FfmpegRunner.cs:335), lignes 335, 398, 458–459, 650–663 et 1034 ; [FfprobeRunner.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/FFprobe/FfprobeRunner.cs:687), lignes 687–705 ; [Program.cs](E:/AI/FrameShift_V1/src/FrameShift/Program.cs:386), lignes 386–389 ; [CreateSubtitlesWorkerRunner.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesWorkerRunner.cs:79).

Stderr est accumulé sans limite dans des `StringBuilder` ; les captures utilisent `ReadToEndAsync` sur stdout/stderr. Des diagnostics issus d'un fichier endommagé peuvent donc faire croître la mémoire indépendamment de la rotation du journal. FFprobe ne dispose pas d'un délai interne et le chemin CLI Media Info le lance avec `CancellationToken.None`, en attendant son résultat avant l'ouverture du formulaire.

**Correction :** conserver un buffer circulaire borné pour stderr, borner aussi la longueur des lignes et la taille du JSON stdout. Ajouter un budget configurable pour sondes et aperçus ; garder un budget adapté aux traitements longs. En cas de dépassement, terminer le processus et nettoyer proprement. L'annulation existante doit être conservée.

**Validation attendue :** processus de test très bavard, ligne sans fin, JSON surdimensionné et sonde immobile ; retour borné dans le temps, mémoire bornée et absence de processus restant après arrêt.

### FS-SEC-11 — Téléchargements sans limite de volume ni d'inactivité

**Priorité : P2. Nature : saturation disque ou téléchargement bloqué.**

**Localisations :** [AiModelFileDownloader.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/AiModelFileDownloader.cs:30), lignes 30–32 et 43–91 ; [downloader audio](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/SeparateAudio/ModelDownloader.cs:73) ; [DeepFilterNetModelDownloader.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/RemoveNoise/DeepFilterNetModelDownloader.cs:95).

Le timeout HTTP est infini et la boucle écrit jusqu'à la fin de la réponse. `Content-Length` sert à l'affichage, pas à une limite de sécurité. Les tailles attendues des catalogues ne deviennent pas un plafond de téléchargement. La vérification SHA-256 finale protège l'intégrité, mais intervient après consommation du disque et n'empêche pas un flux sans fin.

**Scénario :** réponse défectueuse du serveur/CDN, incident réseau prolongé ou endpoint compromis. Une intervention manuelle peut annuler, mais aucun garde-fou autonome ne borne le coût.

**Correction :** transmettre une taille/plafond attendu au downloader ; compter les octets réellement reçus même sans `Content-Length`, vérifier la place disponible et ajouter un délai de connexion/inactivité. Garder les redirections HTTPS nécessaires au fournisseur et le SHA-256 épinglé.

**Validation attendue :** petit serveur local simulant réponse trop grande, réponse sans taille et flux interrompu ; arrêt prévisible, suppression du temporaire propre à l'opération et conservation du modèle valide antérieur.

### FS-SEC-12 — Temporaire partagé entre téléchargements et remplacement fragile

**Priorité : P2. Nature : conflit entre instances et perte de modèle valide.**

**Localisations :** [AiModelFileDownloader.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/AiModelFileDownloader.cs:24), lignes 24, 49–55 et 88–98 ; [downloader audio](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/SeparateAudio/ModelDownloader.cs:65) ; [DeepFilterNetModelDownloader.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/RemoveNoise/DeepFilterNetModelDownloader.cs:101).

Les téléchargements vers une même destination partagent `destination + ".tmp"`. `FileShare.None` protège le handle ouvert sous Windows, mais ne sérialise pas toute la séquence fermeture → vérification → déplacement. Un second téléchargement peut échouer, puis tenter de nettoyer le temporaire commun, ou l'ouvrir après fermeture du premier. Le downloader commun supprime aussi la destination existante avant de déplacer le nouveau fichier ; un échec entre ces opérations laisse le modèle absent.

**Impact :** téléchargements simultanés instables, fichiers vérifiés devenant indisponibles et modèle valide perdu. Aucun contournement du SHA-256 n'a été démontré.

**Correction :** utiliser des temporaires uniques créés exclusivement, un verrou interprocessus par modèle et un engagement final atomique adapté à la destination. Ne supprimer que son propre temporaire. Revalider une destination créée par une autre instance avant de considérer l'opération réussie.

**Validation attendue :** deux instances téléchargeant le même petit fichier ; annulation de l'une ; collision au déplacement ; destination verrouillée. Le modèle final doit rester valide et disponible, sans temporaire d'une autre opération supprimé.

### FS-SEC-13 — Télémétrie ONNX non explicitement désactivée

**Priorité : P2. Nature : cohérence de la promesse de confidentialité/offline.**

**Localisations :** [OnnxProviderHelper.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/OnnxProviderHelper.cs:16) ; [Program.cs](E:/AI/FrameShift_V1/src/FrameShift/Program.cs:18) ; [Program du worker](E:/AI/FrameShift_V1/src/FrameShift.SubtitlesWorker/Program.cs:26) ; autres créations directes d'`InferenceSession`.

Aucun appel à `DisableTelemetryEvents` n'a été trouvé dans le code applicatif. La documentation de la version **1.24.4** indique que les builds officiels Windows activent par défaut la télémétrie de plateforme ETW. Les événements peuvent être collectés par Windows et transmis selon ses réglages/consentements. [Politique ONNX Runtime 1.24.4](https://github.com/microsoft/onnxruntime/blob/v1.24.4/docs/Privacy.md).

**Impact :** FrameShift ne contrôle pas explicitement cette collecte, alors que sa documentation affirme limiter le réseau aux téléchargements de modèles. Aucune transmission de média, aucune connexion directe du processus ONNX et aucune collecte effective ETW n'ont été observées pendant cet audit.

**Correction :** désactiver la télémétrie ONNX dès l'initialisation de l'environnement, avant les sessions, dans chaque processus. Vérifier également le runtime natif distinct du worker et les moyens d'appel disponibles via sherpa-onnx. Documenter précisément la portée de la désactivation. [API C# ONNX Runtime](https://onnxruntime.ai/docs/api/csharp-api.html).

**Validation attendue :** vérifier l'état de l'environnement principal et du worker ; réaliser une session ETW de contrôle et une observation réseau pendant une inférence locale.

### FS-SEC-14 — Garde-fou de release incomplet pour les DLL natives

**Priorité : P3. Nature : durcissement de la chaîne de publication.**

**Localisations :** [build_installer.ps1](E:/AI/FrameShift_V1/build_installer.ps1:8), lignes 8–11 et 82 ; [FrameShift.SubtitlesWorker.csproj](E:/AI/FrameShift_V1/src/FrameShift.SubtitlesWorker/FrameShift.SubtitlesWorker.csproj:31) ; [notices natives](E:/AI/FrameShift_V1/src/FrameShift.SubtitlesWorker/native-dml/THIRD_PARTY_NOTICES.txt:11).

La release contrôle les empreintes FFmpeg/FFprobe, mais ne contrôle pas les quatre DLL copiées depuis `native-dml`. Le paquet sherpa-onnx est remplacé par des DLL locales ; leur intégrité n'est donc pas garantie uniquement par les fichiers de verrouillage NuGet. Les empreintes sont documentées mais non appliquées par le script de release.

**État actuel :** les quatre empreintes calculées correspondent aux notices. Aucun binaire altéré n'a été identifié.

**Correction :** ajouter un manifeste vérifiable de versions/empreintes natives et contrôler sources puis payload publié avant Inno Setup. Documenter le commit exact, les options et la procédure de reconstruction du sherpa-onnx personnalisé. Produire un inventaire des composants distribués incluant DLL et runtime.

**Validation attendue :** une modification d'une DLL de test bloque la release ; la publication correcte passe ; un fichier natif manquant ou une substitution CPU/DML incorrecte est détecté.

### FS-SEC-15 — Distribution sans signature Authenticode

**Priorité : P3. Nature : authentification de l'éditeur. Preuve : Windows.**

**Artefacts :** [FrameShift.exe publié](E:/AI/FrameShift_V1/publish/FrameShift-win-x64/FrameShift.exe), [installateur 1.20.0](E:/AI/FrameShift_V1/installer/FrameShift_1.20.0_Setup.exe). Les deux retournent `NotSigned` via `Get-AuthenticodeSignature`.

**Impact :** Windows ne peut pas authentifier l'éditeur à partir de ces fichiers. Un utilisateur dispose de moins de garanties pour distinguer le vrai installateur d'une substitution, particulièrement lorsqu'il accorde les droits administrateur. L'absence de signature n'est pas une preuve de compromission.

**Correction :** intégrer, lorsque le moyen de signature est disponible, la signature et l'horodatage de l'application, du worker, de l'installateur et du désinstalleur. Contrôler le statut après signature. En attendant, publier les empreintes depuis un canal de distribution authentifié.

**Validation attendue :** identité d'éditeur attendue, chaîne valide, horodatage et signature restant vérifiable après téléchargement ; aucun artefact signé remplacé ensuite par le packaging.

### FS-SEC-16 — Diagnostics trop riches activés par défaut

**Priorité : P3. Nature : confidentialité locale et partage involontaire.**

**Localisations :** [AppLogger.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/Logging/AppLogger.cs:35) ; [FfmpegRunner.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/FFmpeg/FfmpegRunner.cs:343) ; [CreateSubtitlesAssDiagnostic.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/AI/CreateSubtitles/CreateSubtitlesAssDiagnostic.cs:137), lignes 137–156 et 173–201.

Les journaux conservent les commandes complètes, chemins et diagnostics. Le format ASS avancé produit automatiquement un `.diagnostic.json` adjacent contenant notamment chemins, texte des segments, mots et tokens. Une personne partageant les fichiers générés ou un journal de support peut transmettre plus d'informations que prévu. La rotation à 1 MiB est utile, mais n'est pas un plafond strict sur une écriture unique.

**Correction :** rendre le diagnostic de transcription explicite et désactivable ; réduire les chemins dans les logs courants, conserver le détail dans un mode diagnostic choisi. Fournir un export de support expurgé, une durée de conservation et une taille maximale par entrée.

**Validation attendue :** traitement normal sans copie de transcription de diagnostic non demandée ; export support sans chemins personnels complets ; logs restant lisibles après réduction et rotation.

### FS-SEC-17 — Nettoyage des temporaires partiel et fondé sur l'âge

**Priorité : P3. Nature : confidentialité des résidus et nettoyage conservateur.**

**Localisations :** [Program.cs](E:/AI/FrameShift_V1/src/FrameShift/Program.cs:44), lignes 44–100 ; [JoinVideosAction.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/Actions/JoinVideosAction.cs:442) ; [ChangeSpeedForm.cs](E:/AI/FrameShift_V1/src/FrameShift/Windows/Forms/ChangeSpeedForm.cs:422) ; [ConvertToIconAction.cs](E:/AI/FrameShift_V1/src/FrameShift/Core/Actions/ConvertToIconAction.cs:218).

Le nettoyage au démarrage couvre quatre racines de sessions, mais pas tous les emplacements utilisés : assemblage, icônes et plusieurs aperçus dispersés. Après crash, des images, de l'audio ou des JSON de transcription peuvent rester. Pour les racines couvertes, le code supprime les sous-dossiers anciens sans vérifier le nom GUID annoncé dans le commentaire ni un marqueur de propriété.

**Correction :** regrouper les sessions temporaires sous une racine utilisateur FrameShift, associer un marqueur de session/propriétaire et ne nettoyer que les sessions inactives reconnues. Conserver un nettoyage prudent en présence d'une autre instance ; éviter d'effacer du contenu seulement parce qu'il est ancien.

**Validation attendue :** crash simulé avec petites fixtures puis redémarrage ; résidus connus éliminés, session active et dossier étranger conservés. Ne pas assimiler une suppression de jonction à la suppression de sa cible : les API modernes/Inno ont des protections spécifiques ; [DelTree documente ce comportement](https://jrsoftware.org/ishelp/topic_isxfunc_deltree.htm).

### FS-SEC-18 — Politique et qualification de sécurité incomplètes

**Priorité : P3. Nature : maintenance et qualité de qualification.**

**Localisations :** [SECURITY.md](E:/AI/FrameShift_V1/SECURITY.md:5) ; [build_installer.ps1](E:/AI/FrameShift_V1/build_installer.ps1:301) ; [tests de release](E:/AI/FrameShift_V1/tests/FrameShift.Tests/ReleaseScriptTests.cs).

La table des versions supportées indique encore `1.0.x` alors que le projet est en 1.20.0. Les dépendances amont sont exclues du signalement alors que leur intégration expose FrameShift à leurs défauts. Les affirmations réseau/offline doivent tenir compte du runtime ONNX. La release restaure en mode verrouillé et exécute des tests, mais ne possède pas de contrôle explicite couvrant ensemble avis éditeurs, runtime embarqué et DLL natives.

**Correction :** mettre à jour la version supportée et le modèle de menace ; accepter les signalements sur les dépendances lorsqu'ils concernent FrameShift. Ajouter une vérification périodique/à chaque release des avis publics, de leur accessibilité réelle et des composants distribués. Le travail peut rester limité à un script et à une checklist de release, sans nouvelle architecture.

**Validation attendue :** politique cohérente avec la distribution courante ; preuve d'audit dépendances datée ; les erreurs réseau du scanner sont signalées comme échec de vérification, et non comme absence de vulnérabilité. Une sortie NuGet vide ne remplace pas la consultation des avis primaires.

## 4. Protections observées et faux positifs écartés

- **Exécution sans shell :** FFmpeg, FFprobe et le worker utilisent `UseShellExecute=false`, `CreateNoWindow=true` et des arguments distincts. Aucun chemin d'injection de commande shell à partir d'un nom de média n'a été établi.
- **Outils FFmpeg contrôlés à la publication :** exécutables source et publiés conformes aux SHA-256 épinglés. Le resolver ne cherche pas FFmpeg sur `PATH` ; il privilégie des chemins de l'application/projet.
- **Modèles :** téléchargement HTTPS, empreintes SHA-256 épinglées et vérifications existantes des modèles. Aucun contournement de SHA-256 n'a été démontré. Les URL de catalogues pointant sur une branche mobile ne suffisent pas à contourner une empreinte épinglée.
- **Annulation :** les runners possèdent des mécanismes d'arrêt de l'arbre de processus ; les tests ciblés d'annulation et de cycle de vie passent. Cela ne prouve pas le comportement après toutes les formes de crash du processus parent.
- **Désinstallation des modèles :** liste de fichiers autorisés, marqueur de propriété, contrôle de reparse points et retrait non récursif des dossiers. L'ancienne hypothèse « suppression récursive de toute la racine personnalisée » ne s'applique pas au code actuel.
- **Pipes batch :** plafond de message de 1 MiB déjà présent ; le rapport ne prétend pas qu'il manque dans ce canal.
- **Flux HTTP provenant de playlists :** HLS renommé en MP4 refusé ; HLS avec extension réelle et DASH renommé en MP4 bloquent les références HTTP avec `Protocol 'http' not on whitelist 'file,crypto,data'`. **Zéro requête reçue** par le serveur loopback de test. Une fuite HTTP via ces échantillons n'est donc pas retenue comme vulnérabilité confirmée. Cela ne démontre pas l'absence de tout accès réseau pour tous les formats et chemins UNC.
- **Secrets :** aucun secret correspondant aux motifs ciblés de clés privées et tokens usuels n'a été détecté dans les sources actives examinées. La recherche n'était pas exhaustive et n'incluait pas l'historique Git complet.
- **Site local :** la page statique contient une redirection vers une URL HTTPS fixe ; aucun backend applicatif local ou interpolation de données utilisateur dans ce fichier n'a été identifié.
- **BinaryFormatter :** le runtimeconfig WinForms publié l'active, mais aucun appel applicatif à ce désérialiseur n'a été identifié. Il n'est pas présenté comme une exploitation confirmée.

## 5. Plan de correction recommandé

### Lot 1 — Avant la prochaine distribution

1. **FS-SEC-01 :** supprimer le chemin HKCU → exécution élevée et qualifier en VM.
2. **FS-SEC-02 :** neutraliser les décodeurs non autorisés et mettre ImageSharp à une version corrigée validée.
3. **FS-SEC-03 :** sécuriser l'engagement des sorties et leur propriété ; couvrir fichiers et dossiers.
4. **FS-SEC-04 :** republier avec un runtime .NET 8 corrigé et vérifier application + worker.

Critère de sortie : aucun exécutable utilisateur lancé en administrateur, aucune sentinelle de sortie effacée, aucun TIFF déguisé décodé par une action qui ne supporte pas TIFF, versions embarquées approuvées et tests de release réussis.

### Lot 2 — Cycle de sécurisation suivant

5. **FS-SEC-05/06 :** fermer le contournement par jonction et remplacer la sonde `.writetest`.
6. **FS-SEC-07/08 :** isoler et borner les pipes.
7. **FS-SEC-09/10 :** définir les budgets d'entrée, de sortie de processus et de sonde.
8. **FS-SEC-11/12 :** sécuriser volume, délais, temporaires et engagement des téléchargements.
9. **FS-SEC-13 :** désactiver et vérifier la télémétrie des deux runtimes ONNX.

Critère de sortie : annulation et fermeture fonctionnelles avec un client bloqué, ressources bornées, aucun fichier extérieur aux dossiers possédés modifié et modèle valide conservé lors des échecs.

### Lot 3 — Maintenance de distribution et confidentialité

10. **FS-SEC-14/15 :** contrôle complet du payload natif et authentification de la distribution.
11. **FS-SEC-16/17 :** diagnostics minimisés et gestion cohérente des temporaires.
12. **FS-SEC-18 :** politique et qualification sécurité mises à jour.

Les corrections restent compatibles avec C#, .NET 8, WinForms, les runners existants et Inno Setup. Aucun conteneur DI, service locator ou changement de framework UI n'est nécessaire.

## 6. Résultats de vérification reproductibles

| Vérification | Résultat observé |
|---|---|
| TIFF inoffensif renommé en `.png` | Extension acceptée ; `DecodedImageFormat.Name = TIFF` |
| Deux sélections de sortie avant création | Même chemin retourné |
| Sentinelle créée avant écriture avec arguments réels de conversion | FFmpeg réussit ; sentinelle remplacée par un PNG |
| `.writetest` préexistant dans un dossier de modèles autorisé | Dossier accepté ; fichier supprimé |
| Jonction vers un dossier directement interdit par le validateur | Chemin direct refusé ; jonction acceptée ; sentinelle de destination supprimée |
| HLS/DASH référençant HTTP loopback | Aucune requête reçue ; protocoles bloqués par le binaire embarqué |
| Tests existants ciblés | 98 réussis, 0 échec, 0 ignoré |
| Audit NuGet application + worker | Commandes réussies ; aucun paquet signalé par la source consultée ; portée limitée par les avis éditeurs relevés séparément |
| Signatures app/installer 1.20.0 | `NotSigned` |
| Hashes FFmpeg source et payload publié | Conformes aux valeurs épinglées |
| Quatre hashes `native-dml` | Conformes aux notices |
| Runtime du payload app et worker | 8.0.26 |

Commandes principales à rejouer depuis le dépôt :

```powershell
dotnet list src\FrameShift\FrameShift.csproj package --vulnerable --include-transitive --format json
dotnet list src\FrameShift.SubtitlesWorker\FrameShift.SubtitlesWorker.csproj package --vulnerable --include-transitive --format json

dotnet test tests\FrameShift.Tests\FrameShift.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AiModelStorageSafetyTests|FullyQualifiedName~OutputPathHelperTests|FullyQualifiedName~FfmpegRunnerTests|FullyQualifiedName~RunnerCancellationTests|FullyQualifiedName~RawVideoProcessLifecycleTests|FullyQualifiedName~ReleaseScriptTests|FullyQualifiedName~ProgramCliTests|FullyQualifiedName~ConversionBatchQueueMessageTests|FullyQualifiedName~AddSubtitlesToVideoPlannerTests' --logger 'console;verbosity=minimal'

Get-AuthenticodeSignature -LiteralPath 'E:\AI\FrameShift_V1\publish\FrameShift-win-x64\FrameShift.exe'
Get-AuthenticodeSignature -LiteralPath 'E:\AI\FrameShift_V1\installer\FrameShift_1.20.0_Setup.exe'
Get-FileHash -LiteralPath 'E:\AI\FrameShift_V1\src\FrameShift\Tools\ffmpeg\ffmpeg.exe' -Algorithm SHA256
Get-FileHash -LiteralPath 'E:\AI\FrameShift_V1\src\FrameShift\Tools\ffmpeg\ffprobe.exe' -Algorithm SHA256
```

Les reproductions de perte de données ont exclusivement utilisé des sentinelles dans le dossier de travail de l'audit. Pour rejouer ces cas, créer un dossier isolé et suivre les scénarios de FS-SEC-02/03/05/06 ; ne pas les appliquer à des fichiers de production. Les critères de validation décrivent le comportement attendu **après correction**, pas des tests déjà réussis sur la version auditée.
