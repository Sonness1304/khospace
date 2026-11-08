import { useEffect, useRef } from 'react';
import { priceLabel, type PriceBasis } from '../pricing/priceLabel';
export type PresentedUnit = PriceBasis & { id: string; code: string; areaM2: string };
export function UnitIllustration() {
  return <svg className="ks-unit-art" viewBox="0 0 360 170" aria-hidden="true"><rect width="360" height="170" fill="#e7eff8" /><ellipse cx="182" cy="152" rx="145" ry="14" fill="#c1d3e3" /><path d="M40 75 176 5 314 77 178 147Z" fill="#f5f9fe" stroke="#adc2d4" strokeWidth="2" /><path d="M40 75v31l138 65v-24Z" fill="#b3cadd" /><path d="M178 147v24l136-62V77Z" fill="#8eacc6" /><path d="m93 76 61-31 59 32-59 31z" fill="#ebc99e" /><path d="m93 76 61 32v31L93 105z" fill="#c99866" /><path d="m154 108 59-31v31l-59 31z" fill="#ddb181" /><path d="m173 110 55-29 59 32-57 29z" fill="#c0d2ed" /><path d="m173 110 57 32v24l-57-30z" fill="#789ac4" /><path d="m230 142 57-29v25l-57 28z" fill="#9ab6da" /></svg>;
}
export function UnitDetails({ unit, facility, fresh, onClose, onChoose }: { unit: PresentedUnit; facility: string; fresh: boolean; onClose: () => void; onChoose: () => void }) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    const trigger = document.activeElement;
    dialog?.showModal();
    return () => {
      if (dialog?.open) dialog.close();
      if (trigger instanceof HTMLElement && trigger.isConnected) trigger.focus();
    };
  }, []);
  return <dialog ref={ref} className="ks-unit-dialog" aria-labelledby="unit-details-title" onCancel={event => { event.preventDefault(); onClose(); }} onClick={event => { if (event.target === event.currentTarget) onClose(); }}>
    <div className="ks-dialog-body"><span className="ks-eyebrow">Thông tin khoang đã tra cứu</span><h2 id="unit-details-title">Khoang {unit.code}</h2><dl><div><dt>Cơ sở</dt><dd>{facility}</dd></div><div><dt>Diện tích</dt><dd>{unit.areaM2} m²</dd></div><div><dt>Đơn giá</dt><dd>{priceLabel(unit)}</dd></div><div><dt>Tình trạng</dt><dd>{fresh ? 'Phù hợp lịch vừa tìm; chưa giữ chỗ' : 'Điều kiện đã thay đổi; cần tìm lại'}</dd></div></dl><p>Hình ảnh là minh họa. Báo giá và đặt thuê sẽ kiểm tra lại điều kiện, giá và quyền của bạn.</p><div className="ks-dialog-actions"><button type="button" className="secondary-button" onClick={onClose}>Đóng</button><button type="button" disabled={!fresh} onClick={onChoose}>Chọn nhận báo giá</button></div></div>
  </dialog>;
}
