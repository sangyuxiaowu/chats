﻿using Microsoft.AspNetCore.Mvc;

namespace Chats.BE.Controllers.Users.Usages.Dtos;

public interface IUsageQuery
{
    public string? User { get; }

    public string? ApiKeyId { get; }

    public string? Provider { get; }
    public string? ModelKey { get; init; }
    public string? Model { get; init; }

    public DateOnly? Start { get; }

    public DateOnly? End { get; }

    public UsageQueryType? Source { get; }

    public short TimezoneOffset { get; }
}
