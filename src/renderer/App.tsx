import { useCallback, useEffect, useMemo, useState } from "react";
import type { FileItem, FileQuery, SemesterNode, SyncState, View } from "../shared/api.js";
import { Details } from "./components/Details.js";
import { describeDrop, useDropTarget, type DropResult, type DropTarget } from "./dnd.js";
import { FileBrowser, type Mode } from "./components/FileBrowser.js";
import { Icon } from "./components/Icon.js";
import { Sidebar } from "./components/Sidebar.js";
import { Assignments } from "./pages/Assignments.js";
import { Home } from "./pages/Home.js";
import { Settings } from "./pages/Settings.js";

const store = { get: (k: string, d: string) => { try { return localStorage.getItem(k) ?? d; } catch { return d; } },
  set: (k: string, v: string) => { try { localStorage.setItem(k, v); } catch { /* ignoré */ } } };

/** Vue <-> fragment d'URL (#course/12/TD), pour pouvoir ouvrir une vue précise. */
function parseHash(h: string): View {
  const [k, a, b] = decodeURIComponent(h.replace(/^#/, "")).split("/");
  if (k === "course" && a) return { kind: "course", courseId: Number(a), category: b || undefined };
  if (k === "search") return { kind: "search", query: a ?? "" };
  if (["all", "recent", "favorites", "assignments", "work", "settings"].includes(k ?? "")) return { kind: k } as View;
  return { kind: "home" };
}
const toHash = (v: View) => v.kind === "course" ? `course/${v.courseId}${v.category ? `/${encodeURIComponent(v.category)}` : ""}`
  : v.kind === "search" ? `search/${encodeURIComponent(v.query)}` : v.kind;

export function App() {
  const [view, setView] = useState<View>(() => parseHash(location.hash));
  const [tree, setTree] = useState<SemesterNode[]>([]);
  const [files, setFiles] = useState<FileItem[]>([]);
  const [selected, setSelected] = useState<number | null>(null);
  const [mode, setModeState] = useState<Mode>(() => store.get("mode", "list") as Mode);
  const [theme, setThemeState] = useState(() => store.get("theme", "dark"));
  const [query, setQuery] = useState(view.kind === "search" ? view.query : "");
  const [sync, setSync] = useState<SyncState>({ running: false, message: "", lastAt: null, needsLogin: false });
  const [version, setVersion] = useState(0);
  const [toastMsg, setToast] = useState<string | null>(null);

  const go = useCallback((v: View) => { setView(v); setSelected(null); location.hash = toHash(v); if (v.kind !== "search") setQuery(""); }, []);
  const refresh = useCallback(() => setVersion((n) => n + 1), []);
  const toast = useCallback((m: string) => { setToast(m); setTimeout(() => setToast((t) => (t === m ? null : t)), 3200); }, []);
  const setMode = (m: Mode) => { setModeState(m); store.set("mode", m); };
  const setTheme = (t: string) => { setThemeState(t); store.set("theme", t); };

  useEffect(() => { document.documentElement.dataset.theme = theme; }, [theme]);
  useEffect(() => { const f = () => setView(parseHash(location.hash)); addEventListener("hashchange", f); return () => removeEventListener("hashchange", f); }, []);
  useEffect(() => { window.campus.syncState().then(setSync); return window.campus.onSyncState((s) => { setSync(s); if (!s.running) refresh(); }); }, [refresh]);
  // Une action faite ailleurs (ex. devoir coché dans le widget) met cette fenêtre à jour.
  useEffect(() => window.campus.onChanged(refresh), [refresh]);
  useEffect(() => { window.campus.tree().then(setTree); }, [version]);

  const fileQuery = useMemo((): FileQuery | null => {
    switch (view.kind) {
      case "all": return {};
      case "recent": return { recent: true, limit: 60 };
      case "favorites": return { favorites: true };
      case "work": return { categories: ["TD", "TP", "Exercices", "Corrections"] };
      case "course": return { courseId: view.courseId, category: view.category };
      case "search": return view.query === "À classer" ? { category: "À classer" } : { search: view.query };
      default: return null;
    }
  }, [view]);
  useEffect(() => { if (fileQuery) window.campus.files(fileQuery).then(setFiles); }, [fileQuery, version]);

  // Recherche globale : déclenchée en tapant (après une courte pause).
  useEffect(() => {
    if (!query.trim()) { if (view.kind === "search") go({ kind: "all" }); return; }
    const t = setTimeout(() => { setView({ kind: "search", query }); location.hash = toHash({ kind: "search", query }); }, 220);
    return () => clearTimeout(t);
  }, [query]);

  // Glisser-déposer : résultat commun à toutes les zones de dépôt.
  const onDropped = useCallback((r: DropResult | Error) => {
    if (r instanceof Error) toast(`Dépôt impossible : ${r.message.replace(/^Error invoking remote method '[^']+': (Error: )?/, "")}`);
    else { toast(describeDrop(r)); refresh(); }
  }, [toast, refresh]);
  const dropTarget: DropTarget | null = view.kind === "course" ? { courseId: view.courseId, category: view.category } : null;
  const zone = useDropTarget(dropTarget, onDropped);
  // Un dépôt hors zone ne doit rien faire (pas d'ouverture du fichier dans la fenêtre).
  useEffect(() => {
    const stop = (e: globalThis.DragEvent) => e.preventDefault();
    addEventListener("dragover", stop); addEventListener("drop", stop);
    return () => { removeEventListener("dragover", stop); removeEventListener("drop", stop); };
  }, []);

  const runSync = (relogin = false) => { window.campus.sync(relogin).then((s) => { setSync(s); refresh(); if (s.message) toast(s.message); }); };

  const course = view.kind === "course" ? tree.flatMap((s) => s.courses.map((c) => ({ ...c, sem: s.name }))).find((c) => c.id === view.courseId) : undefined;
  const crumbs: string[] =
    view.kind === "course" ? [course?.sem ?? "", course?.folder ?? "", ...(view.category ? [view.category] : [])]
    : [{ home: "Accueil", all: "Tous les fichiers", recent: "Récemment ajoutés", favorites: "Favoris", assignments: "Devoirs",
         work: "TD / TP", settings: "Paramètres", search: `Recherche « ${view.kind === "search" ? view.query : ""} »` }[view.kind]];
  const isFiles = fileQuery != null;

  return (
    <div className="app">
      <Sidebar view={view} tree={tree} go={go} onDropped={onDropped} />
      <div className="main">
        <header className="topbar">
          <div className="crumbs">
            {crumbs.map((c, i) => (
              <span key={i} className={i === crumbs.length - 1 ? "cur" : ""}
                onClick={() => view.kind === "course" && i === 1 && go({ kind: "course", courseId: view.courseId })}>
                {i > 0 && <Icon name="chevron" size={12} />}{c}
              </span>
            ))}
            {isFiles && <span className="muted small count-badge">{files.length}</span>}
          </div>
          <label className="search">
            <Icon name="search" size={16} />
            <input placeholder="Rechercher un cours, un fichier, une matière…" value={query} onChange={(e) => setQuery(e.target.value)}
              onKeyDown={(e) => e.key === "Escape" && setQuery("")} />
          </label>
          {isFiles && (
            <div className="modes">
              {(["list", "grid", "large", "compact"] as Mode[]).map((m) => (
                <button key={m} className={`icon-btn ${mode === m ? "on" : ""}`} onClick={() => setMode(m)}
                  title={{ list: "Liste détaillée", grid: "Grille", large: "Grandes icônes", compact: "Vue compacte" }[m]}>
                  <Icon name={m} size={16} />
                </button>
              ))}
            </div>
          )}
          <button className={`icon-btn sync ${sync.running ? "spin" : ""}`} title={sync.running ? sync.message : "Synchroniser avec EPREL"} onClick={() => runSync()}>
            <Icon name="sync" size={17} />
          </button>
          <button className="icon-btn" title="Thème" onClick={() => setTheme(theme === "dark" ? "light" : "dark")}>
            <Icon name={theme === "dark" ? "sun" : "moon"} size={16} />
          </button>
        </header>

        <div className="content">
          <div className={`scroll ${zone.over ? "drop-zone" : ""}`} {...zone.props}>
            {zone.over && (
              <div className="drop-hint">
                <div>Déposer dans <b>{course?.folder}</b>{view.kind === "course" && view.category ? <> / <b>{view.category}</b></> : <> (dossier « Personnel »)</>}</div>
                <span>Fichier de Windows : copié · fichier de la bibliothèque : déplacé</span>
              </div>
            )}
            {view.kind === "home" && <Home go={go} sync={sync} version={version} onSync={() => runSync()} />}
            {view.kind === "assignments" && <Assignments version={version} />}
            {view.kind === "settings" && <Settings sync={sync} onSync={runSync} theme={theme} setTheme={setTheme} />}
            {isFiles && (
              <FileBrowser files={files} tree={tree} mode={mode} selected={selected} onSelect={setSelected}
                showCourse={view.kind !== "course"} onChanged={refresh} toast={toast}
                empty={view.kind === "favorites" ? "Aucun favori : clic droit sur un fichier > Ajouter aux favoris."
                  : view.kind === "search" ? "Aucun résultat." : "Aucun fichier ici."} />
            )}
          </div>
          {isFiles && selected != null && <Details id={selected} version={version} onClose={() => setSelected(null)} />}
        </div>
      </div>
      {toastMsg && <div className="toast">{toastMsg}</div>}
    </div>
  );
}
