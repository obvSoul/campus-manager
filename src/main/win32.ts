/**
 * Appels Win32 pour le widget (via koffi, sans compilation).
 *
 * Intégration au bureau : la fenêtre du widget est « possédée » par la fenêtre du bureau (Progman),
 * comme les widgets Rainmeter en mode « sur le bureau ». Conséquences :
 *  - elle reste au niveau du bureau, sous les fenêtres des applications ;
 *  - elle reste visible avec Win+D (« afficher le bureau ») ;
 *  - elle reste cliquable (contrairement à une fenêtre placée DERRIÈRE les icônes).
 * Tout échec est sans gravité : le widget fonctionne alors comme une fenêtre basse normale.
 */
import { createRequire } from "node:module";

type Fn = (...a: unknown[]) => unknown;
interface Api {
  FindWindowW: Fn; SetWindowLongPtrW: Fn; SetWindowPos: Fn; GetForegroundWindow: Fn; GetWindowRect: Fn;
  GetClassNameW: Fn; MonitorFromWindow: Fn; GetMonitorInfoW: Fn; GetShellWindow: Fn;
}
let api: Api | null | undefined;

function load(): Api | null {
  if (api !== undefined) return api;
  try {
    const koffi = createRequire(__filename)("koffi");
    const u = koffi.load("user32.dll");
    const RECT = koffi.struct("RECT", { left: "long", top: "long", right: "long", bottom: "long" });
    koffi.struct("MONITORINFO", { cbSize: "uint32", rcMonitor: RECT, rcWork: RECT, dwFlags: "uint32" });
    api = {
      FindWindowW: u.func("intptr __stdcall FindWindowW(str16, str16)"),
      SetWindowLongPtrW: u.func("intptr __stdcall SetWindowLongPtrW(intptr, int, intptr)"),
      SetWindowPos: u.func("bool __stdcall SetWindowPos(intptr, intptr, int, int, int, int, uint)"),
      GetForegroundWindow: u.func("intptr __stdcall GetForegroundWindow()"),
      GetShellWindow: u.func("intptr __stdcall GetShellWindow()"),
      GetWindowRect: u.func("bool __stdcall GetWindowRect(intptr, _Out_ RECT *)"),
      GetClassNameW: u.func("int __stdcall GetClassNameW(intptr, _Out_ uint16 *, int)"),
      MonitorFromWindow: u.func("intptr __stdcall MonitorFromWindow(intptr, uint)"),
      GetMonitorInfoW: u.func("bool __stdcall GetMonitorInfoW(intptr, _Inout_ MONITORINFO *)"),
    };
  } catch {
    api = null;
  }
  return api;
}

/** HWND d'une fenêtre Electron (Buffer de getNativeWindowHandle). */
export const hwndOf = (b: Buffer): bigint => (b.length >= 8 ? b.readBigUInt64LE(0) : BigInt(b.readUInt32LE(0)));

const GWLP_HWNDPARENT = -8;
const HWND_BOTTOM = 1n;
const SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

/** Rattache le widget au bureau. Retourne false si l'intégration n'a pas pu se faire. */
export function attachToDesktop(hwnd: bigint): boolean {
  const a = load();
  if (!a) return false;
  const progman = a.FindWindowW("Progman", null) as bigint | number;
  if (!progman) return false;
  a.SetWindowLongPtrW(hwnd, GWLP_HWNDPARENT, progman);
  sendToBottom(hwnd);
  return true;
}

/** Replace le widget sous toutes les fenêtres d'applications (sans l'activer). */
export function sendToBottom(hwnd: bigint): void {
  load()?.SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
}

function className(a: Api, hwnd: unknown): string {
  const buf = new Uint16Array(64);
  const n = a.GetClassNameW(hwnd, buf, 64) as number;
  return String.fromCharCode(...buf.slice(0, n));
}

/** Une application (jeu, vidéo, présentation) occupe-t-elle tout l'écran ? */
export function isForegroundFullscreen(ownHwnds: bigint[] = []): boolean {
  const a = load();
  if (!a) return false;
  const fg = a.GetForegroundWindow() as bigint | number;
  if (!fg || ownHwnds.some((h) => BigInt(fg) === h) || fg === a.GetShellWindow()) return false;
  const cls = className(a, fg);
  if (["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"].includes(cls)) return false;
  const r = { left: 0, top: 0, right: 0, bottom: 0 };
  if (!a.GetWindowRect(fg, r)) return false;
  const mon = a.MonitorFromWindow(fg, 2 /* MONITOR_DEFAULTTONEAREST */);
  const mi = { cbSize: 40, /* sizeof(MONITORINFO) */ rcMonitor: { left: 0, top: 0, right: 0, bottom: 0 }, rcWork: { left: 0, top: 0, right: 0, bottom: 0 }, dwFlags: 0 };
  if (!a.GetMonitorInfoW(mon, mi)) return false;
  const m = mi.rcMonitor;
  return r.left <= m.left && r.top <= m.top && r.right >= m.right && r.bottom >= m.bottom;
}

export const win32Available = () => load() !== null;
