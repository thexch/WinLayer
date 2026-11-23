import { useEffect, useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import {
  BatteryCharging, Circle, Cpu, Crosshair, Download, Droplet, Folder, Image, Keyboard, Layers, LayoutGrid, Monitor,
  MousePointerClick, Pin, Radio, ScanLine, Search, Settings2, Star, TextCursor, Workflow, X,
} from 'lucide-react'
import { Choice, Flag, Hotkey, NumberField } from './HubControls'
import { shortcutName } from './hub'
import { defaults, luminance, searchEntries, standardThemes, type AppTheme, type Entry, type UtilityOptions, type UtilityRequest } from './utility'

const glyphs = {
  home: LayoutGrid, battery: BatteryCharging, mouse: MousePointerClick, keyboard: Keyboard, macro: Workflow, pin: Pin,
  windows: ScanLine, layers: Layers, screen: Monitor, color: Droplet, text: TextCursor, folder: Folder, circle: Circle,
  search: Search, image: Image, network: Radio, cpu: Cpu, settings: Settings2, download: Download,
}

export function EntryIcon({ entry, size = 19 }: { entry: Entry; size?: number }) {
  const Glyph = glyphs[entry.icon as keyof typeof glyphs] ?? Crosshair
  return <Glyph size={size} aria-hidden="true" />
}

export function ActionSearch({ open, setOpen, items, options, recent, choose, favorite, canEdit }: {
  open: boolean; setOpen: (open: boolean) => void; items: Entry[]; options: UtilityOptions; recent: string[]
  choose: (item: Entry) => void; favorite: (id: string) => void; canEdit: boolean
}) {
  const [query, setQuery] = useState('')
  const [index, setIndex] = useState(0)
  const input = useRef<HTMLInputElement>(null)
  const pendingChoice = useRef<Entry | null>(null)
  const priority = new Set([...options.favorites, ...recent])
  const initial = [...priority].flatMap(id => items.find(item => item.id === id) ?? [])
  const matches = searchEntries(query.trim() ? items : [...initial, ...items.filter(item => !priority.has(item.id))], query).slice(0, 12)
  const active = Math.min(index, Math.max(0, matches.length - 1))
  function select(item: Entry) { pendingChoice.current = item; changeOpen(false) }
  function changeOpen(next: boolean) { setOpen(next); if (!next) { setQuery(''); setIndex(0) } }

  useEffect(() => {
    if (open) document.getElementById('search-action-' + active)?.scrollIntoView({ block: 'nearest' })
  }, [active, open])

  const highlight = (label: string) => {
    const needle = query.trim().toLowerCase().normalize('NFD').replace(/\p{M}/gu, '')
    const text = label.toLowerCase().normalize('NFD').replace(/\p{M}/gu, '')
    const start = needle ? text.indexOf(needle) : -1
    return start < 0 ? label : <>{label.slice(0, start)}<mark>{label.slice(start, start + needle.length)}</mark>{label.slice(start + needle.length)}</>
  }
  return <Dialog.Root open={open} onOpenChange={changeOpen}>
    <Dialog.Trigger asChild>
      <button type="button" className="hub-search-trigger" aria-keyshortcuts="Control+K"><Search size={17} aria-hidden="true" /><span>Rechercher</span><kbd aria-hidden="true">Ctrl K</kbd></button>
    </Dialog.Trigger>
    <Dialog.Portal>
      <Dialog.Overlay className="action-search-shade" />
      <Dialog.Content className="action-search" onOpenAutoFocus={event => { event.preventDefault(); input.current?.focus() }}
        onCloseAutoFocus={event => {
          const item = pendingChoice.current
          pendingChoice.current = null
          if (item?.target) event.preventDefault()
          if (item) choose(item)
        }}>
        <Dialog.Title className="sr-only">Recherche WinLayer</Dialog.Title>
        <Dialog.Description className="sr-only">Rechercher un outil, un réglage ou une macro.</Dialog.Description>
        <div className="action-search-input"><Search size={21} aria-hidden="true" />
          <input ref={input} aria-label="Chercher une action ou un réglage" placeholder="Un outil, un réglage, une macro…"
            value={query} maxLength={120} onChange={event => { setQuery(event.target.value); setIndex(0) }}
            onKeyDown={event => {
              if (event.key === 'Enter' && matches[active]) { event.preventDefault(); select(matches[active]) }
              if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault()
                setIndex(Math.max(0, Math.min(matches.length - 1, active + (event.key === 'ArrowDown' ? 1 : -1))))
              }
            }} />
          <Dialog.Close asChild><button type="button" className="icon-button" aria-label="Fermer la recherche"><X size={18} /></button></Dialog.Close>
        </div>
        <ul aria-label="Résultats de recherche" className="action-search-results">
          {matches.map((item, i) => <li key={item.id} id={'search-action-' + i} className={active === i ? 'search-current' : undefined}>
            <button type="button" className="action-result" onClick={() => select(item)}>
              <EntryIcon entry={item} /><span><strong>{highlight(item.label)}</strong><small>{item.category}</small></span><span aria-hidden="true">{item.page ? '↗' : '↵'}</span>
            </button>
            <button type="button" aria-label={`Favori : ${item.label}`} aria-pressed={options.favorites.includes(item.id)}
              disabled={!canEdit} className="icon-button favorite-button" onClick={() => favorite(item.id)}><Star size={17} aria-hidden="true" /></button>
          </li>)}
        </ul>
        {matches.length === 0 && <p className="search-empty">Aucun résultat pour cette recherche.</p>}
        <div className="search-hint"><kbd>↑ ↓</kbd> Parcourir <kbd>Entrée</kbd> Ouvrir <kbd>Échap</kbd> Fermer</div>
      </Dialog.Content>
    </Dialog.Portal>
  </Dialog.Root>
}

