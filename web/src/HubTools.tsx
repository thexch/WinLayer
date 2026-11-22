import { useState, type CSSProperties } from 'react'
import { ArrowDown, ArrowUp, Crosshair, Download, Folder, LayoutGrid, Monitor, MousePointer2, Play, Plus, Search, Square, Trash2 } from 'lucide-react'
import { Accent, Choice, Field, Flag, Hotkey, HubForm, NumberField, TabButtons, type RunHub } from './HubControls'
import { defaultHubOptions, virtualKeys, type HubOptions, type HubPreferences, type HubStatus, type HubTool,
  type MacroPreset, type MacroStep } from './hub'

export interface HubPageProps {
  data?: HubPreferences | null
  status: HubStatus
  run: RunHub
  busy: boolean
  setProcessView?: (active: boolean) => void
}

function useOptions(tool: HubTool, data?: HubPreferences | null) {
  return data?.tools[tool] ?? defaultHubOptions(tool)
}

function ToolFeedback({ status, tool }: { status: HubStatus; tool: HubTool }) {
  const error = status.errors[tool]
  return error && <p className="notice" role="alert">{error}</p>
}

export function TaskbarPage({ data, status, run, busy }: HubPageProps) {
  const saved = useOptions('taskbar', data)
  const options = { ...saved, taskbarEffect: saved.taskbarEffect ?? (saved.enabled ? 'accent' : 'windows') } as const
  const [name, setName] = useState('')
  return <>
    <ToolFeedback tool="taskbar" status={status} />
    <HubForm tool="taskbar" key={JSON.stringify(options)} options={options} run={run} busy={busy}
      extra={<button type="button" className="button secondary" disabled={busy || options.taskbarEffect === 'windows'} onClick={() => { void run({
        tool: 'taskbar', action: 'configure', options: { ...defaultHubOptions('taskbar'), styles: options.styles },
      }) }}>Revenir au style Windows</button>}>
      {(draft, patch) => <>
        <Choice label="Effet sur la barre" value={draft.taskbarEffect ?? 'windows'}
          choices={[[ 'windows', 'Style Windows' ], [ 'accent', 'Liseré' ], [ 'frame', 'Cadre' ], [ 'tint', 'Teinte' ]]}
          onChange={taskbarEffect => patch({ taskbarEffect, opacity: taskbarEffect === 'tint' ? Math.min(draft.opacity, 35) : draft.opacity })} />
        <div className="hub-taskbar-preview" role="img" aria-label="Aperçu de la barre des tâches"
          data-effect={draft.taskbarEffect}
          style={{ '--bar-accent': draft.color, '--bar-opacity': Math.min(draft.opacity, draft.taskbarEffect === 'tint' ? 35 : 100) / 100,
            '--bar-radius': draft.radius + 'px', '--bar-inset': draft.inset + 'px', '--bar-size': draft.size + 'px' } as CSSProperties}>
          <div className="hub-taskbar-sample">
            <LayoutGrid size={16} /><Search size={16} /><Folder size={16} /><Monitor size={16} />
            <span className="hub-taskbar-tray">⌃ &nbsp; 12:30</span>
          </div>
        </div>
        {draft.taskbarEffect !== 'windows' && <>
          <p className="fine">WinLayer dessine cet effet sur la barre principale. Clique sur Appliquer pour le voir sur ton bureau.</p>
          <div className="hub-form-grid">
            <Field label="Couleur"><input type="color" value={draft.color} onChange={event => patch({ color: event.currentTarget.value })} /></Field>
            <NumberField label="Opacité (%)" value={draft.opacity} min={5} max={draft.taskbarEffect === 'tint' ? 35 : 100} onChange={opacity => patch({ opacity })} />
            {draft.taskbarEffect !== 'tint' &&
              <NumberField label="Épaisseur (px)" value={draft.size} min={1} max={24} onChange={size => patch({ size })} />}
            <NumberField label="Arrondi (px)" value={draft.radius} min={0} max={16} onChange={radius => patch({ radius })} />
            <NumberField label="Retrait aux extrémités (px)" value={draft.inset} min={0} max={80} onChange={inset => patch({ inset })} />
          </div>
        </>}
        <div className="hub-inline hub-saved">
          {draft.styles.map(style => <span key={style.name} className="hub-style">
            <button type="button" className="button secondary" onClick={() => patch({ color: style.color, opacity: style.opacity, size: style.size, radius: style.radius, inset: style.inset, taskbarEffect: style.effect ?? 'accent', profile: style.name })}>{style.name}</button>
            <button type="button" className="icon-button" aria-label={`Supprimer le style ${style.name}`}
              onClick={() => patch({ styles: draft.styles.filter(item => item.name !== style.name) })}><Trash2 size={16} /></button>
          </span>)}
        </div>
        <div className="hub-inline">
          <Field label="Nom du style"><input value={name} maxLength={40} onChange={event => setName(event.currentTarget.value)} /></Field>
          <button type="button" className="button secondary" disabled={!name.trim() || draft.styles.length >= 8}
            onClick={() => {
              patch({ styles: [...draft.styles.filter(style => style.name.toLowerCase() !== name.trim().toLowerCase()),
                { name: name.trim(), color: draft.color, opacity: draft.opacity, size: draft.size, radius: draft.radius, inset: draft.inset, effect: draft.taskbarEffect ?? 'windows' }] })
              setName('')
            }}><Plus size={16} aria-hidden="true" /> Mémoriser le style</button>
        </div>
      </>}
    </HubForm>
  </>
}

