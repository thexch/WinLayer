# WinLayer

- Utiliser le nom WinLayer. Garder l’architecture simple et respecter les conventions C#, TypeScript, CSS et MSBuild du dépôt.
- Privilégier le coût au repos. Garder WebView2 dans le processus de réglages, privilégier les événements et arrêter les tâches, abonnements et ressources natifs de tout module désactivé.
- Avant chaque travail, consulter `docs/SKILLS.md` et les `SKILL.md` pertinents sous `.agents/skills/`. Rechercher un spécialiste maintenu uniquement si le besoin dépasse ces skills et la documentation officielle. Épingler et inspecter toute source avant de l’ajouter.
- Maintenir les contrats locaux et les préférences persistées. Signaler les capacités partielles et les erreurs. Ne jamais présenter une donnée simulée comme une lecture de périphérique.
- Ajouter des tests ciblés pour chaque fonctionnalité. Vérifier les régressions des modules existants, y compris leur désactivation, et actualiser la documentation lors de changements importants.
- Limiter les dépendances et justifier celles qui sont ajoutées. Utiliser le SDK de `global.json` et des versions stables.
- Construire le frontend local avec `npm ci --prefix web` puis `npm run build --prefix web`. Vérifier les interactions et les états visuels avec Playwright. Les fixtures de navigateur restent sous `web/tests`.
- Restreindre le pont WebView2 à l’origine locale et aux commandes explicitement typées. Conserver le protocole IPC et les préférences V1. N’ajouter aucune URL externe, dépendance runtime web ou commande générique au pont.
- Exécuter `dotnet build WinLayer.slnx -c Release -warnaserror` et `dotnet test WinLayer.slnx -c Release --no-build`. La CI Windows valide aussi l’installation, la migration depuis V1 et le cycle de vie de WebView2 et produit un installateur de test.
- Garder les données utilisateur hors de l’installation. Ne jamais intégrer de jeton ou autre secret aux binaires de distribution.
- Réserver les tags stables aux commits validés. Suivre `docs/RELEASING.md` pour les essais de l’installation et des mises à jour.

- Pour chaque mise à jour de l’application livrée à Théo, publier directement une version stable après la réussite de la CI sur `main`. Vérifier que le workflow de release a réussi et que le flux Velopack public propose la version, pour permettre l’installation depuis **Mises à jour** dans WinLayer.
