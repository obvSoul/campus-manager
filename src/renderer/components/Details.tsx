import { useEffect, useState } from "react";
import type { FileDetails } from "../../shared/api.js";
import { formatDate, formatSize, previewable } from "../util.js";
import { FileIcon } from "./FileIcon.js";
import { Icon } from "./Icon.js";

/** Panneau de détails : aperçu local + métadonnées. */
export function Details({ id, onClose, version }: { id: number; onClose: () => void; version: number }) {
  const [d, setD] = useState<FileDetails | null>(null);
  const [err, setErr] = useState<string | null>(null);
  useEffect(() => { setD(null); setErr(null); window.campus.details(id).then(setD, (e) => setErr(String(e))); }, [id, version]);

  if (err) return <aside className="details"><p className="muted">Détails indisponibles.</p></aside>;
  if (!d) return <aside className="details" />;
  const url = window.campus.previewUrl(d.id);
  const img = ["png", "jpg", "jpeg", "gif", "svg", "webp", "bmp"].includes(d.ext);

  return (
    <aside className="details">
      <div className="details-head">
        <FileIcon ext={d.ext} size={30} />
        <div className="ellipsis title" title={d.name}>{d.name}</div>
        <button className="icon-btn" onClick={onClose} title="Fermer"><Icon name="close" size={16} /></button>
      </div>
      <div className="preview">
        {d.missing ? <div className="nopreview"><Icon name="alert" size={28} />Fichier introuvable sur le disque</div>
          : img ? <img src={url} alt="" />
          : previewable(d.ext) ? <iframe src={url} title="Aperçu" />
          : <div className="nopreview"><FileIcon ext={d.ext} size={54} />Pas d'aperçu pour ce type de fichier</div>}
      </div>
      <div className="details-actions">
        <button className="btn primary" onClick={() => window.campus.open(d.id)}><Icon name="open" size={15} /> Ouvrir</button>
        <button className="btn ghost" onClick={() => window.campus.reveal(d.id)}>Emplacement</button>
      </div>
      <dl className="meta">
        <dt>Matière</dt><dd>{d.course || "—"}</dd>
        <dt>Semestre</dt><dd>{d.semester || "—"}</dd>
        <dt>Type</dt><dd>{d.category}</dd>
        <dt>Extension</dt><dd>.{d.ext || "?"}</dd>
        <dt>Taille</dt><dd>{formatSize(d.size)}</dd>
        <dt>Ajouté le</dt><dd>{formatDate(d.addedAt, true)}</dd>
        <dt>Modifié le</dt><dd>{formatDate(d.modifiedAt, true)}</dd>
        <dt>Origine</dt><dd>{d.fromEprel ? "EPREL" : "Personnel"}{d.remoteDeleted ? " (retiré d'EPREL)" : ""}</dd>
        {d.locallyModified && <><dt>État</dt><dd className="warn-text">Modifié par toi : jamais écrasé par la synchronisation</dd></>}
        {d.resourceName && <><dt>Ressource</dt><dd>{d.resourceName}</dd></>}
        {d.section && <><dt>Section</dt><dd>{d.section}</dd></>}
        {d.description && <><dt>Description</dt><dd className="desc">{d.description}</dd></>}
        <dt>Chemin</dt><dd className="path">{d.path}</dd>
      </dl>
    </aside>
  );
}