export function SwitcherPage({ data, status, run, busy }: HubPageProps) {
  const options = useOptions('switcher', data)
  return <>
    <ToolFeedback tool="switcher" status={status} />
    <HubForm tool="switcher" key={JSON.stringify(options)} options={options} run={run} busy={busy}
      extra={<button className="button primary" disabled={busy} type="button"
        onClick={() => { void run({ tool: 'switcher', action: 'open' }) }}>Ouvrir le sélecteur</button>}>
      {(draft, patch) => <>
        <Choice label="Disposition" value={draft.layout} choices={[['grid', 'Grille'], ['list', 'Liste'], ['cards', 'Cartes']]}
          onChange={layout => patch({ layout })} />
        <div className="hub-form-grid">
          <NumberField label="Largeur (px)" value={draft.width} min={420} max={1000} onChange={width => patch({ width })} />
          <Field label="Couleur d’accent"><input type="color" value={draft.color}
            onChange={event => patch({ color: event.currentTarget.value })} /></Field>
        </div>
        <Field label="Applications exclues"><input maxLength={512} placeholder="explorer, notepad" value={draft.exclusions}
          onChange={event => patch({ exclusions: event.currentTarget.value })} /></Field>
        <Hotkey label="Raccourci global" value={draft.shortcut} onChange={shortcut => patch({ shortcut })} />
        <p className="fine">Utilise les flèches ou Tab pour parcourir, Entrée pour ouvrir et Échap pour fermer. Alt+Tab de Windows reste disponible.</p>
      </>}
    </HubForm>
  </>
}

export function RepeaterPage({ data, status, run, busy }: HubPageProps) {
  const options = useOptions('repeater', data)
  const state = { idle: 'Inactif', active: 'Actif', paused: 'En pause' }[status.repeater.state]
  return <>
    <ToolFeedback tool="repeater" status={status} />
    {status.repeater.error && <p className="notice" role="alert">{status.repeater.error}</p>}
    <HubForm tool="repeater" key={JSON.stringify(options)} options={options} run={run} busy={busy} status={state}
      extra={<button type="button" className="button secondary" disabled={busy}
        onClick={() => { void run({ tool: 'repeater', action: state === 'En pause' ? 'rearm' : 'stop' }) }}>
        {state === 'En pause' ? 'Réarmer' : 'Mettre en pause'}
      </button>}>
      {(draft, patch) => {
        const changeRepeat = (value: Partial<HubOptions['repeat']>) => patch({ repeat: { ...draft.repeat, ...value } })
        const interval = draft.repeat.cps > 0 ? (1000 / draft.repeat.cps).toLocaleString('fr-FR', { maximumFractionDigits: 2 }) : '—'
        return <>
          <Hotkey label="Touche à répéter" value={{ key: draft.outputKey, modifiers: draft.outputModifiers }}
            onChange={({ key: outputKey, modifiers: outputModifiers }) => patch({ outputKey, outputModifiers })} output />
          <div className="hub-form-grid">
            <div>
              <NumberField label="Fréquence (répétitions/s)" min={1} max={200} value={draft.repeat.cps} onChange={cps => changeRepeat({ cps })} />
              <output className="fine">{interval} ms par répétition</output>
            </div>
            <Choice label="Mode" value={draft.repeat.mode} choices={[['toggle', 'Bascule'], ['hold', 'Maintien']]}
              onChange={mode => changeRepeat({ mode })} />
            <Choice label="Limite" value={draft.repeat.limit} choices={[['unlimited', 'Illimitée'], ['count', 'Nombre'], ['duration', 'Durée']]}
              onChange={limit => changeRepeat({ limit })} />
            {draft.repeat.limit === 'count' && <NumberField label="Nombre maximal" min={1} max={1000000} value={draft.repeat.count}
              onChange={count => changeRepeat({ count })} />}
            {draft.repeat.limit === 'duration' && <NumberField label="Durée maximale (s)" min={1} max={86400}
              value={draft.repeat.durationSeconds} onChange={durationSeconds => changeRepeat({ durationSeconds })} />}
          </div>
          <Hotkey label="Déclenchement" value={draft.repeat.shortcut} onChange={shortcut => changeRepeat({ shortcut })} />
          <Hotkey label="Arrêt d’urgence" value={draft.repeat.emergency} onChange={emergency => changeRepeat({ emergency })} />
        </>
      }}
    </HubForm>
  </>
}

