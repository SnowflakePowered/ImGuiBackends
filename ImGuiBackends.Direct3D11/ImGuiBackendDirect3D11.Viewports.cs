using ImGuiNET;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ImGuiBackends.Direct3D11
{
    public partial class ImGuiBackendDirect3D11
    {
        [StructLayout(LayoutKind.Sequential)]
        unsafe struct ViewportData
        {
            public IDXGISwapChain* SwapChain;
            public ID3D11RenderTargetView* RTView;
        }

        private void ShutdownPlatformInterface()
        {
            ImGui.DestroyPlatformWindows();
        }

        private unsafe void CreateWindow(ImGuiViewport* viewport)
        {
            var vd = (ViewportData*)Marshal.AllocHGlobal(Marshal.SizeOf<ViewportData>());
            viewport->RendererUserData = vd;

            // PlatformHandleRaw should always be a HWND, whereas PlatformHandle might be a higher-level handle (e.g. GLFWWindow*, SDL_Window*).
            // Some backend will leave PlatformHandleRaw NULL, in which case we assume PlatformHandle will contain the HWND.
            void * hWnd = viewport->PlatformHandleRaw;
            if (hWnd == null)
                hWnd = viewport->PlatformHandle;

            SwapChainDesc desc = new()
            {
                BufferDesc = new()
                {
                    Width = (uint)viewport->Size.X,
                    Height = (uint)viewport->Size.Y,
                    Format = Format.FormatR8G8B8A8Unorm
                },
                SampleDesc = new(1, 0),
                BufferUsage = DXGI.UsageRenderTargetOutput,
                BufferCount = 1,
                OutputWindow = (nint)hWnd,
                Windowed = 1,
                SwapEffect = SwapEffect.SwapEffectDiscard,
                Flags = 0
            };

            _pFactory->CreateSwapChain((IUnknown*)_pd3dDevice, &desc, &vd->SwapChain);

            if (vd->SwapChain != null)
            {
                ID3D11Texture2D* pBackBuffer = null;
                Guid tex2DRiid = ID3D11Texture2D.Guid;
                vd->SwapChain->GetBuffer(0, &tex2DRiid, (void**)&pBackBuffer);
                _pd3dDevice->CreateRenderTargetView((ID3D11Resource*)pBackBuffer, null, &vd->RTView);
                pBackBuffer->Release();
            }
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
