import type { ClickShortcut, Page } from './bridge'
import catalog from '../../src/WinLayer.Core/actions.json'

export interface Entry { id: string; label: string; category: string; keywords: string; icon: string; page?: Page; target?: string; tool?: string }
export interface AppTheme {
  name: string; canvas: string; surface: string; ink: string; accent: string; border: string
  radius: number; density: number; motion: boolean; font: 'Manrope' | 'Segoe UI' | 'Geist Mono'
}
export interface UtilityOptions {
  ocr: ClickShortcut; pin: ClickShortcut; shelf: ClickShortcut; palette: ClickShortcut; radial: ClickShortcut
  language: 'fr' | 'en'; autoCopy: boolean; ocrHistory: boolean; persistRecent: boolean; shelfPosition: number; shelfHours: number; shelfWidth: number; shelfHeight: number
  caps: boolean; num: boolean; scroll: boolean; osdSeconds: number; osdPosition: number; osdOpacity: number; osdSize: number; compactOsd: boolean
  hideOsdFullscreen: boolean; excludeFullscreen: boolean; cornerExclusions: string; cornerPixels: number; cornerCooldown: number; cornerModifier: boolean
  corners: { action: string; delay: number }[]; radialItems: string[]; radialAccent: string; radialPresets: { name: string; actions: string[]; accent: string }[]
  launchers: { id: string; label: string; path: string }[]; favorites: string[]; themes: AppTheme[]; theme: string
}
export type UtilityRequest = { action: 'execute'; id: string } | { action: 'save'; options: UtilityOptions } | { action: 'undo' | 'export' | 'import' | 'addFile' | 'addFolder' }

export const entries = catalog as Entry[]
export const defaults: UtilityOptions = {
  ocr: { key: 117, modifiers: 3 }, pin: { key: 118, modifiers: 3 }, shelf: { key: 119, modifiers: 3 },
  palette: { key: 80, modifiers: 6 }, radial: { key: 82, modifiers: 6 }, language: 'fr', autoCopy: true, ocrHistory: false,
  persistRecent: false, shelfPosition: 3, shelfHours: 4, shelfWidth: 400, shelfHeight: 370, caps: false, num: false, scroll: false, osdSeconds: 2, osdPosition: 1,
  hideOsdFullscreen: true, osdOpacity: 94, osdSize: 100, compactOsd: false, excludeFullscreen: true, cornerExclusions: '', cornerPixels: 5,
  cornerCooldown: 2000, cornerModifier: true, corners: Array.from({ length: 4 }, () => ({ action: '', delay: 600 })),
  radialItems: ['ocr', 'screen-pin', 'color', 'shelf', 'converter', 'palette'], radialAccent: '#9A86F1', radialPresets: [], launchers: [], favorites: [], themes: [], theme: '',
}

export const standardThemes: AppTheme[] = [
  { name: 'Quartz', canvas: '#F4F5FC', surface: '#FFFFFF', ink: '#20243B', accent: '#4C40AC', border: '#CFD2E7', radius: 15, density: 100, motion: true, font: 'Manrope' },
  { name: 'Nocturne', canvas: '#14172B', surface: '#20243C', ink: '#F1EFFE', accent: '#B7AAFF', border: '#414664', radius: 15, density: 100, motion: true, font: 'Manrope' },
  { name: 'Forêt', canvas: '#101E1C', surface: '#192F2B', ink: '#E3F7ED', accent: '#8FE1BF', border: '#385D50', radius: 13, density: 100, motion: true, font: 'Segoe UI' },
  { name: 'Contraste', canvas: '#121212', surface: '#202020', ink: '#FFFFFF', accent: '#FFDB6E', border: '#818181', radius: 6, density: 105, motion: false, font: 'Segoe UI' },
]

export function luminance(hex: string) {
  return [1, 3, 5].reduce((light, i, index) => {
    const level = parseInt(hex.slice(i, i + 2), 16) / 255
    return light + [0.2126, 0.7152, 0.0722][index] * (level <= 0.04045 ? level / 12.92 : ((level + 0.055) / 1.055) ** 2.4)
  }, 0)
}

export const fold = (value: string) => value.toLowerCase().normalize('NFD').replace(/\p{M}/gu, '').replace(/[^a-z0-9]/g, ' ')

function distance(a: string, b: string) {
  if (Math.abs(a.length - b.length) > 2) return Infinity
  const row = Array.from({ length: b.length + 1 }, (_, i) => i)
  for (let i = 1; i <= a.length; i++) {
    let previous = row[0]
    row[0] = i
    for (let j = 1; j <= b.length; j++) {
      const old = row[j]
      row[j] = Math.min(old + 1, row[j - 1] + 1, previous + Number(a[i - 1] !== b[j - 1]))
      previous = old
    }
  }
  return row[b.length]
}

export function searchEntries(items: Entry[], query: string) {
  const tokens = [...new Set(fold(query).split(' ').filter(Boolean))]
  if (!tokens.length) return items
  return items.map((entry) => {
    const title = fold(entry.label)
    const terms = (title + ' ' + fold(entry.keywords + ' ' + entry.category)).split(' ').filter(Boolean)
    const score = tokens.reduce((sum, token) => {
      let best = title.includes(token) ? 8 : 0
      for (const term of terms) {
        if (term === token) best = Math.max(best, 12)
        else if (term.startsWith(token)) best = Math.max(best, 7)
        else if (token.length >= 4 && distance(token, term) <= (token.length >= 7 ? 2 : 1)) best = Math.max(best, 4)
      }
      return best > 0 ? sum + best + 4 : sum
    }, 0)
    return { entry, score }
  }).filter(({ score }) => score > 0).sort((a, b) => b.score - a.score).map(({ entry }) => entry)
}
