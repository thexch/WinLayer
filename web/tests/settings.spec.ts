import { mkdir } from 'node:fs/promises'
import { join } from 'node:path'
import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'
import type { AgentStatus, AutoClickerOptions, Command } from '../src/bridge'

const status: AgentStatus = {
  preferences: {
    appearance: 'system', startWithWindows: false, automaticUpdateChecks: true,
    battery: { enabled: true, refreshMinutes: 5, alerts: true, lowThreshold: 20 },
  },
  battery: {
    devices: [
      { id: 'headset', name: 'Casque Bluetooth', state: 'available', percentage: 72 },
      { id: 'mouse', name: 'Souris sans fil', state: 'available', percentage: 14 },
      { id: 'keyboard', name: 'Clavier de bureau', state: 'disconnected', percentage: null },
      { id: 'remote', name: 'Télécommande Bluetooth', state: 'unknown', percentage: null },
      { id: 'trackpad', name: 'Pavé tactile', state: 'unsupported', percentage: null },
    ],
    updatedAt: '2026-11-22T16:42:00Z', error: null,
  },
  updates: { state: 'notChecked', version: null, info: null },
  requestedPage: 'dashboard', navigationRevision: 1, version: '2.0.0', error: null,
}

const clickerOptions: AutoClickerOptions = {
  enabled: false, kind: 'left', cps: 10, shortcut: { key: 117, modifiers: 0 },
  emergency: { key: 121, modifiers: 7 }, mode: 'toggle', position: 'cursor', x: 0, y: 0,
  limit: 'unlimited', count: 100, durationSeconds: 10,
}

async function withAgent(page: Page, fixture: AgentStatus) {
  await page.addInitScript((initial) => {
    const agent = {
      status: initial, online: true, received: [] as Command[], rejectSave: false,
    }
    const win = window as typeof window & { __testAgent: typeof agent }
    win.__testAgent = agent
    const transport = new EventTarget()
    window.chrome ??= {}
    window.chrome.webview = Object.assign(transport, {
      postMessage(command: Command & { id: number }) {
        agent.received.push(command)
        let error: string | null = null
        if (command.action === 'save') {
          if (agent.rejectSave) error = 'L’enregistrement a échoué.'
          else agent.status.preferences = command.preferences
        }
        if (command.action === 'saveAutoClicker') {
          if (command.clickerOptions.shortcut.key === 119) error = 'Le raccourci F8 est indisponible.'
          else {
            agent.status.preferences.autoClicker = command.clickerOptions
            if (!command.clickerOptions.enabled) agent.status.autoClicker = { state: 'idle', error: null }
          }
        }
        if (command.action === 'pauseAutoClicker')
          agent.status.autoClicker = { state: 'paused', error: null }
        if (command.action === 'rearmAutoClicker')
          agent.status.autoClicker = { state: 'idle', error: null }
        if (command.action === 'refreshBattery')
          agent.status.battery.updatedAt = new Date().toISOString()
        if (command.action === 'checkUpdates')
          agent.status.updates = { state: 'available', version: '2.0.1', info: null }

        if (command.action === 'installUpdate') {
          queueMicrotask(() => transport.dispatchEvent(new MessageEvent('message', {
            data: { id: command.id, response: { status: null, error: null, progress: 42 } },
          })))
          setTimeout(() => transport.dispatchEvent(new MessageEvent('message', {
            data: { id: command.id, response: { status: null, error: 'Installation interrompue.' } },
          })), 1500)
          return
        }

        queueMicrotask(() => transport.dispatchEvent(new MessageEvent('message', {
          data: {
            id: command.id,
            response: {
              status: command.action === 'ready' || !agent.online ? null : structuredClone(agent.status),
              error: agent.online ? error : 'Agent indisponible',
            },
          },
        })))
      },
    })
  }, fixture)
  await page.goto('/')
  await expect(page.getByText('Agent actif')).toBeVisible()
}

async function capture(page: Page, name: string) {
  const destination = process.env.WINLAYER_SCREENSHOTS ?? '../artifacts/ui'
  await mkdir(destination, { recursive: true })
  await page.evaluate(() => document.fonts.ready)
  await page.screenshot({ path: join(destination, name), animations: 'disabled' })
}

const sidebar = (page: Page) => page.getByRole('navigation', { name: 'Réglages' })

test('production overview and device states render locally and pass accessibility checks', async ({ page }) => {
  const external: string[] = []
  const errors: string[] = []
  page.on('request', (request) => {
    if (!request.url().startsWith('http://127.0.0.1:4173/')) external.push(request.url())
  })
  page.on('pageerror', (error) => errors.push(error.message))
  page.on('console', (message) => { if (message.type() === 'error') errors.push(message.text()) })
  await withAgent(page, status)
  await expect(page.getByRole('heading', { name: 'Vue d’ensemble' })).toBeVisible()
  await capture(page, '01-apercu-clair.png')
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])

  await page.getByRole('button', { name: 'Voir les périphériques' }).click()
  await expect(page.getByRole('meter', { name: 'Charge de Casque Bluetooth' })).toHaveAttribute('value', '72')
  await expect(page.getByText('Déconnecté', { exact: true })).toBeVisible()
  await expect(page.getByText('Niveau inconnu', { exact: true })).toBeVisible()
  await expect(page.getByText('Service de batterie non exposé', { exact: true })).toBeVisible()
  await expect(page.getByRole('meter')).toHaveCount(2)
  await capture(page, '02-peripheriques.png')
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
  expect(external).toEqual([])
  expect(errors).toEqual([])
})

