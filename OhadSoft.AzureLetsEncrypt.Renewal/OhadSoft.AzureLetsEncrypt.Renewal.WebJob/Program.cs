﻿using System;
using System.Configuration;
using System.Diagnostics;
using OhadSoft.AzureLetsEncrypt.Renewal.Management;
using OhadSoft.AzureLetsEncrypt.Renewal.WebJob.AppSettings;
using OhadSoft.AzureLetsEncrypt.Renewal.WebJob.CLI;
using OhadSoft.AzureLetsEncrypt.Renewal.WebJob.Email;
using OhadSoft.AzureLetsEncrypt.Renewal.WebJob.Telemetry;
using OhadSoft.AzureLetsEncrypt.Renewal.WebJob.Util;
using AppSettingsReader = OhadSoft.AzureLetsEncrypt.Renewal.WebJob.AppSettings.AppSettingsReader;

namespace OhadSoft.AzureLetsEncrypt.Renewal.WebJob
{
    internal static class Program
    {
        private const int Success = 0;
        private const int ArgumentError = 1;
        private const string TraceSourceName = "OhadSoft.AzureLetsEncrypt.Renewal.WebJob";

        private static int Main(string[] args)
        {
            var telemetryEnvironmentVariable = AppSettingsRenewalParamsReader.KeyPrefix + "DISABLE_TELEMETRY";
            if (Environment.GetEnvironmentVariable(telemetryEnvironmentVariable) == null)
            {
                TelemetryHelper.Setup();
            }
            else
            {
                Console.WriteLine("'{0}' environment variable detected - telemetry disabled", telemetryEnvironmentVariable);
            }

            try
            {
                var webjobName = Environment.GetEnvironmentVariable("WEBJOBS_NAME");
                return webjobName != null ? WebJobMain(webjobName) : CliMain(args);
            }
            catch (Exception e) when (!ExceptionHelper.IsCriticalException(e))
            {
                TelemetryHelper.Client.TrackException(e);
                throw;
            }
            finally
            {
                TelemetryHelper.Client.Flush();
            }
        }

        private static int WebJobMain(string webjobName)
        {
            var startTicks = Environment.TickCount;
            Events.WebJobRenewalStarted(webjobName);

            Console.WriteLine("Web App SSL renewal job ({0}) started", webjobName);
            var renewr = new AppSettingsRenewer(
                new RenewalManager(),
                new AppSettingsRenewalParamsReader(new AppSettingsReader(ConfigurationManager.AppSettings, ConfigurationManager.ConnectionStrings)),
                new SendGridNotifier(ConfigurationManager.ConnectionStrings[AppSettingsRenewalParamsReader.KeyPrefix + "SendGridApiKey"]?.ConnectionString));
            try
            {
                renewr.Renew();
            }
            catch (Exception e) when (!ExceptionHelper.IsCriticalException(e))
            {
                Console.WriteLine("***ERROR*** Unexpected exception: {0}", e);
                throw; // we want the webjob to fail
            }

            Events.WebjobRenewalCompleted(webjobName, startTicks);
            return Success;
        }

        private static int CliMain(string[] args)
        {
            var startTicks = Environment.TickCount;
            Events.CliRenewalStarted(args);

            Trace.Listeners.Add(new ConsoleTraceListener());
            Console.WriteLine("Web App SSL renewal CLI started, parameters: {0}", string.Join(", ", args));
            var renewer = new CliRenewer(new RenewalManager(), new CommandlineRenewalParamsReader());

            try
            {
                renewer.Renew(args);
            }
            catch (ArgumentException e)
            {
                Console.WriteLine(
                    "***ERROR*** Could not parse arguments: {0}{1}(To see the full exception enable the {2} trace source in app.config)",
                    e.Message,
                    Environment.NewLine,
                    TraceSourceName);

                TelemetryHelper.Client.TrackException(e);

                new TraceSource(TraceSourceName).TraceEvent(TraceEventType.Error, 1, e.ToString());
                PrintUsage();
                return ArgumentError;
            }
            catch (Exception e) when (!ExceptionHelper.IsCriticalException(e))
            {
                Console.WriteLine("***ERROR*** Unexpected exception: {0}", e);
                throw;
            }

            Events.CliRenewalCompleted(startTicks);
            return Success;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", Justification = "technical terms not in dictionary")]
        private static void PrintUsage()
        {
            Console.WriteLine(
                "Usage: {0}.exe SubscriptionId TenantId ResourceGroup WebApp Hosts Email ClientId ClientSecret [UseIpBasedSsl] [RsaKeyLength] [AcmeBaseUri]",
                typeof(Program).Assembly.GetName().Name);
            Console.WriteLine("'Hosts' is a semicolon-delimited list of host names");
            Console.WriteLine("'UseIpBasedSsl' is optional and defaults to false");
            Console.WriteLine("'RsaKeyLength' is optional and defaults to 2048");
            Console.WriteLine("'AcmeBaseUri' is optional and defaults to https://acme-v01.api.letsencrypt.org/");
            Console.WriteLine("Consult the Let's Encrypt documentation for rate limits: https://letsencrypt.org/docs/rate-limits/");
            Console.WriteLine("Exit codes: {0} = success, {1} = argument error (any other error will crash the process)", Success, ArgumentError);
        }
    }
}