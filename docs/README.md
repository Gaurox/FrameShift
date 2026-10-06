# FrameShift Documentation

Documentation centrale du projet FrameShift.

## Développer une future fenêtre

Lire dans cet ordre : [PROJECT_RULES](PROJECT_RULES.md), [ARCHITECTURE_FREEZE](ARCHITECTURE_FREEZE.md), [UI_FOUNDATION](UI_FOUNDATION.md), [UI_STANDARDIZATION](UI_STANDARDIZATION.md), puis le [guide pratique de développement](UI_WINDOW_DEVELOPMENT_GUIDE.md).

- **Contrat technique et métriques :** `UI_FOUNDATION`, avec les constantes de `FrameShiftUiMetrics`.
- **Choix visuels et variantes validées :** `UI_STANDARDIZATION`.
- **Construction, exemples complets et checklist :** `UI_WINDOW_DEVELOPMENT_GUIDE`.
- **Preuves et historique :** audit du 28 septembre et recettes par phase ; ils ne prescrivent pas de recréer les anciens layouts.
- **Distribution installée :** `RELEASE_CHECKLIST` et recette G.

Les plans Main, Remove Object et Extract Frames conservent leur conception d'origine. Pour leurs interfaces, le standard UI courant prévaut sur les anciennes tailles, factories ou maquettes. La validation 1.20.0 ne dispense pas de vérifier une nouvelle fenêtre.

## Quick Links

- [Extract Text — OCR, PDF Options and Model Downloads](EXTRACT_TEXT.md)
- [Release Notes 1.21.1](RELEASE_NOTES_1.21.1.md)
- [Release Qualification 1.21.1](RELEASE_QUALIFICATION_1.21.1.md)
- [Release Notes 1.21.0](RELEASE_NOTES_1.21.0.md)
- [Release Qualification 1.21.0](RELEASE_QUALIFICATION_1.21.0.md)
- [Project Overview](PRODUCT_GUIDE.md)
- [Project Rules](PROJECT_RULES.md)
- [Architecture Freeze](ARCHITECTURE_FREEZE.md)
- [BiRefNet High Resolution Decision Note](BIREFNET_HIGH_RESOLUTION_DECISION.md)
- [Migration Plan](MIGRATION_PLAN.md)
- [Changelog](CHANGELOG.md)
- [Release Notes 1.20.1](RELEASE_NOTES_1.20.1.md)
- [Release Qualification 1.20.1](RELEASE_QUALIFICATION_1.20.1.md)
- [Release Notes 1.20.0](RELEASE_NOTES_1.20.0.md)
- [Release Qualification 1.20.0](RELEASE_QUALIFICATION_1.20.0.md)
- [UI Phase G — Installed Release Tests](UI_PHASE_G_MANUAL_TESTS.md)
- [Release Notes 1.19.0](RELEASE_NOTES_1.19.0.md)
- [Release Notes 1.18.1](RELEASE_NOTES_1.18.1.md)
- [Release Notes 1.18.0](RELEASE_NOTES_1.18.0.md)
- [Release Notes 1.17.0](RELEASE_NOTES_1.17.0.md)
- [Release Checklist and Versioning](RELEASE_CHECKLIST.md)
- [Code File Index](CODE_FILE_INDEX.md)
- [Extract Specific Frames Implementation Guide](EXTRACT_SPECIFIC_FRAMES_IMPLEMENTATION_GUIDE.md)
- [Dark / Light Theme Implementation](DARK_LIGHT_THEME_IMPLEMENTATION.md)
- [UI Foundation — Active Contract and Examples](UI_FOUNDATION.md)
- [UI Standardization — Visual and Layout Rules](UI_STANDARDIZATION.md)
- [UI Window Development — Recipes and Acceptance Checklist](UI_WINDOW_DEVELOPMENT_GUIDE.md)
- [UI DPI Audit — Historical Notes](UI_DPI_AUDIT.md)
- [UI Audit and Standardization Plan — 2026-09-28](UI_AUDIT_AND_STANDARDIZATION_PLAN_2026-09-28.md)
- [UI Phase C — Manual Pilot Tests](UI_PHASE_C_MANUAL_TESTS.md)
- [UI Phase D1 — Manual Tests](UI_PHASE_D1_MANUAL_TESTS.md)
- [UI Phase D2 — Manual Tests](UI_PHASE_D2_MANUAL_TESTS.md)
- [UI Phase D3 — Manual Tests](UI_PHASE_D3_MANUAL_TESTS.md)
- [UI Phase E — Manual Editor Tests](UI_PHASE_E_MANUAL_TESTS.md)
- [UI Phase F2 — Theme and State Tests](UI_PHASE_F2_MANUAL_TESTS.md)
- [UI Phase F3 — Keyboard and Focus Tests](UI_PHASE_F3_MANUAL_TESTS.md)
- [UI Phase F4 — Resources and Combined F Tests](UI_PHASE_F4_MANUAL_TESTS.md)
- [Add Subtitles to Video Notes](ADD_SUBTITLES_TO_VIDEO.md)
- [Join Videos Notes](JOIN_VIDEOS.md)
- [Create Subtitle File Notes](CREATE_SUBTITLES.md)
- [RIFE Interpolation Notes](RIFE_INTERPOLATION_NOTES.md)
- [Upscale Video Implementation](UPSCALE_VIDEO_PLAN.md)

## Scope

Cette documentation couvre la structure active du projet ainsi que les plans et audits historiques explicitement datés. Les documents historiques ne décrivent pas nécessairement le code actuel.

Exclusions volontaires :
- `references/`
- sorties `bin/`
- sorties `obj/`

Les éléments présents dans `references/` restent des sources de comparaison ou de récupération, jamais des dépendances actives.
