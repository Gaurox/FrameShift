# Phase F3 — Clavier et focus

## Lancement

Double-cliquer sur [TEST_PHASE_F3.cmd](../TEST_PHASE_F3.cmd) dans `E:\AI\FrameShift_V1`. Le testeur Debug est compilé. Ce mode réutilise le lanceur F2 ; l'application installée ne contient pas ces changements.

Les boutons **Clair / Sombre / Système** restent temporaires. Main utilise les fichiers fictifs de F2 et intercepte les actions. Compress Audio ferme seulement son dialogue. Progress est simulé. **Join Videos** crée, au clic, trois copies distinctement nommées de la vidéo de `scratch/phase-a` dans `scratch/phase-f3/clips clavier`, puis produit de vrais aperçus locaux. Son bouton Join ferme l'éditeur sans lancer l'export ; son tri est sauvegardé dans ce dossier test. Les copies existantes sont conservées. L'ouverture du lanceur ne prépare aucun média.

Cette recette peut se faire sans captures. Ouvrir une fenêtre avec le lanceur, puis utiliser le clavier pour les étapes suivantes. Commencer à **100 %**, puis vérifier le bandeau, les champs, la timeline et le footer à **150 / 200 / 300 %**, dans les deux thèmes. L'utilisateur change lui-même les réglages Windows et relance le testeur.

## 1. Bandeau et métadonnées

1. Ouvrir **Compress Audio**. Parcourir avec **Tab / Maj+Tab** : bandeau, options, statut s'il propose la sélection de texte, puis Cancel et Compress. Le focus est visible et le retour en arrière fonctionne.
2. Sur le bandeau, **Entrée** ouvre les détails complets, même si la source était raccourcie. Le texte se sélectionne et se copie avec **Ctrl+A, Ctrl+C** ; **Échap** ferme les détails. Le focus revient au bandeau. Cette action ne confirme pas la compression.
3. Refaire avec **Espace**. Sur le bandeau, **Ctrl+C** copie directement les métadonnées complètes. Les coller dans un éditeur texte pour vérifier accents et espaces. **Maj+F10** ouvre le menu avec View details et Copy details ; naviguer avec les flèches et valider avec Entrée. Échap ferme le menu.

## 2. Champs, choix et commandes

1. Dans **Compress Audio**, atteindre les qualités avec Tab. Les **flèches** changent le choix radio dans son groupe ; **Espace** sélectionne le choix. Un seul reste coché.
2. Atteindre **Target file size** et basculer avec Espace. Le champ et l'unité suivent leur état ; les contrôles désactivés ne bloquent pas le parcours. Décocher l'option avant de confirmer, ou saisir une valeur positive.
3. Sur un sélecteur, les flèches changent uniquement sa valeur. Sur un champ texte, les raccourcis d'édition restent normaux.
4. Sur Cancel, Espace ferme la fenêtre. La rouvrir : Échap annule ; sur Compress, Espace et Entrée confirment seulement les réglages de cette recette. Le bouton et le focus restent lisibles pendant l'appui.

## 3. Main, filtres et retour de dialogue

1. Ouvrir **Main / Files**. Avec Tab, atteindre **Search actions**, saisir `Compress`, puis effacer. Le focus et le curseur de saisie restent dans le champ pendant la reconstruction des actions.
2. Aller aux filtres **All / Video / Audio / Image** avec Tab. Espace active le filtre ; le nouveau bouton du même filtre garde le focus après reconstruction. Tab continue aux actions ; Maj+Tab revient aux filtres. Aucun filtre n'exécute une action.
3. Vérifier que les actions grisées sont sautées. Activer une action disponible avec Espace : le lanceur rapporte son nom, sans traitement média.
4. Dans Files, naviguer et sélectionner avec les flèches ; **Suppr** retire les fichiers sélectionnés de la file, sans effacer les fichiers du disque. Le bouton × a le sens Remove file, avec le nom du fichier.
5. Atteindre **Settings**, l'ouvrir avec Espace, fermer avec Échap. Le focus revient à Settings. Pour changer le thème, utiliser les boutons temporaires du lanceur : le vrai sélecteur de Settings sauvegarderait la préférence.

