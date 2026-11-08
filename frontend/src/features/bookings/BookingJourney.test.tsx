import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import FacilitiesPanel from '../facilities/FacilitiesPanel';
const access={userId:'qa-customer',organizationId:'org-qa',organizationRole:'CustomerAdmin',facilityId:'',facilityRoles:[],operatorAdmin:false};
const reply=(body:unknown,status=200)=>({ok:status>=200&&status<300,status,json:async()=>body});
afterEach(()=>vi.unstubAllGlobals());
function setup(holdStatus=200){
 let dates={startDate:'2028-01-01',endDate:'2028-01-03'};let state='Held';
 const booking=()=>({id:'booking-qa',quoteId:'quote-qa',organizationId:'org-qa',facilityId:'facility-qa',...dates,expiresAt:'2028-01-01T00:15:00Z',state,version:'booking-v1',rentalAccountId:'account-qa',requiredDeposit:'400',refundNeeded:false});
 const requests=vi.fn(async(url:string,options:RequestInit={})=>{
  if(url.endsWith('/public/facilities'))return reply({items:[{id:'facility-qa',name:'Synthetic QA warehouse',address:'Fixture only',permittedGoods:'Dry goods'}],nextCursor:null});
  if(url.includes('/availability?'))return reply({items:[{id:'unit-qa',code:'QA-01',areaM2:'10',dailyRate:'100',currency:'VND'}],nextCursor:null,reservation:false});
  if(url.endsWith('/auth/csrf'))return reply({token:'fixture-csrf'});
  if(url.endsWith('/quotes')&&options?.method==='POST'){dates=JSON.parse(options.body as string);return reply({result:{resourceId:'quote-qa'}});}
  if(url.endsWith('/quotes/quote-qa'))return reply({id:'quote-qa',organizationId:'org-qa',facilityId:'facility-qa',...dates,expiresAt:'2028-01-01T00:15:00Z',version:'quote-v1',rentalSubtotal:'2000',requiredDeposit:'400',currency:'VND',reservation:false,lines:[{id:'line-qa',unitCode:'QA-01',...dates,days:2,areaM2:'10',dailyRate:'100',billingBasis:'Daily',amount:'2000'}]});
  if(url.endsWith('/quotes/quote-qa/holds'))return reply({result:{resourceId:'booking-qa'}},holdStatus);
  if(url.includes('/commands/'))return reply({result:{resourceId:'booking-qa'}});
  if(url.endsWith('/bookings/booking-qa/cancel')){state='Cancelled';return reply({result:{resourceId:'booking-qa'}});}
  if(url.endsWith('/bookings/booking-qa'))return reply(booking());
  throw Error('Unexpected synthetic fixture route: '+url);
 });
 vi.stubGlobal('fetch',requests);return requests;
}
async function quoteSelection(){
 const user=userEvent.setup();render(<FacilitiesPanel access={access}/>);
 await user.selectOptions(await screen.findByLabelText('Cơ sở kho'),'facility-qa');
 await user.click(screen.getByRole('button',{name:'Tìm khoang còn trống'}));
 await user.click(await screen.findByRole('checkbox',{name:'QA-01'}));
 await user.click(screen.getByRole('button',{name:'Nhận báo giá chính xác'}));
 await screen.findByText('2000 VND');return user;
}
it('fixture journey: search -> explicit quote -> hold -> cancel keeps server money state pending',async()=>{
 const requests=setup();const user=await quoteSelection();
 expect(requests.mock.calls.some(([url])=>url.endsWith('/holds'))).toBe(false);
 await user.click(screen.getByRole('button',{name:'Giữ các khoang đã chọn trong 15 phút'}));
 await screen.findByText('Đang giữ chỗ');
 expect(screen.getByText(/Trạng thái đặt cọc: Đang chờ/)).toBeInTheDocument();
 expect(screen.queryByRole('button',{name:'Xác nhận đặt chỗ'})).not.toBeInTheDocument();
 await user.type(screen.getByLabelText('Lý do hủy'),'Synthetic cancellation, no real reservation');
 await user.click(screen.getByRole('button',{name:'Hủy thuê'}));await screen.findByText('Đã hủy');
 const posts=requests.mock.calls.filter(([,options])=>options?.method==='POST');
 expect(posts.map(([url])=>url)).toEqual(['/api/v1/quotes','/api/v1/quotes/quote-qa/holds','/api/v1/bookings/booking-qa/cancel']);
 for(const [,options] of posts){expect(options?.headers).toMatchObject({'X-CSRF-Token':'fixture-csrf','Idempotency-Key':expect.any(String)});}
 expect(JSON.parse(posts[0][1]!.body as string)).toMatchObject({organizationId:'org-qa',facilityId:'facility-qa',unitIds:['unit-qa']});
 expect(JSON.parse(posts[1][1]!.body as string)).toEqual({expectedQuoteVersion:'quote-v1'});
 expect(posts[2][1]?.headers).toMatchObject({'If-Match':'"booking-v1"'});
});
it('fixture journey: conflicting capacity never displays a successful hold',async()=>{
 const requests=setup(409);const user=await quoteSelection();
 await user.click(screen.getByRole('button',{name:'Giữ các khoang đã chọn trong 15 phút'}));
 await screen.findByText(/Yêu cầu thuê bị từ chối/);
 expect(screen.queryByText('Đang giữ chỗ')).not.toBeInTheDocument();
 expect(requests.mock.calls.filter(([url])=>url.endsWith('/holds'))).toHaveLength(1);
});
it('fixture journey: unknown hold is resolved by readback with no second POST',async()=>{
 const requests=setup(504);const user=await quoteSelection();
 await user.click(screen.getByRole('button',{name:'Giữ các khoang đã chọn trong 15 phút'}));
 await screen.findByText(/Chưa xác định kết quả yêu cầu thuê/);
 expect(screen.getByRole('button',{name:'Giữ các khoang đã chọn trong 15 phút'})).toBeDisabled();
 await user.click(screen.getByRole('button',{name:'Đọc kết quả yêu cầu thuê'}));
 await screen.findByText('Đang giữ chỗ');
 await waitFor(()=>expect(requests.mock.calls.filter(([url])=>url.endsWith('/holds'))).toHaveLength(1));
 expect(requests.mock.calls.find(([url])=>url.includes('/commands/'))?.[0]).toContain('scopeId=org-qa');
});
