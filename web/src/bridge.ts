// This web contract is distinct from the resident agent's V1 named-pipe format.
export type Page = 'dashboard' | 'battery' | 'general' | 'updates'
export type Appearance = 'system' | 'light' | 'dark'

export interface BatteryOptions {
  enabled: boolean
  refreshMinutes: number
  alerts: boolean
  lowThreshold: number
}

export interface Preferences {
  appearance: Appearance
  startWithWindows: boolean
  automaticUpdateChecks: boolean
  battery: BatteryOptions
}

export interface BatteryDevice {
  id: string
  name: string
  state: 'available' | 'unknown' | 'disconnected' | 'unsupported'
  percentage: number | null
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
}

export type Command =
  | { action: 'ready' | 'status' | 'refreshBattery' | 'checkUpdates' }
  | { action: 'save'; preferences: Preferences }
  | { action: 'installUpdate'; version: string }

export interface Response {
  status: AgentStatus | null
  error: string | null
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

function exchange(command: Command): Promise<Response> {
  const webview = window.chrome?.webview
  if (!webview)
    return Promise.resolve({ status: null, error: 'Ouvrez les réglages depuis l’application WinLayer.' })

  const id = ++nextId
  const timeoutMs = command.action === 'installUpdate' ? 340_000 : command.action === 'checkUpdates' ? 125_000 : 10_000

  return new Promise((resolve) => {
    const finish = (response: Response) => {
      window.clearTimeout(timer)
      webview.removeEventListener('message', receive)
      resolve(response)
    }
    const receive = ({ data }: MessageEvent<unknown>) => {
      if (isReply(data) && data.id === id)
        finish(data.response)
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

export function send(command: Command): Promise<Response> {
  const response = queue.then(() => exchange(command))
  queue = response.catch(() => undefined)
  return response
}
