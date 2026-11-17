const { contextBridge } = require("electron");

contextBridge.exposeInMainWorld("umaConnectDesktop", {
  platform: process.platform,
  appName: "UMA Connect"
});

