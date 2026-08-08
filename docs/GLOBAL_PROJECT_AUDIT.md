# Audit global du projet FrameShift

Date de l'audit : 4 août 2026  
Projet audité : `E:\AI\FrameShift_V1`  
Base Git : branche `main`, commit `614c896`, avec les modifications locales présentes au moment de l'audit  
Version applicative active observée lors de l’audit : `1.17.0` ; version de préparation actuelle : `1.17.0`

## 0. Portée, méthode et état de référence

L'audit porte uniquement sur le projet actif. Les dossiers `references/`, `bin/`, `obj/`, les sorties générées et les anciens artefacts de build ont été exclus de l'analyse de code. Les modifications locales déjà présentes ont été considérées comme faisant partie de l'état actif à auditer ; elles n'ont pas été modifiées ni attribuées à cet audit.

Les documents suivants ont été lus en priorité et confrontés au code :

- `docs/PROJECT_RULES.md` ;
- `docs/ARCHITECTURE_FREEZE.md` ;
- `docs/UI_STANDARDIZATION.md` ;
- `docs/CODE_FILE_INDEX.md` ;
- `docs/RELEASE_CHECKLIST.md`.

L'analyse a ensuite couvert les projets C#, les scripts de build/release, l'installateur Inno Setup, les menus Explorer, les outils embarqués, les notices de licences et les tests. Les constats ci-dessous reposent sur des chemins et lignes vérifiés dans l'état courant. Une vérification expérimentale isolée a aussi été effectuée pour la rotation vidéo ; ses fichiers temporaires ont été supprimés après le test.

### État de référence exécuté

| Contrôle | Résultat |
|---|---|
| `dotnet build src\FrameShift\FrameShift.csproj --verbosity minimal` | Réussi, 0 avertissement, 0 erreur ; l'application et `FrameShift.SubtitlesWorker` sont construites. |
| `dotnet test tests\FrameShift.Tests\FrameShift.Tests.csproj -c Release --verbosity minimal` | Réussi : 281 tests, 281 réussis, 0 échec, 0 ignoré. |
| SDK .NET | `8.0.420` ; cible applicative `.NET 8` / `net8.0-windows`. |
| Scan NuGet `--vulnerable --include-transitive` | Aucun package vulnérable signalé pour l'application, le worker ou les tests, avec les données NuGet disponibles le 4 août 2026. |
| Scan NuGet `--deprecated --include-transitive` | Aucun package déprécié pour l'application et le worker. `xunit 2.9.2` et ses composants v2 sont signalés « Legacy » dans le projet de tests. |
| FFmpeg / FFprobe embarqués | Release `9.0-essentials_build-www.gyan.dev` (FFmpeg 9.0, 4 août 2026), vérifiée par SHA-256 et republiée dans le payload `win-x64`. |

Le publish et l'installateur n'ont pas été régénérés, afin de ne pas réécrire `publish/` ni l'exécutable Inno existant. Leur cohérence a été contrôlée par lecture des projets/scripts/ISS et inspection en lecture seule du payload présent ; la feuille de route demande précisément un smoke test reproductible de ces étapes.

Le résultat vert des tests doit être nuancé : cinq tests d'intégration Create Subtitles quittent la méthode avec `return` lorsque leurs ressources locales `scratch/` sont absentes, tout en étant comptés comme réussis et non comme ignorés. Le chiffre de 281 ne signifie donc pas que 281 scénarios ont réellement exercé leur chemin fonctionnel complet.

### Échelle de sévérité

- **Critique** : possibilité crédible de perte de données étendue ou effet de sécurité/destruction majeur ; correction préalable à toute release.
- **Élevée** : défaut fonctionnel important, risque de corruption, d'orphelin, d'instabilité native ou défaut de distribution ; doit bloquer la stabilisation 1.17.
- **Moyenne** : défaut matériel mais conditionnel, dette de fiabilité ou de maintenance qui mérite un lot planifié.
- **Faible** : durcissement, hygiène ou dette acceptable sans urgence immédiate.

## 1. Résumé exécutif et appréciation générale

FrameShift possède une base saine et nettement plus structurée qu'un simple assemblage de scripts : la frontière Core/Windows est globalement respectée, les commandes utilisent `ArgumentList` plutôt que des chaînes shell, les actions nettoient généralement leurs sorties partielles, les modèles ne sont pas embarqués, le worker Whisper isole correctement sa pile native et le build est sans avertissement. Le choix d'une application WinForms simple, locale et centrée sur FFmpeg reste adapté au produit ; aucun changement de framework ni architecture « enterprise » n'est justifié.

En revanche, l'état actif ne doit pas encore être considéré comme prêt à publier. L'intégration de la fenêtre principale 1.17 comporte un défaut central : une sélection automatique du `DataGridView` réduit par défaut la portée au premier fichier, alors que la promesse produit est précisément le traitement de lots importants. Deux chemins de queue peuvent aussi perdre ou exécuter malgré tout des demandes que l'interface présente comme ajoutées ou retirées. Les pipelines rawvideo corrompent silencieusement la géométrie des vidéos portant une rotation 90°/270°. Plusieurs fermetures/annulations laissent une fenêtre de course avec FFmpeg ou ONNX Runtime.

Le risque le plus grave est dans la désinstallation : le chemin libre des modèles peut être transmis à une suppression récursive élevée, ce qui permet d'effacer les fichiers étrangers d'un dossier partagé, voire beaucoup plus si la configuration pointe vers un chemin dangereux et que l'utilisateur confirme. Côté release, les deux scripts concurrents ne donnent pas la même garantie, le publish canonique n'est pas nettoyé avant emballage, les installations personnalisées peuvent omettre les runtimes d'actions toujours visibles, et les licences/notices maintenues dans le dépôt ne sont pas livrées.

Appréciation générale : **fondation technique solide, lisible et pragmatique, mais stabilisation fonctionnelle et sécurisation de la distribution incomplètes**. Les risques sont localisés et réparables sans refonte : il faut consolider les chemins déjà existants (`FfmpegRunner`, `ConversionBatchSession`, downloader commun, helpers UI) plutôt que créer de nouvelles couches.

## 2. Points solides à préserver

### Architecture et lisibilité

- Aucun usage de `System.Windows.Forms` ni manipulation de contrôle WinForms n'a été trouvé sous `src/FrameShift/Core`. La séparation fondamentale Core/Windows est donc réelle.
- La répartition de `Program` en fichiers `partial` reste une dette raisonnable : elle sépare parsing, préflight IA, batch et pickers sans introduire de conteneur DI ni de framework inutile.
- `ActionCatalog`, `ActionScopeResolver`, `MediaFileClassifier` et `FileQueueModel` sont petits, explicites, sans WinForms et bien couverts par des tests unitaires.
- `ConversionBatchSession` fournit déjà une identité distincte par invocation, transporte les options de chaque élément et accepte les relances tardives. C'est la bonne base à généraliser, pas à remplacer.

### Processus externes, chemins et nettoyage

- `FfmpegRunner.CreateStartInfo` (`src/FrameShift/Core/FFmpeg/FfmpegRunner.cs:226-242`) et `FfprobeRunner.CreateStartInfo` construisent les arguments via `ProcessStartInfo.ArgumentList`, avec `UseShellExecute = false`, `CreateNoWindow = true` et redirections. Les chemins avec espaces et accents ne passent pas par un shell.
- Le chemin principal `FfmpegRunner.RunAsync` (`FfmpegRunner.cs:304-384`) enregistre les annulations globale et courante, tue l'arbre de processus et draine stdout/stderr. Les défauts relevés concernent les chemins secondaires, pas ce mécanisme nominal.
- Le worker Create Subtitles a une isolation de processus, un signal d'annulation, un garde-fou de six heures, un kill de l'arbre et un drainage des pipes (`CreateSubtitlesWorkerRunner.cs:49-113`).
- La majorité des actions suppriment la sortie partielle sur annulation ou échec et placent les temporaires dans des chemins uniques. Les éditeurs Crop Video et Burn Subtitles illustrent aussi un bon modèle de preview : CTS remplacé, résultat périmé rejeté, bitmap et temporaires supprimés.

