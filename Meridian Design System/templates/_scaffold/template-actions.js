/* Read-only exports for authored synthetic examples. No application transport. */
(function (scope) {
  "use strict";
  scope.MeridianTemplateActions = Object.freeze({
    downloadJson(filename, data) {
      const blob = new Blob([JSON.stringify({ synthetic: true, data }, null, 2)], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = filename;
      document.body.appendChild(link);
      link.click();
      link.remove();
      // Let the browser finish its download before releasing the object URL.
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    },
  });
})(window);
