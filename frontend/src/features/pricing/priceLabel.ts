export type PriceBasis = { billingBasis?: string; dailyRate?: string; monthlyRate?: string | null; calendarDays?: number | null; days?: number };
export function priceLabel(value: PriceBasis) {
  return value.billingBasis === 'CalendarMonth' ? `${value.monthlyRate} VND/m²/tháng dương lịch` : `${value.dailyRate ?? ''} VND/m²/ngày`;
}
