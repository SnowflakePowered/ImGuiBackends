using ImGuiNET;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImGuiBackends.Direct3D11
{
    public partial class ImGuiBackendDirect3D11
    {
        unsafe struct ViewportData
        {
            IDXGISwapChain* SwapChain;
            ID3D11RenderTargetView* RTView;
        }

        private void ShutdownPlatformInterface()
        {
            ImGui.DestroyPlatformWindows();
        }

        private void InitPlatformInterface()
        {
            ImGuiPlatformIOPtr ptr = ImGui.GetPlatformIO();

            //_createWindow = CreateWindow;
            //_destroyWindow = DestroyWindow;
            //_setWindowSize = SetWindowSize;
            //_renderWindow = RenderWindow;
            //_swapBuffers = SwapBuffers;

            //ptr.Renderer_CreateWindow = Marshal.GetFunctionPointerForDelegate(_createWindow);
            //ptr.Renderer_DestroyWindow = Marshal.GetFunctionPointerForDelegate(_destroyWindow);
            //ptr.Renderer_SetWindowSize = Marshal.GetFunctionPointerForDelegate(_setWindowSize);
            //ptr.Renderer_RenderWindow = Marshal.GetFunctionPointerForDelegate(_renderWindow);
            //ptr.Renderer_SwapBuffers = Marshal.GetFunctionPointerForDelegate(_swapBuffers);
        }
    }
}
