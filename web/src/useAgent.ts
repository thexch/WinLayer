import { useEffect, useRef, useState } from 'react'
import { send, type AgentStatus, type Command, type Page } from './bridge'

export function useAgent() {
  const [status, setStatus] = useState<AgentStatus>()
  const [page, setPage] = useState<Page>('dashboard')
  const [connection, setConnection] = useState<'opening' | 'connected' | 'offline'>('opening')
  const [feedback, setFeedback] = useState('')
  const [busy, setBusy] = useState<Command['action'] | null>(null)
  const [downloadProgress, setDownloadProgress] = useState<number | null>(null)
  const active = useRef(false)
  const revision = useRef<number | null>(null)
  const refreshStarted = useRef<number | null>(null)

  function accept(next: AgentStatus) {
    if (revision.current !== next.navigationRevision) {
      revision.current = next.navigationRevision
      setPage(next.requestedPage)
    }
    if (refreshStarted.current !== null && next.battery.updatedAt &&
        Date.parse(next.battery.updatedAt) >= refreshStarted.current) {
      refreshStarted.current = null
      setFeedback(next.battery.error ?? 'Périphériques actualisés.')
    }
    setStatus(next)
    setConnection('connected')
  }

  useEffect(() => {
    let disposed = false
    let timer = 0
    let ready = false

    async function poll() {
      if (!active.current) {
        const response = await send({ action: 'status' })
        if (disposed) return
        if (response.status) accept(response.status)
        else setConnection('offline')
        if (!ready) {
          ready = true
          void send({ action: 'ready' })
        }
      }
      if (!disposed)
        timer = window.setTimeout(poll, 2000)
    }

    void poll()
    return () => { disposed = true; window.clearTimeout(timer) }
  }, [])

  async function act(command: Command, pending: string, success = '') {
    if (active.current) return false
    active.current = true
    setBusy(command.action)
    setFeedback(pending)
    if (command.action === 'installUpdate')
      setDownloadProgress(null)
    if (command.action === 'refreshBattery')
      refreshStarted.current = Date.now()

    try {
      const response = await send(command, command.action === 'installUpdate' ? setDownloadProgress : undefined)
      if (!response.status) setConnection('offline')
      else accept(response.status)
      if (response.error) {
        if (command.action === 'refreshBattery') refreshStarted.current = null
        setFeedback(response.error)
        return false
      }
      if (command.action !== 'refreshBattery') setFeedback(success)
      return true
    } finally {
      setBusy(null)
      active.current = false
    }
  }

  function go(next: Page) {
    setPage(next)
    if (!active.current) setFeedback('')
  }

  return { status, page, connection, feedback, busy, downloadProgress, go, act }
}
