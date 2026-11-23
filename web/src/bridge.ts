import type { UtilityOptions, UtilityRequest } from './utility'
import type { HubPreferences, HubRequest, HubStatus } from './hub'

// This web contract is distinct from the resident agent's V1 named-pipe format.
export type Page = 'dashboard' | 'battery' | 'general' | 'updates' | 'autoClicker' | 'taskbar' | 'switcher' | 'macro' | 'screen' | 'network' | 'system' | 'pin' | 'utilities'
export type Appearance = 'system' | 'light' | 'dark'

export interface BatteryOptions {
  enabled: boolean
  refreshMinutes: number
  alerts: boolean
  lowThreshold: number
}

export interface ClickShortcut { key: number; modifiers: number }

export interface AutoClickerOptions {
  enabled: boolean
  kind: 'left' | 'right' | 'middle' | 'double'
  cps: number
  shortcut: ClickShortcut
  emergency: ClickShortcut
  mode: 'toggle' | 'hold'
  position: 'cursor' | 'fixed'
  x: number
  y: number
  limit: 'unlimited' | 'count' | 'duration'
  count: number
  durationSeconds: number
}

export interface AutoClickerStatus {
  state: 'idle' | 'active' | 'paused'
  error: string | null
}

export interface Preferences {
  appearance: Appearance
  startWithWindows: boolean
  automaticUpdateChecks: boolean
  batteryTrayIcons?: boolean
  battery: BatteryOptions
  autoClicker?: AutoClickerOptions
  hub?: HubPreferences | null
  utilities?: UtilityOptions | null
}

export interface BatteryDevice {
  id: string
  name: string
  state: 'available' | 'unknown' | 'disconnected' | 'unsupported'
  percentage: number | null
  source?: 'bluetoothLe' | 'hyperXUsb'
}

export interface AgentStatus {
  preferences: Preferences
  battery: { devices: BatteryDevice[]; updatedAt: string | null; error: string | null }
  updates: {
    state: 'unavailable' | 'notChecked' | 'current' | 'available' | 'error' | 'installing'
    version: string | null
    info: string | null
  }
  version: string
  error: string | null
  requestedPage: Page
  navigationRevision: number
  autoClicker?: AutoClickerStatus
  hub?: HubStatus
  utilities?: { undo: string | null; error: string | null; recent: string[] }
  requestedTarget?: string | null
}

export type Command =
  | { action: 'ready' | 'refreshBattery' | 'checkUpdates' | 'pauseAutoClicker' | 'rearmAutoClicker' }
  | { action: 'status'; view?: Page | 'systemProcesses' }
  | { action: 'saveAutoClicker'; clickerOptions: AutoClickerOptions }
  | { action: 'save'; preferences: Preferences }
  | { action: 'installUpdate'; version: string }
  | { action: 'hub'; hub: HubRequest }
  | { action: 'utility'; utility: UtilityRequest }

export interface Response {
  status: AgentStatus | null
  error: string | null
  progress?: number | null
}

interface WebView {
  postMessage(message: Command & { id: number }): void
  addEventListener(type: 'message', listener: (event: MessageEvent<unknown>) => void): void
  removeEventListener(type: 'message', listener: (event: MessageEvent<unknown>) => void): void
}

declare global {
  interface Window { chrome?: { webview?: WebView } }
}

let nextId = 0
let queue: Promise<unknown> = Promise.resolve()

function isReply(value: unknown): value is { id: number; response: Response } {
  return typeof value === 'object' && value !== null && 'id' in value && typeof value.id === 'number' &&
    'response' in value && typeof value.response === 'object' && value.response !== null &&
    'status' in value.response && 'error' in value.response
}

function exchange(command: Command, reportProgress?: (value: number) => void): Promise<Response> {
  const webview = window.chrome?.webview
  if (!webview)
    return Promise.resolve({ status: null, error: 'Ouvrez les réglages depuis l’application WinLayer.' })

  const id = ++nextId
  const timeoutMs = command.action === 'installUpdate' ? 340_000 : command.action === 'checkUpdates' ? 125_000 : command.action === 'hub' && command.hub.action === 'exportMacro' ? 130_000 : 10_000

  return new Promise((resolve) => {
    const finish = (response: Response) => {
      window.clearTimeout(timer)
      webview.removeEventListener('message', receive)
      resolve(response)
    }
    const receive = ({ data }: MessageEvent<unknown>) => {
      if (!isReply(data) || data.id !== id) return
      if (typeof data.response.progress === 'number') {
        reportProgress?.(data.response.progress)
      } else {
        finish(data.response)
      }
    }
    const timer = window.setTimeout(() => finish({
      status: null, error: 'Le délai de réponse des réglages est dépassé. Réessayez.',
    }), timeoutMs)

    try {
      webview.addEventListener('message', receive)
      webview.postMessage({ ...command, id })
    } catch {
      finish({ status: null, error: 'La connexion aux réglages a été interrompue.' })
    }
  })
}

export function send(command: Command, reportProgress?: (value: number) => void): Promise<Response> {
  const response = queue.then(() => exchange(command, reportProgress))
  queue = response.catch(() => undefined)
  return response
}
