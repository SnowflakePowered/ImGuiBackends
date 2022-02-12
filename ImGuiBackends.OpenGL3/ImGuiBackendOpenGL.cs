using ImGuiNET;
using System;
using System.Numerics;

#if GLES
using Silk.NET.OpenGLES;
#elif GL
using Silk.NET.OpenGL;
#endif

namespace ImGuiBackends.OpenGL3
{
    // Literally ripped from with all the input stuff stripped out.
    // https://github.com/dotnet/Silk.NET/blob/main/src/OpenGL/Extensions/Silk.NET.OpenGL.Extensions.ImGui/ImGuiController.cs
    public class ImGuiBackendOpenGL
    {
        private GL _gl;
        private Version _glVersion;

        private int _attribLocationTex;
        private int _attribLocationProjMtx;
        private int _attribLocationVtxPos;
        private int _attribLocationVtxUV;
        private int _attribLocationVtxColor;
        private uint _vboHandle;
        private uint _elementsHandle;
        private uint _vertexArrayObject;

        private nuint _vertexBufferSize;
        private nuint _indexBufferSize;

        private Texture _fontTexture;
        private Shader _shader;

        public void Init(GL gl)
        {
            _gl = gl;
            _glVersion = new Version(gl.GetInteger(GLEnum.MajorVersion), gl.GetInteger(GLEnum.MinorVersion));

#if GL
            var io = ImGui.GetIO();
            if (_glVersion.Major >= 3 && _glVersion.Minor >= 2)
                io.BackendFlags = io.BackendFlags | ImGuiBackendFlags.RendererHasVtxOffset;
#endif
        }

        public void NewFrame()
        {
            if (_shader == null)
            {
                this.CreateDeviceObjects();
            }
        }

        public unsafe void RenderDrawData(ImDrawDataPtr drawDataPtr)
        {
            int framebufferWidth = (int)(drawDataPtr.DisplaySize.X * drawDataPtr.FramebufferScale.X);
            int framebufferHeight = (int)(drawDataPtr.DisplaySize.Y * drawDataPtr.FramebufferScale.Y);
            if (framebufferWidth <= 0 || framebufferHeight <= 0)
                return;

            // Backup GL state
            _gl.GetInteger(GLEnum.ActiveTexture, out int lastActiveTexture);
            _gl.ActiveTexture(GLEnum.Texture0);

            _gl.GetInteger(GLEnum.CurrentProgram, out int lastProgram);
            _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);

            _gl.GetInteger(GLEnum.SamplerBinding, out int lastSampler);

            _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
            _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArrayObject);

#if !GLES
            Span<int> lastPolygonMode = stackalloc int[2];
            _gl.GetInteger(GLEnum.PolygonMode, lastPolygonMode);
#endif

            Span<int> lastScissorBox = stackalloc int[4];
            _gl.GetInteger(GLEnum.ScissorBox, lastScissorBox);

            _gl.GetInteger(GLEnum.BlendSrcRgb, out int lastBlendSrcRgb);
            _gl.GetInteger(GLEnum.BlendDstRgb, out int lastBlendDstRgb);

            _gl.GetInteger(GLEnum.BlendSrcAlpha, out int lastBlendSrcAlpha);
            _gl.GetInteger(GLEnum.BlendDstAlpha, out int lastBlendDstAlpha);

            _gl.GetInteger(GLEnum.BlendEquationRgb, out int lastBlendEquationRgb);
            _gl.GetInteger(GLEnum.BlendEquationAlpha, out int lastBlendEquationAlpha);

            bool lastEnableBlend = _gl.IsEnabled(GLEnum.Blend);
            bool lastEnableCullFace = _gl.IsEnabled(GLEnum.CullFace);
            bool lastEnableDepthTest = _gl.IsEnabled(GLEnum.DepthTest);
            bool lastEnableStencilTest = _gl.IsEnabled(GLEnum.StencilTest);
            bool lastEnableScissorTest = _gl.IsEnabled(GLEnum.ScissorTest);

#if !GLES
            bool lastEnablePrimitiveRestart = _gl.IsEnabled(GLEnum.PrimitiveRestart);
#endif

            SetupRenderState(drawDataPtr, framebufferWidth, framebufferHeight);