test('device battery can be disabled, enabled, refreshed and configured', async ({ page }) => {
  await withAgent(page, status)
  await sidebar(page).getByRole('button', { name: 'Périphériques' }).click()
  const enabled = page.getByRole('switch', { name: 'Surveiller les batteries' })
  await enabled.click()
  await expect(enabled).toHaveAttribute('aria-checked', 'false')
  await expect(page.getByRole('button', { name: 'Actualiser' })).toBeDisabled()
  await enabled.click()
  await expect(enabled).toHaveAttribute('aria-checked', 'true')

  await page.getByRole('button', { name: 'Actualiser' }).click()
  await expect(page.getByRole('status')).toContainText('Périphériques actualisés.')
  await page.getByText('Configurer la lecture').click()
  await page.getByRole('slider', { name: 'Intervalle de secours' }).press('ArrowRight')
  await page.getByRole('slider', { name: 'Seuil de l’alerte' }).press('ArrowRight')
  await page.getByRole('button', { name: 'Enregistrer la configuration' }).click()
  await expect(page.getByRole('status')).toContainText('Préférences enregistrées.')
  await expect(page.getByRole('slider', { name: 'Intervalle de secours' })).toHaveValue('6')
  await expect(page.getByRole('slider', { name: 'Seuil de l’alerte' })).toHaveValue('21')
})

