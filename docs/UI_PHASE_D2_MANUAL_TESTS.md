# Phase D2 — recette des dialogues

Base : `c26a723`, après validation complète de D1. D2 comprend 12 fenêtres ; Conversion et Speed ont plusieurs variantes, Resize Image/Video partagent leur base existante.

## Ouvrir le lanceur

Double-cliquer sur **`TEST_PHASE_D2.cmd` à la racine du projet**. Aucune commande à saisir. Choisir une vidéo, un audio et une image avec les boutons du lanceur ; les fichiers de `scratch/phase-a` sont proposés quand disponibles.

Le lanceur ouvre les formulaires réels du build de développement. OK / Apply / Compress / Convert / Continue affichent uniquement les réglages choisis dans le lanceur : aucun export final. Les aperçus lisent les sources. **Preview 5s** crée un aperçu temporaire ; Speed Video ouvre le lecteur vidéo habituel. Fermer ce lecteur avant de quitter le dialogue pour permettre la suppression immédiate du temporaire. Add Subtitles ouvre seulement le sélecteur, pas l'éditeur d'incrustation.

Le lanceur ne modifie ni les réglages Windows ni l'application installée. Les essais visuels sont manuels, sans prise de contrôle ni capture automatisée.

## Premier passage à 100 %

Pour chaque fenêtre : vérifier le bandeau, les espacements, les noms longs, les commandes de taille identique et la fermeture par Cancel / Échap. Les dialogues compacts doivent afficher leurs options sans défilement quand l'écran le permet. Le défilement est accepté en espace réduit. Réduire puis agrandir la fenêtre et parcourir les champs avec Tab / Maj+Tab.

| Fenêtre | Manipulations |
|---|---|
| Conversion vidéo/audio/image | Changer format et profil ; lire les descriptions. Image n'a pas de profil dans ce lanceur. Valider et vérifier les identifiants affichés. |
| Compression multiple | Alterner « mêmes réglages » et « chaque fichier ». Un seul choix doit rester actif. |
| Compress Audio/Video | Choisir successivement les trois qualités ; activer une cible, saisir `1,5`, alterner MB/KB. Vérifier le résultat. Pour un FLAC/WAV, la cible audio doit être indisponible. |
| Resize Image/Video | Modifier largeur puis hauteur, pixels puis pourcentages ; vérifier le ratio verrouillé/déverrouillé, les cinq préréglages et Reset. Les valeurs média ne doivent pas changer quand seule la fenêtre est redimensionnée. |
| Change Pitch | Saisir `12` demi-tons : `200 %`. Tester un préréglage négatif et Keep original duration. Preview 5s, puis fermeture pendant sa préparation ; aucun son tardif après fermeture. |
| Speed Audio/Video | Saisir `200 %` : durée divisée par deux. Modifier la durée, essayer les préréglages et Keep original audio pitch. Sur vidéo sans audio, cette option est désactivée. Tester la fermeture pendant Preview 5s. |
| Rotate/Flip Image/Video | Tester les deux rotations, les deux miroirs, Reset. Apply désactivé sans transformation ou sans aperçu chargé. Les commandes restent sous l'image ; la timeline vidéo reste accessible. Déplacer rapidement la timeline puis fermer. Refaire avec une image WebP et un média portrait. |
| Convert to Icon | Select all / Clear all, sélectionner seulement 32 et 256 ; cliquer les vignettes ; tester Fit/Fill et les trois fonds. Les tailles exportées restent 32/256 même si la fenêtre ou son échelle change. |
| Add Subtitles | Alterner piste sélectionnable/incrustation ; choisir un fichier accepté par le mode. Tester un chemin long ; vérifier les indications de formats et le résultat après Continue. |

Pour les messages d'erreur : un fichier absent/invalide doit produire une indication lisible. Les aperçus Rotate restent dans la fenêtre avec leur erreur, sans fermer brutalement le dialogue. Vérifier aussi l'accès à la dernière ligne des descriptions longues.

## Après validation du rendu

- Refaire les variantes à **150/200/300 %**, avec ouverture fraîche, maximisation/restauration et largeur réduite.
- Tester le déplacement entre écrans de DPI différents et le texte Windows agrandi séparément.
- Vérifier clair/sombre si utilisé, focus clavier, Enter/Échap et absence de commandes inaccessibles.
- Revoir le message d'information et le footer des pilotes C / surfaces D1 : D2 corrige la mesure de hauteur du composant commun `FrameShiftStatusMessage` après changement de police/largeur.
- Revenir aux réglages Windows souhaités.

Un retour écrit suffit : **D2 : rendu à 100 % OK**, puis **échelles, clavier, champs liés, aperçus/fermeture, multi-écran et texte agrandi OK** ; préciser les points non testés.

## Vérifications par commandes

Build application et UiSamples ; tests natifs avec handles cachés uniquement. Les tests D2 couvrent les variantes, footer, champs, contenu compact, réglages renvoyés, transformations, tailles ICO et annulation/nettoyage d'un aperçu terminant après fermeture. Les retours tardifs Pitch/Speed sont injectés dans les tests ; ils ne simulent pas une vraie validation visuelle ou un encodage complet.

```powershell
dotnet build tests/FrameShift.UiSamples/FrameShift.UiSamples.csproj --no-restore
dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj --no-restore --filter FullyQualifiedName~UiD2Tests -- xUnit.MaxParallelThreads=1
```

La suite de non-régression utilise le même filtre sans affichage que D1. Les 12 cas ouvrant des fenêtres restent exclus ; les skips média/IA sont comptés séparément. Résultats et traces : section D2 de l'audit. Aucun test de cette recette ne qualifie l'installateur ou le mode DPI Release.
