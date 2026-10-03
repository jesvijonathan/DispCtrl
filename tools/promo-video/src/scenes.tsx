import React from "react";
import {
  AbsoluteFill,
  Easing,
  Img,
  interpolate,
  spring,
  staticFile,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
import { Desk } from "./Desk";
import { Backdrop, Chip, Key, Rise, Window } from "./parts";
import { colour, font, gradientText, useLayout } from "./theme";

const Headline: React.FC<{ size?: number; children: React.ReactNode; style?: React.CSSProperties }> = ({
  size = 96,
  children,
  style,
}) => {
  const { u, shape } = useLayout();
  const fit = shape === "wide" ? 1 : 0.86;
  return (
    <div
      style={{
        fontFamily: font,
        fontSize: size * u * fit,
        fontWeight: 700,
        lineHeight: 1.08,
        letterSpacing: "-0.02em",
        color: colour.text,
        textWrap: "balance",
        ...style,
      }}
    >
      {children}
    </div>
  );
};

const Body: React.FC<{ children: React.ReactNode; style?: React.CSSProperties }> = ({ children, style }) => {
  const { u } = useLayout();
  return (
    <div style={{ fontFamily: font, fontSize: 44 * u, lineHeight: 1.3, color: colour.muted, textWrap: "balance", ...style }}>
      {children}
    </div>
  );
};

/** Centred column inside the safe area, used by every scene. */
const Stage: React.FC<{ children: React.ReactNode; gap?: number; row?: boolean }> = ({ children, gap = 50, row }) => {
  const { u, safeTop, safeBottom, shape } = useLayout();
  return (
    <AbsoluteFill>
      <Backdrop />
      <AbsoluteFill
        style={{
          padding: `${safeTop}px ${(shape === "wide" ? 130 : 80) * u}px ${safeBottom}px`,
          display: "flex",
          flexDirection: row ? "row" : "column",
          alignItems: "center",
          justifyContent: "center",
          gap: gap * u,
          textAlign: row ? "left" : "center",
        }}
      >
        {children}
      </AbsoluteFill>
    </AbsoluteFill>
  );
};

// ------------------------------------------------------------------ hook

export const Hook: React.FC = () => {
  const { shape } = useLayout();
  return (
    <Stage gap={70}>
      <div>
        <Rise delay={4}>
          <Headline>Two screens.</Headline>
        </Rise>
        <Rise delay={18}>
          <Headline style={{ color: colour.muted }}>Two brightness levels.</Headline>
        </Rise>
      </div>
      <Rise delay={8} distance={20}>
        <Desk monitor={32} laptop={100} scale={shape === "wide" ? 1 : 0.92} />
      </Rise>
    </Stage>
  );
};

// ------------------------------------------------------------------ reveal

export const Reveal: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const { u, shape, height } = useLayout();
  // Up from below the frame, as the panel rises from the taskbar.
  const rise = spring({ frame: frame - 4, fps, config: { damping: 18, mass: 0.9 } });
  const panelHeight = (shape === "wide" ? 860 : shape === "square" ? 820 : 1000) * u;
  const panel = (
    <div style={{ translate: `0px ${(1 - rise) * height * 0.8}px` }}>
      <Window src="quick-panel.png" width={(panelHeight * 720) / 1160} />
    </div>
  );
  const name = (
    <div style={{ display: "flex", flexDirection: "column", alignItems: shape === "tall" ? "center" : "flex-start", gap: 24 * u }}>
      <Rise delay={10}>
        <Img src={staticFile("logo.png")} style={{ width: 120 * u, height: 120 * u }} />
      </Rise>
      <Rise delay={16}>
        <Headline size={120} style={gradientText}>
          DispCtrl
        </Headline>
      </Rise>
      <Rise delay={24}>
        <Body>Click the tray icon.</Body>
      </Rise>
    </div>
  );
  return shape !== "tall" ? (
    <Stage row gap={shape === "wide" ? 140 : 70}>
      {name}
      {panel}
    </Stage>
  ) : (
    <Stage gap={50}>
      {name}
      {panel}
    </Stage>
  );
};

// ------------------------------------------------------------------ unison, the money shot

/** The panel's "All displays" row: its label, the slider and the percentage. */
const UnisonSlider: React.FC<{ value: number; width: number }> = ({ value, width }) => {
  const { u } = useLayout();
  return (
    <div
      style={{
        width,
        padding: `${30 * u}px ${36 * u}px`,
        borderRadius: 22 * u,
        background: colour.card,
        border: `${1.5 * u}px solid ${colour.line}`,
        boxShadow: `0 ${30 * u}px ${70 * u}px rgba(0,0,0,.5)`,
        fontFamily: font,
        textAlign: "left",
      }}
    >
      <div style={{ display: "flex", justifyContent: "space-between", color: colour.text, fontSize: 36 * u, fontWeight: 600 }}>
        <span>All displays</span>
        <span style={{ color: colour.muted, fontVariantNumeric: "tabular-nums" }}>{Math.round(value)}%</span>
      </div>
      <div style={{ position: "relative", height: 34 * u, marginTop: 20 * u }}>
        <div style={{ position: "absolute", top: 13 * u, left: 0, right: 0, height: 8 * u, borderRadius: 8 * u, background: "#3a3346" }} />
        <div
          style={{
            position: "absolute",
            top: 13 * u,
            left: 0,
            width: `${value}%`,
            height: 8 * u,
            borderRadius: 8 * u,
            background: `linear-gradient(90deg, ${colour.a2}, ${colour.a1})`,
          }}
        />
        <div
          style={{
            position: "absolute",
            top: 0,
            left: `calc(${value}% - ${17 * u}px)`,
            width: 34 * u,
            height: 34 * u,
            borderRadius: "50%",
            background: colour.a1,
            border: `${7 * u}px solid #2a2338`,
            boxShadow: `0 0 0 ${2 * u}px ${colour.a1}`,
          }}
        />
      </div>
    </div>
  );
};

