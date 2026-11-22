# Campus Manager : architecture

EPREL (Moodle UPEC) n'est qu'une **source de synchronisation**. Tout le reste fonctionne hors ligne
sur les fichiers du disque et la base SQLite locale.

## Constats sur EPREL
- Plateforme Moodle ; l'API officielle des services web mobiles est activée (`/login/token.php` répond).
- Les comptes UPEC passent par un SSO (« Compte UPEC ») : la connexion se fait dans une fenêtre où tu
  tapes toi-même tes identifiants ; l'appli ne reçoit qu'un jeton (flux officiel de l'appli mobile).
- EPREL impose le schéma `moodlemobile://` pour rendre le jeton ; après le SSO il renvoie sur le tableau
  de bord : il faut redemander `launch.php`.

## Electron, Tauri ou C# ?
La version 0.1 était en Electron (≈ 116 Mo de mémoire avec le widget seul). La version 0.2 est en
**C# / WPF** : .NET 10 était déjà installé, l'appli est 100 % Windows (widget au bureau, registre, DPAPI)
et le code reste lisible et modifiable. Tauri (Rust + WebView2) gardait l'interface React mais aurait
coûté environ autant de mémoire qu'un moteur web ; une interface Rust native (GPUI) aurait demandé de
tout refaire sans aperçu PDF.

## Projets
| Projet | Rôle | Parle à EPREL ? |
|---|---|---|
| `CampusManager.Core/Sync` | client Moodle, SSO (décodage/vérification), moteur de synchronisation | **seul module** |
| `CampusManager.Core/Library` | fichiers réels : import atomique, déplacement, renommage, dépôt, réconciliation | non |
| `CampusManager.Core/Classify` | catégorie d'un document (plusieurs signaux + confiance) | non |
| `CampusManager.Core/Db` | schéma SQLite (matières, ressources, fichiers, devoirs, journal de sync), migrations douces | non |
| `CampusManager.Core/Queries` | lectures pour l'interface et le widget | non |
| `CampusManager.App` | WPF : fenêtre principale, widget, connexion WebView2, Win32, jeton DPAPI, démarrage Windows | via Core |

`Db` n'a qu'une connexion SQLite, protégée par un verrou : la synchronisation (thread de fond) et
l'interface peuvent l'utiliser en même temps.

## Stockage physique
- Bibliothèque : `Documents\Campus Manager\<Semestre>\<Matière>\<Catégorie>\<fichier>` (vrais fichiers).
- Données de l'appli : `%APPDATA%\Campus Manager\` (`library.db`, `token.bin` chiffré par Windows,
  `logs\campus.log`, `WebView2\` pour les cookies de connexion). Même base que la version Electron.
- Écriture atomique + vérification de taille ; empreinte SHA-256 de chaque fichier.
- Un fichier modifié par l'utilisateur n'est jamais écrasé (nouvelle version enregistrée à côté).
- Une ressource disparue d'EPREL est signalée, jamais supprimée localement.
- Un fichier déplacé hors de l'appli est retrouvé par taille + empreinte.

## Cycle de vie
- Un seul processus et une seule instance (mutex `Local\CampusManager.Instance` + canal nommé).
- Fermer la fenêtre principale la **ferme vraiment** (mémoire rendue) ; le widget et la synchro continuent.
- Démarrage avec Windows : valeur `HKCU\…\Run\Campus Manager` = `"<exe>" --hidden`, recréée au lancement
  si le choix de l'utilisateur est « activé » (une réinstallation peut l'avoir retirée).

## Tests
`dotnet test` : 61 tests xUnit sur de vrais fichiers dans un dossier temporaire ; l'API Moodle y est
simulée (format documenté). Ils prouvent la logique, pas que le vrai EPREL expose ces données.
