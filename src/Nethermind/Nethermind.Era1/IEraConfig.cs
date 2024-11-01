// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Config;

namespace Nethermind.Era1;

public interface IEraConfig : IConfig
{
    [ConfigItem(Description = "Directory of era1 archives to be imported.", DefaultValue = "", HiddenFromDocs = false)]
    public string? ImportDirectory { get; set; }

    [ConfigItem(Description = "Directory of archive export.", DefaultValue = "", HiddenFromDocs = false)]
    public string? ExportDirectory { get; set; }

    [ConfigItem(Description = "Block number to import/export from.", DefaultValue = "0", HiddenFromDocs = false)]
    long From { get; set; }

    [ConfigItem(Description = "Block number to import/export to.", DefaultValue = "0", HiddenFromDocs = false)]
    long To { get; set; }

    [ConfigItem(Description = "Accumulator file to be used for trusting era files.", DefaultValue = "null", HiddenFromDocs = false)]
    string? TrustedAccumulatorFile { get; set; }

    [ConfigItem(Description = "Max era1 size.", DefaultValue = "8192", HiddenFromDocs = true)]
    int MaxEra1Size { get; set; }

    [ConfigItem(Description = "Network name used for era directory naming. When null, it will imply from network.", DefaultValue = "null", HiddenFromDocs = true)]
    string? NetworkName { get; set; }
}
