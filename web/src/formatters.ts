export function formatRateMbps(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return '不可用'
  return value < 1 ? `${(value * 1000).toFixed(2)} Kbps` : `${value.toFixed(2)} Mbps`
}
