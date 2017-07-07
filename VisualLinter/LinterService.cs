﻿using jwldnr.VisualLinter.Helpers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace jwldnr.VisualLinter
{
    internal interface ILinterService
    {
        Task<IEnumerable<LinterMessage>> Lint(string filePath);
    }

    [Export(typeof(ILinterService))]
    internal class LinterService : ILinterService
    {
        internal string Executable { get; }

        private const string LinterName = "eslint";

        internal LinterService()
        {
            Executable = EnvironmentHelper.GetVariable(LinterName);
        }

        public async Task<IEnumerable<LinterMessage>> Lint(string filePath)
        {
            try
            {
                var results = await ExecuteProcessAsync($"--format json \"{filePath}\"");
                if (null == results)
                    throw new ArgumentNullException(nameof(results));

                return ProcessResults(results);
            }
            catch (Exception)
            {
                OutputWindowHelper.WriteLine($"error linting file: {filePath}");
            }

            return Enumerable.Empty<LinterMessage>();
        }

        private static IEnumerable<LinterMessage> ProcessResults(IEnumerable<LinterResult> results)
        {
            // this extension only support 1-1 linting
            // therefor results count will always be 1
            var result = results.FirstOrDefault();

            return null == result
                ? Enumerable.Empty<LinterMessage>()
                : result.Messages;
        }

        private async Task<IEnumerable<LinterResult>> ExecuteProcessAsync(string args)
        {
            var startInfo = new ProcessStartInfo(Executable, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = Process.Start(startInfo))
            {
                if (null == process)
                    return null;

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                if (!string.IsNullOrEmpty(error))
                    OutputWindowHelper.WriteLine(error);

                process.WaitForExit();

                try
                {
                    return JsonConvert.DeserializeObject<IEnumerable<LinterResult>>(output);
                }
                catch (Exception)
                {
                    OutputWindowHelper.WriteLine($"could not deserialize // not a json object: {output}");
                }

                return Enumerable.Empty<LinterResult>();
            }
        }
    }
}