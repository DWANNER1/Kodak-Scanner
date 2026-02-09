(function () {
  function apiGet(path) {
    return fetch(path, { cache: "no-store" }).then(r => r.json());
  }

  function apiPost(path, body) {
    return fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body || {})
    }).then(r => r.json());
  }

  var deviceSelect = document.getElementById("deviceSelect");
  var statusEl = document.getElementById("status");
  var filesEl = document.getElementById("files");

  function loadDevices() {
    apiGet("/api/devices").then(function (devices) {
      deviceSelect.innerHTML = "";
      if (!devices || devices.length === 0) {
        var opt = document.createElement("option");
        opt.value = "";
        opt.textContent = "No devices found";
        deviceSelect.appendChild(opt);
        return;
      }
      devices.forEach(function (d) {
        var opt = document.createElement("option");
        opt.value = d.Id;
        opt.textContent = d.Name;
        deviceSelect.appendChild(opt);
      });
    });
  }

  function refreshStatus() {
    apiGet("/api/status").then(function (status) {
      if (!status) return;
      statusEl.textContent = status.State + ": " + status.Message;
      if (status.Files) {
        filesEl.innerHTML = "";
        status.Files.forEach(function (f) {
          var li = document.createElement("li");
          li.textContent = f;
          filesEl.appendChild(li);
        });
      }
    });
  }

  document.getElementById("scanBtn").addEventListener("click", function () {
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
    });
  });

  document.getElementById("exportBtn").addEventListener("click", function () {
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
    });
  });

  document.getElementById("clearBtn").addEventListener("click", function () {
    apiPost("/api/clear", {}).then(function () {
      refreshStatus();
    });
  });

  loadDevices();
  refreshStatus();
  setInterval(refreshStatus, 2000);
})();
