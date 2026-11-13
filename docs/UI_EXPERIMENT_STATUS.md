# Interface de référence pour la suite du développement

Décision utilisateur du 13 novembre 2026 : **continuer le développement avec l'ancienne interface**. La refonte visuelle essayée dans ce chat est mise de côté, pas abandonnée ni supprimée. Ne pas la réappliquer ou la fusionner sans nouvelle demande de l'utilisateur.

## Travail conservé pour de futurs tests

- Branche : `codex/ui-modernisation`.
- Tag : `ui/shelved-modernisation-2026-11-13`.
- Commit de conservation : `a78790f` ; dernier essai de code : `0b7da6f`.
- Maquettes, captures, code pilote et historique complet restent sur cette branche.
- Son suivi détaillé est `docs/UI_REDESIGN_TRACKING.md`, consultable sans changer de branche avec :

```powershell
git show ui/shelved-modernisation-2026-11-13:docs/UI_REDESIGN_TRACKING.md
```

## Base courante

Branche active après remise de côté : `codex/integrate-find-loop-1.23`. Le code de l'interface et des traitements revient à `a4728f519c0788252b12f6c9d77bce0e3ae1e5f2`, avant l'expérimentation. Les nouveaux développements doivent conserver cette interface historique tant qu'aucune autre décision n'est prise.

Les fenêtres expérimentales déjà lancées ne changent pas d'apparence à chaud ; les fermer et utiliser les nouveaux lancements de la compilation historique. La version installée n'a pas été modifiée par ces essais.
