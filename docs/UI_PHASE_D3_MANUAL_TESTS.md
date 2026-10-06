# Phase D3 — recette des fenêtres IA

Lot commencé le **1er octobre 2026**, après validation utilisateur de D2 à 100/150/200/300 % et commit documentaire `d8bd717`. Les huit fenêtres D3 utilisent le socle validé ; **rendu validé par l'utilisateur à 100/150/200/300 % le 1er octobre** (« je valide toutes les fenetres dans toutes les mises a l'échelle »). Les contrôles complémentaires ne sont pas confirmés séparément dans ce retour. Les essais automatisés cachés ne certifient pas les échelles Windows ; la validation de ces paliers vient de la recette utilisateur. Les manipulations ci-dessous restent disponibles pour compléter ou rejouer les vérifications.

## Ouvrir sans commande

Double-cliquer sur **`TEST_PHASE_D3.cmd` à la racine du projet**. Le lanceur utilise le build de développement, avec les fichiers de `scratch/phase-a` proposés lorsqu'ils existent. Choisir une vidéo, un audio et une image via les boutons si nécessaire.

Les boutons de validation des six pickers affichent uniquement les réglages choisis dans le lanceur : aucun export final, aucune inférence RIFE/Upscale/Separate et aucun téléchargement déclenché par leur ouverture. Les informations des fichiers sont lues par le runner FFprobe du projet.

- **Aperçu Remove Noise :** désactivé par défaut dans le lanceur. Cocher « Activer l'aperçu audio réel » seulement si le modèle DeepFilterNet3 est déjà installé. Preview extrait huit secondes dans un temporaire, traite et lit le son localement. Tester aussi la fermeture pendant la préparation ; les temporaires sont nettoyés par le picker après la fin de l'opération. Le lanceur ne télécharge pas le modèle.
- **Separate Audio :** la case DirectML du lanceur simule la disponibilité pour vérifier le choix désactivé et le fallback. Elle ne détecte ni ne certifie le matériel.
- **Upscale :** la case « Simuler plusieurs fichiers » désactive la taille personnalisée. Les facteurs 2x/3x/4x restent proposés.
- **Download Model :** les trois scénarios sont explicitement simulés. Aucun réseau ni fichier modèle. Ils ouvrent le vrai formulaire avec une tâche injectée.
- **BRIA :** vérification simulée, aucun modèle requis ou modifié. Re-check passe au premier clic à « incorrect », puis au second à « valide ». Open BRIA page ouvre le navigateur sur la page officielle ; Open folder crée/ouvre un dossier de démonstration dans les temporaires, pas le dossier réel de modèles.

Aucune capture ou prise de contrôle du PC n'est nécessaire. Les réglages Windows et les interactions sont effectués par l'utilisateur.

## Premier passage à 100 %

Pour chaque fenêtre : vérifier bandeau, marges, écarts, textes longs et boutons de même taille. Les fenêtres compactes affichent leurs options complètes par défaut si l'écran le permet. Réduire puis agrandir : le corps peut défiler, les commandes restent accessibles. Parcourir Tab/Maj+Tab, utiliser les flèches des radios/menus natifs, Entrée et Échap.

| Fenêtre | Manipulations |
|---|---|
| Remove Noise Audio / Video | Maximum sélectionné par défaut. Choisir Light/Normal/Strong/Maximum par clic et clavier : un seul actif. Tester Stereo sur une source stéréo puis mono ; la case est indisponible sur mono. Denoise affiche la force et le choix stéréo. Si le modèle existe, activer l'aperçu dans le lanceur, essayer Preview puis la fermeture pendant la préparation ; aucun son tardif. |
| Separate Audio | Décocher tous les stems : Separate doit devenir indisponible. Cocher Vocals et Instrumental seulement ; vérifier les options affichées. Tester Automatic/GPU/CPU et les descriptions. Refaire avec la simulation DirectML décochée : GPU indisponible, Automatic utilisable. |
| RIFE | Parcourir les deux modèles, cibles 2/4 et modes Normal/Slow x2/Slow x4. FPS source et sortie restent des champs compacts. Vérifier `FPS sortie = FPS source × cible / diviseur`. En Normal, Remove audio indisponible et non coché ; Keep pitch coché mais indisponible. En Slow, cocher Remove audio désactive Keep pitch ; décocher permet de le modifier. Start affiche les réglages sans traiter la vidéo. |
| Upscale Image / Video | Parcourir chaque modèle : nom et description lisibles. Tester 2x/3x/4x, puis Custom size sur une source seule. Modifier largeur puis hauteur : ratio source conservé, champs compacts. Les résultats sont bornés entre x1 et x4. Refaire avec « plusieurs fichiers » : Custom indisponible. Les modèles image/vidéo restent des listes distinctes. |
| Download : succès | Cliquer Download, lire la progression simulée, attendre : fermeture avec résultat OK. Aucune tâche ne démarre à l'ouverture. |
| Download : erreur longue | Cliquer Download : l'erreur de vérification simulée apparaît après environ une seconde. Le détail complet occupe une vraie zone multiligne en pleine largeur sous l'activité. Lire le début, défiler jusqu'au détail 40, sélectionner/copier avec Ctrl+A/Ctrl+C. Close et Download restent atteignables. Relancer Download : le second essai simulé réussit. |
| Download : annulation | Cliquer Download puis Cancel : l'annulation est annoncée, la tâche termine son nettoyage, Close et Download redeviennent actifs. Refaire en fermant par la croix pendant l'activité : la fermeture attend la fin sans figer le PC. Refaire Échap. Aucun résultat tardif ne doit remplacer l'annulation. |
| BRIA | Lire le fichier attendu, le dossier long et les indications manuelles. Re-check absent → incorrect → valide ferme avec Proceed. Dans le scénario incorrect, Use anyway permet de continuer. Cancel/Échap annulent. Les liens sont séparés des commandes principales, sans dépassement en petite largeur. |

