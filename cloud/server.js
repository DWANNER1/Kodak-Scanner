const express = require("express");
const http = require("http");
const path = require("path");
const crypto = require("crypto");
const WebSocket = require("ws");
const fs = require("fs");
const multer = require("multer");

const app = express();
app.use(express.json());

const PORT = process.env.PORT || 10000;
const CLOUD_USER = process.env.CLOUD_USER || "kodak";
const CLOUD_PASS = process.env.CLOUD_PASS || "kodak";

const tokens = new Set();
let agentSocket = null;
const uiSockets = new Set();
let lastStatus = null;
const previewMap = new Map();

const previewDir = path.join(__dirname, "previews");
if (!fs.existsSync(previewDir)) {
  fs.mkdirSync(previewDir, { recursive: true });
}

const upload = multer({ dest: previewDir });

function clearPreviews() {
  try {
    for (const file of fs.readdirSync(previewDir)) {
      fs.unlinkSync(path.join(previewDir, file));
    }
  } catch {}
  previewMap.clear();
}

function issueToken() {
  const token = crypto.randomBytes(24).toString("hex");
  tokens.add(token);
  return token;
}

function verifyToken(token) {
  return token && tokens.has(token);
}

app.post("/login", (req, res) => {
  const { username, password } = req.body || {};
  if (username === CLOUD_USER && password === CLOUD_PASS) {
    return res.json({ ok: true, token: issueToken() });
  }
  res.status(401).json({ ok: false, message: "Invalid credentials" });
});

app.post("/upload", upload.single("file"), (req, res) => {
  const token = req.query.token || req.headers["x-token"];
  if (!verifyToken(token)) {
    return res.status(401).json({ ok: false, message: "Unauthorized" });
  }
  const pageId = req.query.pageId || "";
  if (!pageId || !req.file) {
    return res.status(400).json({ ok: false, message: "Missing upload" });
  }
  const targetName = `${pageId}.jpg`;
  const targetPath = path.join(previewDir, targetName);
  fs.renameSync(req.file.path, targetPath);
  const url = `/previews/${targetName}?v=${Date.now()}`;
  previewMap.set(pageId, url);
  res.json({ ok: true, url });
});

app.use("/previews", express.static(previewDir));
app.use(express.static(path.join(__dirname, "public")));

const server = http.createServer(app);
const wss = new WebSocket.Server({ server });

function broadcastToUI(payload) {
  const data = JSON.stringify(payload);
  for (const sock of uiSockets) {
    if (sock.readyState === WebSocket.OPEN) {
      sock.send(data);
    }
  }
}

function normalizeStatus(status) {
  if (!status || !status.Pages) return status;
  const pages = status.Pages.map((p) => {
    const fileName = p.Path ? p.Path.replace(/^.*[\\\/]/, "") : "";
    return {
      Id: p.Id,
      Name: fileName,
      PreviewUrl: previewMap.get(p.Id) || ""
    };
  });
  return { ...status, Pages: pages };
}

wss.on("connection", (ws, req) => {
  const url = new URL(req.url, `http://${req.headers.host}`);
  const token = url.searchParams.get("token");
  const role = url.searchParams.get("role");

  if (!verifyToken(token)) {
    ws.close(4401, "Unauthorized");
    return;
  }

  if (role === "agent") {
    clearPreviews();
    agentSocket = ws;
    broadcastToUI({ type: "agent_status", connected: true });
  } else {
    uiSockets.add(ws);
    ws.send(JSON.stringify({ type: "agent_status", connected: !!agentSocket }));
    if (lastStatus) {
      ws.send(JSON.stringify({ type: "status", status: normalizeStatus(lastStatus) }));
    }
  }

  ws.on("message", (raw) => {
    let msg;
    try {
      msg = JSON.parse(raw.toString());
    } catch {
      return;
    }

    if (role === "agent") {
      if (msg.type === "status" && msg.status) {
        lastStatus = msg.status;
        broadcastToUI({ type: "status", status: normalizeStatus(lastStatus) });
        return;
      }
      broadcastToUI(msg);
      return;
    }

    if (role === "ui" && agentSocket && agentSocket.readyState === WebSocket.OPEN) {
      agentSocket.send(JSON.stringify(msg));
    }
  });

  ws.on("close", () => {
    if (role === "agent") {
      agentSocket = null;
      clearPreviews();
      broadcastToUI({ type: "agent_status", connected: false });
    } else {
      uiSockets.delete(ws);
    }
  });
});

server.listen(PORT, () => {
  console.log(`Cloud service listening on ${PORT}`);
});
