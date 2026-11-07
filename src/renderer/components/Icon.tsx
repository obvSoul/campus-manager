/** Jeu d'icônes cohérent (traits 1.6 px, 20×20). */
const P: Record<string, string> = {
  home: "M3 9.5 10 4l7 5.5V16a1 1 0 0 1-1 1h-4v-5H8v5H4a1 1 0 0 1-1-1z",
  files: "M4 3h7l5 5v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1zM11 3v5h5",
  book: "M4 4a1 1 0 0 1 1-1h11v13H5a1 1 0 0 0-1 1zM4 17a1 1 0 0 0 1 1h11",
  check: "M4 10.5 8 14.5 16 5.5",
  flask: "M8 3h4M9 3v5l-4.5 7.5A1 1 0 0 0 5.4 17h9.2a1 1 0 0 0 .9-1.5L11 8V3",
  calendar: "M4 5h12v12H4zM4 8h12M7 3v4M13 3v4",
  clock: "M10 3a7 7 0 1 0 0 14 7 7 0 0 0 0-14zM10 6v4l3 2",
  star: "m10 3 2.1 4.4 4.9.6-3.6 3.4.9 4.8L10 14l-4.3 2.2.9-4.8L3 8l4.9-.6z",
  archive: "M3 4h14v4H3zM4 8v8a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1V8M8 11h4",
  gear: "M10 7a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM10 2v2M10 16v2M2 10h2M16 10h2M4.3 4.3l1.4 1.4M14.3 14.3l1.4 1.4M4.3 15.7l1.4-1.4M14.3 5.7l1.4-1.4",
  search: "M9 3a6 6 0 1 0 0 12A6 6 0 0 0 9 3zM13.5 13.5 17 17",
  folder: "M3 5a1 1 0 0 1 1-1h4l2 2h6a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z",
  chevron: "m7 5 5 5-5 5",
  sync: "M16 8a6 6 0 0 0-11-2M4 4v3h3M4 12a6 6 0 0 0 11 2M16 16v-3h-3",
  list: "M7 5h10M7 10h10M7 15h10M3 5h.01M3 10h.01M3 15h.01",
  grid: "M3 3h6v6H3zM11 3h6v6h-6zM3 11h6v6H3zM11 11h6v6h-6z",
  large: "M3 3h14v10H3zM3 16h14",
  compact: "M3 4h14M3 8h14M3 12h14M3 16h14",
  close: "M5 5l10 10M15 5 5 15",
  open: "M11 3h6v6M17 3l-8 8M14 12v4a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h4",
  sun: "M10 6a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM10 1.5v2M10 16.5v2M1.5 10h2M16.5 10h2M4 4l1.4 1.4M14.6 14.6 16 16M4 16l1.4-1.4M14.6 5.4 16 4",
  moon: "M16 12.5A7 7 0 0 1 7.5 4a7 7 0 1 0 8.5 8.5z",
  alert: "M10 3 2 17h16zM10 8v4M10 14.5v.01",
};
export function Icon({ name, size = 18 }: { name: string; size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth={1.6}
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d={P[name] ?? P.files} />
    </svg>
  );
}
