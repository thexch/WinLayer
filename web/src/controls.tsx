import { useId } from 'react'
import { Check } from 'lucide-react'

export function Switch({ label, description, checked, disabled, onChange }: {
  label: string
  description?: string
  checked: boolean
  disabled?: boolean
  onChange: (checked: boolean) => void
}) {
  return (
    <button type="button" role="switch" aria-checked={checked} aria-label={label} disabled={disabled}
      className="switch-row" onClick={() => onChange(!checked)}>
      <span className="switch-copy">
        <strong>{label}</strong>
        {description && <span className="fine">{description}</span>}
      </span>
      <span className="switch-control" aria-hidden="true"><span>{checked && <Check size={12} strokeWidth={3} />}</span></span>
    </button>
  )
}

export function Range({ label, value, min, max, unit, onChange }: {
  label: string
  value: number
  min: number
  max: number
  unit: string
  onChange: (value: number) => void
}) {
  const id = useId()
  return (
    <div className="range-field">
      <div className="flex items-baseline justify-between gap-3">
        <label htmlFor={id}>{label}</label>
        <output className="mono" aria-hidden="true">{value} {unit}</output>
      </div>
      <input id={id} name={id} type="range" min={min} max={max} value={value}
        aria-valuetext={`${value} ${unit}`} onChange={(event) => onChange(event.target.valueAsNumber)} />
      <div className="range-ends mono" aria-hidden="true"><span>{min} {unit}</span><span>{max} {unit}</span></div>
    </div>
  )
}
