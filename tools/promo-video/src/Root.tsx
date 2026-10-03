import { Composition } from "remotion";
import { FullCut, fullDuration, StoryCut, storyDuration } from "./Promo";

// One composition per platform: a 16:9 cut cropped to 9:16 afterwards looks bad,
// so each shape is laid out on its own (theme.ts useLayout).
export const RemotionRoot: React.FC = () => {
  return (
    <>
      <Composition
        id="YouTube"
        component={FullCut}
        durationInFrames={fullDuration}
        fps={30}
        width={1920}
        height={1080}
        defaultProps={{ version: "v0.2.1" }}
      />
      <Composition
        id="LinkedIn"
        component={FullCut}
        durationInFrames={fullDuration}
        fps={30}
        width={1200}
        height={1200}
        defaultProps={{ version: "v0.2.1" }}
      />
      <Composition
        id="InstagramStory"
        component={StoryCut}
        durationInFrames={storyDuration}
        fps={30}
        width={1080}
        height={1920}
        defaultProps={{ version: "v0.2.1" }}
      />
    </>
  );
};