            // Will project scissor/clipping rectangles into framebuffer space
            Vector2 clipOff = drawDataPtr.DisplayPos;         // (0,0) unless using multi-viewports
            Vector2 clipScale = drawDataPtr.FramebufferScale; // (1,1) unless using retina display which are often (2,2)

            // Render command lists
            for (int n = 0; n < drawDataPtr.CmdListsCount; n++)
            {
                ImDrawListPtr cmdListPtr = drawDataPtr.CmdListsRange[n];

                // Upload vertex/index buffers

                // patch: https://github.com/ocornut/imgui/commit/389982eb5afbb9f93873d87f1201842ccbc82dec#diff-518fceee829a7e5d6551f48feb3080c081c00b4a153b9ee07905c58f8a854136R431

                nuint vtxBufferSize = unchecked((nuint)(cmdListPtr.VtxBuffer.Size * sizeof(ImDrawVert)));
                nuint idxBufferSize = unchecked((nuint)(cmdListPtr.IdxBuffer.Size * sizeof(ushort))); // sizeof(ushort) == ImDrawIdx

                if (_vertexBufferSize < vtxBufferSize)
                {
                    _vertexBufferSize = vtxBufferSize;
                    _gl.BufferData(GLEnum.ArrayBuffer, _vertexBufferSize, null, GLEnum.StreamDraw);
                }
                if (_indexBufferSize < idxBufferSize)
                {
                    _indexBufferSize = idxBufferSize;
                    _gl.BufferData(GLEnum.ElementArrayBuffer, _indexBufferSize, null, GLEnum.StreamDraw);
                }

                _gl.BufferSubData(GLEnum.ArrayBuffer, 0, vtxBufferSize, cmdListPtr.VtxBuffer.Data.ToPointer());
                _gl.CheckGlError($"Data Vert {n}");
                _gl.BufferSubData(GLEnum.ElementArrayBuffer, 0, idxBufferSize, cmdListPtr.IdxBuffer.Data.ToPointer());
                _gl.CheckGlError($"Data Idx {n}");

                for (int cmd_i = 0; cmd_i < cmdListPtr.CmdBuffer.Size; cmd_i++)
                {
                    ImDrawCmdPtr cmdPtr = cmdListPtr.CmdBuffer[cmd_i];

                    if (cmdPtr.UserCallback != IntPtr.Zero)
                    {
                        if (cmdPtr.UserCallback == (nint)(-1))
                        {
                            this.SetupRenderState(drawDataPtr, framebufferWidth, framebufferHeight);
                        }
                        else
                        {
#if NET6_0
                            // typedef void (*ImDrawCallback)(const ImDrawList* parent_list, const ImDrawCmd* cmd);
                            delegate*<ImDrawList*, ImDrawCmd*, void> imDrawCallback = (delegate*<ImDrawList*, ImDrawCmd*, void>)cmdPtr.UserCallback;
                            if (imDrawCallback != null)
                            {
                                imDrawCallback(cmdList, cmdPtr);
                            }
#else
                            throw new NotImplementedException();
#endif
                        }
                    }
                    else
                    {
                        Vector4 clipRect;
                        
                        clipRect.X = (cmdPtr.ClipRect.X - clipOff.X) * clipScale.X;
                        clipRect.Y = (cmdPtr.ClipRect.Y - clipOff.Y) * clipScale.Y;
                        clipRect.Z = (cmdPtr.ClipRect.Z - clipOff.X) * clipScale.X;
                        clipRect.W = (cmdPtr.ClipRect.W - clipOff.Y) * clipScale.Y;

                        if (clipRect.X < framebufferWidth && clipRect.Y < framebufferHeight && clipRect.Z >= 0.0f && clipRect.W >= 0.0f)
                        {
                            // Apply scissor/clipping rectangle
                            _gl.Scissor((int)clipRect.X, (int)(framebufferHeight - clipRect.W), (uint)(clipRect.Z - clipRect.X), (uint)(clipRect.W - clipRect.Y));
                            _gl.CheckGlError("Scissor");

                            // Bind texture, Draw
                            _gl.BindTexture(GLEnum.Texture2D, (uint)cmdPtr.TextureId);
                            _gl.CheckGlError("Texture");

                            _gl.DrawElementsBaseVertex(GLEnum.Triangles, cmdPtr.ElemCount, GLEnum.UnsignedShort, (void*)(cmdPtr.IdxOffset * sizeof(ushort)), (int)cmdPtr.VtxOffset);
                            _gl.CheckGlError("Draw");
                        }
                    }
                }
            }

