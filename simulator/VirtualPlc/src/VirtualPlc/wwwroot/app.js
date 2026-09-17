"use strict";

const POLL_INTERVAL_MS = 100;
const CHANGE_HIGHLIGHT_MS = 1500;
const MAX_HISTORY_ITEMS = 80;
const heartbeatSignalNames = new Set([
  "PLC_Heartbeat_Req",
  "PC_Heartbeat_Resp"
]);

const signalDescriptions = {
  PLC_Heartbeat_Req: "PLC 心跳请求",
  PC_Heartbeat_Resp: "上位机心跳应答",
  PC_System_Ready: "上位机系统就绪",
  PLC_System_Fault: "PLC 系统故障",
  PLC_Mode_Auto: "PLC 自动模式",
  Soft_Stop_Cmd: "软停止命令",
  Manual_Zone_Occupied: "人工区域占用",
  Manual_Flip_Complete: "人工翻面完成",
  XY_Move_Cmd: "XY 移动命令",
  XY_Pos_Confirmed: "XY 到位状态",
  Camera_Target_X: "相机目标 X",
  Camera_Target_Y: "相机目标 Y",
  Camera_Target_Z: "相机目标 Z",
  Z_Axis_Move_Status: "Z 轴移动状态",
  Machine_Current_Pos_X: "设备当前 X",
  Machine_Current_Pos_Y: "设备当前 Y",
  Flip_Trigger_Cmd: "翻面触发命令",
  Flip_Status: "翻面执行状态",
  Flip_Result_Angle: "翻面结果角度",
  Sorting_Part_Index: "分拣零件槽位",
  Sorting_Cmd: "分拣命令",
  Sorting_Exec_Status: "分拣执行状态",
  Pallet_Lock_Cmd: "托盘锁紧命令",
  Pallet_Lock_Status: "托盘锁紧状态",
  NG_Zone_Count: "NG 区数量",
  Pending_Zone_Count: "待定区数量",
  Zone_Config_Ready: "区域配置就绪",
  Zone_Config_Ack: "区域配置确认",
  Retry_Cmd: "重试命令"
};

const registerMeanings = {
  XY_Move_Cmd: { 0: "空闲，等待上位机指令", 1: "去上料位", 2: "去检测位", 3: "去翻转/扫码位", 4: "去下料位", 5: "去扫码位" },
  XY_Pos_Confirmed: { 0: "运动中", 1: "已到位", 2: "超时未到位" },
  Z_Axis_Move_Status: { 0: "空闲", 1: "运动中", 2: "已到位", 3: "超时/失败" },
  Flip_Trigger_Cmd: { 0: "空闲，等待上位机指令", 1: "翻转 90°", 2: "翻转 180°" },
  Flip_Status: { 0: "空闲", 1: "执行中", 2: "已完成", 3: "失败" },
  Sorting_Cmd: { 0: "空闲，等待上位机指令", 1: "执行分拣", 2: "满盘报警" },
  Sorting_Exec_Status: { 0: "空闲", 1: "执行中", 2: "成功", 3: "失败", 4: "满盘" },
  Pallet_Lock_Cmd: { 0: "解锁", 1: "锁紧" },
  Pallet_Lock_Status: { 0: "未锁/已解锁", 1: "已锁紧", 2: "锁紧失败" },
  Zone_Config_Ready: { 0: "空闲，等待上位机配置完成信号", 1: "配置完成" },
  Zone_Config_Ack: { 0: "未收到", 1: "已更新" },
  Retry_Cmd: { 0: "空闲，等待上位机指令", 1: "重试当前动作", 2: "跳过/放弃当前零件", 3: "复位分拣队列" }
};

const emptyWhenZero = {
  Camera_Target_X: "空，等待上位机写入目标 X 坐标",
  Camera_Target_Y: "空，等待上位机写入目标 Y 坐标",
  Camera_Target_Z: "空，等待上位机写入目标 Z 坐标",
  Machine_Current_Pos_X: "空，等待 PLC 到位后反馈 X 坐标",
  Machine_Current_Pos_Y: "空，等待 PLC 到位后反馈 Y 坐标",
  Flip_Result_Angle: "空，等待 PLC 反馈实际翻转角度",
  Sorting_Part_Index: "空，等待上位机写入零件槽位号",
  NG_Zone_Count: "空，等待上位机写入 NG 区数量",
  Pending_Zone_Count: "空，等待上位机写入待定区数量"
};

const binaryRegisterNames = new Set([
  "Pallet_Lock_Cmd",
  "Zone_Config_Ready",
  "Zone_Config_Ack"
]);

