# Sécurité

Driver Updater installe des pilotes, change des réglages de Windows et lance des installateurs,
avec ton autorisation administrateur. Une faille dans l'appli peut donc toucher tout le système :
merci de la signaler en privé.

## Signaler une faille

Ne l'écris pas dans une issue publique. Utilise le signalement privé de GitHub : onglet
**Security** du dépôt, puis **Report a vulnerability**. Décris le problème, comment le reproduire,
et la version concernée (en bas de la barre latérale de l'appli).

Tu recevras une réponse dès que possible. Le correctif est publié dans une nouvelle Release, puis
la faille est décrite publiquement, avec ton nom si tu le souhaites.

## Versions suivies

Seule la dernière Release reçoit des correctifs : l'appli signale elle-même qu'une version plus
récente existe, et l'installe d'un clic.

## Ce que le projet fait déjà

**Les droits administrateur**

- **Pas de droits administrateur par défaut.** La fenêtre tourne avec les droits de
  l'utilisateur. Chaque action qui en a besoin (installer ou masquer un pilote, point de
  restauration, sauvegarde, `C:\Windows\Temp`, réparation, usure des disques, pilotes du
  catalogue, programmes au démarrage de tous les utilisateurs, restes dans Program Files,
  anciennes versions de pilotes) relance l'appli en administrateur pour cette seule action :
  Windows demande l'autorisation, l'action se fait, le processus se termine. Aucun processus
  administrateur ne reste ouvert pour qu'un autre programme lui parle.
- **Jamais depuis un dossier modifiable par tous.** Le module administrateur exécute les fichiers
  de l'appli : depuis un dossier où n'importe quel programme peut écrire (un zip portable dans
  Téléchargements), un fichier pourrait être remplacé avant que tu acceptes. La fenêtre refuse
  alors les actions administrateur et dit pourquoi. L'installateur place l'appli dans
  `Program Files`, que seul un administrateur peut modifier.
- **Seule la fenêtre de Driver Updater commande le module administrateur.** N'importe quel
  programme peut le lancer avec une commande valide et obtenir de Windows la question
  « Autoriser Driver Updater ? », à laquelle on répondrait oui. Le module vérifie donc que le
  processus à l'autre bout du canal est bien l'exe de Driver Updater, et refuse sinon. La fenêtre
  vérifie de son côté que c'est bien le module qu'elle a lancé, et le module n'agit qu'après son
  feu vert.
- **Une liste fermée de commandes.** Le module n'accepte que les commandes qu'il connaît, et
  vérifie leurs arguments : identifiants de mise à jour au format GUID, dossiers locaux en chemin
  complet, noms de paquets de pilotes, identifiants de réglages de sa propre liste, tâches
  planifiées hors de celles de Windows, nom de canal généré par l'appli. Il rend compte par un
  canal nommé privé, réservé au compte, sans possibilité pour la fenêtre d'emprunter ses droits.
- **Comptes standard.** Sur un compte sans droits, Windows demande le mot de passe d'un
  administrateur et le module tourne sous ce compte-là : le canal l'accepte, et vérifie toujours
  quel processus s'y connecte.

**Ce qui est téléchargé et installé**

- **Pilotes du catalogue.** Le lien de téléchargement est retrouvé par le module lui-même à partir
  de l'identifiant de la mise à jour, jamais transmis par la fenêtre, et n'est accepté que sur les
  serveurs de Microsoft en HTTPS, sans suivre de redirection. Le pilote est vérifié (empreinte)
  puis extrait dans un dossier que seuls SYSTEM et les administrateurs peuvent modifier, créé avec
  ces droits d'un seul coup ; `pnputil` refuse tout pilote non signé.
- **Pilotes NVIDIA.** La fenêtre ne transmet que le numéro de la carte et le choix Game Ready ou
  Studio : le module redemande lui-même la version à NVIDIA, la télécharge depuis les serveurs de
  NVIDIA seulement (HTTPS, révocation des certificats vérifiée, aucune redirection suivie) dans un
  dossier où seuls les administrateurs peuvent écrire. Windows vérifie sa signature sur le fichier
  ouvert en lecture seule, et il n'est lancé que si le signataire est NVIDIA.
- **Mise à jour de l'appli.** L'installateur n'est pris que dans les Releases de ce dépôt, et
  n'est lancé que si son empreinte SHA-256 est celle publiée avec lui dans `SHA256SUMS.txt`.
- **Sauvegarde et restauration des pilotes.** Le dossier de sauvegarde est créé neuf (jamais un
  lien posé à sa place), réservé aux administrateurs pendant que `pnputil` le remplit, puis il
  reprend les droits du dossier qui le contient. À la restauration, seul un dossier fait comme une
  sauvegarde (un sous-dossier `oemNN.inf` par pilote, aucun lien) est accepté, vérifié par la
  fenêtre puis par le module, et copié dans un dossier réservé aux administrateurs avant que
  `pnputil` l'installe.

**Fichiers et programmes**

- **Nettoyage.** Les liens (jonctions, liens symboliques) ne sont jamais suivis. Chaque fichier
  est supprimé via un handle, après avoir vérifié l'emplacement réel du fichier ouvert : un dossier
  remplacé par un lien en cours de route ne peut pas rediriger la suppression.
- **Restes de désinstallation.** Seulement dans Program Files, ProgramData et AppData, jamais un
  dossier partagé par plusieurs programmes ou encore utilisé par un programme installé, jamais un
  lien, jamais le dossier de l'appli. Le module revérifie chaque dossier en gardant ouverts tous
  ceux qui mènent à lui, pour qu'aucun ne puisse être remplacé par un lien entre la vérification
  et le déplacement.
- **Programmes lancés.** Les outils de Windows sont appelés par leur chemin complet dans
  `System32`, jamais par leur seul nom. Le PowerShell que lance le module (magasin de pilotes,
  cache de l'Optimisation de distribution) ne charge aucun module tout seul : il chercherait
  d'abord dans `Documents\WindowsPowerShell\Modules`, où n'importe quel programme de l'utilisateur
  peut écrire. Chaque module de Windows est importé par son chemin dans `System32`, et chaque
  commande appelée avec le nom de son module.
- **DLL.** Les DLL natives sont livrées à côté de l'exe, jamais décompressées dans `%TEMP%`.

**Réglages de Windows**

- **Réglages de jeu, confidentialité, faux boosts.** Avant chaque modification, la valeur
  d'origine est gardée sous `HKCU\Software\Driver Updater\Tweaks` ; « Annuler » la remet telle
  quelle, ou supprime la valeur si elle n'existait pas. Remettre la minuterie de démarrage passe
  par `bcdedit` : BitLocker est d'abord suspendu pour un seul redémarrage, sans quoi il
  demanderait la clé de récupération.
- **Fréquence d'écran.** Windows teste le mode avant qu'il soit appliqué ; sans réponse, l'ancien
  revient au bout de 15 secondes, et il n'est enregistré qu'une fois gardé.

**Le code**

- **Dépendances.** Elles sont verrouillées (`packages.lock.json`), contrôlées contre les
  vulnérabilités connues, et Dependabot les tient à jour. Tous les analyseurs de sécurité .NET sont
  actifs ; en CI, le moindre avertissement fait échouer la compilation.
- **Provenance.** Chaque Release est construite par la CI de ce dépôt, avec une attestation de
  provenance vérifiable par `gh attestation verify`.
