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
  var lastJobDir = "";
  var zoomState = {};

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
      lastJobDir = status.CurrentJobDir || "";

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

          var actions = document.createElement("div");
          actions.className = "file-actions";

          var zoomOut = buildIconButton("Zoom out", "zoom_out");
          var zoomIn = buildIconButton("Zoom in", "zoom_in");
          var rotateLeft = buildIconButton("Rotate left", "rotate_left");
          var rotateRight = buildIconButton("Rotate right", "rotate_right");
          var del = buildIconButton("Delete", "trash");
          del.classList.add("danger");

          var rel = toRelativeScanPath(f, lastOutputRoot);
          if (rel) {
            img.dataset.baseSrc = "/scans/" + rel;
            img.src = img.dataset.baseSrc;
          }

          img.dataset.scale = "1";
          img.style.transformOrigin = "center center";
          img.style.transition = "transform 0.15s ease";

          var scale = zoomState[f] || 1;
          img.dataset.scale = scale.toString();
          img.style.transform = "scale(" + scale + ")";
          zoomIn.addEventListener("click", function () {
            var next = Math.min(3, parseFloat(img.dataset.scale || "1") + 0.25);
            img.dataset.scale = next.toString();
            img.style.transform = "scale(" + next + ")";
            zoomState[f] = next;
          });
          zoomOut.addEventListener("click", function () {
            var next = Math.max(0.5, parseFloat(img.dataset.scale || "1") - 0.25);
            img.dataset.scale = next.toString();
            img.style.transform = "scale(" + next + ")";
            zoomState[f] = next;
          });
          rotateLeft.addEventListener("click", function () {
            rotateFile(f, "left", img);
          });
          rotateRight.addEventListener("click", function () {
            rotateFile(f, "right", img);
          });
          del.addEventListener("click", function () {
            if (!confirm("Delete this scan?")) return;
            apiPost("/api/delete", { Path: f }).then(function (res) {
              if (!res.Ok) {
                alert(res.Message || "Delete failed");
                return;
              }
              delete zoomState[f];
              refreshStatus();
            });
          });

          actions.appendChild(zoomOut);
          actions.appendChild(zoomIn);
          actions.appendChild(rotateLeft);
          actions.appendChild(rotateRight);
          actions.appendChild(del);

          var wrap = document.createElement("div");
          wrap.className = "file-thumb-wrap";
          wrap.appendChild(img);

          li.appendChild(wrap);
          li.appendChild(actions);
          filesEl.appendChild(li);
        });
      }
    });
  }

  function toRelativeScanPath(filePath, rootPath) {
    if (!filePath || !rootPath) return "";
    var normalizedFile = filePath.replace(/\\/g, "/");
    var normalizedRoot = rootPath.replace(/\\/g, "/");
    if (!normalizedRoot.endsWith("/")) {
      normalizedRoot += "/";
    }
    var fileLower = normalizedFile.toLowerCase();
    var rootLower = normalizedRoot.toLowerCase();
    if (fileLower.indexOf(rootLower) !== 0) return "";
    var rel = normalizedFile.substring(normalizedRoot.length);
    return encodeURI(rel);
  }

  function rotateFile(path, direction, img) {
    apiPost("/api/rotate", { Path: path, Direction: direction }).then(function (res) {
      if (!res.Ok) {
        alert(res.Message || "Rotate failed");
        return;
      }
      if (img && img.dataset.baseSrc) {
        img.src = img.dataset.baseSrc + "?v=" + Date.now();
      }
    });
  }

  function buildIconButton(label, iconName) {
    var button = document.createElement("button");
    button.type = "button";
    button.className = "icon-btn";
    button.setAttribute("aria-label", label);
    button.title = label;
    button.appendChild(buildIcon(iconName));
    return button;
  }

  function buildIcon(name) {
    var svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", "0 0 24 24");
    svg.setAttribute("aria-hidden", "true");
    var path = document.createElementNS("http://www.w3.org/2000/svg", "path");
    path.setAttribute("d", iconPath(name));
    svg.appendChild(path);
    return svg;
  }

  function iconPath(name) {
    if (name === "zoom_in") return "M10.5 3a7.5 7.5 0 0 1 5.99 12.06l4.23 4.23-1.42 1.42-4.23-4.23A7.5 7.5 0 1 1 10.5 3zm0 2a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11zm-1 2h2v2h2v2h-2v2h-2v-2h-2V9h2V7z";
    if (name === "zoom_out") return "M10.5 3a7.5 7.5 0 0 1 5.99 12.06l4.23 4.23-1.42 1.42-4.23-4.23A7.5 7.5 0 1 1 10.5 3zm0 2a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11zm-3 4h6v2h-6V9z";
    if (name === "rotate_left") return "M7 7h5V4l4 4-4 4V9H7a5 5 0 1 0 5 5h2a7 7 0 1 1-7-7z";
    if (name === "rotate_right") return "M17 7h-5V4l-4 4 4 4V9h5a5 5 0 1 1-5 5H5a7 7 0 1 0 12-7z";
    return "M6 7h12l-1 14H7L6 7zm3-3h6l1 2H8l1-2z";
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
    if (!document.getElementById("outputPath").value && lastJobDir) {
      document.getElementById("outputPath").value = lastJobDir;
    }
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
