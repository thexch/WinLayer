# Skills Codex

La découverte a couvert les skills fournis par la session, le catalogue officiel OpenAI et le dépôt officiel .NET. Les skills .NET retenus sont installés sous `.agents/skills/` dans le dépôt, selon le mécanisme de découverte de Codex. Les instructions ont été lues directement depuis ces fichiers dans la session en cours. Leur découverte automatique sera effectuée au démarrage des nouvelles sessions.

| Skill | Provenance et statut | Usage |
| --- | --- | --- |
| `setup-local-sdk` | .NET, installé dans le dépôt | SDK .NET 10 isolé, sélection via `global.json`, archive officielle vérifiée par SHA-512. |
| `csharp-expert` | .NET, installé dans le dépôt | Choix des spécialistes et conventions C# pour la séparation agent, interface et logique testable. |
| `msbuild` | .NET, installé dans le dépôt | Projets SDK-style, ciblage Windows, compilation sans restauration redondante. |
| `code-testing` et `code-testing-extensions` | .NET, installés dans le dépôt | Cas de persistance, données invalides, états et annulation du module. Extension .NET utilisée pour les commandes. |
| `analyzing-dotnet-performance` | .NET, installé dans le dépôt | Relecture des chemins résidents, annulation, attente asynchrone et protocole de mesure. |
| `authoring-github-workflows` | .NET, installé dans le dépôt | Vérification des deux workflows avec actionlint épinglé. |
| `cloud-environment-runtime` et `plugin-management` | Codex, déjà disponibles | Inventaire des connexions et installation du SDK sans modifier le système. |

La copie .NET provient de la révision `c227688ed14de4467e3e45f95dc69a94e905e219`. La licence MIT est incluse dans `.agents/skills/`. Le catalogue OpenAI propose un skill WinUI, spécifique à WinUI et au Windows App SDK. WPF s’appuie ici sur sa documentation Microsoft. Aucun skill officiel dédié à WPF ou au packaging Velopack n’a été retenu. Le packaging, l’intégrité et la signature s’appuient sur les documentations officielles. Le runtime Cloud n’exécute pas WPF ni les outils de packaging Windows.

[dotnet/skills](https://github.com/dotnet/skills/tree/c227688ed14de4467e3e45f95dc69a94e905e219) · [openai/skills](https://github.com/openai/skills) · [Skills Codex](https://developers.openai.com/codex/skills) · [WPF](https://learn.microsoft.com/dotnet/desktop/wpf/) · [Velopack](https://docs.velopack.io/)

## Migration V2

Les skills suivants sont installés sous `.agents/skills/` à partir de révisions épinglées. Pour les futures sessions Cloud, utiliser les fichiers locaux à la phase correspondante.

| Skill | Révision et provenance | Utilisation V2 |
| --- | --- | --- |
| `frontend-design` | [Anthropic](https://github.com/anthropics/skills/tree/e4bb5d960d87126ff3b3a8af1a104b7db67560ba/skills/frontend-design) | Comparaison des directions, hiérarchie, illustration et relecture des captures. |
| `ui-ux-pro-max` | [Next Level Builder](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill/tree/6ec2bb58334ca3014573c4cbd4834ee3a40afd9d) | Recherche locale inspectée sur les ensembles de styles et la visibilité du focus. La proposition de page marketing a été écartée pour cet utilitaire. |
| `design-motion-principles` | [Kyle Zantos](https://github.com/kylezantos/design-motion-principles/tree/4a9ca879f24a361f4dca4174fe2da0f67b5ddee3) | Fréquence des gestes, durée courte pour la confirmation et mouvement réduit. |
| `vercel-react-best-practices`, `vercel-composition-patterns` et `web-design-guidelines` | [Vercel](https://github.com/vercel-labs/agent-skills/tree/5a37a6329fbad3666c3207392a6a7c0110e92efb) | État local des formulaires, composition des contrôles, chargement différé de la confirmation et revue clavier, contraste, états et redimensionnement. |
| `playwright-interactive` | [OpenAI](https://github.com/openai/skills/tree/49f948faa9258a0c61caceaf225e179651397431/skills/.curated/playwright-interactive) | Inventaire des interactions, tests du bundle, scénarios hors connexion et refus d’enregistrement, contrôle visuel et captures. Utilisation via Playwright Test dans le bac à sable Cloud. Le mode `js_repl` sans sandbox demandé par le skill n’a pas été activé. |

Les workflows restent vérifiés avec le skill GitHub Actions .NET. Les fichiers de licence et notices lorsqu’ils sont fournis accompagnent les copies locales.
