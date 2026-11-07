# Research: how design systems do tag / multi-value combobox input

Feature 014, TokenCombobox. Fetched 2026-11-06. Every quote below is copied from the page named.

## Sources

| Id | Source | URL |
|---|---|---|
| S1 | W3C ARIA APG, Combobox pattern | https://www.w3.org/WAI/ARIA/apg/patterns/combobox/ |
| S2 | W3C ARIA APG, Listbox pattern | https://www.w3.org/WAI/ARIA/apg/patterns/listbox/ |
| S3 | Microsoft Fluent 2, Tag picker | https://fluent2.microsoft.design/components/web/react/core/tagpicker/usage |
| S4 | GitHub Primer, Autocomplete (API) | https://primer.style/product/components/autocomplete/ |
| S5 | GitHub Primer, Forms | https://primer.style/product/ui-patterns/forms/ |
| S6 | Atlassian, Select (usage) | https://atlassian.design/components/select/usage |
| S7 | Shopify Polaris, Combobox | https://polaris-react.shopify.com/components/selection-and-input/combobox |
| S8 | IBM Carbon, Dropdown / combo box | https://carbondesignsystem.com/components/dropdown/usage/ |
| S9 | Google Material 3, Chips | https://m3.material.io/components/chips/guidelines |
| S10 | GOV.UK accessible-autocomplete | https://github.com/alphagov/accessible-autocomplete |
| S11 | GOV.UK Text input | https://design-system.service.gov.uk/components/text-input/ |
| S12 | Adobe React Aria, TagGroup | https://react-spectrum.adobe.com/react-aria/TagGroup.html |

Not reached: Adobe Spectrum's tag-field page (404) and Carbon's multiselect page (crawler
failure). Carbon is cited from its dropdown page only.

## Principles (each with its evidence)

P1 Values are removable tags inside the field, and the field grows.
- S6: "Selected options appear inside the text input area."
- S3: "tags that get inserted to the input wrap and will make the height grow to fit the content."
- S9 (input chips): the trailing icon is "required and must be used to remove the chip."

P2 Typing filters the list. Matching is case-insensitive.
- S4: "By default, we filter out items that don't match the value of the autocomplete text input. The default filter is not case-sensitive."
- S7: "As merchants type in the text field, the list of options is filtered by the input value."

P3 The first match is highlighted as you type, so Enter takes it.
- S3: "Active descendant will always land on the first item, and will be the closest match to what the user has typed. Hitting enter will select the first item unless the user traverses the list with up and down arrow keys."
- S8: "The best-matching option is highlighted as users type."
- S1 also allows "list autocomplete with manual selection", where nothing is preselected. Fluent and Carbon both preselect.

P4 The "add a new value" row is appended at the end of the list.
- S4 (`addNewItem`): "A menu item that is used to allow users make a selection that is not available in the array passed to the `items` prop. This menu item gets appended to the end of the list of options."

P5 Focus stays in the text box, and the highlighted option is exposed through aria-activedescendant. Esc closes the list without losing what was typed.
- S1: "DOM Focus is maintained on the combobox and the assistive technology focus is moved within the listbox using `aria-activedescendant`"
- S1: "users can navigate the set of available choices in a combobox popup or menu and then press `escape`, which closes the popup or menu without changing previous input."
- S1: "The popup indicator icon or button (if present), the popup, and the popup descendants are excluded from the page `Tab` sequence."

P6 Backspace in an empty box removes the last tag, and that behaviour must be communicated.
- S3: "When `TagPickerInput` is focused, pressing Backspace will remove the last tag. This behavior should be communicated to the user to avoid confusion."

P7 A visible label above the field. Never use the placeholder as a label, hint or state.
- S5: "Placeholder text is never an acceptable substitute for a label because: The placeholder text disappears as soon as the input has a value; Placeholder text colors are typically too light …; Screen readers do not read placeholder text as a label"
- S11: "Do not use placeholder text in place of a label, or for hints or examples"
- S6: "Don't use placeholder text to clarify field inputs – use the field label and helper text."

