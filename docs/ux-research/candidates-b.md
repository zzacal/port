# Portfolio Site Candidates — Batch B

Research for a single-owner photography portfolio UX.
Design goals: minimal, image-first, little chrome, fast, calm, no-JS public site.

---

## Nadav Kander

URL: https://www.nadavkander.com  
Found via: https://siiimple.com/nadav-kander/ (siiimple curated gallery)  
Fetch: OK

- **Home page layout:** Auto-advancing horizontal slideshow of recent works and news items. Each slide is a single image that links out to an external context (gallery, magazine, show). Very little chrome — just the image and a wordmark + horizontal nav at top.
- **Gallery page layout:** Text index. `/works-in-series` lists every series as a titled hyperlink followed by "Series of N" — no thumbnails, purely typographic. Clicking a series enters a `/single` route.
- **Single-image view:** Full-bleed image centred on a white field, title below. Clicking moves to the next image; no explicit prev/next buttons visible in the extracted markup, navigation appears to be click-anywhere.
- **Navigation style:** Slim horizontal top nav, one level: Studio Feed / Works in Series / Portraits / Commissions / Film / Archive / Monographs / Exhibitions & Talks / Essays & Reviews / News / Biography / Contact. No hamburger.
- **Typography feel:** Clean, neutral serif wordmark; nav in small sans-serif. Very restrained type hierarchy.
- **Colour:** Light — white ground, black text.
- **What makes it clean:** The gallery index deliberately withholds thumbnails, forcing attention onto series titles and counts; the work reveals itself only after a click.
- **Works without JS?** Probably not fully — slideshow home requires JS. Text-index and single-image pages may degrade acceptably, but cannot confirm without a JS-disabled browser test.

---

## Rinko Kawauchi

URL: https://rinkokawauchi.com/en/  
Found via: https://siiimple.com/rinko-kawauchi/ (siiimple curated gallery)  
Fetch: OK

- **Home page layout:** Almost empty — just the photographer's name as a wordmark with a slim horizontal nav. No hero image. Text-only entry point.
- **Gallery page layout:** `/en/works/` displays a uniform grid of titled square cover images, each labelled with series title and year. Padding is generous; the grid reads as calm and deliberate.
- **Single-image view:** Individual series pages show images one at a time or in a vertical sequence (varies by series). Caption appears as small text beneath. No explicit prev/next arrows visible but the structure supports linear movement through a series.
- **Navigation style:** Horizontal top bar: News / Biography / Works / Publications / Articles / Contact / Instagram / EN / JP. No dropdown or hamburger.
- **Typography feel:** Minimal sans-serif in small point size; names rendered in mixed case. Very quiet.
- **Colour:** Light — white ground, black text.
- **What makes it clean:** The near-empty home page is an extreme statement of restraint — the photographer's name is the whole interface until you choose where to go.
- **Works without JS?** The site is WordPress-based and the structure of pages loads without visible JS dependencies. Likely functional without JS.

---

## Michael Kenna

URL: https://michaelkenna.com/index2.php  
Found via: https://siiimple.com/category/photography/ (search/research); confirmed well-known landscape B&W photographer  
Fetch: OK

- **Home page layout:** Dark-ground page. A large featured splash image fills the upper viewport, overlaid with the photographer's logotype. Below: a two-column text block with upcoming exhibitions and new books. Simple, bespoke PHP site, no platform.
- **Gallery page layout:** `/gallery.php?id=11` — uniform grid of small square thumbnails on a dark background, arranged in dense rows. Contact-sheet aesthetic. No text labels on thumbnails.
- **Single-image view:** Clicking a thumbnail loads `/image.php` — image centred, title and edition info below. The page is functional and austere.
- **Navigation style:** Horizontal nav at top: Recent Work / Image Archive / Upcoming / Resume / Interviews / Publications / Commercial Work / Gallery Representatives / Store / Contact. Also a Print Search form and a Title Index link. Old-school but uncluttered.
- **Typography feel:** Understated serif logotype; nav in small caps sans. The retro feel is coherent with the B&W film aesthetic.
- **Colour:** Dark — near-black ground, white/light text and images on dark.
- **What makes it clean:** A bespoke PHP site with no framework overhead; the uniform dark thumbnail grid echoes the contact-sheet print tradition and avoids any decorative flourish.
- **Works without JS?** Very likely — the site is classic server-rendered PHP with `<img>` thumbnails and anchor links. No JS framework detected.

