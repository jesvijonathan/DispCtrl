# Release video

The DispCtrl release video, in [Remotion](https://www.remotion.dev): React drawn frame by frame and rendered to MP4. One set of scenes lays itself out for three platforms.

| Composition | Size | Length |
|---|---|---|
| `YouTube` | 1920×1080 | 34 s |
| `LinkedIn` | 1200×1200 | 34 s |
| `InstagramStory` | 1080×1920 | 20 s (text kept clear of Instagram's own top and bottom bars) |

```powershell
npm install
npm run dev                                   # Remotion Studio: preview and edit live
npx remotion render YouTube out/youtube.mp4 --codec=h264 --crf=18
npx remotion still YouTube out/frame.png --frame 300
```

For a new release, change `version` in `src/Root.tsx`, and copy fresh screenshots from `site/assets/screenshots` into `public/` when the app has changed. Rendered videos go to `out/`, which is not committed.

- `src/Promo.tsx` - the two cuts and their scene timings
- `src/scenes.tsx` - hook, reveal, unison, features, power users, end card
- `src/Desk.tsx` - the monitor and laptop whose brightness the video moves
- `src/theme.ts` - the website's colours and font, and the layout for each shape

Never shipped: nothing under `tools/` goes into a release package. Remotion is free for individuals and teams of up to three ([licence](https://www.remotion.dev/license)).
