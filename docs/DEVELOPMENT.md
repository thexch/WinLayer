# Développement

## Cloud et Windows

Le SDK .NET 10 est épinglé dans `global.json`. Utiliser un SDK officiel installé dans un dossier accessible, ou celui de GitHub Actions. Dans un environnement Cloud à domicile en lecture seule, définir `DOTNET_CLI_HOME` et `NUGET_PACKAGES` vers des dossiers temporaires inscriptibles. Vérifier le SHA-512 de l’archive du SDK à partir des métadonnées officielles avant de l’extraire.

```shell
dotnet restore WinLayer.slnx
dotnet build WinLayer.slnx -c Release --no-restore -warnaserror
dotnet test WinLayer.slnx -c Release --no-build
```

`EnableWindowsTargeting` permet la compilation des projets Windows depuis Linux. L’exécution de WinForms, WPF, WinRT et du packaging Windows se fait sous Windows. La CI installe silencieusement le paquet puis teste le processus agent, l’instance unique, l’identification de la version, le maintien de l’agent après fermeture de WPF et l’arrêt propre sur son runner. Les tests du module et du stockage s’exécutent sur les deux systèmes.

Pour lancer les réglages dans un environnement Windows de développement, installer le SDK .NET 10 et ouvrir PowerShell 7.

```powershell
./scripts/Start-Development.ps1
```

Quitter d’abord toute autre instance de WinLayer. Un exécutable de développement utilise les mêmes données de profil que le paquet. Utiliser un autre compte ou une VM pour isoler les essais. Le test de l’agent installé s’active dans une session Windows isolée lorsque `WINLAYER_SMOKE_EXE` et `WINLAYER_SMOKE_VERSION` désignent le binaire et la version installés.

## Validation manuelle sur Windows 11 x64

1. Installer l’artefact **WinLayer-Windows-x64** d’une CI verte sous un compte standard. Vérifier le lancement initial depuis le menu Démarrer et l’icône dans la zone de notification.
2. Ouvrir et fermer les réglages depuis l’icône. Vérifier dans le Gestionnaire des tâches que `WinLayer.Settings.exe` a quitté et que `WinLayer.exe` reste prêt. Rouvrir Device Battery via le menu et quitter l’agent puis vérifier la disparition de l’icône.
3. Vérifier les thèmes clair, sombre, système et contraste élevé, le clavier seul, les lecteurs d’écran et les échelles 100 % et 150 %.
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
