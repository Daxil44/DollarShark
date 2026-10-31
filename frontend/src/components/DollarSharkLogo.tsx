export default function DollarSharkLogo() {
  return <span className="dollarshark-brand" aria-label="DollarShark">
    <svg className="dollarshark-mark" viewBox="0 0 48 48" aria-hidden="true" focusable="false">
      <rect x="1" y="1" width="46" height="46" rx="14" fill="#123c46" />
      <path d="M8 27c5-7 12-11 23-10l8 4-6 2 5 4-10 1-7 6H12l5-6-9-1Z" fill="#f4fbf8" />
      <path d="m21 18 5-9 5 9" fill="#48c78e" />
      <circle cx="33" cy="22" r="1.35" fill="#123c46" />
      <path d="M23 23.4h3.2a1.7 1.7 0 0 1 0 3.4h-3.1a1.7 1.7 0 0 0 0 3.4h3.4M24.7 21.6v10.1" fill="none" stroke="#48c78e" strokeWidth="1.8" strokeLinecap="round" />
    </svg>
    <span className="dollarshark-wordmark">Dollar<span>Shark</span></span>
  </span>;
}
