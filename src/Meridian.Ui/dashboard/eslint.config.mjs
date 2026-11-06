import js from "@eslint/js";
import reactHooks from "eslint-plugin-react-hooks";
import tseslint from "typescript-eslint";

import { readFileSync } from "node:fs";
import { URL } from "node:url";

import kebabFilename from "./scripts/eslint-rules/kebab-filename.mjs";

// Screens format through the shared `@/lib/format` module. Files still in the design-system
// ratchet baseline are exempt until migrated; the ratchet test stops them adding more calls,
// and lowering the baseline (`npm run ratchets:update`) brings each migrated file under the rule.
const ratchetBaseline = JSON.parse(readFileSync(new URL("./scripts/design-system-ratchets.baseline.json", import.meta.url), "utf8"));
const directIntlBaselineFiles = Object.keys(ratchetBaseline.directIntlFormatting ?? {});
const directIntlMessage = "Format numbers, money and dates with @/lib/format (formatCount, formatMoney, formatQuantity, formatUtcTimestamp), not Intl or toLocale*String directly.";

export default tseslint.config(
  {
    ignores: [
      ".tmp/**",
      "artifacts/**",
      "coverage/**",
      "dist/**",
      "node_modules/**"
    ]
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ["src/**/*.{ts,tsx}"],
    plugins: {
      "react-hooks": reactHooks,
      meridian: {
        rules: {
          "kebab-filename": kebabFilename
        }
      }
    },
    languageOptions: {
      parserOptions: {
        ecmaFeatures: {
          jsx: true
        }
      }
    },
    rules: {
      "no-undef": "off",
      "no-unused-vars": "off",
      "no-useless-assignment": "error",
      "no-useless-escape": "error",
      "preserve-caught-error": "error",
      "@typescript-eslint/no-explicit-any": "warn",
      "@typescript-eslint/no-unused-vars": [
        "error",
        {
          argsIgnorePattern: "^_",
          varsIgnorePattern: "^_",
          caughtErrors: "none",
          destructuredArrayIgnorePattern: "^_",
          ignoreRestSiblings: true
        }
      ],
      "meridian/kebab-filename": [
        "error",
        {
          ignoredPrefixes: [
            "src/components/accounting/",
            "src/components/charts/",
            "src/features/accounting/"
          ]
        }
      ],
      "react-hooks/exhaustive-deps": "warn",
      "react-hooks/rules-of-hooks": "error"
    }
  },
  {
    files: ["src/screens/**/*.{ts,tsx}"],
    ignores: ["src/screens/**/*.test.{ts,tsx}", ...directIntlBaselineFiles],
    rules: {
      "no-restricted-syntax": [
        "error",
        { selector: "NewExpression[callee.object.name='Intl'][callee.property.name=/^(NumberFormat|DateTimeFormat)$/]", message: directIntlMessage },
        { selector: "CallExpression[callee.object.name='Intl'][callee.property.name=/^(NumberFormat|DateTimeFormat)$/]", message: directIntlMessage },
        { selector: "CallExpression[callee.property.name=/^toLocale(Date|Time)?String$/]", message: directIntlMessage }
      ]
    }
  }
);