P8 Results, empty results and changes are announced, and the empty case gets its own message.
- S10: the component "will render an autocomplete `<input>` and its accompanying suggestions and `aria-live` region", with `tNoResults` (default "No results found") and `tStatusResults` messages.
- S4: `emptyStateText`, "The text that appears in the menu when there are no options".
- S12: `renderEmptyState={() => <div>No categories</div>}`.

P9 Options are short, one line each, and the unique part comes first.
- S6: "Keep options to a single line of text."
- S8: "Avoid multiple lines of text in a dropdown. If the text is too long for a single line, add an ellipsis (…) for overflow, and use a tooltip to display the full text"
- S2: "The entire name of an option is spoken as a single unit of speech when the option is read … Long names inhibit perception". Options that all repeat the same phrase "significantly degrade usability for keyboard and screen reader users."

P10 Long tag labels truncate, but the full text stays available.
- S3: "a max width can be assigned so that if text in a tag exceeds it, it will truncate."
- S9: "Chip label text should be 20 characters or fewer"
- S9: "Chip labels will truncate when in a wrapped layout".

P11 Keep the list short, and order it on purpose.
- S6: "Don't overwhelm people with too many options." "Order your list of options in a way that will make the most sense."
- S7: "Order items in an intentional way so it's easy for the merchant to find a specific value"

P12 Tap targets are large enough.
- S9: "Chips must also have a minimum 48dp target size, regardless of placement or density. If required, the target can extend beyond the visible container of the chip."

P13 Optional extras: a "Clear all" action (S3, a secondary action) and a tag that can be a link
(S12, `href` on `Tag`).

## Our design (contracts/components.md before this pass) against the principles

| P | Before | Verdict | Change |
|---|---|---|---|
| P1 | tokens in the box, × remove, the box wraps | follows | none |
| P2 | case-insensitive substring | follows | none |
| P3 | nothing highlighted; Enter with no highlight does nothing | deviates | the first visible row is highlighted on every input; Enter takes it; arrows move from there |
| P4 | "+ text" last | follows | none |
| P5 | aria-activedescendant, Esc closes, list not in Tab order | follows | none |
| P6 | Backspace removes the last token, not communicated | partly | add a screen-reader description on the input: "Backspace removes the last one." |
| P7 | the placeholder carries state ("Not in a gallery" / "Add to a gallery") | deviates | no placeholder; an empty token list shows a visible empty-state line (`EmptyText`) |
| P8 | only the too-long message | deviates | the status line announces the result count ("3 matches", "No matches") and what a pick did ("Added Coast", "Removed Coast"); when nothing can be offered, the list shows its empty text ("Already added" when the exact match is taken) |
| P9 | every public option carries a second line, "Visitors will see these photos once they're added." | deviates | options become one line: "Coast · Public". The public warning moves to ONE message, shown when a pending token points at a public option: "Visitors will see this photo in Coast after you save." (`PublicNote` format per option, generic as `Option.Warning`) |
| P10 | no truncation | gap | tokens max-width 20ch with ellipsis; the full label is in `title` and in the link text, so screen readers get it whole |
| P11 | 10 shown, title order | follows | none |
| P12 | × 24×24 (WCAG 2.2 minimum) | partly | × hit box 44×44 via padding and negative margin, the same 44 px the site uses for tap areas (010 `TextLink Tap`) |
| P13 | links on saved tokens; no Clear all | links follow; Clear all skipped | V.4: one owner, a few galleries per photo |

Not adopted, with reasons:
- Tab or blur turning typed text into a value (S8 combo box, S1 manual-selection example): for us,
  creating a gallery is a deliberate act, so it needs an explicit pick of the "+" row. Typed text
  that was never picked is not saved. The status line says "Not added" on blur when text remains.
- Inline completion (S1 "list with inline autocomplete"): it adds complexity, and S3 and S7 do
  without it.
