# Modules

Un module compilé possède un `ModuleDescriptor` avec son identifiant, son nom, ses capacités et une déclaration du besoin d’élévation. Device Battery fonctionne sous le compte courant, avec les droits Bluetooth accordés par Windows. Utiliser des options immuables stockées dans `Preferences` et mettre en œuvre `IWinLayerModule<TOptions>`. L’agent sérialise les appels de configuration et d’arrêt. Une configuration désactivée doit annuler et attendre les tâches, supprimer les abonnements et fermer les handles. Seul le processus agent effectue le travail résident.

Pour ajouter un module, créer le comportement testable dans `WinLayer.Modules`, placer l’adaptateur Windows dans l’agent si nécessaire, puis relier les options et l’instantané à WPF. Documenter les sources, les capacités et les permissions. Ajouter des tests de transition et de défaillance en utilisant un fournisseur de test.

## Device Battery

Le fournisseur V1 énumère les périphériques **Bluetooth LE associés** avec WinRT. Il lit la caractéristique GATT standard Battery Level (`0x2A19`) du service Battery (`0x180F`) sur les appareils signalés comme connectés. Il accepte un octet entre 0 et 100. Une lecture expirée, non autorisée ou incomplète reste inconnue. Un appareil annoncé comme déconnecté est affiché sans pourcentage. Si la recherche du service réussit sans résultat, l’interface indique que Windows n’expose pas le service de batterie.

Les périphériques Bluetooth Classic seuls, dongles propriétaires, API Logitech/Razer et extensions constructeur sont hors périmètre V1. Certaines variantes HID exposent leur batterie dans les paramètres Windows via un autre mécanisme. Il est possible que WinLayer ne puisse pas la lire. La configuration donne un intervalle de secours de 2 à 60 minutes et un seuil de 5 à 50 %. La notification se déclenche une fois par passage sous le seuil et se réarme lorsqu’une lecture repasse au-dessus. Les notifications peuvent être masquées par les réglages de Windows.

Pour un futur fournisseur, implémenter `IBatteryProvider` avec des identifiants stables et les mêmes états de lecture. Si plusieurs fournisseurs cohabitent, définir la déduplication avant de fusionner leurs résultats et laisser l’agent n’activer que les sources configurées.

[API Bluetooth GATT de Windows](https://learn.microsoft.com/windows/apps/develop/devices-sensors/gatt-client) · [DeviceWatcher](https://learn.microsoft.com/uwp/api/windows.devices.enumeration.devicewatcher)
