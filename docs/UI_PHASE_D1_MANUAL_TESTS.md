# Phase D1 — recette des surfaces communes

Date : 29 septembre 2026. Base : `5b62534`, modifications D1 du dossier de développement.

Périmètre : Progress, Main avec sa file et ses actions, Settings, Media Info. Les standards validés en B/C restent la référence. D2/D3 attendent la validation de ce lot, conformément à l'audit.

**État de validation :** interfaces D1 validées par l'utilisateur à **100/150/200/300 %**, sur le rendu final du commit `c26a723`. Les essais multi-écran, texte Windows agrandi et relecture des pilotes C après D1 sont également confirmés par « je valide aussi ». **Recette D1 validée.** La procédure ci-dessous reste disponible pour les régressions futures.

## Ouvrir les fenêtres

Double-cliquer sur **`TEST_PHASE_D1.cmd` à la racine du projet**. Aucun texte à saisir dans un terminal. Ce lanceur ouvre les véritables formulaires du build de développement, avec des données fictives pour Progress et Media Info. Main intercepte les actions et affiche les fichiers concernés au lieu de lancer un traitement. Les fichiers factices de Main restent dans `scratch/phase-d1/fichiers fictifs`.

Settings garde ses fonctions réelles : un changement de thème ou de dossier des modèles est enregistré. Pour le premier passage, ouvrir/fermer suffit ; le test Browse peut être annulé sans changement. Donate ouvre un lien externe : il n'est pas nécessaire de l'activer pour cette recette.

Le lanceur ne change aucun réglage Windows, ne déplace pas les fenêtres, n'injecte pas de clics et ne capture pas l'écran.

## Premier passage à 100 % — design et ergonomie

### Progress

Présentation révisée après les retours utilisateur : **Activity**, puis la file, puis **Messages & details sur toute la largeur sous la file**, quelle que soit la largeur de fenêtre. Le pourcentage est plus visible, les états sont présentés avec des libellés lisibles et les lignes de fichiers partagent un fond uniforme avec une sélection neutre discrète. La fenêtre s'ouvre plus haute si l'écran le permet. Le texte intégral a sa propre zone de lecture native (minimum 110 unités logiques et six lignes selon la police ; panneau réduit de 60 % à 30 % du corps au profit de la file), avec retour à la ligne, scrollbar verticale et bouton **Copy details**.

1. **En cours** : vérifier le bandeau, le nom long, le statut, le pourcentage, le temps restant et la file de 80 éléments. Redimensionner la fenêtre ; Cancel all reste visible. La progression boucle volontairement pour laisser le temps de tester.
2. Défiler dans la file, retirer une ligne en attente avec ×, puis annuler la ligne active avec × : les autres lignes restent présentes. Cliquer **Cancel all**, puis **Close**. Refaire une ouverture et essayer Échap puis Entrée après l'annulation.
3. **Attente** : la barre est indéterminée ; Cancel all termine cette simulation et devient Close.
4. **Erreur longue** : vérifier que plusieurs lignes sont immédiatement lisibles dans Details, puis défiler jusqu'au dernier détail. **Copy details** copie le chemin et le message complet. Sélectionner un autre fichier dans la file affiche ses propres détails ; **Current task** revient au message courant. Les commandes restent visibles ; Close fonctionne.
5. **Terminé** : vérifier 100 %, les lignes terminées et Close. Fermer le bandeau de soutien avec **Close banner** : la file récupère la place. Le bandeau reste masqué pour la session, comme auparavant.
6. **En cours**, sélectionner une ligne en attente puis lire/sélectionner son texte : les progrès du fichier actif ne doivent pas remplacer le détail consulté. Revenir au suivi avec **Current task**. Réduire puis élargir la fenêtre : les détails restent sous la file, sur toute sa largeur, avec défilement du corps si l'espace manque. Vérifier le bandeau Activity, le pourcentage, le compteur de fichiers et le fond uniforme des lignes dans le thème utilisé.
7. Cliquer plusieurs lignes et les en-têtes File / Status : l'en-tête doit rester neutre, la sélection de ligne neutre discrète, sans alternance de fonds, et le fond de la colonne × doit suivre celui de sa ligne. Vérifier aussi ce rendu dans le thème sombre si utilisé.