export function ScreenUtilities({ options, canEdit, run }: {
  options: UtilityOptions; canEdit: boolean; run: (request: UtilityRequest) => Promise<boolean>
}) {
  const [draft, setDraft] = useState(options)
  return <section className="utility-section" aria-labelledby="screen-capture-tools">
    <h2 id="screen-capture-tools">Captures et reconnaissance</h2>
    <div className="utility-launches">
      <button type="button" className="button secondary" disabled={!canEdit} onClick={() => void run({ action: 'execute', id: 'ocr' })}>
        <TextCursor size={18} aria-hidden="true" /> Screen OCR <kbd>{shortcutName(options.ocr)}</kbd>
      </button>
      <button type="button" className="button secondary" disabled={!canEdit} onClick={() => void run({ action: 'execute', id: 'screen-pin' })}>
        <Pin size={18} aria-hidden="true" /> Épingler une capture <kbd>{shortcutName(options.pin)}</kbd>
      </button>
      <button type="button" className="button secondary" disabled={!canEdit} onClick={() => void run({ action: 'execute', id: 'ocr-history' })}>Historique OCR</button>
    </div>
    <fieldset className="utility-controls" disabled={!canEdit}>
      <div id="ocrLanguage"><Choice label="Langue de reconnaissance" value={draft.language}
        choices={[[ 'fr', 'Français' ], [ 'en', 'English' ]]} onChange={language => setDraft({ ...draft, language })} /></div>
      <Flag label="Copier automatiquement le texte" value={draft.autoCopy} onChange={autoCopy => setDraft({ ...draft, autoCopy })} />
      <Flag label="Conserver un historique OCR local" value={draft.ocrHistory} onChange={ocrHistory => setDraft({ ...draft, ocrHistory })} />
      <button type="button" className="button primary" disabled={draft === options} onClick={() => void run({ action: 'save', options: draft })}>Enregistrer</button>
    </fieldset>
    <p className="fine">Screen OCR utilise la reconnaissance optique installée dans les paramètres de langue de Windows.</p>
  </section>
}

