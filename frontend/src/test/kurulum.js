// ============================================================================
//  Test hazırlığı — her test dosyasından ÖNCE bir kez çalışır
//  (vite.config.js → test.setupFiles)
// ============================================================================

// jest-dom eşleştiricileri: toBeInTheDocument, toHaveTextContent, toBeDisabled…
//
// Neden gerekli? Çıplak expect yalnızca değer karşılaştırıyor; "bu düğme
// gerçekten kapalı mı?" gibi DOM sorularını okunur biçimde soramıyor.
// Alternatifi her testte `el.hasAttribute('disabled')` yazmaktı — çalışır
// ama testin ne iddia ettiği kayboluyordu.
import '@testing-library/jest-dom/vitest'

import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// Her testten SONRA render edilen ağacı söküyoruz.
//
// Testing Library bunu bazı kurulumlarda kendisi yapıyor ama globals açıkken
// garanti değil. Temizlik olmazsa ikinci test, birinci testin bıraktığı
// DOM'u da görür ve getByRole "birden çok eşleşme" diye patlar — ya da daha
// kötüsü, yanlış elemanı bulup sessizce geçer.
afterEach(() => cleanup())
