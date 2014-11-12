﻿﻿using Microsoft.WindowsAzure.Management.HDInsight.ClusterProvisioning.Data;
using Microsoft.WindowsAzure.Management.HDInsight.Cmdlet.DataObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.WindowsAzure.Management.HDInsight.Cmdlet.Commands.BaseCommandInterfaces
{
    internal interface IAddAzureHDInsightConfigActionBase
    {
        /// <summary>  
        /// Gets or sets the AzureHDInsightConfig.  
        /// </summary>  
        AzureHDInsightConfig Config { get; set; }

        /// <summary>  
        /// Gets or sets the name of the config action.  
        /// </summary>  
        string Name { get; set; }

        /// <summary>  
        /// Gets or sets the affected nodes of the config action.  
        /// </summary>  
        ClusterNodeType[] ClusterRoleCollection { get; set; }
    }
}