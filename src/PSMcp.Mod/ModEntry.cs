using System;
using System.Collections.Concurrent;
using System.Collections;
using Newtonsoft.Json;
using PSMcp.Contracts;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PSMcp.Mod
{
    public sealed class ModEntry : IMod
    {
        private ModLog? log;
        private ConfigEntry<int>? port;
        private GameObject? host;
        private HttpBridge? bridge;
        private GameSession? session;

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            port = ModHelper.GetConfig(manifest.ID).Define("port", Protocol.DefaultPort, "Bridge",
                () => "Local bridge port", min: 1024, max: 65535, requiresRestart: true);
        }

        public void OnEnable()
        {
            try
            {
                var queue = new MainThreadQueue(error => log?.Error(error.ToString()));
                var views = new MainThreadQueue(error => log?.Error(error.ToString()));
                var errors = new ConcurrentQueue<string>();
                session = new GameSession(new GameAdapter(), state => JsonConvert.SerializeObject(state, HttpBridge.JsonSettings));
                host = new GameObject("PSMcp.Bridge");
                UnityEngine.Object.DontDestroyOnLoad(host);
                var pump = host.AddComponent<BridgePump>();
                NativeInput.Initialize(pump);
                pump.Initialize(queue, views, errors, message => log?.Error(message));
                var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                var listenPort = port!.Value;
                bridge = new HttpBridge(listenPort, token, queue, session, errors.Enqueue, views, () => ViewCapture.Capture(session));
                bridge.Start();
                ModHelper.WriteModDataFileText(Protocol.ModId, "connection.json", JsonConvert.SerializeObject(
                    new ConnectionInfo { BaseUrl = "http://127.0.0.1:" + listenPort + "/", Token = token }, HttpBridge.JsonSettings));
                ModHook.OnGameLoadedLate += OnGameLoaded;
                SceneManager.activeSceneChanged += OnSceneChanged;
                log?.Log("Bridge ready. MCP connection file: " + ModHelper.GetModDataFolder(Protocol.ModId) + "/connection.json");
            }
            catch
            {
                OnDisable();
                throw;
            }
        }

        private void OnGameLoaded() { NativeInput.Cancel(); session?.Reset(); }
        private void OnSceneChanged(Scene previous, Scene next) { NativeInput.Cancel(); session?.Reset(); }

        public void OnDisable()
        {
            NativeInput.Cancel();
            ModHook.OnGameLoadedLate -= OnGameLoaded;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            bridge?.Dispose();
            bridge = null;
            if (host != null) UnityEngine.Object.Destroy(host);
            host = null;
            session = null;
        }
    }

    public sealed class BridgePump : MonoBehaviour
    {
        private MainThreadQueue? queue;
        private MainThreadQueue? views;
        private ConcurrentQueue<string>? errors;
        private Action<string>? logError;

        public void Initialize(MainThreadQueue queue, MainThreadQueue views, ConcurrentQueue<string> errors, Action<string> logError)
        {
            this.queue = queue;
            this.views = views;
            this.errors = errors;
            this.logError = logError;
            StartCoroutine(CaptureFrames());
        }

        private IEnumerator CaptureFrames()
        {
            var end = new WaitForEndOfFrame();
            while (true) { yield return end; views?.Drain(1); }
        }

        private void Update()
        {
            queue?.Drain();
            for (var i = 0; i < 8 && errors != null && errors.TryDequeue(out var message); i++) logError?.Invoke(message);
        }

        private void OnDestroy() { queue?.Dispose(); views?.Dispose(); NativeInput.Reset(); }
    }
}
