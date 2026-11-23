# Installation et releases

## De Codex Cloud au PC Windows

1. Développer et tester depuis Cloud, puis pousser sur `main`. **Windows CI** restaure, construit React, compile .NET, package et exécute les tests sur `windows-latest`. Le job frontend valide le bundle de production avec Chromium. Aucun commit ne publie de version stable.
2. Télécharger l’installateur x64 depuis l’artefact **WinLayer-Windows-x64** du commit à publier. Valider sur un vrai Windows 11 les cas de [DEVELOPMENT.md](DEVELOPMENT.md). Les versions CI ont un numéro de préversion et restent dans Actions.
3. Créer un tag stable sur le commit validé. La release exige une CI Windows réussie pour ce SHA sur `main`, puis reconstruit et reteste le paquet sur Windows avant de publier ses fichiers. Le compte qui pousse le tag doit avoir les droits GitHub requis. Exemples après vérification et avec un `HEAD` validé.

   ```shell
   git tag -a v2.0.0 -m "WinLayer 2.0.0" HEAD
   git push origin v2.0.0
   ```

4. Depuis [GitHub Releases](https://github.com/thexch/WinLayer/releases), télécharger et exécuter `WinLayer-win-Setup.exe`. L’environnement autonome x64 est inclus. Pour les versions suivantes, augmenter la version sémantique et répéter la validation avant de pousser le tag.

## Packaging et mise à jour

Pour créer un paquet hors GitHub Actions, exécuter sur Windows avec PowerShell 7, Node.js 24 et le SDK .NET du dépôt. Le script compile le bundle React local avant de créer l’installateur.

```powershell
./scripts/Package-Windows.ps1 -Version 2.0.0
```

Velopack produit sous `artifacts/releases` le Setup, le paquet complet `.nupkg` et l’index d’updates `releases.win.json`. Conserver ensemble les fichiers de distribution générés. L’agent consulte l’index du canal Windows stable via GitHub. Il exige un SHA-256, et Velopack vérifie le paquet. Le workflow de publication utilise son jeton GitHub éphémère, transmis uniquement à la CLI.

Pour la V2, le runner Windows installe la V1 officielle après vérification de son SHA-256, sauvegarde des préférences via l’agent V1 et applique le paquet V2 avec `Update.exe`. Il contrôle la version, les préférences et l’ouverture des réglages. Ces essais couvrent la migration locale depuis les versions publiées 1.0.0, 2.4.0 et 3.1.2. Valider aussi le parcours depuis GitHub avant une diffusion stable en utilisant un dépôt public de test. Sur une branche jetable, changer l’URL de la source GitHub dans l’agent et produire deux paquets stables de test. Publier les assets uniquement dans le dépôt d’essai. Installer le plus ancien, modifier une préférence, puis faire les deux parcours **Annuler** et **Installer**. Vérifier l’arrêt de l’hôte WebView2, le redémarrage de l’agent, l’affichage de la nouvelle version dans les réglages, la conservation des préférences et la reprise de Device Battery. Tester également une coupure réseau, puis une nouvelle tentative.

Après confirmation, l’agent ferme les réglages par le flux de mise à jour et Velopack redémarre WinLayer. Les données sont dans un dossier de profil indépendant.

Le feed GitHub intégré est prévu pour le dépôt public `thexch/WinLayer`. Un dépôt privé requiert un mécanisme authentifié de distribution, comme un feed interne et des identifiants individuels gérés à l’extérieur du binaire. Les téléchargements anonymes GitHub n’y fonctionnent pas.

## Signature

La chaîne actuelle produit des exécutables non signés. Windows peut présenter un avertissement SmartScreen ou bloquer leur exécution selon la politique de la machine. Pour distribuer avec une identité vérifiée, configurer la signature Authenticode horodatée via l’option Velopack documentée, avec un certificat ou un service de signature dont les identifiants restent dans l’environnement de release. Le SHA-256 contrôle l’intégrité du paquet par rapport à son index, tandis que la signature atteste l’éditeur.

[Velopack et GitHub Actions](https://docs.velopack.io/distributing/github-actions) · [Microsoft Defender SmartScreen](https://learn.microsoft.com/windows/security/operating-system-security/virus-and-threat-protection/microsoft-defender-smartscreen/)
