(function () {
  function apiGet(path) {
    return fetch(path, { cache: "no-store" }).then(function (r) {
      return r.json();
    });
  }

  function apiPost(path, body) {
    return fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body || {})
    }).then(function (r) {
      return r.json();
    });
  }

  var deviceSelect = document.getElementById("deviceSelect");
  var statusEl = document.getElementById("status");
  var filesEl = document.getElementById("files");
  var navItems = document.querySelectorAll(".nav-item");
  var tabSections = document.querySelectorAll(".tab-section");
  var lastDevices = [];
  var lastOutputRoot = "";

  function setActiveTab(tabName) {
    navItems.forEach(function (item) {
      item.classList.toggle("active", item.dataset.tab === tabName);
    });
    tabSections.forEach(function (section) {
      section.classList.toggle("active", section.dataset.section === tabName);
    });
  }

  navItems.forEach(function (item) {
    item.addEventListener("click", function () {
      setActiveTab(item.dataset.tab);
    });
  });

  function loadDevices() {
    apiGet("/api/devices").then(function (devices) {
      lastDevices = devices || [];
      deviceSelect.innerHTML = "";
      if (!lastDevices || lastDevices.length === 0) {
        var opt = document.createElement("option");
        opt.value = "";
        opt.textContent = "No devices found";
        deviceSelect.appendChild(opt);
        setStatus("Offline", "No scanner detected", "offline");
        return;
      }
      lastDevices.forEach(function (d) {
        var opt = document.createElement("option");
        opt.value = d.Id;
        opt.textContent = d.Name;
        deviceSelect.appendChild(opt);
      });
    });
  }

  function setStatus(state, message, tone) {
    statusEl.textContent = state + (message ? ": " + message : "");
    statusEl.classList.remove("available", "offline", "busy");
    if (tone) {
      statusEl.classList.add(tone);
    }
  }

  function refreshStatus() {
    apiGet("/api/status").then(function (status) {
      if (!status) return;
      var state = status.State || "Idle";
      var message = status.Message || "";
      var tone = "available";
      lastOutputRoot = status.OutputRoot || "";

      if (!lastDevices || lastDevices.length === 0) {
        setStatus("Offline", "No scanner detected", "offline");
        return;
      }

      if (state.toLowerCase().indexOf("scan") !== -1) {
        tone = "busy";
      } else if (state.toLowerCase().indexOf("error") !== -1) {
        tone = "offline";
      }

      setStatus(state, message, tone);
      if (status.Files) {
        filesEl.innerHTML = "";
        status.Files.forEach(function (f) {
          var li = document.createElement("li");
          var img = document.createElement("img");
          img.className = "file-thumb";
          img.alt = "Scanned page preview";

          var text = document.createElement("div");
          text.className = "file-path";
          text.textContent = f;

          var rel = toRelativeScanPath(f, lastOutputRoot);
          if (rel) {
            img.src = "/scans/" + rel;
          }

          li.appendChild(img);
          li.appendChild(text);
          filesEl.appendChild(li);
        });
      }
    });
  }

  function toRelativeScanPath(filePath, rootPath) {
    if (!filePath || !rootPath) return "";
    var normalizedFile = filePath.replace(/\\\\/g, "/");
    var normalizedRoot = rootPath.replace(/\\\\/g, "/");
    if (!normalizedRoot.endsWith("/")) {
      normalizedRoot += "/";
    }
    if (normalizedFile.indexOf(normalizedRoot) !== 0) return "";
    var rel = normalizedFile.substring(normalizedRoot.length);
    return encodeURI(rel);
  }

  document.getElementById("scanBtn").addEventListener("click", function (event) {
    event.preventDefault();
    event.stopPropagation();
    var payload = {
      DeviceId: deviceSelect.value,
      Dpi: parseInt(document.getElementById("dpi").value || "300", 10),
      ColorMode: document.getElementById("colorMode").value,
      Duplex: document.getElementById("duplex").value === "true",
      MaxPages: parseInt(document.getElementById("maxPages").value || "100", 10)
    };

    apiPost("/api/scan", payload).then(function (res) {
      if (!res.Ok) {
        alert(res.Message || "Scan failed");
      }
      refreshStatus();
    }).catch(function (err) {
      alert("Scan failed: " + err);
    });
  });

  document.getElementById("exportBtn").addEventListener("click", function (event) {
    event.preventDefault();
    event.stopPropagation();
    var payload = {
      Format: document.getElementById("format").value,
      OutputPath: document.getElementById("outputPath").value,
      BaseName: document.getElementById("baseName").value
    };

    apiPost("/api/export", payload).then(function (res) {
      if (!res.Ok) {
        alert(res.Message || "Export failed");
      } else {
        alert(res.Message + "\n" + (res.Files || []).join("\n"));
      }
      refreshStatus();
    }).catch(function (err) {
      alert("Export failed: " + err);
    });
  });

  document.getElementById("clearBtn").addEventListener("click", function (event) {
    event.preventDefault();
    event.stopPropagation();
    apiPost("/api/clear", {}).then(function () {
      refreshStatus();
    }).catch(function (err) {
      alert("Clear failed: " + err);
    });
  });

  loadDevices();
  refreshStatus();
  setInterval(refreshStatus, 2000);
})();
