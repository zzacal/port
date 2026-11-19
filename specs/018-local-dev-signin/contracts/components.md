# Component contract: Local Sign-In Without Google

## K1 Input atom (`src/Gallery.Web/Components/Atoms/Input.razor`, changed)

Add `[Parameter] public string? Type { get; set; }` and write `type="@Type"` on the single-line
`<input>` element only (not the textarea). When `Type` is null, Razor omits the attribute, so every
existing usage and the `/ui/atoms/input` baseline stay byte-identical.

## K2 SignInCard organism (`src/Gallery.Web/Components/Organisms/SignInCard.razor` + `.razor.css`, changed)

Add `[Parameter] public bool Local { get; set; }`.

In the SignIn and Claim branches, append `@LocalForm` directly after the Google form's closing
`</form>`, on the same line, so that with `Local = false` the output is byte-identical (the 007
technique: an expression that renders null). The Refused branch is unchanged.

```razor
private RenderFragment? LocalForm => !Local ? null :
    @<form class="si-local" method="post" action="/admin/signin/local">@TokenInput<Input Label="Local password" Name="password" Type="password" Required="true" /><Button Variant="ButtonVariant.Secondary">@(State == SignInState.Claim ? "Claim locally" : "Sign in locally")</Button></form>;
```

CSS (card-scoped, existing tokens only, no `style=`):

```css
.si-local { display: flex; flex-direction: column; gap: var(--space-s); margin-top: var(--space-m); }
```

Rules:
- The Google button stays the only Primary button (UI-3); the local button is Secondary.
- Copy is exactly as above. No hint text and no warning prose (the owner prefers the fewest words).

## K3 SignInPage (`src/Gallery.Web/Admin/Pages/SignInPage.razor`, changed)

Add `[Parameter] public bool Local { get; set; }` and pass it through to `SignInCard`. The default
is false, which gives today's output.

## Not changed

`AdminLayout`, `Button`, the `/ui` catalogue pages and their baselines (no new specimen; research
R-07 names the severable seam).
