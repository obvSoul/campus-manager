/**
 * Client de l'API officielle Moodle (services web « moodle_mobile_app », la même API que l'appli mobile).
 * C'est le SEUL fichier qui parle à EPREL avec le moteur de sync. Le reste du logiciel n'en dépend pas.
 */
export class MoodleError extends Error {
  constructor(message: string, readonly code: string) { super(message); this.name = "MoodleError"; }
  /** Jeton expiré/refusé : il faut se reconnecter. */
  get isAuthError() { return ["invalidtoken", "invalidlogin", "accessexception", "forcepasswordchangenotice"].includes(this.code); }
  /** Réseau ou serveur indisponible : réessayer plus tard. */
  get isTransient() { return this.code === "network" || this.code === "http5xx"; }
}

export interface MoodleCourse { id: number; fullname: string; shortname: string; startdate?: number; enddate?: number; category?: number }
export interface ModuleContent { type: string; filename: string; filesize: number; fileurl: string; timemodified: number }
export interface CourseModule { id: number; name: string; modname: string; description?: string; url?: string; contents?: ModuleContent[]; added?: number }
export interface CourseSection { id: number; name: string; summary?: string; modules: CourseModule[] }
export interface Assignment { id: number; cmid: number; course: number; name: string; intro?: string; duedate: number; allowsubmissionsfromdate: number; timemodified: number }
export interface SiteInfo { userid: number; fullname: string; release?: string; functions: { name: string }[] }

/** Échéance telle qu'affichée dans la chronologie EPREL (tient compte des prolongations/dérogations). */
export interface ActionEvent { modulename: string; instance: number; timesort: number; url?: string }

/** Interface utilisée par le moteur de sync : permet de tester sans réseau. */
export interface MoodleApi {
  courses(): Promise<MoodleCourse[]>;
  contents(courseId: number): Promise<CourseSection[]>;
  assignments(courseIds: number[]): Promise<Assignment[]>;
  download(fileurl: string): Promise<Uint8Array>;
  /** Facultatif : état de rendu (« submitted », « new », « draft »…). */
  submissionStatus?(assignId: number): Promise<string | null>;
  /** Facultatif : échéances actives de la chronologie. */
  actionEvents?(fromTime: number): Promise<ActionEvent[]>;
}

export class MoodleClient implements MoodleApi {
  private userid?: number;
  constructor(private readonly base: string, private readonly token: string,
              private readonly fetchImpl: typeof fetch = fetch, private readonly timeoutMs = 30_000) {}

  static async request(fetchImpl: typeof fetch, url: string, init: RequestInit, timeoutMs: number): Promise<unknown> {
    let res: Response;
    try { res = await fetchImpl(url, { ...init, signal: AbortSignal.timeout(timeoutMs) }); }
    catch (e) { throw new MoodleError(`Réseau indisponible : ${(e as Error).message}`, "network"); }
    if (res.status >= 500) throw new MoodleError(`Serveur EPREL en erreur (${res.status})`, "http5xx");
    if (!res.ok) throw new MoodleError(`Réponse HTTP ${res.status}`, "http");
    let data: unknown;
    try { data = await res.json(); } catch { throw new MoodleError("Réponse non JSON (service désactivé ?)", "badresponse"); }
    const d = data as { error?: string; errorcode?: string; exception?: string; message?: string };
    if (d && !Array.isArray(d) && (d.exception || d.error)) throw new MoodleError(d.message ?? d.error ?? "Erreur Moodle", d.errorcode ?? "moodle");
    return data;
  }

  private call<T>(fn: string, params: Record<string, string | number | number[]> = {}): Promise<T> {
    const body = new URLSearchParams({ wstoken: this.token, wsfunction: fn, moodlewsrestformat: "json" });
    for (const [k, v] of Object.entries(params)) {
      if (Array.isArray(v)) v.forEach((x, i) => body.append(`${k}[${i}]`, String(x))); else body.set(k, String(v));
    }
    return MoodleClient.request(this.fetchImpl, `${this.base}/webservice/rest/server.php`, { method: "POST", body }, this.timeoutMs) as Promise<T>;
  }

  siteInfo() { return this.call<SiteInfo>("core_webservice_get_site_info"); }
  async courses() {
    this.userid ??= (await this.siteInfo()).userid;
    return this.call<MoodleCourse[]>("core_enrol_get_users_courses", { userid: this.userid });
  }
  contents(courseId: number) { return this.call<CourseSection[]>("core_course_get_contents", { courseid: courseId }); }
  async assignments(courseIds: number[]) {
    if (!courseIds.length) return [];
    const r = await this.call<{ courses: { id: number; assignments: Omit<Assignment, "course">[] }[] }>("mod_assign_get_assignments", { courseids: courseIds });
    return r.courses.flatMap((c) => c.assignments.map((a) => ({ ...a, course: c.id })));
  }

  async submissionStatus(assignId: number): Promise<string | null> {
    const r = await this.call<{ lastattempt?: { submission?: { status?: string }; teamsubmission?: { status?: string } } }>(
      "mod_assign_get_submission_status", { assignid: assignId });
    return r.lastattempt?.submission?.status ?? r.lastattempt?.teamsubmission?.status ?? null;
  }

  async actionEvents(fromTime: number): Promise<ActionEvent[]> {
    const out: ActionEvent[] = [];
    let after: number | undefined;
    for (let page = 0; page < 10; page++) { // pagination de la chronologie
      const p: Record<string, string | number> = { timesortfrom: fromTime, limitnum: 50 };
      if (after) p.aftereventid = after;
      const r = await this.call<{ events: (ActionEvent & { id: number })[] }>("core_calendar_get_action_events_by_timesort", p);
      out.push(...r.events.map(({ modulename, instance, timesort, url }) => ({ modulename, instance, timesort, url })));
      if (r.events.length < 50) break;
      after = r.events[r.events.length - 1]!.id;
    }
    return out;
  }

  /** Télécharge un fichier de cours. Le jeton n'apparaît dans aucun message d'erreur ni journal. */
  async download(fileurl: string): Promise<Uint8Array> {
    // Avec les services web, Moodle donne déjà des liens en /webservice/pluginfile.php ; on ne double pas.
    const u = new URL(fileurl.includes("/webservice/pluginfile.php") ? fileurl : fileurl.replace("/pluginfile.php", "/webservice/pluginfile.php"));
    u.searchParams.set("token", this.token);
    let res: Response;
    try { res = await this.fetchImpl(u, { signal: AbortSignal.timeout(this.timeoutMs * 4) }); }
    catch (e) { throw new MoodleError(`Téléchargement impossible : ${(e as Error).message.replace(this.token, "***")}`, "network"); }
    if (res.status >= 500) throw new MoodleError(`Serveur EPREL en erreur (${res.status})`, "http5xx");
    if (!res.ok) throw new MoodleError(`Téléchargement refusé (HTTP ${res.status})`, "http");
    const buf = new Uint8Array(await res.arrayBuffer());
    if (res.headers.get("content-type")?.includes("application/json")) {
      // Moodle répond en JSON quand le jeton n'a pas le droit de lire le fichier.
      const err = JSON.parse(new TextDecoder().decode(buf)) as { errorcode?: string; error?: string };
      throw new MoodleError(err.error ?? "Accès au fichier refusé", err.errorcode ?? "accessexception");
    }
    return buf;
  }
}