            // Destroy the temporary VAO
            _gl.DeleteVertexArray(_vertexArrayObject);
            _vertexArrayObject = 0;

            // Restore modified GL state
            _gl.UseProgram((uint)lastProgram);
            _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);

            _gl.BindSampler(0, (uint)lastSampler);

            _gl.ActiveTexture((GLEnum)lastActiveTexture);

            _gl.BindVertexArray((uint)lastVertexArrayObject);

            _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);
            _gl.BlendEquationSeparate((GLEnum)lastBlendEquationRgb, (GLEnum)lastBlendEquationAlpha);
            _gl.BlendFuncSeparate((GLEnum)lastBlendSrcRgb, (GLEnum)lastBlendDstRgb, (GLEnum)lastBlendSrcAlpha, (GLEnum)lastBlendDstAlpha);

            if (lastEnableBlend)
            {
                _gl.Enable(GLEnum.Blend);
            }
            else
            {
                _gl.Disable(GLEnum.Blend);
            }

            if (lastEnableCullFace)
            {
                _gl.Enable(GLEnum.CullFace);
            }
            else
            {
                _gl.Disable(GLEnum.CullFace);
            }

            if (lastEnableDepthTest)
            {
                _gl.Enable(GLEnum.DepthTest);
            }
            else
            {
                _gl.Disable(GLEnum.DepthTest);
            }
            if (lastEnableStencilTest)
            {
                _gl.Enable(GLEnum.StencilTest);
            }
            else
            {
                _gl.Disable(GLEnum.StencilTest);
            }

            if (lastEnableScissorTest)
            {
                _gl.Enable(GLEnum.ScissorTest);
            }
            else
            {
                _gl.Disable(GLEnum.ScissorTest);
            }

#if !GLES
            if (lastEnablePrimitiveRestart)
            {
                _gl.Enable(GLEnum.PrimitiveRestart);
            }
            else
            {
                _gl.Disable(GLEnum.PrimitiveRestart);
            }

            _gl.PolygonMode(GLEnum.FrontAndBack, (GLEnum)lastPolygonMode[0]);
#endif

            _gl.Scissor(lastScissorBox[0], lastScissorBox[1], (uint)lastScissorBox[2], (uint)lastScissorBox[3]);
        }

        public unsafe bool CreateDeviceObjects()
        {
            if (_gl == null)
                return false;

            // Backup GL state
            _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);
            _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
            _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArray);

            string vertexSource =
#if GLES
                @"#version 300 es
        precision highp float;
            
        layout (location = 0) in vec2 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        uniform mat4 ProjMtx;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy,0.0,1.0);
        }";
#elif GL
                @"#version 330
        layout (location = 0) in vec2 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        uniform mat4 ProjMtx;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy,0,1);
        }";
#endif


            string fragmentSource =
#if GLES
                @"#version 300 es
        precision highp float;
        
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        uniform sampler2D Texture;
        layout (location = 0) out vec4 Out_Color;
        void main()
        {
            Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
        }";
#elif GL
                @"#version 330
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        uniform sampler2D Texture;
        layout (location = 0) out vec4 Out_Color;
        void main()
        {
            Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
        }";
