SnG Control Backend — Agent Instructions
These instructions are permanent repository rules.
Task-specific instructions override this file only when explicitly stated.
1. Primary objective
Implement the requested business functionality correctly with the smallest coherent change.
Priorities:
1. business correctness;
2. data integrity;
3. traceability;
4. maintainability;
5. efficient execution.
Prefer extending existing patterns over introducing new abstractions.
Do not perform unrelated refactors.
Do not redesign stable features unless the requested task actually requires it.
Before writing code, inspect the directly relevant implementation and tests.
Do not scan the entire repository unless the task genuinely requires it.
2. Repository architecture
Solution structure:
- src/Api
- src/Application
- src/Domain
- src/Infrastructure
- tests/SnG.Control.Tests
Domain
Owns entities, enums, value objects, business invariants and business state transitions.
Must NOT depend on:
- ASP.NET Core
- EF Core
- Infrastructure
- IConfiguration
- HttpContext
Application
Owns use cases, feature-specific contracts, store abstractions, current identity abstractions and orchestration of business operations.
Must NOT depend on:
- ASP.NET Core
- HttpContext
- IConfiguration
- EF Core implementation details
Infrastructure
Owns PostgreSQL, EF Core, OpenXML, filesystem, concrete stores and external technical implementations.
Api
Owns Controllers, HTTP contracts, authentication, authorization, configuration, runtime hosting and background hosted services.
Controllers must remain thin.
3. Do not introduce unnecessary frameworks
Do NOT add unless explicitly requested:
- MediatR
- AutoMapper
- generic repositories
- custom UnitOfWork
- Docker
- Redis
- RabbitMQ
- Hangfire
- event bus infrastructure
- generic workflow engines
Do not create abstractions for hypothetical future requirements.
Use the simplest architecture consistent with the existing codebase.
4. Existing identity model
Google is the authority of identity.
Stable identity: Google sub.
Email is informational only.
Never use email as the authorization key.
Existing concepts include:
- ICurrentUser
- IRoleResolver and request-scoped IActiveRoleContext
- ICommercialHeadIdentity
- Google JwtBearer authentication
- CommercialHead authorization policy
There is intentionally NO:
- Users table
- Employees table
- ASP.NET Identity user store
- local password authentication
Do not create one unless explicitly requested.
Never hardcode:
- Google Subjects
- Google Client IDs
- tokens
- passwords
- database credentials
- local storage paths
Real values belong in User Secrets or environment configuration.
5. Project commercial responsibility
Current business rule: Project creator == responsible commercial advisor.
Project stores:
- CreatedByGoogleSubject
- CreatedByEmail
- CreatedByName
Do NOT duplicate this with fields such as ResponsibleCommercialSubject, AdvisorId or ResponsibleUserId unless reassignment is explicitly introduced later.
Legacy projects may have creator fields null.
Never invent identity values for legacy data.
6. Stable ProjectTask enum contract
Existing values are stable:
- PrepareQuotation = 1
- ReviewQuotation = 2
- PresentQuotationToClient = 3
Never renumber existing values.
Future task types must receive explicit new numeric values.
7. Current workflow invariants
Current approved workflow:
Commercial → sends Project to Budgets → PrepareQuotation.
First SG assignment → starts PrepareQuotation.
Budgets confirms quotation → sends to Commercial → completes PrepareQuotation → creates and starts ReviewQuotation → assigns it to Commercial Head.
Commercial quotation confirmation → completes ReviewQuotation → creates and starts PresentQuotationToClient → assigns it to original Project creator.
Preserve these transitions unless the task explicitly changes them.
8. Quotation history invariants
Presupuestos historical snapshot:
- BudgetQuotationVersion
- CommercialCatalog
- CommercialCatalogItem
- CommercialCatalogItemExtraction
Commercial final snapshot:
- CommercialQuotation
- CommercialQuotationItem
Commercial confirmation must NOT overwrite the Presupuestos snapshot.
After commercial confirmation, CommercialQuotationItem is the current commercial reality and CommercialCatalogItem remains historical Presupuestos evidence.
9. Quotation row identity
Stable structural identity: Section + SourceRow.
Do NOT use as persistent identity:
- ItemNumber
- Location
- visible ordering
- display number
ItemNumber may change without changing row identity.
Structural additions/deletions must not be guessed or fuzzy-matched unless the business rule explicitly changes.
10. XLSX rules
Do not change quotation parsers unless the requested task concerns XLSX behavior.
Existing parser behavior is regression-tested and should be treated as stable.
Economic contract:
- N{SourceRow} = UnitPrice
- O{SourceRow} = TotalPrice
ProjectTotal contract:
1. work on COTIZACIÓN;
2. locate ADICIONALES;
3. find exactly one VALOR TOTAL in column N before ADICIONALES;
4. read column O from that row.
Never hardcode a row such as O169.
Never infer ProjectTotal from item totals.
Never calculate IVA, discounts, administration, UnitPrice × Quantity, project total or additional total.
Read document values.
Formula cells require cached values.
Use decimal, not floating point.
Use invariant culture for OOXML numeric parsing.
SG780 CONTRACTED/MODIFIED remains unsupported unless explicitly changed.
11. File retention invariants
Permanent:
- original.xlsx
- commercial.xlsx after commercial confirmation
- extracted historical images
Operational temporary:
- confirmed.xlsx
After successful commercial confirmation:
- original.xlsx remains
- images remain
- commercial.xlsx remains
- confirmed.xlsx is physically deleted
- ConfirmedFileStorageKey becomes null
Do not duplicate images.
Do not regenerate commercial.xlsx on download.
Do not silently modify uploaded commercial.xlsx.
When it is confirmed, preserve its exact bytes.
12. Filesystem/database consistency
PostgreSQL and filesystem do not form a distributed transaction.
When one workflow changes both:
- follow existing explicit compensation patterns;
- preserve files if commit outcome is uncertain;
- do not blindly delete data after unknown commit result;
- restore operational files when rollback is known to have succeeded.
Do not weaken LocalFileStorage path traversal or symlink protections.
13. PostgreSQL / EF Core rules
Do NOT:
- drop the database
- recreate the database
- use EnsureCreated
- automatically apply migrations on startup
- modify old migrations
- squash migrations
Create a new migration only when the EF model actually changes.
Use PostgreSQL constraints for concurrency-sensitive invariants.
When mapping PostgreSQL 23505, only map the exact known constraint.
Do not turn every unique violation into a generic conflict.
Do not add speculative indexes.
Prefer DeleteBehavior.Restrict where consistent with the existing historical model.
14. Time rules
Persist timestamps as UTC.
Business timezone rules must be explicit.
Never use server local timezone as business truth.
Prefer TimeProvider for new time-sensitive behavior.
Tests involving time must use controllable time.
Do not sleep in tests.
15. Lead Time rules
Current approved Lead Time policy:
- PrepareQuotation = 5 business days
- ReviewQuotation = 5 business days
- PresentQuotationToClient = 5 business days
Business calendar:
- Monday–Saturday = business days
- Sunday = non-business day
- Colombian national holidays = non-business days
Timezone: America/Bogota.
The start day does NOT count.
Counting begins on the next business day.
Deadline: 17:00 America/Bogota on the fifth business day.
Example contract:
- Started: 2026-10-14
- Deadline: 2026-10-20 17:00 America/Bogota
Deadline must be calculated once when the task starts and persisted.
Do NOT recalculate previously persisted deadlines after policy changes.
Completion exactly at deadline counts as on time.
CompletedAt > DeadlineAt means late.
Once WasOverdue becomes true, it never returns to false.
16. Notification rules
Current notification kinds:
- TaskAssigned
- DueInTwoBusinessDays
- DueToday
- Overdue
Current notification semantics:
- TaskAssigned: informational
- DueInTwoBusinessDays: informational
- DueToday: informational
- Overdue: active until task completion, then resolved
No notification should be deleted as part of normal workflow.
There is intentionally NO:
- read/unread
- IsRead
- ReadAt
- notification bell count
- email
- WhatsApp
- SMS
- push notification
Notifications currently target only AssignedToGoogleSubject.
No area/group subscriptions yet.
17. Notification timing
Two business days before deadline: 08:00 America/Bogota.
Deadline day: 08:00 America/Bogota.
Overdue: only after now > DeadlineAt.
Exactly at DeadlineAt is NOT overdue.
Completed tasks must not receive future reminder notifications.
If a task completes late before the worker generated Overdue, the workflow must still preserve late-event traceability.
18. Notification recovery
Notification processing must be recoverable after downtime.
If the server missed one alert and restarts later, generate the relevant missed alert.
If several temporal milestones were missed, generate only the most relevant CURRENT temporal alert.
Example: missed DueInTwoBusinessDays and DueToday, now overdue → create only Overdue.
TaskAssigned is independent and should be recoverable for currently relevant assigned tasks.
Idempotency must rely on PostgreSQL uniqueness, not only pre-checks.
19. Future reassignment rule
Do NOT implement task reassignment unless explicitly requested.
The agreed future rule is: if an active task is reassigned:
- preserve prior history;
- new assignee receives a new TaskAssigned notification;
- future notifications target the new Subject;
- Lead Time restarts;
- a new deadline is calculated from the reassignment cycle.
Do not implement APIs or data structures for this prematurely.
20. Configuration rules
Runtime configuration belongs in User Secrets for local development and environment variables for deployed environments.
Do not version real values.
Examples of existing configuration concepts:
- Authentication:Google:ClientId
- Authentication:CommercialHeadSubject
- Authentication:BudgetsAssigneeSubject
- Authentication:AccountingAssigneeSubject
- ConnectionStrings:SnGControl
- FileStorage:RootPath
Configuration validation errors should name the missing key but not reveal its value.
21. Documentation rules
README.md describes current functional/business behavior.
README_TECHNICAL.md acts as technical handoff/manual for the next developer.
They are living documentation, NOT changelogs.
When changing functionality:
- update only affected sections;
- fix directly discovered stale statements;
- do not rewrite the whole documents without reason.
Never include real credentials, real Subjects, tokens, passwords or connection strings.
22. Scope discipline
Before coding:
1. identify the smallest affected surface;
2. inspect relevant implementation;
3. inspect relevant tests;
4. reuse established patterns.
Do not inspect every repository file by default.
Do not refactor unrelated features.
Do not upgrade dependencies unless required.
Do not rename stable concepts for style preferences.
Do not change parser behavior unless necessary.
Do not add optional future functionality.
23. Efficient verification strategy
Use the cheapest reliable feedback loop during implementation.
During development, run focused tests only, for example:
dotnet test --filter "FullyQualifiedName~CommercialQuotation"
dotnet test --filter "FullyQualifiedName~ProjectTask"
dotnet test --filter "FullyQualifiedName~Notification"
Do NOT run the entire test suite after every edit.
Use dotnet build --no-restore after meaningful compile-level changes.
Do not repeatedly restore packages when package references did not change.
Once implementation is stable, run the complete suite ONCE with dotnet test.
If it fails: fix the affected area, rerun focused tests, then rerun the full suite once after fixes.
Do not repeatedly execute a passing full suite.
24. Restore policy
Run dotnet restore only when package references changed, assets are missing, the environment requires it, or the task explicitly requests it.
Run dotnet tool restore only when the tool manifest changed, dotnet-ef is unavailable, migration tooling is required, or the task explicitly requests it.
Do not execute restore commands as ceremony on every task.
25. Migration verification policy
Run EF migration diagnostics only when the EF model or migrations changed:
dotnet ef migrations list
dotnet ef migrations has-pending-model-changes
Apply local migration only when appropriate and safe.
If the EF model did not change, skip migration diagnostics.
26. Corpus verification policy
The real quotation corpus is expensive to process.
Run the full corpus only when changes affect XLSX parsing, worksheet layout, images, technical extraction, economic extraction or OpenXML behavior.
Otherwise rely on existing automated regression tests.
Never copy customer XLSX files into Git.
27. PostgreSQL smoke policy
Run real PostgreSQL smoke tests only when the task changes EF persistence, migrations, constraints, locking, concurrency or DB/filesystem compensation.
For pure domain, DTO, controller or Application-only behavior, automated tests are normally sufficient.
Never mutate real business projects merely to smoke-test.
Prefer synthetic data and rollback transactions.
28. Background services
Background services must keep orchestration thin.
Business processing should live in a deterministic one-iteration service that can be tested directly.
Do not test hosted scheduling by waiting real minutes.
Background service failures must not crash the API.
Respect cancellation.
Do not log secrets.
29. Test quality
Never remove or weaken an existing assertion merely to make tests pass.
Tests must not depend on Google real, Internet, external APIs or real tokens.
Authentication uses synthetic identities.
Time-based tests use controlled time.
Concurrency-sensitive behavior must be enforced by DB constraints when appropriate.
30. Working tree discipline
Before changes:
git status --short
Do not overwrite unrelated user changes.
At the end:
git diff --check
git status --short
Inspect final diff for accidental changes.
Do not commit or push unless explicitly requested.
31. Default execution order
For each task:
1. Read this AGENTS.md.
2. Read only relevant implementation/tests.
3. Form a short implementation plan.
4. Implement the smallest coherent change.
5. Run focused tests.
6. Fix issues.
7. Build if useful.
8. Run full suite once when stable.
9. Run migration checks only if EF changed.
10. Run PostgreSQL smoke only if relevant.
11. Run XLSX corpus only if relevant.
12. Update affected documentation.
13. Run git diff --check.
14. Review final diff.
15. Report.
16. Stop.
Do not continue into another business block without explicit instruction.
32. Final report format
Keep reports concise.
Report:
1. functional change;
2. main files/components affected;
3. important business rules implemented;
4. migration if applicable;
5. tests added/changed;
6. focused test result;
7. full suite result;
8. build result;
9. migration / DB / corpus result only if relevant;
10. documentation updated;
11. unresolved issues or assumptions.
Do not produce dozens of numbered points merely repeating the prompt.
33. Priority rule
Correctness and business invariants come first.
Efficiency means:
- avoiding repeated repository exploration;
- avoiding redundant restores;
- avoiding repeated full-suite runs;
- avoiding unnecessary corpus runs;
- avoiding unnecessary database smokes;
- avoiding over-engineering.
Efficiency must never mean skipping the final validation actually required by the task.
Identity & Areas V1: Google Subject is identity; configuration defines membership; exactly one owned X-SnG-Active-Role authorizes each operational request. Auth/me is exempt from active-role selection. CommercialAdvisorSubjects is explicit; do not infer advisor access from Google authentication. Never bypass active-role checks because one Subject owns several roles. Project ownership and task assignments remain unchanged when switching area. Management is always read-only while active.


