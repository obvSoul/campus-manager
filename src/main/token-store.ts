/** Jeton chiffré par Windows (DPAPI via safeStorage) : lisible seulement par ta session Windows. */
import { app, safeStorage } from "electron";
import { existsSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const file = () => join(app.getPath("userData"), "session.bin");

export function saveToken(token: string): void {
  if (!safeStorage.isEncryptionAvailable()) throw new Error("Chiffrement Windows indisponible : jeton non enregistré");
  writeFileSync(file(), safeStorage.encryptString(token));
}
export function loadToken(): string | null {
  if (!existsSync(file())) return null;
  try { return safeStorage.decryptString(readFileSync(file())); } catch { return null; }
}
export function clearToken(): void { rmSync(file(), { force: true }); }
