import { Link } from 'react-router-dom';
import DollarSharkLogo from '../components/DollarSharkLogo';
import './landing.css';

const features = [
  {
    title: 'Statement import',
    text: 'Upload bank and credit card CSVs, preview every row, fix what looks off, then confirm. Nothing is saved until you say so.',
    icon: <path d="M12 3v12m0 0-4-4m4 4 4-4M5 17v2a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-2" />,
  },
  {
    title: 'Duplicate detection',
    text: 'Re-imported the same statement? Likely duplicates are flagged for review so your totals stay honest.',
    icon: <><rect x="8" y="8" width="12" height="12" rx="2" /><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2" /></>,
  },
  {
    title: 'Linked transfers',
    text: 'Moving money between accounts is not spending. Match both sides of a transfer and keep it out of your expenses.',
    icon: <path d="M7 7h11l-3-3m3 3-3 3M17 17H6l3 3m-3-3 3-3" />,
  },
  {
    title: 'Dashboards & insights',
    text: 'Income vs expenses, savings rate, spending by category, and balance history for every account.',
    icon: <path d="M4 20V10m6 10V4m6 16v-7m4 7H3" />,
  },
  {
    title: 'Every account type',
    text: 'Chequing, savings, credit cards and investments, with assets and liabilities shown the right way round.',
    icon: <><rect x="3" y="6" width="18" height="13" rx="2" /><path d="M3 10h18M7 15h3" /></>,
  },
  {
    title: 'Private by design',
    text: 'Your data is isolated to your account. Statement files stay off the public web, and full card numbers are never stored.',
    icon: <><rect x="5" y="11" width="14" height="10" rx="2" /><path d="M8 11V7a4 4 0 0 1 8 0v4" /></>,
  },
];

const securityPoints = [
  { text: "Each user's accounts and transactions are fully isolated", icon: <><circle cx="12" cy="8" r="4" /><path d="M4 21a8 8 0 0 1 16 0" /></> },
  { text: 'HttpOnly session cookies with CSRF protection', icon: <path d="M12 3 4 6v6c0 5 3.4 8.4 8 9 4.6-.6 8-4 8-9V6l-8-3Zm-3 9 2 2 4-4" /> },
  { text: 'Statement files stored privately, never served back over the web', icon: <path d="M4 7a2 2 0 0 1 2-2h4l2 2h6a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V7Z" /> },
  { text: 'Only the last four digits of a card are ever stored', icon: <><rect x="3" y="6" width="18" height="13" rx="2" /><path d="M3 10h18M15 15h3" /></> },
];

const steps = [
  { title: 'Add your accounts', text: 'Name each account, pick its type and set an opening balance.' },
  { title: 'Upload a statement', text: 'Drop in a CSV export from your bank or card provider.' },
  { title: 'Review & import', text: 'Check the preview, skip duplicates and confirm in one click.' },
];

function HeroIllustration() {
  const bars = [62, 48, 70, 55, 82, 66];
  return <div className="hero-art" aria-hidden="true">
    <div className="mock-window">
      <div className="mock-topbar"><span /><span /><span /><div className="mock-url">dollarshark · dashboard</div></div>
      <div className="mock-body">
        <div className="mock-stats">
          <div className="mock-stat"><small>Income</small><strong className="pos">$4,200</strong><em className="pos">▲ 4.2%</em></div>
          <div className="mock-stat"><small>Expenses</small><strong className="neg">$1,830</strong><em className="pos">▼ 22%</em></div>
          <div className="mock-stat"><small>Saved</small><strong>56%</strong><em className="pos">▲ 8 pts</em></div>
        </div>
        <div className="mock-charts">
          <div className="mock-card">
            <small>Income vs expenses</small>
            <svg viewBox="0 0 220 110" className="mock-bars">
              {[0, 1, 2, 3].map(line => <line key={line} x1="0" x2="220" y1={20 + line * 28} y2={20 + line * 28} />)}
              {bars.map((height, index) => <g key={index} transform={`translate(${12 + index * 35} 0)`}>
                <rect className="bar-in" x="0" y={104 - height} width="11" height={height} rx="3" style={{ animationDelay: `${index * 90}ms` }} />
                <rect className="bar-out" x="13" y={104 - height * 0.55} width="11" height={height * 0.55} rx="3" style={{ animationDelay: `${index * 90 + 40}ms` }} />
              </g>)}
            </svg>
          </div>
          <div className="mock-card mock-donut-card">
            <small>Spending</small>
            <svg viewBox="0 0 120 120" className="mock-donut">
              <circle cx="60" cy="60" r="44" className="donut-track" />
              <circle cx="60" cy="60" r="44" className="seg seg-a" strokeDasharray="124 400" />
              <circle cx="60" cy="60" r="44" className="seg seg-b" strokeDasharray="62 400" strokeDashoffset="-128" />
              <circle cx="60" cy="60" r="44" className="seg seg-c" strokeDasharray="44 400" strokeDashoffset="-194" />
              <circle cx="60" cy="60" r="44" className="seg seg-d" strokeDasharray="30 400" strokeDashoffset="-242" />
              <text x="60" y="57" textAnchor="middle" className="donut-label">Spent</text>
              <text x="60" y="73" textAnchor="middle" className="donut-value">$1.8k</text>
            </svg>
          </div>
        </div>
        <div className="mock-card mock-line-card">
          <small>Cash balance</small>
          <svg viewBox="0 0 440 90" preserveAspectRatio="none" className="mock-line">
            <defs><linearGradient id="hero-area" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stopColor="#48c78e" stopOpacity=".35" /><stop offset="1" stopColor="#48c78e" stopOpacity="0" /></linearGradient></defs>
            <path d="M0 72 C40 66 70 70 110 58 S180 50 220 44 S300 40 330 30 S400 16 440 12 V90 H0 Z" fill="url(#hero-area)" />
            <path className="line-path" d="M0 72 C40 66 70 70 110 58 S180 50 220 44 S300 40 330 30 S400 16 440 12" />
          </svg>
        </div>
      </div>
    </div>
    <div className="float-chip chip-a"><span className="chip-icon ok">✓</span><div><strong>Duplicate caught</strong><small>FreshMart · $180.00 skipped</small></div></div>
    <div className="float-chip chip-b"><span className="chip-icon link">⇄</span><div><strong>Transfer linked</strong><small>Chequing → Savings</small></div></div>
  </div>;
}

