# Phase F4 — Ressources et bilan F

Le build de développement est prêt. Les tests automatiques cachés vérifient la libération des icônes, bitmaps et polices ; les compteurs GDI/USER restent stables après échauffement. Le rendu visible et la netteté à plusieurs DPI reposent sur la recette utilisateur, confirmée le 2 octobre 2026 : « ok c'est bon je valide tout. comitte ».

## Essais de ce soir, sans commande ni capture

1. Double-cliquer sur [TEST_PHASE_F3.cmd](../TEST_PHASE_F3.cmd), dans le dossier du projet. Ce lanceur couvre aussi les thèmes F2. Utiliser ce build, car l'application installée ne contient pas ces changements.
2. Suivre [les vérifications de couleurs F2](UI_PHASE_F2_MANUAL_TESTS.md#manipulations) et [le parcours clavier F3](UI_PHASE_F3_MANUAL_TESTS.md). Le mode F3 ajoute les clips test dans Join : pour l'état désactivé prévu par F2, retirer ces clips dans le testeur, puis rouvrir Join pour la recette clavier. Son bouton Join n'exporte pas de vidéo.
3. Pour F4, ouvrir puis fermer **Main / Files**, **Settings**, **Compress Audio**, **Progress — Erreur** et **Join Videos** une dizaine de fois au total. Vérifier qu'il n'y a ni blocage, ni fenêtre résiduelle, ni icône manquante ou corrompue. Réduire, agrandir et rouvrir quelques fenêtres. La barre de titre et le bandeau doivent conserver leur icône ; les détails complets du bandeau doivent se rouvrir et se refermer normalement.
4. Garder Main, Compress Audio et Progress ouverts. Passer **Clair → Sombre → Clair** avec les boutons du lanceur. Les icônes doivent rester présentes et correctement dessinées ; les commandes et les textes gardent leur rendu accepté. Utiliser ces boutons temporaires pour le thème : le vrai sélecteur de Settings sauvegarde la préférence.
5. Refaire le noyau aux échelles **100 / 150 / 200 / 300 %**, en fermant puis relançant le testeur après chaque changement Windows. Si deux écrans sont disponibles, déplacer une fenêtre entre eux : bandeau et icônes suivent le DPI sans disparaître. Une icône ne doit pas devenir durablement floue après retour à l'échelle précédente.
6. Ouvrir [TEST_PHASE_D3.cmd](../TEST_PHASE_D3.cmd), puis **RIFE** ou **Upscale Image** et fermer sans traiter. Vérifier l'identité **FrameShift AI** dans la barre de titre et le bandeau. Il suffit d'une fenêtre IA pour ce contrôle ciblé ; aucun téléchargement ou aperçu Remove Noise n'est nécessaire. Les modes et échelles de ces pickers ont déjà leur validation D3.

Les compteurs GDI/USER ont été mesurés automatiquement dans un processus caché : aucun relevé du Gestionnaire des tâches n'est demandé. La revue exhaustive des 36 fenêtres, les exports et l'installateur appartiennent à G.

## Confirmation attendue

Indiquer simplement les thèmes et échelles testés, puis confirmer **couleurs/états F2**, **clavier/focus F3**, **réouvertures/icônes F4**. Signaler les étapes non exécutées, notamment le déplacement entre écrans si indisponible. Pour un défaut, préciser la fenêtre, l'échelle, le thème et la manipulation ; les captures restent facultatives.

**Statut au 2 octobre 2026 : F2/F3/F4 validées par l'utilisateur**, sur la recette du commit `4d3a07d`, build Debug `1.19.1`. Les contrôles de thèmes/états, clavier/focus et réouvertures/icônes sont acceptés à 100/150/200/300 %. F atteint ses critères de sortie sur le périmètre retenu. La confirmation est globale, sans relevé séparé de résolution/moniteurs/taille du texte ; elle ne certifie pas la matrice ni le binaire Release de G. G n'est pas lancée. Résultats et limites dans [l'audit](UI_AUDIT_AND_STANDARDIZATION_PLAN_2026-09-28.md).
