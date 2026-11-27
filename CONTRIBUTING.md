# Contribuer à Driver Updater

Merci de vouloir aider. Ce guide dit comment signaler un problème, proposer une idée et soumettre
du code.

## Signaler un bug ou proposer une idée

Ouvre une [issue](https://github.com/Augaton/driver_updater/issues/new). Pour un bug, colle le
rapport de « Copier le diagnostic » (en bas de la fenêtre) : il donne la version de l'appli et de
Windows, le matériel concerné et le détail des erreurs. Dis aussi ce que tu faisais, et ce que tu
attendais.

Une faille de sécurité ne va pas dans une issue publique : suis [SECURITY.md](SECURITY.md).

## Préparer l'environnement

Il faut le [SDK .NET 10](https://dotnet.microsoft.com/download). L'appli ne se construit et ne
tourne que sous Windows 10 ou 11 en 64 bits ; les tests portables (`tests/DriverUpdater.Tests`)
passent aussi sous Linux. L'installateur demande [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```sh
dotnet build DriverUpdater.sln -c Release
dotnet run --project src/DriverUpdater
```

Pour compiler comme la CI, définis `CI=true` : la restauration se fait alors en mode verrouillé
(`packages.lock.json`), et chaque avertissement devient une erreur.

## Avant d'envoyer une pull request

La CI refuse une pull request qui ne passe pas ces vérifications :

```sh
dotnet build DriverUpdater.sln -c Release      # avec CI=true : aucun avertissement
dotnet test tests/DriverUpdater.Tests          # logique portable
dotnet test DriverUpdater.sln                  # tout : vrai Windows et module administrateur
```

Les tests du module administrateur font un vrai aller-retour avec lui : sur ton PC, ils ouvrent
des demandes d'autorisation. Pour vérifier seulement la fenêtre, lance son test, qui parcourt
chaque page sans rien changer et en garde une capture dans `screenshots/` :

```sh
dotnet test tests/DriverUpdater.WindowsTests --filter "FullyQualifiedName~Main_window_loads_and_every_section_renders"
```

Avec `DRIVERUPDATER_LANG=en`, il passe en anglais et vérifie qu'aucun texte français ne reste ; avec
`DRIVERUPDATER_THEME=light`, en thème clair. Il échoue sur tout texte qui ne tient pas un
contraste de 4,5:1 (3:1 pour un contrôle désactivé), et sur tout contrôle sans nom pour les
lecteurs d'écran.

Et à la main : lance l'appli, et vérifie le changement dans la vraie fenêtre.

## Règles du projet

- **Rien qui ne marche pas vraiment.** Un réglage n'entre que s'il a un effet mesuré ou documenté
  par Microsoft, garde la valeur d'origine et s'annule exactement. La page dit ce qu'il fait,
  et ce qu'il ne fait pas.
- **Le module administrateur reste fermé.** Une nouvelle action administrateur est une commande
  de plus dans `Admin/AdminCommand.cs`, avec ses arguments vérifiés, et vérifiés encore côté
  module avant d'agir. Rien qu'un programme de l'utilisateur pourrait modifier n'est exécuté en
  administrateur ; un outil de Windows est appelé par son chemin dans `System32`.
- **Le code reste simple.** Avant d'ajouter une fonction, cherche si le dépôt ou .NET l'offre
  déjà. Pas de nouvelle dépendance pour quelques lignes, pas d'abstraction pour un seul usage.
- **Une logique non triviale a son test** : une branche, une boucle, une règle. La logique qui ne
  dépend pas de Windows va dans un fichier lié aux tests portables (voir
  `tests/DriverUpdater.Tests/DriverUpdater.Tests.csproj`).
- **L'interface parle français**, en phrases courtes, et vouvoie. Les couleurs viennent de la
  palette (`App.xaml.cs`), jamais d'une valeur en dur. Les commentaires du code sont en anglais.

## Textes et traduction

Le français est la source, et chaque texte est sa propre clé : un nouveau texte s'écrit
`L.T("…")`, ou `L.F("… {0} …", valeur)` s'il contient une valeur. Les textes du XAML sont traduits
à la création de la fenêtre ; dans un modèle (`DataTemplate`), ils s'écrivent
`{local:Translate '…'}`. Leur anglais va dans `src/DriverUpdater/Localization/en.json`. Le test
`LocalizationTests` liste les textes sans traduction et les traductions qui ne servent plus.

## Messages de commit

En français, avec un titre court qui dit ce qui change pour l'utilisateur, et un corps qui dit
pourquoi, en liste quand il y a plusieurs changements. Par exemple :

```
Restauration d'une sauvegarde : le dossier vérifié, les pilotes listés

- Seul un dossier fait comme une sauvegarde est accepté.
- Le module administrateur le copie dans un dossier protégé avant d'installer.
```

## Publication

La CI (`.github/workflows/build.yml`) tourne sur Windows à chaque push, chaque pull request et
chaque semaine, pour les correctifs de .NET et les vulnérabilités nouvellement publiées. Elle
compile, lance tous les tests, publie l'appli, construit l'installateur, puis l'installe et le
désinstalle pour de vrai. Les captures de chaque page sont jointes à chaque exécution (artefact
`screenshots`).

Pour publier une version, change `<Version>` dans `src/DriverUpdater/DriverUpdater.csproj`,
committe, puis pousse un tag du même numéro :

```sh
git tag v1.2.3
git push origin v1.2.3
```

La CI crée alors la Release : installateur, zip portable, `SHA256SUMS.txt` et attestation de
provenance.

## Signature de code

Une appli signée n'affiche plus l'avertissement SmartScreen, et Windows montre l'éditeur dans sa
demande d'autorisation administrateur. Sans signature configurée, la CI livre l'appli non signée.
Deux possibilités, à choisir :

**SignPath Foundation, gratuit pour l'open source.** Le dépôt est sous licence MIT, ce qui remplit
leur condition.

1. Demande l'adhésion sur [signpath.org](https://signpath.org).
2. Dans SignPath, crée deux configurations d'artefact : `app` (signe `DriverUpdater.exe` à la
   racine de l'artefact) et `installer` (signe `DriverUpdater-Setup-*.exe`).
3. Dans GitHub (Settings, Secrets and variables, Actions), ajoute le secret `SIGNPATH_API_TOKEN`
   et les variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` et
   `SIGNPATH_SIGNING_POLICY_SLUG`.

**Ton propre certificat** (`.pfx`, payant) : ajoute les secrets `SIGNING_CERT_PFX_BASE64` (le
fichier en base64, obtenu sous Windows avec
`[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx"))`) et `SIGNING_CERT_PASSWORD`.

## Licence

En contribuant, tu acceptes que ta contribution soit publiée sous la [licence MIT](LICENSE) du
projet.
