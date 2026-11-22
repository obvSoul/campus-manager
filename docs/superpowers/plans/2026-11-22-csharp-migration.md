# Campus Manager en C# WPF — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remplacer l'appli Electron par une appli C# WPF (.NET 10) aux mêmes fonctions, même look, mêmes données, plus légère.

**Architecture:** `CampusManager.Core` (net10.0, sans WPF) porte un pour un `src/core/*.ts` et `src/core/queries.ts`. `CampusManager.App` (WPF, MVVM) remplace `src/main/*` et `src/renderer/*`. Les fichiers TypeScript existants sont la **référence de comportement** : chaque tâche de portage dit quel fichier imiter.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite + Dapper, System.Text.Json, ProtectedData (DPAPI), Microsoft.Web.WebView2 (connexion seulement), Windows.Data.Pdf (TFM `net10.0-windows10.0.19041.0`), xUnit, Inno Setup.

**Spec:** `docs/superpowers/specs/2026-11-22-csharp-migration-design.md`

## Global Constraints
- Branche `csharp-wpf`. `main` n'est pas touché.
- Données : `%APPDATA%\Campus Manager\library.db`, `%APPDATA%\Campus Manager\logs\campus.log`, bibliothèque `Documents\Campus Manager` (ou réglage `settings.root`). Schéma SQLite **identique** à `src/core/db/database.ts`, migrations douces comprises.
- Horodatages : secondes Unix (`long`), comme aujourd'hui.
- Jeton : jamais dans un journal, un message d'erreur ou une URL journalisée.
- Réglages de compilation de l'App (repris de NEXUS) : `ServerGarbageCollector=false`, `ConcurrentGarbageCollection=false`, `TieredPGO=false`, `System.GC.ConserveMemory=5`, `SatelliteResourceLanguages=fr`, publication `PublishReadyToRun=true`, autonome `win-x64`.
- Objectifs mesurés (jeu de travail privé, somme des processus de l'appli) : ≤ 70 Mo widget seul, ≤ 110 Mo avec la fenêtre ; lancement pas plus lent qu'Electron (réf. 0,43-0,67 s à chaud, 0,34 s réouverture).
- Thème Onyx : fond `#070709`, surfaces `#0e0e10` / `#111214`, accent `#5865f2` (survol `#4752c4`), vert `#23a55a`, jaune `#f0b232`, rouge `#f23f43` ; valeurs exactes du thème clair et des autres couleurs dans `src/renderer/styles.css` et `widget.css`.
- Textes de l'interface : en français, repris des fichiers `.tsx` correspondants.
- Pièges WPF connus (note Obsidian « Pièges WPF et Windows ») : style de `Window` avec clé explicite ; styler barres de défilement, menus, infobulles, cases ; `ImplicitUsings` n'inclut pas `System.IO` ; ne pas mettre `Margin="{TemplateBinding Padding}"` sur `PART_ContentHost`.

## Review Focus
1. **Base Electron existante ouverte par C#** : une copie de la vraie `library.db` doit s'ouvrir, migrer sans perte et donner les mêmes comptes (127 fichiers, devoirs, favoris). Test : Task 1 `OpensDatabaseCreatedByElectronSchema` + vérification Task 12.
2. **Accès concurrent** : la synchro (thread de fond) écrit pendant que l'interface lit ou déplace un fichier. Attendu : pas d'exception « database is locked », pas de corruption. `Db` sérialise les accès (verrou) — test Task 1 `ConcurrentWritesFromTwoThreadsSucceed`.
3. **Chemins à accents, longs ou en nom court 8.3** (`PRENOM~1`) : déposer ou retrouver un tel fichier doit marcher. Test Task 2 `FindByAbsPathAcceptsShortAndAccentedPaths`.
4. **Hors ligne au démarrage** : synchro impossible → message « Hors ligne… », interface et widget utilisables. Test Task 3 `NetworkDown_IsTransient_TokenNotLeaked` + Task 5 `SyncOffline_SetsOfflineMessage`.
5. **Moniteur débranché** : position du widget enregistrée hors écran → revient à la position par défaut. Test Task 10 `BoundsOffScreen_FallBackToDefault`.

---

### Task 1 : Solution, base de données, noms, classement

**Files:**
- Create: `CampusManager.sln`, `Directory.Build.props` (`Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `TreatWarningsAsErrors=false`)
- Create: `src/CampusManager.Core/CampusManager.Core.csproj` (net10.0 ; Microsoft.Data.Sqlite, Dapper)
- Create: `src/CampusManager.Core/Db/Db.cs`, `Db/Schema.cs`
- Create: `src/CampusManager.Core/Library/Names.cs`
- Create: `src/CampusManager.Core/Classify/Classifier.cs`
- Create: `tests/CampusManager.Tests/CampusManager.Tests.csproj` (xUnit), `TempDir.cs`, `ClassifierTests.cs`, `NamesTests.cs`, `DbTests.cs`

**Interfaces:**
- Produces:
  - `sealed class Db : IDisposable` — `static Db Open(string path)` (`":memory:"` accepté) ; `int Execute(string sql, object? p = null)` ; `T? One<T>(string sql, object? p = null)` ; `List<T> All<T>(string sql, object? p = null)` ; `T Scalar<T>(string sql, object? p = null)` ; `long Insert(string sql, object? p = null)` (retourne `last_insert_rowid()`) ; `void Tx(Action body)`. Une seule `SqliteConnection`, chaque méthode sous `lock`. Dapper avec `DefaultTypeMap.MatchNamesWithUnderscores = true`.
  - `Schema.Apply(Db)` : SQL de `database.ts` mot pour mot + migrations douces (`layout_manual`, `user_deleted`, `startdate`, `cmid`, `base_due_at`, `submitted`, `status_source`) + `remote_aliases`. Pragmas `journal_mode=WAL`, `foreign_keys=ON`.
  - `static class Names` — `string Sanitize(string name, int max = 120)`, `string UniquePath(string dir, string name)`.
  - `static class Classifier` — `string[] Categories` (8 catégories de `classifier.ts`, même ordre) ; `const string Unsorted = "À classer"` ; `record ClassifyInput(string? Modname = null, string? Title = null, string? Filename = null, string? Section = null, string? Description = null)` ; `record Classification(string Category, double Confidence, List<string> Reasons)` ; `Classification Classify(ClassifyInput input)`.

- [ ] **Step 1: Écrire les tests** — porter `tests/classifier.test.ts` (7 cas) et le bloc « noms Windows » de `tests/library.test.ts` (2 cas) avec les mêmes entrées et attentes. Ajouter :
  - `OpensDatabaseCreatedByElectronSchema` : créer une base avec le SCHEMA d'origine **sans** les colonnes migrées, y insérer une ligne `files`, `Db.Open` → les 7 colonnes migrées existent, la ligne est intacte.
  - `ConcurrentWritesFromTwoThreadsSucceed` : 2 tâches × 200 `INSERT INTO settings` sur un fichier temporaire → 400 lignes, aucune exception.
- [ ] **Step 2: Lancer** `dotnet test` → échec de compilation (types absents).
- [ ] **Step 3: Implémenter** `Db`, `Schema`, `Names`, `Classifier` en imitant `database.ts`, `names.ts`, `classifier.ts`. Regex .NET : `RegexOptions.IgnoreCase | CultureInvariant` ; `\b` se comporte comme en JS pour ces motifs. `Names.Sanitize` normalise en NFC.
- [ ] **Step 4: Lancer** `dotnet test` → 11 tests verts.
- [ ] **Step 5: Commit** `feat(core): solution C#, base SQLite, noms, classement`.

### Task 2 : Bibliothèque physique

**Files:**
- Create: `src/CampusManager.Core/Library/Library.cs`, `Library/Models.cs`
- Test: `tests/CampusManager.Tests/LibraryTests.cs`

**Interfaces:**
- Consumes: `Db`, `Names`.
- Produces:
  - `record FileRow` (colonnes de `files`, propriétés PascalCase : `Id, ResourceId, CourseId, Category, RemoteKey, LocalPath, OriginalName, Size, Sha256, DownloadedSha256, RemoteModified, RemoteSize, Favorite, Missing, RemoteDeleted, UserDeleted, AddedAt`).
  - `enum ImportStatus { Added, Unchanged, Updated, Conflict, Duplicate }` ; `record ImportResult(ImportStatus Status, long FileId, string Path)`.
  - `record ImportRequest(long CourseId, long? ResourceId, string Category, string Name, string? RemoteKey, long? RemoteModified, long? RemoteSize, Func<Task<byte[]>> FetchBytes)`.
  - `record DropResult(int Imported, int Moved, int Duplicates, List<string> Skipped)`.
  - `class Library(Db db, string root)` : `string Root` ; `string Abs(string rel)` ; `string CourseDir(long courseId, string category, string? subfolder = null)` ; `FileRow GetFile(long id)` ; `bool IsLocallyModified(FileRow f)` ; `Task<ImportResult> ImportFileAsync(ImportRequest r)` ; `FileRow MoveFile(long id, long? courseId = null, string? category = null, string? subfolder = null)` ; `FileRow RenameFile(long id, string newName)` ; `FileRow? FindByAbsPath(string abs)` ; `ImportResult ImportLocal(string src, long courseId, string category)` ; `DropResult Drop(IEnumerable<string> paths, long courseId, string? category)` ; `void MarkDeleted(long id)` ; `void SetFavorite(long id, bool on)` ; `string CreateFolder(long courseId, string category, string subfolder)` ; `void RelocateCourse(long courseId, string semester, string folder)` ; `(int Relinked, int Missing) Reconcile()` ; `static string Sha256Hex(byte[] data)` (minuscules).

- [ ] **Step 1: Écrire les tests** — porter les 13 cas « Library » de `tests/library.test.ts` (vrais fichiers dans `TempDir`). Ajouter `FindByAbsPathAcceptsShortAndAccentedPaths` : fichier dans `…\Matière é\x.pdf` ; `FindByAbsPath` avec le chemin long ET le chemin 8.3 (obtenu par `GetShortPathName` ; test ignoré si les noms 8.3 sont désactivés sur le volume) → même `FileRow`.
- [ ] **Step 2: Lancer** `dotnet test --filter LibraryTests` → échec.
- [ ] **Step 3: Implémenter** en imitant `library.ts`. Écriture atomique : `.part` + vérif taille + `File.Move(tmp, dest, overwrite: true)`. Déplacement entre volumes : copie + suppression. `FindByAbsPath` : `Path.GetFullPath` puis `GetLongPathName` (P/Invoke kernel32) avant le calcul du chemin relatif ; comparaison insensible à la casse.
- [ ] **Step 4: Lancer** les tests → verts.
- [ ] **Step 5: Commit** `feat(core): bibliothèque physique`.

### Task 3 : Client Moodle, SSO, moteur de synchronisation

**Files:**
- Create: `src/CampusManager.Core/Sync/MoodleModels.cs`, `Sync/MoodleClient.cs`, `Sync/Sso.cs`, `Sync/SyncEngine.cs`
- Test: `tests/CampusManager.Tests/SyncTests.cs`, `MoodleClientTests.cs`, `SsoTests.cs`, `FakeMoodle.cs`

**Interfaces:**
- Consumes: `Db`, `Library`, `Classifier`, `Names`.
- Produces:
  - `class MoodleException(string message, string code) : Exception` — `string Code`, `bool IsAuthError` (invalidtoken, invalidlogin, accessexception, forcepasswordchangenotice), `bool IsTransient` (network, http5xx).
  - Modèles (records, noms JSON Moodle via `JsonPropertyName`) : `MoodleCourse(Id, Fullname, Shortname, Startdate?, Enddate?)`, `ModuleContent(Type, Filename, Filesize, Fileurl, Timemodified)`, `CourseModule(Id, Name, Modname, Description?, Url?, Contents?, Added?)`, `CourseSection(Id, Name, Modules)`, `Assignment(Id, Cmid, Course, Name, Intro?, Duedate, Allowsubmissionsfromdate, Timemodified)`, `SiteInfo(Userid, Fullname)`, `ActionEvent(Modulename, Instance, Timesort, Url?)`.
  - `interface IMoodleApi` : `Task<List<MoodleCourse>> CoursesAsync()`, `Task<List<CourseSection>> ContentsAsync(long courseId)`, `Task<List<Assignment>> AssignmentsAsync(IReadOnlyList<long> courseIds)`, `Task<byte[]> DownloadAsync(string fileurl)`, `Task<string?> SubmissionStatusAsync(long assignId)`, `Task<List<ActionEvent>> ActionEventsAsync(long fromTime)`.
  - `class MoodleClient(string baseUrl, string token, HttpMessageHandler? handler = null, TimeSpan? timeout = null) : IMoodleApi` + `Task<SiteInfo> SiteInfoAsync()`.
  - `static class Sso` — `const string UrlScheme = "moodlemobile"` ; `string NewPassport()` ; `string BuildLaunchUrl(string baseUrl, string passport)` ; `bool IsTokenRedirect(string url)` ; `(string Token, string? PrivateToken) ParseTokenUrl(string url, string baseUrl, string passport)`.
  - `record SyncReport(...)` (champs de `SyncReport` dans `engine.ts`, `List<string> Conflicts, Errors`) ; `static class SyncEngine` — `Task<SyncReport> SyncAllAsync(IMoodleApi api, Db db, Library lib, int retryMs = 1000, long? now = null)`, `string GuessSemester(string fullname, string shortname)`, `string CourseFolderName(string fullname)`, `Task<T> WithRetry<T>(Func<Task<T>> fn, int tries = 3, int baseMs = 1000)`.

- [ ] **Step 1: Écrire les tests** — porter les 13 cas `syncAll`, 4 cas `MoodleClient`, 2 cas `SSO` de `tests/sync.test.ts` (même faux EPREL : `FakeMoodle : IMoodleApi` ; pour `MoodleClient`, un `HttpMessageHandler` de test qui enregistre les requêtes). Le cas « réseau coupé » s'appelle `NetworkDown_IsTransient_TokenNotLeaked`.
- [ ] **Step 2: Lancer** → échec.
- [ ] **Step 3: Implémenter** en imitant `moodle.ts`, `sso.ts`, `engine.ts`. Appels POST `application/x-www-form-urlencoded` vers `/webservice/rest/server.php`, tableaux en `courseids[0]=…`. MD5 de `site + passport` en hexadécimal minuscule. Téléchargement : réponse `application/json` = erreur Moodle ; jeton remplacé par `***` dans tout message.
- [ ] **Step 4: Lancer** `dotnet test` → tous verts (≈ 44).
- [ ] **Step 5: Commit** `feat(core): client Moodle, SSO, synchronisation`.

### Task 4 : Requêtes pour l'interface et le widget

**Files:**
- Create: `src/CampusManager.Core/Queries/Dtos.cs`, `Queries/Queries.cs`
- Test: `tests/CampusManager.Tests/QueriesTests.cs`

**Interfaces:**
- Consumes: `Db`.
- Produces: records de `src/shared/api.ts` en PascalCase : `CourseNode`, `SemesterNode`, `FileItem`, `FileDetails`, `FileQuery`, `AssignmentItem` (`Status` = `"todo" | "doing" | "done"` en `string`), `Dashboard` (+ `DashboardCounts`, `CourseProgress`, `LastSync`), `WidgetSettings` (+ `WidgetSections`, `static WidgetSettings Default` = `DEFAULT_WIDGET`), `WidgetData`, `SyncState`. `static class Queries` : `List<FileItem> ListFiles(Db db, FileQuery q)`, `List<SemesterNode> Tree(Db db)`, `long AcademicYearStart(long now)`, `List<AssignmentItem> ListAssignments(Db db, long now)`, `void SetAssignment(Db db, long id, string? status, int? priority, string? notes, bool notesSet)`, `WidgetData WidgetData(Db db, long now)`, `Dashboard Dashboard(Db db, long now)`, `FileDetails Details(Db db, Library lib, long id)`.
- `WidgetSettings` se sérialise en JSON **avec les mêmes noms camelCase** qu'aujourd'hui (clé `settings.widget` relue telle quelle).

- [ ] **Step 1: Écrire les tests** — porter les 6 cas de `tests/queries.test.ts`. Ajouter `WidgetSettingsJsonFromElectronIsRead` : JSON `{"enabled":true,"opacity":0.3,"sections":{"recent":false}}` → `Opacity=0.3`, `Sections.Recent=false`, autres valeurs = défaut.
- [ ] **Step 2: Lancer** → échec.
- [ ] **Step 3: Implémenter** en imitant `queries.ts` et le gestionnaire `details` de `src/main/ipc.ts`. `AcademicYearStart` en heure locale (`DateTime` `Kind=Local`).
- [ ] **Step 4: Lancer** → verts.
- [ ] **Step 5: Commit** `feat(core): requêtes interface et widget`.

### Task 5 : Hôte de l'application (cycle de vie, services, jeton, démarrage Windows)

**Files:**
- Create: `src/CampusManager.App/CampusManager.App.csproj` (WinExe, `net10.0-windows10.0.19041.0`, `UseWPF`, réglages mémoire, icône `build/icon.ico`, CommunityToolkit.Mvvm, Microsoft.Web.WebView2)
- Create: `App.xaml(.cs)`, `Services/AppServices.cs`, `Services/TokenStore.cs`, `Services/Autostart.cs`, `Services/SingleInstance.cs`, `Services/Hotkeys.cs`, `Services/Log.cs`
- Test: `tests/CampusManager.Tests/AppServicesTests.cs` (le projet de test référence l'App pour ces classes non visuelles)

**Interfaces:**
- Consumes: tout Core.
- Produces:
  - `class AppServices` — `Db Db`, `Library Lib`, `string DataDir`, `SyncState State`, `event Action<SyncState> StateChanged`, `event Action DataChanged`, `void Log(string msg)`, `Task<SyncState> SyncAsync(bool relogin)`, `void NotifyChanged()`. Constructeur `(string dataDir, string? defaultRoot, ITokenStore tokens, Func<Task<string>> login, Func<string, IMoodleApi> clientFactory)` ; lance `Reconcile` au démarrage (comme `services.ts`). Après une synchro : `GC.Collect(2, GCCollectionMode.Aggressive, true, true)`.
  - `interface ITokenStore { string? Load(); void Save(string token); void Clear(); }` ; `TokenStore(string dataDir)` : fichier `token.bin`, `ProtectedData` portée `CurrentUser`.
  - `static class Autostart` — `const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run"`, `const string ValueName = "Campus Manager"` ; `string Command(string exePath) => $"\"{exePath}\" --hidden"` ; `bool Get()` ; `bool Set(bool on)` ; `void Ensure(bool wanted)` (recrée l'entrée si absente ou pointant ailleurs).
  - `SingleInstance` : `Mutex` `Local\CampusManager.Instance` + canal nommé `CampusManager.Open` ; message reçu = « ouvrir la fenêtre principale » (sur le thread UI).
  - `Hotkeys` : `RegisterHotKey` sur une `HwndSource` cachée ; Ctrl+Alt+C → ouvrir, Ctrl+Alt+W → afficher/masquer widget, Ctrl+Alt+X → bascule clic à travers.
  - `App` : `ShutdownMode=OnExplicitShutdown` ; arguments `--hidden` (widget seul) et `--page=<vue>` ; `void OpenMain(string? page = null)` ; `void QuitAll()`. Réglage `autostart` : même logique que `app.ts` (activé une fois par défaut, puis `Ensure`).

- [ ] **Step 1: Écrire les tests** : `TokenRoundTrip_FileIsNotPlainText` ; `AutostartCommandQuotesPathAndAddsHidden` ; `SyncOffline_SetsOfflineMessage` (client qui lève `MoodleException("…","network")` → `State.Message` commence par « Hors ligne », `Running=false`) ; `SyncAuthError_ClearsTokenAndCallsLogin` ; `LogNeverContainsToken` (synchro réussie avec jeton `SECRET123` → `campus.log` ne contient pas `SECRET123`).
- [ ] **Step 2: Lancer** → échec.
- [ ] **Step 3: Implémenter** en imitant `services.ts`, `token-store.ts`, `autostart.ts`, `app.ts`. Messages de `SyncState` identiques à `services.ts`. La synchro tourne sur `Task.Run` ; les événements sont relayés au thread UI par l'appelant.
- [ ] **Step 4: Lancer** → verts ; `dotnet build src/CampusManager.App` sans erreur.
- [ ] **Step 5: Commit** `feat(app): cycle de vie, services, jeton DPAPI, démarrage Windows`.

### Task 6 : Thème Onyx, fenêtre principale, accueil

**Files:**
- Create: `src/CampusManager.App/Theme/Onyx.Dark.xaml`, `Theme/Onyx.Light.xaml`, `Theme/Controls.xaml` (boutons, champs, listes, barres de défilement fines et arrondies sans flèches, menus contextuels, infobulles, cases, `ComboBox`)
- Create: `Views/MainWindow.xaml(.cs)`, `Views/Sidebar.xaml(.cs)`, `Views/HomePage.xaml(.cs)`, `ViewModels/MainViewModel.cs`, `ViewModels/HomeViewModel.cs`, `Views/Toast.xaml`, `Views/Icons.xaml` (géométries des icônes de `Icon.tsx` / `FileIcon.tsx`), `Services/Dwm.cs`

**Interfaces:**
- Consumes: `AppServices`, `Queries`.
- Produces: `MainViewModel` — `object CurrentPage`, `void Go(string page)` (format de fragment de `App.tsx` : `home`, `all`, `recent`, `favorites`, `assignments`, `work`, `settings`, `course/<id>[/<cat>]`, `search/<q>`), `void Toast(string msg)` (3,2 s), `string Theme` (`dark`/`light`, mémorisé dans `settings.uiTheme`), `string ViewMode` (`list|grid|large|compact`, `settings.uiMode`). Les pages se rafraîchissent sur `AppServices.DataChanged` et `StateChanged`.

- [ ] **Step 1: Implémenter** le thème (couleurs de `styles.css`), la fenêtre (barre de titre sombre via `DwmSetWindowAttribute` 20/35/36, taille 1280×800, min 900×560, fond `#070709`), la barre latérale (`Sidebar.tsx` : arborescence semestres/matières/catégories avec compteurs, liens, bouton de synchro avec état) et l'accueil (`Home.tsx`).
- [ ] **Step 2: Lancer** `dotnet run --project src/CampusManager.App -- --page=home` avec une **copie** de `%APPDATA%\Campus Manager` (variable `CAMPUS_DATA_DIR` lue par `App` pour les tests) → capture `PrintWindow` (drapeau 2) enregistrée dans le dossier temporaire ; comparer à `docs/accueil.png` : mêmes blocs, mêmes couleurs, aucun contrôle blanc.
- [ ] **Step 3: Mesurer** jeu de travail privé, fenêtre ouverte, 5 s après affichage → noter le chiffre dans le message de commit (cible ≤ 110 Mo).
- [ ] **Step 4: Commit** `feat(ui): thème Onyx, fenêtre principale, accueil`.

### Task 7 : Explorateur, détails, aperçu, recherche, glisser-déposer

**Files:**
- Create: `Views/FileBrowser.xaml(.cs)`, `ViewModels/FilesViewModel.cs`, `Views/DetailsPane.xaml(.cs)`, `Services/PdfPreview.cs`, `Services/Shell.cs`, `Views/PromptDialog.xaml(.cs)`, `Views/ConfirmDialog.xaml(.cs)`

**Interfaces:**
- Consumes: `MainViewModel`, `Library`, `Queries`.
- Produces: `PdfPreview.RenderAsync(string path, int page, double width) : Task<ImageSource>` via `Windows.Data.Pdf` ; `Shell.Open(string path) : string?` (message d'erreur ou null), `Shell.Reveal(string path)`, `Shell.Trash(string path)` (`FileSystem.DeleteFile(…, RecycleOption.SendToRecycleBin)`).

- [ ] **Step 1: Implémenter** en imitant `FileBrowser.tsx`, `Details.tsx`, `dnd.ts` et les gestionnaires de `ipc.ts` : 4 vues (liste triable par en-têtes, grille, grandes icônes, compacte ; `VirtualizingStackPanel` / `VirtualizingWrapPanel` maison si besoin) ; menu contextuel (ouvrir, afficher dans l'Explorateur, renommer, déplacer, favori, copier le chemin, corbeille avec la même confirmation que `ipc.ts`, nouveau dossier) ; panneau de détails avec aperçu image (`BitmapImage` `CacheOption=OnLoad`, décodage à la largeur affichée) et PDF (pages rendues à la demande, libérées en quittant le fichier) ; recherche globale (`FileQuery.Search`) ; dépôt de fichiers Windows sur une matière/catégorie de la barre latérale ou de la vue courante → `Library.Drop` puis toast de `describeDrop` ; glisser sortant d'un fichier → `DragDrop.DoDragDrop` avec `DataFormats.FileDrop`.
- [ ] **Step 2: Vérifier** sur la copie des données : capture de l'explorateur comparée à `docs/explorateur.png` ; aperçu d'un PDF et d'une image ; renommer puis renommer à l'inverse ; dépôt d'un fichier temporaire dans une matière puis mise à la corbeille ; glisser un fichier vers un dossier temporaire de l'Explorateur.
- [ ] **Step 3: Commit** `feat(ui): explorateur, détails, aperçu, glisser-déposer`.

### Task 8 : Devoirs et paramètres

**Files:**
- Create: `Views/AssignmentsPage.xaml(.cs)`, `ViewModels/AssignmentsViewModel.cs`, `Views/SettingsPage.xaml(.cs)`, `ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Implémenter** en imitant `Assignments.tsx` (retards, à venir, anciens à part, statut/priorité/notes → `Queries.SetAssignment`) et `Settings.tsx` (dossier racine en lecture + ouvrir, dossier de données, synchro/reconnexion, réglages du widget, démarrage avec Windows → `Autostart.Set` + `settings.autostart`, thème, « Quitter complètement »).
- [ ] **Step 2: Vérifier** : capture comparée à `docs/devoirs.png` ; changer un statut, relancer l'appli, statut conservé.
- [ ] **Step 3: Commit** `feat(ui): devoirs et paramètres`.

### Task 9 : Fenêtre de connexion UPEC

**Files:**
- Create: `Views/LoginWindow.xaml(.cs)`

**Interfaces:**
- Produces: `static Task<string> LoginWindow.LoginAsync(string baseUrl, Action<string> log)` — branché comme `login` de `AppServices`.

- [ ] **Step 1: Implémenter** en imitant `auth-window.ts` : WebView2 avec dossier de données `%APPDATA%\Campus Manager\WebView2` ; `NavigationStarting` et `NewWindowRequested` interceptent `Sso.IsTokenRedirect` (annuler puis `ParseTokenUrl`) ; relance de `BuildLaunchUrl` (2 fois max) si on atterrit connecté hors de `/login` et de `/mobile/launch` ; repli sur `ExecuteScriptAsync` qui cherche `a[href^="moodlemobile://"]` sur la page de lancement ; journal limité à origine + chemin ; fermeture de la fenêtre = « Connexion annulée ». Après succès : `WebView2.Dispose()` puis fermeture (libère les processus WebView2).
- [ ] **Step 2: Vérifier** avec l'utilisateur (vraie connexion UPEC) : jeton reçu, synchro « 0 nouveau fichier », plus aucun processus `msedgewebview2` lié à l'appli 10 s après.
- [ ] **Step 3: Commit** `feat(app): connexion UPEC par WebView2`.

### Task 10 : Widget de bureau

**Files:**
- Create: `Widget/WidgetWindow.xaml(.cs)`, `Widget/WidgetViewModel.cs`, `Widget/WidgetController.cs`, `Widget/Win32.cs`
- Test: `tests/CampusManager.Tests/WidgetBoundsTests.cs`

**Interfaces:**
- Produces: `WidgetController(AppServices s)` — `WidgetSettings Get()`, `WidgetSettings Update(Action<WidgetSettings> patch)`, `void Create()`, `void Show()`, `void Hide()`, `void Destroy()`, `void SendBack()`, `bool IsVisible`. `static Rect WidgetBounds.Resolve(Rect? saved, IReadOnlyList<Rect> screens, Rect primaryWorkArea)` (défaut 340×520, à 24 px du coin haut droit, règle de visibilité de `widget.ts`).

- [ ] **Step 1: Écrire les tests** `BoundsOffScreen_FallBackToDefault`, `BoundsVisible_Kept`, `NoSavedBounds_TopRightDefault`.
- [ ] **Step 2: Lancer** → échec ; **Step 3: implémenter** `WidgetBounds` → verts.
- [ ] **Step 4: Implémenter** la fenêtre en imitant `widget.ts`, `widget.tsx`, `widget.css`, `win32.ts` : `WindowStyle=None`, `AllowsTransparency=True`, fond à 1 % d'opacité minimum pour recevoir les clics, `ShowInTaskbar=False`, `ShowActivated=False` ; après `SourceInitialized` : `WS_EX_TOOLWINDOW`, propriétaire = `FindWindowW("Progman")` via `SetWindowLongPtrW(GWLP_HWNDPARENT)` ; `SetWindowPos(HWND_BOTTOM, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)` à chaque activation (30 ms après) et quand une autre fenêtre de l'appli prend le focus ; clic à travers = `WS_EX_TRANSPARENT|WS_EX_LAYERED` ; `Closing` annulé sauf fermeture voulue (Alt+F4) ; plein écran testé toutes les 1,5 s ; poignée de redimensionnement (260-900 × 160-1400) ; repli 46 px ; position et taille enregistrées (`settings.widgetBounds`, 400 ms après le dernier mouvement). Acrylique : essayer `DWMWA_SYSTEMBACKDROP_TYPE` ; s'il ne marche pas avec la transparence WPF, retirer l'option de l'interface.
- [ ] **Step 5: Vérifier** sur le vrai bureau : capture comparée à `docs/widget.png` ; propriétaire = Progman, style outil, absent de la barre des tâches et d'Alt+Tab ; ne passe pas devant une autre fenêtre après un clic ; Alt+F4 sans effet ; survit à la fermeture de la fenêtre principale ; Ctrl+Alt+W/X marchent. **Mesurer** le jeu de travail privé widget seul (cible ≤ 70 Mo).
- [ ] **Step 6: Commit** `feat(app): widget de bureau`.

### Task 11 : Installateur, nettoyage, documentation

**Files:**
- Create: `installer/CampusManager.iss` (UTF-8 **avec BOM**), `installer/build.ps1`
- Delete: `src/main`, `src/renderer`, `src/core/*.ts`, `src/shared`, `scripts/`, `tests/*.ts`, `package.json`, `package-lock.json`, `tsconfig.json`
- Modify: `README.md`, `ARCHITECTURE.md`, `.gitignore` (`bin/`, `obj/`, `publish/`, `installer/Output/`)

- [ ] **Step 1: Écrire** `build.ps1 -Version x.y.z` : `dotnet publish src/CampusManager.App -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o publish/<version>` puis `ISCC`. `.iss` : `PrivilegesRequired=lowest`, `{userpf}\Campus Manager`, `AppId` fixe, `AppMutex=Local\CampusManager.Instance`, raccourcis Bureau et Démarrer, `[Registry]` valeur `Run` « Campus Manager » avec `ValueType: none; Flags: dontcreatekey uninsdeletevalue`, `[Run]` postinstall, `usPostUninstall` qui **ne supprime pas** les données sans confirmation.
- [ ] **Step 2: Construire** `installer\build.ps1 -Version 0.2.0` → `installer\Output\Campus Manager Setup 0.2.0.exe`.
- [ ] **Step 3: Supprimer** le code Electron, mettre à jour README (badges C#/.NET/WPF, commande de build, tests) et ARCHITECTURE (nouveau découpage, pourquoi C#). `dotnet test` toujours vert.
- [ ] **Step 4: Commit** `chore: installateur Inno Setup, retrait du code Electron, docs`.

### Task 12 : Bascule sur la vraie machine (avec l'utilisateur)

- [ ] **Step 1:** l'utilisateur redémarre le PC ; mesurer l'appli Electron à froid (heure de connexion → widget visible ; première ouverture de la fenêtre ; mémoire).
- [ ] **Step 2:** sauvegarder `%APPDATA%\Campus Manager\library.db` (copie datée dans le même dossier) ; désinstaller la version Electron (données conservées) ; installer `0.2.0`.
- [ ] **Step 3:** reconnexion UPEC unique ; synchro → « 0 nouveau(x) fichier(s) » ; nombres de fichiers, devoirs et favoris identiques à avant.
- [ ] **Step 4:** vérifier la valeur `Run` côté Windows (via `Win32_Process.Create`, piège MSIX) ; redémarrer ; refaire les mesures à froid et à chaud ; tableau avant/après dans README.
- [ ] **Step 5:** commit `docs: mesures avant/après` ; noter le résultat dans le coffre Obsidian (note « Campus Manager »).
