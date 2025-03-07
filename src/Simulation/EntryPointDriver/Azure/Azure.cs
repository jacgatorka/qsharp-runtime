﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.Quantum;
using Microsoft.Azure.Quantum.Exceptions;
using Microsoft.Quantum.EntryPointDriver.Mock;
using Microsoft.Quantum.Runtime;
using Microsoft.Quantum.Runtime.Submitters;
using Microsoft.Quantum.Simulation.Common.Exceptions;
using static Microsoft.Quantum.EntryPointDriver.Driver;

namespace Microsoft.Quantum.EntryPointDriver
{
    using Environment = System.Environment;

    /// <summary>
    /// Provides entry point submission to Azure Quantum.
    /// </summary>
    public static class Azure
    {

        /// <summary>
        /// Generates payload for Azure offline.
        /// </summary>
        /// <param name="settings">The settings for generating azure payload.</param>
        /// <param name="qirSubmission">A QIR entry point submission.</param>
        /// <returns>The exit code.</returns>
        public static Task<int> GenerateAzurePayload(
            GenerateAzurePayloadSettings settings, QirSubmission? qirSubmission)
        {
            Console.WriteLine("Microsoft.Quantum.EntryPointDriver.Azure.GenerateAzurePayload");
            if (!(qirSubmission is null) && QirAzurePayloadGenerator(settings) is { } generator)
            {
                return GenerateQirAzurePayload(settings, generator, qirSubmission);
            }

            if (qirSubmission is null)
            {
                DisplayError(
                    $"The target {settings.Target} requires QIR submission, but the project was built without QIR. "
                    + "Please enable QIR generation in the project settings.",
                    null);
            }
            else
            {
                DisplayError($"No submitters were found for the target {settings.Target}.", null);
            }

            return Task.FromResult(1);
        }

        /// <summary>
        /// Submits an entry point to Azure Quantum. If <paramref name="qirSubmission"/> is non-null and a QIR submitter
        /// is available for the target in <paramref name="settings"/>, the QIR entry point is submitted. Otherwise, the
        /// Q# entry point is submitted.
        /// </summary>
        /// <param name="settings">The Azure submission settings.</param>
        /// <param name="qsSubmission">A Q# entry point submission.</param>
        /// <param name="qirSubmission">A QIR entry point submission.</param>
        /// <typeparam name="TIn">The entry point argument type.</typeparam>
        /// <typeparam name="TOut">The entry point return type.</typeparam>
        /// <returns>The exit code.</returns>
        public static Task<int> Submit<TIn, TOut>(
            AzureSettings settings, QSharpSubmission<TIn, TOut> qsSubmission, QirSubmission? qirSubmission)
        {
            LogIfVerbose(settings, settings.ToString());

            if ((settings.Location is null) && (settings.BaseUri is null))
            {
                DisplayError($"Either --location or --base-uri must be provided.", null);
            }

            if (!(qirSubmission is null) && QirSubmitter(settings) is { } qirSubmitter)
            {
                return SubmitQir(settings, qirSubmitter, qirSubmission);
            }

            if (QSharpSubmitter(settings) is { } qsSubmitter)
            {
                return SubmitQSharp(settings, qsSubmitter, qsSubmission);
            }

            if (QSharpMachine(settings) is { } machine)
            {
                return SubmitQSharpMachine(settings, machine, qsSubmission);
            }

            if (qirSubmission is null && !(QirSubmitter(settings) is null))
            {
                DisplayError(
                    $"The target {settings.Target} requires QIR submission, but the project was built without QIR. "
                    + "Please enable QIR generation in the project settings.",
                    null);
            }
            else
            {
                DisplayError(
                    $"No submitters were found for the target \"{settings.Target}\" " + 
                    $"and target capability \"{settings.TargetCapability}\".",
                    null);
            }

            return Task.FromResult(1);
        }

