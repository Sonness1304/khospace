# Online payments — P2.2

WarehouseRent supports hosted VNPAY international-card, domestic-bank and QR checkout, plus hosted MoMo wallet checkout. Cash/BankTransfer/Other manual receipts remain available. Customers enter payment details only on the provider's page. WarehouseRent does not collect PAN, CVV, banking passwords or OTPs.

**Delivery state:** source integration and synthetic tests. Merchant onboarding, provider sandbox certification, live settlement, public HTTPS callbacks and browser acceptance are not verified. No real gateway credentials or money were used in development. Your personal Visa card, MoMo wallet or VietinBank account is a payer/recipient account; accepting payments automatically additionally requires a merchant relationship and approved methods. Individual issuer/card acceptance and settlement to a particular bank must be confirmed with the provider.

## Customer workflow

1. Sign in and select your customer organization. Open **Finance → Online payments**.
2. Select **Read online methods**. Disabled means the operator has not configured that gateway. Each available method explicitly shows Sandbox or Live.
3. Enter the existing rental account ID, a positive whole-VND amount between1,000 and50,000,000, method and reason. The amount is an explicit account top-up, not a calculated invoice balance. Check existing unapplied funds before paying again.
4. Select **Create online payment**. Keep its displayed Payment ID. This saves a durable local intent; it does not yet contact the provider.
5. Read that payment, enter a reason and select **Prepare hosted checkout**. Then choose **Open secure hosted checkout**. Complete payment on the provider page. VNPAY options request international card, domestic banking or QR; merchant approval controls actual availability.
6. On return, read the Payment ID again. The return page never confirms money. Only a verified provider notification updates the intent.
7. A Live successful notification creates one **Recorded** receipt. Give its displayed receipt ID to Finance for existing verification and separate deposit/rent allocation. Payment does not extend a15-minute hold, confirm a booking, release stock or increase capacity. Late money on a cancelled/expired booking remains a reconciliation/refund concern under the existing workflow.

Sandbox **Paid** is synthetic evidence only: it creates no PaymentReceipt, no deposit and no charge allocation. Sandbox success cannot pay a real rental balance. There is no automatic refund, subscription, saved card, tokenization or payout feature in this slice.

## Outcomes and recovery

| State | Meaning / next action |
| --- | --- |
| Created | Saved locally, no provider dispatch. Prepare checkout or abandon the unsent payment with the current version. |
| Creating | Dispatch claim committed. A request may be in flight or interrupted. Read later; do not create a replacement. |
| Ready | A validated hosted URL is available. Payment still awaits a signed notification. |
| Unknown | Provider create response was lost, invalid or unavailable. No second create is sent for this intent. Contact Finance with its ID. |
| Failed | A signed failure or explicit abandonment before dispatch. No funds are recorded. A later signed success is still reconciled. |
| Paid | Verified provider success; Sandbox creates no money, Live creates a Recorded receipt requiring Finance verification. |
| ReviewRequired | Valid late success arrived after the account permanently closed. Transaction identity is retained; account is not reopened and no receipt/refund is fabricated. Finance must investigate and reconcile/refund with the provider outside this closed account. |

Created/Creating/Ready/Unknown/ReviewRequired block permanent financial closure. Only **Created** can be abandoned locally; an attempted checkout cannot be declared unpaid merely because a browser tab closed or link expired. Unknown/pending orders require provider investigation and authenticated notification replay. Automated provider-query reconciliation and cancellation/refund APIs are not implemented; accepting this operational limitation is a live-launch prerequisite. Never force status with SQL or issue a new charge to resolve an unknown one.

If a session command response is lost, the panel retains its exact key/body/workspace (and version for unsent abandonment). Use **Read original payment command** or **Retry original payment command** in the original workspace. Retries never resend a claimed provider create. A short browser timeout can end before a bounded provider call finishes; read the saved intent later. State stays Creating on process interruption, preserving the unknown outcome. Record the displayed command/intent IDs before reload; pending UI state is in memory. A lost create receipt can be recovered by the original scoped command key or account payment history.

Finance can read an account's paginated intents and individual details at their assigned facility, including ReviewRequired outcomes, but does not impersonate a customer to create checkout. Online provider transaction IDs should not be recorded a second time as a manual BankTransfer receipt. Use the existing provider-created receipt ID and check its amount/reference against the merchant dashboard before verifying.

## Merchant setup — owner-run prerequisites

