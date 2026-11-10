# Menus Explorer complets — étude d'intégration à 1.23.0

Date : **10 novembre 2026**. Statut : **candidate commune construite et qualification automatisée terminée ; première candidate vidéo confirmée sur MP4 par le propriétaire, recette installée de la paire commune encore ouverte**.

### Extension demandée après le premier essai utilisateur

Le propriétaire confirme que la candidate vidéo affiche les 18 actions sur MP4.
Sa capture révèle que FrameShift et FrameShift AI sont séparés : le premier est
inséré par le handler natif, le second demeure un verbe statique. Le périmètre
vidéo décrit ci-dessous est celui de la première candidate ; la correction
suivante étend le même handler aux deux groupes pour toutes les familles.

- Un handler commun insère **FrameShift puis FrameShift AI** à deux positions
  consécutives à l'index donné par Explorer ; aucun groupe vide n'est créé.
- Vidéo : 18 classiques + 4 AI ; audio MP3 : 7 + 3 ; images : 8 + 8 ; PDF : AI
  OCR uniquement. Les sous-ensembles installés et les formats réellement pris en
  charge restent filtrés. Les variantes de fond utilisent les cinq modèles fixes.
- Migration de l'ancien `FrameShift.NativeVideo` vers `FrameShift.NativeMenus`
  et retrait des deux racines statiques pour chaque extension concernée. Nouveau
  CLSID `{F6740A5A-42BC-4716-9512-531F0B254862}`, métadonnées schéma 2 : l'ancien
  handler éventuellement encore chargé ignore ces nouvelles données.
- Le contrat Microsoft permet d'insérer plusieurs items au point fourni au
  handler ; il ne fixe pas la position globale par rapport aux autres logiciels.
  Voir [QueryContextMenu](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-querycontextmenu).
- Les 23 contrôles natifs de cette extension passent hors Explorer dans les cinq
  langues, avec tous les types et routages, les groupes seuls/vides et les bornes
  USER/GDI. La chaîne canonique réussit également : **1 443 tests réussis,
  0 échec, 18 skips conditionnels**, puis compilation Inno Setup. L'archive est
  `builds/release-1.23-private/20261110T182134Z-unified-menus/` ; identités et
  limites sont consignées dans la [qualification](RELEASE_QUALIFICATION_1.23.0.md).

La confirmation utilisateur qualifie l'affichage vidéo observé, pas les cycles de
mise à jour/désinstallation ni la nouvelle paire de menus. Ceux-ci restent à
valider avec la nouvelle candidate. L'agent ne lance aucun installateur et ne
prend aucun contrôle du bureau.

## Décision proposée

Intégrer le correctif de visibilité des commandes vidéo sur `codex/integrate-find-loop-1.23`, puis le qualifier dans une nouvelle candidate privée **1.23.0**. La version n'est pas publiée ni figée : ce travail peut rejoindre Find Loop sans changer de numéro de version.

L'intégration est techniquement crédible mais conditionnée à une vraie recette Explorer et à la gestion du cycle de vie de la DLL. Le prototype isolé ne suffit pas à autoriser une livraison.

L'étude initiale ne modifiait que la documentation. Le propriétaire a ensuite autorisé l'intégration au code et au packaging, sans modification de son installation ni prise de contrôle par l'agent. Les fichiers produit/installateur sont maintenant modifiés dans le dépôt ; l'agent n'exécute pas l'installateur et n'enregistre aucune association produit. L'essai de la première candidate est effectué séparément par le propriétaire. Le prototype reste sous `tools/prototypes/FrameShift.MenuPrototype` ; il n'est pas emballé.

## État vérifié

| Élément | État au moment de l'étude |
| --- | --- |
| Branche | `codex/integrate-find-loop-1.23`, HEAD `ff6ad5d` |
| Find Loop | Intégré et traduit, accessible depuis Cut Video ; moteur, timings, previews et exports présents |
| Candidate privée du 9 novembre | Construite à partir de `df0fae2`, non installée, non publiée ; 1 414 tests réussis, 18 skips conditionnels |
| Recette manuelle 1.23 | Explorer, installation/mise à jour/désinstallation, DPI et autres limites encore ouverts dans la qualification existante |
| Menu vidéo classique | 16 entrées principales enregistrées plus 3 enfants ; capture utilisateur arrêtée après Media Info |
| Prototype | DLL native x64 issue de C#/.NET 8 Native AOT, 18 commandes plates via COM/HMENU ; 14 vérifications réussies |
| Portée de cette preuve | Hôte isolé avec assez d'IDs ; activation enregistrée, sélection de fichiers, rendu Explorer et exécution réelle non testés |