### IA, performances et sécurité déjà en place

- Les téléchargements communs utilisent HTTPS, streaming, fichier `.tmp`, annulation et SHA-256 avant activation. Remove Background, Upscale, RIFE et Create Subtitles revalident déjà leurs fichiers présents.
- Les modèles ne sont ni versionnés dans Git ni livrés dans l'installateur, conformément au gel d'architecture.
- Les traitements Upscale récents utilisent tuilage, buffers réutilisés, `ArrayPool` et `Dispose` de manière pertinente. Les optimisations HostSpectro restent ciblées sur les vrais coûts numériques.
- Les quatre DLL natives DirectML du worker correspondent aux SHA-256 documentés dans `native-dml/THIRD_PARTY_NOTICES.txt`. Les hashes FFmpeg/FFprobe sont également documentés et cohérents avec les binaires examinés.

### UI et documentation utile

- La palette, les métriques, headers, sections et boutons partagés sont largement réutilisés. La dérive DPI constatée est ciblée, pas généralisée.
- La documentation de règles décrit correctement la philosophie du produit et évite l'over-engineering. Plusieurs guides spécialisés restent précieux, même si leur index/version doivent être remis à niveau.
- Le build sans avertissement et la couverture de nombreux planners, settings, formatters, helpers et géométries offrent une bonne base de régression.

## 3. Constats détaillés classés par sévérité

### Critique

#### C-01 — La désinstallation peut supprimer récursivement un dossier utilisateur arbitraire

**Preuves.** `SettingsForm` accepte et enregistre directement tout dossier choisi (`src/FrameShift/Windows/Forms/SettingsForm.cs:117-140`). L'installateur offre également un champ libre pour cette racine (`installer/FrameShift.iss:1074-1083`, `1116-1123`). À la désinstallation, `ReadModelsDirectoryFromSettings()` fournit ce chemin à `DelTree(ModelsDir, True, True, True)` (`FrameShift.iss:1391-1405`).

**Impact.** Si l'utilisateur choisit un dossier partagé tel que `D:\Models`, la réponse « Yes » à « delete downloaded AI models » supprime aussi tout contenu non FrameShift. Une configuration modifiée peut viser une racine, un profil ou un dossier système ; l'uninstaller élevé affiche bien le chemin, mais la formulation laisse raisonnablement croire que seuls les modèles FrameShift seront retirés. Le consentement explicite réduit l'exploitabilité accidentelle, sans rendre la suppression sûre.

**Traitement pragmatique.** Ne jamais supprimer la racine libre. Supprimer uniquement une liste de sous-dossiers FrameShift connus ou un dossier portant un marqueur d'appartenance créé par FrameShift. Canonicaliser et refuser au minimum racines de volume, profils, répertoires Windows/Program Files, `{app}` et chemins parents de ces emplacements. Tester avec un dossier contenant des fichiers étrangers.

**Statut — corrigé dans 1.16.1.** L’uninstallateur ne transmet plus la racine `ModelsDir` à `DelTree` et n’utilise plus de suppression récursive pour les dossiers de modèles. Il valide le chemin lu dans `settings.json`, cible seulement les sous-dossiers connus portant le marqueur créé par FrameShift, refuse les jonctions dans tout le chemin avant suppression et exige que le dossier ne contienne aucun artefact inconnu. Il retire uniquement les fichiers explicitement autorisés, puis le dossier par une opération non récursive seulement s’il est vide. La racine et tout dossier non marqué ou contenant un fichier externe sont conservés. Les répertoires de modèles historiques non marqués restent volontairement en place.

### Élevée

#### H-01 — La fenêtre principale traite par défaut le premier fichier au lieu de la file entière

**Preuves.** `FileQueuePanel.AddFiles` ajoute les lignes sans neutraliser la sélection automatique (`FileQueuePanel.cs:87-135`). La grille est en `FullRowSelect` (`389-443`). `MainForm.RefreshActions` transmet toujours `SelectedPaths` (`MainForm.cs:102-105`) et `ActionScopeResolver.ResolveScope` remplace toute la file dès qu'une sélection existe (`ActionScopeResolver.cs:30-44`). Une vérification runtime avec deux fichiers existants a donné `Items.Count == 2` et `SelectedPaths.Count == 1`, le premier fichier étant sélectionné automatiquement.

**Impact.** Badges, actions applicables et lancement portent sur un seul fichier tant que l'utilisateur ne désélectionne pas explicitement. Cela neutralise la promesse centrale du launcher 1.17 et peut donner une impression de traitement incomplet sans erreur.

**Correction ciblée.** Distinguer sélection automatique et sélection utilisateur, ou remettre `CurrentCell = null` et vider la sélection après ajout programmatique initial. Couvrir le câblage réel MainForm → FileQueuePanel → ActionsPanel, pas seulement le resolver pur.

**Statut — corrigé dans 1.18.1.** `FileQueuePanel.AddFiles` mémorise l’existence d’une sélection avant l’ajout. Sans sélection préexistante, il vide explicitement la sélection automatique et remet `CurrentCell` à `null`, ce qui rend la file entière à `MainForm` / `ActionScopeResolver`. Une sélection existante est conservée lors d’un ajout ultérieur. Les tests WinForms ciblés couvrent l’ajout de plusieurs fichiers sans sélection et la conservation d’une sélection explicite.

#### H-02 — Les pipelines rawvideo déforment silencieusement les vidéos pivotées à 90°/270°

**Preuves.** Le probe conserve dimensions codées et rotation séparée (`src/FrameShift/Core/FFprobe/FfprobeRunner.cs:136-145`, `MediaProbeResult.cs:25-45`). RIFE utilise `probe.VideoWidth/VideoHeight` (`RifeRawVideoPipeline.cs:64-71`) ; Upscale fait de même (`UpscaleRawVideoPipeline.cs:52-59`). Leurs arguments de décodage n'ajoutent pas `-noautorotate` (`RifeRawVideoPipeline.cs:247-263`, `UpscaleRawVideoPipeline.cs:153-167`).

**Reproduction.** Sur un MP4 codé 64×32 avec Display Matrix 90°, FFprobe rapporte 64×32 et rotation 90°, tandis que FFmpeg décode par défaut des frames 32×64. Le nombre total d'octets est identique : le lecteur accepte la frame mais réinterprète ses lignes comme 64×32. Le défaut ne déclenche donc pas forcément d'erreur ou de fallback.

**Correction ciblée.** Choisir explicitement une politique : ajouter `-noautorotate` et conserver les dimensions codées/métadonnées, ou laisser l'autorotation et utiliser `DisplayVideoWidth/DisplayVideoHeight` en supprimant/normalisant la rotation de sortie. Ajouter des tests réels 90°, 180° et 270° sur RIFE et Upscale.

**Statut — corrigé dans 1.18.1.** La politique retenue conserve l’autorotation FFmpeg : les pipelines rawvideo RIFE et Upscale dimensionnent désormais buffers, lecteurs ImageSharp, tailles `rawvideo` et cibles à partir de `DisplayVideoWidth` / `DisplayVideoHeight`. Upscale Video utilise aussi ces dimensions d’affichage pour la validation, le picker de taille personnalisée et le choix interne du modèle AnimeVideo. Les pipelines BMP restent autorotés sans changement ; leurs images ont déjà cette géométrie. Les encodeurs continuent de mapper la vidéo générée depuis leur première entrée et l’audio depuis la source, sans recopier de Display Matrix : la sortie est donc physiquement orientée et sa rotation est absente ou nulle. Les tests de contrat couvrent 0°/90°/180°/270°/−90°, RIFE raw x2/BMP/x4, Upscale raw/BMP, x2, cible personnalisée, audio et l’absence de double rotation ; `dotnet build` et `dotnet test` ont réussi (367 tests). Une matrice manuelle avec médias réellement pivotés et les conteneurs supportés reste requise avant diffusion.

