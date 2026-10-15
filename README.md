# Driver Updater

Petite application Windows 10/11, dans l'esprit de Driver Booster, mais honnête :
elle ne propose que ce qui marche vraiment.

## Ce qu'elle fait

- **Pilotes.** Elle cherche les mises à jour de pilotes sur Windows Update, avec, en face
  de chacune, le pilote installé aujourd'hui. Elle installe ceux que vous cochez, un par un,
  après avoir créé un point de restauration système. Les pilotes « recommandés » sont
  cochés ; les « facultatifs » ne le sont pas, car Microsoft ne les conseille que pour
  corriger un problème précis. Un pilote refusé peut être masqué pour ne plus revenir.
- **Vérification hebdomadaire.** Option : chaque mercredi, l'application cherche en
  silence et affiche une notification seulement s'il y a du nouveau.
- **BIOS et carte graphique.** Windows Update les livre avec retard, ou pas du tout :
  l'application affiche les versions installées et ouvre l'outil du fabricant (Lenovo,
  Dell, HP, ASUS, Acer, MSI, Gigabyte, ASRock ; NVIDIA, AMD, Intel).
- **Logiciels.** Mise à jour des logiciels installés via winget, le gestionnaire de
  logiciels intégré à Windows.
- **Historique.** Les pilotes installés par Windows Update, quand, et avec quel résultat.
- **Santé.** État des disques, usure et température des SSD, état de la batterie d'un
  portable.
- **Réparer Windows.** SFC, DISM et le nettoyage des composants Windows, les outils
  officiels de Microsoft.
- **Sauvegarde des pilotes.** Copie tous les pilotes des fabricants dans un dossier, et
  les réinstalle en une fois après une réinstallation de Windows.
- **Fichiers temporaires.** Suppression des fichiers de plus de 2 jours dans
  `AppData\Local\Temp` et, sur demande, `C:\Windows\Temp`.
- **Copier le diagnostic.** Un rapport texte (versions, matériel, actions récentes) à
  coller dans un signalement de bug.
- **Nouvelle version.** Un bandeau prévient quand une version plus récente est publiée
  sur GitHub. L'application interroge pour cela l'API publique de GitHub à chaque
  démarrage ; elle n'envoie rien d'autre.

L'interface suit le thème de Windows 11, clair ou sombre.

Pourquoi Windows Update et pas « la toute dernière version » : les pilotes de Windows
Update sont signés et validés par Microsoft pour votre matériel. Les versions plus
récentes publiées directement par les fabricants sont parfois des bêtas.

## Installer

Téléchargez `DriverUpdater-Setup-x.y.z.exe` depuis la page
[Releases](https://github.com/Augaton/driver_updater/releases). Il installe l'application
dans `C:\Program Files\Driver Updater` et l'ajoute au menu Démarrer. Le zip « portable »
fonctionne aussi.

Pour vérifier qu'un fichier a bien été produit par le pipeline de ce dépôt :

```sh
gh attestation verify DriverUpdater-Setup-x.y.z.exe --repo Augaton/driver_updater
```

## Sécurité

- **Pas de droits administrateur par défaut.** La fenêtre tourne avec les droits de
  l'utilisateur. Chaque action qui en a besoin (installer ou masquer un pilote, point de
  restauration, sauvegarde, `C:\Windows\Temp`, réparation, lecture de l'usure des
  disques) relance l'application en administrateur pour cette seule action : Windows
  demande l'autorisation, l'action se fait, le processus se termine. Aucun processus
  administrateur ne reste ouvert pour qu'un autre programme lui parle.
