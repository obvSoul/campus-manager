# Campus Manager : passage d'Electron à C# WPF

Date : 2026-11-22 — Taille : projet (réécriture complète : cœur + interface). Branche : `csharp-wpf`.

## Pourquoi
L'utilisateur ne veut plus d'Electron et veut une appli plus légère. Elle tourne en permanence
(lancée avec Windows, widget sur le bureau).

Mesures de l'appli Electron installée (22/11/2026, « jeu de travail privé » = colonne Mémoire du
Gestionnaire des tâches, somme de tous les processus de l'appli) :
- widget seul : **116 Mo** ; avec la fenêtre principale : **125 Mo** (5 s après ouverture) ;
- réouverture de la fenêtre : 0,34 s ; lancement complet (cache disque chaud) : 0,43-0,67 s ;
- lancement à froid après redémarrage du PC : **à mesurer** avant le remplacement.

## Objectifs
- Mêmes fonctions qu'aujourd'hui, même look (thème Onyx), mêmes données sur le disque.
- Mémoire (même méthode) : **≤ 70 Mo widget seul**, **≤ 110 Mo avec la fenêtre**.
- Lancement : pas plus lent qu'Electron, à chaud comme à froid.
- Code C# lisible et modifiable par l'utilisateur (il connaît WPF, cf. projet NEXUS).

## Non-objectifs
- Aucune nouvelle fonction (calendrier, statuts TD/TP, synchro auto… viendront après).
- Pas de macOS/Linux.

## Anti-objectifs (échec même si « ça marche »)
- Perdre ou retélécharger la bibliothèque : `library.db` et `Documents\Campus Manager` repris tels quels
  (127 fichiers, devoirs et leurs statuts, favoris, réglages du widget).
- Régression du widget : niveau bureau, cliquable, hors barre des tâches et Alt+Tab, ne remonte jamais
  devant les fenêtres, survit à la fermeture de la fenêtre principale et à Alt+F4.
- Journaliser une URL contenant le jeton.
- Une appli plus lourde ou plus lente que la version Electron.

## Approches envisagées
| | RAM widget seul (estim.) | Travail | Verdict |
|---|---|---|---|
| **C# WPF (.NET 10)** | 40-70 Mo | tout réécrire, interface en XAML | **choisi par l'utilisateur** |
| Tauri 2 (Rust + WebView2), React gardé | 50-80 Mo | réécrire le processus principal en Rust, installer Rust | interface gardée mais Rust moins lisible pour lui |
| Rust natif (GPUI, comme Discord) | 20-40 Mo | tout réécrire, bibliothèque jeune, pas d'aperçu PDF | trop risqué |
Raisons du choix : .NET 10 déjà installé, WPF connu, appli 100 % Windows (widget, registre, DPAPI),
code modifiable par l'utilisateur.

## Conception

### Solution (même découpage que NEXUS)
```
CampusManager.sln
src/CampusManager.Core/     net10.0 : base SQLite, bibliothèque, classement, client Moodle, synchro, SSO (décodage)
src/CampusManager.App/      net10.0-windows WPF : fenêtres, widget, Win32, jeton, démarrage, raccourcis
tests/CampusManager.Tests/  xUnit : portage des 36 tests (vrais fichiers en dossier temporaire, Moodle simulé)
installer/                  Inno Setup (.iss + build.ps1)
```
Le code Electron (`src/`, `package.json`, `scripts/`, `tests/*.ts`) est supprimé de la branche une fois
le portage vérifié ; il reste dans l'historique et sur `main`.

### Réglages mémoire (repris de NEXUS)
GC station sans thread d'arrière-plan (`ServerGarbageCollector=false`, `ConcurrentGarbageCollection=false`),
`TieredPGO=false`, `System.GC.ConserveMemory=5`, `PublishReadyToRun=true` (démarrage rapide).
Compactage du tas après une synchro et à la fermeture de la fenêtre principale.

