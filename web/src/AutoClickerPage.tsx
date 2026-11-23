import { useRef, useState, type KeyboardEvent } from 'react'
import { CircleAlert, Keyboard, Pause, Play } from 'lucide-react'
import type { AutoClickerOptions, AutoClickerStatus, ClickShortcut } from './bridge'

const modifiers = [
  { label: 'Ctrl', flag: 2 },
  { label: 'Alt', flag: 1 },
  { label: 'Maj', flag: 4 },
  { label: 'Windows', flag: 8 },
]
const keys = [
  ...Array.from({ length: 24 }, (_, index) => index + 112).filter((key) => key !== 123),
  ...Array.from('ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789', (key) => key.charCodeAt(0)),
]
const labels = { idle: 'Inactif', active: 'Actif', paused: 'En pause' }
const milliseconds = new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 2 })

function keyLabel(key: number) {
  return key >= 112 ? `F${key - 111}` : String.fromCharCode(key)
}

function shortcutLabel({ key, modifiers: flags }: ClickShortcut) {
  return [...modifiers.filter(({ flag }) => flags & flag).map(({ label }) => label), keyLabel(key)]
}

function readShortcut(event: KeyboardEvent): ClickShortcut | undefined {
  const code = /^[a-z0-9]$/i.test(event.key) ? event.key.toUpperCase() : /^Digit([0-9])$/.exec(event.code)?.[1]
  const fn = /^F(\d{1,2})$/.exec(event.key)?.[1]
  const key = code ? code.charCodeAt(0) : fn ? Number(fn) + 111 : 0
  const flags = Number(event.altKey) | (Number(event.ctrlKey) << 1) |
    (Number(event.shiftKey) << 2) | (Number(event.metaKey) << 3)
  if (keys.includes(key) && (key >= 112 || (flags & 11) !== 0)) return { key, modifiers: flags }
}

function ShortcutField({ label, value, onChange, onRecord }: {
  label: string
  value: ClickShortcut
  onChange: (value: ClickShortcut) => void
  onRecord: () => Promise<boolean>
}) {
  const [recording, setRecording] = useState(false)
  const record = useRef<HTMLButtonElement>(null)

  async function begin() {
    if (recording) {
      setRecording(false)
      return
    }
    if (await onRecord()) {
      setRecording(true)
      requestAnimationFrame(() => record.current?.focus())
    }
  }

  function capture(event: KeyboardEvent<HTMLButtonElement>) {
    if (!recording || event.key === 'Tab') return
    event.preventDefault()
    event.stopPropagation()
    if (event.key === 'Escape') {
      setRecording(false)
      return
    }
    const shortcut = readShortcut(event)
    if (shortcut) {
      setRecording(false)
      onChange(shortcut)
    }
  }

  return <div className="clicker-shortcut">
    <div className="clicker-shortcut-head">
      <label><span>{label}</span>
        <select aria-label={label} value={value.key} onChange={(event) => onChange({ ...value, key: Number(event.target.value) })}>
          {keys.map((key) => <option key={key} value={key}>{keyLabel(key)}</option>)}
        </select>
      </label>
      <button type="button" className="button secondary" ref={record} onClick={() => { void begin() }}
        aria-label={recording ? `Appuyez sur les touches… ${label}` : `Enregistrer les touches · ${label}`}
        aria-pressed={recording} onKeyDown={capture} onBlur={() => setRecording(false)}>
        <Keyboard size={16} aria-hidden="true" /> {recording ? 'Appuyez sur les touches…' : 'Enregistrer les touches'}
      </button>
    </div>
    <div className="clicker-modifiers" role="group" aria-label={`Modificateurs · ${label}`}>
      {modifiers.map(({ label: name, flag }) => <label key={flag}>
        <input type="checkbox" checked={(value.modifiers & flag) !== 0}
          onChange={() => onChange({ ...value, modifiers: value.modifiers ^ flag })} />
        <span>{name}</span>
      </label>)}
    </div>
  </div>
}

function NumberSetting({ label, value, min, max, unit, onChange }: {
  label: string; value: number; min: number; max: number; unit?: string; onChange: (value: number) => void
}) {
  const [draft, setDraft] = useState<string | null>(null)
  return <label className="clicker-number">
    <span>{label}</span>
    <span className="clicker-number-input">
      <input type="number" value={draft ?? value} min={min} max={max} step={1}
        onChange={(event) => setDraft(event.currentTarget.value)}
        onKeyDown={(event) => { if (event.key === 'Enter') event.currentTarget.blur() }}
        onBlur={(event) => {
          const number = event.currentTarget.valueAsNumber
          setDraft(null)
          if (Number.isInteger(number)) {
            const bounded = Math.max(min, Math.min(max, number))
            if (bounded !== value) onChange(bounded)
          }
        }} />
      {unit && <span className="mono fine" aria-hidden="true">{unit}</span>}
    </span>
  </label>
}

