# Campus Manager : architecture

EPREL (Moodle UPEC) n'est qu'une **source de synchronisation**. Tout le reste fonctionne hors ligne
sur les fichiers du disque et la base SQLite locale.

## Constats sur EPREL (vérifiés sur le vrai site, sans compte)
- Plateforme Moodle ; l'API officielle des services web mobiles est activée (`/login/token.php` répond).
- Les comptes UPEC passent par un SSO (« Compte UPEC ») : la connexion se fait dans une fenêtre où tu
  tapes toi-même tes identifiants ; l'appli ne reçoit qu'un jeton (flux officiel de l'appli mobile).
- **Non vérifié** (nécessite ton compte) : que le jeton soit délivré, quelles fonctions sont ouvertes,
  que le téléchargement des fichiers soit autorisé, les vrais noms de sections/semestres.

## Electron ou Tauri ?
| | Electron | Tauri |
|---|---|---|
| RAM | plus lourd | léger (WebView2) |
| Outils requis ici | Node (présent) | Rust + outils C++ (absents) |
| Widget Windows | koffi/FFI vers user32 | crate `windows` natif |
| Coffre jeton | `safeStorage` (DPAPI) | plugin à ajouter |
Choix : **Electron**, parce que la chaîne Rust n'est pas installée et que le gain de RAM ne justifie pas
de la bloquer. La logique métier (`src/core`) ne dépend pas d'Electron : un portage reste possible.

## Modules (`src/core`, sans dépendance à l'interface)
| Dossier | Rôle | Parle à EPREL ? |
|---|---|---|
| `sync/` | client Moodle, SSO, moteur de synchronisation | **seul module** |
| `library/` | fichiers réels : import atomique, déplacement, renommage, reconcile | non |
| `classify/` | catégorie d'un document (plusieurs signaux + confiance) | non |
| `db/` | schéma SQLite (matières, ressources, fichiers, devoirs, journal de sync) | non |

## Stockage physique
- Bibliothèque : `Documents\Campus Manager\<Semestre>\<Matière>\<Catégorie>\<fichier>` (vrais fichiers).
- Données de l'appli : `%APPDATA%\Campus Manager\` (`library.db`, jeton chiffré par Windows, journaux).
- Écriture atomique + vérification de taille ; empreinte SHA-256 de chaque fichier.
- Un fichier modifié par l'utilisateur n'est jamais écrasé (nouvelle version enregistrée à côté).
- Une ressource disparue d'EPREL est signalée, jamais supprimée localement.
- Un fichier déplacé hors de l'appli est retrouvé par taille + empreinte.

## État
Fait et testé (33 tests, vrais fichiers sur disque ; l'API Moodle y est simulée) : base, bibliothèque,
classement, moteur de sync, client Moodle, SSO (décodage/vérification).
À faire : fenêtre de connexion + stockage du jeton (Electron), validation sur le vrai EPREL,
interface (gestionnaire de fichiers, devoirs, calendrier), widget, arrière-plan, notifications, installateur.
