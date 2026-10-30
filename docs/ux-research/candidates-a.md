# UX Research — Photographer Portfolio Candidates A

Research for a single-owner photography portfolio (home → gallery grid → single image).
Design goals: minimal, image-first, little chrome, fast, calm, works without JavaScript.

Discovery sources checked: siteinspire.com (Photography/Minimal/Portfolio categories), similar-site suggestions on Siteinspire entries, ondisplay.website.
All sites below were loaded and confirmed live as of the research date.

---

## Tessa van den Berg

URL: https://tessavandenberg.com  
Found via: https://siteinspire.com/website/13410-tessa-van-den-berg (tagged: Grid Layout, Minimal, Photography, Personal, Portfolio; published March 2026)  
Fetch: OK

- **Home page layout:** Single scrolling column of full-frame photographs — no grid, no captions, just images stacked vertically. The only navigation element is the name (top-left) and a "contact" link (top-right).
- **Gallery page layout:** No separate gallery pages; all work surfaces on one scrolling home page.
- **Single-image view:** None observed — images are not individually clickable to a detail view.
- **Navigation style:** Bare minimum: name + single "contact" link. No hamburger, no menu bar.
- **Typography feel:** Very small, lightweight sans-serif; almost invisible.
- **Colour:** Light (white background).
- **What makes it clean:** There is practically no UI at all — the entire site is photographs and a contact email. Amsterdam-based, all 35mm film, prints available on request.
- **Works without JS:** Unlikely — built on Framer (framerusercontent.com image CDN), which requires JS to render.

---

## Lasse Fløde

URL: https://lassefloede.com  
Found via: https://siteinspire.com/website/8276-lasse-flode (tagged: Minimal, Photography, Portfolio; published August 2023)  
Fetch: OK

- **Home page layout:** Uniform grid of project cover images (portrait-format editorial stories). Each tile is a full-bleed image with the project title appearing on hover or as a caption below.
- **Gallery page layout:** Individual project pages show a vertical scroll of full-width images (no grid inside the project — one image per viewport width). Example: `/roberto-mancini-for-richard-mille` shows sequential full-width frames.
- **Single-image view:** No lightbox; images are large enough that each frame acts as its own full-page view within the scroll.
- **Navigation style:** Site name as home link; project title on each grid tile. No persistent header on project pages beyond a back arrow.
- **Typography feel:** Clean, minimal sans-serif; project titles in small caps or light weight.
- **Colour:** Light/white.
- **What makes it clean:** A pure cover-grid home feeds into a sequential image scroll per project — exactly the three-level structure (index → gallery → image) in our design goals, without any decorative chrome.
- **Works without JS:** Unlikely — built on Next.js with Sanity CMS image CDN.

---

## Stephen Kent Johnson

URL: https://stephenkentjohnson.com  
Found via: https://siteinspire.com/website/13519-stephen-kent-johnson (published August 2026; credits: Frederik Delmotte)  
Fetch: OK

- **Home page layout:** Typography-first landing — the home renders as bold project labels with publication credits in small text (e.g. "PATMOS HOME / WSJ MAGAZINE / DESIGN KATERINA TSIGARIDA"). No thumbnails on the home; projects are listed as text.
- **Gallery page layout:** `/photography` route shows the same text-list index of projects. Clicking a project presumably opens a grid or sequence of interior/architectural images (full detail not retrievable via static extract).
- **Single-image view:** Not confirmed via extract, but the editorial nature of the work suggests a single-column or lightbox view per image.
- **Navigation style:** Name · Photography · Info — three links, all text.
- **Typography feel:** Elegant mixed case, small serif or all-caps sans for project labels; very editorial.
- **Colour:** Light/white.
- **What makes it clean:** No images whatsoever on the home or gallery index — work is catalogued as pure typographic lines; the photographs only appear when you commit to a project.
- **Works without JS:** Unknown — static extract was very minimal, likely a JS-rendered SPA.

---

## Yuri Roga

URL: https://yuriroga.com  
Found via: https://siteinspire.com/website/13593-yuri-roga (published September 2026)  
Fetch: OK

- **Home page layout:** Pure text list of project names only — "T1 Energy", "Snap Implants", "Raura", "TAG Heuer x Porsche", "MWS Findings", "Air Company", "Equinox", "Sisyphe", "Mercedes Benz", "Prada", "Poclain". No thumbnails. No dates. Name at top.
- **Gallery page layout:** Individual project pages appear to be JS-rendered SPAs (direct URL fetch returned HTTP error); likely a sequence or grid of campaign images.
- **Single-image view:** Not confirmed.
- **Navigation style:** Project names as an unstyled vertical list. Extremely low-chrome.
- **Typography feel:** Appears lowercase, minimal weight sans-serif.
- **Colour:** Likely light/neutral (minimal CSS indicators in extract).
- **What makes it clean:** The home page is a raw text list with zero imagery — a deliberate choice that puts the work's quality ahead of visual sales pitch.
- **Works without JS:** Home may be static HTML (text-only), but project pages appear to require JS.

---

## Angela Ricciardi

URL: https://www.angelaricciardi.com  
Found via: https://siteinspire.com/website/13481-angela-ricciardi (published July 2026; credits: Octaeve)  
Fetch: OK

