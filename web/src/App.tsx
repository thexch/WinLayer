import { lazy, Suspense, useEffect, useRef, useState, type ReactNode } from 'react'
import {
  ArrowDownToLine, ArrowRight, ArrowUpRight, BatteryCharging, BatteryLow, Bluetooth,
  ChevronDown, CircleAlert, Headphones, LayoutGrid, Monitor, MousePointerClick, Power, RefreshCw, Settings2, SlidersHorizontal, Sun, Moon,
} from 'lucide-react'
import type { AgentStatus, Appearance, BatteryDevice, BatteryOptions, Page, Preferences } from './bridge'
import { AutoClickerPage } from './AutoClickerPage'
import { Range, Switch } from './controls'
import { useAgent } from './useAgent'

const navigation = [
  { id: 'dashboard', label: 'Vue d’ensemble', Icon: LayoutGrid },
  { id: 'battery', label: 'Périphériques', Icon: BatteryCharging },
  { id: 'autoClicker', label: 'Auto Clicker', Icon: MousePointerClick },
  { id: 'general', label: 'Général', Icon: Settings2 },
  { id: 'updates', label: 'Mises à jour', Icon: ArrowDownToLine },
] as const

const ConfirmInstall = lazy(() => import('./ConfirmInstall'))
const readingTime = new Intl.DateTimeFormat('fr-FR', { hour: '2-digit', minute: '2-digit' })
const appearanceNames: Record<Appearance, string> = { system: 'Système', light: 'Clair', dark: 'Sombre' }

function Header({ title, description, children }: { title: string; description: string; children?: ReactNode }) {
  return <header className="page-heading">
    <div><h1>{title}</h1><p>{description}</p></div>
    {children}
  </header>
}

function Status({ children, tone = 'neutral' }: { children: ReactNode; tone?: 'neutral' | 'positive' | 'warning' }) {
  return <span className={`status status-${tone}`}><span className="status-dot" aria-hidden="true" />{children}</span>
}

function Brand() {
  return <div className="brand">
    <span className="brand-mark" aria-hidden="true"><i /><i /></span>
    <span>WinLayer</span>
  </div>
}

function Sidebar({ selection, go, connection, version }: {
  selection: Page; go: (page: Page) => void; connection: ReturnType<typeof useAgent>['connection']; version?: string
}) {
  return <aside className="sidebar">
    <Brand />
    <nav aria-label="Réglages">
      {navigation.map(({ id, label, Icon }) =>
        <button type="button" key={id} aria-label={label} aria-current={selection === id ? 'page' : undefined}
          className="nav-link" onClick={() => go(id)}>
          <Icon size={19} aria-hidden="true" /><span>{label}</span>
        </button>
      )}
    </nav>
    <div className="sidebar-foot">
      <span className={`presence-dot ${connection}`} aria-hidden="true" />
      <div>
        <strong>{connection === 'connected' ? 'Agent actif' : connection === 'opening' ? 'Connexion…' : 'Agent indisponible'}</strong>
        {version && <span className="mono">Version {version}</span>}
      </div>
    </div>
  </aside>
}

function BatteryReading({ device, threshold }: { device: BatteryDevice; threshold: number }) {
  const level = device.state === 'available' && device.percentage !== null &&
    device.percentage >= 0 && device.percentage <= 100 ? device.percentage : null
  const label = {
    available: level === null ? 'Niveau inconnu' : 'Batterie disponible',
    unknown: 'Niveau inconnu',
    disconnected: 'Déconnecté',
    unsupported: 'Service de batterie non exposé',
  }[device.state]
  const isUsb = device.source === 'hyperXUsb'
  const Icon = isUsb ? Headphones : Bluetooth
  return <li className="device-row">
    <span className="tile-icon"><Icon size={20} aria-hidden="true" /></span>
    <div className="device-name">
      <strong>{device.name}</strong>
      <span className="fine">{isUsb ? `Dongle USB 2,4 GHz · ${label}` : label}</span>
    </div>
    {level !== null && <div className={`device-reading ${level <= threshold ? 'reading-low' : ''}`}>
      <span className="reading-value mono">{level}<span> %</span></span>
      <meter aria-label={`Charge de ${device.name}`} min={0} max={100} low={threshold} optimum={100} value={level} />
    </div>}
  </li>
}

