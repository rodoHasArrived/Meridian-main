# Meridian UI Screenshots

Screenshots of the Meridian Terminal web dashboard running in default (no-provider) mode.

## How to run

```bash
dotnet build src/Meridian/Meridian.csproj -c Release /p:EnableWindowsTargeting=true
cd src/Meridian/bin/Release/net9.0
MDC_AUTH_MODE=optional ./Meridian --ui --http-port 8200
# open http://localhost:8200
```

---

## 01 – Main Dashboard

Full-page view of the Meridian Terminal dashboard (`/`). Shows the overview panel, activity log, data provider selector, storage configuration, data sources, historical backfill controls, derivatives tracking, and subscribed symbols table.

![Meridian Terminal – Main Dashboard](https://github.com/user-attachments/assets/44f8822e-c141-4105-80bf-81fe2a9de7da)

---

## 02 – React Workstation Shell

The React-based trading workstation is served at `/workstation/` and provides the modern portfolio/trading workspace UI.

![Workstation Shell](02-workstation.png)

---

## 03 – Swagger API Docs

Interactive REST API documentation is available at `/swagger/index.html` and covers all 300+ API routes (backfill, providers, storage, security master, execution, etc.).

![Swagger API Docs](03-swagger.png)

---

## 04 – Storage Configuration & Data Sources

Mid-page view showing the **Storage Configuration** card (data root path, naming convention, date partitioning, preview path) and the top of the **Data Sources** panel with automatic failover toggle.

![Storage Configuration & Data Sources](https://github.com/user-attachments/assets/48bc62b6-4906-4483-af23-2632524c9f79)