### Main

1. **Main vide** : vérifier le bandeau, Browse et Settings / Close. Browse peut être annulé.
2. **Main avec fichiers** : vérifier les trois fichiers factices, Add / Clear, les filtres All / Video / Audio / Image et la recherche.
3. Sélectionner uniquement la vidéo dans la file, rechercher **Cut video**, activer l'action : le résumé doit citer uniquement cette vidéo. Aucun traitement n'est lancé.
4. Retirer des éléments avec × ou Suppr ; Clear ramène à l'état vide. Ajouter à nouveau des fichiers si souhaité : aucune source n'est modifiée par ce lanceur.
5. Réduire fortement la largeur puis l'agrandir : les panneaux se placent l'un sous l'autre, puis côte à côte. Tester aussi leur séparateur et le défilement des actions.
6. Parcourir les filtres et les actions avec Tab / Maj+Tab, activer par Espace. Le focus doit rester identifiable.

### Settings

Vérifier que toutes les options apparaissent à l'ouverture si l'espace disponible le permet. Le chemin des modèles revient à la ligne. Browse, Reset to default et Open folder restent accessibles quand la fenêtre rétrécit. Ouvrir Browse puis annuler. Tester Light / Dark / System si souhaité, puis remettre sa préférence. Fermer par le bouton ou Échap.

### Media Info

Vérifier le bandeau et le texte monospacé. Défiler verticalement et horizontalement pour lire les 80 lignes longues. Copy doit copier le texte intégral (vérifiable en le collant soi-même dans le Bloc-notes). Copy / Close ont la même taille. Fermer avec Close ou Échap.

## Après accord sur le rendu à 100 %

- Refaire les quatre fenêtres à **150, 200 et 300 %**, ouverture fraîche à chaque palier, puis redimensionnement / maximisation / restauration. Le défilement est accepté quand l'espace manque.
- Déplacer les fenêtres ouvertes entre deux écrans à DPI différents si disponibles. Les valeurs, la sélection et les commandes doivent rester accessibles.
- Refaire les vérifications avec la taille du texte Windows agrandie.
- Vérifier les cinq pilotes C pour le bandeau, les sections et le footer, puisque D1 étend deux helpers partagés.
- Revenir ensuite aux réglages Windows souhaités.

Un retour écrit suffit : **Progress / Main / Settings / Media Info : OK à 100 %**, puis **100/150/200/300 %, clavier, redimensionnement, messages longs : OK ; multi-écran / texte agrandi : OK ou non testés**. En cas de défaut : fenêtre, échelle, manipulation et résultat observé.

Cette recette simule les états de Progress ; elle ne qualifie pas de nouveaux traitements média, téléchargements IA ou versions installées. Les runners et la logique Core n'ont pas changé dans D1.

## Vérifications automatiques du lot

Build de l'application et de `tests/FrameShift.UiSamples`. Contrôles natifs cachés uniquement pour les tests UI, sans `Show` ni `ShowDialog`. Le mode `--check` du lanceur enregistre le DPI natif réellement observé ; il ne simule pas des paliers Windows.

Pour reproduire la suite compatible avec cette contrainte depuis le dossier du projet :

```powershell
dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj --no-restore --filter 'FullyQualifiedName!~ExplorerArrivals_BeforeAndAfterHandle&FullyQualifiedName!~AddVideosButton_AddsEveryPickerOccurrence&FullyQualifiedName!~ShownWindow_IsVisibleAndCanBeginClosing&FullyQualifiedName!~Closing_DoesNotBlockTheStaMessagePump&FullyQualifiedName!~RemoveNoisePickers_PreserveRequestedDialogResult' --logger 'trx;LogFileName=d1-final.trx' --results-directory scratch/phase-d1 -- xUnit.MaxParallelThreads=1
```

Les 12 cas qui affichent des fenêtres restent exclus ; les skips média/IA existants sont comptés séparément. L'exécution séquentielle évite de faire concourir les tests natifs UI qui partagent l'état du processus. Voir l'audit pour les résultats et les limites observées.
