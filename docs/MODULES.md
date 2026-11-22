# Modules

Un module compilé possède un `ModuleDescriptor` avec son identifiant, son nom, ses capacités et une déclaration du besoin d’élévation. Device Battery fonctionne sous le compte courant, avec les accès Bluetooth et USB HID accordés par Windows. Utiliser des options immuables stockées dans `Preferences` et mettre en œuvre `IWinLayerModule<TOptions>`. L’agent sérialise les appels de configuration et d’arrêt. Une configuration désactivée doit annuler et attendre les tâches, supprimer les abonnements et fermer les handles. Seul le processus agent effectue le travail résident.

Pour ajouter un module, créer le comportement testable dans `WinLayer.Modules`, placer l’adaptateur Windows dans l’agent si nécessaire, puis relier les options et l’instantané à l’interface. Documenter les sources, les capacités et les permissions. Ajouter des tests de transition et de défaillance en utilisant un fournisseur de test.

## Device Battery

Le fournisseur Bluetooth énumère les périphériques **Bluetooth LE associés** avec WinRT. Il lit la caractéristique GATT standard Battery Level (`0x2A19`) du service Battery (`0x180F`) sur les appareils signalés comme connectés. Il accepte un octet entre 0 et 100. Une lecture expirée, non autorisée ou incomplète reste inconnue. Un appareil annoncé comme déconnecté est affiché sans pourcentage. Si la recherche du service réussit sans résultat, l’interface indique que Windows n’expose pas le service de batterie.

La V2.1 ajoute le **HyperX Cloud III Wireless via son dongle USB**, pour les identifiants HP `03F0:05B7` et `03F0:0C9D`. D’autres dongles, dont celui du Cloud III S Wireless, exigent un protocole dédié.  La configuration donne un intervalle de secours de 2 à 60 minutes et un seuil de 5 à 50 %. La notification se déclenche une fois par passage sous le seuil et se réarme lorsqu’une lecture repasse au-dessus. Les notifications peuvent être masquées par les réglages de Windows.

Pour un futur fournisseur, implémenter `IBatteryProvider` avec des identifiants stables et les mêmes états de lecture. Si plusieurs fournisseurs cohabitent, définir la déduplication avant de fusionner leurs résultats et laisser l’agent n’activer que les sources configurées.

### HyperX Cloud III Wireless

Le fournisseur cible la collection HID constructeur `FF13:0001`. Il demande l’état de la liaison et le pourcentage via deux commandes de lecture et ouvre le handle en partage avec les autres applications. Les lectures refusées, incomplètes ou expirées laissent le niveau inconnu. Une réponse de liaison explicite permet d’afficher **Déconnecté** quand le dongle reste branché et que le casque est éteint. Les fournisseurs s’exécutent en parallèle et la panne de l’un laisse les lectures de l’autre visibles.

Sur un PC Windows équipé du casque, brancher le dongle, allumer le casque, activer **Device Battery**, puis sélectionner **Périphériques → Actualiser**. Comparer le niveau avec NGENUITY, tester le casque éteint avec le dongle branché, puis débrancher et rebrancher le dongle. Vérifier aussi le cas où NGENUITY est ouvert. Le runner Windows vérifie l’énumération et les tests valident les rapports, mais la communication physique avec le dongle se vérifie sur le matériel.

[Protocole HyperHeadset](https://github.com/LennardKittner/HyperHeadset/blob/3652a5de38c100842e4b2f882af0e04897804638/src/devices/cloud_iii_wireless.rs) · [Implémentation Windows de référence](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor/blob/1dc6bc5d77c7272f60ee478877e068677cb3ab9d/src/drivers/hyperx.cpp) · [Collections HID Windows](https://learn.microsoft.com/windows-hardware/drivers/hid/hid-collections)

[API Bluetooth GATT de Windows](https://learn.microsoft.com/windows/apps/develop/devices-sensors/gatt-client) · [DeviceWatcher](https://learn.microsoft.com/uwp/api/windows.devices.enumeration.devicewatcher)

### Batteries dans la zone de notification

Les niveaux connus et connectés peuvent s’afficher dans la zone de notification Windows. Chaque icône affiche un pictogramme et le pourcentage. WinLayer reconnaît le casque HyperX et déduit d’autres formes courantes, telles que manette, clavier et souris, à partir du nom. Les autres appareils utilisent une icône de batterie. La disponibilité des niveaux dépend du fournisseur de batterie, indépendamment du pictogramme.

Windows gère la visibilité et l’ordre des icônes. Pour afficher une icône dans la barre des tâches, ouvrir le menu **^** et la faire glisser vers la zone visible. Les identifiants stables permettent à Windows de mémoriser la préférence de visibilité. Le réglage **Pourcentages dans la barre des tâches** se trouve dans **Périphériques**. Cliquer une icône de batterie ouvre cet écran.

[Méthode Windows pour déplacer les icônes de notification](https://support.microsoft.com/windows/customize-the-taskbar-in-windows-0657a50f-0cc7-dbfd-ae6b-05020b195b07)
