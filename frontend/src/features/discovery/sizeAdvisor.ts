export const storageGoods = [
  { id: 'box', label: 'Thùng carton', size: '60 × 40 cm', icon: '▣', footprint: 0.24, initial: 6 },
  { id: 'sofa', label: 'Ghế sofa', size: '2 × 0,85 m', icon: '▰', footprint: 1.7, initial: 1 },
  { id: 'bike', label: 'Xe đạp', size: '1,6 × 0,6 m', icon: '◎', footprint: 0.96, initial: 0 },
  { id: 'wardrobe', label: 'Tủ quần áo', size: '1,2 × 0,6 m', icon: '▥', footprint: 0.72, initial: 0 },
  { id: 'fridge', label: 'Tủ lạnh', size: '0,7 × 0,7 m', icon: '▤', footprint: 0.49, initial: 0 },
  { id: 'washer', label: 'Máy giặt', size: '0,65 × 0,65 m', icon: '◉', footprint: 0.4225, initial: 0 },
  { id: 'desk', label: 'Bàn làm việc', size: '1,2 × 0,6 m', icon: '▱', footprint: 0.72, initial: 0 },
] as const;
export type GoodsCounts = Record<string, number>;
export function estimateStorage(counts: GoodsCounts) {
  let count = 0; let footprint = 0;
  for (const item of storageGoods) {
    const raw = counts[item.id] ?? 0;
    const quantity = Number.isFinite(raw) ? Math.min(50, Math.max(0, Math.floor(raw))) : 0;
    count += quantity; footprint += quantity * item.footprint;
  }
  const estimated = count ? footprint * 1.2 + 0.4 : 0;
  const recommended = count ? [2, 4, 6, 8, 12, 16, 24, 32].find(size => size >= estimated) ?? Math.ceil(estimated / 4) * 4 : 0;
  return { count, footprint, estimated, recommended };
}
export type SearchCriteria = { facilityId: string; startDate: string; endDate: string; minArea: string };
export type SearchPreset = SearchCriteria & { id: string };
export const discoveryDate = (days = 0) => new Date(Date.now() + 7 * 3600000 + days * 86400000).toISOString().slice(0, 10);
