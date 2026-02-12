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
      renderPages(msg.status.Pages || []);
      if (msg.about && agentVersion) {
        agentVersion.textContent = `Build: ${msg.about.BuildTime} · Version ${msg.about.Version}`;
        if (aboutInfo) {
          aboutInfo.textContent = `Build: ${msg.about.BuildTime} · Version ${msg.about.Version}`;
        }
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
    const chip = document.createElement("span");
    chip.className = "page-chip";
    chip.textContent = `Page ${index + 1}`;
    const name = document.createElement("span");
    name.textContent = page.Name || "";
    actions.appendChild(chip);
    actions.appendChild(name);
    li.appendChild(wrap);
    li.appendChild(actions);
    filesEl.appendChild(li);
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