- **Module administrateur.** Il n'accepte qu'une liste fermée de commandes, et vérifie
  leurs arguments : identifiants de mise à jour au format GUID, dossiers locaux en chemin
  complet, nom de canal généré par l'application. Il rend compte par un canal nommé privé
  (réservé au compte, contrôle du processus à l'autre bout, sans possibilité pour la
  fenêtre d'emprunter ses droits).
- **Nettoyage.** Les liens (jonctions, liens symboliques) ne sont jamais suivis. Sous
  Windows, chaque fichier est supprimé via un handle, après avoir vérifié l'emplacement
  réel du fichier ouvert : un dossier remplacé par un lien en cours de route ne peut pas
  rediriger la suppression.
- **Emplacement.** L'installateur place l'application dans `Program Files`, que seul un
  administrateur peut modifier : personne ne peut remplacer l'exe que Windows élèvera.
- **Programmes lancés.** Les outils Windows sont appelés par leur chemin complet dans
  `System32`, jamais par leur seul nom.
- **DLL.** Les DLL natives sont livrées à côté de l'exe, pas décompressées dans `%TEMP%`.
- **Dépendances.** Elles sont verrouillées (`packages.lock.json`) et contrôlées contre les
  vulnérabilités connues. Tous les analyseurs de sécurité .NET sont actifs. En CI, le
  moindre avertissement fait échouer la compilation.

## Compiler

Il faut le SDK .NET 10. La compilation et les tests portables fonctionnent aussi sous
Linux ; l'application et son installateur ne se construisent et ne tournent que sous
Windows.

```sh
dotnet test tests/DriverUpdater.Tests        # tests portables (Linux ou Windows)
dotnet test DriverUpdater.sln                # tous les tests (Windows seulement)
dotnet publish src/DriverUpdater -c Release -p:PublishProfile=win-x64
ISCC installer\DriverUpdater.iss             # installateur (Inno Setup 6)
```

## Pipeline (GitHub Actions)

`.github/workflows/build.yml` tourne sur Windows à chaque push, pull request et chaque
semaine (correctifs .NET, nouvelles vulnérabilités). Il restaure en mode verrouillé,
compile, lance tous les tests (y compris ceux qui interrogent le vrai Windows et un
aller-retour réel avec le module administrateur), publie, construit l'installateur, puis
l'installe et le désinstalle pour de vrai. Dependabot tient à jour les actions et les
paquets.

**Publier une version :** changez `<Version>` dans `src/DriverUpdater/DriverUpdater.csproj`,
committez, puis poussez un tag du même numéro :

```sh
git tag v0.4.0
git push origin v0.4.0
```

Le pipeline crée alors la release GitHub : installateur, zip portable, `SHA256SUMS.txt`
et attestation de provenance.

## Signature de code

Une application signée n'affiche plus l'avertissement SmartScreen, et Windows montre
l'éditeur dans sa demande d'autorisation administrateur. Deux possibilités, à choisir :

**SignPath Foundation (gratuit pour l'open source).** Le dépôt est sous licence MIT, ce
qui remplit leur condition de licence open source. Ensuite :

1. Demandez l'adhésion sur [signpath.org](https://signpath.org).
2. Dans SignPath, créez deux configurations d'artefact : `app` (signe `DriverUpdater.exe`
   à la racine de l'artefact) et `installer` (signe `DriverUpdater-Setup-*.exe`).
3. Dans GitHub (Settings → Secrets and variables → Actions), ajoutez le secret
   `SIGNPATH_API_TOKEN` et les variables `SIGNPATH_ORGANIZATION_ID`,
   `SIGNPATH_PROJECT_SLUG` et `SIGNPATH_SIGNING_POLICY_SLUG`.

**Votre propre certificat** (`.pfx`, payant) : ajoutez les secrets
`SIGNING_CERT_PFX_BASE64` (le fichier en base64, obtenu sous Windows avec
`[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx"))`) et
`SIGNING_CERT_PASSWORD`.

Sans l'un ou l'autre, le pipeline livre l'application non signée.

## Licence

MIT : voir [LICENSE](LICENSE).

## Code

| Fichier | Rôle |
|---|---|
| `src/DriverUpdater/Elevation.cs` | Lancement du module administrateur et canal privé avec la fenêtre |
| `src/DriverUpdater/AdminCommand.cs` | Commandes admises par le module administrateur, et vérification de leurs arguments |
| `src/DriverUpdater/AdminHelper.cs` | Exécution des commandes administrateur |
| `src/DriverUpdater/WindowsUpdateDrivers.cs` | Recherche, installation, masquage et historique via l'API COM de Windows Update |
| `src/DriverUpdater/SystemInfo.cs` | Pilotes installés, PC et BIOS, cartes graphiques, disques, batterie, points de restauration (WMI) |
| `src/DriverUpdater/WinGet.cs`, `WingetOutput.cs` | Mises à jour de logiciels via winget |
| `src/DriverUpdater/Repair.cs` | SFC et DISM |
| `src/DriverUpdater/WeeklyCheck.cs` | Tâche planifiée hebdomadaire et notification |
| `src/DriverUpdater/DriverBackup.cs` | Sauvegarde et restauration des pilotes (pnputil) |
| `src/DriverUpdater/TempCleaner.cs` | Nettoyage des fichiers temporaires, résistant aux liens |
| `src/DriverUpdater/UpdateCheck.cs` | Détection d'une nouvelle version sur GitHub |
| `src/DriverUpdater/Diagnostics.cs` | Rapport de diagnostic |
| `src/DriverUpdater/MainWindow.*` | Interface, un fichier par onglet |
| `installer/DriverUpdater.iss` | Installateur Inno Setup |
| `build/sign.ps1` | Signature avec votre propre certificat |
| `tests/DriverUpdater.Tests` | Tests portables : nettoyeur, commandes administrateur, winget, versions |
| `tests/DriverUpdater.WindowsTests` | Tests sur le vrai Windows : fenêtre, WMI, Windows Update, tâche planifiée, module administrateur |
