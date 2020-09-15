//Filename:       TruckWorkGlobalDataAggregator.cs
//Created by:     Nick Korte
//Created Date:   September 2020
//Code Language:  C#
//Purpose:        This file is a HTPP Trigger Function that runs in Azure as part of a specific Function App.  It demonstrates use of the Wavefront
//                Opentracing SDK for C# to create a new span, the context of which is passed into this function via HTTP headers.  Additionally, it uses the
//                Metrics SDK for C# to send a delta counter to Tanzu Observability (Wavefront) via direct ingestion.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

//Add these statements so SDK calls work after installing the NuGet packages for the Metrics and Opentracing SDKs for the specific VS Code Project to which this .cs file is tied.  
using Wavefront.SDK.CSharp.Common.Application;
using Wavefront.SDK.CSharp.Common;
using Wavefront.SDK.CSharp.DirectIngestion;
using App.Metrics;
using App.Metrics.Counter;
using App.Metrics.Reporting.Wavefront.Builder;
using Wavefront.OpenTracing.SDK.CSharp.Reporting;
using Wavefront.OpenTracing.SDK.CSharp;
using OpenTracing.Propagation;
using System.Collections.Generic;

namespace NetworkNerd.Functions
{
    public static class TruckGlobalDataAggregator
    {
        [FunctionName("TruckGlobalDataAggregator")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
             //Begin code instrumentation - reference https://github.com/wavefrontHQ/wavefront-opentracing-sdk-csharp
            //The application, service, cluster, and shard variables are all metadata to be added to each span created.
            string application = "VMworld2020Demo";
            string service = "GlobalDataAggregator";
            string cluster = "Azure";
            string shard = "networknerd4";

            //The URL and token are for direct ingestion of metrics, traces, and spans (no proxy in use here).
            //The API token can be found inside the Tanzu Observability (Wavefront) web UI and is unique to your environment.  Click the gear icon in the upper right, click your e-mail address, and then select API Access. 
            string wfURL = "https://vmware.wavefront.com";
            string token = "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx";   

            // Create ApplicationTags - for tracing purposes
            ApplicationTags applicationTags = new ApplicationTags.Builder(application, service).Cluster(cluster).Shard(shard).Build();

            //Configure a MetricsBuilder object - for custom metrics sent via the Metrics SDK
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
                options.Source="TruckGlobalDataAggregator";
            });

            //Build IMetrics instance
            var MyMetrics = MyMetricsBuilder.Build();

            //These are arrays for key value pairs to add as metric tags.  You can add some or many here as you instrument your code.
            string[] keys = new string[3]{"FunctionApp","Cloud","Region"};
            string[] values = new string[3]{"networknerd4","Azure","Central-US"};

            // Configure and instantiate a DeltaCounter using DeltaCounterOptions.Builder.  The metric name is azure.function.execution.deltacounter.
            var myDeltaCounter = new DeltaCounterOptions.Builder("azure.function.execution.deltacounter").MeasurementUnit(Unit.Calls).Tags(new MetricTags(keys, values)).Build();

            // Increment the counter by 1
            MyMetrics.Measure.Counter.Increment(myDeltaCounter);

            //Force reporting all custom metrics
            await Task.WhenAll(MyMetrics.ReportRunner.RunAllAsync());

        
            //Create a WavefrontSpanReporter for reporting trace data that originates on <sourceName>.  The source is the function name in this case.
            IReporter wfSpanReporter = new WavefrontSpanReporter.Builder()
            .WithSource("TruckGlobalDataAggregator").Build(wavefrontSender);

            //Create CompositeReporter and ConsoleReporter objects for more OpenTracing metrics
            IReporter consoleReporter = new ConsoleReporter("TruckGlobalDataAggregator");
            IReporter compositeReporter = new CompositeReporter(wfSpanReporter,consoleReporter);
        
            //Create the WavefrontTracer.
            WavefrontTracer MyTracer = new WavefrontTracer.Builder(wfSpanReporter, applicationTags).Build();

            //The variable MyDictionary is needed to extract span context in case a call is made from another function / outside this function.
            IDictionary<string,string> MyDictionary = new Dictionary <string,string>();
            foreach (var entry in req.Headers)
            {
                MyDictionary.TryAdd(entry.Key, entry.Value);

            }

            //Attempt to pull span fontext from HTTP headers passed into this function to continue a span across environments.  The proper context will be loaded into the variable
            //ctx if so.  The second line of code loads all metadata from the span context.  
            ITextMap carrier = new TextMapExtractAdapter(MyDictionary);
            OpenTracing.ISpanContext ctx = MyTracer.Extract(BuiltinFormats.HttpHeaders, carrier);
            OpenTracing.IScope receivingScope = MyTracer.BuildSpan("TruckGlobalDataAggregator.Execute").AsChildOf(ctx).StartActive(true);

            //Start building a new span called TruckGlobalDataAggregator.Execute if there was no context passed into headers.
           if (MyTracer.ActiveSpan != null)
            {
              MyTracer.BuildSpan("TruckGlobalDataAggregator.Execute").StartActive();
            }

            log.LogInformation("C# HTTP trigger function processed a request.");

            string name = req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            //Add function execution delays based on input - for personal testing only.
            if (string.Equals(name,"0.5")) await Task.Delay(500);
            if (string.Equals(name,"1")) await Task.Delay(1000);
            if (string.Equals(name,"1.5")) await Task.Delay(1500);
            if (string.Equals(name,"2")) await Task.Delay(2000);

            string responseMessage = string.IsNullOrEmpty(name)
                ? "This HTTP triggered function executed successfully. Pass a name in the query string or in the request body for a personalized response."
                : $"Hello, {name}. This HTTP triggered function executed successfully.";

             //Finish the span
            MyTracer.ActiveSpan.Finish();   
            
            //Close the tracer before application exit
            MyTracer.Close();

            return new OkObjectResult(responseMessage);
        }
    }
}
