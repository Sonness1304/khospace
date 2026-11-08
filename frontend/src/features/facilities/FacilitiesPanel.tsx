import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError, command, isDefiniteRejection } from '../../api';
import type { WorkspaceAccess } from '../auth/workspace';
import QuotePanel from '../quotes/QuotePanel';
import { priceLabel } from '../pricing/priceLabel';
import type { SearchPreset } from '../discovery/sizeAdvisor';
import { UnitDetails, UnitIllustration } from '../discovery/UnitPresentation';

type Facility = { id: string; name: string; address: string; permittedGoods: string };
type Unit = { id: string; code: string; areaM2: string; dailyRate?: string; billingBasis?: string; monthlyRate?: string | null; currency?: string; zoneId?: string; published?: boolean; version?: string; retiredAt?: string | null; retirementReason?: string | null };
type Page<T> = { items: T[]; nextCursor: string | null };
type Catalog = { facility: Facility; zones: { id: string; code: string }[]; units: Unit[]; unitsPage?: number; zonesPage?: number; unitTotal?: number; zoneTotal?: number };
type Pending = { key: string; facility: string; userId: string; path: string; body: unknown; method: string; version?: string; uncertain?: boolean };
const localDate = (days = 0) => new Date(Date.now() + 7 * 3600000 + days * 86400000).toISOString().slice(0, 10);

