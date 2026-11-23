import { useId, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { X } from 'lucide-react'

const swatches = ['#191D34', '#F4F5FC', '#7669DD', '#8FE1BF', '#58B7D9', '#F3B568', '#FF759B', '#C65FD2']

export function ColorField({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  const id = useId()
  const [draft, setDraft] = useState(value)
  const valid = /^#[0-9a-f]{6}$/i.test(draft)
  const color = valid ? draft : value
  const channels = [1, 3, 5].map(index => parseInt(color.slice(index, index + 2), 16))
  function changeChannel(index: number, number: number) {
    const next = channels.map((channel, position) => position === index ? number : channel)
    setDraft('#' + next.map(channel => channel.toString(16).padStart(2, '0')).join('').toUpperCase())
  }

  return <div className="hub-field">
    <label htmlFor={id}>{label}</label>
    <Dialog.Root onOpenChange={open => { if (open) setDraft(value) }}>
      <Dialog.Trigger asChild><button type="button" id={id} className="choice-trigger color-trigger">
        <span className="color-chip" style={{ background: value }} aria-hidden="true" /><span className="mono">{value.toUpperCase()}</span>
      </button></Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="action-search-shade" />
        <Dialog.Content className="color-dialog">
          <div className="color-dialog-title">
            <Dialog.Title>{label}</Dialog.Title>
            <Dialog.Close asChild><button type="button" className="icon-button" aria-label="Fermer"><X size={18} /></button></Dialog.Close>
          </div>
          <Dialog.Description className="sr-only">Choisissez une couleur ou saisissez son code hexadécimal.</Dialog.Description>
          <div className="color-sample" style={{ background: color }} aria-hidden="true" />
          <div className="color-swatches" role="group" aria-label="Couleurs proposées">
            {swatches.map(swatch => <button key={swatch} type="button" style={{ background: swatch }}
              aria-label={swatch} aria-pressed={draft.toUpperCase() === swatch} onClick={() => setDraft(swatch)} />)}
          </div>
          <form onSubmit={event => event.preventDefault()} className="color-fields">
            <label>Hexadécimal <input className="mono" value={draft} maxLength={7} aria-invalid={!valid}
              onChange={event => setDraft(event.target.value)} /></label>
            {['Rouge', 'Vert', 'Bleu'].map((channel, index) => <label key={channel}>{channel}
              <input type="range" min={0} max={255} value={channels[index]} onChange={event => changeChannel(index, event.target.valueAsNumber)} />
            </label>)}
            <div className="utility-launches">
              <Dialog.Close asChild><button type="button" className="button secondary">Annuler</button></Dialog.Close>
              <Dialog.Close asChild><button type="button" className="button primary" disabled={!valid}
                onClick={() => onChange(draft.toUpperCase())}>Choisir la couleur</button></Dialog.Close>
            </div>
          </form>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  </div>
}