## Ensuite : échelles et contrôles complémentaires

Une fois le design à 100 % accepté, refaire à **150/200/300 %** avec fermeture/réouverture, maximisation/restauration, largeur réduite et accès au dernier contrôle. Changer d'écran à DPI différents si disponible ; tester séparément la taille du texte Windows. Revoir le clair/sombre si utilisé, le focus et les menus longs. Revenir ensuite aux réglages Windows souhaités.

Un retour écrit suffit : **D3 à 100 % OK**, puis **D3 à 150/200/300 % OK**, en précisant les scénarios non exécutés (aperçu avec modèle, multi-écran, texte agrandi…). La validation du simulateur Download/BRIA qualifie l'interface et les routes d'état ; elle ne certifie pas un vrai téléchargement, un checksum ou une inférence IA.

## Vérifications par commandes

```powershell
dotnet build src/FrameShift/FrameShift.csproj --no-restore
dotnet build tests/FrameShift.UiSamples/FrameShift.UiSamples.csproj --no-restore
dotnet test tests/FrameShift.Tests/FrameShift.Tests.csproj --no-restore --filter FullyQualifiedName~UiD3Tests -- xUnit.MaxParallelThreads=1
```

Les tests D3 créent des handles natifs cachés, sans saisie injectée sur le bureau. Ils vérifient variantes stéréo/mono, GPU disponible/indisponible, batch/single, géométrie, choix et valeurs métier, erreurs/retry/cancel, retours tardifs et durée de vie. Les fixtures injectées de téléchargement ne touchent aucun serveur ou modèle. La suite de non-régression conserve le filtre sans affichage de B/C/D1/D2 ; les 12 cas ouvrant des fenêtres sont exclus, distincts des skips média/IA. Voir les résultats dans l'audit.

Les exports réels, les téléchargements réels, la distribution installée et le mode DPI Release restent à qualifier avant publication. Le re-check BRIA conserve son callback synchrone de vérification ; la lecture d'un gros fichier peut encore prendre du temps. L'audit garde cette limite distincte du rendu UI.

## Régression Remove Noise — 7 octobre 2026

L'[audit élargi des fermetures modales](MODAL_CLOSING_AUDIT_2026-10-07.md) a ensuite identifié et corrigé trois cas supplémentaires : annulation immédiate du dialogue de téléchargement partagé, et seconde demande de fermeture pendant l'aperçu de Cut Video/Crop Image. La validation élargie compte 146 tests réussis.

Le retour sur la 1.21 signale un aperçu utilisable après installation manuelle des modèles, puis une fenêtre bloquée sur « Closing... » au clic sur Denoise. Les deux pickers diffèrent maintenant la fermeture finale après l'événement `FormClosing` annulé. Le parcours applicatif vérifie les modèles avant d'ouvrir les réglages.

Validation Debug : **57 tests ciblés réussis, aucun échec ni test ignoré**. Les huit routes de fermeture audio/vidéo échouaient avant le correctif avec `ShowDialog()` ; elles passent après. Quatre cas supplémentaires simulent un aperçu encore actif et vérifient l'annulation, l'attente et le résultat modal. Deux cas ouvrent le vrai dialogue de téléchargement avec un dossier de modèles temporaire vide et vérifient que son annulation arrête le préflight avant les réglages, sans téléchargement. La compilation Debug émet les avertissements ImageSharp de licence absente dans cet environnement.

Recette réelle à compléter sur un build corrigé, depuis le hub ou Explorer (le lanceur D3 ne teste pas le préflight applicatif) :

1. Choisir un dossier de modèles de test vide. Lancer Remove Noise audio puis vidéo ; vérifier que Download apparaît avant les réglages. Vérifier aussi Cancel à cette étape.
2. Télécharger les modèles depuis FrameShift, ouvrir les réglages et écouter Preview. Cliquer Denoise une fois l'aperçu terminé ; vérifier la fermeture et le démarrage du traitement complet.
3. Rejouer Denoise sans aperçu, puis Cancel et la croix. Fermer aussi pendant la préparation d'un aperçu ; vérifier le nettoyage des temporaires après la fin du travail et l'absence de processus FFmpeg orphelin.
4. Utiliser des chemins avec espaces et accents et relancer sur le même média ; vérifier les sorties uniques, la conservation de la source et les logs. Les modèles réels, l'export complet et un nouvel installateur n'ont pas été qualifiés par les tests ciblés ci-dessus.
