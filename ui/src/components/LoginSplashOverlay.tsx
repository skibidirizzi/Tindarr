import { useEffect, useRef, useState } from 'react'

const SPLASH_DURATION_MS = 2000
const HEARTBEAT_PERIOD_MS = 600
const PULSE_AMPLITUDE = 0.12
const BASE_SCALE = 3
// Logo from ui/public/tindarr.png (Vite serves public at /)
const LOGO_SRC = '/tindarr.png'

/**
 * Full-screen overlay shown for 2s after login. Displays the Tindarr logo
 * with a sinus heartbeat pulsation while the swipedeck loads behind.
 */
export default function LoginSplashOverlay() {
  const [elapsed, setElapsed] = useState(0)
  const rafRef = useRef<number>(0)
  const startRef = useRef<number>(0)

  useEffect(() => {
    startRef.current = performance.now()

    const tick = (now: number) => {
      const e = now - startRef.current
      setElapsed(e)
      if (e < SPLASH_DURATION_MS) {
        rafRef.current = requestAnimationFrame(tick)
      }
    }
    rafRef.current = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(rafRef.current)
  }, [])

  const scale =
    BASE_SCALE *
    (1 +
      PULSE_AMPLITUDE *
        Math.sin((2 * Math.PI * elapsed) / HEARTBEAT_PERIOD_MS))

  return (
    <div
      className="fixed inset-0 z-[100] flex items-center justify-center bg-gradient-to-br from-slate-900 via-purple-900 to-slate-900"
      aria-hidden="true"
    >
      <img
        src={LOGO_SRC}
        alt=""
        className="h-24 w-auto object-contain md:h-32"
        style={{
          transform: `scale(${scale})`,
          transition: 'none',
        }}
      />
    </div>
  )
}