Le snapshot privé du 9 novembre est conservé. Son résultat n'est pas transférable à une candidate qui ajoute une extension Shell.

La nouvelle candidate privée du 10 novembre est construite par la chaîne canonique : **1 437 tests réussis, 0 échec, 18 skips conditionnels**, puis **15 contrôles natifs réussis** et compilation Inno Setup. L'archive `builds/release-1.23-private/20261110T174906Z-menus/` conserve l'installateur, les hashes et les preuves de source/payload. La [qualification](RELEASE_QUALIFICATION_1.23.0.md) distingue ces validations de la recette Explorer et du cycle installation/mise à jour/désinstallation encore ouverts. L'installation utilisateur reste inchangée.

## Périmètre produit

- Un seul groupe **FrameShift** pour les vidéos, avec les commandes classiques au même niveau.
- Aplatir les trois choix d'extraction spécifiques : Extract first frame, Extract last frame, Extract keyframes.
- Rendre accessibles Remove audio, Resize video et Rotate / Flip video, aujourd'hui absents de la capture.
- Respecter les composants choisis dans Inno Setup et les cinq langues existantes.
- Conserver les menus image/audio et le groupe FrameShift AI dans leur mécanisme actuel pour cette première intégration.
- Cibler le menu classique. Le menu moderne Windows 11 est un sujet distinct.

Find Loop continue de s'ouvrir par **Cut Video**, avec le même identifiant `cut-video`. Aucun nouveau moteur vidéo ni action Find Loop séparée n'est nécessaire. Le downscale avancé envisagé initialement est un autre travail fonctionnel ; cette intégration restaure l'accès au Resize Video actuel.

## Structure proposée

Créer `src/FrameShift.ShellMenu/`, bibliothèque C# ciblant .NET 8 / win-x64, publiée comme DLL Native AOT. Elle ne référence pas l'assembly applicatif contenant WinForms, ONNX et les traitements multimédia.

La bibliothèque prend uniquement en charge :

1. factory COM et `IShellExtInit` pour recevoir la sélection ;
2. `IContextMenu` pour décrire et construire le menu ;
3. titres, icônes, IDs et état activé/désactivé ;
4. lancement du `FrameShift.exe` de cette installation à la demande de l'utilisateur.

L'ouverture d'un menu ne doit lancer ni FrameShift, ni FFprobe, ni un téléchargement ou un traitement média. Les erreurs COM doivent être contenues et les ressources natives libérées. L'enregistrement COM utilisera `ThreadingModel=Apartment`.

### Commandes et sélection

Utiliser une petite table explicite de clés de commandes, IDs d'actions et arguments fixes. La contrôler par tests contre le catalogue et les composants installateur, plutôt qu'introduire un registre générique de services.

- `Resize video` lance `--action resize-video` ; `Cut video` lance `--action cut-video`.
- Les quatre extractions lancent `extract-frames` avec `--frame-mode all|first|last|keyframes`.
- Les actions à un fichier sont présentes mais désactivées si plusieurs sources sont sélectionnées.
- Join Videos nécessite au moins deux fichiers et conserve le passage vers son éditeur existant. L'ordre reçu d'Explorer n'est pas présenté comme un ordre de montage garanti.
- Les actions de lot passent la sélection complète au flux existant, sans construire une multitude de traitements dans Explorer.
- Chemins espaces/accents, sélection mixte, éléments virtuels et tailles importantes de sélection ont des cas de test explicites. Ne pas traiter une image ou un dossier comme une vidéo.
- Tenir compte de la limite de ligne de commande Windows ; aucune sélection ne doit être tronquée silencieusement. Choisir une limite explicite ou un transport existant adapté avant de promettre de très grandes sélections.

Le chemin de l'exécutable doit venir de l'installation liée à la DLL. Ne pas utiliser `Environment.ProcessPath` pour le résoudre : à l'intérieur de l'extension il peut désigner Explorer ou un autre hôte Shell. Ne pas interpréter une chaîne arbitraire du registre comme commande ; utiliser les actions/arguments connus et des arguments distincts, sans `cmd.exe`.

### Métadonnées, langues et composants

Privilégier la réutilisation des descriptions déjà produites par l'installateur : choix des composants, valeurs `MUIVerb`, chemins d'icônes et correspondance avec les actions. Le natif lit les données de menu de son installation ; il n'embarque pas une copie indépendante de toutes les traductions du produit.