export function ScreenToolsPage(props: HubPageProps) {
  const [tool, setTool] = useState<'picker' | 'ruler'>('picker')
  const { data, status, run, busy } = props
  const [palette, setPalette] = useState('')
  const options = useOptions(tool, data)
  const history = data?.colors ?? []
  const favorites = data?.favorites ?? []
  const [shownPalette, setShownPalette] = useState('')
  const shown = options.palettes.find(item => item.name === shownPalette)?.colors ?? []
  return <>
    <TabButtons label="Outils d’écran" choices={[[ 'picker', 'Color Picker' ], [ 'ruler', 'Screen Ruler' ]]} active={tool} select={setTool} />
    <ToolFeedback tool={tool} status={status} />
    <HubForm tool={tool} key={`${tool}/${JSON.stringify(options)}`} options={options} run={run} busy={busy}
      extra={<><button className="button primary" type="button" disabled={busy}
        onClick={() => { void run({ tool, action: 'open' }) }}>
        <Crosshair size={17} aria-hidden="true" /> {tool === 'picker' ? 'Prélever' : 'Mesurer'}
      </button>
        {tool === 'ruler' && <output className="mono">{status.measurement ?? 'Aucune mesure'}</output>}
        {tool === 'picker' && status.selectedColor && <output className="hub-color-result"><span style={{ backgroundColor: status.selectedColor }} />
          <strong className="mono">{status.selectedColor}</strong></output>}
      </>}>
      {(draft, patch) => <>
        {tool === 'ruler' ? <>
          <Choice label="Axe de mesure" value={draft.rulerMode} choices={[['free', 'Libre'], ['horizontal', 'Horizontal'], ['vertical', 'Vertical']]}
            onChange={rulerMode => patch({ rulerMode })} />
          <p className="fine">Placez deux points. Faites glisser l’un d’eux pour l’ajuster, puis utilisez C pour copier ou Échap pour fermer.</p>
        </> : <>
          <div className="hub-form-grid">
            <Choice label="Format de copie" value={draft.colorFormat} choices={[[ 'hex', 'HEX' ], [ 'rgb', 'RGB' ], [ 'hsl', 'HSL' ]]}
              onChange={colorFormat => patch({ colorFormat })} />
            <NumberField label="Grossissement" min={4} max={24} value={draft.zoom} onChange={zoom => patch({ zoom })} />
          </div>
          <Flag label="Copier la couleur sélectionnée" value={draft.autoCopy} onChange={autoCopy => patch({ autoCopy })} />
          <div className="hub-inline">
            <Field label="Nom de la palette"><input value={palette} maxLength={40} onChange={event => setPalette(event.currentTarget.value)} /></Field>
            <button type="button" className="button secondary" disabled={!palette.trim() || favorites.length === 0 || draft.palettes.length >= 8}
              onClick={() => {
                patch({ palettes: [...draft.palettes.filter(item => item.name !== palette.trim()), { name: palette.trim(), colors: favorites }] })
                setPalette('')
              }}>Sauver les favoris en palette</button>
          </div>
          <p className="fine">Les couleurs proviennent d’une capture ponctuelle du bureau.</p>
        </>}
        <Hotkey label="Raccourci global" value={draft.shortcut} onChange={shortcut => patch({ shortcut })} />
      </>}
    </HubForm>
    {tool === 'picker' && <section className="hub-panel hub-palette">
      <div className="hub-panel-heading"><h2>Couleurs</h2>
        <Choice label="Palette" value={shownPalette} choices={[[ '', 'Historique et favoris' ], ...options.palettes.map(item => [item.name, item.name] as const)]}
          onChange={setShownPalette} />
      </div>
      {(shownPalette ? shown : [...new Set([...favorites, ...history])]).length === 0 && <p className="fine">Les couleurs prélevées apparaîtront ici.</p>}
      {status.selectedColor && <dl className="hub-metric-pairs hub-selected-formats">
        <div><dt>HEX</dt><dd>{status.selectedColor}</dd></div>
        <div><dt>RGB</dt><dd>{status.selectedRgb ?? '—'}</dd></div>
        <div><dt>HSL</dt><dd>{status.selectedHsl ?? '—'}</dd></div>
      </dl>}
      <ul className="hub-swatches">
        {(shownPalette ? shown : [...new Set([...favorites, ...history])]).map(hex => <li key={hex}>
          <button type="button" onClick={() => { void run({ tool, action: 'copyColor', value: hex }) }} disabled={busy}
            aria-label={`Copier ${hex}`} className="hub-swatch"><span style={{ backgroundColor: hex }} /><strong className="mono">{hex}</strong></button>
          <button type="button" className="button secondary" aria-pressed={favorites.includes(hex)}
            aria-label={`Favori ${hex}`} disabled={busy} onClick={() => { void run({ tool, action: 'color', value: hex }) }}>★</button>
        </li>)}
      </ul>
    </section>}
  </>
}

