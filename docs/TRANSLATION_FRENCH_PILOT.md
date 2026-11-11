# Pilote français — parcours de contribution

Date : 11 novembre 2026. Source fournie : commit `e57903b`.

Mise à jour après le pilote : les cinq descriptions françaises du catalogue source
ont été corrigées en anglais. Les autres versions existantes, dont le français,
étaient correctes. Les cinq descriptions ont été ajoutées aux brouillons japonais
et indonésien. Utiliser désormais le [CSV français 002](../translations/contributions/fr/002/translation.csv)
et les [dossiers actuels](../translations/README.md#current-contribution-folders).
Les fichiers et rapports `001` / `001-returned` ci-dessous restent des preuves
historiques ; ils ne doivent pas être réimportés avec leurs anciennes sources.

La contribution française comporte **2 167 traductions renseignées**, soit
2 024 ressources d’application/worker et 143 messages personnalisés d’installeur.
Elle a été importée dans le brouillon français uniquement. La langue reste
inactive et l’installeur existant est inchangé.

## Ce qui a été essayé

Le parcours a commencé par le dossier fourni à un traducteur : lecture de
`START_HERE.txt`, `READY.txt` et du glossaire, copie du dossier `001`, édition de
la seule colonne `Translation`, conservation de toutes les références et remise
d’un nouveau CSV avec le formulaire de retour rempli. Aucune modification de
code C#, de ressource distribuée ou de configuration d’installeur n’a servi à
rédiger la contribution.

La prise de contrôle du PC ayant été exclue par l’utilisateur, l’édition et les
réouvertures du CSV ont été assistées par des outils de traitement de texte/CSV.
**Ce n’est pas un essai réel dans Excel ou LibreOffice ni un test d’utilisabilité
avec une personne extérieure non développeuse.** Il ne faut pas déduire ces
validations des résultats techniques ci-dessous.

Fichiers à consulter :

- [CSV français remis](../translations/contributions/fr/001-returned/translation.fr.csv).
- [Retour du contributeur](../translations/contributions/fr/001-returned/FEEDBACK.txt).
- [Choix terminologiques français](../translations/contributions/fr/001-returned/GLOSSARY.fr.md).
- [Rapport technique final](../translations/contributions/fr/001-returned/technical-check.json).
- [Brouillon français](../translations/drafts/fr/Strings.fr.resx).

La copie `translation.csv` du dossier retourné reste vide : c’est le fichier
initial conservé, pas la contribution à importer. Le fichier remis est
`translation.fr.csv`. Le `snapshot.json` fourni n’a pas été réécrit et reste un
état de fourniture, sans valeur d’approbation linguistique.

## Contrôles au fil du travail

| Contrôle intermédiaire | Lignes renseignées | Lignes vides | Erreurs techniques |
| --- | ---: | ---: | ---: |
| 1 | 813 | 1 354 | 0 |
| 2 | 1 053 | 1 114 | 0 |
| 3 | 1 304 | 863 | 0 |
| 4 | 1 567 | 600 | 0 |
| 5 | 1 832 | 335 | 0 |
| 6 | 2 167 | 0 | 0 |

À chaque étape, les huit colonnes autres que `Translation`, l’ordre et le nombre
de lignes ont été comparés au CSV initial. Le contrôle du projet a vérifié les
sources, empreintes, références et variables. Les contrôles partiels tolèrent les
cases vides ; le contrôle strict final passe sans erreur ni avertissement.

La relecture a distingué les mots identiques de sens différent : thème « Clair »
et intensité « Léger », limite « Début » et bouton « Démarrer ». Les 60 valeurs
identiques à la source ont été relues ; elles comprennent des mots communs aux
deux langues, unités, formats et descriptions déjà françaises. Une recherche des
restes anglais n’a relevé qu’un identifiant CLI attendu parmi les mots recherchés.
Ce sondage ne constitue pas une preuve automatique de qualité linguistique.

La réception a été jouée séparément, dans le rôle du mainteneur :

1. Contrôle strict et aperçu : 2 167 changements dans deux fichiers de brouillon.
2. Sur des copies jetables, suppression d’une variable et modification du texte
   source : les deux contributions sont refusées, sans import.
3. Import explicite avec sauvegarde préalable des deux fichiers et manifeste
   SHA-256 dans `scratch/fr-translation/backup-fr-001` ; sauvegardes comparées.
4. Réexport : les 2 167 traductions sont exactement identiques au CSV remis.
   Seule l’empreinte de traduction initiale reflète désormais le nouveau brouillon.
5. Les 51 correspondances de libellés Explorer concordent entre les deux scopes.
   Un second aperçu d’import propose zéro changement.
6. Comparaison au snapshot initial : 2 167 traductions changées, aucun changement
   de source/contexte ni traduction vide. Espaces de début/fin et nombres de
   retours à la ligne sont conservés dans toutes les cellules traduites.

Les ressources distribuées, fichiers de l’installeur, script de génération,
dossiers japonais/indonésiens et dossier français initial `001` ont été vérifiés
inchangés. Aucun test de l’interface ou du cycle d’installation n’a été exécuté.

## Le parcours est-il complet pour un non-développeur ?

**La contribution sans coder est possible avec un mainteneur. Le parcours ne peut
pas encore être déclaré entièrement validé pour un non-développeur autonome.**

Les points solides sont le dossier indépendant du code, la colonne de traduction
unique, les consignes de variables, le glossaire, les contributions partielles,
les protections d’import, les sauvegardes et la séparation nette entre un
brouillon et une langue activée. Le traducteur n’a besoin ni de C#, ni de Git, ni
de .NET. Le contrôle technique et l’intégration restent des tâches du mainteneur,
comme prévu par cette architecture.

Les limites observées sont précises :

- **Contexte insuffisant** : 2 018 ressources sur 2 024 ont seulement un contexte
  automatique. Les noms de classes ou clés hachées ne disent pas toujours où le
  texte s’affiche, ce que désigne une variable ou quelle longueur est acceptable.
  Exemples : `Light`, `Start`, messages de recherche de boucle et fragments BRIA.
- **Source anglaise imparfaite, corrigée après le pilote** : cinq descriptions d’action étaient déjà en
  français (`Agrandit…`, `Sépare…`, trois `Supprime…`). Elles devraient être
  clarifiées dans le catalogue source avant la traduction japonaise/indonésienne.
  Le pilote n’a pas modifié ce catalogue pendant la contribution ; la correction
  ultérieure conserve les clés et fournit de nouveaux exports et empreintes.
- **CSV fragile à manipuler** : neuf colonnes, valeurs techniques et cellules
  multilignes. Le contrôleur détecte des erreurs, mais ne protège pas les cellules
  pendant la saisie. L’import/export en texte dans un vrai tableur reste à tester.
- **Retour dépendant du mainteneur** : le traducteur ne dispose pas d’un contrôle
  convivial immédiat ni d’un aperçu des fenêtres dans sa langue. Il peut traduire
  sans développer, mais doit attendre un retour pour les erreurs et l’affichage.
- **Validation humaine ouverte** : une simulation par agent ne prouve ni la
  compréhension des consignes par une autre personne, ni l’absence de texte coupé,
  de collision de raccourcis ou d’erreur de terminologie dans les fenêtres.

La suite utile reste limitée : fournir du contexte et des captures pour les cas
ambigus, puis faire un vrai aller-retour
de tableur avec une personne non développeuse et une relecture visuelle. Un
contrôle local plus facile à lancer peut ensuite être envisagé si l’essai montre
que les allers-retours avec le mainteneur sont pénibles. Une refonte générale de
l’architecture n’est pas nécessaire sur la base de ce pilote.

La correction des cinq sources est désormais faite. Les autres améliorations ne
sont pas implémentées par ce compte rendu. Le français reste inactif et
l’installeur inchangé. La correction ultérieure se limite aux cinq descriptions
dans les brouillons japonais et indonésien ; le reste de ces traductions est en
attente.
