# Phase C — Recette manuelle des cinq pilotes

État au 29 septembre 2026 : **les cinq pilotes sont validés par l'utilisateur à 100 %**, y compris la dernière correction de hauteur Subtitles. La prochaine étape est la recette DPI ci-dessous. La validation de B ne valide pas automatiquement ces fenêtres métier.

## Ouvrir les bonnes fenêtres

1. Dans l'Explorateur, ouvrir `E:\AI\FrameShift_V1`.
2. Double-cliquer **TEST_PHASE_C.cmd**. Aucune commande à taper. Une console de lancement peut apparaître brièvement.
3. La fenêtre **Recette des cinq pilotes C** apparaît. La vidéo et l'image de `scratch\phase-a` sont présélectionnées si elles existent. Les boutons « Choisir… » permettent de prendre d'autres fichiers.
4. Ouvrir chaque pilote avec son bouton. Il s'agit de la véritable fenêtre du **build Debug de développement**. L'application installée conserve son ancienne UI.

Le lanceur ne produit pas de vidéo, image ou sous-titre et ne télécharge aucun modèle. Il lit les médias pour les aperçus et affiche les réglages après validation. **Interpolate Video (FFmpeg)** est le pilote C ; le dialogue IA **RIFE** sera migré ultérieurement.

## Premier passage — design à 100 %

Ce premier passage à **100 % est validé**. Pour tout rejeu ou essai DPI, fermer les anciennes fenêtres de recette puis rouvrir `TEST_PHASE_C.cmd` pour charger le nouveau build.

- Interpolate Video : champ FPS compact, trois boutons espacés et arrondis ; pas de défilement à l'ouverture si l'écran suffit.
- Compress Image : trois cartes de qualité sur une ligne ; format, cible et unité regroupés ; contenu complet à l'ouverture si l'écran suffit.
- Cut Video : champs début/fin et barre de frames **toujours sous l'aperçu**. Les champs passent sur deux rangées si la fenêtre devient étroite.
- Create Subtitles : listes verticales de boutons radio avec descriptions sous chaque choix, sans cartes ; hauteur adaptée aux options visibles. SRT et Project restent compacts ; ASS agrandit la fenêtre pour ses styles, dans les limites de l'écran, puis SRT la réduit à nouveau. Aucun espace réservé aux options masquées.
- Crop Image : disposition conservée, boutons harmonisés avec le standard partagé.

La validation utilisateur à 100 % est acquise pour les cinq pilotes, y compris la hauteur Subtitles. Les étapes de cette section restent disponibles pour un rejeu ; les essais DPI peuvent désormais être entrepris manuellement.

## Parcours commun

Pour chacune des cinq fenêtres :

1. Vérifier le titre, les labels, les champs et les boutons : aucun chevauchement ni texte coupé. Un nom de source long peut finir par « … » ; son texte complet doit être disponible au survol et avec « Copy details » au clic droit.
2. Réduire la fenêtre, la maximiser puis la restaurer, trois fois. **Annuler et valider restent accessibles**. Les options trop hautes défilent. Pour Crop Image, les options passent sous l'aperçu si la largeur manque, puis reviennent à droite. Pour Cut Video, elles restent sous l'aperçu.
3. Si une réduction de fenêtre fait apparaître le défilement, descendre jusqu'en bas puis remonter. La première et la dernière option restent accessibles. À taille initiale sur un écran assez grand, les dialogues compacts n'ont pas de barre de défilement.
4. Parcourir avec Tab / Maj+Tab. Utiliser les flèches pour les choix et Espace pour les cases. Vérifier que le focus est visible et qu'un champ saisi garde sa valeur après redimensionnement.
5. Effectuer les manipulations propres au pilote ci-dessous, puis valider et lire les réglages affichés dans le lanceur. Rouvrir et tester aussi **Échap / Cancel**.

## Manipulations par pilote

### Interpolate Video

- Choisir ×2, ×3 puis ×4 : le champ Custom FPS suit le FPS source multiplié.
- Saisir un FPS valide supérieur au FPS source (par exemple 60 pour la vidéo de test à 15 FPS).
- Valider avec Entrée : le lanceur rapporte ce FPS.

### Compress Image