#### H-03 — Le batch de compression peut perdre une invocation pourtant acceptée

**Preuves.** `ProgramCompressBatch` maintient un second protocole de pipe (`src/FrameShift/ProgramCompressBatch.cs:49-93`). Il déduplique par chemin (`76-77`, `108-111`), contrairement à la règle d'identités distinctes. Après 700 ms, `pipeClosing` est positionné puis la liste est figée (`95-112`), alors que le thread peut rester bloqué dans `WaitForConnection` (`61-67`). Une connexion tardive peut écrire avec succès après le snapshot et ne jamais être traitée.

**Impact.** Deux lancements légitimes du même fichier deviennent un seul ; une invocation tardive peut retourner le succès au processus secondaire mais disparaître. Cela contredit explicitement `ARCHITECTURE_FREEZE.md:163` et la garantie déjà fournie par `ConversionBatchSession`.

**Correction ciblée.** Migrer les trois compressions vers `ConversionBatchSession` et ses IDs/options par invocation. Réparer ce protocole parallèle serait plus coûteux et maintiendrait deux sémantiques de batch.

**Statut — corrigé dans 1.18.1.** Les compressions passent désormais par `ConversionBatchSession` ; l’ancien pipe/debounce parallèle et sa déduplication par chemin ont été supprimés. Le pipe commun transporte une invocation atomique avec accusé d’acceptation : le secondaire ne retourne `0` qu’après insertion confirmée ; une fermeture/refus déclenche l’ouverture d’une nouvelle session via le mutex existant. La frontière de fermeture refuse atomiquement les nouvelles invocations, et une confirmation impossible annule l’insertion avant exécution. Les choix compression `SameForAll` / `PerFile`, les formulaires vidéo/audio/image et le chemin headless sont conservés. Des tests ciblés couvrent doublons, arrivées durant debounce/picker/exécution, fermeture, annulation, options partagées/par fichier et retrait d’une occurrence.

#### H-04 — « Retirer de la file » masque certains éléments sans empêcher leur exécution

**Preuves.** `ProgressForm.QueueGridOnCellContentClick` retire d'abord la ligne et ses mappings, puis ajoute l'ID à `_removedQueueItems` (`src/FrameShift/Windows/ProgressUI/ProgressForm.cs:755-784`). `RemoveQueueRowMappings` enlève cet ID de `_queueItemIdsByPath` (`940-957`). `IsQueueItemRemovalRequested(path)` cherche ensuite précisément dans ce mapping désormais vide (`299-309`). `ActionQueueRunner` dépend de ce résultat pour sauter l'élément (`src/FrameShift/Core/Actions/ActionQueueRunner.cs:37-41`).

**Impact.** Les `ConversionBatchSession` écoutant l'événement par ID restent correctes, mais les queues génériques peuvent exécuter un fichier que l'utilisateur pense avoir retiré, notamment pour des actions comme remove/reverse audio et certains chemins de compression.

**Correction ciblée.** Conserver un tombstone `path/ID` consultable jusqu'à consommation, ou porter l'identité de queue jusqu'à `ActionQueueRunner`.

**Statut — corrigé dans 1.18.1.** `ActionQueueRunner` attribue désormais un ID déterministe à chaque occurrence de sa liste statique et transmet cet ID à `IProgressReporter` lors du contrôle précédant l’exécution. `ProgressForm` conserve le tombstone par ID même après suppression de la ligne et de ses mappings visuels, puis le consomme à ce contrôle. Deux occurrences du même chemin restent donc indépendantes. Le retrait de l’élément courant, `Cancel all` et le flux ID propre à `ConversionBatchSession` sont inchangés ; des tests ciblés couvrent le retrait en attente, les doublons, la course juste avant exécution et la session existante.

#### H-05 — Des annulations et démarrages secondaires peuvent laisser FFmpeg/FFprobe actifs

**Preuves.** `FfmpegRunner.RunCaptureAsync` lance le processus puis attend `WaitForExitAsync(cancellationToken)` sans kill en cas d'annulation (`src/FrameShift/Core/FFmpeg/FfmpegRunner.cs:409-430`). `FfprobeRunner.RunProbeAsync` présente le même schéma (`src/FrameShift/Core/FFprobe/FfprobeRunner.cs:505-527`). `Process.Dispose()` ne termine pas l'enfant.

Dans les deux pipelines raw, le décodeur est démarré avant l'encodeur, avant l'enregistrement d'annulation et avant l'entrée dans le `try` (`UpscaleRawVideoPipeline.cs:76-101`, `RifeRawVideoPipeline.cs:88-115`). Si le second `Start()` ou la préparation intermédiaire échoue, le premier processus échappe au `finally`. Ces deux classes dupliquent par ailleurs une partie sensible du cycle de vie hors de `FfmpegRunner`.

**Impact.** Processus orphelin, fichier encore verrouillé, charge CPU/GPU persistante et cleanup qui échoue. Le risque est conditionnel mais contrevient à une priorité explicite du projet.

**Correction ciblée.** Ajouter au runner un chemin de capture et un primitive « paire de processus/pipes » qui garantissent enregistrement avant attente, kill de l'arbre, attente bornée et drainage. La nature duplex rawvideo justifie une API spéciale ; elle ne justifie pas quatre implémentations de cycle de vie.

**État H-05a — corrigé dans 1.18.1.** `FfmpegRunner.RunCaptureAsync` et `FfprobeRunner.RunProbeAsync` refusent désormais un token déjà annulé, enregistrent l'annulation juste après `Start`, tuent l'arbre, confirment la sortie avec une seconde attente bornée après un second kill, puis drainent stdout/stderr sans réutiliser le token utilisateur. Le helper FFmpeg nominal applique aussi cette seconde attente.

**État H-05b — corrigé dans 1.18.1.** Les pipelines RIFE et Upscale rawvideo démarrent chaque FFmpeg via un handle interne de `FfmpegRunner`, qui inscrit immédiatement l'annulation, draine stderr (et stdout inutilisé), ferme stdin, tue l'arbre, attend l'arrêt de façon bornée et draine les flux avant le cleanup. Le démarrage partiel du second processus, les sorties prématurées et les annulations ne peuvent plus court-circuiter le `finally` qui arrête les deux processus avant de rendre les buffers ou supprimer la sortie partielle.

#### H-06 — Cycle de vie de Cut Audio pendant les opérations FFmpeg/FFprobe — Corrigé

`CutAudioForm` dispose désormais d'un CTS de durée de vie et mémorise l'opération active. La fermeture bloque tout nouveau travail, arrête la preview, annule puis attend l'opération, avant un cleanup idempotent. Preview, suppression, silence, waveform et probe utilisent le token du formulaire ; l'initialisation est asynchrone après `Shown`. Les continuations n'actualisent plus une fenêtre en fermeture. Si FFmpeg/FFprobe ne confirme pas sa terminaison, le workspace est conservé et le cas est journalisé. Couverture ciblée : `CutAudioFormLifetimeTests`.

#### H-07 — Cycle de vie ONNX des éditeurs et pickers IA — Corrigé

`RemoveObjectEditorForm` et les deux pickers Remove Noise mémorisent désormais leur opération active via un coordinateur local. Toute fermeture bloque les nouvelles actions, demande l'annulation puis attend la tâche avant de disposer moteur/session, lecteur, CTS et temporaires. OK, Cancel, X et Escape conservent le `DialogResult` demandé. Les moteurs ONNX ont un `Dispose` idempotent ; Remove Noise vérifie désormais l'annulation entre chaque appel ONNX successif sans utiliser `InferenceSession.Dispose()` comme interruption. Couverture ciblée : `OnnxFormLifetimeTests`.

#### H-08 — Consommations IA longues : sécurisé par des garde-fous ciblés

