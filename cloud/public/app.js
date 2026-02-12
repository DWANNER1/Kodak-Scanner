let token = "";
let socket = null;

const loginBtn = document.getElementById("loginBtn");
const loginStatus = document.getElementById("loginStatus");
const loginCard = document.getElementById("loginCard");
const tabsCard = document.getElementById("tabsCard");
const tabButtons = document.querySelectorAll(".tab-btn");
const tabSections = document.querySelectorAll(".tab-section");
const aboutInfo = document.getElementById("aboutInfo");
const agentState = document.getElementById("agentState");
const deviceSelect = document.getElementById("deviceSelect");
const statusLog = document.getElementById("statusLog");
const scanStatus = document.getElementById("scanStatus");
const refreshDevices = document.getElementById("refreshDevices");
const scanBtn = document.getElementById("scanBtn");
const filesEl = document.getElementById("files");
const agentVersion = document.getElementById("agentVersion");
const exportBtn = document.getElementById("exportBtn");
const appendBtn = document.getElementById("appendBtn");
const outputPath = document.getElementById("outputPath");
const baseName = document.getElementById("baseName");
const appendPath = document.getElementById("appendPath");
const exportStatus = document.getElementById("exportStatus");
const headerBtn = document.getElementById("headerBtn");

let pagesState = [];
let draggingId = "";
let dragSource = null;

function logStatus(msg) {
  statusLog.textContent = msg + "\n" + statusLog.textContent;
}

function setAgentStatus(connected) {
  agentState.textContent = connected ? "Agent: Online" : "Agent: Offline";
  agentState.style.background = connected ? "#d7f4e3" : "#f5d7d7";
}

function setActiveTab(name) {
  tabButtons.forEach((btn) => btn.classList.toggle("active", btn.dataset.tab === name));
  tabSections.forEach((section) => {
    section.classList.toggle("active", section.id === `tab-${name}`);
  });
}

tabButtons.forEach((btn) => {
  btn.addEventListener("click", () => setActiveTab(btn.dataset.tab));
});

async function login() {
  const username = document.getElementById("username").value;
  const password = document.getElementById("password").value;
  loginStatus.textContent = "Signing in...";
  const res = await fetch("/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password })
  });
  const data = await res.json();
  if (!data.ok) {
    loginStatus.textContent = data.message || "Login failed";
    return;
  }
  token = data.token;
  loginStatus.textContent = "Signed in";
  loginCard.classList.add("hidden");
  tabsCard.classList.remove("hidden");
  setActiveTab("config");
  tabSections.forEach((section) => section.classList.remove("hidden"));
  connectSocket();
}

function connectSocket() {
  const proto = location.protocol === "https:" ? "wss" : "ws";
  socket = new WebSocket(`${proto}://${location.host}/?token=${token}&role=ui`);
  socket.onmessage = (evt) => {
    const msg = JSON.parse(evt.data);
    if (msg.type === "agent_status") {
      setAgentStatus(msg.connected);
      return;
    }
    if (msg.type === "devices") {
      deviceSelect.innerHTML = "";
      (msg.devices || []).forEach((d) => {
        const opt = document.createElement("option");
        opt.value = d.Id;
        opt.textContent = d.Name;
        deviceSelect.appendChild(opt);
      });
      return;
    }
    if (msg.type === "status") {
      scanStatus.textContent = `${msg.status.State}: ${msg.status.Message}`;
      logStatus(JSON.stringify(msg.status));
      pagesState = msg.status.Pages || [];
      renderPages(pagesState);
      if (msg.about && agentVersion) {
        agentVersion.textContent = `Build: ${msg.about.BuildTime} · Version ${msg.about.Version}`;
        if (aboutInfo) {
          aboutInfo.textContent = `Build: ${msg.about.BuildTime} · Version ${msg.about.Version}`;
        }
      }
      return;
    }
    if (msg.type === "export_result") {
      if (exportStatus) {
        exportStatus.textContent = msg.result?.Message || "Export complete";
      }
      return;
    }
    if (msg.type === "scan_result") {
      scanStatus.textContent = msg.result.Message || "Scan result";
      logStatus(JSON.stringify(msg.result));
      return;
    }
    logStatus(JSON.stringify(msg));
  };
}

