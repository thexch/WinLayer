import { lazy, Suspense, useEffect, useRef, useState, type ReactNode } from 'react'
import {
  ArrowDownToLine, ArrowRight, ArrowUpRight, BatteryCharging, BatteryLow, Bluetooth,
  ChevronDown, CircleAlert, Crosshair, Cpu, Headphones, Layers, Pin, Radio, Workflow, ScanLine, LayoutGrid, Monitor, MousePointerClick, Power, RefreshCw, Settings2, SlidersHorizontal, Sun, Moon, Wrench, Star,
} from 'lucide-react'
import type { AgentStatus, Appearance, BatteryDevice, BatteryOptions, Page, Preferences } from './bridge'
import { AutoClickerPage } from './AutoClickerPage'
import { TabButtons } from './HubControls'
import { MacroPage, PinPage, RepeaterPage, ScreenToolsPage, SwitcherPage, TaskbarPage, type ScreenTool } from './HubTools'
import { NetworkPage, SystemPage } from './HubMonitor'
import type { HubRequest } from './hub'
import { Range, Switch } from './controls'
import { useAgent } from './useAgent'
import { ActionSearch, EntryIcon, ScreenUtilities, UtilitiesPage, utilitySectionFor, type UtilitySection } from './UtilitiesPage'
import { defaults, entries, luminance, type AppTheme, type Entry, type UtilityRequest } from './utility'
import appIcon from '../../src/WinLayer.Agent/WinLayer.ico'

const navigation = [
  { links: [{ id: 'dashboard', label: 'Vue d’ensemble', Icon: LayoutGrid }] },
  { label: 'Surveillance', links: [
    { id: 'battery', label: 'Périphériques', Icon: BatteryCharging },
    { id: 'system', label: 'System Tray Monitor', Icon: Cpu },
    { id: 'network', label: 'Network Monitor', Icon: Radio },
  ] },
  { label: 'Automatisation', links: [
    { id: 'autoClicker', label: 'Auto Clicker', Icon: MousePointerClick },
    { id: 'macro', label: 'Macro Recorder', Icon: Workflow },
  ] },
  { label: 'Bureau', links: [
    { id: 'screen', label: 'Screen Tools', Icon: Crosshair },
    { id: 'switcher', label: 'Alt-Tab Customizer', Icon: ScanLine },
    { id: 'pin', label: 'Always on Top', Icon: Pin },
    { id: 'taskbar', label: 'Taskbar Studio', Icon: Layers },
    { id: 'utilities', label: 'Utilitaires', Icon: Wrench },
  ] },
  { label: 'WinLayer', links: [
    { id: 'general', label: 'Général', Icon: Settings2 },
    { id: 'updates', label: 'Mises à jour', Icon: ArrowDownToLine },
  ] },
] as const

const hubPages = {
  macro: [MacroPage, 'Macro Recorder', 'Enregistrez, éditez et rejouez vos séquences locales.'],
  taskbar: [TaskbarPage, 'Taskbar Studio', 'Compose un style, prévisualise-le et applique-le à la barre des tâches.'],
  switcher: [SwitcherPage, 'Alt-Tab Customizer', 'Recherche une fenêtre ouverte et retrouve-la grâce à son aperçu.'],
  pin: [PinPage, 'Always on Top', 'Gardez les fenêtres de votre choix au premier plan.'],
  network: [NetworkPage, 'Network Monitor', 'Suivez ce qui transite sur votre interface réseau.'],
  system: [SystemPage, 'System Tray Monitor', 'Diagnostiquez les ressources et les processus de votre PC.'],
} as const

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
    <img className="brand-mark" src={appIcon} alt="" width={32} height={32} />
    <span>WinLayer</span>
  </div>
}

