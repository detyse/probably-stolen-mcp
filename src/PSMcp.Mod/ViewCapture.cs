using System;
using PSMcp.Contracts;
using UnityEngine;

namespace PSMcp.Mod
{
    internal static class ViewCapture
    {
        // Called only after WaitForEndOfFrame, including overlays and inspection art.
        public static BridgeResponse Capture(GameSession session)
        {
            if (Screen.width <= 0 || Screen.height <= 0) return BridgeResponse.Error("view_unavailable", "No game frame is available.");
            Texture2D? source = null;
            Texture2D? scaled = null;
            RenderTexture? target = null;
            var previous = RenderTexture.active;
            try
            {
                // The game's scanner classifies every ScreenCapture call as file access,
                // including its in-memory overload. Read the rendered backbuffer directly;
                // this keeps screenshots in memory and needs no filesystem capability.
                RenderTexture.active = null;
                source = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                source.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                source.Apply();
                var ratio = Math.Min(1f, 1600f / Math.Max(source.width, source.height));
                var width = Math.Max(1, (int)(source.width * ratio));
                var height = Math.Max(1, (int)(source.height * ratio));
                target = RenderTexture.GetTemporary(width, height, 0);
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                scaled = new Texture2D(width, height, TextureFormat.RGB24, false);
                scaled.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                scaled.Apply();
                var response = BridgeResponse.Ok(session.Observe(), "Rendered game frame. Snapshot and image were captured on the same game-thread turn.");
                response.View = new GameView { SnapshotId = response.State!.SnapshotId, Width = width, Height = height,
                    ImageBase64 = Convert.ToBase64String(ImageConversion.EncodeToJPG(scaled, 90)) };
                return response;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (scaled != null) UnityEngine.Object.Destroy(scaled);
                if (source != null) UnityEngine.Object.Destroy(source);
            }
        }
    }
}
