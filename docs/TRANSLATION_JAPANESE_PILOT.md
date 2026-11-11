# Pilote japonais — contribution et contrôles

Date : 11 novembre 2026. Travail précédent committé dans `4ec9deb`.
Source : dossier japonais `002`, avec cinq descriptions déjà traduites.

Le japonais comporte désormais **2 167 traductions renseignées**, dont 2 024
ressources d’application/worker et 143 messages personnalisés d’installeur.
Les 2 162 traductions restantes ont été ajoutées au brouillon ; les cinq textes
fournis ont été conservés. La langue reste inactive.

## Parcours et livrables

Le travail suit le dossier de contribution : consignes et glossaire lus, copie
du dossier, édition de la seule colonne `Translation`, conservation des huit
autres colonnes et du snapshot, nouveau CSV et formulaire de retour rempli.
L’édition et les réouvertures sont assistées par des outils texte/CSV, sans
contrôle du PC. Ce n’est ni une session Excel/LibreOffice ni une évaluation
d’utilisabilité observée avec une personne non développeuse.

- [CSV japonais remis](../translations/contributions/ja/002-returned/translation.ja.csv).
- [Retour du contributeur](../translations/contributions/ja/002-returned/FEEDBACK.txt).
- [Terminologie japonaise](../translations/contributions/ja/002-returned/GLOSSARY.ja.md).
- [Contrôle technique final](../translations/contributions/ja/002-returned/technical-check.json).
- [Dossier actuel pour relecture — 003](../translations/contributions/ja/003/).

Le fichier `translation.csv` conservé dans le retour est celui de la fourniture
initiale à cinq textes. Le fichier complet remis est `translation.ja.csv`.
Les dossiers `001` et `002` sont conservés ; `003` contient l’état complet et un
rapport des changements. Aucun de ces snapshots ne certifie la qualité native.

## Contrôles pendant la traduction

| Lot | Lignes renseignées | Lignes vides | Erreurs |
| --- | ---: | ---: | ---: |
| 1 | 245 | 1 922 | 0 |
| 2 | 579 | 1 588 | 0 |
| 3 | 828 | 1 339 | 0 |
| 4 | 1 057 | 1 110 | 0 |
| 5 | 1 298 | 869 | 0 |
| 6 | 1 550 | 617 | 0 |
| 7 | 1 801 | 366 | 0 |
| 8 | 1 962 | 205 | 0 |
| 9 | 2 167 | 0 | 0 |

Les références, sources, empreintes et contextes ont été comparés à la fourniture
à chaque lot. Les cinq traductions existantes sont identiques. La relecture finale
vérifie variables, noms de produits/modèles, espaces de bord, retours à la ligne,
marqueurs de raccourcis et motifs des six filtres de fichiers. Les neuf valeurs
identiques à l’anglais sont attendues : OK, ch, formats techniques et noms de profils.

Le contrôle strict final et celui du dossier `003` passent sans erreur,
avertissement ni case vide. Un essai avec une variable transformée par une saisie
pleine chasse (`{0}` → `｛０｝`) dans une copie jetable est correctement refusé.

Réception : aperçu limité aux deux fichiers de brouillon, sauvegardes avec
manifeste SHA-256, import puis réexport. Les 2 167 valeurs réexportées correspondent
exactement au CSV remis. Les 51 correspondances Explorer concordent et le second
aperçu d’import est vide. Le comparatif avec `002` montre uniquement 2 162
traductions changées, sans changement de source ou de contexte.

Les ressources distribuées, tous les fichiers d’installeur et les fichiers de
traduction français/indonésiens et fournitures précédentes ont été vérifiés
inchangés. **85 tests standalone Release passent**, sans échec ni test ignoré.
Les journaux et sauvegardes restent dans `scratch/ja-translation`.

## Correctif préalable trouvé dans le parcours Git

Un contrôle du checkout filtré par Git sur Windows, avec `core.autocrlf=true`, a
reproduit **huit erreurs de source périmée** dans le CSV français `002`, pourtant
valide directement. Git convertissait les sauts de ligne internes des cellules
multilignes ; leur texte ne correspondait plus à la source et à ses empreintes.

Le correctif est limité à `.gitattributes` : `/translations/**/*.csv -text`.
Les CSV restent textuels et comparables, mais Git ne transforme plus leurs octets.
Après correction, le même checkout filtré passe le contrôle strict sans erreur.
Les valeurs de toutes les cellules des huit CSV historiques ont été comparées
entre les blobs committés et les fichiers de travail, sans différence. Le hash
Git filtré du nouveau CSV japonais est égal à son hash brut. Aucune référence de
contribution ni empreinte de cellule n’a été réécrite pour contourner le contrôle.

## Ce qui reste à valider

La contribution est techniquement complète. Il reste une relecture japonaise
indépendante, un aller-retour dans un vrai tableur et le contrôle des fenêtres :
polices et glyphes japonais, longueurs, DPI, navigation et raccourcis `(&F)`.
Les fragments de phrases BRIA et le terme de recherche de boucle « comparaison
de séries » (`比較系列`) doivent être revus en contexte. Les 2 018 contextes
automatiques du catalogue restent une limite du dossier de traduction.

Aucun autre défaut technique bloquant n’a été identifié dans les contrôles
exécutés. Les améliorations de contexte n’imposent pas une refonte du parcours.
L’activation, les textes standard Inno `.isl` et la génération d’installeur restent
des étapes distinctes. L’indonésien est toujours limité à ses cinq descriptions.