#endif

            _shader = new Shader(_gl, vertexSource, fragmentSource);

            _attribLocationTex = _shader.GetUniformLocation("Texture");
            _attribLocationProjMtx = _shader.GetUniformLocation("ProjMtx");
            _attribLocationVtxPos = _shader.GetAttribLocation("Position");
            _attribLocationVtxUV = _shader.GetAttribLocation("UV");
            _attribLocationVtxColor = _shader.GetAttribLocation("Color");

            _vboHandle = _gl.GenBuffer();
            _elementsHandle = _gl.GenBuffer();

            CreateFontsTexture();

            // Restore modified GL state
            _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
            _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);

            _gl.BindVertexArray((uint)lastVertexArray);

            _gl.CheckGlError("End of ImGui setup");
            return _gl.GetError() == GLEnum.NoError;
        }

        public unsafe void InvalidateDeviceObjects()
        {
            if (_gl == null)
                return;

            _gl.DeleteBuffer(_vboHandle);
            _gl.DeleteBuffer(_elementsHandle);
            _gl.DeleteVertexArray(_vertexArrayObject);
            _shader.Dispose();
            _fontTexture.Dispose();
        }

        public void Shutdown()
        {
            // Nothing much to do here since we don't need to release any device pointers.
            this.InvalidateDeviceObjects();

            // let go of the GL context..
            _gl = null;
            _glVersion = null;
        }

        #region Internal
        private unsafe void SetupRenderState(ImDrawDataPtr drawDataPtr, int framebufferWidth, int framebufferHeight)
        {
            // Setup render state: alpha-blending enabled, no face culling, no depth testing, scissor enabled, polygon fill
            _gl.Enable(GLEnum.Blend);
            _gl.BlendEquation(GLEnum.FuncAdd);
            _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
            _gl.Disable(GLEnum.CullFace);
            _gl.Disable(GLEnum.DepthTest);
            _gl.Disable(GLEnum.StencilTest);
            _gl.Enable(GLEnum.ScissorTest);
#if !GLES
            _gl.Disable(GLEnum.PrimitiveRestart);
            _gl.PolygonMode(GLEnum.FrontAndBack, GLEnum.Fill);
#endif

            float L = drawDataPtr.DisplayPos.X;
            float R = drawDataPtr.DisplayPos.X + drawDataPtr.DisplaySize.X;
            float T = drawDataPtr.DisplayPos.Y;
            float B = drawDataPtr.DisplayPos.Y + drawDataPtr.DisplaySize.Y;

            Span<float> orthoProjection = stackalloc float[] {
                2.0f / (R - L),
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                2.0f / (T - B),
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                -1.0f,
                0.0f,
                (R + L) / (L - R),
                (T + B) / (B - T),
                0.0f,
                1.0f,
            };

            _shader.UseShader();
            _gl.Uniform1(_attribLocationTex, 0);
            _gl.UniformMatrix4(_attribLocationProjMtx, 1, false, orthoProjection);
            _gl.CheckGlError("Projection");

            _gl.BindSampler(0, 0);

            // Setup desired GL state
            // Recreate the VAO every time (this is to easily allow multiple GL contexts to be rendered to. VAO are not shared among GL contexts)
            // The renderer would actually work without any VAO bound, but then our VertexAttrib calls would overwrite the default one currently bound.
            _vertexArrayObject = _gl.GenVertexArray();
            _gl.BindVertexArray(_vertexArrayObject);
            _gl.CheckGlError("VAO");

            // Bind vertex/index buffers and setup attributes for ImDrawVert
            _gl.BindBuffer(GLEnum.ArrayBuffer, _vboHandle);
            _gl.BindBuffer(GLEnum.ElementArrayBuffer, _elementsHandle);
            _gl.EnableVertexAttribArray((uint)_attribLocationVtxPos);
            _gl.EnableVertexAttribArray((uint)_attribLocationVtxUV);
            _gl.EnableVertexAttribArray((uint)_attribLocationVtxColor);
            _gl.VertexAttribPointer((uint)_attribLocationVtxPos, 2, GLEnum.Float, false, (uint)sizeof(ImDrawVert), (void*)0);
            _gl.VertexAttribPointer((uint)_attribLocationVtxUV, 2, GLEnum.Float, false, (uint)sizeof(ImDrawVert), (void*)8);
            _gl.VertexAttribPointer((uint)_attribLocationVtxColor, 4, GLEnum.UnsignedByte, true, (uint)sizeof(ImDrawVert), (void*)16);
        }

        /// <summary>
        /// Creates the texture used to render text.
        /// </summary>
        private unsafe void CreateFontsTexture()
        {
            // Build texture atlas
            var io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int _bytesPerPixel);   // Load as RGBA 32-bit (75% of the memory is wasted, but default font is so small) because it is more likely to be compatible with user's existing shaders. If your ImTextureId represent a higher-level concept than just a GL texture id, consider calling GetTexDataAsAlpha8() instead to save on GPU memory.

            // Upload texture to graphics system
            _gl.GetInteger(GLEnum.Texture2D, out int lastTexture);

            _fontTexture = new Texture(_gl, width, height, pixels);
            _fontTexture.Bind();
            _fontTexture.SetMagFilter(TextureMagFilter.Linear);
            _fontTexture.SetMinFilter(TextureMinFilter.Linear);

            // Store our identifier
            io.Fonts.SetTexID((IntPtr)_fontTexture.GlTexture);

            // Restore state
            _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
        }
        #endregion
    }
}