### Correspondances
| Electron | C# |
|---|---|
| `node:sqlite` | `Microsoft.Data.Sqlite`, **même schéma, même fichier** |
| fetch Moodle | `HttpClient` + `System.Text.Json` |
| SHA-256, écriture atomique | `SHA256`, fichier temporaire + `File.Move(overwrite)` |
| React (Accueil, Explorateur 4 vues, Recherche, Détails, Devoirs, Paramètres) | WPF + MVVM (`CommunityToolkit.Mvvm`), mêmes écrans ; `VirtualizingStackPanel` pour les listes |
| thème Onyx (CSS) | `ResourceDictionary` : mêmes couleurs (#070709, #0e0e10, #111214, #5865f2…), styles de barres de défilement, menus, infobulles, cases ; barre de titre sombre via `DwmSetWindowAttribute` |
| aperçu `campus://` (PDF/images) | images : `BitmapImage` ; PDF : `Windows.Data.Pdf` rend les pages en images (pas de moteur web) |
| fenêtre SSO | fenêtre WPF + contrôle WebView2 **créé seulement pour la connexion puis détruit** ; `NavigationStarting` intercepte `moodlemobile://` ; relance de `launch.php` si on atterrit sur le tableau de bord ; dossier de données WebView2 propre (cookies gardés) |
| `safeStorage` | `ProtectedData` (DPAPI, portée utilisateur) — **une reconnexion UPEC unique** : l'ancien jeton Electron n'est pas relisible |
| widget (koffi) | `Window` sans cadre, `AllowsTransparency`, `ShowInTaskbar=false`, `WS_EX_TOOLWINDOW`, propriétaire = Progman (`SetWindowLongPtrW(GWLP_HWNDPARENT)`), `HWND_BOTTOM` à l'activation, détection plein écran (minuterie 1,5 s), clic à travers via `WS_EX_TRANSPARENT`, redimensionnement par poignée |
| fermeture = destruction de la fenêtre | `ShutdownMode=OnExplicitShutdown` ; la fenêtre principale est fermée (pas cachée) |
| instance unique | `Mutex` nommé + canal nommé : un 2ᵉ lancement demande d'ouvrir la fenêtre puis quitte |
| raccourcis Ctrl+Alt+C/W/X | `RegisterHotKey` sur une fenêtre de messages |
| démarrage avec Windows | `HKCU\…\Run` « Campus Manager » = `"<exe>" --hidden` ; réglage `autostart` en base ; `EnsureAutostart` au lancement |
| glisser-déposer entrée/sortie | `DragDrop` WPF natif (`DataFormats.FileDrop`) dans les deux sens |
| corbeille, boîtes de dialogue, presse-papiers, ouvrir/révéler | `FileSystem.DeleteFile(..., SendToRecycleBin)`, `MessageBox` stylé, `Clipboard`, `Process.Start` / `explorer /select,` |
| electron-builder NSIS | Inno Setup par utilisateur (`PrivilegesRequired=lowest`), `AppMutex`, publication autonome (pas besoin d'installer .NET) |

### Données
Chemins conservés : `%APPDATA%\Campus Manager\library.db`, `logs\campus.log`, `Documents\Campus Manager\…`.
Pas de migration : schéma identique (`CREATE TABLE IF NOT EXISTS`). Réglages du widget lus depuis la
même clé `settings.widget` (JSON).

### Ancienne version
1. Mesurer l'appli Electron à froid (après redémarrage).
2. La désinstaller (les données restent).
3. Installer la version C#, se reconnecter une fois, vérifier qu'une synchro ne retélécharge rien.
4. Vérifier l'entrée « Run » côté Windows (piège MSIX : voir note Obsidian « Pièges WPF et Windows »).

## Vérifications avant de dire « fini »
- `dotnet test` : les cas portés passent.
- Captures réelles (`PrintWindow`) de chaque écran, comparées aux captures Electron du README.
- Sur les vraies données : arborescence, recherche, aperçus, devoirs, renommer/déplacer/corbeille,
  glisser-déposer dans les deux sens, reconnexion puis synchro (0 retéléchargement).
- Widget : niveau bureau, Alt+F4, fermeture de la fenêtre principale, raccourcis, clic à travers.
- Démarrage avec Windows après installation ; mesures mémoire et lancement, avant/après.

## Laissé au constructeur
- Découpage exact des classes, navigation entre pages.
- Effet acrylique du widget : à garder seulement s'il marche avec la transparence WPF, sinon retiré
  (déjà marqué « non vérifié » dans la version Electron).
- Garder ou non un outil de synchro en ligne de commande (utile pour les tests).