export default function FacilitiesPanel({ access, preset, active = true }: { access: WorkspaceAccess | null; preset?: SearchPreset; active?: boolean }) {
  const [facilities, setFacilities] = useState<Facility[]>([]);
  const [facilityNext, setFacilityNext] = useState<string | null>(null);
  const [facilityId, setFacilityId] = useState('');
  const [startDate, setStartDate] = useState(localDate(1));
  const [endDate, setEndDate] = useState(localDate(2));
  const [minArea, setMinArea] = useState('0');
  const [units, setUnits] = useState<Unit[] | null>(null);
  const [quoteUnits, setQuoteUnits] = useState<string[]>([]);
  const [next, setNext] = useState<string | null>(null);
  const [searched, setSearched] = useState('');
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState('');
  const [comparison, setComparison] = useState<string[]>([]);
  const [detailsId, setDetailsId] = useState('');
  const [renderedActive, setRenderedActive] = useState(active);
  if (renderedActive !== active) { setRenderedActive(active); setDetailsId(''); }
  const [sort, setSort] = useState('area');
  const [comparisonNotice, setComparisonNotice] = useState('');
  const [appliedPreset, setAppliedPreset] = useState('');
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [code, setCode] = useState(''); const [area, setArea] = useState(''); const [zone, setZone] = useState('');
  const [published, setPublished] = useState(false); const [reason, setReason] = useState(''); const [rate, setRate] = useState('');
  const [billingBasis, setBillingBasis] = useState('Daily');
  const [rateHistory, setRateHistory] = useState<{ id: string; billingBasis: string; dailyRate: string; monthlyRate: string | null; salesValidFrom: string; salesValidTo: string | null }[] | null>(null);
  const [historyPage, setHistoryPage] = useState(0); const [historyNext, setHistoryNext] = useState<number | null>(null); const historyRequest = useRef(0);
  const [busy, setBusy] = useState(false); const [pending, setPending] = useState<Pending | null>(null);
  const [canRetry, setCanRetry] = useState(false);
  const [retirementFor, setRetirementFor] = useState('');
  const lifecycle = useRef<AbortController | null>(null); const searchController = useRef<AbortController | null>(null);
  const writeBusy = useRef(false); const pendingRef = useRef<Pending | null>(null);
  const canManage = !!access && (access.operatorAdmin || access.facilityId === facilityId && access.facilityRoles.includes('FacilityManager'));
  const criteria = JSON.stringify([facilityId, startDate, endDate, minArea]);
  const currentCriteria = useRef(criteria); currentCriteria.current = criteria;
  const [renderedCriteria, setRenderedCriteria] = useState(criteria);
  if (renderedCriteria !== criteria) { setRenderedCriteria(criteria); setLoading(false); }
  if (preset && appliedPreset !== preset.id) {
    setAppliedPreset(preset.id);
    setFacilityId(preset.facilityId); setStartDate(preset.startDate); setEndDate(preset.endDate); setMinArea(preset.minArea);
    setUnits(null); setNext(null); setSearched(''); setQuoteUnits([]); setComparison([]); setDetailsId(''); setComparisonNotice('');
  }
  const contextKey = `${access?.userId ?? ''}/${access?.facilityId ?? ''}/${canManage}`;
  const visibleContext = useRef(`${contextKey}/${facilityId}`);
  const [renderedContext, setRenderedContext] = useState(`${contextKey}/${facilityId}`);
  if (renderedContext !== `${contextKey}/${facilityId}`) {
    setRenderedContext(`${contextKey}/${facilityId}`);
    setUnits(null); setNext(null); setSearched(''); setLoading(false);
    setCatalog(null); setSelected([]); setCode(''); setArea(''); setZone(''); setRate('');
    setQuoteUnits([]); setBillingBasis('Daily'); setRateHistory(null); historyRequest.current++;
    setComparison([]); setDetailsId(''); setComparisonNotice('');
  }
  useEffect(() => {
    const controller = new AbortController(); lifecycle.current = controller;
    void api<Page<Facility>>('/public/facilities', controller.signal).then(result => {
      if (!controller.signal.aborted) { setFacilities(result.items); setFacilityNext(result.nextCursor); }
    }).catch(() => { if (!controller.signal.aborted) setMessage('Không tải được danh mục cơ sở kho. Tải lại trang để thử lại.'); });
    return () => { controller.abort(); searchController.current?.abort(); };
  }, []);
  useEffect(() => {
    visibleContext.current = `${contextKey}/${facilityId}`;
    searchController.current?.abort();
    if (!facilityId || !canManage) return;
    const controller = new AbortController();
    void api<Catalog>(`/facilities/${facilityId}/catalog`, controller.signal).then(value => {
      if (!controller.signal.aborted) { setCatalog(value); setZone(value.zones[0]?.id ?? ''); }
    }).catch(() => { if (!controller.signal.aborted) setMessage('Chưa xác minh được quyền quản lý. Làm mới quyền truy cập rồi thử lại.'); });
    return () => controller.abort();
  }, [facilityId, canManage, contextKey]);
  useEffect(() => { searchController.current?.abort(); }, [criteria]);
  useEffect(() => {
    if (!preset?.facilityId) return;
    const controller = new AbortController(); searchController.current = controller;
    const expected = JSON.stringify([preset.facilityId, preset.startDate, preset.endDate, preset.minArea]);
    const query = new URLSearchParams({ facilityId: preset.facilityId, startDate: preset.startDate, endDate: preset.endDate, minArea: preset.minArea });
    void Promise.resolve().then(() => {
      if (controller.signal.aborted) return null;
      setLoading(true); setMessage('');
      return api<Page<Unit>>(`/availability?${query}`, controller.signal);
    }).then(result => {
      if (!result) return;
      if (!controller.signal.aborted && currentCriteria.current === expected) {
        setUnits(result.items); setQuoteUnits([]); setComparison([]); setDetailsId(''); setNext(result.nextCursor); setSearched(expected);
      }
    }).catch(() => {
      if (!controller.signal.aborted) { setMessage('Chưa hoàn tất tìm kiếm. Kiểm tra ngày thuê và kết nối rồi thử lại.'); setUnits(null); }
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [preset]);

  async function moreFacilities() {
    const controller = lifecycle.current; if (!controller || !facilityNext) return;
    try { const result = await api<Page<Facility>>(`/public/facilities?after=${encodeURIComponent(facilityNext)}`, controller.signal); if (!controller.signal.aborted) { setFacilities(values => [...values, ...result.items]); setFacilityNext(result.nextCursor); } }
    catch { if (!controller.signal.aborted) setMessage('Chưa tải thêm được cơ sở kho. Giữ lựa chọn hiện tại.'); }
  }
  async function catalogPage(unitPage: number, zonePage: number) {
    const controller = lifecycle.current; if (!controller || !canManage || busy || !!pending) return; setBusy(true); const expected = `${contextKey}/${facilityId}`;
    try { const value = await api<Catalog>(`/facilities/${facilityId}/catalog?unitsPage=${unitPage}&zonesPage=${zonePage}`, controller.signal); if (!controller.signal.aborted && visibleContext.current === expected) { setCatalog(value); setSelected([]); } }
    catch { if (!controller.signal.aborted) setMessage('Chưa xác minh được dữ liệu quản lý.'); } finally { if (!controller.signal.aborted) setBusy(false); }
  }
  async function search(after?: string) {
    if (!facilityId) return;
    searchController.current?.abort(); const controller = new AbortController(); searchController.current = controller;
    setLoading(true); setMessage('');
    try {
      const query = new URLSearchParams({ facilityId, startDate, endDate, minArea, ...(after ? { after } : {}) });
      const result = await api<Page<Unit>>(`/availability?${query}`, controller.signal);
      if (!controller.signal.aborted && currentCriteria.current === criteria) { setUnits(result.items); setQuoteUnits([]); setComparison([]); setDetailsId(''); setComparisonNotice(''); setNext(result.nextCursor); setSearched(criteria); }
    } catch { if (!controller.signal.aborted) { setMessage('Chưa hoàn tất tìm kiếm. Kiểm tra ngày thuê và kết nối rồi thử lại.'); setUnits(null); } }
    finally { if (!controller.signal.aborted) setLoading(false); }
  }
  function submit(event: FormEvent) { event.preventDefault(); void search(); }
  async function reloadCatalog(signal: AbortSignal, expectedContext: string) {
    const value = await api<Catalog>(`/facilities/${facilityId}/catalog`, signal);
    if (!signal.aborted && visibleContext.current === expectedContext) { setCatalog(value); setSelected([]); setUnits(null); }
  }
  async function write(path: string, body: unknown, method = 'POST', version?: string) {
    if (pendingRef.current || !access) return;
    const receipt = { key: crypto.randomUUID(), facility: facilityId, userId: access.userId, path, body: structuredClone(body), method, version };
    await attempt(receipt);
  }
  async function attempt(receipt: Pending) {
    const controller = lifecycle.current;
    if (!controller || controller.signal.aborted || writeBusy.current || !canManage || receipt.userId !== access?.userId || receipt.facility !== facilityId) return;
    const expectedContext = `${contextKey}/${facilityId}`;
    writeBusy.current = true; pendingRef.current = receipt; setPending(receipt); setBusy(true); setCanRetry(false); setMessage('');
    try {
      await command(receipt.path, receipt.body, receipt.key, controller.signal, receipt.method, receipt.version);
      if (controller.signal.aborted) return;
      pendingRef.current = null; setPending(null); setMessage('Đã lưu yêu cầu. Tải danh mục hiện tại trước khi thay đổi tiếp.');
      if (visibleContext.current === expectedContext) {
        try { await reloadCatalog(controller.signal, expectedContext); }
        catch { if (!controller.signal.aborted && visibleContext.current === expectedContext) setMessage('Đã lưu yêu cầu nhưng chưa tải lại được danh mục. Làm mới quyền truy cập trước khi thay đổi tiếp.'); }
      }
    } catch (error) {
      if (controller.signal.aborted) return;
      if (!receipt.uncertain && isDefiniteRejection(error)) {
        pendingRef.current = null; setPending(null); setMessage('Yêu cầu bị từ chối. Làm mới quyền truy cập và phiên bản dữ liệu, kiểm tra các trường và giữ bản nháp.'); }
      else {
        const unresolved = { ...receipt, uncertain: true }; pendingRef.current = unresolved; setPending(unresolved);
        setMessage('Chưa xác định kết quả yêu cầu. Đọc kết quả đã lưu trước khi gửi yêu cầu khác.');
      }
    } finally { writeBusy.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  async function readback() {
    const controller = lifecycle.current;
    if (!controller || !pending || busy || pending.userId !== access?.userId || pending.facility !== facilityId || !canManage) return;
    setBusy(true);
    const expectedContext = `${contextKey}/${facilityId}`;
    try {
      await api(`/commands/${pending.key}?scopeId=${pending.facility}`, controller.signal);
      if (!controller.signal.aborted) { pendingRef.current = null; setPending(null); setMessage('Đã xác minh yêu cầu đã lưu.');
        setCanRetry(false);
        if (visibleContext.current === expectedContext) {
          try { await reloadCatalog(controller.signal, expectedContext); }
          catch { if (!controller.signal.aborted) setMessage('Đã xác minh yêu cầu đã lưu nhưng chưa tải lại được danh mục.'); }
        } }
    } catch (error) { if (!controller.signal.aborted) {
      setCanRetry(error instanceof ApiError && error.status === 404);
      setMessage('Chưa xác minh được kết quả đã lưu. Giữ mã định danh yêu cầu này và kiểm tra lại.');
    } }
    finally { if (!controller.signal.aborted) setBusy(false); }
  }
  function pick(unit: Unit) {
    setRateHistory(null); historyRequest.current++; setHistoryPage(0); setHistoryNext(null); setSelected([unit.id]); setCode(unit.code); setArea(unit.areaM2); setZone(unit.zoneId ?? ''); setPublished(unit.published ?? false);
  }
  const single = selected.length === 1 ? catalog?.units.find(u => u.id === selected[0]) : undefined;
  async function readRates(page = 0) {
    const controller = lifecycle.current; if (!controller || !single || !canManage) return;
    const expected = visibleContext.current; const selectedId = single.id; const request = ++historyRequest.current;
    setRateHistory(null);
    try {
      const result = await api<{ items: NonNullable<typeof rateHistory>; nextPage: number | null }>('/units/' + selectedId + '/rates?page=' + page, controller.signal);
      if (!controller.signal.aborted && expected === visibleContext.current && request === historyRequest.current) { setRateHistory(result.items); setHistoryPage(page); setHistoryNext(result.nextPage); }
    } catch { if (!controller.signal.aborted && expected === visibleContext.current && request === historyRequest.current) setMessage('Chưa xác minh được lịch sử đơn giá.'); }
  }
  const disabled = busy || !!pending || !catalog;
  const fresh = searched === criteria && !loading;
  const facilityName = facilities.find(item => item.id === facilityId)?.name ?? 'Cơ sở đã chọn';
  const detailUnit = units?.find(unit => unit.id === detailsId);
  const comparedUnits = units?.filter(unit => comparison.includes(unit.id)) ?? [];
  const sortedUnits = [...(units ?? [])].sort((a, b) => sort === 'code' ? a.code.localeCompare(b.code, 'vi') : Number(a.areaM2) - Number(b.areaM2) || a.code.localeCompare(b.code, 'vi'));
  function compare(id: string, checked: boolean) {
    if (checked && comparison.length >= 3) { setComparisonNotice('Chỉ so sánh tối đa 3 khoang. Bỏ một lựa chọn để thêm khoang khác.'); return; }
    setComparisonNotice(''); setComparison(values => checked ? [...values, id] : values.filter(value => value !== id));
  }

  return <section className="workspace-panel catalog-panel" aria-labelledby="catalog-title">
    <div className="panel-heading"><div><p className="eyebrow">TÌM KHÔNG GIAN LƯU TRỮ</p><h2 id="catalog-title">Tìm kho theo lịch của bạn</h2></div><span className="outline-badge">Thuê nguyên khoang</span></div>
    <div className="session-content">
      <ol className="catalog-progress" aria-label="Các bước đặt kho"><li aria-current={units === null ? 'step' : undefined}><span>1</span>Chọn lịch thuê</li><li aria-current={units !== null && quoteUnits.length === 0 ? 'step' : undefined}><span>2</span>Chọn khoang phù hợp</li><li aria-current={quoteUnits.length > 0 ? 'step' : undefined}><span>3</span>Xem báo giá</li></ol>
      <form className="catalog-search" onSubmit={submit}>
        <label>Cơ sở kho<select required value={facilityId} onChange={event => setFacilityId(event.target.value)}><option value="">Chọn cơ sở kho</option>{facilities.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label>Ngày bắt đầu<input required type="date" value={startDate} onChange={event => setStartDate(event.target.value)} /></label>
        <label>Ngày kết thúc (không tính)<input required type="date" value={endDate} onChange={event => setEndDate(event.target.value)} /></label>
        <label>Diện tích tối thiểu (m²)<input type="number" min="0" step="0.01" value={minArea} onChange={event => setMinArea(event.target.value)} /></label>
        <button disabled={loading || !facilityId} type="submit">Tìm khoang còn trống</button>
      </form>
      {facilityNext && <button type="button" onClick={() => void moreFacilities()}>Tải thêm cơ sở kho</button>}
      {facilityId && <p className="catalog-help">{facilities.find(f => f.id === facilityId)?.address} · {facilities.find(f => f.id === facilityId)?.permittedGoods}</p>}
      <p className="catalog-help">Tình trạng còn trống chỉ để tham khảo. Tìm kiếm hoặc nhận báo giá chưa giữ chỗ. Không tính ngày kết thúc; tiền thuê áp dụng cho nguyên khoang.</p>
      {units === null && !loading && !message && <div className="catalog-start"><span aria-hidden="true">⌑</span><div><h3>Kho phù hợp bắt đầu từ nhu cầu của bạn</h3><p>Chọn cơ sở, thời gian và diện tích. Chúng tôi sẽ kiểm tra các khoang còn trống theo điều kiện bạn chọn.</p></div></div>}
      {loading && <p role="status">Đang kiểm tra khoang còn trống…</p>}
      {message && <p role="status" className="session-notice">{message}</p>}
      {units && <div aria-label="Khoang còn trống">
        {searched !== criteria && <p role="status">Điều kiện tìm kiếm đã thay đổi. Tìm lại để xác minh các khoang này.</p>}
        {!units.length && <p>Không có khoang phù hợp. Hãy thử đổi ngày thuê hoặc giảm diện tích tối thiểu rồi tìm lại.</p>}
        {!!units.length && <div className="ks-results-toolbar"><strong>{units.length} khoang trên trang này · {facilityName}</strong><label>Sắp xếp theo<select value={sort} onChange={event => setSort(event.target.value)}><option value="area">Diện tích tăng dần</option><option value="code">Mã khoang</option></select></label></div>}
        <div className="ks-availability-grid">{sortedUnits.map(unit => <article key={unit.id} className="ks-unit-card">
          <div className="ks-unit-visual"><UnitIllustration /><span className="ks-unit-state">{fresh ? 'Phù hợp lịch tìm kiếm' : 'Cần xác minh lại'}</span></div>
          <div className="ks-unit-body"><div className="ks-unit-title"><h3>{unit.code}</h3><strong>{unit.areaM2} m²</strong></div><p>{facilityName} · Thuê nguyên khoang</p><div className="ks-unit-price">{priceLabel(unit)}<small>Đơn giá theo m² · chưa phải tổng tiền thuê</small></div>
            <div className="ks-unit-actions"><label className="ks-unit-select"><input type="checkbox" aria-label={unit.code} disabled={!fresh} checked={quoteUnits.includes(unit.id)} onChange={event => setQuoteUnits(ids => event.target.checked ? [...ids, unit.id] : ids.filter(id => id !== unit.id))} />Chọn báo giá</label><button type="button" className="secondary-button" aria-label={`Chi tiết ${unit.code}`} onClick={() => setDetailsId(unit.id)}>Xem chi tiết →</button></div>
            <label className="ks-compare-select"><input type="checkbox" aria-label={`So sánh ${unit.code}`} disabled={!fresh} checked={comparison.includes(unit.id)} onChange={event => compare(unit.id, event.target.checked)} />Thêm vào so sánh</label>
          </div>
        </article>)}</div>
        {comparisonNotice && <p role="status">{comparisonNotice}</p>}
        {!!comparison.length && <div className="ks-compare-box"><div><strong>So sánh {comparison.length}/3 khoang</strong><p>{fresh ? 'Chọn ít nhất 2 khoang để đối chiếu diện tích và đơn giá.' : 'Điều kiện đã thay đổi. Tìm lại trước khi chọn báo giá.'}</p></div><button type="button" className="secondary-button" onClick={() => { setComparison([]); setComparisonNotice(''); }}>Xóa so sánh</button></div>}
        {comparedUnits.length >= 2 && <div className="ks-comparison"><table><caption>So sánh khoang trên trang hiện tại · chưa giữ chỗ</caption><thead><tr><th scope="col">Thông tin</th>{comparedUnits.map(unit => <th scope="col" key={unit.id}>{unit.code}</th>)}</tr></thead><tbody><tr><th scope="row">Diện tích</th>{comparedUnits.map(unit => <td key={unit.id}>{unit.areaM2} m²</td>)}</tr><tr><th scope="row">Đơn giá / m²</th>{comparedUnits.map(unit => <td key={unit.id}>{priceLabel(unit)}</td>)}</tr></tbody></table></div>}
        {next && searched === criteria && <button type="button" disabled={loading} onClick={() => void search(next)}>Trang khoang sau</button>}
      </div>}
      {active && detailUnit && <UnitDetails key={detailUnit.id} unit={detailUnit} facility={facilityName} fresh={fresh} onClose={() => setDetailsId('')} onChoose={() => { if (fresh) setQuoteUnits(ids => ids.includes(detailUnit.id) ? ids : [...ids, detailUnit.id]); setDetailsId(''); }} />}
      {quoteUnits.length > 0 && units && <div className="quote-selection" role="status"><strong>Đã chọn {quoteUnits.length} khoang · {new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(units.filter(unit => quoteUnits.includes(unit.id)).reduce((area, unit) => area + Number(unit.areaM2), 0))} m²</strong><span>Từ {startDate} đến trước {endDate}. Ngày kết thúc không tính tiền thuê.</span>{searched !== criteria && <span>Hãy tìm lại để xác minh lựa chọn với điều kiện mới.</span>}</div>}
      <QuotePanel access={access} selection={{ facilityId, unitIds: quoteUnits, startDate, endDate, verified: !!units && searched === criteria }} />
      {canManage && <details className="management-panel"><summary>Quản lý cơ sở đã chọn</summary>
        {!catalog ? <p>Đang tải cơ sở được phân công…</p> : <>
          <div className="unit-list">{catalog.units.map(unit => <div className="unit-row" key={unit.id}>
            <label><input type="checkbox" checked={selected.includes(unit.id)} disabled={disabled || !!unit.retiredAt} onChange={event => { setRetirementFor(''); setRateHistory(null); historyRequest.current++; setSelected(items => event.target.checked ? [...items, unit.id] : items.filter(id => id !== unit.id)); }} />{unit.code} · {unit.retiredAt ? 'Đã ngừng sử dụng' : unit.published ? 'Công khai' : 'Ẩn'}</label>
            <button className="secondary-button" type="button" disabled={disabled} onClick={() => pick(unit)}>Chỉnh sửa {unit.code}</button></div>)}</div>
          <div className="session-actions"><button disabled={disabled || (catalog.unitsPage ?? 1) <= 1} onClick={() => void catalogPage((catalog.unitsPage ?? 1) - 1, catalog.zonesPage ?? 1)}>Trang khoang quản lý trước</button><button disabled={disabled || (catalog.unitsPage ?? 1) * 50 >= (catalog.unitTotal ?? catalog.units.length)} onClick={() => void catalogPage((catalog.unitsPage ?? 1) + 1, catalog.zonesPage ?? 1)}>Trang khoang quản lý sau</button><button disabled={disabled || (catalog.zonesPage ?? 1) <= 1} onClick={() => void catalogPage(catalog.unitsPage ?? 1, (catalog.zonesPage ?? 1) - 1)}>Trang khu trước</button><button disabled={disabled || (catalog.zonesPage ?? 1) * 50 >= (catalog.zoneTotal ?? catalog.zones.length)} onClick={() => void catalogPage(catalog.unitsPage ?? 1, (catalog.zonesPage ?? 1) + 1)}>Trang khu sau</button></div>
          <div className="manager-fields">
            <label>Mã khoang<input value={code} maxLength={40} onChange={e => setCode(e.target.value)} /></label>
            <label>Diện tích (m²)<input type="number" min="0.01" step="0.01" value={area} onChange={e => setArea(e.target.value)} /></label>
            <label>Khu vực<select value={zone} onChange={e => setZone(e.target.value)}>{catalog.zones.map(z => <option key={z.id} value={z.id}>{z.code}</option>)}</select></label>
            <label className="check-label"><input type="checkbox" checked={published} onChange={e => setPublished(e.target.checked)} />Công khai khoang đủ điều kiện</label>
            <label>Lý do<input maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></label>
            <label>Cơ sở tính giá<select value={billingBasis} onChange={e => setBillingBasis(e.target.value)}><option value="Daily">Theo ngày</option><option value="CalendarMonth">Theo tháng dương lịch</option></select></label>
            <label>{billingBasis === 'CalendarMonth' ? 'Đơn giá tháng (VND/m²/tháng dương lịch)' : 'Đơn giá ngày (VND/m²)'}<input type="number" min="0.0001" step="0.0001" value={rate} onChange={e => setRate(e.target.value)} /></label>
          </div>
          <div className="session-actions">
            <button type="button" disabled={disabled || !code || !area || !zone || !reason} onClick={() => void write(`/facilities/${facilityId}/units`, { code, areaM2: area, zoneId: zone, published, reason })}>Tạo khoang</button>
            <button type="button" disabled={disabled || !single || !!single.retiredAt || !code || !area || !reason} onClick={() => single && void write(`/units/${single.id}`, { code, areaM2: area, zoneId: zone, published, reason }, 'PATCH', single.version)}>Cập nhật khoang đã chọn</button>
            <button type="button" disabled={disabled || !single || !!single.retiredAt || !rate || !reason} onClick={() => single && void write(`/units/${single.id}/rates`, { dailyRate: billingBasis === 'CalendarMonth' ? '0' : rate, ...(billingBasis === 'CalendarMonth' ? { billingBasis, monthlyRate: rate } : {}), salesValidFrom: startDate, salesValidTo: null, reason }, 'POST', single.version)}>Đặt đơn giá theo thời gian</button>
            <button type="button" disabled={busy || !single} onClick={() => void readRates()}>Xem lịch sử đơn giá đã chọn</button>
            <button type="button" disabled={disabled || !selected.length || catalog.units.some(u => selected.includes(u.id) && !!u.retiredAt) || !reason} onClick={() => void write('/maintenance-blocks', { facilityId, unitIds: selected, startDate, endDate, reason })}>Chặn lịch bảo trì</button>
            <label className="check-label"><input type="checkbox" checked={!!single && retirementFor === `${contextKey}/${facilityId}/${single.id}/${single.version}`} disabled={disabled || !single || !!single.retiredAt} onChange={e => setRetirementFor(e.target.checked && single ? `${contextKey}/${facilityId}/${single.id}/${single.version}` : '')} />Tôi xác nhận ngừng sử dụng vĩnh viễn khoang trống đã chọn</label>
            <button type="button" disabled={disabled || !single || !!single.retiredAt || !reason.trim() || retirementFor !== `${contextKey}/${facilityId}/${single.id}/${single.version}`} onClick={() => single && void write(`/units/${single.id}/retire`, { reason, retirementConfirmed: true }, 'POST', single.version)}>Ngừng sử dụng khoang đã chọn</button>
            {single?.retiredAt && <p>Đã ngừng sử dụng vĩnh viễn: {single.retiredAt} · {single.retirementReason}. Vẫn có thể xem hồ sơ lịch sử.</p>}
          </div>{rateHistory && <div><p>Lịch sử đơn giá · trang {historyPage + 1}</p>{rateHistory.map(rate => <p key={rate.id}>{rate.id} · {priceLabel(rate)} · {rate.salesValidFrom} đến {rate.salesValidTo ?? 'không có ngày kết thúc'}</p>)}<button type="button" disabled={historyPage === 0} onClick={() => void readRates(historyPage - 1)}>Trang đơn giá trước</button><button type="button" disabled={historyNext === null} onClick={() => historyNext !== null && void readRates(historyNext)}>Trang đơn giá sau</button></div>}<p className="catalog-help">Đơn giá tháng tính theo số ngày thực tế của từng tháng dương lịch. Bảo trì áp dụng theo ngày tìm kiếm và mọi khoang đã chọn. Đơn giá theo thời gian bắt đầu từ ngày đã chọn; không chấp nhận các khoảng áp dụng trùng nhau.</p>
        </>}
        {pending && <button type="button" disabled={busy || pending.facility !== facilityId || pending.userId !== access?.userId} onClick={() => void readback()}>Đọc kết quả yêu cầu</button>}
        {pending && canRetry && <button type="button" disabled={busy || pending.facility !== facilityId || pending.userId !== access?.userId} onClick={() => void attempt(pending)}>Thử lại yêu cầu gốc</button>}
      </details>}
    </div>
  </section>;
}