const coilMeanings = {
  PLC_Heartbeat_Req: { 0: "低电平", 1: "高电平" },
  PC_Heartbeat_Resp: { 0: "低电平", 1: "高电平" },
  PC_System_Ready: { 0: "未就绪", 1: "已就绪" },
  PLC_System_Fault: { 0: "正常", 1: "故障" },
  PLC_Mode_Auto: { 0: "非自动模式", 1: "自动模式" },
  Soft_Stop_Cmd: { 0: "未请求", 1: "请求软停" },
  Manual_Zone_Occupied: { 0: "无人介入", 1: "人工介入" },
  Manual_Flip_Complete: { 0: "未确认", 1: "已确认" }
};

const elements = Object.fromEntries([
  "connectionDot", "connectionText", "lastRefresh", "signalCount",
  "signalBreakdown", "activeAction", "faultCount", "faultNames", "changeCount", "searchInput",
  "pauseButton", "plcToPcCoilRows", "plcToPcRegisterRows", "pcToPlcCoilRows",
  "pcToPlcRegisterRows", "plcToPcCoilCount", "plcToPcRegisterCount",
  "pcToPlcCoilCount", "pcToPlcRegisterCount", "plcToPcTotal", "pcToPlcTotal",
  "historyList", "clearHistoryButton"
].map((id) => [id, document.getElementById(id)]));

const monitor = {
  previousValues: new Map(),
  changedAt: new Map(),
  history: [],
  historyVersion: 0,
  renderedHistoryVersion: -1,
  totalChanges: 0,
  paused: false,
  latestSnapshot: null,
  timer: null
};

function pointKey(area, point) {
  return `${area}:${point.address}`;
}

