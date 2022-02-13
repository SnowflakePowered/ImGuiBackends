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
    public partial class ImGuiBackendDirect3D11
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

        private unsafe ID3D11Device* _device;
        private unsafe ID3D11DeviceContext* _deviceContext;
        private unsafe IDXGIFactory* _factory;
        private unsafe ID3D11Buffer* _vertexBuffer;
        private unsafe ID3D11Buffer* _indexBuffer;
        private unsafe ID3D11VertexShader* _vertexShader;
        private unsafe ID3D11InputLayout* _inputLayout;
        private unsafe ID3D11Buffer* _vertexConstantBuffer;
        private unsafe ID3D11PixelShader* _pixelShader;
        private unsafe ID3D11SamplerState* _fontSampler;
        private unsafe ID3D11ShaderResourceView* _fontTextureView;
        private unsafe ID3D11RasterizerState* _rasterizerState;
        private unsafe ID3D11BlendState* _blendState;
        private unsafe ID3D11DepthStencilState* _depthStencilState;
        private int _vertexBufferSize;
        private int _indexBufferSize;
        private bool _backupState;
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

            _deviceContext->IASetInputLayout(_inputLayout);
            _deviceContext->IASetVertexBuffers(0, 1, ref _vertexBuffer, &stride, &offset);
            _deviceContext->IASetIndexBuffer(_indexBuffer, sizeof(ushort) == 2 ? Format.FormatR16Uint : Format.FormatR32Uint, 0);
            _deviceContext->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);
            _deviceContext->VSSetShader(_vertexShader, null, 0);
            _deviceContext->VSSetConstantBuffers(0, 1, ref _vertexConstantBuffer);
            _deviceContext->PSSetShader(_pixelShader, null, 0);
            _deviceContext->PSSetSamplers(0, 1, ref _fontSampler);
            _deviceContext->GSSetShader(null, null, 0);
            _deviceContext->HSSetShader(null, null, 0); // In theory we should backup and restore this as well.. very infrequently used..
            _deviceContext->DSSetShader(null, null, 0); // In theory we should backup and restore this as well.. very infrequently used..
            _deviceContext->CSSetShader(null, null, 0); // In theory we should backup and restore this as well.. very infrequently used..

            // Setup blend state
            Vector4 blendFactor = new(0, 0, 0, 0);
            _deviceContext->OMSetBlendState(_blendState, (float*)&blendFactor, 0xffffffff);

            _deviceContext->OMSetDepthStencilState(_depthStencilState, 0);
            _deviceContext->RSSetState(_rasterizerState);
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
            if (_vertexBuffer == null || _vertexBufferSize < drawData.TotalVtxCount)
            {
                if (_vertexBuffer != null)
                {
                    _vertexBuffer->Release();
                    _vertexBuffer = null;
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

                if (CheckDxError(_device->CreateBuffer(&desc, null, ref _vertexBuffer), "Grow VB"))
                    return;
            }

            if (_indexBuffer == null || _indexBufferSize < drawData.TotalIdxCount)
            {
                if (_indexBuffer != null)
                {
                    _indexBuffer->Release();
                    _indexBuffer = null;
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

                if (CheckDxError(_device->CreateBuffer(&desc, null, ref _indexBuffer), "Grow IB"))
                    return;
            }

            // upload vertex/index data into a single contiguous GPU buffer
            {
                MappedSubresource vtxResource = new(), idxResource = new();
                if (_vertexBuffer == null || _indexBuffer == null)
                    return;
                // if this doesn't work then we need to queryinterface :(
                if (CheckDxError(deviceContext->Map((ID3D11Resource*)_vertexBuffer, 0, Map.MapWriteDiscard, 0, &vtxResource), "Map VTX"))
                    return;
                if (CheckDxError(deviceContext->Map((ID3D11Resource*)_indexBuffer, 0, Map.MapWriteDiscard, 0, &idxResource), "Map IDX"))
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

                deviceContext->Unmap((ID3D11Resource*)_vertexBuffer, 0);
                deviceContext->Unmap((ID3D11Resource*)_indexBuffer, 0);
            }

            // Setup orthographic projection matrix into our constant buffer
            // Our visible imgui space lies from drawData->DisplayPos (top left) to drawData->DisplayPos+dataData->DisplaySize (bottom right).
            // DisplayPos is (0,0) for single viewport apps.
            {
                MappedSubresource projectionMatrix = new();
                if (CheckDxError(deviceContext->Map((ID3D11Resource*)_vertexConstantBuffer, 
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
                deviceContext->Unmap((ID3D11Resource*)_vertexConstantBuffer, 0);
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
                            // typedef void (*ImDrawCallback)(const ImDrawList* parent_list, const ImDrawCmd* cmd);
                            delegate* unmanaged<ImDrawList*, ImDrawCmd*, void> imDrawCallback = (delegate* unmanaged<ImDrawList*, ImDrawCmd*, void>)cmdPtr.UserCallback;
                            if (imDrawCallback != null)
                            {
                                imDrawCallback(cmdList, cmdPtr);
                            }
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
            this.RestoreState(&backup);
            // backup disposed here.
        }

        /// <summary>
        /// Restore DX11 Device State.
        /// 
        /// If <see cref="_backupState"/> is false, this method does nothing.
        /// </summary>
        /// <param name="_backup">A pointer to a stack-allocated <see cref="D3D11StateBackup"/> instance.</param>
        private unsafe void RestoreState(D3D11StateBackup* _backup)
        {
            if (!_backupState)
                return;

            ID3D11DeviceContext* deviceContext = _deviceContext;

            ref readonly D3D11StateBackup backup = ref *_backup;

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
            deviceContext->OMSetBlendState(backup.BlendState, _backup->BlendFactor, backup.SampleMask);
            deviceContext->OMSetDepthStencilState(backup.DepthStencilState, backup.DepthStencilRef);
            deviceContext->OMSetRenderTargets((uint)backup.RenderTargetViews.Length, backup.RenderTargetViews, backup.DepthStencilView);

            // -- VS
            deviceContext->VSSetShader(backup.VS, backup.VSBackup.Instances, backup.VSBackup.InstancesCount);
            deviceContext->VSSetSamplers(0, (uint)backup.VSBackup.Samplers.Length, backup.VSBackup.Samplers);
            deviceContext->VSSetConstantBuffers(0, (uint)backup.VSBackup.ConstantBuffers.Length, backup.VSBackup.ConstantBuffers);
            deviceContext->VSSetShaderResources(0, (uint)backup.VSBackup.ResourceViews.Length, backup.VSBackup.ResourceViews);

            // -- HS
            deviceContext->HSSetShader(backup.HS, backup.HSBackup.Instances, backup.HSBackup.InstancesCount);
            deviceContext->HSSetSamplers(0, (uint)backup.HSBackup.Samplers.Length, backup.HSBackup.Samplers);
            deviceContext->HSSetConstantBuffers(0, (uint)backup.HSBackup.ConstantBuffers.Length, backup.HSBackup.ConstantBuffers);
            deviceContext->HSSetShaderResources(0, (uint)backup.HSBackup.ResourceViews.Length, backup.HSBackup.ResourceViews);

            // -- DS
            deviceContext->DSSetShader(backup.DS, backup.DSBackup.Instances, backup.DSBackup.InstancesCount);
            deviceContext->DSSetSamplers(0, (uint)backup.DSBackup.Samplers.Length, backup.DSBackup.Samplers);
            deviceContext->DSSetConstantBuffers(0, (uint)backup.DSBackup.ConstantBuffers.Length, backup.DSBackup.ConstantBuffers);
            deviceContext->DSSetShaderResources(0, (uint)backup.DSBackup.ResourceViews.Length, backup.DSBackup.ResourceViews);

            // -- GS
            deviceContext->GSSetShader(backup.GS, backup.GSBackup.Instances, backup.GSBackup.InstancesCount);
            deviceContext->GSSetSamplers(0, (uint)backup.GSBackup.Samplers.Length, backup.GSBackup.Samplers);
            deviceContext->GSSetConstantBuffers(0, (uint)backup.GSBackup.ConstantBuffers.Length, backup.GSBackup.ConstantBuffers);
            deviceContext->GSSetShaderResources(0, (uint)backup.GSBackup.ResourceViews.Length, backup.GSBackup.ResourceViews);

            // -- PS
            deviceContext->PSSetShader(backup.PS, backup.PSBackup.Instances, backup.PSBackup.InstancesCount);
            deviceContext->PSSetSamplers(0, (uint)backup.PSBackup.Samplers.Length, backup.PSBackup.Samplers);
            deviceContext->PSSetConstantBuffers(0, (uint)backup.PSBackup.ConstantBuffers.Length, backup.PSBackup.ConstantBuffers);
            deviceContext->PSSetShaderResources(0, (uint)backup.PSBackup.ResourceViews.Length, backup.PSBackup.ResourceViews);

            // -- CS
            deviceContext->CSSetShader(backup.CS, backup.CSBackup.Instances, backup.CSBackup.InstancesCount);
            deviceContext->CSSetSamplers(0, (uint)backup.CSBackup.Samplers.Length, backup.CSBackup.Samplers);
            deviceContext->CSSetConstantBuffers(0, (uint)backup.CSBackup.ConstantBuffers.Length, backup.CSBackup.ConstantBuffers);
            deviceContext->CSSetShaderResources(0, (uint)backup.CSBackup.ResourceViews.Length, backup.CSBackup.ResourceViews);

            // CSUAVs are never very big, shouldn't worry about overflow here.
            uint* uavInitialCounts = stackalloc uint[backup.CSUAVs.Length];
            for (int i = 0; i < backup.CSUAVs.Length; i++)
                uavInitialCounts[i] = unchecked((uint)-1);

            deviceContext->CSSetUnorderedAccessViews(0, (uint)backup.CSUAVs.Length, backup.CSUAVs, uavInitialCounts);
        }

        public unsafe void InvalidateDeviceObjects()
        {
            if (_device == null)
                return;

            if (_fontSampler != null) { _fontSampler->Release(); _fontSampler = null; }
            if (_fontTextureView != null) 
            {
                _fontTextureView->Release(); 
                _fontTextureView = null;
                // We copied data->pFontTextureView to io.Fonts->TexID so let's clear that as well.
                ImGui.GetIO().Fonts.SetTexID((nint)0);  
            } 
            if (_indexBuffer != null) { _indexBuffer->Release(); _indexBuffer = null; }
            if (_vertexBuffer != null) { _vertexBuffer->Release(); _vertexBuffer = null; }
            if (_blendState != null) { _blendState->Release(); _blendState = null; }
            if (_depthStencilState != null) { _depthStencilState->Release(); _depthStencilState = null; }
            if (_rasterizerState != null) { _rasterizerState->Release(); _rasterizerState = null; }
            if (_pixelShader != null) { _pixelShader->Release(); _pixelShader = null; }
            if (_vertexConstantBuffer != null) { _vertexConstantBuffer->Release(); _vertexConstantBuffer = null; }
            if (_inputLayout != null) { _inputLayout->Release(); _inputLayout = null; }
            if (_vertexShader != null) { _vertexShader->Release(); _vertexShader = null; }
        }

        public unsafe bool CreateDeviceObjects()
        {
            if (_device == null)
                return false;
            if (_fontSampler != null)
                this.InvalidateDeviceObjects();

            // Create the vertex buffer
            fixed (byte* vertexBuffer = ShaderBlobs.VertexBuffer)
            {
                nuint vertexBufferSize = (nuint)(sizeof(byte) * ShaderBlobs.VertexBuffer.Length);
                // Shader blobs are precompiled so no need to release the compiled shader.
                if (CheckDxError(_device->CreateVertexShader(vertexBuffer,
                       vertexBufferSize, null, ref _vertexShader), "Create Vertex Shader"))
                    return false;
  
                InputElementDesc* localLayout = stackalloc InputElementDesc[]
                {
                    new(CSTR_POSITION, 0, Format.FormatR32G32Float, 0, (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.pos)), InputClassification.InputPerVertexData, 0),
                    new(CSTR_TEXCOORD, 0, Format.FormatR32G32Float, 0, (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.uv)), InputClassification.InputPerVertexData, 0),
                    new(CSTR_COLOR, 0, Format.FormatR8G8B8A8Unorm, 0, (uint)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.col)), InputClassification.InputPerVertexData, 0)
                };
                if (CheckDxError(_device->CreateInputLayout(localLayout, 3, vertexBuffer, vertexBufferSize, ref _inputLayout), 
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
            
                CheckDxError(_device->CreateBuffer(&desc, null, ref _vertexConstantBuffer), "Create VertexConstantBuffer");
            }

            // Create the pixel shader
            fixed (byte* pixelShader = ShaderBlobs.PixelShader)
            {
                nuint pixelShaderSize = (nuint)(sizeof(byte) * ShaderBlobs.PixelShader.Length);

                if (CheckDxError(_device->CreatePixelShader(pixelShader, pixelShaderSize, null, ref _pixelShader), "Create Pixel Shader"))
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

                CheckDxError(_device->CreateBlendState(&desc, ref _blendState), "Create Blend State");
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

                CheckDxError(_device->CreateRasterizerState(&desc, ref _rasterizerState), "Create Rasterizer State");
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

                CheckDxError(_device->CreateDepthStencilState(&desc, ref _depthStencilState), "Create DS State");
            }

            this.CreateFontsTexture();
            return true;
        }

        public unsafe void Init(IntPtr device, IntPtr deviceContext, bool backupState = true)
        {
            this.Init((ID3D11Device*)device.ToPointer(), (ID3D11DeviceContext*)deviceContext.ToPointer(), backupState);
        }

        public unsafe void Init(ID3D11Device* device, ID3D11DeviceContext* deviceContext, bool backupState = true)
        {
            var io = ImGui.GetIO();
            // We can honor the ImDrawCmd::VtxOffset field, allowing for large meshes.
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasViewports; 

            this._backupState = backupState;

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
                        this._device = device;
                        _deviceContext = deviceContext;
                        _factory = pFactory;
                    }
                }
            }

            if (pDXGIDevice != null) pDXGIDevice->Release();
            if (pDXGIAdapter != null) pDXGIAdapter->Release();
            device->AddRef();
            _deviceContext->AddRef();

            if (io.ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable)) this.InitPlatformInterface();
        }

        public unsafe void Shutdown()
        {
            this.ShutdownPlatformInterface();
            this.InvalidateDeviceObjects();
            if (_factory != null) _factory->Release();
            if (_device != null) _device->Release();
            if (_deviceContext != null) _deviceContext->Release();
        }

        public unsafe void NewFrame()
        {
            if (_fontSampler == null)
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

                CheckDxError(_device->CreateTexture2D(&desc, &subResource, &pTexture), "Create Font Tex2D");

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
                CheckDxError(_device->CreateShaderResourceView((ID3D11Resource*)pTexture, &srvDesc, ref _fontTextureView), "Create Font SRV Tex2D");
                pTexture->Release();

                io.Fonts.SetTexID((nint)_fontTextureView);
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

                CheckDxError(_device->CreateSamplerState(&desc, ref _fontSampler), "Create Text Sampler");
            }
        }

        // Full state save ported from 
        // https://github.com/ff-meli/ImGuiScene/blob/master/ImGuiScene/ImGui_Impl/Renderers/ImGui_Impl_DX11.cs
        /// <summary>
        /// Saves the D3D11 Device State.
        /// 
        /// If <see cref="_backupState"/> is false, the contents of the returned value is undefined. 
        /// The returned struct itself must be disposed.
        /// </summary>
        /// <returns>A backup of the D3D11 Device State. The value must be disposed after use.</returns>
        private unsafe D3D11StateBackup StateBackup()
        {
            D3D11StateBackup backup = new();
            ID3D11DeviceContext* deviceContext = _deviceContext;

            if (_backupState)
                return backup;

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

            // -- OM
            // Allocate
            backup.RenderTargetViews = new(D3D11.SimultaneousRenderTargetCount);

            deviceContext->OMGetBlendState(&backup.BlendState, backup.BlendFactor, &backup.SampleMask);
            deviceContext->OMGetDepthStencilState(&backup.DepthStencilState, &backup.DepthStencilRef);
            deviceContext->OMGetRenderTargets(D3D11.SimultaneousRenderTargetCount, backup.RenderTargetViews, &backup.DepthStencilView);

            // -- VS
            // Allocate
            backup.VSBackup = new();
            deviceContext->VSGetShader(&backup.VS, backup.VSBackup.Instances, &backup.VSBackup.InstancesCount);
            deviceContext->VSGetSamplers(0, D3D11.CommonshaderSamplerSlotCount, backup.VSBackup.Samplers);
            deviceContext->VSGetConstantBuffers(0, D3D11.CommonshaderConstantBufferApiSlotCount, backup.VSBackup.ConstantBuffers);
            deviceContext->VSGetShaderResources(0, D3D11.CommonshaderInputResourceSlotCount, backup.VSBackup.ResourceViews);

            // -- HS
            // Allocate
            backup.HSBackup = new();

            deviceContext->HSGetShader(&backup.HS, backup.HSBackup.Instances, &backup.HSBackup.InstancesCount);
            deviceContext->HSGetSamplers(0, D3D11.CommonshaderSamplerSlotCount, backup.HSBackup.Samplers);
            deviceContext->HSGetConstantBuffers(0, D3D11.CommonshaderConstantBufferApiSlotCount, backup.HSBackup.ConstantBuffers);
            deviceContext->HSGetShaderResources(0, D3D11.CommonshaderInputResourceSlotCount, backup.HSBackup.ResourceViews);

            // -- DS
            // Allocate
            backup.DSBackup = new();

            deviceContext->DSGetShader(&backup.DS, backup.DSBackup.Instances, &backup.DSBackup.InstancesCount);
            deviceContext->DSGetSamplers(0, D3D11.CommonshaderSamplerSlotCount, backup.DSBackup.Samplers);
            deviceContext->DSGetConstantBuffers(0, D3D11.CommonshaderConstantBufferApiSlotCount, backup.DSBackup.ConstantBuffers);
            deviceContext->DSGetShaderResources(0, D3D11.CommonshaderInputResourceSlotCount, backup.DSBackup.ResourceViews);

            // -- GS
            // Allocate
            backup.GSBackup = new();

            deviceContext->GSGetShader(&backup.GS, backup.GSBackup.Instances, &backup.GSBackup.InstancesCount);
            deviceContext->GSGetSamplers(0, D3D11.CommonshaderSamplerSlotCount, backup.GSBackup.Samplers);
            deviceContext->GSGetConstantBuffers(0, D3D11.CommonshaderConstantBufferApiSlotCount, backup.GSBackup.ConstantBuffers);
            deviceContext->GSGetShaderResources(0, D3D11.CommonshaderInputResourceSlotCount, backup.GSBackup.ResourceViews);

            // -- PS
            // Allocate
            backup.PSBackup = new();
            deviceContext->PSGetShader(&backup.PS, backup.PSBackup.Instances, &backup.PSBackup.InstancesCount);
            deviceContext->PSGetSamplers(0, D3D11.CommonshaderSamplerSlotCount, backup.PSBackup.Samplers);
            deviceContext->PSGetConstantBuffers(0, D3D11.CommonshaderConstantBufferApiSlotCount, backup.PSBackup.ConstantBuffers);
            deviceContext->PSGetShaderResources(0, D3D11.CommonshaderInputResourceSlotCount, backup.PSBackup.ResourceViews);

            // -- CS
            // Allocate
            backup.CSBackup = new();
            backup.CSUAVs = new(D3D11.D3D111UavSlotCount);

            deviceContext->CSGetShader(&backup.CS, backup.CSBackup.Instances, &backup.CSBackup.InstancesCount);
            deviceContext->CSGetSamplers(0, D3D11.CommonshaderSamplerSlotCount, backup.CSBackup.Samplers);
            deviceContext->CSGetConstantBuffers(0, D3D11.CommonshaderConstantBufferApiSlotCount, backup.CSBackup.ConstantBuffers);
            deviceContext->CSGetShaderResources(0, D3D11.CommonshaderInputResourceSlotCount, backup.CSBackup.ResourceViews);
            deviceContext->CSGetUnorderedAccessViews(0, D3D11.D3D111UavSlotCount, backup.CSUAVs);

            return backup;
        }
#endregion
    }
}