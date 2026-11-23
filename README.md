# WinLayer

WinLayer est une application Windows 11 x64 de personnalisation. Un agent reste accessible dans la zone de notification. Les réglages React s’ouvrent à la demande dans une fenêtre Windows et chargent leurs fichiers locaux via WebView2. Les modules actifs continuent de fonctionner après leur fermeture.

## Installer

Téléchargez `WinLayer-win-Setup.exe` depuis une [version stable de GitHub Releases](https://github.com/thexch/WinLayer/releases), lorsqu’une version est disponible. Exécutez l’installateur depuis votre session Windows. Le paquet inclut le runtime .NET. Pour les builds V2, la fenêtre utilise Microsoft Edge WebView2 Evergreen, fourni avec Windows 11 dans une installation standard. Pour essayer une version avant la première release, téléchargez l’artefact **WinLayer-Windows-x64** d’une [CI Windows réussie](https://github.com/thexch/WinLayer/actions/workflows/ci.yml), décompressez-le et lancez le Setup sur un PC de test. Les builds actuels sont non signés et peuvent déclencher SmartScreen.

## Utiliser

- Cliquez sur l’icône WinLayer près de l’horloge. Elle peut être dans les icônes masquées. Le menu donne accès aux réglages, à Device Battery, à l’état de l’agent et à **Quitter WinLayer**.
- Activez **Device Battery** pour suivre la batterie des périphériques Bluetooth LE compatibles et du **HyperX Cloud III Wireless via son dongle USB** à partir de la version 2.1.0. Allumez le casque puis sélectionnez **Actualiser** dans **Périphériques**. L’intervalle de secours et les alertes sont réglables.
- Activez **Démarrer avec Windows** dans **Général** si vous le souhaitez. Le démarrage automatique ouvre uniquement l’agent.
- Cherchez les nouvelles versions dans **Mises à jour**. L’installation demande une confirmation et redémarre WinLayer. Les préférences sont stockées dans `%LOCALAPPDATA%\WinLayerData\preferences.json`.

### Auto Clicker

Activez le module dans **Auto Clicker**, puis choisissez le type de clic, la cadence de 1 à 200 CPS, la position et la limite éventuelle. Par défaut, **F6** démarre ou arrête la séquence en mode Bascule. En mode Maintien, gardez le raccourci enfoncé. **Ctrl+Alt+Maj+F10** met le module en pause jusqu’au réarmement. Les deux raccourcis sont modifiables et fonctionnent quand les réglages sont fermés. Les paramètres se sauvegardent automatiquement.

Certaines applications filtrent les entrées simulées. Consultez les limites de Windows et le protocole de vérification. [Modules](docs/MODULES.md) · [Validation sur Windows](docs/DEVELOPMENT.md#vérifier-auto-clicker-sur-windows)

### Windows Hub

- **Taskbar Studio** applique un liseré, un cadre ou une teinte à couleur et opacité réglables. Enregistrez des styles et restaurez la barre en sélectionnant **Style Windows**.
- **Alt-Tab Customizer** ouvre un sélecteur indépendant avec aperçu, recherche et exclusions. **Always on Top** épingle la fenêtre active et peut signaler l’épinglage par un contour.
- **Key Repeater** se trouve dans **Auto Clicker**. **Macro Recorder** enregistre dans une fenêtre dédiée, édite les étapes et rejoue les séquences enregistrées vers une fenêtre choisie.
- **Screen Tools** regroupe la pipette avec historique et palettes et la règle en pixels sur le bureau.
- **Network Monitor** affiche les débits mesurés par interface. **System Tray Monitor** affiche les compteurs disponibles de Windows, les volumes et les processus et propose des indicateurs de notification.

Les outils sont disponibles dès le démarrage de l’agent. Le monitoring collecte en continu et les outils interactifs se déclenchent à la demande. [Fonctionnement et limites Windows](docs/MODULES.md#windows-hub)

## Contribuer

[Architecture](docs/ARCHITECTURE.md) · [Design V2](docs/DESIGN.md) · [Développement et mesures](docs/DEVELOPMENT.md) · [Modules](docs/MODULES.md) · [Installation et releases](docs/RELEASING.md) · [Skills Codex](docs/SKILLS.md)

## WinLayer 4

La vue d’ensemble présente les actions rapides, les favoris, les batteries et l’état du PC. **Ctrl+K** ouvre la recherche dans les réglages. Depuis Windows, **Ctrl+Maj+P** ouvre la palette de commandes et **Ctrl+Maj+R** le menu radial. Les raccourcis sont modifiables dans **Utilitaires**.

- **Screen OCR** extrait le texte d’une zone avec le moteur Windows. Installer la reconnaissance optique dans les options de langue de Windows pour le français ou l’anglais. L’historique est facultatif.
- **Screen Pin** affiche une capture dans une fenêtre flottante et **Drop Shelf** conserve temporairement les éléments glissés.
- **Image Converter** exporte des lots en PNG, JPEG ou BMP, et en WebP si Windows expose un encodeur. Les sources sont conservées et les métadonnées retirées des exports.
- **Coins actifs**, **Indicateurs clavier** et **App Themes** se règlent dans **Utilitaires**. Le panier s’ouvre par raccourci ou par action et stocke les éléments en mémoire jusqu’à expiration ou à la fermeture de l’agent.

Utiliser **Restaurer** dans les utilitaires pour exporter un profil et annuler un changement réversible. La [matrice de validation Windows 11](docs/DEVELOPMENT.md#validation-interactive-de-winlayer-4) décrit les essais DPI, multi-écrans, capture et migration.