export function AutoClickerPage({ options, status, canEdit, onSave, onPause, onRearm }: {
  options: AutoClickerOptions
  status: AutoClickerStatus
  canEdit: boolean
  onSave: (options: AutoClickerOptions) => void
  onPause: () => Promise<boolean>
  onRearm: () => void
}) {
  const [validation, setValidation] = useState('')
  const update = (delta: Partial<AutoClickerOptions>) => {
    const next = { ...options, ...delta }
    if ([next.shortcut, next.emergency].some(({ key, modifiers: flags }) => key < 112 && (flags & 11) === 0)) {
      setValidation('Ajoutez Ctrl, Alt ou Windows pour utiliser une lettre ou un chiffre.')
      return
    }
    if (next.shortcut.key === next.emergency.key && next.shortcut.modifiers === next.emergency.modifiers) {
      setValidation('Choisissez deux raccourcis différents.')
      return
    }
    setValidation('')
    onSave(next)
  }
  const { state } = status

  return <div className="clicker-layout">
    <section className="clicker-console" aria-labelledby="clicker-controls">
      <div className="clicker-console-head">
        <h2 id="clicker-controls">Commandes</h2>
        <span className={`status status-${state === 'active' ? 'positive' : state === 'paused' ? 'warning' : 'neutral'}`}
          aria-live="polite"><span className="status-dot" aria-hidden="true" />{labels[state]}</span>
      </div>
      <div className="clicker-keyline">
        <span>{options.mode === 'hold' ? 'Maintenir pour cliquer' : 'Démarrer / arrêter'}</span>
        <span>{shortcutLabel(options.shortcut).map((label) => <kbd key={label}>{label}</kbd>)}</span>
      </div>
      <div className="clicker-keyline">
        <span>Arrêt d’urgence</span>
        <span>{shortcutLabel(options.emergency).map((label) => <kbd key={label}>{label}</kbd>)}</span>
      </div>
      <button type="button" className="button secondary" disabled={!canEdit}
        onClick={state === 'paused' ? onRearm : () => { void onPause() }}>
        {state === 'paused' ? <><Play size={16} aria-hidden="true" /> Réarmer</> :
          <><Pause size={16} aria-hidden="true" /> Mettre en pause</>}
      </button>
      {(validation || status.error) && <p className="notice" role="alert"><CircleAlert size={16} aria-hidden="true" />
        {validation || status.error}</p>}
    </section>

    <section className="clicker-configuration" aria-labelledby="clicker-settings">
      <div className="section-heading"><h2 id="clicker-settings">Configuration</h2><span className="fine">Sauvegarde automatique</span></div>
      <fieldset disabled={!canEdit} className="clicker-fields">
        <legend>Séquence</legend>
        <div className="clicker-grid">
          <label>Type de clic
            <select aria-label="Type de clic" value={options.kind} onChange={(event) => update({ kind: event.target.value as AutoClickerOptions['kind'] })}>
              <option value="left">Gauche</option>
              <option value="right">Droit</option>
              <option value="middle">Molette</option>
              <option value="double">Double-clic</option>
            </select>
          </label>
          <div>
            <div id="clickerSpeed"><NumberSetting label="Vitesse (CPS)" value={options.cps} min={1} max={200} unit="CPS" onChange={(cps) => update({ cps })} /></div>
            <output className="fine mono" aria-label="Intervalle entre actions">{milliseconds.format(1000 / options.cps)} ms par action</output>
          </div>
          <label>Mode
            <select aria-label="Mode" value={options.mode} onChange={(event) => update({ mode: event.target.value as AutoClickerOptions['mode'] })}>
              <option value="toggle">Bascule (Toggle)</option>
              <option value="hold">Maintien (Hold)</option>
            </select>
          </label>
          <label>Position
            <select aria-label="Position" value={options.position} onChange={(event) => update({ position: event.target.value as AutoClickerOptions['position'] })}>
              <option value="cursor">Suivre le curseur</option>
              <option value="fixed">Coordonnées fixes</option>
            </select>
          </label>
          {options.position === 'fixed' && <>
            <NumberSetting label="Coordonnée X" value={options.x} min={-2147483648} max={2147483647} unit="px" onChange={(x) => update({ x })} />
            <NumberSetting label="Coordonnée Y" value={options.y} min={-2147483648} max={2147483647} unit="px" onChange={(y) => update({ y })} />
          </>}
          <label>Limite
            <select aria-label="Limite" value={options.limit} onChange={(event) => update({ limit: event.target.value as AutoClickerOptions['limit'] })}>
              <option value="unlimited">Illimitée</option>
              <option value="count">Nombre d’actions</option>
              <option value="duration">Durée maximale</option>
            </select>
          </label>
          {options.limit === 'count' && <NumberSetting label="Nombre d’actions" value={options.count} min={1} max={1_000_000}
            onChange={(count) => update({ count })} />}
          {options.limit === 'duration' && <NumberSetting label="Durée maximale (s)" value={options.durationSeconds} min={1} max={86_400}
            onChange={(durationSeconds) => update({ durationSeconds })} />}
        </div>
        {options.kind === 'double' && <p className="fine">Un double-clic compte pour une action.</p>}
      </fieldset>
      <fieldset disabled={!canEdit} className="clicker-fields">
        <legend>Raccourcis globaux</legend>
        <ShortcutField label="Activation" value={options.shortcut} onChange={(shortcut) => update({ shortcut })}
          onRecord={() => state !== 'paused' ? onPause() : Promise.resolve(true)} />
        <ShortcutField label="Arrêt d’urgence" value={options.emergency} onChange={(emergency) => update({ emergency })}
          onRecord={() => state !== 'paused' ? onPause() : Promise.resolve(true)} />
        <p className="fine">Si une touche est déjà capturée par Windows, utilisez les listes. Échap annule l’enregistrement des touches.</p>
      </fieldset>
    </section>
  </div>
}
