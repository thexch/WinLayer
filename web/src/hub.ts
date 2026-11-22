import type { AutoClickerOptions, AutoClickerStatus, ClickShortcut } from './bridge'

export const hubTools = ['taskbar', 'switcher', 'macro', 'repeater', 'picker', 'ruler', 'network', 'system', 'pin'] as const
export type HubTool = typeof hubTools[number]
export type ColorFormat = 'hex' | 'rgb' | 'hsl'
export interface TaskbarStyle { name: string; color: string; opacity: number; size: number; radius: number; inset: number }
export interface ColorPalette { name: string; colors: string[] }
export interface HubOptions {
  enabled: boolean
  shortcut: ClickShortcut
  seconds: number
  history: number
  adapter: string
  exclusions: string
  indicator: boolean
  color: string
  opacity: number
  size: number
  zoom: number
  radius: number
  inset: number
  threshold: number
  alerts: boolean
  width: number
  layout: 'grid' | 'list' | 'cards'
  rulerMode: 'free' | 'horizontal' | 'vertical'
  colorFormat: ColorFormat
  rateUnit: 'kilobytes' | 'megabytes' | 'megabits'
  trayMetrics: ('cpu' | 'ram' | 'gpu')[]
  autoCopy: boolean
  repeat: AutoClickerOptions
  outputKey: number
  outputModifiers: number
  playCount: number
  speedPercent: number
  loop: boolean
  selectedMacro: string
  pinRules: string
  profile: string
  styles: TaskbarStyle[]
  palettes: ColorPalette[]
}

export const defaultHubOptions = (tool: HubTool): HubOptions => ({
  enabled: false, shortcut: { key: 112 + hubTools.indexOf(tool), modifiers: 6 }, seconds: 2, history: 60,
  adapter: '', exclusions: '', indicator: tool !== 'network', color: '#7669DD', opacity: 75, size: 4, zoom: 12, radius: 0, inset: 0,
  threshold: 90, alerts: false, width: 700, layout: 'grid', rulerMode: 'free', colorFormat: 'hex',
  rateUnit: 'kilobytes', trayMetrics: [], autoCopy: true, outputKey: 32, outputModifiers: 0, playCount: 1,
  speedPercent: 100, loop: false, selectedMacro: '', pinRules: '', profile: '', styles: [], palettes: [],
  repeat: {
    enabled: false, kind: 'left', cps: 10, shortcut: { key: 120, modifiers: 0 },
    emergency: tool === 'macro' ? { key: 122, modifiers: 6 } : { key: 121, modifiers: 3 },
    mode: 'toggle', position: 'cursor', x: 0, y: 0, limit: 'unlimited', count: 100, durationSeconds: 10,
  },
})

export interface MacroStep {
  kind: 'wait' | 'move' | 'left' | 'right' | 'middle' | 'key'
  delay: number
  x: number
  y: number
  key: number
  modifiers: number
}
export interface MacroPreset { name: string; steps: MacroStep[] }
export interface HubPreferences {
  tools: Partial<Record<HubTool, HubOptions>>
  macros: MacroPreset[]
  colors: string[]
  favorites: string[]
}

export interface HubSample { at: string; cpu: number | null; ram: number | null; down: number | null; up: number | null; gpu: number | null }
export interface HubProcess { pid: number; name: string; started: number; ram: number | null; cpu: number | null; path: string | null; icon?: string | null }
export interface HubMetrics {
  adapters: { id: string; name: string; kind: string }[]
  activeAdapter: string | null
  received: number
  sent: number
  history: HubSample[]
  totalRam: number | null
  freeRam: number | null
  processor: string | null
  threads: number
  disks: { name: string; size: number; free: number }[]
  processes: HubProcess[]
  gpuName: string | null
  gpuMemory: number | null
  gpuUsed: number | null
  cores: number | null
  cpuMHz: number | null
  logicalCpu: (number | null)[] | null
  diskRead: number | null
  diskWrite: number | null
}
export interface HubStatus {
  errors: Partial<Record<HubTool, string>>
  metrics: HubMetrics | null
  pinned: { handle: number; title: string; app: string; pid: number }[]
  selectedColor: string | null
  selectedRgb?: string | null
  selectedHsl?: string | null
  measurement: string | null
  macroState: 'recording' | 'paused' | 'playing' | null
  recording: MacroStep[]
  repeater: AutoClickerStatus
}
export type HubRequest = { tool: HubTool } & (
  | { action: 'configure'; options: HubOptions }
  | { action: 'saveMacro' | 'exportMacro'; macro: MacroPreset }
  | { action: 'deleteMacro' | 'play' | 'color' | 'copyColor'; value: string }
  | { action: 'endProcess'; pid: number; started: number }
  | { action: 'open' | 'stop' | 'rearm' | 'pauseRecording' | 'pinAllOff' | 'processes' }
)

export const keyNames: Readonly<Record<number, string>> = {
  8: 'Retour arrière', 9: 'Tab', 13: 'Entrée', 32: 'Espace', 33: 'Page ↑', 34: 'Page ↓', 35: 'Fin',
  36: 'Début', 37: '←', 38: '↑', 39: '→', 40: '↓', 46: 'Suppr',
}
const fn = Array.from({ length: 24 }, (_, index) => index + 112)
const alphanum = Array.from('ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789', key => key.charCodeAt(0))
export const virtualKeys = [...Object.keys(keyNames).map(Number), ...fn, ...alphanum]
export const hotkeyKeys = [...fn.filter(key => key !== 123), ...alphanum]
export const keyName = (key: number) => keyNames[key] ?? (key >= 112 ? `F${key - 111}` : String.fromCharCode(key))
export const modifierNames = [{ name: 'Ctrl', mask: 2 }, { name: 'Alt', mask: 1 }, { name: 'Maj', mask: 4 }, { name: 'Win', mask: 8 }]
export const shortcutName = ({ key, modifiers }: ClickShortcut) =>
  [...modifierNames.filter(({ mask }) => modifiers & mask).map(({ name }) => name), keyName(key)].join(' + ')
