using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

#if GLES
using Silk.NET.OpenGLES;
#elif GL
using Silk.NET.OpenGL;
#endif

namespace ImGuiBackends.OpenGL3
{
    public partial class ImGuiBackendOpenGL
    {
        private delegate void RenderWindowDelegate(ImGuiViewportPtr ptr, IntPtr v);
        private RenderWindowDelegate? _renderWindow;

        private void InitPlatformInterface()
        {
            ImGuiPlatformIOPtr io = ImGui.GetPlatformIO();
            _renderWindow = RenderWindow;

            // what if we make a vtable so we can use function pointers instead..?
            io.Renderer_RenderWindow = Marshal.GetFunctionPointerForDelegate(_renderWindow);
        }

        private void RenderWindow(ImGuiViewportPtr viewport, IntPtr v)
        {
            if (_gl == null)
                return;

            if (!(viewport.Flags.HasFlag(ImGuiViewportFlags.NoRendererClear)))
            {
                Vector4 clear_color = new(0.0f, 0.0f, 0.0f, 1.0f);
                _gl.ClearColor(clear_color.X, clear_color.Y, clear_color.Z, clear_color.W);
                _gl.Clear((uint)GLEnum.ColorBufferBit);
            }
            this.RenderDrawData(viewport.DrawData);
        }

        private void ShutdownPlatformInterface()
        {
            ImGui.DestroyPlatformWindows();
        }
    }
}