test('preferences preview dark mode and persist, including both background switches', async ({ page }) => {
  await withAgent(page, status)
  await sidebar(page).getByRole('button', { name: 'Général' }).click()
  await page.getByText('Sombre', { exact: true }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
  await page.getByRole('switch', { name: 'Démarrer avec Windows' }).click()
  await page.getByRole('switch', { name: 'Rechercher automatiquement les mises à jour' }).click()
  await page.getByRole('button', { name: 'Enregistrer les préférences' }).click()
  await expect(page.getByRole('status')).toContainText('Préférences enregistrées.')
  await capture(page, '03-general-sombre.png')
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
  await sidebar(page).getByRole('button', { name: 'Vue d’ensemble' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
})

test('update installation waits for keyboard-accessible confirmation', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await withAgent(page, status)
  await sidebar(page).getByRole('button', { name: 'Mises à jour' }).click()
  await page.getByRole('button', { name: 'Rechercher' }).click()
  const install = page.getByRole('button', { name: 'Installer la mise à jour' })
  await install.click()
  const modal = page.getByRole('dialog')
  await expect(modal.getByRole('button', { name: 'Annuler' })).toBeFocused()
  await capture(page, '04-confirmation.png')
  await page.keyboard.press('Escape')
  await expect(modal).toBeHidden()
  await expect(install).toBeFocused()
  await install.press('Enter')
  await modal.getByRole('button', { name: 'Installer et redémarrer' }).click()
  await expect(page.getByRole('progressbar', { name: 'Téléchargement de la mise à jour' })).toHaveAttribute('value', '42')
  const calls = await page.evaluate(() => {
    const { __testAgent } = window as unknown as { __testAgent: { received: Command[] } }
    return __testAgent.received.filter(({ action }) => action === 'installUpdate')
  })
  expect(calls).toEqual([{ id: expect.any(Number), action: 'installUpdate', version: '2.0.1' }])
})

test('empty narrow view and offline recovery keep controls understandable', async ({ page }) => {
  await page.setViewportSize({ width: 660, height: 500 })
  const empty = structuredClone(status)
  empty.requestedPage = 'battery'
  empty.battery.devices = []
  await withAgent(page, empty)
  await expect(page.getByText('Aucun périphérique compatible détecté.')).toBeVisible()
  await expect(page.getByText('Compatible avec le Bluetooth LE et le HyperX Cloud III Wireless via son dongle USB.')).toBeVisible()
  await capture(page, '05-etat-vide-fenetre-compacte.png')
  const horizontalOverflow = await page.locator('#main-content').evaluate((element) => element.scrollWidth > element.clientWidth)
  expect(horizontalOverflow).toBe(false)
  await page.evaluate(() => { (window as unknown as { __testAgent: { online: boolean } }).__testAgent.online = false })
  await expect(page.getByText('Nouvelle tentative automatique.', { exact: false })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Actualiser' })).toBeDisabled()
  await page.evaluate(() => { (window as unknown as { __testAgent: { online: boolean } }).__testAgent.online = true })
  await expect(page.getByRole('button', { name: 'Actualiser' })).toBeEnabled()
})

test('failed save is visible and a later tray request changes the active page', async ({ page }) => {
  await withAgent(page, status)
  await sidebar(page).getByRole('button', { name: 'Général' }).click()
  await page.getByRole('switch', { name: 'Démarrer avec Windows' }).click()
  await page.evaluate(() => { (window as unknown as { __testAgent: { rejectSave: boolean } }).__testAgent.rejectSave = true })
  await page.getByRole('button', { name: 'Enregistrer les préférences' }).click()
  await expect(page.getByRole('status')).toContainText('L’enregistrement a échoué.')
  await page.evaluate(() => {
    const { __testAgent } = window as unknown as { __testAgent: { status: AgentStatus } }
    __testAgent.status.navigationRevision += 1
    __testAgent.status.requestedPage = 'battery'
  })
  await expect(page.getByRole('heading', { level: 1, name: 'Périphériques' })).toBeVisible()
})

test('shows the HyperX USB battery even when another source is unavailable', async ({ page }) => {
  const fixture = structuredClone(status)
  fixture.requestedPage = 'battery'
  fixture.battery.error = 'Une source de batterie est indisponible.'
  fixture.battery.devices = [
    { id: 'hyperx:receiver', name: 'HyperX Cloud III Wireless', source: 'hyperXUsb', state: 'available', percentage: 0 },
    { id: 'hyperx:spare', name: 'HyperX déconnecté', source: 'hyperXUsb', state: 'disconnected', percentage: null },
  ]
  await withAgent(page, fixture)
  await expect(page.getByText('Une source de batterie est indisponible.', { exact: true })).toBeVisible()
  await expect(page.getByText('Dongle USB 2,4 GHz · Déconnecté')).toBeVisible()
  await expect(page.getByRole('meter', { name: 'Charge de HyperX Cloud III Wireless' })).toHaveAttribute('value', '0')
  await expect(page.getByRole('meter')).toHaveCount(1)
})


test('can disable taskbar percentages without disabling battery monitoring', async ({ page }) => {
  await withAgent(page, status)
  await sidebar(page).getByRole('button', { name: 'Périphériques' }).click()
  const icons = page.getByRole('switch', { name: 'Bandeau batterie dans la barre des tâches' })
  await expect(icons).toHaveAttribute('aria-checked', 'true')
  await icons.click()
  await expect(icons).toHaveAttribute('aria-checked', 'false')
  await expect(page.getByRole('switch', { name: 'Surveiller les batteries' })).toHaveAttribute('aria-checked', 'true')
})

test('auto clicker saves settings, records keys safely, and reports conflicts and pause', async ({ page }) => {
  await withAgent(page, {
    ...status,
    preferences: { ...status.preferences, autoClicker: clickerOptions },
    autoClicker: { state: 'idle', error: null },
  })
  await sidebar(page).getByRole('button', { name: 'Auto Clicker' }).click()
  await expect(page.getByRole('heading', { name: 'Auto Clicker' })).toBeVisible()
  await capture(page, '09-auto-clicker.png')
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])

  await page.getByLabel('Vitesse (CPS)').fill('200')
  await page.getByLabel('Vitesse (CPS)').press('Enter')
  await expect(page.getByText('5 ms par action')).toBeVisible()
  await page.getByLabel('Type de clic').selectOption('double')
  await expect(page.getByText('Un double-clic compte pour une action.')).toBeVisible()
  await page.getByLabel('Mode', { exact: true }).selectOption('hold')
  await expect(page.getByText('Maintenir pour cliquer')).toBeVisible()
  await page.getByLabel('Position', { exact: true }).selectOption('fixed')
  await page.getByLabel('Coordonnée X').fill('-256')
  await page.getByLabel('Coordonnée X').press('Enter')
  await expect(page.getByLabel('Coordonnée X')).toHaveValue('-256')
  await page.getByLabel('Limite').selectOption('duration')
  await expect(page.getByLabel('Durée maximale (s)')).toBeVisible()
  await page.getByLabel('Limite').selectOption('count')
  await expect(page.getByLabel('Nombre d’actions')).toBeVisible()

  await page.getByRole('switch', { name: 'Activer Auto Clicker' }).click()
  await expect(page.getByRole('switch', { name: 'Activer Auto Clicker' })).toHaveAttribute('aria-checked', 'true')
  await page.getByRole('button', { name: 'Enregistrer les touches · Activation', exact: true }).click()
  const recording = page.getByRole('button', { name: 'Appuyez sur les touches… Activation' })
  await expect(recording).toBeFocused()
  await expect(page.getByText('En pause', { exact: true })).toBeVisible()
  await recording.press('Control+Alt+c')
  await expect(page.getByLabel('Activation', { exact: true })).toHaveValue('67')
  await page.getByRole('button', { name: 'Enregistrer les touches · Activation', exact: true }).click()
  await expect(recording).toBeFocused()
  await recording.press('F8')
  await expect(page.getByText('Le raccourci F8 est indisponible.')).toBeVisible()
  await expect(page.getByLabel('Activation', { exact: true })).toHaveValue('67')
  await page.getByRole('button', { name: 'Réarmer' }).click()
  await expect(page.getByText('Inactif', { exact: true })).toBeVisible()
})
