# Pilote indonésien — contribution et contrôles

Le 11 novembre 2026, après le commit japonais `a585e4e`, la contribution
indonésienne a été complétée suivant le dossier `002`, sans contrôle du PC.
Les 2 167 lignes sont renseignées : 2 024 ressources application/worker et
143 messages personnalisés d’installeur. Les cinq descriptions fournies et les
huit colonnes de référence sont conservées exactement.

- [CSV remis](../translations/contributions/id/002-returned/translation.id.csv).
- [Retour du contributeur](../translations/contributions/id/002-returned/FEEDBACK.txt).
- [Terminologie](../translations/contributions/id/002-returned/GLOSSARY.id.md).
- [Contrôle strict](../translations/contributions/id/002-returned/technical-check.json).
- [Fourniture complète avant activation — 003](../translations/contributions/id/003/).

Les contrôles incrémentaux passent sans erreur : 245, 579, 828, 1 057, 1 298,
1 550, 1 801 puis 2 167 lignes remplies. Le contrôle final ne présente ni case
vide ni avertissement. Les 46 valeurs identiques à l’anglais correspondent à des
mots partagés, identifiants et formats attendus. Les espaces, sauts de ligne,
variables, raccourcis, six filtres de fichiers et noms techniques ont été revus.

L’import propose puis applique 2 162 changements dans deux fichiers de brouillon,
avec sauvegardes vérifiées. Le réexport restitue exactement les 2 167 valeurs ;
les 51 correspondances Explorer concordent. Le second import est vide. La revue
du snapshot constate uniquement 2 162 traductions changées. Une variable altérée
dans une copie jetable est correctement refusée. Les fichiers sans rapport ont
été comparés par SHA-256 avant l’intégration séparée.

Le parcours CSV est utilisable sans modifier du C# ou des ressources. L’essai
reste assisté par des outils texte/CSV : il ne remplace pas un essai observé avec
une personne non développeuse dans un tableur. Les contextes automatiques, les
fragments de phrases et la relecture linguistique restent des limites connues.
L’architecture ne nécessite pas de refonte supplémentaire pour ce lot.

À la demande du propriétaire, les trois langues sont ensuite intégrées au
candidat local 1.24.0. Leurs brouillons restent historiques ; la maintenance
utilise désormais les commandes normales sans `--draft`. La relecture native,
l’aller-retour dans un vrai tableur et l’acceptation visuelle restent à effectuer.