**Separate Audio — corrigé, chantier streaming clôturé.** `AudioChunkReader` lit désormais le flux normalisé séquentiellement dans une unique fenêtre `LEN`, réutilisée d'un chunk à l'autre ; il ne conserve que l'overlap Demucs et une frame de lookahead pour détecter l'EOF exact. Sa mémoire propre est donc bornée et indépendante de la durée du fichier, et la lecture vérifie l'annulation entre les appels au provider. Les buffers Demucs/ONNX/OLA restent volontairement importants, mais sont eux aussi bornés par `LEN` et par la session active. Le temps de traitement et l'espace disque des stems WAV restent naturellement linéaires avec la durée ; aucune optimisation supplémentaire n'est nécessaire actuellement sur Separate Audio. Les validations DirectML et CPU ont conservé les mêmes durées, formats et tailles de stems ; l'écart CPU/DirectML se limite à de faibles arrondis PCM liés aux fournisseurs ONNX.

**Remove Noise — copies majeures réduites et préflight dynamique.** Le moteur ne conserve plus de copie audio d'analyse, de jeux complets de features décalées, de copies des sorties encodeur pour chaque décodeur, de spectres traités complets, ni de seconde onde de synthèse. Les tenseurs encodeur restent vivants seulement le temps des deux décodeurs, les scratchs par frame sont réutilisés et la synthèse est écrite directement dans la sortie. La mémoire nécessaire reste linéaire avec la durée car DeepFilterNet conserve volontairement le spectre complet pour son inférence séquentielle ; un préflight estime ce besoin depuis la durée 48 kHz et la RAM physique réellement disponible, avec réserve ONNX, sans plafond de durée arbitraire. Les très longs fichiers peuvent donc être refusés proprement lorsque la machine ne dispose pas des ressources requises ; une segmentation complète reste une optimisation à évaluer séparément.

**Fallbacks vidéo BMP — préflight disque et cleanup progressif.** Avant toute extraction BMP, RIFE et Upscale Video estiment le pic temporaire, l'espace de sortie et une marge de sécurité, puis vérifient le volume `%TEMP%` et le volume de destination (additionnés s'ils sont identiques). Le traitement échoue avant extraction si l'espace est insuffisant. Après une passe RIFE réussie, sa génération d'entrée est supprimée ; Upscale Video supprime chaque BMP source uniquement après écriture et fermeture du BMP upscalé. Les choix historiques rawvideo et RIFE x4/BMP restent inchangés. Les BMP restent naturellement coûteux (temps et disque linéaires avec le nombre de frames) : ce lot sécurise le mode fallback sans introduire un nouveau pipeline.

**Validation.** Couverture ciblée : estimation RAM et décision selon ressources Remove Noise, conservation du décalage de features et de la synthèse, estimation/refus disque RIFE/Upscale, cleanup progressif et annulation. L'optimisation ne modifie pas les mathématiques DeepFilterNet ni les modèles ONNX. Un benchmark d'inférence local reste à refaire lorsqu'un jeu DeepFilterNet est installé ; aucun modèle n'était disponible dans l'environnement de validation.

#### H-09 — La règle « ne jamais écraser » n'est pas atomique entre processus

**Preuves.** `OutputPathHelper.CreateUniqueOutputPath` fait un test `File.Exists` puis retourne le chemin sans le réserver (`src/FrameShift/Core/Helpers/OutputPathHelper.cs:9-36`). De nombreuses commandes FFmpeg utilisent ensuite `-y`, par exemple `UpscaleRawVideoPipeline.cs:180-190` et `RifeRawVideoPipeline.cs:284-315`. Le launcher peut démarrer plusieurs processus enfants sans attendre (`ActionLauncher.cs:85-93`), notamment pour les actions mono-fichier qui ne passent pas par un mutex batch.

**Impact.** Deux exécutions simultanées sur la même source peuvent choisir le même candidat ; `-y` autorise alors l'écrasement ou la concurrence sur une sortie. Les mutex batch réduisent ce risque pour plusieurs actions, mais pas pour les éditeurs/mono-actions ni pour des processus lancés par d'autres voies.

**Correction ciblée.** Réserver atomiquement le nom (`FileMode.CreateNew`/fichier de réservation) ou utiliser `-n` et retenter sur collision ; supprimer proprement la réservation. `-y` n'est pas un défaut en soi lorsque le nom est réellement réservé.

#### H-10 — Les composants installateur ne correspondent pas aux actions visibles

**Preuves.** Les composants sont sélectionnables finement (`installer/FrameShift.iss:45-93`). Le payload `core` exclut FFmpeg et le worker ; ils ne sont inclus que selon les composants (`95-99`). La fenêtre principale affiche pourtant tout `ActionCatalog` sans manifeste d'installation (`ActionCatalog.cs:115-172`, `ActionsPanel.cs:155-170`). Cas certain : `ai\remove_noise_video` n'est dans aucune condition FFmpeg/FFprobe des lignes 98-99, alors que `RemoveNoiseVideoAction` exige les deux (`RemoveNoiseVideoAction.cs:73-78`). Le chemin stéréo Remove Noise audio peut aussi en avoir besoin (`RemoveNoiseAction.cs:85-97`, `137-149`).

**Impact.** Une installation personnalisée peut montrer des actions impossibles à exécuter. Lors d'une mise à jour avec composant décoché, un runtime ancien n'est pas explicitement retiré et peut continuer à être utilisé.

**Correction appliquée — état actif 1.18.1.** Les trois entrées `[Files]` des runtimes partagés (`ffmpeg.exe`, `ffprobe.exe` et tout `Workers\CreateSubtitlesWorker`, DLL natives incluses) appartiennent désormais au composant fixe `core`. Elles sont donc présentes dans toute installation fraîche et remplacées par le payload courant lors d’un upgrade, y compris lorsque les anciens composants d’action sont décochés ; `ignoreversion` reste appliqué. Les composants optionnels ne sont plus utilisés par le payload runtime et servent à `InstallSelectedMenus` pour les intégrations Explorer. La fenêtre principale peut conserver l’intégralité de `ActionCatalog` sans manifeste ni filtrage dynamique. Les modèles IA restent téléchargés à la demande ou fournis manuellement pour BRIA, jamais embarqués dans l’installateur. La checklist de release porte désormais les scénarios complete/custom/upgrade/uninstall correspondants.

#### H-11 — Les deux chemins de release donnent des garanties contradictoires

**Preuves.** La checklist et l'index présentent `build_installer.ps1` comme canonique (`docs/RELEASE_CHECKLIST.md:30-40`, `CODE_FILE_INDEX.md:335`), tandis que le README recommande `build_publish.ps1` (`README.md:227-233`). Ce dernier ne lance les tests qu'avec `-RunTests` (`build_publish.ps1:98-109`) et retourne le succès quand Inno échoue (`151-157`). Le script canonique teste avec `--no-restore` avant tout restore explicite (`build_installer.ps1:63-70`) et ne nettoie pas `publish/FrameShift-win-x64` avant `dotnet publish` (`72-78`). L'ISS embarque récursivement ce répertoire (`FrameShift.iss:95-99`).

**Impact.** Un clone réellement propre peut échouer au gate `--no-restore`; l'autre voie peut publier sans tests ou masquer l'échec installateur. Un fichier résiduel/étranger du publish précédent peut entrer dans l'installateur canonique.

**Correction appliquée — état actif 1.18.1.** `build_installer.ps1` est désormais l'unique chaîne complète : validation des entrées, version/changelog et Git ; restore de l'application, du worker et des tests en `--locked-mode` ; tests Release obligatoires en `--no-restore` ; nettoyage strict de `publish\FrameShift-win-x64` seulement, avec refus des reparse points ; publish self-contained `win-x64` en `--no-restore` ; vérification du payload minimal ; puis Inno compilé avec `PublishOutputDir` explicitement fixé à ce publish courant. Une sortie Inno absente, vide ou non rafraîchie échoue. Les anciens `build_all.ps1`, `build_publish.ps1` et `build_publish.bat` sont de simples wrappers, et README/checklist ne documentent plus qu'une commande officielle. Les tests de contrat exécutent le script dans un clone synthétique sans `obj`, couvrent l'ordre restore/test, l'arrêt sur échec de test ou publish, l'échec Inno, l'absence de résidu et la provenance du payload Inno.

