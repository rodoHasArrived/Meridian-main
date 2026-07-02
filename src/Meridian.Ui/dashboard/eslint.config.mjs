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
      "no-useless-assignment": "error",
      "no-useless-escape": "error",
      "preserve-caught-error": "error",
      "@typescript-eslint/no-explicit-any": "off",
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
  }
);
