# Phase G — Essais de la candidate installée 1.20.0

## Objectif et méthode

Tester le programme distribué, puis décider de la publication. Les phases A–F sont déjà acceptées sur leurs builds de développement ; cette recette confirme le build Release installé et complète les scénarios restants.

Les captures sont facultatives. Pour chaque bloc, une confirmation textuelle suffit : version, résolution, échelle, taille du texte, thème, écran(s), scénario et résultat. Signaler « non disponible » ou « non testé » lorsque nécessaire. Voir [le relevé de candidate](RELEASE_QUALIFICATION_1.20.0.md) pour le fichier exact et son empreinte.

## 1. Installation et identité

1. Fermer FrameShift après la fin des traitements en cours.
2. Double-cliquer `installer/FrameShift_1.20.0_Setup.exe`. Installer par-dessus 1.19.1 pour le parcours de mise à jour, en conservant le dossier choisi et les réglages habituels.
3. Parcourir les pages, la sélection des composants et le choix du dossier de modèles : texte lisible, commandes accessibles, chemin valide.
4. Ouvrir FrameShift depuis son installation. Dans les propriétés de `FrameShift.exe`, vérifier la version fichier `1.20.0.0` ; comparer le chemin/version/SHA-256 au relevé si un doute existe.
5. Vérifier la conservation des réglages (thème, dossier des modèles), des modèles et des fichiers utilisateur. Lancer une action depuis le hub et une depuis Explorer : les deux doivent utiliser la nouvelle installation.

Installer/désinstaller les configurations suivantes dans une VM ou un environnement de recette distinct pour préserver l'installation de travail :

- installation neuve complète ; installation personnalisée avec seulement le composant fixe `core` ;
- sélections limitées à Remove Noise Video, Media Info, Create Subtitles audio puis vidéo : menus sélectionnés présents, autres absents, FFmpeg/FFprobe et worker toujours disponibles ;
- mise à jour d'une ancienne installation minimale et désélection d'anciens composants : runtimes à jour, menus retirés correctement ;
- désinstallation : runtimes et menus retirés ; dossier partagé des modèles et fichiers étrangers préservés, y compris un fichier étranger placé dans un sous-dossier de modèle FrameShift.

La désinstallation n'est pas nécessaire pour mettre à jour l'installation de travail.

## 2. Matrice UI

Pour les 36 fenêtres ci-dessous et leurs variantes, contrôler au minimum **100 / 150 / 200 / 300 %**. À chaque échelle, fermer puis rouvrir l'application ; examiner le contenu, réduire/agrandir et maximiser/restaurer, vérifier que les commandes restent accessibles et que le défilement sert uniquement au contenu qui manque de place.

| Famille | Fenêtres / variantes |
|---|---|
| Principales (4) | Main, Settings, Media Info, Progress |
| Conversions et options (15) | Conversion Picker (vidéo/audio/image/extraction), Compress Multiple, Compress Audio, Compress Video, Compress Image, Resize Image, Resize Video, Interpolate Video, Change Pitch, Change Speed (audio/vidéo), Rotate/Flip Image, Rotate/Flip Video, Convert to Icon, Add Subtitles Picker, Create GIF |
| Éditeurs (7) | Cut Video, Cut Audio, Crop Image, Crop Video, Image to PDF, Join Videos, Burn Subtitles Editor |
| IA (10) | Remove Noise Audio, Remove Noise Video, Separate Audio, RIFE Interpolation, Upscale Image, Upscale Video, Create Subtitles (audio/vidéo), Remove Object, Download Model, BRIA Notice |

Compléments de la matrice G :

- composants partagés à **125 / 175 / 250 %** (bandeau, champs, options, boutons, progression et rail d'éditeur) ; si un défaut apparaît, rejouer ses fenêtres consommatrices ;
- 1920 × 1080, 2560 × 1440, et 3840 × 2160 à 150/200/250/300 % lorsque ces écrans existent ; espace logique réduit proche de 1280 × 720, en tenant compte de la barre des tâches ;
- transitions répétées entre écrans 100 ↔ 200 % et 150 ↔ 300 % : ouvrir sur chacun, déplacer aller-retour, maximiser/restaurer ; pas de croissance/réduction cumulative ;
- texte Windows par défaut puis agrandi, clair/sombre/System, contrôle ciblé en contraste élevé pour relever les limites ;
- chemins/noms longs avec espaces et accents, détails complets consultables et copiables.

La résolution physique et l'échelle sont des informations distinctes. Un changement d'échelle sur un écran ne remplace pas un essai multi-écran.

## 3. Options, clavier et aperçus

- Alterner formats et cible des compressions ; cocher/décocher les options audio ; sélectionner les presets vitesse/interpolation/rotation ; vérifier alignement, exclusivité et états actifs.
- Passer SRT → ASS → projet → SRT ; essayer les presets ASS, modèles, facteurs et dimensions personnalisées ; changer les modes d'ajout de sous-titres et les états BRIA.
- Parcourir Tab/Maj+Tab, flèches/Espace et Entrée/Échap. Après un dialogue de fichier ou une erreur, le focus revient à un contrôle utile.
- Charger un média lent/invalide, changer rapidement frame/sélection, puis fermer pendant la préparation sur Create GIF, Crop Video et les autres éditeurs concernés. L'UI doit répondre et pouvoir rouvrir la fonction.
- Sur Cut/Crop, PDF, Remove Object et Join, essayer poignées/pinceau/rotation/ordre aux extrémités, sur médias portrait/paysage et vidéo avec métadonnée de rotation. Valeurs source conservées après redimensionnement/changement de DPI ; clics et dessin alignés.
- Vérifier les détails longs de Progress et Download Model : plusieurs lignes lisibles, copie complète, actions visibles. Tester longue file, retrait d'un fichier en attente et donation affichée/masquée.

## 4. Traitements réels

Utiliser des fichiers de test, par exemple les médias locaux de `scratch/phase-a/`, puis appliquer aux actions migrées pertinentes :

1. Faire un traitement court jusqu'au bout, ouvrir le résultat et contrôler durée, dimensions/recadrage, audio, pages PDF ou synchronisation des sous-titres selon l'action.
2. Relancer sur la même source : sortie adjacente avec suffixe unique, aucun fichier existant écrasé.
3. Annuler un traitement plus long, puis simuler une erreur avec un fichier invalide : fermeture possible, sortie partielle nettoyée, message/log compréhensible, aucun FFmpeg/FFprobe/worker orphelin ni console visible.
4. Réouvrir/fermer plusieurs fois les éditeurs, modifier la sélection du hub et les options : pas de blocage ni dégradation progressive.
5. Sur les actions IA, utiliser les modèles déjà présents ; vérifier fermeture/annulation pendant aperçu/inférence et, lorsqu'un téléchargement est effectivement nécessaire, son interruption propre. Ne pas assimiler un picker ouvert à une inférence testée.

## 5. Retour attendu

Exemple sans capture :

```text
1.20.0 installée | 2560 × 1440 | 100/150/200/300 % | texte par défaut
Clair/sombre/System | un écran | blocs 1–4 : OK
Multi-écran / 4K / installation core-only : non disponibles ou à compléter
Défaut éventuel : fonction, étapes, comportement, message exact
```

La publication demande l'acceptation de la candidate, un périmètre de matrice documenté et aucun défaut P1 restant sur les configurations retenues. Les scénarios non testés restent identifiés dans le rapport.
