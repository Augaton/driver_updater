# Driver Updater

Petite application Windows 10/11, dans l'esprit de Driver Booster, mais honnête :
elle ne propose que ce qui marche vraiment. En français ou en anglais.

## Ce qu'elle fait

Une page par fonction, rangées dans une barre latérale en quatre branches repliables,
plus la page Paramètres (au clavier aussi : flèches haut et bas, gauche et droite pour
replier et déplier).

**Accueil.** Le gros bouton compare chaque périphérique à trois sources : Windows Update,
le catalogue Microsoft Update (les pilotes des fabricants) et, pour une carte NVIDIA, le
site de NVIDIA. Le rapport donne pour chacun la version installée, la version disponible,
sa source et un verdict (à jour, mise à jour, aucun pilote, erreur). Ce qui est à mettre à
jour arrive coché et s'installe en une fois, sous une seule autorisation administrateur,
après un point de restauration. Le même passage vérifie logiciels, démarrage, sécurité et
disques.

### Pilotes

- **Windows Update.** Les mises à jour de pilotes, en face du pilote installé. Les
  « recommandées » sont cochées, les « facultatives » non (Microsoft ne les conseille que
  pour un problème précis). Un pilote refusé peut être masqué.
- **Fabricants.** Pour chaque périphérique, une version plus récente dans le catalogue
  Microsoft Update, signée par Microsoft, téléchargée depuis ses serveurs et vérifiée.
  Windows garde le pilote actuel s'il est plus adapté au PC. On peut choisir la version (la
  précédente, si la dernière pose problème). Les pilotes de stockage ne sont jamais
  installés ainsi, même s'ils sont dans le même paquet : un mauvais pilote à cet endroit
  empêche Windows de démarrer.
- **Carte graphique.** Le pilote qui compte le plus en jeu. Pour NVIDIA, le dernier Game
  Ready est demandé à NVIDIA directement : le catalogue a souvent plusieurs semaines de
  retard et saute des versions. Une seule autorisation : point de restauration, puis
  installateur officiel téléchargé depuis les serveurs de NVIDIA et lancé seulement si
  Windows valide sa signature et que le signataire est NVIDIA. Game Ready ou Studio, au
  choix dans Paramètres. Pour AMD et Intel, l'outil du fabricant.
- **Carte mère.** Le BIOS installé et l'outil du fabricant pour le mettre à jour.
- **Inventaire.** Chaque périphérique, son pilote, sa date, sa signature et son état,
  filtrable ; mis à jour tout seul quand on branche ou débranche quelque chose. « Revenir
  au pilote précédent » fait comme le Gestionnaire de périphériques.
- **Périphériques en erreur.** Ce que signale le Gestionnaire de périphériques, avec le sens
  de chaque code d'erreur.
- **Anciennes versions.** Windows garde chaque version installée (près de 1 Go par pilote
  graphique). Celles qu'aucun périphérique n'utilise peuvent partir ; la précédente de
  chaque pilote est gardée, pour pouvoir y revenir.
- **Sauvegarde.** Tous les pilotes des fabricants copiés dans un dossier, et réinstallés
  en une fois après une réinstallation de Windows.
- **Historique.** Ce que Windows Update a installé, quand, et avec quel résultat.

### Jeux

