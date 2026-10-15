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
  silence et affiche une notification seulement s'il y a du nouveau. Elle n'est
  disponible qu'une fois l'application installée (voir Sécurité).
- **Carte graphique.** Windows Update livre ces pilotes avec retard : l'application
  affiche le pilote installé et ouvre l'outil du fabricant (NVIDIA App, AMD Software,
  Assistant Intel).
- **Périphériques en erreur.** Ceux que le Gestionnaire de périphériques signale.
- **Santé.** État des disques, usure et température des SSD, état de la batterie d'un
  portable.
- **Sauvegarde des pilotes.** Copie tous les pilotes des fabricants dans un dossier, et
  les réinstalle en une fois après une réinstallation de Windows.
- **Fichiers temporaires.** Suppression des fichiers de plus de 2 jours dans
  `AppData\Local\Temp` et `C:\Windows\Temp`.
- **Raccourcis** vers les réglages Windows utiles : démarrage, stockage, alimentation,
  historique des mises à jour, restauration du système.

L'interface suit le thème de Windows 11, clair ou sombre.

Pourquoi Windows Update et pas « la toute dernière version » : les pilotes de Windows
Update sont signés et validés par Microsoft pour votre matériel. Les versions plus
récentes publiées directement par les fabricants sont parfois des bêtas.

## Installer

Téléchargez `DriverUpdater-Setup-x.y.z.exe` depuis la page
[Releases](https://github.com/Augaton/driver_updater/releases). Il installe l'application
dans `C:\Program Files\Driver Updater` et l'ajoute au menu Démarrer. Le zip « portable »
fonctionne aussi, mais sans la vérification hebdomadaire.

Pour vérifier qu'un fichier a bien été produit par le pipeline de ce dépôt :

```sh
gh attestation verify DriverUpdater-Setup-x.y.z.exe --repo Augaton/driver_updater
```

## Sécurité

L'application tourne en administrateur. Elle part donc du principe qu'un autre programme
pourrait essayer de la détourner.

- **Emplacement.** L'installateur place l'application dans `Program Files`, que seul un
  administrateur peut modifier. Lancée depuis Téléchargements, n'importe quel programme
  pourrait glisser une DLL piégée à côté de l'exe. C'est aussi pourquoi la vérification
  hebdomadaire, qui démarre l'application avec tous les droits sans rien demander, refuse
  de s'activer ailleurs.
- **Nettoyage.** Les liens (jonctions, liens symboliques) ne sont jamais suivis. Sous
  Windows, chaque fichier est supprimé via un handle, après avoir vérifié l'emplacement
  réel du fichier ouvert : un dossier remplacé par un lien en cours de route ne peut pas
  rediriger la suppression. Les dossiers à nettoyer sont calculés depuis le profil, pas
  depuis `%TEMP%`, qu'un programme peut faire pointer n'importe où.
- **État de la vérification hebdomadaire.** Il est rangé dans `HKLM`, modifiable par les
  seuls administrateurs, et non dans un fichier du profil qu'un lien pourrait détourner.
- **Programmes et sites lancés.** Les outils Windows sont appelés par leur chemin complet
  dans `System32`, jamais par leur seul nom. Les sites des fabricants s'ouvrent via
  l'Explorateur, donc dans un navigateur sans droits administrateur.
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
compile, lance tous les tests (y compris ceux qui interrogent le vrai Windows), publie,
construit l'installateur, puis l'installe et le désinstalle pour de vrai. Dependabot tient
à jour les actions et les paquets.

**Publier une version :** changez `<Version>` dans `src/DriverUpdater/DriverUpdater.csproj`,
committez, puis poussez un tag du même numéro :

```sh
git tag v0.3.0
git push origin v0.3.0
```

Le pipeline crée alors la release GitHub : installateur, zip portable, `SHA256SUMS.txt`
et attestation de provenance.

**Signer l'exe** (supprime l'avertissement SmartScreen) : achetez un certificat de
signature de code au format `.pfx`, puis ajoutez deux secrets au dépôt (Settings →
Secrets and variables → Actions) :

- `SIGNING_CERT_PFX_BASE64` : le fichier en base64, obtenu sous Windows avec
  `[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx"))` ;
- `SIGNING_CERT_PASSWORD` : son mot de passe.

Le pipeline signe alors l'application et l'installateur. Sans ces secrets, il les livre
non signés.

## Code

| Fichier | Rôle |
|---|---|
| `src/DriverUpdater/WindowsUpdateDrivers.cs` | Recherche, installation et masquage via l'API COM de Windows Update |
| `src/DriverUpdater/SystemInfo.cs` | Pilotes installés, périphériques, cartes graphiques, disques, batterie, points de restauration (WMI) |
| `src/DriverUpdater/WeeklyCheck.cs` | Tâche planifiée hebdomadaire et notification |
| `src/DriverUpdater/DriverBackup.cs` | Sauvegarde et restauration des pilotes (pnputil) |
| `src/DriverUpdater/TempCleaner.cs` | Nettoyage des fichiers temporaires, résistant aux liens |
| `src/DriverUpdater/MainWindow.xaml(.cs)` | Interface |
| `installer/DriverUpdater.iss` | Installateur Inno Setup |
| `build/sign.ps1` | Signature de code, appelée par le pipeline |
| `tests/DriverUpdater.Tests` | Tests du nettoyeur (dont l'attaque par lien, sous Windows) |
| `tests/DriverUpdater.WindowsTests` | Tests de fumée sur le vrai Windows (fenêtre, WMI, Windows Update, tâche planifiée) |
