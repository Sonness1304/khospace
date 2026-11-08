export type NoticeTarget = { kind: 'lease' | 'request'; id: string };
export function noticeTarget(kind: string, aggregateId?: string): NoticeTarget | null {
 if (typeof aggregateId !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(aggregateId) ||
  aggregateId === '00000000-0000-0000-0000-000000000000') return null;
 const target = kind === 'LeaseEnding' ? 'lease' : kind === 'StockAction' ? 'request' : null;
 return target ? { kind: target, id: aggregateId.toLowerCase() } : null;
}
