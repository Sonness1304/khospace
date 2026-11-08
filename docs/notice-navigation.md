# Open related records from notices

KhoSpace notices describe historical events, not the current authorization or business state of a record.

1. Select the intended customer organization and facility, then open Overview.
2. Load in-app notices. Lease-ending events with a valid record ID offer **Mở hợp đồng liên quan**; stock-action events offer **Mở yêu cầu nhập / xuất liên quan**.
3. The action opens the existing workspace, fills the record ID and clears previously displayed detail. Select **Tải hợp đồng thuê** or **Tải yêu cầu** to retrieve the current record using your current session and permissions.
4. A failed or forbidden read does not restore old detail. Review the current record before taking any separate business action.

Opening a notice does not create a hold, renew/terminate a lease, approve/post stock, send a message or mark the event complete. Receipt/command recovery remains independent: navigating to another notice does not discard a pending uncertain command or change its idempotency key, payload, version or original resource.

Successful recovery settles the original command without replacing the newly selected record or clearing its verified detail. Automatic post-command refresh is limited to the unchanged original view intent, including intentional selection of a newly created request in that same view. Reopening a notice, even for the same ID, requires the explicit current-state read again.

Only the structured kind and canonical nonempty GUID determine the target. Message text is never interpreted as a URL or command. Unknown kinds, missing/malformed IDs and receivable reminders remain informational; a receivable reminder references a statement, not a lease or account. Reopening the same notice creates a fresh read intent, so historical detail is not silently reused. Changing user, customer organization or facility invalidates the selected record context.

This local feature adds no backend endpoint, schema, dependency, scheduled job, email transport or live-payment activation. Google login, owner-session browser acceptance and production deployment remain separate checks.
