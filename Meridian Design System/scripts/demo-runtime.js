/* Maintained bootstrap for standalone React demo cards. Runtime dependencies remain
   pinned and integrity checked; the acceptance harness fulfills these URLs from its
   digest-verified local cache. This file is not consumed by Meridian's application. */
(() => {
  if (window.__meridianDemoRuntime) return;
  const peers = [
    ['React', 'https://unpkg.com/react@18.3.1/umd/react.development.js', 'sha384-hD6/rw4ppMLGNu3tX5cjIb+uRZ7UkRJ6BPkLpg4hAu/6onKUg4lLsHAs9EBPT82L'],
    ['ReactDOM', 'https://unpkg.com/react-dom@18.3.1/umd/react-dom.development.js', 'sha384-u6aeetuaXnQ38mYT8rp6sbXaQe3NL9t+IBXmnYxwkUI2Hw4bsp2Wvmx4yRQF1uAm'],
    ['Babel', 'https://unpkg.com/@babel/standalone@7.29.0/babel.min.js', 'sha384-m08KidiNqLdpJqLq95G/LEi8Qvjl/xUYll3QILypMoQ65QorJ9Lvtp2RXYGBFj1y']
  ];
  const load = ([globalName, src, integrity]) => new Promise((resolve, reject) => {
    if (window[globalName]) return resolve();
    const script = document.createElement('script');
    script.src = src; script.integrity = integrity; script.crossOrigin = 'anonymous'; script.async = false;
    script.onload = resolve;
    script.onerror = () => reject(new Error(`Meridian demo could not load its pinned ${globalName} runtime`));
    document.head.append(script);
  });
  window.__meridianDemoRuntime = (async () => {
    for (const peer of peers) await load(peer);
    // Babel's automatic listener may be registered after the document has loaded.
    // Own the one-time transform explicitly so cards work in both loading orders.
    window.Babel.disableScriptTags();
    const mount = () => {
      window.Babel.transformScriptTags();
      document.documentElement.dataset.meridianDemoRuntime = 'ready';
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true });
    else mount();
  })();
  window.__meridianDemoRuntime.catch((error) => {
    document.documentElement.dataset.meridianDemoRuntime = 'failed';
    console.error(error);
  });
})();