Register/verify a merchant account with [VNPAY](https://sandbox.vnpayment.vn/apis/docs/huong-dan-tich-hop/) and [MoMo for Business](https://business.momo.vn/). Agree the accepted methods, receiving bank, fees, refunds and provider certification directly with them. Do not assume a personal wallet is an API merchant account or that the listed methods are automatically approved.

Use server process environment variables or an appropriate hosting secret store. Do not paste secrets into chat, source, `.env.example`, tracked JSON, browser code, screenshots or shell history. This project does not store them in the application database. Configure the following names privately:

| Setting / environment variable | Value |
| --- | --- |
| `Payments__PublicOrigin` | Your public HTTPS origin only, e.g. `https://warehouse.example.com`, default443, no path/query/userinfo |
| `Payments__Vnpay__Mode` | `Disabled` (default), `Sandbox` or `Live` |
| `Payments__Vnpay__MerchantId` | Merchant TmnCode issued by VNPAY |
| `Payments__Vnpay__Secret` | VNPAY HashSecret |
| `Payments__Momo__Mode` | `Disabled` (default), `Sandbox` or `Live` |
| `Payments__Momo__MerchantId` | MoMo PartnerCode |
| `Payments__Momo__AccessKey` | MoMo AccessKey |
| `Payments__Momo__Secret` | MoMo SecretKey |
| `Payments__AllowLive` | Must be exactly `true` for Live; API host environment must also be `Production` |

Missing/invalid configuration fails closed. Sandbox works with an explicitly chosen demo/test environment and sandbox merchant credentials. Live requires a separately deployed Production runtime and schema; the repository's demo-only migration script does not authorize or support an owner production migration. Use a reviewed production migration/deployment procedure separately. No server, public tunnel or deployment was started for this delivery.

Provider endpoints are fixed HTTPS URLs; do not proxy them through an arbitrary host. Sandbox VNPAY uses `https://sandbox.vnpayment.vn/paymentv2/vpcpay.html`; Live uses `https://pay.vnpay.vn/vpcpay.html`. MoMo uses `https://test-payment.momo.vn/v2/gateway/api/create` or Live `https://payment.momo.vn/v2/gateway/api/create`. Validate these endpoints against your issued merchant documentation before go-live. HTTP redirects from API creation are disabled; hosted URLs must match the correct provider/mode host/path.

Provide these exact URLs to the providers (replace only the example origin):

- Browser return: `https://warehouse.example.com/payments/return`.
- VNPAY GET IPN: `https://warehouse.example.com/api/payment-notifications/vnpay`.
- MoMo POST IPN: `https://warehouse.example.com/api/payment-notifications/momo`.

Only these callback endpoints bypass session authentication/CSRF; signatures are mandatory. All authenticated write endpoints retain CSRF. Notifications are bounded to16KiB, duplicate/ambiguous fields rejected, and constant-time HMAC validation precedes ledger effects. Reverse proxy/body/time limits, public availability, secrets/key rotation, DataProtection, monitoring and provider retries need a separate deployment acceptance. Do not log raw callback bodies, hosted signed URLs or secrets at the proxy. Drain/reconcile pending intents before changing merchant IDs, mode or keys; notifications are accepted only when current config matches the intent's stored provider/mode/merchant.

## API and accounting

| Method | Path under `/api/v1` | Purpose |
| --- | --- | --- |
| GET | `/payment-methods` | Availability/mode only, no secrets |
| POST | `/payment-intents` | `{rentalAccountId,method,amount,reason}`, UUID Idempotency-Key |
| GET | `/payment-intents/{id}` | Scoped status, checkout URL when Ready, receipt ID |
| GET | `/accounts/{id}/payment-intents?page=1` | Scoped50-row account history |
| POST | `/payment-intents/{id}/checkout` | `{reason}` and original UUID key; dispatch at most once |
| POST | `/payment-intents/{id}/abandon` | Created only, `{reason}`, UUID key and If-Match rowversion |

Supported method IDs: `VnpayCard`, `VnpayBank`, `VnpayQr`, `MomoWallet`. Account/org/facility/mode/provider identities are computed on the server and preserved. Provider success is not a customer's authorization to allocate deposit or settle an invoice. Existing receipt verification and ledger constraints continue to apply.

Migration `20261029054446_OnlinePaymentIntents` adds immutable payment identities, unique provider/mode/merchant transaction identity and receipt relationship. Populated downgrade refuses to erase evidence. Recovery rehearsal includes intent binding/mode/amount/state/transaction and receipt relationship; it restores only a GUID test database. Raw payer/card/notification payloads and merchant secrets are not part of that projection.

## Provider sources and acceptance

Sources checked October29,2026: [VNPAY PAY2.1](https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html), [MoMo one-time wallet checkout](https://developers.momo.vn/v3/docs/payment/api/wallet/onetime/), [MoMo payment notification](https://developers.momo.vn/v3/docs/payment/api/result-handling/notification/). Direct fetches sometimes returned403/timeout; official search-indexed sections supplied method codes, signature fields and callback acknowledgements. VNPAY signs sorted URL-encoded values with HMAC-SHA512 and uses x100 VND; MoMo uses HMAC-SHA256 raw field ordering, autoCapture wallet checkout and IPN204. VNPAY debited/suspicious, reversal and processing states remain unresolved. MoMo1000/7000/7002/9000 and unclassified results remain Unknown; only explicit terminal failure codes release the payment blocker. Signed status codes and a bounded transaction reference are retained in the audit. The MoMo result-code and notification pages describe9000 differently; this implementation conservatively requires captured result0 pending merchant certification. See [MoMo result codes](https://developers.momo.vn/v3/docs/payment/api/result-handling/resultcode/).

Tests use fake HTTP and synthetic signing keys, including tests of the Live configuration path with **no actual provider request**. These do not prove merchant acceptance or actual settlement. Before public use, complete provider sandbox cases, duplicate/reordered/lost IPNs, manual reconciliation/refund procedure, public HTTPS security and backup checks, and a deliberately authorized small real payment with merchant statement reconciliation. Browser/scanner acceptance remains deferred by the owner. The old P1 archive is unchanged and does not include P2 payments.

## Dispatch and notification review corrections

External checkout starts only after CommandExecutor confirms that the claim and command receipt committed. A caught SQL concurrency failure returns412 without contacting the provider, including on retry. Pending transaction references are reserved across intents; a conflicting reference cannot replace existing evidence. Reordered pending notifications preserve Paid/ReviewRequired. A cancellation fixture uses transaction0 when no payment transaction exists; late captured money remains reviewable after closure. These checks are local synthetic evidence, not provider certification.
