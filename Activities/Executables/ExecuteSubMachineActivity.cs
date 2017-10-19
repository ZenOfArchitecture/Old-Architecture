using System;
using Ventana.Core.Base.Activities;
using Ventana.Core.Utilities;

namespace Ventana.Core.Activities.Executables
{
    public class ExecuteSubMachineActivity : DoIfConditionActivity<object>
    {
        public ExecuteSubMachineActivity(string name, DynamicConfiguration machineConfig, Func<DynamicConfiguration, IActivityMachine> creator)
            : base(name, null, o => true, machineConfig)
        {
            CreateMachine = creator;
        }
    }
}
