import path from "node:path";

const KEBAB_SEGMENT = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const TYPE_DECLARATION_SUFFIX = ".d.ts";

export default {
  meta: {
    type: "suggestion",
    docs: {
      description: "Require dashboard source filenames to use kebab-case segments."
    },
    schema: [
      {
        type: "object",
        properties: {
          ignoredPrefixes: {
            type: "array",
            items: { type: "string" }
          }
        },
        additionalProperties: false
      }
    ],
    messages: {
      expectedKebab:
        "Dashboard source filename '{{filename}}' should use kebab-case segments."
    }
  },
  create(context) {
    const filename = context.filename ?? context.getFilename?.() ?? "";
    const normalized = filename.replaceAll(path.sep, "/");
    const srcIndex = normalized.lastIndexOf("/src/");
    const relative = srcIndex >= 0 ? normalized.slice(srcIndex + 1) : normalized;
    const ignoredPrefixes = context.options[0]?.ignoredPrefixes ?? [];

    if (ignoredPrefixes.some((prefix) => relative.startsWith(prefix))) {
      return {};
    }

    const basename = path.posix.basename(relative);
    const stem = basename.endsWith(TYPE_DECLARATION_SUFFIX)
      ? basename.slice(0, -TYPE_DECLARATION_SUFFIX.length)
      : basename.replace(/\.(tsx|ts)$/, "");
    const valid = stem.split(".").every((segment) => KEBAB_SEGMENT.test(segment));

    if (valid) {
      return {};
    }

    return {
      Program(node) {
        context.report({
          node,
          messageId: "expectedKebab",
          data: { filename: basename }
        });
      }
    };
  }
};
