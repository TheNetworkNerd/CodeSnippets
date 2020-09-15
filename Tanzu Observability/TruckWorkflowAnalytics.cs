//Filename:       TruckWorkflowAnalytics.cs
//Created by:     Nick Korte
//Created Date:   September 2020
//Code Language:  C#
//Purpose:        This file is a HTPP Trigger Function that runs in Azure as part of a specific Function App.  It demonstrates use of the Wavefront
//                Metrics SDK for C# to create and send a delta counter to a Tanzu Observability (Wavefront) instance basked on URL and API token. 
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

//Add these statements so SDK calls work after installing the NuGet package for the specific VS Code Project to which this .cs file is tied.  
using Wavefront.SDK.CSharp.Common.Application;
using Wavefront.SDK.CSharp.Common;
using Wavefront.SDK.CSharp.DirectIngestion;
using App.Metrics;
using App.Metrics.Counter;
using App.Metrics.Reporting.Wavefront.Builder;

//This namespace was something I customized when creating the function inside VS Code.
namespace NetworkNerd.Functions
{
    public static class TruckWorkflowAnalytics
    {
        [FunctionName("TruckWorkflowAnalytics")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            //Begin code instrumentation - reference https://github.com/wavefrontHQ/wavefront-appmetrics-sdk-csharp
            //The URL and token are for direct ingestion of metrics (no proxy in use here).
            //The API token can be found inside the Tanzu Observability (Wavefront) web UI and is unique to your environment.  Click the gear icon in the upper right, click your e-mail address, and then select API Access. 
            string wfURL = "https://vmware.wavefront.com";
            string token = "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx";   

            //Configure a MetricsBuilder object
            var MyMetricsBuilder = new MetricsBuilder();

            //Initialize WavefrontDirectIngestionClient
            WavefrontDirectIngestionClient.Builder wfDirectIngestionClientBuilder =  new WavefrontDirectIngestionClient.Builder(wfURL, token);

            // Create an IWavefrontSender instance for sending data via direct ingestion.
            IWavefrontSender wavefrontSender = wfDirectIngestionClientBuilder.Build();

            //Configure MeetricsBuilder to Report to Wavefront with proper sender object and source tag specified.  In this case my source is the function name.
            MyMetricsBuilder.Report.ToWavefront(
            options=>
            {
                options.WavefrontSender=wavefrontSender;
                options.Source="TruckWorkflowAnalytics";
            });

            //Build IMetrics instance
            var MyMetrics = MyMetricsBuilder.Build();

            //These are arrays for key value pairs to add as metric tags.  You can add some or many here as you instrument your code.
            string[] keys = new string[3]{"FunctionApp","Cloud","Region"};
            string[] values = new string[3]{"networknerd5","Azure","Central-US"};

            // Configure and instantiate a DeltaCounter using DeltaCounterOptions.Builder.  The metric name is azure.function.execution.deltacounter.
            var myDeltaCounter = new DeltaCounterOptions.Builder("azure.function.execution.deltacounter").MeasurementUnit(Unit.Calls).Tags(new MetricTags(keys,values)).Build();

            // Increment the delta counter by 1
            MyMetrics.Measure.Counter.Increment(myDeltaCounter);

            //Force reporting all metrics
            //end of code instrumentation for metrics
            await Task.WhenAll(MyMetrics.ReportRunner.RunAllAsync());

            //Begin default template code from the HTTP trigger function template as part of the Azure Functions Extension for C#.
            log.LogInformation("C# HTTP trigger function processed a request.");

            string name = req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            //Add function execution delays based on input (for my own testing only).
            if (string.Equals(name,"0.5")) await Task.Delay(500);
            if (string.Equals(name,"1")) await Task.Delay(1000);
            if (string.Equals(name,"1.5")) await Task.Delay(1500);
            if (string.Equals(name,"2")) await Task.Delay(2000);

            string responseMessage = string.IsNullOrEmpty(name)
                ? "This HTTP triggered function executed successfully. Pass a name in the query string or in the request body for a personalized response."
                : $"Hello, {name}. This HTTP triggered function executed successfully.";

            return new OkObjectResult(responseMessage);
        }
    }
}