- **PC de guerre.** Une jauge et tous les réglages qui font gagner en jeu, chacun avec ce
  qui le justifie : Mode jeu (activé par défaut, indispensable aux Ryzen X3D),
  enregistrement en arrière-plan coupé (jusqu'à 7 % d'images), optimisations des jeux
  fenêtrés (recommandées par Microsoft), planification GPU (nécessaire au DLSS Frame
  Generation). « Tout optimiser » les applique en une fois ; chacun s'annule exactement,
  valeur d'origine comprise. À part, sans gain d'images et dit tel quel : la précision du
  pointeur et « Fin de tâche ». Et ce qu'aucun réglage ne corrige d'ici, vérifié sur le
  matériel :
  - écran réglé sous sa meilleure fréquence : passé à la bonne d'un clic, après un test de
    Windows ; si l'image ne revient pas, l'ancienne fréquence revient seule au bout de
    15 secondes ;
  - écran branché sur la carte mère alors qu'il y a une carte graphique ;
  - sur un portable à deux puces graphiques, jeux Steam laissés au choix de Windows, qui
    peut les lancer sur la moins puissante : forcés d'un clic sur la carte dédiée, par le
    réglage de Windows lui-même, et annulable ;
  - mémoire sans profil XMP/EXPO (lu aussi dans la référence des barrettes, quand le
    BIOS ne le dit pas), ou une seule barrette ;
  - mode d'alimentation qui bride le processeur ; Ryzen X3D sans son pilote de cache 3D,
    sans Game Bar ou hors du mode Équilibré ;
  - jeux Steam installés sur un disque dur ; disque système plein.

  Laissés de côté parce que rien ne les mesure ou qu'ils nuisent : réglages réseau
  (Nagle, MMCSS), HPET et minuterie, parcage des cœurs, services coupés, MPO, et le plan
  « Performances optimales » (aucun gain mesuré, et il casse le cache 3D des Ryzen X3D).
  Ceux qui l'avaient appliqué avec une version précédente gardent le bouton « Annuler ».
  Ceux qu'un autre outil a laissés se défont dans la page Faux boosts.
- **Composants de jeu.** Visual C++ 2005 à 2022, DirectX 9, XNA, OpenAL, PhysX, détectés
  et installés via winget.
- **Conseils par jeu.** Pour les jeux Steam installés, les réglages des communautés ; pour
  Garry's Mod, le cfg multicœur, ajouté à `autoexec.cfg` et retiré d'un clic sans toucher
  à vos propres lignes.

### Système

- **Performances.** Dix secondes d'observation, les applications classées par processeur,
  mémoire, disque et carte graphique, et un verdict en clair.
- **Démarrage.** Les programmes lancés avec Windows, activés ou désactivés comme dans le
  Gestionnaire des tâches, avec le temps que chacun fait perdre d'après les mesures de
  Windows, le graphique des 30 derniers démarrages et, après vos changements, le temps
  gagné d'après les démarrages suivants.
- **Confidentialité.** Les réglages les plus suivis (dont ceux de WinUtil de Chris Titus) :
  identifiant publicitaire, expériences personnalisées, applications suggérées, Bing dans
  la recherche, historique des activités, télémétrie. Ils ne rendent pas le PC plus
  rapide, et c'est dit ; chacun s'annule exactement.
- **Faux boosts.** Ce que les « optimiseurs » et les guides laissent sur un PC et qui le
  ralentit ou l'expose : minuterie forcée au démarrage, fichier d'échange supprimé,
  parcage des cœurs coupé (indispensable aux Ryzen X3D), protections Spectre et Meltdown
  désactivées, UAC coupé, Windows Update ou Defender bloqués par stratégie, services
  utiles désactivés, IPv6 coupé… Chaque point s'appuie sur la documentation de Microsoft
  ou sur des mesures publiées. « Rétablir » remet la valeur de Windows et garde l'ancienne
  pour revenir en arrière. Ce qui n'est qu'un choix défendable (ou sans effet prouvé) est
  seulement signalé, et sur un PC géré par une entreprise, c'est l'affaire de son service
  informatique.
- **Sécurité.** Antivirus actif et à jour, pare-feu actif, mises à jour de Windows en
  attente, avec un lien vers l'endroit où agir. Pas de « score ».
- **Points de restauration.** La liste, en créer un, ouvrir la restauration du système.
- **Disques et batterie.** État, usure et température des disques, santé de la batterie.
- **Nettoyage.** Fichiers temporaires de plus de 2 jours, anciennes versions des composants
  de Windows (DISM), mise en veille prolongée. Pas de « nettoyage du registre ».
