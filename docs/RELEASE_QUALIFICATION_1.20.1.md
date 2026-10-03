# Qualification de FrameShift 1.20.1

## Statut

Candidate de maintenance sécurité en préparation, **non publiée**. Le tag prévu est `1.20.1`. Les corrections des quatre P1 sont décrites dans [le bilan d'implémentation](SECURITY_P1_IMPLEMENTATION_2026-10-03.md).

La licence Community fournie par le propriétaire est acceptée par le vérificateur officiel ImageSharp. Le fichier reste privé, hors du dépôt et de l'installateur. Son courriel annonce une expiration le 1er janvier 2028 ; le [README](../README.md#imagesharp-licence--maintainers-and-ai-agents) rappelle le renouvellement.

## Chaîne officielle et identité de la candidate

Les résultats de `build_installer.ps1`, le commit source, les empreintes et les vérifications du payload seront consignés ici après construction. Aucun ancien artefact de contrôle Debug ou de syntaxe Inno ne doit servir à cette release.

## Qualification installée restante

La [phase 3 du plan P1](SECURITY_P1_REMEDIATION_PLAN_2026-10-03.md#phase-3--construire-et-vérifier-la-distribution) prévoit une VM Windows avec snapshot :

- Installation neuve, mise à jour depuis 1.20.0, réinstallation, installation core seule, menus Explorer et désinstallation Windows.
- Entrée HKCU trompeuse et témoin inoffensif, sans exécution élevée ; élévation avec le même compte puis un autre compte.
- Conservation des modèles et fichiers étrangers lors de la désinstallation.
- Vérification du runtime réellement installé pour l'application et le worker ; traitements représentatifs, collisions, annulation, PDF, refus de TIFF et chemins CPU/DirectML affectés.

Ces scénarios ne sont pas validés par la seule compilation ou les tests du dépôt. Aucun environnement VM/Sandbox prêt à l'emploi n'a été trouvé lors du chantier P1. La machine de développement ne sert pas à simuler une attaque sur le registre élevé.

Les modèles LaMa/DeepFilterNet et certains corpus Whisper ne sont pas disponibles localement. Les tests ignorés et les recettes non exécutées doivent rester explicites.

## Décision de publication

Aucun push, upload ou publication GitHub n'est autorisé par la seule préparation de cette candidate. L'acceptation finale du propriétaire et son périmètre seront consignés après son retour. Les quatre P1 ne sont pas déclarés clos sur une version distribuée avant cette décision et la vérification de la distribution.
