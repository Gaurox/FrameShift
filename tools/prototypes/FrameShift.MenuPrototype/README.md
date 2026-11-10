# Prototype isolé — menu FrameShift complet

Ce prototype C# / .NET 8 vérifie la construction d'un unique menu natif **FrameShift** avec **18 commandes vidéo classiques au même niveau**, dont Resize video, Remove audio et Rotate / Flip video. Les trois extractions spécifiques sont directement dans la liste. Les commandes du groupe séparé FrameShift AI ne font pas partie de ce snapshot.

Il ne modifie ni l'installation FrameShift, ni l'installateur, ni les associations de fichiers. Il n'écrit aucune clé de registre, ne s'enregistre pas comme extension Shell, n'ouvre aucune fenêtre et ne pilote pas le bureau. Aucun média, FFmpeg, FFprobe ou processus FrameShift n'est lancé.

## Principe

- `Shell/` : vraie DLL native x64 produite par Native AOT, avec `DllGetClassObject`, une factory COM et `IContextMenu` exposé par les générateurs COM de .NET 8.
- `Probe/` : hôte .NET 8 indépendant. Il charge cette DLL directement et appelle sa vtable COM, sans `CoCreateInstance` ni enregistrement Windows.
- `Shared/` : définitions ABI et appels Win32 de création, inspection et destruction des `HMENU`. Aucun appel d'affichage de menu n'est présent.
- `InvokeCommand` renvoie toujours `E_NOTIMPL`, y compris pour un verbe connu. Le banc vérifie les verbes canoniques ; il ne teste pas un lancement réel de l'action.
- Le contrat attendu du banc est défini séparément de la table de commandes de la DLL.

Le banc ne référence aucun projet applicatif FrameShift. Il ne participe ni au build produit ni à la publication ou à l'installation. Il ne faut pas copier cette DLL dans l'installation.

## Exécution hors ligne

Prérequis déjà présents dans l'environnement de validation : Windows x64, SDK .NET **8.0.420**, outils C++ de Visual Studio et Windows SDK pour l'édition de liens Native AOT. Les sources restent entièrement en C# ; aucun projet C++ n'est ajouté.

Le `global.json` de ce dossier est indépendant de celui du dépôt. Il utilise le SDK existant et les packages AOT **8.0.26** déjà en cache, pour permettre une compilation sans installation ni téléchargement. Cette chaîne expérimentale ne remplace pas le SDK 8.0.427 ni les exigences de servicing du produit. L'hôte a tourné sur le runtime installé .NET 8.0.27.

Depuis ce dossier :

```powershell
./run.ps1 -DotnetPath 'C:/Program Files/dotnet/dotnet.exe'
```

Le script utilise `NuGet.Config` sans source réseau et les lockfiles. Le cache existant est configurable via `-PackageCache`. Il borne ses options CLI à son processus et conserve les artefacts sous les dossiers ignorés `bin/` et `obj/`. Le rapport reçoit un nom unique ; un rapport existant n'est jamais écrasé. `-ReportPath` permet de choisir un nouveau chemin explicite.

Le linker Native AOT 8.0.26 peut émettre deux avertissements **LNK4104** : les exports COM ne sont pas marqués `PRIVATE` dans son fichier `.def` généré. Ils concernent la bibliothèque d'import générée, qui n'est pas utilisée ici ; les exports sont obtenus dynamiquement. Aucun avertissement n'est masqué. Le packaging de ces exports reste à traiter pour une éventuelle version produit.

## Vérifications

Le banc retourne un code non nul si une vérification échoue. Il contrôle :

- DLL réellement native x64, exports attendus et absence d'exports d'enregistrement ;
- factory COM, rejet d'un CLSID/interface inconnu, rejet de l'agrégation et identité `IUnknown` ;
- un seul groupe FrameShift, exactement 18 commandes natives activées, sans sous-menu interne ;
- titres, ordre, IDs distincts et verbes canoniques de toutes les commandes, notamment les 17e et 18e ;
- insertion entre des commandes de l'hôte sans les déplacer incorrectement ni les remplacer ;
- respect de `CMF_DEFAULTONLY`, des plages d'IDs et des buffers ANSI/Unicode ;
- rejet d'une plage trop petite sans créer un menu tronqué ;
- invocation toujours désactivée ;
- nettoyage après 100 insertions dans un parent invalide, 200 créations/destructions et 50 nouvelles instances COM.

Le rapport JSON contient la liste lue depuis les vrais `HMENU`, les résultats, l'environnement et le SHA-256 de la DLL testée. Les indicateurs de périmètre décrivent le comportement du programme ; ils ne constituent pas un audit système externe.

## Portée de la preuve

Un résultat positif prouve que la DLL C# compilée en natif expose un `IContextMenu` qui construit et restitue les 18 commandes au-delà de la 16e dans un véritable menu Win32, quand son hôte lui fournit assez d'identifiants. Le test vérifie l'état activé et la structure ; il n'affiche pas les pixels du menu.

Il **ne certifie pas** le rendu dans Explorer, sa plage d'identifiants effectivement allouée, le menu moderne Windows 11, l'activation enregistrée, la sélection de fichiers, les icônes, la traduction, les DPI, les clics réels ou les mises à jour/désinstallations. Le snapshot est volontairement en anglais et sans icônes. Le choix entre 18 verbes classiques et une entrée dynamique dans Explorer reste à valider séparément.

Ce n'est pas encore une extension Shell installable : il manque notamment l'initialisation de la sélection (`IShellExtInit`), le routage réel des actions, le déploiement et sa recette.

**Limite Native AOT importante :** le déchargement de la DLL n'est pas pris en charge. `DllCanUnloadNow` renvoie donc `S_FALSE` et l'hôte conserve la bibliothèque jusqu'à sa sortie. Une intégration dans Explorer devrait résoudre les conséquences sur les fichiers verrouillés et les mises à jour avant toute livraison.

## Sources techniques

- [IContextMenu::QueryContextMenu — plage d'identifiants et insertion](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-querycontextmenu)
- [COM généré dans .NET 8](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/comwrappers-source-generation)
- [Bibliothèques Native AOT — exports et absence de déchargement](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries)
