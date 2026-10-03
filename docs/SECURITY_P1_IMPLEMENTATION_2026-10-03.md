# FrameShift — Application du plan correctif P1

Date : 3 octobre 2026. Branche de travail : `codex/security-p1`, changements poussés vers `main`. Version : **[1.20.1, publiée](https://github.com/Gaurox/FrameShift/releases/tag/1.20.1)**.

Références : [audit initial](SECURITY_AUDIT_2026-10-03.md), [plan en trois phases](SECURITY_P1_REMEDIATION_PLAN_2026-10-03.md).

## État des quatre P1

**Les corrections de code et la construction officielle de la candidate sont terminées.** ImageSharp est réellement migré en 4.1.2. La licence Community est acceptée ; la chaîne Release complète réussit : **690 tests réussis, 5 ignorés, aucun échec**, payload autonome contrôlé et installeur 1.20.1 compilé. Le [rapport de candidate](RELEASE_QUALIFICATION_1.20.1.md) identifie le commit, les empreintes et les vérifications. Le fichier de licence reste hors du dépôt ; aucun contrôle n'est désactivé.

| Priorité | Défaut | Correction réalisée | Reste avant clôture de distribution |
|---|---|---|---|
| P1 / FS-SEC-01 | Exécution élevée d'un désinstalleur provenant du registre | Chemin d'exécution et option supprimés. Détection machine HKLM séparée de l'information HKCU. Mise à jour/réinstallation conservées ; désinstallation par Windows. | Recette de l'installateur en VM : première installation, mise à jour, réinstallation, désinstallation Windows et entrée HKCU trompeuse. |
| P1 / FS-SEC-02 | Décodeurs ImageSharp vulnérables | Dépendance directe **4.1.2**, API BMP adaptée, trois verrouillages et notices actualisés. Toutes les lectures de fichiers utilisent exclusivement PNG/JPEG/WebP/BMP ; ICC préservé explicitement. Formats, PDF et inférences CPU/DirectML vérifiés. Licence acceptée, suite Release réussie et paquet corrigé embarqué. | Vérification sur la candidate installée. |
| P1 / FS-SEC-03 | Écrasement ou suppression de fichiers lors de collisions/annulations | Producteurs FFmpeg, PDF, PNG IA, WAV et sous-titres migrés vers un workspace adjacent créé exclusivement, puis publication sans remplacement. Chemins Windows longs et UNC pris en charge. | Rejouer les cas représentatifs sur la candidate installée ; les coupures d'un partage distant et le disque plein ne sont pas simulés ici. |
| P1 / FS-SEC-04 | Runtime autonome .NET ancien | SDK **8.0.425** épinglé, .NET/Windows Desktop **8.0.31** dans le vrai payload Release autonome, worker inclus. Contrôles canoniques des manifests, DLL, ImageSharp, outils et notices réussis ; démarrage autonome et traitements réels réussis. | Contrôler le contenu installé. |

**Les preuves de clôture exhaustive des quatre P1 restent ouvertes pour la recette installée.** Le propriétaire a autorisé la diffusion après présentation des résultats et de cette limite. La release `1.20.1` et son tag sont publiés ; le téléchargement public est vérifié identique à l'installeur construit. Aucun scénario d'installation en VM n'est ajouté aux réussites. La publication ne met pas automatiquement à jour l'application déjà installée.

## Conservation des sorties

- `OutputOperation` crée exclusivement `.frameshift-<GUID>` à côté de la sortie. Pour un export de frames, seul son sous-dossier `payload` est publié.
- `File.Move(..., overwrite: false)` ou `Directory.Move` publie le résultat. Seules les collisions Windows sont retentées, avec un maximum de 1 000 noms. Un verrouillage ou un refus d'accès reste une erreur.
- Les chemins longs utilisent le préfixe Windows étendu lors de l'appel natif de création ; les chemins UNC et les jonctions ne sont pas interdits.
- Annulation avant publication : nettoyage du workspace possédé. Après publication : résultat conservé, même si une notification échoue ou déclenche une annulation tardive.
- Tous les entêtes WAV sont finalisés avant la première publication. Un échec de création du deuxième writer nettoie le premier ; un échec de publication après une première sortie conserve celle-ci et indique son chemin.
- Les diagnostics ASS suivent le nom final des sous-titres et ne remplacent pas un rapport existant. Leur échec ne supprime pas les sous-titres terminés. Le nettoyage d'un fichier étranger `outputPath + ".tmp"` est retiré.
- Les écritures intermédiaires FFmpeg avec `-y` restent confinées aux chemins de travail possédés. Journaux, configurations, caches et téléchargement des modèles restent hors du périmètre de FS-SEC-03.

## Preuves locales

| Vérification | Résultat |
|---|---|
| Suite complète Debug après migration ImageSharp 4 et correction des chemins longs | **686 réussis, 5 ignorés, aucun échec** ; 691 tests. Les cinq ignorés nécessitent le corpus et les modèles Whisper base/small dans les chemins scratch attendus. |
| Dernière passe ciblée P1 | **54 réussis, aucun ignoré ni échec**. Inclut quatre cas ajoutés après la suite complète : refus d'accès NTFS et trois parcours rawvideo réels. Dernier ajustement du handle du test NTFS revérifié séparément : 1 réussi. |
| Contrôles de la chaîne de distribution | **15 tests réussis** dans les suites ci-dessus : cas conforme, runtime application/worker ancien, ImageSharp ancien, fichier absent, manifeste incohérent, DLL runtime incorrecte. |
| Images et ICC | TIFF renommés refusés ; PNG/JPEG/WebP/BMP, transparence, dimensions/couleurs ; ICC embarqué préservé et régression du profil CLUT tronqué sans allocation excessive. |
| Intégrations sans IA | FFmpeg réel, extraction de frames, PDFsharp/WebP et exports concurrents, diagnostics ASS, collisions fichier/dossier, annulation et WAV partiellement publié. Chemins avec espaces et accents. |
| Inférences avec modèles locaux | **10 cas réussis** : upscale PNG, upscale BMP vidéo et upscale rawvideo sur **CPU et DirectML** ; détourage BiRefNet sur **CPU et DirectML** ; RIFE BMP et rawvideo sur **DirectML**. Annulation avant/après publication et conservation de sorties étrangères vérifiées selon les cas. Aucun modèle téléchargé. |
| Deux processus Windows distincts | Deux contenus conservés sous des noms distincts, aucun workspace résiduel : chemin local, jonction, UNC localhost et UNC long. |
| Projet WinForms de qualification | Restore verrouillé et compilation Debug de `FrameShift.UiSamples` réussis. |
| Vrai payload autonome de contrôle | Publication Debug `win-x64` réussie dans `.buildcheck/security-p1/debug-payload`. `Assert-PublishPayload` de la chaîne officielle passe intégralement, sans modifier les manifests ni remplacer manuellement des DLL. |
| Ancien contrôle sans licence | Trois restores verrouillés réussis ; compilation des tests arrêtée par la licence ImageSharp manquante, avant publication et Inno. Journal conservé : `.buildcheck/security-p1/canonical-release-check.log`. |
| Vérification de la licence reçue | Fichier Community accepté par la validation officielle ImageSharp 4.1.2. Trois restores Release verrouillés et compilation Release de l'application/worker réussis : **0 erreur, 0 avertissement**. Le courriel annonce une validité jusqu'au **1er janvier 2028**. Journal local sans clé affichée : `.buildcheck/security-p1/license-release-verification.log`. |
| Installateur | Script corrigé compilé avec Inno Setup **6.7.1** lors du lot précédent. Son artefact de syntaxe utilise un ancien payload de contrôle ; ce n'est pas une candidate corrigée à distribuer. |
| Candidate officielle 1.20.1 | Chaîne complète sur le commit propre `0b31a16`, code **0** : **690 réussis, 5 ignorés, aucun échec**, dix cas IA inclus ; vrai payload Release vérifié, Inno **6.7.1**, installer **171 550 754 octets**. Identité et SHA-256 dans le rapport de candidate. |
| Programme autonome 1.20.1 | Deux exports successifs et deux instances simultanées réussis ; originaux et sorties conservés, chemins avec espaces/accents et aucun workspace restant. Version 1.20.1 et initialisation DPI confirmées sur le DLL publié. |
| Revue finale | Aucun appel `UninstallString`/désinstalleur externe restant dans le setup ; toutes les lectures ImageSharp de fichiers centralisées ; `LoadPixelData` rawvideo conservé ; `git diff --check` sans anomalie. |

Les résultats TRX sont conservés dans `.buildcheck/security-p1/test-results` : `security-p1-final-debug.trx` et `security-p1-qualification-final.trx`. Les preuves temporaires, sondes et payloads restent ignorés par Git. Le SDK local a été téléchargé depuis Microsoft et son SHA-512 vérifié contre les métadonnées officielles.

Le payload Debug est un contrôle technique, **pas une release à distribuer**. Les inférences locales ne remplacent pas la qualification de l'installation. Aucun environnement VM/Sandbox prêt à l'emploi n'a été trouvé. Les modèles LaMa/DeepFilterNet et le corpus Whisper requis par les tests ignorés ne sont pas disponibles ; leurs recettes complètes restent à reprendre avec les actifs adaptés. Le loopback UNC ne prouve pas le comportement de tous les serveurs SMB ou de leurs coupures.

## Ce qui reste à faire, dans l'ordre

1. **Licence : étape réalisée.** Le propriétaire a effectué la demande et fourni le fichier Community. Son acceptation par le paquet officiel est vérifiée. Le fichier reste privé, hors du dépôt et hors des artefacts distribués.
2. **Chaîne officielle : étape réalisée.** Suite Release complète réussie sur arbre propre, application et worker autonomes contrôlés, Inno compilé. Le tag local identifie le commit compilé. Aucun correctif de migration ImageSharp supplémentaire n'est actuellement identifié.
3. **Compléter la qualification installée en VM**, selon la phase 3 du plan, pour terminer les preuves de clôture exhaustive. Le propriétaire a autorisé la diffusion de la 1.20.1 avec la limite signalée ; cette autorisation ne vaut pas résultat positif des scénarios non exécutés.

## Reprendre les vérifications

Le SDK préparé localement peut être utilisé sans changer le SDK système :

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.buildcheck\dotnet-home"
$dotnet = "$PWD\.buildcheck\sdk\8.0.425\dotnet.exe"
& $dotnet restore tests/FrameShift.Tests/FrameShift.Tests.csproj -p:Configuration=Debug --locked-mode
& $dotnet restore src/FrameShift.SubtitlesWorker/FrameShift.SubtitlesWorker.csproj -p:Configuration=Debug --locked-mode
# Facultatif : chemin de modèles existants pour activer les 10 cas IA locaux.
$env:FRAMESHIFT_SECURITY_AI_MODELS = 'G:\FrameShiftModels'
& $dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj -c Debug --no-restore
```

Après obtention de la licence, conserver le fichier hors du dépôt et définir son chemin pour MSBuild. Le script détecte le SDK local préparé, sinon il exige le SDK épinglé :

```powershell
$env:SixLaborsLicenseFile = 'C:\chemin-prive\sixlabors.lic'
.\build_installer.ps1
```

Le fichier `sixlabors.lic` est ignoré par Git. Aucune clé ne doit être inscrite dans le code ou les journaux. Ne pas désactiver les contrôles de licence, les tests Release ni les vérifications du payload.