function Readings({ status, limit }: { status?: AgentStatus; limit?: number }) {
  const battery = status?.battery
  if (!status || !battery || !status.preferences.battery.enabled || !battery.devices.length) {
    const message = !status ? 'Les données apparaîtront quand l’agent répondra.' :
      !status.preferences.battery.enabled ? 'Activez Device Battery pour afficher les niveaux de charge.' :
      battery?.error ?? (battery?.updatedAt ? 'Aucun périphérique compatible détecté.' : 'Recherche des périphériques…')
    return <div className="empty-state">
      <span className="tile-icon"><Bluetooth size={20} aria-hidden="true" /></span>
      <p>{message}</p>
    </div>
  }

  return <>
    {battery.error && <p className="fine compatibility">{battery.error}</p>}
    <ul className="device-list" aria-label="Lectures de batterie">
      {battery.devices.slice(0, limit).map((device) =>
        <BatteryReading key={device.id} device={device} threshold={status.preferences.battery.lowThreshold} />
      )}
    </ul>
  </>
}

function GlassLayers() {
  return <div className="glass-art" aria-hidden="true">
    <div className="glass-orbit orbit-one" /><div className="glass-orbit orbit-two" />
    <div className="glass-cross cross-top" /><div className="glass-cross cross-bottom" />
    <div className="glass-piece piece-back" /><div className="glass-piece piece-middle" />
    <div className="glass-piece piece-front"><i /><i /><i /></div>
  </div>
}

function Dashboard({ status, go }: { status?: AgentStatus; go: (page: Page) => void }) {
  const batteryEnabled = status?.preferences.battery.enabled
  const known = status?.battery.devices.filter((device) => device.state === 'available' && device.percentage !== null).length ?? 0
  return <>
    <Header title="Vue d’ensemble" description="Retrouvez vos batteries, l’état de WinLayer et vos réglages." />
    <section className="signal-board" aria-labelledby="module-title">
      <div className="signal-copy">
        <Status tone={!status ? 'neutral' : status.battery.error ? 'warning' : batteryEnabled ? 'positive' : 'neutral'}>
          {!status ? 'État indisponible' : status.battery.error ? 'Lecture indisponible' : batteryEnabled ? 'Surveillance active' : 'Module désactivé'}
        </Status>
        <h2 id="module-title">Device Battery</h2>
        <p>{batteryEnabled && known > 0 ? `${known} niveau${known > 1 ? 'x' : ''} de charge disponible${known > 1 ? 's' : ''}.` :
          'Suivez la charge de vos périphériques compatibles.'}</p>
        <button type="button" className="button hero-button" onClick={() => go('battery')}>
          Voir les périphériques <ArrowRight size={17} aria-hidden="true" />
        </button>
      </div>
      <GlassLayers />
    </section>
    <div className="overview-detail">
      <section aria-labelledby="overview-devices">
        <div className="section-heading"><h2 id="overview-devices">Périphériques</h2>
          {status?.battery.updatedAt && <time className="mono fine" dateTime={status.battery.updatedAt}>{readingTime.format(new Date(status.battery.updatedAt))}</time>}
        </div>
        <Readings status={status} limit={3} />
        {(status?.battery.devices.length ?? 0) > 3 && <button type="button" className="text-button" onClick={() => go('battery')}>Voir tous les périphériques <ArrowRight size={16} /></button>}
      </section>
      <section className="overview-preferences" aria-labelledby="overview-preferences">
        <div className="section-heading"><h2 id="overview-preferences">Préférences système</h2>
          <button type="button" className="icon-button" aria-label="Ouvrir les paramètres généraux" onClick={() => go('general')}><ArrowUpRight size={17} /></button>
        </div>
        <dl className="summary-list">
          <div><dt><Power size={17} aria-hidden="true" /> Démarrage avec Windows</dt><dd>{status ? status.preferences.startWithWindows ? 'Activé' : 'Désactivé' : '—'}</dd></div>
          <div><dt><Monitor size={17} aria-hidden="true" /> Apparence</dt><dd>{status ? appearanceNames[status.preferences.appearance] : '—'}</dd></div>
        </dl>
      </section>
    </div>
  </>
}