function Sidebar({ selection, go, connection, version, search }: {
  selection: Page; go: (page: Page) => void; connection: ReturnType<typeof useAgent>['connection']; version?: string
  search: ReactNode
}) {
  return <aside className="sidebar">
    <Brand />
    {search}
    <nav aria-label="Réglages">
      {navigation.map(group => <div className="nav-group" key={group.links[0].id}>
        {'label' in group && <div className="nav-group-label">{group.label}</div>}
        {group.links.map(({ id, label, Icon }) =>
          <button type="button" key={id} aria-label={label} title={label} aria-current={selection === id ? 'page' : undefined}
            className="nav-link" onClick={() => go(id)}>
            <Icon size={19} aria-hidden="true" /><span>{label}</span>
          </button>
        )}
      </div>)}
    </nav>
    <div className="sidebar-foot">
      <span className={connection === 'opening' ? 'connection-ring' : `presence-dot ${connection}`} aria-hidden="true" />
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
  if (!status || !battery || !battery.devices.length) {
    const message = !status ? 'Les données apparaîtront quand l’agent répondra.' :
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

function Dashboard({ status, go, choose, opening, favorites, canEdit }: {
  status?: AgentStatus; go: (page: Page) => void; choose: (item: Entry) => void; opening: boolean; favorites: Entry[]; canEdit: boolean
}) {
  const sample = status?.hub?.metrics?.history.at(-1)
  const percent = (value?: number | null) => value == null ? '—' : Math.round(value) + ' %'
  const quick = ['ocr', 'color', 'shelf', 'switch']
  return <>
    <Header title="Vue d’ensemble" description="Vos outils, vos périphériques et l’activité de votre PC." />
    <section className="signal-board home-board" aria-labelledby="module-title">
      <div className="signal-copy">
        <Status tone={status ? 'positive' : 'neutral'}>
          {status ? 'WinLayer est prêt' : opening ? 'Connexion à l’agent…' : 'Agent indisponible'}
        </Status>
        <h2 id="module-title">Votre bureau, à votre façon.</h2>
        <p>Capturez, automatisez et personnalisez Windows depuis WinLayer.</p>
        <button type="button" className="button hero-button" onClick={() => go('utilities')}>
          Explorer les utilitaires <ArrowRight size={17} aria-hidden="true" />
        </button>
      </div>
      <GlassLayers />
    </section>
    <div className="home-actions">
      <section aria-labelledby="quick-actions">
        <div className="section-heading"><h2 id="quick-actions">Actions rapides</h2></div>
        <div className="home-launch-grid">{quick.flatMap(id => entries.filter(item => item.id === id)).map(item =>
          <button type="button" className="home-launch" key={item.id} disabled={!canEdit} onClick={() => choose(item)}>
            <span className="tile-icon"><EntryIcon entry={item} /></span><strong>{item.label}</strong><ArrowUpRight size={16} aria-hidden="true" />
          </button>)}</div>
      </section>
      <section aria-labelledby="home-favorites">
        <div className="section-heading"><h2 id="home-favorites">Favoris</h2><Star size={18} aria-hidden="true" /></div>
        {favorites.length > 0 ? <div className="home-launch-grid">{favorites.map(item =>
          <button type="button" className="home-launch" key={item.id} disabled={!item.page && !canEdit} onClick={() => choose(item)}>
            <span className="tile-icon"><EntryIcon entry={item} /></span><strong>{item.label}</strong><ArrowUpRight size={16} aria-hidden="true" />
          </button>)}</div> : <p className="fine home-empty">Sélectionnez l’étoile dans les résultats de recherche pour retrouver une action ici.</p>}
      </section>
    </div>
    <div className="overview-detail">
      <section aria-labelledby="overview-devices">
        <div className="section-heading"><h2 id="overview-devices">Périphériques</h2>
          {status?.battery.updatedAt && <time className="mono fine" dateTime={status.battery.updatedAt}>{readingTime.format(new Date(status.battery.updatedAt))}</time>}
        </div>
        <Readings status={status} limit={3} />
        <button type="button" className="text-button" onClick={() => go('battery')}>Voir les périphériques <ArrowRight size={16} aria-hidden="true" /></button>
      </section>
      <section className="overview-preferences" aria-labelledby="overview-system">
        <div className="section-heading"><h2 id="overview-system">État du PC</h2>
          <button type="button" className="icon-button" aria-label="Ouvrir System Tray Monitor" onClick={() => go('system')}><ArrowUpRight size={17} /></button>
        </div>
        <dl className="summary-list">
          <div><dt><Cpu size={17} aria-hidden="true" /> Processeur</dt><dd>{percent(sample?.cpu)}</dd></div>
          <div><dt><Monitor size={17} aria-hidden="true" /> Mémoire</dt><dd>{percent(sample?.ram)}</dd></div>
          <div><dt><Radio size={17} aria-hidden="true" /> Réseau</dt><dd>{sample?.down == null ? '—' : `${(sample.down / 1024).toFixed(1)} Ko/s ↓`}</dd></div>
          <div><dt><Power size={17} aria-hidden="true" /> Démarrage avec Windows</dt><dd>{status ? status.preferences.startWithWindows ? 'Activé' : 'Désactivé' : '—'}</dd></div>
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
      <fieldset disabled={!canEdit}>
        <Range label="Intervalle de secours" min={2} max={60} unit="min" value={draft.refreshMinutes}
          onChange={(refreshMinutes) => setDraft({ ...draft, refreshMinutes })} />
        <div id="batteryAlert"><Switch label="Alerte de batterie faible" checked={draft.alerts} onChange={(alerts) => setDraft({ ...draft, alerts })} />
        {draft.alerts && <Range label="Seuil de l’alerte" min={5} max={50} unit="%" value={draft.lowThreshold}
          onChange={(lowThreshold) => setDraft({ ...draft, lowThreshold })} />}</div>
        <button className="button primary" disabled={!changed}>Enregistrer la configuration</button>
      </fieldset>
    </form>
}

const themeChoices = [
  { id: 'system', Icon: Monitor },
  { id: 'light', Icon: Sun },
  { id: 'dark', Icon: Moon },
] as const

type GeneralSection = 'preferences' | 'appearance' | 'profiles'

function GeneralForm({ preferences, canEdit, onSave, onPreview, appearanceOnly }: {
  preferences: Preferences; canEdit: boolean; onSave: (preferences: Preferences) => void; onPreview: (appearance: Appearance) => void
  appearanceOnly: boolean
}) {
  const [draft, setDraft] = useState(preferences)
  const changed = draft.appearance !== preferences.appearance || draft.startWithWindows !== preferences.startWithWindows ||
    draft.automaticUpdateChecks !== preferences.automaticUpdateChecks
  return <form className="preferences-form" onSubmit={(event) => { event.preventDefault(); if (changed) onSave(draft) }}>
    {appearanceOnly ? <fieldset disabled={!canEdit}>
      <legend>Thème de base</legend>
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
    </fieldset> : <fieldset disabled={!canEdit}>
      <legend>Exécution en arrière-plan</legend>
      <Switch checked={draft.startWithWindows} label="Démarrer avec Windows"
        description="L’agent démarre à l’ouverture de la session." onChange={(startWithWindows) => setDraft({ ...draft, startWithWindows })} />
      <Switch checked={draft.automaticUpdateChecks} label="Rechercher automatiquement les mises à jour"
        description="Recevez une notification lorsqu’une version stable est disponible."
        onChange={(automaticUpdateChecks) => setDraft({ ...draft, automaticUpdateChecks })} />
    </fieldset>}
    <button className="button primary" disabled={!canEdit || !changed}>Enregistrer les préférences</button>
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
  const { status, page, requestedTarget, connection, feedback, busy, downloadProgress, go, act, setProcessView } = useAgent()
  const [generalSection, setGeneralSection] = useState<GeneralSection>('preferences')
  const [screenTool, setScreenTool] = useState<ScreenTool>('picker')
  const [utilitySection, setUtilitySection] = useState<UtilitySection>('files')
  const [automation, setAutomation] = useState<'clicker' | 'repeater'>('clicker')
  const [preview, setPreview] = useState<Appearance>()
  const [themePreview, setThemePreview] = useState<AppTheme>()
  const [searchOpen, setSearchOpen] = useState(false)
  const [ignoredTarget, setIgnoredTarget] = useState<{ id: string }>()
  const [localTarget, setLocalTarget] = useState<{ id: string }>()
  const main = useRef<HTMLElement>(null)
  const preferences = status?.preferences
  const options = preferences?.battery
  const utilityOptions = preferences?.utilities ?? defaults
  const utilityKey = JSON.stringify(utilityOptions)
  const items = [...entries, ...(preferences?.hub?.macros ?? []).map(macro => ({
    id: 'macro:' + macro.name, label: macro.name, category: 'Macro', keywords: 'jouer exécuter séquence', icon: 'macro', tool: 'macro',
  })), ...utilityOptions.launchers.map(item => ({
    id: item.id, label: item.label, category: 'Raccourcis locaux', keywords: 'ouvrir application fichier dossier', icon: 'folder', tool: 'local',
  }))]
  const favorites = utilityOptions.favorites.flatMap(id => items.find(item => item.id === id) ?? [])
  const selectedTheme = utilityOptions.themes.find(theme => theme.name === utilityOptions.theme)
  const theme = page === 'general' && themePreview ? themePreview : page === 'general' && preview ? undefined : selectedTheme
  const canEdit = connection === 'connected' && busy === null
  const appearance = page === 'general' && preview ? preview : preferences?.appearance ?? 'system'

  useEffect(() => {
    const root = document.documentElement
    root.dataset.theme = theme ? luminance(theme.canvas) < .25 ? 'dark' : 'light' : appearance
    const custom = theme ? {
      '--canvas': theme.canvas, '--surface': theme.surface, '--ink': theme.ink, '--accent': theme.accent,
      '--line': theme.border, '--focus': theme.accent, '--radius': theme.radius + 'px', '--density': theme.density / 100,
      '--on-accent': luminance(theme.accent) > .179 ? '#000' : '#fff',
      '--accent-deep': theme.accent, '--accent-tint': 'color-mix(in srgb, var(--accent) 16%, var(--surface))',
      '--muted': 'color-mix(in srgb, var(--ink) 86%, var(--canvas))',
      '--font-sans-custom': theme.font === 'Manrope' ? 'var(--font-sans)' : theme.font === 'Geist Mono' ? 'var(--font-mono)' : '"Segoe UI", sans-serif',
      '--sidebar': theme.surface, '--sidebar-text': theme.ink, '--sidebar-bright': theme.ink,
      '--sidebar-active': 'color-mix(in srgb, var(--accent) 18%, var(--surface))', '--sidebar-focus': theme.accent,
    } : {}
    Object.entries(custom).forEach(([key, value]) => root.style.setProperty(key, String(value)))
    if (theme && !theme.motion) root.dataset.static = 'true'
    return () => { Object.keys(custom).forEach(key => root.style.removeProperty(key)); delete root.dataset.static }
  }, [appearance, theme])
  useEffect(() => {
    function shortcut(event: KeyboardEvent) {
      if (event.ctrlKey && !event.altKey && !event.shiftKey && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setSearchOpen(true)
      }
    }
    window.addEventListener('keydown', shortcut)
    return () => window.removeEventListener('keydown', shortcut)
  }, [])
  useEffect(() => {
    const target = localTarget ?? (ignoredTarget === requestedTarget ? undefined : requestedTarget)
    if (!target) return
    const frame = requestAnimationFrame(() => {
      const node = document.getElementById(target.id)
      if (!node) return
      const disclosure = node.closest('details')
      if (disclosure) disclosure.open = true
      node.scrollIntoView({ block: 'center' })
      const control = node.querySelector<HTMLElement>('input, button, select, summary, textarea')
      if (control) control.focus({ preventScroll: true })
      else { node.tabIndex = -1; node.focus({ preventScroll: true }) }
    })
    return () => cancelAnimationFrame(frame)
  }, [page, localTarget, requestedTarget, ignoredTarget])
  useEffect(() => {
    setLocalTarget(undefined)
    if (requestedTarget?.id === 'repeat') setAutomation('repeater')
    if (requestedTarget?.id === 'clickerSpeed') setAutomation('clicker')
    if (requestedTarget?.id === 'ocrLanguage') setScreenTool('captures')
    const section = utilitySectionFor(requestedTarget?.id)
    if (section === 'appearance' || section === 'profiles') setGeneralSection(section)
    else if (section) setUtilitySection(section)
  }, [requestedTarget])
  useEffect(() => { main.current?.scrollTo({ top: 0 }) }, [page])

  function save({ autoClicker: _autoClicker, hub: _hub, utilities: _utilities, ...next }: Preferences) {
    void act({ action: 'save', preferences: next }, 'Enregistrement…', 'Préférences enregistrées.')
  }

  function runHub(hub: HubRequest) {
    return act({ action: 'hub', hub }, 'Action en cours…', hub.action === 'configure' ? 'Configuration appliquée.' :
      hub.action === 'copyColor' ? 'Couleur copiée.' : '')
  }

  function navigate(next: Page) {
    setPreview(undefined)
    setThemePreview(undefined)
    setLocalTarget(undefined)
    setIgnoredTarget(requestedTarget)
    go(next)
  }

  function runUtility(utility: UtilityRequest) {
    return act({ action: 'utility', utility }, 'Action en cours…', utility.action === 'save' ? 'Réglages enregistrés.' :
      utility.action === 'undo' ? 'Dernière modification annulée.' : '')
  }

  function choose(item: Entry) {
    if (item.page) {
      navigate(item.page)
      if (item.target === 'repeat' || item.target === 'clickerSpeed') setAutomation(item.target === 'repeat' ? 'repeater' : 'clicker')
      if (item.target === 'ocrLanguage') setScreenTool('captures')
      const section = utilitySectionFor(item.target)
      if (section === 'appearance' || section === 'profiles') setGeneralSection(section)
      else if (section) setUtilitySection(section)
      setLocalTarget(item.target ? { id: item.target } : undefined)
    } else if (canEdit) {
      void runUtility({ action: 'execute', id: item.id })
    }
  }

  function favorite(id: string) {
    const existing = utilityOptions.favorites
    const next = existing.includes(id) ? existing.filter(item => item !== id) : [...existing, id].slice(-16)
    void runUtility({ action: 'save', options: { ...utilityOptions, favorites: next } })
  }

  const hubPage = page in hubPages ? hubPages[page as keyof typeof hubPages] : undefined
  const hubProps = status?.hub ? { target: localTarget?.id ?? (ignoredTarget === requestedTarget ? undefined : requestedTarget?.id), status: status.hub, data: preferences?.hub, run: runHub, busy: !canEdit, setProcessView } : undefined
  const Module = hubPage?.[0]

  return <>
    <div className="shell">
      <Sidebar selection={page} go={navigate} connection={connection} version={status?.version}
        search={<ActionSearch open={searchOpen} setOpen={setSearchOpen} choose={choose} favorite={favorite}
          canEdit={canEdit} items={items} options={utilityOptions} recent={status?.utilities?.recent ?? []} />} />
      <main id="main-content" ref={main} tabIndex={-1}>
        <div className="workspace">
          {connection === 'offline' && <div className="notice"><CircleAlert size={17} aria-hidden="true" /> L’agent est indisponible. Nouvelle tentative automatique.</div>}
          {status?.error && <div className="notice"><CircleAlert size={17} aria-hidden="true" /> {status.error}</div>}
          {page === 'dashboard' && <Dashboard status={status} go={navigate} choose={choose} canEdit={canEdit} favorites={favorites} opening={connection === 'opening'} />}
          {page === 'battery' && <>
            <Header title="Périphériques" description="Retrouvez les niveaux de charge transmis à Windows.">
              <button type="button" className={`button secondary ${busy === 'refreshBattery' ? 'refreshing' : ''}`}
                disabled={!canEdit || !options} onClick={() => { void act({ action: 'refreshBattery' }, 'Actualisation demandée…') }}>
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
                <div id="batteryTray"><Switch label="Bandeau batterie dans la barre des tâches" checked={preferences?.batteryTrayIcons ?? true}
                  description="Affiche les pictogrammes et les pourcentages à gauche de la zone de notification."
                  disabled={!canEdit || !options}
                  onChange={(batteryTrayIcons) => { if (preferences) save({ ...preferences, batteryTrayIcons }) }} /></div>
                {options && <details className="settings-disclosure">
                  <summary><SlidersHorizontal size={18} aria-hidden="true" /> Configurer la lecture <ChevronDown size={18} aria-hidden="true" /></summary>
                  <BatteryForm key={JSON.stringify(options)} options={options} canEdit={canEdit}
                    onSave={(battery) => { if (preferences) save({ ...preferences, battery }) }} />
                </details>}
              </section>
            </div>
          </>}
          {page === 'autoClicker' && <>
            <Header title={automation === 'clicker' ? 'Auto Clicker' : 'Key Repeater'}
              description={automation === 'clicker' ? 'Automatisez vos clics avec un raccourci global.' : 'Répétez une touche ou une combinaison.'} />
            <TabButtons label="Automatisation" choices={[[ 'clicker', 'Auto Clicker' ], [ 'repeater', 'Key Repeater' ]]}
              active={automation} select={setAutomation} />
            {automation === 'repeater' ? (hubProps && <div id="repeat"><RepeaterPage {...hubProps} /></div>) :
              preferences?.autoClicker && status?.autoClicker ?
              <AutoClickerPage options={preferences.autoClicker} status={status.autoClicker} canEdit={canEdit}
                onSave={(clickerOptions) => { void act({ action: 'saveAutoClicker', clickerOptions }, 'Enregistrement…', 'Configuration enregistrée.') }}
                onPause={() => act({ action: 'pauseAutoClicker' }, 'Mise en pause…')}
                onRearm={() => { void act({ action: 'rearmAutoClicker' }, 'Réarmement…') }} /> :
              <p className="fine">La configuration apparaîtra quand l’agent répondra.</p>}
          </>}
          {hubPage && <>
            <Header title={hubPage[1]} description={hubPage[2]} />
            {Module && hubProps ? <Module {...hubProps} /> : <p className="fine">En attente de l’agent WinLayer.</p>}
          </>}
          {page === 'screen' && <>
            <Header title="Screen Tools" description="Prélevez une couleur, mesurez le bureau ou capturez une zone." />
            {hubProps && <ScreenToolsPage {...hubProps} active={screenTool} select={setScreenTool}
              captures={<ScreenUtilities key={utilityKey} options={utilityOptions} canEdit={canEdit} run={runUtility} />} />}
          </>}
          {page === 'utilities' && <>
            <Header title="Utilitaires" description="Lancez vos outils et personnalisez les raccourcis de votre bureau." />
            {status?.utilities?.error && <p className="notice" role="status">{status.utilities.error}</p>}
            <UtilitiesPage key={utilityKey} options={utilityOptions} items={items} canEdit={canEdit}
              run={runUtility} preview={setThemePreview} section={utilitySection} selectSection={setUtilitySection} />
          </>}
          {page === 'general' && <>
            <Header title="Général" description="Personnalisez l’apparence et le démarrage de WinLayer." />
            <TabButtons label="Préférences WinLayer" choices={[[ 'preferences', 'Préférences' ], [ 'appearance', 'Apparence' ], [ 'profiles', 'Profils et restauration' ]]}
              active={generalSection} select={next => { setPreview(undefined); setThemePreview(undefined); setGeneralSection(next) }} />
            {generalSection !== 'profiles' && (preferences ? <GeneralForm
              key={`${generalSection}/${preferences.appearance}/${preferences.startWithWindows}/${preferences.automaticUpdateChecks}`}
              appearanceOnly={generalSection === 'appearance'} preferences={preferences} canEdit={canEdit} onSave={save} onPreview={value => {
                setThemePreview(undefined)
                setPreview(value)
              }} /> : <p className="fine">Les préférences apparaîtront quand l’agent répondra.</p>)}
            {generalSection !== 'preferences' && <UtilitiesPage key={`${generalSection}/${utilityKey}`} options={utilityOptions} items={items}
              canEdit={canEdit} undo={status?.utilities?.undo} run={runUtility} preview={setThemePreview} section={generalSection} />}
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