export const Unison: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const { u, shape } = useLayout();
  const ease = { extrapolateLeft: "clamp", extrapolateRight: "clamp", easing: Easing.bezier(0.45, 0, 0.2, 1) } as const;
  // Before: as the hook left them. After: one value, with a spring and a flash.
  const snap = spring({ frame: frame - 26, fps, config: { damping: 11, mass: 0.7 } });
  const level = interpolate(frame, [60, 110, 130, 175], [30, 85, 85, 50], ease);
  const monitor = interpolate(snap, [0, 1], [32, level]);
  const laptop = interpolate(snap, [0, 1], [100, level]);
  const flash = interpolate(frame, [26, 30, 44], [0, 1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const sliderWidth = (shape === "wide" ? 620 : 820) * u;
  return (
    <Stage gap={shape === "wide" ? 60 : 56}>
      <div>
        <Rise delay={2}>
          <Headline>One slider. Every display.</Headline>
        </Rise>
        <Rise delay={30}>
          <Headline style={gradientText}>In step.</Headline>
        </Rise>
      </div>
      <div
        style={{
          display: "flex",
          flexDirection: shape === "wide" ? "row" : "column",
          alignItems: "center",
          gap: (shape === "wide" ? 100 : 56) * u,
        }}
      >
        <Desk monitor={monitor} laptop={laptop} flash={flash} labels scale={shape === "wide" ? 0.9 : 0.88} />
        <Rise delay={8}>
          <UnisonSlider value={interpolate(snap, [0, 1], [30, level])} width={sliderWidth} />
        </Rise>
      </div>
    </Stage>
  );
};

// ------------------------------------------------------------------ one feature, on a real screenshot

export const Feature: React.FC<{ src: string; title: string; caption: string; chip?: string }> = ({
  src,
  title,
  caption,
  chip,
}) => {
  const frame = useCurrentFrame();
  const { durationInFrames } = useVideoConfig();
  const { u, shape, width } = useLayout();
  const drift = interpolate(frame, [0, durationInFrames], [1, 1.045]);
  const text = (
    <div style={{ maxWidth: shape === "wide" ? 620 * u : undefined }}>
      <Rise delay={2}>
        <Headline size={shape === "wide" ? 88 : 92}>
          {title}
          {chip && (
            <span style={{ marginLeft: 22 * u, verticalAlign: "middle" }}>
              <Chip>{chip}</Chip>
            </span>
          )}
        </Headline>
      </Rise>
      <Rise delay={10}>
        <Body style={{ marginTop: 22 * u }}>{caption}</Body>
      </Rise>
    </div>
  );
  const shot = (
    <Rise delay={4} distance={60}>
      <div style={{ scale: drift }}>
        <Window src={src} width={shape === "wide" ? 1000 * u : width - 2 * 80 * u} />
      </div>
    </Rise>
  );
  return shape === "wide" ? (
    <Stage row gap={90}>
      {text}
      {shot}
    </Stage>
  ) : (
    <Stage gap={70}>
      {text}
      {shot}
    </Stage>
  );
};

// ------------------------------------------------------------------ power users

export const PowerUsers: React.FC = () => {
  const { u, shape, width } = useLayout();
  return (
    <Stage gap={60}>
      <Rise delay={2}>
        <Headline size={shape === "wide" ? 84 : 88}>
          Shortcuts and a command line
          <br />
          <span style={gradientText}>for everything.</span>
        </Headline>
      </Rise>
      <Rise delay={8} distance={60}>
        <Window src="cli.png" width={shape === "wide" ? 1050 * u : width - 2 * 80 * u} />
      </Rise>
      <Rise delay={16}>
        <div style={{ display: "flex", flexWrap: "wrap", justifyContent: "center", rowGap: 16 * u }}>
          <span style={{ marginRight: 30 * u }}>
            <Key>Ctrl</Key>
            <Key>Alt</Key>
            <Key>PgUp</Key>
          </span>
          <span>
            <Key>Ctrl</Key>
            <Key>Alt</Key>
            <Key>D</Key>
          </span>
        </div>
      </Rise>
    </Stage>
  );
};

// ------------------------------------------------------------------ end card

export const EndCard: React.FC<{ version: string }> = ({ version }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const { u } = useLayout();
  const pop = spring({ frame: frame - 2, fps, config: { damping: 12 } });
  return (
    <Stage gap={40}>
      <Img
        src={staticFile("logo.png")}
        style={{ width: 170 * u, height: 170 * u, scale: interpolate(pop, [0, 1], [0.6, 1]), opacity: pop }}
      />
      <Rise delay={8}>
        <Headline size={128} style={gradientText}>
          DispCtrl
        </Headline>
      </Rise>
      <Rise delay={14}>
        <Body style={{ color: colour.text, fontSize: 50 * u }}>Every monitor on your desk, controlled as one.</Body>
      </Rise>
      <Rise delay={24}>
        <div style={{ display: "flex", gap: 18 * u, flexWrap: "wrap", justifyContent: "center", marginTop: 16 * u }}>
          <Chip accent>Get it on the Microsoft Store</Chip>
          <Chip>github.com/jesvijonathan/DispCtrl</Chip>
        </div>
      </Rise>
      <Rise delay={32}>
        <Body style={{ fontSize: 32 * u }}>Free and open source · Windows 11 · {version}</Body>
      </Rise>
    </Stage>
  );
};
