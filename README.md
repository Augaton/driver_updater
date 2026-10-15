# Driver Updater

Petite application Windows 10/11, dans l'esprit de Driver Booster, mais honnête :
elle ne propose que ce qui marche vraiment.

## Ce qu'elle fait

- **Analyse de tous les pilotes.** Le gros bouton de l'accueil compare chaque périphérique
  aux dernières versions de Windows Update et des fabricants, et rend un rapport : version
  installée, version disponible, source, et un verdict (à jour, mise à jour, aucun pilote,
  erreur). Tout ce qui est à mettre à jour arrive coché ; un bouton installe la sélection
  en une seule fois, sous une seule autorisation administrateur, après un point de
  restauration. Le même passage vérifie aussi logiciels, démarrage et disques.
- **Jeux.** Le pilote graphique comparé au dernier publié (les pilotes « Game Ready » de
  NVIDIA et AMD arrivent signés par Microsoft dans le catalogue) et l'outil du fabricant ;
  les composants que les jeux réclament (Visual C++ 2005 à 2022, DirectX 9, XNA, OpenAL,
  PhysX), détectés et installés via winget ; les réglages de jeu de Windows (Mode jeu,
  planification GPU). Pas de « Game Boost » : le Mode jeu de Windows fait déjà ce travail.
  Pour les jeux Steam installés, les conseils des communautés, et pour Garry's Mod le cfg
  multicœur conseillé, ajouté à `autoexec.cfg` et retiré d'un clic sans toucher à vos
  propres lignes.
- **PC de guerre.** Tous les réglages utiles pour jouer, regroupés sur un écran avec une
  jauge : mode d'alimentation performances, Mode jeu, pas d'enregistrement vidéo en
  arrière-plan, optimisations des jeux fenêtrés, planification GPU, précision du pointeur,
  et les réglages de confidentialité les plus suivis (ceux de WinUtil de Chris Titus,
  entre autres) : identifiant publicitaire, suggestions, Bing dans la recherche,
  historique des activités, télémétrie. Chacun s'annule exactement, valeur d'origine
  comprise ; « Tout optimiser » applique les autres en une fois. Ce qui ne se règle pas
  d'ici est signalé : écran réglé sous sa fréquence maximale, mémoire sans XMP/EXPO,
  disque système plein. Pas de « boosts » sans effet mesurable (réglages TCP, HPET,
  services coupés à l'aveugle, suppression d'Edge).
- **Pilotes.** Elle cherche les mises à jour de pilotes sur Windows Update, avec, en face
  de chacune, le pilote installé aujourd'hui. Elle installe ceux que vous cochez, un par un,
  après avoir créé un point de restauration système. Les pilotes « recommandés » sont
  cochés ; les « facultatifs » ne le sont pas, car Microsoft ne les conseille que pour
  corriger un problème précis. Un pilote refusé peut être masqué pour ne plus revenir.
- **Inventaire.** Chaque périphérique avec son pilote, sa date, son fournisseur, sa
  signature et son état, filtrable (à problème, sans pilote, plus de 3 ans, non signés).
  Il se met à jour tout seul quand on branche ou débranche un périphérique ; un nouveau
  périphérique sans pilote est signalé avec un bouton pour lui en chercher un. Un pilote
  qui pose problème revient à sa version précédente d'un clic, comme dans le Gestionnaire
  de périphériques.
- **Anciennes versions de pilotes.** Windows garde chaque version installée (près de 1 Go
  par mise à jour de pilote graphique). Celles qu'aucun périphérique n'utilise et qu'une
  version plus récente remplace sont proposées à la suppression ; Windows refuse de toute
  façon de supprimer un pilote utilisé.
