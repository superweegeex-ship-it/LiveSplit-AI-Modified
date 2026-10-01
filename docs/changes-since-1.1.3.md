# Changes since 1.1.3

## Fixes and responsiveness

- Fixed overlapping glyph contours that could make outlined text glitch into itself with some fonts.
- Improved layout-settings opening speed and reduced redundant layout and redraw work.
- Improved settings responsiveness under excessive refresh load. High-refresh-rate stalls remain a possible bug, but have been reduced to more reasonable levels; this is not a claim that every stall is fixed.
- Fixed unnecessary indentation on split rows without icons.
- Improved Text and World Record text sizing and clipping so text stays within the available space instead of overlapping icons or adjacent text.
- Added selectable World Record text-shortening formats, including automatic, full/short labels, time and runner, and time only.
- Empty background-image entries no longer cause layout loading to fail.

## Layout customization

- Splits: adjustable column spacing and icon-to-text spacing, including negative spacing for tighter layouts.
- Splits: horizontal icon-position control and an option to hide the current split's icon and shadow while reclaiming the indentation.
- Title: independent text and icon position sliders.
- Title, World Record, and Text: custom picture selection and icon sizing from 10% to 200%, with aspect ratios preserved.
- Text: optional game/custom icons, left/right placement, separate text/icon offsets, and layout-based icon shadows.
- Detailed Timer: an option to put the segment timer above the main timer, plus a separate checkbox to keep the left-side icon and comparison times in their original positions.

## External media

- Added external file references for layout background images, component pictures, and game/segment icons in split files.
- Newly selected background/component pictures are saved as paths. Existing embedded images still load; existing files are not automatically bulk-converted.
- External split icons remain external when saving or cloning a run. Missing linked pictures do not discard the run or layout.
- Externalizing images can greatly reduce layout/split file sizes without removing times, comparisons, or attempt history. A timer-performance improvement from smaller files has not been established.

## Notes

- Keep linked image files available at their saved paths. Moving or sharing a layout/run also requires its images and valid paths; older builds may not display these linked images.
- Video initialization/render-context failures are still a known issue. Removing video settings from affected layouts was a recovery workaround, not a fix for the video backend.
- No Roboto font files or personal layouts, split files, or media are included in these source changes.
