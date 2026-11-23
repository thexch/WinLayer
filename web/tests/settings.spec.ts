import { mkdir } from 'node:fs/promises'
import { join } from 'node:path'
import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'
import type { AgentStatus, AutoClickerOptions, Command } from '../src/bridge'
import type { HubStatus } from '../src/hub'

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

async function withAgent(page: Page, fixture: AgentStatus, holdFirstStatus = false) {
  await page.addInitScript(({ initial, holdFirstStatus }) => {
    const agent = {
      status: initial, online: true, received: [] as Command[], rejectSave: false,
      releaseStatus: null as (() => void) | null,
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
        if (command.action === 'utility' && command.utility.action === 'save') {
          agent.status.preferences.utilities = command.utility.options
          agent.status.utilities = { undo: 'Outils et favoris', error: null, recent: [] }
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
        if (command.action === 'hub') {
          const request = command.hub
          agent.status.preferences.hub ??= { tools: {}, macros: [], favorites: [], colors: [] }
          const hub = agent.status.preferences.hub
          if (request.action === 'configure') hub.tools[request.tool] = request.options
          if (request.action === 'saveMacro') hub.macros = [...hub.macros.filter(macro => macro.name !== request.macro.name), request.macro]
          if (request.action === 'deleteMacro') hub.macros = hub.macros.filter(macro => macro.name !== request.value)
          if (request.action === 'color') hub.favorites = hub.favorites.includes(request.value)
            ? hub.favorites.filter(color => color !== request.value) : [...hub.favorites, request.value]
          if (request.action === 'endProcess' && agent.status.hub?.metrics) {
            agent.status.hub.metrics.processes = agent.status.hub.metrics.processes.filter(process => process.pid !== request.pid)
          }
        }
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

        const reply = () => queueMicrotask(() => transport.dispatchEvent(new MessageEvent('message', {
          data: {
            id: command.id,
            response: {
              status: command.action === 'ready' || !agent.online ? null : structuredClone(agent.status),
              error: agent.online ? error : 'Agent indisponible',
            },
          },
        })))
        if (command.action === 'status' && holdFirstStatus) {
          holdFirstStatus = false
          agent.releaseStatus = reply
        } else reply()
      },
    })
  }, { initial: fixture, holdFirstStatus })
  await page.goto('/')
  if (!holdFirstStatus) await expect(page.getByText('Agent actif')).toBeVisible()
}

async function capture(page: Page, name: string) {
  const destination = process.env.WINLAYER_SCREENSHOTS ?? '../artifacts/ui'
  await mkdir(destination, { recursive: true })
  await page.evaluate(() => document.fonts.ready)
  await page.screenshot({ path: join(destination, name), animations: 'disabled' })
}

const sidebar = (page: Page) => page.getByRole('navigation', { name: 'Réglages' })

test('keyboard opens WinLayer search without an overlay on the brand', async ({ page }) => {
  await withAgent(page, status)
  await expect(page.getByText('Aller au contenu')).toHaveCount(0)
  await page.keyboard.press('Tab')
  const search = page.getByRole('complementary').getByRole('button', { name: 'Rechercher', exact: true })
  await expect(search).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('textbox', { name: 'Chercher une action ou un réglage' })).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(search).toBeFocused()
})

