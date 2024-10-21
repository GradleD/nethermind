// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Config;

namespace Nethermind.Era1;

public class EraConfig : IEraConfig
{
    public string? ImportDirectory { get; set; } = null;
}