- **Pilotes récents des fabricants.** NVIDIA, AMD, Intel, Realtek… publient leurs pilotes
  dans le catalogue Microsoft Update, signés par Microsoft, souvent avant que Windows
  Update ne les propose. L'application y cherche une version plus récente pour chaque
  périphérique, la télécharge depuis les serveurs de Microsoft, vérifie son empreinte et
  laisse Windows l'installer : il garde le pilote actuel s'il est plus adapté au PC
  (pilote adapté par le fabricant du portable, par exemple). Couvre les cartes PCI,
  l'audio, l'USB, le chipset (ACPI) et les pavés tactiles (HID), et cherche aussi un
  pilote de fabricant pour le graphique, le réseau, l'audio, le Bluetooth et les caméras
  qui tournent sur un pilote générique de Windows. On peut choisir la version à installer
  (l'avant-dernière, si la dernière pose problème). Les contrôleurs de stockage ne sont
  jamais touchés : un mauvais pilote à cet endroit empêche Windows de démarrer.
- **Vérification hebdomadaire.** Option : chaque mercredi, l'application cherche en
  silence et affiche une notification seulement s'il y a du nouveau.
- **BIOS et carte graphique.** Windows Update les livre avec retard, ou pas du tout :
  l'application affiche les versions installées et ouvre l'outil du fabricant (Lenovo,
  Dell, HP, ASUS, Acer, MSI, Gigabyte, ASRock ; NVIDIA, AMD, Intel).
- **Logiciels.** Mise à jour des logiciels installés via winget, le gestionnaire de
  logiciels intégré à Windows. Désinstallation par le désinstalleur du programme, puis
  recherche des dossiers laissés derrière lui, mis à la corbeille (donc récupérables).
  Le registre n'est pas « nettoyé » : ça ne fait rien gagner et c'est la première cause
  de casse de ce genre d'outil.
- **Démarrage.** Les programmes lancés avec Windows, activés ou désactivés comme dans le
  Gestionnaire des tâches (réversible), avec le temps que chacun fait perdre d'après les
  mesures que Windows enregistre lui-même à chaque démarrage, et le graphique de la durée
  des 30 derniers démarrages.
- **Performances.** Dix secondes d'observation, puis les applications classées par
  processeur, mémoire, disque et carte graphique, et un verdict en clair : ressource
  saturée et responsable, mode économie d'énergie, processeur bridé par la chaleur.
- **Historique.** Les pilotes installés par Windows Update, quand, et avec quel résultat.
- **Sécurité.** Antivirus actif et à jour, pare-feu actif, mises à jour de Windows en
  attente, lus dans Windows lui-même, avec un lien vers l'endroit où agir. Pas de « score ».
- **Points de restauration.** La liste, un bouton pour en créer un tout de suite, un autre
  pour ouvrir la restauration du système.
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
  sur GitHub, et l'installe d'un clic. L'application interroge pour cela l'API publique de
  GitHub à chaque démarrage ; elle n'envoie rien d'autre.

Interface sombre aux accents néon, barre latérale rangée par thèmes (Pilotes, Jeux,
Système, Logiciels), jauges circulaires pour les performances.

Pourquoi Windows Update et pas « la toute dernière version » : les pilotes de Windows
Update sont signés et validés par Microsoft pour votre matériel. Les versions plus
récentes publiées directement par les fabricants sont parfois des bêtas.

## Installer

Téléchargez `DriverUpdater-Setup-x.y.z.exe` depuis la page
[Releases](https://github.com/Augaton/driver_updater/releases). Il installe l'application
dans `C:\Program Files\Driver Updater` et l'ajoute au menu Démarrer. Le zip « portable »
fonctionne aussi.

Lancé alors que Driver Updater est déjà installé, l'installateur indique la version en
place et propose ce qui a du sens : mettre à jour, réparer (même version : tous les
fichiers sont réinstallés), revenir à une version plus ancienne (avec avertissement) ou
désinstaller. Le bouton « Modifier » de Windows (Paramètres → Applications) ouvre la même
page, grâce à une copie de l'installateur gardée dans `Driver Updater\setup`. Si la
vérification hebdomadaire était activée depuis une autre copie de l'application (le zip
portable, par exemple), elle est redirigée vers la copie installée.

Pour vérifier qu'un fichier a bien été produit par le pipeline de ce dépôt :

```sh
gh attestation verify DriverUpdater-Setup-x.y.z.exe --repo Augaton/driver_updater
```

## Sécurité

- **Pas de droits administrateur par défaut.** La fenêtre tourne avec les droits de
  l'utilisateur. Chaque action qui en a besoin (installer ou masquer un pilote, point de
  restauration, sauvegarde, `C:\Windows\Temp`, réparation, lecture de l'usure des
  disques, pilotes du catalogue, programmes au démarrage de tous les utilisateurs,
  restes dans Program Files, anciennes versions de pilotes) relance l'application en administrateur pour cette seule action : Windows
  demande l'autorisation, l'action se fait, le processus se termine. Aucun processus
  administrateur ne reste ouvert pour qu'un autre programme lui parle.
- **Seule la fenêtre de Driver Updater commande le module administrateur.** N'importe quel
  programme peut le lancer avec une commande valide et obtenir de Windows la question
  « Autoriser Driver Updater ? », à laquelle on répondrait oui. Le module vérifie donc que
  le processus à l'autre bout du canal est bien l'exe de Driver Updater, et refuse sinon.
