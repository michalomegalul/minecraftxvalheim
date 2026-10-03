using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using HarmonyLib;
using UnityEngine;

namespace Valcraft
{
    /// <summary>
    /// Draws Minecraft's hand and GUI (hotbar, hearts, inventory, chat) over Valheim, read from the
    /// shared-memory frame Minecraft publishes (see GuiCapture.java for the layout). While it's
    /// showing, Valheim's own HUD and small minimap are hidden.
    /// </summary>
    internal static class GuiOverlay
    {
        private const string FilePath = "/dev/shm/valcraft_gui";
        private const int Header = 32;
        private const int Magic = 0x49474356;

        private static MemoryMappedFile _file;
        private static MemoryMappedViewAccessor _view;
        private static long _viewSize;
        private static Texture2D _tex;
        private static long _lastFrame;
        private static float _lastFrameTime = -10f;
        /// <summary>F10: flip the overlay if it ever shows upside down.</summary>
        public static bool Flip;

        /// <summary>Minecraft frames are arriving (so its hotbar etc. are visible).</summary>
        public static bool Showing => Follow.Linked && _tex != null && Time.time - _lastFrameTime < 2f;

        /// <summary>Hide Valheim's HUD in favour of Minecraft's.</summary>
        public static bool ReplaceHud => Showing && Follow.Active;

        public static void Update()
        {
            if (!Follow.Linked) return;
            try
            {
                if (!Open()) return;
                if (_view.ReadInt32(0) != Magic) return;
                int w = _view.ReadInt32(4), h = _view.ReadInt32(8);
                long frame = _view.ReadInt64(16);
                if ((frame & 1) != 0 || frame == _lastFrame || w <= 0 || h <= 0) return;
                long need = Header + (long)w * h * 4;
                if (need > _viewSize)
                {
                    Close(); // Minecraft's window grew; remap next frame
                    return;
                }
                if (_tex == null || _tex.width != w || _tex.height != h)
                {
                    if (_tex != null) UnityEngine.Object.Destroy(_tex);
                    _tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                }
                Upload(w * h * 4);
                _lastFrame = frame;
                _lastFrameTime = Time.time;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"GUI overlay: {e.Message}");
                Close();
            }
        }

        private static unsafe void Upload(int bytes)
        {
            byte* ptr = null;
            var handle = _view.SafeMemoryMappedViewHandle;
            handle.AcquirePointer(ref ptr);
            try
            {
                _tex.LoadRawTextureData((IntPtr)(ptr + _view.PointerOffset + Header), bytes);
            }
            finally
            {
                handle.ReleasePointer();
            }
            _tex.Apply(false);
        }

        private static bool Open()
        {
            if (_view != null) return true;
            var info = new FileInfo(FilePath);
            if (!info.Exists || info.Length <= Header) return false;
            _file = MemoryMappedFile.CreateFromFile(FilePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _viewSize = info.Length;
            return true;
        }

        private static void Close()
        {
            _view?.Dispose();
            _file?.Dispose();
            _view = null;
            _file = null;
            _viewSize = 0;
        }

        /// <summary>Where the overlay is drawn (GUI coordinates, top-left origin).</summary>
        public static Rect ScreenRect
        {
            get
            {
                if (_tex == null) return Rect.zero;
                // Keep Minecraft's aspect ratio, centred (its window may not match Valheim's).
                float scale = Mathf.Min((float)Screen.width / _tex.width, (float)Screen.height / _tex.height);
                float w = _tex.width * scale, h = _tex.height * scale;
                return new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            }
        }

        public static void OnGUI()
        {
            if (!Showing || Event.current.type != EventType.Repaint || InputForward.ValheimUiOpen()) return;
            var rect = ScreenRect;
            // Readback rows arrive top-down on both backends in practice (seen in-game), and Unity
            // textures start at the bottom row, so flip by default; F10 toggles it.
            bool flip = !Flip;
            GUI.DrawTextureWithTexCoords(rect, _tex, flip ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1), true);
        }
    }

    [HarmonyPatch(typeof(Hud), "SetVisible")]
    internal static class HudSetVisiblePatch
    {
        private static void Prefix(ref bool visible)
        {
            if (GuiOverlay.ReplaceHud) visible = false;
        }
    }

    [HarmonyPatch(typeof(Minimap), "Update")]
    internal static class MinimapUpdatePatch
    {
        private static void Postfix(Minimap __instance)
        {
            if (GuiOverlay.ReplaceHud && __instance.m_smallRoot != null) __instance.m_smallRoot.SetActive(false);
        }
    }
}
