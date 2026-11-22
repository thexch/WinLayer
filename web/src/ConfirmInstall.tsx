import { type ReactNode } from 'react'
import { ArrowUpRight, X } from 'lucide-react'
import * as Dialog from '@radix-ui/react-dialog'
import { LazyMotion, domAnimation, m, useReducedMotion } from 'motion/react'

export default function ConfirmInstall({ version, onInstall, children }: {
  version: string; onInstall: () => void; children: ReactNode
}) {
  const reduce = useReducedMotion()
  return (
    <Dialog.Root>
      <Dialog.Trigger asChild>{children}</Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content className="dialog-position" onOpenAutoFocus={(event) => {
          const cancel = document.getElementById('cancel-install')
          if (cancel) { event.preventDefault(); cancel.focus() }
        }}>
          <LazyMotion features={domAnimation}>
            <m.div className="dialog-panel" initial={reduce ? false : { opacity: 0, y: 7 }}
              animate={{ opacity: 1, y: 0 }} transition={reduce ? { duration: 0 } : { duration: 0.18, ease: [0.22, 1, 0.36, 1] }}>
              <Dialog.Close asChild>
                <button type="button" className="icon-button dialog-close" aria-label="Fermer la confirmation"><X size={18} /></button>
              </Dialog.Close>
              <span className="tile-icon accent-tile"><ArrowUpRight size={23} /></span>
              <Dialog.Title>Installer WinLayer {version} ?</Dialog.Title>
              <Dialog.Description>Les réglages se fermeront et WinLayer redémarrera après l’installation.</Dialog.Description>
              <div className="dialog-actions">
                <Dialog.Close asChild><button id="cancel-install" type="button" className="button secondary">Annuler</button></Dialog.Close>
                <Dialog.Close asChild><button type="button" className="button primary" onClick={onInstall}>Installer et redémarrer</button></Dialog.Close>
              </div>
            </m.div>
          </LazyMotion>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
