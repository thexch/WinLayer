# Développement

## Cloud et Windows

Le SDK .NET 10 est épinglé dans `global.json`. Utiliser Node.js 24 et le lockfile npm du dossier `web`. Utiliser un SDK officiel installé dans un dossier accessible, ou celui de GitHub Actions. Dans un environnement Cloud à domicile en lecture seule, définir `DOTNET_CLI_HOME` et `NUGET_PACKAGES` vers des dossiers temporaires inscriptibles. Vérifier le SHA-512 de l’archive du SDK à partir des métadonnées officielles avant de l’extraire.

```shell
npm ci --prefix web
npm run build --prefix web
dotnet restore WinLayer.slnx
dotnet build WinLayer.slnx -c Release --no-restore -warnaserror
dotnet test WinLayer.slnx -c Release --no-build
```

`EnableWindowsTargeting` permet la compilation des projets Windows depuis Linux. L’exécution de WinForms, WebView2, WinRT et du packaging Windows se fait sous Windows. La CI teste l’installation fraîche, l’ouverture de React dans WebView2, la libération du processus navigateur et le maintien de l’agent après la fermeture. Elle applique aussi le paquet V2 avec l’outil Velopack de la V1 publique, puis vérifie la conservation des préférences. Les tests du module et du stockage s’exécutent sur les deux systèmes.

Pour lancer les réglages dans un environnement Windows de développement, installer le SDK .NET 10 et ouvrir PowerShell 7.

```powershell
./scripts/Start-Development.ps1
```

Quitter d’abord toute autre instance de WinLayer. Un exécutable de développement utilise les mêmes données de profil que le paquet. Utiliser un autre compte ou une VM pour isoler les essais. Le test de l’agent installé s’active dans une session Windows isolée lorsque `WINLAYER_SMOKE_EXE` et `WINLAYER_SMOKE_VERSION` désignent le binaire et la version installés.

## Vérifier React depuis Cloud

Après le build du bundle, exécuter les contrôles Playwright avec un Chromium installé dans l’environnement.

```shell
cd web
npx playwright install chromium
npm test
```

Avec un Chromium système, définir `CHROMIUM_PATH` vers son exécutable et lancer directement `npm test`. Le serveur de prévisualisation tourne uniquement pendant les tests. Les fixtures injectent le pont dans Playwright et restent hors du bundle. Les captures sont écrites sous `artifacts/ui` et jointes à la CI. Elles couvrent la vue d’ensemble, les appareils, le thème sombre, la confirmation et la fenêtre compacte. Les valeurs de ces captures sont des données de test.

