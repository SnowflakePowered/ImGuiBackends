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
            public ID3D11Device* Device;
            public ID3D11DeviceContext* DeviceContext;
            public delegate* unmanaged<ImDrawDataPtr, void> RenderDelegate;
        }

        private delegate void CreateWindowDelegate(ImGuiViewportPtr ptr);
        // Pin delegates to GC
        private CreateWindowDelegate? _createWindow;
        private void InitPlatformInterface()
        {
            ImGuiPlatformIOPtr io = ImGui.GetPlatformIO();

            _createWindow = CreateWindow;

            unsafe
            {
                // what if we make a vtable so we can use function pointers instead..?
                io.Renderer_CreateWindow = Marshal.GetFunctionPointerForDelegate(_createWindow);
                io.Renderer_DestroyWindow = (nint)(delegate* unmanaged<ImGuiViewportPtr, void>)&ImGuiBackendDirect3D11.DestroyWindow;
                io.Renderer_SetWindowSize = (nint)(delegate* unmanaged<ImGuiViewportPtr, Vector2, void>)&ImGuiBackendDirect3D11.SetWindowSize;
                io.Renderer_RenderWindow = (nint)(delegate* unmanaged<ImGuiViewportPtr, IntPtr, void>)&ImGuiBackendDirect3D11.RenderWindow;
                io.Renderer_SwapBuffers = (nint)(delegate* unmanaged<ImGuiViewportPtr, IntPtr, void>)&ImGuiBackendDirect3D11.SwapBuffers;
            }
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

            if (vd->Device == null)
            {
                vd->Device = _device;
                vd->Device->AddRef();
            }

            if (vd->DeviceContext == null)
            {
                vd->DeviceContext = _deviceContext;
                vd->DeviceContext->AddRef();
            }

            _factory->CreateSwapChain((IUnknown*)_device, &desc, &vd->SwapChain);

            if (vd->SwapChain != null)
            {
                ID3D11Texture2D* pBackBuffer = null;
                Guid tex2DRiid = ID3D11Texture2D.Guid;
                vd->SwapChain->GetBuffer(0, &tex2DRiid, (void**)&pBackBuffer);
                vd->Device->CreateRenderTargetView((ID3D11Resource*)pBackBuffer, null, &vd->RTView);
                pBackBuffer->Release();
            }

            if (vd->RenderDelegate == null)
            {
                vd->RenderDelegate = (delegate* unmanaged<ImDrawDataPtr, void>)Marshal.GetFunctionPointerForDelegate(this.RenderDrawData);
            }
        }

        [UnmanagedCallersOnly]
        private static unsafe void DestroyWindow(ImGuiViewportPtr viewport)
        {
            // The main viewport (owned by the application) will always have RendererUserData == NULL since we didn't create the data for it.
            ViewportData* vd = (ViewportData*)viewport.RendererUserData.ToPointer();
            if (vd == null)
                return;

            if (vd->SwapChain != null)
            {
                vd->SwapChain->Release();
                vd->SwapChain = null;
            }

            if (vd->RTView != null)
            {
                vd->RTView->Release();
                vd->RTView = null;
            }

            if (vd->Device != null)
            {
                vd->Device->Release();
                vd->Device = null;
            }

            if (vd->DeviceContext != null)
            {
                vd->DeviceContext->Release();
                vd->DeviceContext = null;
            }

            if (vd->RenderDelegate != null)
            {
                vd->RenderDelegate = null;
            }

            viewport.RendererUserData = IntPtr.Zero;
        }

        [UnmanagedCallersOnly]
        private static unsafe void SetWindowSize(ImGuiViewportPtr viewport, Vector2 size)
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
                viewportData->Device->CreateRenderTargetView((ID3D11Resource*)pBackBuffer, null, &viewportData->RTView);
                pBackBuffer->Release();
            }
        }

        [UnmanagedCallersOnly]
        private static unsafe void RenderWindow(ImGuiViewportPtr viewport, IntPtr v)
        {
            ViewportData* viewportData = (ViewportData*)viewport.RendererUserData.ToPointer();
            if (viewportData == null)
                return;
            Vector4 clearColor = new(0, 0, 0, 1);

            viewportData->DeviceContext->OMSetRenderTargets(1, &viewportData->RTView, null);
            if (!(viewport.Flags.HasFlag(ImGuiViewportFlags.NoRendererClear)))
                viewportData->DeviceContext->ClearRenderTargetView(viewportData->RTView, (float*)&clearColor);

            if (viewportData->RenderDelegate != null)
                viewportData->RenderDelegate(viewport.DrawData);
        }

        [UnmanagedCallersOnly]
        private static unsafe void SwapBuffers(ImGuiViewportPtr viewport, IntPtr v)
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