#### H-12 — Le snapshot FFmpeg embarqué précède des correctifs de sécurité publics

**Preuves.** Le binaire et `THIRD_PARTY_NOTICES.md:10-21` identifient le snapshot du 17 avril 2025. La [page sécurité officielle FFmpeg](https://ffmpeg.org/security.html) liste des correctifs intégrés ultérieurement à master et aux branches 7/8, dont plusieurs CVE 2025 et 2026. Le build expose un large ensemble de décodeurs, dont OpenEXR.

**Impact.** FrameShift ouvre des médias potentiellement non fiables ; un snapshot antérieur ne peut pas contenir les commits de correction postérieurs. L'audit ne démontre pas que chaque CVE est exploitable dans chaque commande FrameShift, mais la dette de patch est réelle et la surface est directement exposée aux fichiers utilisateur.

**Correction ciblée.** Passer à un build stable récent, vérifier provenance/configuration/hash, rejouer les tests fonctionnels et médias réels, puis pinner les nouveaux SHA dans le gate de release.

**Statut — corrigé le 9 août 2026.** FrameShift embarque désormais le build statique GPLv3 `9.0-essentials_build-www.gyan.dev` de Gyan Doshi, correspondant à la release stable FFmpeg 9.0 du 4 août 2026 et au commit source `d32b387f2b`. L’archive `ffmpeg-release-essentials.zip` a été téléchargée depuis Gyan.dev et son SHA-256 publié a été confirmé (`E6B54767A6065919048F1A098EB27211CA4E12B4348A05D88777A5855D0B6E71`). Les binaires embarqués sont `ffmpeg.exe` `227AF0691433B703FFC5725E47F7D06EEFC34B4A72E7870E73D30E2CDA483ECF` et `ffprobe.exe` `901F0EFE4793CBB0F017101E3427F816E8FBF9A407BD585F49DF30F4325CFD88`. Leur configuration confirme `--enable-gpl --enable-version3 --enable-static` et la disponibilité des bibliothèques utilisées par FrameShift, notamment libx264, libx265, libvpx, libopus, libmp3lame, libwebp et libass. `build_installer.ps1` refuse désormais une source ou un payload publié dont l’un de ces hashes diffère ; le test de release couvre aussi une altération simulée avant Inno Setup. Les tests Release (433/433), le build Release (0 avertissement, 0 erreur), le publish self-contained `win-x64`, l’installateur Inno Setup et les smokes réels sur médias courts ont réussi. Les commandes FrameShift existantes sont restées inchangées : aucune incompatibilité 9.0 n’a été constatée.

#### H-13 — Les licences/notices du dépôt ne sont pas livrées avec le produit

**Preuves.** Le projet principal ne publie comme contenu que `Assets` et `Tools` (`src/FrameShift/FrameShift.csproj:39-48`). Le worker ne copie que ses DLL natives (`FrameShift.SubtitlesWorker.csproj:26-39`) et omet son `native-dml/THIRD_PARTY_NOTICES.txt`. L'ISS n'a ni `LicenseFile`, ni entrée pour `LICENSE`, `THIRD_PARTY_NOTICES.md` ou les notices natives (`FrameShift.iss:15-36`, `95-99`). Le publish existant examiné ne contenait aucun de ces fichiers.

**Impact.** Risque de non-conformité/traçabilité pour FrameShift GPLv3, FFmpeg GPL, .NET self-contained, Apache-2.0, DirectML et autres composants. Il s'agit d'un risque de distribution, pas d'un conseil juridique définitif.

**Correction ciblée.** Installer un dossier `licenses/` déterministe avec licence projet, notices tierces et textes requis ; envisager `LicenseFile` Inno ; vérifier l'obligation et l'offre de source correspondant exactement au build FFmpeg distribué.

**Statut — corrigé le 9 août 2026.** Le projet publie désormais un dossier statique `licenses/` : `LICENSE`, `THIRD_PARTY_NOTICES.md` et les notices natives du worker (`subtitles-worker-native/THIRD_PARTY_NOTICES.txt`, `APACHE-2.0.txt`, `DirectML-LICENSE.txt`, `DirectML-THIRD_PARTY_NOTICES.txt`). `FrameShift.csproj` copie explicitement cet ensemble vers le publish `win-x64`; l’entrée récursive déjà canonique de l’ISS l’installe donc au même emplacement. `LicenseFile={#AppPayloadDir}\licenses\LICENSE` affiche la licence GPLv3 FrameShift pendant l’installation sans ajouter de payload distinct. Le script de release vérifie les sources puis la présence exacte de chaque fichier dans le publish avant d’appeler Inno Setup. `THIRD_PARTY_NOTICES.md` conserve la provenance, les hashes et la représentation de FFmpeg/FFprobe, du runtime .NET et des dépendances applicatives ; les termes DirectML 1.15.4 et ses notices de package sont maintenant distribués tels quels. Les tests Release, build, publish et compilation ISS ont été rejoués avec succès ; Inno Setup a lu `LicenseFile` et compilé les entrées `[Files]` depuis ce publish validé. La validation interactive d’une installation reste à faire sur une machine ou un environnement de test dédié.

### Moyenne

#### M-01 — Trois familles de modèles acceptent ensuite la seule présence du fichier

DeepFilterNet (`DeepFilterNetModelLocator.cs:22-29`, `ProgramAiPreflight.cs:638-665`), HTDemucs (`SeparateAudio/ModelLocator.cs:20-30`, `ProgramAiPreflight.cs:588-635`) et Remove Object (`RemoveObjectEditorForm.cs:779-795`, `843-863`) ne revalident pas les SHA au préflight. Le validateur Remove Object existe (`RemoveObject/ModelDownloader.cs:28-31`) mais n'est pas appelé. Les downloaders DeepFilterNet et Demucs ignorent aussi un fichier déjà présent sans vérifier son hash (`DeepFilterNetModelDownloader.cs:75-79`, `SeparateAudio/ModelDownloader.cs:56-60`).

Le téléchargement initial reste HTTPS et vérifié, ce qui évite de présenter ceci comme une compromission distante directe. Le risque réel est la corruption, une migration héritée, un remplacement local ou une course de destination, suivis d'une erreur native opaque. Réutiliser le downloader/validateur commun et mettre en cache une validation par chemin/taille/date durant le processus.

#### M-02 — Les téléchargements et le test de dossier manquent de garde-fous simples

- `AiModelFileDownloader` accepte un flux de taille arbitraire avec timeout infini ; `Content-Length` ne sert qu'à la progression (`AiModelFileDownloader.cs:30-85`). Le SHA empêche l'activation finale mais pas le remplissage du disque. Le timeout infini est raisonnable pour 3,1 Go ; l'absence de plafond est le vrai défaut.
- Le chemin temporaire fixe `destination + ".tmp"` rend deux téléchargements concurrents conflictuels (`24-25`). Après validation, le downloader générique supprime l'ancienne destination puis déplace le temporaire (`93-98`), créant une courte fenêtre sans modèle valide.
- `AiModelSettings.IsDirectoryUsable` écrit puis supprime un nom fixe `.writetest` (`src/FrameShift/Core/AI/AiModelSettings.cs:78-87`). Un fichier utilisateur portant ce nom est écrasé puis détruit.

Transmettre la taille attendue, arrêter au-delà d'une marge faible, utiliser un temporaire unique, remplacer atomiquement quand possible et tester la writabilité via `CreateNew` sur un nom aléatoire supprimé en `finally`.

#### M-03 — Les limites de pixels interviennent après l'allocation de l'image complète

Remove Background (`BackgroundRemovalEngine.cs:67-72`), Remove Object (`ObjectRemovalEngine.cs:65-69`) et Upscale (`UpscaleEngine.cs:49-50`) chargent l'image avant de vérifier la limite. Une image compressée géante peut donc provoquer la pression mémoire que la limite devait éviter. Utiliser `Image.Identify` avant `Image.Load`, puis conserver le contrôle post-chargement pour défense en profondeur.

#### M-04 — La suite de tests donne des faux verts et touche la vraie configuration utilisateur

Cinq `[Fact]` Create Subtitles font `return` si les actifs locaux manquent (`tests/FrameShift.Tests/CreateSubtitlesTests.cs:385-386`, `469-470`, `590-591`, `655-656`, `844-847`). Ils apparaissent dans « 281 passed, 0 skipped ». Plusieurs tests lisent/écrivent le vrai `%LOCALAPPDATA%\FrameShift\config\settings.json`, par exemple `399-406`, `603-610`, `862-899`, et restaurent seulement en `finally` (`987-1002`). Un arrêt brutal peut laisser les préférences utilisateur altérées et des exécutions parallèles peuvent interférer.

Les marquer explicitement comme intégration/skip quand les actifs manquent, isoler la racine de config par variable/injection minimale et séparer le job CI reproductible du job lourd optionnel.

#### M-05 — Plusieurs chemins UI restent synchrones, non annulables ou incohérents

- `MainForm.ExpandPaths` appelle `Directory.GetFiles` sur le thread UI et matérialise tout un dossier (`MainForm.cs:169-203`). Les répertoires volumineux ou réseau peuvent figer la fenêtre.
- Le drop de dossier fonctionne sur la surface vide via `MainForm`, mais la grille intercepte le drop et appelle directement `FileQueuePanel.AddFiles`, qui rejette les dossiers (`FileQueuePanel.cs:42-44`, `115`, `229-241`).
- Cut Video exécute des captures synchrones avec token nul (`CutVideoForm.cs:459-471`, `708-718`), Create GIF laisse des captures se chevaucher (`CreateGifForm.cs:535-550`, `650-675`, `848-867`) et Image to PDF convertit WebP synchronement (`ImageToPdfForm.cs:3570-3594`).
- `ActionsPanel` reconstruit contrôles et ToolTips via `Controls.Clear()` sans les disposer (`ActionsPanel.cs:133-157`, `239-242`), créant une pression GDI/mémoire après de nombreuses frappes/filtres.
- `SettingsForm`, Main/Progress et Cut Audio ne suivent pas clairement la stratégie DPI explicite exigée ; Settings est entièrement positionné en coordonnées fixes (`SettingsForm.cs:21-115`).

Réutiliser le bon pattern de preview déjà présent dans Crop Video/Burn Subtitles, centraliser l'expansion de dossier côté Windows et valider réellement 125/133/150/175 % avant toute modification DPI.

#### M-06 — Deux hotspots cumulent complexité, duplication et mémoire

`ImageToPdfForm.cs` atteint 4 909 lignes et mélange UI, cache bitmap, clipboard, temporaires, historique, interactions, snapping, zoom, impression et conversion. `PreviewPanelOnMouseDown` (`2099-2359`), `PreviewPanelOnMouseMove` (`2360-2628`) et `ResizeRectFromHandle` (`4216-4421`) dépassent chacun 200 lignes. La limite de 64 millions de pixels est par image (`22-24`, `764-769`) ; tous les bitmaps restent en cache jusqu'à fermeture (`789-790`, `3459-3473`, `4679-4688`). Dix images proches de la limite représentent environ 2,5 Go en RGBA.

`CropImageForm` (1 234 lignes) et `CropVideoForm` (1 416) partagent environ un millier de lignes d'interaction/viewport ; les pickers Remove Noise audio/vidéo sont eux aussi presque identiques. Une extraction ciblée du cache/chargement, de l'état/historique et des interactions partagées est rentable. Une base générique pour tous les formulaires ne l'est pas.

`FfmpegRunner` (environ 811 lignes) et `ConversionBatchSession` (environ 1 034) sont longs mais cohérents avec une responsabilité complexe ; leur taille seule n'est pas un défaut. Les chemins de lifecycle manquants doivent y être consolidés sans les réécrire.

#### M-07 — Le parseur CLI masque les options inconnues et peut avaler leur valeur comme entrée

`ProgramCli.cs:353-358` ignore tout token commençant par `--`, mais traite le token suivant comme chemin. Avec `--unknown-flag value <fichier>`, `value` rejoint les entrées. Le test intitulé `UnknownFlags_AreIgnoredSilently` (`ProgramCliTests.cs:193-203`) ne vérifie que l'unique occurrence du fichier attendu et passe malgré un élément supplémentaire possible. Les valeurs manquantes de plusieurs options sont aussi ignorées silencieusement.

Pour un outil appelé depuis Explorer et scripts, mieux vaut rejeter option inconnue/valeur manquante avec un message précis. Les commandes installateur connues ne sont pas affectées aujourd'hui.

#### M-08 — Des fallbacks de développement et pipes insuffisamment bornés restent actifs en Release

`ToolLocator` cherche, après le chemin app-local, trois parents plus haut (`src/FrameShift/Core/Helpers/ToolLocator.cs:25-42`) ; depuis une installation classique cela peut résoudre `C:\Tools\ffmpeg`. `CreateSubtitlesWorkerRunner` cherche aussi des sorties `src/.../bin/Debug|Release` hors de l'application (`155-173`). Avec une installation custom incomplète, un binaire inattendu peut donc être exécuté. Limiter ces fallbacks à Debug ou valider une vraie racine de dépôt.

Les pipes nommés de batch/compression/Image-to-PDF n'activent pas `PipeOptions.CurrentUserOnly`. L'application n'est pas élevée en fonctionnement normal, donc le risque local est limité, mais le durcissement est peu coûteux.

#### M-09 — Une erreur de cleanup peut interrompre toute une queue

`ConversionActionHelper.DeleteIfExists` retente dix fois puis laisse remonter la dernière exception (`ConversionActionHelper.cs:31-58`). `ActionQueueRunner` n'a aucune frontière d'exception par élément autour de `ExecuteAsync` (`ActionQueueRunner.cs:43-47`). Un fichier partiel toujours verrouillé peut donc masquer l'erreur initiale et empêcher les éléments suivants d'être traités. Le cleanup doit journaliser sans remplacer l'erreur principale ; le runner doit convertir toute exception non gérée en résultat d'échec pour l'élément courant et continuer, sauf annulation globale.

#### M-10 — Versions et documentation de release synchronisées pour 1.17.0

Les écarts de release relevés lors de l'audit ont été corrigés pour `1.17.0` : les versions du projet, du README, du changelog, de `ARCHITECTURE_FREEZE.md` et du guide produit sont alignées ; l'index inclut `SettingsForm` et les helpers de thème ; la checklist décrit la version injectée dans l'ISS via `/DMyAppVersion`.

Deux guides historiques restent à revoir séparément, sans bloquer cette release : `SECURITY.md` doit définir les versions réellement supportées et `DEMUCS_FRAME_SHIFT_INTEGRATION_GUIDE.md` doit être présenté comme document d'intégration historique ou remis à jour.

#### M-11 — Menus Explorer, licences de modèles et catalogue ne partagent pas la même vérité

L'ISS applique `AudioExtensions` à toutes les actions audio (`FrameShift.iss:108-112`, `622-639`, `947-975`), alors que `ActionCatalog` définit des sous-ensembles pour convert, Separate Audio et Remove Noise (`ActionCatalog.cs:91-99`, `137-145`). Des commandes Explorer apparaissent donc sur des fichiers rejetés ensuite, parfois après préflight du modèle.

Les notices indiquent que les poids Remove Object/LaMa dérivent de Places2 avec restrictions non commerciales/recherche et un statut de redistribution à clarifier (`THIRD_PARTY_NOTICES.md:159-186`), tandis que le catalogue rapide affiche seulement « Apache-2.0 » (`ObjectRemovalModelCatalog.cs:21-29`). Une décision explicite du propriétaire est nécessaire avant diffusion large : droits de miroir/redistribution et message utilisateur cohérent. Le flux manuel BRIA est, lui, une exception explicite et correctement signalée.

#### M-12 — Quelques coûts et ressources sont inutilement répétés ou conservés

- Create Subtitles recalcule les SHA de plusieurs gigaoctets au préflight (`ProgramAiPreflight.cs:516-559`), puis dans chaque action (`CreateSubtitlesAction.cs:88-93`). Pour Whisper Turbo, le coût est sensible en batch. Cacher le résultat par chemin/taille/date pendant le processus conserve la garantie d'intégrité.
- `FfprobeRunner` ne dispose pas explicitement deux `JsonDocument` (`FfprobeRunner.cs:64`, `269`), ce qui retarde le retour de buffers lors de probes répétés.
- `MainForm` ignore le `Process` renvoyé par le launcher (`MainForm.cs:138-143`) ; disposer immédiatement le wrapper libère son handle sans tuer l'enfant.
- Les buffers stderr de `FfmpegRunner` ne sont pas bornés (`FfmpegRunner.cs:305`, `349`). `-loglevel error` rend le risque faible, mais un fichier très corrompu peut générer une croissance prolongée.

### Faible / dette acceptable

- `ActionLauncher` est rangé sous Core alors que le gel place le lancement de processus dans Windows. Il ne référence aucun contrôle WinForms et son builder d'arguments est pur ; déplacer seulement la partie `Process.Start` lors d'une modification naturelle suffit.
- Les trois projets ont des lockfiles, mais les scripts de release n'imposent pas `RestoreLockedMode`/`--locked-mode`. À renforcer dans le gate, sans urgence runtime.
- xUnit v2 est maintenant marqué « Legacy » par NuGet. La migration v3 est à planifier uniquement quand elle apporte une valeur ou devient nécessaire ; elle ne justifie pas de retarder les corrections fonctionnelles.
- Le type d'installation « complete » est libellé `First test installation` (`FrameShift.iss:41-43`) et quelques libellés Explorer/installer divergent, par exemple Upscale Video 4x contre 2x/3x/4x. Ce sont des corrections éditoriales faciles.
- Les fontes statiques et quelques ressources de processus conservées jusqu'à sortie sont mineures comparées aux courses ONNX/FFmpeg.
- L'absence de signature Authenticode est acceptable pour un petit projet open source. Pour une diffusion plus large, publier automatiquement le SHA-256 de l'installateur est un premier pas rentable.

## 4. Risques fonctionnels, techniques, sécurité et maintenance

| Domaine | Risques dominants | Conséquence probable |
|---|---|---|
| Fonctionnel | Portée réduite au premier fichier, queues compression/removal incohérentes, rotation rawvideo, composants custom absents | Fichier non traité, action affichée mais inutilisable, sortie visuellement corrompue, confiance utilisateur dégradée |
| Données | Suppression récursive du dossier modèles, collision de sortie concurrente, cleanup sur répertoire actif | Perte de fichiers étrangers, sortie écrasée/partielle, temporaires verrouillés |
| Processus / annulation | Capture/probe sans kill, fenêtre de démarrage raw, Cut Audio token nul | Processus orphelin, CPU/GPU et verrous persistants, batch bloqué |
| Runtime natif | Dispose ONNX concurrent, modèles présents non revalidés, images géantes chargées avant limite | Exception native, crash/instabilité, OOM ou erreur de modèle opaque |
| Ressources | Audio entier en RAM, BMP sans préflight disque, cache PDF agrégé | Pagination/OOM, saturation de `%TEMP%`, interface non réactive |
| Sécurité / supply chain | FFmpeg ancien, fallbacks Release, download sans plafond, pipes inter-utilisateurs | Exposition à parseurs non patchés, exécution d'un outil inattendu, remplissage disque, injection locale limitée |
| Distribution / juridique | Notices absentes du produit, statut LaMa à clarifier, scripts release divergents | Release non reproductible, payload contaminé, risque de conformité |
| Maintenance | Source de vérité dupliquée entre catalogue/ISS/docs, deux infrastructures batch, grands éditeurs dupliqués | Régression à chaque ajout d'action, corrections divergentes, coût de test croissant |

## 5. Couverture de tests et validations insuffisantes

Les tests actuels sont utiles sur les fonctions pures, planners, formatters et géométries. Ils ne couvrent pas les principaux risques révélés par l'audit. Les ajouts prioritaires sont :

1. **Launcher réel WinForms** : ajout de deux fichiers, aucune sélection implicite, sélection explicite, clear/re-add, portée MainForm → ActionsPanel → ActionLauncher, drop de dossier sur toutes les surfaces.
2. **Queues** : même chemin deux fois, arrivée pendant/après debounce compression, retrait d'un élément générique, doublons, annulation courante/globale et exception d'une action sans arrêt du lot.
3. **Processus** : faux exécutables contrôlés pour FFmpeg/FFprobe, annulation puis assertion que le PID/l'arbre est terminé, échec du second `Start`, fermeture Cut Audio pendant opération.
4. **Médias réels minimaux** : chemins espaces/accents, vidéo rotation 90/180/270, sortie concurrente, fichier corrompu, cleanup après annulation, test de `%TEMP%` insuffisant.
5. **ONNX/UI** : fermeture Remove Object/Remove Noise pendant une inférence artificiellement lente ; aucune session disposée avant fin.
6. **IA et stockage** : SHA incorrect, fichier préexistant invalide, course de download, annulation, contenu trop long, modèle migré, JSON settings corrompu, racines dangereuses et writetest non destructif.
7. **Ressources** : tests/benchmarks bornés sur audio long, estimation RAM/disque, refus avant allocation/extraction et suppression progressive des passes.
8. **Release** : exécution depuis clone sans `obj`, manifeste post-publish, absence de résidus/PDB/debug, présence licences/notices, hashes vendored, compilation ISS, installation custom, upgrade/désélection et désinstallation avec dossier partagé.
9. **Cohérence** : inventaire automatique ActionRegistry/ActionCatalog/ConversionBatchSession/ISS, extensions et dépendances FFmpeg/FFprobe/worker.
10. **Tests lourds Create Subtitles** : job explicite avec actifs, vrai statut skipped si absents et config isolée ; ne plus compter un `return` comme succès fonctionnel.

Une couverture numérique globale n'est pas la priorité. Quelques tests d'intégration déterministes aux frontières processus, queue, installateur et filesystem apporteront davantage que des centaines de tests de getters supplémentaires.

## 6. Feuille de route de correction

### Phase 0 — Neutraliser les risques de destruction et de supply chain — 16 %

- **Priorité :** P0, avant tout nouvel installateur public.
- **Objectif :** empêcher toute suppression hors périmètre FrameShift et remettre les binaires exposés à un niveau de patch acceptable.
- **Périmètre :** uninstaller/config des modèles, validation canonique de chemins, mise à jour FFmpeg/FFprobe, hashes vendored.
- **Constats traités :** C-01, H-12, partie sécurité de M-02/M-08.
- **Risques du lot :** rendre inaccessible un ancien dossier modèle légitime ; régression codec/performance après changement FFmpeg.
- **Critères de validation :** racines/profils/système refusés ; dossier partagé conservant ses fichiers étrangers après uninstall ; seuls sous-dossiers/artefacts FrameShift supprimés ; nouveau FFmpeg identifié, hashé, testé sur toutes les actions et médias de non-régression ; aucun fallback externe en Release.
- **Part du travail total :** **16 %**.

### Phase 1 — Rétablir la sémantique du launcher et des queues — 19 %

- **Priorité :** P0/P1, blocage fonctionnel 1.17.
- **Objectif :** garantir qu'une file est réellement la portée par défaut et qu'aucune invocation acceptée/retirée n'est perdue ou exécutée à tort.
- **Périmètre :** FileQueuePanel/MainForm, ProgressForm/ActionQueueRunner, migration des compressions vers ConversionBatchSession, drop de dossiers.
- **Constats traités :** H-01, H-03, H-04, partie de M-05 et M-09.
- **Risques du lot :** modifier la sélection explicite ou le comportement historique de regroupement Explorer.
- **Critères de validation :** file multi-format entièrement ciblée sans sélection ; sélection utilisateur respectée ; deux invocations identiques conservent deux IDs ; injections tardives traitées ; retrait garantit zéro exécution ; exception d'un élément n'arrête pas les suivants ; tests pipe et WinForms déterministes.
- **Part du travail total :** **19 %**.

### Phase 2 — Unifier cycle de vie, annulation et intégrité des sorties — 23 %

- **Priorité :** P1.
- **Objectif :** aucun processus enfant ni session native ne survit à tort ; aucune sortie n'est écrasée par course.
- **Périmètre :** capture/probe runners, API rawvideo duplex, Cut Audio, fermeture des pickers ONNX, réservation atomique des sorties, cleanup.
- **Constats traités :** H-02, H-05, H-06, H-07, H-09, M-09, ressources de M-12.
- **Risques du lot :** deadlock de drainage, fermeture UI plus lente, changement de métadonnées rotation, réservations abandonnées.
- **Critères de validation :** tests PID/arbre après annulation et échec de démarrage ; aucune session disposée pendant `Run`; Cut Audio ferme après fin/kill puis nettoie ; vidéos pivotées conservent géométrie/orientation ; deux processus simultanés produisent deux noms uniques ; aucune réservation restante après échec.
- **Part du travail total :** **23 %**.

### Phase 3 — Borner les ressources et harmoniser l'intégrité IA — 20 %

- **Priorité :** P1/P2.
- **Objectif :** échouer tôt et clairement plutôt que saturer RAM/disque, tout en appliquant une politique SHA cohérente.
- **Périmètre :** Separate Audio, Remove Noise, fallbacks BMP RIFE/Upscale, préflight image, downloader commun, DeepFilterNet/Demucs/Remove Object, cache SHA Whisper.
- **Constats traités :** H-08, M-01, M-02, M-03, partie de M-12.
- **Risques du lot :** limites initiales trop conservatrices ; régression de qualité lors d'une future segmentation ; coût SHA au démarrage.
- **Critères de validation :** estimation RAM/disque visible avant traitement ; refus documenté et testable si capacité insuffisante ; annulation pendant chargement ; aucune image géante décodée avant Identify/limite ; tout modèle présent est validé ou re-téléchargé ; download arrêté au plafond ; cache SHA invalidé par taille/date ; tests audio long sans OOM dans l'enveloppe définie.
- **Part du travail total :** **20 %**.

### Phase 4 — Stabiliser les surfaces UI et réduire les hotspots ciblés — 10 %

- **Priorité :** P2.
- **Objectif :** préserver la réactivité WinForms et diminuer les zones où une correction est risquée, sans framework supplémentaire.
- **Périmètre :** previews Cut Video/Create GIF/Image to PDF, ActionsPanel, DPI Main/Settings/Progress/Cut Audio, cache ImageToPdf, interactions Crop partagées, pickers Remove Noise.
- **Constats traités :** M-05, M-06 et dettes UI faibles.
- **Risques du lot :** double scaling, régression des interactions précises, cache trop agressif.
- **Critères de validation :** aucun FFmpeg long sur thread UI ; previews périmées annulées/rejetées ; contrôles reconstruits disposés ; test manuel 100/125/133/150/175 % ; budget mémoire agrégé ImageToPdf ; extraction de classes simples avec tests géométriques et comportement inchangé.
- **Part du travail total :** **10 %**.

### Phase 5 — Rendre release, tests, documentation et licences reproductibles — 12 %

- **Priorité :** P1 avant release finale, parallélisable après les correctifs fonctionnels.
- **Objectif :** une commande canonique produit un payload propre, testé, traçable et documenté.
- **Périmètre :** scripts build/release, publish manifest, ISS/components/Explorer, licences/notices, tests lourds isolés, lock mode, docs/version/index, décision licence Remove Object.
- **Constats traités :** H-10, H-11, H-13, M-04, M-07, M-10, M-11 et dettes dépendances faibles.
- **Risques du lot :** changement du comportement custom installer ; allongement du build ; documentation mise à jour avant que les choix produit soient figés.
- **Critères de validation :** clone sans `obj` → restore verrouillé → 100 % tests requis → publish nettoyé → manifeste conforme → ISS réussi ; code non nul à tout échec ; installation complete/custom/upgrade/uninstall testée ; catalogue/menus/extensions/dépendances alignés ; licences présentes dans publish et installation ; tests scratch explicitement skipped ou exécutés ; version 1.17 cohérente partout.
- **Part du travail total :** **12 %**.

## 7. Répartition globale du travail

| Phase | Part |
|---|---:|
| Phase 0 — Destruction et supply chain | 16 % |
| Phase 1 — Launcher et queues | 19 % |
| Phase 2 — Processus, annulation, rotation et sorties | 23 % |
| Phase 3 — Ressources et intégrité IA | 20 % |
| Phase 4 — UI et hotspots | 10 % |
| Phase 5 — Release, tests, documentation et licences | 12 % |
| **Total** | **100 %** |

Ces pourcentages estiment l'effort de correction et de validation, pas le nombre de fichiers. La phase 2 est la plus lourde parce qu'elle exige des tests de processus et de fermeture réels ; la phase 0 reste la première à exécuter malgré une part plus faible.

## 8. Améliorations facultatives, faux problèmes et travaux à reporter

À reporter tant que les constats critiques/élevés ne sont pas clos :

- migration WPF/MAUI/Avalonia/Electron, MVVM, conteneur DI, service locator ou architecture en couches générique ;
- compatibilité Linux ou abstraction multiplateforme des dialogues/processus ;
- réécriture globale de `FfmpegRunner`, `ConversionBatchSession` ou de tous les formulaires uniquement à cause de leur taille ;
- création d'interfaces/factories pour chaque action simple ;
- fusion de toutes les petites duplications de formulaires stables ; cibler seulement Crop et Remove Noise ;
- embedding des modèles IA dans l'installateur ; leur téléchargement à la demande est un choix correct ;
- remplacement de la queue séquentielle par du parallélisme général ; elle protège aujourd'hui GPU, disque et mémoire ;
- ajout mécanique de `AutoScaleMode.Dpi` à tous les éditeurs sans test, au risque d'un double scaling ;
- suppression aveugle de tous les `-y` ; le besoin réel est une réservation atomique du chemin ;
- obligation de `-progress pipe:1` dans les pipelines raw duplex : leur progression par frames est une exception technique légitime, à documenter, tandis que leur cycle de vie doit rester centralisé ;
- migration xUnit v3 ou mise à niveau majeure de chaque package uniquement parce qu'une version plus récente existe ; aucun package NuGet vulnérable n'a été signalé lors de cet audit ;
- signature Authenticode immédiate : utile avant diffusion large, mais moins urgente que la sécurité de désinstallation, les binaires FFmpeg et les notices ;
- tests UI exhaustifs pixel-perfect. Des tests ciblés de portée, queue, fermeture, DPI et ressources suffisent d'abord ;
- optimisation de micro-allocations ou suppression des fontes statiques avant d'avoir borné les tableaux audio, les BMP et le cache PDF.

Dettes acceptables à conserver : WinForms/.NET 8, traitement local, `Program` partiel, classes numériques explicites, batch séquentiel et approche Windows-first. L'architecture actuelle n'a pas besoin d'être « modernisée » ; elle a besoin que ses quelques frontières sensibles soient rendues cohérentes et testables.
