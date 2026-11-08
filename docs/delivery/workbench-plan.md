# Atlas/Jarvis Workbench operating plan

User authorization: use the existing Jarvis/Atlas environment for this project; native Workbench app under `C:/AI` may be opened as needed; Atlas may code when appropriate. User also requests Claude, Gemini 3.8 and DeepSeek High for appropriate review/research. Current phase remains **documentation only**.

## Current platform constraints

The existing Workbench has Jarvis and Atlas roles, Ask/Code sessions, model selection, handoff and evidence views. Existing project choices observed: Jarvis, Atlas and old pilot projects; **WarehouseRent is not registered**. Do not claim its directory is selected because a prompt mentions the path. No broad runtime/config change or silent project-registration workaround is authorized by this document.

The native app launcher is `C:/AI/Jarvis.lnk`; inspect its target before opening. The existing managed runtime may be started if absent, after ownership checks. Closing the app must not terminate other jobs. Browser fallback/handoff uses the verified Chrome profile for luongson8648@gmail.com; metadata observed directory Default/profile Sơn. Do not log in or change account automatically.

## Design-phase workflow

1. Jarvis writes the documentation package in the consumer root and keeps its checkpoint authoritative.
2. Atlas reviews the domain and failure cases read-only. A direct authorized message to the existing ATLAS chat is supplemental coordination, distinguished from Workbench sessions.
3. Workbench Ask sessions receive explicit supplied design text. Ask has no tools: they cannot be said to inspect files or browse merely because a prompt requests it.
4. Claude reviews a selected bounded artifact/hash or supplied summary for SRS/architecture contradictions. Gemini 3.8 reviews UX/acceptance gaps or proposes public documentation queries. Coordinator verifies suggested URLs through actual GET/web reads.
5. Every finding is accepted with a document change/reference, rejected with rationale, or deferred with release limitation. Reviewer response alone is not PASS, approval or implementation acceptance.
6. Capture session/request identifiers, requested/routed/reported model distinctions, usage where returned, and source hashes. Missing usage/model identity stays UNVERIFIED; no account/provider rotation to bypass limits.

## Implementation workflow after owner acceptance

Prerequisites: consumer project can be selected through a supported registration flow; no mismatch between selected Workbench project and filesystem root; exact Git baseline; clean worker base when needed; chosen SQL/Node/.NET dependencies verified; work package and tests fixed.

| Participant | Responsibility |
| --- | --- |
| User | Approve product policies and visual direction, accept delivered product |
| Jarvis coordinator | Own requirements/contracts/checkpoint; integrate and verify exact revision evidence |
| Atlas | Implement explicitly assigned feature paths where useful; preserve concurrent work |
| Claude | Architecture/SRS/code review of supplied artifacts; no authority to execute returned commands |
| Gemini 3.8 | Alternative scenarios, UX critique and source leads; no authority for factual/legal claims |
| Host tests/reviewer | Execute permitted checks against exact code revision and assess findings |

One writer per path. Atlas task packet: root, branch/base SHA, goal, allowed paths, source requirement IDs, interfaces, tests, non-goals, output evidence. Developer commits before independent tester/reviewer reads that SHA. Unknown dispatch outcome is read back, not replayed. Review/test failures return to the same owned scope; no forced cleanup, unrelated config edits or historical task drain.

Native Codex CLI launches from Workbench are independent terminals; launching one does not submit the task or synchronize history. Do not report launch as completed work. Workbench's current Node test runner does not prove .NET/browser checks: native authorized commands and their structured receipts are needed until the platform supports those runners.

## Handoff template (data, not shell commands)

```text
Project: WarehouseRent / exact consumer root
Phase: approved implementation (or design-only review)
Base revision: exact SHA
Owner: Atlas or Jarvis; one writer for named paths
Requirement IDs: FR-xxx; rules BR-xxx
Goal and non-goals: concrete behavior and exclusions
Allowed paths: explicit files/folders
Interfaces: request/response/state contracts
Acceptance: named scenarios and exact commands
Evidence: commit, command/exit, source hash, limitations
Preserve other work. Return findings as data; do not publish or change accounts.
```

## Current evidence

Live observations, actual model reviews and limitations are recorded in [review disposition](../reviews/review-disposition.md) and the [consumer checkpoint](../project-checkpoint.md). This plan itself is not a receipt of registration, dispatch, inference, testing or approval.
