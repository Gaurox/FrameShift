# Qualification de FrameShift 1.20.1

## Statut

**Release de maintenance sécurité publiée le 3 octobre 2026 après autorisation du propriétaire ; téléchargement public vérifié identique à l'installeur préparé.** Le tag annoté `1.20.1` identifie exactement le commit compilé. Les corrections des quatre P1 sont décrites dans [le bilan d'implémentation](SECURITY_P1_IMPLEMENTATION_2026-10-03.md). La recette installée reste non exécutée, avec le périmètre de cette limite indiqué ci-dessous.

La licence Community fournie par le propriétaire est acceptée par le vérificateur officiel ImageSharp. Le fichier reste privé, hors du dépôt et de l'installateur. Son courriel annonce une expiration le 1er janvier 2028 ; le [README](../README.md#imagesharp-licence--maintainers-and-ai-agents) rappelle le renouvellement.

## Chaîne officielle et identité de la candidate

| Élément | Résultat |
|---|---|
| Commit source de l'artefact et cible du tag | `0b31a16329eba739cd47e725b287362684389c47` |
| État au lancement | Arbre propre ; commande officielle `build_installer.ps1`, sans `-AllowDirty`, filtre de tests ni `-RunInstaller` |
| Chaîne officielle | Début **22:59:40 CEST**, installateur finalisé **23:06:35 CEST**, code de sortie **0** |
| SDK / runtimes | SDK **8.0.425** ; application .NET/Windows Desktop **8.0.31**, worker .NET **8.0.31**, autonomes `win-x64` |
| Suite Release complète | **690 réussis, aucun échec, 5 ignorés, 695 cas** ; durée **5 min 11 s** |
| Modèles IA | Variable `FRAMESHIFT_SECURITY_AI_MODELS` configurée sur les modèles locaux existants ; les **10 cas IA P1** sont inclus dans les réussites, sans téléchargement |
| Paquets / payload | Trois restores verrouillés ; contrôles canoniques ImageSharp **4.1.2**, manifests et binaires des runtimes, worker, FFmpeg/FFprobe et notices tous réussis |
| Inno Setup | **6.7.1**, compilation réussie en **80,250 s**, depuis le vrai payload Release |
| Inventaire du payload | **776 fichiers**, **601 651 177 octets** avant exclusions de l'installateur ; manifeste SHA-256 de chaque fichier conservé localement |
| Hygiène | Aucun `.pdb`, `.dbg`, `sixlabors.lic`, assembly de tests, testeur UI ou sonde de qualification dans le payload |

Les cinq tests ignorés nécessitent les modèles/corpus de `scratch/WhisperBaseOnnxSpike/` : formats alternatifs de sous-titres, audio long, équivalence audio/vidéo, modèle Small FR/EN et annulation entre fenêtres. Ils ne sont pas comptés comme réussis.

### Identité des artefacts

| Fichier | Version / taille | SHA-256 |
|---|---|---|
| `installer/FrameShift_1.20.1_Setup.exe` | **1.20.1**, **171 550 754 octets** | `E606E5A30B4F7AC5E9ECD9685C261FE3964641321F301933296BE64C764B8991` |
| `publish/FrameShift-win-x64/FrameShift.exe` | **1.20.1.0** | `983BBE2D1D744D36F9D5F84570A3869A4E40293FBC765279F56D05950E2C3188` |
| `publish/FrameShift-win-x64/FrameShift.dll` | **1.20.1.0** | `675942CBC54F138283E01ABE40025C97BA9A918406DE93B79873E47278706985` |
| `Workers/CreateSubtitlesWorker/FrameShift.SubtitlesWorker.exe` | **1.18.1.0** | `5083F3C94B30F70DAF8CC0BE137D7A094571246A3438E7D07596FF86E0C60613` |

Le numéro propre au worker reste 1.18.1 ; ses fichiers sont reconstruits depuis le même commit et embarquent le runtime corrigé. Les versions produit de l'application et du worker portent toutes le suffixe `+0b31a16329eba739cd47e725b287362684389c47`. Les commits documentaires ultérieurs ne modifient pas cet artefact ; le tag reste sur son commit de construction. Une reconstruction devra être identifiée et vérifiée de nouveau.

### Contrôles du programme autonome

- L'apphost publié est lancé directement, sur des copies d'image dans des chemins avec espaces et accents. **Deux redimensionnements successifs** produisent des images 160 × 120 sous des noms distincts ; source et premier résultat conservés.
- **Deux apphosts simultanés** produisent deux images 160 × 120 distinctes, avec code de sortie 0 ; source intacte et aucun workspace `.frameshift-*` restant. Ce traitement concurrent complète les tests de publication synchronisés du chantier P1.
- La sonde invoque l'initialiseur du véritable `FrameShift.dll` publié : version **1.20.1.0**, `PerMonitorV2` managé et natif, contrôle caché à 96 DPI. Ce résultat ne qualifie pas plusieurs moniteurs ou facteurs de mise à l'échelle.
- La chaîne et les sondes utilisent des bureaux Windows privés, sans bascule ni interaction avec le bureau utilisateur. Les fichiers de configuration et temporaires des traitements restent propres aux essais.
- Aucun processus FFmpeg/FFprobe restant lors du contrôle final. Aucun installeur n'est exécuté sur la machine de développement.

