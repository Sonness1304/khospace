import SePayPayment, { type PaymentIntent } from './SePayPayment';
import { displayValue } from '../../locale/vi';
import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { api, command, isDefiniteRejection } from '../../api';
import type { WorkspaceAccess } from '../auth/workspace';
type Method = { method: string; mode: string; available: boolean };
type Intent = PaymentIntent;
type Pending = { key: string; binding: string; organizationId: string; path: string; body: object; resourceId?: string; version?: string; uncertain?: boolean };
const names: Record<string,string> = { VnpayCard: 'Visa / thẻ quốc tế qua VNPAY', VnpayBank: 'Ngân hàng nội địa qua VNPAY', VnpayQr: 'Mã QR ngân hàng qua VNPAY', MomoWallet: 'Ví MoMo', SePayQr: 'Chuyển khoản QR (SePay)' };
function hosted(item: Intent) {
  try { const u = new URL(item.checkoutUrl ?? ''); if (u.protocol !== 'https:' || u.username || u.password || u.port || u.hash || !['Sandbox','Live'].includes(item.mode)) return false;
    return item.provider === 'Vnpay' ? u.hostname === (item.mode === 'Live' ? 'pay.vnpay.vn' : 'sandbox.vnpayment.vn') && u.pathname === (item.mode === 'Live' ? '/vpcpay.html' : '/paymentv2/vpcpay.html') : item.provider === 'Momo' && u.hostname === (item.mode === 'Live' ? 'payment.momo.vn' : 'test-payment.momo.vn') && u.pathname === '/v2/gateway/pay';
  } catch { return false; }
}
export default function OnlinePaymentPanel({ access }: { access: WorkspaceAccess | null }) {
  const binding = JSON.stringify([access?.userId,access?.organizationId,access?.facilityId,access?.facilityRoles,access?.operatorAdmin,access?.organizationRole]);
  const current = useRef(binding); const epoch=useRef(0); const reading=useRef<AbortController|null>(null); useLayoutEffect(()=>{current.current=binding;epoch.current+=1;reading.current?.abort();},[binding]);
  const [detailRevision,setDetailRevision]=useState(0);
  const [account,setAccount]=useState(''); const [id,setId]=useState(''); const [amount,setAmount]=useState(''); const [reason,setReason]=useState(''); const [method,setMethod]=useState('VnpayCard');
  const [methods,setMethods]=useState<{binding:string;items:Method[]}|null>(null); const [data,setData]=useState<{binding:string;value:Intent}|null>(null);
  const [history,setHistory]=useState<{binding:string;account:string;page:number;total:number;items:Intent[]}|null>(null);
  const [pending,setPending]=useState<Pending|null>(null); const unresolved=useRef<Pending|null>(null); const [message,setMessage]=useState(''); const [busy,setBusy]=useState(false);
  const [draftBinding,setDraftBinding]=useState(binding); if(draftBinding!==binding){setDraftBinding(binding);setAccount('');setId('');setAmount('');setReason('');setMessage('');}
  const running=useRef(false); const lifecycle=useRef<AbortController|null>(null);
  useEffect(()=>{const c=new AbortController();lifecycle.current=c;return()=>{c.abort();reading.current?.abort();};},[]);
  const item=data?.binding===binding&&data.value.id===id?data.value:null; const list=history?.binding===binding&&history.account===account?history:null;
  const choices=methods?.binding===binding?methods.items:[]; const chosen=choices.find(x=>x.method===method); const customer=!!access?.organizationId;
  const allowed=(value:Pick<Intent,'organizationId'|'facilityId'>)=>!!access&&(value.organizationId===access.organizationId||access.operatorAdmin||value.facilityId===access.facilityId&&access.facilityRoles.includes('Finance'));
  function edit(setter:(value:string)=>void,value:string){epoch.current+=1;reading.current?.abort();setMessage('');setData(previous=>previous?.value.provider==='SePay'?null:previous);setter(value);}
  async function load(target:string,original:string,signal:AbortSignal,generation=epoch.current) {
    const value=await api<Intent>(`/payment-intents/${encodeURIComponent(target)}`,signal);
    if(value.id!==target||!value.rentalAccountId||!allowed(value)||!value.state||!value.mode)throw Error('Chưa xác minh ngữ cảnh thanh toán');
    if(!signal.aborted&&current.current===original&&epoch.current===generation){setId(target);setData({binding:original,value});setDetailRevision(value=>value+1);}
  }
  async function read(kind:'methods'|'intent'|'list',page=1){
    const c=lifecycle.current;if(!c||running.current||!access)return;running.current=true;setBusy(true);setMessage('');const original=binding;const generation=epoch.current;const readController=new AbortController();reading.current=readController;const signal=AbortSignal.any([c.signal,readController.signal]);
    try{if(kind==='methods'){const result=await api<{items:Method[]}>('/payment-methods',signal);if(!Array.isArray(result.items))throw Error();if(!signal.aborted&&current.current===original&&epoch.current===generation)setMethods({binding:original,items:result.items});}
      else if(kind==='intent')await load(id,original,signal,generation);
      else{const result=await api<{organizationId:string;facilityId:string;items:Intent[];total:number}>(`/accounts/${encodeURIComponent(account)}/payment-intents?page=${page}`,signal);if(!Array.isArray(result.items)||!allowed(result)||result.items.some(x=>x.rentalAccountId!==account||!allowed(x)))throw Error();if(!signal.aborted&&current.current===original&&epoch.current===generation)setHistory({...result,binding:original,account,page});}}
    catch{if(!signal.aborted&&current.current===original&&epoch.current===generation){setData(null);setHistory(null);setMessage('Không tải được dữ liệu thanh toán trực tuyến. Kiểm tra quyền truy cập và kết nối hiện tại.');}}
    finally{running.current=false;if(!c.signal.aborted)setBusy(false);}
  }
  async function finish(receipt:{result:{resourceId:string}},original:Pending,signal:AbortSignal,generation:number){
    const resource=receipt.result?.resourceId;if(!resource||original.resourceId&&resource!==original.resourceId)throw Error('Chưa xác minh kết quả thanh toán');
    if(signal.aborted)return;unresolved.current=null;setPending(null);
    if(current.current===original.binding&&generation===epoch.current){setMessage('Đã lưu yêu cầu thanh toán. Tải trạng thái hiện tại trước khi thanh toán tiếp.');try{const controller=new AbortController();reading.current=controller;await load(resource,original.binding,AbortSignal.any([signal,controller.signal]),generation);}catch{if(!signal.aborted&&current.current===original.binding&&generation===epoch.current)setMessage(`Đã lưu yêu cầu thanh toán. ID thanh toán: ${resource}. Chưa đọc lại được dữ liệu hiện tại.`);}}
  }
  async function attempt(original:Pending){
    const c=lifecycle.current;if(!c||running.current||original.binding!==binding||!customer)return;running.current=true;setBusy(true);unresolved.current=original;setPending(original);setMessage('');const generation=epoch.current;
    try{await finish(await command(original.path,original.body,original.key,c.signal,'POST',original.version),original,c.signal,generation);}
    catch(error){if(!c.signal.aborted){if(!original.uncertain&&isDefiniteRejection(error)){unresolved.current=null;setPending(null);if(current.current===original.binding&&generation===epoch.current)setMessage('Yêu cầu thanh toán trực tuyến bị từ chối. Kiểm tra cấu hình, tài khoản và số tiền.');}else{const unknown={...original,uncertain:true};unresolved.current=unknown;setPending(unknown);if(current.current===original.binding&&generation===epoch.current)setMessage('Chưa xác định kết quả thanh toán trực tuyến. Giữ yêu cầu gốc và đọc kết quả trước khi thanh toán khác.');}}}
    finally{running.current=false;if(!c.signal.aborted)setBusy(false);}
  }
  function send(path:string,body:object,resourceId?:string,version?:string){if(!access?.organizationId||unresolved.current||!reason.trim())return;void attempt({key:crypto.randomUUID(),binding,organizationId:access.organizationId,path,body,resourceId,version});}
  async function readCommand(){const c=lifecycle.current;if(!c||running.current||!pending||pending.binding!==binding||!customer)return;running.current=true;setBusy(true);const generation=epoch.current;try{await finish(await api(`/commands/${pending.key}?scopeId=${pending.organizationId}`,c.signal),pending,c.signal,generation);}catch{if(!c.signal.aborted&&current.current===binding&&generation===epoch.current)setMessage('Chưa xác minh được kết quả thanh toán đã lưu. Giữ yêu cầu gốc; chỉ thử lại trong không gian ban đầu.');}finally{running.current=false;if(!c.signal.aborted)setBusy(false);}}
  return <section className="workspace-panel" aria-labelledby="online-payment-title" aria-busy={busy}><div className="panel-heading"><h2 id="online-payment-title">Thanh toán trực tuyến</h2></div><div className="session-content">
    <p>Thanh toán trên trang của nhà cung cấp. WarehouseRent không yêu cầu số thẻ, CVV hay OTP. Thẻ và ngân hàng khả dụng phụ thuộc phê duyệt của nhà cung cấp. Phiếu thu qua VNPAY/MoMo đã ghi vẫn cần tài chính xác minh. Tiền chưa được phân bổ tự động vào tiền cọc hoặc tiền thuê.</p>
    <p>Thanh toán thử nghiệm không tạo ra tiền có thể sử dụng. Trang quay về trên trình duyệt không xác nhận đã thanh toán.</p>
    {message&&<p role="status">{message}</p>}
    {pending&&<div><p>Yêu cầu thanh toán gốc: {pending.key}</p><button disabled={busy||pending.binding!==binding||!customer} onClick={()=>void readCommand()}>Đọc kết quả yêu cầu thanh toán gốc</button><button disabled={busy||pending.binding!==binding||!customer} onClick={()=>void attempt(pending)}>Thử lại yêu cầu thanh toán gốc</button></div>}
    <button disabled={busy||!access} onClick={()=>void read('methods')}>Tải phương thức thanh toán</button>
    <label>ID tài khoản thuê thanh toán trực tuyến<input value={account} onChange={e=>edit(setAccount,e.target.value)} /></label>
    <button disabled={busy||!access||!account} onClick={()=>void read('list')}>Tải thanh toán của tài khoản</button>
    {customer&&<><label>Lý do thanh toán trực tuyến<input maxLength={500} value={reason} onChange={e=>edit(setReason,e.target.value)} /></label><form onSubmit={e=>{e.preventDefault();send('/payment-intents',{rentalAccountId:account,method,amount,reason});}}>
      <label>Phương thức trực tuyến<select value={method} onChange={e=>edit(setMethod,e.target.value)}>{choices.length?choices.map(x=><option key={x.method} value={x.method} disabled={!x.available}>{names[x.method]??'Phương thức chưa xác định'} · {displayValue(x.mode)}</option>):<option value="VnpayCard">Tải phương thức trước</option>}</select></label>
      <label>Số tiền thanh toán trực tuyến (VND)<input required inputMode="numeric" pattern="[0-9]+" value={amount} onChange={e=>edit(setAmount,e.target.value)} /></label><p>Chỉ nạp tiền vào tài khoản. Nhờ bộ phận tài chính phân bổ riêng cho đặt cọc hoặc tiền thuê.</p>
      <button disabled={busy||!!pending||!chosen?.available||!account||!reason.trim()}>Tạo thanh toán trực tuyến</button></form></>}
    <form onSubmit={e=>{e.preventDefault();void read('intent');}}><label>ID thanh toán trực tuyến<input required value={id} onChange={e=>edit(setId,e.target.value)} /></label><button disabled={busy||!access||!id}>Tải thanh toán trực tuyến</button></form>
    {item?.provider==='SePay'&&<SePayPayment key={`${binding}:${detailRevision}:${item.id}`} intent={item} account={account} />}
    {item&&item.provider!=='SePay'&&<div className="booking-detail"><p>ID thanh toán: <code>{item.id}</code></p><p>Trạng thái thanh toán: {displayValue(item.state)}</p><p>{names[item.method]??'Phương thức chưa xác định'} · {displayValue(item.mode)} · {item.amount} VND</p>
      {item.state==='Created'&&customer&&<button disabled={busy||!!pending||!reason.trim()} onClick={()=>send(`/payment-intents/${item.id}/checkout`,{reason},item.id)}>Chuẩn bị trang thanh toán</button>}
      {item.state==='Created'&&customer&&<button disabled={busy||!!pending||!reason.trim()} onClick={()=>send(`/payment-intents/${item.id}/abandon`,{reason},item.id,item.version)}>Hủy thanh toán chưa gửi</button>}
      {item.state==='Ready'&&hosted(item)&&<a href={item.checkoutUrl} rel="noopener noreferrer" target="_blank">Mở trang thanh toán bảo mật</a>}
      {['Creating','Unknown'].includes(item.state)&&<p>Chưa xác định kết quả từ nhà cung cấp. Không tạo thanh toán thay thế; yêu cầu tài chính đối soát ID này với nhà cung cấp.</p>}
      {item.state==='ReviewRequired'&&<p>Tiền đến muộn cần tài chính đối soát ngoài tài khoản đã đóng này. Chưa xác nhận phiếu thu hoặc hoàn tiền.</p>}
      {item.paymentReceiptId&&<p>ID phiếu thu đã ghi: {item.paymentReceiptId}</p>}
    </div>}
    {list&&<div><p>Trang thanh toán trực tuyến {list.page} · {list.total} thanh toán</p>{list.items.map(x=><p key={x.id}><button disabled={busy} onClick={()=>{epoch.current+=1;reading.current?.abort();setId(x.id);setData({binding,value:x});setDetailRevision(value=>value+1);}}>{x.id} · {names[x.method]??'Phương thức chưa xác định'} · {displayValue(x.mode)} · {displayValue(x.state)} · {x.amount} VND</button></p>)}<button disabled={busy||list.page<=1} onClick={()=>void read('list',list.page-1)}>Trang thanh toán trực tuyến trước</button><button disabled={busy||list.page*50>=list.total} onClick={()=>void read('list',list.page+1)}>Trang thanh toán trực tuyến sau</button></div>}
  </div></section>;
}
