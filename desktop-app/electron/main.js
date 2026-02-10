const { app, BrowserWindow } = require("electron");
const path = require("path");
const { spawn } = require("child_process");
const http = require("http");

const isDev = !app.isPackaged;

function createWindow() {
  const win = new BrowserWindow({
    width: 1200,
    height: 800,
    backgroundColor: "#111318",
    icon: path.join(__dirname, "assets", "app.ico"),
    webPreferences: {
      contextIsolation: true
    }
  });

  if (isDev) {
    win.loadURL("http://localhost:5173/");
    win.webContents.openDevTools({ mode: "detach" });
  } else {
    win.loadFile(path.join(__dirname, "..", "dist", "index.html"));
  }
}

function getServiceCandidates() {
  const envPath = process.env.KODAK_SCANNER_SERVICE;
  if (envPath) {
    return [envPath];
  }

  return [
    path.join(__dirname, "..", "..", "KodakScannerApp", "bin", "Release", "KodakScannerApp.exe"),
    path.join(__dirname, "..", "..", "KodakScannerApp", "bin", "Debug", "KodakScannerApp.exe")
  ];
}

function isServiceUp(callback) {
  const req = http.get("http://localhost:5005/api/status", (res) => {
    res.resume();
    callback(res.statusCode === 200);
  });
  req.on("error", () => callback(false));
  req.setTimeout(1500, () => {
    req.destroy();
    callback(false);
  });
}

function startServiceIfNeeded() {
  isServiceUp((up) => {
    if (up) {
      return;
    }

    const candidates = getServiceCandidates();
    const exe = candidates.find((p) => {
      try {
        return require("fs").existsSync(p);
      } catch {
        return false;
      }
    });

    if (!exe) {
      console.warn("KodakScannerApp.exe not found. Set KODAK_SCANNER_SERVICE env var.");
      return;
    }

    const child = spawn(exe, [], {
      detached: true,
      stdio: "ignore"
    });
    child.unref();
  });
}

app.whenReady().then(() => {
  startServiceIfNeeded();
  createWindow();

  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") {
    app.quit();
  }
});
