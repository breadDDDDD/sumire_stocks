const TZ = 'Asia/Jakarta'

const timeFmt = new Intl.DateTimeFormat('en-GB', {
  timeZone: TZ,
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false,
})

const dateFmt = new Intl.DateTimeFormat('en-GB', {
  timeZone: TZ,
  day: '2-digit',
  month: 'short',
  year: 'numeric',
})

const partsFmt = new Intl.DateTimeFormat('en-US', {
  timeZone: TZ,
  weekday: 'short',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
})

function toDate(d: Date | string | number): Date {
  return d instanceof Date ? d : new Date(d)
}

/** "14:32:05" in WIB. */
export function formatTimeWib(d: Date | string | number): string {
  return timeFmt.format(toDate(d))
}

/** "22 Oct 2026" in WIB. */
export function formatDateWib(d: Date | string | number): string {
  return dateFmt.format(toDate(d))
}

/** Weekday (0 = Sun) and minutes since midnight in WIB. */
export function wibParts(d: Date = new Date()): { weekday: number; minutes: number } {
  const parts = partsFmt.formatToParts(d)
  const get = (t: string) => parts.find((p) => p.type === t)?.value ?? ''
  const weekday = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].indexOf(get('weekday'))
  return { weekday, minutes: Number(get('hour')) * 60 + Number(get('minute')) }
}
