using ImGuiNET;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

        private delegate void CreateWindowDelegate(ImGuiViewportPtr ptr);
        private delegate void DestroyWindowDelegate(ImGuiViewportPtr ptr);
        private delegate void SetWindowSizeDelegate(ImGuiViewportPtr ptr, Vector2 size);
        private delegate void RenderWindowDelegate(ImGuiViewportPtr ptr, IntPtr v);
        private delegate void SwapBuffersDelegate(ImGuiViewportPtr ptr, IntPtr v);

        // Pin delegates to GC
        private CreateWindowDelegate? _createWindow;
        private DestroyWindowDelegate? _destroyWindow;
        private SetWindowSizeDelegate? _setWindowSize;
        private RenderWindowDelegate? _renderWindow;
        private SwapBuffersDelegate? _swapBuffers;

        private void InitPlatformInterface()
        {
            ImGuiPlatformIOPtr io = ImGui.GetPlatformIO();

            _createWindow = CreateWindow;
            _destroyWindow = DestroyWindow;
            _setWindowSize = SetWindowSize;
            _renderWindow = RenderWindow;
            _swapBuffers = SwapBuffers;

            // what if we make a vtable so we can use function pointers instead..?
            io.Renderer_CreateWindow = Marshal.GetFunctionPointerForDelegate(_createWindow);
            io.Renderer_DestroyWindow = Marshal.GetFunctionPointerForDelegate(_destroyWindow);
            io.Renderer_SetWindowSize = Marshal.GetFunctionPointerForDelegate(_setWindowSize);
            io.Renderer_RenderWindow = Marshal.GetFunctionPointerForDelegate(_renderWindow);
            io.Renderer_SwapBuffers = Marshal.GetFunctionPointerForDelegate(_swapBuffers);
        }

        private unsafe void CreateWindow(ImGuiViewportPtr viewport)
        {
            var vd = (ViewportData*)Marshal.AllocHGlobal(Marshal.SizeOf<ViewportData>());
            viewport.RendererUserData = (nint)vd;

            // PlatformHandleRaw should always be a HWND, whereas PlatformHandle might be a higher-level handle (e.g. GLFWWindow*, SDL_Window*).
            // Some backend will leave PlatformHandleRaw NULL, in which case we assume PlatformHandle will contain the HWND.
            nint hWnd = viewport.PlatformHandleRaw;
            if (hWnd == 0)
                hWnd = viewport.PlatformHandle;

            SwapChainDesc desc = new()
            {
                BufferDesc = new()
                {
                    Width = (uint)viewport.Size.X,
                    Height = (uint)viewport.Size.Y,
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

            _factory->CreateSwapChain((IUnknown*)_device, &desc, &vd->SwapChain);

            if (vd->SwapChain != null)
            {
                ID3D11Texture2D* pBackBuffer = null;
                Guid tex2DRiid = ID3D11Texture2D.Guid;
                vd->SwapChain->GetBuffer(0, &tex2DRiid, (void**)&pBackBuffer);
                _device->CreateRenderTargetView((ID3D11Resource*)pBackBuffer, null, &vd->RTView);
                pBackBuffer->Release();
            }
        }

        private unsafe void DestroyWindow(ImGuiViewportPtr viewport)
        {
            // The main viewport (owned by the application) will always have RendererUserData == NULL since we didn't create the data for it.
            ViewportData* viewportData = (ViewportData*)viewport.RendererUserData.ToPointer();
            if (viewportData == null)
                return;

            if (viewportData->SwapChain != null)
            {
                viewportData->SwapChain->Release();
                viewportData->SwapChain = null;
            }

            if (viewportData->RTView != null)
            {
                viewportData->RTView->Release();
                viewportData->RTView = null;
            }

            viewport.RendererUserData = IntPtr.Zero;
        }

        public unsafe void SetWindowSize(ImGuiViewportPtr viewport, Vector2 size)
        {
            ViewportData* viewportData = (ViewportData*)viewport.RendererUserData.ToPointer();
            if (viewportData == null)
                return;

            if (viewportData->RTView != null)
            {
                viewportData->RTView->Release();
                viewportData->RTView = null;
            }

            if (viewportData->SwapChain != null)
            {
                ID3D11Texture2D* pBackBuffer = null;
                viewportData->SwapChain->ResizeBuffers(0, (uint)size.X, (uint)size.Y, Format.FormatUnknown, 0);
                Guid tex2DRiid = ID3D11Texture2D.Guid;
                viewportData->SwapChain->GetBuffer(0, &tex2DRiid, (void**)pBackBuffer);
                if (pBackBuffer == null)
                {
                    CheckDxError(-1, "DX11 SetWindowSize() failed creating buffers.");
                    return;
                }
                _device->CreateRenderTargetView((ID3D11Resource*)pBackBuffer, null, &viewportData->RTView);
                pBackBuffer->Release();
            }
        }

        public unsafe void RenderWindow(ImGuiViewportPtr viewport, IntPtr v)
        {
            ViewportData* viewportData = (ViewportData*)viewport.RendererUserData.ToPointer();
            if (viewportData == null)
                return;
            Vector4 clearColor = new(0, 0, 0, 1);

            _deviceContext->OMSetRenderTargets(1, &viewportData->RTView, null);
            if (!(viewport.Flags.HasFlag(ImGuiViewportFlags.NoRendererClear)))
                _deviceContext->ClearRenderTargetView(viewportData->RTView, (float*)&clearColor);
            this.RenderDrawData(viewport.DrawData);
        }

        public unsafe void SwapBuffers(ImGuiViewportPtr viewport, IntPtr v)
        {
            ViewportData* viewportData = (ViewportData*)viewport.RendererUserData.ToPointer();
            if (viewportData == null)
                return;
            viewportData->SwapChain->Present(0, 0); // Present without vsync
        }

        private void ShutdownPlatformInterface()
        {
            ImGui.DestroyPlatformWindows();
        }
    }
}