Quotation Portfolio Analytics V1: Cross-project quotation analytics use the official Management
portfolio capability. Never emulate them by iterating projects. Backend owns exact aggregate
sums and deterministic bounded rankings over current confirmed Commercial quotations, not
sales/revenue/margin or a historical version archive. No UnitPrice × Quantity or item-total
recalculation. Use Money.CurrencyCode, preserve monetary precision and reject aggregate overflow.
Queries are read-only, AsNoTracking and constant in count; preserve a consistent read snapshot.
No arbitrary limit, new migration, entity changes or storage identifiers in the public projection.


Quotation Mix Analytics V2: the same portfolio capability owns system, glass,
aluminum finish and system/glass aggregates. Quantity, row frequency and quoted
value are distinct measures; none means demand, sales or profitability. Preserve
stored case, exclude blank categories, require both combination values. Execute
bounded SQL rankings and coverage in the existing RepeatableRead snapshot, with
constant query count and exact monetary overflow checks across all categories.


Commercial Presentation Outcome V1: only the assigned active CommercialAdvisor may record
one immutable client decision per confirmed CommercialQuotation and presentation task.
Use the existing project/task lock order and an atomic insert + task.Complete + notification
reconciliation transaction. Preserve Lead Time rules. Shared reads remain scope-filtered.
No decision inference from Completed; Approved != paid, ChangesRequested != requotation,
Rejected != closed project. No downstream task or project lifecycle in this block.