export function PinPage({ data, status, run, busy }: HubPageProps) {
  const options = useOptions('pin', data)
  return <>
    <ToolFeedback tool="pin" status={status} />
    <HubForm tool="pin" options={options} key={JSON.stringify(options)} run={run} busy={busy}
      extra={<button type="button" disabled={busy || status.pinned.length === 0} className="button secondary"
        onClick={() => { void run({ tool: 'pin', action: 'pinAllOff' }) }}>Tout désépingler</button>}>
      {(draft, patch) => <>
        <Hotkey label="Épingler / désépingler la fenêtre active" value={draft.shortcut} onChange={shortcut => patch({ shortcut })} />
        <Flag label="Afficher un contour autour des fenêtres épinglées" value={draft.indicator}
          onChange={indicator => patch({ indicator })} />
        {draft.indicator && <Accent options={draft} update={patch} />}
        <Field label="Applications exclues"><input value={draft.exclusions} maxLength={512}
          onChange={event => patch({ exclusions: event.currentTarget.value })} /></Field>
        <Field label="Épingler automatiquement au premier plan"><input placeholder="notepad, calculator" value={draft.pinRules} maxLength={512}
          onChange={event => patch({ pinRules: event.currentTarget.value })} /></Field>
        <p className="fine">Séparez les noms de processus par des virgules. Les fenêtres épinglées par WinLayer sont restaurées à la désactivation.</p>
      </>}
    </HubForm>
    <section className="hub-panel"><h2>Fenêtres épinglées</h2>
      {status.pinned.length === 0 ? <p className="fine">Placez une fenêtre au premier plan et utilisez votre raccourci.</p> :
        <ul className="hub-detail-list">{status.pinned.map(window => <li key={window.handle}><strong>{window.title}</strong><span>{window.app}</span></li>)}</ul>}
    </section>
  </>
}

const stepChoices = [['wait', 'Attendre'], ['key', 'Touche'], ['move', 'Déplacement'], ['left', 'Clic gauche'], ['right', 'Clic droit'],
  ['middle', 'Clic molette']] as const
const wait: MacroStep = { kind: 'wait', delay: 250, x: 0, y: 0, key: 32, modifiers: 0 }

