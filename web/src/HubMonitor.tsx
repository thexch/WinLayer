import { useMemo, useState } from 'react'
import { ArrowDownToLine, ArrowUpFromLine, Cpu, HardDrive, MemoryStick, Radio, RefreshCw } from 'lucide-react'
import { Choice, Field, Flag, HubForm, NumberField, TabButtons } from './HubControls'
import { defaultHubOptions, type HubMetrics, type HubOptions, type HubProcess, type HubSample } from './hub'
import type { HubPageProps } from './HubTools'

const fmt = new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 1 })
const clock = new Intl.DateTimeFormat('fr-FR', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
type Unit = HubOptions['rateUnit']
type Series = { key: 'cpu' | 'ram' | 'gpu' | 'down' | 'up'; label: string; ink: 'accent' | 'positive' }
const networkSeries: Series[] = [{ key: 'down', label: 'Réception', ink: 'accent' }, { key: 'up', label: 'Envoi', ink: 'positive' }]

function rate(value: number | null | undefined, unit: Unit) {
  if (value == null) return '—'
  if (unit === 'megabytes') return `${fmt.format(value / 1_000_000)} MB/s`
  if (unit === 'megabits') return `${fmt.format(value / 125_000)} Mb/s`
  return `${fmt.format(value / 1000)} KB/s`
}
const percent = (value: number | null | undefined) => value == null ? '—' : `${fmt.format(value)} %`
const bytes = (value: number | null | undefined) => value == null ? '—' :
  value >= 1_000_000_000 ? `${fmt.format(value / 1_000_000_000)} Go` : `${fmt.format(value / 1_000_000)} Mo`

function StatsChart({ history, series, unit }: { history: HubSample[]; series: Series[]; unit?: Unit }) {
  const [hidden, setHidden] = useState<string[]>([])
  const visible = series.filter(({ key }) => !hidden.includes(key))
  const absolute = unit ? Math.max(1, ...history.flatMap(sample => visible.map(({ key }) => sample[key] ?? 0))) : 100
  const available = history.some(sample => visible.some(({ key }) => sample[key] != null))
  const y = (value: number) => 110 - Math.min(1, value / absolute) * 103
  const x = (index: number) => 1 + 638 * index / Math.max(1, history.length - 1)
  function line(key: Series['key']) {
    let start = true
    return history.map((sample, index) => {
      const value = sample[key]
      if (value == null) { start = true; return '' }
      const segment = `${start ? 'M' : 'L'}${x(index)},${y(value)}`
      start = false
      return segment
    }).join(' ')
  }
  return <section className="hub-chart" aria-label="Graphique historique">
    <div className="hub-chart-head">
      {series.map(({ key, label, ink }) => <button key={key} type="button" className="button secondary" aria-pressed={!hidden.includes(key)}
        onClick={() => setHidden(items => items.includes(key) ? items.filter(item => item !== key) : [...items, key])}>
        <span className={`hub-sample-dot ${ink}`} />{label}
      </button>)}
      <span className="mono fine">{unit ? rate(absolute, unit) : '100 %'}</span>
    </div>
    {available ? <svg role="img" aria-label={visible.map(({ label }) => label).join(' et ')} viewBox="0 0 640 116" preserveAspectRatio="none">
      {[7, 33, 59, 84, 110].map(row => <path key={row} d={`M0 ${row}H640`} className="hub-grid-line" />)}
      {visible.map(({ key, ink }) => <g key={key} className={`hub-plot ${ink}`}>
        <path d={line(key)} vectorEffect="non-scaling-stroke" />
        {history.length === 1 && history[0]?.[key] != null && <circle cx={x(0)} cy={y(history[0][key])} r="3" />}
      </g>)}
    </svg> : <p className="hub-chart-empty fine">En attente de mesures disponibles.</p>}
    <div className="hub-chart-times mono fine"><span>{history[0] ? clock.format(Date.parse(history[0].at)) : '—'}</span>
      <span>{history.at(-1) ? clock.format(Date.parse(history.at(-1)!.at)) : '—'}</span></div>
  </section>
}

function summarize(history: HubSample[], key: 'down' | 'up') {
  const values = history.map(sample => sample[key]).filter((sample): sample is number => sample !== null)
  return values.length ? { mean: values.reduce((sum, number) => sum + number, 0) / values.length, peak: Math.max(...values) } :
    { mean: null, peak: null }
}

function NetworkReadings({ metrics, options }: { metrics?: HubMetrics | null; options: HubOptions }) {
  const history = metrics?.history.slice(-options.history) ?? []
  const present = history.at(-1)
  const adapter = metrics?.adapters.find(adapter => adapter.id === metrics.activeAdapter)
  return <section className="hub-panel hub-monitor-panel">
    <div className="hub-panel-heading"><h2>Trafic réseau</h2><span className="fine">{adapter?.name ?? 'Interface indisponible'}</span></div>
    <div className="hub-net-values">
      {networkSeries.map(({ key, label }, index) => {
        const Icon = index === 0 ? ArrowDownToLine : ArrowUpFromLine
        const signal = key === 'down' ? 'down' : 'up'
        const summary = summarize(history, signal)
        return <div key={key}>
          <span className="hub-stat-label"><Icon size={16} aria-hidden="true" /> {label}</span>
          <strong className="hub-value">{rate(present?.[signal], options.rateUnit)}</strong>
          <span className="fine">Moyenne {rate(summary.mean, options.rateUnit)} · Pic {rate(summary.peak, options.rateUnit)}</span>
        </div>
      })}
    </div>
    <StatsChart history={history} series={networkSeries} unit={options.rateUnit} />
    <dl className="hub-metric-pairs">
      <div><dt>Adaptateur</dt><dd>{adapter ? `${adapter.name} · ${adapter.kind}` : '—'}</dd></div>
      <div><dt>Reçus depuis la remise à zéro du compteur</dt><dd>{adapter ? bytes(metrics?.received) : '—'}</dd></div>
      <div><dt>Envoyés depuis la remise à zéro du compteur</dt><dd>{adapter ? bytes(metrics?.sent) : '—'}</dd></div>
    </dl>
  </section>
}

export function NetworkPage({ status, data, run, busy }: HubPageProps) {
  const options = data?.tools.network ?? defaultHubOptions('network')
  const metrics = status.metrics
  return <>
    <NetworkReadings metrics={options.enabled ? metrics : null} options={options} />
    <HubForm tool="network" options={options} key={JSON.stringify(options)} run={run} busy={busy}>
      {(draft, patch) => <>
        <Choice label="Interface réseau" value={draft.adapter}
          choices={[[ '', 'Automatique' ], ...(draft.adapter && !metrics?.adapters.some(adapter => adapter.id === draft.adapter)
            ? [[draft.adapter, 'Adaptateur indisponible'] as const] : []),
          ...(metrics?.adapters ?? []).map(adapter => [adapter.id, adapter.name] as const)]}
          onChange={adapter => patch({ adapter })} />
        <Choice label="Unité" value={draft.rateUnit} choices={[[ 'kilobytes', 'KB/s' ], [ 'megabytes', 'MB/s' ], [ 'megabits', 'Mb/s' ]]}
          onChange={rateUnit => patch({ rateUnit })} />
        <div className="hub-form-grid">
          <NumberField label="Actualisation (s)" min={1} max={60} value={draft.seconds} onChange={seconds => patch({ seconds })} />
          <NumberField label="Historique (échantillons)" min={15} max={180} value={draft.history} onChange={history => patch({ history })} />
        </div>
        <Flag label="Icône de débit dans la zone de notification" value={draft.indicator} onChange={indicator => patch({ indicator })} />
        {draft.indicator && <Field label="Couleur de l’icône"><input type="color" value={draft.color}
          onChange={event => patch({ color: event.currentTarget.value })} /></Field>}
      </>}
    </HubForm>
  </>
}

type SystemView = 'summary' | 'cpu' | 'gpu' | 'ram' | 'disk' | 'network' | 'processes'
const systemViews: readonly (readonly [SystemView, string])[] = [
  ['summary', 'Vue système'], ['cpu', 'CPU'], ['gpu', 'GPU'], ['ram', 'Mémoire'], ['disk', 'Disques'], ['network', 'Réseau'], ['processes', 'Processus'],
]

function Processes({ items, busy, run, enabled }: { items: HubProcess[]; busy: boolean; run: HubPageProps['run']; enabled: boolean }) {
  const [search, setSearch] = useState('')
  const [sort, setSort] = useState<'ram' | 'cpu' | 'name'>('ram')
  const [target, setTarget] = useState<HubProcess>()
  const [details, setDetails] = useState<HubProcess>()
  const shown = useMemo(() => items.filter(process => `${process.name} ${process.pid}`.toLowerCase().includes(search.trim().toLowerCase()))
    .sort((a, b) => sort === 'name' ? a.name.localeCompare(b.name) : (b[sort] ?? -1) - (a[sort] ?? -1)), [items, search, sort])

  return <section className="hub-panel hub-process-panel">
    <div className="hub-panel-heading"><h2>Processus</h2>
      <button type="button" className="button secondary" disabled={busy || !enabled} onClick={() => { void run({ tool: 'system', action: 'processes' }) }}>
        <RefreshCw size={16} aria-hidden="true" /> Actualiser les processus
      </button>
    </div>
    <div className="hub-inline">
      <Field label="Rechercher un processus"><input type="search" value={search} onChange={event => setSearch(event.currentTarget.value)} /></Field>
      <Choice label="Tri" value={sort} choices={[[ 'ram', 'Mémoire' ], [ 'cpu', 'Processeur' ], [ 'name', 'Nom' ]]} onChange={setSort} />
    </div>
    <p className="fine">Deux actualisations sont nécessaires pour calculer le CPU. La mémoire indique l’ensemble de travail.</p>
    {target && <div className="notice" role="group" aria-label="Confirmer la fermeture">
      <span>Terminer {target.name} (PID {target.pid}) ? Le travail en cours peut être perdu.</span>
      <button type="button" className="button secondary" onClick={() => setTarget(undefined)}>Annuler</button>
      <button type="button" className="button primary" disabled={busy} onClick={() => {
        void run({ tool: 'system', action: 'endProcess', pid: target.pid, started: target.started }).then(ok => { if (ok) setTarget(undefined) })
      }}>Terminer maintenant</button>
    </div>}
    {details && <dl className="hub-metric-pairs">
      <div><dt>Processus</dt><dd>{details.name} · {details.pid}</dd></div>
      <div><dt>Exécutable</dt><dd>{details.path ?? 'Chemin indisponible'}</dd></div>
      <div><dt>Démarrage</dt><dd>{details.started ? new Date(details.started).toLocaleString('fr-FR') : '—'}</dd></div>
    </dl>}
    {!shown.length ? <p className="fine">{search ? 'Aucun processus correspondant.' : 'Actualisez pour charger la liste des processus.'}</p> :
      <div className="hub-table-wrap"><table>
        <thead><tr><th scope="col">Processus</th><th scope="col">PID</th><th scope="col">CPU</th><th scope="col">Mémoire</th><th scope="col">Action</th></tr></thead>
        <tbody>{shown.map(item => <tr key={`${item.pid}-${item.started}`}>
          <td><button className="text-button" type="button" onClick={() => setDetails(item)}>{item.icon && <img className="hub-process-icon" src={`data:image/png;base64,${item.icon}`} alt="" />} {item.name}</button></td>
          <td className="mono">{item.pid}</td><td>{percent(item.cpu)}</td><td>{bytes(item.ram)}</td>
          <td><button className="button secondary" type="button" disabled={busy || item.pid <= 4 || item.started === 0 || /^WinLayer/i.test(item.name)}
            onClick={() => setTarget(item)} aria-label={`Terminer ${item.name}, PID ${item.pid}`}>Terminer</button></td>
        </tr>)}</tbody>
      </table></div>}
  </section>
}

function Hardware({ metrics, view, history }: { metrics?: HubMetrics | null; view: 'summary' | 'cpu' | 'gpu' | 'ram' | 'disk'; history: number }) {
  const readings = metrics?.history.slice(-history) ?? []
  const latest = readings.at(-1)
  const identity = [
    { key: 'cpu', label: 'CPU', value: latest?.cpu, Icon: Cpu, ink: 'accent' },
    { key: 'ram', label: 'Mémoire', value: latest?.ram, Icon: MemoryStick, ink: 'positive' },
    { key: 'gpu', label: 'GPU', value: latest?.gpu, Icon: Radio, ink: 'accent' },
  ] as const
  const wanted = identity.filter(item => view === 'summary' || item.key === view)
  const active = view === 'cpu' || view === 'summary'
  return <section className="hub-panel hub-monitor-panel">
    <div className="hub-panel-heading"><h2>{systemViews.find(([key]) => key === view)?.[1]}</h2><span className="fine">{metrics ? 'Compteurs Windows' : 'Collecte indisponible'}</span></div>
    {wanted.length > 0 && <>
      <div className="hub-system-values">
        {wanted.map(({ key, label, value, Icon }) => <div key={key}><span className="hub-stat-label"><Icon size={17} aria-hidden="true" />{label}</span>
          <strong className="hub-value">{percent(value)}</strong></div>)}
      </div>
      <StatsChart history={readings} series={wanted.map(({ key, label, ink }) => ({ key, label, ink }))} />
    </>}
    <dl className="hub-metric-pairs">
      {active && <>
        <div><dt>Modèle CPU</dt><dd>{metrics?.processor ?? 'Indisponible'}</dd></div>
        <div><dt>Cœurs / processeurs logiques</dt><dd>{metrics?.cores ?? '—'} / {metrics?.threads ?? '—'}</dd></div>
        <div><dt>Fréquence nominale Windows</dt><dd>{metrics?.cpuMHz ? `${metrics.cpuMHz} MHz` : '—'}</dd></div>
      </>}
      {(view === 'gpu' || view === 'summary') && <>
        <div><dt>Adaptateurs graphiques</dt><dd>{metrics?.gpuName ?? 'Indisponible'}</dd></div>
        <div><dt>Mémoire dédiée utilisée / totale</dt><dd>{bytes(metrics?.gpuUsed)} / {bytes(metrics?.gpuMemory)}</dd></div>
      </>}
      {(view === 'ram' || view === 'summary') && <>
        <div><dt>Mémoire totale visible par Windows</dt><dd>{bytes(metrics?.totalRam)}</dd></div>
        <div><dt>Utilisée / disponible</dt><dd>{metrics?.totalRam != null && metrics.freeRam != null ? bytes(metrics.totalRam - metrics.freeRam) : '—'} / {bytes(metrics?.freeRam)}</dd></div>
      </>}
      {view === 'disk' && <>
        <div><dt>Lecture totale</dt><dd>{rate(metrics?.diskRead, 'megabytes')}</dd></div>
        <div><dt>Écriture totale</dt><dd>{rate(metrics?.diskWrite, 'megabytes')}</dd></div>
      </>}
      {view !== 'disk' && view !== 'ram' && <div><dt>Températures</dt><dd>Capteurs non pris en charge</dd></div>}
    </dl>
    {view === 'cpu' && <div className="hub-core-grid">
      {metrics?.logicalCpu?.some(value => value !== null) ? metrics.logicalCpu.map((value, index) => <div key={index}>
        <span className="fine">CPU {index}</span><strong>{percent(value)}</strong>{value != null && <meter min={0} max={100} value={value} aria-label={`CPU ${index}`} />}
      </div>) : <p className="fine">Les compteurs par processeur logique sont indisponibles.</p>}
    </div>}
    {view === 'disk' && <ul className="hub-detail-list">
      {metrics?.disks.map(disk => <li key={disk.name}><span className="hub-stat-label"><HardDrive size={16} aria-hidden="true" />{disk.name}</span>
        <span>{bytes(disk.size - disk.free)} utilisés · {bytes(disk.free)} libres · {bytes(disk.size)} au total</span></li>)}
      {!metrics?.disks.length && <li>Aucun volume accessible.</li>}
    </ul>}
  </section>
}

export function SystemPage({ status, data, run, busy }: HubPageProps) {
  const [view, setView] = useState<SystemView>('summary')
  const options = data?.tools.system ?? defaultHubOptions('system')
  const optionsNetwork = data?.tools.network ?? defaultHubOptions('network')
  const metrics = options.enabled ? status.metrics : null
  return <>
    <TabButtons label="Ressources système" choices={systemViews} active={view} select={setView} />
    {view === 'network' ? <NetworkReadings metrics={metrics} options={optionsNetwork} /> :
      view === 'processes' ? <Processes items={metrics?.processes ?? []} busy={busy} run={run} enabled={options.enabled} /> :
      <Hardware view={view} metrics={metrics} history={options.history} />}
    <HubForm tool="system" options={options} key={JSON.stringify(options)} run={run} busy={busy}>
      {(draft, patch) => <>
        <div className="hub-form-grid">
          <NumberField label="Actualisation (s)" min={1} max={60} value={draft.seconds} onChange={seconds => patch({ seconds })} />
          <NumberField label="Historique (échantillons)" min={15} max={180} value={draft.history} onChange={history => patch({ history })} />
        </div>
        <fieldset className="hub-hotkey"><legend>Indicateurs de la zone de notification</legend>
          {([['cpu', 'CPU'], ['ram', 'Mémoire'], ['gpu', 'GPU']] as const).map(([metric, label]) =>
            <Flag key={metric} label={label} value={draft.trayMetrics.includes(metric)}
              onChange={value => patch({ trayMetrics: value ? [...draft.trayMetrics, metric] : draft.trayMetrics.filter(item => item !== metric) })} />)}
        </fieldset>
        {draft.trayMetrics.length > 0 && <>
          <Field label="Couleur de l’indicateur"><input type="color" value={draft.color}
            onChange={event => patch({ color: event.currentTarget.value })} /></Field>
          <Flag label="Alerte de seuil" value={draft.alerts} onChange={alerts => patch({ alerts })} />
          {draft.alerts && <NumberField label="Seuil (%)" min={1} max={100} value={draft.threshold} onChange={threshold => patch({ threshold })} />}
        </>}
      </>}
    </HubForm>
  </>
}