function BatteryForm({ options, canEdit, onSave }: {
  options: BatteryOptions; canEdit: boolean; onSave: (options: BatteryOptions) => void
}) {
  const [draft, setDraft] = useState(options)
  const changed = draft.alerts !== options.alerts || draft.lowThreshold !== options.lowThreshold || draft.refreshMinutes !== options.refreshMinutes
  return <form className="settings-body" onSubmit={(event) => { event.preventDefault(); onSave(draft) }}>
      <fieldset disabled={!canEdit || !options.enabled}>
        <Range label="Intervalle de secours" min={2} max={60} unit="min" value={draft.refreshMinutes}
          onChange={(refreshMinutes) => setDraft({ ...draft, refreshMinutes })} />
        <Switch label="Alerte de batterie faible" checked={draft.alerts} onChange={(alerts) => setDraft({ ...draft, alerts })} />
        {draft.alerts && <Range label="Seuil de l’alerte" min={5} max={50} unit="%" value={draft.lowThreshold}
          onChange={(lowThreshold) => setDraft({ ...draft, lowThreshold })} />}
        <button className="button primary" disabled={!changed}>Enregistrer la configuration</button>
      </fieldset>
    </form>
}

const themeChoices = [
  { id: 'system', Icon: Monitor },
  { id: 'light', Icon: Sun },
  { id: 'dark', Icon: Moon },
] as const

function GeneralForm({ preferences, canEdit, onSave, onPreview }: {
  preferences: Preferences; canEdit: boolean; onSave: (preferences: Preferences) => void; onPreview: (appearance: Appearance) => void
}) {
  const [draft, setDraft] = useState(preferences)
  const changed = draft.appearance !== preferences.appearance || draft.startWithWindows !== preferences.startWithWindows ||
    draft.automaticUpdateChecks !== preferences.automaticUpdateChecks
  return <form className="preferences-form" onSubmit={(event) => { event.preventDefault(); if (changed) onSave(draft) }}>
    <fieldset disabled={!canEdit}>
      <legend>Apparence</legend>
      <p className="fine">Choisissez le thème de la fenêtre de réglages.</p>
      <div className="theme-picker">
        {themeChoices.map(({ id, Icon }) => <label key={id} className="theme-option">
          <input name="appearance" type="radio" value={id} checked={draft.appearance === id} onChange={() => {
            setDraft({ ...draft, appearance: id })
            onPreview(id)
          }} />
          <Icon size={19} aria-hidden="true" /><span>{appearanceNames[id]}</span>
        </label>)}
      </div>
    </fieldset>
    <fieldset disabled={!canEdit}>
      <legend>Exécution en arrière-plan</legend>
      <Switch checked={draft.startWithWindows} label="Démarrer avec Windows"
        description="L’agent démarre à l’ouverture de la session." onChange={(startWithWindows) => setDraft({ ...draft, startWithWindows })} />
      <Switch checked={draft.automaticUpdateChecks} label="Rechercher automatiquement les mises à jour"
        description="Recevez une notification lorsqu’une version stable est disponible."
        onChange={(automaticUpdateChecks) => setDraft({ ...draft, automaticUpdateChecks })} />
      <button className="button primary" disabled={!changed}>Enregistrer les préférences</button>
    </fieldset>
  </form>
}