## 4. Join Videos

1. Ouvrir **Join Videos** et attendre les trois aperçus. Avec Tab, atteindre la timeline après ses commandes. Un clip sélectionné et un indicateur de focus sont visibles.
2. **Gauche / Droite** sélectionnent les clips sans changer l'ordre. **Début / Fin** sélectionnent le premier/dernier. Aux extrémités, les flèches restent sur le clip existant.
3. Sur le clip 02, **Ctrl+Droite**, puis **Ctrl+Gauche** : le clip se déplace d'une place et reste sélectionné ; le tri devient Custom. Le glisser-déposer reste utilisable lors d'un contrôle complémentaire à la souris.
4. Dans la timeline, **Entrée / Espace** conservent la sélection sans confirmer Join. **Suppr** retire uniquement le clip sélectionné et la sélection passe au voisin. Avec moins de deux clips, Join est désactivé ; aucune confirmation ne part.
5. Revenir au sélecteur de tri avec Maj+Tab. Ses flèches changent le tri ; le même clip reste sélectionné à sa nouvelle position. **Suppr / Ctrl+flèches dans ce sélecteur ne retirent ni ne déplacent un clip**.
6. Atteindre **Add videos**, ouvrir le sélecteur de fichiers, puis annuler avec Échap. Le focus revient à Add videos. Ajouter une des copies pour vérifier l'arrivée d'un clip ; les fichiers restent indépendants de la suppression dans la timeline.
7. Vérifier que le clip sélectionné reste atteignable en fenêtre réduite et que le footer reste accessible avec Tab. Échap ferme Join. Refaire une ouverture/fermeture pendant le chargement des aperçus pour vérifier le garde-fou de fermeture.

## 5. Progress et éditeurs

1. Ouvrir **Progress — Erreur**. Tab atteint la file, les commandes de détails, le texte intégral et Close. Les flèches changent le fichier sélectionné ; le message correspondant reste consultable. Dans le texte, **Ctrl+A / Ctrl+C** fonctionnent. Échap ferme en mode Close.
2. Ouvrir **Progress — Attente**. Échap/Cancel all demandent seulement l'annulation simulée ; une fermeture devient ensuite possible. Une commande désactivée n'est pas activée par Entrée.
3. Pour les canevas, le [lanceur E](../TEST_PHASE_E.cmd) permet de contrôler Crop Video et Image to PDF : parcourir les champs/commandes à Tab, utiliser les alternatives de position/taille dans Crop et les commandes natives de page/image dans PDF, puis annuler. Les aperçus/canevas ont des noms descriptifs. L'édition exhaustive d'un masque au clavier ou la navigation de tous les objets d'un canevas par lecteur d'écran n'est pas promise par F3. Dans Remove Object, Apply reste une vraie opération ; il n'est pas nécessaire à cette recette.

## Confirmation attendue

Indiquer : bandeau/lecture/copie, parcours Tab avant/arrière, choix et champs, filtres et retour de focus, Join et ses garde-fous, Progress, thèmes et échelles testés. Noter les étapes non exécutées. Une confirmation sans capture suffit.

**Statut au 2 octobre 2026 : validée par l'utilisateur**, après remise de la liste complète des tests F2/F3/F4 : « ok c'est bon je valide tout. comitte ». Le parcours clavier/focus, la consultation/copie des métadonnées, les filtres, Join et les contrôles ciblés Progress/éditeurs sont acceptés, dans les thèmes et aux paliers 100/150/200/300 % demandés. Référence de recette : commit `4d3a07d`, build Debug `1.19.1`. Les preuves automatiques et cette confirmation manuelle restent distinguées dans [l'audit](UI_AUDIT_AND_STANDARDIZATION_PLAN_2026-09-28.md). L'accessibilité exhaustive des canevas est hors périmètre F3 ; la distribution finale relève de G.