- **Réparer Windows.** SFC et DISM, les outils de Microsoft.

### Logiciels

- **Mises à jour.** Les logiciels installés, mis à jour via winget.
- **Désinstaller.** Le désinstalleur du programme, puis les dossiers laissés derrière lui,
  mis à la corbeille. Un dossier qu'un autre programme installé utilise encore n'est
  jamais proposé.

**Paramètres.** La vérification hebdomadaire, silencieuse et sans droits administrateur :
les pilotes de Windows Update et, au choix, un nouveau pilote NVIDIA et les mises à jour
de logiciels ; une notification seulement s'il y a du nouveau, une seule fois pour chaque
nouveauté. Les pilotes NVIDIA Game Ready ou Studio. La langue : français, anglais, ou
celle de Windows (par défaut).

**Et aussi.** « Copier le diagnostic » prépare un rapport texte pour signaler un bug. Un
bandeau prévient quand une nouvelle version est publiée sur GitHub et l'installe d'un clic.

## Installer

Téléchargez `DriverUpdater-Setup-x.y.z.exe` depuis la page
[Releases](https://github.com/Augaton/driver_updater/releases). Il installe l'application
dans `C:\Program Files\Driver Updater` et l'ajoute au menu Démarrer ; il parle français sur
un Windows français, anglais sinon. Le zip « portable »
fonctionne aussi, mais ses actions administrateur ne s'ouvrent que s'il est placé dans un
dossier que seuls les administrateurs peuvent modifier (voir Sécurité).

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
- **Jamais depuis un dossier modifiable par tous.** Le module administrateur exécute les
  fichiers de l'application : depuis un dossier où n'importe quel programme peut écrire (un
  zip portable dans Téléchargements), un fichier pourrait être remplacé avant que vous
  n'acceptiez. La fenêtre refuse alors les actions administrateur et dit pourquoi.
- **Comptes standard.** Sur un compte sans droits, Windows demande le mot de passe d'un
  administrateur et le module tourne sous ce compte-là : le canal l'accepte, et vérifie
  toujours quel processus s'y connecte.
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
- **Faux boosts.** Mêmes garanties : l'ancienne valeur est gardée avant d'être rétablie.
  Remettre la minuterie de démarrage passe par `bcdedit` : BitLocker est d'abord suspendu
  pour un seul redémarrage, sans quoi il demanderait la clé de récupération.
- **Fréquence d'écran.** Windows teste le mode avant qu'il soit appliqué ; sans réponse,
  l'ancien revient au bout de 15 secondes, et il n'est enregistré qu'une fois gardé.
- **Pilotes NVIDIA.** Demandés au service qu'utilise l'application de NVIDIA ; une réponse
  qui ne ressemble pas à ce qui est attendu est ignorée. La fenêtre ne transmet que le
  numéro de la carte : le module administrateur redemande lui-même la version à NVIDIA, la
  télécharge depuis les serveurs de NVIDIA seulement (HTTPS, révocation des certificats
  vérifiée, aucune redirection suivie, abandon si le serveur cesse d'envoyer) dans un
  dossier où seuls les administrateurs peuvent écrire (rien ne peut être déposé à côté de
  l'installateur), Windows vérifie sa signature sur le fichier ouvert en lecture seule, et
  il n'est lancé que si le signataire est NVIDIA.
- **Sauvegarde des pilotes.** Le dossier de sauvegarde est créé neuf (jamais un lien posé à
  sa place), réservé aux administrateurs pendant que `pnputil` le remplit, puis il reprend
  les droits du dossier qui le contient.
- **Restes de désinstallation.** Seulement dans Program Files, ProgramData et AppData,
  jamais un dossier partagé par plusieurs programmes (Microsoft, Common Files…) ou encore
  utilisé par un programme installé, jamais un lien, jamais le dossier de l'application. Le
  module administrateur ne s'occupe que de Program Files et ProgramData : il revérifie
  chaque dossier en gardant ouverts tous ceux qui mènent à lui, pour qu'aucun ne puisse être
  remplacé par un lien entre la vérification et le déplacement. Si un dossier est trop
  gros pour la corbeille, Windows demande avant de le supprimer pour de bon.
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

**Textes.** Le français est la source, et chaque texte est sa propre clé : un nouveau texte
s'écrit `L.T("…")`, ou `L.F("… {0} …", valeur)` s'il contient une valeur ; les textes du
XAML sont traduits à l'affichage. Son anglais va dans
`src/DriverUpdater/Localization/en.json`. Les tests portables (`LocalizationTests`)
listent les textes sans traduction et les traductions qui ne servent plus.

## Pipeline (GitHub Actions)

`.github/workflows/build.yml` tourne sur Windows à chaque push, pull request et chaque
semaine (correctifs .NET, nouvelles vulnérabilités). Il restaure en mode verrouillé,
compile, lance tous les tests (y compris ceux qui interrogent le vrai Windows et un
aller-retour réel avec le module administrateur), publie, construit l'installateur, puis
l'installe et le désinstalle pour de vrai. Le test de la fenêtre la parcourt comme un
utilisateur (menu, branches, tuiles), vérifie que chaque contrôle a un nom pour les lecteurs
d'écran, et garde une capture de chaque page, en français puis en anglais : elles sont
jointes à chaque exécution (artefact `screenshots`). Dependabot tient à jour les actions et
les paquets.

**Publier une version :** changez `<Version>` dans `src/DriverUpdater/DriverUpdater.csproj`,
committez, puis poussez un tag du même numéro :

```sh
git tag v0.9.0
git push origin v0.9.0
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
| `Tuning/` | « PC de guerre » : réglages réversibles et leur liste, faux boosts et leur correction, vérifications du matériel et leurs règles, écrans et leur fréquence, carte graphique des jeux sur portable, jeux Steam et leurs conseils |
| `Software/` | winget, programmes installés et désinstallation, restes, composants de jeu |
| `Common/` | Textes et langue (`L.cs`), paramètres, messages d'erreur, tailles, nouvelle version de l'application, signatures Authenticode, opérations sur fichiers sûres en administrateur |
| `Localization/` | `en.json` : l'anglais de chaque texte français |
| `UI/` | La fenêtre, son menu en arbre (`MainWindow.Nav.cs`), un fichier par domaine (`MainWindow.Home.cs`, `.Games.cs`…), l'en-tête commun des pages, la jauge, la traduction des textes du XAML, la question « garder cet affichage ? » |
| (racine) | `App.xaml` (thème néon), manifeste, icône, projet |

| Ailleurs | Contenu |
|---|---|
| `installer/DriverUpdater.iss` | Installateur Inno Setup, en français et en anglais : thème sombre, mise à jour, réparation, désinstallation |
| `installer/art/make-art.py` | Dessine l'icône et les images de l'installateur |
| `build/sign.ps1` | Signature avec votre propre certificat |
| `tests/DriverUpdater.Tests` | Tests portables : nettoyeur, commandes administrateur, catalogue, rapport, restes, démarrage, verdicts, composants, réglages et leur annulation, cfg de jeu, sécurité, mise à jour, règles du matériel (XMP, écrans), réponses de NVIDIA (Game Ready et Studio), faux boosts, carte graphique des portables, vérification hebdomadaire, temps de démarrage gagné, traduction complète |
| `tests/DriverUpdater.WindowsTests` | Tests sur le vrai Windows : fenêtre parcourue comme un utilisateur, accessibilité, capture de chaque section en français et en anglais, réglages dans le vrai registre, signatures Authenticode, service de NVIDIA, WMI, Windows Update, tâche planifiée, module administrateur, compteurs de performance, magasin de pilotes, catalogue en direct |
