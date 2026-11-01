# Component Contract: Owner Sign-In and Claim (007)

Additions only. Every new parameter defaults to "absent", and with defaults the rendered markup
MUST be byte-identical to today's, which the existing `UiPagesUnchangedTests` and
`PublicOutputUnchangedTests` already enforce. Those tests and the 006/005 baselines MUST pass
unmodified. No `/ui/` catalogue page changes; new states are tested in a new file
`tests/Gallery.Web.Tests/SignInComponentTests.cs`.

## Type

`Components/ComponentTypes.cs`: add `public sealed record FormToken(string FieldName, string Value);`

## SignInCard (`Components/Organisms/SignInCard.razor`)

New parameters:

- `Antiforgery` (`FormToken?`, default null). When set, each `<form>` the card renders gains, as
  its first child, `<input type="hidden" name="@FieldName" value="@Value">`. The Refused state has
  no form and is unaffected.
- `Notice` (`string?`, default null). When set, `<p class="si-notice" role="alert">@Notice</p>` is
  rendered directly after the `<h1>`, in every state.

CSS (`SignInCard.razor.css`): `.si-notice` uses the existing danger text token colour and the body
type size; no other rule changes.

## OwnerBar (`Components/Organisms/OwnerBar.razor`)

New parameter `SignOut` (`FormToken?`, default null). When set, after `@PrimaryAction` inside
`.ob-actions`:

```html
<form method="post" action="/admin/signout" class="ob-signout">
  <input type="hidden" name="{FieldName}" value="{Value}">
  <Button Variant="ButtonVariant.Quiet">Sign out</Button>
</form>
```

CSS: `.ob-signout { display: contents; }` so the button sits in the actions row like its siblings.

## AdminLayout (`Components/Templates/AdminLayout.razor`)

New parameters: `ShowOwnerBar` (`bool`, default true) — when false, the `<OwnerBar>` element is not
rendered; `SignOut` (`FormToken?`, default null) — passed to `OwnerBar`.

## Pages (new, `src/Gallery.Web/Admin/Pages/`)

- `SignInPage.razor`: parameters `State`, `Cover`, `Notice`, `Antiforgery`; renders
  `AdminLayout Title="Sign in · Gallery" ShowOwnerBar=false` containing the `SignInCard`.
- `ShellPage.razor`: parameter `SignOut`; renders `AdminLayout Title="Gallery admin"
  SignOut=@SignOut` with the `admin-empty` section from contracts/http.md.

Pages are endpoint views, not catalogue components; they are not added to `UiCatalog`.
