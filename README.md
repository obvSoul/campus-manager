# Campus Manager

**Application Windows de gestion des cours universitaires**, qui synchronise la plateforme Moodle de mon université (EPREL, UPEC) puis fonctionne entièrement hors ligne.

Les fichiers de cours sont réellement téléchargés et classés automatiquement sur le disque, les devoirs sont suivis avec leurs vraies échéances, et un widget reste affiché sur le bureau Windows.

![TypeScript](https://img.shields.io/badge/TypeScript-strict-3178c6) ![Electron](https://img.shields.io/badge/Electron-45-47848f) ![React](https://img.shields.io/badge/React-19-61dafb) ![SQLite](https://img.shields.io/badge/SQLite-node%3Asqlite-003b57) ![Tests](https://img.shields.io/badge/tests-47%20passing-23a55a)

<!-- Captures d'écran : docs/accueil.png, docs/explorateur.png, docs/widget.png -->

## Pourquoi

Sur Moodle, les documents sont dispersés entre des dizaines de sections, les dates limites changent (prolongations), et rien n'est accessible sans connexion. Campus Manager traite Moodle comme une simple **source de synchronisation** : une fois les cours récupérés, tout est local, rangé et consultable sans Internet.

## Fonctionnalités

- **Synchronisation Moodle** : connexion unique via le SSO de l'université (CAS), jeton chiffré par Windows, aucun mot de passe stocké.
- **Bibliothèque physique** : fichiers rangés dans `Documents\Campus Manager\Semestre\Matière\Catégorie`, utilisables avec n'importe quel logiciel.
- **Classement automatique** (Cours, TD, TP, Corrections, Projets…) par un système de règles pondérées avec score de confiance ; les cas douteux vont dans « À classer ».
- **Devoirs** : échéances réelles (prolongations comprises), statut « rendu » importé, devoirs des années précédentes séparés.
- **Explorateur** : 4 vues, tri, recherche globale, aperçu PDF/images, glisser-déposer depuis et vers l'Explorateur Windows.
- **Widget de bureau** : transparent, fixé au niveau du bureau (sous les fenêtres), masqué automatiquement pendant un jeu en plein écran.
- **Arrière-plan** : démarrage avec Windows, raccourcis clavier globaux, l'appli continue quand la fenêtre est fermée.
- **Installateur** Windows (NSIS) sans droits administrateur.

## Points techniques intéressants

- **Authentification SSO sans navigateur intégré à l'appli** : reproduction du flux de l'application Moodle officielle (`launch.php` → redirection `moodlemobile://token=…`), interception de la redirection dans Electron et vérification de la signature MD5 renvoyée par le serveur.
- **Synchronisation sûre** : écriture atomique (`.part` puis renommage), empreinte SHA-256, aucun retéléchargement des fichiers inchangés. Un fichier modifié par l'utilisateur n'est **jamais écrasé** : la nouvelle version est enregistrée à côté. Un fichier déplacé dans l'Explorateur est retrouvé par taille et empreinte.
- **Widget « collé » au bureau** : appels directs à l'API Win32 depuis TypeScript (FFI avec `koffi`) : rattachement à la fenêtre du bureau (`Progman`), `SetWindowPos(HWND_BOTTOM)`, détection d'une application plein écran avec `GetForegroundWindow` / `GetMonitorInfoW`.
- **Sécurité Electron** : `contextIsolation`, `sandbox`, pont IPC typé et validé ; protocole interne `campus://` qui ne sert que les fichiers connus de la bibliothèque. Les URL contenant un jeton ne sont jamais journalisées.
- **Échéances fiables** : l'API des devoirs ne donne que la date initiale ; la vraie date (avec prolongation) est récupérée dans la chronologie du calendrier Moodle puis rapprochée par identifiant de module.

## Architecture

```
src/
  core/        logique sans Electron (testée)
    db/        schéma SQLite et migrations
    library/   fichiers sur disque : import, déplacement, conflits, réconciliation
    classify/  classement automatique
    sync/      client API Moodle, SSO, moteur de synchronisation
  main/        processus principal Electron : fenêtres, IPC, widget, Win32, démarrage Windows
  renderer/    interface React (application et widget)
tests/         tests vitest (vrais fichiers sur disque, API Moodle simulée)
```

Détails : [ARCHITECTURE.md](ARCHITECTURE.md).

**Stack** : TypeScript (strict), Electron, React, SQLite (`node:sqlite`), esbuild, vitest, electron-builder, koffi.

## Lancer le projet

Prérequis : Windows 10/11, Node.js 22+.

```bash
npm install
npm start          # compile et lance l'application
```

```bash
npm test           # 47 tests
npm run typecheck
npm run dist       # crée l'installateur dans release/
```

La première synchronisation ouvre la page de connexion de l'université. L'application est conçue pour EPREL (UPEC) ; un autre Moodle qui autorise l'application mobile officielle peut être visé avec la variable d'environnement `EPREL_URL` (non testé).

## Feuille de route

Calendrier, synchronisation automatique avec notifications, choix du dossier racine.