        /// <summary>
        /// Generates QIR payload for Azure.
        /// </summary>
        /// <param name="settings">The settings for generating azure payload.</param>
        /// <param name="generator">The payload generator.</param>
        /// <param name="submission">A QIR entry point submission.</param>
        /// <returns>The exit code.</returns>
        private static async Task<int> GenerateQirAzurePayload(
            GenerateAzurePayloadSettings settings, IQirSubmitter generator, QirSubmission submission)
        {
            LogIfVerbose(settings, "Generating QIR Azure Payload");
            return await Task<int>.Run(() => DisplayPayloadGeneration(generator.Validate(
                submission.QirStream, submission.EntryPointName, submission.Arguments)));
        }

        /// <summary>
        /// Submits a Q# entry point to Azure Quantum using a quantum machine.
        /// </summary>
        /// <typeparam name="TIn">The entry point's argument type.</typeparam>
        /// <typeparam name="TOut">The entry point's return type.</typeparam>
        /// <param name="settings">The Azure submission settings.</param>
        /// <param name="machine">The quantum machine used for submission.</param>
        /// <param name="submission">The Q# entry point submission.</param>
        /// <returns>The exit code.</returns>
        private static async Task<int> SubmitQSharpMachine<TIn, TOut>(
            AzureSettings settings, IQuantumMachine machine, QSharpSubmission<TIn, TOut> submission)
        {
            LogIfVerbose(settings, "Submitting Q# entry point using a quantum machine.");

            if (settings.DryRun)
            {
                var (valid, message) = machine.Validate(submission.EntryPointInfo, submission.Argument);
                return DisplayValidation(valid ? null : message);
            }

            var context = new SubmissionContext
            {
                FriendlyName = settings.JobName,
                Shots = settings.Shots,
                InputParams = settings.JobParams
            };

            var job = machine.SubmitAsync(submission.EntryPointInfo, submission.Argument, context);
            return await DisplayJobOrError(settings, job);
        }

        /// <summary>
        /// Submits a Q# entry point to Azure Quantum.
        /// </summary>
        /// <typeparam name="TIn">The entry point's argument type.</typeparam>
        /// <typeparam name="TOut">The entry point's return type.</typeparam>
        /// <param name="settings">The Azure submission settings.</param>
        /// <param name="submitter">The Q# submitter.</param>
        /// <param name="submission">The Q# entry point submission.</param>
        /// <returns>The exit code.</returns>
        private static async Task<int> SubmitQSharp<TIn, TOut>(
            AzureSettings settings, IQSharpSubmitter submitter, QSharpSubmission<TIn, TOut> submission)
        {
            LogIfVerbose(settings, "Submitting Q# entry point.");

            if (settings.DryRun)
            {
                return DisplayValidation(submitter.Validate(submission.EntryPointInfo, submission.Argument));
            }

            var job = submitter.SubmitAsync(
                submission.EntryPointInfo, submission.Argument, settings.SubmissionOptions);
            return await DisplayJobOrError(settings, job);
        }

        /// <summary>
        /// Submits a QIR entry point to Azure Quantum. 
        /// </summary>
        /// <param name="settings">The Azure submission settings.</param>
        /// <param name="submitter">The QIR entry point submitter.</param>
        /// <param name="submission">The QIR entry point submission.</param>
        /// <returns>The exit code.</returns>
        private static async Task<int> SubmitQir(
            AzureSettings settings, IQirSubmitter submitter, QirSubmission submission)
        {
            LogIfVerbose(settings, "Submitting QIR entry point.");

            if (settings.DryRun)
            {
                return DisplayValidation(submitter.Validate(
                    submission.QirStream, submission.EntryPointName, submission.Arguments));
            }

            var job = submitter.SubmitAsync(
                submission.QirStream, submission.EntryPointName, submission.Arguments, settings.SubmissionOptions);
            return await DisplayJobOrError(settings, job);
        }

