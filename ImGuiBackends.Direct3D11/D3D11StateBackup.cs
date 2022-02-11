using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ImGuiBackends.Direct3D11
{
    /// <summary>
    /// Transient struct containing Direct3D 11 pipeline state.
    /// </summary>
    internal unsafe ref struct D3D11StateBackup
    {
        private bool _isDisposed;
        // IA
        public ID3D11InputLayout* InputLayout;
        public D3DPrimitiveTopology PrimitiveTopology;
        public ID3D11Buffer* IndexBuffer;
        public Format IndexBufferFormat;
        public uint IndexBufferOffset;

        public NativeArray<ID3D11Buffer>.PointerArray VertexBuffers;
        public NativeArray<uint> VertexBufferStrides;
        public NativeArray<uint> VertexBufferOffsets;

        // RS
        public ID3D11RasterizerState* RS;
        public uint ScissorRectsCount;
        public uint ViewportsCount;
        public NativeArray<Rectangle<int>> ScissorRects;
        public NativeArray<Viewport> Viewports;

        // OM
        public ID3D11BlendState* BlendState;
        public fixed float BlendFactor[4];
        public uint SampleMask;
        public ID3D11DepthStencilState* DepthStencilState;
        public uint DepthStencilRef;
        public ID3D11DepthStencilView* DepthStencilView;
        public ID3D11RenderTargetView*[] RenderTargetViews;

        // VS
        public ID3D11VertexShader* VS;
        public D3D11ShaderStageBackup VSBackup;

        // HS
        public ID3D11HullShader* HS;
        public D3D11ShaderStageBackup HSBackup;

        // DS
        public ID3D11DomainShader* DS;
        public D3D11ShaderStageBackup DSBackup;

        // GS
        public ID3D11GeometryShader* GS;
        public D3D11ShaderStageBackup GSBackup;

        // PS
        public ID3D11PixelShader* PS;
        public D3D11ShaderStageBackup PSBackup;

        // CS
        public ID3D11ComputeShader* CS;
        public ID3D11UnorderedAccessView*[] CSUAVs;
        public D3D11ShaderStageBackup CSBackup;

        public void Dispose()
        {
            if (_isDisposed)
                return;

            // IA
            if (this.InputLayout != null)
                this.InputLayout->Release();
            if (this.IndexBuffer != null)
                this.IndexBuffer->Release();
            foreach (var buffer in this.VertexBuffers)
            {
                if (buffer != null)
                    buffer->Release();
            }

            this.VertexBuffers.Dispose();
            this.VertexBufferStrides.Dispose();
            this.VertexBufferOffsets.Dispose();

            // RS
            if (this.RS != null)
                this.RS->Release();

            // OM
            if (this.BlendState != null)
                this.BlendState->Release();
            if (this.DepthStencilState != null)
                this.DepthStencilState->Release();
            if (this.DepthStencilView != null)
                this.DepthStencilView->Release();
            foreach (var rtv in this.RenderTargetViews)
            {
                if (rtv != null)
                    rtv->Release();
            }

            // VS
            if (this.VS != null)
                this.VS->Release();
            this.VSBackup.Dispose();

            // HS
            if (this.HS != null)
                this.HS->Release();
            this.HSBackup.Dispose();

            // DS
            if (this.DS != null)
                this.DS->Release();
            this.DSBackup.Dispose();

            // GS
            if (this.GS != null)
                this.GS->Release();
            this.GSBackup.Dispose();

            // PS
            if (this.PS != null)
                this.PS->Release();
            this.PSBackup.Dispose();

            // CS
            if (this.CS != null)
                this.CS->Release();
            this.CSBackup.Dispose();
            foreach (var uav in this.CSUAVs)
            {
                if (uav != null)
                    uav->Release();
            }

            this.ScissorRects.Dispose();
            this.Viewports.Dispose();
            _isDisposed = true;
        }
    }

    /// <summary>
    /// Transient struct containing Direct3D11 shader stage state.
    /// </summary>
    internal unsafe ref struct D3D11ShaderStageBackup
    {
        public ID3D11Buffer*[] ConstantBuffers;
        public ID3D11SamplerState*[] Samplers;
        public ID3D11ShaderResourceView*[] ResourceViews;
        public ID3D11ClassInstance*[] Instances;
        public uint InstancesCount;

        private bool _isDisposed = false;

        public D3D11ShaderStageBackup()
        {
            this.ConstantBuffers = new ID3D11Buffer*[D3D11.CommonshaderConstantBufferApiSlotCount];
            this.Samplers = new ID3D11SamplerState*[D3D11.CommonshaderSamplerSlotCount];
            this.ResourceViews = new ID3D11ShaderResourceView*[D3D11.CommonshaderInputResourceSlotCount];
            this.Instances = new ID3D11ClassInstance*[256]; // 256 is max according to PSSetShader documentation
            this.InstancesCount = 0;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            foreach (ID3D11Buffer* buffer in this.ConstantBuffers)
            {
                if (buffer != null)
                    buffer->Release();
            }

            foreach (ID3D11SamplerState* sampler in this.Samplers)
            {
                if (sampler != null)
                    sampler->Release();
            }

            foreach (ID3D11ShaderResourceView* resource in this.ResourceViews)
            {
                if (resource != null)
                    resource->Release();
            }

            foreach (ID3D11ClassInstance* classInstance in this.Instances)
            {
                if (classInstance != null)
                    classInstance->Release();
            }
            _isDisposed = true;
        }
    }
}