---

## Tyler Mitchell

URL: https://tylermitchell.co/  
Found via: https://siiimple.com/tyler-mitchell/ (siiimple curated gallery); photographer's official site confirmed via Instagram bio link  
Fetch: OK

- **Home page layout:** White page, photographer's name top-left, slim horizontal nav (About / photography / films / exhibitions / books / all). Below: a dense, multi-column contact-sheet grid of small thumbnails — hundreds of images in even rows. A size toggle (50 / 200 / 500) controls thumbnail density.
- **Gallery page layout:** `/photography` — two-column masonry-style grid of larger images at natural aspect ratios. White background, no captions on thumbnails.
- **Single-image view:** Not prominently exposed; the site is more of a stream/archive than a per-image detail view.
- **Navigation style:** Minimal top bar, five items. No dropdown, no hamburger. Size-density toggle is the most unusual control.
- **Typography feel:** Small, modern sans-serif. The photographer's name appears in regular weight, not oversized. Very utilitarian.
- **Colour:** Light — white ground.
- **What makes it clean:** The density toggle is a distinctive, honest approach — showing all the work at once rather than curating a "best of" selection, which removes the usual editorial pretension.
- **Works without JS?** No — the site is a Sanity-backed React/Next.js app; thumbnails are served via CDN URLs dynamically. Would not function without JS.

---

## Danielle Levitt

URL: https://www.daniellelevitt.com/  
Found via: https://minimal.gallery/danielle-levitt/ (Minimal Gallery curated list)  
Fetch: OK

- **Home page layout:** White ground. Large, full-width images stacked vertically in a single column — each linked to either a "projects" or "portraits" category, with the label shown as a small text overlay. The photographer's name appears at the top left; a short bio block near the bottom of the page with contact details.
- **Gallery page layout:** `/projects` and `/portraits` — vertical single-column scroll of large images. No masonry, no grid. Images run full browser width.
- **Single-image view:** Not a separate route; images expand or link to project/portrait detail pages with image sequences.
- **Navigation style:** No persistent nav bar — just the wordmark at top-left and category labels embedded inline on the home page scroll. A text "Index" link at the bottom. Very minimal.
- **Typography feel:** Light sans-serif wordmark; body text is small, neutral, almost invisible. The design removes typography from competition with images.
- **Colour:** Light — white ground, black text.
- **What makes it clean:** There is almost no navigation chrome; the entire site is images stacked in sequence, and every element of UI has been compressed to near-zero.
- **Works without JS?** Unclear — the site uses DatoCMS-sourced images with `auto=format` parameters. Basic image display likely survives without JS, but progressive enhancement is uncertain.

---

## Samuel Angibaud

URL: https://www.samuelangibaud.com/  
Found via: https://www.framer.com/blog/photography-portfolio-websites/ (Framer blog editorial list); also https://minimal.gallery/angibaud-samuel/  
Fetch: OK

- **Home page layout:** Minimal split layout. The homepage presents two large entry cards side by side: "Design" (7 Projects) and "Images" (36 Images), each with a single representative image. A one-line tagline and contact/Instagram link at the bottom. Extremely spare.
- **Gallery page layout:** `/photography` — vertical single-column scroll of images at natural sizes on a white field. No grid, no labels on thumbnails. The Framer blog notes that clicking anywhere flips to the next image, creating a monograph-like experience.
- **Single-image view:** Full-screen per-image navigation with click-to-advance. Minimal caption, if any.
- **Navigation style:** Top: just "Design" and "Images" as two nav items (dual-discipline). No hamburger. Footer: year, location, Instagram, Contact.
- **Typography feel:** Clean, modern sans-serif in small size. Very Parisian minimalism — precise, not decorative.
- **Colour:** Light — white ground.
- **What makes it clean:** Reduces the entire site to a binary choice at the home page; everything extraneous is removed, and the photography section behaves like a printed monograph.
- **Works without JS?** No — built in Framer (React-based). Requires JS for rendering.

