# Phase E — recette des sept éditeurs

Lot commencé le **2 octobre 2026**, après le commit D3 `50a4626` et le GO utilisateur « commite et go pour E ». Les changements E et leurs correctifs sont sauvegardés sous `902f2a9`. Le même jour, l'utilisateur valide le **rendu des sept éditeurs à 100/150/200/300 %** (« E phalidé dans toutes les échélles »). Les manipulations complémentaires, le multi-écran, la taille du texte indépendante et les exports réels ne sont pas confirmés séparément. Les étapes ci-dessous restent la procédure de référence pour ces contrôles.

## Ouvrir par double-clic

Double-cliquer sur **`TEST_PHASE_E.cmd` à la racine du projet**. Choisir vidéo, audio, image et sous-titres avec les boutons du lanceur. Les fichiers de `scratch/phase-a` sont proposés lorsqu'ils existent ; un petit SRT de démonstration est disponible dans `scratch/phase-e` sur le poste de développement. Une deuxième vidéo est facultative : à défaut, Join propose deux occurrences de la première.

Les fenêtres sont celles du build de développement, indépendamment de l'application installée. Le lanceur utilise les runners FFmpeg/FFprobe du projet pour les aperçus réels. Les boutons de validation rapportent les réglages au lanceur ; ils ne réalisent pas les exports finaux Cut/GIF/Crop/Join/Burn/PDF.

**Remove Object : Apply conserve le comportement réel**, avec inférence et création d'un PNG. Si un modèle manque, son téléchargement est proposé ; annuler la proposition si l'essai doit rester sans réseau. Cancel suffit pour la recette du pinceau. **Image to PDF : Print ouvre le dialogue d'impression réel** ; annuler ce dialogue pour ne rien imprimer.

## Premier passage : design à 100 %

### Reprise après le correctif de fermeture du 2 octobre

Le blocage signalé sur Create GIF/Crop Video est corrigé dans le testeur reconstruit. Fermer l'ancienne instance puis relancer `TEST_PHASE_E.cmd`. À 100 %, vérifier d'abord ces deux fenêtres :

1. Ouvrir, attendre l'aperçu, puis **Cancel**. Le lanceur doit redevenir utilisable immédiatement.
2. Réouvrir et fermer avec **Échap**, puis refaire avec la **croix**.
3. Cocher le retard de trois secondes dans le lanceur, ouvrir puis fermer immédiatement. La fermeture peut attendre l'arrêt du décodage, mais elle doit aboutir ; aucune image tardive ne doit réapparaître.
4. Pour GIF, fermer aussi pendant **Preview GIF**. Pour Crop, fermer après avoir changé de frame et le rectangle de sélection.
5. Réouvrir et valider avec la commande principale : les réglages doivent être rapportés au lanceur, qui doit permettre une nouvelle ouverture.

Les cinq autres éditeurs ont reçu la correction du même mécanisme de fermeture. Le rendu des sept fenêtres est désormais validé aux quatre échelles ; la vérification spécifique des routes de fermeture reste à confirmer séparément.

**Image to PDF — ajustements du 2 octobre :** contrôler que « Remove selected » et les autres boutons d'outils ont les mêmes dimensions, et que les trois champs du bloc Page partagent exactement leurs bords gauche et droit, y compris après redimensionnement.

Pour chacune des sept fenêtres :

1. Vérifier bandeau, titres, textes longs, marges et écarts entre sections.
2. Vérifier les commandes du bas : tailles identiques, accessibles par défaut et après réduction.
3. Réduire puis agrandir la fenêtre. Les options peuvent défiler ; les éditeurs avec rail latéral le replient sous l'aperçu lorsque la largeur est insuffisante.
4. Parcourir Tab/Maj+Tab, listes et radios avec les flèches. Tester Échap et la croix. Entrée valide lorsque la commande principale est disponible.
5. Vérifier que la taille par défaut n'impose pas de défilement inutile sur votre écran et qu'il n'y a pas d'espace vide disproportionné entre les options et les commandes. Signaler les écarts avant de poursuivre aux échelles élevées.