function UpdatePage({ status, canEdit, onCheck, onInstall, downloading, progress }: {
  status?: AgentStatus; canEdit: boolean; onCheck: () => void; onInstall: (version: string) => void
  downloading: boolean; progress: number | null
}) {
  const update = status?.updates
  const [title, description] = {
    unavailable: ['Mises à jour indisponibles', 'Installez WinLayer avec Velopack pour recevoir les nouvelles versions.'],
    notChecked: ['Rechercher une nouvelle version', 'WinLayer vérifie les versions stables publiées sur GitHub.'],
    current: ['WinLayer est à jour', 'Vous utilisez la dernière version stable disponible.'],
    available: [`WinLayer ${update?.version ?? ''} est disponible`, 'L’agent redémarrera après l’installation.'],
    error: ['Vérification indisponible', 'Réessayez dans quelques instants.'],
    installing: ['Installation en cours', 'WinLayer prépare le redémarrage.'],
  }[update?.state ?? 'unavailable']
  const version = update?.state === 'available' ? update.version : null
  return <>
    <Header title="Mises à jour" description="Gérez les versions stables de WinLayer." />
    <section className="update-panel" aria-labelledby="update-status-heading">
      <span className="tile-icon accent-tile"><ArrowDownToLine size={25} aria-hidden="true" /></span>
      <div className="update-copy">
        <h2 id="update-status-heading">{status ? title : 'État indisponible'}</h2>
        <p>{!status ? 'Ouvrez les réglages depuis l’agent WinLayer.' : update?.info ?? description}</p>
        {status && <p className="fine mono">Version installée {status.version}</p>}
      </div>
      {downloading && <div className="update-progress">
        <div><span>{progress === 100 ? 'Préparation du redémarrage…' : 'Téléchargement du paquet'}</span>
          {progress !== null && <strong className="mono">{progress} %</strong>}</div>
        <progress aria-label="Téléchargement de la mise à jour" max={100} value={progress ?? undefined} />
      </div>}
      <div className="update-actions">
        <button type="button" className="button secondary" onClick={onCheck}
          disabled={!canEdit || !update || update.state === 'unavailable' || update.state === 'installing'}>
          <RefreshCw size={16} aria-hidden="true" /> Rechercher
        </button>
        {version && <Suspense fallback={<button type="button" className="button primary" disabled>Installer la mise à jour</button>}>
          <ConfirmInstall version={version} onInstall={() => onInstall(version)}>
            <button type="button" className="button primary" disabled={!canEdit}>Installer la mise à jour</button>
          </ConfirmInstall>
        </Suspense>}
      </div>
    </section>
  </>
}