---

## Ami Vitale

URL: https://amivitale.com/  
Found via: https://ueni.com/blog/best-photographer-website (ueni "30 Best Photographer Websites" editorial list)  
Fetch: OK

- **Home page layout:** White/neutral ground. Text-heavy home with a bio paragraph, a "Stories" section as a text list of titled links, newsletter signup, and a Nikon Ambassador badge. No hero image on the home page — the content index is intentionally typographic.
- **Gallery page layout:** `/stories/` — plain text list of story/essay titles as hyperlinks, no thumbnails. Each story then opens a dedicated page with a narrative text block followed by a scrolling horizontal image gallery.
- **Single-image view:** Per-story pages load a photoshelter-hosted slideshow widget; images display with full captions below (who, what, where, date, agency). Captions are detailed and journalistic.
- **Navigation style:** Horizontal top nav: Stories / Video / Learn / Blog / Shop / About. Clean, one level.
- **Typography feel:** Readable, journalistic sans-serif. Larger body text than most portfolio sites; readability over aesthetics.
- **Colour:** Light — white/neutral.
- **What makes it clean:** The home page and story index are pure text — no decorative images — which forces the photography to earn attention only when you enter a story, giving each project full stage.
- **Works without JS?** Partially — the WordPress site structure renders without JS; the photoshelter gallery widget requires JS for slideshow. Static text content is JS-independent.

---

## Alex Strohl

URL: https://www.alexstrohl.com/  
Found via: https://ueni.com/blog/best-photographer-website (ueni "30 Best Photographer Websites" editorial list)  
Fetch: OK

- **Home page layout:** Single-column vertical scroll of full-bleed images, each expanding on click via a "View fullsize" lightbox. Below the scroll: a commercial clients section and a brand-name marquee. The site is Squarespace-powered. Tagline ("Co-founder Offpiste / Filmmaker / Photographer") appears at top.
- **Gallery page layout:** Same single-column format. There is no separate "gallery" page — the home page IS the portfolio, one image after another in a long scroll.
- **Single-image view:** Lightbox triggered by "View fullsize" on each image. No dedicated route per image.
- **Navigation style:** Cart icon top-right (for print shop); otherwise the site has no visible traditional nav on the scrolling portfolio. A separate commercial work section lower on the page links to named campaign projects.
- **Typography feel:** Minimal, monospaced-adjacent sans for the tagline. Brand names in uppercase. Very restrained.
- **Colour:** Neutral to dark — images often full-bleed with dark backgrounds; white text on images.
- **What makes it clean:** Removing any navigation and letting the single-column image stream be the entire site is the boldest possible expression of "image first." The site has almost no UI.
- **Works without JS?** No — Squarespace relies on JS for layout and lightbox. Image `src` URLs would still render, but the intended layout and interaction require JS.

---

## Rejected

| Site | URL checked | Reason |
|---|---|---|
| Typical Mitul | typicalmitul.com | Live, but a Toronto-based commercial hire photographer, not well-known in landscape/street/fine-art; the site is built in Next.js with JS-only rendering |
| laurencegibbs.com | From stylecloud.co list | Wedding/lifestyle photographer, not a well-known individual in the target genres (landscape, travel, street, fine-art, documentary) |
| www.alexstrohl.com/work | Direct URL attempt | HTTP error on /work sub-path; main domain used instead |
| tylermitchell.photos | Attempted | Blocked (private/internal network address per web-extract error) — used tylermitchell.co instead which loaded correctly |
| ryanmadfuckinchan.com | From minimalio.org list | Interesting design (single-column zine), but photographer is not widely established/well-known in editorial contexts |
| www.samuelangibaud.com (Design) | Split site | Angibaud is dual-discipline (design + photography); included for photography section only, which is legitimately strong |
