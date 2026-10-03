import React from "react";
import { linearTiming, TransitionSeries } from "@remotion/transitions";
import { fade } from "@remotion/transitions/fade";
import { slide } from "@remotion/transitions/slide";
import { useVideoConfig } from "remotion";
import { EndCard, Feature, Hook, PowerUsers, Reveal, Unison } from "./scenes";

export type PromoProps = { version: string };

/** Frames each cross-fade or slide overlaps its two scenes by. */
export const T = 12;

const fadeT = <TransitionSeries.Transition presentation={fade()} timing={linearTiming({ durationInFrames: T })} />;
const slideT = (
  <TransitionSeries.Transition
    presentation={slide({ direction: "from-right" })}
    timing={linearTiming({ durationInFrames: T })}
  />
);

// Scene lengths in frames at 30 fps. The full cut is ~34 s, for YouTube and LinkedIn.
export const FULL = { hook: 90, reveal: 90, unison: 210, feature: 105, power: 120, end: 180 };
export const fullDuration =
  FULL.hook + FULL.reveal + FULL.unison + 4 * FULL.feature + FULL.power + FULL.end - 8 * T;

/** YouTube and LinkedIn: the problem, the panel, unison, four features, the command line, the end card. */
export const FullCut: React.FC<PromoProps> = ({ version }) => {
  const { fps } = useVideoConfig();
  return (
    <TransitionSeries>
      <TransitionSeries.Sequence name="Hook" durationInFrames={90} premountFor={fps}>
        <Hook />
      </TransitionSeries.Sequence>
      {fadeT}
      <TransitionSeries.Sequence name="Reveal" durationInFrames={90} premountFor={fps}>
        <Reveal />
      </TransitionSeries.Sequence>
      {fadeT}
      <TransitionSeries.Sequence name="Unison" durationInFrames={210} premountFor={fps}>
        <Unison />
      </TransitionSeries.Sequence>
      {fadeT}
      <TransitionSeries.Sequence name="Night light" durationInFrames={105} premountFor={fps}>
        <Feature src="brightness.png" title="Night light, per display" caption="Warmer and dimmer than Windows allows, down to 1900K." />
      </TransitionSeries.Sequence>
      {slideT}
      <TransitionSeries.Sequence name="OLED care" durationInFrames={105} premountFor={fps}>
        <Feature src="screen-care.png" title="OLED care" caption="Rests each display on its own, and pauses for video." />
      </TransitionSeries.Sequence>
      {slideT}
      <TransitionSeries.Sequence name="Taskbar glass" durationInFrames={105} premountFor={fps}>
        <Feature src="taskbar.png" title="Taskbar glass" caption="Blur, clear or acrylic, in your own colour." />
      </TransitionSeries.Sequence>
      {slideT}
      <TransitionSeries.Sequence name="Presets" durationInFrames={105} premountFor={fps}>
        <Feature src="presets.png" title="Presets" chip="Beta" caption="Your whole desk, back in a click." />
      </TransitionSeries.Sequence>
      {fadeT}
      <TransitionSeries.Sequence name="Power users" durationInFrames={120} premountFor={fps}>
        <PowerUsers />
      </TransitionSeries.Sequence>
      {fadeT}
      <TransitionSeries.Sequence name="End card" durationInFrames={180} premountFor={fps}>
        <EndCard version={version} />
      </TransitionSeries.Sequence>
    </TransitionSeries>
  );
};

// The story cut is ~22 s: Instagram plays stories up to 60 s, but short holds attention.
export const storyDuration = 75 + 75 + 180 + 90 + 90 + 150 - 5 * T;

/** Instagram story: the problem, the panel, unison, two features, the end card. */
export const StoryCut: React.FC<PromoProps> = ({ version }) => {
  const { fps } = useVideoConfig();
  return (
    <TransitionSeries>
      <TransitionSeries.Sequence name="Hook" durationInFrames={75} premountFor={fps}>
        <Hook />
      </TransitionSeries.Sequence>
      {fadeT}
      <TransitionSeries.Sequence name="Reveal" durationInFrames={75} premountFor={fps}>
        <Reveal />
      </TransitionSeries.Sequence>
      {fadeT}
      <TransitionSeries.Sequence name="Unison" durationInFrames={180} premountFor={fps}>
        <Unison />
      </TransitionSeries.Sequence>
      {fadeT}
      <TransitionSeries.Sequence name="OLED care" durationInFrames={90} premountFor={fps}>
        <Feature src="screen-care.png" title="OLED care" caption="Rests each display on its own, and pauses for video." />
      </TransitionSeries.Sequence>
      {slideT}
      <TransitionSeries.Sequence name="Taskbar glass" durationInFrames={90} premountFor={fps}>
        <Feature src="taskbar.png" title="Taskbar glass" caption="Blur, clear or acrylic, in your own colour." />
      </TransitionSeries.Sequence>
      {fadeT}
      <TransitionSeries.Sequence name="End card" durationInFrames={150} premountFor={fps}>
        <EndCard version={version} />
      </TransitionSeries.Sequence>
    </TransitionSeries>
  );
};