La solution retenue déplace les descriptions vidéo sous HKLM64 `Software\FrameShift\ShellMenus`. L'ancien parent statique disparaît des associations vidéo après préparation des descriptions et du CLSID. Aucun mécanisme de masquage `ProgrammaticAccessOnly` ou `LegacyDisable` n'est utilisé. Le synchroniseur lit les deux magasins, prévalide les accès en écriture avant de modifier les captions et conserve les descriptions des composants effectivement sélectionnés. La migration prévoit une restauration du menu statique en cas d'échec ; son comportement réel reste dans la recette installée.

Adapter `ExplorerMenuLanguage` et ses tests pour les descriptions effectivement consommées. Conserver son contrôle d'appartenance à l'installation, son préflight d'écriture, son comportement d'élévation et l'absence de recréation de composants désélectionnés.

Les libellés enfants actuels (First frame, Last frame, Keyframes) ont perdu leur contexte lorsqu'ils sont aplatis. Ajouter trois libellés complets dans les cinq langues, sans réécrire les 1 843 traductions de 1.22 ni les 163 clés Find Loop validées. Les tests doivent vérifier les nouvelles propriétés/chemins de libellés, pas seulement l'ancien sous-menu.

Enregistrer le handler uniquement pour les six extensions vidéo actuellement prises en charge, et uniquement quand une commande classique vidéo est sélectionnée, y compris Media Info si ce composant fournit la seule commande. Une installation core-only ne crée aucun menu spécifique. Ne pas enregistrer le handler globalement sur tous les fichiers.

## Point de décision : durée de vie Native AOT

Microsoft indique que le déchargement d'une DLL Native AOT n'est pas pris en charge. Le prototype respecte cela avec `DllCanUnloadNow = S_FALSE`.

Conséquences à résoudre avant la livraison :

- La DLL peut rester chargée dans Explorer **ou dans une autre application hôte du Shell** jusqu'à sa fermeture.
- Un simple `SHChangeNotify` ne décharge pas ce code et ne garantit pas le remplacement d'une factory déjà en cache.
- Un remplacement au même chemin peut échouer ou être reporté. Une désinstallation doit traiter les fichiers encore chargés.

Piste recommandée pour les candidates : installer la DLL sous `Shell/<identité-de-build>/FrameShift.ShellMenu.dll`, puis pointer l'enregistrement COM vers la nouvelle copie. Une simple sous-version `1.23.0` ne suffit pas : plusieurs candidates privées portent ce même numéro. Utiliser une identité incluant le hash de la DLL ou une autre identité immuable de build.

Ce stockage évite l'écrasement du binaire chargé ; il ne garantit pas son activation immédiate dans un processus déjà ouvert. Définir et tester le moment où un redémarrage de l'hôte/Windows devient nécessaire, et l'annoncer clairement. Aucun arrêt forcé d'Explorer ne doit être dissimulé dans un script de build ou une action ordinaire.

Examiner explicitement le comportement `CloseApplications=yes` déjà présent dans l'ISS : Inno Setup utilise Restart Manager ; en mode silencieux, il peut fermer et relancer automatiquement les applications détectées. Le choix de copie versionnée et de nettoyage doit être cohérent avec ce comportement.

La désinstallation doit retirer uniquement les associations/CLSID appartenant à FrameShift, puis supprimer ou reporter la suppression des DLL encore chargées. Ne pas supprimer récursivement des emplacements génériques. Tester également le retour à 1.22 : procédure désinstallation 1.23 puis réinstallation 1.22 au minimum ; ne pas annoncer le downgrade direct tant que les anciennes inscriptions COM ne sont pas correctement neutralisées.

## Build et installateur

La DLL expérimentale utilise SDK 8.0.420 et compilateur AOT 8.0.26 en cache. Elle **n'entre pas dans le payload 1.23**. La production suit le servicing existant : SDK **8.0.427**, outillage/runtime embarqué AOT aligné sur **8.0.33**, dépendances verrouillées et provenance vérifiable. Les packages officiels AOT 8.0.33 ont été restaurés dans le cache projet pour cette intégration, sans installer de SDK ni modifier l'application installée.

Étendre exclusivement `build_installer.ps1` :

1. vérifier les prérequis AOT/éditeur de liens déjà installés sur la machine de build ; ne rien installer automatiquement ;
2. restaurer la DLL et le banc natif avec des lockfiles, au même titre que les projets existants ;
3. conserver la suite Release complète de FrameShift/Find Loop ;
4. compiler/publier la DLL de production et l'application, puis exécuter le banc natif dans un processus indépendant ;
5. vérifier le PE x64 natif, les exports COM, les versions de l'outillage et un manifeste/hash du composant ; traiter les avertissements d'exports LNK4104 du prototype ;
6. copier seulement le composant produit et ses données nécessaires dans le payload, jamais le prototype, le banc, les SDK ou les PDB ;
7. étendre les contrôles de payload avant de compiler l'ISS depuis ce même payload.

