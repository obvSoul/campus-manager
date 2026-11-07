/** Contrat entre l'interface et le processus principal. L'interface ne touche jamais au disque ni à EPREL. */

export interface CourseNode { id: number; name: string; folder: string; semester: string; categories: { name: string; count: number }[] }
export interface SemesterNode { name: string; courses: CourseNode[] }

export interface FileItem {
  id: number; name: string; ext: string; category: string; courseId: number | null; course: string;
  semester: string; size: number; addedAt: number; favorite: boolean; missing: boolean; remoteDeleted: boolean; path: string;
}
export interface FileDetails extends FileItem {
  modifiedAt: number | null; locallyModified: boolean; resourceName: string | null; section: string | null;
  description: string | null; fromEprel: boolean; absPath: string;
}

export type View =
  | { kind: "home" } | { kind: "all" } | { kind: "recent" } | { kind: "favorites" }
  | { kind: "course"; courseId: number; category?: string } | { kind: "assignments" } | { kind: "work" }
  | { kind: "search"; query: string } | { kind: "settings" };

export interface FileQuery { courseId?: number; category?: string; categories?: string[]; search?: string; recent?: boolean; favorites?: boolean; limit?: number }

export type AssignmentStatus = "todo" | "doing" | "done";
export interface AssignmentItem {
  id: number; name: string; course: string; courseId: number | null; description: string | null;
  dueAt: number | null; publishedAt: number | null; status: AssignmentStatus; late: boolean; priority: number; notes: string | null;
  /** Rendu sur EPREL (information récupérée, lecture seule). */
  submitted: boolean;
  /** Échéance antérieure au début du cours (reste d'une année précédente). */
  old: boolean;
}

export interface Dashboard {
  upcoming: AssignmentItem[]; recentFiles: FileItem[];
  counts: { files: number; courses: number; td: number; tp: number; assignmentsOpen: number; assignmentsDone: number; assignmentsLate: number; unsorted: number };
  perCourse: { course: string; done: number; total: number }[];
  lastSync: { at: number; ok: boolean; added: number; errors: number } | null;
}

export interface WidgetSettings {
  enabled: boolean;
  sections: { assignments: boolean; work: boolean; recent: boolean; progress: boolean };
  opacity: number;              // 0 (entièrement transparent) à 1 (fond du widget)
  theme: "dark" | "light";
  clickThrough: boolean;        // les clics traversent le widget
  autoHideFullscreen: boolean;  // masqué quand une appli est en plein écran
  blur: boolean;                // effet de flou Windows (acrylique)
  collapsed: boolean;
}
export const DEFAULT_WIDGET: WidgetSettings = {
  enabled: true, sections: { assignments: true, work: true, recent: true, progress: true },
  opacity: 0, theme: "dark", clickThrough: false, autoHideFullscreen: true, blur: false, collapsed: false,
};
export interface WidgetData {
  assignments: AssignmentItem[]; work: FileItem[]; recent: FileItem[];
  progress: { done: number; open: number; late: number }; lastSync: number | null;
}

export interface SyncState { running: boolean; message: string; lastAt: number | null; needsLogin: boolean }

export interface CampusApi {
  tree(): Promise<SemesterNode[]>;
  files(q: FileQuery): Promise<FileItem[]>;
  details(id: number): Promise<FileDetails>;
  open(id: number): Promise<string | null>;
  reveal(id: number): Promise<void>;
  rename(id: number, name: string): Promise<FileItem>;
  move(id: number, to: { courseId?: number; category?: string }): Promise<FileItem>;
  favorite(id: number, on: boolean): Promise<void>;
  trash(id: number): Promise<boolean>;
  copyPath(id: number): Promise<void>;
  createFolder(courseId: number, category: string, name: string): Promise<void>;
  assignments(): Promise<AssignmentItem[]>;
  setAssignment(id: number, patch: Partial<Pick<AssignmentItem, "status" | "priority" | "notes">>): Promise<void>;
  dashboard(): Promise<Dashboard>;
  settings(): Promise<{ root: string; dataDir: string }>;
  openRoot(): Promise<void>;
  sync(relogin?: boolean): Promise<SyncState>;
  syncState(): Promise<SyncState>;
  onSyncState(cb: (s: SyncState) => void): () => void;
  previewUrl(id: number): string;
  // Widget et cycle de vie
  widgetData(): Promise<WidgetData>;
  widgetSettings(): Promise<WidgetSettings>;
  setWidgetSettings(p: Partial<WidgetSettings>): Promise<WidgetSettings>;
  openMain(hash?: string): Promise<void>;
  widget(cmd: "collapse" | "expand" | "hide" | "show" | "quit"): Promise<void>;
  /** Redimensionnement par la poignée (les fenêtres transparentes n'ont pas de bord redimensionnable). */
  widgetResize(width: number, height: number): Promise<void>;
  /** Glisser-déposer */
  pathForFile(f: File): string;
  drop(paths: string[], target: { courseId: number; category?: string }): Promise<{ imported: number; moved: number; duplicates: number; skipped: string[] }>;
  /** Commence un glisser natif d'un fichier vers l'Explorateur, le Bureau, un mail… */
  startDrag(id: number): void;
  /** Lancement automatique (widget seul) à l'ouverture de session Windows. */
  autostart(): Promise<boolean>;
  setAutostart(on: boolean): Promise<boolean>;
  onChanged(cb: () => void): () => void;
  onWidgetSettings(cb: (s: WidgetSettings) => void): () => void;
}

declare global { interface Window { campus: CampusApi } }
