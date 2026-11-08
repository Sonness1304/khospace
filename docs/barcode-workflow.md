# Barcode-assisted inventory — P2.1

This slice identifies an existing customer item and optionally selects it in a draft stock request. A scan never creates a request, changes quantity, approves a movement or posts inventory. Use the existing customer approval and physically checked posting workflow afterwards.

## Register a label

1. Sign in as a CustomerAdmin and select the customer organization.
2. In **Requests**, press **Load customer items**, then expand **Customer item IDs for barcode registration** and copy the ID next to the intended SKU/base unit. Use Previous/Next customer items to reach other pages. Creating an item also displays its ID beside the success notice, even if the follow-up item list is unavailable.
3. Open **Settings → Item barcode registry**. Enter the item ID and read its labels.
4. Select **GTIN** or **Internal**, enter a value and a change reason, then register.
5. Read the list again for current status and versions. Fifty labels appear per page.

Active customer members can read their own item labels. Only a current CustomerAdmin can register, enable or disable them. Staff cannot browse another customer's registry. A label remains attached to its original item permanently, including while disabled. Reactivate that mapping instead of registering it against another item. An inactive original item cannot have its labels enabled or resolved.

## Code rules

- **GTIN:** exactly 8, 12, 13 or 14 ASCII digits with the GS1 modulo-10 check digit; stored as 14 digits by adding leading zeroes. Equivalent shorter and padded forms identify one label. All-zero codes, spaces, control characters and non-ASCII digits are rejected. Check-digit validity does not establish ownership, GS1 licensing or product authenticity.
- **Internal:** 1–64 ASCII letters/digits, dot, underscore, slash or hyphen; first character must be alphanumeric. Stored uppercase. Internal `box-01` and `BOX-01` identify one label.
- The user selects the kind explicitly. A numeric Internal code is not automatically treated as GTIN.
- Uniqueness includes disabled labels and is scoped to customer organization and kind. Two customers may use the same printed value without sharing their items.

GTIN lengths and zero padding follow [GS1's GTIN guidance](https://support.gs1.org/support/solutions/articles/43000734386-are-all-gtins-unique-numbers-). The check digit uses alternating weights 3 and 1 from the last data digit towards the left, then the complement to the next multiple of 10; see [GS1 US check-digit guidance](https://www.gs1us.org/tools/check-digit-calculator). No external product service is called.

## Find an item

1. In **Requests**, enter the rental lease ID. Staff use **Barcode lookup lease ID** in the same view.
2. Select the code kind, focus **Scanned barcode**, and type or scan with a keyboard-wedge reader.
3. Press Enter or **Find barcode item**. The lookup derives the customer and facility from the accessible lease. Customer membership, active identity and staff facility assignments are checked on the server.
4. Confirm the displayed SKU and base unit. Customer users can press **Use item in request** to fill an empty draft line. If that item is already selected, its line and quantity remain unchanged. At 50 occupied lines, select a line manually.
5. Enter the observed/requested quantity explicitly, select the lease unit, and follow the normal draft/submit/approval/posting steps. A barcode identifies an item, not a pallet, lot, location or quantity.

Staff can identify an item within their assigned facility lease context, without creating a customer instruction through this helper. Editing the code/kind/lease or switching workspace invalidates the displayed result. An older asynchronous response cannot supply an item to a different context. Lookup is allowed for an accessible historical lease; the existing request/posting rules still decide whether stock activity is allowed.

Keyboard input and Enter are component-tested. Physical scanner behavior, browser focus, mobile layout, camera scanning and full browser/accessibility acceptance remain unverified. No scanner SDK or camera permission is included. Configure a reader to send plain code characters followed by Enter; control prefixes embedded in values are rejected.

## Commands, concurrency and history

Registry writes require a reason and UUID `Idempotency-Key`; status changes additionally require `If-Match: "<base64 rowversion>"`. The mapping update, audit and saved command receipt commit together. A concurrent duplicate produces a conflict. A stale version is rejected; read the current label list before issuing a new change.

When a response is lost, the UI retains the original key, body, version and workspace. Use **Retry original barcode command** in that workspace. Do not invent a new key to resolve an unknown outcome. Retried authorization or rate-limit errors do not discard the original identity. A successful receipt clears the draft command and requires a fresh list read for current versions. Page reload does not persist this in-memory recovery state: record the displayed **Original barcode command** key before leaving an uncertain session; server-side saved commands remain queryable at the existing scoped command endpoint.

API surface (under `/api/v1`):

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/items/{itemId}/barcodes?organizationId={org}&page=1` | Read own item labels |
| POST | `/items/{itemId}/barcodes` | Register `{organizationId,kind,value,reason}` |
| POST | `/barcodes/{id}/status` | Change `{organizationId,active,reason}` with current version |
| GET | `/leases/{leaseId}/barcode-lookup?kind=GTIN&value=4006381333931` | Resolve within current lease access, selection only |

Unknown/inaccessible resources return safe 404 responses; invalid code/reason returns 422, duplicate label 409, missing version 428 and stale version 412. Standard authentication/CSRF protections apply.

## Migration and recovery

The additive migration is `20261029042636_CustomerItemBarcodes`. It creates customer/item references, a unique code index and rowversion. Populated downgrade refuses to delete label history; restore a validated backup instead. EF application writes reject reassignment and deletion. This does not restrict a privileged database administrator's direct SQL access.

The SQL recovery rehearsal includes label identity, customer/item binding, code, status and timestamps in its reconciliation hash. It restores a GUID test database and verifies that accessible users can resolve the label while revoked/foreign users cannot. It does not migrate or restore the owner's runtime database.

The previously delivered P1 archive is unchanged and does not contain P2.1. Build this revision separately for manual P2 exploration following [local setup](../README.md#local-setup--synthetic-demo). This source delivery does not establish a running API, deployment or full P2 completion.
