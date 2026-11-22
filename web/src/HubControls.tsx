import { useId, useState, type ReactNode } from 'react'
import { Command } from 'lucide-react'
import type { ClickShortcut } from './bridge'
import { hotkeyKeys, keyName, modifierNames, shortcutName, virtualKeys, type HubOptions, type HubRequest, type HubTool } from './hub'

export type RunHub = (request: HubRequest) => Promise<boolean>

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return <label className="hub-field"><span>{label}</span>{children}</label>
}

export function NumberField({ label, value, min, max, onChange }: {
  label: string; value: number; min: number; max: number; onChange: (number: number) => void
}) {
  return <Field label={label}><input type="number" min={min} max={max} value={value}
    onChange={event => onChange(event.currentTarget.valueAsNumber || 0)} /></Field>
}

export function Choice<Value extends string>({ label, value, choices, onChange }: {
  label: string; value: Value; choices: readonly (readonly [Value, string])[]; onChange: (value: Value) => void
}) {
  const id = useId()
  return <div className="hub-field"><label htmlFor={id}>{label}</label>
    <select id={id} value={value} onChange={event => onChange(event.currentTarget.value as Value)}>
      {choices.map(([id, name]) => <option key={id} value={id}>{name}</option>)}
    </select>
  </div>
}

export function Flag({ label, value, onChange }: { label: string; value: boolean; onChange: (value: boolean) => void }) {
  return <label className="hub-check"><input type="checkbox" checked={value} onChange={event => onChange(event.currentTarget.checked)} />{label}</label>
}

export function Hotkey({ label, value, onChange, output = false }: {
  label: string; value: ClickShortcut; onChange: (value: ClickShortcut) => void; output?: boolean
}) {
  const id = useId()
  return <fieldset className="hub-hotkey">
    <legend>{label}</legend>
    <label className="sr-only" htmlFor={id}>{label} · touche</label>
    <select id={id} value={value.key} onChange={event => onChange({ ...value, key: Number(event.currentTarget.value) })}>
      {(output ? virtualKeys : hotkeyKeys).map(key => <option key={key} value={key}>{keyName(key)}</option>)}
    </select>
    <div className="hub-inline">
      {modifierNames.filter(({ mask }) => !output || mask !== 8).map(({ name, mask }) => <Flag key={name} label={name}
        value={(value.modifiers & mask) !== 0} onChange={checked => onChange({
          ...value, modifiers: checked ? value.modifiers | mask : value.modifiers & ~mask,
        })} />)}
    </div>
  </fieldset>
}

export function HubForm({ tool, options, run, busy, children, extra, status }: {
  tool: HubTool; options: HubOptions; run: RunHub; busy: boolean
  children: (draft: HubOptions, set: (patch: Partial<HubOptions>) => void) => ReactNode
  extra?: ReactNode; status?: string
}) {
  const [draft, setDraft] = useState(options)
  const [shortcutError, setShortcutError] = useState('')
  const patch = (delta: Partial<HubOptions>) => setDraft(value => ({ ...value, ...delta }))
  const modified = JSON.stringify(draft) !== JSON.stringify(options)
  const activeShortcut = tool === 'repeater' ? options.repeat.shortcut : options.shortcut
  const hasShortcut = !(['taskbar', 'network', 'system'] as HubTool[]).includes(tool)

  async function save(candidate: HubOptions) {
    const shortcuts = [tool === 'repeater' ? candidate.repeat.shortcut : candidate.shortcut]
    if (tool === 'macro' || tool === 'repeater') shortcuts.push(candidate.repeat.emergency)
    if (hasShortcut && shortcuts.some(shortcut => shortcut.key < 112 && (shortcut.modifiers & 11) === 0)) {
      setShortcutError('Associez une lettre ou un chiffre à Ctrl, Alt ou Win.')
      return
    }
    setShortcutError('')
    await run({ tool, action: 'configure', options: { ...candidate, enabled: true } })
  }

  return <div className="hub-tool-layout">
    <div className="hub-module-controls">
      {status && <div className="status status-neutral"><span className="status-dot" />{status}</div>}
      {hasShortcut && <div className="hub-shortcut-display"><Command size={16} aria-hidden="true" /><kbd>{shortcutName(activeShortcut)}</kbd></div>}
      {extra}
    </div>
    <form className="hub-panel hub-form" onSubmit={event => { event.preventDefault(); void save(draft) }}>
      <fieldset disabled={busy}>
        <div className="hub-panel-heading">
          <h2>Configuration</h2>

        </div>
        {children(draft, patch)}
        {shortcutError && <p role="alert" className="notice">{shortcutError}</p>}
        <button className="button primary" type="submit" disabled={!modified}>Appliquer</button>
      </fieldset>
    </form>
  </div>
}

export function Accent({ options, update, size = true }: {
  options: HubOptions; update: (patch: Partial<HubOptions>) => void; size?: boolean
}) {
  return <div className="hub-form-grid">
    <Field label="Couleur"><input type="color" value={options.color} onChange={event => update({ color: event.currentTarget.value.toUpperCase() })} /></Field>
    <NumberField label="Opacité (%)" value={options.opacity} min={5} max={100} onChange={opacity => update({ opacity })} />
    {size && <NumberField label="Épaisseur (px)" value={options.size} min={1} max={24} onChange={size => update({ size })} />}
  </div>
}

export function TabButtons<T extends string>({ choices, active, select, label }: {
  choices: readonly (readonly [T, string])[]; active: T; select: (value: T) => void; label: string
}) {
  return <div className="hub-tabs" role="group" aria-label={label}>
    {choices.map(([id, name]) => <button className="button secondary" type="button" aria-pressed={active === id}
      key={id} onClick={() => select(id)}>{name}</button>)}
  </div>
}