### Traces locales et fichiers de publication

Preuves ignorées par Git dans `scratch/release-1.20.1/` : `canonical-build-01.log`, `published-runtime-smoke.json`, `published-concurrent-smoke.json`, `published-dpi.json`, `payload-manifest.json` et `artifact-verification.json`, avec les scripts et journaux associés.

Fichiers prêts pour la future publication :

- `installer/FrameShift_1.20.1_Setup.exe` : installer effectivement construit et contrôlé ;
- `builds/release-1.20.1/FrameShift_1.20.1_Setup.exe.sha256` : empreinte au format standard ;
- `builds/release-1.20.1/github-release-body.md` : texte exact extrait des [notes de release](RELEASE_NOTES_1.20.1.md), utilisable avec `gh release create --notes-file`.

Les anciens artefacts Debug et de syntaxe Inno du chantier P1 ne servent pas à cette release.

## Qualification installée restante

La [phase 3 du plan P1](SECURITY_P1_REMEDIATION_PLAN_2026-10-03.md#phase-3--construire-et-vérifier-la-distribution) prévoit une VM Windows avec snapshot :

- Installation neuve, mise à jour depuis 1.20.0, réinstallation, installation core seule, menus Explorer et désinstallation Windows.
- Entrée HKCU trompeuse et témoin inoffensif, sans exécution élevée ; élévation avec le même compte puis un autre compte.
- Conservation des modèles et fichiers étrangers lors de la désinstallation.
- Vérification du runtime réellement installé pour l'application et le worker ; traitements représentatifs, collisions, annulation, PDF, refus de TIFF et chemins CPU/DirectML affectés.

Ces scénarios ne sont pas validés par la seule compilation ou les tests du dépôt. Aucun environnement VM/Sandbox prêt à l'emploi n'a été trouvé lors du chantier P1. La machine de développement ne sert pas à simuler une attaque sur le registre élevé.

Les modèles LaMa/DeepFilterNet et certains corpus Whisper ne sont pas disponibles localement. Les tests ignorés et les recettes non exécutées doivent rester explicites.

## Décision de publication

Après remise de l'installeur, des résultats automatiques et de la limite de qualification installée, le propriétaire autorise explicitement : **« ok parfait push et publie »**.

La publication porte sur le tag **`1.20.1`**, le commit source **`0b31a16329eba739cd47e725b287362684389c47`** et l'installeur de SHA-256 **`E606E5A30B4F7AC5E9ECD9685C261FE3964641321F301933296BE64C764B8991`**. Aucune reconstruction ni substitution de l'artefact accepté n'est prévue.

Ce retour autorise la diffusion avec les limites précédemment signalées. Il ne fournit pas de résultats supplémentaires pour l'installation en VM, les entrées HKCU trompeuses, la désinstallation ou les recettes manquantes. Ces scénarios restent non exécutés et les preuves de clôture exhaustive des quatre P1 restent ouvertes.

## Publication GitHub vérifiée

- [Release stable **1.20.1**](https://github.com/Gaurox/FrameShift/releases/tag/1.20.1), identifiant `402694541`, publiée le **3 octobre 2026 à 23:15:40 CEST** (`2026-10-03T21:15:40Z`). Publique, sans statut prerelease, marquée comme dernière version.
- Code et documents poussés vers `main`. Le push initial atomique pointe la branche sur `99f028d`, avec le tag annoté **`1.20.1`**, objet `2d58c412a2235645c1e6aecf8555c0b248dd3abc`, pointant sur le commit de construction **`0b31a16329eba739cd47e725b287362684389c47`**.
- [Installeur public](https://github.com/Gaurox/FrameShift/releases/download/1.20.1/FrameShift_1.20.1_Setup.exe), asset `608547272`, **171 550 754 octets**. Empreinte GitHub identique à l'artefact accepté : **`E606E5A30B4F7AC5E9ECD9685C261FE3964641321F301933296BE64C764B8991`**.
- [Fichier SHA-256](https://github.com/Gaurox/FrameShift/releases/download/1.20.1/FrameShift_1.20.1_Setup.exe.sha256), asset `608547274`, vérifié conforme au fichier local. Aucun secret de licence joint.
- Téléchargement **sans authentification** depuis l'URL publique, puis contrôle local : taille et SHA-256 identiques. Vérification terminée à `2026-10-03T21:16:26Z`. Aucune reconstruction ou substitution du binaire.
- Texte publié conforme aux [notes de release](RELEASE_NOTES_1.20.1.md). README et changelog actualisés après vérification du téléchargement.
- Traces locales : `github-draft-metadata.json`, `github-published-metadata.json`, `github-download-verification.json`, et copie téléchargée dans `scratch/release-1.20.1/download-verification/`.
