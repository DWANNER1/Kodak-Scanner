const express = require("express");
const http = require("http");
const path = require("path");
const crypto = require("crypto");
const WebSocket = require("ws");

const app = express();
app.use(express.json());

const PORT = process.env.PORT || 10000;
const CLOUD_USER = process.env.CLOUD_USER || "kodak";
const CLOUD_PASS = process.env.CLOUD_PASS || "kodak";

const tokens = new Set();
let agentSocket = null;
const uiSockets = new Set();

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

wss.on("connection", (ws, req) => {
  const url = new URL(req.url, `http://${req.headers.host}`);
  const token = url.searchParams.get("token");
  const role = url.searchParams.get("role");

  if (!verifyToken(token)) {
    ws.close(4401, "Unauthorized");
    return;
  }

  if (role === "agent") {
    agentSocket = ws;
    broadcastToUI({ type: "agent_status", connected: true });
  } else {
    uiSockets.add(ws);
    ws.send(JSON.stringify({ type: "agent_status", connected: !!agentSocket }));
  }

  ws.on("message", (raw) => {
    let msg;
    try {
      msg = JSON.parse(raw.toString());
    } catch {
      return;
    }

    if (role === "agent") {
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
      broadcastToUI({ type: "agent_status", connected: false });
    } else {
      uiSockets.delete(ws);
    }
  });
});

server.listen(PORT, () => {
  console.log(`Cloud service listening on ${PORT}`);
});