export function MacroPage({ data, status, run, busy }: HubPageProps) {
  const options = useOptions('macro', data)
  const [macro, setMacro] = useState<MacroPreset>({ name: '', steps: [] })
  const [fileError, setFileError] = useState('')
  const [selected, setSelected] = useState(0)
  const entry = macro.steps[selected]
  const state = status.macroState
  const writeStep = (newStep: MacroStep) => setMacro(value => ({ ...value, steps: value.steps.map((step, index) => index === selected ? newStep : step) }))
  function reorder(delta: number) {
    const next = selected + delta
    if (!entry || next < 0 || next >= macro.steps.length) return
    setMacro(value => {
      const steps = value.steps.slice()
      ;[steps[selected], steps[next]] = [steps[next]!, steps[selected]!]
      return { ...value, steps }
    })
    setSelected(next)
  }

  async function importFile(file: File | undefined) {
    if (!file) return
    setFileError('')
    try {
      if (file.size > 32_000) throw new Error()
      const value: unknown = JSON.parse(await file.text())
      if (!validMacro(value)) throw new Error()
      setMacro(value)
      setSelected(0)
    } catch { setFileError('Le fichier ne respecte pas le format des macros WinLayer.') }
  }

  return <>
    <ToolFeedback tool="macro" status={status} />
    <HubForm tool="macro" key={JSON.stringify(options)} options={options} run={run} busy={busy}
      status={state ? { playing: 'Lecture', recording: 'Enregistrement', paused: 'En pause' }[state] : undefined}
      extra={<>
        <button type="button" className="button primary" disabled={busy || !!state}
          onClick={() => { void run({ tool: 'macro', action: 'open' }) }}><MousePointer2 size={16} aria-hidden="true" /> Enregistrer</button>
        {state && <button type="button" className="button secondary" disabled={busy}
          onClick={() => { void run({ tool: 'macro', action: 'stop' }) }}><Square size={16} aria-hidden="true" /> Arrêter</button>}
        <button type="button" className="button secondary" disabled={busy || state === 'recording' || status.recording.length === 0}
          onClick={() => { setMacro({ name: 'Nouvel enregistrement', steps: status.recording }); setSelected(0) }}>
          Charger la capture ({status.recording.length})
        </button>
      </>}>
      {(draft, patch) => <>
        <p className="fine">Saisissez vos actions dans la fenêtre d’enregistrement WinLayer. Évitez les mots de passe et les données sensibles.</p>
        <Choice label="Macro du raccourci" value={draft.selectedMacro}
          choices={[[ '', 'Aucune' ], ...(data?.macros ?? []).map(item => [item.name, item.name] as const)]}
          onChange={selectedMacro => patch({ selectedMacro })} />
        <div className="hub-form-grid">
          <NumberField label="Vitesse (%)" value={draft.speedPercent} min={25} max={400} onChange={speedPercent => patch({ speedPercent })} />
          <NumberField label="Répétitions" value={draft.playCount} min={1} max={100} onChange={playCount => patch({ playCount })} />
        </div>
        <Flag label="Lire en boucle" value={draft.loop} onChange={loop => patch({ loop })} />
        <Field label="Applications exclues"><input value={draft.exclusions} maxLength={512}
          onChange={event => patch({ exclusions: event.currentTarget.value })} /></Field>
        <Hotkey label="Lecture / arrêt" value={draft.shortcut} onChange={shortcut => patch({ shortcut })} />
        <Hotkey label="Arrêt d’urgence" value={draft.repeat.emergency}
          onChange={emergency => patch({ repeat: { ...draft.repeat, emergency } })} />
      </>}
    </HubForm>
    <section className="hub-panel hub-macro">
      <div className="hub-panel-heading"><h2>Bibliothèque et éditeur</h2>
        <Field label="Importer un JSON"><input type="file" accept=".json,application/json"
          onChange={event => { void importFile(event.currentTarget.files?.[0]); event.currentTarget.value = '' }} /></Field>
      </div>
      {fileError && <p className="notice" role="alert">{fileError}</p>}
      {data?.macros.length ? <ul className="hub-detail-list">
        {data.macros.map(value => <li key={value.name}>
          <button type="button" className="text-button" onClick={() => { setMacro(value); setSelected(0) }}>{value.name}</button>
          <div className="hub-inline">
            <button className="icon-button" type="button" aria-label={`Lire ${value.name}`} disabled={busy}
              onClick={() => { void run({ tool: 'macro', action: 'play', value: value.name }) }}><Play size={16} /></button>
            <button className="icon-button" type="button" aria-label={`Supprimer ${value.name}`} disabled={busy}
              onClick={() => { void run({ tool: 'macro', action: 'deleteMacro', value: value.name }) }}><Trash2 size={16} /></button>
          </div>
        </li>)}
      </ul> : <p className="fine">Enregistrez une macro, importez-en une ou ajoutez des étapes.</p>}
      <Field label="Nom de la macro"><input value={macro.name} maxLength={60}
        onChange={event => setMacro(value => ({ ...value, name: event.currentTarget.value }))} /></Field>
      <div className="hub-step-layout">
        <div className="hub-step-list" role="group" aria-label="Étapes">
          {macro.steps.map((step, index) => <button type="button" key={index} className="button secondary" aria-pressed={selected === index}
            onClick={() => setSelected(index)}>{index + 1}. {stepChoices.find(([kind]) => kind === step.kind)?.[1]} · {step.delay} ms</button>)}
        </div>
        {entry && <div className="hub-step-details">
          <Choice label="Action" value={entry.kind} choices={stepChoices} onChange={kind => writeStep({ ...entry, kind, key: entry.key || 32 })} />
          <NumberField label="Délai (ms)" value={entry.delay} min={0} max={60000} onChange={delay => writeStep({ ...entry, delay })} />
          {entry.kind === 'key' && <Hotkey label="Touche" value={{ key: entry.key, modifiers: entry.modifiers }} output
            onChange={key => writeStep({ ...entry, ...key })} />}
          {entry.kind !== 'key' && entry.kind !== 'wait' && <div className="hub-form-grid">
            <NumberField label="X relatif (0–10 000)" min={0} max={10000} value={entry.x} onChange={x => writeStep({ ...entry, x })} />
            <NumberField label="Y relatif (0–10 000)" min={0} max={10000} value={entry.y} onChange={y => writeStep({ ...entry, y })} />
          </div>}
          <div className="hub-inline">
            <button type="button" className="icon-button" aria-label="Monter l’étape" disabled={selected === 0} onClick={() => reorder(-1)}><ArrowUp size={16} /></button>
            <button type="button" className="icon-button" aria-label="Descendre l’étape" disabled={selected === macro.steps.length - 1} onClick={() => reorder(1)}><ArrowDown size={16} /></button>
            <button type="button" className="icon-button" aria-label="Retirer l’étape" onClick={() => {
              setMacro(value => ({ ...value, steps: value.steps.filter((_, index) => index !== selected) }))
              setSelected(Math.max(0, selected - 1))
            }}><Trash2 size={16} /></button>
          </div>
        </div>}
      </div>
      <div className="hub-inline">
        <button type="button" className="button secondary" disabled={macro.steps.length >= 240}
          onClick={() => { setSelected(macro.steps.length); setMacro(value => ({ ...value, steps: [...value.steps, wait] })) }}>
          <Plus size={16} aria-hidden="true" /> Ajouter un délai
        </button>
        <button type="button" className="button primary" disabled={busy || !validMacro(macro)}
          onClick={() => { void run({ tool: 'macro', action: 'saveMacro', macro }) }}>Enregistrer la macro</button>
        <button type="button" className="button secondary" disabled={busy || !validMacro(macro)}
          onClick={() => { void run({ tool: 'macro', action: 'exportMacro', macro }) }}><Download size={16} aria-hidden="true" /> Exporter</button>
      </div>
    </section>
  </>
}

function validMacro(value: unknown): value is MacroPreset {
  if (!value || typeof value !== 'object' || !('name' in value) || typeof value.name !== 'string' ||
      !value.name.trim() || value.name.length > 60 || !('steps' in value) || !Array.isArray(value.steps) ||
      value.steps.length === 0 || value.steps.length > 240 || Object.keys(value).length !== 2) return false
  const bounded = (value: unknown, max: number) => typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= max
  return value.steps.every(step => step && typeof step === 'object' && Object.keys(step).length === 6 &&
    stepChoices.some(([kind]) => kind === step.kind) && bounded(step.delay, 60000) && bounded(step.x, 10000) &&
    bounded(step.y, 10000) && bounded(step.key, 255) && bounded(step.modifiers, 7) &&
    (step.kind !== 'key' || virtualKeys.includes(step.key)))
}
