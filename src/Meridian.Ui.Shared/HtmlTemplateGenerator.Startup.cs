namespace Meridian.Ui.Shared;

public static partial class HtmlTemplateGenerator
{
    /// <summary>
    /// Renders the sanitized pre-login Startup Center. All dynamic values are assigned with
    /// textContent from /startupz so startup diagnostics cannot inject markup.
    /// </summary>
    public static string Startup() => """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Meridian Startup Center</title>
  <style>
    :root { color-scheme: dark; font-family: Inter, Segoe UI, sans-serif; background:#07101d; color:#e5edf8; }
    body { margin:0; min-height:100vh; display:grid; place-items:center; background:radial-gradient(circle at top,#153153,#07101d 58%); }
    main { width:min(760px,calc(100vw - 32px)); background:#0d1a2b; border:1px solid #29415f; border-radius:16px; box-shadow:0 24px 80px #0008; overflow:hidden; }
    header { padding:28px 32px 20px; border-bottom:1px solid #29415f; }
    h1 { margin:0 0 8px; font-size:24px; }
    p { margin:0; color:#9eb1c9; line-height:1.5; }
    .summary { display:flex; gap:16px; align-items:center; padding:22px 32px; }
    .badge { padding:6px 10px; border-radius:999px; background:#24405f; font-weight:700; text-transform:uppercase; font-size:12px; letter-spacing:.08em; }
    .badge.ready { background:#164e3a; color:#9ff3ce; }
    .badge.degraded { background:#5b4715; color:#ffe08a; }
    .badge.failed,.badge.stopping { background:#5a2430; color:#ffb4c0; }
    #phase { color:#c5d3e5; }
    ul { list-style:none; padding:0 32px 22px; margin:0; display:grid; gap:10px; }
    li { padding:14px 16px; border:1px solid #29415f; border-radius:10px; background:#0a1524; display:grid; grid-template-columns:1fr auto; gap:5px 18px; }
    li span { color:#9eb1c9; font-size:13px; }
    li strong { font-size:14px; }
    footer { display:flex; justify-content:space-between; align-items:center; padding:18px 32px 28px; }
    a { color:#dbeafe; background:#2563eb; padding:10px 16px; border-radius:8px; text-decoration:none; font-weight:700; }
    a[aria-disabled="true"] { pointer-events:none; opacity:.45; }
    #updated { color:#7187a3; font-size:12px; }
  </style>
</head>
<body>
  <main aria-labelledby="title">
    <header>
      <h1 id="title">Meridian Startup Center</h1>
      <p>Meridian is validating the workstation, database, storage, and event pipeline before sign-in.</p>
    </header>
    <section class="summary" aria-live="polite">
      <span id="status" class="badge">Starting</span>
      <span id="phase">Waiting for lifecycle status…</span>
    </section>
    <ul id="checks" aria-label="Startup checks"></ul>
    <footer>
      <span id="updated">Checking…</span>
      <a id="login" href="/login" aria-disabled="true">Continue to sign in</a>
    </footer>
  </main>
  <script>
    const statusNode = document.getElementById('status');
    const phaseNode = document.getElementById('phase');
    const checksNode = document.getElementById('checks');
    const updatedNode = document.getElementById('updated');
    const loginNode = document.getElementById('login');
    let timer;

    function render(snapshot) {
      const readiness = String(snapshot.readiness || snapshot.state || 'starting').toLowerCase();
      statusNode.textContent = readiness;
      statusNode.className = `badge ${readiness}`;
      phaseNode.textContent = snapshot.activePhase || snapshot.state || 'starting';
      checksNode.replaceChildren();
      for (const check of snapshot.checks || []) {
        const row = document.createElement('li');
        const name = document.createElement('strong');
        const state = document.createElement('strong');
        const message = document.createElement('span');
        name.textContent = check.displayName || check.id;
        state.textContent = check.status;
        message.textContent = check.message;
        row.append(name, state, message);
        checksNode.append(row);
      }
      const canLogin = snapshot.acceptingWork === true && (readiness === 'ready' || readiness === 'degraded');
      loginNode.setAttribute('aria-disabled', canLogin ? 'false' : 'true');
      updatedNode.textContent = `Updated ${new Date().toLocaleTimeString()}`;
      clearTimeout(timer);
      timer = setTimeout(refresh, canLogin ? 5000 : 500);
    }

    async function refresh() {
      try {
        const response = await fetch('/startupz', { cache: 'no-store' });
        render(await response.json());
      } catch {
        statusNode.textContent = 'unavailable';
        statusNode.className = 'badge failed';
        phaseNode.textContent = 'The local host did not return lifecycle status.';
        clearTimeout(timer);
        timer = setTimeout(refresh, 1000);
      }
    }
    refresh();
  </script>
</body>
</html>
""";
}
