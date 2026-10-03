import React from "react";
import {
  AbsoluteFill,
  Easing,
  Img,
  interpolate,
  staticFile,
  useCurrentFrame,
} from "remotion";
import { colour, font, useLayout } from "./theme";

/** The site's dark background with two slow purple glows drifting behind everything. */
export const Backdrop: React.FC = () => {
  const frame = useCurrentFrame();
  const { width, height } = useLayout();
  const size = Math.max(width, height) * 0.7;
  return (
    <AbsoluteFill style={{ backgroundColor: colour.bg, overflow: "hidden" }}>
      <div
        style={{
          position: "absolute",
          width: size,
          height: size,
          borderRadius: "50%",
          background: `radial-gradient(circle, rgba(139,92,246,.30), transparent 65%)`,
          left: width * 0.55 - size / 2 + Math.sin(frame / 90) * 60,
          top: height * 0.25 - size / 2 + Math.cos(frame / 110) * 40,
        }}
      />
      <div
        style={{
          position: "absolute",
          width: size * 0.8,
          height: size * 0.8,
          borderRadius: "50%",
          background: `radial-gradient(circle, rgba(240,166,216,.14), transparent 65%)`,
          left: width * 0.2 - size * 0.4 + Math.cos(frame / 100) * 50,
          top: height * 0.8 - size * 0.4 + Math.sin(frame / 120) * 50,
        }}
      />
    </AbsoluteFill>
  );
};

/** Fades and lifts its children in, starting at `delay` frames into the scene. */
export const Rise: React.FC<{
  delay?: number;
  distance?: number;
  children: React.ReactNode;
  style?: React.CSSProperties;
}> = ({ delay = 0, distance = 40, children, style }) => {
  const frame = useCurrentFrame();
  const { u } = useLayout();
  const t = interpolate(frame, [delay, delay + 18], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.bezier(0.16, 1, 0.3, 1),
  });
  return (
    <div
      style={{
        opacity: t,
        translate: `0px ${(1 - t) * distance * u}px`,
        ...style,
      }}
    >
      {children}
    </div>
  );
};

/** A screenshot in a window: rounded, outlined, lifted off the background. */
export const Window: React.FC<{
  src: string;
  width: number;
  style?: React.CSSProperties;
}> = ({ src, width, style }) => {
  const { u } = useLayout();
  return (
    <div
      style={{
        width,
        borderRadius: 18 * u,
        overflow: "hidden",
        border: `${Math.max(1, 1.5 * u)}px solid ${colour.line}`,
        boxShadow: `0 ${40 * u}px ${90 * u}px rgba(0,0,0,.55), 0 0 ${80 * u}px rgba(139,92,246,.18)`,
        lineHeight: 0,
        ...style,
      }}
    >
      <Img src={staticFile(src)} style={{ width: "100%" }} />
    </div>
  );
};

export const Chip: React.FC<{ children: React.ReactNode; accent?: boolean }> = ({
  children,
  accent,
}) => {
  const { u } = useLayout();
  return (
    <span
      style={{
        display: "inline-block",
        fontFamily: font,
        fontSize: 30 * u,
        fontWeight: 600,
        padding: `${8 * u}px ${20 * u}px`,
        borderRadius: 999,
        color: accent ? colour.onAccent : colour.muted,
        background: accent
          ? `linear-gradient(135deg, ${colour.a1}, ${colour.a2})`
          : "rgba(255,255,255,.05)",
        border: accent ? "none" : `${1.5 * u}px solid ${colour.line}`,
        whiteSpace: "nowrap",
      }}
    >
      {children}
    </span>
  );
};

export const Key: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { u } = useLayout();
  return (
    <span
      style={{
        display: "inline-block",
        fontFamily: font,
        fontSize: 34 * u,
        fontWeight: 600,
        color: colour.text,
        padding: `${8 * u}px ${18 * u}px`,
        borderRadius: 12 * u,
        background: colour.card,
        border: `${1.5 * u}px solid ${colour.line}`,
        borderBottomWidth: 5 * u,
        marginRight: 10 * u,
      }}
    >
      {children}
    </span>
  );
};
