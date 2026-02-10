import React, { useMemo } from "react";

export default function App() {
  const serviceUrl = useMemo(() => {
    return import.meta.env.VITE_SERVICE_URL || "http://localhost:5005/";
  }, []);

  return (
    <div className="shell">
      <header className="topbar">
        <div>
          <h1>Kodak Scanner</h1>
          <p>Connected to local service at {serviceUrl}</p>
        </div>
        <div className="actions">
          <button onClick={() => window.location.reload()}>Reload</button>
          <button onClick={() => window.open(serviceUrl, "_blank")}>Open In Browser</button>
        </div>
      </header>
      <main className="frame-wrap">
        <iframe title="Kodak Scanner" src={serviceUrl} />
      </main>
    </div>
  );
}