export function App() {
  const { status, page, connection, feedback, busy, downloadProgress, go, act } = useAgent()
  const [preview, setPreview] = useState<Appearance>()
  const main = useRef<HTMLElement>(null)
  const preferences = status?.preferences
  const options = preferences?.battery
  const canEdit = connection === 'connected' && busy === null
  const appearance = page === 'general' && preview ? preview : preferences?.appearance ?? 'system'

  useEffect(() => { document.documentElement.dataset.theme = appearance }, [appearance])
  useEffect(() => { main.current?.scrollTo({ top: 0 }) }, [page])

  function save({ autoClicker: _autoClicker, ...next }: Preferences) {
    void act({ action: 'save', preferences: next }, 'Enregistrement…', 'Préférences enregistrées.')
  }

  function navigate(next: Page) {
    setPreview(undefined)
    go(next)
  }

  return <>
    <a className="skip-link" href="#main-content" onClick={(event) => { event.preventDefault(); main.current?.focus() }}>Aller au contenu</a>
    <div className="shell">
      <Sidebar selection={page} go={navigate} connection={connection} version={status?.version} />
      <main id="main-content" ref={main} tabIndex={-1}>
        <div className="workspace">
          {connection === 'offline' && <div className="notice"><CircleAlert size={17} aria-hidden="true" /> L’agent est indisponible. Nouvelle tentative automatique.</div>}
          {status?.error && <div className="notice"><CircleAlert size={17} aria-hidden="true" /> {status.error}</div>}
          {page === 'dashboard' && <Dashboard status={status} go={navigate} />}
          {page === 'battery' && <>
            <Header title="Périphériques" description="Retrouvez les niveaux de charge transmis à Windows.">
              <button type="button" className={`button secondary ${busy === 'refreshBattery' ? 'refreshing' : ''}`}
                disabled={!canEdit || !options?.enabled} onClick={() => { void act({ action: 'refreshBattery' }, 'Actualisation demandée…') }}>
                <RefreshCw size={17} aria-hidden="true" /> Actualiser
              </button>
            </Header>
            <div className="battery-layout">
              <section aria-labelledby="paired-devices" className="readings-panel">
                <div className="section-heading"><h2 id="paired-devices">Batteries</h2>
                  {status?.battery.updatedAt && <time className="mono fine" dateTime={status.battery.updatedAt}>Lecture à {readingTime.format(new Date(status.battery.updatedAt))}</time>}
                </div>
                <Readings status={status} />
                <p className="fine compatibility">Compatible avec le Bluetooth LE et le HyperX Cloud III Wireless via son dongle USB.</p>
              </section>
              <section aria-labelledby="battery-options" className="battery-panel">
                <div className="section-heading"><h2 id="battery-options">Device Battery</h2><BatteryLow size={20} aria-hidden="true" /></div>
                <Switch label="Surveiller les batteries" checked={options?.enabled ?? false} disabled={!canEdit || !options}
                  onChange={(enabled) => { if (preferences && options) save({ ...preferences, battery: { ...options, enabled } }) }} />
                <Switch label="Bandeau batterie dans la barre des tâches" checked={preferences?.batteryTrayIcons ?? true}
                  description="Affiche les pictogrammes et les pourcentages à gauche de la zone de notification."
                  disabled={!canEdit || !options?.enabled}
                  onChange={(batteryTrayIcons) => { if (preferences) save({ ...preferences, batteryTrayIcons }) }} />
                {options && <details className="settings-disclosure">
                  <summary><SlidersHorizontal size={18} aria-hidden="true" /> Configurer la lecture <ChevronDown size={18} aria-hidden="true" /></summary>
                  <BatteryForm key={JSON.stringify(options)} options={options} canEdit={canEdit}
                    onSave={(battery) => { if (preferences) save({ ...preferences, battery }) }} />
                </details>}
              </section>
            </div>
          </>}
          {page === 'autoClicker' && <>
            <Header title="Auto Clicker" description="Automatisez vos clics avec un raccourci global." />
            {preferences?.autoClicker && status?.autoClicker ?
              <AutoClickerPage options={preferences.autoClicker} status={status.autoClicker} canEdit={canEdit}
                onSave={(clickerOptions) => { void act({ action: 'saveAutoClicker', clickerOptions }, 'Enregistrement…', 'Configuration enregistrée.') }}
                onPause={() => act({ action: 'pauseAutoClicker' }, 'Mise en pause…')}
                onRearm={() => { void act({ action: 'rearmAutoClicker' }, 'Réarmement…') }} /> :
              <p className="fine">La configuration apparaîtra quand l’agent répondra.</p>}
          </>}
          {page === 'general' && <>
            <Header title="Général" description="Personnalisez l’apparence et le démarrage de WinLayer." />
            {preferences ? <GeneralForm key={`${preferences.appearance}/${preferences.startWithWindows}/${preferences.automaticUpdateChecks}`}
              preferences={preferences} canEdit={canEdit} onSave={save} onPreview={setPreview} /> :
              <p className="fine">Les préférences apparaîtront quand l’agent répondra.</p>}
          </>}
          {page === 'updates' && <UpdatePage status={status} canEdit={canEdit} downloading={busy === 'installUpdate'} progress={downloadProgress}
            onCheck={() => { void act({ action: 'checkUpdates' }, 'Recherche de mises à jour…') }}
            onInstall={(version) => { void act({ action: 'installUpdate', version }, `Téléchargement de WinLayer ${version}…`) }} />}
          <div className="feedback" role="status" aria-live="polite">{feedback}</div>
        </div>
      </main>
    </div>
  </>
}
