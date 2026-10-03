import React from "react";
import { colour, font, useLayout } from "./theme";

/**
 * A monitor and a laptop, as on the reference desk: the external display on the
 * left, the laptop beside it. Each screen's brightness is 0-100; the same
 * wallpaper dims under a black veil, and a bright screen throws a glow.
 */
export const Desk: React.FC<{
  monitor: number;
  laptop: number;
  scale: number;
  flash?: number;
  labels?: boolean;
}> = ({ monitor, laptop, scale, flash = 0, labels }) => {
  const { u } = useLayout();
  const k = scale * u;
  return (
    <div style={{ display: "flex", alignItems: "flex-end", gap: 60 * k }}>
      <div style={{ display: "flex", flexDirection: "column", alignItems: "center" }}>
        <Screen w={560 * k} h={315 * k} level={monitor} flash={flash} radius={10 * k} bezel={10 * k} />
        <div style={{ width: 40 * k, height: 70 * k, background: "#2a2338" }} />
        <div style={{ width: 200 * k, height: 14 * k, borderRadius: 8 * k, background: "#2a2338" }} />
        {labels && <Label k={k}>DELL U2424H</Label>}
      </div>
      <div style={{ display: "flex", flexDirection: "column", alignItems: "center" }}>
        <Screen w={330 * k} h={206 * k} level={laptop} flash={flash} radius={10 * k} bezel={9 * k} />
        <div
          style={{
            width: 380 * k,
            height: 18 * k,
            borderRadius: `0 0 ${14 * k}px ${14 * k}px`,
            background: "#2a2338",
          }}
        />
        {labels && <Label k={k}>Internal 2880x1800</Label>}
      </div>
    </div>
  );
};

const Label: React.FC<{ k: number; children: React.ReactNode }> = ({ k, children }) => (
  <div
    style={{
      marginTop: 18 * k,
      fontFamily: font,
      fontSize: 32 * k,
      color: colour.muted,
      fontWeight: 600,
    }}
  >
    {children}
  </div>
);

const Screen: React.FC<{
  w: number;
  h: number;
  level: number;
  flash: number;
  radius: number;
  bezel: number;
}> = ({ w, h, level, flash, radius, bezel }) => {
  const veil = (1 - level / 100) * 0.88;
  const glow = Math.max(0, level - 40) / 60;
  return (
    <div
      style={{
        width: w,
        height: h,
        padding: bezel,
        borderRadius: radius,
        background: "#1c1727",
        boxShadow: `0 0 ${w * 0.35 * glow}px rgba(196,166,255,${0.55 * glow})`,
      }}
    >
      <div
        style={{
          position: "relative",
          width: "100%",
          height: "100%",
          borderRadius: radius * 0.5,
          overflow: "hidden",
          background:
            "radial-gradient(120% 90% at 20% 15%, #6d5cff 0%, #2f2a8f 35%, #151247 70%), linear-gradient(160deg, #3b2f9c, #0d0b2e)",
        }}
      >
        <div style={{ position: "absolute", inset: 0, background: `rgba(0,0,0,${veil})` }} />
        <div style={{ position: "absolute", inset: 0, background: `rgba(196,166,255,${0.35 * flash})` }} />
      </div>
    </div>
  );
};
