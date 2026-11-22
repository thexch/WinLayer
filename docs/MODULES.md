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

### Bandeau batterie dans la barre des tâches

Le bandeau place un pictogramme et le pourcentage côte à côte, juste avant la zone de notification de la barre des tâches principale. WinLayer reconnaît le casque HyperX et déduit les formes courantes à partir du nom du périphérique. Les autres modèles utilisent un pictogramme de batterie. Seuls les niveaux connus des périphériques connectés sont affichés.

Le réglage **Bandeau batterie dans la barre des tâches** se trouve dans **Périphériques**. Un clic sur le bandeau ouvre cet écran. La préférence d’affichage de la V2.2 est reprise.

WinLayer crée sa propre fenêtre enfant translucide sous `Shell_TrayWnd` et la place avant `TrayNotifyWnd`. Il garde les API de dessin et le code dans son propre processus. La mise en page s’adapte au DPI et suit les changements de position ainsi que les redémarrages d’Explorer. Une barre verticale ou une zone de notification introuvable masque le bandeau. Windows ne réserve pas d’espace pour cette fenêtre sur une barre très remplie. Le placement et la lisibilité se vérifient sur un bureau Windows réel.

[Principe de placement de BatteryHub](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor/blob/1dc6bc5d77c7272f60ee478877e068677cb3ab9d/README.md#how-it-works) · [Fenêtres superposées Win32](https://learn.microsoft.com/windows/win32/winmsg/window-features#layered-windows)

## Auto Clicker

`AutoClickerOptions` porte les réglages et la validation. `AutoClicker` détermine les transitions des raccourcis et la cadence sans dépendre de Win32. L’agent Windows réserve deux combinaisons via `RegisterHotKey`, avec rejet des conflits avant enregistrement des préférences. Il installe un hook clavier de bas niveau uniquement pendant une séquence Maintien, pour arrêter la série au relâchement. Un thread et un minuteur Windows haute résolution attendent entre les actions, et sont libérés à l’arrêt de la séquence. Le module désactivé n’enregistre aucun raccourci.

Les clics sont transmis via `SendInput`. La restriction UIPI de Windows peut bloquer l’injection vers une application exécutée avec plus de privilèges. Un bureau sécurisé, un jeu ou une application qui filtre les entrées simulées peuvent aussi ignorer les clics. En mode Maintien, les modificateurs restent enfoncés et la cible peut interpréter, par exemple, Ctrl+clic. Un double-clic envoie deux pressions du bouton gauche et compte pour une action. La position fixe utilise les coordonnées en pixels du bureau virtuel, y compris les coordonnées négatives. [Documentation SendInput](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendinput)