test('overview, fuzzy search, direct settings navigation and favorites share the same actions', async ({ page }) => {
  await withAgent(page, status)
  await expect(page.getByRole('heading', { name: 'Actions rapides' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'État du PC' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Device Battery' })).toHaveCount(0)
  await capture(page, 'overview-hub.png')
  await page.keyboard.press('Control+k')
  const search = page.getByRole('textbox', { name: 'Chercher une action ou un réglage' })
  await search.fill('batteri')
  await expect(page.getByRole('button', { name: /^Device Battery/ })).toBeVisible()
  await search.fill('seuil batterie faible')
  await page.getByRole('button', { name: 'Favori : Seuil de batterie faible' }).click()
  await expect(page.getByRole('button', { name: 'Favori : Seuil de batterie faible' })).toHaveAttribute('aria-pressed', 'true')
  await capture(page, 'smart-search.png')
  await search.press('Enter')
  await expect(page.getByRole('heading', { name: 'Périphériques', exact: true })).toBeVisible()
  await expect(page.getByRole('switch', { name: 'Alerte de batterie faible' })).toBeFocused()
  await sidebar(page).getByRole('button', { name: 'Vue d’ensemble' }).click()
  await expect(page.getByRole('button', { name: 'Seuil de batterie faible' })).toBeVisible()
  await page.getByRole('button', { name: 'Screen OCR' }).click()
  await expect.poll(() => page.evaluate(() => JSON.stringify((window as unknown as { __testAgent: { received: Command[] } }).__testAgent.received))).toContain('"id":"ocr"')
})

test('utilities expose a configurable radial menu and persist a previewed theme', async ({ page }) => {
  await withAgent(page, status)
  await sidebar(page).getByRole('button', { name: 'Utilitaires' }).click()
  await expect(page.getByRole('heading', { name: 'Utilitaires' })).toBeVisible()
  const radial = page.getByRole('group', { name: 'Menu radial', exact: true })
  await radial.getByRole('button', { name: 'Secteur 2 : Épingler une capture' }).click()
  await radial.getByRole('combobox', { name: 'Action du secteur 2' }).selectOption('desktop')
  await expect(radial.getByRole('button', { name: 'Secteur 2 : Afficher le bureau' })).toHaveAttribute('aria-pressed', 'true')
  await capture(page, 'utilitaires-radial.png')
  await page.getByRole('button', { name: 'Enregistrer les outils et favoris' }).click()
  await expect(page.getByRole('status')).toContainText('Réglages enregistrés.')
  const themes = page.getByRole('group', { name: 'App Themes' })
  await themes.getByRole('button', { name: 'Forêt' }).click()
  await expect(page.locator('html')).toHaveCSS('--accent', '#8FE1BF')
  await themes.getByRole('button', { name: 'Appliquer le thème' }).click()
  await expect(page.getByRole('status')).toContainText('Réglages enregistrés.')
  await sidebar(page).getByRole('button', { name: 'Vue d’ensemble' }).click()
  await expect(page.locator('html')).toHaveCSS('--accent', '#8FE1BF')
  await capture(page, 'overview-foret.png')
})

test('settings report readiness and remain navigable while agent data are pending', async ({ page }) => {
  await withAgent(page, status, true)
  await expect(page.getByText('Connexion…', { exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Vue d’ensemble' })).toBeVisible()
  await expect.poll(() => page.evaluate(() =>
    (window as unknown as { __testAgent: { received: Command[] } }).__testAgent.received
      .slice(0, 2).map(command => command.action))).toEqual(['ready', 'status'])
  await capture(page, '15-ouverture-des-reglages.png')

  await sidebar(page).getByRole('button', { name: 'Périphériques' }).click()
  await expect(page.getByRole('heading', { name: 'Périphériques' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Actualiser' })).toBeDisabled()
  await page.evaluate(() =>
    (window as unknown as { __testAgent: { releaseStatus: (() => void) | null } }).__testAgent.releaseStatus?.())
  await expect(page.getByText('Agent actif')).toBeVisible()
  await expect(page.getByRole('meter', { name: 'Charge de Casque Bluetooth' })).toBeVisible()
})

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

test('device battery is available and can be refreshed and configured', async ({ page }) => {
  await withAgent(page, status)
  await sidebar(page).getByRole('button', { name: 'Périphériques' }).click()
  await expect(page.getByRole('switch', { name: 'Surveiller les batteries' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Actualiser' })).toBeEnabled()

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
  await page.getByRole('main').getByRole('button', { name: 'Rechercher', exact: true }).click()
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
  await expect(page.getByRole('meter', { name: 'Charge de Casque Bluetooth' })).toBeVisible()
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

  await expect(page.getByRole('switch', { name: 'Activer Auto Clicker' })).toHaveCount(0)
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

const hubStatus: HubStatus = {
  errors: {}, pinned: [], selectedColor: '#9458EE', measurement: '3 × 4 px · 5 px',
  macroState: null, recording: [], repeater: { state: 'idle', error: null },
  metrics: {
    adapters: [{ id: 'ethernet', name: 'Ethernet', kind: 'Ethernet' }],
    activeAdapter: 'ethernet', received: 2000000, sent: 700000,
    history: [
      { at: '2026-11-22T16:42:00Z', cpu: 20, ram: 40, down: 3_000, up: 9_000, gpu: null },
      { at: '2026-11-22T16:42:03Z', cpu: 25, ram: 42, down: 12_000, up: 8_000, gpu: 15 },
    ],
    totalRam: 16_000_000_000, freeRam: 8_000_000_000, processor: 'Processeur de test', threads: 12, cores: 6,
    cpuMHz: 3000, logicalCpu: [20, 30], disks: [{ name: 'C:', size: 512_000_000_000, free: 200_000_000_000 }],
    gpuName: null, gpuMemory: null, gpuUsed: null, diskRead: null, diskWrite: null,
    processes: [{ pid: 220, name: 'Processus de test', started: 1_800_000_000, ram: 10_000_000, cpu: 1.2, path: null }],
  },
}
const hubFixture: AgentStatus = {
  ...status, preferences: { ...status.preferences, autoClicker: clickerOptions, hub: {
    tools: {}, macros: [], colors: ['#9458EE'], favorites: [],
  } }, autoClicker: { state: 'idle', error: null }, hub: hubStatus,
}

test('hub exposes reversible desktop tools and distinct screen sub-tools', async ({ page }) => {
  await withAgent(page, hubFixture)
  const menu = sidebar(page)
  await menu.getByRole('button', { name: 'Taskbar Studio' }).click()
  await expect(page.getByLabel('Aperçu de la barre des tâches')).toBeVisible()
  await expect(page.locator('main')).toHaveCSS('scrollbar-width', 'thin')
  await page.getByLabel('Effet sur la barre').selectOption('frame')
  await expect(page.getByLabel('Aperçu de la barre des tâches')).toHaveAttribute('data-effect', 'frame')
  await page.getByLabel('Nom du style').fill('Violet')
  await page.getByRole('button', { name: 'Mémoriser le style' }).click()
  await page.getByRole('button', { name: 'Appliquer' }).click()
  await expect(page.getByRole('button', { name: 'Violet', exact: true })).toBeVisible()
  await page.locator('main').evaluate(main => { main.scrollTop = 0 })
  await capture(page, '10-taskbar-studio.png')
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
  await menu.getByRole('button', { name: 'Alt-Tab Customizer' }).click()
  await page.getByLabel('Disposition').selectOption('cards')
  await page.getByRole('button', { name: 'Appliquer' }).click()
  await page.getByRole('button', { name: 'Ouvrir le sélecteur' }).click()
  await expect(page.getByLabel('Disposition')).toHaveValue('cards')
  await menu.getByRole('button', { name: 'Always on Top' }).click()
  await expect(page.getByRole('heading', { name: 'Fenêtres épinglées' })).toBeVisible()
  await menu.getByRole('button', { name: 'Screen Tools' }).click()
  await page.getByRole('button', { name: 'Favori #9458EE' }).click()
  await expect(page.getByRole('button', { name: 'Favori #9458EE' })).toHaveAttribute('aria-pressed', 'true')
  await expect(page.getByRole('button', { name: 'Prélever' })).toBeEnabled()
  await page.getByRole('button', { name: 'Screen Ruler' }).click()
  await expect(page.getByText('3 × 4 px · 5 px')).toBeVisible()
  await page.getByLabel('Axe de mesure').selectOption('horizontal')
  await page.locator('main').evaluate(main => { main.scrollTop = 0 })
  await capture(page, '11-screen-ruler.png')
})

test('automation hub preserves auto clicker and saves a bounded macro', async ({ page }) => {
  await withAgent(page, hubFixture)
  await sidebar(page).getByRole('button', { name: 'Auto Clicker' }).click()
  await page.getByRole('button', { name: 'Key Repeater' }).click()
  await expect(page.getByRole('heading', { name: 'Key Repeater' })).toBeVisible()
  await page.getByLabel('Fréquence (répétitions/s)').fill('200')
  await expect(page.getByText('5 ms par répétition')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Mettre en pause' })).toBeVisible()
  await sidebar(page).getByRole('button', { name: 'Macro Recorder' }).click()
  await page.getByLabel('Nom de la macro').fill('Démonstration')
  await page.getByRole('button', { name: 'Ajouter un délai' }).click()
  await page.getByLabel('Délai (ms)').fill('630')
  await page.getByRole('button', { name: 'Enregistrer la macro', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Démonstration', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '1. Attendre · 630 ms' })).toBeVisible()
  await capture(page, '12-macro-recorder.png')
})

test('monitoring displays real-shaped history and requires process termination confirmation', async ({ page }) => {
  await withAgent(page, hubFixture)
  await sidebar(page).getByRole('button', { name: 'Network Monitor' }).click()
  await expect(page.getByRole('img', { name: 'Réception et Envoi' })).toBeVisible()
  await page.getByLabel('Unité').selectOption('megabits')
  await page.getByRole('button', { name: 'Appliquer' }).click()
  await expect(page.getByText('0,1 Mb/s', { exact: true }).first()).toBeVisible()
  await page.locator('main').evaluate(main => { main.scrollTop = 0 })
  await capture(page, '13-network-monitor.png')
  await sidebar(page).getByRole('button', { name: 'System Tray Monitor' }).click()
  await expect(page.getByText('Processeur de test')).toBeVisible()
  await page.locator('main').evaluate(main => { main.scrollTop = 0 })
  await capture(page, '14-system-monitor.png')
  await page.getByRole('button', { name: 'Processus', exact: true }).click()
  await page.getByRole('button', { name: 'Terminer Processus de test, PID 220' }).click()
  await expect(page.getByRole('group', { name: 'Confirmer la fermeture' })).toBeVisible()
  await page.getByRole('button', { name: 'Annuler', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Terminer Processus de test, PID 220' })).toBeVisible()
  await page.getByRole('button', { name: 'Terminer Processus de test, PID 220' }).click()
  await page.getByRole('button', { name: 'Terminer maintenant' }).click()
  await expect(page.getByRole('button', { name: 'Terminer Processus de test, PID 220' })).toHaveCount(0)
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([])
})
