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
  var scrollState = {};
  var imageVersion = {};
  var lastFilesKey = "";
  var isDragging = false;
  var dragPath = "";

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
        if (isDragging) {
          return;
        }
        var nextKey = status.Files.join("|");
        lastFilesKey = nextKey;
        filesEl.innerHTML = "";
        var previousCount = parseInt(filesEl.dataset.count || "0", 10);
        filesEl.dataset.count = status.Files.length.toString();
        status.Files.forEach(function (f, index) {
          var filePath = f;
          var li = document.createElement("li");
          var img = document.createElement("img");
          img.className = "file-thumb";
          img.alt = "Scanned page preview";

          var actions = document.createElement("div");
          actions.className = "file-actions";

          var ball = document.createElement("div");
          ball.className = "page-ball";
          ball.textContent = (index + 1).toString();
          ball.setAttribute("draggable", "true");
          ball.dataset.path = filePath;
          ball.dataset.index = index.toString();

          var zoomOut = buildIconButton("Zoom out", "zoom_out");
          var zoomIn = buildIconButton("Zoom in", "zoom_in");
          var rotateLeft = buildIconButton("Rotate left", "rotate_left");
          var rotateRight = buildIconButton("Rotate right", "rotate_right");
          var del = buildIconButton("Delete", "trash");
          del.classList.add("danger");

          var rel = toRelativeScanPath(filePath, lastOutputRoot);
          if (rel) {
            img.dataset.baseSrc = "/scans/" + rel;
            var v = imageVersion[f] || "";
            img.src = img.dataset.baseSrc + (v ? "?v=" + v : "");
          }

          img.dataset.scale = "1";
          img.style.transformOrigin = "center center";
          img.style.transition = "transform 0.15s ease";

          var wrap = document.createElement("div");
          wrap.className = "file-thumb-wrap";
          wrap.appendChild(img);
          enableDragScroll(wrap);

          var scale = zoomState[filePath] || 1;
          img.dataset.scale = scale.toString();
          img.style.transform = "scale(" + scale + ")";
          if (scale > 1) {
            wrap.classList.add("zoomed");
          }
          zoomIn.addEventListener("click", function () {
            var next = Math.min(3, parseFloat(img.dataset.scale || "1") + 0.25);
            img.dataset.scale = next.toString();
            img.style.transform = "scale(" + next + ")";
            zoomState[filePath] = next;
            wrap.classList.toggle("zoomed", next > 1);
          });
          zoomOut.addEventListener("click", function () {
            var next = Math.max(0.5, parseFloat(img.dataset.scale || "1") - 0.25);
            img.dataset.scale = next.toString();
            img.style.transform = "scale(" + next + ")";
            zoomState[filePath] = next;
            wrap.classList.toggle("zoomed", next > 1);
          });
          rotateLeft.addEventListener("click", function () {
            rotateFile(filePath, "left", img);
          });
          rotateRight.addEventListener("click", function () {
            rotateFile(filePath, "right", img);
          });
          del.addEventListener("click", function () {
            if (!confirm("Delete this scan?")) return;
            var folder = filePath.replace(/\\[^\\]+$/, "");
            var nodes = Array.prototype.slice.call(filesEl.querySelectorAll("li[data-path]"));
            var domIndex = nodes.indexOf(li);
            apiPost("/api/delete", { Path: filePath, Index: domIndex, Folder: folder }).then(function (res) {
              if (!res.Ok) {
                alert(res.Message || "Delete failed");
                return;
              }
              delete zoomState[filePath];
              delete imageVersion[filePath];
              delete scrollState[filePath];
              lastFilesKey = "";
              refreshStatus();
            });
          });

          actions.appendChild(ball);
          actions.appendChild(zoomOut);
          actions.appendChild(zoomIn);
          actions.appendChild(rotateLeft);
          actions.appendChild(rotateRight);
          actions.appendChild(del);

          var savedScroll = scrollState[filePath];
          if (savedScroll) {
            wrap.scrollLeft = savedScroll.left;
            wrap.scrollTop = savedScroll.top;
          }

          wrap.addEventListener("scroll", function () {
            scrollState[filePath] = { left: wrap.scrollLeft, top: wrap.scrollTop };
          });

          li.dataset.path = filePath;
          li.draggable = false;
          ball.addEventListener("dragstart", function (e) {
            isDragging = true;
            dragPath = filePath;
            li.classList.add("dragging-card");
            e.dataTransfer.effectAllowed = "move";
            e.dataTransfer.setData("text/plain", filePath);
            if (e.dataTransfer.setDragImage) {
              e.dataTransfer.setDragImage(li, li.offsetWidth / 2, 20);
            }
          });
          ball.addEventListener("dragend", function () {
            isDragging = false;
            dragPath = "";
            li.classList.remove("dragging-card");
          });
          li.appendChild(wrap);
          li.appendChild(actions);
          filesEl.appendChild(li);
        });
        if (status.Files.length > previousCount) {
          setTimeout(function () {
            window.scrollTo(0, document.body.scrollHeight);
          }, 0);
        }
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
      imageVersion[path] = Date.now();
      if (img && img.dataset.baseSrc) {
        img.src = img.dataset.baseSrc + "?v=" + imageVersion[path];
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
    if (name === "rotate_left") return "M7.8 7.1H12V4l4.5 4.5L12 13V9.9H7.8a5.7 5.7 0 1 0 5.6 7.2l2 .6A7.7 7.7 0 1 1 7.8 7.1z";
    if (name === "rotate_right") return "M16.2 7.1H12V4L7.5 8.5 12 13V9.9h4.2a5.7 5.7 0 1 1-5.6 7.2l-2 .6a7.7 7.7 0 1 0 7.6-10.6z";
    return "M6 7h12l-1 14H7L6 7zm3-3h6l1 2H8l1-2z";
  }

  function enableDragScroll(container) {
    var isDown = false;
    var startX = 0;
    var startY = 0;
    var scrollLeft = 0;
    var scrollTop = 0;

    container.style.cursor = "grab";

    container.addEventListener("mousedown", function (e) {
      if (e.button !== 0) return;
      isDown = true;
      container.style.cursor = "grabbing";
      startX = e.pageX - container.offsetLeft;
      startY = e.pageY - container.offsetTop;
      scrollLeft = container.scrollLeft;
      scrollTop = container.scrollTop;
    });

    container.addEventListener("mouseleave", function () {
      isDown = false;
      container.style.cursor = "grab";
    });

    container.addEventListener("mouseup", function () {
      isDown = false;
      container.style.cursor = "grab";
    });

    container.addEventListener("mousemove", function (e) {
      if (!isDown) return;
      e.preventDefault();
      var x = e.pageX - container.offsetLeft;
      var y = e.pageY - container.offsetTop;
      var walkX = x - startX;
      var walkY = y - startY;
      container.scrollLeft = scrollLeft - walkX;
      container.scrollTop = scrollTop - walkY;
    });
  }

  function enableReorder() {

    filesEl.addEventListener("dragover", function (e) {
      if (!dragPath) return;
      var li = e.target.closest("li");
      if (!li || !li.dataset.path) return;
      e.preventDefault();
      e.dataTransfer.dropEffect = "move";
    });

    filesEl.addEventListener("drop", function (e) {
      if (!dragPath) return;
      var li = e.target.closest("li");
      if (!li || !li.dataset.path) return;
      e.preventDefault();

      var targetPath = li.dataset.path || "";
      if (!targetPath || targetPath === dragPath) {
        dragPath = "";
        return;
      }

      var ordered = Array.prototype.map.call(filesEl.querySelectorAll("li[data-path]"), function (liItem) {
        return liItem.dataset.path;
      });
      var fromIndex = ordered.indexOf(dragPath);
      var toIndex = ordered.indexOf(targetPath);
      if (fromIndex === -1 || toIndex === -1) {
        dragPath = "";
        return;
      }
      ordered.splice(fromIndex, 1);
      ordered.splice(toIndex, 0, dragPath);

      dragPath = "";

      apiPost("/api/reorder", { Files: ordered }).then(function (res) {
        if (!res.Ok) {
          alert(res.Message || "Reorder failed");
        }
        lastFilesKey = "";
        refreshStatus();
      });
    });
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

  function handleExport(append, event) {
    if (event) {
      event.preventDefault();
      event.stopPropagation();
    }
    if (!document.getElementById("outputPath").value && lastJobDir) {
      document.getElementById("outputPath").value = lastJobDir;
    }
    var payload = {
      Format: document.getElementById("format").value,
      OutputPath: document.getElementById("outputPath").value,
      BaseName: document.getElementById("baseName").value,
      Append: !!append,
      AppendPath: append ? (document.getElementById("outputPath").value + "\\" + document.getElementById("baseName").value + ".pdf") : ""
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
  }

  document.getElementById("exportBtn").addEventListener("click", function (event) {
    handleExport(false, event);
  });

  document.getElementById("appendBtn").addEventListener("click", function (event) {
    event.preventDefault();
    event.stopPropagation();
    apiGet("/api/pickpdf").then(function (res) {
      if (!res || !res.Path) {
        return;
      }
      var path = res.Path;
      var dir = path.replace(/\\[^\\]+$/, "");
      var base = path.replace(/^.*[\\\/]/, "").replace(/\.pdf$/i, "");
      document.getElementById("outputPath").value = dir;
      document.getElementById("baseName").value = base;
      handleExport(true);
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

  enableReorder();
  loadDevices();
  refreshStatus();
  setInterval(refreshStatus, 2000);
})();
