import { ColorField } from './ColorField'
import { useId, useState, type ReactNode } from 'react'
import { Check, ChevronDown, Command } from 'lucide-react'
import * as Select from '@radix-ui/react-select'
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

export function Choice<Value extends string | number>({ label, value, choices, onChange }: {
  label: string; value: Value; choices: readonly (readonly [Value, string])[]; onChange: (value: Value) => void
}) {
  const id = useId()
  const shown = choices.some(([item]) => item === value) ? choices : [[value, 'Option indisponible'] as const, ...choices]
  return <div className="hub-field">
    <label htmlFor={id}>{label}</label>
    <Select.Root value={`item:${value}`} onValueChange={selected => {
      const item = shown.find(([key]) => `item:${key}` === selected)
      if (item) onChange(item[0])
    }}>
      <Select.Trigger id={id} className="choice-trigger">
        <Select.Value />
        <Select.Icon asChild><ChevronDown aria-hidden="true" size={16} /></Select.Icon>
      </Select.Trigger>
      <Select.Portal>
        <Select.Content className="choice-popup" position="popper" sideOffset={6} collisionPadding={12}>
          <Select.Viewport className="choice-options">
            {shown.map(([key, name]) => <Select.Item key={key} value={`item:${key}`} className="choice-option">
              <Select.ItemText>{name}</Select.ItemText>
              <Select.ItemIndicator><Check size={15} aria-hidden="true" /></Select.ItemIndicator>
            </Select.Item>)}
          </Select.Viewport>
        </Select.Content>
      </Select.Portal>
    </Select.Root>
  </div>
}

export function Flag({ label, value, onChange }: { label: string; value: boolean; onChange: (value: boolean) => void }) {
  return <label className="hub-check"><input type="checkbox" checked={value} onChange={event => onChange(event.currentTarget.checked)} />{label}</label>
}

export function Hotkey({ label, value, onChange, output = false }: {
  label: string; value: ClickShortcut; onChange: (value: ClickShortcut) => void; output?: boolean
}) {
  return <fieldset className="hub-hotkey">
    <legend>{label}</legend>
    <Choice label={`${label} · touche`} value={value.key} choices={(output ? virtualKeys : hotkeyKeys).map(key => [key, keyName(key)] as const)}
      onChange={key => onChange({ ...value, key })} />
    <div className="hub-inline">
      {modifierNames.filter(({ mask }) => !output || mask !== 8).map(({ name, mask }) => <Flag key={name} label={name}
        value={(value.modifiers & mask) !== 0} onChange={checked => onChange({
          ...value, modifiers: checked ? value.modifiers | mask : value.modifiers & ~mask,
        })} />)}
    </div>
  </fieldset>
}

export function HubForm({ tool, options, run, busy, children, extra, status, compact }: {
  tool: HubTool; options: HubOptions; run: RunHub; busy: boolean
  children: (draft: HubOptions, set: (patch: Partial<HubOptions>) => void) => ReactNode
  extra?: ReactNode; status?: string; compact?: boolean
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

  return <div className={compact ? 'hub-tool-layout hub-inline-settings' : 'hub-tool-layout'}>
    {(status || hasShortcut || extra) && <div className="hub-module-controls">
      {status && <div className="status status-neutral"><span className="status-dot" />{status}</div>}
      {hasShortcut && <div className="hub-shortcut-display"><Command size={16} aria-hidden="true" /><kbd>{shortcutName(activeShortcut)}</kbd></div>}
      {extra}
    </div>}
    <form className="hub-panel hub-form" onSubmit={event => { event.preventDefault(); void save(draft) }}>
      <fieldset disabled={busy}>
        {!compact && <div className="hub-panel-heading"><h2>Configuration</h2></div>}
        {children(draft, patch)}
        {shortcutError && <p role="alert" className="notice">{shortcutError}</p>}
        <button className="button primary" type="submit" disabled={!modified}>Appliquer</button>
      </fieldset>
    </form>
  </div>
}

export function Accent({ options, update, size = true, opacityId }: {
  options: HubOptions; update: (patch: Partial<HubOptions>) => void; size?: boolean; opacityId?: string
}) {
  return <div className="hub-form-grid">
    <ColorField label="Couleur" value={options.color} onChange={color => update({ color })} />
    <div id={opacityId}><NumberField label="Opacité (%)" value={options.opacity} min={5} max={100} onChange={opacity => update({ opacity })} /></div>
    {size && <NumberField label="Épaisseur (px)" value={options.size} min={1} max={24} onChange={size => update({ size })} />}
  </div>
}

export function TabButtons<T extends string>({ choices, active, select, label }: {
  choices: readonly (readonly [T, string])[]; active: T; select: (value: T) => void; label: string
}) {
  return <div className="hub-tabs" role="group" aria-label={label} onKeyDown={event => {
    const buttons = Array.from(event.currentTarget.querySelectorAll('button'))
    const index = buttons.findIndex(button => button === event.target)
    if (index < 0) return
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 :
      event.key === 'ArrowRight' ? (index + 1) % buttons.length :
        event.key === 'ArrowLeft' ? (index - 1 + buttons.length) % buttons.length : -1
    if (next < 0) return
    event.preventDefault()
    buttons[next].focus()
  }}>
    {choices.map(([id, name]) => <button className="button secondary" type="button" aria-pressed={active === id}
      key={id} onClick={() => select(id)}>{name}</button>)}
  </div>
}