Sous Windows, le runtime Microsoft Edge WebView2 Evergreen est requis pour les réglages. Il est normalement déjà présent sur Windows 11. Si une image Windows ne l’inclut pas, installer le **Evergreen Standalone Installer** x64 de Microsoft en amont. Le paquet WinLayer inclut les assets React et le chargeur natif, et les réglages fonctionnent hors connexion lorsque le runtime est installé. [Microsoft WebView2 Evergreen](https://developer.microsoft.com/microsoft-edge/webview2/)

## Validation manuelle sur Windows 11 x64

1. Installer l’artefact **WinLayer-Windows-x64** d’une CI verte sous un compte standard. Vérifier le lancement initial depuis le menu Démarrer et l’icône dans la zone de notification.
2. Ouvrir et fermer les réglages depuis l’icône. Vérifier dans le Gestionnaire des tâches que `WinLayer.Settings.exe` a quitté et que `WinLayer.exe` reste prêt. Rouvrir Device Battery via le menu et quitter l’agent puis vérifier la disparition de l’icône.
3. Vérifier les thèmes clair, sombre, système et contraste élevé, le clavier seul, le Narrateur et les échelles Windows 100 % et 150 %. Comparer le rendu WebView2 au rendu Chromium des captures CI.
4. Activer le lancement avec Windows, fermer la session et la rouvrir. Vérifier l’icône sans ouverture des réglages. Refaire le test après désactivation.
5. Associer un périphérique Bluetooth LE compatible et un appareil dont le niveau est indisponible. Vérifier le niveau effectivement lu, la déconnexion, l’actualisation manuelle, le seuil d’alerte et l’absence d’alertes après désactivation. Vérifier la persistance au redémarrage.
6. Tester l’installation et les mises à jour selon [RELEASING.md](RELEASING.md).

## Mesurer sur le PC

Sur la même machine, conserver plan d’alimentation, état du Bluetooth et nombre de périphériques constants. Quitter WinLayer, lancer le binaire agent avec `--background --measure-startup`, puis relever la dernière ligne `tray-ready-ms=` dans `%LOCALAPPDATA%\WinLayerData\agent.log`. Cette durée couvre l’initialisation depuis la fin des hooks Velopack jusqu’à la création de l’icône et exclut le chargement du processus avant `Main`. Répéter au moins cinq fois et noter les conditions froides et chaudes.

```powershell
$agent = Get-Process WinLayer
./scripts/Measure-WinLayer.ps1 -AgentPid $agent.Id -Seconds 360 -Scenario 'Battery désactivé'
./scripts/Measure-WinLayer.ps1 -AgentPid $agent.Id -Seconds 360 -Scenario 'Battery activé'
```

Activer le module entre ces mesures et fermer les réglages avant chaque échantillon. Garder une fenêtre de mesure supérieure à l’intervalle de secours testé et effectuer une série avec puis sans périphérique connecté. Pour mesurer un jeu, reproduire le même scénario et comparer l’agent arrêté, actif avec module désactivé, puis actif avec module activé. Ajouter un profil ETW/WPR sur les scénarios qui nécessitent d’attribuer des coûts à un composant. Les valeurs CPU du script représentent le pourcentage d’un cœur logique.

[Métadonnées officielles du SDK](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) · [Ciblage Windows depuis Linux](https://learn.microsoft.com/dotnet/core/tools/sdk-errors/netsdk1100) · [Outils de performance Windows](https://learn.microsoft.com/windows-hardware/test/wpt/)

## Vérifier Auto Clicker sur Windows

1. Ouvrir Auto Clicker et activer le module. Utiliser une fenêtre de test non élevée, avec une zone qui enregistre les clics sans exécuter d’action. Tester les boutons gauche, droit et molette, puis le double-clic.
2. En mode Bascule, démarrer puis arrêter avec F6. En mode Maintien, relâcher F6, puis refaire le test avec Ctrl+Alt+C et relâcher un modificateur avant la touche principale. Répéter avec la fenêtre WinLayer fermée.
3. À 1, 25 et 200 CPS, comparer le nombre reçu et les intervalles pour une série bornée. Vérifier un compte de 10, une durée de 2 secondes et l’absence de rafale après un réveil tardif. À 200 CPS, l’intervalle demandé est de 5 ms. La cadence réelle dépend de l’ordonnanceur et de la capacité de l’application cible à traiter les événements.
4. Arrêter une série illimitée avec Ctrl+Alt+Maj+F10. L’état doit passer à **En pause** et F6 doit rester sans effet jusqu’au réarmement. Vérifier aussi la désactivation, la fermeture de l’agent et la réutilisation des deux combinaisons après désactivation.
5. Essayer des coordonnées négatives sur un deuxième écran, à deux échelles DPI. Essayer un point hors écran et vérifier la mise en pause avec un message. Redémarrer WinLayer et vérifier les réglages sauvegardés, avec un module inactif tant que le raccourci n’a pas été utilisé.

Les tests automatisés contrôlent la cadence sur une horloge déterministe, le nombre et la durée, les transitions des raccourcis et l’annulation. Le test du paquet vérifie la réservation Windows et la libération des raccourcis alors que WebView2 est fermé. Les clics sur le bureau et la réception de messages par la fenêtre cible sont à vérifier dans une session Windows interactive.

## Validation du Windows Hub

1. **Taskbar Studio**. Comparer l’aperçu et le liseré, tester barre en masquage automatique, déplacement après changement d’affichage, redémarrage d’Explorer, restauration et clics sur la barre.
2. **Alt-Tab Customizer et Always on Top**. Vérifier les fenêtres minimisées, réduites à l’icône, fermées pendant la sélection et les exclusions. Tester Tab, flèches, Entrée et Échap. Vérifier les contours et restaurer plusieurs fenêtres en désactivant le module.
3. **Macro Recorder et Key Repeater**. Dans un éditeur de test, vérifier clavier, mouvements et clics reçus. Tester enregistrement, pause, exclusion, import/export, relecture limitée, perte de focus, raccourcis conflictuels et arrêt d’urgence. Tester le répétiteur en Bascule et Maintien, puis à 1, 25 et 200 répétitions/s après fermeture de WebView2. Comparer les événements reçus.
4. **Screen Tools**. Prélever une couleur SDR connue et mesurer un rectangle de référence sur un puis deux écrans. Tester les écrans à 100 %/150 %, les coordonnées négatives, Échap, le presse-papiers et le contenu protégé ou HDR.
5. **Monitoring**. Comparer débits, occupation et mémoire au Gestionnaire des tâches sur le même intervalle. Tester réseau déconnecté, compteur manquant, sélection d’adaptateur, alertes, téléchargement contrôlé, processus terminable et annulation. Vérifier qu’aucun capteur inaccessible n’affiche de valeur estimée.
6. **Mise à jour**. Installer 2.4.0, modifier les réglages d’Auto Clicker, puis installer un paquet de test du Hub. Vérifier les valeurs, la fermeture de WebView2 et l’arrêt propre de l’agent. La CI couvre aussi la mise à jour locale depuis les installateurs publiés 1.0.0 et 2.4.0.

### Comparer les coûts au repos

Utiliser le même Windows, poste, plan d’alimentation et ensemble de périphériques. Pour chaque scénario, relever cinq démarrages avec `--measure-startup`, attendre une minute puis exécuter `./scripts/Measure-WinLayer.ps1 -AgentPid $agent.Id -Seconds 360 -Scenario '<scénario>'`. Répéter trois fois.

| Scénario | Conditions |
| --- | --- |
| Référence | WinLayer 2.4.0, réglages fermés, outils désactivés |
| Nouveaux outils désactivés | Paquet Hub, mêmes conditions |
| Outils courants | Battery, raccourcis et Always on Top activés, réglages fermés |
| Monitoring | Mesurer à 2 s avec les deux vues ouvertes à tour de rôle, avec le seul débit réseau et les indicateurs choisis en tâche de fond, puis après désactivation |

Consigner le CPU d’un cœur, working set, mémoire privée, nombre de handles, démarrage et présence du processus de réglages. Capturer aussi un profil WPR lors du monitoring si une différence doit être expliquée. La validation des mesures, du HDR et des combinaisons d’écrans reste à faire sur un Windows physique.

## Validation interactive de WinLayer 4

Utiliser l’installateur de CI sur Windows 11. Tester avec la fenêtre des paramètres fermée. La CI vérifie le paquet, les migrations et l’IPC de l’agent, dont la réservation et la restauration des raccourcis, ainsi que l’ouverture et la fermeture du Drop Shelf. Exécuter aussi les contrôles suivants sur le bureau interactif avant le tag stable.

| Zone | Manipulation | Résultat attendu |
| --- | --- | --- |
| Barre des tâches | Connecter casque et manette. Comparer le bandeau à l’heure Windows à 100 %, 125 %, 150 % et 200 %. Basculer le thème et masquer la barre. | Les symboles et pourcentages restent lisibles et centrés. Le bandeau se place à gauche de la zone de notification et suit sa visibilité. |
| Screen OCR | Sélectionner du texte hors ligne sur chaque écran. Tester les langues installées et une langue absente. Relancer, éditer, copier, fermer avec Échap. | Le texte suit les lignes et la zone demandée. L’absence d’un pack est indiquée. Par défaut, aucun fichier d’historique OCR n’est créé. Tester ensuite l’activation et l’effacement de l’historique. |
| Screen Pin | Épingler deux régions sur des moniteurs à DPI différents. Redimensionner, zoomer, modifier l’opacité, copier puis enregistrer. | Les captures restent visibles après fermeture des paramètres et disparaissent à la fermeture de l’agent. |
| Drop Shelf | Déposer un dossier, une image et du texte. Réorganiser les entrées, glisser vers une autre application et déplacer le fichier d’origine depuis Explorer. | Les originaux sont conservés. Les références invalides sont refusées. Le nettoyage exclut les entrées épinglées jusqu’à leur retrait. |
| Palette et menu radial | Ajouter un favori et une macro, réorganiser les secteurs et ouvrir chaque menu au clavier et près de chaque bord. | Échap ou un clic extérieur referme les menus. Une action native s’ouvre directement. Un réglage ouvre la bonne page avec le contrôle ciblé. |
| OSD et coins actifs | Activer Caps Lock, Num Lock, puis Scroll Lock. Configurer deux coins avec des délais distincts et tester un jeu en plein écran. | L’OSD n’intercepte pas les clics et ne prend pas le focus. Un coin se déclenche une fois par entrée et respecte le plein écran, les exclusions et le délai. |
| Image Converter | Convertir un JPEG orienté, un PNG transparent, un BMP et un WebP, puis relancer le même lot et annuler à mi-parcours. | Les formats proposés reflètent les codecs Windows. L’orientation et les proportions sont correctes. Les sorties sont renommées en cas de doublon et les sources restent intactes. |
| Thèmes, restauration et mise à jour | Prévisualiser Forêt et Contraste, sauvegarder un thème, exporter un profil, changer de raccourci puis redémarrer et annuler. Mettre à jour depuis la version précédente. | La configuration survit au redémarrage et à la mise à jour. Les collisions de raccourcis refusent le changement. Une restauration validée applique le profil ou laisse l’ancienne configuration en cas d’échec. |

Mesurer le CPU, la RAM et le démarrage avec `scripts/Measure-WinLayer.ps1`. Comparer le repos après fermeture des réglages à une session avec coins actifs et indicateurs clavier. Attendre que les overlays soient fermés et répéter avec plusieurs écrans. Le suivi des coins utilise un timer seulement quand un coin est configuré. L’OSD utilise Raw Input seulement quand au moins un indicateur est activé. L’OCR et la conversion travaillent à la demande.

La version actuelle propose un profil radial actif sélectionné dans l’éditeur, des réglages de coins communs aux écrans, et l’ouverture du panier par action ou raccourci. Elle exporte les images sans métadonnées et utilise la qualité réglable pour JPEG. Le thème change les pictogrammes du hub et les outils natifs rouverts. Les icônes Windows de l’exécutable et de l’installation suivent les ressources du paquet.

## Navigation et thèmes des fenêtres

Le réseau a sa propre page et les mesures matérielles restent dans System Tray Monitor. Ouvrir **Réglages du réseau** ou **Mesures et indicateurs** montre les paramètres près des relevés. Dans **Screen Tools**, les raccourcis de capture se trouvent avec l’OCR. Dans **Utilitaires** et **Général**, vérifier que la recherche arrive dans la bonne catégorie et que les modifications en attente restent accessibles lorsque l’on change de catégorie.

Tester les listes et les palettes de couleur avec Tab, les flèches et Échap. Sur Windows 11, ouvrir Drop Shelf, la palette, Image Converter, l’historique OCR, le sélecteur de fenêtres et l’enregistreur de macros. Contrôler le cadre, la fermeture, les listes et la lecture de longues listes de résultats en thème clair, sombre, personnalisé et en contraste élevé. Les dialogues de fichiers et les fenêtres de sécurité Windows sont affichés par le système.