Versioned Requotation Loop V2: ChangesRequested allows, but never automatically starts,
a new round. Only the assigned active advisor explicitly submits immutable requestedChanges.
Lock project then tasks then source decision; atomically persist request, new started Budgets
task and notifications. Existing round tasks, decisions, versions and files are immutable.
Use QuotationRound for workflow selection; reject multiple eligible active tasks. Latest
confirmed Budget differs from latest confirmed Commercial. Portfolio/Mix uses one latest
Commercial per project by Budget VersionNumber, never confirmation time. History reads are
bounded first 20 ascending with coverage; no implied current state from truncated history.


Accounting Initial Deposit V1: Approved never creates Accounting work automatically. Only the
assigned active advisor explicitly hands off the current approved Commercial round. Accounting
area=3 and RegisterInitialDeposit=4; task has null QuotationRound and no SLA/deadline. Only the
configured active Accounting Subject assigned to that task records one immutable initial deposit.
Lock project/tasks/source and commit entity, task transition and notifications atomically.
Use existing Money decimal COP normalization, not float; ReceivedAt is aware/nonfuture and
RecordedAt is server-owned. StartUntracked + Complete use RecordedAt. Absence is not zero or
proof of nonpayment. No expected percentage, balance, full-payment inference or Engineering/
Production transition. Shared reads remain scope-filtered. Do not apply operational migrations
without explicit migration authorization; preserve previous migration files.

Deposit Verification: the assigned active advisor uploads receipts only for the current Approved
Commercial round and explicitly submits exact immutable document IDs with an idempotency key.
Only one pending review per project. Rejection requires a note and preserves all evidence;
resubmission reuses the pending Accounting task. Approval atomically records InitialDeposit,
completes Accounting, notifies the advisor and creates one TakeMeasurements=5 task in
Installation area=4, assigned by Authentication:InstallationAssigneeSubject, without SLA.
Retired direct Accounting HTTP writes must not bypass verification. Preserve document versions,
historical deposits and project/task locking; no operational migration without authorization.
