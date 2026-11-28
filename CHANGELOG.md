# Changements

Chaque version a sa Release, avec l'installateur et le zip portable. La CI met la section de la
version en tête des notes de la Release.

## 0.12.0 — 28 novembre 2026

**Interface**

- Les couleurs de Windows 11, claires ou sombres comme Windows, avec la couleur d'accent choisie
  dans Windows ; un changement de thème repeint la fenêtre ouverte. Nouvelle icône, sans néon.
- Page Sécurité en cartes : chaque fait, son état, et l'endroit de Windows où agir.
- « PC de guerre » s'appelle maintenant « Réglages de jeu ».

**Pilotes**

- Hors ligne, rien ne passe plus pour « à jour » : un périphérique qu'aucune source n'a pu
  vérifier est « non vérifié », et l'analyse se dit incomplète.
- Les pilotes « facultatifs » de Windows Update ne sont plus cochés ni signalés chaque semaine.
- Catalogue : seuls les paquets publiés pour la version de Windows du PC sont proposés, et un
  pilote que Windows a ajouté sans le choisir n'est plus reproposé. Masquer et réafficher sur la
  page Fabricants.
- « Arrêter », dans la barre d'état, interrompt l'analyse et les recherches.
- Le point de restauration est créé juste avant les pilotes. Si la protection du système est
  coupée, l'appli le dit et propose de l'activer.
- Sauvegarde : la version que chaque périphérique utilise, pour qu'un retour au pilote précédent
  tienne. La restauration n'accepte qu'un dossier de sauvegarde, et liste ses pilotes avant.
- Périphériques en erreur : « Chercher un pilote » ou le Gestionnaire de périphériques, selon
  le code.
- Historique : les anciens pilotes ne disparaissent plus derrière les mises à jour de Defender.

**Jeux**

- Vérifiés sur le matériel : Secure Boot et TPM 2.0 pour les anti-triche, microcode des Core
  Intel de 13e et 14e génération, jeux d'Epic, GOG et Ubisoft sur un disque dur.
- « Mesurer un jeu » laisse 15 secondes pour retourner en jeu, et sonne au début et à la fin.
- « Forcer la carte dédiée » s'annule aussi depuis le Journal.

**Système**

- Démarrage : les tâches planifiées lancées à l'ouverture de session.
- Sécurité : un redémarrage en attente pour finir des mises à jour est signalé.
- Nettoyage : corbeille, vidages de plantage de plus de 30 jours, cache de l'Optimisation de
  distribution, et fichiers aux chemins très longs.
- Réparer Windows : DISM puis SFC en un seul passage, avec un verdict pour les deux.
- Faux boosts : restauration du système, ou Windows Update, bloqués par stratégie ; Windows
  Update envoyé vers un faux serveur d'entreprise.

**Logiciels**

- winget : la raison d'un échec en clair, et une installation qui attend seulement un
  redémarrage compte comme réussie.
- « Ne plus proposer » un logiciel, par l'épinglage de winget.
- Restes de désinstallation aussi dans `%LocalAppData%\Programs`.

**Fiabilité et sécurité**

- Une seule fenêtre : relancer l'appli ramène celle qui est ouverte. Elle ne dépasse plus de
  l'écran et rouvre là où elle était.
- La notification hebdomadaire ouvre la page de ce qui est nouveau.
- Le PowerShell que lance le module administrateur ne charge que les modules de Windows : un
  module déposé dans `Documents\WindowsPowerShell\Modules` pouvait remplacer une de ses commandes.
- Le diagnostic joint le détail des erreurs inattendues ; un plantage le laisse dans `crash.txt`.

## Versions précédentes

Leurs fichiers sont dans les [Releases](https://github.com/Augaton/driver_updater/releases).
