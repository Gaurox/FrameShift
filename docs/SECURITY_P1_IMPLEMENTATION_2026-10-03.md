# FrameShift — Application du plan correctif P1

Date : 3 octobre 2026. Branche : `codex/security-p1`. Version de travail : **1.20.1, non publiée**.

Références : [audit](SECURITY_AUDIT_2026-10-03.md), [plan validé](SECURITY_P1_REMEDIATION_PLAN_2026-10-03.md).

## État réel

| P1 | Correction appliquée | Validation restante |
|---|---|---|
| FS-SEC-01 — Installateur | Suppression du lancement d'un désinstalleur issu du registre et de son option. Détection machine HKLM séparée de l'information HKCU. Désinstallation par Windows conservée. | Recette en VM : installation, mise à jour, réinstallation, désinstallation Windows, entrée HKCU trompeuse. |
| FS-SEC-02 — ImageSharp | Toutes les lectures de fichiers ImageSharp utilisent une configuration explicite PNG/JPEG/WebP/BMP. TIFF renommés refusés. Aucun changement de `Configuration.Default` ni de `LoadPixelData`. | **Paquet applicatif toujours en 3.1.12**. Migration 4.1.2, adaptations éventuelles, verrouillages et notices après réception d'une licence valide ; tests de rendu et d'inférence à refaire sur cette version. Ce P1 n'est pas clôturé. |
| FS-SEC-03 — Sorties | `OutputOperation` crée exclusivement un workspace adjacent, écrit dedans puis déplace sans remplacement. Migration des producteurs médias FFmpeg, PDF, PNG IA, WAV et sous-titres. Les collisions reçoivent un suffixe numérique. Le nettoyage ne vise que les fichiers de travail. | Qualification installée, inférence avec modèles réels, volumes UNC/jonctions et erreurs d'accès/espace disque représentatives. |
| FS-SEC-04 — Runtime | SDK 8.0.425 épinglé ; runtime autonome 8.0.31 constaté dans l'application et le worker. Restore Release cohérent, contrôle des manifests et DLL avant Inno Setup. | Refaire la chaîne canonique et qualifier la candidate installée lorsque ImageSharp est migré. |

**Les quatre P1 ne sont donc pas déclarés clos.** Aucun installateur destiné à la distribution n'a été produit par la chaîne officielle, aucune installation locale ni publication distante n'a été effectuée.

## Points particuliers des sorties

- Les actions écrivent dans `.frameshift-<GUID>` créé par `CreateDirectoryW`, qui refuse un dossier existant. Les exports de frames sont écrits dans un sous-dossier `payload` puis publiés ensemble.
- `File.Move(..., overwrite: false)` et `Directory.Move` constituent la publication. Seules les erreurs Windows de collision sont retentées ; un fichier verrouillé ou une erreur d'accès reste une erreur. Limite : 1 000 noms, comme le helper existant.
- Le token d'annulation est vérifié avant chaque tentative. Une fois le déplacement réussi, une notification UI défaillante ou une annulation tardive ne supprime pas le résultat.
- Les entêtes WAV de tous les stems sont finalisés avant la première publication. Un échec après une première sortie conserve celle-ci et rapporte son chemin, sans rollback destructif.
- Les diagnostics ASS utilisent la même publication, suivent le nom final des sous-titres et ne remplacent pas un diagnostic existant. Leur échec après publication des sous-titres est journalisé. Le nettoyage de `outputPath + ".tmp"` est retiré.
- `-y` est conservé pour les écritures intermédiaires dans les workspaces possédés. FFmpeg n'écrit plus directement la sortie finale dans ce périmètre. Les journaux/configurations/caches de modèles restent hors du périmètre de FS-SEC-03 ; les autres défauts demeurent dans l'audit.

## Vérifications locales