- Passer entre High quality, Balanced et Small file : un seul profil est sélectionné.
- En JPG, cocher Target file size, saisir `1,5` et choisir MB.
- Passer à WEBP : la cible reste activée et la saisie reste `1,5 MB`.
- Passer à PNG : la cible est désactivée et décochée.
- Revenir à JPG, recocher la cible : les valeurs précédentes sont conservées.
- Avec Balanced et JPG, valider : le lanceur indique ce profil, `jpg` et une cible de **1572864 octets**.

### Cut Video

- Attendre l'aperçu puis changer les bornes avec les champs de frame, les flèches et les poignées de la barre.
- Pour la vidéo de test à 15 FPS : saisir début `00:00:01.000` et fin `00:00:04.000`, puis Tab. Les frames doivent être **16 et 60**.
- Redimensionner puis vérifier que ces valeurs et la durée restent cohérentes. Valider : le lanceur rapporte les mêmes bornes.
- Déplacer rapidement une borne plusieurs fois : l'aperçu final correspond à la dernière demande, sans blocage de l'interface.
- Dans le lanceur, cocher **retarder l'aperçu de 3 s**, rouvrir Cut puis fermer immédiatement avec Cancel, Échap ou la croix. La fermeture doit aboutir sans erreur ; rouvrir ensuite et vérifier que l'aperçu fonctionne. Décocher le retard après le test.

### Create Subtitles

- Choisir les trois modèles successivement : un seul choix actif.
- Faire **SRT → ASS → SRT**, trois fois. Les presets ASS apparaissent/disparaissent ; la hauteur s'ajuste au contenu sans dérive cumulative et la largeur est conservée. La fenêtre reste dans l'écran, avec défilement si nécessaire. Une fenêtre maximisée conserve son état.
- En ASS, choisir Word Highlight, revenir à SRT puis ASS : Word Highlight reste choisi.
- Tester aussi FrameShift Customization Project : les presets ASS sont masqués.
- Valider en ASS : le lanceur rapporte le modèle, le format et le preset retenus.

### Crop Image

- Vérifier l'apparition de l'image et ses dimensions source.
- Déplacer et redimensionner le cadre avec ses poignées, puis essayer Free, Square, 16:9 et 9:16.
- Zoomer à la molette puis faire glisser au bouton gauche une partie de l'image située **hors du cadre de recadrage** pour déplacer la vue ; revenir avec Fit. Le recadrage reste cohérent.
- Noter la taille Crop en pixels, redimensionner la fenêtre puis cliquer Fit : la taille du recadrage doit rester identique.
- Essayer Reset. Tester Auto crop avec une image comportant une bordure nette (sinon le message d'absence de bordure détectée est normal).
- Valider : les coordonnées et dimensions sont rapportées par le lanceur.
- Rouvrir avec une grande image puis fermer pendant le chargement : aucune erreur ni blocage de l'interface. Le décodage natif d'image déjà démarré est attendu jusqu'à sa libération ; le fil UI reste disponible.

## Écrans et taille du texte

**À faire après validation du design à 100 %.** Refaire le parcours à 150 %, 200 % et 300 %, à résolution physique constante, en fermant et rouvrant les pilotes à chaque palier. Les changements Windows restent manuels.

Puis :

- Si deux écrans à DPI différents sont disponibles, déplacer chaque pilote ouvert entre eux trois fois, maximiser/restaurer et vérifier texte, commandes, aperçus et valeurs conservées. Préciser si le test n'est pas possible.
- Augmenter manuellement la **taille du texte Windows** et refaire les vérifications de lisibilité et d'accès aux options sur les cinq pilotes.
- Revenir aux réglages Windows souhaités une fois terminé.

## Retour sans captures

Un retour écrit suffit. Pour le premier passage :

> Design à 100 % : Interpolate / Compress / Cut / Subtitles / Crop OK (ou détails à ajuster).

Après accord sur ce design et essais DPI :

> Les cinq pilotes sont OK à 100/150/200/300 %, options/clavier/redimensionnement OK, Cut lent et fermeture OK, Crop/valeurs pixels OK. Multi-écran : OK / non testé. Texte Windows agrandi : OK / non testé.

En cas de défaut, indiquer seulement **pilote + échelle + manipulation + résultat observé**. Les traitements média complets, la distribution installée et les fenêtres hors pilotes ne sont pas qualifiés par ce lanceur.

Le bilan de C et le GO/NO-GO seront complétés après ce retour. D/E attendent une validation explicite.
