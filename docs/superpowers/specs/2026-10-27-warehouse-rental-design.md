# WarehouseRent design baseline

Date: October27,2026; approved October28,2026. Status: **APPROVED FOR P0 IMPLEMENTATION — UI A**. Owner explicitly approved proposed policies, selected direction A and requested Atlas coding with independent model reviews. This approves local P0 implementation, not P1/P2 or publication/deployment.

## Intent

Build a useful .NET/React portfolio application for a single operator renting fixed warehouse units and tracking customer-owned goods. User has confirmed multiple facilities, whole fixed m² units, multiple units per rental and documentation before implementation.

## Goals and boundaries

Correct allocation under concurrency, isolated customer inventory, traceable posting/finance, coherent customer/operator workflows and demonstrable testing. P0 is a portfolio slice; P1 completes operational workflows. Live payments, statutory invoices, legal execution, hazardous/cold goods and shared pallet capacity remain outside P0/P1.

## Approaches and proposals

The user selected fixed units over shared pallet capacity. The [product brief](../../requirements/product-brief.md) compares ERP customization, booking-only and modular full-stack approaches. Modular ASP.NET/React and [UX direction A](../../design/ux-design.md), the light operations desk with customer booking wizard, are approved. No live UI existed at approval.

## Authoritative package

- Requirements: [SRS](../../requirements/SRS.md), [rules](../../requirements/business-rules.md), [use cases](../../requirements/use-cases.md).
- Design: [data](../../design/data-model.md), [architecture/API](../../design/architecture-api.md), [UX](../../design/ux-design.md).
- Assurance: [test strategy](../../testing/test-strategy.md), [traceability](../../testing/traceability.md), [review disposition](../../reviews/review-disposition.md).
- Delivery: [roadmap](../../delivery/roadmap.md), [Workbench plan](../../delivery/workbench-plan.md), [work packages](../plans/warehouse-rental/plan.md).
- Sources: [comparison](../../research/comparative-study.md). Terminology: [glossary](../../../GLOSSARY.md).

## Approved demo decisions

Daily VND tariff, exclusive end date, 1–365 day interval, one facility per booking, 15-minute hold, 24-hour quote,20% separate deposit and manual verification are **owner-approved demo defaults**. Rate drift before hold requires re-quote/explicit acceptance; hold freezes accepted terms. Overstay blocks physical handover and new unconditional holds until resolved. P0 cannot issue statutory invoices or transfer real funds.

Selected direction A: light operations desk with a customer booking wizard. Owner also approved scheduled-date billing after activation despite delayed handover, no automatic evidence-upload hold extension and the other defaults in the owner review guide. Real-operator/legal validation remains outside the demo approval.

## Builder and model checks

Atlas initial domain review is partial and hash-scoped. Workbench Ask reviews receive supplied text and cannot claim filesystem access. Claude/Gemini/DeepSeek use depends on live catalog and successful inference; aliases/readiness are not proof. Findings are resolved in the review disposition. A fresh builder check will question remaining material ambiguities before owner handoff.
