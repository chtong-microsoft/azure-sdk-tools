﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

namespace Microsoft.WindowsAzure.Management.ServiceManagement.IaaS
{
    using System;
    using System.Management.Automation;
    using Samples.WindowsAzure.ServiceManagement;
    using Service.Gateway;
    using Management.Model;

    [Cmdlet(VerbsCommon.Remove, "AzureVNetGateway"), OutputType(typeof(ManagementOperationContext))]
    public class RemoveAzureVNetGatewayCommand : GatewayCmdletBase
    {
        public RemoveAzureVNetGatewayCommand()
        {
        }

        public RemoveAzureVNetGatewayCommand(IGatewayServiceManagement channel)
        {
            Channel = channel;
        }

        [Parameter(Position = 0, Mandatory = true, HelpMessage = "Virtual network name.")]
        public string VNetName
        {
            get;
            set;
        }

        protected override void OnProcessRecord()
        {
            ExecuteClientActionInOCS(null, this.CommandRuntime.ToString(), s => this.Channel.DeleteVirtualNetworkGateway(s, this.VNetName), this.WaitForGatewayOperation);
        }
    }
}
