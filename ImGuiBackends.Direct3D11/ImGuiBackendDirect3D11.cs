using ImGuiNET;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Maths;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ImGuiBackends.Direct3D11
{
    // todo: use nativearray
    public class ImGuiBackendDirect3D11
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckDxError(int error, string title)
        {
#if DEBUG
            if (error < 0)
            {
                Debug.WriteLine($"{title}: {error:x}");
            }
#endif
            return error < 0;
        }

        private unsafe ID3D11Device* _pd3dDevice;
        private unsafe ID3D11DeviceContext* _deviceContext;
        private unsafe IDXGIFactory* _pFactory;
        private unsafe ID3D11Buffer* _pVB;
        private unsafe ID3D11Buffer* _pIB;
        private unsafe ID3D11VertexShader* _pVertexShader;
        private unsafe ID3D11InputLayout* _pInputLayout;
        private unsafe ID3D11Buffer* _pVertexConstantBuffer;
        private unsafe ID3D11PixelShader* _pPixelShader;
        private unsafe ID3D11SamplerState* _pFontSampler;
        private unsafe ID3D11ShaderResourceView* _pFontTextureView;
        private unsafe ID3D11RasterizerState* _pRasterizerState;
        private unsafe ID3D11BlendState* _pBlendState;
        private unsafe ID3D11DepthStencilState* _pDepthStencilState;
        private int _vertexBufferSize;
        private int _indexBufferSize;

        private static unsafe readonly byte* CSTR_POSITION = (byte*)SilkMarshal.StringToPtr("POSITION");
        private static unsafe readonly byte* CSTR_TEXCOORD = (byte*)SilkMarshal.StringToPtr("TEXCOORD");
        private static unsafe readonly byte* CSTR_COLOR = (byte*)SilkMarshal.StringToPtr("COLOR");

        private unsafe void SetupRenderState(in ImDrawDataPtr drawData)
        {
            // Setup viewport
            Viewport vp = new()
            {
                Width = drawData.DisplaySize.X,
                Height = drawData.DisplaySize.Y,
                MinDepth = 0f,
                MaxDepth = 1f,
                TopLeftX = 0,
                TopLeftY = 0,
            };

            _deviceContext->RSSetViewports(1, &vp);

            // Setup shader and vertex buffers
            uint stride = (uint)sizeof(ImDrawVert);
            uint offset = 0;

            _deviceContext->IASetInputLayout(_pInputLayout);
            _deviceContext->IASetVertexBuffers(0, 1, ref _pVB, &stride, &offset);
            _deviceContext->IASetIndexBuffer(_pIB, sizeof(ushort) == 2 ? Format.FormatR16Uint : Format.FormatR32Uint, 0);
            _deviceContext->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);
            _deviceContext->VSSetShader(_pVertexShader, null, 0);
            _deviceContext->VSSetConstantBuffers(0, 1, ref _pVertexConstantBuffer);
            _deviceContext->PSSetShader(_pPixelShader, null, 0);
            _deviceContext->PSSetSamplers(0, 1, ref _pFontSampler);
            _deviceContext->GSSetShader(null, null, 0);
            _deviceContext->HSSetShader(null, null, 0); // In theory we should backup and restore this as well.. very infrequently used..
            _deviceContext->DSSetShader(null, null, 0); // In theory we should backup and restore this as well.. very infrequently used..
            _deviceContext->CSSetShader(null, null, 0); // In theory we should backup and restore this as well.. very infrequently used..


            // Setup blend state
            Vector4 blendFactor = new(0, 0, 0, 0);
            _deviceContext->OMSetBlendState(_pBlendState, (float*)&blendFactor, 0xffffffff);

            _deviceContext->OMSetDepthStencilState(_pDepthStencilState, 0);
            _deviceContext->RSSetState(_pRasterizerState);
        }

        public unsafe void RenderDrawData(ImDrawDataPtr drawData)
        {
            // Avoid rendering when minimized
            if (drawData.DisplaySize.X <= 0 || drawData.DisplaySize.Y <= 0)
                return;

            if (!drawData.Valid || drawData.CmdListsCount == 0)
                return;

            ID3D11DeviceContext* deviceContext = _deviceContext;

            //  Create and grow vertex/index buffers if needed
            // vertex buffers
            if (_pVB == null || _vertexBufferSize < drawData.TotalVtxCount)
            {
                if (_pVB != null)
                {
                    _pVB->Release();
                    _pVB = null;
                }

                _vertexBufferSize = drawData.TotalVtxCount + 5000;

                BufferDesc desc = new()
                {
                    Usage = Usage.UsageDynamic,
                    ByteWidth = (uint)(_vertexBufferSize * sizeof(ImDrawVert)),
                    BindFlags = (uint)BindFlag.BindVertexBuffer,
                    CPUAccessFlags = (uint)CpuAccessFlag.CpuAccessWrite,
                    MiscFlags = 0,
                };

                if (CheckDxError(_pd3dDevice->CreateBuffer(&desc, null, ref _pVB), "Grow VB"))
                    return;
            }

            if (_pIB == null || _indexBufferSize < drawData.TotalIdxCount)
            {
                if (_pIB != null)
                {
                    _pIB->Release();
                    _pIB = null;
                }
                _indexBufferSize = drawData.TotalIdxCount + 10000;

                BufferDesc desc = new()
                {
                    Usage = Usage.UsageDynamic,
                    ByteWidth = (uint)(_indexBufferSize * sizeof(ushort)),
                    BindFlags = (uint)BindFlag.BindIndexBuffer,
                    CPUAccessFlags = (uint)CpuAccessFlag.CpuAccessWrite,
                    MiscFlags = 0,
                };

                if (CheckDxError(_pd3dDevice->CreateBuffer(&desc, null, ref _pIB), "Grow IB"))
                    return;
            }

            // upload vertex/index data into a single contiguous GPU buffer
            {
                MappedSubresource vtxResource = new(), idxResource = new();
                if (_pVB == null || _pIB == null)
                    return;
                // if this doesn't work then we need to queryinterface :(
                if (CheckDxError(deviceContext->Map((ID3D11Resource*)_pVB, 0, Map.MapWriteDiscard, 0, &vtxResource), "Map VTX"))
                    return;
                if (CheckDxError(deviceContext->Map((ID3D11Resource*)_pIB, 0, Map.MapWriteDiscard, 0, &idxResource), "Map IDX"))
                    return;

                ImDrawVert* vtxDst = (ImDrawVert*)vtxResource.PData;
                ushort* idxDst = (ushort*)idxResource.PData;

                for (int n = 0; n < drawData.CmdListsCount; n++)
                {
                    var cmdList = drawData.CmdListsRange[n];
                    Buffer.MemoryCopy(cmdList.VtxBuffer.Data.ToPointer(), vtxDst,
                                         sizeof(ImDrawVert) * cmdList.VtxBuffer.Size,
                                         sizeof(ImDrawVert) * cmdList.VtxBuffer.Size);

                    Buffer.MemoryCopy(cmdList.IdxBuffer.Data.ToPointer(), idxDst,
                                      sizeof(ushort) * _indexBufferSize,
                                      sizeof(ushort) * cmdList.IdxBuffer.Size);

                    vtxDst += cmdList.VtxBuffer.Size;
                    idxDst += cmdList.IdxBuffer.Size;
                }

                deviceContext->Unmap((ID3D11Resource*)_pVB, 0);
                deviceContext->Unmap((ID3D11Resource*)_pIB, 0);
            }

            // Setup orthographic projection matrix into our constant buffer
            // Our visible imgui space lies from drawData->DisplayPos (top left) to drawData->DisplayPos+dataData->DisplaySize (bottom right).
            // DisplayPos is (0,0) for single viewport apps.
            {
                MappedSubresource projectionMatrix = new();
                if (CheckDxError(deviceContext->Map((ID3D11Resource*)_pVertexConstantBuffer, 
                    0, Map.MapWriteDiscard, 0, &projectionMatrix), "Map Projection Matrix"))
                    return;

                float L = drawData.DisplayPos.X;
                float R = drawData.DisplayPos.X + drawData.DisplaySize.X;
                float T = drawData.DisplayPos.Y;
                float B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;
                float* mvp = stackalloc float[]
                {
                    2f / (R - L), 0, 0, 0,
                    0, 2f / (T - B), 0, 0,
                    0, 0, 0.5f, 0,
                    (R + L) / (L - R), (T + B) / (B - T), 0.5f, 1f
                };

                Buffer.MemoryCopy(mvp, projectionMatrix.PData, 16 * sizeof(float), 16 * sizeof(float));
                deviceContext->Unmap((ID3D11Resource*)_pVertexConstantBuffer, 0);
            }

            using D3D11StateBackup backup = this.StateBackup();
            this.SetupRenderState(drawData);

            // Render command lists
            // (Because we merged all buffers into a single one, we maintain our own offset into them)
            int globalIdxOffset = 0;
            int globalVtxOffset = 0;

            Vector2 clipOff = drawData.DisplayPos;
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                ImDrawListPtr cmdList = drawData.CmdListsRange[n];
                for (int cmd = 0; cmd < cmdList.CmdBuffer.Size; cmd++)
                {
                    var cmdPtr = cmdList.CmdBuffer[cmd];
                    if (cmdPtr.UserCallback != IntPtr.Zero)
                    {
                        if (cmdPtr.UserCallback == (nint)(-1))
                        {
                            this.SetupRenderState(drawData);
                        }
                        else
                        {
                            // todo, might be able to cast this...? quite dangerous though.
                            throw new NotImplementedException();
                        }
                    }
                    else
                    {
                        Vector2 clipMin = new(cmdPtr.ClipRect.X - clipOff.X, cmdPtr.ClipRect.Y - clipOff.Y);
                        Vector2 clipMax = new(cmdPtr.ClipRect.Z - clipOff.X, cmdPtr.ClipRect.W - clipOff.Y);
                        if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y)
                            continue;

                        // Apply scissor/clipping rectangle
                        Rectangle<int> r = new((int)clipMin.X, (int)clipMin.Y, (int)clipMax.X, (int)clipMax.Y);
                        deviceContext->RSSetScissorRects(1, &r);

                        // Bind texture, Draw
                        ID3D11ShaderResourceView* textureSrv = (ID3D11ShaderResourceView*)cmdPtr.GetTexID();
                        deviceContext->PSSetShaderResources(0, 1, &textureSrv);
                        deviceContext->DrawIndexed(cmdPtr.ElemCount, cmdPtr.IdxOffset + (uint)globalIdxOffset,
                                                                     (int)(cmdPtr.VtxOffset + (uint)globalVtxOffset));
                    }
                }

                globalIdxOffset += cmdList.IdxBuffer.Size;
                globalVtxOffset += cmdList.VtxBuffer.Size;
            }
            this.RestoreState(backup);

            // backup disposed here.
        }

        private unsafe void RestoreState(in D3D11StateBackup backup)
        {
            ID3D11DeviceContext* deviceContext = _deviceContext;
            deviceContext->IASetInputLayout(backup.InputLayout);
            deviceContext->IASetIndexBuffer(backup.IndexBuffer, backup.IndexBufferFormat, backup.IndexBufferOffset);
            deviceContext->IASetPrimitiveTopology(backup.PrimitiveTopology);
            deviceContext->IASetVertexBuffers(0, (uint)backup.VertexBuffers.Length, backup.VertexBuffers,
                   backup.VertexBufferStrides, backup.VertexBufferOffsets);

            // -- RS
            deviceContext->RSSetState(backup.RS);
            deviceContext->RSSetScissorRects(backup.ScissorRectsCount, backup.ScissorRects);
            deviceContext->RSSetViewports(backup.ViewportsCount, backup.Viewports);
            
            // -- OM
            fixed (float* blendFlactor = backup.BlendFactor)
            {
                deviceContext->OMSetBlendState(backup.BlendState, blendFlactor, backup.SampleMask);
                deviceContext->OMSetDepthStencilState(backup.DepthStencilState, backup.DepthStencilRef);
                deviceContext->OMSetRenderTargets((uint)backup.RenderTargetViews.Length, backup.RenderTargetViews, backup.DepthStencilView);
            }

            // -- VS
            fixed (ID3D11ClassInstance** classInstances = backup.VSBackup.Instances)
            fixed (ID3D11SamplerState** samplers = backup.VSBackup.Samplers)
            fixed (ID3D11Buffer** constantBuffers = backup.VSBackup.ConstantBuffers)
            fixed (ID3D11ShaderResourceView** resourceViews = backup.VSBackup.ResourceViews)
            {
                deviceContext->VSSetShader(backup.VS, classInstances, backup.VSBackup.InstancesCount);
                deviceContext->VSSetSamplers(0, (uint)backup.VSBackup.Samplers.Length, samplers);
                deviceContext->VSSetConstantBuffers(0, (uint)backup.VSBackup.ConstantBuffers.Length, constantBuffers);
                deviceContext->VSSetShaderResources(0, (uint)backup.VSBackup.ResourceViews.Length, resourceViews);
            }

            // -- HS
            fixed (ID3D11ClassInstance** classInstances = backup.HSBackup.Instances)
            fixed (ID3D11SamplerState** samplers = backup.HSBackup.Samplers)
            fixed (ID3D11Buffer** constantBuffers = backup.HSBackup.ConstantBuffers)
            fixed (ID3D11ShaderResourceView** resourceViews = backup.HSBackup.ResourceViews)
            {
                deviceContext->HSSetShader(backup.HS, classInstances, backup.HSBackup.InstancesCount);
                deviceContext->HSSetSamplers(0, (uint)backup.HSBackup.Samplers.Length, samplers);
                deviceContext->HSSetConstantBuffers(0, (uint)backup.HSBackup.ConstantBuffers.Length, constantBuffers);
                deviceContext->HSSetShaderResources(0, (uint)backup.HSBackup.ResourceViews.Length, resourceViews);
            }

            // -- DS
            fixed (ID3D11ClassInstance** classInstances = backup.DSBackup.Instances)
            fixed (ID3D11SamplerState** samplers = backup.DSBackup.Samplers)
            fixed (ID3D11Buffer** constantBuffers = backup.DSBackup.ConstantBuffers)
            fixed (ID3D11ShaderResourceView** resourceViews = backup.DSBackup.ResourceViews)
            {
                deviceContext->DSSetShader(backup.DS, classInstances, backup.DSBackup.InstancesCount);
                deviceContext->DSSetSamplers(0, (uint)backup.DSBackup.Samplers.Length, samplers);
                deviceContext->DSSetConstantBuffers(0, (uint)backup.DSBackup.ConstantBuffers.Length, constantBuffers);
                deviceContext->DSSetShaderResources(0, (uint)backup.DSBackup.ResourceViews.Length, resourceViews);
            }

            // -- GS
            fixed (ID3D11ClassInstance** classInstances = backup.GSBackup.Instances)
            fixed (ID3D11SamplerState** samplers = backup.GSBackup.Samplers)
            fixed (ID3D11Buffer** constantBuffers = backup.GSBackup.ConstantBuffers)
            fixed (ID3D11ShaderResourceView** resourceViews = backup.GSBackup.ResourceViews)
            {
                deviceContext->GSSetShader(backup.GS, classInstances, backup.GSBackup.InstancesCount);
                deviceContext->GSSetSamplers(0, (uint)backup.GSBackup.Samplers.Length, samplers);
                deviceContext->GSSetConstantBuffers(0, (uint)backup.GSBackup.ConstantBuffers.Length, constantBuffers);
                deviceContext->GSSetShaderResources(0, (uint)backup.GSBackup.ResourceViews.Length, resourceViews);
            }

            // -- PS
            fixed (ID3D11ClassInstance** classInstances = backup.PSBackup.Instances)
            fixed (ID3D11SamplerState** samplers = backup.PSBackup.Samplers)
            fixed (ID3D11Buffer** constantBuffers = backup.PSBackup.ConstantBuffers)
            fixed (ID3D11ShaderResourceView** resourceViews = backup.PSBackup.ResourceViews)
            {
                deviceContext->PSSetShader(backup.PS, classInstances, backup.PSBackup.InstancesCount);
                deviceContext->PSSetSamplers(0, (uint)backup.PSBackup.Samplers.Length, samplers);
                deviceContext->PSSetConstantBuffers(0, (uint)backup.PSBackup.ConstantBuffers.Length, constantBuffers);
                deviceContext->PSSetShaderResources(0, (uint)backup.PSBackup.ResourceViews.Length, resourceViews);
            }

            // -- CS
            fixed (ID3D11ClassInstance** classInstances = backup.CSBackup.Instances)
            fixed (ID3D11SamplerState** samplers = backup.CSBackup.Samplers)
            fixed (ID3D11Buffer** constantBuffers = backup.CSBackup.ConstantBuffers)
            fixed (ID3D11ShaderResourceView** resourceViews = backup.CSBackup.ResourceViews)
            {
                deviceContext->CSSetShader(backup.CS, classInstances, backup.CSBackup.InstancesCount);
                deviceContext->CSSetSamplers(0, (uint)backup.CSBackup.Samplers.Length, samplers);
                deviceContext->CSSetConstantBuffers(0, (uint)backup.CSBackup.ConstantBuffers.Length, constantBuffers);
                deviceContext->CSSetShaderResources(0, (uint)backup.CSBackup.ResourceViews.Length, resourceViews);

                // CSUAVs are never very big, shouldn't worry about overflow here.
                uint* uavInitialCounts = stackalloc uint[backup.CSUAVs.Length];
                for (int i = 0; i < backup.CSUAVs.Length; i++)
                    uavInitialCounts[i] = unchecked((uint)-1);

                deviceContext->CSSetUnorderedAccessViews(0, (uint)backup.CSUAVs.Length, backup.CSUAVs, uavInitialCounts);
            }
        }

        public unsafe void InvalidateDeviceObjects()
        {
            if (_pd3dDevice == null)
                return;

            if (_pFontSampler != null) { _pFontSampler->Release(); _pFontSampler = null; }
            if (_pFontTextureView != null) 
            {
                _pFontTextureView->Release(); 
                _pFontTextureView = null;
                // We copied data->pFontTextureView to io.Fonts->TexID so let's clear that as well.
                ImGui.GetIO().Fonts.SetTexID((nint)0);  
            } 
            if (_pIB != null) { _pIB->Release(); _pIB = null; }
            if (_pVB != null) { _pVB->Release(); _pVB = null; }
            if (_pBlendState != null) { _pBlendState->Release(); _pBlendState = null; }
            if (_pDepthStencilState != null) { _pDepthStencilState->Release(); _pDepthStencilState = null; }
            if (_pRasterizerState != null) { _pRasterizerState->Release(); _pRasterizerState = null; }
            if (_pPixelShader != null) { _pPixelShader->Release(); _pPixelShader = null; }
            if (_pVertexConstantBuffer != null) { _pVertexConstantBuffer->Release(); _pVertexConstantBuffer = null; }
            if (_pInputLayout != null) { _pInputLayout->Release(); _pInputLayout = null; }
            if (_pVertexShader != null) { _pVertexShader->Release(); _pVertexShader = null; }
        }

        public unsafe bool CreateDeviceObjects()
        {
            if (_pd3dDevice == null)
                return false;
            if (_pFontSampler != null)
                this.InvalidateDeviceObjects();

            // Create the vertex buffer
            fixed (byte* vertexBuffer = ShaderBlobs.VertexBuffer)
            {
                nuint vertexBufferSize = (nuint)(sizeof(byte) * ShaderBlobs.VertexBuffer.Length);
                // Shader blobs are precompiled so no need to release the compiled shader.
                if (CheckDxError(_pd3dDevice->CreateVertexShader(vertexBuffer,
                       vertexBufferSize, null, ref _pVertexShader), "Create Vertex Shader"))
                    return false;
  
                InputElementDesc* localLayout = stackalloc InputElementDesc[]
                {
                    new(CSTR_POSITION, 0, Format.FormatR32G32Float, 0, (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.pos)), InputClassification.InputPerVertexData, 0),
                    new(CSTR_TEXCOORD, 0, Format.FormatR32G32Float, 0, (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.uv)), InputClassification.InputPerVertexData, 0),
                    new(CSTR_COLOR, 0, Format.FormatR8G8B8A8Unorm, 0, (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.col)), InputClassification.InputPerVertexData, 0)
                };
                if (CheckDxError(_pd3dDevice->CreateInputLayout(localLayout, 3, vertexBuffer, vertexBufferSize, ref _pInputLayout), 
                        "Create Input Layout"))
                    return false;
            }

            // Create the VertexConstantBuffer
            {
                BufferDesc desc = new()
                {
                    Usage = Usage.UsageDynamic,
                    BindFlags = (uint)BindFlag.BindConstantBuffer,
                    CPUAccessFlags = (uint)CpuAccessFlag.CpuAccessWrite,
                    MiscFlags = 0,
                    ByteWidth = 16 * sizeof(float)
                };
            
                CheckDxError(_pd3dDevice->CreateBuffer(&desc, null, ref _pVertexConstantBuffer), "Create VertexConstantBuffer");
            }

            // Create the pixel shader
            fixed (byte* pixelShader = ShaderBlobs.PixelShader)
            {
                nuint pixelShaderSize = (nuint)(sizeof(byte) * ShaderBlobs.PixelShader.Length);

                if (CheckDxError(_pd3dDevice->CreatePixelShader(pixelShader, pixelShaderSize, null, ref _pPixelShader), "Create Pixel Shader"))
                    return false;
            }

            // Create the blending setup
            {
                BlendDesc desc = new()
                {
                    AlphaToCoverageEnable = 0,
                    RenderTarget = new()
                    {
                        Element0 = new()
                        {
                            BlendEnable = 1,
                            SrcBlend = Blend.BlendSrcAlpha,
                            DestBlend = Blend.BlendInvSrcAlpha,
                            BlendOp = BlendOp.BlendOpAdd,
                            SrcBlendAlpha = Blend.BlendOne,
                            DestBlendAlpha = Blend.BlendInvSrcAlpha,
                            BlendOpAlpha = BlendOp.BlendOpAdd,
                            RenderTargetWriteMask = (byte)ColorWriteEnable.ColorWriteEnableAll,
                        }
                    }
                };

                CheckDxError(_pd3dDevice->CreateBlendState(&desc, ref _pBlendState), "Create Blend State");
            }

            // Create the rasterizer state
            {
                RasterizerDesc desc = new()
                {
                    FillMode = FillMode.FillSolid,
                    CullMode = CullMode.CullNone,
                    ScissorEnable = 1,
                    DepthClipEnable = 1,
                };

                CheckDxError(_pd3dDevice->CreateRasterizerState(&desc, ref _pRasterizerState), "Create Rasterizer State");
            }

            // Create depth-stencil State
            {
                DepthStencilDesc desc = new()
                {
                    DepthEnable = 0,
                    DepthWriteMask = DepthWriteMask.DepthWriteMaskAll,
                    DepthFunc = ComparisonFunc.ComparisonAlways,
                    StencilEnable = 0,
                    FrontFace = new()
                    {
                        StencilFailOp = StencilOp.StencilOpKeep,
                        StencilDepthFailOp = StencilOp.StencilOpKeep,
                        StencilPassOp = StencilOp.StencilOpKeep,
                        StencilFunc = ComparisonFunc.ComparisonAlways
                    },
                    BackFace = new()
                    {
                        StencilFailOp = StencilOp.StencilOpKeep,
                        StencilDepthFailOp = StencilOp.StencilOpKeep,
                        StencilPassOp = StencilOp.StencilOpKeep,
                        StencilFunc = ComparisonFunc.ComparisonAlways
                    }
                };

                CheckDxError(_pd3dDevice->CreateDepthStencilState(&desc, ref _pDepthStencilState), "Create DS State");
            }

            this.CreateFontsTexture();
            return true;
        }

        public unsafe void Init(IntPtr device, IntPtr deviceContext)
        {
            this.Init((ID3D11Device*)device.ToPointer(), (ID3D11DeviceContext*)deviceContext.ToPointer());
        }

        // maybe pointers? idk
        public unsafe void Init(ID3D11Device* device, ID3D11DeviceContext* deviceContext)
        {
            // uh... not sure if this will work but maybe.
            var io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;  // We can honor the ImDrawCmd::VtxOffset field, allowing for large meshes.

            IDXGIDevice* pDXGIDevice = null;
            IDXGIAdapter* pDXGIAdapter = null;
            IDXGIFactory* pFactory = null;

            Guid guidIDXGIDevice = IDXGIDevice.Guid;
            Guid guidIDXGIAdapter = IDXGIAdapter.Guid;
            Guid guidIDXGIFactory = IDXGIFactory.Guid;

            if (device->QueryInterface(&guidIDXGIDevice, (void**)&pDXGIDevice) == 0)
            {
                if (pDXGIDevice->GetParent(&guidIDXGIAdapter, (void**)&pDXGIAdapter) == 0)
                {
                    if (pDXGIAdapter->GetParent(&guidIDXGIFactory, (void**)&pFactory) == 0)
                    {
                        _pd3dDevice = device;
                        _deviceContext = deviceContext;
                        _pFactory = pFactory;
                    }
                }
            }

            if (pDXGIDevice != null) pDXGIDevice->Release();
            if (pDXGIAdapter != null) pDXGIAdapter->Release();
            _pd3dDevice->AddRef();
            _deviceContext->AddRef();
        }

        public unsafe void Shutdown()
        {
            this.InvalidateDeviceObjects();
            if (_pFactory != null) _pFactory->Release();
            if (_pd3dDevice != null) _pd3dDevice->Release();
            if (_deviceContext != null) _deviceContext->Release();
        }

        public unsafe void NewFrame()
        {
            if (_pFontSampler == null)
                this.CreateDeviceObjects();
        }

        #region Internal
        private unsafe void CreateFontsTexture()
        {
            var io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr 
                pixels, out int fontWidth, out int fontHeight, out int bytesPerPixel);

            // Upload texture to graphics system
            {
                Texture2DDesc desc = new()
                {
                    Width = (uint)fontWidth,
                    Height = (uint)fontHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.FormatR8G8B8A8Unorm,
                    SampleDesc = new(1, 0),
                    Usage = Usage.UsageImmutable,
                    BindFlags = (uint)BindFlag.BindShaderResource,
                    CPUAccessFlags = 0,
                    MiscFlags = 0,
                };

                ID3D11Texture2D* pTexture = null;
                SubresourceData subResource = new()
                {
                    PSysMem = pixels.ToPointer(),
                    SysMemPitch = desc.Width * (uint)bytesPerPixel,
                    SysMemSlicePitch = 0,
                };

                CheckDxError(_pd3dDevice->CreateTexture2D(&desc, &subResource, &pTexture), "Create Font Tex2D");

                // todo: check error
                if (pTexture == null)
                    return;

                ShaderResourceViewDesc srvDesc = new()
                {
                    Format = Format.FormatR8G8B8A8Unorm,
                    ViewDimension = D3DSrvDimension.D3D101SrvDimensionTexture2D,
                    Anonymous = new(texture2D: new()
                    {
                        MipLevels = desc.MipLevels,
                        MostDetailedMip = 0
                    })
                };

                // todo: check if cast is proper
                CheckDxError(_pd3dDevice->CreateShaderResourceView((ID3D11Resource*)pTexture, &srvDesc, ref _pFontTextureView), "Create Font SRV Tex2D");
                pTexture->Release();

                io.Fonts.SetTexID((nint)_pFontTextureView);
                io.Fonts.ClearTexData();
            }


            // Create texture sampler
            {
                SamplerDesc desc = new()
                {
                    Filter = Filter.FilterMinMagMipLinear,
                    AddressU = TextureAddressMode.TextureAddressWrap,
                    AddressV = TextureAddressMode.TextureAddressWrap,
                    AddressW = TextureAddressMode.TextureAddressWrap,
                    MipLODBias = 0f,
                    ComparisonFunc = ComparisonFunc.ComparisonAlways,
                    MinLOD = 0,
                    MaxLOD = 0,
                };

                CheckDxError(_pd3dDevice->CreateSamplerState(&desc, ref _pFontSampler), "Create Text Sampler");
            }
        }

        // Full state save ported from 
        // https://github.com/ff-meli/ImGuiScene/blob/master/ImGuiScene/ImGui_Impl/Renderers/ImGui_Impl_DX11.cs
        private unsafe D3D11StateBackup StateBackup()
        {
            D3D11StateBackup backup = new();
            ID3D11DeviceContext* deviceContext = _deviceContext;

            // Could clean up but we need this in order to pass ref struct
            // https://github.com/dotnet/csharplang/issues/1792

            // -- IA
            // Allocate
            //backup.VertexBuffers = new ID3D11Buffer*[D3D11.IAVertexInputResourceSlotCount];
            backup.VertexBuffers = new(D3D11.IAVertexInputResourceSlotCount);
            backup.VertexBufferStrides = new(D3D11.IAVertexInputResourceSlotCount);
            backup.VertexBufferOffsets = new(D3D11.IAVertexInputResourceSlotCount);

            deviceContext->IAGetInputLayout(&backup.InputLayout);
            deviceContext->IAGetIndexBuffer(&backup.IndexBuffer, &backup.IndexBufferFormat, &backup.IndexBufferOffset);
            deviceContext->IAGetPrimitiveTopology(&backup.PrimitiveTopology);
            deviceContext->IAGetVertexBuffers(0, D3D11.IAVertexInputResourceSlotCount, backup.VertexBuffers,
                  backup.VertexBufferStrides, backup.VertexBufferOffsets);

            // -- RS
            // Allocate
            backup.ScissorRectsCount = D3D11.ViewportAndScissorrectObjectCountPerPipeline;
            backup.ViewportsCount = D3D11.ViewportAndScissorrectObjectCountPerPipeline;
            backup.ScissorRects = new((int)backup.ScissorRectsCount);
            backup.Viewports = new((int)backup.ViewportsCount);

            deviceContext->RSGetState(&backup.RS);
            deviceContext->RSGetScissorRects(&backup.ScissorRectsCount, backup.ScissorRects);
            deviceContext->RSGetViewports(&backup.ViewportsCount, backup.Viewports);

            //fixed (Rectangle<int>* scissorRects = backup.ScissorRects)
            //fixed (Viewport* viewports = backup.Viewports)
            //{
            //    deviceContext->RSGetScissorRects(&backup.ScissorRectsCount, scissorRects);
            //    deviceContext->RSGetViewports(&backup.ViewportsCount, viewports);
            //}

            // -- OM
            // Allocate
            backup.RenderTargetViews = new(D3D11.SimultaneousRenderTargetCount);

            deviceContext->OMGetBlendState(&backup.BlendState, backup.BlendFactor, &backup.SampleMask);
            deviceContext->OMGetDepthStencilState(&backup.DepthStencilState, &backup.DepthStencilRef);
            deviceContext->OMGetRenderTargets(D3D11.SimultaneousRenderTargetCount, backup.RenderTargetViews, &backup.DepthStencilView);

            //fixed (ID3D11RenderTargetView** renderTargetViews = backup.RenderTargetViews) 
            //{
            //    deviceContext->OMGetRenderTargets(D3D11.SimultaneousRenderTargetCount, renderTargetViews, &backup.DepthStencilView);
            //}

            // -- VS
            // Allocate
            backup.VSBackup = new();

            fixed (ID3D11ClassInstance** classInstances = backup.VSBackup.Instances)
            fixed (ID3D11SamplerState** samplers = backup.VSBackup.Samplers)
            fixed (ID3D11Buffer** constantBuffers = backup.VSBackup.ConstantBuffers)
            fixed (ID3D11ShaderResourceView** resourceViews = backup.VSBackup.ResourceViews)
            {
                deviceContext->VSGetShader(&backup.VS, classInstances, &backup.VSBackup.InstancesCount);
                deviceContext->VSGetSamplers(0, D3D11.CommonshaderSamplerSlotCount, samplers);
                deviceContext->VSGetConstantBuffers(0, D3D11.CommonshaderConstantBufferApiSlotCount, constantBuffers);
                deviceContext->VSGetShaderResources(0, D3D11.CommonshaderInputResourceSlotCount, resourceViews);
            }


            // -- HS
            // Allocate
            backup.HSBackup = new();

            fixed (ID3D11ClassInstance** classInstances = backup.HSBackup.Instances)
            fixed (ID3D11SamplerState** samplers = backup.HSBackup.Samplers)
            fixed (ID3D11Buffer** constantBuffers = backup.HSBackup.ConstantBuffers)
            fixed (ID3D11ShaderResourceView** resourceViews = backup.HSBackup.ResourceViews)
            {
                deviceContext->HSGetShader(&backup.HS, classInstances, &backup.HSBackup.InstancesCount);
                deviceContext->HSGetSamplers(0, D3D11.CommonshaderSamplerSlotCount, samplers);
                deviceContext->HSGetConstantBuffers(0, D3D11.CommonshaderConstantBufferApiSlotCount, constantBuffers);
                deviceContext->HSGetShaderResources(0, D3D11.CommonshaderInputResourceSlotCount, resourceViews);
            }

            // -- DS
            // Allocate
            backup.DSBackup = new();

            fixed (ID3D11ClassInstance** classInstances = backup.DSBackup.Instances)
            fixed (ID3D11SamplerState** samplers = backup.DSBackup.Samplers)
            fixed (ID3D11Buffer** constantBuffers = backup.DSBackup.ConstantBuffers)
            fixed (ID3D11ShaderResourceView** resourceViews = backup.DSBackup.ResourceViews)
            {
                deviceContext->DSGetShader(&backup.DS, classInstances, &backup.DSBackup.InstancesCount);
                deviceContext->DSGetSamplers(0, D3D11.CommonshaderSamplerSlotCount, samplers);
                deviceContext->DSGetConstantBuffers(0, D3D11.CommonshaderConstantBufferApiSlotCount, constantBuffers);
                deviceContext->DSGetShaderResources(0, D3D11.CommonshaderInputResourceSlotCount, resourceViews);
            }

            // -- GS
            // Allocate
            backup.GSBackup = new();

            fixed (ID3D11ClassInstance** classInstances = backup.GSBackup.Instances)
            fixed (ID3D11SamplerState** samplers = backup.GSBackup.Samplers)
            fixed (ID3D11Buffer** constantBuffers = backup.GSBackup.ConstantBuffers)
            fixed (ID3D11ShaderResourceView** resourceViews = backup.GSBackup.ResourceViews)
            {
                deviceContext->GSGetShader(&backup.GS, classInstances, &backup.GSBackup.InstancesCount);
                deviceContext->GSGetSamplers(0, D3D11.CommonshaderSamplerSlotCount, samplers);
                deviceContext->GSGetConstantBuffers(0, D3D11.CommonshaderConstantBufferApiSlotCount, constantBuffers);
                deviceContext->GSGetShaderResources(0, D3D11.CommonshaderInputResourceSlotCount, resourceViews);
            }

            // -- PS
            // Allocate
            backup.PSBackup = new();
            fixed (ID3D11ClassInstance** classInstances = backup.PSBackup.Instances)
            fixed (ID3D11SamplerState** samplers = backup.PSBackup.Samplers)
            fixed (ID3D11Buffer** constantBuffers = backup.PSBackup.ConstantBuffers)
            fixed (ID3D11ShaderResourceView** resourceViews = backup.PSBackup.ResourceViews)
            {
                deviceContext->PSGetShader(&backup.PS, classInstances, &backup.PSBackup.InstancesCount);
                deviceContext->PSGetSamplers(0, D3D11.CommonshaderSamplerSlotCount, samplers);
                deviceContext->PSGetConstantBuffers(0, D3D11.CommonshaderConstantBufferApiSlotCount, constantBuffers);
                deviceContext->PSGetShaderResources(0, D3D11.CommonshaderInputResourceSlotCount, resourceViews);
            }

            // -- CS
            // Allocate
            backup.CSBackup = new();
            backup.CSUAVs = new(D3D11.D3D111UavSlotCount);

            fixed (ID3D11ClassInstance** classInstances = backup.CSBackup.Instances)
            fixed (ID3D11SamplerState** samplers = backup.CSBackup.Samplers)
            fixed (ID3D11Buffer** constantBuffers = backup.CSBackup.ConstantBuffers)
            fixed (ID3D11ShaderResourceView** resourceViews = backup.CSBackup.ResourceViews)
            {
                deviceContext->CSGetShader(&backup.CS, classInstances, &backup.CSBackup.InstancesCount);
                deviceContext->CSGetSamplers(0, D3D11.CommonshaderSamplerSlotCount, samplers);
                deviceContext->CSGetConstantBuffers(0, D3D11.CommonshaderConstantBufferApiSlotCount, constantBuffers);
                deviceContext->CSGetShaderResources(0, D3D11.CommonshaderInputResourceSlotCount, resourceViews);
                deviceContext->CSGetUnorderedAccessViews(0, D3D11.D3D111UavSlotCount, backup.CSUAVs);
            }
            return backup;
        }
        #endregion
    }
}