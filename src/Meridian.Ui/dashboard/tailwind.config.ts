import type { Config } from "tailwindcss";

export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  // Cash-flow-factor-sch uses [data-appearance="dark"]; Meridian is dark-first
  // so class-based dark mode is used — toggle by adding/removing "dark" on <html>.
  darkMode: ["class"],
  theme: {
    extend: {
      fontFamily: {
        // Inter is the primary sans-serif, aligned with cash-flow-factor-sch.
        // IBM Plex Sans retained as secondary / fallback display face.
        sans: ["Inter", "IBM Plex Sans", "ui-sans-serif", "system-ui"],
        // JetBrains Mono added to match cash-flow-factor-sch; IBM Plex Mono as fallback.
        mono: ["JetBrains Mono", "IBM Plex Mono", "ui-monospace", "SFMono-Regular"],
        display: ["Space Grotesk", "IBM Plex Sans", "ui-sans-serif"]
      },
      colors: {
        border: "hsl(var(--border) / <alpha-value>)",
        input: "hsl(var(--input) / <alpha-value>)",
        ring: "hsl(var(--ring) / <alpha-value>)",
        background: "hsl(var(--background) / <alpha-value>)",
        foreground: "hsl(var(--foreground) / <alpha-value>)",
        primary: {
          DEFAULT: "hsl(var(--primary) / <alpha-value>)",
          foreground: "hsl(var(--primary-foreground) / <alpha-value>)"
        },
        secondary: {
          DEFAULT: "hsl(var(--secondary) / <alpha-value>)",
          foreground: "hsl(var(--secondary-foreground) / <alpha-value>)"
        },
        muted: {
          DEFAULT: "hsl(var(--muted) / <alpha-value>)",
          foreground: "hsl(var(--muted-foreground) / <alpha-value>)"
        },
        accent: {
          DEFAULT: "hsl(var(--accent) / <alpha-value>)",
          foreground: "hsl(var(--accent-foreground) / <alpha-value>)"
        },
        card: {
          DEFAULT: "hsl(var(--card) / <alpha-value>)",
          foreground: "hsl(var(--card-foreground) / <alpha-value>)"
        },
        popover: {
          DEFAULT: "hsl(var(--popover) / <alpha-value>)",
          foreground: "hsl(var(--popover-foreground) / <alpha-value>)"
        },
        success: "hsl(var(--success) / <alpha-value>)",
        positive: "hsl(var(--success) / <alpha-value>)",
        warning: "hsl(var(--warning) / <alpha-value>)",
        danger: "hsl(var(--danger) / <alpha-value>)",
        destructive: "hsl(var(--danger) / <alpha-value>)",
        paper: "hsl(var(--paper) / <alpha-value>)",
        live: "hsl(var(--live) / <alpha-value>)",
        "live-env": "hsl(var(--live-env) / <alpha-value>)",
        "panel-strong": "hsl(var(--panel-strong) / <alpha-value>)",
        "panel-soft": "hsl(var(--panel-soft) / <alpha-value>)",
        "surface-raise": "hsl(var(--surface-raise) / <alpha-value>)",
        // Sidebar tokens — aligned with cash-flow-factor-sch token contract.
        // Maps to the operator rail and navigation surface.
        sidebar: {
          DEFAULT: "hsl(var(--sidebar) / <alpha-value>)",
          foreground: "hsl(var(--sidebar-foreground) / <alpha-value>)",
          primary: "hsl(var(--sidebar-primary) / <alpha-value>)",
          "primary-foreground": "hsl(var(--sidebar-primary-foreground) / <alpha-value>)",
          accent: "hsl(var(--sidebar-accent) / <alpha-value>)",
          "accent-foreground": "hsl(var(--sidebar-accent-foreground) / <alpha-value>)",
          border: "hsl(var(--sidebar-border) / <alpha-value>)",
          ring: "hsl(var(--sidebar-ring) / <alpha-value>)"
        },
        // Chart tokens — named chart-1...5 per cash-flow-factor-sch convention.
        // Semantic: cyan / green / red / amber / blue.
        chart: {
          "1": "hsl(var(--chart-1) / <alpha-value>)",
          "2": "hsl(var(--chart-2) / <alpha-value>)",
          "3": "hsl(var(--chart-3) / <alpha-value>)",
          "4": "hsl(var(--chart-4) / <alpha-value>)",
          "5": "hsl(var(--chart-5) / <alpha-value>)"
        }
      },
      borderRadius: {
        xl: "var(--radius-xl)",
        lg: "var(--radius-lg)",
        md: "var(--radius-md)",
        sm: "var(--radius-sm)",
        xs: "var(--radius-xs)"
      },
      boxShadow: {
        workstation: "var(--shadow-workstation)",
        panel: "var(--shadow-panel)",
        float: "var(--shadow-float)",
        // Flat hard-offset shadow aligned with cash-flow-factor-sch's shadow system.
        flat: "var(--shadow-offset-x) var(--shadow-offset-y) var(--shadow-blur) var(--shadow-spread) hsl(var(--shadow-color) / var(--shadow-opacity))"
      }
    }
  },
  plugins: []
} satisfies Config;
