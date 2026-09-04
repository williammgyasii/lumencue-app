## Context

See proposal.md. Default Lower Third body is a ~273px band at y=720 (`ResolveRegions`). `DeckBuilder` already paginates against that box and subtracts region padding. The body `Grid` in `ProjectorView` is not clipped; only the background `Border` is. `OutlinedTextBlock` AutoFit still draws min size when it overflows.

## Goals / Non-Goals

**Goals:**

- Prove paging on a stock Lower Third theme with a long body
- Clip the three region grids
- Keep measurement using the same usable box (width/height − padding)

**Non-Goals:**

- Changing default Lower Third art
- Forcing AutoFit on every theme

## Decisions

1. **Keep `DeckBuilder` as the pager.** Add tests that fail if a long verse stays one overflowing slide. If measurement already pages, the test locks it; if not, tighten `Fits`.

2. **`ClipToBounds` on the region `Grid`s** (and a chrome flag so the policy is testable).

3. **Usable box helper on `Theme`** (`UsablePaginationBox`) so tests and `DeckBuilder` share one subtraction.

## Risks / Trade-offs

- [Very long single word] → still one token per page (existing wrap). Clip hides the rest.
- [AutoFit themes] → still paginate at `MinFontSize` so the renderer can grow; clip is the backstop.
