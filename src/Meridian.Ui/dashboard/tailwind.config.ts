import type { Config } from "tailwindcss";

export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  // The workstation is light-first. Class dark mode stays available for local
  // experiments, but the production visual contract is token-driven in CSS.
  darkMode: ["class"],
  theme: {
    extend: {
      fontFamily: {
        sans: ["Segoe UI Variable Text", "Segoe UI", "system-ui", "ui-sans-serif"],
        mono: ["Cascadia Mono", "JetBrains Mono", "Consolas", "ui-monospace", "SFMono-Regular"],
        display: ["Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI", "system-ui", "ui-sans-serif"]
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
        // Sidebar tokens map to the light institutional operator rail.
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
        // Chart tokens retain the chart-1...5 contract with Meridian semantic colors.
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
        // Flat is retained as a compatibility key, now backed by a hairline shadow.
        flat: "var(--shadow-offset-x) var(--shadow-offset-y) var(--shadow-blur) var(--shadow-spread) hsl(var(--shadow-color) / var(--shadow-opacity))"
      }
    }
  },
  plugins: []
} satisfies Config;
