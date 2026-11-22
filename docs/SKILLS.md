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