export function UtilitiesPage({ options, items, canEdit, undo, run, preview }: {
  options: UtilityOptions; items: Entry[]; canEdit: boolean; undo?: string | null; run: (request: UtilityRequest) => Promise<boolean>
  preview: (theme: AppTheme | undefined) => void
}) {
  const [draft, setDraft] = useState(options)
  const [theme, setTheme] = useState<AppTheme>(() => options.themes.find(item => item.name === options.theme) ?? standardThemes[1])
  const [message, setMessage] = useState('')
  const change = <K extends keyof UtilityOptions>(key: K, value: UtilityOptions[K]) => setDraft(current => ({ ...current, [key]: value }))
  const numbers = [['shelfHours', 'Conservation (heures)', 1, 72], ['shelfWidth', 'Largeur du panier', 320, 800], ['shelfHeight', 'Hauteur du panier', 280, 760]] as const
  const locks = [['caps', 'Caps Lock'], ['num', 'Num Lock'], ['scroll', 'Scroll Lock'], ['compactOsd', 'Affichage compact'], ['hideOsdFullscreen', 'Masquer en plein écran']] as const
  const shortcuts = [['ocr', 'Screen OCR'], ['pin', 'Screen Pin'], ['shelf', 'Drop Shelf'], ['palette', 'Palette de commandes'], ['radial', 'Menu radial']] as const
  const choices = items.map(item => [item.id, item.label] as [string, string])
  const [presetName, setPresetName] = useState('')
  const [sector, setSector] = useState(0)
  const [selectedCorner, setSelectedCorner] = useState(0)
  const shownSector = Math.min(sector, draft.radialItems.length - 1)
  const moveSector = (step: number) => {
    const next = (shownSector + step + draft.radialItems.length) % draft.radialItems.length
    const actions = [...draft.radialItems]
    ;[actions[shownSector], actions[next]] = [actions[next], actions[shownSector]]
    change('radialItems', actions)
    setSector(next)
  }
  const emptyChoices: [string, string][] = [['', 'Aucune action'], ...choices]

  async function save() {
    setMessage('')
    if (new Set(shortcuts.map(([key]) => `${draft[key].key}/${draft[key].modifiers}`)).size !== shortcuts.length) {
      setMessage('Choisissez un raccourci différent pour chaque outil.')
      return
    }
    await run({ action: 'save', options: draft })
  }

  async function saveTheme() {
    const contrast = (a: string, b: string) => (Math.max(luminance(a), luminance(b)) + .05) / (Math.min(luminance(a), luminance(b)) + .05)
    if (!theme.name.trim() || contrast(theme.ink, theme.canvas) < 4.5 || contrast(theme.ink, theme.surface) < 4.5 ||
        contrast(theme.accent, theme.canvas) < 3 || contrast(theme.accent, theme.surface) < 3) {
      setMessage('Choisissez un nom et augmentez le contraste des couleurs.')
      return
    }
    const themes = draft.themes.filter(item => item.name.toLowerCase() !== theme.name.trim().toLowerCase())
    const selected = { ...theme, name: theme.name.trim() }
    if (themes.length >= 8) { setMessage('La bibliothèque accepte huit thèmes. Supprimez un thème pour libérer une place.'); return }
    setMessage('')
    if (await run({ action: 'save', options: { ...draft, themes: [...themes, selected], theme: selected.name } })) preview(undefined)
  }

  return <div className="utilities-layout">
    <div className="utility-launches">
      {items.filter(item => ['shelf', 'radial', 'palette', 'converter'].includes(item.id)).map(item =>
        <button type="button" key={item.id} className="button secondary" disabled={!canEdit} onClick={() => void run({ action: 'execute', id: item.id })}>
          <EntryIcon entry={item} /> {item.label}
        </button>)}
    </div>
    <fieldset disabled={!canEdit} className="utility-section"><legend>Raccourcis globaux</legend>
      <div className="utility-controls">{shortcuts.map(([key, label]) => <Hotkey key={key} label={label} value={draft[key]} onChange={value => change(key, value)} />)}
        <Flag label="Conserver les actions récentes entre les sessions" value={draft.persistRecent} onChange={value => change('persistRecent', value)} />
      </div>
    </fieldset>
    <fieldset disabled={!canEdit} className="utility-section" id="shelfHours"><legend>Drop Shelf</legend>
      <div className="utility-controls">{numbers.map(([key, label, min, max]) =>
        <NumberField key={key} label={label} min={min} max={max} value={draft[key]} onChange={value => change(key, value)} />)}
        <Choice label="Position du panier" value={String(draft.shelfPosition)}
          choices={[[ '0', 'Haut gauche' ], [ '1', 'Haut droit' ], [ '2', 'Bas gauche' ], [ '3', 'Bas droit' ]]}
          onChange={value => change('shelfPosition', Number(value))} />
      </div>
    </fieldset>
    <fieldset disabled={!canEdit} className="utility-section" id="radialSettings"><legend>Menu radial</legend>
      <div className="radial-editor">
        <div className="radial-preview" aria-label="Aperçu du menu radial">
          {draft.radialItems.map((id, index) => {
            const item = items.find(entry => entry.id === id)
            const angle = Math.PI * (2 * index / draft.radialItems.length - .5)
            return <button key={index} type="button" aria-label={`Secteur ${index + 1} : ${item?.label ?? 'Action indisponible'}`}
              aria-pressed={index === shownSector} onClick={() => setSector(index)}
              style={{ left: (50 + 33 * Math.cos(angle)) + '%', top: (50 + 33 * Math.sin(angle)) + '%', borderColor: draft.radialAccent }}>
              {item ? <EntryIcon entry={item} /> : <Circle size={17} />}
            </button>
          })}
        </div>
        <div className="radial-editor-controls">
          <Choice label={`Action du secteur ${shownSector + 1}`} value={draft.radialItems[shownSector]}
            choices={choices} onChange={value => change('radialItems', draft.radialItems.map((item, i) => i === shownSector ? value : item))} />
          <div className="utility-launches">
            <button type="button" className="button secondary" disabled={draft.radialItems.length < 2} onClick={() => moveSector(-1)}>↶ Déplacer</button>
            <button type="button" className="button secondary" disabled={draft.radialItems.length < 2} onClick={() => moveSector(1)}>Déplacer ↷</button>
          </div>
        </div>
      </div>
      <div className="utility-controls">
        <label className="utility-color">Couleur du menu <input type="color" value={draft.radialAccent} onChange={event => change('radialAccent', event.target.value)} /></label>
        <NumberField label="Nombre de secteurs" min={1} max={8} value={draft.radialItems.length} onChange={value => change('radialItems', Array.from({ length: Math.max(1, Math.min(8, value)) }, (_, index) => draft.radialItems[index] ?? 'palette'))} />
      </div>
      <div className="utility-controls">
        <label className="utility-text">Nom du profil radial
          <input maxLength={40} value={presetName} onChange={event => setPresetName(event.target.value)} />
        </label>
        <button type="button" className="button secondary" disabled={!presetName.trim() || draft.radialPresets.length >= 8}
          onClick={() => {
            const name = presetName.trim()
            change('radialPresets', [...draft.radialPresets.filter(preset => preset.name.toLowerCase() !== name.toLowerCase()),
              { name, actions: draft.radialItems, accent: draft.radialAccent }])
            setPresetName('')
          }}>Mémoriser le profil</button>
      </div>
      <ul className="favorite-list">{draft.radialPresets.map(preset => <li key={preset.name}>
        <span>{preset.name}</span>
        <button type="button" className="button secondary" onClick={() => setDraft({ ...draft, radialItems: preset.actions, radialAccent: preset.accent })}>Charger</button>
        <button type="button" className="icon-button" aria-label={`Supprimer le profil ${preset.name}`}
          onClick={() => change('radialPresets', draft.radialPresets.filter(item => item.name !== preset.name))}><X size={17} /></button>
      </li>)}</ul>
    </fieldset>
    <fieldset disabled={!canEdit} className="utility-section" id="corners"><legend>Coins actifs</legend>
      <div className="utility-controls">
        <div className="corner-screen" aria-label="Coins d’un écran">
          {['Haut gauche', 'Haut droit', 'Bas gauche', 'Bas droit'].map((label, index) =>
            <button key={label} type="button" className="icon-button" aria-label={label}
              aria-pressed={selectedCorner === index} data-configured={Boolean(draft.corners[index].action)}
              onClick={() => setSelectedCorner(index)}>{['↖', '↗', '↙', '↘'][index]}</button>)}
        </div>
        <div className="corner-control">
          <Choice label={['Haut gauche', 'Haut droit', 'Bas gauche', 'Bas droit'][selectedCorner]} value={draft.corners[selectedCorner].action} choices={emptyChoices}
            onChange={action => change('corners', draft.corners.map((item, i) => i === selectedCorner ? { ...item, action } : item))} />
          <NumberField label="Délai d’activation (ms)" min={200} max={3000} value={draft.corners[selectedCorner].delay}
            onChange={delay => change('corners', draft.corners.map((item, i) => i === selectedCorner ? { ...item, delay } : item))} />
        </div>
        <Flag label="Maintenir Maj pour déclencher" value={draft.cornerModifier} onChange={value => change('cornerModifier', value)} />
        <Flag label="Ignorer les applications en plein écran" value={draft.excludeFullscreen} onChange={value => change('excludeFullscreen', value)} />
        <NumberField label="Taille de la zone (px)" min={2} max={24} value={draft.cornerPixels} onChange={value => change('cornerPixels', value)} />
        <NumberField label="Délai entre actions (ms)" min={500} max={10000} value={draft.cornerCooldown} onChange={value => change('cornerCooldown', value)} />
        <label className="utility-text">Applications à exclure, séparées par des virgules
          <input value={draft.cornerExclusions} maxLength={512} onChange={event => change('cornerExclusions', event.target.value)} />
        </label>
      </div>
    </fieldset>
    <fieldset disabled={!canEdit} className="utility-section"><legend>Applications, fichiers et dossiers</legend>
      <div className="utility-launches">
        <button type="button" className="button secondary" disabled={draft.launchers.length >= 12} onClick={() => void run({ action: 'addFile' })}>Ajouter un fichier ou une application…</button>
        <button type="button" className="button secondary" disabled={draft.launchers.length >= 12} onClick={() => void run({ action: 'addFolder' })}>Ajouter un dossier…</button>
      </div>
      <ul className="favorite-list">{draft.launchers.map(item => <li key={item.id}>
        <span><strong>{item.label}</strong><br /><small>{item.path}</small></span>
        <button type="button" className="icon-button" aria-label={`Retirer le raccourci ${item.label}`} onClick={() => change('launchers', draft.launchers.filter(action => action.id !== item.id))}><X size={17} /></button>
      </li>)}</ul>
    </fieldset>
    <fieldset disabled={!canEdit} className="utility-section" id="lockSettings"><legend>Indicateurs clavier</legend>
      <div className="utility-controls">
        {locks.map(([key, label]) => <Flag key={key} label={label} value={draft[key]} onChange={value => change(key, value)} />)}
        <NumberField label="Durée (secondes)" min={1} max={8} value={draft.osdSeconds} onChange={value => change('osdSeconds', value)} />
        <NumberField label="Opacité (%)" min={25} max={100} value={draft.osdOpacity} onChange={value => change('osdOpacity', value)} />
        <NumberField label="Taille (%)" min={75} max={150} value={draft.osdSize} onChange={value => change('osdSize', value)} />
        <Choice label="Position" value={String(draft.osdPosition)} choices={[[ '0', 'Haut' ], [ '1', 'Bas' ], [ '2', 'Centre' ]]} onChange={value => change('osdPosition', Number(value))} />
      </div>
    </fieldset>
    <fieldset disabled={!canEdit} className="utility-section"><legend>Favoris</legend>
      <p className="fine">Ajoutez une action à vos favoris avec l’étoile dans la recherche.</p>
      <ol className="favorite-list">{draft.favorites.map((id, index) => <li key={id}>
        <span>{items.find(item => item.id === id)?.label ?? id}</span>
        <button type="button" className="icon-button" disabled={index === 0} aria-label="Monter le favori" onClick={() => {
          const next = [...draft.favorites]; [next[index - 1], next[index]] = [next[index], next[index - 1]]; change('favorites', next)
        }}>↑</button>
        <button type="button" className="icon-button" aria-label="Retirer le favori" onClick={() => change('favorites', draft.favorites.filter(item => item !== id))}><X size={17} /></button>
      </li>)}</ol>
      <button type="button" className="button primary" disabled={draft === options} onClick={() => void save()}>Enregistrer les outils et favoris</button>
    </fieldset>
    <fieldset disabled={!canEdit} className="utility-section" id="themes"><legend>App Themes</legend>
      <div className="theme-swatches">{[...standardThemes, ...draft.themes.filter(item => !standardThemes.some(standard => item.name === standard.name))].map(item =>
        <button type="button" key={item.name} className="button secondary" aria-pressed={item.name === theme.name}
          onClick={() => { const selected = draft.themes.find(saved => saved.name === item.name) ?? item; setTheme(selected); preview(selected) }}>
          <i style={{ background: item.accent }} />{item.name}
        </button>)}</div>
      <div className="utility-controls">
        <label className="utility-text">Nom du thème <input value={theme.name} maxLength={40} onChange={event => setTheme({ ...theme, name: event.target.value })} /></label>
        {([['canvas', 'Fond'], ['surface', 'Surfaces'], ['ink', 'Texte'], ['accent', 'Accent'], ['border', 'Bordures']] as const).map(([key, label]) =>
          <label key={key} className="utility-color">{label} <input type="color" value={theme[key]} onChange={event => setTheme({ ...theme, [key]: event.target.value })} /></label>)}
        <NumberField label="Arrondis" min={4} max={30} value={theme.radius} onChange={radius => setTheme({ ...theme, radius })} />
        <NumberField label="Densité (%)" min={85} max={120} value={theme.density} onChange={density => setTheme({ ...theme, density })} />
        <Flag label="Animations" value={theme.motion} onChange={motion => setTheme({ ...theme, motion })} />
        <Choice label="Police" value={theme.font} choices={[[ 'Manrope', 'Manrope' ], [ 'Segoe UI', 'Segoe UI' ], [ 'Geist Mono', 'Geist Mono' ]]} onChange={font => setTheme({ ...theme, font })} />
      </div>
      <div className="utility-launches">
        <button type="button" className="button secondary" onClick={() => preview(theme)}>Prévisualiser</button>
        <button type="button" className="button primary" onClick={() => void saveTheme()}>Appliquer le thème</button>
        <button type="button" className="button secondary" onClick={async () => { if (await run({ action: 'save', options: { ...draft, theme: '' } })) preview(undefined) }}>Utiliser l’apparence générale</button>
        {draft.themes.some(item => item.name === theme.name) && <button type="button" className="button secondary" onClick={async () => {
          if (await run({ action: 'save', options: { ...draft, themes: draft.themes.filter(item => item.name !== theme.name), theme: draft.theme === theme.name ? '' : draft.theme } })) preview(undefined)
        }}>Supprimer ce thème</button>}
      </div>
    </fieldset>
    <fieldset disabled={!canEdit} className="utility-section" id="history"><legend>Restauration</legend>
      <div className="utility-launches">
        <button type="button" className="button secondary" disabled={!undo} onClick={() => void run({ action: 'undo' })}>{undo ? `Annuler · ${undo}` : 'Aucune modification à annuler'}</button>
        <button type="button" className="button secondary" onClick={() => void run({ action: 'export' })}>Exporter un profil</button>
        <button type="button" className="button secondary" onClick={() => void run({ action: 'import' })}>Importer un profil</button>
        <button type="button" className="button secondary" onClick={() => setDraft({ ...defaults, themes: draft.themes, theme: draft.theme, favorites: draft.favorites, launchers: draft.launchers })}>Rétablir les outils par défaut</button>
      </div>
    </fieldset>
    {message && <p className="notice" role="alert">{message}</p>}
  </div>
}
