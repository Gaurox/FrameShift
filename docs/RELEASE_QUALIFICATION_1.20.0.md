# Qualification de FrameShift 1.20.0

## Statut

**Installateur accepté par l'utilisateur le 2 octobre 2026 ; publication GitHub autorisée et en préparation.** Le fichier validé est conservé avec les empreintes ci-dessous.

Les validations A–F restent conservées dans [l'audit](UI_AUDIT_AND_STANDARDIZATION_PLAN_2026-09-28.md). Elles portent sur les builds de développement identifiés ; elles ne sont pas remplacées par une affirmation de recette de l'installation 1.20.0.

## Décision de préparation

- Version application : `1.20.0` ; versions assembly/fichier : `1.20.0.0`.
- `PerMonitorV2` activé pour Debug **et Release** via l'initialisation SDK, avant les fenêtres.
- Version indépendante du worker sous-titres conservée : `1.18.1` ; pas de modification de son moteur ou de ses dépendances.
- Unique chaîne de production : `build_installer.ps1`, sans filtre de tests, sans `-AllowDirty` et sans `-RunInstaller`.
- Tests WinForms exécutables sur un bureau Windows privé, sans `SwitchDesktop`, capture, modification des réglages ou interaction avec le bureau utilisateur. Cette isolation ne simule pas une recette réelle à plusieurs DPI.
- Aucun tag, push, upload ou release GitHub dans cette préparation.

## Identification et preuves automatiques

### Source et chaîne

| Élément | Résultat |
|---|---|
| Commit source de l'artefact | `dd5428701ebe7bf62e46906ff89f49906a1a217d` (`Prepare 1.20.0 release candidate and enable Release DPI handling`) |
| État au lancement | Commit propre, sans `-AllowDirty`, filtre de tests ni installation automatique |
| SDK / runtime embarqué | .NET SDK `8.0.420` ; runtime application/worker `8.0.26`, `win-x64` self-contained |
| Build Debug requis après modification | Réussi, **0 avertissement / 0 erreur** |
| Run canonique | Début le 2 octobre 2026 à **22:39:50 CEST** ; `build_installer.ps1`, code de sortie **0** |
| Suite Release complète | **642 réussites, 0 échec, 5 ignorés, 647 cas** ; durée annoncée **4 min 49 s** |
| Compilation Inno | `6.7.1`, réussie en **72,578 s** ; installateur actualisé le **2 octobre 2026 à 22:46:09 CEST** |
| Payload | **775 fichiers**, **556 254 907 octets** ; FFmpeg/FFprobe conformes aux SHA-256 verrouillés |
| Dépendances et licences | Restauration verrouillée réussie ; worker, ONNX, Sherpa, DirectML, runtimes et notices requis présents |
| Hygiène | Aucun `.pdb`, `.dbg`, testeur UI, assembly de tests ou faux outil média dans le payload |

Les cinq cas ignorés sont les tests d'intégration Create Subtitles nécessitant les médias/modèles du dossier local `scratch/WhisperBaseOnnxSpike/` : formats de sortie alternatifs, audio long, équivalence audio/vidéo, modèle Small FR/EN et annulation entre fenêtres. Leur attribut de découverte les exclut explicitement en cas d'assets manquants ; ils ne sont pas comptés parmi les réussites. L'inférence réelle et ces exports restent à vérifier dans la recette installée.

### Identité de l'installateur et du programme

| Fichier | Version / taille | SHA-256 |
|---|---|---|
| `installer/FrameShift_1.20.0_Setup.exe` | Version installateur `1.20.0` ; **167 335 254 octets** (environ 167 Mo) | `5B055346F20E4359249B84F64C62D3153EF0765819C996B9A51A52D557C2D6A6` |
| `publish/FrameShift-win-x64/FrameShift.exe` | Version fichier `1.20.0.0` | `EF4EAE644BE188688C82120AE922D9690880D5B65317DA26E128DE757C9959FB` |
| `publish/FrameShift-win-x64/FrameShift.dll` | Version assembly `1.20.0.0` | `53DEA54697361942369181B96E0DFF3518C42E759B28A36CEED023E5827565F7` |
| `Workers/CreateSubtitlesWorker/FrameShift.SubtitlesWorker.exe` | Version fichier `1.18.1.0` | `DA4C15F6B6626FB1EAB712572D7C5E403230E2D2A68B1C53FC1690CAFCDD5463` |

Les versions produit application/worker contiennent le suffixe source `+dd5428701ebe7bf62e46906ff89f49906a1a217d`. Les commits documentaires ultérieurs de consignation ne changent pas cet artefact : le futur tag doit identifier ce commit de production pour publier le fichier effectivement testé, ou une nouvelle construction doit être identifiée et validée.

### Contrôles du binaire publié

- Une sonde .NET 8 invoque **l'initialiseur SDK de `publish/FrameShift-win-x64/FrameShift.dll`**, avec le runtimeconfig/deps de la distribution. Résultat : version `1.20.0.0`, `Application.HighDpiMode=PerMonitorV2`, contexte natif Windows égal à `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2`, handle caché à **96 DPI**, `Visible=false`, code **0**. Le DPI reçu sur ce bureau privé n'est pas une preuve de recette à 150/200/300 %.
- L'apphost autonome **`publish/FrameShift-win-x64/FrameShift.exe`** est lancé directement deux fois sur une copie d'image dans un chemin avec espaces et accents. `resize-image` produit deux images **160 × 120**, chacune avec code **0** ; sorties adjacentes distinctes, hash de la source et du premier résultat conservés. Logs/config/temp restent propres à ce scénario sous `scratch/`.
- L'isolation native est d'abord vérifiée par un `Form.Show()` sur un bureau `FrameShiftRelease_…`, sans bascule. Le run canonique utilise `FrameShiftRelease_8dfc6faa1e594ca0934e6cba434963ad` ; les tests affichants ne s'ouvrent pas sur le bureau utilisateur. Aucune capture, injection d'entrée ou modification des réglages Windows.
- Vérification documentaire finale : **158 liens locaux**, aucun fichier manquant ; `git diff --check` sans défaut d'espacement. Cette consignation finale ne modifie pas le code ou l'installateur testé.

### Traces locales

Dans `scratch/release-1.20.0/`, hors distribution :

- `canonical-build-01.log` : chaîne officielle complète, résultats tests et compilation ;
- `isolation-probe.log`, `Invoke-PrivateDesktop.ps1`, `Build-Candidate.ps1` : exécution isolée ;
- `published-dpi.json` et `published-dpi-probe-01.log` : sonde de l'initialiseur publié ;
- `published-runtime-smoke.json` et `published-runtime-smoke-01.log` : deux traitements réels ;
- `payload-manifest.json` : taille et SHA-256 de chacun des 775 fichiers ;
- `FrameShift_1.20.0_Setup.exe.sha256` : empreinte de l'installateur ;
- `document-links.json` : vérification des liens.

## Acceptation utilisateur et décision de publication

Après remise de l'installateur et de [la procédure G](UI_PHASE_G_MANUAL_TESTS.md), l'utilisateur confirme : **« ok validé. push le programme sur githuib et publie la release avec le tag 1.20.0 et l'installeur à télécharger »**, avec une demande de description courte.

L'acceptation porte sur la candidate identifiée, sans modification de son code, de son installer ou de ses dépendances après recette. La publication est autorisée avec le tag exact **`1.20.0`** sur le commit source **`dd54287`**, et l'installateur de SHA-256 **`5B055346F20E4359249B84F64C62D3153EF0765819C996B9A51A52D557C2D6A6`**.

**Portée des preuves :** le retour manuel est global. Il ne fournit pas de relevé distinct des résolutions, moniteurs, taille du texte, installations personnalisées ou résultats des cinq intégrations Whisper ignorées. Ces axes ne sont pas transformés en résultats automatiques ni en certification exhaustive de la matrice section 8. Aucun défaut P1 n'est signalé dans cette acceptation ; la décision de publication de l'artefact validé est explicite.
