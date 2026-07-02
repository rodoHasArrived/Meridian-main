import js from "@eslint/js";
import reactHooks from "eslint-plugin-react-hooks";
import tseslint from "typescript-eslint";

import kebabFilename from "./scripts/eslint-rules/kebab-filename.mjs";

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
      "no-useless-assignment": "warn",
      "no-useless-escape": "warn",
      "preserve-caught-error": "warn",
      "@typescript-eslint/no-empty-object-type": "off",
      "@typescript-eslint/no-explicit-any": "off",
      "@typescript-eslint/no-unused-vars": "warn",
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
    files: [
      "src/screens/settings-screen.tsx",
      "src/screens/strategy-screen.tsx"
    ],
    rules: {
      "react-hooks/rules-of-hooks": "warn"
    }
  }
);