| Fenêtre | Manipulations ciblées |
|---|---|
| **Cut Audio** | Attendre la waveform. Déplacer les deux bornes, puis saisir un début et une fin précis. Redimensionner : les temps restent identiques et les poignées correspondent toujours à la waveform. Essayer Play/Stop, Remove selection et Silence selection ; vérifier la nouvelle durée/waveform. Cut rapporte la sélection au lanceur. Fermer une nouvelle ouverture pendant la préparation : aucun lecteur tardif ni fermeture bloquée. |
| **Create GIF** | La sélection temporelle reste sous l'aperçu. Déplacer les bornes, contrôler les temps/durée et changer résolution, FPS et qualité. Preview GIF puis Stop : retour à l'image de sélection, commandes accessibles. Redimensionner pendant la sélection : temps inchangés. Cocher dans le lanceur le retard de trois secondes, ouvrir puis fermer immédiatement ; refaire pendant Preview GIF. |
| **Crop Video** | Parcourir la vidéo par clic puis déplacement du curseur natif. Déplacer/redimensionner le rectangle, tester les ratios, Auto crop, Fit et Reset. Faire varier le zoom et la fenêtre : dimensions source du crop conservées, poignées et zones de clic alignées. Changer de frame après un crop : il doit être conservé. Refaire sur une vidéo portrait. Tester aussi le retard de trois secondes et la fermeture immédiate. |
| **Join Videos** | Charger deux fichiers, puis ajouter à nouveau le premier : les occurrences restent distinctes. Choisir chaque ordre initial, déplacer les clips, utiliser Ctrl+flèches et Delete, Remove et Clear all. Les vignettes restent proportionnelles aux durées. Join rapporte l'ordre final au lanceur. Fermer pendant le chargement de plusieurs clips ; aucun aperçu tardif. |
| **Burn Subtitles** | Ouvrir un SRT, changer preset/police/taille/marge/couleurs, parcourir le temps puis Preview motion/Stop. L'aperçu reflète les réglages ; les champs restent compacts. Refaire avec un ASS : style externe conservé, réglages correspondants indisponibles. Tester une source portrait et des sous-titres longs. Fermer pendant le rendu de l'image puis de l'animation. |
| **Remove Object** | Attendre l'image. Peindre, gommer, changer le diamètre, zoomer/déplacer l'image, utiliser Fit et Reset mask. Le masque reste attaché aux mêmes pixels source après zoom et changement de taille. Les états Brush/Eraser restent cohérents. Cancel puis réouvrir : aucune image/masque de l'essai précédent. Apply et annulation de l'inférence se vérifient séparément avec un modèle déjà installé si disponible. |
| **Image to PDF** | Ajouter PNG/JPEG/WebP, sélectionner puis déplacer/redimensionner/faire pivoter une image. Tester crop, Ratio, Snap, règles et pouces, ordre avant/arrière, formats portrait/paysage et dimensions personnalisées. Ctrl+Z/Ctrl+Y rétablissent aussi crop/rotation/ordre. Zoom/Fit view et redimensionnement ne changent pas les dimensions de la page ou des images. Export rapporte les réglages ; Print doit rester accessible. Fermer pendant l'ajout de plusieurs grandes images, puis réouvrir. |

## Après acceptation du design : DPI

Refaire les contrôles aux paliers Windows **150/200/300 %**, avec fermeture/réouverture, maximisation/restauration et largeur réduite. Contrôler particulièrement la waveform, les bornes GIF, les poignées crop/rotation, la timeline et le pinceau : le dessin et les zones de clic doivent correspondre.

Si plusieurs écrans sont disponibles, déplacer chaque éditeur aller-retour entre écrans de DPI différents, puis contrôler les mêmes valeurs source. Tester séparément la taille du texte Windows et les thèmes utilisés. Les réglages Windows sont modifiés par l'utilisateur ; aucune capture ni prise de contrôle du bureau n'est nécessaire.

Un retour écrit suffit : **E à 100 % OK**, puis **E à 150/200/300 % OK**, avec les scénarios non réalisés (multi-écran, modèle IA, impression…).

## Exports réels et clôture

Le lanceur qualifie l'interface et les aperçus, pas les exports finaux. Pour comparer les résultats produits, utiliser ensuite le build de développement de FrameShift avec les mêmes fichiers de test. Pour chaque action applicable :

- effectuer une sortie avec espaces/accents et relancer pour vérifier le nom unique, à côté du média ;
- contrôler durée, dimensions, ordre, sous-titres, masque ou mise en page attendus ;
- annuler une opération puis fermer, vérifier le nettoyage des temporaires et l'absence de processus FFmpeg restant ;
- vérifier qu'aucune console ne s'affiche et que les erreurs/logs sont lisibles.

Une validation de la distribution installée ou de l'installateur relève de la qualification finale G et de la chaîne canonique du projet. Le rendu DPI réel de E est validé par le retour utilisateur du 2 octobre. Les contrôles cachés automatisés et ce retour visuel ne remplacent pas les preuves des contrôles complémentaires nécessaires à la clôture complète de la recette.

## Vérifications par commandes

```powershell
dotnet build src/FrameShift/FrameShift.csproj --no-restore
dotnet build tests/FrameShift.UiSamples/FrameShift.UiSamples.csproj --no-restore
dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj --no-restore --filter FullyQualifiedName~UiEditorTests -- xUnit.MaxParallelThreads=1
```

Les tests construisent uniquement des contrôles natifs cachés. Ils vérifient le footer, les conversions géométriques/temporelles, les retours tardifs d'aperçus, l'historique PDF et les chargements réels de petits médias générés par les runners. La fermeture utilise aussi le contrôle de fin de dialogue WinForms et `Close()`, avec conservation de OK/Cancel et absence de réentrée. Les résultats et limites sont consignés dans l'audit.
