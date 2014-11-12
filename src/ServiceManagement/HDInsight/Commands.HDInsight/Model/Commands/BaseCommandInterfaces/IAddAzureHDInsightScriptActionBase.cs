using Microsoft.WindowsAzure.Management.HDInsight.Cmdlet.DataObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.WindowsAzure.Management.HDInsight.Cmdlet.Commands.BaseCommandInterfaces
{
    internal interface IAddAzureHDInsightScriptActionBase : IAddAzureHDInsightConfigActionBase
    {
        /// <summary>  
        /// Gets or sets the uri of the script action.  
        /// </summary>  
        Uri Uri { get; set; }

        /// <summary>  
        /// Gets or sets the parameters of the script action.  
        /// </summary>  
        string Parameters { get; set; }
    }
}