function pointValue(area, point) {
  return area === "coil" ? (point.value ? 1 : 0) : point.rawValue;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function directionText(direction) {
  return direction === "PcToPlc" ? "上位机 → PLC" : "PLC → 上位机";
}

function actionText(action) {
  const labels = {
    Move: "移动",
    Flip: "翻面",
    Sort: "分拣",
    PalletLock: "托盘锁紧",
    ZoneConfig: "区域配置"
  };
  return action ? (labels[action] ?? action) : "空闲";
}

function updateChanges(area, points) {
  const now = Date.now();
  for (const point of points) {
    const key = pointKey(area, point);
    const value = pointValue(area, point);
    if (monitor.previousValues.has(key)) {
      const previous = monitor.previousValues.get(key);
      if (previous !== value) {
        if (!heartbeatSignalNames.has(point.name)) {
          monitor.changedAt.set(key, now);
          monitor.totalChanges += 1;
          monitor.history.unshift({
            key,
            time: new Date(),
            address: point.address,
            name: point.name,
            direction: point.direction,
            from: previous,
            to: value
          });
          monitor.historyVersion += 1;
        }
      }
    }
    monitor.previousValues.set(key, value);
  }
  monitor.history = monitor.history.slice(0, MAX_HISTORY_ITEMS);
}

function isRecentlyChanged(area, point) {
  if (heartbeatSignalNames.has(point.name)) {
    return false;
  }

  const changedAt = monitor.changedAt.get(pointKey(area, point));
  return changedAt && Date.now() - changedAt < CHANGE_HIGHLIGHT_MS;
}

function matchesFilters(point) {
  const query = elements.searchInput.value.trim().toLowerCase();
  if (!query) {
    return true;
  }

  const description = signalDescriptions[point.name] ?? "";
  return `${point.address} ${point.name} ${description}`.toLowerCase().includes(query);
}

function pointNameHtml(point) {
  const description = signalDescriptions[point.name] ?? "";
  return `<span class="signal-name"><span>${escapeHtml(point.name)}</span><small>${escapeHtml(description)}</small></span>`;
}

function binaryValueHtml(value, meaning, changed) {
  const numeric = value ? 1 : 0;
  const stateClass = value ? "binary-on" : "binary-off";
  const changedClass = changed ? "value-changed" : "";
  return `<span class="value-pill ${stateClass} ${changedClass}">${numeric}</span><span class="value-detail">${escapeHtml(meaning ?? (value ? "ON" : "OFF"))}</span>`;
}

function coilValueHtml(point, changed) {
  const numeric = point.value ? 1 : 0;
  return binaryValueHtml(point.value, coilMeanings[point.name]?.[numeric], changed);
}

function registerTone(point) {
  const value = point.rawValue;
  if (["XY_Pos_Confirmed", "Z_Axis_Move_Status", "Flip_Status", "Sorting_Exec_Status", "Pallet_Lock_Status"].includes(point.name)) {
    if ((point.name === "XY_Pos_Confirmed" && value === 2) ||
        (point.name === "Z_Axis_Move_Status" && value === 3) ||
        (point.name === "Flip_Status" && value === 3) ||
        (point.name === "Sorting_Exec_Status" && value >= 3) ||
        (point.name === "Pallet_Lock_Status" && value === 2)) {
      return "tone-danger";
    }
    if ((point.name === "XY_Pos_Confirmed" && value === 1) ||
        (point.name === "Z_Axis_Move_Status" && value === 2) ||
        (point.name === "Flip_Status" && value === 2) ||
        (point.name === "Sorting_Exec_Status" && value === 2) ||
        (point.name === "Pallet_Lock_Status" && value === 1)) {
      return "tone-success";
    }
    return value === 0 ? "tone-neutral" : "tone-info";
  }

  if (registerMeanings[point.name]) {
    return value === 0 ? "tone-neutral" : "tone-command";
  }
  return value === 0 ? "tone-neutral" : "tone-data";
}

function registerValueHtml(point, changed) {
  const meaning = registerMeanings[point.name]?.[point.rawValue];
  const moveIsActive = monitor.latestSnapshot?.activeAction === "Move" ||
    monitor.latestSnapshot?.holdingRegisters?.some((item) =>
      item.name === "Z_Axis_Move_Status" && item.rawValue === 1);
  const xyStatusIsInitiallyEmpty = point.name === "XY_Pos_Confirmed" &&
    point.rawValue === 0 && !moveIsActive;
  const emptyMeaning = xyStatusIsInitiallyEmpty
    ? "空，等待 PLC 执行移动后反馈"
    : (point.rawValue === 0 ? emptyWhenZero[point.name] : null);
  if (emptyMeaning) {
    const changedClass = changed ? "value-changed" : "";
    return `<span class="value-pill empty-value ${changedClass}">—</span><span class="value-detail">${escapeHtml(emptyMeaning)}</span>`;
  }

  if (binaryRegisterNames.has(point.name)) {
    return binaryValueHtml(point.rawValue === 1, meaning, changed);
  }

  const signed = point.signedValue !== point.rawValue ? ` · 有符号 ${point.signedValue}` : "";
  const detail = meaning ? `${meaning}${signed}` : `UINT16${signed}`;
  const changedClass = changed ? "value-changed" : "";
  return `<span class="value-pill numeric-value ${registerTone(point)} ${changedClass}">${point.rawValue}</span><span class="value-detail">${escapeHtml(detail)}</span>`;
}

function renderRows(area, points, direction, target, countTarget) {
  const directionalPoints = points.filter((point) => point.direction === direction);
  const visible = directionalPoints.filter(matchesFilters);
  const countText = `${visible.length} / ${directionalPoints.length} 个信号`;
  if (countTarget.textContent !== countText) {
    countTarget.textContent = countText;
  }

  if (!target.dataset.initialized) {
    target.replaceChildren();
    target.dataset.initialized = "true";
  }

  const existingRows = new Map(
    [...target.querySelectorAll("tr[data-point-key]")]
      .map((row) => [row.dataset.pointKey, row]));
  const currentKeys = new Set();

  for (const point of directionalPoints) {
    const key = pointKey(area, point);
    currentKeys.add(key);
    let row = existingRows.get(key);
    if (!row) {
      row = document.createElement("tr");
      row.dataset.pointKey = key;
      row.innerHTML = `
        <td class="address-cell">${escapeHtml(point.address)}<small>PDU ${point.pduOffset}</small></td>
        <td>${pointNameHtml(point)}</td>
        <td class="value-cell"></td>`;
      target.appendChild(row);
    }

    const shouldHide = !matchesFilters(point);
    if (row.hidden !== shouldHide) {
      row.hidden = shouldHide;
    }
    const changed = isRecentlyChanged(area, point);
    const value = area === "coil" ? coilValueHtml(point, changed) : registerValueHtml(point, changed);
    const valueCell = row.querySelector(".value-cell");
    const valueToken = `${pointValue(area, point)}:${changed}:${value}`;
    if (valueCell.dataset.valueToken !== valueToken) {
      valueCell.innerHTML = value;
      valueCell.dataset.valueToken = valueToken;
    }
  }

  for (const [key, row] of existingRows) {
    if (!currentKeys.has(key)) {
      row.remove();
    }
  }

  let emptyRow = target.querySelector("tr[data-empty-row]");
  if (visible.length === 0) {
    if (!emptyRow) {
      emptyRow = document.createElement("tr");
      emptyRow.dataset.emptyRow = "true";
      emptyRow.innerHTML = '<td colspan="3" class="empty-cell">没有符合筛选条件的信号</td>';
      target.appendChild(emptyRow);
    }
    if (emptyRow.hidden) {
      emptyRow.hidden = false;
    }
  } else if (emptyRow) {
    if (!emptyRow.hidden) {
      emptyRow.hidden = true;
    }
  }
}

function renderOverview(snapshot) {
  const signalTotal = snapshot.coils.length + snapshot.holdingRegisters.length;
  elements.signalCount.textContent = signalTotal;
  elements.signalBreakdown.textContent = `0x ${snapshot.coils.length} 个 · 4x ${snapshot.holdingRegisters.length} 个`;
  elements.activeAction.textContent = actionText(snapshot.activeAction);
  const faults = [...snapshot.activeFaults];
  if (snapshot.communicationTimedOut) {
    faults.unshift("CommunicationTimeout");
  }
  elements.faultCount.textContent = faults.length;
  elements.faultNames.textContent = faults.length ? faults.join("、") : "无故障";
  elements.changeCount.textContent = monitor.totalChanges;
}

function renderHistory() {
  if (monitor.renderedHistoryVersion === monitor.historyVersion) {
    return;
  }
  monitor.renderedHistoryVersion = monitor.historyVersion;

  if (monitor.history.length === 0) {
    elements.historyList.innerHTML = '<p class="history-empty">监控启动后，线圈和寄存器的变化会显示在这里。</p>';
    return;
  }

  elements.historyList.innerHTML = monitor.history.map((item) => `
    <div class="history-item">
      <span class="history-time">${item.time.toLocaleTimeString("zh-CN", { hour12: false })}</span>
      <span class="history-signal" title="${escapeHtml(item.name)} · ${directionText(item.direction)}">
        <span class="history-address">${escapeHtml(item.address)}</span>${escapeHtml(item.name)}
        <small>${directionText(item.direction)}</small>
      </span>
      <span class="history-change">${item.from} → ${item.to}</span>
    </div>`).join("");
}

function renderSnapshot(snapshot) {
  monitor.latestSnapshot = snapshot;
  renderOverview(snapshot);
  renderRows("coil", snapshot.coils, "PlcToPc", elements.plcToPcCoilRows, elements.plcToPcCoilCount);
  renderRows("register", snapshot.holdingRegisters, "PlcToPc", elements.plcToPcRegisterRows, elements.plcToPcRegisterCount);
  renderRows("coil", snapshot.coils, "PcToPlc", elements.pcToPlcCoilRows, elements.pcToPlcCoilCount);
  renderRows("register", snapshot.holdingRegisters, "PcToPlc", elements.pcToPlcRegisterRows, elements.pcToPlcRegisterCount);
  const plcToPcCount = [...snapshot.coils, ...snapshot.holdingRegisters]
    .filter((point) => point.direction === "PlcToPc").length;
  const pcToPlcCount = [...snapshot.coils, ...snapshot.holdingRegisters]
    .filter((point) => point.direction === "PcToPlc").length;
  elements.plcToPcTotal.textContent = `${plcToPcCount} 个点位`;
  elements.pcToPlcTotal.textContent = `${pcToPlcCount} 个点位`;
  renderHistory();
}

function setConnection(online, message) {
  elements.connectionDot.className = `status-dot ${online ? "online" : "offline"}`;
  elements.connectionText.textContent = online ? "监控已连接" : "监控已断开";
  elements.lastRefresh.textContent = message;
}

async function poll() {
  if (!monitor.paused) {
    try {
      const response = await fetch("/api/simulator/state", { cache: "no-store" });
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const snapshot = await response.json();
      updateChanges("coil", snapshot.coils);
      updateChanges("register", snapshot.holdingRegisters);
      renderSnapshot(snapshot);
      setConnection(true, `更新于 ${new Date().toLocaleTimeString("zh-CN", { hour12: false })}`);
    } catch (error) {
      setConnection(false, `读取失败：${error.message}`);
    }
  }

  monitor.timer = window.setTimeout(poll, POLL_INTERVAL_MS);
}

function rerender() {
  if (monitor.latestSnapshot) {
    renderSnapshot(monitor.latestSnapshot);
  }
}

elements.searchInput.addEventListener("input", rerender);
elements.pauseButton.addEventListener("click", () => {
  monitor.paused = !monitor.paused;
  elements.pauseButton.textContent = monitor.paused ? "继续刷新" : "暂停刷新";
  elements.pauseButton.classList.toggle("active", monitor.paused);
  if (monitor.paused) {
    elements.connectionText.textContent = "监控已暂停";
    elements.lastRefresh.textContent = "页面保留最后一次状态";
    elements.connectionDot.className = "status-dot";
  }
});
elements.clearHistoryButton.addEventListener("click", () => {
  monitor.history = [];
  monitor.historyVersion += 1;
  monitor.totalChanges = 0;
  renderHistory();
  elements.changeCount.textContent = "0";
});

poll();
