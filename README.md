# ImGuiNETBackends

"Experimental" ImGui renderer backends for C#, ported from [ocornut/imgui](https://github.com/ocornut/imgui/). Supports `docking` where available. They were written for use in Snowflake's [Ingame API](https://github.com/SnowflakePowered/snowflake/pull/836) and geared for use in minimal environments without a controller, such as in hooked rendering contexts.

Currently tested with ImGui.NET 1.86.

## Implemented
These backends have been implemented fully including viewports and user callback support.
- [x] Direct3D11
    - Full state save from [ff-meli/ImGuiScene](https://github.com/ff-meli/ImGuiScene/).
- [x] OpenGL3/ES
    - This implementation was mostly pulled verbatim from [Silk.NET.OpenGL.Extensions.ImGui](https://github.com/dotnet/Silk.NET/tree/main/src/OpenGL/Extensions/Silk.NET.OpenGL.Extensions.ImGui) and extracted here for use without an owning controller.

## To do
These will be implemented soon.
- [ ] Direct3D 12
- [ ] Vulkan

## Wontfix and low-priority
Input backends are not in scope for this project. Additionally, the following desktop renderer backends are low priority and may not ever be implemented.

- [ ] Direct3D10
    * May only need minimal modifications from the Direct3D11 backend, so maybe.
- [ ] Direct3D9
    * Maybe, if I find a use for it, or if someone else does this in a PR.
- [ ] OpenGL2 
  * This backend is wontfix.