        /// <summary>
        /// Displays the submitted job information or an error message.
        /// </summary>
        /// <param name="settings">The submission settings.</param>
        /// <param name="job">The submitted job task.</param>
        /// <returns>The exit code.</returns>
        private static async Task<int> DisplayJobOrError(AzureSettings settings, Task<IQuantumMachineJob> job)
        {
            void DisplayAzureQuantumException(AzureQuantumException ex) =>
                DisplayError(
                    "An error occurred when submitting to the Azure Quantum service.",
                    ex.Message);
            
            void DisplayQuantumProcessorTranslationException(QuantumProcessorTranslationException ex) =>
                DisplayError(
                    "Unable to translate the program for execution on the target quantum machine.",
                    ex.Message);

            bool HandleTargetInvocationException(TargetInvocationException ex)
            {
                if (ex.InnerException is AzureQuantumException azureQuantumEx)
                {
                    DisplayAzureQuantumException(azureQuantumEx);
                    return true;
                }
                else if (ex.InnerException is QuantumProcessorTranslationException quantumProcessorTranslationEx)
                {
                    DisplayQuantumProcessorTranslationException(quantumProcessorTranslationEx);
                    return true;
                }

                return false;
            }

            try
            {
                DisplayJob(await job, settings.Output);
                return 0;
            }
            catch (AzureQuantumException ex)
            {
                DisplayAzureQuantumException(ex);
                return 1;
            }
            catch (QuantumProcessorTranslationException ex)
            {
                DisplayQuantumProcessorTranslationException(ex);
                return 1;
            }
            catch (TargetInvocationException ex)
            {
                if (!HandleTargetInvocationException(ex))
                {
                    throw;
                }
                return 1;
            }
        }

        /// <summary>
        /// Displays a message depending on whether the payload could be generated.
        /// </summary>
        /// <param name="message">The paylod generation error message, or null if payload generation was successfull.</param>
        /// <returns>The exit code.</returns>
        private static int DisplayPayloadGeneration(string? message)
        {
            if (message is null)
            {
                Console.WriteLine("✔️  Payload generated.");
                return 0;
            }

            Console.WriteLine("❌  Payload could not be generated." + Environment.NewLine);
            Console.WriteLine(message);
            return 1;
        }

        /// <summary>
        /// Displays a validation message for a program.
        /// </summary>
        /// <param name="message">The validation error message, or null if the program is valid.</param>
        /// <returns>The exit code.</returns>
        private static int DisplayValidation(string? message)
        {
            if (message is null)
            {
                Console.WriteLine("✔️  The program is valid!");
                return 0;
            }

            Console.WriteLine("❌  The program is invalid." + Environment.NewLine);
            Console.WriteLine(message);
            return 1;
        }

        /// <summary>
        /// Displays the job using the output format.
        /// </summary>
        /// <param name="job">The job.</param>
        /// <param name="format">The output format.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the output format is invalid.</exception>
        private static void DisplayJob(IQuantumMachineJob job, OutputFormat format)
        {
            switch (format)
            {
                case OutputFormat.FriendlyUri:
                    try
                    {
                        Console.WriteLine(job.Uri);
                    }
                    catch (Exception ex)
                    {
                        DisplayWithColor(
                            ConsoleColor.Yellow,
                            Console.Error,
                            $"The friendly URI for viewing job results could not be obtained.{Environment.NewLine}" +
                            $"Error details: {ex.Message}{Environment.NewLine}" +
                            "Showing the job ID instead.");

                        Console.WriteLine(job.Id);
                    }

                    break;
                case OutputFormat.Id:
                    Console.WriteLine(job.Id);
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"Invalid output format '{format}'.");
            }
        }

        /// <summary>
        /// Displays an error to the console.
        /// </summary>
        /// <param name="summary">A summary of the error.</param>
        /// <param name="message">The full error message.</param>
        private static void DisplayError(string summary, string? message)
        {
            DisplayWithColor(ConsoleColor.Red, Console.Error, summary);
            if (!string.IsNullOrWhiteSpace(message))
            {
                Console.Error.WriteLine(Environment.NewLine + message);
            }
        }

