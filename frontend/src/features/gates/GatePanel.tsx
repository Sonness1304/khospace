import { displayValue } from '../../locale/vi';
import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { api, command, isDefiniteRejection } from '../../api';
import type { WorkspaceAccess } from '../auth/workspace';
type Lane = { id:string; facilityId:string; code:string; active:boolean; version:string };
type Visit = { id:string; organizationId:string; facilityId:string; laneId:string; leaseId:string; direction:string; vehicle:string; startUtc:string; endUtc:string; state:string; version:string; arrivedAt?:string; departedAt?:string };
type Pending = { key:string; binding:string; scopeId:string; path:string; body:object; kind:'lane'|'visit'; resourceId?:string; version?:string; uncertain?:boolean };
type Page<T> = { facilityId:string; organizationId?:string; items:T[]; page:number; total:number };
export default function GatePanel({access}:{access:WorkspaceAccess|null}) {
  const binding=JSON.stringify([access?.userId,access?.organizationId,access?.facilityId,access?.facilityRoles,access?.operatorAdmin]);
  const current=useRef(binding);useLayoutEffect(()=>{current.current=binding;},[binding]);
  const [code,setCode]=useState('');const [laneId,setLaneId]=useState('');const [leaseId,setLeaseId]=useState('');const [vehicle,setVehicle]=useState('');const [direction,setDirection]=useState('Inbound');
  const [start,setStart]=useState('');const [end,setEnd]=useState('');const [reason,setReason]=useState('');const [id,setId]=useState('');const [from,setFrom]=useState('');const [until,setUntil]=useState('');
  const [laneData,setLaneData]=useState<{binding:string;value:Lane}|null>(null);const [visitData,setVisitData]=useState<{binding:string;value:Visit}|null>(null);
  const [lanes,setLanes]=useState<{binding:string;value:Page<Lane>}|null>(null);const [visits,setVisits]=useState<{binding:string;query:string;value:Page<Visit>}|null>(null);
  const [pending,setPending]=useState<Pending|null>(null);const unresolved=useRef<Pending|null>(null);const [message,setMessage]=useState('');const [busy,setBusy]=useState(false);
  const [draftBinding,setDraftBinding]=useState(binding);if(draftBinding!==binding){setDraftBinding(binding);setCode('');setLaneId('');setLeaseId('');setVehicle('');setDirection('Inbound');setStart('');setEnd('');setReason('');setId('');setFrom('');setUntil('');setMessage('');}
  const running=useRef(false);const lifecycle=useRef<AbortController|null>(null);useEffect(()=>{const c=new AbortController();lifecycle.current=c;return()=>c.abort();},[]);
  const manager=!!access&&(access.operatorAdmin||access.facilityRoles.includes('FacilityManager'));const staff=manager||!!access?.facilityRoles.includes('Clerk');
  const canSchedule=!!access?.organizationId;const lane=laneData?.binding===binding&&laneData.value.id===laneId?laneData.value:null;const visit=visitData?.binding===binding&&visitData.value.id===id?visitData.value:null;
  const laneList=lanes?.binding===binding?lanes.value:null;const visitList=visits?.binding===binding&&visits.query===JSON.stringify([from,until])?visits.value:null;
  const permitted=(value:Visit)=>!!access&&value.facilityId===access.facilityId&&(staff||value.organizationId===access.organizationId);
  const orgQuery=access?.organizationId?`&organizationId=${encodeURIComponent(access.organizationId)}`:'';
  async function load(kind:'lane'|'visit',target:string,original:string,signal:AbortSignal){
    if(kind==='lane'){const value=await api<Lane>(`/gate-lanes/${encodeURIComponent(target)}?${orgQuery.slice(1)}`,signal);if(value.id!==target||value.facilityId!==access?.facilityId)throw Error();if(!signal.aborted&&current.current===original){setLaneId(target);setLaneData({binding:original,value});}}
    else{const value=await api<Visit>(`/gate-visits/${encodeURIComponent(target)}`,signal);if(value.id!==target||!permitted(value))throw Error();if(!signal.aborted&&current.current===original){setId(target);setVisitData({binding:original,value});}}
  }
  async function read(kind:'lane'|'visit'|'làn xe'|'lượt xe',page=1){
    const c=lifecycle.current;if(!c||running.current||!access)return;running.current=true;setBusy(true);setMessage('');const original=binding;
    try{if(kind==='lane'||kind==='visit')await load(kind,kind==='lane'?laneId:id,original,c.signal);
      else if(kind==='làn xe'){const value=await api<Page<Lane>>(`/gate-lanes?facilityId=${encodeURIComponent(access.facilityId)}${orgQuery}&page=${page}`,c.signal);if(value.facilityId!==access.facilityId||!Array.isArray(value.items)||value.items.some(x=>x.facilityId!==access.facilityId))throw Error();if(!c.signal.aborted&&current.current===original)setLanes({binding:original,value});}
      else{const value=await api<Page<Visit>>(`/gate-visits?facilityId=${encodeURIComponent(access.facilityId)}${orgQuery}&startUtc=${encodeURIComponent(from)}&endUtc=${encodeURIComponent(until)}&page=${page}`,c.signal);if(value.facilityId!==access.facilityId||value.organizationId!==(access.organizationId||null)||!Array.isArray(value.items)||value.items.some(x=>!permitted(x)))throw Error();if(!c.signal.aborted&&current.current===original)setVisits({binding:original,query:JSON.stringify([from,until]),value});}}
    catch{if(!c.signal.aborted&&current.current===original){setLaneData(null);setVisitData(null);setLanes(null);setVisits(null);setMessage('Không tải được dữ liệu cổng. Kiểm tra quyền truy cập, khoảng giờ UTC và kết nối.');}}
    finally{running.current=false;if(!c.signal.aborted)setBusy(false);}
  }
  async function finish(receipt:{result:{resourceId:string}},original:Pending,signal:AbortSignal){
    const resource=receipt.result?.resourceId;if(!resource||original.resourceId&&resource!==original.resourceId)throw Error();if(signal.aborted)return;
    unresolved.current=null;setPending(null);if(current.current===original.binding){setMessage(`Đã lưu yêu cầu cổng. ID ${original.kind==='lane'?'làn xe':'lịch xe'}: ${resource}`);try{await load(original.kind,resource,original.binding,signal);}catch{if(!signal.aborted)setMessage(`Đã lưu yêu cầu cổng. ID ${original.kind==='lane'?'làn xe':'lịch xe'}: ${resource}. Tải lại trạng thái hiện tại.`);}}
  }
  async function attempt(original:Pending){
    const c=lifecycle.current;if(!c||running.current||original.binding!==binding||!access)return;running.current=true;setBusy(true);unresolved.current=original;setPending(original);setMessage('');
    try{await finish(await command(original.path,original.body,original.key,c.signal,'POST',original.version),original,c.signal);}
    catch(error){if(!c.signal.aborted){if(!original.uncertain&&isDefiniteRejection(error)){unresolved.current=null;setPending(null);if(current.current===original.binding)setMessage('Yêu cầu cổng bị từ chối. Tải dữ liệu hiện tại và kiểm tra khung giờ cùng quyền truy cập.');}else{const unknown={...original,uncertain:true};unresolved.current=unknown;setPending(unknown);if(current.current===original.binding)setMessage('Chưa xác định kết quả thao tác cổng. Giữ yêu cầu gốc và đọc kết quả đã lưu trước khi thay đổi tiếp.');}}}
    finally{running.current=false;if(!c.signal.aborted)setBusy(false);}
  }
  function send(kind:'lane'|'visit',scopeId:string,path:string,body:object,resourceId?:string,version?:string){if(!access||!scopeId||unresolved.current||!reason.trim())return;void attempt({key:crypto.randomUUID(),binding,scopeId,path,body,kind,resourceId,version});}
  async function readCommand(){const c=lifecycle.current;if(!c||running.current||!pending||pending.binding!==binding)return;running.current=true;setBusy(true);try{await finish(await api(`/commands/${pending.key}?scopeId=${pending.scopeId}`,c.signal),pending,c.signal);}catch{if(!c.signal.aborted&&current.current===binding)setMessage('Chưa xác minh được kết quả cổng đã lưu. Giữ yêu cầu gốc để đọc lại hoặc thử lại.');}finally{running.current=false;if(!c.signal.aborted)setBusy(false);}}
  return <section className="workspace-panel" aria-labelledby="gate-title" aria-busy={busy}><div className="panel-heading"><h2 id="gate-title">Lịch xe qua cổng</h2></div><div className="session-content">
    <p>Đặt làn xe, sau đó ghi nhận thời điểm xe đến và rời đi thực tế. Lịch qua cổng không làm thay đổi nhập xuất tồn kho.</p>
    <p>Nhập thời điểm UTC kết thúc bằng Z. Mỗi khung giờ dài 5–240 phút; danh sách cho phép xem tối đa 31 ngày. Xe chưa rời cổng vẫn hiển thị dù nằm ngoài khoảng đã chọn.</p>
    {message&&<p role="status">{message}</p>}
    {pending&&(pending.binding===binding?<div><p>Yêu cầu cổng gốc: {pending.key}</p><button disabled={busy} onClick={()=>void readCommand()}>Đọc kết quả yêu cầu cổng gốc</button><button disabled={busy} onClick={()=>void attempt(pending)}>Thử lại yêu cầu cổng gốc</button></div>:<p>Có yêu cầu cổng chưa rõ kết quả trong không gian làm việc khác. Quay lại không gian đó để đọc kết quả.</p>)}
    <label>Lý do thao tác cổng<input maxLength={500} value={reason} onChange={e=>setReason(e.target.value)} /></label>
    <button disabled={busy||!access?.facilityId} onClick={()=>void read('làn xe')}>Tải danh sách làn xe</button>
    {manager&&<form onSubmit={e=>{e.preventDefault();send('lane',access!.facilityId,'/gate-lanes',{facilityId:access!.facilityId,code,reason});}}><label>Mã làn xe mới<input required maxLength={40} pattern="[A-Z0-9_-]+" value={code} onChange={e=>setCode(e.target.value)} /></label><button disabled={busy||!!pending||!code||!reason.trim()}>Tạo làn xe</button></form>}
    <form onSubmit={e=>{e.preventDefault();void read('lane');}}><label>ID làn xe<input value={laneId} onChange={e=>setLaneId(e.target.value)} /></label><button disabled={busy||!access||!laneId}>Tải làn xe</button></form>
    {lane&&<div><p>ID làn xe: <code>{lane.id}</code> - {lane.code} - {lane.active?'Đang hoạt động':'Đã vô hiệu hóa'}</p>{manager&&<button disabled={busy||!!pending||!reason.trim()} onClick={()=>send('lane',lane.facilityId,`/gate-lanes/${lane.id}/status`,{active:!lane.active,reason},lane.id,lane.version)}>{lane.active?'Vô hiệu hóa làn xe':'Kích hoạt làn xe'}</button>}</div>}
    {laneList&&<div><p>Trang làn xe {laneList.page} - {laneList.total} làn xe</p>{laneList.items.map(x=><p key={x.id}><button disabled={busy} onClick={()=>{setLaneId(x.id);setLaneData({binding,value:x});}}>{x.code} - {x.id} - {x.active?'Đang hoạt động':'Đã vô hiệu hóa'}</button></p>)}<button disabled={busy||laneList.page<=1} onClick={()=>void read('làn xe',laneList.page-1)}>Trang làn xe trước</button><button disabled={busy||laneList.page*50>=laneList.total} onClick={()=>void read('làn xe',laneList.page+1)}>Trang làn xe sau</button></div>}
    <form onSubmit={e=>{e.preventDefault();if(canSchedule)send('visit',access!.organizationId,'/gate-visits',{laneId,leaseId,direction,vehicle,startUtc:start,endUtc:end,reason});}}>
      <label>ID hợp đồng cho lịch xe<input required value={leaseId} onChange={e=>setLeaseId(e.target.value)} /></label><label>Biển số xe<input required maxLength={32} value={vehicle} onChange={e=>setVehicle(e.target.value)} /></label>
      <label>Hướng di chuyển<select value={direction} onChange={e=>setDirection(e.target.value)}><option value="Inbound">Vào kho</option><option value="Outbound">Ra kho</option></select></label>
      <label>Bắt đầu lịch xe (UTC)<input required placeholder="2026-11-01T09:00:00Z" value={start} onChange={e=>setStart(e.target.value)} /></label><label>Kết thúc lịch xe (UTC)<input required placeholder="2026-11-01T09:30:00Z" value={end} onChange={e=>setEnd(e.target.value)} /></label>
      {!canSchedule&&<p>Chọn không gian khách hàng để đặt lịch xe cho hợp đồng của khách.</p>}<button disabled={busy||!!pending||!canSchedule||!laneId||!leaseId||!vehicle||!start||!end||!reason.trim()}>Đặt lịch xe qua cổng</button>
    </form>
    <form onSubmit={e=>{e.preventDefault();void read('visit');}}><label>ID lịch xe qua cổng<input value={id} onChange={e=>setId(e.target.value)} /></label><button disabled={busy||!access||!id}>Tải lịch xe qua cổng</button></form>
    {visit&&<div className="booking-detail"><p>ID lịch xe: <code>{visit.id}</code></p><p>Trạng thái lịch xe: {displayValue(visit.state)}</p><p>{visit.vehicle} - {displayValue(visit.direction)}</p><p>Dự kiến: {visit.startUtc} đến {visit.endUtc}</p>{visit.arrivedAt&&<p>Đã đến: {visit.arrivedAt}</p>}{visit.departedAt&&<p>Đã rời đi: {visit.departedAt}</p>}
      {visit.state==='Scheduled'&&<button disabled={busy||!!pending||!reason.trim()} onClick={()=>send('visit',visit.organizationId,`/gate-visits/${visit.id}/cancel`,{reason},visit.id,visit.version)}>Hủy lịch xe qua cổng</button>}
      {staff&&visit.state==='Scheduled'&&<button disabled={busy||!!pending||!reason.trim()} onClick={()=>send('visit',visit.organizationId,`/gate-visits/${visit.id}/arrive`,{reason},visit.id,visit.version)}>Ghi nhận xe đến</button>}
      {staff&&visit.state==='CheckedIn'&&<button disabled={busy||!!pending||!reason.trim()} onClick={()=>send('visit',visit.organizationId,`/gate-visits/${visit.id}/depart`,{reason},visit.id,visit.version)}>Ghi nhận xe rời đi</button>}
    </div>}
    <form onSubmit={e=>{e.preventDefault();void read('lượt xe');}}><label>Đầu khoảng xem lịch xe (UTC)<input required value={from} onChange={e=>setFrom(e.target.value)} /></label><label>Cuối khoảng xem lịch xe (UTC)<input required value={until} onChange={e=>setUntil(e.target.value)} /></label><button disabled={busy||!access||!from||!until}>Tải danh sách lịch xe</button></form>
    {visitList&&<div><p>Trang lịch xe {visitList.page} - {visitList.total} lượt xe</p>{visitList.items.map(x=><p key={x.id}><button disabled={busy} onClick={()=>{setId(x.id);setVisitData({binding,value:x});}}>{x.id} - {x.vehicle} - {displayValue(x.state)}</button></p>)}<button disabled={busy||visitList.page<=1} onClick={()=>void read('lượt xe',visitList.page-1)}>Trang lịch xe trước</button><button disabled={busy||visitList.page*50>=visitList.total} onClick={()=>void read('lượt xe',visitList.page+1)}>Trang lịch xe sau</button></div>}
  </div></section>;
}