- **Home page layout:** Horizontal-scroll ribbon of mixed thumbnails (still photographs + video thumbnails from Mux), all small and uniform, presented as a continuous film-strip. Clicking likely expands to a project view.
- **Gallery page layout:** `/projects` lists works by numbered entries with type tags (Fashion, Campaign, Brand, Film, Photography) and service roles (Direction, Photography). No large thumbnails — a structured text index.
- **Single-image view:** Not confirmed; likely a full-page image/video view per project.
- **Navigation style:** Projects · Information · Archive — three links.
- **Typography feel:** Minimal sans-serif, small body text, numbered entries.
- **Colour:** Light/white.
- **What makes it clean:** The home film-strip keeps all work at thumbnail scale, resisting the impulse to blow up hero images; the projects index is a clean numbered list with metadata.
- **Works without JS:** No — built on Next.js with Sanity and Mux video; requires JS.

---

## Hélène del Marmol

URL: https://helenedelmarmol.com  
Found via: https://siteinspire.com/website/13480-helene-del-marmol (published July 2026; credits: Bureau DAM)  
Fetch: OK

- **Home page layout:** Single-column stacked list of project cover images (tall portrait frames) with project titles below each. Scrolling the home is effectively scrolling through project covers.
- **Gallery page layout:** Each project page (`/projects/massimo-dutti`, etc.) shows a vertical single-column slideshow of full-height images — one image per scroll unit, no grid.
- **Single-image view:** The project scroll is functionally a per-image view — each frame fills the screen as you scroll.
- **Navigation style:** No persistent visible nav header; project titles serve as the only navigation from the home list. A small animated candle-light gif appears as a decorative easter egg on project pages.
- **Typography feel:** Light, elegant sans-serif; project titles in small caps.
- **Colour:** Light/white, warm off-white tones in photography.
- **What makes it clean:** No navigation chrome on project pages — you arrive and the images simply fill the screen top-to-bottom, nothing framing them but white margin.
- **Works without JS:** No — Next.js with Sanity CMS.

---

## Sam Thies

URL: https://samthies.com  
Found via: https://siteinspire.com/website/13642-sam-thies (similar to Lasse Fløde; published September 2026; credits: URFD / Carl Beaverson)  
Fetch: OK

- **Home page layout:** Minimal — name as header, three nav links (Projects, Shop, About). No hero image or home thumbnails visible from static extract; landing appears to be text-only until navigating to Projects.
- **Gallery page layout:** `/projects/` renders a structured text list: project name, category tags (Photography / Motion), edition count, year. Example entries: "Viel Obscura — Archival Print Series", "Gulf Country — Archival Print Series — 2024". Very minimal metadata-driven list.
- **Single-image view:** Not confirmed via extract; print-series nature of work suggests lightbox or full-page image view.
- **Navigation style:** Projects · Shop · About; within Projects a plain text list with year + tags.
- **Typography feel:** Clean, structured sans-serif; metadata presented as small labels.
- **Colour:** Not confirmed; likely light.
- **What makes it clean:** Projects list reads like a catalogue entry — title, type, year — without decorative thumbnail previews on the index page.
- **Works without JS:** Unknown — minimal static content in extract; may be partially progressive.

---

## Alexandra Tellez

URL: https://alexandratellez.com  
Found via: https://siteinspire.com/website/13547-alexandra-tellez (published September 2026; credits: Frederik Delmotte)  
Fetch: OK

- **Home page layout:** Numbered project index — "1 | 10 Lululemon – Train", "2 | 10 KANE", "3 | 10 Yahoo", etc. — ten projects listed with the count fraction visible, functioning as a sparse numbered list.
- **Gallery page layout:** Each project page (`/lululemon-train/`) shows a mix of video stills and photographs in a grid of thumbnails, then scrolls into full-resolution images and production notes below. Outtakes shown as a numbered horizontal sequence "1 | 17 … 17 | 17".
- **Single-image view:** Within project pages, individual images are linked as larger views; outtake numbers act as a prev/next sequence.
- **Navigation style:** "Index" as the sole back-navigation link from project pages; fractional count ("1 | 10") gives orientation within the set.
- **Typography feel:** Clean sans-serif, fractional numbering as primary visual language.
- **Colour:** Light/white.
- **What makes it clean:** Home is a pure numbered text index with no imagery; the fractional counter replaces decorative pagination, giving structure without clutter. Built on WordPress (wp-content URLs visible) — may partially function without JS.
- **Works without JS:** Partially likely — WordPress backend with some static HTML; thumbnails load via standard `<img>` tags.

---

## Rejected

| Site | URL | Reason |
|---|---|---|
| Daigh Rick | daighrick.com | Landscape architecture studio ("Where Beauty Grows") — not a photographer's portfolio |
| Pawel Achtelik | pawelachtelik.com | Video editor, not a photographer; siteinspire categorised as Unusual Layout/Photography but the site owner's bio is "queer video editor based in London" |
| Kai Blamey Nguyen | kaiblamey.com | Cinematographer/DOP, not a stills photography portfolio; primary work is film/commercial video |
| Tillmann Franzen | tillmannfranzen.com | Live and minimal-ish, but heavy corporate commercial photographer site with scrolling client logo marquee (BASF, BMW, Lufthansa, Audi, Rimowa) — not minimal personal portfolio |
| Emily Rosser | emily-rosser.com | Listed on siteinspire (2021); URL not fetched to confirm live — skipped as discovery was too old relative to available confirmed candidates |
