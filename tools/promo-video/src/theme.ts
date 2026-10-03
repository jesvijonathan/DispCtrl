import { useVideoConfig } from "remotion";

// The website's own tokens (site/index.html :root), so the video and the site match.
export const colour = {
  bg: "#0d0a14",
  bg2: "#141020",
  card: "#1a1426",
  line: "#3a2f55",
  text: "#f4f1fa",
  muted: "#b2a8c6",
  a1: "#c4a6ff",
  a2: "#8b5cf6",
  a3: "#f0a6d8",
  onAccent: "#120d1c",
};

export const font =
  '"Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif';

export const gradientText = {
  backgroundImage: `linear-gradient(120deg, ${colour.a1}, ${colour.a2} 55%, ${colour.a3})`,
  WebkitBackgroundClip: "text" as const,
  backgroundClip: "text" as const,
  color: "transparent",
};

export type Shape = "wide" | "square" | "tall";

/**
 * The frame's shape and a unit that scales type and spacing with it. One unit
 * is a pixel on a 1080-high wide frame; the shorter side sets it, so headlines
 * keep the same weight on every platform.
 */
export const useLayout = () => {
  const { width, height } = useVideoConfig();
  const shape: Shape =
    width / height > 1.3 ? "wide" : width / height < 0.8 ? "tall" : "square";
  const u = Math.min(width, height) / 1080;
  // Instagram covers the top ~250 px and bottom ~340 px of a story with its own UI.
  const safeTop = shape === "tall" ? 260 * u : 90 * u;
  const safeBottom = shape === "tall" ? 360 * u : 90 * u;
  return { width, height, shape, u, safeTop, safeBottom };
};
