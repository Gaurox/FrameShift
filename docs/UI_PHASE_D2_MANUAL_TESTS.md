# Phase D2 — recette des dialogues

Base du lot : `d83a9e4`, après validation complète de D1. Révision du 1er octobre 2026 : hauteurs compactes, grille Resize 2 × 2 avec saisies compactes, préréglages Speed Video sur une ligne, états Rotate sobres et agencement Icon proche de l'ancien. **Rendu et recette à 100 % validés par l'utilisateur le 1er octobre** (« ok tout validé en 100%/ committe »). Les essais aux autres échelles et les contrôles complémentaires restent en attente. D2 comprend 12 fenêtres ; Conversion et Speed ont plusieurs variantes, Resize Image/Video partagent leur base existante.

## Ouvrir le lanceur

Double-cliquer sur **`TEST_PHASE_D2.cmd` à la racine du projet**. Aucune commande à saisir. Choisir une vidéo, un audio et une image avec les boutons du lanceur ; les fichiers de `scratch/phase-a` sont proposés quand disponibles.

Le lanceur privilégie désormais MP3/M4A/OGG pour l'audio. Un MP3 d'essai a été préparé dans `scratch/phase-a` à partir du WAV existant pour permettre la vérification de la taille cible. Sur WAV/FLAC, cette fonction est indisponible : l'explication s'affiche dans la fenêtre. Utiliser le MP3 proposé ou sélectionner un fichier compatible pour tester la case.

Le lanceur ouvre les formulaires réels du build de développement. OK / Apply / Compress / Convert / Continue affichent uniquement les réglages choisis dans le lanceur : aucun export final. Les aperçus lisent les sources. **Preview 5s** crée un aperçu temporaire ; Speed Video ouvre le lecteur vidéo habituel. Fermer ce lecteur avant de quitter le dialogue pour permettre la suppression immédiate du temporaire. Add Subtitles ouvre seulement le sélecteur, pas l'éditeur d'incrustation.

Le lanceur ne modifie ni les réglages Windows ni l'application installée. Les essais visuels sont manuels, sans prise de contrôle ni capture automatisée.

## Premier passage à 100 %

Pour chaque fenêtre : vérifier le bandeau, les espacements, les noms longs, les commandes de taille identique et la fermeture par Cancel / Échap. Les dialogues compacts doivent afficher leurs options sans défilement quand l'écran le permet. Le défilement est accepté en espace réduit. Réduire puis agrandir la fenêtre et parcourir les champs avec Tab / Maj+Tab.

| Fenêtre | Manipulations |
|---|---|
| Conversion vidéo/audio/image | Changer format et profil ; lire les descriptions. Image n'a pas de profil dans ce lanceur. Valider et vérifier les identifiants affichés. |
| Compression multiple | Vérifier l'absence de grand vide en bas à l'ouverture. Alterner « mêmes réglages » et « chaque fichier ». Un seul choix doit rester actif. |
| Compress Audio/Video | Vérifier la hauteur initiale compacte. Choisir successivement les trois qualités ; sur un MP3/M4A/OGG pour l'audio, cocher Target file size, saisir `1,5`, alterner MB/KB. Décocher puis recocher et vérifier l'accès à la saisie et à l'unité. Vérifier le résultat. Pour un FLAC/WAV, la cible audio doit être indisponible avec une explication explicite. |
| Resize Image/Video | Vérifier les quatre saisies compactes, identiques et alignées : largeur/hauteur en rangées, pixels/pourcentages en colonnes. Leur largeur est environ divisée par deux par rapport au premier rendu 2 × 2, et reste compacte quand on agrandit la fenêtre. Modifier les quatre champs ; vérifier le ratio verrouillé/déverrouillé, les cinq préréglages et Reset. Les valeurs média ne doivent pas changer quand seule la fenêtre est redimensionnée. |
| Change Pitch | Saisir `12` demi-tons : `200 %`. Tester un préréglage négatif et Keep original duration. Preview 5s, puis fermeture pendant sa préparation ; aucun son tardif après fermeture. |
| Speed Audio/Video | Sur Speed Video, vérifier que les huit préréglages tiennent sur une seule ligne à l'ouverture par défaut. Saisir `200 %` : durée divisée par deux. Modifier la durée, essayer les préréglages. Keep original audio pitch est cochée par défaut sur audio : la décocher puis la recocher par clic, puis avec Tab/Espace ; vérifier le réglage renvoyé. Sur vidéo sans audio, cette option est désactivée. Tester la fermeture pendant Preview 5s. |
| Rotate/Flip Image/Video | Tester les deux rotations, les deux miroirs, Reset. Les boutons gardent leurs couleurs standard ; ✓ indique un miroir actif, le résumé indique l'angle. Apply désactivé sans transformation ou sans aperçu chargé. Les commandes restent sous l'image ; la timeline vidéo reste accessible. Déplacer rapidement la timeline puis fermer. Refaire avec une image WebP et un média portrait. |
| Convert to Icon | À l'ouverture, retrouver les tailles en liste à gauche, les réglages au centre et les neuf aperçus à droite. Réduire la largeur : les blocs doivent se replier sans commandes coupées. Select all / Clear all, sélectionner seulement 32 et 256 ; cliquer les vignettes ; tester Fit/Fill et les trois fonds. Les tailles exportées restent 32/256 même si la fenêtre ou son échelle change. |
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
