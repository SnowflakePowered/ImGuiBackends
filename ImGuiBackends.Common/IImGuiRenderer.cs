using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImGuiBackends.Common
{
    public interface IImGuiRenderer
    {
        void RenderDrawData(ImDrawDataPtr drawData);

        void Shutdown();

        void NewFrame();

        bool CreateDeviceObjects();
    }
}
