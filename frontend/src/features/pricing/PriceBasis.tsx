import { priceLabel, type PriceBasis } from './priceLabel';
export default function PriceBasisNote({ value }: { value: PriceBasis }) {
  return <span>{priceLabel(value)}{value.billingBasis === 'CalendarMonth' && value.calendarDays ? ` · ${value.days}/${value.calendarDays} ngày` : ''}</span>;
}
