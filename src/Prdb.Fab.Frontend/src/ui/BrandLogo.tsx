/** Decorative beside the app name; shared with the static loading screen. */
export function BrandLogo({ className = '' }: { className?: string }) {
  return (
    <img
      className={`brandLogo ${className}`}
      src="/logo.svg"
      width="32"
      height="32"
      alt=""
      aria-hidden="true"
      data-brand-logo
    />
  )
}
