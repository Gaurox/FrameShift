# Qualification de FrameShift 1.20.0

## Statut

Candidate autorisée le 2 octobre 2026 : préparer l'installateur et remettre le programme à l'utilisateur pour essais. Publication explicitement différée jusqu'à sa vérification et son feu vert.

Les validations A–F restent conservées dans [l'audit](UI_AUDIT_AND_STANDARDIZATION_PLAN_2026-09-28.md). Elles portent sur les builds de développement identifiés ; elles ne sont pas remplacées par une affirmation de recette de l'installation 1.20.0.

## Décision de préparation

- Version application : `1.20.0` ; versions assembly/fichier : `1.20.0.0`.
- `PerMonitorV2` activé pour Debug **et Release** via l'initialisation SDK, avant les fenêtres.
- Version indépendante du worker sous-titres conservée : `1.18.1` ; pas de modification de son moteur ou de ses dépendances.
- Unique chaîne de production : `build_installer.ps1`, sans filtre de tests, sans `-AllowDirty` et sans `-RunInstaller`.
- Tests WinForms exécutables sur un bureau Windows privé, sans `SwitchDesktop`, capture, modification des réglages ou interaction avec le bureau utilisateur. Cette isolation ne simule pas une recette réelle à plusieurs DPI.
- Aucun tag, push, upload ou release GitHub dans cette préparation.

## Identification et preuves automatiques

À compléter après le run canonique : commit source, SDK, date, code de sortie, résultats des tests Release, logs, manifeste du payload, versions, sommes SHA-256 de l'application et de l'installateur, vérification du mode DPI réellement reçu.

Les résultats locaux restent dans `scratch/release-1.20.0/` et ne sont pas distribués. L'installateur attendu est `installer/FrameShift_1.20.0_Setup.exe`.

## Vérifications installées attendues

Utiliser [la procédure G](UI_PHASE_G_MANUAL_TESTS.md). Les essais d'installation, de mise à jour 1.19.1, d'Explorer, de DPI réels et de sorties métier restent à confirmer par l'utilisateur sur la candidate identifiée. Aucun environnement non exécuté n'est déclaré validé.

**Critères de sortie G : non atteints à ce stade.** La préparation d'un installateur ne vaut pas approbation de publication.