- Suite Release complète : **670 réussis, 5 ignorés**, après migration de tous les producteurs ; aucun échec. Les cinq tests ignorés nécessitent le corpus et les modèles Whisper. Les derniers tests supplémentaires de diagnostic et d'annulation ainsi que l'ajustement des noms GIF/sous-titres sont vérifiés séparément après cette suite.
- Contrôle de distribution : **15 tests réussis**. Cas conforme, runtime application/worker ancien, ImageSharp ancien, fichier requis absent, manifeste incohérent et binaire runtime incorrect arrêtent la chaîne avant Inno selon le cas attendu.
- Dernière passe ciblée Release : **61 réussis, 5 ignorés**, incluant les deux nouveaux tests de diagnostic et d'annulation et les derniers ajustements de noms. Aucune erreur de compilation ni aucun échec.
- Tests d'intégration supplémentaires : FFmpeg réel (conversion/extraction), PDFsharp réel (WebP et exports concurrents), collision fichier/dossier, notification finale défaillante et commit WAV partiel. Les tests utilisent des chemins avec espaces et accents.
- Debug : **19 tests ciblés réussis**, comprenant le lecteur d'images, la publication et les premiers cas d'intégration.
- Deux processus Windows distincts publient simultanément vers le même nom : deux contenus distincts conservés, noms base/`_001`, aucun workspace résiduel. Sonde locale par réflexion sur la vraie classe `OutputOperation` ; barrière avant publication.
- Publication locale de contrôle sous `.buildcheck/security-p1/payload` : application .NET/Windows Desktop **8.0.31**, worker .NET **8.0.31**. DLL vérifiées : `coreclr`, `System.Private.CoreLib`, `System.Windows.Forms`. Le contrôle du worker passe ; celui de l'application bloque explicitement sur ImageSharp 3.1.12.
- Inno Setup **6.7.1** : compilation réussie du script corrigé dans `.buildcheck/security-p1/installer-syntax`. L'exécutable `FrameShift_security_syntax_only.exe` sert uniquement à vérifier la compilation et **ne doit pas être distribué ou installé comme correctif complet** : son payload de contrôle contient encore ImageSharp 3.1.12.
- `git diff --check` : sans anomalie.

Les preuves temporaires et le SDK préparé restent dans `.buildcheck`, ignoré par Git. Le SDK a été téléchargé depuis Microsoft et son SHA-512 comparé aux métadonnées officielles. Les tests de processus/cancellation existants couvrent les runners ; la recette d'inférence GPU et l'installation en VM ne sont pas remplacées par les tests unitaires.

## Prérequis ImageSharp : démarche à terminer

FrameShift est publié sous GPL v3. Les [conditions Six Labors](https://sixlabors.com/posts/licence-enforcement-changes/) permettent aux projets open source de demander une licence auprès du [portail de licences](https://licensing.sixlabors.com/). La demande doit être faite par le propriétaire avec les informations du projet ; aucune demande ni création de compte n'a été effectuée ici.

Le paquet officiel 4.1.2 a été inspecté : il valide la licence avant `CoreCompile`, avec erreur bloquante en Release. L'exemple de licence fourni sur le site a expiré le 4 septembre 2026. Aucun contournement de cette validation n'est ajouté.

Quand un fichier valide est obtenu, le conserver hors du contrôle de version (`sixlabors.lic` est ignoré) et indiquer son chemin avec `SixLaborsLicenseFile`. Ensuite : migrer la dépendance directe, régénérer les trois verrouillages concernés, adapter les API et notices, puis tester Debug/Release et les formats/couleurs/pipelines IA. Le binaire 4.1.2 téléchargé par le projet de tests est uniquement une fixture de lecture de métadonnées pour la validation du packaging ; il n'est ni référencé ni exécuté et ne remplace pas la dépendance applicative.

## Reprendre la validation

Installer le SDK **8.0.425**, ou utiliser celui préparé localement :

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.buildcheck\dotnet-home"
$dotnet = "$PWD\.buildcheck\sdk\8.0.425\dotnet.exe"
& $dotnet restore tests/FrameShift.Tests/FrameShift.Tests.csproj -p:Configuration=Release --locked-mode
& $dotnet restore src/FrameShift.SubtitlesWorker/FrameShift.SubtitlesWorker.csproj -p:Configuration=Release --locked-mode
& $dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj -c Release --no-restore
```

Le script canonique détecte ce SDK local, vérifie sa version exacte et impose les contrôles du payload. Après migration ImageSharp et tests verts, reprendre les recettes VM/GPU du plan, puis produire la candidate par `build_installer.ps1` sur arbre propre. La diffusion reste conditionnée aux preuves de qualification.