- **Module administrateur.** Il n'accepte qu'une liste fermée de commandes, et vérifie
  leurs arguments : identifiants de mise à jour au format GUID, dossiers locaux en chemin
  complet, nom de canal généré par l'application. Il rend compte par un canal nommé privé
  (réservé au compte, contrôle du processus à l'autre bout, sans possibilité pour la
  fenêtre d'emprunter ses droits).
- **Nettoyage.** Les liens (jonctions, liens symboliques) ne sont jamais suivis. Sous
  Windows, chaque fichier est supprimé via un handle, après avoir vérifié l'emplacement
  réel du fichier ouvert : un dossier remplacé par un lien en cours de route ne peut pas
  rediriger la suppression.
- **Pilotes du catalogue.** Le lien de téléchargement est retrouvé par le module
  administrateur lui-même à partir de l'identifiant de la mise à jour, jamais transmis par
  la fenêtre, et n'est accepté que sur les serveurs de Microsoft en HTTPS, sans suivre de
  redirection. Le pilote est
  vérifié puis extrait dans un dossier que seuls SYSTEM et les administrateurs peuvent
  modifier, créé avec ces droits d'un seul coup, et `pnputil` refuse tout pilote non signé.
- **Mise à jour de l'application.** L'installateur n'est pris que dans les releases de ce
  dépôt, et n'est lancé que si son empreinte SHA-256 est celle publiée avec lui dans
  `SHA256SUMS.txt`.
- **Réglages « PC de guerre ».** Avant chaque modification, la valeur d'origine est
  gardée sous `HKCU\Software\Driver Updater\Tweaks` ; « Annuler » la remet telle quelle,
  ou supprime la valeur si elle n'existait pas. Le module administrateur n'accepte que les
  identifiants de réglages de sa propre liste.
- **Restes de désinstallation.** Seulement dans Program Files, ProgramData et AppData,
  jamais un dossier partagé par plusieurs programmes (Microsoft, Common Files…), jamais un
  lien, jamais le dossier de l'application ; le module administrateur revérifie chaque
  dossier avant de le mettre à la corbeille.
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
l'installe et le désinstalle pour de vrai. Le test de la fenêtre affiche chaque section et
en garde une capture : elles sont jointes à chaque exécution (artefact `screenshots`). Dependabot tient à jour les actions et les
paquets.

**Publier une version :** changez `<Version>` dans `src/DriverUpdater/DriverUpdater.csproj`,
committez, puis poussez un tag du même numéro :

```sh
git tag v0.7.1
git push origin v0.7.1
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

Le code de l'application est rangé par domaine, un dossier par espace de noms
(`DriverUpdater.Drivers`, `DriverUpdater.Maintenance`…). La logique qui ne dépend pas de
Windows est testée sous Linux comme sous Windows ; le reste l'est sur un vrai Windows par
le pipeline.

| Dossier de `src/DriverUpdater` | Contenu |
|---|---|
| `Admin/` | Élévation : lancement du module administrateur, canal privé avec la fenêtre, commandes admises et leur vérification, exécution |
| `Drivers/` | Windows Update, catalogue Microsoft Update, inventaire, détection des branchements, rapport de tous les pilotes, magasin de pilotes, sauvegarde, retour au pilote précédent, vérification hebdomadaire |
| `Maintenance/` | Matériel et santé (WMI), sécurité, points de restauration, performances, démarrage de Windows, programmes au démarrage, réparation (SFC, DISM), fichiers temporaires, diagnostic |
| `Tuning/` | « PC de guerre » : réglages réversibles et leur liste, vérifications du matériel, écrans, jeux Steam et leurs conseils |
| `Software/` | winget, programmes installés et désinstallation, restes, composants de jeu |
| `Common/` | Messages d'erreur, tailles, nouvelle version de l'application (détection, téléchargement vérifié) |
| `UI/` | La fenêtre, un fichier par section (`MainWindow.Home.cs`, `.Games.cs`…), la jauge |
| (racine) | `App.xaml` (thème néon), manifeste, icône, projet |

| Ailleurs | Contenu |
|---|---|
| `installer/DriverUpdater.iss` | Installateur Inno Setup : thème sombre, mise à jour, réparation, désinstallation |
| `installer/art/make-art.py` | Dessine l'icône et les images de l'installateur |
| `build/sign.ps1` | Signature avec votre propre certificat |
| `tests/DriverUpdater.Tests` | Tests portables : nettoyeur, commandes administrateur, catalogue, rapport, restes, démarrage, verdicts, composants, réglages et leur annulation, cfg de jeu, sécurité, mise à jour |
| `tests/DriverUpdater.WindowsTests` | Tests sur le vrai Windows : fenêtre et capture de chaque section, réglages dans le vrai registre, WMI, Windows Update, tâche planifiée, module administrateur, compteurs de performance, magasin de pilotes, catalogue en direct |