Les vérifications actuelles de runtime applicatif/worker ne suffisent pas à identifier le runtime statiquement incorporé dans une DLL AOT. Ajouter sa preuve de build et ses dépendances au manifeste et aux notices nécessaires.

L'ISS doit gérer explicitement CLSID, associations, migration de l'ancien affichage vidéo, composants et désinstallation. Éviter deux copies du même groupe FrameShift. Ne pas prendre les binaires sous `tools/prototypes` ou `references` comme sources installateur.

## Lots d'implémentation et critères d'acceptation

| Lot | Travail | Preuve attendue |
| --- | --- | --- |
| A — compléter hors installation | Sélection synthétique, routage avec lanceur factice, métadonnées/langues, icônes et état multi-fichier | Banc indépendant : 18 commandes avec tous composants, sous-ensembles exacts sinon ; arguments corrects et aucune exécution pendant la consultation |
| B — préparer le déploiement | DLL AOT servie, copie versionnée, enregistrement/désenregistrement et gestion des fichiers chargés | Tests des données d'installation et d'ownership ; stratégie d'activation/mise à jour documentée |
| C — fabriquer une nouvelle candidate privée | Chaîne canonique complète, tests natifs bloquants et suite Release existante | Nouvel installateur daté, checksum et manifeste distincts du snapshot du 9 novembre ; aucune installation/publication implicite |
| D — qualifier Windows | Recette par l'utilisateur ou environnement Windows de test autorisé | Vrai menu complet, lancement des actions, upgrade/désinstallation et non-régression Find Loop |

Recette installée minimale :

- installation neuve, mise à jour depuis 1.22, puis remplacement d'une candidate 1.23 par une autre portant le même numéro ;
- core-only, uniquement Resize Video, uniquement Cut Video, uniquement Media Info et extraction seule ; composants retirés lors d'une mise à jour ;
- MP4/MKV/AVI/MOV/WebM/M4V ; aucun doublon ni effet parasite sur images, audio et FrameShift AI ;
- 18 commandes classiques visibles avec tous composants, dont les anciennes entrées tronquées ; aucun sous-menu d'extraction ;
- cinq langues, noms longs, icônes, clavier, thèmes et DPI ;
- une et plusieurs vidéos, sélection mixte et chemins espaces/accents ; Join Videos préserve la sélection et ses règles ;
- lancement de Resize Video et des quatre modes d'extraction ;
- **Cut Video depuis Explorer → Find Loop → aperçu → export**, avec annulation, nommage unique, queue et nettoyage ;
- mise à jour/désinstallation alors que la DLL est chargée ; vérification des associations résiduelles et du comportement après redémarrage ;
- procédure de retour à 1.22 et restauration du menu classique précédent.

Les 14 vérifications du prototype restent une preuve utile, mais aucune ligne de cette recette ne peut être marquée réussie sur cette seule base. Les skips conditionnels de la suite générale restent rapportés comme tels.

## Décision de livraison

**Oui pour intégrer le travail à la branche de développement 1.23.0.** La livraison du correctif avec Find Loop devient acceptable après réussite des lots A à D, en particulier le vrai menu Explorer et les mises à jour/désinstallations Native AOT.

Mettre à jour les notes et le changelog quand l'implémentation existe. Créer ensuite une nouvelle section de qualification avec son identité de source, ses résultats et ses limites, sans réécrire les preuves du 9 novembre. L'autorisation de publication reste une étape ultérieure.

Si le cycle de vie Native AOT ne passe pas la recette, ne pas présenter le prototype comme un correctif livré. Réexaminer alors ce seul composant d'intégration Windows ; une exception de langage éventuelle demanderait une décision explicite du propriétaire.

## Références

- [Plan de la branche 1.23](RELEASE_PLAN_1.22_1.23.md)
- [Qualification privée actuelle](RELEASE_QUALIFICATION_1.23.0.md)
- [Prototype isolé](../tools/prototypes/FrameShift.MenuPrototype/README.md)
- [Enregistrement des extensions Shell — Microsoft](https://learn.microsoft.com/en-us/windows/win32/shell/reg-shell-exts)
- [Verbes et contrôle de visibilité — Microsoft](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers)
- [Bibliothèques Native AOT — Microsoft](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries)
- [CloseApplications — Inno Setup](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm)
- [Section Files — Inno Setup](https://jrsoftware.org/ishelp/topic_filessection.htm)
