// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.JsonRpc.Modules;
using Nethermind.JsonRpc.Test.Modules;
using Nethermind.Logging;
using Nethermind.Serialization.Json;

namespace Nethermind.JsonRpc.Test;

public static class RpcTest
{
    public static async Task<JsonRpcResponse> TestRequest<T>(T module, string method, params object?[] parameters) where T : class, IRpcModule
    {
        IJsonRpcService service = BuildRpcService(module);
        JsonRpcRequest request = GetJsonRequest(method, parameters);
        return await service.SendRequestAsync(request, new JsonRpcContext(RpcEndpoint.Http));
    }

    public static async Task<string> TestSerializedRequest<T>(T module, string method, params object?[] parameters) where T : class, IRpcModule
    {
        IJsonRpcService service = BuildRpcService(module);
        JsonRpcRequest request = GetJsonRequest(method, parameters);

        JsonRpcContext context = module is IContextAwareRpcModule { Context: not null } contextAwareModule ?
            contextAwareModule.Context :
            new JsonRpcContext(RpcEndpoint.Http);
        using JsonRpcResponse response = await service.SendRequestAsync(request, context);

        EthereumJsonSerializer serializer = new();

        Stream stream = new MemoryStream();
        long size = await serializer.SerializeAsync(stream, response);

        // for coverage (and to prove that it does not throw
        Stream indentedStream = new MemoryStream();
        await serializer.SerializeAsync(indentedStream, response, true);

        stream.Seek(0, SeekOrigin.Begin);
        string serialized = await new StreamReader(stream).ReadToEndAsync();

        size.Should().Be(serialized.Length);

        return serialized;
    }

    private static IJsonRpcService BuildRpcService<T>(T module) where T : class, IRpcModule
    {
        var moduleProvider = new TestRpcModuleProvider<T>(module);

        moduleProvider.Register(new SingletonModulePool<T>(new TestSingletonFactory<T>(module), true));
        IJsonRpcService service = new JsonRpcService(moduleProvider, LimboLogs.Instance, new JsonRpcConfig());
        return service;
    }

    // Parameters from tests are provided as either already serialized object, raw string, or raw object.
    // We need to handle all these cases, while preventing double serialization.
    private static string GetSerializedParameter(object? parameter)
    {
        if (parameter is string serialized and (['[', ..] or ['{', ..] or ['"', ..]))
            return serialized; // Already serialized

        return JsonSerializer.Serialize(parameter, EthereumJsonSerializer.JsonOptions);
    }

    public static JsonRpcRequest GetJsonRequest(string method, params object?[]? parameters)
    {
        var doc = JsonDocument.Parse($"[{string.Join(",", parameters?.Select(GetSerializedParameter) ?? [])}]");
        var request = new JsonRpcRequest()
        {
            JsonRpc = "2.0",
            Method = method,
            Params = doc.RootElement,
            Id = 67
        };

        return request;
    }

    private class TestSingletonFactory<T>(T module) : SingletonFactory<T>(module)
        where T : IRpcModule;
}
