// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only
using System.Linq;
using System.Numerics;
using Nethermind.Core;
using Nethermind.Core.Extensions;
using Nethermind.Evm.Tracing.GethStyle;
using Nethermind.Int256;
using NUnit.Framework;
using Nethermind.Specs;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System;
using Nethermind.Evm.Tracing.GethStyle.Javascript;

namespace Nethermind.Evm.Test.Tracing;

public class GethLikeJavascriptTracerTests :VirtualMachineTestsBase
{

    /// <summary>
    /// Testing Javascript tracers functions
    /// </summary>

    [Test]
    public void JS_tracers_log_functions()
    {
        string userTracer = @"{
                    retVal: [],
                    step: function(log, db) { this.retVal.push(log.getPC() + ':' + log.op.toString() + ':' + log.getCost() + ':' + log.getGas() + ':' + log.getRefund()) },
                    fault: function(log, db) { this.retVal.push('FAULT: ' + JSON.stringify(log)) },
                    result: function(ctx, db) { return this.retVal }
                }";
        GethLikeTxTrace traces = Execute(
                new GethLikeJavascriptTxTracer(TestState, GethTraceOptions.Default with { EnableMemory = true, Tracer = userTracer  }),
                GetBytecode(),
                MainnetSpecProvider.CancunActivation)
            .BuildResult();
        string[] expectedStrings = {
            "0:PUSH32:0:79000:null",
            "33:PUSH1:0:78997:null",
            "35:MSTORE:0:78994:null",
            "36:PUSH32:0:78988:null",
            "69:PUSH1:0:78985:null",
            "71:MSTORE:0:78982:null",
            "72:STOP:0:78976:null"
        };
        Assert.That(traces.CustomTracerResult, Is.EqualTo(expectedStrings));
    }
    [Test]
    public void JS_tracers_log_op_functions()
    {
        string userTracer = @"{
                    retVal: [],
                    step: function(log, db) { this.retVal.push(log.op.toString() + ' : ' + log.op.toNumber() + ' : ' + log.op.isPush() ) },
                    fault: function(log, db) { this.retVal.push('FAULT: ' + JSON.stringify(log)) },
                    result: function(ctx, db) { return this.retVal }
                }";
        GethLikeTxTrace traces = Execute(
                new GethLikeJavascriptTxTracer(TestState,GethTraceOptions.Default with { EnableMemory = true, Tracer = userTracer }),
                GetBytecode(),
                MainnetSpecProvider.CancunActivation)
            .BuildResult();
        string[] expectedStrings = {
            "PUSH32 : 0x7F : true", "PUSH1 : 0x60 : true", "MSTORE : 0x52 : false", "PUSH32 : 0x7F : true", "PUSH1 : 0x60 : true", "MSTORE : 0x52 : false", "STOP : 0x00 : false"
        };
        Assert.That(traces.CustomTracerResult, Is.EqualTo(expectedStrings));
    }
    [Test]
    public void JS_tracers_log_stack_functions()
    {
        string userTracer = @"{
                    retVal: [],
                    step: function(log, db) { this.retVal.push(log.stack.length()) },
                    fault: function(log, db) { this.retVal.push('FAULT: ' + JSON.stringify(log)) },
                    result: function(ctx, db) { return this.retVal }
                }";
        GethLikeTxTrace traces = Execute(
                new GethLikeJavascriptTxTracer(TestState,GethTraceOptions.Default with { EnableMemory = true, Tracer = userTracer }),
                GetBytecode(),
                MainnetSpecProvider.CancunActivation)
            .BuildResult();
        // Assert.That(traces.CustomTracerResult, Has.All.Empty);
    }
    [Test]
    public void JS_tracers_log_memory_functions()
    {
        string userTracer = @"{
                    retVal: [],
                    step: function(log, db) { this.retVal.push(log.stack.length()) },
                    fault: function(log, db) { this.retVal.push('FAULT: ' + JSON.stringify(log)) },
                    result: function(ctx, db) { return this.retVal }
                }";
        GethLikeTxTrace traces = Execute(
                new GethLikeJavascriptTxTracer(TestState,GethTraceOptions.Default with { EnableMemory = true, Tracer = userTracer }),
                GetBytecode(),
                MainnetSpecProvider.CancunActivation)
            .BuildResult();
        // Assert.That(traces.CustomTracerResult, Has.All.Empty);
    }
    [Test]
    public void JS_tracers_log_contract_functions()
    {
        string userTracer = @"{
                    retVal: [],
                    step: function(log, db) { this.retVal.push(log.contract.getAddress() + ':' + log.contract.getCaller() + ':' + log.contract.getInput() ) },
                    fault: function(log, db) { this.retVal.push('FAULT: ' + JSON.stringify(log)) },
                    result: function(ctx, db) { return this.retVal }
                }";
        GethLikeTxTrace traces = Execute(
                new GethLikeJavascriptTxTracer(TestState,GethTraceOptions.Default with { EnableMemory = true, Tracer = userTracer }),
                GetBytecode(),
                MainnetSpecProvider.CancunActivation)
            .BuildResult();
        // Assert.That(traces.CustomTracerResult, Has.All.Empty);
    }
    /// <summary>
    /// Testing Javascript tracers implementation as per geth example implementation
    /// </summary>
    [Test]
    public void Js_traces_simple_filter()
    {
        string userTracer = "{" +
                            "retVal: []," +
                            "step: function(log, db) { this.retVal.push(log.getPC() + ':' + log.op.toString()) }," +
                            "fault: function(log, db) { this.retVal.push('FAULT: ' + JSON.stringify(log)) }," +
                            "result: function(ctx, db) { return this.retVal }" +
                            "}";;

        GethLikeTxTrace traces = Execute(
            new GethLikeJavascriptTxTracer(TestState,GethTraceOptions.Default with { EnableMemory = true, Tracer = userTracer  }),
            GetBytecode(),
            MainnetSpecProvider.CancunActivation)
            .BuildResult();
        string[] expectedStrings = {
            "0:PUSH32", "33:PUSH1", "35:MSTORE", "36:PUSH32", "69:PUSH1", "71:MSTORE", "72:STOP"
        };
        Assert.That(traces.CustomTracerResult, Is.EqualTo(expectedStrings));
    }
    [Test]
    public void Js_traces_filter_with_conditionals()
    {
        string userTracer = @"{
                    retVal: [],
                    step: function(log, db) {
                        if (log.op.toNumber() == 0x60) {
                            this.retVal.push(log.getPC() + ': PUSH1');
                        } else if (log.op.toNumber() == 0x52) {
                            this.retVal.push(log.getPC() + ': MSTORE');
                        }
                    },
                    fault: function(log, db) { this.retVal.push('FAULT: ' + JSON.stringify(log)); },
                    result: function(ctx, db) { return this.retVal; }
                }";
        GethLikeTxTrace traces = Execute(
            new GethLikeJavascriptTxTracer(TestState,GethTraceOptions.Default with { EnableMemory = true, Tracer = userTracer }),
            GetBytecode(),
            MainnetSpecProvider.CancunActivation)
            .BuildResult();
        string[] expectedStrings = {
            "33: PUSH1", "35: MSTORE", "69: PUSH1", "71: MSTORE"
        };
        Assert.That(traces.CustomTracerResult, Is.EqualTo(expectedStrings));
    }

    [Test]
    public void Js_traces_storage_information()
    {
        string userTracer = @"{
                    retVal: [],
                    step: function(log, db) {
                        if (log.op.toNumber() == 0x55)
                            this.retVal.push(log.getPC() + ': SSTORE ' + log.stack.peek(0).toString(16));
                        if (log.op.toNumber() == 0x54)
                            this.retVal.push(log.getPC() + ': SLOAD ' + log.stack.peek(0).toString(16));
                        if (log.op.toNumber() == 0x00)
                            this.retVal.push(log.getPC() + ': STOP ' + log.stack.peek(0).toString(16) + ' <- ' + log.stack.peek(1).toString(16));
                    },
                    fault: function(log, db) {
                        this.retVal.push('FAULT: ' + JSON.stringify(log));
                    },
                    result: function(ctx, db) {
                        return this.retVal;
                    }
                }";
        GethLikeTxTrace traces = Execute(
            new GethLikeJavascriptTxTracer(TestState,GethTraceOptions.Default with { EnableMemory = true, Tracer = userTracer }),
            GetStorageBytecode(),
            MainnetSpecProvider.CancunActivation)
            .BuildResult();
        string[] expectedStrings = {
            "35: SSTORE 0xa01234", "71: SSTORE 0xb15678", "107: SLOAD 0xa01234", "108: STOP 0x0 <- 0xa01234"
        };
        Assert.That(traces.CustomTracerResult, Is.EqualTo(expectedStrings));
    }
    [Test]
    public void Js_traces_operation_results()
    {
        string userTracer = @"{
                    retVal: [],
                    afterSload: false,
                    step: function(log, db) {
                        if (this.afterSload) {
                                this.retVal.push(""Result: "" + log.stack.peek(0).toString(16));
                            this.afterSload = false;
                        }
                        if (log.op.toNumber() == 0x54) {
                                this.retVal.push(log.getPC() + ""SLOAD "" + log.stack.peek(0).toString(16));
                            this.afterSload = true;
                        }
                        if (log.op.toNumber() == 0x55)
                            this.retVal.push(log.getPC() + "" SSTORE"" + log.stack.peek(0).toString(16) + "" <- "" + log.stack.peek(1).toString(16));
                    },
                    fault: function(log, db) {
                        this.retVal.push(""FAULT: "" + JSON.stringify(log));
                    },
                    result: function(ctx, db) {
                        return this.retVal;
                    }
                }";
        GethLikeTxTrace traces = Execute(
            new GethLikeJavascriptTxTracer(TestState,GethTraceOptions.Default with { EnableMemory = true, Tracer = userTracer }),
            GetOperationalBytecode(),
            MainnetSpecProvider.CancunActivation)
            .BuildResult();
        string[] expectedStrings = {
            "68 SSTORE0xa01234 <- 0xb15678", "104SLOAD 0xa01234", "Result: 0x0"
        };
        Assert.That(traces.CustomTracerResult, Is.EqualTo(expectedStrings));
    }

    [Test]
    public void Js_traces_calls_btn_contracts()
    {
        string userTracer = @"{
                    retVal: [],
                    afterSload: false,
                    callStack: [],
                    byte2Hex: function(byte) {
                        if (byte < 0x10) {
                            return ""0"" + byte.toString(16);
                        }
                        return byte.toString(16);
                    },
                    array2Hex: function(arr) {
                        var retVal = """";
                        for (var i=0; i<arr.length; i++) {
                            retVal += this.byte2Hex(arr[i]);
                        }
                        return retVal;
                    },
                    getAddr: function(log) {
                        return this.array2Hex(log.contract.getAddress());
                    },
                    step: function(log, db) {
                        var opcode = log.op.toNumber();
                        // SLOAD
                        if (opcode == 0x54) {
                            this.retVal.push(log.getPC() + "": SLOAD "" +
                                this.getAddr(log) + "":"" +
                                log.stack.peek(0).toString(16));
                            this.afterSload = true;
                        }
                        // SLOAD Result
                        if (this.afterSload) {
                            this.retVal.push(""Result: "" +
                                log.stack.peek(0).toString(16));
                            this.afterSload = false;
                        }
                        // SSTORE
                        if (opcode == 0x55) {
                            this.retVal.push(log.getPC() + "": SSTORE "" +
                                this.getAddr(log) + "":"" +
                                log.stack.peek(0).toString(16) + "" <- "" +
                                log.stack.peek(1).toString(16));
                        }
                        // End of step

                    },
                    fault: function(log, db) {
                        this.retVal.push(""FAULT: "" + JSON.stringify(log));
                    },
                    result: function(ctx, db) {
                        return this.retVal;
                }
            }";

        GethLikeTxTrace traces = Execute(
                new GethLikeJavascriptTxTracer(TestState,GethTraceOptions.Default with { EnableMemory = true, Tracer = userTracer }),
                GetOperationalBytecode(),
                MainnetSpecProvider.CancunActivation)
            .BuildResult();
        string[] expectedStrings = {
            "68: SSTORE 942921b14f1b1c385cd7e0cc2ef7abe5598c8358:0xa01234 <- 0xb15678", "104: SLOAD 942921b14f1b1c385cd7e0cc2ef7abe5598c8358:0xa01234", "Result: 0xa01234"
        };
        Assert.That(traces.CustomTracerResult, Is.EqualTo(expectedStrings));
    }

    [Test]
    public void JS_tracers_builtIns_opcount_tracer()
    {
        string userTracer = "opcount_tracer";
        GethLikeTxTrace traces = Execute(
                new GethLikeJavascriptTxTracer(TestState,GethTraceOptions.Default with { EnableMemory = true, Tracer = userTracer  }),
                GetBytecode(),
                MainnetSpecProvider.CancunActivation)
            .BuildResult();
        Assert.That(traces.CustomTracerResult, Is.EqualTo(7));
    }

    [Test]
    public void JS_tracers_builtIns_noop_tracer_legacy()
    {
        string userTracer = "noop_tracer_legacy";
        GethLikeTxTrace traces = Execute(
                new GethLikeJavascriptTxTracer(TestState,GethTraceOptions.Default with { EnableMemory = true, Tracer = userTracer  }),
                GetBytecode(),
                MainnetSpecProvider.CancunActivation)
            .BuildResult();
       Assert.That(traces.CustomTracerResult, Has.All.Empty);
    }

    [Test]
    public void JS_tracers_builtIns_trigram_tracer()
    {
        string userTracer = "trigram_tracer";
        GethLikeTxTrace traces = Execute(
                new GethLikeJavascriptTxTracer(TestState,GethTraceOptions.Default with { EnableMemory = true, Tracer = userTracer  }),
                GetBytecode(),
                MainnetSpecProvider.CancunActivation)
            .BuildResult();
        // Assert.That(traces.CustomTracerResult, Has.All.Empty);
    }
    private static byte[] GetBytecode()
    {
       return  Prepare.EvmCode
            .PushData(SampleHexData1.PadLeft(64, '0'))
            .PushData(0)
            .Op(Instruction.MSTORE)
            .PushData(SampleHexData2.PadLeft(64, '0'))
            .PushData(32)
            .Op(Instruction.MSTORE)
            .Op(Instruction.STOP)
            .Done;
    }
    private static byte[] GetStorageBytecode()
    {
        return  Prepare.EvmCode
             .PushData(SampleHexData1.PadLeft(64, '0'))
             .PushData(0)
             .Op(Instruction.SSTORE)
             .PushData(SampleHexData2.PadLeft(64, '0'))
             .PushData(32)
             .Op(Instruction.SSTORE)
             .PushData(SampleHexData1.PadLeft(64, '0'))
             .PushData(0)
             .Op(Instruction.SLOAD)
             .Op(Instruction.STOP)
             .Done;
    }
    private static byte[] GetOperationalBytecode()
    {
        return  Prepare.EvmCode
             .PushData(SampleHexData2.PadLeft(64, '0'))
             .PushData(SampleHexData1.PadLeft(64, '0'))
             .PushData(0)
             .Op(Instruction.SSTORE)
             .PushData(SampleHexData1.PadLeft(64, '0'))
             .PushData(0)
             .Op(Instruction.SLOAD)
             .Op(Instruction.STOP)
             .Done;
    }
}
