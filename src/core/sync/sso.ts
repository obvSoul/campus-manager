/**
 * Connexion SSO officielle de l'appli mobile Moodle. Le mot de passe est saisi sur la page UPEC, jamais
 * lu par nous ; on reçoit seulement `<scheme>://token=BASE64(md5(site+passport):::jeton[:::privé])`
 * et on vérifie le md5 pour être sûr que le jeton répond bien à NOTRE demande.
 */
import { createHash, randomBytes } from "node:crypto";
import { MoodleError } from "./moodle.js";

// EPREL (comme la plupart des Moodle) n'accepte que le schéma officiel de l'appli mobile : il ignore
// un schéma personnalisé et redirige toujours vers moodlemobile://. On s'y aligne.
export const URL_SCHEME = "moodlemobile";
const site = (base: string) => base.replace(/\/+$/, "");

export const newPassport = () => randomBytes(12).toString("hex");

export function buildLaunchUrl(base: string, passport: string, scheme = URL_SCHEME) {
  return `${site(base)}/admin/tool/mobile/launch.php?${new URLSearchParams({ service: "moodle_mobile_app", passport, urlscheme: scheme })}`;
}
export const isTokenRedirect = (url: string, scheme = URL_SCHEME) => url.toLowerCase().startsWith(`${scheme}://token=`);

export function parseTokenUrl(url: string, base: string, passport: string, scheme = URL_SCHEME) {
  if (!isTokenRedirect(url, scheme)) throw new MoodleError("Redirection inattendue", "badresponse");
  let decoded: string;
  try { decoded = Buffer.from(decodeURIComponent(url.slice(`${scheme}://token=`.length)), "base64").toString("utf8"); }
  catch { throw new MoodleError("Jeton illisible", "badresponse"); }
  const [siteid, token, privatetoken] = decoded.split(":::");
  if (siteid !== createHash("md5").update(site(base) + passport).digest("hex"))
    throw new MoodleError("Jeton refusé : il ne correspond pas à cette demande de connexion", "badpassport");
  if (!token) throw new MoodleError("Jeton vide", "badresponse");
  return { token, privatetoken };
}
