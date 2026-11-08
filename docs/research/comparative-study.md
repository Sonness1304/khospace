# Research and comparative assessment

Observed October 27, 2026. Method: public GitHub metadata, selected READMEs/tree/config files through authenticated `gh` GET, and vendor documentation through web read tools. No source repository was cloned, installed or executed. No operational system or claim of production quality was audited.

## Repositories inspected

| Source and observed revision | Evidence read | Useful lesson | Limitation / reuse decision |
| --- | --- | --- | --- |
| [ERPNext](https://github.com/frappe/erpnext/tree/821ca1d87e81751216f1f6fd5ed89935b1677f98) — GPL-3.0 metadata | Repo metadata, README, warehouse/ledger vendor docs | Hierarchical locations and stock movement history | Different ERP stack; use concepts, no code copying |
| [InvenTree](https://github.com/inventree/InvenTree/tree/3e55d413eca530871e15178deab6849d25c425ab) — MIT metadata | Repo metadata, README, stock-tracking docs | Actor/time/quantity history and traceability | Inventory system, not proof of space-rental capability; reference only |
| [OpenBoxes](https://github.com/openboxes/openboxes/tree/d28bf7c0a19ed2ac61ad0f168039160a6a551932) — EPL-1.0 metadata | Repo metadata and selected README | Mature warehouse-domain reference worth deeper future study | Not run; rental/customer isolation not verified |
| [pavo management-system](https://github.com/pavo-company/management-system/tree/b63c3ad49d749dc5e0fd9430eb6f584445eec3d9) — MIT metadata | README, root list, project file | Small inventory examples can inform simple workflows | WPF/net5-era project; not ASP.NET/React or a modern architecture template |
| [WarehouseRentalSystem](https://github.com/haotnahe181274/WarehouseRentalSystem/tree/bb0e8a0bb069e79068dd2e9a0fa51a017f0ddb76) — no license found in metadata | Root/tree, servlet names | Rental/contract/check-request concepts appear in a Java project | No reuse permission established; no correctness/security claims; do not copy code |
| [WarehouseRentalManagement](https://github.com/therealtuan23/WarehouseRentalManagement/tree/f76067dbd3c5f1adf6156e0080951e3209d2a737) — no license found | Root and README | Search-result names can be misleading | Only a README outlining Spring/React folders; not an implemented template |

These SHAs identify metadata observations; README fetches used the then-current default branch. They are not a full-file audit pinned atomically to each SHA. Recheck selected files at exact SHA before any later reuse.

## Articles and product documentation

- [ERPNext Warehouse](https://docs.frappe.io/erpnext/warehouse): location trees and item/location balance concepts. We adapt the hierarchy to facility → zone → fixed unit, and add customer ownership as a separate dimension.
- [ERPNext Stock Ledger](https://docs.frappe.io/erpnext/stock-ledger): movement history links to source documents. Our ledger records quantities/custody only; goods valuation accounting is outside MVP.
- [InvenTree Stock Tracking](https://docs.inventree.org/en/stable/stock/tracking/): history records action, actor and quantity changes. We require retention and linked reversals in our own design; that is our requirement, not a claim every upstream configuration is immutable.
- [Odoo Rental](https://www.odoo.com/documentation/19.0/applications/sales/rental.html): search excerpt confirmed rental scheduling/pricing/status coverage. Full page retrieval timed out twice; detailed policies were **not** relied on.
- [EF Core concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency): version tokens help detect conflicting updates. This alone does not prevent arbitrary interval-overlap inserts; the SQL Server allocation-lock design is specified separately.
- [ASP.NET integration testing](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests): test-host approach for HTTP/auth wiring; production-provider concurrency still needs real SQL Server tests.
- [.NET support](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core): .NET 10 LTS is the proposed baseline; live SDKs observed locally were 10.0.401 and 10.0.403, not installed by this task.

## Synthesis: original design choices

Leased floor area and stored item quantity are different capacities. Renting an entire unit blocks that unit regardless of how many goods are stored in it. Scheduling and physical occupancy also differ: a date may be free on paper while a previous customer has not cleared the unit.

Customer ownership must survive every stock operation, search/export and attachment download. Warehouse stock should not be treated as merchandise owned by the operator. A refundable deposit should not be silently classified as rental revenue.

Use an original modular .NET/React implementation. Reuse public ideas with attribution; inspect source and license again before copying any third-party code/assets. The project name/feature list of a search result is not proof of a working reference application.

## Coverage gaps

No interviews with operators/customers, site visit, real tariff or contract review; no validation of fire/load/insurance/retention/legal policy; no upstream applications executed. Operational policies in this package are proposed demo defaults. Flexe's attempted page could not be retrieved. No competitor pricing or claim of commercial superiority is inferred.
