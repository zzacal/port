# Implementation Plan: Admin UI Components

**Branch**: `005-admin-ui-components` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/005-admin-ui-components/spec.md`

## Summary

This feature builds the D-23 admin component set as static, server-rendered Razor components in
`src/Gallery.Web/Components/{Atoms,Molecules,Organisms,Templates}/`, next to the existing components:
- 4 atoms
- 6 molecules
- 6 organisms
- 1 template
- the admin use of ContactSheet

Each component has a `/ui/` page under its ordinary atomic level showing every state, including the states that had no
public reference, which are designed here (contracts/admin-components.md).

Tile and ContactSheet gain optional admin parameters, and the public output is unchanged. Three
colour tokens are added. There are no scripts and no packages. D-21 is amended: TagChip and
TagEditor are dropped, and AdminLayout's scripts are deferred.

## Technical Context

**Language/Version**: C# on .NET 10 (SDK 10.0.403)

**Primary Dependencies**: none added. Built-in Razor components, CSS isolation, HtmlRenderer.

**Storage**: N/A. Specimens use the feature 003 sample photos and literal sample text.

**Testing**: xUnit component tests through the existing `Render` helper, live `verify.sh`, and
browser measurement.

**Target Platform**: local development. `/ui` is Development-only.

**Project Type**: server-rendered web app.

**Performance Goals**: unchanged from 003. One inlined stylesheet, no extra requests, no layout
shift.

**Constraints**:
- no `style=` attributes
- no `<script>`
- media queries only at 600 and 1200
- public output byte-identical

**Scale/Scope**: 17 new components, 18 new `/ui` pages, and about 40 new files.

## Constitution Check

| Principle | Status | How |
|---|---|---|
| I.3 / I.6 Owner-only admin, CSRF | PASS (justified N/A) | No admin routes or forms are mapped. Components render real forms, so antiforgery attaches when the behaviour features map `/admin/*` (D-06). `/ui` stays Development-only. |
| II Visibility | PASS | The VisibilityToggle copy states "unlisted, not secret" (II.2) and the publish warning makes II.5 explicit. No data changes. |
| III Images leak nothing | PASS | Only the existing metadata-free samples are used. No uploads. |
| IV.1–IV.4 Timing | PASS | The new pages go through the existing RequestTimingMiddleware. verify.sh SC-008 checks 20 requests produce 20 events. |
| IV.8 No layout shift | PASS | BatchItem and Tile cells reserve the 4:3 box before any preview loads (edge case). |
| IV.22 No client framework on public pages | PASS | Public pages are unchanged. Admin has no scripts yet. |
| V.1 Minimal scope | PASS | Only the D-23 component list. TagChip and TagEditor are dropped. VisibilityToggle and SignInCard are flagged as severable (spec). |
| V.2 Dependencies | PASS | Zero packages (R-09). |
| V.3 Admin may need JS | PASS | None is used. Forms work without it. |
| VI CLI scaffolding | PASS (N/A) | No projects or packages. Files are added inside the existing project. |
| VII.2 Design deviations recorded | PASS | D-21 is amended in the design draft in this feature (T030). D-23's gap list is resolved by reference to this contract. |
| VIII Cost | PASS (N/A) | No hosted resources. |
| Workflow: existing tests unmodified | PASS with one stated change | `IndexTests` count 14 → 32 is a requirement change (SC-004). No other 003 test may change. |

Post-design re-check: unchanged. The Tile/ContactSheet additions are optional parameters, and
the byte-identity guard (T004) proves the public output did not move.

## Project Structure

```text
src/Gallery.Core/Design/Tokens.cs                   (+3 colours)
src/Gallery.Web/Components/_Imports.razor           (+admin namespaces)
src/Gallery.Web/Components/Molecules/Tile.razor(.css)              (optional params)
src/Gallery.Web/Components/Organisms/ContactSheet.razor            (optional ChildContent)
src/Gallery.Web/Components/ComponentTypes.cs
src/Gallery.Web/Components/Atoms/{Button,Input,Switch,SelectMark}.razor(.css)
src/Gallery.Web/Components/Molecules/{BatchItem,CoverMarker,DragHandle,SelectionBar,DangerConfirm,VisibilityToggle}.razor(.css)
src/Gallery.Web/Components/Organisms/{OwnerBar,SidePanel,UploadDropzone,BatchFooter,GalleryBuilder,SignInCard}.razor(.css)
src/Gallery.Web/Components/Templates/AdminLayout.razor(.css)
src/Gallery.Web/Ui/UiCatalog.cs                     (+17 entries in the existing levels)
src/Gallery.Web/Ui/Pages/Index.razor                (level display names)
src/Gallery.Web/Ui/Pages/*Page.razor(.css)          (17 new pages; ContactSheetPage extended)
tests/Gallery.Web.Tests/Admin*Tests.cs              (new)
tests/Gallery.Web.Tests/PublicOutputUnchangedTests.cs (new byte-identity guard)
```

## Complexity Tracking

None.
