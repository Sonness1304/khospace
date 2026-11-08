import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import SessionPanel from './SessionPanel';
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
function signedOut(status = 401) {
 const fetchMock = vi.fn(async (url: string) => url.endsWith('/providers') ? reply({ google: false }) : url.endsWith('/csrf') ? reply({ token: 'fixture-csrf' }) : url.endsWith('/login') ? reply({}, status) : reply({}, 401));
 vi.stubGlobal('fetch', fetchMock); return fetchMock;
}
afterEach(() => { vi.unstubAllGlobals(); window.history.replaceState(null, '', '/'); });
async function mismatch() {
 const user=userEvent.setup(); render(<SessionPanel />);
 await user.click(await screen.findByRole('tab',{name:'Đăng ký'}));
 await user.type(screen.getByLabelText('Tên cá nhân hoặc doanh nghiệp'),'Synthetic QA');
 await user.type(screen.getByLabelText('Email'),'fixture@example.test');
 await user.type(screen.getByLabelText('Mật khẩu'),'SyntheticPass!42');
 await user.type(screen.getByLabelText('Nhập lại mật khẩu'),'OtherSynthetic!42');
 await user.click(screen.getByRole('button',{name:'Tạo tài khoản'})); return user;
}
it('associates mismatched confirmation with its alert and focuses the field without any POST',async()=>{
 const fetchMock=signedOut(); await mismatch();
 const field=screen.getByLabelText('Nhập lại mật khẩu');
 expect(field).toHaveAttribute('aria-invalid','true');
 expect(field).toHaveAccessibleDescription('Mật khẩu nhập lại chưa khớp.');
 expect(screen.getByRole('alert')).toHaveTextContent('Mật khẩu nhập lại chưa khớp.');
 await waitFor(()=>expect(field).toHaveFocus());
 expect(fetchMock.mock.calls.some(([url])=>url.endsWith('/register')||url.endsWith('/csrf'))).toBe(false);
});
it('clears obsolete mismatch on confirmation edit and can focus a repeated mismatch again',async()=>{
 signedOut(); const user=await mismatch(); const field=screen.getByLabelText('Nhập lại mật khẩu');
 await user.clear(field);
 expect(field).not.toHaveAttribute('aria-invalid','true'); expect(screen.queryByRole('alert')).not.toBeInTheDocument();
 await user.type(field,'StillDifferent!42'); await user.click(screen.getByRole('button',{name:'Tạo tài khoản'}));
 await waitFor(()=>expect(field).toHaveFocus()); expect(field).toHaveAttribute('aria-invalid','true');
});
it('clears mismatch when original password changes and when switching modes',async()=>{
 signedOut(); const user=await mismatch();
 await user.type(screen.getByLabelText('Mật khẩu'),'X');
 expect(screen.getByLabelText('Nhập lại mật khẩu')).not.toHaveAttribute('aria-invalid','true');
 expect(screen.queryByRole('alert')).not.toBeInTheDocument();
 await user.click(screen.getByRole('tab',{name:'Đăng nhập'}));
 expect(screen.getByRole('tabpanel')).not.toHaveAttribute('aria-describedby');
 expect(screen.getByLabelText('Mật khẩu')).toHaveValue('');
});
it.each([401,429])('focuses a form-level rejection summary for HTTP%s without guessing the wrong field',async status=>{
 const fetchMock=signedOut(status); const user=userEvent.setup();render(<SessionPanel />);
 await user.type(await screen.findByLabelText('Email'),'fixture@example.test');
 await user.type(screen.getByLabelText('Mật khẩu'),'SyntheticPass!42');
 await user.click(screen.getByRole('button',{name:'Đăng nhập'}));
 const alert=await screen.findByRole('alert'); await waitFor(()=>expect(alert).toHaveFocus());
 expect(screen.getByRole('tabpanel')).toHaveAttribute('aria-describedby',alert.id);
 expect(screen.getByLabelText('Email')).not.toHaveAttribute('aria-invalid','true');
 expect(screen.getByLabelText('Mật khẩu')).toHaveValue('');
 expect(fetchMock.mock.calls.filter(([url])=>url.endsWith('/login'))).toHaveLength(1);
});
it('does not focus passive OAuth guidance on initial page load',async()=>{
 window.history.replaceState(null,'','/?authError=google-cancelled#account');signedOut();render(<SessionPanel />);
 const email=await screen.findByLabelText('Email');email.focus();
 expect(screen.getByRole('status')).toHaveTextContent('Bạn đã dừng đăng nhập Google.');
 expect(email).toHaveFocus(); expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});
