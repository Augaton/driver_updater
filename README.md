# Driver Updater

Petite application Windows 10/11, dans l'esprit de Driver Booster, mais honnête :
elle ne propose que ce qui marche vraiment.

## Ce qu'elle fait

- **Pilotes.** Elle cherche les mises à jour de pilotes sur Windows Update, avec, en face
  de chacune, le pilote installé aujourd'hui. Elle installe ceux que vous cochez, un par un,
  après avoir créé un point de restauration système. Les pilotes « recommandés » sont
  cochés ; les « facultatifs » ne le sont pas, car Microsoft ne les conseille que pour
  corriger un problème précis.
- **Périphériques en erreur.** Elle liste ceux que le Gestionnaire de périphériques signale
  (pilote manquant, appareil qui ne démarre pas…).
- **Disques.** Elle affiche l'état de santé rapporté par Windows.
- **Fichiers temporaires.** Elle supprime les fichiers de plus de 2 jours dans
  `AppData\Local\Temp` et `C:\Windows\Temp`.
- **Raccourcis** vers les réglages Windows utiles : démarrage, stockage, alimentation,
  historique des mises à jour, restauration du système.

Pourquoi Windows Update et pas « la toute dernière version » : les pilotes de Windows
Update sont signés et validés par Microsoft pour votre matériel. Les versions plus
récentes publiées directement par les fabricants sont parfois des bêtas ; pour la carte
graphique, passez plutôt par l'application du fabricant (NVIDIA App, AMD Adrenalin,
Intel Graphics Software).

## Sécurité

L'application tourne en administrateur. Elle part donc du principe qu'un autre programme
pourrait essayer de la détourner.

- **Nettoyage.** Les liens (jonctions, liens symboliques) ne sont jamais suivis. Sous
  Windows, chaque fichier est supprimé via un handle, après avoir vérifié l'emplacement
  réel du fichier ouvert : un dossier remplacé par un lien en cours de route ne peut pas
  rediriger la suppression. Les dossiers à nettoyer sont calculés depuis le profil, pas
  depuis `%TEMP%`, qu'un programme peut faire pointer n'importe où.
- **Programmes lancés.** Ils sont appelés par leur chemin complet dans `System32`, jamais
  par leur seul nom.
- **DLL.** Les DLL natives sont livrées à côté de l'exe, pas décompressées dans `%TEMP%`.
  Placez le dossier dans un endroit que seul un administrateur peut modifier, par exemple
  `C:\Program Files\Driver Updater`.
- **Dépendances.** Elles sont verrouillées (`packages.lock.json`) et contrôlées contre les
  vulnérabilités connues. Tous les analyseurs de sécurité .NET sont actifs. En CI, le
  moindre avertissement fait échouer la compilation.

## Compiler

Il faut le SDK .NET 10. La compilation et les tests portables fonctionnent aussi sous
Linux ; l'application, elle, ne tourne que sous Windows.

```sh
dotnet test tests/DriverUpdater.Tests        # tests portables (Linux ou Windows)
dotnet test DriverUpdater.sln                # tous les tests (Windows seulement)
dotnet publish src/DriverUpdater -c Release -p:PublishProfile=win-x64
```

Le dossier `publish/` contient l'exe et ses 5 DLL natives. Il est autonome : inutile
d'installer .NET. Le runtime .NET y est figé : recompilez après chaque correctif .NET
(le pipeline le fait chaque semaine). L'exe n'est pas signé, donc SmartScreen affiche un
avertissement au premier lancement.

## Pipeline (GitHub Actions)

`.github/workflows/build.yml` tourne sur Windows à chaque push, pull request et chaque
semaine. Il restaure en mode verrouillé, compile, lance tous les tests (y compris ceux qui
interrogent le vrai Windows), publie, puis met en ligne le dossier `publish/` avec ses
empreintes SHA-256. Dependabot tient à jour les actions et les paquets.

## Code

| Fichier | Rôle |
|---|---|
| `src/DriverUpdater/WindowsUpdateDrivers.cs` | Recherche et installation via l'API COM de Windows Update |
| `src/DriverUpdater/SystemInfo.cs` | Pilotes installés, périphériques, disques, points de restauration (WMI) |
| `src/DriverUpdater/TempCleaner.cs` | Nettoyage des fichiers temporaires, résistant aux liens |
| `src/DriverUpdater/MainWindow.xaml(.cs)` | Interface |
| `tests/DriverUpdater.Tests` | Tests du nettoyeur (dont l'attaque par lien, sous Windows) |
| `tests/DriverUpdater.WindowsTests` | Tests de fumée sur le vrai Windows (fenêtre, WMI, Windows Update) |
