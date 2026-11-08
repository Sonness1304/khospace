import { useState } from 'react';
import { estimateStorage, storageGoods, type GoodsCounts } from './sizeAdvisor';

export default function StoragePlanner({ onApply }: { onApply: (area: number) => void }) {
  const [counts, setCounts] = useState<GoodsCounts>(() => Object.fromEntries(storageGoods.map(item => [item.id, item.initial])));
  const estimate = estimateStorage(counts);
  return <section className="ks-section ks-planner-section" id="planner" tabIndex={-1} aria-labelledby="planner-title">
    <div className="ks-section-heading"><div><span className="ks-eyebrow">Gợi ý diện tích</span><h2 id="planner-title">Chọn m² bằng đồ đạc.<br />Không cần đoán.</h2></div><p>Bắt đầu với đồ bạn có. Ước tính không gian cần dùng trước khi tìm kho theo lịch thuê.</p></div>
    <div className="ks-planner">
      <div className="ks-panel"><div className="ks-panel-heading"><h3>Danh sách đồ cần lưu trữ</h3><span className="ks-chip">{estimate.count} món đồ</span></div><p className="ks-help">Tăng hoặc giảm số lượng để cập nhật diện tích gợi ý.</p>
        <div className="ks-item-list">{storageGoods.map(item => <div className="ks-item" key={item.id}><span className={`ks-item-icon ${item.id}`} aria-hidden="true">{item.icon}</span><div><strong>{item.label}</strong><small>{item.size}</small><div className="ks-counter"><button type="button" aria-label={`Giảm ${item.label}`} disabled={!counts[item.id]} onClick={() => setCounts(value => ({ ...value, [item.id]: Math.max(0, value[item.id] - 1) }))}>−</button><output aria-label={`Số lượng ${item.label}`}>{counts[item.id]}</output><button type="button" aria-label={`Tăng ${item.label}`} disabled={counts[item.id] >= 50} onClick={() => setCounts(value => ({ ...value, [item.id]: Math.min(50, value[item.id] + 1) }))}>+</button></div></div></div>)}</div>
        <div className="ks-planner-reset"><p className="ks-help">Ước tính theo kích thước tiêu chuẩn của từng món.</p><button type="button" className="ks-text-button" onClick={() => setCounts(Object.fromEntries(storageGoods.map(item => [item.id, 0])))}>Xóa lựa chọn</button></div>
      </div>
      <div className="ks-panel"><div className="ks-estimate" role="status" aria-live="polite"><div><span>Diện tích đề xuất · ước lượng</span><strong>{estimate.recommended ? `${estimate.recommended} m²` : '— m²'}</strong><p>{estimate.count ? `Khoảng ${estimate.estimated.toLocaleString('vi-VN', { maximumFractionDigits: 1 })} m² gồm dự phòng sơ bộ.` : 'Chọn đồ để bắt đầu tính không gian.'}</p></div><span className="ks-estimate-icon" aria-hidden="true">▦</span></div>
        <div className="ks-plan-box"><div className="ks-plan-heading"><span>Mặt bằng minh họa · không đúng tỷ lệ</span><strong>{estimate.recommended ? `${estimate.recommended} m²` : 'Chưa chọn đồ'}</strong></div><div className="ks-floor-plan" role="img" aria-label="Sơ đồ minh họa đồ cần lưu trữ, không phải bản vẽ xếp hàng">{storageGoods.flatMap(item => Array.from({ length: Math.min(counts[item.id], 8) }, (_, index) => <span key={`${item.id}-${index}`} className={`ks-plan-object ${item.id}`} aria-hidden="true">{item.icon}</span>))}</div><p className="ks-help">Dải vàng mô tả cửa. Chỉ hiển thị tối đa 8 biểu tượng mỗi loại; phép tính vẫn dùng toàn bộ số lượng.</p></div>
        <p className="ks-planner-disclaimer">Công thức: diện tích mặt sàn × 1,2 + 0,4 m², làm tròn lên. Chưa xét xếp chồng, chiều cao, cửa và vật cản; không cam kết mọi đồ đạc đều chứa vừa.</p>
        <div className="ks-callout"><p>Dùng gợi ý làm diện tích tối thiểu, rồi kiểm tra kho theo ngày thuê.</p><button type="button" disabled={!estimate.count} onClick={() => onApply(estimate.recommended)}>Xem kho phù hợp <span aria-hidden="true">→</span></button></div>
      </div>
    </div>
  </section>;
}