function SharkWave() {
  return <div className="shark-wave" aria-hidden="true">
    <svg className="fin" viewBox="0 0 60 40"><path d="M2 40C14 30 26 10 46 2c-6 12-6 26 12 38Z" /></svg>
    <svg className="wave" viewBox="0 0 1440 120" preserveAspectRatio="none">
      <path className="wave-back" d="M0 64c120 22 240 34 360 24s240-46 360-46 240 40 360 50 240-14 360-30v58H0Z" />
      <path className="wave-front" d="M0 80c160 26 320 30 480 12s320-48 480-40 320 44 480 40v28H0Z" />
    </svg>
  </div>;
}

export default function LandingPage() {
  return <div className="landing">
    <header className="landing-nav">
      <div className="landing-container nav-inner">
        <Link to="/" className="brand-link" aria-label="DollarShark home"><DollarSharkLogo /></Link>
        <nav className="nav-links" aria-label="Page sections">
          <a href="#features">Features</a><a href="#how">How it works</a><a href="#security">Security</a>
        </nav>
        <div className="nav-actions">
          <Link to="/login" className="btn-ghost">Sign in</Link>
          <Link to="/register" className="btn-primary-ds">Get started</Link>
        </div>
      </div>
    </header>

    <section className="hero">
      <div className="hero-glow" aria-hidden="true" />
      <div className="landing-container hero-inner">
        <div className="hero-copy">
          <span className="hero-pill"><span className="dot" />Personal finance, made clearer</span>
          <h1>Take a bite out of <span className="accent">money chaos.</span></h1>
          <p className="lead-text">Import your bank and credit card statements, catch duplicates, link transfers, and see exactly where every dollar swims — all in one private workspace.</p>
          <div className="hero-ctas">
            <Link to="/register" className="btn-primary-ds lg">Create free account <span aria-hidden="true">→</span></Link>
            <Link to="/login" className="btn-outline-light-ds lg">I already have one</Link>
          </div>
          <ul className="hero-trust">
            <li>No bank passwords needed</li><li>CSV import with preview</li><li>Your data stays yours</li>
          </ul>
        </div>
        <HeroIllustration />
      </div>
      <SharkWave />
    </section>

    <section className="strip">
      <div className="landing-container strip-inner">
        <span>Works with</span>
        <b>Chequing</b><i>·</i><b>Savings</b><i>·</i><b>Credit cards</b><i>·</i><b>Investments</b><i>·</i><b>Multiple currencies</b>
      </div>
    </section>

    <section className="section" id="features">
      <div className="landing-container">
        <div className="section-head"><span className="kicker">Features</span><h2>Everything you need to understand your money</h2><p>Built around the boring parts that usually go wrong — so the numbers you see are numbers you can trust.</p></div>
        <div className="feature-grid">
          {features.map(feature => <article className="feature-card" key={feature.title}>
            <div className="feature-icon"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">{feature.icon}</svg></div>
            <h3>{feature.title}</h3><p>{feature.text}</p>
          </article>)}
        </div>
      </div>
    </section>

    <section className="section section-tint" id="how">
      <div className="landing-container">
        <div className="section-head"><span className="kicker">How it works</span><h2>From statement to insight in three steps</h2></div>
        <ol className="steps">
          {steps.map((step, index) => <li key={step.title}><span className="step-num">{index + 1}</span><h3>{step.title}</h3><p>{step.text}</p></li>)}
        </ol>
      </div>
    </section>

    <section className="section" id="security">
      <div className="landing-container">
        <div className="security-card">
          <div className="security-copy">
            <span className="kicker light">Security</span>
            <h2>Private by default, not as an afterthought</h2>
            <p>DollarShark never asks for your online-banking login. You bring the statements; you decide what gets imported.</p>
          </div>
          <ul className="security-list">
            {securityPoints.map(point => <li key={point.text}><span className="security-icon"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{point.icon}</svg></span>{point.text}</li>)}
          </ul>
        </div>
      </div>
    </section>

    <section className="cta-band">
      <div className="landing-container"><div className="cta-inner">
        <h2>Ready to see where your money goes?</h2>
        <p>Set up takes a couple of minutes. Bring a CSV.</p>
        <Link to="/register" className="btn-primary-ds lg">Get started free <span aria-hidden="true">→</span></Link>
      </div></div>
    </section>

    <footer className="landing-footer">
      <div className="landing-container footer-inner">
        <DollarSharkLogo />
        <span>Personal finance, made clearer.</span>
        <div className="footer-links"><Link to="/login">Sign in</Link><Link to="/register">Create account</Link></div>
      </div>
    </footer>
  </div>;
}