        /// <summary>
        /// Logs a message if the verbose setting is enabled.
        /// </summary>
        /// <param name="settings">The Azure Quantum submission settings.</param>
        /// <param name="message">The message.</param>
        private static void LogIfVerbose(AzureSettings settings, string message)
        {
            if (settings.Verbose)
            {
                Console.WriteLine(message);
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Logs a message if the verbose setting is enabled.
        /// </summary>
        /// <param name="settings">The generate Azure payload settings.</param>
        /// <param name="message">The message.</param>
        private static void LogIfVerbose(GenerateAzurePayloadSettings settings, string message)
        {
            if (settings.Verbose)
            {
                Console.WriteLine(message);
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Returns a Q# machine for the target in the given Azure settings.
        /// </summary>
        /// <param name="settings">The Azure settings.</param>
        /// <returns>A quantum machine.</returns>
        private static IQuantumMachine? QSharpMachine(AzureSettings settings) => settings.Target switch
        {
            null => null,
            NoOpQuantumMachine.Target => new NoOpQuantumMachine(),
            ErrorQuantumMachine.Target => new ErrorQuantumMachine(),
            _ => QuantumMachineFactory.CreateMachine(settings.CreateWorkspace(), settings.Target, settings.Storage)
        };

        /// <summary>
        /// Returns a Q# submitter for the target in the given Azure settings.
        /// </summary>
        /// <param name="settings">The Azure settings.</param>
        /// <returns>A Q# submitter.</returns>
        private static IQSharpSubmitter? QSharpSubmitter(AzureSettings settings) => settings.Target switch
        {
            null => null,
            NoOpSubmitter.Target => new NoOpSubmitter(),
            _ => SubmitterFactory.QSharpSubmitter(settings.Target, settings.CreateWorkspace(), settings.Storage)
        };

        /// <summary>
        /// Returns a QIR submitter for the target in the given settings.
        /// </summary>
        /// <param name="settings">The generate Azure payload settings.</param>
        /// <returns>A QIR submitter.</returns>
        private static IQirSubmitter? QirAzurePayloadGenerator(GenerateAzurePayloadSettings settings) => settings.Target switch
        {
            null => null,
            NoOpQirSubmitter.Target => new NoOpQirSubmitter(),
            NoOpSubmitter.Target => new NoOpSubmitter(),
            _ => SubmitterFactory.QirPayloadGenerator(settings.Target)
        };

        /// <summary>
        /// Returns a QIR submitter for the target in the given Azure settings.
        /// </summary>
        /// <param name="settings">The Azure settings.</param>
        /// <returns>A QIR submitter.</returns>
        private static IQirSubmitter? QirSubmitter(AzureSettings settings)
        {
            if (settings.Target is null)
            {
                return null;
            }

            // If a configuration file is provided, create a submitter using it.
            if (!(settings.SubmitConfigFile is null))
            {
                return SubmitterFactory.QirSubmitterFromConfigFile(
                    settings.Target, settings.SubmitConfigFile, settings.CreateWorkspace(), settings.Storage);
            }

            // No configuration file was provided, create a submitter depending on the target.
            return settings.Target switch
            {
                NoOpQirSubmitter.Target => new NoOpQirSubmitter(),
                NoOpSubmitter.Target => new NoOpSubmitter(),
                _ => SubmitterFactory.QirSubmitter(
                    settings.Target, settings.CreateWorkspace(), settings.Storage, settings.TargetCapability)
            };
        }

        /// <summary>
        /// The quantum machine submission context.
        /// </summary>
        private sealed class SubmissionContext : IQuantumMachineSubmissionContext
        {
            public string? FriendlyName { get; set; }

            public int Shots { get; set; }

            public ImmutableDictionary<string, string> InputParams { get; set; } = ImmutableDictionary<string, string>.Empty;
        }
    }
}
