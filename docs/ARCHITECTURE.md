# Architecture

**WinLayer.Agent** est le seul processus résident. Il héberge l’icône WinForms `NotifyIcon`, le démarrage par utilisateur, les modules et Velopack. Un mutex par utilisateur et session Windows évite deux agents sur le même bureau. Une invocation secondaire transmet sa demande d’ouverture à l’agent actif. `--background` démarre sans fenêtre, `--battery` ouvre Device Battery et `--quit` demande l’arrêt de l’agent.

**WinLayer.Settings** est un processus WPF à part entière. La fermeture de sa fenêtre arrête le processus et libère le runtime graphique. La navigation et l’état sont synchronisés par un instantané de l’agent toutes les deux secondes tant que la fenêtre existe. L’agent attend les demandes locales sans minuterie pour les réglages.

**WinLayer.Core** définit les contrats des préférences et du protocole. Le canal local est un *named pipe* limité au compte utilisateur Windows courant. Les messages JSON sont encadrés et bornés. L’agent valide les valeurs reçues et remplace le fichier de préférences après l’écriture complète d’un fichier voisin. Un fichier illisible est conservé jusqu’à un nouvel enregistrement explicite. La clé `HKCU\…\CurrentVersion\Run` contient le chemin de l’exécutable courant et `--background`. Windows reste libre d’appliquer ses propres politiques de démarrage.

**WinLayer.Modules** héberge le module Device Battery, ses dépendances injectées et son cycle de vie. **WinLayer.Agent** implémente le fournisseur Windows, isolé des règles de suivi et d’alerte. Les mécanismes WinRT reposent sur les événements d’association et de connexion, regroupés sur deux secondes, avec un intervalle de secours configurable. Une annulation arrête les lectures et désabonne le watcher avant la désactivation. L’agent ignore les résultats tardifs après désactivation.

## Distribution et exploitation

Velopack installe l’agent et WPF dans un même répertoire. Le packaging réunit leurs fichiers de runtime après vérification des doublons par SHA-256. WPF et ses ressources sont chargés sur demande. Les préférences et le journal rotatif vivent hors de l’installation dans `%LOCALAPPDATA%\WinLayerData`. Le journal reste local.

L’agent consulte les releases publiques stables au bout d’une minute, puis toutes les 24 heures si la recherche automatique est activée. Il télécharge seulement après confirmation, exige un SHA-256 pour le paquet complet et délègue à Velopack sa vérification. Il attend la fermeture de la fenêtre puis programme l’installation au moment où l’agent se termine.

Le modèle V1 cible une session interactive Windows 11 par compte. Deux sessions simultanées du même compte partagent le profil et les préférences. Les sessions multi-utilisateurs ou RDP simultanées ne font pas partie du périmètre validé.

[NotifyIcon](https://learn.microsoft.com/dotnet/api/system.windows.forms.notifyicon) · [NamedPipeServerStream](https://learn.microsoft.com/dotnet/api/system.io.pipes.namedpipeserverstream) · [Clés Run et RunOnce](https://learn.microsoft.com/windows/win32/setupapi/run-and-runonce-registry-keys) · [Intégration Velopack](https://docs.velopack.io/integrating/overview)