loginBtn.addEventListener("click", login);

refreshDevices.addEventListener("click", () => {
  if (!socket) return;
  socket.send(JSON.stringify({ type: "get_devices" }));
});

function renderPages(pages) {
  if (!filesEl) return;
  filesEl.innerHTML = "";
  pages.forEach((page, index) => {
    const li = document.createElement("li");
    const wrap = document.createElement("div");
    wrap.className = "file-thumb-wrap";
    const img = document.createElement("img");
    img.className = "file-thumb";
    img.alt = "Scanned page preview";
    img.src = page.PreviewUrl || "";
    wrap.appendChild(img);
    const actions = document.createElement("div");
    actions.className = "file-actions";
    const left = document.createElement("div");
    const chip = document.createElement("span");
    chip.className = "page-chip";
    chip.textContent = `Page ${index + 1}`;
    left.appendChild(chip);
    const right = document.createElement("div");
    right.className = "page-actions";
    const drag = document.createElement("div");
    drag.className = "drag-handle";
    drag.textContent = String(index + 1);
    drag.setAttribute("draggable", "true");
    drag.dataset.id = page.Id;
    const del = document.createElement("button");
    del.className = "icon-btn danger";
    del.textContent = "Del";
    del.addEventListener("click", () => {
      if (!confirm("Delete this page?")) return;
      sendMessage({ type: "delete", id: page.Id });
    });
    right.appendChild(drag);
    right.appendChild(del);
    actions.appendChild(left);
    actions.appendChild(right);
    li.appendChild(wrap);
    li.appendChild(actions);
    filesEl.appendChild(li);

    drag.addEventListener("dragstart", () => {
      draggingId = page.Id;
      dragSource = filesEl;
    });
    drag.addEventListener("dragend", () => {
      draggingId = "";
      dragSource = null;
    });

    li.addEventListener("dragover", (e) => {
      if (!draggingId) return;
      e.preventDefault();
      if (e.clientY > window.innerHeight - 80) window.scrollBy(0, 20);
      if (e.clientY < 80) window.scrollBy(0, -20);
    });

    li.addEventListener("drop", (e) => {
      if (!draggingId || dragSource !== filesEl) return;
      e.preventDefault();
      const targetId = page.Id;
      if (targetId === draggingId) return;
      const ordered = pagesState.map((p) => p.Id);
      const fromIndex = ordered.indexOf(draggingId);
      const toIndex = ordered.indexOf(targetId);
      if (fromIndex < 0 || toIndex < 0) return;
      ordered.splice(fromIndex, 1);
      ordered.splice(toIndex, 0, draggingId);
      sendMessage({ type: "reorder", ids: ordered });
    });
  });
}

scanBtn.addEventListener("click", () => {
  if (!socket) return;
  const payload = {
    type: "scan_start",
    settings: {
      DeviceId: deviceSelect.value,
      Dpi: parseInt(document.getElementById("dpi").value || "300", 10),
      ColorMode: document.getElementById("colorMode").value,
      Duplex: document.getElementById("duplex").value === "true",
      MaxPages: parseInt(document.getElementById("maxPages").value || "100", 10)
    }
  };
  socket.send(JSON.stringify(payload));
});

function sendMessage(payload) {
  if (!socket) return;
  socket.send(JSON.stringify(payload));
}

if (exportBtn) {
  exportBtn.addEventListener("click", () => {
    const payload = {
      type: "export",
      request: {
        Format: "pdf",
        OutputPath: outputPath.value,
        BaseName: baseName.value,
        Append: false,
        AppendPath: ""
      }
    };
    sendMessage(payload);
  });
}

if (appendBtn) {
  appendBtn.addEventListener("click", () => {
    const payload = {
      type: "export",
      request: {
        Format: "pdf",
        OutputPath: outputPath.value,
        BaseName: baseName.value,
        Append: true,
        AppendPath: appendPath.value || ""
      }
    };
    sendMessage(payload);
  });
}

if (headerBtn) {
  headerBtn.addEventListener("click", () => {
    const text = prompt("Header text:");
    if (!text) return;
    sendMessage({ type: "header", text });
  });
}
