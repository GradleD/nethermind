// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.CommandLine;
using System.Text.Json;
using Evm.T8n.Errors;
using Evm.T8n.JsonConverters;
using Evm.T8n.JsonTypes;
using Nethermind.Serialization.Json;

namespace Evm.T8n;

public static class T8nCommand
{
    private static readonly EthereumJsonSerializer _ethereumJsonSerializer = new();
    private const string Stdout = "stdout";

    static T8nCommand()
    {
        EthereumJsonSerializer.AddConverter(new ReceiptJsonConverter());
        EthereumJsonSerializer.AddConverter(new AccountStateJsonConverter());
    }

    public static void Configure(ref CliRootCommand rootCmd)
    {
        CliCommand cmd = T8nCommandOptions.CreateCommand();

        cmd.SetAction(parseResult =>
        {
            var arguments = T8nCommandArguments.FromParseResult(parseResult);

            T8nOutput t8nOutput = new();
            try
            {
                T8nExecutionResult t8nExecutionResult = T8nExecutor.Execute(arguments);

                if (arguments.OutputAlloc == Stdout) t8nOutput.Alloc = t8nExecutionResult.Accounts;
                else WriteToFile(arguments.OutputAlloc, arguments.OutputBaseDir, t8nExecutionResult.Accounts);

                if (arguments.OutputResult == Stdout) t8nOutput.Result = t8nExecutionResult.PostState;
                else WriteToFile(arguments.OutputResult, arguments.OutputBaseDir, t8nExecutionResult.PostState);

                if (arguments.OutputBody == Stdout) t8nOutput.Body = t8nExecutionResult.TransactionsRlp;
                else if (arguments.OutputBody is not null)
                {
                    WriteToFile(arguments.OutputBody, arguments.OutputBaseDir, t8nExecutionResult.TransactionsRlp);
                }

                if (t8nOutput.Body is not null || t8nOutput.Alloc is not null || t8nOutput.Result is not null)
                {
                    Console.WriteLine(_ethereumJsonSerializer.Serialize(t8nOutput, true));
                }
            }
            catch (T8nException e)
            {
                t8nOutput = new T8nOutput(e.Message, e.ExitCode);
            }
            catch (IOException e)
            {
                t8nOutput = new T8nOutput(e.Message, T8nErrorCodes.ErrorIO);
            }
            catch (JsonException e)
            {
                t8nOutput = new T8nOutput(e.Message, T8nErrorCodes.ErrorJson);
            }
            catch (Exception e)
            {
                t8nOutput = new T8nOutput(e.Message, T8nErrorCodes.ErrorEvm);
            }
            finally
            {
                Environment.ExitCode = t8nOutput.ExitCode;
                if (t8nOutput.ErrorMessage != null)
                {
                    Console.WriteLine(t8nOutput.ErrorMessage);
                }
            }
        });

        rootCmd.Add(cmd);
    }

    private static void WriteToFile(string filename, string? basedir, object outputObject)
    {
        FileInfo fileInfo = new(basedir + filename);
        Directory.CreateDirectory(fileInfo.DirectoryName!);
        using StreamWriter writer = new(fileInfo.FullName);
        writer.Write(_ethereumJsonSerializer.Serialize(outputObject, true));
    }
}
