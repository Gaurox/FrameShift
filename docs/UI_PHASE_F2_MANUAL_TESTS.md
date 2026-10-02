# Phase F2 — Couleurs et états

## Lancement

Double-cliquer sur [TEST_PHASE_F2.cmd](../TEST_PHASE_F2.cmd) dans `E:\AI\FrameShift_V1`. Le testeur Debug doit être compilé. Utiliser ce build de développement pour vérifier F2 ; l'application installée ne contient pas ces changements.

Les boutons **Clair / Sombre / Système** ne sauvegardent aucune préférence et ne changent aucun réglage Windows. Ils rafraîchissent les fenêtres ouvertes du même processus. La fermeture du lanceur termine cette session de recette. Settings est le vrai formulaire : **utiliser les boutons du lanceur pour changer le thème**, car le sélecteur de Settings sauvegarde réellement la préférence si on le modifie.

Main contient trois fichiers fictifs, créés au clic dans `scratch/phase-f2/fichiers fictifs`. Ses actions sont interceptées. Compress Audio présente des métadonnées fictives et rapporte seulement un résultat de dialogue. Progress est entièrement simulé. Join s'ouvre vide, sans acquisition de fichiers par son bouton Add ; ne pas déposer de média dans cette recette de couleurs. Aucun traitement média n'est lancé à l'ouverture.

## Manipulations

Commencer à **100 %**, puis refaire ce petit ensemble à **150 / 200 / 300 %**. Aucun besoin de réexaminer les 36 fenêtres. L'utilisateur change lui-même l'échelle ; le lanceur ne la modifie pas. Refermer et relancer le testeur après chaque changement, puis reproduire les choix Clair/Sombre.

1. Ouvrir **Main / Files**, **Settings** et **Compress Audio**. Ces fenêtres sont non modales : le lanceur reste accessible pour changer de thème.
2. Dans le lanceur, choisir **Clair → Sombre → Clair** en gardant les fenêtres ouvertes. Vérifier les textes, les types Video/Audio/Image dans Files, la sélection de ligne et le bouton ×. Le fond de Files reste uniforme, avec une sélection discrète, sans bandes alternées ni en-tête bleu foncé.
3. Dans Compress Audio, vérifier **Cancel** et **Compress** au repos, au survol et pendant un appui bref. Le texte du bouton principal reste lisible. Appuyer sur Tab : le focus est perceptible. Le retour du pointeur hors du bouton remet son aspect de repos. Le bouton Compress de cette recette ferme simplement la fenêtre.
4. Cocher/décocher **Target file size** : le champ devient éditable/en lecture seule et le sélecteur d'unité devient actif/désactivé. Les trois choix qualité restent actifs ; leur sélection est exclusive et lisible. Décocher cette option avant de fermer avec Compress, ou saisir une valeur positive. Pour voir un bouton principal désactivé, utiliser Join vide : Join reste inactif et distinct des commandes actives.
5. Ouvrir **Progress — Erreur**, puis **Succès** et **Attente**. Passer d'un thème à l'autre sans fermer ces fenêtres. Les libellés Failed/Completed, le résumé et le contexte des détails changent ensemble. Les erreurs sont lisibles en rouge atténué en sombre et les succès en vert. Les messages restent aussi compréhensibles par leur texte.
6. Dans Progress, sélectionner une portion de message, changer le thème et vérifier qu'elle reste sélectionnée. Vérifier la sélection d'un fichier et la lecture du message complet sous la file. Le changement de couleur conserve tailles, marges et hauteur de lecture.
7. Dans **Join Videos**, vérifier les boutons actifs/désactivés et les textes dans les deux thèmes. Cette recette vide ne vérifie pas visuellement une vignette en erreur ou l'export ; les erreurs Join utilisent le même rôle sémantique testé que Progress. Pour revoir les outils actifs Brush/Eraser ou Crop PDF, utiliser ensuite le lanceur E avec vos fichiers test, sans lancer Apply/Print.
8. Fermer les fenêtres et le lanceur. Les boutons de thème F2 n'ont pas modifié la préférence persistante.

## Résultat attendu

Confirmer thème clair/sombre, repos/survol/appui/focus/désactivation, changement avec fenêtres ouvertes et échelles testées. Les captures sont facultatives. En cas de défaut, indiquer fenêtre, thème, échelle et état du contrôle.

**Statut au 2 octobre 2026 :** code et vérifications automatisées consignés dans [l'audit](UI_AUDIT_AND_STANDARDIZATION_PLAN_2026-09-28.md) ; recette visuelle F2 en attente. Les validations C/D/E antérieures ne valident pas automatiquement cette nouvelle palette. F3/F4 et G restent distincts.
